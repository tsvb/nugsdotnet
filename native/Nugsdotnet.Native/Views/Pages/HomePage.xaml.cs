using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Nugsdotnet.Native.Core;
using Nugsdotnet.Native.ViewModels;

namespace Nugsdotnet.Native.Views.Pages;

public sealed partial class HomePage : Page
{
    private readonly HomeViewModel _vm;
    private readonly ArtistsViewModel _artists;
    private Storyboard? _pulse;

    public HomePage()
    {
        InitializeComponent();
        _vm = App.Services.GetRequiredService<HomeViewModel>();
        _artists = App.Services.GetRequiredService<ArtistsViewModel>();
        DataContext = _vm;
        _vm.PropertyChanged += OnVmPropertyChanged;
        Unloaded += (_, _) => _vm.PropertyChanged -= OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HomeViewModel.HasContinue)
            or nameof(HomeViewModel.HasShow)
            or nameof(HomeViewModel.HasJournal)
            or nameof(HomeViewModel.StashTotal)
            or nameof(HomeViewModel.YourArtists))
            RefreshChrome();
        else if (e.PropertyName is nameof(HomeViewModel.NewMilestoneId))
            PulseMilestone();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        await ReloadAsync();
        // The catalog may not be fetched yet on a cold start; when it lands,
        // "your artists" can finally match names to ids.
        if (_artists.Artists.Count == 0)
        {
            await _artists.LoadArtistsAsync();
            await _vm.RefreshRailsAsync();
            RefreshChrome();
        }
    }

    private async Task ReloadAsync()
    {
        await _vm.RefreshRailsAsync();
        RefreshChrome();
    }

    private void RefreshChrome()
    {
        JournalSection.Visibility = _vm.HasJournal ? Visibility.Visible : Visibility.Collapsed;
        BeginSection.Visibility = _vm.HasJournal ? Visibility.Collapsed : Visibility.Visible;
        ContinueSection.Visibility = _vm.HasContinue ? Visibility.Visible : Visibility.Collapsed;
        ContinueOpenButton.Visibility = _vm.HasShow ? Visibility.Visible : Visibility.Collapsed;
        ShelfSection.Visibility = _vm.Shelf.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        YourArtistsSection.Visibility = _vm.YourArtists.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RunLabel.Visibility = _vm.RunLabel is null ? Visibility.Collapsed : Visibility.Visible;
        RecapLineText.Visibility = _vm.RecapLine is null ? Visibility.Collapsed : Visibility.Visible;
        MilestoneLineText.Visibility = _vm.MilestoneLine is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Amber pulse when a milestone fires — once per visit.</summary>
    private void PulseMilestone()
    {
        if (_vm.NewMilestoneId is null) return;
        _pulse?.Stop();
        var sb = new Storyboard();
        var anim = new DoubleAnimation
        {
            From = 1.0, To = 0.25, Duration = new Duration(TimeSpan.FromMilliseconds(420)),
            AutoReverse = true, RepeatBehavior = new RepeatBehavior(3),
        };
        Storyboard.SetTarget(anim, MilestoneLineText);
        Storyboard.SetTargetProperty(anim, "Opacity");
        sb.Children.Add(anim);
        _pulse = sb;
        sb.Begin();
    }

    /// <summary>WinUI rejects x:Bind on Row/ColumnDefinitions (read-only
    /// collections), so bars carry their star weights in Tag and apply them
    /// here — e.g. "92*,8*" fills the first 92% of the track.</summary>
    private void OnStarsLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Grid { Tag: string stars } g) return;
        var rows = g.RowDefinitions.Count > 0;
        g.RowDefinitions.Clear();
        g.ColumnDefinitions.Clear();
        foreach (var part in stars.Split(','))
        {
            var t = part.Trim();
            var weight = t.EndsWith('*') && double.TryParse(t[..^1], out var n) && n > 0 ? n : 1;
            if (rows)
                g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(weight, GridUnitType.Star) });
            else
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(weight, GridUnitType.Star) });
        }
    }

    private void OnCardClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ShowCard c })
            Frame.Navigate(typeof(AlbumPage), c.ContainerId);
    }

    private void OnArtistChipClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ArtistEntry a })
            Frame.Navigate(typeof(ArtistPage), a.Id);
    }

    /// <summary>Top-artist rows come from listening history (names, not ids) —
    /// resolve to the catalog when possible, else fall back to search.</summary>
    private void OnTopArtistClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string name }) return;
        var entry = _artists.CatalogSnapshot()
            .FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
        if (entry is not null)
            Frame.Navigate(typeof(ArtistPage), entry.Id);
        else
            Frame.Navigate(typeof(SearchResultsPage), name);
    }

    private void OnSeeAllStash(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(StashPage));

    private void OnFullIndex(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(ArtistsPage));

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
}
