using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Nugsdotnet.Native.Playback;
using Nugsdotnet.Native.Services;
using Nugsdotnet.Native.ViewModels;
using Nugsdotnet.Native.Views;
using Nugsdotnet.Native.Views.Pages;
using Windows.Graphics;
using Windows.System;
using Windows.UI;

namespace Nugsdotnet.Native;

public sealed partial class MainWindow : Window
{
    private readonly ShellViewModel _shell;
    private readonly PlayerService _player;
    private readonly SettingsStore _settings;
    private readonly UpdateChecker _updates;
    private bool _restoreDashboard;

    public MainWindow()
    {
        InitializeComponent();
        Title = "nugsdotnet";

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarStrip);
        BrandTitleBar();
        RestoreWindowState();

        _shell = App.Services.GetRequiredService<ShellViewModel>();
        _player = App.Services.GetRequiredService<PlayerService>();
        _settings = App.Services.GetRequiredService<SettingsStore>();
        _updates = App.Services.GetRequiredService<UpdateChecker>();
        DataContext = _shell;

        Transport.AlbumRequested += id => ContentFrame.Navigate(typeof(AlbumPage), id);
        LoginPanel.LoggedIn += async (_, _) =>
        {
            await _shell.InitializeAsync();
            _player.Volume = _settings.Current.DefaultVolume;
            await _player.RestoreAsync();
            ShowMain();
        };
        Closed += (_, _) => SaveWindowState();
        ShowLogin();
        _ = InitializeAsync();
    }

    private void RestoreWindowState()
    {
        if (WindowStateStore.TryLoad() is not { } ws) return;
        _restoreDashboard = ws.DashboardOpen;

        var area = DisplayArea.GetFromPoint(new PointInt32(ws.X, ws.Y), DisplayAreaFallback.Nearest);
        var work = area.WorkArea;
        var w = Math.Clamp(ws.Width, 720, work.Width);
        var h = Math.Clamp(ws.Height, 480, work.Height);
        var x = Math.Clamp(ws.X, work.X, work.X + work.Width - w);
        var y = Math.Clamp(ws.Y, work.Y, work.Y + work.Height - h);
        AppWindow.MoveAndResize(new RectInt32(x, y, w, h));
    }

    private void SaveWindowState()
    {
        var pos = AppWindow.Position;
        var size = AppWindow.Size;
        WindowStateStore.Save(new WindowState(
            pos.X, pos.Y, size.Width, size.Height,
            Dashboard.Visibility == Visibility.Visible));
        _player.SaveNow();
    }

    private void BrandTitleBar()
    {
        var tb = AppWindow.TitleBar;
        tb.ButtonBackgroundColor = Color.FromArgb(0x00, 0x00, 0x00, 0x00);
        tb.ButtonInactiveBackgroundColor = Color.FromArgb(0x00, 0x00, 0x00, 0x00);
        tb.ButtonForegroundColor = Color.FromArgb(0xFF, 0xEF, 0xE4, 0xCF);
        tb.ButtonInactiveForegroundColor = Color.FromArgb(0xFF, 0x9A, 0x8B, 0x6E);
        tb.ButtonHoverBackgroundColor = Color.FromArgb(0xFF, 0x1F, 0x1A, 0x12);
        tb.ButtonHoverForegroundColor = Color.FromArgb(0xFF, 0xFF, 0xB2, 0x2E);
        tb.ButtonPressedBackgroundColor = Color.FromArgb(0xFF, 0x3A, 0x30, 0x24);
        tb.ButtonPressedForegroundColor = Color.FromArgb(0xFF, 0xFF, 0xB2, 0x2E);
    }

    private async Task InitializeAsync()
    {
        await _settings.LoadAsync();
        App.Services.GetRequiredService<NugsStreamResolver>()
            .SetPreferredFormatProvider(() => _settings.Current.PreferredFormat);
        _player.Volume = _settings.Current.DefaultVolume;

        await _shell.InitializeAsync();
        if (_shell.IsLoggedIn)
        {
            await _player.RestoreAsync();
            ShowMain();
        }
        else
        {
            ShowLogin();
        }

        _ = CheckForUpdatesAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        var update = await _updates.CheckAsync();
        if (update is null) return;
        UpdateBannerText.Text = $"Update available: v{update.Tag}";
        UpdateBannerPanel.Visibility = Visibility.Visible;
    }

    private void ShowMain()
    {
        LoginPanel.Visibility = Visibility.Collapsed;
        MainPanel.Visibility = Visibility.Visible;
        SubscriptionBanner.Visibility = _shell.SubscriptionWarning is not null
            ? Visibility.Visible : Visibility.Collapsed;
        if (_restoreDashboard && Dashboard.Visibility == Visibility.Collapsed)
        {
            Dashboard.Visibility = Visibility.Visible;
            Dashboard.OnShown();
        }
        if (ContentFrame.Content is null)
            ContentFrame.Navigate(typeof(HomePage));
    }

    private void ShowLogin()
    {
        LoginPanel.Visibility = Visibility.Visible;
        MainPanel.Visibility = Visibility.Collapsed;
        SubscriptionBanner.Visibility = Visibility.Collapsed;
        UpdateBannerPanel.Visibility = Visibility.Collapsed;
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        if (ContentFrame.CanGoBack) ContentFrame.GoBack();
    }

    private void OnHome(object sender, RoutedEventArgs e) => ContentFrame.Navigate(typeof(HomePage));

    private void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        var q = SearchBox.Text?.Trim();
        if (!string.IsNullOrEmpty(q))
            ContentFrame.Navigate(typeof(SearchResultsPage), q);
    }

    private async void OnSignOut(object sender, RoutedEventArgs e)
    {
        _player.SaveNow();
        _player.Stop();
        App.Services.GetRequiredService<HomeViewModel>().ResetRails();
        await _shell.SignOutAsync();
        ContentFrame.Content = null;
        ContentFrame.BackStack.Clear();
        ShowLogin();
    }

    private void OnSettings(object sender, RoutedEventArgs e) =>
        ContentFrame.Navigate(typeof(SettingsPage));

    private async void OnAbout(object sender, RoutedEventArgs e)
    {
        var update = await _updates.CheckAsync();
        await AboutDialog.ShowAsync(this, _updates, update);
    }

    private async void OnUpdateBannerClick(object sender, RoutedEventArgs e)
    {
        var update = await _updates.CheckAsync();
        if (update is not null)
            await Launcher.LaunchUriAsync(new Uri(update.Url));
    }

    private async void OnRenewSubscription(object sender, RoutedEventArgs e) =>
        await Launcher.LaunchUriAsync(new Uri("https://www.nugs.net/subscribe"));

    private void OnDashboardToggle(object sender, RoutedEventArgs e) => ToggleDashboard();

    private void ToggleDashboard()
    {
        if (Dashboard.Visibility == Visibility.Visible)
            Dashboard.Visibility = Visibility.Collapsed;
        else
        {
            Dashboard.Visibility = Visibility.Visible;
            Dashboard.OnShown();
        }
    }

    private bool ShellActive => MainPanel.Visibility == Visibility.Visible;

    private void OnNextAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!ShellActive) return;
        _player.Next();
        args.Handled = true;
    }

    private void OnPreviousAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!ShellActive) return;
        _player.Previous();
        args.Handled = true;
    }

    private void OnPlayPauseAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!ShellActive) return;
        _player.TogglePlayPause();
        args.Handled = true;
    }

    private void OnSearchAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!ShellActive) return;
        SearchBox.Focus(FocusState.Programmatic);
        SearchBox.SelectAll();
        args.Handled = true;
    }

    private void OnDashboardAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!ShellActive) return;
        ToggleDashboard();
        args.Handled = true;
    }

    private void OnSkipBackAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!ShellActive) return;
        _player.SkipBack();
        args.Handled = true;
    }

    private void OnSkipForwardAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!ShellActive) return;
        _player.SkipForward();
        args.Handled = true;
    }
}
