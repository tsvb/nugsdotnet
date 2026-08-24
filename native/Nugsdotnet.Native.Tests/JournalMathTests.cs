using Nugsdotnet.Native.Core;

namespace Nugsdotnet.Native.Tests;

public class JournalMathTests
{
    private static JournalDay Day(string date, double seconds, long tracks = 0,
        JournalArtistPlay[]? artists = null, JournalShowPlay[]? shows = null) =>
        new(date, seconds, tracks, artists?.ToList() ?? [], shows?.ToList() ?? []);

    private static readonly DateOnly Today = new(2026, 8, 21);

    // ---- thresholds -------------------------------------------------------

    [Theory]
    [InlineData(299, false)]
    [InlineData(300, true)]
    [InlineData(3600, true)]
    public void IsListeningNight_requires_five_minutes(double seconds, bool expected) =>
        Assert.Equal(expected, JournalMath.IsListeningNight(Day("2026-08-20", seconds)));

    [Theory]
    [InlineData(29, false)]
    [InlineData(30, true)]
    public void ShowHeard_requires_thirty_seconds(double seconds, bool expected) =>
        Assert.Equal(expected, JournalMath.ShowHeard(new JournalShow("c", null, null, seconds, 0, default)));

    [Theory]
    [InlineData(570, 600, true)]     // 95 %
    [InlineData(569, 600, false)]
    [InlineData(300, 0, true)]       // unknown duration — natural advance
    public void TrackCompleted_needs_95pct_or_unknown_duration(double pos, double dur, bool expected) =>
        Assert.Equal(expected, JournalMath.TrackCompleted(pos, dur));

    // ---- run with grace -----------------------------------------------------

    [Fact]
    public void Run_counts_back_to_back_nights()
    {
        var days = new[] { Day("2026-08-19", 600), Day("2026-08-20", 600) };
        var run = JournalMath.Run(days, Today);
        Assert.Equal(2, run.Current);
        Assert.Equal(2, run.Best);
        Assert.Equal(0, run.GraceNights);
    }

    [Fact]
    public void One_skipped_night_is_grace_and_does_not_break_the_run()
    {
        var days = new[]
        {
            Day("2026-08-17", 600), Day("2026-08-18", 600),
            Day("2026-08-19", 0),                     // grace night
            Day("2026-08-20", 600),
        };
        var run = JournalMath.Run(days, Today);
        Assert.Equal(4, run.Current);                 // span includes the grace night
        Assert.Equal(1, run.GraceNights);
    }

    [Fact]
    public void Two_consecutive_skips_end_the_run()
    {
        var days = new[]
        {
            Day("2026-08-15", 600), Day("2026-08-16", 600),
            Day("2026-08-19", 600), Day("2026-08-20", 600),
        };
        var run = JournalMath.Run(days, Today);
        Assert.Equal(2, run.Current);
        Assert.Equal(2, run.Best);
        Assert.Equal(0, run.GraceNights);
    }

    [Fact]
    public void Today_listening_extends_the_run()
    {
        var days = new[] { Day("2026-08-21", 600), Day("2026-08-20", 600) };
        Assert.Equal(2, JournalMath.Run(days, Today).Current);
    }

    [Fact]
    public void Stale_run_reports_current_zero()
    {
        var days = new[] { Day("2026-08-10", 600) };
        var run = JournalMath.Run(days, Today);
        Assert.Equal(0, run.Current);                 // last listen was 11 nights ago
        Assert.Equal(1, run.Best);
    }

    [Fact]
    public void Best_run_survives_old_gaps()
    {
        var days = new List<JournalDay>();
        for (var i = 0; i < 9; i++)   // 9-night run ending 2026-08-09
            days.Add(Day(DateOnly.FromDateTime(new DateTime(2026, 8, 1)).AddDays(i).ToString("yyyy-MM-dd"), 600));
        days.Add(Day("2026-08-20", 600));
        var run = JournalMath.Run(days, Today);
        Assert.Equal(1, run.Current);
        Assert.Equal(9, run.Best);
    }

