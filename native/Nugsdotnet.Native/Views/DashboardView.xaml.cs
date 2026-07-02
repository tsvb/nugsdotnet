using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Nugsdotnet.Native.Core;
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
/// Right-hand inspector: now playing, stream quality, and the up-next queue.
/// Poll-based like the transport (PlayerService has no events); the queue list
/// rebuilds only when <see cref="PlayerService.QueueVersion"/> moves.
/// </summary>
public sealed partial class DashboardView : UserControl
{
    private readonly PlayerService _player;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private int _queueVersion = -1;

    public DashboardView()
    {
        InitializeComponent();
        _player = App.Services.GetRequiredService<PlayerService>();
        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { OnShown(); _timer.Start(); };
        Unloaded += (_, _) => _timer.Stop();
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
        NpTime.Text = c is null
            ? ""
            : $"{TransportView.Fmt(_player.Position.TotalSeconds)} / {TransportView.Fmt(_player.Duration.TotalSeconds)}";

        var pick = _player.CurrentPick;
        QFormat.Text = pick is null ? "—" : FormatInfo.Badge(pick.Format);
        QFormat.Foreground = (Brush)Application.Current.Resources[
            pick is not null && FormatInfo.IsLossless(pick.Format) ? "BrandAccent" : "BrandText"];
        QSignal.Text = pick is null ? "—" : FormatInfo.Signal(pick.Format);
        QTier.Text = pick is null ? "—" : $"platform {pick.PlatformId}";
        QMime.Text = pick is null ? "—" : NugsStreamResolver.GetMimeType(pick.Format);

        if (_player.QueueVersion != _queueVersion)
        {
            _queueVersion = _player.QueueVersion;
            RebuildQueue();
        }
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
}
