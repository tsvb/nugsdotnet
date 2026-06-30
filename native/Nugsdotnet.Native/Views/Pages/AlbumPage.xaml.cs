using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Nugsdotnet.Native.Core;
using Nugsdotnet.Native.ViewModels;

namespace Nugsdotnet.Native.Views.Pages;

public sealed partial class AlbumPage : Page
{
    private readonly AlbumViewModel _vm;

    public AlbumPage()
    {
        InitializeComponent();
        _vm = App.Services.GetRequiredService<AlbumViewModel>();
        DataContext = _vm;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        var containerId = e.Parameter as string ?? "";
        await _vm.LoadAsync(containerId);
    }

    private void OnPlayAll(object sender, RoutedEventArgs e) => _vm.PlayAll();

    private void OnPlayTrack(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TrackRow t }) _vm.PlayFrom(t);
    }

    private void OnPlayNextTrack(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TrackRow t }) _vm.PlayNextOne(t);
    }

    private void OnEnqueueTrack(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TrackRow t }) _vm.EnqueueOne(t);
    }
}