    // ---- timeline -------------------------------------------------------------

    [Fact]
    public void Timeline_is_fourteen_nights_ending_today()
    {
        var days = new[] { Day("2026-08-20", 600), Day("2026-08-15", 1200) };
        var bars = JournalMath.Timeline(days, Today);
        Assert.Equal(14, bars.Count);
        Assert.Equal("2026-08-08", bars[0].Date);
        Assert.Equal("2026-08-21", bars[^1].Date);
        Assert.True(bars[12].IsListening);            // 08-20
        Assert.False(bars[13].IsListening);           // today, nothing yet
        Assert.True(bars[7].IsListening);             // 08-15
    }

    [Fact]
    public void Timeline_marks_the_grace_night_inside_a_run()
    {
        var days = new[]
        {
            Day("2026-08-17", 600), Day("2026-08-18", 600),
            Day("2026-08-19", 0), Day("2026-08-20", 600),
        };
        var bars = JournalMath.Timeline(days, Today);
        Assert.True(bars.First(b => b.Date == "2026-08-19").IsGrace);
        Assert.False(bars.First(b => b.Date == "2026-08-14").IsGrace);
    }

    [Fact]
    public void NightToolTip_lists_shows_and_hours()
    {
        var day = Day("2026-08-12", 6600, shows: new[]
        {
            new JournalShowPlay("c1", "Give It Time", 3600),
            new JournalShowPlay("c2", "Hot Tea", 3000),
        });
        Assert.Equal("Aug 12 · 1.8 hrs — Give It Time, Hot Tea", JournalMath.NightToolTip(day));
    }

    [Fact]
    public void NightToolTip_caps_at_three_shows()
    {
        var day = Day("2026-08-12", 9000, shows: new[]
        {
            new JournalShowPlay("c1", "One", 3000),
            new JournalShowPlay("c2", "Two", 3000),
            new JournalShowPlay("c3", "Three", 2000),
            new JournalShowPlay("c4", "Four", 1000),
        });
        Assert.Equal("Aug 12 · 2.5 hrs — One, Two, Three +1 more", JournalMath.NightToolTip(day));
    }

    // ---- top artists ------------------------------------------------------------

    [Fact]
    public void TopArtists_sums_a_trailing_window_and_ranks()
    {
        var days = new List<JournalDay>
        {
            Day("2026-07-01", 600, artists: [new JournalArtistPlay("Old Band", 600)]),  // outside 30
            Day("2026-08-19", 600, artists: [new JournalArtistPlay("Phish", 600)]),
            Day("2026-08-20", 1800, artists: [new JournalArtistPlay("Goose", 1200), new JournalArtistPlay("Phish", 600)]),
        };
        var rows = JournalMath.TopArtists(days, Today);
        Assert.Equal(2, rows.Count);
        Assert.Equal(("Goose", 1200), (rows[0].Name, rows[0].Seconds));
        Assert.Equal(("Phish", 1200), (rows[1].Name, rows[1].Seconds));
        Assert.Equal([1, 2], rows.Select(r => r.Rank));
        Assert.Equal("98*,2*", rows[0].BarStars);     // leader clamps to a full bar
        Assert.Equal("98*,2*", rows[1].BarStars);     // tie with leader → full bar
    }

    [Fact]
    public void TopArtists_scales_bars_to_the_leader()
    {
        var days = new[] { Day("2026-08-20", 2000, artists:
            new[] { new JournalArtistPlay("Goose", 1600), new JournalArtistPlay("Phish", 400) }) };
        var rows = JournalMath.TopArtists(days, Today);
        Assert.Equal("98*,2*", rows[0].BarStars);
        Assert.Equal("25*,75*", rows[1].BarStars);
        Assert.Equal("0.4 h", rows[0].HoursLabel);    // 1600 s — brief said "1.6 h" (arithmetic slip)
        Assert.Equal("1", rows[0].RankText);
    }

