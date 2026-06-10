using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Nugsdotnet.Core;
using Nugsdotnet.Core.Api;

namespace Nugsdotnet.App.Services;

/// <summary>
/// The embedded Kestrel host. The WebView's own network stack fetches audio
/// and images itself (it can't be served by in-process request interception —
/// WebView interceptors can't stream HTTP Range/206 media), so the proven
/// /api proxy runs here on a loopback port and the &lt;audio&gt;/&lt;img&gt;
/// elements point at it. Binds 127.0.0.1:0 and publishes the real port.
/// </summary>
public sealed class LoopbackServer : IAsyncDisposable
{
    private WebApplication? _app;

    /// <summary>The port Kestrel actually bound — read at URL-construction time.</summary>
    public int Port { get; private set; }

    public async Task StartAsync(string tokenPath)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddNugsCore(tokenPath);

        _app = builder.Build();
        _app.MapApi();
        await _app.StartAsync();

        var address = _app.Services
            .GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();
        Port = new Uri(address).Port;
    }

    /// <summary>
    /// Hands out the singletons built inside the loopback container so the
    /// MAUI container can register the very same instances — one TokenStore,
    /// one NugsClient pipeline, one StreamInspector across both worlds.
    /// </summary>
    public T GetService<T>() where T : notnull =>
        _app!.Services.GetRequiredService<T>();

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
