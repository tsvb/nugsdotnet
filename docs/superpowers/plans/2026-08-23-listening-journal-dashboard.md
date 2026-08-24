# Listening Journal Dashboard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the Home dashboard into a listening journal — VU-meter stats, night-run streaks, top-artist charts, a 14-night timeline, and milestones — backed by a new local `journal.json` store, and move the A–Z artist grid to a dedicated Artists page.

**Architecture:** All journal decision logic (streaks-with-grace, top-N windows, milestone evaluation, VU scale mapping, track-completion rules) lives in pure, tested code in `Nugsdotnet.Native.Core`. A thin `JournalTracker` in the app samples `PlayerService` every 15 s and flushes to disk ~60 s. The WinUI pages stay thin binding surfaces, per the existing `HomeDashboard` pattern.

**Tech Stack:** .NET 10, WinUI 3 (Windows App SDK), CommunityToolkit.Mvvm, xUnit (Core only — the test project never references the WinUI app).

**Spec:** `docs/superpowers/specs/2026-08-23-listening-journal-dashboard-design.md`

## Global Constraints

- Tests reference **only** `Nugsdotnet.Native.Core` — never the WinUI app (`native/Nugsdotnet.Native.Tests/Nugsdotnet.Native.Tests.csproj` comment says so explicitly). Core must never take a UI dependency: bind-ready records expose `string`/`double`/`bool` only.
- Store discipline: semaphore lock, `AtomicFile.WriteAsync`, corrupt file loads empty, write failures swallowed — copy `RecentsStore`/`StashStore` patterns exactly.
- Journal writes must **never** break playback.
- Records with list members are compared in tests via JSON bytes, never `Assert.Equal` on the record (list members compare by reference).
- Palette/typography only from `Themes/Brand.xaml` keys (`BrandAccent`, `BrandDim`, `BrandMonoFont`, `FaceplateLabelStyle`, …). No new colors outside the RECEIVER '74 palette (the red-zone wash `#26FF7A1A` is `BrandAccent2` at low alpha — allowed).
- Day keys are local calendar date strings `yyyy-MM-dd` (DST-safe; no 24 h deltas).
- Definitions (verbatim from spec): listening night ≥ 300 s; show heard ≥ 30 s cumulative; track counted on natural completion or ≥ 95 % position; grace = one skipped night per gap, two consecutive skips break a run; milestones shows 10/25/50/100/250/500/1000, hours 10/50/100/500, run 7/14/30; daily aggregates capped at 400 nights; tonight's shelf capped at 24 cards; TRACKS meter scales 0–1000 with no red zone (no tracks milestones exist).
- Commit style: short imperative, no prefix (repo log: "Make Home a faceplate: continue hero, A–Z, your artists").
- Build the app with: `dotnet build native/Nugsdotnet.Native/Nugsdotnet.Native.csproj -c Debug -p:Platform=x64 -p:RuntimeIdentifier=win-x64`
- Run tests with: `dotnet test native/Nugsdotnet.Native.Tests/Nugsdotnet.Native.Tests.csproj`

---

### Task 1: Journal store — `ListeningJournal` + path constant

**Files:**
- Create: `native/Nugsdotnet.Native.Core/ListeningJournal.cs`
- Modify: `native/Nugsdotnet.Native.Core/NugsLocalPaths.cs`
- Test: `native/Nugsdotnet.Native.Tests/ListeningJournalTests.cs`

**Interfaces:**
- Consumes: `AccountLocalStore.File(string)`, `AtomicFile.WriteAsync(string, byte[], CancellationToken)` (existing).
- Produces (used by Tasks 2, 3, 5):
  - `record JournalArtistPlay(string Name, double Seconds)`
  - `record JournalShowPlay(string ContainerId, string? Title, double Seconds)`
  - `record JournalDay(string Date, double Seconds, long TracksCompleted, List<JournalArtistPlay> Artists, List<JournalShowPlay> Shows)` — `Date` is `yyyy-MM-dd` local
  - `record JournalShow(string ContainerId, string? Artist, string? Title, double Seconds, long TracksCompleted, DateTimeOffset LastPlayed)`
  - `record MilestoneReached(string Id, string DateFired)` — `DateFired` is `yyyy-MM-dd`
  - `record JournalState(List<JournalDay> Days, List<JournalShow> Shows, List<MilestoneReached> Milestones, long TracksCompleted)`
  - `sealed class ListeningJournal` with:
    - `ListeningJournal(AccountLocalStore? accounts)` (app) / `ListeningJournal(string path)` (tests)
    - `Task<JournalState> LoadAsync(CancellationToken ct = default)` — unbound/corrupt → fresh empty state
    - `Task<bool> SaveAsync(JournalState state, CancellationToken ct = default)` — false on failure, never throws
  - `static JournalState ListeningJournal.Accumulate(JournalState state, string date, string containerId, string? artist, string? title, double seconds, long tracksCompleted)` — mutates lists in place, returns the same state
  - `static JournalState ListeningJournal.Prune(JournalState state)` — keeps newest 400 non-empty days, ascending by date
  - `const int DayCap = 400` and `NugsLocalPaths.JournalFileName = "journal.json"`

- [ ] **Step 1: Add the file name constant**

In `native/Nugsdotnet.Native.Core/NugsLocalPaths.cs`, add to the constants block (after `PlaybackFileName`):

```csharp
    public const string JournalFileName = "journal.json";
```

and add `"journal.json"` to the `MigrateLegacy` array:

```csharp
        foreach (var name in new[] { StashFileName, RecentsFileName, PlaybackFileName, JournalFileName })
```

- [ ] **Step 2: Write the failing tests**

Create `native/Nugsdotnet.Native.Tests/ListeningJournalTests.cs`:

```csharp
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
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test native/Nugsdotnet.Native.Tests/Nugsdotnet.Native.Tests.csproj`
Expected: FAIL — `ListeningJournal` / `JournalState` do not exist (compile errors).

- [ ] **Step 4: Implement `ListeningJournal.cs`**

Create `native/Nugsdotnet.Native.Core/ListeningJournal.cs`:

```csharp
using System.Text.Json;

namespace Nugsdotnet.Native.Core;

/// <summary>Seconds credited to one artist inside one night.</summary>
public sealed record JournalArtistPlay(string Name, double Seconds);

/// <summary>Seconds credited to one show inside one night (title kept for tooltips).</summary>
public sealed record JournalShowPlay(string ContainerId, string? Title, double Seconds);

/// <summary>One local calendar night of listening. Date is yyyy-MM-dd.</summary>
public sealed record JournalDay(
    string Date, double Seconds, long TracksCompleted,
    List<JournalArtistPlay> Artists, List<JournalShowPlay> Shows);

/// <summary>Lifetime totals for one show — the "shows heard" ledger.</summary>
public sealed record JournalShow(
    string ContainerId, string? Artist, string? Title,
    double Seconds, long TracksCompleted, DateTimeOffset LastPlayed);

/// <summary>A milestone that already fired, so the amber pulse happens once.</summary>
public sealed record MilestoneReached(string Id, string DateFired);

/// <summary>The whole journal as it persists to journal.json.</summary>
public sealed record JournalState(
    List<JournalDay> Days,
    List<JournalShow> Shows,
    List<MilestoneReached> Milestones,
    long TracksCompleted);

/// <summary>
/// File-backed listening journal feeding the Home dashboard's VU meters, run
/// streaks, charts, and timeline. Plain JSON (titles and counts, nothing
/// sensitive) at %LOCALAPPDATA%\nugsdotnet\accounts\{userId}\journal.json —
/// same locking discipline as <see cref="RecentsStore"/>. Losing an entry must
/// never break playback, so write failures are swallowed.
/// </summary>
public sealed class ListeningJournal
{
    /// <summary>Nights of daily aggregates kept on disk; oldest pruned.</summary>
    public const int DayCap = 400;

    private readonly AccountLocalStore? _accounts;
    private readonly string? _fixedPath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>Account-scoped store used by the app. Empty until Bind.</summary>
    public ListeningJournal(AccountLocalStore? accounts) => _accounts = accounts;

    /// <summary>Fixed path — tests and one-off tools.</summary>
    public ListeningJournal(string path)
    {
        _fixedPath = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    }

    private string? ResolvePath() => _fixedPath ?? _accounts?.File(NugsLocalPaths.JournalFileName);

    public async Task<JournalState> LoadAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var path = ResolvePath();
            if (path is null || !File.Exists(path)) return Empty();
            await using var fs = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<JournalState>(fs, cancellationToken: ct) ?? Empty();
        }
        catch
        {
            return Empty();   // corrupt/unreadable — the journal starts over
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Atomic write. Returns false on any failure — never throws.</summary>
    public async Task<bool> SaveAsync(JournalState state, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var path = ResolvePath();
            if (path is null) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await AtomicFile.WriteAsync(path, JsonSerializer.SerializeToUtf8Bytes(state), ct);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            _lock.Release();
        }
    }

    public static JournalState Empty() => new([], [], [], 0);

    /// <summary>
    /// Credits <paramref name="seconds"/> and <paramref name="tracksCompleted"/>
    /// to the night, show, and artist. Mutates the lists in place and returns
    /// the same state — the tracker owns its working copy. Days stay sorted
    /// ascending by date.
    /// </summary>
    public static JournalState Accumulate(
        JournalState state, string date,
        string containerId, string? artist, string? title,
        double seconds, long tracksCompleted)
    {
        var di = state.Days.FindIndex(d => d.Date == date);
        if (di < 0)
        {
            state.Days.Add(new JournalDay(date, 0, 0, [], []));
            state.Days.Sort(static (a, b) => string.CompareOrdinal(a.Date, b.Date));
            di = state.Days.FindIndex(d => d.Date == date);
        }
        var day = state.Days[di];
        state.Days[di] = day with
        {
            Seconds = day.Seconds + seconds,
            TracksCompleted = day.TracksCompleted + tracksCompleted,
        };
        day = state.Days[di];

        if (!string.IsNullOrWhiteSpace(artist))
        {
            var ai = day.Artists.FindIndex(x => x.Name == artist);
            if (ai < 0) day.Artists.Add(new JournalArtistPlay(artist, seconds));
            else day.Artists[ai] = day.Artists[ai] with { Seconds = day.Artists[ai].Seconds + seconds };
        }

        if (!string.IsNullOrEmpty(containerId))
        {
            var si = day.Shows.FindIndex(s => s.ContainerId == containerId);
            if (si < 0) day.Shows.Add(new JournalShowPlay(containerId, title, seconds));
            else day.Shows[si] = day.Shows[si] with { Seconds = day.Shows[si].Seconds + seconds };

            var ti = state.Shows.FindIndex(s => s.ContainerId == containerId);
            if (ti < 0)
            {
                state.Shows.Add(new JournalShow(
                    containerId, artist, title, seconds, tracksCompleted, DateTimeOffset.Now));
            }
            else
            {
                state.Shows[ti] = state.Shows[ti] with
                {
                    Seconds = state.Shows[ti].Seconds + seconds,
                    TracksCompleted = state.Shows[ti].TracksCompleted + tracksCompleted,
                    LastPlayed = DateTimeOffset.Now,
                };
            }
        }

        state.TracksCompleted += tracksCompleted;
        return state;
    }

    /// <summary>Keeps the newest <see cref="DayCap"/> non-empty nights, ascending.</summary>
    public static JournalState Prune(JournalState state)
    {
        state.Days.RemoveAll(d => d.Seconds <= 0 && d.TracksCompleted <= 0);
        if (state.Days.Count > DayCap)
            state.Days.RemoveRange(0, state.Days.Count - DayCap);
        return state;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test native/Nugsdotnet.Native.Tests/Nugsdotnet.Native.Tests.csproj`
