using Nugsdotnet.Native.Core;

namespace Nugsdotnet.Native.Tests;

public class StreamResolverTests
{
    [Theory]
    [InlineData("https://cdn/x.flac16/file", AudioFormat.Flac16)]
    [InlineData("https://cdn/x.mqa24/file", AudioFormat.Mqa24)]
    [InlineData("https://cdn/x.alac16/file", AudioFormat.Alac16)]
    [InlineData("https://cdn/x.s360/file", AudioFormat.S360Ra)]
    [InlineData("https://cdn/x.aac150/file", AudioFormat.Aac150)]
    [InlineData("https://cdn/playlist.m3u8", AudioFormat.Hls)]
    [InlineData("https://cdn/bare.flac", AudioFormat.Flac16)]
    [InlineData("https://cdn/bare.m4a", AudioFormat.Aac150)]
    [InlineData("https://cdn/mystery.bin", AudioFormat.Unknown)]
    public void IdentifyFormat_maps_url_patterns(string url, AudioFormat expected)
        => Assert.Equal(expected, NugsStreamResolver.IdentifyFormat(url));

    [Fact]
    public void PickBest_prefers_flac_over_lossy_and_lossless_alternatives()
    {
        var picks = new[]
        {
            new StreamPick("u-aac", 5, AudioFormat.Aac150),
            new StreamPick("u-flac", 2, AudioFormat.Flac16),
            new StreamPick("u-alac", 1, AudioFormat.Alac16),
        };
        Assert.Equal("u-flac", NugsStreamResolver.PickBest(picks)!.Url);
    }

    [Fact]
    public void PickBest_treats_hls_as_last_resort()
    {
        var picks = new[]
        {
            new StreamPick("u-hls", 10, AudioFormat.Hls),
            new StreamPick("u-aac", 5, AudioFormat.Aac150),
        };
        Assert.Equal("u-aac", NugsStreamResolver.PickBest(picks)!.Url);
    }

    [Fact]
    public void PickBest_falls_back_to_first_when_nothing_preferred()
    {
        var picks = new[] { new StreamPick("u-unknown", 9, AudioFormat.Unknown) };
        Assert.Equal("u-unknown", NugsStreamResolver.PickBest(picks)!.Url);
    }

    [Fact]
    public void PickBest_returns_null_for_empty_set()
        => Assert.Null(NugsStreamResolver.PickBest(Array.Empty<StreamPick>()));

    [Theory]
    [InlineData(AudioFormat.Flac16, "audio/flac")]
    [InlineData(AudioFormat.Mqa24, "audio/flac")]
    [InlineData(AudioFormat.Alac16, "audio/mp4")]
    [InlineData(AudioFormat.Aac150, "audio/mp4")]
    [InlineData(AudioFormat.Hls, "application/vnd.apple.mpegurl")]
    public void GetMimeType_maps_format_to_container(AudioFormat f, string expected)
        => Assert.Equal(expected, NugsStreamResolver.GetMimeType(f));
}
