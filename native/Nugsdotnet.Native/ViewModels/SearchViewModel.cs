using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Nugsdotnet.Native.Core;
using Nugsdotnet.Native.Playback;

namespace Nugsdotnet.Native.ViewModels;

/// <summary>
/// Search + play-a-track. The vertical slice that exercises the whole native
/// path: catalog search → defensive parse → stream resolve → native playback.
/// </summary>
public partial class SearchViewModel : ObservableObject
{
    private readonly NugsCatalog _catalog;
    private readonly NugsStreamResolver _resolver;
    private readonly NugsAuth _auth;
    private readonly PlayerService _player;

    public ObservableCollection<TrackEntry> Results { get; } = new();

    [ObservableProperty] private string query = "";
    [ObservableProperty] private bool busy;
    [ObservableProperty] private string? status;

    public SearchViewModel(
        NugsCatalog catalog, NugsStreamResolver resolver, NugsAuth auth, PlayerService player)
    {
        _catalog = catalog;
        _resolver = resolver;
        _auth = auth;
        _player = player;
    }

    public async Task SearchAsync()
    {
        if (Busy || string.IsNullOrWhiteSpace(Query)) return;
        Busy = true;
        Status = null;
        Results.Clear();
        try
        {
            var raw = await _catalog.SearchAsync(Query.Trim());
            foreach (var t in NugsCatalog.ParseSearchTracks(raw))
                Results.Add(t);
            Status = Results.Count == 0 ? "No tracks found." : $"{Results.Count} tracks.";
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

    public async Task PlayAsync(TrackEntry track)
    {
        Status = $"Resolving “{track.Title}”…";
        try
        {
            var session = await _auth.GetSessionAsync();
            var pick = await _resolver.ResolveBestStreamAsync(track.TrackId, session);
            if (pick is null)
            {
                Status = "No playable stream for that track.";
                return;
            }
            if (pick.Format == AudioFormat.Hls)
            {
                Status = "HLS-only track — not supported yet.";
                return;
            }
            await _player.PlaySingleAsync(pick, track.Title, track.Artist);
            Status = $"Playing — {NugsStreamResolver.GetQualityLabel(pick.Format)}";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
    }
}