Expected: PASS — all `ListeningJournalTests` green (and existing suites still green).

- [ ] **Step 6: Commit**

```bash
git add native/Nugsdotnet.Native.Core/ListeningJournal.cs native/Nugsdotnet.Native.Core/NugsLocalPaths.cs native/Nugsdotnet.Native.Tests/ListeningJournalTests.cs
git commit -m "Add ListeningJournal store: nights, show totals, milestones"
```

---

### Task 2: Pure journal math — `JournalMath` + tests

**Files:**
- Create: `native/Nugsdotnet.Native.Core/JournalMath.cs`
- Test: `native/Nugsdotnet.Native.Tests/JournalMathTests.cs`

**Interfaces:**
- Consumes: `JournalState`, `JournalDay`, `JournalShow` (Task 1).
- Produces (used by Tasks 3, 5, 7):
  - `record NightBar(string Date, double Seconds, bool IsListening, bool IsGrace, string BarStars, string ToolTip)` — plus computed `bool HasToolTip => ToolTip.Length > 0`
  - `record TopArtistRow(int Rank, string Name, double Seconds, string BarStars)` — plus computed `string RankText => $"{Rank}"` and `string HoursLabel => $"{JournalMath.FormatHours(Seconds)} h"`
  - `record MeterScale(double Needle, double RedZone)` — both 0..1
  - `record RunInfo(int Current, int Best, int GraceNights)` — Current is 0 when the latest listening night is neither today nor yesterday
  - `record MilestoneDef(string Id, string Kind, double Threshold, string Label)`
  - `static class JournalMath` with constants `ListeningNightSeconds = 300`, `ShowHeardSeconds = 30`, `CompletionFraction = 0.95`, `RedZoneStart = 0.85`, `TopArtistWindow = 30`, `TopArtistCount = 5`, `TimelineNights = 14`, `TracksScaleEnd = 1000`, and `Catalog` (14 milestones), plus:
    - `static bool IsListeningNight(JournalDay day)`
    - `static bool ShowHeard(JournalShow show)`
    - `static bool TrackCompleted(double lastPositionSeconds, double lastDurationSeconds)`
    - `static RunInfo Run(IReadOnlyList<JournalDay> days, DateOnly today)`
    - `static IReadOnlyList<NightBar> Timeline(IReadOnlyList<JournalDay> days, DateOnly today, int nights = TimelineNights)`
    - `static IReadOnlyList<TopArtistRow> TopArtists(IReadOnlyList<JournalDay> days, DateOnly today, int nights = TopArtistWindow, int top = TopArtistCount)`
    - `static int ShowsHeard(JournalState state)`
    - `static double Hours(JournalState state)`
    - `static IReadOnlyList<string> NewlyReached(JournalState state, DateOnly today)` — ids not yet in `state.Milestones`
    - `static string? MilestoneLine(JournalState state)` — latest-fired, `"10 hours on air ▸ next: 50"`, null when none
    - `static MeterScale Scale(double value, double? nextThreshold)` — needle 0..1 toward the next milestone; 1 when none
    - `static double? NextThreshold(string kind, double value)` — next catalog threshold above value, per kind
    - `static string FormatHours(double seconds)` — `"38.5"` style, `"0.#"`
    - `static string BarStars(double fraction)` — `"92*,8*"` for Grid Row/ColumnDefinitions, clamped 2..98
    - `static string NightHeading(DateOnly today, JournalState state)` — `"NIGHT 14 ON THE RECEIVER"` (distinct listening nights), or `"THE JOURNAL BEGINS TONIGHT"` when none
    - `static string? Recap(JournalDay? yesterday)` — `"last night — 1.8 hrs · 1 show · Goose led"`, null when nothing/short
    - `static string NightToolTip(JournalDay day)` — `"Aug 12 · 1.8 hrs — Give It Time, Hot Tea"` (top 3 shows, `"+N more"` tail)

- [ ] **Step 1: Write the failing tests**

Create `native/Nugsdotnet.Native.Tests/JournalMathTests.cs`:

```csharp
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
        Assert.Equal("1.6 h", rows[0].HoursLabel);
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test native/Nugsdotnet.Native.Tests/Nugsdotnet.Native.Tests.csproj`
Expected: FAIL — `JournalMath` does not exist.

- [ ] **Step 3: Implement `JournalMath.cs`**

Create `native/Nugsdotnet.Native.Core/JournalMath.cs`:

```csharp
using System.Globalization;

namespace Nugsdotnet.Native.Core;

/// <summary>One night on the 14-night timeline, ready to bind.</summary>
public sealed record NightBar(
    string Date, double Seconds, bool IsListening, bool IsGrace,
    string BarStars, string ToolTip)
{
    public bool HasToolTip => ToolTip.Length > 0;
}

/// <summary>One ranked row of the top-artists chart, ready to bind.</summary>
public sealed record TopArtistRow(int Rank, string Name, double Seconds, string BarStars)
{
    public string RankText => Rank.ToString(CultureInfo.InvariantCulture);
    public string HoursLabel => $"{FormatHours(Seconds)} h";
}

/// <summary>Needle + red-zone fractions (0..1) for one VU meter.</summary>
public sealed record MeterScale(double Needle, double RedZone);

/// <summary>Current/best night run. Current and Best count the inclusive night
/// span; a single skipped night inside a run is grace and keeps it alive.
/// Current is 0 unless the latest listening night is today or yesterday.</summary>
public sealed record RunInfo(int Current, int Best, int GraceNights);

/// <summary>One milestone threshold ("shows-50").</summary>
public sealed record MilestoneDef(string Id, string Kind, double Threshold, string Label);

/// <summary>
/// Pure journal computations — streaks with grace, timelines, top-artist
/// windows, milestone evaluation, VU scale mapping. No I/O, no clock reads:
/// callers pass <paramref name="today"/> so everything stays testable.
/// </summary>
public static class JournalMath
{
    public const int ListeningNightSeconds = 300;
    public const int ShowHeardSeconds = 30;
    public const double CompletionFraction = 0.95;
    public const double RedZoneStart = 0.85;
    public const int TopArtistWindow = 30;
    public const int TopArtistCount = 5;
    public const int TimelineNights = 14;

    /// <summary>Fixed scale ceiling for the TRACKS meter (no tracks milestones).</summary>
    public const double TracksScaleEnd = 1000;

    public static readonly MilestoneDef[] Catalog =
    [
        new("shows-10", "shows", 10, "shows logged"),
        new("shows-25", "shows", 25, "shows logged"),
        new("shows-50", "shows", 50, "shows logged"),
        new("shows-100", "shows", 100, "shows logged"),
        new("shows-250", "shows", 250, "shows logged"),
        new("shows-500", "shows", 500, "shows logged"),
        new("shows-1000", "shows", 1000, "shows logged"),
        new("hours-10", "hours", 10, "hours on air"),
        new("hours-50", "hours", 50, "hours on air"),
        new("hours-100", "hours", 100, "hours on air"),
        new("hours-500", "hours", 500, "hours on air"),
        new("run-7", "run", 7, "night run"),
        new("run-14", "run", 14, "night run"),
        new("run-30", "run", 30, "night run"),
    ];

    public static bool IsListeningNight(JournalDay day) => day.Seconds >= ListeningNightSeconds;

    public static bool ShowHeard(JournalShow show) => show.Seconds >= ShowHeardSeconds;

    /// <summary>Natural completion or ≥ 95 %; unknown duration means auto-advance.</summary>
    public static bool TrackCompleted(double lastPositionSeconds, double lastDurationSeconds) =>
        lastDurationSeconds <= 0 || lastPositionSeconds >= lastDurationSeconds * CompletionFraction;

    public static int ShowsHeard(JournalState state) => state.Shows.Count(ShowHeard);

    public static double Hours(JournalState state) => state.Days.Sum(d => d.Seconds) / 3600.0;

    /// <summary>
    /// Walks the listening nights ascending. Consecutive nights extend a run; a
    /// gap of exactly one night is bridged as grace (counted in the span); a
    /// gap of two or more nights ends it. Today only extends the current run
    /// when the latest listening night is today or yesterday.
    /// </summary>
    public static RunInfo Run(IReadOnlyList<JournalDay> days, DateOnly today)
    {
        var listening = days.Where(IsListeningNight)
            .Select(d => DateOnly.ParseExact(d.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture))
            .OrderBy(d => d)
            .ToList();
        if (listening.Count == 0) return new RunInfo(0, 0, 0);

        var runs = new List<RunInfo>();
        var start = listening[0];
        var cursor = listening[0];
        var grace = 0;
        foreach (var day in listening.Skip(1))
        {
            var gap = (day - cursor).Days;
            if (gap == 1) { cursor = day; continue; }
            if (gap == 2) { grace++; cursor = day; continue; }
            runs.Add(new RunInfo((cursor - start).Days + 1, (cursor - start).Days + 1, grace));
            start = cursor = day;
            grace = 0;
        }
        runs.Add(new RunInfo((cursor - start).Days + 1, (cursor - start).Days + 1, grace));

        var best = runs.Max(r => r.Best);
        var last = runs[^1];
        var fresh = (today - listening[^1]).Days <= 1;
        return new RunInfo(fresh ? last.Current : 0, best, fresh ? last.GraceNights : 0);
    }

    public static IReadOnlyList<NightBar> Timeline(
        IReadOnlyList<JournalDay> days, DateOnly today, int nights = TimelineNights)
    {
        var byDate = days.ToDictionary(d => d.Date, d => d);
        var grace = GraceDates(days, today);
        var max = Math.Max(3600, days.Count == 0 ? 3600 : days.Max(d => d.Seconds));
        var list = new List<NightBar>(nights);
        for (var i = nights - 1; i >= 0; i--)
        {
            var date = today.AddDays(-i);
            var key = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var day = byDate.GetValueOrDefault(key);
            var listening = day is not null && IsListeningNight(day);
            list.Add(new NightBar(
                key,
                day?.Seconds ?? 0,
                listening,
                !listening && grace.Contains(date),
                BarStars(Math.Clamp((day?.Seconds ?? 0) / max, 0, 1)),
                day is null ? "" : NightToolTip(day)));
        }
        return list;
    }

    /// <summary>Single-night gaps between listening nights — the hollow cells.</summary>
    private static HashSet<DateOnly> GraceDates(IReadOnlyList<JournalDay> days, DateOnly today)
    {
        var listening = days.Where(IsListeningNight)
            .Select(d => DateOnly.ParseExact(d.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture))
            .ToHashSet();
        var grace = new HashSet<DateOnly>();
        foreach (var night in listening)
        {
            var g = night.AddDays(1);
            if (g <= today && !listening.Contains(g) && listening.Contains(g.AddDays(1)))
                grace.Add(g);
        }
        return grace;
    }

    public static string NightToolTip(JournalDay day)
    {
        var date = DateOnly.ParseExact(day.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var shows = day.Shows.OrderByDescending(s => s.Seconds).ToList();
        var names = shows.Take(3).Select(s => s.Title).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        var tail = shows.Count > 3 ? $" +{shows.Count - 3} more" : "";
        return $"{date:MMM d} · {FormatHours(day.Seconds)} hrs — {string.Join(", ", names)}{tail}";
    }

    public static IReadOnlyList<TopArtistRow> TopArtists(
        IReadOnlyList<JournalDay> days, DateOnly today, int nights = TopArtistWindow, int top = TopArtistCount)
    {
        var from = today.AddDays(-(nights - 1)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var totals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var day in days)
        {
            if (string.CompareOrdinal(day.Date, from) < 0) continue;
            foreach (var a in day.Artists)
                totals[a.Name] = totals.GetValueOrDefault(a.Name) + a.Seconds;
        }
        var leaders = totals.OrderByDescending(kv => kv.Value).Take(top).ToList();
        var max = leaders.Count > 0 ? leaders[0].Value : 0;
        return leaders
            .Select((kv, i) => new TopArtistRow(i + 1, kv.Key, kv.Value, BarStars(max > 0 ? kv.Value / max : 0)))
            .ToList();
    }

    public static double? NextThreshold(string kind, double value) =>
        Catalog.Where(m => m.Kind == kind && m.Threshold > value)
            .OrderBy(m => m.Threshold)
            .Select(m => (double?)m.Threshold)
            .FirstOrDefault();

    public static MeterScale Scale(double value, double? nextThreshold) => new(
        Needle: nextThreshold is { } n && n > 0 ? Math.Clamp(value / n, 0, 1) : 1,
        RedZone: RedZoneStart);

    public static string FormatHours(double seconds) =>
        (seconds / 3600.0).ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>Star weights for a fraction-filled bar ("92*,8*"), clamped 2..98
    /// so a sliver stays visible and a leader reads full.</summary>
    public static string BarStars(double fraction)
    {
        var pct = (int)Math.Round(Math.Clamp(fraction, 0, 1) * 100);
        pct = Math.Clamp(pct, 2, 98);
        return $"{pct}*,{100 - pct}*";
    }

    /// <summary>Threshold ids crossed but not yet fired — caller persists them.</summary>
    public static IReadOnlyList<string> NewlyReached(JournalState state, DateOnly today)
    {
        var shows = (double)ShowsHeard(state);
        var hours = Hours(state);
        var run = (double)Run(state.Days, today).Best;
        var fired = state.Milestones.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        return Catalog
            .Where(m => !fired.Contains(m.Id) && ValueOf(m.Kind, shows, hours, run) >= m.Threshold)
            .Select(m => m.Id)
            .ToList();
    }

    private static double ValueOf(string kind, double shows, double hours, double run) => kind switch
    {
        "shows" => shows,
        "hours" => hours,
        _ => run,
    };

    /// <summary>Faceplate line for the latest-fired milestone, null when none.</summary>
    public static string? MilestoneLine(JournalState state)
    {
        var latest = state.Milestones
            .OrderByDescending(m => m.DateFired, StringComparer.Ordinal)
            .Join(Catalog, m => m.Id, d => d.Id, (m, d) => d)
            .FirstOrDefault();
        if (latest is null) return null;
        var next = NextThreshold(latest.Kind, latest.Threshold);
        return next is { } n
            ? $"{latest.Threshold:0} {latest.Label} ▸ next: {n:0}"
            : $"{latest.Threshold:0} {latest.Label}";
    }

    public static string NightHeading(DateOnly today, JournalState state)
    {
        var nights = state.Days.Where(IsListeningNight)
            .Select(d => d.Date)
            .Distinct()
            .Count();
        return nights == 0
            ? "THE JOURNAL BEGINS TONIGHT"
            : $"NIGHT {nights} ON THE RECEIVER";
    }

    /// <summary>"last night — 1.8 hrs · 1 show · Goose led"; null when nothing or too short.</summary>
    public static string? Recap(JournalDay? yesterday)
    {
        if (yesterday is null || yesterday.Seconds < ListeningNightSeconds) return null;
        var shows = yesterday.Shows.Count(s => s.Seconds >= ShowHeardSeconds);
        var top = yesterday.Artists.OrderByDescending(a => a.Seconds).FirstOrDefault();
        var topPart = top is null ? "" : $" · {top.Name} led";
        return $"last night — {FormatHours(yesterday.Seconds)} hrs · {shows} show{(shows == 1 ? "" : "s")}{topPart}";
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test native/Nugsdotnet.Native.Tests/Nugsdotnet.Native.Tests.csproj`
Expected: PASS — all `JournalMathTests` green.

