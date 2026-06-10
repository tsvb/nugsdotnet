using Nugsdotnet.UI.Services;

namespace Nugsdotnet.Client.Services;

/// <summary>
/// Web-head media URLs: same-origin relative paths served by the host Server.
/// </summary>
public sealed class WebMediaUrls : IMediaUrls
{
    public string PlayUrl(string trackId) => $"/api/play/{trackId}";

    public string ImageUrl(string relativePath) =>
        $"/api/image?path={Uri.EscapeDataString(relativePath)}";
}
