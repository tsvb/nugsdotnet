using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using static Nugsdotnet.Core.Nugs.NugsConstants;

namespace Nugsdotnet.Core.Nugs;

/// <summary>
/// Thin client for the unofficial nugs.net API. Owns the HttpClient
/// pre-configured with the mobile User-Agent.
/// </summary>
public sealed class NugsClient
{
    private readonly HttpClient _http;
    private readonly TokenStore _store;
    private readonly ILogger<NugsClient> _log;

    public NugsClient(HttpClient http, TokenStore store, ILogger<NugsClient> log)
    {
        _http = http;
        _store = store;
        _log = log;
    }

    // --- auth -------------------------------------------------------------

    public async Task LoginAsync(string email, string password, CancellationToken ct = default)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["grant_type"] = "password",
            ["scope"] = "openid profile email nugsnet:api nugsnet:legacyapi offline_access",
            ["username"] = email,
            ["password"] = password,
        });
        using var req = new HttpRequestMessage(HttpMethod.Post, AuthUrl) { Content = form };
        SetMobileUA(req);
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

    /// <summary>
    /// Returns a session with a fresh access token, refreshing if needed.
    /// </summary>
    public async Task<Session> GetSessionAsync(CancellationToken ct = default)
    {
        var state = await _store.LoadAsync(ct)
            ?? throw new InvalidOperationException("not logged in");

        if (DateTimeOffset.UtcNow < state.Tokens.ExpiresAt)
        {
            return Session.From(state);
        }

        // Refresh.
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = state.Tokens.RefreshToken,
        });
        using var req = new HttpRequestMessage(HttpMethod.Post, AuthUrl) { Content = form };
        SetMobileUA(req);
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

    private async Task<string> GetUserIdAsync(string token, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, UserInfoUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        SetMobileUA(req);
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return json.GetProperty("sub").GetString()
            ?? throw new InvalidOperationException("userinfo missing sub");
    }

    private async Task<SubInfo> GetSubInfoAsync(string token, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, SubInfoUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        SetMobileUA(req);
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<SubInfo>(cancellationToken: ct)
            ?? throw new InvalidOperationException("empty sub info");
    }

    // --- catalog ----------------------------------------------------------

    public Task<JsonNode?> SearchAsync(string query, CancellationToken ct = default)
        => CatalogGetAsync(new()
        {
            ["method"] = "catalog.search",
            ["searchStr"] = query,
        }, ct);

    public Task<JsonNode?> GetAlbumAsync(string containerId, CancellationToken ct = default)
        => CatalogGetAsync(new()
        {
            ["method"] = "catalog.container",
            ["containerID"] = containerId,
            ["vdisp"] = "1",
        }, ct);

    public Task<JsonNode?> GetAllArtistsAsync(CancellationToken ct = default)
        => CatalogGetAsync(new()
        {
            ["method"] = "catalog.artists",
        }, ct);

    public Task<JsonNode?> GetArtistShowsAsync(
        string artistId, int offset = 1, int limit = 100, CancellationToken ct = default)
        => CatalogGetAsync(new()
        {
            ["method"] = "catalog.containersAll",
            ["artistList"] = artistId,
            ["startOffset"] = offset.ToString(CultureInfo.InvariantCulture),
            ["limit"] = limit.ToString(CultureInfo.InvariantCulture),
            ["availType"] = "1",
            ["vdisp"] = "1",
        }, ct);

    private async Task<JsonNode?> CatalogGetAsync(
        Dictionary<string, string> query, CancellationToken ct)
    {
        var session = await GetSessionAsync(ct);
        var url = $"{StreamApiBase}/api.aspx?{ToQueryString(query)}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        SetMobileUA(req);
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<JsonNode>(cancellationToken: ct);
    }

    // --- streaming --------------------------------------------------------

    /// <summary>
    /// Resolves the underlying CDN URL for a track at a given platformID.
    /// Returns null when nugs has no stream for that combination.
    /// </summary>
    public async Task<string?> GetStreamUrlAsync(
        string trackId, int platformId, Session session, CancellationToken ct = default)
    {
        var query = new Dictionary<string, string>
        {
            ["trackID"] = trackId,
            ["platformID"] = platformId.ToString(CultureInfo.InvariantCulture),
            ["app"] = "1",
            ["subscriptionID"] = session.SubscriptionId,
            ["subCostplanIDAccessList"] = session.PlanId,
            ["nn_userID"] = session.UserId,
            ["startDateStamp"] = session.StartStamp.ToString(CultureInfo.InvariantCulture),
            ["endDateStamp"] = session.EndStamp.ToString(CultureInfo.InvariantCulture),
        };
        var url = $"{StreamApiBase}/bigriver/subPlayer.aspx?{ToQueryString(query)}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        // Legacy endpoints use a different UA than the JSON catalog ones.
        SetUA(req, LegacyUserAgent);
        using var res = await _http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) return null;
        var json = await res.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        if (json.TryGetProperty("streamLink", out var s) ||
            json.TryGetProperty("StreamLink", out s))
        {
            var link = s.GetString();
            return string.IsNullOrEmpty(link) ? null : link;
        }
        return null;
    }

    /// <summary>
    /// Probes every standard platformID, identifies the format of each
    /// returned URL, and picks the best one for browser playback.
    ///
    /// The four platformIDs {1, 4, 7, 10} are device tiers — each returns
    /// "some" URL whose actual format is identified by URL path patterns
    /// (`.flac16/`, `.alac16/`, `.m3u8`, etc.). Different tiers can return
    /// different formats for the same track, so we collect all and pick.
    /// </summary>
    public async Task<StreamPick?> ResolveBestStreamAsync(
        string trackId, Session session, CancellationToken ct = default)
    {
        var available = new List<StreamPick>();
        foreach (var p in ProbePlatforms)
        {
            try
            {
                var url = await GetStreamUrlAsync(trackId, p, session, ct);
                if (string.IsNullOrEmpty(url)) continue;
                var fmt = IdentifyFormat(url);
                available.Add(new StreamPick(url, p, fmt));
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "platform {p} failed for track {t}", p, trackId);
            }
        }
        if (available.Count == 0) return null;

        // Preference order for browser playback. FLAC plays everywhere modern;
        // ALAC plays in Chrome/Firefox/Safari with audio/mp4 MIME; HLS we punt.
        var pref = new[]
        {
            AudioFormat.Flac16,
            AudioFormat.Mqa24,
            AudioFormat.Alac16,
            AudioFormat.Aac150,
            AudioFormat.Hls,
        };
        foreach (var f in pref)
        {
            var match = available.FirstOrDefault(a => a.Format == f);
            if (match is not null) return match;
        }
        return available[0];
    }

    /// <summary>Inspect a stream URL to figure out what format the bytes are.</summary>
    public static AudioFormat IdentifyFormat(string url)
    {
        if (url.Contains(".flac16/", StringComparison.OrdinalIgnoreCase)) return AudioFormat.Flac16;
        if (url.Contains(".mqa24/", StringComparison.OrdinalIgnoreCase)) return AudioFormat.Mqa24;
        if (url.Contains(".alac16/", StringComparison.OrdinalIgnoreCase)) return AudioFormat.Alac16;
        if (url.Contains(".s360/", StringComparison.OrdinalIgnoreCase)) return AudioFormat.S360Ra;
        if (url.Contains(".aac150/", StringComparison.OrdinalIgnoreCase)) return AudioFormat.Aac150;
        if (url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase)) return AudioFormat.Hls;
        // Fall back to extension sniffing for unknown patterns.
        if (url.Contains(".flac", StringComparison.OrdinalIgnoreCase)) return AudioFormat.Flac16;
        if (url.Contains(".m4a", StringComparison.OrdinalIgnoreCase)) return AudioFormat.Aac150;
        return AudioFormat.Unknown;
    }

    /// <summary>
    /// MIME type to send to the browser. nugs's Akamai CDN returns wrong
    /// types (e.g. `audio/mp4a-latm` for ALAC), so we override based on
    /// what the URL pattern actually contains.
    /// </summary>
    public static string GetMimeType(AudioFormat f) => f switch
    {
        AudioFormat.Flac16 or AudioFormat.Mqa24 => "audio/flac",
        AudioFormat.Alac16 or AudioFormat.Aac150 or AudioFormat.S360Ra => "audio/mp4",
        AudioFormat.Hls => "application/vnd.apple.mpegurl",
        _ => "application/octet-stream",
    };

    /// <summary>
    /// Human-readable quality label for the dashboard. Describes the format
    /// tier only — exact sample rate / bit depth come from header parsing.
    /// </summary>
    public static string GetQualityLabel(AudioFormat f) => f switch
    {
        AudioFormat.Flac16 => "FLAC 16-bit lossless",
        AudioFormat.Mqa24 => "MQA 24-bit (FLAC)",
        AudioFormat.Alac16 => "ALAC 16-bit lossless",
        AudioFormat.S360Ra => "Sony 360 Reality Audio",
        AudioFormat.Aac150 => "AAC ~150 kbps",
        AudioFormat.Hls => "HLS adaptive",
        _ => "Unknown",
    };

    /// <summary>
    /// Fetches a public asset (image, etc.) from nugs.net with our mobile UA
    /// and no cookies. Caller disposes the response.
    /// </summary>
    public async Task<HttpResponseMessage> FetchPublicAsync(
        string url, CancellationToken ct = default)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        SetUA(req, MobileUserAgent);
        return await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    /// <summary>
    /// Streams audio bytes from the nugs CDN with the required Referer/UA.
    /// Forwards the client's Range header so &lt;audio&gt; seeking works.
    /// Caller is responsible for disposing the returned response message.
    /// </summary>
    public async Task<HttpResponseMessage> FetchAudioAsync(
        string url, string? rangeHeader, CancellationToken ct = default)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Referer", PlayerReferer);
        SetUA(req, MobileUserAgent);
        if (!string.IsNullOrEmpty(rangeHeader))
        {
            req.Headers.TryAddWithoutValidation("Range", rangeHeader);
        }
        return await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    /// <summary>
    /// Fetches the first 64 KB of a CDN audio file so its container header can
    /// be parsed for exact specs. Returns the body bytes plus the total file
    /// size from the 206 Content-Range. Returns (null, null) on any non-success.
    /// </summary>
    public async Task<(byte[]? Data, long? TotalSize)> FetchHeaderBytesAsync(
        string url, CancellationToken ct = default)
    {
        using var res = await FetchAudioAsync(url, "bytes=0-65535", ct);
        // Require a partial response. A 200 means the CDN ignored the Range and
        // would hand us the whole (hundreds-of-MB) file — never buffer that.
        if (res.StatusCode != System.Net.HttpStatusCode.PartialContent) return (null, null);

        long? total = null;
        if (res.Content.Headers.TryGetValues("Content-Range", out var vals))
        {
            // Format: "bytes 0-65535/1234567"
            var v = vals.FirstOrDefault();
            var slash = v?.LastIndexOf('/') ?? -1;
            if (v is not null && slash >= 0 && long.TryParse(v[(slash + 1)..], out var t))
            {
                total = t;
            }
        }

        // Bounded read: at most 64 KB into the buffer regardless of the body.
        const int cap = 65536;
        await using var stream = await res.Content.ReadAsStreamAsync(ct);
        var buf = new byte[cap];
        var read = await stream.ReadAtLeastAsync(buf, cap, throwOnEndOfStream: false, ct);
        return (read > 0 ? buf.AsSpan(0, read).ToArray() : null, total);
    }

    // --- helpers ----------------------------------------------------------

    private static string ToQueryString(Dictionary<string, string> q) =>
        string.Join("&", q.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

    private static void SetMobileUA(HttpRequestMessage req) =>
        SetUA(req, MobileUserAgent);

    private static void SetUA(HttpRequestMessage req, string ua)
    {
        req.Headers.UserAgent.Clear();
        req.Headers.TryAddWithoutValidation("User-Agent", ua);
    }
}

