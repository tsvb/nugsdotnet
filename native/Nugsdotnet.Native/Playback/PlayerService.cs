using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
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

    public PlayerService(HttpClient http, NugsStreamResolver resolver, NugsAuth auth)
    {
        _http = http;
        _resolver = resolver;
        _auth = auth;
        _player = new MediaPlayer { AudioCategory = MediaPlayerAudioCategory.Media };
        _player.CommandManager.IsEnabled = true;   // System Media Transport Controls
        _player.MediaEnded += (_, _) => OnMediaEnded();
    }

    public NowPlaying? Current => _index >= 0 && _index < _queue.Count ? _queue[_index] : null;
    public bool HasNext => _index >= 0 && _index < _queue.Count - 1;
    public bool HasPrevious => _index > 0;
    public bool IsPlaying => _player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;
    public TimeSpan Duration => _player.PlaybackSession.NaturalDuration;

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
        _ = LoadCurrentAsync();
    }

    /// <summary>Append tracks; start playing them if nothing is playing.</summary>
    public void Enqueue(IReadOnlyList<NowPlaying> tracks)
    {
        if (tracks.Count == 0) return;
        var startHere = Current is null;
        var firstNew = _queue.Count;
        _queue.AddRange(tracks);
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
    }

    public void Next()
    {
        if (!HasNext) return;
        _index++;
        _ = LoadCurrentAsync();
    }

    public void Previous()
    {
        if (!HasPrevious) return;
        _index--;
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
            Status = $"Resolving “{track.Title}”…";
            var session = await _auth.GetSessionAsync();
            var pick = await _resolver.ResolveBestStreamAsync(track.TrackId, session);
            if (token != _loadToken) return;   // superseded by a newer load

            if (pick is null || pick.Format == AudioFormat.Hls)
            {
                Status = pick is null ? "No playable stream — skipping." : "HLS track — skipping.";
                if (HasNext) { _index++; _ = LoadCurrentAsync(); }   // auto-skip unplayable
                return;
            }

            var stream = await HttpAudioStream.CreateAsync(
                _http, pick.Url, NugsConstants.PlayerReferer, NugsConstants.MobileUserAgent);
            if (token != _loadToken) return;

            _player.Source = MediaSource.CreateFromStream(stream, NugsStreamResolver.GetMimeType(pick.Format));
            _player.Play();
            Status = null;
        }
        catch (Exception ex)
        {
            if (token != _loadToken) return;
            Status = ex.Message;   // network/probe error — stop here, user can retry/skip
        }
    }
}
