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
    public string HoursLabel => $"{JournalMath.FormatHours(Seconds)} h";
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
            var gap = day.DayNumber - cursor.DayNumber;
            if (gap == 1) { cursor = day; continue; }
            if (gap == 2) { grace++; cursor = day; continue; }
            runs.Add(Span(start, cursor, grace));
            start = cursor = day;
            grace = 0;
        }
        runs.Add(Span(start, cursor, grace));

        var best = runs.Max(r => r.Best);
        var last = runs[^1];
        var fresh = today.DayNumber - listening[^1].DayNumber <= 1;
        return new RunInfo(fresh ? last.Current : 0, best, fresh ? last.GraceNights : 0);
    }

    /// <summary>Inclusive night count from <paramref name="start"/> to <paramref name="end"/>.</summary>
    private static RunInfo Span(DateOnly start, DateOnly end, int grace) =>
        new(end.DayNumber - start.DayNumber + 1, end.DayNumber - start.DayNumber + 1, grace);

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
        return $"{date.ToString("MMM d", CultureInfo.InvariantCulture)} · {FormatHours(day.Seconds)} hrs — {string.Join(", ", names)}{tail}";
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
        // Seconds desc, then name asc so equal totals rank deterministically
        // (dictionary order is hash-dependent and must never leak into ranks).
        var leaders = totals.OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Take(top)
            .ToList();
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
