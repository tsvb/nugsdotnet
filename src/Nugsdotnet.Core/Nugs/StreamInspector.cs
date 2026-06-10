using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Nugsdotnet.Shared;

namespace Nugsdotnet.Core.Nugs;

/// <summary>
/// Resolves and caches per-track stream metadata for the dashboard.
///
/// It owns the expensive bits: the /api/play handler seeds the resolved
/// <see cref="StreamPick"/> via <see cref="StorePick"/> so a following
/// /api/stream-info request doesn't re-probe nugs, and it parses the audio
/// file header (FLAC STREAMINFO / MP4 atoms) for exact sample rate, bit depth,
/// channels and duration.
///
/// Singleton — the cache must outlive individual requests. It does NOT capture
/// a <see cref="NugsClient"/> (that's a typed HttpClient and shouldn't be held
/// by a singleton); callers pass the request-scoped client into each method.
/// </summary>
public sealed class StreamInspector
{
    private readonly ILogger<StreamInspector> _log;

    // Picks seeded by /api/play, reused to avoid a second 4-platform probe.
    private readonly ConcurrentDictionary<string, StreamPick> _seededPicks = new();

    // Fully-built entries (pick + parsed specs). Lazy<Task> ensures exactly one
    // build runs per trackId even under concurrent /play + /stream-info hits.
    private readonly ConcurrentDictionary<string, Lazy<Task<CachedEntry?>>> _cache = new();

    // CDN signed URLs rotate on session boundaries — don't trust a pick forever.
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(4);

    public StreamInspector(ILogger<StreamInspector> log) => _log = log;

    internal sealed record AudioSpecs(
        int SampleRate, int BitDepth, int Channels, double DurationSeconds, long? FileSizeBytes);

    private sealed record CachedEntry(StreamPick Pick, AudioSpecs? Specs, DateTimeOffset ExpiresAt);

    /// <summary>
    /// Seeds the cache with a pick already resolved by /api/play so a following
    /// /api/stream-info call skips re-probing. Header parsing still happens
    /// lazily on the first <see cref="GetStreamInfoAsync"/>.
    /// </summary>
    public void StorePick(string trackId, StreamPick pick) => _seededPicks[trackId] = pick;

    public async Task<StreamInfoResponse?> GetStreamInfoAsync(
        string trackId, Session session, NugsClient nugs, CancellationToken ct)
    {
        var entry = await GetOrBuildAsync(trackId, session, nugs, ct);
        return entry is null ? null : BuildResponse(entry);
    }

    private async Task<CachedEntry?> GetOrBuildAsync(
        string trackId, Session session, NugsClient nugs, CancellationToken ct)
    {
        while (true)
        {
            // The cached Task is shared across callers, so it must not be bound
            // to any single request's token — build it under None and apply each
            // caller's token only to their own await below.
            var lazy = _cache.GetOrAdd(trackId,
                _ => new Lazy<Task<CachedEntry?>>(() => BuildAsync(trackId, session, nugs, CancellationToken.None)));

            CachedEntry? entry;
            try
            {
                entry = await lazy.Value.WaitAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The build itself failed — don't let it stick in the cache.
                // (A caller-cancellation just propagates; the shared build lives on
                // for other callers.)
                _cache.TryRemove(new KeyValuePair<string, Lazy<Task<CachedEntry?>>>(trackId, lazy));
                throw;
            }

            if (entry is not null && entry.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                _cache.TryRemove(new KeyValuePair<string, Lazy<Task<CachedEntry?>>>(trackId, lazy));
                continue; // expired — rebuild
            }
            return entry;
        }
    }

    private async Task<CachedEntry?> BuildAsync(
        string trackId, Session session, NugsClient nugs, CancellationToken ct)
    {
        // Consume the seed (single-use): stops an expired signed URL being
        // reused after the cache entry's TTL, and keeps _seededPicks from
        // growing without bound. Falls back to a fresh probe when absent.
        var pick = _seededPicks.TryRemove(trackId, out var seeded)
            ? seeded
            : await nugs.ResolveBestStreamAsync(trackId, session, ct);
        if (pick is null) return null;

        var specs = await ParseSpecsAsync(pick, nugs, ct);
        return new CachedEntry(pick, specs, DateTimeOffset.UtcNow + Ttl);
    }

