using Nugsdotnet.Native.Core;

namespace Nugsdotnet.Native.Tests;

public class NugsUriTests
{
    [Theory]
    [InlineData("https://stream.nugs.net/x.flac16/file")]
    [InlineData("https://assets-01.nugscdn.net/livedownloads/images/x.jpg")]
    [InlineData("https://cdn.nugs.net/playlist.m3u8")]
    public void IsSafeHttps_accepts_public_https_hosts(string url)
    {
        Assert.True(NugsUri.IsSafeHttps(url, out var uri));
        Assert.Equal(Uri.UriSchemeHttps, uri!.Scheme);
    }

    [Theory]
    [InlineData("http://stream.nugs.net/file")]
    [InlineData("https://127.0.0.1/file")]
    [InlineData("https://[::1]/file")]
    [InlineData("https://localhost/file")]
    [InlineData("https://foo.localhost/file")]
    [InlineData("https://printer.local/file")]
    [InlineData("https://user:pass@stream.nugs.net/file")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://stream.nugs.net/file")]
    [InlineData("not a url")]
    [InlineData("")]
    [InlineData(null)]
    public void IsSafeHttps_rejects_local_and_non_https(string? url)
        => Assert.False(NugsUri.IsSafeHttps(url, out _));

    [Fact]
    public void ResolveImageUrl_prefixes_catalog_relative_paths()
    {
        var url = NugsUri.ResolveImageUrl("/images/art.jpg");
        Assert.Equal("https://assets-01.nugscdn.net/livedownloads/images/art.jpg?h=400", url);
    }

    [Fact]
    public void ResolveImageUrl_rejects_http_and_loopback()
    {
        Assert.Null(NugsUri.ResolveImageUrl("http://evil.example/x.png"));
        Assert.Null(NugsUri.ResolveImageUrl("https://127.0.0.1/x.png"));
        Assert.Null(NugsUri.ResolveImageUrl("javascript:alert(1)"));
        Assert.Null(NugsUri.ResolveImageUrl("images/no-leading-slash.jpg"));
    }
}
