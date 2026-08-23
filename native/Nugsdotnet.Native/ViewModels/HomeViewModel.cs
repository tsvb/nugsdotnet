using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using Nugsdotnet.Native.Core;
using Nugsdotnet.Native.Imaging;
using Nugsdotnet.Native.Playback;

namespace Nugsdotnet.Native.ViewModels;

/// <summary>One album card on a horizontal art rail (recents, stash, artist releases).</summary>
public sealed partial class ShowCard : ObservableObject
{
    public string ContainerId { get; }
    public string? Title { get; }
    public string? Sub { get; }
    public string? Artist { get; }
    private readonly string? _imagePath;

    [ObservableProperty] public partial ImageSource? Art { get; set; }

    public ShowCard(string containerId, string? title, string? sub, string? imagePath, string? artist = null)
    {
        ContainerId = containerId;
        Title = title;
        Sub = sub;
        Artist = artist;
        _imagePath = imagePath;
    }

    public ShowCard(RecentPlay play) : this(
        play.ContainerId, play.Title,
        HomeDashboard.RailSubtitle(play.Date, play.Venue, play.Artist),
        play.ImagePath, play.Artist)
    {
    }

    public ShowCard(StashEntry entry) : this(
        entry.ContainerId, entry.Title,
        HomeDashboard.RailSubtitle(entry.Date, entry.Venue, entry.Artist),
        entry.ImagePath, entry.Artist)
    {
    }

    /// <summary>UI thread only (builds a BitmapImage); never throws.</summary>
    public async Task LoadArtAsync(ImageLoader images) => Art = await images.LoadAsync(_imagePath);
}

/// <summary>
/// Home dashboard: time-of-day greeting, continue-listening hero, Recently
/// Played + Stash rails, personal artists, and a filterable A–Z artist grid.
/// Registered as a singleton — artists fetch once per session, the rails and
/// continue card refresh on every visit.
/// </summary>
public partial class HomeViewModel : ObservableObject
{
    private readonly NugsCatalog _catalog;
    private readonly RecentsStore _recents;
    private readonly StashStore _stash;
    private readonly ImageLoader _images;
    private readonly PlayerService _player;
    private List<ArtistEntry> _all = new();
    private List<string?> _recentArtists = new();
    private List<string?> _stashArtists = new();
    private string? _continueArtPath;

    public ObservableCollection<ArtistEntry> Artists { get; } = new();
    public ObservableCollection<ArtistEntry> YourArtists { get; } = new();
    public ObservableCollection<ShowCard> Recent { get; } = new();
    public ObservableCollection<ShowCard> Stash { get; } = new();
    public ObservableCollection<LetterChip> Letters { get; } = new();

    [ObservableProperty] public partial string Filter { get; set; } = "";
    [ObservableProperty] public partial string? ActiveLetter { get; set; }
    [ObservableProperty] public partial bool Busy { get; set; }
    [ObservableProperty] public partial string? Status { get; set; }
    [ObservableProperty] public partial string Greeting { get; set; } = "WELCOME BACK";
    [ObservableProperty] public partial string ArtistsLabel { get; set; } = "ARTISTS";
    [ObservableProperty] public partial string StashLabel { get; set; } = "STASH";
    [ObservableProperty] public partial string RecentMeter { get; set; } = "0";
    [ObservableProperty] public partial string StashMeter { get; set; } = "0";
    [ObservableProperty] public partial string ArtistMeter { get; set; } = "0";
    [ObservableProperty] public partial bool HasContinue { get; set; }
    [ObservableProperty] public partial bool HasShow { get; set; }
    [ObservableProperty] public partial bool IsFirstRun { get; set; }
    [ObservableProperty] public partial bool IsFiltered { get; set; }
    [ObservableProperty] public partial string ContinueCue { get; set; } = "CONTINUE";
    [ObservableProperty] public partial string ContinueTitle { get; set; } = "";
    [ObservableProperty] public partial string ContinueSub { get; set; } = "";
    [ObservableProperty] public partial string ContinuePosition { get; set; } = "";
    [ObservableProperty] public partial string ContinuePlayLabel { get; set; } = "PLAY";
    [ObservableProperty] public partial string? ContinueContainerId { get; set; }
    [ObservableProperty] public partial ImageSource? ContinueArt { get; set; }

    public int StashTotal { get; private set; }

    public HomeViewModel(
        NugsCatalog catalog, RecentsStore recents, StashStore stash,
        ImageLoader images, PlayerService player)
    {
        _catalog = catalog;
        _recents = recents;
        _stash = stash;
        _images = images;
        _player = player;
    }

    /// <summary>Rebuilds rails, meters, and the continue hero from disk + the player.</summary>
    public async Task RefreshRailsAsync()
    {
        Greeting = HomeDashboard.GreetingFor(DateTime.Now.Hour);
        var plays = await _recents.LoadAsync();
        Recent.Clear();
        foreach (var p in plays) Recent.Add(new ShowCard(p));
        _recentArtists = plays.Select(p => p.Artist).ToList();

        var stashed = await _stash.LoadAsync();
        StashTotal = stashed.Count;
        Stash.Clear();
        foreach (var s in stashed.Take(HomeDashboard.StashRailCap))
            Stash.Add(new ShowCard(s));
        _stashArtists = stashed.Select(s => s.Artist).ToList();
        StashLabel = HomeDashboard.StashLabel(stashed.Count);

        RefreshContinue();
        RebuildYourArtists();
        RefreshMeters();

        _ = LoadArtsAsync(Recent.Concat(Stash).ToList());   // ImageLoader never throws
    }

