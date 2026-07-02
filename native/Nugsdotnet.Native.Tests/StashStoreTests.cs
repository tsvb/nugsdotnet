using Nugsdotnet.Native.Core;

namespace Nugsdotnet.Native.Tests;

public class StashStoreTests
{
    private static StashEntry Entry(string id, int minutesAgo = 0) => new(
        id, $"Show {id}", "Artist", "6/24/2023", "Venue", "/images/x.jpg",
        DateTimeOffset.UtcNow.AddMinutes(-minutesAgo));

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), "nugsdotnet-tests", Path.GetRandomFileName(), "stash.json");

    // ---- pure toggle -----------------------------------------------------

    [Fact]
    public void Toggle_adds_new_entries_to_the_front()
    {
        var (entries, stashed) = StashStore.Toggle(new[] { Entry("a") }, Entry("b"));

        Assert.True(stashed);
        Assert.Equal(new[] { "b", "a" }, entries.Select(e => e.ContainerId));
    }

    [Fact]
    public void Toggle_removes_an_already_stashed_container()
    {
        var (entries, stashed) = StashStore.Toggle(
            new[] { Entry("a"), Entry("b"), Entry("c") }, Entry("b"));

        Assert.False(stashed);
        Assert.Equal(new[] { "a", "c" }, entries.Select(e => e.ContainerId));
    }

    // ---- disk roundtrip --------------------------------------------------

    [Fact]
    public async Task Toggle_then_Load_roundtrips_newest_first()
    {
        var store = new StashStore(TempPath());
        Assert.True(await store.ToggleAsync(Entry("a", minutesAgo: 5)));
        Assert.True(await store.ToggleAsync(Entry("b")));

        var loaded = await store.LoadAsync();
        Assert.Equal(new[] { "b", "a" }, loaded.Select(e => e.ContainerId));
        Assert.Equal("Show a", loaded[1].Title);

        Assert.True(await store.ContainsAsync("a"));
        Assert.False(await store.ContainsAsync("zzz"));
    }

    [Fact]
    public async Task Toggling_twice_unstashes_and_persists_the_removal()
    {
        var store = new StashStore(TempPath());
        Assert.True(await store.ToggleAsync(Entry("a")));
        Assert.False(await store.ToggleAsync(Entry("a")));

        Assert.Empty(await store.LoadAsync());
        Assert.False(await store.ContainsAsync("a"));
    }

    [Fact]
    public async Task Toggle_reports_the_persisted_state_when_the_write_fails()
    {
        // A directory at the store path makes the final rename throw while
        // reads treat it as missing — the toggle must not claim "stashed".
        var dirAsPath = Path.Combine(
            Path.GetTempPath(), "nugsdotnet-tests", Path.GetRandomFileName(), "stash.json");
        Directory.CreateDirectory(dirAsPath);
        var store = new StashStore(dirAsPath);

        Assert.False(await store.ToggleAsync(Entry("a")));
        Assert.False(await store.ContainsAsync("a"));
    }

    [Fact]
    public async Task Load_returns_empty_for_missing_or_corrupt_file()
    {
        var path = TempPath();
        Assert.Empty(await new StashStore(path).LoadAsync());

        await File.WriteAllTextAsync(path, "{not json");
        Assert.Empty(await new StashStore(path).LoadAsync());
    }
}
