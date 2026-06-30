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
    private readonly HttpClient _http;

    public ImageLoader(HttpClient http) => _http = http;

    /// <summary>
    /// Loads an image from an absolute URL or a catalog-relative "/images/…"
    /// path (resolved against the CDN with a 400px resize hint). Returns null on
    /// any failure so the UI just shows no art.
    /// </summary>
    public async Task<BitmapImage?> LoadAsync(string? pathOrUrl)
    {
        if (string.IsNullOrEmpty(pathOrUrl)) return null;

        var url = pathOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? pathOrUrl
            : $"{NugsConstants.ImageCdnBase}{pathOrUrl}?h=400";
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
            return bmp;
        }
        catch
        {
            return null;
        }
    }
}
