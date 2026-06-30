using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Nugsdotnet.Native.Imaging;
using Nugsdotnet.Native.Playback;

namespace Nugsdotnet.Native.Views;

public sealed partial class TransportView : UserControl
{
    // Segoe Fluent Icons glyphs (monochrome, inherit Foreground).
    private const string PlayGlyph = "\uE768";   // Segoe Fluent: Play
    private const string PauseGlyph = "\uE769";  // Segoe Fluent: Pause

    private readonly PlayerService _player;
    private readonly ImageLoader _images;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool _scrubbing;
    private string? _thumbTrackId;

    public TransportView()
    {
        InitializeComponent();
        _player = App.Services.GetRequiredService<PlayerService>();
        _images = App.Services.GetRequiredService<ImageLoader>();
        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    /// <summary>Polls the player ~4×/sec on the UI thread.</summary>
    private void Refresh()
    {
        PlayPauseButton.Content = _player.IsPlaying ? PauseGlyph : PlayGlyph;
        NowPlayingText.Text = _player.Status ?? NowPlayingLabel();
        UpdateThumb();

        var dur = _player.Duration.TotalSeconds;
        var pos = _player.Position.TotalSeconds;
        if (!_scrubbing)
        {
            PositionSlider.Maximum = dur > 0 ? dur : 1;
            PositionSlider.Value = Math.Clamp(pos, 0, PositionSlider.Maximum);
        }
        TimeText.Text = $"{Fmt(pos)} / {Fmt(dur)}";
    }

    private string NowPlayingLabel()
    {
        var c = _player.Current;
        if (c is null) return "";
        return string.IsNullOrEmpty(c.Artist) ? (c.Title ?? "") : $"{c.Title} — {c.Artist}";
    }

    /// <summary>Reloads the thumbnail only when the current track changes.</summary>
    private async void UpdateThumb()
    {
        var c = _player.Current;
        if (c?.TrackId == _thumbTrackId) return;
        _thumbTrackId = c?.TrackId;
        Thumb.Source = null;
        if (c?.ImagePath is { Length: > 0 } path)
            Thumb.Source = await _images.LoadAsync(path);
    }

    private void OnPlayPause(object sender, RoutedEventArgs e) => _player.TogglePlayPause();
    private void OnPrev(object sender, RoutedEventArgs e) => _player.Previous();
    private void OnNext(object sender, RoutedEventArgs e) => _player.Next();

    private void OnSeekStart(object sender, PointerRoutedEventArgs e) => _scrubbing = true;

    private void OnSeekEnd(object sender, PointerRoutedEventArgs e)
    {
        _scrubbing = false;
        _player.Position = TimeSpan.FromSeconds(PositionSlider.Value);
    }

    private void OnVolumeChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_player is null) return;   // fires during XAML init before _player is set
        _player.Volume = e.NewValue / 100.0;
    }

    private static string Fmt(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) seconds = 0;
        var ts = TimeSpan.FromSeconds(seconds);
        return $"{(int)ts.TotalMinutes}:{ts.Seconds:00}";
    }
}
