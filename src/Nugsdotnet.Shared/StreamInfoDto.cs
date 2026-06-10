namespace Nugsdotnet.Shared;

/// <summary>
/// Returned by GET /api/stream-info/{trackId}. Describes the resolved audio
/// stream for the now-playing track so the dashboard can show real quality.
/// The exact spec fields (sample rate, bit depth, channels, duration, size,
/// bitrate) are null when the server could not parse the file header — the
/// client then falls back to <see cref="FormatLabel"/>.
/// </summary>
public sealed record StreamInfoResponse(
    string FormatName,          // AudioFormat enum name, e.g. "Flac16"
    string FormatLabel,         // human label, e.g. "FLAC 16-bit lossless"
    int PlatformId,
    bool Playable,              // false for HLS (currently unsupported in-browser)
    int? SampleRate = null,     // Hz
    int? BitDepth = null,
    int? Channels = null,
    double? DurationSeconds = null,
    long? FileSizeBytes = null,
    double? AvgBitrateKbps = null);
