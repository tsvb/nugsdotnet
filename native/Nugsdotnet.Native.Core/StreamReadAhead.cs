namespace Nugsdotnet.Native.Core;

/// <summary>
/// Serves sequential (and lightly random) range reads from 1 MB windows,
/// prefetching the next window so Media Foundation never waits on a CDN
/// GET at a window boundary. Shared across <c>HttpAudioStream</c> clones.
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
    /// Returns up to <paramref name="count"/> bytes at <paramref name="position"/>,
    /// never crossing a window boundary. A short buffer is EOF or a boundary;
    /// the caller should issue another read for the rest.
    /// </summary>
    public async Task<byte[]> ReadAsync(
        ulong position, uint count, ulong size, CancellationToken ct)
    {
        if (count == 0 || (size > 0 && position >= size)) return [];

        var remaining = size > 0 ? size - position : count;
        var want = (int)Math.Min(count, remaining);
        var (wStart, wEnd) = _cache.WindowFor(position, size);
        var inWindow = (int)(wEnd - position + 1);
        if (inWindow <= 0) return [];
        want = Math.Min(want, inWindow);

        if (!_cache.TryRead(position, want, out var bytes))
        {
            var downloaded = await _cache.GetOrFetchAsync(
                wStart, token => _download(wStart, wEnd, token), ct).ConfigureAwait(false);
            if (downloaded.Length == 0) return [];
            var rel = (int)(position - wStart);
            if (rel >= downloaded.Length) return [];
            want = Math.Min(want, downloaded.Length - rel);
            if (!_cache.TryRead(position, want, out bytes))
            {
                bytes = new byte[want];
                Buffer.BlockCopy(downloaded, rel, bytes, 0, want);
            }
        }

        KickPrefetch(wStart + (ulong)_cache.WindowSize, size);
        return bytes;
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
