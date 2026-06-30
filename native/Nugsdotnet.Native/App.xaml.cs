using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Nugsdotnet.Native.Core;
using Nugsdotnet.Native.Imaging;
using Nugsdotnet.Native.Playback;
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

        // One HttpClient shared across auth, catalog, stream-resolve, and the
        // audio range reads (matches the original's single typed-client story).
        sc.AddSingleton(_ => new HttpClient());

        // Core services (reimplemented, no dependency on the original project).
        sc.AddSingleton<NugsSessionStore>();
        sc.AddSingleton<NugsAuth>();
        sc.AddSingleton<NugsCatalog>();
        sc.AddSingleton<NugsStreamResolver>();
        sc.AddSingleton<ImageLoader>();

        // Playback + view models.
        sc.AddSingleton<PlayerService>();
        sc.AddSingleton<ShellViewModel>();
        sc.AddTransient<LoginViewModel>();
        sc.AddTransient<HomeViewModel>();
        sc.AddTransient<SearchResultsViewModel>();
        sc.AddTransient<ArtistViewModel>();
        sc.AddTransient<AlbumViewModel>();

        return sc.BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
