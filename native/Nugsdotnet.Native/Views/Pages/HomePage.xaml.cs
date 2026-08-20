using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Nugsdotnet.Native.Core;
using Nugsdotnet.Native.ViewModels;

namespace Nugsdotnet.Native.Views.Pages;

public sealed partial class HomePage : Page
{
    private readonly HomeViewModel _vm;

    public HomePage()
    {
        InitializeComponent();
        _vm = App.Services.GetRequiredService<HomeViewModel>();
        DataContext = _vm;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e) => await ReloadAsync();

    private async Task ReloadAsync()
    {
        await _vm.RefreshRailsAsync();
        RecentSection.Visibility = _vm.Recent.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        StashSection.Visibility = _vm.Stash.Count > 0 || _vm.StashLabel.Contains("·", StringComparison.Ordinal)
            ? Visibility.Visible : Visibility.Collapsed;
        BusyRing.Visibility = Visibility.Visible;
        RetryButton.Visibility = Visibility.Collapsed;
        await _vm.LoadArtistsAsync();
        BusyRing.Visibility = Visibility.Collapsed;
        RetryButton.Visibility = _vm.Status is not null && _vm.Artists.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnArtistClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ArtistEntry a)
            Frame.Navigate(typeof(ArtistPage), a.Id);
    }

    private void OnCardClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ShowCard c })
            Frame.Navigate(typeof(AlbumPage), c.ContainerId);
    }

    private void OnSeeAllStash(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(StashPage));

    private async void OnRetry(object sender, RoutedEventArgs e) => await ReloadAsync();
}
