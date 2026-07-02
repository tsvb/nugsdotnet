using Nugsdotnet.Native.Core;

namespace Nugsdotnet.Native.Tests;

public class PlaybackStateStoreTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), "nugsdotnet-tests", Path.GetRandomFileName(), "playback.json");

    private static PlaybackSnapshot Snapshot(int index = 1) => new(
        new[]
        {
            new NowPlaying("t1", "Opener", "Goose", "2023-06-24 The Cap", "/images/a.jpg", "c100"),
            new NowPlaying("t2", "Closer", "Goose", "2023-06-24 The Cap", "/images/a.jpg", "c100"),
        },
        index, PositionSeconds: 123.5, Volume: 0.8, IsMuted: false);

    [Fact]
    public async Task Roundtrips_queue_index_position_and_setup()
    {
        var store = new PlaybackStateStore(TempPath());
        await store.SaveAsync(Snapshot());

        var loaded = await store.LoadAsync();

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.Queue.Count);
        Assert.Equal("t2", loaded.Queue[1].TrackId);
        Assert.Equal("c100", loaded.Queue[1].ContainerId);   // album click-through survives
        Assert.Equal(1, loaded.Index);
        Assert.Equal(123.5, loaded.PositionSeconds);
        Assert.Equal(0.8, loaded.Volume);
        Assert.False(loaded.IsMuted);
    }

    [Fact]
    public async Task Synchronous_save_is_equivalent()
    {
        var store = new PlaybackStateStore(TempPath());
        store.Save(Snapshot(index: 0));

        var loaded = await store.LoadAsync();
        Assert.Equal(0, loaded!.Index);
    }

    [Fact]
    public async Task Missing_or_corrupt_file_yields_nothing_to_resume()
    {
        var path = TempPath();
        Assert.Null(await new PlaybackStateStore(path).LoadAsync());

        await File.WriteAllTextAsync(path, "{not json");
        Assert.Null(await new PlaybackStateStore(path).LoadAsync());
    }

    [Fact]
    public async Task Unusable_snapshots_yield_nothing_to_resume()
    {
        var store = new PlaybackStateStore(TempPath());

        await store.SaveAsync(Snapshot() with { Queue = Array.Empty<NowPlaying>(), Index = 0 });
        Assert.Null(await store.LoadAsync());

        await store.SaveAsync(Snapshot() with { Index = 99 });   // index outside the queue
        Assert.Null(await store.LoadAsync());
    }
}
