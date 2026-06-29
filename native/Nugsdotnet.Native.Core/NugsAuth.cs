using System.Net.Http.Headers;
using System.Net.Http.Json;
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

    public NugsAuth(HttpClient http, NugsSessionStore store)
    {
        _http = http;
        _store = store;
    }

    public async Task LoginAsync(string email, string password, CancellationToken ct = default)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
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
            var body = await res.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"auth failed: {(int)res.StatusCode} {body}");
        }
        var token = await res.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("empty token response");

        var userId = await GetUserIdAsync(token.access_token, ct);
        var sub = await GetSubInfoAsync(token.access_token, ct);

        await _store.SaveAsync(new PersistedSession(
            new TokenSet(token.access_token, token.refresh_token,
                DateTimeOffset.UtcNow.AddSeconds(token.expires_in - 60)),
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

            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = NugsConstants.ClientId,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = state.Tokens.RefreshToken,
            });
            using var req = new HttpRequestMessage(HttpMethod.Post, NugsConstants.AuthUrl) { Content = form };
            SetUA(req, NugsConstants.MobileUserAgent);
            using var res = await _http.SendAsync(req, ct);
            res.EnsureSuccessStatusCode();
            var token = await res.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct)
                ?? throw new InvalidOperationException("empty refresh response");

            var refreshed = state with
            {
                Tokens = new TokenSet(token.access_token, token.refresh_token,
                    DateTimeOffset.UtcNow.AddSeconds(token.expires_in - 60))
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
        var json = await res.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return json.GetProperty("sub").GetString()
            ?? throw new InvalidOperationException("userinfo missing sub");
    }

    private async Task<SubInfo> GetSubInfoAsync(string token, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, NugsConstants.SubInfoUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        SetUA(req, NugsConstants.MobileUserAgent);
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<SubInfo>(cancellationToken: ct)
            ?? throw new InvalidOperationException("empty sub info");
    }

    internal static void SetUA(HttpRequestMessage req, string ua)
    {
        req.Headers.UserAgent.Clear();
        req.Headers.TryAddWithoutValidation("User-Agent", ua);
    }
}
