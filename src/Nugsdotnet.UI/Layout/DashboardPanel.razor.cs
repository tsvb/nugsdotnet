using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Nugsdotnet.UI.Services;
using Nugsdotnet.Shared;

namespace Nugsdotnet.UI.Layout;

public partial class DashboardPanel : IDisposable
{
    private StreamInfoResponse? _streamInfo;
    private string? _lastTrackId;

    /// <summary>Centralized live audio snapshot — fed by the layout into PlayerService.</summary>
    private PlaybackStatus? S => Player.Playback;

    protected override void OnInitialized()
    {
        Player.StateChanged += OnPlayerStateChanged;
        Player.PlaybackChanged += OnPlaybackChanged;
        Dashboard.StateChanged += OnDashboardStateChanged;
    }

    private void OnPlayerStateChanged()
    {
        var trackId = Player.Current?.TrackId;
        if (trackId != _lastTrackId)
        {
            _lastTrackId = trackId;
            _streamInfo = null;
            if (trackId is not null) _ = LoadStreamInfoAsync(trackId);
        }
        _ = InvokeAsync(StateHasChanged);
    }

    private void OnPlaybackChanged()
    {
        // Skip the ~5x/sec re-render while collapsed; opening the panel fires
        // Dashboard.StateChanged, which repaints with the latest snapshot.
        if (!Dashboard.PanelOpen) return;
        _ = InvokeAsync(StateHasChanged);
    }

    private async Task LoadStreamInfoAsync(string trackId)
    {
        StreamInfoResponse? result;
        try { result = await Api.GetStreamInfoAsync(trackId); }
        catch { result = null; } // 404 (no stream) / offline — leave blank
        // Only publish if the track hasn't changed under us, so a slow fetch for
        // a previous track can't paint stale specs next to the current one.
        if (_lastTrackId == trackId)
        {
            _streamInfo = result;
            await InvokeAsync(StateHasChanged);
        }
    }

    private void OnDashboardStateChanged() => _ = InvokeAsync(StateHasChanged);

    /// <summary>
    /// Click-to-seek on the now-playing meter (mouse convenience). The transport's
    /// range slider is the keyboard/assistive-tech-accessible seek control, so this
    /// bar is aria-hidden.
    /// </summary>
    private async Task SeekClick(MouseEventArgs e) =>
        await JS.InvokeVoidAsync("audioInterop.seekProgressClick", e.ClientX);

    public void Dispose()
    {
        Player.StateChanged -= OnPlayerStateChanged;
        Player.PlaybackChanged -= OnPlaybackChanged;
        Dashboard.StateChanged -= OnDashboardStateChanged;
    }

    // --- display helpers --------------------------------------------------

    /// <summary>Live track length: prefer the audio element, fall back to parsed specs.</summary>
    private double StreamDuration =>
        S is { Duration: > 0 } ? S.Duration : _streamInfo?.DurationSeconds ?? 0;

    private double ProgressFraction =>
        StreamDuration > 0 ? Math.Clamp((S?.CurrentTime ?? 0) / StreamDuration, 0, 1) : 0;

    private string EffectiveBitrate =>
        S is { DecodedBytes: > 0, CurrentTime: > 0.5 }
            ? (S.DecodedBytes * 8.0 / S.CurrentTime / 1000.0).ToString("0") + " kbps"
            : "—";

    /// <summary>
    /// Approximate bytes fetched so far = seconds buffered (start → buffered-range
    /// end) × the parsed average bitrate. A range-streamed media element can't be
    /// measured via the Resource Timing API, so we estimate from the exact bitrate
    /// parsed server-side. "≈" signals it's an estimate (bitrate is a file average).
    /// </summary>
    private string DownloadedLabel
    {
        get
        {
            if (S is null || _streamInfo?.AvgBitrateKbps is not double kbps || kbps <= 0)
                return "—";
            var bytes = (long)((S.CurrentTime + S.BufferedAhead) * kbps * 1000 / 8);
            return bytes > 0 ? "≈ " + FmtBytes(bytes) : "—";
        }
    }

    private static string Pct(double fraction) =>
        (fraction * 100).ToString("0.##", CultureInfo.InvariantCulture) + "%";

    private static string Fmt(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) seconds = 0;
        var ts = TimeSpan.FromSeconds(seconds);
        return ((int)ts.TotalMinutes).ToString(CultureInfo.InvariantCulture) + ":" + ts.Seconds.ToString("00");
    }

    private static string FmtKhz(int hz) =>
        (hz / 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + " kHz";

    private static string FmtBytes(long b) => b switch
    {
        >= 1_048_576 => (b / 1_048_576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB",
        >= 1024 => (b / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB",
        _ => b + " B",
    };

    private static string ChannelLabel(int ch) => ch switch
    {
        1 => "Mono",
        2 => "Stereo",
        _ => ch + " ch",
    };

    private static string NetworkLabel(int n) => n switch
    {
        0 => "empty",
        1 => "idle",
        2 => "loading",
        3 => "no source",
        _ => n.ToString(),
    };

    private static string ReadyLabel(int n) => n switch
    {
        0 => "nothing",
        1 => "metadata",
        2 => "current",
        3 => "future",
        4 => "enough",
        _ => n.ToString(),
    };
}
