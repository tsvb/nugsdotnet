namespace Nugsdotnet.Native.Core;

/// <summary>
/// A handful of aligned byte windows shared across stream clones. Media
/// Foundation issues tiny sequential reads; caching whole windows turns
/// those into memory hits instead of a CDN round-trip each time.
/// </summary>
internal sealed class ByteRangeCache
{
    private readonly object _gate = new();
    private readonly List<Window> _windows = [];
    private readonly Dictionary<ulong, Task<byte[]>> _inflight = [];

    public ByteRangeCache(int windowSize, int maxWindows = 3)
    {
        if (windowSize < 1) throw new ArgumentOutOfRangeException(nameof(windowSize));
        if (maxWindows < 1) throw new ArgumentOutOfRangeException(nameof(maxWindows));
        WindowSize = windowSize;
        MaxWindows = maxWindows;
    }

    public int WindowSize { get; }
    public int MaxWindows { get; }

    public ulong Align(ulong position) => position / (ulong)WindowSize * (ulong)WindowSize;

    /// <summary>Inclusive HTTP Range for the aligned window that contains
    /// <paramref name="position"/>, clamped to <paramref name="size"/> when known.</summary>
    public (ulong Start, ulong EndInclusive) WindowFor(ulong position, ulong size)
    {
        var start = Align(position);
        var end = start + (ulong)WindowSize - 1;
        if (size > 0) end = Math.Min(end, size - 1);
        return (start, end);
    }

    public bool TryRead(ulong position, int count, out byte[] data)
    {
        if (count <= 0)
        {
            data = [];
            return true;
        }
        lock (_gate)
        {
            foreach (var w in _windows)
            {
                if (position < w.Offset) continue;
                var rel = position - w.Offset;
                if (rel >= (ulong)w.Data.Length) continue;
                if (rel + (ulong)count > (ulong)w.Data.Length) continue;
                data = new byte[count];
                Buffer.BlockCopy(w.Data, (int)rel, data, 0, count);
                return true;
            }
        }
        data = [];
        return false;
    }

    public int AvailableFrom(ulong position)
    {
        lock (_gate)
        {
            foreach (var w in _windows)
            {
                if (position < w.Offset) continue;
                var rel = position - w.Offset;
                if (rel >= (ulong)w.Data.Length) continue;
                return w.Data.Length - (int)rel;
            }
        }
        return 0;
    }

    public bool HasWindow(ulong windowStart)
    {
        lock (_gate)
        {
            foreach (var w in _windows)
                if (w.Offset == windowStart) return true;
        }
        return false;
    }

    public void Add(ulong offset, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0) return;
        lock (_gate) AddUnlocked(offset, data);
    }

    /// <summary>One in-flight download per window start so clones don't stampede the CDN.</summary>
    public Task<byte[]> GetOrFetchAsync(
        ulong windowStart, Func<CancellationToken, Task<byte[]>> fetch, CancellationToken ct)
    {
        TaskCompletionSource<byte[]>? created = null;
        lock (_gate)
        {
            foreach (var w in _windows)
                if (w.Offset == windowStart) return Task.FromResult(w.Data);
            if (_inflight.TryGetValue(windowStart, out var pending))
                return pending;
            created = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            _inflight[windowStart] = created.Task;
        }
        return CompleteFetch(created, windowStart, fetch, ct);
    }

    private async Task<byte[]> CompleteFetch(
        TaskCompletionSource<byte[]> created,
        ulong windowStart,
        Func<CancellationToken, Task<byte[]>> fetch,
        CancellationToken ct)
    {
        try
        {
            var data = await fetch(ct).ConfigureAwait(false);
            if (data.Length > 0) Add(windowStart, data);
            created.SetResult(data);
            return data;
        }
        catch (Exception ex)
        {
            created.TrySetException(ex);
            throw;
        }
        finally
        {
            lock (_gate) _inflight.Remove(windowStart);
        }
    }

    private void AddUnlocked(ulong offset, byte[] data)
    {
        for (var i = 0; i < _windows.Count; i++)
        {
            if (_windows[i].Offset != offset) continue;
            _windows.RemoveAt(i);
            break;
        }
        _windows.Add(new Window(offset, data));
        while (_windows.Count > MaxWindows)
            _windows.RemoveAt(0);
    }

    private sealed record Window(ulong Offset, byte[] Data);
}
