namespace Nugsdotnet.UI.Services;

/// <summary>
/// One track in the queue. Plain data — no logic.
/// </summary>
public sealed record NowPlaying(
    string TrackId, string? Title, string? Artist, string? Show = null);

/// <summary>
/// What pages send to <see cref="PlayerService.Play"/> when the user clicks a
/// track. The full contextual list is included so the queue can autoplay
/// past the clicked item.
/// </summary>
public sealed record PlayRequest(IReadOnlyList<NowPlaying> Tracks, int StartIndex);

/// <summary>
/// How a non-replacing queue change happened — used by the UI to show a brief
/// confirmation toast.
/// </summary>
public enum QueueOp { Enqueued, PlayingNext }

/// <summary>
/// Live runtime snapshot of the &lt;audio&gt; element, pushed from JS interop.
/// Held centrally by <see cref="PlayerService"/> so the transport and the
/// dashboard share one source of truth for paused / position / volume.
/// </summary>
public sealed record PlaybackStatus(
    double CurrentTime,
    double Duration,
    double BufferedAhead,
    int NetworkState,
    int ReadyState,
    bool Paused,
    double Volume,
    double PlaybackRate,
    long DecodedBytes,
    int RebufferCount);

/// <summary>
/// In-memory queue for the audio player. Lives in DI so it survives page
/// navigations (the audio element and JS interop live in MainLayout, which
/// also persists; together they keep playback continuous as the user
/// navigates between routes).
/// </summary>
public sealed class PlayerService
{
    private readonly List<NowPlaying> _queue = new();
    private int _index;

    // True once the last track has played to the end and the audio element is
    // sitting idle. Lets Enqueue/PlayNext know they should start playback
    // rather than silently appending behind a finished queue.
    private bool _ended;

    public IReadOnlyList<NowPlaying> Queue => _queue;
    public int Index => _index;
    public NowPlaying? Current =>
        _index >= 0 && _index < _queue.Count ? _queue[_index] : null;
    public bool HasPrevious => _index > 0;
    public bool HasNext => _index < _queue.Count - 1;

    /// <summary>UI re-render trigger — fires for any state change.</summary>
    public event Action? StateChanged;

    /// <summary>Layout listens to this and invokes JS interop to actually play.</summary>
    public event Action? TrackChangeRequested;

    /// <summary>
    /// Fired when tracks are added without replacing the queue (enqueue /
    /// play-next). The layout uses it to show a brief confirmation toast.
    /// </summary>
    public event Action<QueueOp>? QueueChanged;

    /// <summary>Latest audio runtime snapshot — null until playback first starts.</summary>
    public PlaybackStatus? Playback { get; private set; }

    /// <summary>
    /// Fires as the audio runtime snapshot updates (~5x/sec while playing, plus
    /// immediately on play/pause/volume changes). Pushed from JS via the layout.
    /// </summary>
    public event Action? PlaybackChanged;

    /// <summary>Called by the layout when JS pushes a fresh &lt;audio&gt; snapshot.</summary>
    public void SetPlayback(PlaybackStatus status)
    {
        Playback = status;
        PlaybackChanged?.Invoke();
    }

    /// <summary>True when a track is loaded and actively playing (not paused).</summary>
    public bool IsPlaying => Current is not null && Playback is { Paused: false };

    public void Play(PlayRequest req)
    {
        if (req.Tracks.Count == 0) return;
        _queue.Clear();
        _queue.AddRange(req.Tracks);
        StartAt(Math.Clamp(req.StartIndex, 0, _queue.Count - 1));
    }

    /// <summary>
    /// Append tracks to the end of the queue. Current playback is untouched
    /// (a brief toast confirms the add) — unless nothing is playing (the queue
    /// is empty or has already finished), in which case playback starts at the
    /// first newly-added track and the visible now-playing change is feedback
    /// enough, so no toast is raised.
    /// </summary>
    public void Enqueue(IReadOnlyList<NowPlaying> tracks)
    {
        if (tracks.Count == 0) return;
        var startHere = Current is null || _ended;
        var firstNew = _queue.Count;
        _queue.AddRange(tracks);
        if (startHere)
        {
            StartAt(firstNew);
        }
        else
        {
            StateChanged?.Invoke();
            QueueChanged?.Invoke(QueueOp.Enqueued);
        }
    }

    /// <summary>
    /// Insert tracks immediately after the current track so they play next. If
    /// nothing is playing, behaves like <see cref="Enqueue"/> and starts at the
    /// first inserted track. (When the queue has finished, the cursor sits on
    /// the last track, so appending is equivalent to inserting next.)
    /// </summary>
    public void PlayNext(IReadOnlyList<NowPlaying> tracks)
    {
        if (tracks.Count == 0) return;
        if (Current is null || _ended)
        {
            var firstNew = _queue.Count;
            _queue.AddRange(tracks);
            StartAt(firstNew);
        }
        else
        {
            _queue.InsertRange(_index + 1, tracks);
            StateChanged?.Invoke();
            QueueChanged?.Invoke(QueueOp.PlayingNext);
        }
    }

    /// <summary>Jump straight to a track already in the queue and play it.</summary>
    public void JumpTo(int index)
    {
        if (index < 0 || index >= _queue.Count || index == _index) return;
        StartAt(index);
    }

    /// <summary>
    /// Remove a track from the queue. Playback continues uninterrupted unless the
    /// currently-playing track is removed, in which case whatever slides into its
    /// slot starts playing (or playback ends if the queue empties).
    /// </summary>
    public void RemoveAt(int index)
    {
        if (index < 0 || index >= _queue.Count) return;
        var wasCurrent = index == _index;
        _queue.RemoveAt(index);

        if (_queue.Count == 0)
        {
            _index = 0;
            _ended = false;
            StateChanged?.Invoke();
            TrackChangeRequested?.Invoke();  // emptied → layout stops the audio
        }
        else if (index < _index)
        {
            _index--;                       // keep the cursor on the same track
            StateChanged?.Invoke();
        }
        else if (wasCurrent)
        {
            _index = Math.Min(_index, _queue.Count - 1);
            _ended = false;
            StateChanged?.Invoke();
            TrackChangeRequested?.Invoke();  // play whatever now occupies the slot
        }
        else
        {
            StateChanged?.Invoke();          // removed an upcoming track
        }
    }

    public void Next()
    {
        if (!HasNext) return;
        StartAt(_index + 1);
    }

    public void Previous()
    {
        if (!HasPrevious) return;
        StartAt(_index - 1);
    }

    public void Clear()
    {
        _queue.Clear();
        _index = 0;
        _ended = false;
        StateChanged?.Invoke();
        TrackChangeRequested?.Invoke();  // nothing current → layout stops the audio
    }

    /// <summary>Fired by the audio element's `ended` event in MainLayout.</summary>
    public void HandleEnded()
    {
        if (HasNext) Next();
        else _ended = true;  // queue finished — next enqueue/play-next restarts playback
    }

    /// <summary>
    /// Move the cursor to <paramref name="index"/> and begin playing it: clears
    /// the finished flag and raises both the UI re-render and the audio
    /// side-effect events. Shared by Play/Next/Previous and the idle-start path
    /// of Enqueue/PlayNext.
    /// </summary>
    private void StartAt(int index)
    {
        _index = index;
        _ended = false;
        StateChanged?.Invoke();
        TrackChangeRequested?.Invoke();
    }
}
