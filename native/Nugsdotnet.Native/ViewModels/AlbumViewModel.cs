using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Nugsdotnet.Native.Core;
using Nugsdotnet.Native.Imaging;
using Nugsdotnet.Native.Playback;

namespace Nugsdotnet.Native.ViewModels;

/// <summary>One track row: the immutable Core row plus live now-playing state.</summary>
public sealed partial class TrackItem : ObservableObject
{
    public TrackRow Track { get; }
    public TrackItem(TrackRow track) => Track = track;

    public string Display => Track.Display;
    public string? RunTime => Track.RunTime;

    [ObservableProperty] private bool isNowPlaying;

    /// <summary>x:Bind function binding — AMBER = signal / now-playing.</summary>
    public Brush RowForeground(bool nowPlaying) =>
        (Brush)Application.Current.Resources[nowPlaying ? "BrandAccent" : "BrandText"];
}

/// <summary>A set of tracks under one header (Set 1 / Set 2 / Encore).</summary>
public sealed class TrackGroup : List<TrackItem>
{
    public string Label { get; }
    public TrackGroup(string label, IEnumerable<TrackItem> items) : base(items) => Label = label;
}

/// <summary>Album/show page: header + set-grouped track list, with play / queue actions.</summary>
public partial class AlbumViewModel : ObservableObject
{
    private readonly NugsCatalog _catalog;
    private readonly PlayerService _player;
    private readonly ImageLoader _images;
    private readonly RecentsStore _recents;

    private AlbumView? _album;
    private List<NowPlaying> _queue = new();

    /// <summary>Tracks grouped by set — bound through a grouped CollectionViewSource.</summary>
    public ObservableCollection<TrackGroup> TrackGroups { get; } = new();

    [ObservableProperty] private string? title;
    [ObservableProperty] private string? subtitle;
    [ObservableProperty] private string? runtime;
    [ObservableProperty] private ImageSource? cover;
    [ObservableProperty] private bool busy;
    [ObservableProperty] private string? status;

    public AlbumViewModel(NugsCatalog catalog, PlayerService player, ImageLoader images, RecentsStore recents)
    {
        _catalog = catalog;
        _player = player;
        _images = images;
        _recents = recents;
    }

    public async Task LoadAsync(string containerId)
    {
        Busy = true;
        Status = null;
        TrackGroups.Clear();
        Cover = null;
        try
        {
            _album = NugsCatalog.ParseAlbum(await _catalog.GetAlbumAsync(containerId));
            Title = _album.Title;
            Subtitle = string.Join("   ·   ",
                new[] { _album.Artist, _album.Date, _album.Venue }.Where(x => !string.IsNullOrEmpty(x)));
            Runtime = _album.RunTime;
            _queue = NugsCatalog.ToQueue(_album);

            foreach (var g in _album.Tracks.GroupBy(t => t.SetNum))
                TrackGroups.Add(new TrackGroup(SetLabel(g.Key), g.Select(t => new TrackItem(t))));
            if (_album.Tracks.Count == 0) Status = "No tracks in this container.";
            RefreshNowPlaying();   // this album may already be playing

            if (!string.IsNullOrEmpty(_album.ImagePath))
                Cover = await _images.LoadAsync(_album.ImagePath);
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

    public void PlayAll()
    {
        if (_queue.Count > 0)
        {
            _player.Play(_queue, 0);
            RecordRecent();
        }
        RefreshNowPlaying();
    }

    public void PlayFrom(TrackItem track)
    {
        var i = IndexOf(track.Track);
        if (i >= 0)
        {
            _player.Play(_queue, i);
            RecordRecent();
        }
        RefreshNowPlaying();
    }

    public void EnqueueOne(TrackItem track)
    {
        _player.Enqueue(One(track.Track));
        RefreshNowPlaying();   // starts playing when the queue was idle
    }

    public void PlayNextOne(TrackItem track)
    {
        _player.PlayNext(One(track.Track));
        RefreshNowPlaying();
    }

    /// <summary>
    /// Lights the row whose track is current in the player. Polled by the page
    /// (PlayerService has no events); setters no-op when nothing changed.
    /// </summary>
    public void RefreshNowPlaying()
    {
        var id = _player.Current?.TrackId;
        foreach (var group in TrackGroups)
            foreach (var item in group)
                item.IsNowPlaying = id is not null && item.Track.TrackId == id;
    }

    /// <summary>Feeds the Home dashboard's Recently Played rail (fire-and-forget).</summary>
    private void RecordRecent()
    {
        if (_album is null || string.IsNullOrEmpty(_album.Id)) return;
        _ = _recents.RecordAsync(new RecentPlay(
            _album.Id, _album.Title, _album.Artist, _album.Date, _album.Venue,
            _album.ImagePath, DateTimeOffset.UtcNow));
    }

    private int IndexOf(TrackRow track)
    {
        for (var i = 0; i < _queue.Count; i++)
            if (_queue[i].TrackId == track.TrackId) return i;
        return -1;
    }

    private List<NowPlaying> One(TrackRow t) =>
        new() { new NowPlaying(t.TrackId, t.Title, _album?.Artist, _album?.Title, _album?.ImagePath, _album?.Id) };

    private static string SetLabel(int setNum) => setNum switch
    {
        <= 0 => "Tracks",
        1 or 2 or 3 => $"Set {setNum}",
        4 => "Encore",
        _ => $"Encore {setNum - 3}",
    };
}
