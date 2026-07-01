using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using Nugsdotnet.Native.Core;
using Nugsdotnet.Native.Imaging;

namespace Nugsdotnet.Native.ViewModels;

/// <summary>One album card on the dashboard's Recently Played rail.</summary>
public sealed partial class ShowCard : ObservableObject
{
    public string ContainerId { get; }
    public string? Title { get; }
    public string? Artist { get; }
    private readonly string? _imagePath;

    [ObservableProperty] private ImageSource? art;

    public ShowCard(RecentPlay play)
    {
        ContainerId = play.ContainerId;
        Title = play.Title;
        Artist = play.Artist;
        _imagePath = play.ImagePath;
    }

    /// <summary>UI thread only (builds a BitmapImage); never throws.</summary>
    public async Task LoadArtAsync(ImageLoader images) => Art = await images.LoadAsync(_imagePath);
}

/// <summary>
/// Home dashboard: greeting hero, Recently Played rail, filterable artist grid.
/// Registered as a singleton — artists fetch once per session, recents refresh
/// on every visit.
/// </summary>
public partial class HomeViewModel : ObservableObject
{
    private readonly NugsCatalog _catalog;
    private readonly RecentsStore _recents;
    private readonly ImageLoader _images;
    private List<ArtistEntry> _all = new();

    public ObservableCollection<ArtistEntry> Artists { get; } = new();
    public ObservableCollection<ShowCard> Recent { get; } = new();

    [ObservableProperty] private string filter = "";
    [ObservableProperty] private bool busy;
    [ObservableProperty] private string? status;
    [ObservableProperty] private string greeting = "WELCOME BACK";
    [ObservableProperty] private string artistsLabel = "ARTISTS";

    public HomeViewModel(NugsCatalog catalog, RecentsStore recents, ImageLoader images)
    {
        _catalog = catalog;
        _recents = recents;
        _images = images;
    }

    /// <summary>Rebuilds the rail from disk; art fills in as it downloads.</summary>
    public async Task RefreshRecentsAsync()
    {
        Greeting = GreetingFor(DateTime.Now.Hour);
        var plays = await _recents.LoadAsync();
        Recent.Clear();
        foreach (var p in plays) Recent.Add(new ShowCard(p));
        _ = LoadArtsAsync(Recent.ToList());   // ImageLoader never throws
    }

    public async Task LoadArtistsAsync()
    {
        if (_all.Count > 0) return;   // singleton — cached for the session
        Busy = true;
        Status = null;
        try
        {
            _all = NugsCatalog.ParseArtists(await _catalog.GetAllArtistsAsync());
            ApplyFilter();
            ArtistsLabel = _all.Count > 0 ? $"ARTISTS · {_all.Count}" : "ARTISTS";
            if (_all.Count == 0) Status = "No artists returned.";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task LoadArtsAsync(IReadOnlyList<ShowCard> cards)
    {
        foreach (var card in cards) await card.LoadArtAsync(_images);
    }

    partial void OnFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        Artists.Clear();
        IEnumerable<ArtistEntry> q = _all;
        if (!string.IsNullOrWhiteSpace(Filter))
            q = _all.Where(a => a.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase));
        foreach (var a in q) Artists.Add(a);
    }

    private static string GreetingFor(int hour) => hour switch
    {
        >= 5 and < 12 => "GOOD MORNING",
        >= 12 and < 18 => "GOOD AFTERNOON",
        _ => "GOOD EVENING",
    };
}
