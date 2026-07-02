using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Nugsdotnet.Native.Core;
using Nugsdotnet.Native.Imaging;
using Nugsdotnet.Native.Playback;

namespace Nugsdotnet.Native.Views;

/// <summary>One row of the up-next list. Rebuilt wholesale on queue change, so
/// plain get-only properties are enough for the (OneTime) compiled bindings.</summary>
public sealed class QueueRow
{
    public int Index { get; init; }
    public string Title { get; init; } = "";
    public bool IsCurrent { get; init; }

    public string Position => $"{Index + 1:00}";
    public Brush TitleBrush =>
        (Brush)Application.Current.Resources[IsCurrent ? "BrandAccent" : "BrandText"];
}

/// <summary>
/// Right-hand inspector: a mini player (art, seek, transport), the signal-path
/// metrics of the file being played, and the up-next queue. Poll-based like the
/// transport (PlayerService has no events); the queue list rebuilds only when
/// <see cref="PlayerService.QueueVersion"/> moves.
/// </summary>
public sealed partial class DashboardView : UserControl
{
    private readonly PlayerService _player;
    private readonly ImageLoader _images;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private int _queueVersion = -1;
    private string? _artTrackId;
    private bool _scrubbing;

    public DashboardView()
    {
        InitializeComponent();
        _player = App.Services.GetRequiredService<PlayerService>();
        _images = App.Services.GetRequiredService<ImageLoader>();
        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { OnShown(); _timer.Start(); };
        Unloaded += (_, _) => _timer.Stop();

        // Same story as the transport slider: Slider handles pointer events
        // internally, so scrub detection must see handled events too.
        DashSlider.AddHandler(PointerPressedEvent, new PointerEventHandler(OnSeekStart), true);
        DashSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnSeekEnd), true);
        DashSlider.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnSeekEnd), true);
        DashSlider.AddHandler(PointerCanceledEvent, new PointerEventHandler(OnSeekEnd), true);
    }

    /// <summary>Forces a full refresh — called by the shell when the panel opens.</summary>
    public void OnShown()
    {
        _queueVersion = -1;
        Refresh();
    }

    private void Refresh()
    {
        if (Visibility == Visibility.Collapsed) return;

        var c = _player.Current;
        NpTitle.Text = c?.Title ?? "Nothing on the deck";
        NpArtist.Text = c?.Artist ?? "";
        NpShow.Text = c?.Show ?? "";
        UpdateArt();
        RefreshMiniTransport(c is not null);
        RefreshMetrics();

        if (_player.QueueVersion != _queueVersion)
        {
            _queueVersion = _player.QueueVersion;
            RebuildQueue();
        }
    }

    private void RefreshMiniTransport(bool hasTrack)
    {
        DashPrev.IsEnabled = _player.HasPrevious;
        DashNext.IsEnabled = _player.HasNext;
        DashBack15.IsEnabled = hasTrack;
        DashFwd30.IsEnabled = hasTrack;

        var glyph = _player.IsPlaying ? "\uE769" : "\uE768";   // Pause : Play
        if (DashPlay.Content as string != glyph) DashPlay.Content = glyph;

        var dur = _player.Duration.TotalSeconds;
        var pos = _player.Position.TotalSeconds;
        if (!_scrubbing)
        {
            DashSlider.Maximum = dur > 0 ? dur : 1;
            DashSlider.Value = Math.Clamp(pos, 0, DashSlider.Maximum);
        }
        DashElapsed.Text = TransportView.Fmt(pos);
        DashTotal.Text = TransportView.Fmt(dur);
    }

    private void RefreshMetrics()
    {
        var pick = _player.CurrentPick;
        QFormat.Text = pick is null ? "—" : FormatInfo.Badge(pick.Format);
        QFormat.Foreground = (Brush)Application.Current.Resources[
            pick is not null && FormatInfo.IsLossless(pick.Format) ? "BrandAccent" : "BrandText"];
        QSignal.Text = pick is null ? "—" : FormatInfo.Signal(pick.Format);
        QTier.Text = pick is null ? "—" : $"platform {pick.PlatformId}";
        QMime.Text = pick is null ? "—" : NugsStreamResolver.GetMimeType(pick.Format);

        // Measured, not inferred: size from the CDN's Content-Range, average
        // bitrate from size÷duration, I/O straight off the range-read counters.
        var stream = _player.CurrentStream;
        if (stream is null)
        {
            QSize.Text = QBitrate.Text = QIo.Text = "—";
            return;
        }
        var sizeMb = stream.Size / 1048576.0;
        QSize.Text = $"{sizeMb:0.0} MB";

        var dur = _player.Duration.TotalSeconds;
        QBitrate.Text = dur > 0 && stream.Size > 0
            ? $"{stream.Size * 8 / dur / 1000:0} kbps avg"
            : "—";

        var stats = stream.Stats;
        QIo.Text = $"{stats.BytesFetched / 1048576.0:0.0} MB · {stats.RangeReads} reads";
    }

    private async void UpdateArt()
    {
        var c = _player.Current;
        if (c?.TrackId == _artTrackId) return;
        _artTrackId = c?.TrackId;
        DashArt.Source = null;
        if (c?.ImagePath is { Length: > 0 } path)
            DashArt.Source = await _images.LoadAsync(path);   // in-memory cached
    }

    private void RebuildQueue()
    {
        var queue = _player.Queue;
        var rows = new List<QueueRow>(queue.Count);
        for (var i = 0; i < queue.Count; i++)
        {
            rows.Add(new QueueRow
            {
                Index = i,
                Title = queue[i].Title ?? "(untitled)",
                IsCurrent = i == _player.CurrentIndex,
            });
        }
        QueueList.ItemsSource = rows;
        QueueCount.Text = rows.Count == 0 ? "" : $"{_player.CurrentIndex + 1} / {rows.Count}";
        QueueEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnQueueClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is QueueRow r) _player.PlayAt(r.Index);
    }

    private void OnPlayPause(object sender, RoutedEventArgs e) => _player.TogglePlayPause();
    private void OnPrev(object sender, RoutedEventArgs e) => _player.Previous();
    private void OnNext(object sender, RoutedEventArgs e) => _player.Next();
    private void OnSkipBack(object sender, RoutedEventArgs e) => _player.SkipBack();
    private void OnSkipForward(object sender, RoutedEventArgs e) => _player.SkipForward();

    private void OnSeekStart(object sender, PointerRoutedEventArgs e) => _scrubbing = true;

    private void OnSeekEnd(object sender, PointerRoutedEventArgs e)
    {
        if (!_scrubbing) return;   // CaptureLost follows Released — apply once
        _scrubbing = false;
        _player.Position = TimeSpan.FromSeconds(DashSlider.Value);
    }
}