    // ---- meters -----------------------------------------------------------------

    [Fact]
    public void FormatHours_trims_the_decimal()
    {
        Assert.Equal("38.5", JournalMath.FormatHours(138_600));
        Assert.Equal("1", JournalMath.FormatHours(3600));
        Assert.Equal("0", JournalMath.FormatHours(0));
    }

    [Fact]
    public void Scale_maps_value_to_the_next_milestone()
    {
        var s = JournalMath.Scale(38.5, 50);
        Assert.Equal(0.77, s.Needle, 3);
        Assert.Equal(JournalMath.RedZoneStart, s.RedZone, 3);

        var top = JournalMath.Scale(520, null);       // past the last milestone
        Assert.Equal(1.0, top.Needle, 3);
    }

    [Fact]
    public void NextThreshold_walks_the_catalog()
    {
        Assert.Equal(50, JournalMath.NextThreshold("shows", 42));
        Assert.Equal(10, JournalMath.NextThreshold("hours", 3));
        Assert.Null(JournalMath.NextThreshold("shows", 1000));
        Assert.Null(JournalMath.NextThreshold("tracks", 5));   // no tracks milestones
    }

    [Fact]
    public void BarStars_clamps_to_visible_slivers()
    {
        Assert.Equal("2*,98*", JournalMath.BarStars(0.001));
        Assert.Equal("50*,50*", JournalMath.BarStars(0.5));
        Assert.Equal("98*,2*", JournalMath.BarStars(1));
    }

    // ---- milestones -----------------------------------------------------------------

    [Fact]
    public void NewlyReached_fires_each_threshold_once()
    {
        var state = new JournalState(
            Days: [],
            Shows: Enumerable.Range(0, 42).Select(i =>
                new JournalShow($"c{i}", null, null, 600, 1, default)).ToList(),
            Milestones: [new MilestoneReached("shows-10", "2026-08-01")],
            TracksCompleted: 0);

        Assert.Equal(["shows-25"], JournalMath.NewlyReached(state, Today));
    }

    [Fact]
    public void MilestoneLine_reads_the_latest_fired()
    {
        var state = new JournalState(
            [], [],
            [new MilestoneReached("shows-25", "2026-08-10"), new MilestoneReached("hours-10", "2026-08-15")],
            0);
        Assert.Equal("10 hours on air ▸ next: 50", JournalMath.MilestoneLine(state));
        Assert.Null(JournalMath.MilestoneLine(new JournalState([], [], [], 0)));
    }

    // ---- headings + recap --------------------------------------------------------------

    [Fact]
    public void NightHeading_counts_listening_nights()
    {
        var state = new JournalState([Day("2026-08-10", 600), Day("2026-08-20", 600)], [], [], 0);
        Assert.Equal("NIGHT 2 ON THE RECEIVER", JournalMath.NightHeading(Today, state));
        Assert.Equal("THE JOURNAL BEGINS TONIGHT",
            JournalMath.NightHeading(Today, new JournalState([], [], [], 0)));
    }

    [Fact]
    public void Recap_summarises_yesterday()
    {
        var day = Day("2026-08-20", 6600, shows: new[]
        {
            new JournalShowPlay("c1", "Give It Time", 3600),
            new JournalShowPlay("c2", "Short", 20),          // below show-heard threshold
        }, artists: new[] { new JournalArtistPlay("Goose", 3600), new JournalArtistPlay("Phish", 3000) });
        Assert.Equal("last night — 1.8 hrs · 1 show · Goose led", JournalMath.Recap(day));
        Assert.Null(JournalMath.Recap(null));
        Assert.Null(JournalMath.Recap(Day("2026-08-20", 120)));   // too short to count
    }
}
