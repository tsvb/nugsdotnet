using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;
using Nugsdotnet.Native.Audio;
using Nugsdotnet.Native.Core;

namespace Nugsdotnet.Native.Playback;

/// <summary>
/// A real play queue over a single <see cref="MediaPlayer"/>. Each track's CDN
/// stream is resolved on demand (probe → <see cref="HttpAudioStream"/>) when it
/// becomes current, and the queue advances on MediaEnded. Phase 2 trades true
/// gapless (a MediaPlaybackList look-ahead, deferred) for a simple, reliable
/// resolve-on-advance model.
///
/// No UI thread affinity: state is polled by the transport on a UI timer, and
/// MediaPlayer accepts Source/Play from any thread.
/// </summary>
public sealed class PlayerService
{
    private readonly HttpClient _http;
    private readonly NugsStreamResolver _resolver;
    private readonly NugsAuth _auth;
    private readonly MediaPlayer _player;

    private readonly List<NowPlaying> _queue = new();
    private int _index = -1;
    private long _loadToken;   // only the newest load may touch the player
    private volatile bool _resolving;

    public PlayerService(HttpClient http, NugsStreamResolver resolver, NugsAuth auth)
    {
        _http = http;
        _resolver = resolver;
        _auth = auth;
        _player = new MediaPlayer { AudioCategory = MediaPlayerAudioCategory.Media };

        // System Media Transport Controls: media keys + the system flyout. With a
        // raw (non-playlist) Source the next/prev buttons need explicit wiring.
        _player.CommandManager.IsEnabled = true;
        _player.CommandManager.NextBehavior.EnablingRule = MediaCommandEnablingRule.Always;
        _player.CommandManager.PreviousBehavior.EnablingRule = MediaCommandEnablingRule.Always;
        _player.CommandManager.NextReceived += (_, e) => { Next(); e.Handled = true; };
        _player.CommandManager.PreviousReceived += (_, e) => { Previous(); e.Handled = true; };
        _player.MediaEnded += (_, _) => OnMediaEnded();
    }

    public NowPlaying? Current => _index >= 0 && _index < _queue.Count ? _queue[_index] : null;
    public bool HasNext => _index >= 0 && _index < _queue.Count - 1;
    public bool HasPrevious => _index > 0;
    public bool IsPlaying => _player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;
    public TimeSpan Duration => _player.PlaybackSession.NaturalDuration;

    /// <summary>The queue as the dashboard sees it. Reads only — mutate via Play/Enqueue/PlayNext.</summary>
    public IReadOnlyList<NowPlaying> Queue => _queue;
    public int CurrentIndex => _index;

    /// <summary>Bumped on every queue/index mutation — lets poll-based UI rebuild only on change.</summary>
    public int QueueVersion { get; private set; }

    /// <summary>The resolved stream feeding the player — the transport's format badge.</summary>
    public StreamPick? CurrentPick { get; private set; }

    /// <summary>True while resolving a track or while the pipeline reports Opening/Buffering.</summary>
    public bool IsBuffering =>
        _resolving || _player.PlaybackSession.PlaybackState
            is MediaPlaybackState.Opening or MediaPlaybackState.Buffering;

    public bool IsMuted
    {
        get => _player.IsMuted;
        set => _player.IsMuted = value;
    }

    /// <summary>Last status/error string (e.g. "Resolving…", "No playable stream").</summary>
    public string? Status { get; private set; }

    public TimeSpan Position
    {
        get => _player.PlaybackSession.Position;
        set => _player.PlaybackSession.Position = value;
    }

    public double Volume
    {
        get => _player.Volume;
        set => _player.Volume = Math.Clamp(value, 0, 1);
    }

    /// <summary>Replace the queue with <paramref name="tracks"/> and start at the index.</summary>
    public void Play(IReadOnlyList<NowPlaying> tracks, int startIndex)
    {
        if (tracks.Count == 0) return;
        _queue.Clear();
        _queue.AddRange(tracks);
        _index = Math.Clamp(startIndex, 0, _queue.Count - 1);
        QueueVersion++;
        _ = LoadCurrentAsync();
    }

