using System.Runtime.ExceptionServices;

namespace Nugsdotnet.Native.Core;

/// <summary>
/// Streaming read-ahead for HTTP range sources. Media Foundation issues 16–64 KB
/// reads from several clones at once; a short IInputStream buffer is EOF (mute),
/// and waiting on a full 1 MB GET before returning any bytes underruns the
/// renderer. Chunks fill progressively, two downloads run at a time, and a few
/// megabytes stay ahead of each read so playback does not stall at a boundary.
/// </summary>
public sealed class StreamReadAhead
{
    public const int DefaultChunkSize = 256 * 1024;

    private readonly Func<ulong, ulong, Action<ReadOnlyMemory<byte>>, CancellationToken, Task> _download;
    private readonly object _gate = new();
    private readonly Dictionary<ulong, Chunk> _chunks = [];
    private readonly SemaphoreSlim _slots = new(2);
    private long _lru;

    public StreamReadAhead(
        Func<ulong, ulong, Action<ReadOnlyMemory<byte>>, CancellationToken, Task> download,
        int chunkSize = DefaultChunkSize,
        int? targetAhead = null,
        int? maxBytes = null)
    {
        ArgumentNullException.ThrowIfNull(download);
        if (chunkSize < 1) throw new ArgumentOutOfRangeException(nameof(chunkSize));
        _download = download;
        ChunkSize = chunkSize;
        TargetAhead = targetAhead ?? 8 * chunkSize;
        MaxBytes = maxBytes ?? 16 * chunkSize;
    }

    public int ChunkSize { get; }
    public int TargetAhead { get; }
    public int MaxBytes { get; }

    /// <summary>Completes when every chunk started so far has finished filling.</summary>
    public Task PrefetchTask
    {
        get
        {
            Chunk[] chunks;
            lock (_gate) chunks = _chunks.Values.ToArray();
            return Task.WhenAll(chunks.Select(c => c.Done));
        }
    }

    public ulong Align(ulong position) => position / (ulong)ChunkSize * (ulong)ChunkSize;