    private async Task<AudioSpecs?> ParseSpecsAsync(StreamPick pick, NugsClient nugs, CancellationToken ct)
    {
        if (pick.Format is AudioFormat.Hls or AudioFormat.Unknown) return null;
        try
        {
            var (data, total) = await nugs.FetchHeaderBytesAsync(pick.Url, ct);
            if (data is null) return null;
            return pick.Format switch
            {
                AudioFormat.Flac16 or AudioFormat.Mqa24 => ParseFlac(data, total),
                AudioFormat.Alac16 or AudioFormat.Aac150 or AudioFormat.S360Ra => ParseMp4(data, total),
                _ => null,
            };
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "spec parse failed for {url}", pick.Url);
            return null;
        }
    }

    private StreamInfoResponse BuildResponse(CachedEntry e)
    {
        var s = e.Specs;
        double? bitrate = null;
        if (s is { DurationSeconds: > 0, FileSizeBytes: > 0 })
        {
            bitrate = s.FileSizeBytes.Value * 8.0 / s.DurationSeconds / 1000.0;
        }
        return new StreamInfoResponse(
            FormatName: e.Pick.Format.ToString(),
            FormatLabel: NugsClient.GetQualityLabel(e.Pick.Format),
            PlatformId: e.Pick.PlatformId,
            Playable: e.Pick.Format != AudioFormat.Hls,
            SampleRate: s?.SampleRate,
            BitDepth: s?.BitDepth,
            Channels: s?.Channels,
            DurationSeconds: s?.DurationSeconds,
            FileSizeBytes: s?.FileSizeBytes,
            AvgBitrateKbps: bitrate);
    }

    // --- header parsing ---------------------------------------------------

    /// <summary>
    /// Parses a FLAC STREAMINFO block. File layout: "fLaC" (4 bytes) +
    /// METADATA_BLOCK_HEADER (4 bytes) + STREAMINFO data. Within STREAMINFO:
    /// 16b min-block, 16b max-block, 24b min-frame, 24b max-frame, then a
    /// 64-bit field = 20b sample rate, 3b (channels-1), 5b (bits-per-sample-1),
    /// 36b total-samples. That 64-bit field starts at file byte offset 18
    /// (4 + 4 + 2 + 2 + 3 + 3). Layout per https://xiph.org/flac/format.html.
    /// </summary>
    private static AudioSpecs? ParseFlac(byte[] d, long? total)
    {
        if (d.Length < 26) return null;
        if (d[0] != (byte)'f' || d[1] != (byte)'L' || d[2] != (byte)'a' || d[3] != (byte)'C')
            return null;
        // d[4] low 7 bits = metadata block type; 0 = STREAMINFO (always first).
        if ((d[4] & 0x7F) != 0) return null;

        ulong x = 0;
        for (var i = 18; i < 26; i++) x = (x << 8) | d[i];

        var sampleRate = (int)((x >> 44) & 0xFFFFF);
        var channels = (int)((x >> 41) & 0x7) + 1;
        var bitDepth = (int)((x >> 36) & 0x1F) + 1;
        var totalSamples = x & 0xFFFFFFFFF; // low 36 bits
        if (sampleRate == 0) return null;

        var duration = (double)totalSamples / sampleRate;
        return new AudioSpecs(sampleRate, bitDepth, channels, duration, total);
    }

    /// <summary>
    /// Best-effort ISO-BMFF (MP4) parse for ALAC/AAC: walks moov -> mvhd (for
    /// duration) and moov -> trak -> mdia -> minf -> stbl -> stsd -> audio
    /// sample entry (for sample rate / channels / bit depth). Returns null if
    /// moov isn't within the fetched header window (it can live at end-of-file).
    /// </summary>
    private static AudioSpecs? ParseMp4(byte[] d, long? total)
    {
        if (!FindBox(d, 0, d.Length, "moov", out var moovS, out var moovE)) return null;

        double duration = 0;
        if (FindBox(d, moovS, moovE, "mvhd", out var mvS, out var mvE) && mvE - mvS >= 1)
        {
            var version = d[mvS];
            if (version == 0 && mvE - mvS >= 20)
            {
                var timescale = ReadU32(d, mvS + 12);
                var dur = ReadU32(d, mvS + 16);
                if (timescale != 0) duration = (double)dur / timescale;
            }
            else if (version == 1 && mvE - mvS >= 32)
            {
                var timescale = ReadU32(d, mvS + 20);
                var dur = ReadU64(d, mvS + 24);
                if (timescale != 0) duration = (double)dur / timescale;
            }
        }

        if (!FindBox(d, moovS, moovE, "trak", out var trakS, out var trakE)) return null;
        if (!FindBox(d, trakS, trakE, "mdia", out var mdS, out var mdE)) return null;
        if (!FindBox(d, mdS, mdE, "minf", out var miS, out var miE)) return null;
        if (!FindBox(d, miS, miE, "stbl", out var stS, out var stE)) return null;
        if (!FindBox(d, stS, stE, "stsd", out var sdS, out var sdE)) return null;

        // stsd: 1b version + 3b flags + 4b entryCount, then the first sample
        // entry box (4b size + 4b type, then the AudioSampleEntry body).
        var entry = sdS + 8;
        if (entry + 36 > sdE || entry + 36 > d.Length) return null;

        var channels = ReadU16(d, entry + 24);
        var bitDepth = ReadU16(d, entry + 26);
        var sampleRate = ReadU16(d, entry + 32); // high 16 bits of a 16.16 fixed value = Hz
        if (sampleRate == 0 || channels == 0) return null;
        if (bitDepth == 0) bitDepth = 16; // some ALAC entries report 0 here

        return new AudioSpecs(sampleRate, bitDepth, channels, duration, total);
    }

    /// <summary>
    /// Finds the first child atom of <paramref name="type"/> within [start, end),
    /// returning the payload range [contentStart, contentEnd). Handles 64-bit
    /// extended sizes (size field == 1) and size==0 (extends to container end).
    /// </summary>
    private static bool FindBox(byte[] d, int start, int end, string type, out int contentStart, out int contentEnd)
    {
        contentStart = contentEnd = 0;
        var pos = start;
        while (pos + 8 <= end && pos + 8 <= d.Length)
        {
            long size = ReadU32(d, pos);
            var headerLen = 8;
            if (size == 1)
            {
                if (pos + 16 > d.Length) return false;
                size = (long)ReadU64(d, pos + 8);
                headerLen = 16;
            }
            else if (size == 0)
            {
                size = end - pos;
            }
            if (size < headerLen) return false;
            var boxEnd = pos + size;

            if (d[pos + 4] == type[0] && d[pos + 5] == type[1] &&
                d[pos + 6] == type[2] && d[pos + 7] == type[3])
            {
                contentStart = pos + headerLen;
                contentEnd = (int)Math.Min(boxEnd, d.Length);
                return true;
            }
            if (boxEnd <= pos) return false; // guard against malformed zero-advance
            // Clamp before narrowing: a box larger than our 64 KB buffer ends the
            // walk rather than wrapping (int)boxEnd to a negative offset.
            pos = (int)Math.Min(boxEnd, (long)d.Length + 1);
        }
        return false;
    }

    private static int ReadU16(byte[] d, int o) => (d[o] << 8) | d[o + 1];

    private static uint ReadU32(byte[] d, int o) =>
        ((uint)d[o] << 24) | ((uint)d[o + 1] << 16) | ((uint)d[o + 2] << 8) | d[o + 3];

    private static ulong ReadU64(byte[] d, int o)
    {
        ulong v = 0;
        for (var i = 0; i < 8; i++) v = (v << 8) | d[o + i];
        return v;
    }
}
