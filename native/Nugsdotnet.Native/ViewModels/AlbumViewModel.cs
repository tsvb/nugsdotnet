using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using Nugsdotnet.Native.Core;
using Nugsdotnet.Native.Imaging;
using Nugsdotnet.Native.Playback;

namespace Nugsdotnet.Native.ViewModels;

/// <summary>A set of tracks under one header (Set 1 / Set 2 / Encore).</summary>
public sealed class TrackGroup : List<TrackRow>
{
    public string Label { get; }
    public TrackGroup(string label, IEnumerable<TrackRow> items) : base(items) => Label = label;
}

/// <summary>Album/show page: header + set-grouped track list, with play / queue actions.</summary>
public partial class AlbumViewModel : ObservableObject
{
    private readonly NugsCatalog _catalog;
    private readonly PlayerService _player;
    private readonly ImageLoader _images;

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

    public AlbumViewModel(NugsCatalog catalog, PlayerService player, ImageLoader images)
    {
        _catalog = catalog;
        _player = player;
        _images = images;
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
                TrackGroups.Add(new TrackGroup(SetLabel(g.Key), g));
            if (_album.Tracks.Count == 0) Status = "No tracks in this container.";

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
        if (_queue.Count > 0) _player.Play(_queue, 0);
    }

    public void PlayFrom(TrackRow track)
    {
        var i = IndexOf(track);
        if (i >= 0) _player.Play(_queue, i);
    }

    public void EnqueueOne(TrackRow track) => _player.Enqueue(One(track));

    public void PlayNextOne(TrackRow track) => _player.PlayNext(One(track));

    private int IndexOf(TrackRow track)
    {
        for (var i = 0; i < _queue.Count; i++)
            if (_queue[i].TrackId == track.TrackId) return i;
        return -1;
    }

    private List<NowPlaying> One(TrackRow t) =>
        new() { new NowPlaying(t.TrackId, t.Title, _album?.Artist, _album?.Title, _album?.ImagePath) };

    private static string SetLabel(int setNum) => setNum switch
    {
        <= 0 => "Tracks",
        1 or 2 or 3 => $"Set {setNum}",
        4 => "Encore",
        _ => $"Encore {setNum - 3}",
    };
}
