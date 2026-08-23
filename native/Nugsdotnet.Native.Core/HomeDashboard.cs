namespace Nugsdotnet.Native.Core;

/// <summary>
/// Pure layout helpers for the Home dashboard. No I/O — greeting, rail
/// subtitles, A–Z index, artist filter, and the "your artists" derivation
/// all live here so the WinUI page stays a thin binding surface.
/// </summary>
public static class HomeDashboard
{
    /// <summary>Newest stashed items on the home rail; the store itself is uncapped.</summary>
    public const int StashRailCap = 24;

    /// <summary>Personal-artist chips derived from recents + stash.</summary>
    public const int YourArtistsCap = 12;

    public static string GreetingFor(int hour) => hour switch
    {
        >= 5 and < 12 => "GOOD MORNING",
        >= 12 and < 18 => "GOOD AFTERNOON",
        _ => "GOOD EVENING",
    };

    /// <summary>"date · venue · artist" with blanks skipped — live-show metadata
    /// on a rail card. Null when every part is empty.</summary>
    public static string? RailSubtitle(string? date, string? venue, string? artist)
    {
        var parts = new[] { date, venue, artist }.Where(s => !string.IsNullOrWhiteSpace(s));
        var joined = string.Join("  ·  ", parts);
        return joined.Length == 0 ? null : joined;
    }

    /// <summary>
    /// Index bucket for an artist name. A–Z for letters (case-insensitive),
    /// <c>#</c> for digits and symbols. Null when the name is blank.
    /// </summary>
    public static string? IndexLetter(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var c = name.Trim()[0];
        return char.IsLetter(c) ? char.ToUpperInvariant(c).ToString() : "#";
    }

    /// <summary>Distinct index letters actually present, <c>#</c> first, then A–Z.</summary>
    public static IReadOnlyList<string> LettersFor(IEnumerable<ArtistEntry> artists)
    {
        var set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var a in artists)
        {
            if (IndexLetter(a.Name) is { } letter)
                set.Add(letter);
        }
        return set.ToList();
    }

    /// <summary>Letter bucket AND substring filter. Both optional.</summary>
    public static IEnumerable<ArtistEntry> FilterArtists(
        IEnumerable<ArtistEntry> artists, string? query, string? letter)
    {
        IEnumerable<ArtistEntry> q = artists;
        if (!string.IsNullOrWhiteSpace(letter))
            q = q.Where(a => IndexLetter(a.Name) == letter);
        if (!string.IsNullOrWhiteSpace(query))
            q = q.Where(a => a.Name.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase));
        return q;
    }

    public static string ArtistsLabel(int shown, int total, bool filtered) =>
        total == 0 ? "ARTISTS"
        : filtered && shown != total ? $"ARTISTS · {shown} OF {total}"
        : $"ARTISTS · {total}";

    public static string StashLabel(int count) =>
        count > 0 ? $"STASH · {count}" : "STASH";

    /// <summary>
    /// Artists that appear in recents then stash, matched to the catalog by
    /// name (case-insensitive). Newest-first, deduped by id, capped.
    /// Unmatched names are skipped — recents can predate a catalog rename.
    /// </summary>
    public static List<ArtistEntry> YourArtists(
        IEnumerable<string?> recentArtists,
        IEnumerable<string?> stashArtists,
        IReadOnlyList<ArtistEntry> catalog,
        int cap = YourArtistsCap)
    {
        var byName = new Dictionary<string, ArtistEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in catalog)
        {
            if (!byName.ContainsKey(a.Name))
                byName[a.Name] = a;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<ArtistEntry>();
        foreach (var raw in recentArtists.Concat(stashArtists))
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            if (!byName.TryGetValue(raw.Trim(), out var entry)) continue;
            if (!seen.Add(entry.Id)) continue;
            list.Add(entry);
            if (list.Count >= cap) break;
        }
        return list;
    }

    /// <summary>Faceplate cue above the continue hero.</summary>
    public static string ContinueCue(bool isPlaying) => isPlaying ? "ON THE DECK" : "CONTINUE";

    /// <summary>Matches the transport clock: minutes can run past 60.</summary>
    public static string FormatClock(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) seconds = 0;
        var ts = TimeSpan.FromSeconds(seconds);
        return $"{(int)ts.TotalMinutes}:{ts.Seconds:00}";
    }

    /// <summary>
    /// Position line under the continue title. Null when there's nothing useful
    /// to print (no duration yet and no restore prompt).
    /// </summary>
    public static string? ContinuePosition(double positionSeconds, double durationSeconds, bool primedPaused)
    {
        if (durationSeconds > 0)
            return $"{FormatClock(positionSeconds)} / {FormatClock(durationSeconds)}";
        if (primedPaused) return "press play to resume";
        if (positionSeconds > 0) return FormatClock(positionSeconds);
        return null;
    }
}
