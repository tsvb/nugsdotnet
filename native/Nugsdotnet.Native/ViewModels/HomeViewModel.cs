using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using Nugsdotnet.Native.Core;
using Nugsdotnet.Native.Imaging;

namespace Nugsdotnet.Native.ViewModels;

/// <summary>One album card on a horizontal art rail (recents, stash, artist releases).</summary>
public sealed partial class ShowCard : ObservableObject
{
    public string ContainerId { get; }
    public string? Title { get; }
    public string? Sub { get; }
    private readonly string? _imagePath;

    [ObservableProperty] public partial ImageSource? Art { get; set; }

    public ShowCard(string containerId, string? title, string? sub, string? imagePath)
    {
        ContainerId = containerId;
        Title = title;
        Sub = sub;
        _imagePath = imagePath;
    }

    public ShowCard(RecentPlay play) : this(play.ContainerId, play.Title, play.Artist, play.ImagePath)
    {
    }

    /// <summary>UI thread only (builds a BitmapImage); never throws.</summary>
    public async Task LoadArtAsync(ImageLoader images) => Art = await images.LoadAsync(_imagePath);
}

/// <summary>
/// Home dashboard: greeting hero, Recently Played + Stash rails, filterable
/// artist grid. Registered as a singleton — artists fetch once per session,
/// the rails refresh on every visit.
/// </summary>
public partial class HomeViewModel : ObservableObject
{
    /// <summary>The rail shows the newest stashed items; the store is uncapped.
    /// Keeps the non-virtualized header rail and per-visit art loads bounded.</summary>
    private const int StashRailCap = 24;

    private readonly NugsCatalog _catalog;
    private readonly RecentsStore _recents;
    private readonly StashStore _stash;
    private readonly ImageLoader _images;
    private List<ArtistEntry> _all = new();

    public ObservableCollection<ArtistEntry> Artists { get; } = new();
    public ObservableCollection<ShowCard> Recent { get; } = new();
    public ObservableCollection<ShowCard> Stash { get; } = new();

    [ObservableProperty] public partial string Filter { get; set; } = "";
    [ObservableProperty] public partial bool Busy { get; set; }
    [ObservableProperty] public partial string? Status { get; set; }
    [ObservableProperty] public partial string Greeting { get; set; } = "WELCOME BACK";
    [ObservableProperty] public partial string ArtistsLabel { get; set; } = "ARTISTS";
    [ObservableProperty] public partial string StashLabel { get; set; } = "STASH";

    public HomeViewModel(NugsCatalog catalog, RecentsStore recents, StashStore stash, ImageLoader images)
    {
        _catalog = catalog;
        _recents = recents;
        _stash = stash;
        _images = images;
    }

    /// <summary>Rebuilds both rails from disk; art fills in as it downloads.</summary>
    public async Task RefreshRailsAsync()
    {
        Greeting = GreetingFor(DateTime.Now.Hour);
        var plays = await _recents.LoadAsync();
        Recent.Clear();
        foreach (var p in plays) Recent.Add(new ShowCard(p));

        var stashed = await _stash.LoadAsync();
        Stash.Clear();
        foreach (var s in stashed.Take(StashRailCap))
            Stash.Add(new ShowCard(s.ContainerId, s.Title, s.Artist, s.ImagePath));
        StashLabel = stashed.Count > 0 ? $"STASH · {stashed.Count}" : "STASH";

        _ = LoadArtsAsync(Recent.Concat(Stash).ToList());   // ImageLoader never throws
    }

    /// <summary>Drops rail cards so a signed-out shell cannot flash the previous
    /// nugs account's stash/recents.</summary>
    public void ResetRails()
    {
        Recent.Clear();
        Stash.Clear();
        StashLabel = "STASH";
    }

    public async Task LoadArtistsAsync(bool force = false)
    {
        if (!force && _all.Count > 0) return;   // singleton — cached for the session
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
            Status = UserError.From(ex);
        }
        finally
        {
            Busy = false;
        }
    }

    public Task ReloadArtistsAsync() => LoadArtistsAsync(force: true);

    private async Task LoadArtsAsync(IReadOnlyList<ShowCard> cards)
    {
        // Overlap CDN fetches; BitmapImage decode still resumes on the UI thread.
        await Task.WhenAll(cards.Select(c => c.LoadArtAsync(_images)));
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
