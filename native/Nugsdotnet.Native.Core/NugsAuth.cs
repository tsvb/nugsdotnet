using System.Net.Http.Headers;
using System.Text.Json;

namespace Nugsdotnet.Native.Core;

/// <summary>
/// Owns authentication and the live session: password-grant login, userinfo +
/// subscription lookup, and access-token refresh. Refresh is single-flight
/// (a SemaphoreSlim + re-check inside the lock) so concurrent callers near the
/// expiry boundary don't each POST the rotating refresh_token and invalidate
/// one another — the race flagged in the original Core review.
/// </summary>
public sealed class NugsAuth
{
    private readonly HttpClient _http;
    private readonly NugsSessionStore _store;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    /// <summary>Match <c>ReadFromJsonAsync</c>'s web defaults so wire casing stays tolerant.</summary>
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);

    public NugsAuth(HttpClient http, NugsSessionStore store)
    {
        _http = http;
        _store = store;
    }

    public async Task LoginAsync(string email, string password, CancellationToken ct = default)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = NugsConstants.ClientId,
            ["grant_type"] = "password",
            ["scope"] = "openid profile email nugsnet:api nugsnet:legacyapi offline_access",
            ["username"] = email,
            ["password"] = password,
        });
        using var req = new HttpRequestMessage(HttpMethod.Post, NugsConstants.AuthUrl) { Content = form };
        SetUA(req, NugsConstants.MobileUserAgent);
        using var res = await _http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode)
        {
            // Status only — the IdP body can echo the username or error_description
            // and LoginViewModel surfaces this exception to the UI.
            throw new InvalidOperationException($"sign-in failed ({(int)res.StatusCode})");
        }
        var token = await ReadTokenAsync(res, ct);
        if (string.IsNullOrEmpty(token.refresh_token))
            throw new InvalidOperationException("sign-in failed (no refresh token)");

        var userId = await GetUserIdAsync(token.access_token, ct);
        var sub = await GetSubInfoAsync(token.access_token, ct);

        await _store.SaveAsync(new PersistedSession(
            new TokenSet(token.access_token, token.refresh_token, ExpiresAt(token.expires_in)),
            userId, sub), ct);
    }

    public Task LogoutAsync(CancellationToken ct = default) => _store.ClearAsync(ct);

    public async Task<SessionInfo> GetSessionInfoAsync(CancellationToken ct = default)
    {
        try
        {
            var s = await GetSessionAsync(ct);
            return new SessionInfo(true, s.UserId, s.PlanDescription, s.IsAccessible);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new SessionInfo(false);
        }
    }

    /// <summary>Returns a session with a fresh access token, refreshing if needed.</summary>
    public async Task<Session> GetSessionAsync(CancellationToken ct = default)
    {
        var state = await _store.LoadAsync(ct)
            ?? throw new InvalidOperationException("not logged in");

        if (DateTimeOffset.UtcNow < state.Tokens.ExpiresAt)
            return Session.From(state);

        await _refreshLock.WaitAsync(ct);
        try
        {
            // Re-load + re-check inside the lock: another caller may have just
            // refreshed while we waited, so we'd reuse their token rather than
            // burning the (now-rotated) refresh_token a second time.
            state = await _store.LoadAsync(ct)
                ?? throw new InvalidOperationException("not logged in");
            if (DateTimeOffset.UtcNow < state.Tokens.ExpiresAt)
                return Session.From(state);

            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = NugsConstants.ClientId,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = state.Tokens.RefreshToken,
            });
            using var req = new HttpRequestMessage(HttpMethod.Post, NugsConstants.AuthUrl) { Content = form };
            SetUA(req, NugsConstants.MobileUserAgent);
            using var res = await _http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode)
                throw new InvalidOperationException($"token refresh failed ({(int)res.StatusCode})");
            var token = await ReadTokenAsync(res, ct);

            // Some IdPs omit refresh_token on rotation-less refresh. Keep the
            // one we already have rather than persisting an empty string.
            var refresh = string.IsNullOrEmpty(token.refresh_token)
                ? state.Tokens.RefreshToken
                : token.refresh_token;

            var refreshed = state with
            {
                Tokens = new TokenSet(token.access_token, refresh, ExpiresAt(token.expires_in))
            };
            await _store.SaveAsync(refreshed, ct);
            return Session.From(refreshed);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<string> GetUserIdAsync(string token, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, NugsConstants.UserInfoUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        SetUA(req, NugsConstants.MobileUserAgent);
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await BoundedContent.ReadStringAsync(res, BoundedContent.Auth, ct));
        return doc.RootElement.GetProperty("sub").GetString()
            ?? throw new InvalidOperationException("userinfo missing sub");
    }

    private async Task<SubInfo> GetSubInfoAsync(string token, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, NugsConstants.SubInfoUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        SetUA(req, NugsConstants.MobileUserAgent);
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<SubInfo>(
                   await BoundedContent.ReadStringAsync(res, BoundedContent.Auth, ct), WireJson)
            ?? throw new InvalidOperationException("empty sub info");
    }

    internal static void SetUA(HttpRequestMessage req, string ua)
    {
        req.Headers.UserAgent.Clear();
        req.Headers.TryAddWithoutValidation("User-Agent", ua);
    }

    private static async Task<TokenResponse> ReadTokenAsync(HttpResponseMessage res, CancellationToken ct)
    {
        var token = JsonSerializer.Deserialize<TokenResponse>(
                        await BoundedContent.ReadStringAsync(res, BoundedContent.Auth, ct), WireJson)
            ?? throw new InvalidOperationException("empty token response");
        if (string.IsNullOrEmpty(token.access_token))
            throw new InvalidOperationException("empty token response");
        return token;
    }

    /// <summary>Refresh 60s early, but never treat a short-lived token as already expired.</summary>
    internal static DateTimeOffset ExpiresAt(int expiresInSeconds)
    {
        var lifetime = Math.Max(expiresInSeconds, 0);
        var skew = Math.Min(60, lifetime / 2);
        return DateTimeOffset.UtcNow.AddSeconds(lifetime - skew);
    }
}
