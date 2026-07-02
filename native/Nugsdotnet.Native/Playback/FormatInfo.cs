using Nugsdotnet.Native.Core;

namespace Nugsdotnet.Native.Playback;

/// <summary>Display strings for resolved stream formats — the transport badge
/// and the dashboard's stream-quality rows.</summary>
public static class FormatInfo
{
    /// <summary>Compact transport badge, e.g. "FLAC 16".</summary>
    public static string Badge(AudioFormat f) => f switch
    {
        AudioFormat.Flac16 => "FLAC 16",
        AudioFormat.Alac16 => "ALAC 16",
        AudioFormat.Mqa24 => "MQA 24",
        AudioFormat.S360Ra => "360 RA",
        AudioFormat.Aac150 => "AAC 150",
        AudioFormat.Hls => "HLS",
        _ => "PCM",
    };

    /// <summary>Bit depth / rate line. nugs' flac16/alac16 tiers are CD-spec.</summary>
    public static string Signal(AudioFormat f) => f switch
    {
        AudioFormat.Flac16 or AudioFormat.Alac16 => "16-bit / 44.1 kHz",
        AudioFormat.Mqa24 => "24-bit (MQA)",
        AudioFormat.S360Ra => "object audio",
        AudioFormat.Aac150 => "lossy 150 kbps",
        AudioFormat.Hls => "adaptive (AAC)",
        _ => "—",
    };

    public static bool IsLossless(AudioFormat f) =>
        f is AudioFormat.Flac16 or AudioFormat.Alac16 or AudioFormat.Mqa24;
}
