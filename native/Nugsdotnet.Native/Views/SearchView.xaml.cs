using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Nugsdotnet.Native.Core;
using Nugsdotnet.Native.ViewModels;

namespace Nugsdotnet.Native.Views;

public sealed partial class SearchView : UserControl
{
    private readonly SearchViewModel _vm;

    public SearchView()
    {
        InitializeComponent();
        _vm = App.Services.GetRequiredService<SearchViewModel>();
        DataContext = _vm;
    }

    private async void OnSearch(object sender, RoutedEventArgs e) => await _vm.SearchAsync();

    private async void OnQueryKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            await _vm.SearchAsync();
        }
    }

    private async void OnPlay(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TrackEntry track })
            await _vm.PlayAsync(track);
    }
}