    /// <summary>Append tracks; start playing them if nothing is playing.</summary>
    public void Enqueue(IReadOnlyList<NowPlaying> tracks)
    {
        if (tracks.Count == 0) return;
        var startHere = Current is null;
        var firstNew = _queue.Count;
        _queue.AddRange(tracks);
        QueueVersion++;
        if (startHere)
        {
            _index = firstNew;
            _ = LoadCurrentAsync();
        }
    }

    /// <summary>Insert tracks right after the current one (or start them if idle).</summary>
    public void PlayNext(IReadOnlyList<NowPlaying> tracks)
    {
        if (tracks.Count == 0) return;
        if (Current is null)
        {
            Play(tracks, 0);
            return;
        }
        _queue.InsertRange(_index + 1, tracks);
        QueueVersion++;
    }

    /// <summary>Jump straight to a queue position — the dashboard's up-next list.</summary>
    public void PlayAt(int index)
    {
        if (index < 0 || index >= _queue.Count || index == _index) return;
        _index = index;
        QueueVersion++;
        _ = LoadCurrentAsync();
    }

    public void Next()
    {
        if (!HasNext) return;
        _index++;
        QueueVersion++;
        _ = LoadCurrentAsync();
    }

    public void Previous()
    {
        if (!HasPrevious) return;
        _index--;
        QueueVersion++;
        _ = LoadCurrentAsync();
    }

    public void TogglePlayPause()
    {
        if (IsPlaying) _player.Pause();
        else _player.Play();
    }

    private void OnMediaEnded()
    {
        if (HasNext)
        {
            _index++;
            QueueVersion++;
            _ = LoadCurrentAsync();
        }
    }

    private async Task LoadCurrentAsync()
    {
        var token = ++_loadToken;
        var track = Current;
        if (track is null)
        {
            _player.Pause();
            return;
        }
        try
        {
            _resolving = true;
            CurrentPick = null;
            Status = $"Resolving “{track.Title}”…";
            var session = await _auth.GetSessionAsync();
            var pick = await _resolver.ResolveBestStreamAsync(track.TrackId, session);
            if (token != _loadToken) return;   // superseded by a newer load

            if (pick is null || pick.Format == AudioFormat.Hls)
            {
                Status = pick is null ? "No playable stream — skipping." : "HLS track — skipping.";
                if (HasNext) { _index++; QueueVersion++; _ = LoadCurrentAsync(); }   // auto-skip unplayable
                return;
            }

            var stream = await HttpAudioStream.CreateAsync(
                _http, pick.Url, NugsConstants.PlayerReferer, NugsConstants.MobileUserAgent);
            if (token != _loadToken) return;

            var source = MediaSource.CreateFromStream(stream, NugsStreamResolver.GetMimeType(pick.Format));
            _player.Source = WithDisplayProperties(source, track);
            _player.Play();
            CurrentPick = pick;
            Status = null;
        }
        catch (Exception ex)
        {
            if (token != _loadToken) return;
            Status = ex.Message;   // network/probe error — stop here, user can retry/skip
        }
        finally
        {
            if (token == _loadToken) _resolving = false;
        }
    }

    /// <summary>
    /// Wraps the source so the system media flyout shows title / artist / album
    /// art instead of a blank entry. Thumbnail failures are the system's to
    /// swallow — text metadata still displays.
    /// </summary>
    private static MediaPlaybackItem WithDisplayProperties(MediaSource source, NowPlaying track)
    {
        var item = new MediaPlaybackItem(source);
        var props = item.GetDisplayProperties();
        props.Type = MediaPlaybackType.Music;
        props.MusicProperties.Title = track.Title ?? "";
        props.MusicProperties.Artist = track.Artist ?? "";
        props.MusicProperties.AlbumTitle = track.Show ?? "";
        if (ArtUri(track.ImagePath) is { } uri)
            props.Thumbnail = RandomAccessStreamReference.CreateFromUri(uri);
        item.ApplyDisplayProperties(props);
        return item;
    }

    /// <summary>Absolute art URL, resolving catalog-relative paths like ImageLoader does.</summary>
    private static Uri? ArtUri(string? pathOrUrl)
    {
        if (string.IsNullOrEmpty(pathOrUrl)) return null;
        var url = pathOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? pathOrUrl
            : $"{NugsConstants.ImageCdnBase}{pathOrUrl}?h=400";
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null;
    }
}
