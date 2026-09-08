using Nugsdotnet.Native.Core;

namespace Nugsdotnet.Native.Tests;

public class StreamReadAheadTests
{
    [Fact]
    public void WindowFor_aligns_and_clamps_to_size()
    {
        var cache = new ByteRangeCache(windowSize: 256);
        Assert.Equal((0UL, 255UL), cache.WindowFor(0, 10_000));
        Assert.Equal((0UL, 255UL), cache.WindowFor(255, 10_000));
        Assert.Equal((256UL, 511UL), cache.WindowFor(256, 10_000));
        Assert.Equal((0UL, 99UL), cache.WindowFor(0, 100));
        Assert.Equal((0UL, 255UL), cache.WindowFor(0, 0));
    }

    [Fact]
    public async Task Sequential_small_reads_hit_one_window_download()
    {
        var file = Bytes(1000);
        var (ahead, fetches) = Ahead(file, windowSize: 256);

        var pos = 0UL;
        for (var i = 0; i < 8; i++)
        {
            var got = await ahead.ReadAsync(pos, 16, (ulong)file.Length, CancellationToken.None);
            Assert.Equal(file.AsSpan((int)pos, 16).ToArray(), got);
            pos += 16;
        }

        Assert.Equal(1, fetches.Of(0));
        await ahead.PrefetchTask;
        Assert.Equal(1, fetches.Of(256));
    }

    [Fact]
    public async Task Read_at_window_boundary_uses_the_prefetched_window()
    {
        var file = Bytes(600);
        var (ahead, fetches) = Ahead(file, windowSize: 256);

        var first = await ahead.ReadAsync(0, 16, (ulong)file.Length, CancellationToken.None);
        Assert.Equal(file.AsSpan(0, 16).ToArray(), first);
        await ahead.PrefetchTask;

        var second = await ahead.ReadAsync(256, 16, (ulong)file.Length, CancellationToken.None);
        Assert.Equal(file.AsSpan(256, 16).ToArray(), second);
        Assert.Equal(1, fetches.Of(256));
    }

    [Fact]
    public async Task Concurrent_reads_of_the_same_window_download_once()
    {
        var file = Bytes(512);
        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var log = new FetchLog();

        var ahead = new StreamReadAhead(async (start, end, ct) =>
        {
            log.Record(start);
            if (start == 0) { started.TrySetResult(); await release.Task; }
            return Slice(file, start, end);
        }, windowSize: 256);

        var a = ahead.ReadAsync(0, 16, (ulong)file.Length, CancellationToken.None);
        var b = ahead.ReadAsync(32, 16, (ulong)file.Length, CancellationToken.None);
        await started.Task;
        release.SetResult();
        await Task.WhenAll(a, b);

        Assert.Equal(file.AsSpan(0, 16).ToArray(), await a);
        Assert.Equal(file.AsSpan(32, 16).ToArray(), await b);
        Assert.Equal(1, log.Of(0));
    }

    [Fact]
    public async Task Failed_download_is_retried_on_the_next_read()
    {
        var file = Bytes(64);
        var attempts = 0;
        var ahead = new StreamReadAhead(async (start, end, ct) =>
        {
            if (start != 0) return Slice(file, start, end);
            if (Interlocked.Increment(ref attempts) == 1)
                throw new InvalidOperationException("cdn blip");
            return Slice(file, start, end);
        }, windowSize: 32);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ahead.ReadAsync(0, 8, 64, CancellationToken.None));
        var got = await ahead.ReadAsync(0, 8, 64, CancellationToken.None);
        Assert.Equal(file.AsSpan(0, 8).ToArray(), got);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task Sequential_playback_fetches_each_window_once()
    {
        var file = Bytes(1024);
        var (ahead, fetches) = Ahead(file, windowSize: 256);
        var pos = 0UL;
        while (pos < (ulong)file.Length)
        {
            var got = await ahead.ReadAsync(pos, 16, (ulong)file.Length, CancellationToken.None);
            Assert.True(got.Length > 0);
            pos += (ulong)got.Length;
        }
        await ahead.PrefetchTask;
        Assert.Equal(1, fetches.Of(0));
        Assert.Equal(1, fetches.Of(256));
        Assert.Equal(1, fetches.Of(512));
        Assert.Equal(1, fetches.Of(768));
        Assert.Equal(0, fetches.Of(1024));
    }

