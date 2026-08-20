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
    public void PickBest_honors_user_preferred_format_when_available()
    {
        var picks = new[]
        {
            new StreamPick("u-flac", 2, AudioFormat.Flac16),
            new StreamPick("u-aac", 5, AudioFormat.Aac150),
        };
        Assert.Equal("u-aac", NugsStreamResolver.PickBest(picks, AudioFormat.Aac150)!.Url);
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

    [Fact]
    public async Task ResolveBestStream_stops_once_flac_lands()
    {
        var handler = new ProbeHandler { FlacPlatform = 1, SlowDelayMs = 4000 };
        var resolver = new NugsStreamResolver(new HttpClient(handler));
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var pick = await resolver.ResolveBestStreamAsync("t1", TestSession());

        sw.Stop();
        Assert.Equal(AudioFormat.Flac16, pick!.Format);
        Assert.Contains(".flac16/", pick.Url);
        Assert.True(sw.ElapsedMilliseconds < 2000,
            $"expected early exit after FLAC, took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task GetStreamUrl_drops_non_https_and_loopback_links()
    {
        var handler = new ProbeHandler { LinkOverride = "http://127.0.0.1/secret.flac" };
        var resolver = new NugsStreamResolver(new HttpClient(handler));

        Assert.Null(await resolver.GetStreamUrlAsync("t1", 1, TestSession()));
        Assert.Null(await resolver.ResolveBestStreamAsync("t1", TestSession()));
    }

    [Fact]
    public async Task GetStreamUrl_drops_an_oversized_probe_body()
    {
        var handler = new ProbeHandler { OversizedBody = true };
        var resolver = new NugsStreamResolver(new HttpClient(handler));
        Assert.Null(await resolver.GetStreamUrlAsync("t1", 1, TestSession()));
    }

    private static Session TestSession() => new(
        "access", "user", "sub", "plan", 0, 0, "VIP", true);

    private sealed class ProbeHandler : HttpMessageHandler
    {
        public int FlacPlatform { get; init; } = 1;
        public int SlowDelayMs { get; init; } = 0;
        public string? LinkOverride { get; init; }
        public bool OversizedBody { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var platform = PlatformId(request.RequestUri!);
            if (SlowDelayMs > 0 && platform != FlacPlatform)
                await Task.Delay(SlowDelayMs, cancellationToken);

            string body;
            if (OversizedBody)
            {
                body = "{\"streamLink\":\"" + new string('x', BoundedContent.StreamProbe + 8) + "\"}";
            }
            else
            {
                var link = LinkOverride ?? (platform == FlacPlatform
                    ? "https://cdn.nugs.net/x.flac16/file"
                    : "https://cdn.nugs.net/x.aac150/file");
                body = $"{{\"streamLink\":\"{link}\"}}";
            }
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body),
            };
        }

        private static int PlatformId(Uri uri)
        {
            foreach (var part in uri.Query.TrimStart('?').Split('&'))
            {
                var kv = part.Split('=', 2);
                if (kv.Length == 2 && kv[0] == "platformID")
                    return int.Parse(Uri.UnescapeDataString(kv[1]));
            }
            return -1;
        }
    }
}
