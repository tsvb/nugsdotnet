using Microsoft.Extensions.Logging.Abstractions;
using Nugsdotnet.Core.Nugs;
using Xunit;

namespace Nugsdotnet.Tests;

public class StreamInspectorTests
{
    private static StreamInspector New() => new(NullLogger<StreamInspector>.Instance);

    [Fact]
    public void TryGetResolvedPick_returns_null_when_absent()
    {
        var s = New();
        Assert.Null(s.TryGetResolvedPick("missing"));
    }

    [Fact]
    public void Cached_pick_is_returned_without_being_consumed()
    {
        var s = New();
        var pick = new StreamPick("https://cdn/x.flac16/file", 7, AudioFormat.Flac16);
        s.CacheResolvedPick("t1", pick);

        // Read twice — read-through must NOT be single-use (unlike the
        // /stream-info seed), so preload + the eventual play both hit it.
        Assert.Same(pick, s.TryGetResolvedPick("t1"));
        Assert.Same(pick, s.TryGetResolvedPick("t1"));
    }
}
