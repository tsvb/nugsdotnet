using System.Net.Http.Headers;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace Nugsdotnet.Native.Audio;

/// <summary>
/// Live I/O counters for one logical stream, shared across its clones (Media
/// Foundation reads through <see cref="HttpAudioStream.GetInputStreamAt"/> clones,
/// so per-instance counts would undercount). Reads happen on MF threads —
/// Interlocked all the way. Feeds the dashboard's file metrics.
/// </summary>
public sealed class StreamIoStats
{
    private long _bytesFetched;
    private long _rangeReads;

    public long BytesFetched => Interlocked.Read(ref _bytesFetched);
    public long RangeReads => Interlocked.Read(ref _rangeReads);

    internal void Record(long bytes)
    {
        Interlocked.Add(ref _bytesFetched, bytes);
        Interlocked.Increment(ref _rangeReads);
    }
}

/// <summary>
/// An <see cref="IRandomAccessStream"/> backed by HTTP Range requests against the
/// nugs CDN, injecting the required Referer + mobile User-Agent on every fetch.
///
/// This is the native, in-process replacement for the web head's loopback Kestrel
/// proxy: the WebView needed a server because JS can't set those headers and can't
/// stream Range/206 media, but a native MediaPlayer reads from any IRandomAccessStream
/// we hand it — so we do the ranged GETs ourselves and feed the bytes straight in.
///
/// Read-only. The CDN URL is signed and trusted (it came from nugs's subPlayer API).
/// </summary>
public sealed class HttpAudioStream : IRandomAccessStream
{
    private readonly HttpClient _http;
    private readonly Uri _uri;
    private readonly string _referer;
    private readonly string _userAgent;
    private ulong _size;
    private ulong _position;

    public string ContentType { get; }

    /// <summary>Shared with clones — one logical stream, one set of counters.</summary>
    public StreamIoStats Stats { get; }

    private HttpAudioStream(
        HttpClient http, Uri uri, string referer, string ua, ulong size, string contentType,
        StreamIoStats? stats = null)
    {
        _http = http;
        _uri = uri;
        _referer = referer;
        _userAgent = ua;
        _size = size;
        ContentType = contentType;
        Stats = stats ?? new StreamIoStats();
    }

    /// <summary>
    /// Probes the CDN with a one-byte ranged GET to learn the total size (from the
    /// 206 Content-Range) and the content type before any playback read happens.
    /// </summary>
    public static async Task<HttpAudioStream> CreateAsync(
        HttpClient http, string url, string referer, string ua, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Referer", referer);
        req.Headers.TryAddWithoutValidation("User-Agent", ua);
        req.Headers.Range = new RangeHeaderValue(0, 0);
        using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();

        ulong size = 0;
        if (res.StatusCode == System.Net.HttpStatusCode.PartialContent &&
            res.Content.Headers.ContentRange?.Length is long total)
        {
            size = (ulong)total;                          // 206: total from Content-Range
        }
        else if (res.Content.Headers.ContentLength is long len)
        {
            size = (ulong)len;                            // 200 with a declared length
        }

        var contentType = res.Content.Headers.ContentType?.MediaType ?? "audio/flac";

        if (size == 0)
        {
            // The range probe returned no usable length (e.g. a chunked 200).
            // Media Foundation needs a real Size up front, so ask once via HEAD.
            using var head = new HttpRequestMessage(HttpMethod.Head, url);
            head.Headers.TryAddWithoutValidation("Referer", referer);
            head.Headers.TryAddWithoutValidation("User-Agent", ua);
            using var headRes = await http.SendAsync(head, HttpCompletionOption.ResponseHeadersRead, ct);
            if (headRes.IsSuccessStatusCode && headRes.Content.Headers.ContentLength is long hlen && hlen > 0)
                size = (ulong)hlen;
        }

        if (size == 0)
        {
            // Surface a clear error rather than handing MediaPlayer a zero-length
            // stream, which it reports only as an opaque MediaFailed.
            throw new InvalidOperationException(
                "CDN reported no content length (no 206 Content-Range and no Content-Length).");
        }

        return new HttpAudioStream(http, new Uri(url), referer, ua, size, contentType);
    }

    public bool CanRead => true;
    public bool CanWrite => false;
    public ulong Position => _position;

    public ulong Size
    {
        get => _size;
        set => _size = value;
    }

    public IRandomAccessStream CloneStream() =>
        new HttpAudioStream(_http, _uri, _referer, _userAgent, _size, ContentType, Stats) { _position = _position };

    public IInputStream GetInputStreamAt(ulong position)
    {
        var clone = (HttpAudioStream)CloneStream();
        clone._position = position;
        return clone;
    }

    public IOutputStream GetOutputStreamAt(ulong position) =>
        throw new NotSupportedException("read-only stream");

    public void Seek(ulong position) => _position = position;

    public IAsyncOperationWithProgress<IBuffer, uint> ReadAsync(
        IBuffer buffer, uint count, InputStreamOptions options)
    {
        return AsyncInfo.Run<IBuffer, uint>(async (token, _) =>
        {
            var start = _position;
            if (_size > 0 && start >= _size) return Array.Empty<byte>().AsBuffer();

            var end = start + count - 1;
            if (_size > 0) end = Math.Min(end, _size - 1);

            using var req = new HttpRequestMessage(HttpMethod.Get, _uri);
            req.Headers.TryAddWithoutValidation("Referer", _referer);
            req.Headers.TryAddWithoutValidation("User-Agent", _userAgent);
            req.Headers.Range = new RangeHeaderValue((long)start, (long)end);
            using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token);

            // An over-read past the real end returns 416. The WinRT EOF signal is an
            // empty buffer, never a throw (which would surface as MediaFailed).
            if (res.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
                return Array.Empty<byte>().AsBuffer();
            res.EnsureSuccessStatusCode();

            // Learn the true size from the first ranged response if we didn't know it.
            if (res.Content.Headers.ContentRange?.Length is long total) _size = (ulong)total;

            var bytes = await res.Content.ReadAsByteArrayAsync(token);
            _position = start + (ulong)bytes.Length;
            Stats.Record(bytes.Length);
            return bytes.AsBuffer();
        });
    }

    public IAsyncOperationWithProgress<uint, uint> WriteAsync(IBuffer buffer) =>
        throw new NotSupportedException("read-only stream");

    public IAsyncOperation<bool> FlushAsync() =>
        throw new NotSupportedException("read-only stream");

    public void Dispose()
    {
        // The HttpClient is shared/owned by DI — nothing to release here.
    }
}