/// <summary>
/// View of the persisted session adapted for the calls that need stream params.
/// </summary>
public sealed record Session(
    string AccessToken,
    string UserId,
    string SubscriptionId,
    string PlanId,
    long StartStamp,
    long EndStamp,
    string PlanDescription,
    bool IsAccessible)
{
    public static Session From(PersistedSession state)
    {
        var sub = state.Sub;
        var isPromo = sub.promo is not null;
        var planId = isPromo ? sub.promo!.plan.id : sub.plan!.id;
        var planDesc = isPromo ? sub.promo!.plan.description : sub.plan!.description;
        return new Session(
            state.Tokens.AccessToken,
            state.UserId,
            sub.legacySubscriptionId,
            planId,
            ParseStamp(sub.startedAt),
            ParseStamp(sub.endsAt),
            planDesc,
            sub.isContentAccessible);
    }

    /// <summary>nugs returns timestamps as "MM/dd/yyyy HH:mm:ss" UTC.</summary>
    private static long ParseStamp(string s)
    {
        var dt = DateTime.ParseExact(
            s, "MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        return new DateTimeOffset(dt, TimeSpan.Zero).ToUnixTimeSeconds();
    }
}

// --- DTOs that mirror the wire format -----------------------------------

internal sealed record TokenResponse(string access_token, string refresh_token, int expires_in);

public sealed record TokenSet(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

public sealed record SubInfo(
    string legacySubscriptionId,
    string startedAt,
    string endsAt,
    bool isContentAccessible,
    SubInfo.PlanInfo? plan,
    SubInfo.PromoInfo? promo)
{
    public sealed record PlanInfo(string id, string description);
    public sealed record PromoInfo(PlanInfo plan);
}

public sealed record PersistedSession(TokenSet Tokens, string UserId, SubInfo Sub);

public enum AudioFormat
{
    Unknown,
    Alac16,
    Flac16,
    Mqa24,
    S360Ra,
    Aac150,
    Hls,
}

public sealed record StreamPick(string Url, int PlatformId, AudioFormat Format);
