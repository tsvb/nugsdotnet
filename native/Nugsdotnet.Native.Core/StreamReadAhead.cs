namespace Nugsdotnet.Native.Core;

/// <summary>
/// Serves sequential (and lightly random) range reads from 1 MB windows,
/// prefetching the next window so Media Foundation never waits on a CDN
/// GET at a window boundary. Shared across <c>HttpAudioStream</c> clones.
/// A short buffer is only returned at true EOF — IInputStream treats any
/// short read as end-of-stream, which would mute the player.
/// </summary>
public sealed class StreamReadAhead
{
    public const int DefaultWindowSize = 1024 * 1024;

    private readonly ByteRangeCache _cache;
    private readonly Func<ulong, ulong, CancellationToken, Task<byte[]>> _download;
    private readonly object _prefetchGate = new();
    private Task _prefetch = Task.CompletedTask;

    public StreamReadAhead(
        Func<ulong, ulong, CancellationToken, Task<byte[]>> download,
        int windowSize = DefaultWindowSize,
        int maxWindows = 3)
    {
        _download = download ?? throw new ArgumentNullException(nameof(download));
        _cache = new ByteRangeCache(windowSize, maxWindows);
    }

    /// <summary>The last kicked-off next-window fetch (tests await this).</summary>
    public Task PrefetchTask
    {
        get { lock (_prefetchGate) return _prefetch; }
    }

    public int WindowSize => _cache.WindowSize;

    /// <summary>
    /// Returns <paramref name="count"/> bytes at <paramref name="position"/>,
    /// spanning windows if needed. Shorter only when the file actually ends.
    /// </summary>
    public async Task<byte[]> ReadAsync(
        ulong position, uint count, ulong size, CancellationToken ct)
    {
        if (count == 0 || (size > 0 && position >= size)) return [];

        var remaining = size > 0 ? size - position : count;
        var want = (int)Math.Min(count, remaining);
        if (want <= 0) return [];

        var dest = new byte[want];
        var copied = 0;
        while (copied < want)
        {
            var slice = await ReadWindowSliceAsync(
                position + (ulong)copied, want - copied, size, ct).ConfigureAwait(false);
            if (slice.Length == 0) break;
            Buffer.BlockCopy(slice, 0, dest, copied, slice.Length);
            copied += slice.Length;
        }

        if (copied > 0)
        {
            var (wStart, _) = _cache.WindowFor(position + (ulong)copied - 1, size);
            KickPrefetch(wStart + (ulong)_cache.WindowSize, size);
        }

        if (copied == want) return dest;
        if (copied == 0) return [];
        var clipped = new byte[copied];
        Buffer.BlockCopy(dest, 0, clipped, 0, copied);
        return clipped;
    }

    private async Task<byte[]> ReadWindowSliceAsync(
        ulong position, int count, ulong size, CancellationToken ct)
    {
        var (wStart, wEnd) = _cache.WindowFor(position, size);
        var inWindow = (int)(wEnd - position + 1);
        if (inWindow <= 0) return [];
        var n = Math.Min(count, inWindow);

        if (_cache.TryRead(position, n, out var hit)) return hit;

        var downloaded = await _cache.GetOrFetchAsync(
            wStart, token => _download(wStart, wEnd, token), ct).ConfigureAwait(false);
        if (downloaded.Length == 0) return [];
        var rel = (int)(position - wStart);
        if (rel >= downloaded.Length) return [];
        n = Math.Min(n, downloaded.Length - rel);
        if (_cache.TryRead(position, n, out hit)) return hit;
        var slice = new byte[n];
        Buffer.BlockCopy(downloaded, rel, slice, 0, n);
        return slice;
    }

    private void KickPrefetch(ulong nextStart, ulong size)
    {
        if (size > 0 && nextStart >= size) return;
        if (_cache.HasWindow(nextStart)) return;
        var (wStart, wEnd) = _cache.WindowFor(nextStart, size);
        var task = PrefetchQuietly(wStart, wEnd);
        lock (_prefetchGate) _prefetch = task;
    }

    private async Task PrefetchQuietly(ulong wStart, ulong wEnd)
    {
        try
        {
            await _cache.GetOrFetchAsync(
                wStart, token => _download(wStart, wEnd, token), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch
        {
            // Best-effort: the next ReadAsync retries this window.
        }
    }
}
