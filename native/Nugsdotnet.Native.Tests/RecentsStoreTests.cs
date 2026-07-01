using System.Text.Json.Nodes;
using Nugsdotnet.Native.Core;

namespace Nugsdotnet.Native.Tests;

public class RecentsStoreTests
{
    private static RecentPlay Play(string id, int minutesAgo = 0) => new(
        id, $"Show {id}", "Artist", "6/24/2023", "Venue", "/images/x.jpg",
        DateTimeOffset.UtcNow.AddMinutes(-minutesAgo));

    // ---- pure merge ------------------------------------------------------

    [Fact]
    public void Merge_inserts_newest_first()
    {
        var merged = RecentsStore.Merge(new[] { Play("a"), Play("b") }, Play("c"));
        Assert.Equal(new[] { "c", "a", "b" }, merged.Select(p => p.ContainerId));
    }

    [Fact]
    public void Merge_moves_replayed_container_to_front_without_duplicating()
    {
        var merged = RecentsStore.Merge(new[] { Play("a"), Play("b"), Play("c") }, Play("b"));
        Assert.Equal(new[] { "b", "a", "c" }, merged.Select(p => p.ContainerId));
    }

    [Fact]
    public void Merge_caps_the_list()
    {
        var full = Enumerable.Range(0, RecentsStore.Cap).Select(i => Play($"e{i}")).ToList();
        var merged = RecentsStore.Merge(full, Play("new"));

        Assert.Equal(RecentsStore.Cap, merged.Count);
        Assert.Equal("new", merged[0].ContainerId);
        Assert.DoesNotContain(merged, p => p.ContainerId == $"e{RecentsStore.Cap - 1}");
    }

    // ---- disk roundtrip --------------------------------------------------

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), "nugsdotnet-tests", Path.GetRandomFileName(), "recents.json");

    [Fact]
    public async Task Record_then_Load_roundtrips()
    {
        var store = new RecentsStore(TempPath());
        await store.RecordAsync(Play("a", minutesAgo: 5));
        await store.RecordAsync(Play("b"));

        var loaded = await store.LoadAsync();
        Assert.Equal(new[] { "b", "a" }, loaded.Select(p => p.ContainerId));
        Assert.Equal("Show a", loaded[1].Title);
    }

    [Fact]
    public async Task Load_returns_empty_for_missing_or_corrupt_file()
    {
        var path = TempPath();
        Assert.Empty(await new RecentsStore(path).LoadAsync());

        await File.WriteAllTextAsync(path, "{not json");
        Assert.Empty(await new RecentsStore(path).LoadAsync());
    }

    // ---- ParseArtists hygiene (feeds the same dashboard) -------------------

    [Fact]
    public void ParseArtists_trims_names_so_sorting_is_sane()
    {
        var raw = JsonNode.Parse("""
        {
          "artists": [
            { "artistID": "2", "artistName": " Paco de Lucia" },
            { "artistID": "1", "artistName": "10,000 Maniacs" },
            { "artistID": "3", "artistName": "   " }
          ]
        }
        """);

        var artists = NugsCatalog.ParseArtists(raw);

        Assert.Equal(2, artists.Count);                       // blank-after-trim dropped
        Assert.Equal("10,000 Maniacs", artists[0].Name);      // numbers before letters
        Assert.Equal("Paco de Lucia", artists[1].Name);       // trimmed, sorted under P
    }
}