- [ ] **Step 5: Commit**

```bash
git add native/Nugsdotnet.Native.Core/JournalMath.cs native/Nugsdotnet.Native.Tests/JournalMathTests.cs
git commit -m "Add JournalMath: runs with grace, timeline, charts, milestones, VU scales"
```

---

### Task 3: `JournalTracker` — sampling, completion detection, DI, shell wiring

**Files:**
- Create: `native/Nugsdotnet.Native/Playback/JournalTracker.cs`
- Modify: `native/Nugsdotnet.Native/App.xaml.cs` (store + tracker registrations)
- Modify: `native/Nugsdotnet.Native/ViewModels/ShellViewModel.cs`
- Modify: `native/Nugsdotnet.Native/MainWindow.xaml.cs` (flush on close)

**Interfaces:**
- Consumes: `PlayerService` (`Current`, `IsPlaying`, `Position`, `Duration` — existing), `ListeningJournal.Accumulate/Prune/SaveAsync` + `JournalMath.TrackCompleted` (Tasks 1–2).
- Produces: `sealed class JournalTracker` with `Task StartAsync()`, `Task StopAsync()`, `Task FlushNowAsync()` — consumed by `ShellViewModel` and `MainWindow`.

No unit tests: this file touches `MediaPlayer` state; the repo's test harness is Core-only. All decisions are delegated to tested Core functions. Verification is build + the Task 8 manual pass.

- [ ] **Step 1: Create `JournalTracker.cs`**

```csharp
using Nugsdotnet.Native.Core;

namespace Nugsdotnet.Native.Playback;

/// <summary>
/// Feeds the listening journal from live playback. A 15 s tick credits elapsed
/// seconds to the current night/show/artist while the player reports Playing
/// (paused time never counts), and watches track transitions to count
/// completions (natural advance, ≥ 95 % position, or a repeat-one position
/// wrap). State is held in memory and flushed to journal.json every ~60 s, on
/// stop, and on app close — a crash loses at most ~60 s. Journal failures are
/// swallowed: nothing here may break playback.
/// </summary>
public sealed class JournalTracker
{
    private const int TickSeconds = 15;
    private const int FlushEveryTicks = 4;   // ~60 s

    private readonly PlayerService _player;
    private readonly ListeningJournal _journal;
    private readonly Timer _timer;
    private readonly object _gate = new();

    private JournalState _state = ListeningJournal.Empty();
    private string? _lastTrackId;
    private double _lastPosition;
    private double _lastDuration;
    private DateTime _lastTickAt = DateTime.Now;
    private bool _running;
    private int _ticks;

    public JournalTracker(PlayerService player, ListeningJournal journal)
    {
        _player = player;
        _journal = journal;
        _timer = new Timer(_ => Tick(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public async Task StartAsync()
    {
        lock (_gate)
        {
            if (_running) return;
            _running = true;
            _ticks = 0;
            _lastTrackId = null;
            _lastPosition = _lastDuration = 0;
            _lastTickAt = DateTime.Now;
        }
        _state = await _journal.LoadAsync();
        _timer.Change(TimeSpan.FromSeconds(TickSeconds), TimeSpan.FromSeconds(TickSeconds));
    }

    public async Task StopAsync()
    {
        lock (_gate)
        {
            if (!_running) return;
            _running = false;
        }
        _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        await FlushNowAsync();
        lock (_gate)
        {
            _state = ListeningJournal.Empty();
            _lastTrackId = null;
        }
    }

    /// <summary>Best-effort flush (app close, periodic). Never throws.</summary>
    public async Task FlushNowAsync()
    {
        JournalState snapshot;
        lock (_gate) snapshot = _state;
        await _journal.SaveAsync(ListeningJournal.Prune(snapshot));
    }

    private void Tick()
    {
        try
        {
            TickUnguarded();
        }
        catch
        {
            // a journal tick must never take the app down
        }
    }

    private void TickUnguarded()
    {
        var now = DateTime.Now;
        var current = _player.Current;
        var playing = _player.IsPlaying;
        var position = _player.Position.TotalSeconds;
        var duration = _player.Duration.TotalSeconds;

        lock (_gate)
        {
            if (!_running) return;

            var trackId = current?.TrackId;
            var completed = 0L;

            // Track transition since the last tick: did the previous track finish?
            if (_lastTrackId is not null && trackId != _lastTrackId
                && JournalMath.TrackCompleted(_lastPosition, _lastDuration))
            {
                completed++;
            }
            // Same track but position wrapped backwards: repeat-one (or a manual
            // replay) rolled over — the previous playthrough finished.
            else if (_lastTrackId is not null && trackId == _lastTrackId
                     && position + 5 < _lastPosition
                     && JournalMath.TrackCompleted(_lastPosition, _lastDuration))
            {
                completed++;
            }

            if (playing && current is not null)
            {
                var delta = Math.Clamp((now - _lastTickAt).TotalSeconds, 0, TickSeconds * 2);
                if (delta > 0 || completed > 0)
                {
                    _state = ListeningJournal.Accumulate(
                        _state,
                        now.ToString("yyyy-MM-dd"),
                        current.ContainerId ?? "",
                        current.Artist,
                        current.Show,
                        delta,
                        completed);
                }
            }

            _lastTrackId = trackId;
            _lastPosition = position;
            _lastDuration = duration;
            _lastTickAt = now;

            if (++_ticks % FlushEveryTicks == 0)
                _ = FlushNowAsync();
        }
    }
}
```

- [ ] **Step 2: Register in DI**

In `native/Nugsdotnet.Native/App.xaml.cs`, after the `PlaybackStateStore` registration line:

```csharp
        sc.AddSingleton(sp => new ListeningJournal(sp.GetRequiredService<AccountLocalStore>()));
```

and after `sc.AddSingleton<PlayerService>();`:

```csharp
        sc.AddSingleton<JournalTracker>();
```

- [ ] **Step 3: Wire start/stop into `ShellViewModel`**

In `native/Nugsdotnet.Native/ViewModels/ShellViewModel.cs` — add the field, constructor parameter, and lifecycle calls:

```csharp
public partial class ShellViewModel : ObservableObject
{
    private readonly NugsAuth _auth;
    private readonly AccountLocalStore _accounts;
    private readonly JournalTracker _journal;

    // …

    public ShellViewModel(NugsAuth auth, AccountLocalStore accounts, JournalTracker journal)
    {
        _auth = auth;
        _accounts = accounts;
        _journal = journal;
    }
```

Replace the tail of `InitializeAsync` (the `if (info is …) Bind else Unbind` block) with:

```csharp
        if (info is { LoggedIn: true, UserId: { Length: > 0 } id })
        {
            _accounts.Bind(id);
            await _journal.StartAsync();
        }
        else
        {
            _accounts.Unbind();
        }
```

and start `SignOutAsync` with the flush (while the account path still resolves):

```csharp
    public async Task SignOutAsync()
    {
        await _journal.StopAsync();
        await _auth.LogoutAsync();
        // …rest unchanged
```

- [ ] **Step 4: Flush on window close**

In `native/Nugsdotnet.Native/MainWindow.xaml.cs`, inside `SaveWindowState()` (which already calls `_player.SaveNow()`), add:

```csharp
        _ = App.Services.GetRequiredService<Playback.JournalTracker>().FlushNowAsync();
```

- [ ] **Step 5: Build**

