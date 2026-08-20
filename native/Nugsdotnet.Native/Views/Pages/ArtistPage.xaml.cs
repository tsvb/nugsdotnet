using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Nugsdotnet.Native.Core;
using Nugsdotnet.Native.ViewModels;

namespace Nugsdotnet.Native.Views.Pages;

public sealed partial class ArtistPage : Page
{
    private readonly ArtistViewModel _vm;
    private string _artistId = "";

    public ArtistPage()
    {
        InitializeComponent();
        _vm = App.Services.GetRequiredService<ArtistViewModel>();
        DataContext = _vm;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        _artistId = e.Parameter as string ?? "";
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        BusyRing.Visibility = Visibility.Visible;
        RetryButton.Visibility = Visibility.Collapsed;
        await _vm.LoadAsync(_artistId);
        BusyRing.Visibility = Visibility.Collapsed;
        RefreshSections();
    }

    private void RefreshSections()
    {
        ReleasesSection.Visibility = _vm.Releases.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowsSection.Visibility = _vm.Shows.Count > 0 || _vm.HasMoreShows
            ? Visibility.Visible : Visibility.Collapsed;
        ShowsLabel.Text = _vm.Shows.Count > 0 ? $"SHOWS · {_vm.Shows.Count}" : "SHOWS";
        LoadMoreButton.Visibility = _vm.HasMoreShows ? Visibility.Visible : Visibility.Collapsed;
        RetryButton.Visibility = _vm.Status is not null && _vm.Shows.Count == 0 && _vm.Releases.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnContainerClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ContainerEntry c)
            Frame.Navigate(typeof(AlbumPage), c.Id);
    }

    private void OnReleaseClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ShowCard c })
            Frame.Navigate(typeof(AlbumPage), c.ContainerId);
    }

    private async void OnLoadMore(object sender, RoutedEventArgs e)
    {
        LoadMoreButton.Visibility = Visibility.Collapsed;
        LoadMoreRing.Visibility = Visibility.Visible;
        await _vm.LoadMoreShowsAsync();
        LoadMoreRing.Visibility = Visibility.Collapsed;
        RefreshSections();
    }

    private async void OnRetry(object sender, RoutedEventArgs e) => await ReloadAsync();
}
