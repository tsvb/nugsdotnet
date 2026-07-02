using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Nugsdotnet.Native.Imaging;
using Nugsdotnet.Native.Playback;
using Windows.UI;

namespace Nugsdotnet.Native.Views;

public sealed partial class TransportView : UserControl
{
    // Segoe Fluent Icons glyphs (monochrome, inherit Foreground).
    private const string PlayGlyph = "\uE768";
    private const string PauseGlyph = "\uE769";
    private const string VolumeGlyph = "\uE767";
    private const string MuteGlyph = "\uE74F";

    private readonly PlayerService _player;
    private readonly ImageLoader _images;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly ProgressRing _buffering = new()
    {
        Width = 16,
        Height = 16,
        IsActive = true,
        Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x1A, 0x12, 0x06)),   // dark on amber
    };

    private bool _scrubbing;
    private string? _thumbTrackId;
    private string? _badge;

    /// <summary>Raised when the art is clicked — the shell navigates to the album.</summary>
    public event Action<string>? AlbumRequested;

    public TransportView()
    {
        InitializeComponent();
        _player = App.Services.GetRequiredService<PlayerService>();
        _images = App.Services.GetRequiredService<ImageLoader>();
        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();

        // Slider marks pointer events handled inside its template, so XAML-wired
        // handlers never fire (drags fought the poll timer and seeks were lost).
        // handledEventsToo:true sees them anyway; CaptureLost/Canceled end a drag
        // that leaves the control.
        PositionSlider.AddHandler(PointerPressedEvent, new PointerEventHandler(OnSeekStart), true);
        PositionSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnSeekEnd), true);
        PositionSlider.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnSeekEnd), true);
        PositionSlider.AddHandler(PointerCanceledEvent, new PointerEventHandler(OnSeekEnd), true);
    }

    /// <summary>Polls the player ~4×/sec on the UI thread.</summary>
    private void Refresh()
    {
        RefreshPlayButton();
        PrevButton.IsEnabled = _player.HasPrevious;
        NextButton.IsEnabled = _player.HasNext;

        var c = _player.Current;
        Back15Button.IsEnabled = c is not null;
        Fwd30Button.IsEnabled = c is not null;
        NowPlayingText.Text = _player.Status ?? c?.Title ?? "";
        NowPlayingSub.Text = _player.Status is not null || c is null
            ? ""
            : string.Join("  ·  ", new[] { c.Artist, c.Show }.Where(s => !string.IsNullOrEmpty(s)));
        UpdateThumb();
        RefreshBadge();

        VolumeButton.Content = _player.IsMuted ? MuteGlyph : VolumeGlyph;

        var dur = _player.Duration.TotalSeconds;
        var pos = _player.Position.TotalSeconds;
        if (!_scrubbing)
        {
            PositionSlider.Maximum = dur > 0 ? dur : 1;
            PositionSlider.Value = Math.Clamp(pos, 0, PositionSlider.Maximum);
        }
        ElapsedText.Text = Fmt(pos);
        RemainText.Text = $"-{Fmt(Math.Max(0, dur - pos))}";
    }

    /// <summary>Glyph ↔ spinner swap, guarded so content isn't churned per tick.</summary>
    private void RefreshPlayButton()
    {
        if (_player.IsBuffering)
        {
            if (!ReferenceEquals(PlayPauseButton.Content, _buffering))
                PlayPauseButton.Content = _buffering;
            return;
        }
        var glyph = _player.IsPlaying ? PauseGlyph : PlayGlyph;
        if (PlayPauseButton.Content as string != glyph)
            PlayPauseButton.Content = glyph;
    }

    private void RefreshBadge()
    {
        var pick = _player.CurrentPick;
        if (pick is null)
        {
            if (_badge is not null) { _badge = null; BadgePanel.Visibility = Visibility.Collapsed; }
            return;
        }
        var badge = FormatInfo.Badge(pick.Format);
        if (badge == _badge) return;
        _badge = badge;
        BadgeText.Text = badge;
        var brush = (Brush)Application.Current.Resources[
            FormatInfo.IsLossless(pick.Format) ? "BrandAccent" : "BrandDim"];
        BadgeText.Foreground = brush;
        BadgePanel.BorderBrush = brush;
        BadgePanel.Visibility = Visibility.Visible;
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

    private void OnArtClick(object sender, RoutedEventArgs e)
    {
        if (_player.Current?.ContainerId is { Length: > 0 } id) AlbumRequested?.Invoke(id);
    }

    private void OnPlayPause(object sender, RoutedEventArgs e) => _player.TogglePlayPause();
    private void OnPrev(object sender, RoutedEventArgs e) => _player.Previous();
    private void OnNext(object sender, RoutedEventArgs e) => _player.Next();
    private void OnSkipBack(object sender, RoutedEventArgs e) => _player.SkipBack();
    private void OnSkipForward(object sender, RoutedEventArgs e) => _player.SkipForward();
    private void OnMuteToggle(object sender, RoutedEventArgs e) => _player.IsMuted = !_player.IsMuted;

    private void OnSeekStart(object sender, PointerRoutedEventArgs e) => _scrubbing = true;

    private void OnSeekEnd(object sender, PointerRoutedEventArgs e)
    {
        if (!_scrubbing) return;   // CaptureLost follows Released — apply once
        _scrubbing = false;
        _player.Position = TimeSpan.FromSeconds(PositionSlider.Value);
    }

    private void OnVolumeChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_player is null) return;   // fires during XAML init before _player is set
        _player.Volume = e.NewValue / 100.0;
    }

    internal static string Fmt(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) seconds = 0;
        var ts = TimeSpan.FromSeconds(seconds);
        return $"{(int)ts.TotalMinutes}:{ts.Seconds:00}";
    }
}