Run: `dotnet build native/Nugsdotnet.Native/Nugsdotnet.Native.csproj -c Debug -p:Platform=x64 -p:RuntimeIdentifier=win-x64`
Expected: Build succeeds, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add native/Nugsdotnet.Native/Playback/JournalTracker.cs native/Nugsdotnet.Native/App.xaml.cs native/Nugsdotnet.Native/ViewModels/ShellViewModel.cs native/Nugsdotnet.Native/MainWindow.xaml.cs
git commit -m "Track listening into the journal: 15s sampler, flush on stop and close"
```

---

### Task 4: `ArtistsViewModel` + `ArtistsPage` — the A–Z grid moves out

**Files:**
- Create: `native/Nugsdotnet.Native/ViewModels/ArtistsViewModel.cs`
- Create: `native/Nugsdotnet.Native/Views/Pages/ArtistsPage.xaml`
- Create: `native/Nugsdotnet.Native/Views/Pages/ArtistsPage.xaml.cs`
- Modify: `native/Nugsdotnet.Native/App.xaml.cs` (register `ArtistsViewModel`)

**Interfaces:**
- Consumes: `NugsCatalog`, `ArtistEntry`, `HomeDashboard.LettersFor/FilterArtists/ArtistsLabel` (existing Core), `LetterChip`, `UserError` (existing VMs).
- Produces (Task 5 consumes `ArtistsViewModel` as a singleton): `ArtistsViewModel` with `ObservableCollection<ArtistEntry> Artists`, `ObservableCollection<LetterChip> Letters`, `string Filter`, `string? ActiveLetter`, `bool Busy`, `string? Status`, `string ArtistsLabel`, `bool IsFiltered`, `IReadOnlyList<ArtistEntry> CatalogSnapshot()`, `Task LoadArtistsAsync(bool force = false)`, `Task ReloadArtistsAsync()`, `void ToggleLetter(string?)`, `void ClearFilters()`. `ArtistsPage` — navigable via `Frame.Navigate(typeof(ArtistsPage))`.

- [ ] **Step 1: Create `ArtistsViewModel.cs`**

The artist-grid state moves out of `HomeViewModel` verbatim (`_all`, `Artists`, `Letters`, `Filter`, `ActiveLetter`, `Busy`, `Status`, `ArtistsLabel`, `IsFiltered`, `LoadArtistsAsync`, `ReloadArtistsAsync`, `ToggleLetter`, `ClearFilters`, `ApplyFilter`, `RebuildLetters`, the `OnFilterChanged` hook), wrapped in a new class plus one new accessor:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Nugsdotnet.Native.Core;

namespace Nugsdotnet.Native.ViewModels;

/// <summary>
/// The A–Z artist index on its own page: live filter, letter buckets, and the
/// virtualized grid. State moved here from HomeViewModel when Home became the
/// listening journal. Singleton — the catalog fetch is shared with Home's
/// "your artists" derivation.
/// </summary>
public partial class ArtistsViewModel : ObservableObject
{
    private readonly NugsCatalog _catalog;
    private List<ArtistEntry> _all = new();

    public ObservableCollection<ArtistEntry> Artists { get; } = new();
    public ObservableCollection<LetterChip> Letters { get; } = new();

    [ObservableProperty] public partial string Filter { get; set; } = "";
    [ObservableProperty] public partial string? ActiveLetter { get; set; }
    [ObservableProperty] public partial bool Busy { get; set; }
    [ObservableProperty] public partial string? Status { get; set; }
    [ObservableProperty] public partial string ArtistsLabel { get; set; } = "ARTISTS";
    [ObservableProperty] public partial bool IsFiltered { get; set; }

    public ArtistsViewModel(NugsCatalog catalog) => _catalog = catalog;

    /// <summary>Artists fetched so far this session (empty until first load).</summary>
    public IReadOnlyList<ArtistEntry> CatalogSnapshot() => _all;

    public async Task LoadArtistsAsync(bool force = false)
    {
        if (!force && _all.Count > 0)
        {
            RebuildLetters();
            ApplyFilter();
            return;
        }
        Busy = true;
        Status = null;
        try
        {
            _all = NugsCatalog.ParseArtists(await _catalog.GetAllArtistsAsync());
            RebuildLetters();
            ApplyFilter();
            if (_all.Count == 0) Status = "No artists returned.";
        }
        catch (Exception ex)
        {
            Status = UserError.From(ex);
        }
        finally
        {
            Busy = false;
        }
    }

    public Task ReloadArtistsAsync() => LoadArtistsAsync(force: true);

    /// <summary>Toggles the A–Z bucket. Pressing the active letter clears it.</summary>
    public void ToggleLetter(string? letter)
    {
        ActiveLetter = string.Equals(ActiveLetter, letter, StringComparison.Ordinal)
            ? null : letter;
        RebuildLetters();
        ApplyFilter();
    }

    public void ClearFilters()
    {
        Filter = "";
        ActiveLetter = null;
        RebuildLetters();
        ApplyFilter();
    }

    partial void OnFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var filtered = !string.IsNullOrWhiteSpace(Filter) || !string.IsNullOrWhiteSpace(ActiveLetter);
        IsFiltered = filtered;
        Artists.Clear();
        foreach (var a in HomeDashboard.FilterArtists(_all, Filter, ActiveLetter))
            Artists.Add(a);
        ArtistsLabel = HomeDashboard.ArtistsLabel(Artists.Count, _all.Count, filtered);
    }

    private void RebuildLetters()
    {
        Letters.Clear();
        foreach (var letter in HomeDashboard.LettersFor(_all))
            Letters.Add(new LetterChip { Letter = letter, IsActive = letter == ActiveLetter });
    }
}
```

- [ ] **Step 2: Create `ArtistsPage.xaml`**

The grid, letter strip, filter, and busy chrome move from `HomePage.xaml` unchanged in behavior:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Page
    x:Class="Nugsdotnet.Native.Views.Pages.ArtistsPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:core="using:Nugsdotnet.Native.Core"
    xmlns:vm="using:Nugsdotnet.Native.ViewModels"
    Background="{StaticResource BrandBg}">

    <Page.Resources>
        <ItemsPanelTemplate x:Key="RailPanel">
            <StackPanel Orientation="Horizontal" Spacing="10" />
        </ItemsPanelTemplate>
    </Page.Resources>

    <!-- The grid is the scrolling element; header chrome rides along so
         hundreds of artist chips stay virtualized. -->
    <GridView Padding="24,20,24,12"
              ItemsSource="{Binding Artists}" SelectionMode="None"
              IsItemClickEnabled="True" ItemClick="OnArtistClick">

        <GridView.Header>
            <StackPanel Spacing="16" Margin="0,0,0,16">
                <StackPanel Orientation="Horizontal" Spacing="10">
                    <ProgressRing x:Name="BusyRing" IsActive="True" Visibility="Collapsed"
                                  Width="22" Height="22" Foreground="{StaticResource BrandAccent}" />
                    <TextBlock Text="{Binding Status}" VerticalAlignment="Center"
                               Foreground="{StaticResource BrandDim}" />
                    <Button x:Name="RetryButton" Content="Retry" Click="OnRetry"
                            Style="{StaticResource IconButtonStyle}" Visibility="Collapsed" />
                </StackPanel>

                <StackPanel Spacing="10">
                    <Grid ColumnDefinitions="Auto,*,Auto">
                        <StackPanel Grid.Column="0" Orientation="Horizontal" Spacing="10">
                            <Border Width="6" Height="6" CornerRadius="1" VerticalAlignment="Center"
                                    Background="{StaticResource BrandAccent}" />
                            <TextBlock Text="{Binding ArtistsLabel}" Style="{StaticResource FaceplateLabelStyle}" />
                        </StackPanel>
                        <Button Grid.Column="2" x:Name="ClearFilterButton" Content="clear"
                                Click="OnClearFilters" Style="{StaticResource IconButtonStyle}"
                                HorizontalAlignment="Right" Visibility="Collapsed" />
                    </Grid>
                    <ScrollViewer x:Name="LettersStrip"
                                  HorizontalScrollBarVisibility="Auto" VerticalScrollBarVisibility="Disabled"
                                  HorizontalScrollMode="Enabled" VerticalScrollMode="Disabled">
                        <ItemsControl ItemsSource="{Binding Letters}"
                                      ItemsPanel="{StaticResource RailPanel}">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate x:DataType="vm:LetterChip">
                                    <Button Content="{x:Bind Letter}" Tag="{x:Bind Letter}"
                                            Click="OnLetterClick" Style="{StaticResource IconButtonStyle}"
                                            Foreground="{x:Bind Foreground}" MinWidth="32" Padding="6,4"
                                            FontFamily="{StaticResource BrandMonoFont}" FontSize="12"
                                            ToolTipService.ToolTip="Jump to letter" />
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </ScrollViewer>
                    <TextBox PlaceholderText="Filter artists…" MaxWidth="360" HorizontalAlignment="Left"
                             Text="{Binding Filter, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" />
                </StackPanel>
            </StackPanel>
        </GridView.Header>

        <GridView.ItemsPanel>
            <ItemsPanelTemplate>
                <ItemsWrapGrid Orientation="Horizontal" ItemWidth="232" ItemHeight="50" />
            </ItemsPanelTemplate>
        </GridView.ItemsPanel>
        <GridView.ItemTemplate>
            <DataTemplate x:DataType="core:ArtistEntry">
                <Border Width="220" Height="40" CornerRadius="4" Padding="12,0"
                        Background="{StaticResource BrandSurface}"
                        BorderBrush="{StaticResource BrandBorder}" BorderThickness="1">
                    <TextBlock Text="{x:Bind Name}" VerticalAlignment="Center"
                               FontFamily="{StaticResource BrandBodyFont}" FontSize="14"
                               Foreground="{StaticResource BrandText}"
                               TextTrimming="CharacterEllipsis" />
                </Border>
            </DataTemplate>
        </GridView.ItemTemplate>
    </GridView>
</Page>
```

- [ ] **Step 3: Create `ArtistsPage.xaml.cs`**

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Nugsdotnet.Native.Core;
using Nugsdotnet.Native.ViewModels;

namespace Nugsdotnet.Native.Views.Pages;

public sealed partial class ArtistsPage : Page
{
    private readonly ArtistsViewModel _vm;

    public ArtistsPage()
    {
        InitializeComponent();
        _vm = App.Services.GetRequiredService<ArtistsViewModel>();
        DataContext = _vm;
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ArtistsViewModel.IsFiltered))
                RefreshChrome();
        };
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e) => await LoadAsync();

    private async Task LoadAsync()
    {
        BusyRing.Visibility = Visibility.Visible;
        RetryButton.Visibility = Visibility.Collapsed;
        await _vm.LoadArtistsAsync();
        BusyRing.Visibility = Visibility.Collapsed;
        RetryButton.Visibility = _vm.Status is not null && _vm.Artists.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;
        RefreshChrome();
    }

    private void RefreshChrome()
    {
        LettersStrip.Visibility = _vm.Letters.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearFilterButton.Visibility = _vm.IsFiltered ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnArtistClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ArtistEntry a)
            Frame.Navigate(typeof(ArtistPage), a.Id);
    }

    private void OnLetterClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string letter })
        {
            _vm.ToggleLetter(letter);
            RefreshChrome();
        }
    }

    private void OnClearFilters(object sender, RoutedEventArgs e)
    {
        _vm.ClearFilters();
        RefreshChrome();
    }

    private async void OnRetry(object sender, RoutedEventArgs e)
    {
        BusyRing.Visibility = Visibility.Visible;
        RetryButton.Visibility = Visibility.Collapsed;
        await _vm.ReloadArtistsAsync();
        BusyRing.Visibility = Visibility.Collapsed;
        RetryButton.Visibility = _vm.Status is not null && _vm.Artists.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;
    }
}
```

