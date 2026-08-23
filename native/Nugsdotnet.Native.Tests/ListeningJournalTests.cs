using System.Text.Json;
using Nugsdotnet.Native.Core;

namespace Nugsdotnet.Native.Tests;

public class ListeningJournalTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"journal-{Guid.NewGuid():N}.json");

    private static JournalState Sample() => new(
        Days: [new JournalDay("2026-08-20", 3600, 8,
            [new JournalArtistPlay("Goose", 3600)],
            [new JournalShowPlay("c1", "Give It Time", 3600)])],
        Shows: [new JournalShow("c1", "Goose", "Give It Time", 3600, 8,
            DateTimeOffset.Parse("2026-08-20T22:00:00Z"))],
        Milestones: [new MilestoneReached("shows-10", "2026-08-20")],
        TracksCompleted: 8);

    /// <summary>Records hold lists — compare by serialized bytes, not reference.</summary>
    private static void AssertSameState(JournalState expected, JournalState actual) =>
        Assert.Equal(
            JsonSerializer.SerializeToUtf8Bytes(expected),
            JsonSerializer.SerializeToUtf8Bytes(actual));

    // ---- Accumulate -------------------------------------------------------

    [Fact]
    public void Accumulate_creates_the_day_and_show_on_first_touch()
    {
        var state = ListeningJournal.Accumulate(
            new JournalState([], [], [], 0), "2026-08-21", "c9", "Phish", "Bathtub Gin", 15, 0);

        var day = Assert.Single(state.Days);
        Assert.Equal("2026-08-21", day.Date);
        Assert.Equal(15, day.Seconds);
        Assert.Equal("Phish", Assert.Single(day.Artists).Name);
        var show = Assert.Single(day.Shows);
        Assert.Equal("c9", show.ContainerId);
        Assert.Equal("Bathtub Gin", show.Title);
        var total = Assert.Single(state.Shows);
        Assert.Equal("c9", total.ContainerId);
        Assert.Equal(0, state.TracksCompleted);
    }

    [Fact]
    public void Accumulate_merges_into_existing_day_show_and_artist()
    {
        var state = Sample();
        state = ListeningJournal.Accumulate(state, "2026-08-20", "c1", "Goose", "Give It Time", 45, 1);
        state = ListeningJournal.Accumulate(state, "2026-08-20", "c1", "Goose", "Give It Time", 45, 0);

        var day = Assert.Single(state.Days);
        Assert.Equal(3690, day.Seconds);
        Assert.Equal(9, day.TracksCompleted);
        Assert.Equal(3600 + 90, Assert.Single(day.Artists).Seconds);
        Assert.Equal(3600 + 90, Assert.Single(state.Shows).Seconds);
        Assert.Equal(9, state.TracksCompleted);
    }

    [Fact]
    public void Accumulate_keeps_nights_and_artists_apart()
    {
        var state = Sample();
        state = ListeningJournal.Accumulate(state, "2026-08-21", "c2", "Goose", "Hot Tea", 60, 0);

        Assert.Equal(2, state.Days.Count);
        Assert.Equal(2, state.Shows.Count);
        Assert.Equal("2026-08-20", state.Days[0].Date);   // ascending by date
        Assert.Equal(60, state.Days[1].Seconds);
        Assert.Single(state.Days[0].Artists);
    }

    // ---- Prune ------------------------------------------------------------

    [Fact]
    public void Prune_keeps_the_newest_days_ascending()
    {
        var days = Enumerable.Range(0, ListeningJournal.DayCap + 50)
            .Select(i => new JournalDay(
                DateOnly.FromDateTime(new DateTime(2026, 1, 1)).AddDays(i).ToString("yyyy-MM-dd"),
                600, 1, [], []))
            .ToList();
        var state = ListeningJournal.Prune(new JournalState(days, [], [], 400));

        Assert.Equal(ListeningJournal.DayCap, state.Days.Count);
        Assert.Equal("2026-02-20", state.Days[0].Date);   // oldest 50 pruned (Jan 1 + 50 days)
        Assert.True(string.CompareOrdinal(state.Days[0].Date, state.Days[^1].Date) < 0);
    }

    // ---- store round-trip ---------------------------------------------------

    [Fact]
    public async Task Save_then_load_round_trips_the_state()
    {
        var path = TempPath();
        var store = new ListeningJournal(path);
        Assert.True(await store.SaveAsync(Sample()));

        var loaded = await new ListeningJournal(path).LoadAsync();
        AssertSameState(Sample(), loaded);
    }

    [Fact]
    public async Task Unbound_store_loads_empty_and_refuses_writes()
    {
        var store = new ListeningJournal((AccountLocalStore?)null);
        AssertSameState(new JournalState([], [], [], 0), await store.LoadAsync());
        Assert.False(await store.SaveAsync(Sample()));
    }

    [Fact]
    public async Task Corrupt_file_loads_empty()
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{ not json");
        AssertSameState(new JournalState([], [], [], 0), await new ListeningJournal(path).LoadAsync());
    }
}
