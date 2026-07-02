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
    private readonly Dictionary<string, BitmapImage> _cache = new();
    private readonly Queue<string> _order = new();

    private readonly HttpClient _http;

    public ImageLoader(HttpClient http) => _http = http;

    /// <summary>
    /// Loads an image from an absolute URL or a catalog-relative "/images/…"
    /// path (resolved against the CDN with a 400px resize hint). Returns null on
    /// any failure so the UI just shows no art. UI thread only (BitmapImage) —
    /// which also makes the cache single-threaded.
    /// </summary>
    public async Task<BitmapImage?> LoadAsync(string? pathOrUrl)
    {
        if (string.IsNullOrEmpty(pathOrUrl)) return null;

        var url = pathOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? pathOrUrl
            : $"{NugsConstants.ImageCdnBase}{pathOrUrl}?h=400";
        if (_cache.TryGetValue(url, out var cached)) return cached;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", NugsConstants.MobileUserAgent);
            using var res = await _http.SendAsync(req);
            if (!res.IsSuccessStatusCode) return null;

            var bytes = await res.Content.ReadAsByteArrayAsync();
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
    }
}