- [ ] **Step 4: Register the VM as a singleton**

In `App.xaml.cs`, next to the other view-model registrations:

```csharp
        sc.AddSingleton<ArtistsViewModel>();
```

Singleton, not transient — `HomeViewModel` (Task 5) shares the catalog cache through it.

- [ ] **Step 5: Build**

Run: `dotnet build native/Nugsdotnet.Native/Nugsdotnet.Native.csproj -c Debug -p:Platform=x64 -p:RuntimeIdentifier=win-x64`
Expected: Build succeeds. (Home still owns its own copy of the grid until Task 5 — no behavior change yet.)

- [ ] **Step 6: Commit**

```bash
git add native/Nugsdotnet.Native/ViewModels/ArtistsViewModel.cs native/Nugsdotnet.Native/Views/Pages/ArtistsPage.xaml native/Nugsdotnet.Native/Views/Pages/ArtistsPage.xaml.cs native/Nugsdotnet.Native/App.xaml.cs
git commit -m "Add ArtistsPage: the A–Z index on its own page"
```

---

### Task 5: `HomeViewModel` becomes the journal's binding surface

**Files:**
- Modify: `native/Nugsdotnet.Native/ViewModels/HomeViewModel.cs` (full rewrite)

**Interfaces:**
- Consumes: `ListeningJournal`, `JournalMath` (Tasks 1–2), `ArtistsViewModel.CatalogSnapshot()` (Task 4), `RecentsStore`, `StashStore`, `ImageLoader`, `PlayerService`, `HomeDashboard` (existing).
- Produces (Task 7 binds all of these):
  - `ObservableCollection<TopArtistRow> TopArtists`, `ObservableCollection<NightBar> Nights`, `ObservableCollection<ShowCard> Shelf`
  - `string Heading` — night count or begin-tonight
  - `string? RunLabel` — `"6◆ NIGHT RUN · BEST 9"` (`◇` when the current run spans a grace night), null when no run
  - `string? RecapLine`, `bool HasJournal`, `bool HasTopArtists`
  - `string HoursText/ShowsText/TracksText` (`"38.5 h"` / `"42"` / `"312"`, `"—"` when empty)
  - `string HoursScale/ShowsScale/TracksScale` (right-hand scale label: `"next 50"`, `""` when none)
  - `double HoursNeedle/ShowsNeedle/TracksNeedle`, `double HoursRed/ShowsRed/TracksRed`
  - `string? MilestoneLine`, `string? NewMilestoneId` (set for one visit, then cleared)
  - Continue strip members unchanged: `HasContinue`, `HasShow`, `ContinueCue`, `ContinueTitle`, `ContinueSub`, `ContinuePosition`, `ContinuePlayLabel`, `ContinueContainerId`, `ContinueArt`, `ToggleContinuePlayback()`, `RefreshContinue()`
  - Kept: `YourArtists`, `StashTotal`, `ResetRails()`
  - Removed: `Greeting`, `RecentMeter/StashMeter/ArtistMeter`, `Recent`/`Stash` collections, the A–Z members (moved to `ArtistsViewModel`), `IsFirstRun`

- [ ] **Step 1: Rewrite `HomeViewModel.cs`**

Keep the existing `ShowCard` class at the top of the file unchanged. Replace `HomeViewModel` itself with:

```csharp
/// <summary>
/// Home dashboard: the listening journal. Night heading, run state, last-night
/// recap, VU meters, top artists, 14-night timeline, milestones, a slim resume
/// strip, tonight's shelf (recents + stash merged), and your artists. The A–Z
/// grid lives on ArtistsPage. Registered as a singleton — the journal, shelf,
/// and resume strip refresh on every visit.
/// </summary>
public partial class HomeViewModel : ObservableObject
{
    private readonly RecentsStore _recents;
    private readonly StashStore _stash;
    private readonly ListeningJournal _journal;
    private readonly ImageLoader _images;
    private readonly PlayerService _player;
    private readonly ArtistsViewModel _artists;
    private List<string?> _recentArtists = new();
    private List<string?> _stashArtists = new();
    private string? _continueArtPath;

    public ObservableCollection<ShowCard> Shelf { get; } = new();
    public ObservableCollection<ArtistEntry> YourArtists { get; } = new();
    public ObservableCollection<TopArtistRow> TopArtists { get; } = new();
    public ObservableCollection<NightBar> Nights { get; } = new();

    [ObservableProperty] public partial string Heading { get; set; } = "THE JOURNAL BEGINS TONIGHT";
    [ObservableProperty] public partial string? RunLabel { get; set; }
    [ObservableProperty] public partial string? RecapLine { get; set; }
    [ObservableProperty] public partial bool HasJournal { get; set; }
    [ObservableProperty] public partial bool HasTopArtists { get; set; }
    [ObservableProperty] public partial string HoursText { get; set; } = "—";
    [ObservableProperty] public partial string ShowsText { get; set; } = "—";
    [ObservableProperty] public partial string TracksText { get; set; } = "—";
    [ObservableProperty] public partial string HoursScale { get; set; } = "";
    [ObservableProperty] public partial string ShowsScale { get; set; } = "";
    [ObservableProperty] public partial string TracksScale { get; set; } = "";
    [ObservableProperty] public partial double HoursNeedle { get; set; }
    [ObservableProperty] public partial double ShowsNeedle { get; set; }
    [ObservableProperty] public partial double TracksNeedle { get; set; }
    [ObservableProperty] public partial double HoursRed { get; set; } = JournalMath.RedZoneStart;
    [ObservableProperty] public partial double ShowsRed { get; set; } = JournalMath.RedZoneStart;
    [ObservableProperty] public partial double TracksRed { get; set; }
    [ObservableProperty] public partial string? MilestoneLine { get; set; }
    [ObservableProperty] public partial string? NewMilestoneId { get; set; }
    [ObservableProperty] public partial bool HasContinue { get; set; }
    [ObservableProperty] public partial bool HasShow { get; set; }
    [ObservableProperty] public partial string ContinueCue { get; set; } = "CONTINUE";
    [ObservableProperty] public partial string ContinueTitle { get; set; } = "";
    [ObservableProperty] public partial string ContinueSub { get; set; } = "";
    [ObservableProperty] public partial string ContinuePosition { get; set; } = "";
    [ObservableProperty] public partial string ContinuePlayLabel { get; set; } = "PLAY";
    [ObservableProperty] public partial string? ContinueContainerId { get; set; }
    [ObservableProperty] public partial ImageSource? ContinueArt { get; set; }

    public int StashTotal { get; private set; }

    public HomeViewModel(
        RecentsStore recents, StashStore stash, ListeningJournal journal,
        ImageLoader images, PlayerService player, ArtistsViewModel artists)
    {
        _recents = recents;
        _stash = stash;
        _journal = journal;
        _images = images;
        _player = player;
        _artists = artists;
    }

    /// <summary>Rebuilds the journal hero, shelf, and resume strip from disk + the player.</summary>
    public async Task RefreshRailsAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);

        // ---- journal -------------------------------------------------------
        var state = await _journal.LoadAsync();

        Heading = JournalMath.NightHeading(today, state);
        var run = JournalMath.Run(state.Days, today);
        RunLabel = run.Current > 0
            ? $"{run.Current}{(run.GraceNights > 0 ? "◇" : "◆")} NIGHT RUN · BEST {run.Best}"
            : null;
        RecapLine = JournalMath.Recap(DayOn(state, today.AddDays(-1)));
        HasJournal = state.Days.Any(JournalMath.IsListeningNight) || state.Shows.Count > 0;

        var hours = JournalMath.Hours(state);
        var shows = (double)JournalMath.ShowsHeard(state);
        var tracks = (double)state.TracksCompleted;
        var hasData = hours > 0 || shows > 0 || tracks > 0;
        HoursText = hasData ? $"{JournalMath.FormatHours(hours * 3600)} h" : "—";
        ShowsText = hasData ? $"{shows:0}" : "—";
        TracksText = hasData ? $"{tracks:0}" : "—";

        var hoursNext = JournalMath.NextThreshold("hours", hours);
        var showsNext = JournalMath.NextThreshold("shows", shows);
        (HoursNeedle, HoursScale) = Meter(hours, hoursNext, v => $"next {v:0}");
        (ShowsNeedle, ShowsScale) = Meter(shows, showsNext, v => $"next {v:0}");
        // TRACKS has no milestones: fixed 0–1000 scale, no red zone.
        TracksNeedle = Math.Clamp(tracks / JournalMath.TracksScaleEnd, 0, 1);
        TracksScale = $"{JournalMath.TracksScaleEnd:0}";

        TopArtists.Clear();
        foreach (var row in JournalMath.TopArtists(state.Days, today))
            TopArtists.Add(row);
        HasTopArtists = TopArtists.Count > 0;

        Nights.Clear();
        foreach (var bar in JournalMath.Timeline(state.Days, today))
            Nights.Add(bar);

        MilestoneLine = JournalMath.MilestoneLine(state);
        var newly = JournalMath.NewlyReached(state, today);
        if (newly.Count > 0)
        {
            NewMilestoneId = newly[0];
            state.Milestones.AddRange(newly.Select(id =>
                new MilestoneReached(id, today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))));
            await _journal.SaveAsync(state);
        }
        else
        {
            NewMilestoneId = null;
        }

        // ---- shelf + your artists -------------------------------------------
        var plays = await _recents.LoadAsync();
        _recentArtists = plays.Select(p => p.Artist).ToList();
        var stashed = await _stash.LoadAsync();
        StashTotal = stashed.Count;
        _stashArtists = stashed.Select(s => s.Artist).ToList();

        RebuildShelf(plays, stashed);
        RebuildYourArtists();

        RefreshContinue();
        _ = LoadArtsAsync(Shelf.ToList());   // ImageLoader never throws
    }

    /// <summary>Needle + right-hand scale label toward the next milestone.</summary>
    private static (double Needle, string Scale) Meter(
        double value, double? next, Func<double, string> render)
    {
        var s = JournalMath.Scale(value, next);
        return (s.Needle, next is null ? "" : render(next.Value));
    }

    private static JournalDay? DayOn(JournalState state, DateOnly date) =>
        state.Days.FirstOrDefault(d => d.Date == date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

    /// <summary>Recents first, deduped by container, stash filling behind, 24 cards.</summary>
    private void RebuildShelf(IReadOnlyList<RecentPlay> plays, IReadOnlyList<StashEntry> stashed)
    {
        Shelf.Clear();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in plays)
        {
            if (Shelf.Count >= HomeDashboard.StashRailCap) break;
            if (string.IsNullOrEmpty(p.ContainerId) || !seen.Add(p.ContainerId)) continue;
            Shelf.Add(new ShowCard(p));
        }
        foreach (var s in stashed)
        {
            if (Shelf.Count >= HomeDashboard.StashRailCap) break;
            if (string.IsNullOrEmpty(s.ContainerId) || !seen.Add(s.ContainerId)) continue;
            Shelf.Add(new ShowCard(s));
        }
    }

    /// <summary>Drops personal chrome so a signed-out shell cannot flash the
    /// previous nugs account's journal/shelf/resume card.</summary>
    public void ResetRails()
    {
        Shelf.Clear();
        YourArtists.Clear();
        TopArtists.Clear();
        Nights.Clear();
        _recentArtists = new();
        _stashArtists = new();
        StashTotal = 0;
        Heading = "THE JOURNAL BEGINS TONIGHT";
        RunLabel = null;
        RecapLine = null;
        HasJournal = false;
        HasTopArtists = false;
        HoursText = ShowsText = TracksText = "—";
        HoursScale = ShowsScale = TracksScale = "";
        HoursNeedle = ShowsNeedle = TracksNeedle = 0;
        MilestoneLine = null;
        NewMilestoneId = null;
        ClearContinue();
    }

    public void ToggleContinuePlayback()
    {
        _player.TogglePlayPause();
        RefreshContinue();
    }

    /// <summary>Re-reads the player into the resume strip. Cheap — no disk.</summary>
    public void RefreshContinue()
    {
        var c = _player.Current;
        if (c is null)
        {
            ClearContinue();
            return;
        }

        HasContinue = true;
        ContinueCue = HomeDashboard.ContinueCue(_player.IsPlaying);
        ContinueTitle = c.Title ?? "Untitled";
        ContinueSub = string.Join("  ·  ", new[] { c.Artist, c.Show }.Where(s => !string.IsNullOrEmpty(s)));
        ContinueContainerId = c.ContainerId;
        HasShow = !string.IsNullOrEmpty(c.ContainerId);
        ContinuePlayLabel = _player.IsPlaying ? "PAUSE" : "PLAY";
        var primedPaused = !_player.IsPlaying && _player.Duration.TotalSeconds <= 0;
        ContinuePosition = HomeDashboard.ContinuePosition(
            _player.Position.TotalSeconds, _player.Duration.TotalSeconds, primedPaused) ?? "";

        if (c.ImagePath != _continueArtPath)
        {
            _continueArtPath = c.ImagePath;
            ContinueArt = null;
            if (!string.IsNullOrEmpty(c.ImagePath))
                _ = LoadContinueArtAsync(new ShowCard(
                    c.ContainerId ?? "", c.Title, ContinueSub, c.ImagePath, c.Artist));
        }
    }

    private void ClearContinue()
    {
        HasContinue = false;
        HasShow = false;
        ContinueCue = "CONTINUE";
        ContinueTitle = "";
        ContinueSub = "";
        ContinuePosition = "";
        ContinuePlayLabel = "PLAY";
        ContinueContainerId = null;
        ContinueArt = null;
        _continueArtPath = null;
    }

    private async Task LoadArtsAsync(IReadOnlyList<ShowCard> cards)
    {
        // Overlap CDN fetches; BitmapImage decode still resumes on the UI thread.
        await Task.WhenAll(cards.Select(c => c.LoadArtAsync(_images)));
    }

    private async Task LoadContinueArtAsync(ShowCard card)
    {
        await card.LoadArtAsync(_images);
        ContinueArt = card.Art;
    }

    private void RebuildYourArtists()
    {
        YourArtists.Clear();
        foreach (var a in HomeDashboard.YourArtists(_recentArtists, _stashArtists, _artists.CatalogSnapshot()))
            YourArtists.Add(a);
    }
}
```

