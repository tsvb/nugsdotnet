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

        return state with { TracksCompleted = state.TracksCompleted + tracksCompleted };
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
