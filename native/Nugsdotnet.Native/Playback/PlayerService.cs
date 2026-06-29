using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Nugsdotnet.Native.Audio;
using Nugsdotnet.Native.Core;

namespace Nugsdotnet.Native.Playback;

/// <summary>
/// Wraps a single <see cref="MediaPlayer"/> driven by a <see cref="MediaPlaybackList"/>.
/// Phase 1 plays one track at a time; the list is already in place so later phases
/// get gapless queueing for free (the App SDK pre-buffers the next item itself —
/// no hand-rolled dual-element swap like the web head needed).
///
/// Audio is fed from <see cref="HttpAudioStream"/>, so the required Referer/UA
/// headers are applied per range request and no proxy server is involved.
/// </summary>
public sealed class PlayerService
{
    private readonly HttpClient _http;
    private readonly MediaPlayer _player;
    private readonly MediaPlaybackList _list = new();

    public PlayerService(HttpClient http)
    {
        _http = http;
        _player = new MediaPlayer { AudioCategory = MediaPlayerAudioCategory.Media };
        _player.CommandManager.IsEnabled = true;   // wire System Media Transport Controls
        _player.Source = _list;
    }

    public bool IsPlaying => _player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;
    public TimeSpan Duration => _player.PlaybackSession.NaturalDuration;

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

    /// <summary>Resolves the CDN stream into the player and starts playback.</summary>
    public async Task PlaySingleAsync(
        StreamPick pick, string? title, string? artist, CancellationToken ct = default)
    {
        var stream = await HttpAudioStream.CreateAsync(
            _http, pick.Url, NugsConstants.PlayerReferer, NugsConstants.MobileUserAgent, ct);

        var source = MediaSource.CreateFromStream(stream, NugsStreamResolver.GetMimeType(pick.Format));
        var item = new MediaPlaybackItem(source);

        var props = item.GetDisplayProperties();
        props.Type = MediaPlaybackType.Music;
        props.MusicProperties.Title = title ?? "";
        props.MusicProperties.Artist = artist ?? "";
        item.ApplyDisplayProperties(props);

        _list.Items.Clear();
        _list.Items.Add(item);
        _player.Play();
    }

    public void Play() => _player.Play();

    public void Pause() => _player.Pause();

    public void TogglePlayPause()
    {
        if (IsPlaying) _player.Pause();
        else _player.Play();
    }
}
