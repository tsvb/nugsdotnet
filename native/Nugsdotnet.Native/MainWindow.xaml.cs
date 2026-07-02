using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Nugsdotnet.Native.Playback;
using Nugsdotnet.Native.ViewModels;
using Nugsdotnet.Native.Views.Pages;
using Windows.Graphics;
using Windows.UI;

namespace Nugsdotnet.Native;

public sealed partial class MainWindow : Window
{
    private readonly ShellViewModel _shell;
    private readonly PlayerService _player;
    private bool _restoreDashboard;

    public MainWindow()
    {
        InitializeComponent();
        Title = "nugsdotnet";

        // The cabinet colour runs to the top edge; the strip is the drag region
        // and the caption buttons are repainted to match the faceplate.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarStrip);
        BrandTitleBar();
        RestoreWindowState();

        _shell = App.Services.GetRequiredService<ShellViewModel>();
        _player = App.Services.GetRequiredService<PlayerService>();
        Transport.AlbumRequested += id => ContentFrame.Navigate(typeof(AlbumPage), id);
        LoginPanel.LoggedIn += async (_, _) =>
        {
            await _shell.InitializeAsync();
            await _player.RestoreAsync();   // idempotent — first login on this box
            ShowMain();
        };
        Closed += (_, _) => SaveWindowState();
        ShowLogin();              // show login immediately; the async check may switch to the shell
        _ = InitializeAsync();
    }

    /// <summary>Reopen where the user left the window, clamped to a visible
    /// display so a detached monitor can't strand it off-screen.</summary>
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
        _player.SaveNow();   // final position snapshot for resume-on-launch
    }

    private void BrandTitleBar()
    {
        var tb = AppWindow.TitleBar;
        tb.ButtonBackgroundColor = Color.FromArgb(0x00, 0x00, 0x00, 0x00);
        tb.ButtonInactiveBackgroundColor = Color.FromArgb(0x00, 0x00, 0x00, 0x00);
        tb.ButtonForegroundColor = Color.FromArgb(0xFF, 0xEF, 0xE4, 0xCF);   // BrandText
        tb.ButtonInactiveForegroundColor = Color.FromArgb(0xFF, 0x9A, 0x8B, 0x6E);   // BrandDim
        tb.ButtonHoverBackgroundColor = Color.FromArgb(0xFF, 0x1F, 0x1A, 0x12);   // BrandSurface2
        tb.ButtonHoverForegroundColor = Color.FromArgb(0xFF, 0xFF, 0xB2, 0x2E);   // BrandAccent
        tb.ButtonPressedBackgroundColor = Color.FromArgb(0xFF, 0x3A, 0x30, 0x24);   // BrandBorder
        tb.ButtonPressedForegroundColor = Color.FromArgb(0xFF, 0xFF, 0xB2, 0x2E);
    }

    private async Task InitializeAsync()
    {
        await _shell.InitializeAsync();
        if (_shell.IsLoggedIn)
        {
            await _player.RestoreAsync();   // resume-on-launch: queue primed, paused
            ShowMain();
        }
        else
        {
            ShowLogin();
        }
    }

    private void ShowMain()
    {
        LoginPanel.Visibility = Visibility.Collapsed;
        MainPanel.Visibility = Visibility.Visible;
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
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        if (ContentFrame.CanGoBack) ContentFrame.GoBack();
    }

    private void OnHome(object sender, RoutedEventArgs e) => ContentFrame.Navigate(typeof(HomePage));

    private void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        e.Handled = true;
        var q = SearchBox.Text?.Trim();
        if (!string.IsNullOrEmpty(q))
            ContentFrame.Navigate(typeof(SearchResultsPage), q);
    }

    private async void OnSignOut(object sender, RoutedEventArgs e)
    {
        await _shell.SignOutAsync();
        ContentFrame.Content = null;
        ContentFrame.BackStack.Clear();   // don't let Back reveal the previous session
        ShowLogin();
    }

    // ---- dashboard inspector ----------------------------------------------

    private void OnDashboardToggle(object sender, RoutedEventArgs e) => ToggleDashboard();

    private void ToggleDashboard()
    {
        if (Dashboard.Visibility == Visibility.Visible)
        {
            Dashboard.Visibility = Visibility.Collapsed;
        }
        else
        {
            Dashboard.Visibility = Visibility.Visible;
            Dashboard.OnShown();
        }
    }

    // ---- keyboard shortcuts (inert on the login screen) --------------------

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
}
