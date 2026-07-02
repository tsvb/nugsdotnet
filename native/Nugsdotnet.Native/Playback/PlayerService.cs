using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.Streaming.Adaptive;
using Windows.Storage.Streams;
using Nugsdotnet.Native.Audio;
using Nugsdotnet.Native.Core;

namespace Nugsdotnet.Native.Playback;

/// <summary>
/// A real play queue over a single <see cref="MediaPlayer"/> — now gapless.
/// The current track plays from a <see cref="MediaPlaybackList"/> while the
/// next track's CDN stream resolves in the background and is appended, so
/// Media Foundation pre-rolls it and a set flows track-into-track. Tracks are
/// still resolved lazily (one ahead, not the whole queue): signed CDN URLs
/// age, and probing every tier for 30 tracks up front would hammer the API.
///
/// If the look-ahead hasn't landed when a track ends (slow resolve, dead
/// stream), MediaEnded falls back to the old resolve-on-advance path — a gap,
/// but never a stall. HLS-only tracks play via an adaptive source; anything
/// else streams through <see cref="HttpAudioStream"/>. The queue, position,
/// and listening setup persist to <see cref="PlaybackStateStore"/> for
/// resume-on-launch.
///
/// No UI thread affinity: state is polled by the transport on a UI timer.
/// List/queue bookkeeping is guarded by <see cref="_gate"/> because
/// CurrentItemChanged and the SMTC commands arrive on media threads.
/// </summary>
public sealed class PlayerService
{
    private readonly HttpClient _http;
    private readonly NugsStreamResolver _resolver;
    private readonly NugsAuth _auth;
    private readonly PlaybackStateStore _state;
    private readonly MediaPlayer _player;
    private readonly Timer _saveTimer;

    private readonly object _gate = new();
    private readonly List<NowPlaying> _queue = new();
    private readonly Dictionary<MediaPlaybackItem, ItemInfo> _items = new();
    private MediaPlaybackList? _list;
    private int _index = -1;
    private long _loadToken;          // only the newest rebuild may commit
    private volatile bool _resolving; // a rebuild is resolving the *current* track
    private bool _lookahead;          // a next-track resolve is already in flight
    private TimeSpan? _pendingSeek;   // resume point, applied on the next MediaOpened
    private bool _restored;           // RestoreAsync runs at most once
    private Windows.Web.Http.HttpClient? _hlsHttp;

    /// <summary>What a playback-list item corresponds to: queue slot + resolved
    /// stream. Stream is null for HLS (adaptive source — no byte stream to meter).</summary>
    private sealed record ItemInfo(int QueueIndex, StreamPick Pick, HttpAudioStream? Stream);

    /// <summary>A resolved, playable source for one track.</summary>
    private sealed record Resolved(StreamPick Pick, MediaSource Source, HttpAudioStream? Stream);

