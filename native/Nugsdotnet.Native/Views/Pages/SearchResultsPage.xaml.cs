using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Nugsdotnet.Native.Core;
using Nugsdotnet.Native.ViewModels;

namespace Nugsdotnet.Native.Views.Pages;

public sealed partial class SearchResultsPage : Page
{
    private readonly SearchResultsViewModel _vm;
    private string _query = "";

    public SearchResultsPage()
    {
        InitializeComponent();
        _vm = App.Services.GetRequiredService<SearchResultsViewModel>();
        DataContext = _vm;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        _query = e.Parameter as string ?? "";
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        BusyRing.Visibility = Visibility.Visible;
        RetryButton.Visibility = Visibility.Collapsed;
        await _vm.LoadAsync(_query);
        BusyRing.Visibility = Visibility.Collapsed;
        ArtistsSection.Visibility = _vm.Artists.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ContainersSection.Visibility = _vm.Containers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RetryButton.Visibility = _vm.Status is not null && _vm.Artists.Count == 0 && _vm.Containers.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnArtistClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ArtistEntry a)
            Frame.Navigate(typeof(ArtistPage), a.Id);
    }

    private void OnContainerClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ContainerEntry c)
            Frame.Navigate(typeof(AlbumPage), c.Id);
    }

    private async void OnRetry(object sender, RoutedEventArgs e) => await ReloadAsync();
}
