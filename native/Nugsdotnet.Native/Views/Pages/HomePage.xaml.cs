using Microsoft.Extensions.DependencyInjection;
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

    protected override async void OnNavigatedTo(NavigationEventArgs e) => await _vm.LoadAsync();

    private void OnArtistClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ArtistEntry a)
            Frame.Navigate(typeof(ArtistPage), a.Id);
    }
}
