using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Nugsdotnet.Native.Core;

/// <summary>
/// Resolves the best playable CDN stream for a track. Probes each device-tier
/// platformID, identifies the real format from the returned URL path, and picks
/// by a browser-/decoder-friendly preference order. The probe + pick logic is
/// split so the format identification and preference ordering stay pure and
/// unit-testable; only the network probe needs a session.
/// </summary>
public sealed class NugsStreamResolver
{
    private readonly HttpClient _http;
    private Func<AudioFormat?>? _preferredFormat;

    public NugsStreamResolver(HttpClient http) => _http = http;

    /// <summary>When set, the resolver prefers this format if available (falls back to default order).</summary>
    public void SetPreferredFormatProvider(Func<AudioFormat?>? provider) => _preferredFormat = provider;

    /// <summary>Preference order for native playback. Media Foundation decodes
    /// FLAC/ALAC/AAC natively on Win10+, so all are fair game; HLS is last.</summary>
    private static readonly AudioFormat[] Preference =
    {
        AudioFormat.Flac16,
        AudioFormat.Mqa24,
        AudioFormat.Alac16,
        AudioFormat.Aac150,
        AudioFormat.Hls,
    };

    /// <summary>Resolves the underlying CDN URL for a track at one platformID.
    /// Null when nugs has no stream for that combination.</summary>
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
        var url = $"{NugsConstants.StreamApiBase}/bigriver/subPlayer.aspx?{Query.ToQueryString(query)}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        NugsAuth.SetUA(req, NugsConstants.LegacyUserAgent);  // legacy endpoints use a different UA
        using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!res.IsSuccessStatusCode) return null;
        // Legacy *.aspx may not send application/json; parse the body directly.
        string body;
        try
        {
            body = await BoundedContent.ReadStringAsync(res, BoundedContent.StreamProbe, ct);
        }
        catch
        {
            return null;
        }
        using var doc = JsonDocument.Parse(body);
        var json = doc.RootElement;
        if (json.TryGetProperty("streamLink", out var s) ||
            json.TryGetProperty("StreamLink", out s))
        {
            var link = s.GetString();
            // CDN URL is untrusted input from the API — refuse anything that
            // isn't a public https URL before the player fetches it.
            return NugsUri.IsSafeHttps(link, out _) ? link : null;
        }
        return null;
    }

    /// <summary>Probes every device tier in parallel and returns the best
    /// available pick. Stops early once the top-preference format (FLAC) lands
    /// so a typical play doesn't wait on the lossy tiers.</summary>
    public async Task<StreamPick?> ResolveBestStreamAsync(
        string trackId, Session session, CancellationToken ct = default)
    {
        var available = new ConcurrentBag<StreamPick>();
        using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var tasks = NugsConstants.ProbePlatforms.Select(async p =>
        {
            try
            {
                var url = await GetStreamUrlAsync(trackId, p, session, probeCts.Token);
                if (string.IsNullOrEmpty(url)) return;
                var pick = new StreamPick(url, p, IdentifyFormat(url));
                available.Add(pick);
                if (pick.Format == Preference[0])
                    probeCts.Cancel();   // FLAC — remaining tiers are wasted work
            }
            catch (OperationCanceledException)
            {
                // Expected when a better pick already landed, or the caller cancelled.
            }
            catch
            {
                // A single tier failing is expected; keep probing the rest.
            }
        });
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            // WhenAll surfaces OCE if a task faulted with it before we caught it.
        }
        return PickBest(available.ToArray(), _preferredFormat?.Invoke());
    }

    /// <summary>Pure preference selection over a probed set. Null if empty.</summary>
    public static StreamPick? PickBest(IReadOnlyList<StreamPick> available, AudioFormat? preferred = null)
    {
        if (available.Count == 0) return null;
        if (preferred is { } pf)
        {
            var preferredMatch = available.FirstOrDefault(a => a.Format == pf);
            if (preferredMatch is not null) return preferredMatch;
        }
        foreach (var f in Preference)
        {
            var match = available.FirstOrDefault(a => a.Format == f);
            if (match is not null) return match;
        }
        return available[0];
    }

    /// <summary>Identifies the audio format from a CDN URL's path patterns.</summary>
    public static AudioFormat IdentifyFormat(string url)
    {
        if (url.Contains(".flac16/", StringComparison.OrdinalIgnoreCase)) return AudioFormat.Flac16;
        if (url.Contains(".mqa24/", StringComparison.OrdinalIgnoreCase)) return AudioFormat.Mqa24;
        if (url.Contains(".alac16/", StringComparison.OrdinalIgnoreCase)) return AudioFormat.Alac16;
        if (url.Contains(".s360/", StringComparison.OrdinalIgnoreCase)) return AudioFormat.S360Ra;
        if (url.Contains(".aac150/", StringComparison.OrdinalIgnoreCase)) return AudioFormat.Aac150;
        if (url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase)) return AudioFormat.Hls;
        if (url.Contains(".flac", StringComparison.OrdinalIgnoreCase)) return AudioFormat.Flac16;
        if (url.Contains(".m4a", StringComparison.OrdinalIgnoreCase)) return AudioFormat.Aac150;
        return AudioFormat.Unknown;
    }

    /// <summary>Best-effort MIME type for a format, used when handing a stream to
    /// MediaSource (Media Foundation is tolerant but a correct hint helps).</summary>
    public static string GetMimeType(AudioFormat f) => f switch
    {
        AudioFormat.Flac16 or AudioFormat.Mqa24 => "audio/flac",
        AudioFormat.Alac16 or AudioFormat.Aac150 or AudioFormat.S360Ra => "audio/mp4",
        AudioFormat.Hls => "application/vnd.apple.mpegurl",
        _ => "audio/mpeg",
    };

    /// <summary>Human-readable quality label for the now-playing/dashboard UI.</summary>
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
}
