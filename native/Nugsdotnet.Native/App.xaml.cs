using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Nugsdotnet.Native.Core;
using Nugsdotnet.Native.Imaging;
using Nugsdotnet.Native.Playback;
using Nugsdotnet.Native.Services;
using Nugsdotnet.Native.ViewModels;

namespace Nugsdotnet.Native;

public partial class App : Application
{
    /// <summary>App-wide service container. Resolved by views/windows at construction.</summary>
    public static IServiceProvider Services { get; private set; } = default!;

    private Window? _window;

    public App()
    {
        InitializeComponent();
        Services = ConfigureServices();
    }

    private static IServiceProvider ConfigureServices()
    {
        var sc = new ServiceCollection();

        sc.AddSingleton(_ => new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = DecompressionMethods.All,
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            },
        })
        {
            Timeout = TimeSpan.FromSeconds(30),
            MaxResponseContentBufferSize = 8 * 1024 * 1024,
        });

        sc.AddSingleton<NugsSessionStore>();
        sc.AddSingleton<NugsAuth>();
        sc.AddSingleton<NugsCatalog>();
        sc.AddSingleton<NugsStreamResolver>();
        sc.AddSingleton<AccountLocalStore>();
        sc.AddSingleton<SettingsStore>();
        sc.AddSingleton<UpdateChecker>();
        sc.AddSingleton(sp => new RecentsStore(sp.GetRequiredService<AccountLocalStore>()));
        sc.AddSingleton(sp => new StashStore(sp.GetRequiredService<AccountLocalStore>()));
        sc.AddSingleton(sp => new PlaybackStateStore(sp.GetRequiredService<AccountLocalStore>()));
        sc.AddSingleton<ImageLoader>();

        sc.AddSingleton<PlayerService>();
        sc.AddSingleton<ShellViewModel>();
        sc.AddTransient<LoginViewModel>();
        sc.AddSingleton<HomeViewModel>();
        sc.AddTransient<SearchResultsViewModel>();
        sc.AddTransient<ArtistViewModel>();
        sc.AddTransient<AlbumViewModel>();
        sc.AddTransient<SettingsViewModel>();
        sc.AddTransient<StashViewModel>();

        return sc.BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