    /// <summary>Drops personal chrome so a signed-out shell cannot flash the
    /// previous nugs account's stash/recents/continue card.</summary>
    public void ResetRails()
    {
        Recent.Clear();
        Stash.Clear();
        YourArtists.Clear();
        _recentArtists = new();
        _stashArtists = new();
        StashTotal = 0;
        StashLabel = HomeDashboard.StashLabel(0);
        Filter = "";
        ActiveLetter = null;
        ClearContinue();
        RefreshMeters();
        RebuildLetters();
        ApplyFilter();
    }

    public async Task LoadArtistsAsync(bool force = false)
    {
        if (!force && _all.Count > 0)
        {
            RebuildLetters();
            ApplyFilter();
            RebuildYourArtists();
            RefreshMeters();
            return;
        }
        Busy = true;
        Status = null;
        try
        {
            _all = NugsCatalog.ParseArtists(await _catalog.GetAllArtistsAsync());
            RebuildLetters();
            ApplyFilter();
            RebuildYourArtists();
            RefreshMeters();
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

    /// <summary>Toggles the A–Z bucket. Pressing the active letter clears it.</summary>
    public void ToggleLetter(string? letter)
    {
        ActiveLetter = string.Equals(ActiveLetter, letter, StringComparison.Ordinal)
            ? null : letter;
        RebuildLetters();
        ApplyFilter();
    }

    public void ClearFilters()
    {
        Filter = "";
        ActiveLetter = null;
        RebuildLetters();
        ApplyFilter();
    }

    public void ToggleContinuePlayback()
    {
        _player.TogglePlayPause();
        RefreshContinue();
    }

    /// <summary>Re-reads the player into the continue hero. Cheap — no disk.</summary>
    public void RefreshContinue()
    {
        var c = _player.Current;
        if (c is null)
        {
            ClearContinue();
            RefreshMeters();
            return;
        }

        HasContinue = true;
        ContinueCue = HomeDashboard.ContinueCue(_player.IsPlaying);
        ContinueTitle = c.Title ?? "Untitled";
        ContinueSub = string.Join("  ·  ", new[] { c.Artist, c.Show }.Where(s => !string.IsNullOrEmpty(s)));
        ContinueContainerId = c.ContainerId;
        HasShow = !string.IsNullOrEmpty(c.ContainerId);
        ContinuePlayLabel = _player.IsPlaying ? "PAUSE" : "PLAY";
        var primedPaused = !_player.IsPlaying && _player.Duration.TotalSeconds <= 0;
        ContinuePosition = HomeDashboard.ContinuePosition(
            _player.Position.TotalSeconds, _player.Duration.TotalSeconds, primedPaused) ?? "";

        if (c.ImagePath != _continueArtPath)
        {
            _continueArtPath = c.ImagePath;
            ContinueArt = null;
            if (!string.IsNullOrEmpty(c.ImagePath))
                _ = LoadContinueArtAsync(new ShowCard(
                    c.ContainerId ?? "", c.Title, ContinueSub, c.ImagePath, c.Artist));
        }
    }

    private void ClearContinue()
    {
        HasContinue = false;
        HasShow = false;
        ContinueCue = "CONTINUE";
        ContinueTitle = "";
        ContinueSub = "";
        ContinuePosition = "";
        ContinuePlayLabel = "PLAY";
        ContinueContainerId = null;
        ContinueArt = null;
        _continueArtPath = null;
    }

    private async Task LoadArtsAsync(IReadOnlyList<ShowCard> cards)
    {
        // Overlap CDN fetches; BitmapImage decode still resumes on the UI thread.
        await Task.WhenAll(cards.Select(c => c.LoadArtAsync(_images)));
    }

    private async Task LoadContinueArtAsync(ShowCard card)
    {
        await card.LoadArtAsync(_images);
        ContinueArt = card.Art;
    }

    partial void OnFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var filtered = !string.IsNullOrWhiteSpace(Filter) || !string.IsNullOrWhiteSpace(ActiveLetter);
        IsFiltered = filtered;
        Artists.Clear();
        foreach (var a in HomeDashboard.FilterArtists(_all, Filter, ActiveLetter))
            Artists.Add(a);
        ArtistsLabel = HomeDashboard.ArtistsLabel(Artists.Count, _all.Count, filtered);
    }

    private void RebuildLetters()
    {
        Letters.Clear();
        foreach (var letter in HomeDashboard.LettersFor(_all))
            Letters.Add(new LetterChip { Letter = letter, IsActive = letter == ActiveLetter });
    }

    private void RebuildYourArtists()
    {
        YourArtists.Clear();
        foreach (var a in HomeDashboard.YourArtists(_recentArtists, _stashArtists, _all))
            YourArtists.Add(a);
    }

    private void RefreshMeters()
    {
        RecentMeter = Recent.Count.ToString();
        StashMeter = StashTotal.ToString();
        ArtistMeter = _all.Count.ToString();
        IsFirstRun = Recent.Count == 0 && StashTotal == 0 && !HasContinue;
    }
}