Add `using System.Globalization;` at the top of the file (the milestone date stamp uses it).

- [ ] **Step 2: Build**

Run: `dotnet build native/Nugsdotnet.Native/Nugsdotnet.Native.csproj -c Debug -p:Platform=x64 -p:RuntimeIdentifier=win-x64`
Expected: **FAIL** — `HomePage.xaml`/`.cs` still reference removed members (`Greeting`, `Recent`, `Stash`, `Letters`, `Filter`, `LoadArtistsAsync`, …). That is Task 7's job; the build error list is its worklist. Do not commit a broken build — Tasks 5 and 7 land as separate commits from the same working session, with the build green before each `git commit`.

- [ ] **Step 3: Commit (immediately after Task 7 Step 5 — see Task 7)**

```bash
git add native/Nugsdotnet.Native/ViewModels/HomeViewModel.cs
git commit -m "HomeViewModel becomes the journal: meters, run, recap, shelf"
```

---

### Task 6: `VuMeter` control

**Files:**
- Create: `native/Nugsdotnet.Native/Views/Controls/VuMeter.xaml`
- Create: `native/Nugsdotnet.Native/Views/Controls/VuMeter.xaml.cs`

**Interfaces:**
- Produces (Task 7 consumes): `VuMeter` UserControl with DependencyProperties:
  - `Caption` (string) — faceplate label above the scale
  - `ValueText` (string) — mono readout, right-aligned under the scale
  - `ScaleEnd` (string) — right-hand scale label (e.g. `"next 50"`); empty hides it
  - `Needle` (double, 0..1) — needle position across the scale
  - `RedZone` (double, 0..1) — where the red zone starts (1 = none)

- [ ] **Step 1: Create `VuMeter.xaml`**

```xml
<?xml version="1.0" encoding="utf-8"?>
<UserControl
    x:Class="Nugsdotnet.Native.Views.Controls.VuMeter"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <StackPanel Spacing="4">
        <TextBlock x:Name="CaptionText" Style="{StaticResource FaceplateLabelStyle}" />
        <Grid x:Name="Scale" Height="14" CornerRadius="2"
              Background="{StaticResource BrandSurface2}"
              BorderBrush="{StaticResource BrandBorder}" BorderThickness="1"
              SizeChanged="OnScaleSizeChanged">
            <Rectangle x:Name="RedZoneRect" HorizontalAlignment="Left"
                       Fill="#26FF7A1A" RadiusX="1" RadiusY="1" />
            <Rectangle x:Name="NeedleRect" Width="2" HorizontalAlignment="Left"
                       Fill="{StaticResource BrandAccent}" />
        </Grid>
        <Grid ColumnDefinitions="Auto,*,Auto">
            <TextBlock Grid.Column="0" Text="0" FontFamily="{StaticResource BrandMonoFont}"
                       FontSize="8" Foreground="{StaticResource BrandDim}" />
            <TextBlock Grid.Column="1" x:Name="ScaleEndText" HorizontalAlignment="Right"
                       FontFamily="{StaticResource BrandMonoFont}" FontSize="8"
                       Foreground="{StaticResource BrandDim}" />
            <TextBlock Grid.Column="2" x:Name="ValueTextEl" Margin="10,0,0,0"
                       FontFamily="{StaticResource BrandMonoFont}" FontSize="12"
                       Foreground="{StaticResource BrandAccent}" />
        </Grid>
    </StackPanel>
</UserControl>
```

- [ ] **Step 2: Create `VuMeter.xaml.cs`**

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;

namespace Nugsdotnet.Native.Views.Controls;

/// <summary>
/// One VU meter on the journal hero: a printed scale, a needle resting at the
/// current level, and a red zone marking the next milestone's approach.
/// </summary>
public sealed partial class VuMeter : UserControl
{
    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(
        nameof(Caption), typeof(string), typeof(VuMeter), new PropertyMetadata("", OnLayoutChanged));
    public static readonly DependencyProperty ValueTextProperty = DependencyProperty.Register(
        nameof(ValueText), typeof(string), typeof(VuMeter), new PropertyMetadata("", OnLayoutChanged));
    public static readonly DependencyProperty ScaleEndProperty = DependencyProperty.Register(
        nameof(ScaleEnd), typeof(string), typeof(VuMeter), new PropertyMetadata("", OnLayoutChanged));
    public static readonly DependencyProperty NeedleProperty = DependencyProperty.Register(
        nameof(Needle), typeof(double), typeof(VuMeter), new PropertyMetadata(0.0, OnLayoutChanged));
    public static readonly DependencyProperty RedZoneProperty = DependencyProperty.Register(
        nameof(RedZone), typeof(double), typeof(VuMeter), new PropertyMetadata(0.85, OnLayoutChanged));

    public VuMeter()
    {
        InitializeComponent();
        Layout();
    }

    public string Caption { get => (string)GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }
    public string ValueText { get => (string)GetValue(ValueTextProperty); set => SetValue(ValueTextProperty, value); }
    public string ScaleEnd { get => (string)GetValue(ScaleEndProperty); set => SetValue(ScaleEndProperty, value); }
    public double Needle { get => (double)GetValue(NeedleProperty); set => SetValue(NeedleProperty, value); }
    public double RedZone { get => (double)GetValue(RedZoneProperty); set => SetValue(RedZoneProperty, value); }

    private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((VuMeter)d).Layout();

    private void OnScaleSizeChanged(object sender, SizeChangedEventArgs e) => Layout();

