using System.Text.Json;

namespace Nugsdotnet.Native.Core;

/// <summary>One stashed album/show — a card on the Home dashboard's STASH rail.</summary>
public sealed record StashEntry(
    string ContainerId, string? Title, string? Artist, string? Date, string? Venue,
    string? ImagePath, DateTimeOffset AddedAt);

/// <summary>
/// File-backed local stash (favorites). nugs' legacy API exposes no
/// favorites/stash surface (verified against the community clients), so this is
/// local-first by design: newest-added first, no cap — it's the user's library,
/// not a rail buffer. Files live under
/// %LOCALAPPDATA%\nugsdotnet\accounts\{userId}\stash.json so two nugs logins on
/// the same Windows profile do not share a stash. Same locking discipline as
/// the other stores.
/// </summary>
public sealed class StashStore
{
    private readonly AccountLocalStore? _accounts;
    private readonly string? _fixedPath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>Account-scoped store used by the app. Empty until Bind.</summary>
    public StashStore(AccountLocalStore accounts) => _accounts = accounts;

    /// <summary>Fixed path — tests and one-off tools.</summary>
    public StashStore(string path)
    {
        _fixedPath = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    }

    private string? ResolvePath() => _fixedPath ?? _accounts?.File(NugsLocalPaths.StashFileName);

    public async Task<IReadOnlyList<StashEntry>> LoadAsync(CancellationToken ct = default)
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

    public async Task<bool> ContainsAsync(string containerId, CancellationToken ct = default)
    {
        var entries = await LoadAsync(ct);
        return entries.Any(e => e.ContainerId == containerId);
    }

    /// <summary>
    /// Adds the entry (front of the list) or removes it if its container is
    /// already stashed. Returns true when the container is stashed afterwards —
    /// the state actually on disk, so a failed write reports the old state
    /// rather than lighting the star for a change that never landed.
    /// </summary>
    public async Task<bool> ToggleAsync(StashEntry entry, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (ResolvePath() is null) return false;
            var (merged, stashed) = Toggle(await LoadUnlockedAsync(ct), entry);
            try
            {
                await WriteAtomicAsync(merged, ct);
                return stashed;
            }
            catch
            {
                return !stashed;   // write never landed — the disk still holds the old state
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Temp-file + rename swap. This store is the user's library (uncapped, not
    /// a regenerating rail buffer), so a crash mid-write must not be able to
    /// truncate it — the corrupt-file-loads-empty policy would then let the
    /// next toggle overwrite everything with a one-entry list.
    /// </summary>
    private async Task WriteAtomicAsync(List<StashEntry> entries, CancellationToken ct)
    {
        var path = ResolvePath() ?? throw new InvalidOperationException("stash is not bound to an account");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await AtomicFile.WriteAsync(path, JsonSerializer.SerializeToUtf8Bytes(entries), ct);
    }

    /// <summary>Pure toggle: remove when present, else insert front (newest first).</summary>
    public static (List<StashEntry> Entries, bool Stashed) Toggle(
        IReadOnlyList<StashEntry> existing, StashEntry entry)
    {
        var without = existing.Where(e => e.ContainerId != entry.ContainerId).ToList();
        if (without.Count < existing.Count) return (without, false);   // was stashed — removed
        without.Insert(0, entry);
        return (without, true);
    }

    private async Task<IReadOnlyList<StashEntry>> LoadUnlockedAsync(CancellationToken ct)
    {
        try
        {
            var path = ResolvePath();
            if (path is null || !File.Exists(path)) return Array.Empty<StashEntry>();
            await using var fs = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<List<StashEntry>>(fs, cancellationToken: ct)
                ?? (IReadOnlyList<StashEntry>)Array.Empty<StashEntry>();
        }
        catch
        {
            return Array.Empty<StashEntry>();   // corrupt/unreadable — start fresh
        }
    }
}
