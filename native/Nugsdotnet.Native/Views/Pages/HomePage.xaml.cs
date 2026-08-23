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
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(HomeViewModel.IsFiltered)
                or nameof(HomeViewModel.HasContinue)
                or nameof(HomeViewModel.HasShow)
                or nameof(HomeViewModel.IsFirstRun))
                RefreshChrome();
        };
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e) => await ReloadAsync();

    private async Task ReloadAsync()
    {
        await _vm.RefreshRailsAsync();
        RefreshChrome();
        BusyRing.Visibility = Visibility.Visible;
        RetryButton.Visibility = Visibility.Collapsed;
        await _vm.LoadArtistsAsync();
        BusyRing.Visibility = Visibility.Collapsed;
        RetryButton.Visibility = _vm.Status is not null && _vm.Artists.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;
        RefreshChrome();
    }

    private void RefreshChrome()
    {
        ContinueSection.Visibility = _vm.HasContinue ? Visibility.Visible : Visibility.Collapsed;
        ContinueOpenButton.Visibility = _vm.HasShow ? Visibility.Visible : Visibility.Collapsed;
        RecentSection.Visibility = _vm.Recent.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        StashSection.Visibility = _vm.StashTotal > 0 ? Visibility.Visible : Visibility.Collapsed;
        YourArtistsSection.Visibility = _vm.YourArtists.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        FirstRunCue.Visibility = _vm.IsFirstRun ? Visibility.Visible : Visibility.Collapsed;
        LettersStrip.Visibility = _vm.Letters.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearFilterButton.Visibility = _vm.IsFiltered ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnArtistClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ArtistEntry a)
            Frame.Navigate(typeof(ArtistPage), a.Id);
    }

    private void OnArtistChipClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ArtistEntry a })
            Frame.Navigate(typeof(ArtistPage), a.Id);
    }

    private void OnCardClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ShowCard c })
            Frame.Navigate(typeof(AlbumPage), c.ContainerId);
    }

    private void OnSeeAllStash(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(StashPage));

    private void OnLetterClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string letter })
            _vm.ToggleLetter(letter);
        RefreshChrome();
    }

    private void OnClearFilters(object sender, RoutedEventArgs e)
    {
        _vm.ClearFilters();
        RefreshChrome();
    }

    private void OnContinuePlay(object sender, RoutedEventArgs e)
    {
        _vm.ToggleContinuePlayback();
        RefreshChrome();
    }

    private void OnContinueOpen(object sender, RoutedEventArgs e)
    {
        if (_vm.ContinueContainerId is { Length: > 0 } id)
            Frame.Navigate(typeof(AlbumPage), id);
    }

    private async void OnRetry(object sender, RoutedEventArgs e) => await ReloadAsync();
}
