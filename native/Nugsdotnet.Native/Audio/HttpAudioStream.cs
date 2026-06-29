using System.Net.Http.Headers;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace Nugsdotnet.Native.Audio;

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

    private HttpAudioStream(
        HttpClient http, Uri uri, string referer, string ua, ulong size, string contentType)
    {
        _http = http;
        _uri = uri;
        _referer = referer;
        _userAgent = ua;
        _size = size;
        ContentType = contentType;
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
        if (res.Content.Headers.ContentRange?.Length is long total) size = (ulong)total;
        else if (res.Content.Headers.ContentLength is long len) size = (ulong)len;

        var contentType = res.Content.Headers.ContentType?.MediaType ?? "audio/flac";
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
        new HttpAudioStream(_http, _uri, _referer, _userAgent, _size, ContentType) { _position = _position };

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
            res.EnsureSuccessStatusCode();

            var bytes = await res.Content.ReadAsByteArrayAsync(token);
            _position = start + (ulong)bytes.Length;
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
