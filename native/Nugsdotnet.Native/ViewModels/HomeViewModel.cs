using System.Collections.ObjectModel;
using System.Globalization;
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
/// Home dashboard: the listening journal. Night heading, run state, last-night
/// recap, VU meters, top artists, 14-night timeline, milestones, a slim resume
/// strip, tonight's shelf (recents + stash merged), and your artists. The A–Z
/// grid lives on ArtistsPage. Registered as a singleton — the journal, shelf,
/// and resume strip refresh on every visit.
/// </summary>
public partial class HomeViewModel : ObservableObject
{
    private readonly RecentsStore _recents;
    private readonly StashStore _stash;
    private readonly ListeningJournal _journal;
    private readonly ImageLoader _images;
    private readonly PlayerService _player;
    private readonly ArtistsViewModel _artists;
    private List<string?> _recentArtists = new();
    private List<string?> _stashArtists = new();
    private string? _continueArtPath;

    public ObservableCollection<ShowCard> Shelf { get; } = new();
    public ObservableCollection<ArtistEntry> YourArtists { get; } = new();
    public ObservableCollection<TopArtistRow> TopArtists { get; } = new();
    public ObservableCollection<NightBar> Nights { get; } = new();

    [ObservableProperty] public partial string Heading { get; set; } = "THE JOURNAL BEGINS TONIGHT";
    [ObservableProperty] public partial string? RunLabel { get; set; }
    [ObservableProperty] public partial string? RecapLine { get; set; }
    [ObservableProperty] public partial bool HasJournal { get; set; }
    [ObservableProperty] public partial bool HasTopArtists { get; set; }
    [ObservableProperty] public partial string HoursText { get; set; } = "—";
    [ObservableProperty] public partial string ShowsText { get; set; } = "—";
    [ObservableProperty] public partial string TracksText { get; set; } = "—";
    [ObservableProperty] public partial string HoursScale { get; set; } = "";
    [ObservableProperty] public partial string ShowsScale { get; set; } = "";
    [ObservableProperty] public partial string TracksScale { get; set; } = "";
    [ObservableProperty] public partial double HoursNeedle { get; set; }
    [ObservableProperty] public partial double ShowsNeedle { get; set; }
    [ObservableProperty] public partial double TracksNeedle { get; set; }
    [ObservableProperty] public partial double HoursRed { get; set; } = JournalMath.RedZoneStart;
    [ObservableProperty] public partial double ShowsRed { get; set; } = JournalMath.RedZoneStart;
    [ObservableProperty] public partial double TracksRed { get; set; }
    [ObservableProperty] public partial string? MilestoneLine { get; set; }
    [ObservableProperty] public partial string? NewMilestoneId { get; set; }
    [ObservableProperty] public partial bool HasContinue { get; set; }
    [ObservableProperty] public partial bool HasShow { get; set; }
    [ObservableProperty] public partial string ContinueCue { get; set; } = "CONTINUE";
    [ObservableProperty] public partial string ContinueTitle { get; set; } = "";
    [ObservableProperty] public partial string ContinueSub { get; set; } = "";
    [ObservableProperty] public partial string ContinuePosition { get; set; } = "";
    [ObservableProperty] public partial string ContinuePlayLabel { get; set; } = "PLAY";
    [ObservableProperty] public partial string? ContinueContainerId { get; set; }
    [ObservableProperty] public partial ImageSource? ContinueArt { get; set; }

    public int StashTotal { get; private set; }

    public HomeViewModel(
        RecentsStore recents, StashStore stash, ListeningJournal journal,
        ImageLoader images, PlayerService player, ArtistsViewModel artists)
    {
        _recents = recents;
        _stash = stash;
        _journal = journal;
        _images = images;
        _player = player;
        _artists = artists;
    }

