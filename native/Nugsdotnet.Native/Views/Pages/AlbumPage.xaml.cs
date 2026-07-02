using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Navigation;
using Nugsdotnet.Native.ViewModels;

namespace Nugsdotnet.Native.Views.Pages;

public sealed partial class AlbumPage : Page
{
    private readonly AlbumViewModel _vm;

    // Keeps the amber now-playing row live as the queue advances — the player is
    // poll-based (no events), same idiom as the transport's timer.
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };

    public AlbumPage()
    {
        InitializeComponent();
        _vm = App.Services.GetRequiredService<AlbumViewModel>();
        DataContext = _vm;
        _timer.Tick += (_, _) => _vm.RefreshNowPlaying();
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        var containerId = e.Parameter as string ?? "";
        BusyRing.Visibility = Visibility.Visible;
        await _vm.LoadAsync(containerId);
        BusyRing.Visibility = Visibility.Collapsed;
        // Grouped source is set after the data loads (a CVS in resources can't bind to DataContext).
        var cvs = (CollectionViewSource)Resources["TracksSource"];
        cvs.Source = _vm.TrackGroups;
        TracksList.ItemsSource = cvs.View;
    }

    private void OnPlayAll(object sender, RoutedEventArgs e) => _vm.PlayAll();

    private void OnPlayTrack(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TrackItem t }) _vm.PlayFrom(t);
    }

    private void OnPlayNextTrack(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TrackItem t }) _vm.PlayNextOne(t);
    }

    private void OnEnqueueTrack(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TrackItem t }) _vm.EnqueueOne(t);
    }
}