    [Fact]
    public async Task Read_past_end_returns_empty()
    {
        var file = Bytes(100);
        var (ahead, _) = Ahead(file, windowSize: 256);
        Assert.Empty(await ahead.ReadAsync(100, 16, 100, CancellationToken.None));
        Assert.Empty(await ahead.ReadAsync(0, 0, 100, CancellationToken.None));
    }

    [Fact]
    public async Task Short_final_window_returns_only_remaining_bytes()
    {
        var file = Bytes(300);
        var (ahead, _) = Ahead(file, windowSize: 256);
        await ahead.ReadAsync(0, 1, 300, CancellationToken.None);
        await ahead.PrefetchTask;
        var tail = await ahead.ReadAsync(256, 100, 300, CancellationToken.None);
        Assert.Equal(44, tail.Length);
        Assert.Equal(file.AsSpan(256, 44).ToArray(), tail);
    }

    [Fact]
    public void TryRead_serves_the_middle_of_a_window()
    {
        var cache = new ByteRangeCache(windowSize: 32, maxWindows: 2);
        var data = Bytes(32);
        cache.Add(0, data);
        Assert.True(cache.TryRead(8, 4, out var slice));
        Assert.Equal(data.AsSpan(8, 4).ToArray(), slice);
        Assert.Equal(24, cache.AvailableFrom(8));
        Assert.False(cache.TryRead(30, 8, out _));
    }

    [Fact]
    public void Add_evicts_the_oldest_window()
    {
        var cache = new ByteRangeCache(windowSize: 8, maxWindows: 2);
        cache.Add(0, Bytes(8));
        cache.Add(8, Bytes(8));
        cache.Add(16, Bytes(8));
        Assert.False(cache.HasWindow(0));
        Assert.True(cache.HasWindow(8));
        Assert.True(cache.HasWindow(16));
    }

    [Fact]
    public async Task Spanning_request_stops_at_the_window_edge()
    {
        var file = Bytes(512);
        var (ahead, fetches) = Ahead(file, windowSize: 256);
        var got = await ahead.ReadAsync(240, 32, 512, CancellationToken.None);
        Assert.Equal(16, got.Length);
        Assert.Equal(file.AsSpan(240, 16).ToArray(), got);
        Assert.Equal(1, fetches.Of(0));
    }

    private static byte[] Bytes(int n)
    {
        var data = new byte[n];
        for (var i = 0; i < n; i++) data[i] = (byte)(i * 17 + 3);
        return data;
    }

    private static (StreamReadAhead Ahead, FetchLog Fetches) Ahead(byte[] file, int windowSize)
    {
        var log = new FetchLog();
        var ahead = new StreamReadAhead(async (start, end, ct) =>
        {
            log.Record(start);
            await Task.Yield();
            return Slice(file, start, end);
        }, windowSize);
        return (ahead, log);
    }

    private static byte[] Slice(byte[] file, ulong start, ulong end)
    {
        var s = (int)start;
        var len = (int)(end - start + 1);
        if (s >= file.Length) return [];
        len = Math.Min(len, file.Length - s);
        return file.AsSpan(s, len).ToArray();
    }

    private sealed class FetchLog
    {
        private readonly object _gate = new();
        private readonly List<ulong> _starts = [];
        public void Record(ulong start) { lock (_gate) _starts.Add(start); }
        public int Of(ulong start) { lock (_gate) return _starts.Count(s => s == start); }
    }
}
