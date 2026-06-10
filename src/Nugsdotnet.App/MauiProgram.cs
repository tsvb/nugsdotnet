using Microsoft.Extensions.Logging;
using Nugsdotnet.App.Services;
using Nugsdotnet.Core.Nugs;
using Nugsdotnet.UI.Services;

namespace Nugsdotnet.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        // 1. Start the embedded loopback Kestrel (audio/image proxy) first.
        //    Localhost binding completes in tens of milliseconds; doing it
        //    synchronously here keeps the composition simple.
        var tokenPath = Path.Combine(FileSystem.AppDataDirectory, "tokens.json");
        var loopback = new LoopbackServer();
        loopback.StartAsync(tokenPath).GetAwaiter().GetResult();

        // 2. Pull the shared singletons out of the loopback container so the
        //    MAUI container holds the exact same instances (one token state,
        //    one stream-pick cache across both DI worlds).
        var nugsClient = loopback.GetService<NugsClient>();
        var tokenStore = loopback.GetService<TokenStore>();
        var inspector = loopback.GetService<StreamInspector>();

        // 3. Build the MAUI app around them.
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddMauiBlazorWebView();
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        builder.Services.AddSingleton(loopback);
        builder.Services.AddSingleton(nugsClient);
        builder.Services.AddSingleton(tokenStore);
        builder.Services.AddSingleton(inspector);

        // Native implementations of the UI's abstractions.
        builder.Services.AddSingleton<INugsGateway, NugsGatewayDirect>();
        builder.Services.AddSingleton<IMediaUrls, NativeMediaUrls>();

        // UI services. Singleton = app lifetime (matches the WASM heads'
        // effective lifetimes); DashboardState is scoped to the BlazorWebView
        // circuit, mirroring its per-page-load semantics on the web.
        builder.Services.AddSingleton<PlayerService>();
        builder.Services.AddSingleton<CatalogCache>();
        builder.Services.AddScoped<DashboardState>();

        return builder.Build();
    }
}