    public PlayerService(HttpClient http, NugsStreamResolver resolver, NugsAuth auth, PlaybackStateStore state)
    {
        _http = http;
        _resolver = resolver;
        _auth = auth;
        _state = state;
        _player = new MediaPlayer { AudioCategory = MediaPlayerAudioCategory.Media };

        // System Media Transport Controls: media keys + the system flyout. Our
        // handlers keep queue bookkeeping in charge (Handled suppresses the
        // list's own default next/prev).
        _player.CommandManager.IsEnabled = true;
        _player.CommandManager.NextBehavior.EnablingRule = MediaCommandEnablingRule.Always;
        _player.CommandManager.PreviousBehavior.EnablingRule = MediaCommandEnablingRule.Always;
        _player.CommandManager.NextReceived += (_, e) => { Next(); e.Handled = true; };
        _player.CommandManager.PreviousReceived += (_, e) => { Previous(); e.Handled = true; };

        // Fires when the *list* is exhausted — i.e. the look-ahead never landed.
        _player.MediaEnded += (_, _) => OnListEnded();

        // Resume-on-launch: the saved position is applied once the restored
        // track's media actually opens (seeking before open is a no-op).
        _player.MediaOpened += (_, _) =>
        {
            TimeSpan? seek;
            lock (_gate)
            {
                seek = _pendingSeek;
                _pendingSeek = null;
            }
            if (seek is { } t) _player.PlaybackSession.Position = t;
        };

        // Position snapshots while playing, so a crash loses at most ~10s.
        _saveTimer = new Timer(_ => { if (IsPlaying) SaveSoon(); },
            null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    public NowPlaying? Current
    {
        get { lock (_gate) return _index >= 0 && _index < _queue.Count ? _queue[_index] : null; }
    }

    public bool HasNext { get { lock (_gate) return _index >= 0 && _index < _queue.Count - 1; } }
    public bool HasPrevious { get { lock (_gate) return _index > 0; } }
    public bool IsPlaying => _player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;
    public TimeSpan Duration => _player.PlaybackSession.NaturalDuration;

    /// <summary>The queue as the dashboard sees it. Reads only — mutate via Play/Enqueue/PlayNext.</summary>
    public IReadOnlyList<NowPlaying> Queue { get { lock (_gate) return _queue.ToArray(); } }
    public int CurrentIndex { get { lock (_gate) return _index; } }

    /// <summary>Bumped on every queue/index mutation — lets poll-based UI rebuild only on change.</summary>
    public int QueueVersion { get; private set; }

    /// <summary>The resolved stream feeding the player — the transport's format badge.</summary>
    public StreamPick? CurrentPick { get; private set; }

    /// <summary>The live byte stream under the player — the dashboard's file metrics
    /// (total size + I/O counters). Null until a track is loaded.</summary>
    public HttpAudioStream? CurrentStream { get; private set; }

    /// <summary>True while resolving the current track or while the pipeline reports Opening/Buffering.</summary>
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
        int start;
        lock (_gate)
        {
            _queue.Clear();
            _queue.AddRange(tracks);
            start = _index = Math.Clamp(startIndex, 0, _queue.Count - 1);
            _pendingSeek = null;   // explicit navigation cancels a pending resume point
            QueueVersion++;
        }
        SaveSoon();
        _ = RebuildAtAsync(start);
    }

    /// <summary>Append tracks; start playing them if nothing is playing.</summary>
    public void Enqueue(IReadOnlyList<NowPlaying> tracks)
    {
        if (tracks.Count == 0) return;
        bool startHere;
        int start;
        lock (_gate)
        {
            startHere = _index < 0 || _index >= _queue.Count;
            start = _queue.Count;
            _queue.AddRange(tracks);
            if (startHere)
            {
                _index = start;
                _pendingSeek = null;
            }
            QueueVersion++;
        }
        SaveSoon();
        if (startHere) _ = RebuildAtAsync(start);
        else _ = EnsureLookaheadAsync();   // the current track may have been the last
    }

    /// <summary>Insert tracks right after the current one (or start them if idle).</summary>
    public void PlayNext(IReadOnlyList<NowPlaying> tracks)
    {
        if (tracks.Count == 0) return;
        var inserted = false;
        lock (_gate)
        {
            if (_index >= 0 && _index < _queue.Count)
            {
                _queue.InsertRange(_index + 1, tracks);
                QueueVersion++;
                DropLookaheadLocked();   // an appended next-item now points at the wrong slot
                inserted = true;
            }
        }
        if (inserted)
        {
            SaveSoon();
            _ = EnsureLookaheadAsync();
        }
        else
        {
            Play(tracks, 0);
        }
    }

    /// <summary>Jump straight to a queue position — the dashboard's up-next list.</summary>
    public void PlayAt(int index)
    {
        lock (_gate)
        {
            if (index < 0 || index >= _queue.Count || index == _index) return;
        }
        JumpTo(index);
    }

    public void Next()
    {
        int target;
        lock (_gate)
        {
            if (_index < 0 || _index >= _queue.Count - 1) return;
            target = _index + 1;
        }
        JumpTo(target);
    }

    public void Previous()
    {
        int target;
        lock (_gate)
        {
            if (_index <= 0) return;
            target = _index - 1;
        }
        JumpTo(target);
    }

    public void TogglePlayPause()
    {
        // Resume entry: a restored queue is primed but has no source yet — the
        // first play kicks off the resolve (the pending seek lands on open).
        bool primed;
        int index;
        lock (_gate)
        {
            primed = _list is null && _index >= 0 && _index < _queue.Count;
            index = _index;
        }
        if (primed)
        {
            _ = RebuildAtAsync(index);
            return;
        }
        if (IsPlaying) _player.Pause();
        else _player.Play();
    }

    /// <summary>Transport nudge: back 15 seconds.</summary>
    public void SkipBack() => Nudge(TimeSpan.FromSeconds(-15));

    /// <summary>Transport nudge: ahead 30 seconds.</summary>
    public void SkipForward() => Nudge(TimeSpan.FromSeconds(30));

    private void Nudge(TimeSpan delta)
    {
        if (Current is null) return;
        var session = _player.PlaybackSession;
        var target = session.Position + delta;
        if (target < TimeSpan.Zero) target = TimeSpan.Zero;
        var dur = session.NaturalDuration;
        if (dur > TimeSpan.Zero && target > dur) target = dur;
        session.Position = target;
    }

    /// <summary>
    /// Move to a queue slot. Fast path: the slot's item is already in the
    /// playback list (the pre-rolled next, or an already-played previous) —
    /// MoveTo is instant and gapless. Otherwise resolve-and-rebuild.
    /// </summary>
    private void JumpTo(int target)
    {
        MediaPlaybackList? list = null;
        var listPos = -1;
        lock (_gate)
        {
            if (target < 0 || target >= _queue.Count) return;
            if (_list is { } l)
            {
                for (var k = 0; k < l.Items.Count; k++)
                {
                    if (_items.TryGetValue(l.Items[k], out var info) && info.QueueIndex == target)
                    {
                        list = l;
                        listPos = k;
                        break;
                    }
                }
            }
            _index = target;
            _pendingSeek = null;   // explicit navigation cancels a pending resume point
            QueueVersion++;
        }
        SaveSoon();
        if (list is not null)
        {
            list.MoveTo((uint)listPos);   // CurrentItemChanged confirms pick/stream
            _player.Play();
            return;
        }
        _ = RebuildAtAsync(target);
    }

    /// <summary>The list advanced (pre-rolled auto-advance, or our MoveTo).</summary>
    private void OnCurrentItemChanged(
        MediaPlaybackList sender, CurrentMediaPlaybackItemChangedEventArgs args)
    {
        if (args.NewItem is null) return;
        lock (_gate)
        {
            if (!ReferenceEquals(sender, _list)) return;   // a stale, replaced list
            if (!_items.TryGetValue(args.NewItem, out var info)) return;
            _index = info.QueueIndex;
            CurrentPick = info.Pick;
            CurrentStream = info.Stream;
            QueueVersion++;
        }
        Status = null;
        SaveSoon();
        _ = EnsureLookaheadAsync();
    }

    /// <summary>List exhausted — the look-ahead never landed. Fall back to
    /// resolve-on-advance so playback continues (with a gap) rather than stalls.</summary>
    private void OnListEnded()
    {
        var next = -1;
        lock (_gate)
        {
            _pendingSeek = null;
            if (_index >= 0 && _index < _queue.Count - 1)
            {
                next = ++_index;
                QueueVersion++;
            }
        }
        if (next >= 0) _ = RebuildAtAsync(next);
    }

    // ---- resume-on-launch ---------------------------------------------------

    /// <summary>
    /// Loads the previous session's queue primed-but-paused: the transport shows
    /// the track and the first play resolves it and lands on the saved position.
    /// Runs at most once; a queue the user already started wins over the snapshot.
    /// </summary>
    public async Task RestoreAsync()
    {
        if (_restored) return;
        _restored = true;
        var snap = await _state.LoadAsync();
        if (snap is null) return;
        lock (_gate)
        {
            if (_queue.Count > 0) return;
            _queue.AddRange(snap.Queue);
            _index = snap.Index;
            _pendingSeek = snap.PositionSeconds > 1
                ? TimeSpan.FromSeconds(snap.PositionSeconds)
                : null;
            QueueVersion++;
        }
        Volume = snap.Volume;
        IsMuted = snap.IsMuted;
        Status = $"Ready to resume “{Current?.Title}” — press play.";
    }

    /// <summary>Synchronous save for app shutdown (the window's Closed handler).</summary>
    public void SaveNow()
    {
        if (Snapshot() is { } snap) _state.Save(snap);
    }

    private void SaveSoon()
    {
        if (Snapshot() is { } snap) _ = _state.SaveAsync(snap);
    }

    private PlaybackSnapshot? Snapshot()
    {
        NowPlaying[] queue;
        int index;
        lock (_gate)
        {
            if (_index < 0 || _index >= _queue.Count) return null;
            queue = _queue.ToArray();
            index = _index;
        }
        return new PlaybackSnapshot(
            queue, index, _player.PlaybackSession.Position.TotalSeconds, Volume, IsMuted);
    }

    /// <summary>Start a fresh playback list at a queue slot (initial play, or any
    /// jump whose target wasn't pre-rolled). Skips unplayable tracks forward.</summary>
    private async Task RebuildAtAsync(int startIndex)
    {
        long token;
        NowPlaying? track;
        lock (_gate)
        {
            token = ++_loadToken;
            track = startIndex >= 0 && startIndex < _queue.Count ? _queue[startIndex] : null;
        }
        if (track is null)
        {
            _player.Pause();
            return;
        }
        try
        {
            _resolving = true;
            CurrentPick = null;
            CurrentStream = null;
            Status = $"Resolving “{track.Title}”…";

            var resolved = await ResolvePlayableAsync(track);
            lock (_gate) { if (_loadToken != token) return; }

            if (resolved is null)
            {
                Status = "No playable stream — skipping.";
                var next = -1;
                lock (_gate)
                {
                    if (_loadToken == token && startIndex < _queue.Count - 1)
                    {
                        next = _index = startIndex + 1;
                        QueueVersion++;
                    }
                }
                if (next >= 0) _ = RebuildAtAsync(next);
                return;
            }

            var item = WithDisplayProperties(resolved.Source, track);
            var list = new MediaPlaybackList();
            list.CurrentItemChanged += OnCurrentItemChanged;

            lock (_gate)
            {
                if (_loadToken != token) return;   // superseded — abandon quietly
                if (_list is { } old) old.CurrentItemChanged -= OnCurrentItemChanged;
                _items.Clear();
                _list = list;
                list.Items.Add(item);
                _items[item] = new ItemInfo(startIndex, resolved.Pick, resolved.Stream);
                _index = startIndex;
                CurrentPick = resolved.Pick;
                CurrentStream = resolved.Stream;
                QueueVersion++;
            }
            _player.Source = list;
            _player.Play();
            Status = null;
            _ = EnsureLookaheadAsync();
        }
        catch (Exception ex)
        {
            lock (_gate) { if (_loadToken != token) return; }
            Status = ex.Message;   // network/probe error — stop here, user can retry/skip
        }
        finally
        {
            lock (_gate) { if (_loadToken == token) _resolving = false; }
        }
    }

    /// <summary>
    /// Resolve queue slot current+1 in the background and append it to the live
    /// list so Media Foundation pre-rolls it (the gapless hand-off). One ahead
    /// only; best-effort — a miss is covered by the MediaEnded fallback.
    /// </summary>
    private async Task EnsureLookaheadAsync()
    {
        long token;
        int nextIndex;
        NowPlaying next;
        MediaPlaybackList list;
        lock (_gate)
        {
            if (_lookahead || _list is null) return;
            nextIndex = _index + 1;
            if (nextIndex <= 0 || nextIndex >= _queue.Count) return;
            foreach (var info in _items.Values)
                if (info.QueueIndex == nextIndex) return;   // already pre-rolled
            next = _queue[nextIndex];
            list = _list;
            token = _loadToken;
            _lookahead = true;
        }
        try
        {
            var resolved = await ResolvePlayableAsync(next);
            if (resolved is null) return;   // unplayable — MediaEnded fallback will skip it

            var item = WithDisplayProperties(resolved.Source, next);
            lock (_gate)
            {
                // Only append if the world hasn't moved: same list, same load
                // generation, and the slot is still "current + 1".
                if (_loadToken != token || !ReferenceEquals(list, _list)) return;
                if (nextIndex != _index + 1) return;
                _items[item] = new ItemInfo(nextIndex, resolved.Pick, resolved.Stream);
                list.Items.Add(item);
            }
        }
        catch
        {
            // Best-effort: a failed look-ahead just means a gapped advance later.
        }
        finally
        {
            lock (_gate) { _lookahead = false; }
        }
    }

    /// <summary>Remove pre-rolled items past the current one (queue changed under them).</summary>
    private void DropLookaheadLocked()
    {
        if (_list is null) return;
        for (var k = _list.Items.Count - 1; k >= 0; k--)
        {
            var item = _list.Items[k];
            if (_items.TryGetValue(item, out var info) && info.QueueIndex > _index)
            {
                _list.Items.RemoveAt(k);
                _items.Remove(item);
            }
        }
    }

    private async Task<Resolved?> ResolvePlayableAsync(NowPlaying track)
    {
        var session = await _auth.GetSessionAsync();
        var pick = await _resolver.ResolveBestStreamAsync(track.TrackId, session);
        if (pick is null) return null;

        if (pick.Format == AudioFormat.Hls)
        {
            // HLS-only tracks play through an adaptive source. The playlist and
            // segment fetches carry the CDN's required Referer/UA via a
            // header-carrying WinRT HttpClient (System.Net.Http can't be handed
            // to AdaptiveMediaSource).
            if (!Uri.TryCreate(pick.Url, UriKind.Absolute, out var uri)) return null;
            var created = await AdaptiveMediaSource.CreateFromUriAsync(uri, HlsHttp);
            if (created.Status != AdaptiveMediaSourceCreationStatus.Success) return null;
            return new Resolved(
                pick, MediaSource.CreateFromAdaptiveMediaSource(created.MediaSource), null);
        }

        var stream = await HttpAudioStream.CreateAsync(
            _http, pick.Url, NugsConstants.PlayerReferer, NugsConstants.MobileUserAgent);
        return new Resolved(
            pick, MediaSource.CreateFromStream(stream, NugsStreamResolver.GetMimeType(pick.Format)), stream);
    }

    private Windows.Web.Http.HttpClient HlsHttp => _hlsHttp ??= CreateHlsClient();

    private static Windows.Web.Http.HttpClient CreateHlsClient()
    {
        var client = new Windows.Web.Http.HttpClient();
        client.DefaultRequestHeaders.TryAppendWithoutValidation("Referer", NugsConstants.PlayerReferer);
        client.DefaultRequestHeaders.TryAppendWithoutValidation("User-Agent", NugsConstants.MobileUserAgent);
        return client;
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
