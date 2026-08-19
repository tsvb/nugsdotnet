using System.Text.Json;

namespace Nugsdotnet.Native.Core;

/// <summary>One stashed album/show — a card on the Home dashboard's STASH rail.</summary>
public sealed record StashEntry(
    string ContainerId, string? Title, string? Artist, string? Date, string? Venue,
    string? ImagePath, DateTimeOffset AddedAt);

/// <summary>
/// File-backed local stash (favorites). nugs' legacy API exposes no
/// favorites/stash surface (verified against the community clients), so this is
/// local-first by design: plain JSON at %LOCALAPPDATA%\nugsdotnet\stash.json,
/// newest-added first, no cap — it's the user's library, not a rail buffer.
/// Same locking discipline as the other stores.
/// </summary>
public sealed class StashStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>Default location: %LOCALAPPDATA%\nugsdotnet\stash.json.</summary>
    public StashStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "nugsdotnet", "stash.json"))
    {
    }

    public StashStore(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
    }

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
        await AtomicFile.WriteAsync(_path, JsonSerializer.SerializeToUtf8Bytes(entries), ct);
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
            if (!File.Exists(_path)) return Array.Empty<StashEntry>();
            await using var fs = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<List<StashEntry>>(fs, cancellationToken: ct)
                ?? (IReadOnlyList<StashEntry>)Array.Empty<StashEntry>();
        }
        catch
        {
            return Array.Empty<StashEntry>();   // corrupt/unreadable — start fresh
        }
    }
}
