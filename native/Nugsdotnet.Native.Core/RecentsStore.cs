using System.Text.Json;

namespace Nugsdotnet.Native.Core;

/// <summary>One album/show the user played — a card on the Home dashboard.</summary>
public sealed record RecentPlay(
    string ContainerId, string? Title, string? Artist, string? Date, string? Venue,
    string? ImagePath, DateTimeOffset PlayedAt);

/// <summary>
/// File-backed "recently played" list feeding the Home dashboard rail. Plain
/// JSON (titles and CDN art paths, nothing sensitive) at
/// %LOCALAPPDATA%\nugsdotnet\accounts\{userId}\recents.json — same locking
/// discipline as <see cref="NugsSessionStore"/>.
/// </summary>
public sealed class RecentsStore
{
    /// <summary>Rail length on the dashboard — also the persistence cap.</summary>
    public const int Cap = 12;

    private readonly AccountLocalStore? _accounts;
    private readonly string? _fixedPath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>Account-scoped store used by the app. Empty until Bind.</summary>
    public RecentsStore(AccountLocalStore accounts) => _accounts = accounts;

    /// <summary>Fixed path — tests and one-off tools.</summary>
    public RecentsStore(string path)
    {
        _fixedPath = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    }

    private string? ResolvePath() => _fixedPath ?? _accounts?.File(NugsLocalPaths.RecentsFileName);

    public async Task<IReadOnlyList<RecentPlay>> LoadAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            return await LoadUnlockedAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Records a play: front of the list, deduped by container, capped. Failures
    /// are swallowed — losing a recents entry must never break playback.
    /// </summary>
    public async Task RecordAsync(RecentPlay play, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var path = ResolvePath();
            if (path is null) return;
            var merged = Merge(await LoadUnlockedAsync(ct), play);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await AtomicFile.WriteAsync(path, JsonSerializer.SerializeToUtf8Bytes(merged), ct);
        }
        catch
        {
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Pure merge: newest first, replay moves to the front, capped.</summary>
    public static List<RecentPlay> Merge(IReadOnlyList<RecentPlay> existing, RecentPlay play)
    {
        var list = new List<RecentPlay>(Math.Min(existing.Count + 1, Cap)) { play };
        foreach (var p in existing)
        {
            if (p.ContainerId == play.ContainerId) continue;
            if (list.Count >= Cap) break;
            list.Add(p);
        }
        return list;
    }

    private async Task<IReadOnlyList<RecentPlay>> LoadUnlockedAsync(CancellationToken ct)
    {
        try
        {
            var path = ResolvePath();
            if (path is null || !File.Exists(path)) return Array.Empty<RecentPlay>();
            await using var fs = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<List<RecentPlay>>(fs, cancellationToken: ct)
                ?? (IReadOnlyList<RecentPlay>)Array.Empty<RecentPlay>();
        }
        catch
        {
            return Array.Empty<RecentPlay>();   // corrupt/unreadable — start fresh
        }
    }
}
