using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Nugsdotnet.Native.ViewModels;

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
    }

    private void ShowLogin()
    {
        LoginPanel.Visibility = Visibility.Visible;
        MainPanel.Visibility = Visibility.Collapsed;
    }

    private async void OnSignOut(object sender, RoutedEventArgs e)
    {
        await _shell.SignOutAsync();
        ShowLogin();
    }
}