    public async Task WarmAsync(ulong size, CancellationToken ct = default)
    {
        EnsureAhead(0, size);
        var chunk = GetOrStartChunk(0, size);
        if (chunk is null) return;
        await chunk.WaitUntilAsync(Math.Min(64 * 1024, chunk.Capacity), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns <paramref name="count"/> bytes at <paramref name="position"/>,
    /// spanning chunks if needed. Shorter only when the file actually ends.
    /// </summary>
    public async Task<byte[]> ReadAsync(
        ulong position, uint count, ulong size, CancellationToken ct)
    {
        if (count == 0 || (size > 0 && position >= size)) return [];

        var remaining = size > 0 ? size - position : count;
        var want = (int)Math.Min(count, remaining);
        if (want <= 0) return [];

        EnsureAhead(position, size);

        var dest = new byte[want];
        var copied = 0;
        var faults = 0;
        while (copied < want)
        {
            var pos = position + (ulong)copied;
            if (size > 0 && pos >= size) break;
            var chunk = GetOrStartChunk(Align(pos), size);
            if (chunk is null) break;
            var rel = (int)(pos - chunk.Offset);
            await chunk.WaitUntilAsync(rel + 1, ct).ConfigureAwait(false);
            if (chunk.Error is { } ex)
            {
                if (++faults > 3) ExceptionDispatchInfo.Throw(ex);
                continue;
            }
            var n = chunk.Copy(rel, dest.AsSpan(copied));
            if (n == 0)
            {
                if (chunk.IsCompleted) break;
                continue;
            }
            copied += n;
            EnsureAhead(position + (ulong)copied, size);
        }

        if (copied == want) return dest;
        if (copied == 0) return [];
        var clipped = new byte[copied];
        Buffer.BlockCopy(dest, 0, clipped, 0, copied);
        return clipped;
    }

    private void EnsureAhead(ulong position, ulong size)
    {
        List<(Chunk Chunk, ulong End)> started;
        lock (_gate)
        {
            started = [];
            var until = position + (ulong)TargetAhead;
            if (size > 0) until = Math.Min(until, size);
            for (var off = Align(position); off < until; off += (ulong)ChunkSize)
            {
                if (TotalBytesUnlocked() >= MaxBytes && !_chunks.ContainsKey(off)) break;
                var (_, created) = GetOrStartChunkUnlocked(off, size);
                if (created is { } start)
                    started.Add(start);
            }
            EvictUnlocked(position);
        }
        foreach (var (chunk, end) in started)
            _ = FillAsync(chunk, end);
    }

    private Chunk? GetOrStartChunk(ulong chunkStart, ulong size)
    {
        (Chunk Chunk, ulong End)? created;
        Chunk? chunk;
        lock (_gate)
        {
            (chunk, created) = GetOrStartChunkUnlocked(chunkStart, size);
        }
        if (created is { } start)
            _ = FillAsync(start.Chunk, start.End);
        return chunk;
    }

    /// <summary>When a new chunk is created, the caller must start <see cref="FillAsync"/>
    /// after releasing <see cref="_gate"/> so a sync download cannot deadlock.</summary>
    private (Chunk? Chunk, (Chunk Chunk, ulong End)? Created) GetOrStartChunkUnlocked(
        ulong chunkStart, ulong size)
    {
        if (size > 0 && chunkStart >= size) return (null, null);
        if (_chunks.TryGetValue(chunkStart, out var existing))
        {
            if (existing.Error is null)
            {
                existing.Touch(Interlocked.Increment(ref _lru));
                return (existing, null);
            }
            _chunks.Remove(chunkStart);
        }
        var len = ChunkSize;
        if (size > 0) len = (int)Math.Min((ulong)ChunkSize, size - chunkStart);
        if (len <= 0) return (null, null);
        var chunk = new Chunk(chunkStart, len, Interlocked.Increment(ref _lru));
        _chunks[chunkStart] = chunk;
        var end = chunkStart + (ulong)len - 1;
        return (chunk, (chunk, end));
    }

    private async Task FillAsync(Chunk chunk, ulong end)
    {
        await _slots.WaitAsync().ConfigureAwait(false);
        try
        {
            await _download(chunk.Offset, end, mem => chunk.Append(mem.Span), CancellationToken.None)
                .ConfigureAwait(false);
            chunk.Complete();
        }
        catch (Exception ex)
        {
            chunk.Complete(ex);
        }
        finally
        {
            _slots.Release();
        }
    }

    private int TotalBytesUnlocked()
    {
        var total = 0;
        foreach (var c in _chunks.Values) total += c.Capacity;
        return total;
    }

    private void EvictUnlocked(ulong position)
    {
        var protectFrom = position > (ulong)ChunkSize ? position - (ulong)ChunkSize : 0UL;
        var protectTo = position + (ulong)TargetAhead;
        while (TotalBytesUnlocked() > MaxBytes)
        {
            Chunk? victim = null;
            foreach (var c in _chunks.Values)
            {
                if (!c.IsCompleted) continue;
                var end = c.Offset + (ulong)c.Capacity;
                if (end > protectFrom && c.Offset < protectTo) continue;
                if (victim is null || c.Lru < victim.Lru) victim = c;
            }
            if (victim is null) break;
            _chunks.Remove(victim.Offset);
        }
    }

    private sealed class Chunk
    {
        private readonly object _gate = new();
        private TaskCompletionSource _pulse = NewPulse();
        private readonly TaskCompletionSource _done = NewPulse();
        private Exception? _error;
        private int _filled;

        public Chunk(ulong offset, int capacity, long lru)
        {
            Offset = offset;
            Data = new byte[capacity];
            Lru = lru;
        }

        public ulong Offset { get; }
        public byte[] Data { get; }
        public int Capacity => Data.Length;
        public long Lru { get; private set; }
        public bool IsCompleted { get; private set; }
        public Exception? Error { get { lock (_gate) return _error; } }
        public Task Done => _done.Task;

        public void Touch(long lru) => Lru = lru;

        public void Append(ReadOnlySpan<byte> src)
        {
            lock (_gate)
            {
                var n = Math.Min(src.Length, Data.Length - _filled);
                if (n <= 0) return;
                src[..n].CopyTo(Data.AsSpan(_filled));
                _filled += n;
                Pulse();
            }
        }

        public int Copy(int rel, Span<byte> dest)
        {
            lock (_gate)
            {
                if (rel < 0 || rel >= _filled) return 0;
                var n = Math.Min(dest.Length, _filled - rel);
                Data.AsSpan(rel, n).CopyTo(dest);
                return n;
            }
        }

        public async Task WaitUntilAsync(int need, CancellationToken ct)
        {
            while (true)
            {
                Task wait;
                lock (_gate)
                {
                    if (_error is not null || _filled >= need || IsCompleted) return;
                    wait = _pulse.Task;
                }
                await wait.WaitAsync(ct).ConfigureAwait(false);
            }
        }

        public void Complete(Exception? error = null)
        {
            lock (_gate)
            {
                _error = error;
                IsCompleted = true;
                Pulse();
                _done.TrySetResult();
            }
        }

        private void Pulse()
        {
            var old = _pulse;
            _pulse = NewPulse();
            old.TrySetResult();
        }

        private static TaskCompletionSource NewPulse() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
