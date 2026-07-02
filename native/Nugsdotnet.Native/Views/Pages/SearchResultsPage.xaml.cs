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

    public SearchResultsPage()
    {
        InitializeComponent();
        _vm = App.Services.GetRequiredService<SearchResultsViewModel>();
        DataContext = _vm;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        var query = e.Parameter as string ?? "";
        BusyRing.Visibility = Visibility.Visible;
        await _vm.LoadAsync(query);
        BusyRing.Visibility = Visibility.Collapsed;
        ArtistsSection.Visibility = _vm.Artists.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ContainersSection.Visibility = _vm.Containers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
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
}