    private void Layout()
    {
        CaptionText.Text = Caption;
        ValueTextEl.Text = ValueText;
        ScaleEndText.Text = ScaleEnd;
        ScaleEndText.Visibility = string.IsNullOrEmpty(ScaleEnd) ? Visibility.Collapsed : Visibility.Visible;

        var w = Scale.ActualWidth;
        if (double.IsNaN(w) || w <= 0) return;
        RedZoneRect.Width = Math.Clamp(RedZone, 0, 1) * w;
        RedZoneRect.Visibility = RedZone is > 0 and < 1 ? Visibility.Visible : Visibility.Collapsed;
        NeedleRect.Margin = new Thickness(Math.Clamp(Needle, 0, 1) * w - 1, 0, 0, 0);
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build native/Nugsdotnet.Native/Nugsdotnet.Native.csproj -c Debug -p:Platform=x64 -p:RuntimeIdentifier=win-x64`
Expected: Build succeeds (control unused yet).

- [ ] **Step 4: Commit**

```bash
git add native/Nugsdotnet.Native/Views/Controls/VuMeter.xaml native/Nugsdotnet.Native/Views/Controls/VuMeter.xaml.cs
git commit -m "Add VuMeter: needle, scale, red zone for the journal hero"
```

---

### Task 7: `HomePage` — The Wrap

**Files:**
- Modify: `native/Nugsdotnet.Native/Views/Pages/HomePage.xaml` (full rewrite)
- Modify: `native/Nugsdotnet.Native/Views/Pages/HomePage.xaml.cs` (full rewrite)
- Modify: `README.md` ("On the faceplate" table) and `native/README.md` (per-account store files list, if present)

**Interfaces:**
- Consumes: `HomeViewModel` (Task 5), `VuMeter` (Task 6), `ShowCard`, `ArtistEntry`, `TopArtistRow`, `NightBar`, `ArtistsViewModel.CatalogSnapshot()` (existing/Tasks 2, 4).
- Produces: the new Home page; `Frame.Navigate(typeof(ArtistsPage))` on `full index ▸`.

**Binding facts this task relies on:**
- `x:Bind` converts `bool` → `Visibility` implicitly in WinUI 3 (as in UWP). If the XAML compiler rejects it, add a `BoolToVisibilityConverter` page resource and switch those bindings to it.
- `Grid.RowDefinitions` / `Grid.ColumnDefinitions` accept a star-weight string via `x:Bind` (`"{x:Bind BarStars}"` → `"92*,8*"`), which is how bars scale without pixel math.
- Section visibility (`JournalSection`, `BeginSection`, …) is owned by code-behind `RefreshChrome()` — no converters in XAML.

- [ ] **Step 1: Rewrite `HomePage.xaml`**

```xml
<?xml version="1.0" encoding="utf-8"?>
<Page
    x:Class="Nugsdotnet.Native.Views.Pages.HomePage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:controls="using:Nugsdotnet.Native.Views.Controls"
    xmlns:core="using:Nugsdotnet.Native.Core"
    xmlns:vm="using:Nugsdotnet.Native.ViewModels"
    Background="{StaticResource BrandBg}">

    <Page.Resources>
        <!-- One card template for the shelf rail. -->
        <DataTemplate x:Key="ShowCardTemplate" x:DataType="vm:ShowCard">
            <Button Style="{StaticResource CardButtonStyle}"
                    Tag="{x:Bind}" Click="OnCardClick">
                <StackPanel Width="150" Spacing="6">
                    <Border Width="150" Height="150" CornerRadius="4"
                            Background="{StaticResource BrandSurface2}"
                            BorderBrush="{StaticResource BrandBorder}" BorderThickness="1">
                        <Image Source="{x:Bind Art, Mode=OneWay}" Stretch="UniformToFill" />
                    </Border>
                    <TextBlock Text="{x:Bind Title}" Height="36"
                               FontFamily="{StaticResource BrandBodyFont}"
                               FontWeight="SemiBold" FontSize="13"
                               Foreground="{StaticResource BrandText}"
                               TextWrapping="Wrap" MaxLines="2"
                               TextTrimming="CharacterEllipsis" />
                    <TextBlock Text="{x:Bind Sub}"
                               FontFamily="{StaticResource BrandMonoFont}" FontSize="11"
                               Foreground="{StaticResource BrandDim}"
                               TextTrimming="CharacterEllipsis" />
                </StackPanel>
            </Button>
        </DataTemplate>

        <ItemsPanelTemplate x:Key="RailPanel">
            <StackPanel Orientation="Horizontal" Spacing="10" />
        </ItemsPanelTemplate>

        <DataTemplate x:Key="ArtistChipTemplate" x:DataType="core:ArtistEntry">
            <Button Style="{StaticResource CardButtonStyle}" Padding="0"
                    Tag="{x:Bind}" Click="OnArtistChipClick">
                <Border Width="220" Height="40" CornerRadius="4" Padding="12,0"
                        Background="{StaticResource BrandSurface}"
                        BorderBrush="{StaticResource BrandBorder}" BorderThickness="1">
                    <TextBlock Text="{x:Bind Name}" VerticalAlignment="Center"
                               FontFamily="{StaticResource BrandBodyFont}" FontSize="14"
                               Foreground="{StaticResource BrandText}"
                               TextTrimming="CharacterEllipsis" />
                </Border>
            </Button>
        </DataTemplate>
    </Page.Resources>

    <ScrollViewer VerticalScrollBarVisibility="Auto" VerticalScrollMode="Enabled"
                  HorizontalScrollBarVisibility="Disabled" HorizontalScrollMode="Disabled">
        <StackPanel Padding="24,20,24,24" Spacing="20" MaxWidth="1100" HorizontalAlignment="Left">

            <!-- ── Greeting zone — night count, run state, last-night recap ── -->
            <StackPanel Spacing="6">
                <Grid ColumnDefinitions="*,Auto" ColumnSpacing="24">
                    <TextBlock Text="{Binding Heading}"
                               FontFamily="{StaticResource BrandDisplayFont}" FontWeight="ExtraBold"
                               FontSize="34" CharacterSpacing="20"
                               Foreground="{StaticResource BrandText}" />
                    <TextBlock Grid.Column="1" x:Name="RunLabel" Text="{Binding RunLabel}"
                               VerticalAlignment="Center"
                               FontFamily="{StaticResource BrandMonoFont}" FontSize="12"
                               Foreground="{StaticResource BrandAccent}" />
                </Grid>
                <TextBlock x:Name="RecapLineText" Text="{Binding RecapLine}"
                           FontFamily="{StaticResource BrandMonoFont}" FontSize="12"
                           Foreground="{StaticResource BrandDim}" />
            </StackPanel>

            <!-- ── Journal hero — VU meters, charts, timeline, milestone ───── -->
            <StackPanel x:Name="JournalSection" Spacing="12" Visibility="Collapsed">
                <Grid ColumnDefinitions="*,*,*" ColumnSpacing="20">
                    <controls:VuMeter Grid.Column="0" Caption="HOURS ON AIR"
                                      ValueText="{Binding HoursText}" ScaleEnd="{Binding HoursScale}"
                                      Needle="{Binding HoursNeedle}" RedZone="{Binding HoursRed}" />
                    <controls:VuMeter Grid.Column="1" Caption="SHOWS LOGGED"
                                      ValueText="{Binding ShowsText}" ScaleEnd="{Binding ShowsScale}"
                                      Needle="{Binding ShowsNeedle}" RedZone="{Binding ShowsRed}" />
                    <controls:VuMeter Grid.Column="2" Caption="TRACKS"
                                      ValueText="{Binding TracksText}" ScaleEnd="{Binding TracksScale}"
                                      Needle="{Binding TracksNeedle}" RedZone="{Binding TracksRed}" />
                </Grid>

                <Grid ColumnDefinitions="*,*" ColumnSpacing="20">
                    <!-- Top artists · 30 nights -->
                    <StackPanel Grid.Column="0" Spacing="8">
                        <TextBlock Text="TOP ARTISTS · 30 NIGHTS" Style="{StaticResource FaceplateLabelStyle}" />
                        <ItemsControl ItemsSource="{Binding TopArtists}">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate x:DataType="core:TopArtistRow">
                                    <Button Style="{StaticResource CardButtonStyle}" Padding="4,2"
                                            Tag="{x:Bind Name}" Click="OnTopArtistClick">
                                        <Grid ColumnDefinitions="Auto,Auto,*,Auto" ColumnSpacing="8">
                                            <TextBlock Grid.Column="0" Text="{x:Bind RankText}"
                                                       FontFamily="{StaticResource BrandMonoFont}" FontSize="11"
                                                       Foreground="{StaticResource BrandDim}" />
                                            <TextBlock Grid.Column="1" Text="{x:Bind Name}" Width="140"
                                                       FontFamily="{StaticResource BrandBodyFont}" FontSize="13"
                                                       Foreground="{StaticResource BrandText}"
                                                       TextTrimming="CharacterEllipsis" HorizontalAlignment="Left" />
                                            <!-- bar scales to the leader via star weights -->
                                            <Grid Grid.Column="2" Height="6" VerticalAlignment="Center"
                                                  Background="{StaticResource BrandSurface2}"
                                                  ColumnDefinitions="{x:Bind BarStars}">
                                                <Border Grid.Column="0" Height="6" CornerRadius="2"
                                                        Background="{StaticResource BrandAccent}" />
                                            </Grid>
                                            <TextBlock Grid.Column="3" Text="{x:Bind HoursLabel}"
                                                       FontFamily="{StaticResource BrandMonoFont}" FontSize="10"
                                                       Foreground="{StaticResource BrandDim}" />
                                        </Grid>
                                    </Button>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </StackPanel>

                    <!-- Last 14 nights -->
                    <StackPanel Grid.Column="1" Spacing="8">
                        <TextBlock Text="LAST 14 NIGHTS" Style="{StaticResource FaceplateLabelStyle}" />
                        <ItemsControl ItemsSource="{Binding Nights}">
                            <ItemsControl.ItemsPanel>
                                <ItemsPanelTemplate>
                                    <StackPanel Orientation="Horizontal" Spacing="3" />
                                </ItemsPanelTemplate>
                            </ItemsControl.ItemsPanel>
                            <ItemsControl.ItemTemplate>
                                <DataTemplate x:DataType="core:NightBar">
                                    <Grid Width="18" Height="48" VerticalAlignment="Bottom"
                                          ToolTipService.ToolTip="{x:Bind ToolTip}">
                                        <!-- amber bar, height via star rows -->
                                        <Grid RowDefinitions="{x:Bind BarStars}"
                                              Visibility="{x:Bind IsListening}">
                                            <Border Grid.Row="1" CornerRadius="1,1,0,0"
                                                    Background="{StaticResource BrandAccent}" />
                                        </Grid>
                                        <!-- hollow ring for a grace night -->
                                        <Border CornerRadius="1" BorderBrush="{StaticResource BrandBorder}"
                                                BorderThickness="1"
                                                Visibility="{x:Bind IsGrace}" />
                                    </Grid>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </StackPanel>
                </Grid>

                <TextBlock x:Name="MilestoneLineText" Text="{Binding MilestoneLine}"
                           FontFamily="{StaticResource BrandMonoFont}" FontSize="12"
                           Foreground="{StaticResource BrandAccent}" />
            </StackPanel>

            <!-- ── Day one — the journal begins tonight ────────────────────── -->
            <Border x:Name="BeginSection" CornerRadius="6" Padding="16,12"
                    Background="{StaticResource BrandSurface}"
                    BorderBrush="{StaticResource BrandBorder}" BorderThickness="1"
                    Visibility="Collapsed">
                <StackPanel Spacing="4">
                    <TextBlock Text="THE JOURNAL BEGINS TONIGHT"
                               Style="{StaticResource FaceplateLabelStyle}"
                               Foreground="{StaticResource BrandAccent}" />
                    <TextBlock Text="Press play and the meter starts running — hours, shows, and night runs land here."
                               FontFamily="{StaticResource BrandBodyFont}" FontSize="13"
                               Foreground="{StaticResource BrandDim}" TextWrapping="Wrap" />
                </StackPanel>
            </Border>

            <!-- ── Resume strip — ON THE DECK / CONTINUE ───────────────────── -->
            <StackPanel x:Name="ContinueSection" Spacing="8" Visibility="Collapsed">
                <StackPanel Orientation="Horizontal" Spacing="10">
                    <Border Width="6" Height="6" CornerRadius="1" VerticalAlignment="Center"
                            Background="{StaticResource BrandAccent}" />
                    <TextBlock Text="{Binding ContinueCue}" Style="{StaticResource FaceplateLabelStyle}"
                               Foreground="{StaticResource BrandAccent}" />
                </StackPanel>
                <Grid ColumnDefinitions="Auto,Auto,*,Auto,Auto" ColumnSpacing="14" VerticalAlignment="Center">
                    <Button Grid.Column="0" Style="{StaticResource CardButtonStyle}" Padding="0"
                            Click="OnContinueOpen" IsEnabled="{Binding HasShow}">
                        <Border Width="56" Height="56" CornerRadius="4"
                                Background="{StaticResource BrandSurface2}"
                                BorderBrush="{StaticResource BrandBorder}" BorderThickness="1">
                            <Image Source="{Binding ContinueArt}" Stretch="UniformToFill" />
                        </Border>
                    </Button>
                    <StackPanel Grid.Column="1" VerticalAlignment="Center" Spacing="2" MaxWidth="420">
                        <TextBlock Text="{Binding ContinueTitle}"
                                   FontFamily="{StaticResource BrandBodyFont}" FontWeight="SemiBold"
                                   FontSize="15" Foreground="{StaticResource BrandText}"
                                   TextTrimming="CharacterEllipsis" />
                        <TextBlock Text="{Binding ContinueSub}"
                                   FontFamily="{StaticResource BrandMonoFont}" FontSize="11"
                                   Foreground="{StaticResource BrandDim}"
                                   TextTrimming="CharacterEllipsis" />
                        <TextBlock Text="{Binding ContinuePosition}"
                                   FontFamily="{StaticResource BrandMonoFont}" FontSize="11"
                                   Foreground="{StaticResource BrandAccent}" />
                    </StackPanel>
                    <Button Grid.Column="3" Content="{Binding ContinuePlayLabel}" Click="OnContinuePlay"
                            Style="{StaticResource PlayButtonStyle}"
                            ToolTipService.ToolTip="Play / pause (Ctrl+Space)" />
                    <Button Grid.Column="4" x:Name="ContinueOpenButton" Content="open show"
                            Click="OnContinueOpen" Style="{StaticResource IconButtonStyle}"
                            VerticalAlignment="Center" Visibility="Collapsed" />
                </Grid>
            </StackPanel>

            <!-- ── TONIGHT'S SHELF — recents + stash merged ─────────────────── -->
            <StackPanel x:Name="ShelfSection" Spacing="10" Visibility="Collapsed">
                <Grid ColumnDefinitions="Auto,*,Auto">
                    <StackPanel Grid.Column="0" Orientation="Horizontal" Spacing="10">
                        <Border Width="6" Height="6" CornerRadius="1" VerticalAlignment="Center"
                                Background="{StaticResource BrandAccent}" />
                        <TextBlock Text="TONIGHT'S SHELF" Style="{StaticResource FaceplateLabelStyle}" />
                    </StackPanel>
                    <Button Grid.Column="2" Content="see all" Click="OnSeeAllStash"
                            Style="{StaticResource IconButtonStyle}" HorizontalAlignment="Right" />
                </Grid>
                <ScrollViewer HorizontalScrollBarVisibility="Auto" VerticalScrollBarVisibility="Disabled"
                              HorizontalScrollMode="Enabled" VerticalScrollMode="Disabled">
                    <ItemsControl ItemsSource="{Binding Shelf}"
                                  ItemsPanel="{StaticResource RailPanel}"
                                  ItemTemplate="{StaticResource ShowCardTemplate}" />
                </ScrollViewer>
            </StackPanel>

            <!-- ── YOUR ARTISTS + full index ────────────────────────────────── -->
            <StackPanel x:Name="YourArtistsSection" Spacing="10" Visibility="Collapsed">
                <Grid ColumnDefinitions="Auto,*,Auto">
                    <StackPanel Grid.Column="0" Orientation="Horizontal" Spacing="10">
                        <Border Width="6" Height="6" CornerRadius="1" VerticalAlignment="Center"
                                Background="{StaticResource BrandAccent}" />
                        <TextBlock Text="YOUR ARTISTS" Style="{StaticResource FaceplateLabelStyle}" />
                    </StackPanel>
                    <Button Grid.Column="2" Content="full index ▸" Click="OnFullIndex"
                            Style="{StaticResource IconButtonStyle}" HorizontalAlignment="Right" />
                </Grid>
                <ScrollViewer HorizontalScrollBarVisibility="Auto" VerticalScrollBarVisibility="Disabled"
                              HorizontalScrollMode="Enabled" VerticalScrollMode="Disabled">
                    <ItemsControl ItemsSource="{Binding YourArtists}"
                                  ItemsPanel="{StaticResource RailPanel}"
                                  ItemTemplate="{StaticResource ArtistChipTemplate}" />
                </ScrollViewer>
            </StackPanel>
        </StackPanel>
    </ScrollViewer>
</Page>
```

- [ ] **Step 2: Rewrite `HomePage.xaml.cs`**

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Nugsdotnet.Native.Core;
using Nugsdotnet.Native.ViewModels;

namespace Nugsdotnet.Native.Views.Pages;

public sealed partial class HomePage : Page
{
    private readonly HomeViewModel _vm;
    private readonly ArtistsViewModel _artists;
    private Storyboard? _pulse;

    public HomePage()
    {
        InitializeComponent();
        _vm = App.Services.GetRequiredService<HomeViewModel>();
        _artists = App.Services.GetRequiredService<ArtistsViewModel>();
        DataContext = _vm;
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(HomeViewModel.HasContinue)
                or nameof(HomeViewModel.HasShow)
                or nameof(HomeViewModel.HasJournal)
                or nameof(HomeViewModel.StashTotal)
                or nameof(HomeViewModel.YourArtists))
                RefreshChrome();
            else if (e.PropertyName is nameof(HomeViewModel.NewMilestoneId))
                PulseMilestone();
        };
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        await ReloadAsync();
        // The catalog may not be fetched yet on a cold start; when it lands,
        // "your artists" can finally match names to ids.
        if (_artists.Artists.Count == 0)
        {
            await _artists.LoadArtistsAsync();
            _vm.RefreshRailsAsync();
        }
    }

    private async Task ReloadAsync()
    {
        await _vm.RefreshRailsAsync();
        RefreshChrome();
    }

    private void RefreshChrome()
    {
        JournalSection.Visibility = _vm.HasJournal ? Visibility.Visible : Visibility.Collapsed;
        BeginSection.Visibility = _vm.HasJournal ? Visibility.Collapsed : Visibility.Visible;
        ContinueSection.Visibility = _vm.HasContinue ? Visibility.Visible : Visibility.Collapsed;
        ContinueOpenButton.Visibility = _vm.HasShow ? Visibility.Visible : Visibility.Collapsed;
        ShelfSection.Visibility = _vm.Shelf.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        YourArtistsSection.Visibility = _vm.YourArtists.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RunLabel.Visibility = _vm.RunLabel is null ? Visibility.Collapsed : Visibility.Visible;
        RecapLineText.Visibility = _vm.RecapLine is null ? Visibility.Collapsed : Visibility.Visible;
        MilestoneLineText.Visibility = _vm.MilestoneLine is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Amber pulse when a milestone fires — once per visit.</summary>
    private void PulseMilestone()
    {
        if (_vm.NewMilestoneId is null) return;
        _pulse?.Stop();
        var sb = new Storyboard();
        var anim = new DoubleAnimation
        {
            From = 1.0, To = 0.25, Duration = new Duration(TimeSpan.FromMilliseconds(420)),
            AutoReverse = true, RepeatBehavior = new RepeatBehavior(3),
        };
        Storyboard.SetTarget(anim, MilestoneLineText);
        Storyboard.SetTargetProperty(anim, "Opacity");
        sb.Children.Add(anim);
        _pulse = sb;
        sb.Begin();
    }

    private void OnCardClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ShowCard c })
            Frame.Navigate(typeof(AlbumPage), c.ContainerId);
    }

    private void OnArtistChipClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ArtistEntry a })
            Frame.Navigate(typeof(ArtistPage), a.Id);
    }

    /// <summary>Top-artist rows come from listening history (names, not ids) —
    /// resolve to the catalog when possible, else fall back to search.</summary>
    private void OnTopArtistClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string name }) return;
        var entry = _artists.CatalogSnapshot()
            .FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
        if (entry is not null)
            Frame.Navigate(typeof(ArtistPage), entry.Id);
        else
            Frame.Navigate(typeof(SearchResultsPage), name);
    }

    private void OnSeeAllStash(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(StashPage));

    private void OnFullIndex(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(ArtistsPage));

    private void OnContinuePlay(object sender, RoutedEventArgs e)
    {
        _vm.ToggleContinuePlayback();
        RefreshChrome();
    }

    private void OnContinueOpen(object sender, RoutedEventArgs e)
    {
        if (_vm.ContinueContainerId is { Length: > 0 } id)
            Frame.Navigate(typeof(AlbumPage), id);
    }
}
```

Verify `SearchResultsPage`'s navigation parameter is the query string (check `MainWindow.OnSearchKeyDown` → `Frame.Navigate(typeof(SearchResultsPage), q)`) — it is; the fallback passes the artist name as the query.

- [ ] **Step 3: Build**

Run: `dotnet build native/Nugsdotnet.Native/Nugsdotnet.Native.csproj -c Debug -p:Platform=x64 -p:RuntimeIdentifier=win-x64`
Expected: Build succeeds, 0 errors (all Task 5 removals now resolved).

- [ ] **Step 4: Run the full test suite**

Run: `dotnet test native/Nugsdotnet.Native.Tests/Nugsdotnet.Native.Tests.csproj`
Expected: PASS — all suites green. If `HomeDashboardTests.GreetingFor_follows_the_faceplate_clock` fails because `GreetingFor` was removed from `HomeDashboard`: delete that one test (the greeting retired with the redesign) and keep the rest of the file intact. If nothing referenced `GreetingFor` anymore, removing it is correct; if something still does, keep the helper.

- [ ] **Step 5: Update READMEs**

In `README.md`, replace the **Home** row of the "On the faceplate" table:

```markdown
| **Home** | Listening journal — night count, VU meters with milestone red zones, top artists, 14-night timeline, resume strip, tonight's shelf |
```

and add an **Artists** row after it:

```markdown
| **Artists** | Full A–Z index with letter jump + live filter |
```

In `native/README.md`, wherever the per-account store files are listed (stash/recents/playback), add `journal.json` with a one-line role ("listening journal — nights, shows, milestones").

- [ ] **Step 6: Commit (Task 5's HomeViewModel change lands together with this — the two are one review unit)**

```bash
git add native/Nugsdotnet.Native/Views/Pages/HomePage.xaml native/Nugsdotnet.Native/Views/Pages/HomePage.xaml.cs native/Nugsdotnet.Native/ViewModels/HomeViewModel.cs README.md native/README.md
git commit -m "Home becomes the listening journal: VU hero, resume strip, tonight's shelf"
```

---

### Task 8: End-to-end verification

**Files:** none (verification only; fix-forward commits allowed)

- [ ] **Step 1: Clean full build + tests**

```bash
dotnet test native/Nugsdotnet.Native.Tests/Nugsdotnet.Native.Tests.csproj
dotnet build native/Nugsdotnet.Native/Nugsdotnet.Native.csproj -c Debug -p:Platform=x64 -p:RuntimeIdentifier=win-x64
```
Expected: all tests pass; build 0 errors.

- [ ] **Step 2: Run the app and walk the journal**

Run: `dotnet run --project native\Nugsdotnet.Native\Nugsdotnet.Native.csproj -c Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64`

Verify, in order:
1. **Day one:** sign in with the account's `journal.json` absent (delete `%LOCALAPPDATA%\nugsdotnet\accounts\{userId}\journal.json` first) → `THE JOURNAL BEGINS TONIGHT` panel, meters hidden, no fake numbers.
2. **First play:** play any show ≥ 5 minutes (or temporarily lower `ListeningNightSeconds` in a local scratch build to verify quickly, then revert) → heading becomes `NIGHT 1 ON THE RECEIVER`, HOURS meter creeps, resume strip shows `ON THE DECK` while playing.
3. **Pause:** pause 30 s → meter stops moving (paused time never counts).
4. **Completion:** let a track end naturally → TRACKS increments by 1; skip a track at ~20 % → does not increment.
5. **Timeline + tooltip:** bars render; hover shows `Aug 23 · 0.2 hrs — <show>`.
6. **Top artists:** click a row → artist page when the catalog knows the name, search otherwise.
7. **Milestone:** cross a small threshold (scratch-build with a lowered threshold if needed) → amber line pulses once; relaunch → no re-pulse; the line persists.
8. **Grace:** scratch-build two listening nights with a skipped night between → run shows `◇` and a hollow timeline cell.
9. **full index ▸** → ArtistsPage grid, letters, filter all work; back returns to Home.
10. **Shelf:** recents + stash merge, no duplicate cards, cap 24.
11. **Sign out / in:** journal view clears on sign-out; returns on sign-in; `journal.json` still on disk under the account folder.
12. **Close mid-playback:** kill the app while playing → relaunch → journal shows the seconds credited before the kill (± 60 s).

- [ ] **Step 3: Fix-forward and commit any fixes**

```bash
git add -A
git commit -m "Journal verification pass: fixes from end-to-end run"
```
(only if something needed fixing)

- [ ] **Step 4: Final commit check**

```bash
git log --oneline -10
git status
```
Expected: clean tree; commits follow the plan.
