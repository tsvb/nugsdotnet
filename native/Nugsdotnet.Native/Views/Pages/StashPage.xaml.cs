using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Nugsdotnet.Native.ViewModels;
using Nugsdotnet.Native.Views.Pages;

namespace Nugsdotnet.Native.Views.Pages;

public sealed partial class StashPage : Page
{
    private readonly StashViewModel _vm;

    public StashPage()
    {
        InitializeComponent();
        _vm = App.Services.GetRequiredService<StashViewModel>();
        DataContext = _vm;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e) => await ReloadAsync();

    private async Task ReloadAsync()
    {
        BusyRing.Visibility = Visibility.Visible;
        RetryButton.Visibility = Visibility.Collapsed;
        await _vm.LoadAsync();
        BusyRing.Visibility = Visibility.Collapsed;
        RetryButton.Visibility = _vm.Status is not null && _vm.Items.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnCardClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ShowCard card)
            Frame.Navigate(typeof(AlbumPage), card.ContainerId);
    }

    private async void OnRetry(object sender, RoutedEventArgs e) => await ReloadAsync();
}
