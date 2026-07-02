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

    public ArtistPage()
    {
        InitializeComponent();
        _vm = App.Services.GetRequiredService<ArtistViewModel>();
        DataContext = _vm;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        var artistId = e.Parameter as string ?? "";
        BusyRing.Visibility = Visibility.Visible;
        await _vm.LoadAsync(artistId);
        BusyRing.Visibility = Visibility.Collapsed;
        ReleasesSection.Visibility = _vm.Releases.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowsSection.Visibility = _vm.Shows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowsLabel.Text = _vm.Shows.Count > 0 ? $"SHOWS · {_vm.Shows.Count}" : "SHOWS";
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
}
