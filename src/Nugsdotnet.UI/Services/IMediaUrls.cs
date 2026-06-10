namespace Nugsdotnet.UI.Services;

/// <summary>
/// Resolves the URLs the WebView's own network stack fetches (the &lt;audio&gt;
/// element and &lt;img&gt; tags). The web head returns same-origin relative
/// paths; the native head returns absolute loopback URLs pointing at the
/// embedded Kestrel proxy.
/// </summary>
public interface IMediaUrls
{
    string PlayUrl(string trackId);
    string ImageUrl(string relativePath);
}