    /// <summary>Rebuilds the journal hero, shelf, and resume strip from disk + the player.</summary>
    public async Task RefreshRailsAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);

        // ---- journal -------------------------------------------------------
        var state = await _journal.LoadAsync();

        Heading = JournalMath.NightHeading(today, state);
        var run = JournalMath.Run(state.Days, today);
        RunLabel = run.Current > 0
            ? $"{run.Current}{(run.GraceNights > 0 ? "◇" : "◆")} NIGHT RUN · BEST {run.Best}"
            : null;
        RecapLine = JournalMath.Recap(DayOn(state, today.AddDays(-1)));
        // The journal "starts running" at the first listening night (≥ 5 min):
        // until then the day-one panel shows and the meters read —, so the
        // heading and the meters never disagree mid-window.
        HasJournal = state.Days.Any(JournalMath.IsListeningNight);

        var hours = JournalMath.Hours(state);
        var shows = (double)JournalMath.ShowsHeard(state);
        var tracks = (double)state.TracksCompleted;
        var hasData = HasJournal;
        HoursText = hasData ? $"{JournalMath.FormatHours(hours * 3600)} h" : "—";
        ShowsText = hasData ? $"{shows:0}" : "—";
        TracksText = hasData ? $"{tracks:0}" : "—";

        var hoursNext = JournalMath.NextThreshold("hours", hours);
        var showsNext = JournalMath.NextThreshold("shows", shows);
        (HoursNeedle, HoursScale) = Meter(hours, hoursNext, v => $"next {v:0}");
        (ShowsNeedle, ShowsScale) = Meter(shows, showsNext, v => $"next {v:0}");
        // TRACKS has no milestones: fixed 0–1000 scale, no red zone.
        TracksNeedle = Math.Clamp(tracks / JournalMath.TracksScaleEnd, 0, 1);
        TracksScale = $"{JournalMath.TracksScaleEnd:0}";

        TopArtists.Clear();
        foreach (var row in JournalMath.TopArtists(state.Days, today))
            TopArtists.Add(row);
        HasTopArtists = TopArtists.Count > 0;

        Nights.Clear();
        foreach (var bar in JournalMath.Timeline(state.Days, today))
            Nights.Add(bar);

        MilestoneLine = JournalMath.MilestoneLine(state);
        var newly = JournalMath.NewlyReached(state, today);
        if (newly.Count > 0)
        {
            NewMilestoneId = newly[0];
            state.Milestones.AddRange(newly.Select(id =>
                new MilestoneReached(id, today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))));
            await _journal.SaveAsync(state);
        }
        else
        {
            NewMilestoneId = null;
        }

        // ---- shelf + your artists -------------------------------------------
        var plays = await _recents.LoadAsync();
        _recentArtists = plays.Select(p => p.Artist).ToList();
        var stashed = await _stash.LoadAsync();
        StashTotal = stashed.Count;
        _stashArtists = stashed.Select(s => s.Artist).ToList();

        RebuildShelf(plays, stashed);
        RebuildYourArtists();

        RefreshContinue();
        _ = LoadArtsAsync(Shelf.ToList());   // ImageLoader never throws
    }

    /// <summary>Needle + right-hand scale label toward the next milestone.</summary>
    private static (double Needle, string Scale) Meter(
        double value, double? next, Func<double, string> render)
    {
        var s = JournalMath.Scale(value, next);
        return (s.Needle, next is null ? "" : render(next.Value));
    }

    private static JournalDay? DayOn(JournalState state, DateOnly date) =>
        state.Days.FirstOrDefault(d => d.Date == date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

    /// <summary>Recents first, deduped by container, stash filling behind, 24 cards.</summary>
    private void RebuildShelf(IReadOnlyList<RecentPlay> plays, IReadOnlyList<StashEntry> stashed)
    {
        Shelf.Clear();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in plays)
        {
            if (Shelf.Count >= HomeDashboard.StashRailCap) break;
            if (string.IsNullOrEmpty(p.ContainerId) || !seen.Add(p.ContainerId)) continue;
            Shelf.Add(new ShowCard(p));
        }
        foreach (var s in stashed)
        {
            if (Shelf.Count >= HomeDashboard.StashRailCap) break;
            if (string.IsNullOrEmpty(s.ContainerId) || !seen.Add(s.ContainerId)) continue;
            Shelf.Add(new ShowCard(s));
        }
    }

    /// <summary>Drops personal chrome so a signed-out shell cannot flash the
    /// previous nugs account's journal/shelf/resume card.</summary>
    public void ResetRails()
    {
        Shelf.Clear();
        YourArtists.Clear();
        TopArtists.Clear();
        Nights.Clear();
        _recentArtists = new();
        _stashArtists = new();
        StashTotal = 0;
        Heading = "THE JOURNAL BEGINS TONIGHT";
        RunLabel = null;
        RecapLine = null;
        HasJournal = false;
        HasTopArtists = false;
        HoursText = ShowsText = TracksText = "—";
        HoursScale = ShowsScale = TracksScale = "";
        HoursNeedle = ShowsNeedle = TracksNeedle = 0;
        MilestoneLine = null;
        NewMilestoneId = null;
        ClearContinue();
    }

    public void ToggleContinuePlayback()
    {
        _player.TogglePlayPause();
        RefreshContinue();
    }

    /// <summary>Re-reads the player into the resume strip. Cheap — no disk.</summary>
    public void RefreshContinue()
    {
        var c = _player.Current;
        if (c is null)
        {
            ClearContinue();
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

    private void RebuildYourArtists()
    {
        YourArtists.Clear();
        foreach (var a in HomeDashboard.YourArtists(_recentArtists, _stashArtists, _artists.CatalogSnapshot()))
            YourArtists.Add(a);
    }
}
