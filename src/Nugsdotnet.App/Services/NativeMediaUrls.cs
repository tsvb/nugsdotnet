using Nugsdotnet.UI.Services;

namespace Nugsdotnet.App.Services;

/// <summary>
/// Native-head media URLs: absolute loopback addresses pointing at the
/// embedded Kestrel proxy. Reads the port at call time so there's no
/// startup-ordering dependency on when Kestrel finished binding.
/// </summary>
public sealed class NativeMediaUrls : IMediaUrls
{
    private readonly LoopbackServer _loopback;

    public NativeMediaUrls(LoopbackServer loopback) => _loopback = loopback;

    public string PlayUrl(string trackId) =>
        $"http://127.0.0.1:{_loopback.Port}/api/play/{trackId}";

    public string ImageUrl(string relativePath) =>
        $"http://127.0.0.1:{_loopback.Port}/api/image?path={Uri.EscapeDataString(relativePath)}";
}
