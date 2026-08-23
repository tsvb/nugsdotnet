using Nugsdotnet.Native.Core;

namespace Nugsdotnet.Native.Playback;

/// <summary>
/// Feeds the listening journal from live playback. A 15 s tick credits elapsed
/// seconds to the current night/show/artist while the player reports Playing
/// (paused time never counts), and watches track transitions to count
/// completions (natural advance, ≥ 95 % position, or a repeat-one position
/// wrap). State is held in memory and flushed to journal.json every ~60 s, on
/// stop, and on app close — a crash loses at most ~60 s. Journal failures are
/// swallowed: nothing here may break playback.
/// </summary>
public sealed class JournalTracker
{
    private const int TickSeconds = 15;
    private const int FlushEveryTicks = 4;   // ~60 s

    private readonly PlayerService _player;
    private readonly ListeningJournal _journal;
    private readonly Timer _timer;
    private readonly object _gate = new();

    private JournalState _state = ListeningJournal.Empty();
    private string? _lastTrackId;
    private double _lastPosition;
    private double _lastDuration;
    private DateTime _lastTickAt = DateTime.Now;
    private bool _running;
    private int _ticks;

    public JournalTracker(PlayerService player, ListeningJournal journal)
    {
        _player = player;
        _journal = journal;
        _timer = new Timer(_ => Tick(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public async Task StartAsync()
    {
        lock (_gate)
        {
            if (_running) return;
            _running = true;
            _ticks = 0;
            _lastTrackId = null;
            _lastPosition = _lastDuration = 0;
            _lastTickAt = DateTime.Now;
        }
        _state = await _journal.LoadAsync();
        _timer.Change(TimeSpan.FromSeconds(TickSeconds), TimeSpan.FromSeconds(TickSeconds));
    }

    public async Task StopAsync()
    {
        lock (_gate)
        {
            if (!_running) return;
            _running = false;
        }
        _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        await FlushNowAsync();
        lock (_gate)
        {
            _state = ListeningJournal.Empty();
            _lastTrackId = null;
        }
    }

    /// <summary>Best-effort flush (app close, periodic). Never throws.</summary>
    public async Task FlushNowAsync()
    {
        JournalState snapshot;
        lock (_gate) snapshot = _state;
        await _journal.SaveAsync(ListeningJournal.Prune(snapshot));
    }

    private void Tick()
    {
        try
        {
            TickUnguarded();
        }
        catch
        {
            // a journal tick must never take the app down
        }
    }

    private void TickUnguarded()
    {
        var now = DateTime.Now;
        var current = _player.Current;
        var playing = _player.IsPlaying;
        var position = _player.Position.TotalSeconds;
        var duration = _player.Duration.TotalSeconds;

        lock (_gate)
        {
            if (!_running) return;

            var trackId = current?.TrackId;
            var completed = 0L;

            // Track transition since the last tick: did the previous track finish?
            if (_lastTrackId is not null && trackId != _lastTrackId
                && JournalMath.TrackCompleted(_lastPosition, _lastDuration))
            {
                completed++;
            }
            // Same track but position wrapped backwards: repeat-one (or a manual
            // replay) rolled over — the previous playthrough finished.
            else if (_lastTrackId is not null && trackId == _lastTrackId
                     && position + 5 < _lastPosition
                     && JournalMath.TrackCompleted(_lastPosition, _lastDuration))
            {
                completed++;
            }

            if (playing && current is not null)
            {
                var delta = Math.Clamp((now - _lastTickAt).TotalSeconds, 0, TickSeconds * 2);
                if (delta > 0 || completed > 0)
                {
                    _state = ListeningJournal.Accumulate(
                        _state,
                        now.ToString("yyyy-MM-dd"),
                        current.ContainerId ?? "",
                        current.Artist,
                        current.Show,
                        delta,
                        completed);
                }
            }

            _lastTrackId = trackId;
            _lastPosition = position;
            _lastDuration = duration;
            _lastTickAt = now;

            if (++_ticks % FlushEveryTicks == 0)
                _ = FlushNowAsync();
        }
    }
}
