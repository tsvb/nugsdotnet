using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Nugsdotnet.Native.ViewModels;
using Nugsdotnet.Native.Views.Pages;

namespace Nugsdotnet.Native;

public sealed partial class MainWindow : Window
{
    private readonly ShellViewModel _shell;

    public MainWindow()
    {
        InitializeComponent();
        Title = "nugsdotnet";
        _shell = App.Services.GetRequiredService<ShellViewModel>();
        LoginPanel.LoggedIn += async (_, _) =>
        {
            await _shell.InitializeAsync();
            ShowMain();
        };
        ShowLogin();              // show login immediately; the async check may switch to the shell
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await _shell.InitializeAsync();
        if (_shell.IsLoggedIn) ShowMain();
        else ShowLogin();
    }

    private void ShowMain()
    {
        LoginPanel.Visibility = Visibility.Collapsed;
        MainPanel.Visibility = Visibility.Visible;
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
}
