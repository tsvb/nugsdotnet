using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;
using Nugsdotnet.Native.Core;

namespace Nugsdotnet.Native.Imaging;

/// <summary>
/// Fetches catalog artwork from the nugs CDN with the required mobile UA and
/// decodes it into a BitmapImage. No proxy needed — a native app carries no
/// nugs.net cookies, so there's nothing to strip. Call from the UI thread (it
/// builds a BitmapImage); page/view-model code awaits it on the UI context.
/// </summary>
public sealed class ImageLoader
{
    // Same art repeats across the dashboard, transport, and pages — keep decoded
    // bitmaps for the session. ~200 thumbnails ≈ a few tens of MB, well under an
    // album-art-heavy browser tab. Evicts oldest-inserted beyond the cap.
    private const int CacheCap = 200;
    private const int MaxBytes = 4 * 1024 * 1024;
    private readonly Dictionary<string, BitmapImage> _cache = new();
    private readonly Dictionary<string, Task<BitmapImage?>> _inflight = new();
    private readonly Queue<string> _order = new();

    private readonly HttpClient _http;

    public ImageLoader(HttpClient http) => _http = http;

    /// <summary>
    /// Loads an image from an absolute URL or a catalog-relative "/images/…"
    /// path (resolved against the CDN with a 400px resize hint). Returns null on
    /// any failure so the UI just shows no art. UI thread only (BitmapImage) —
    /// which also makes the cache single-threaded.
    /// </summary>
    public Task<BitmapImage?> LoadAsync(string? pathOrUrl)
    {
        if (string.IsNullOrEmpty(pathOrUrl)) return Task.FromResult<BitmapImage?>(null);

        var url = NugsUri.ResolveImageUrl(pathOrUrl);
        if (url is null) return Task.FromResult<BitmapImage?>(null);

        if (_cache.TryGetValue(url, out var cached)) return Task.FromResult<BitmapImage?>(cached);
        if (_inflight.TryGetValue(url, out var pending)) return pending;

        var task = LoadUncachedAsync(url);
        _inflight[url] = task;
        return task;
    }

    private async Task<BitmapImage?> LoadUncachedAsync(string url)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", NugsConstants.MobileUserAgent);
            using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            if (!res.IsSuccessStatusCode) return null;
            if (res.Content.Headers.ContentLength is long declared && declared > MaxBytes)
                return null;

            var bytes = await ReadAtMostAsync(res, MaxBytes);
            if (bytes is null || bytes.Length == 0) return null;

            var bmp = new BitmapImage();
            using var ms = new InMemoryRandomAccessStream();
            await ms.WriteAsync(bytes.AsBuffer());
            ms.Seek(0);
            await bmp.SetSourceAsync(ms);

            if (_cache.Count >= CacheCap && _order.TryDequeue(out var oldest))
                _cache.Remove(oldest);
            if (_cache.TryAdd(url, bmp)) _order.Enqueue(url);   // awaits may interleave loads
            return bmp;
        }
        catch
        {
            return null;
        }
        finally
        {
            _inflight.Remove(url);
        }
    }

    private static async Task<byte[]?> ReadAtMostAsync(HttpResponseMessage res, int max)
    {
        await using var stream = await res.Content.ReadAsStreamAsync();
        using var ms = new MemoryStream(capacity: Math.Min(max, 64 * 1024));
        var buf = new byte[8192];
        var total = 0;
        while (true)
        {
            var n = await stream.ReadAsync(buf);
            if (n == 0) break;
            total += n;
            if (total > max) return null;
            ms.Write(buf, 0, n);
        }
        return ms.ToArray();
    }
}
