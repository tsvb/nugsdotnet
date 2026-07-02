using System.Text.Json;

namespace Nugsdotnet.Native.Core;

/// <summary>What survives a restart: the queue, where in it we were, and the
/// listening setup. Position is seconds into the current track.</summary>
public sealed record PlaybackSnapshot(
    IReadOnlyList<NowPlaying> Queue, int Index, double PositionSeconds,
    double Volume, bool IsMuted);

/// <summary>
/// File-backed playback state for resume-on-launch. Plain JSON (track ids and
/// titles, nothing sensitive) at %LOCALAPPDATA%\nugsdotnet\playback.json — same
/// locking discipline as the other stores. Has a synchronous save because the
/// final write happens in the window's Closed handler.
/// </summary>
public sealed class PlaybackStateStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>Default location: %LOCALAPPDATA%\nugsdotnet\playback.json.</summary>
    public PlaybackStateStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "nugsdotnet", "playback.json"))
    {
    }

    public PlaybackStateStore(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
    }

    /// <summary>Null when there's nothing to resume (missing/corrupt/empty queue).</summary>
    public async Task<PlaybackSnapshot?> LoadAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (!File.Exists(_path)) return null;
            await using var fs = File.OpenRead(_path);
            var snap = await JsonSerializer.DeserializeAsync<PlaybackSnapshot>(fs, cancellationToken: ct);
            return IsUsable(snap) ? snap : null;
        }
        catch
        {
            return null;   // corrupt/unreadable — nothing to resume
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Failures are swallowed — losing a resume point must never break playback.</summary>
    public async Task SaveAsync(PlaybackSnapshot snapshot, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await File.WriteAllBytesAsync(_path, JsonSerializer.SerializeToUtf8Bytes(snapshot), ct);
        }
        catch
        {
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Synchronous variant for app shutdown (the window's Closed handler).</summary>
    public void Save(PlaybackSnapshot snapshot)
    {
        _lock.Wait();
        try
        {
            File.WriteAllBytes(_path, JsonSerializer.SerializeToUtf8Bytes(snapshot));
        }
        catch
        {
        }
        finally
        {
            _lock.Release();
        }
    }

    private static bool IsUsable(PlaybackSnapshot? snap) =>
        snap is { Queue.Count: > 0 } && snap.Index >= 0 && snap.Index < snap.Queue.Count;
}
