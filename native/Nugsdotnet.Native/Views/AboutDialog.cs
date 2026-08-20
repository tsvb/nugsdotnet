using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Nugsdotnet.Native.Services;

namespace Nugsdotnet.Native.Views;

/// <summary>About dialog with version info and optional update notice.</summary>
public static class AboutDialog
{
    public static async Task ShowAsync(Window owner, UpdateChecker checker, UpdateInfo? update = null)
    {
        var version = checker.CurrentVersion;
        var body = $"Version {version.Major}.{version.Minor}.{version.Build}\n\n" +
                   "A personal hi-fi front panel for nugs.net live music.\n" +
                   "Not affiliated with nugs.net.";
        if (update is not null)
            body += $"\n\nUpdate available: v{update.Tag}\n{update.Url}";

        var dlg = new ContentDialog
        {
            Title = "About nugsdotnet",
            Content = new TextBlock
            {
                Text = body,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Application.Current.Resources["BrandBodyFont"],
            },
            PrimaryButtonText = update is not null ? "Get update" : "OK",
            SecondaryButtonText = update is not null ? "Later" : "",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = owner.Content.XamlRoot,
        };
        var result = await dlg.ShowAsync();
        if (result == ContentDialogResult.Primary && update is not null)
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri(update.Url));
        }
    }
}
