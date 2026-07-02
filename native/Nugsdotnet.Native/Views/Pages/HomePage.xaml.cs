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

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        // Recents are local and fast — show the rail before the artist fetch.
        await _vm.RefreshRecentsAsync();
        RecentSection.Visibility = _vm.Recent.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_vm.Artists.Count == 0) BusyRing.Visibility = Visibility.Visible;
        await _vm.LoadArtistsAsync();
        BusyRing.Visibility = Visibility.Collapsed;
    }

    private void OnArtistClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ArtistEntry a)
            Frame.Navigate(typeof(ArtistPage), a.Id);
    }

    private void OnRecentClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ShowCard c })
            Frame.Navigate(typeof(AlbumPage), c.ContainerId);
    }
}
