# Gapless Playback Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate the audible gap between consecutive tracks in both heads (Blazor web + MAUI native WebView2) by preloading the next track into a second `<audio>` element.

**Architecture:** `MainLayout` owns two hidden `<audio>` elements that ping-pong — one active (audible), one idle (preloading the next queue track). `PlayerService` stays the advancement authority and classifies every track change as `Fresh` (cold-load), `Advance` (swap to the preloaded element), or `PreloadOnly` (only re-point preload). A JS state machine in `audio-interop.js` owns active/idle element identity. Server changes keep long-track preloads alive and avoid re-probing.

**Tech Stack:** .NET 10, Blazor (Razor class library `Nugsdotnet.UI`), ASP.NET Core minimal APIs (`Nugsdotnet.Core`), MAUI BlazorWebView (`Nugsdotnet.App`), vanilla JS interop, xUnit.

## Global Constraints

- Target framework: **net10.0** for all projects (the new test project included).
- `Nullable` enable, `ImplicitUsings` enable (match existing `.csproj` style).
- The two `<audio>` elements keep `class="audio"` **only** for the CSS hide rule (`app.css` `audio.audio { display:none }`). Element identity is owned by the JS state machine — **no `document.querySelector('audio.audio')` for element resolution.**
- `PlayerService` has no DI dependencies; construct with `new PlayerService()` in tests.
- Each commit message ends with the trailer: `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- Spec of record: [docs/superpowers/specs/2026-06-17-gapless-playback-design.md](../specs/2026-06-17-gapless-playback-design.md).
- Branch: `gapless-playback` (already created; the spec is already committed there).

---

### Task 1: Test project scaffold

**Files:**
- Create: `tests/Nugsdotnet.Tests/Nugsdotnet.Tests.csproj`
- Create: `tests/Nugsdotnet.Tests/SmokeTests.cs`
- Delete: `tests/Nugsdotnet.Tests/UnitTest1.cs` (scaffolded default)
- Modify: `nugsdotnet.sln` (via `dotnet sln add`)

**Interfaces:**
- Produces: a runnable xUnit project referencing `Nugsdotnet.UI` and `Nugsdotnet.Core`, so later tasks can unit-test `PlayerService` (UI) and `StreamInspector` (Core) with `dotnet test`.

- [ ] **Step 1: Scaffold the xUnit project and wire references**

```bash
cd "C:/Users/TimVanbenschoten/claude/nugsdotnet"
dotnet new xunit -o tests/Nugsdotnet.Tests
dotnet sln add tests/Nugsdotnet.Tests/Nugsdotnet.Tests.csproj
dotnet add tests/Nugsdotnet.Tests/Nugsdotnet.Tests.csproj reference \
  src/Nugsdotnet.UI/Nugsdotnet.UI.csproj \
  src/Nugsdotnet.Core/Nugsdotnet.Core.csproj
rm tests/Nugsdotnet.Tests/UnitTest1.cs
```

- [ ] **Step 2: Force the target framework to net10.0**

Open `tests/Nugsdotnet.Tests/Nugsdotnet.Tests.csproj` and ensure the `<TargetFramework>` is `net10.0` (the `dotnet new` default may differ). The `<PropertyGroup>` should read:

```xml
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
```

- [ ] **Step 3: Write a trivial smoke test**

`tests/Nugsdotnet.Tests/SmokeTests.cs`:

```csharp
using Xunit;

namespace Nugsdotnet.Tests;

public class SmokeTests
{
    [Fact]
    public void Harness_runs()
    {
        Assert.True(true);
    }
}
```

- [ ] **Step 4: Run the test to verify the harness builds and passes**

Run: `dotnet test tests/Nugsdotnet.Tests/Nugsdotnet.Tests.csproj`
Expected: build succeeds; `Passed!  - Failed: 0, Passed: 1`.

- [ ] **Step 5: Commit**

```bash
git add tests/Nugsdotnet.Tests nugsdotnet.sln
git commit -m "$(cat <<'EOF'
test: scaffold Nugsdotnet.Tests xUnit project

References Nugsdotnet.UI and Nugsdotnet.Core so PlayerService and
StreamInspector can be unit-tested.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: PlayerService — `TrackChangeKind`, `NextTrackId`, parameterized `StartAt`

**Files:**
- Modify: `src/Nugsdotnet.UI/Services/PlayerService.cs`
- Modify: `src/Nugsdotnet.UI/Layout/MainLayout.razor` (minimal: keep it compiling against the new event signature)
- Test: `tests/Nugsdotnet.Tests/PlayerServiceTests.cs` (create)

**Interfaces:**
- Produces:
  - `enum TrackChangeKind { Fresh, Advance, PreloadOnly }` (namespace `Nugsdotnet.UI.Services`).
  - `event Action<TrackChangeKind>? PlayerService.TrackChangeRequested`.
  - `string? PlayerService.NextTrackId` — `Queue[Index+1].TrackId` when `HasNext`, else null.
  - `Next()` raises `Advance`; `Play`/`Previous`/`JumpTo`/idle-start `Enqueue`/`PlayNext`/`Clear`/`RemoveAt` removed-current & emptied raise `Fresh`; append `Enqueue`, insert `PlayNext`, `RemoveAt` non-current branches, and `HandleEnded` queue-end raise `PreloadOnly`.
- Consumes: `NowPlaying`, `PlayRequest` (unchanged records in the same file).

- [ ] **Step 1: Write the failing tests**

`tests/Nugsdotnet.Tests/PlayerServiceTests.cs`:

```csharp
using System.Collections.Generic;
using Nugsdotnet.UI.Services;
using Xunit;

namespace Nugsdotnet.Tests;

public class PlayerServiceTests
{
    private static NowPlaying T(string id) => new(id, id, "Artist");

    private static (PlayerService player, List<TrackChangeKind> kinds) NewPlayer()
    {
        var p = new PlayerService();
        var kinds = new List<TrackChangeKind>();
        p.TrackChangeRequested += k => kinds.Add(k);
        return (p, kinds);
    }

    private static PlayerService Playing(params string[] ids)
    {
        var p = new PlayerService();
        p.Play(new PlayRequest(new List<NowPlaying>(System.Array.ConvertAll(ids, T)), 0));
        return p;
    }

    [Fact]
    public void Play_emits_Fresh_and_sets_NextTrackId()
    {
        var (p, kinds) = NewPlayer();
        p.Play(new PlayRequest(new List<NowPlaying> { T("a"), T("b"), T("c") }, 0));
        Assert.Equal(new[] { TrackChangeKind.Fresh }, kinds);
        Assert.Equal("a", p.Current!.TrackId);
        Assert.Equal("b", p.NextTrackId);
    }

    [Fact]
    public void Next_emits_Advance()
    {
        var p = Playing("a", "b", "c");
        var kinds = new List<TrackChangeKind>();
        p.TrackChangeRequested += k => kinds.Add(k);
        p.Next();
        Assert.Equal(new[] { TrackChangeKind.Advance }, kinds);
        Assert.Equal("b", p.Current!.TrackId);
        Assert.Equal("c", p.NextTrackId);
    }

    [Fact]
    public void Previous_and_JumpTo_emit_Fresh()
    {
        var p = Playing("a", "b", "c");
        p.Next(); // now on b
        var kinds = new List<TrackChangeKind>();
        p.TrackChangeRequested += k => kinds.Add(k);
        p.Previous();
        p.JumpTo(2);
        Assert.Equal(new[] { TrackChangeKind.Fresh, TrackChangeKind.Fresh }, kinds);
    }

    [Fact]
    public void HandleEnded_midqueue_advances_endofqueue_preloadonly()
    {
        var p = Playing("a", "b");
        var kinds = new List<TrackChangeKind>();
        p.TrackChangeRequested += k => kinds.Add(k);
        p.HandleEnded();                 // a -> b (Advance)
        Assert.Equal("b", p.Current!.TrackId);
        Assert.Null(p.NextTrackId);
        p.HandleEnded();                 // queue end -> PreloadOnly, stays on b
        Assert.Equal(new[] { TrackChangeKind.Advance, TrackChangeKind.PreloadOnly }, kinds);
        Assert.Equal("b", p.Current!.TrackId);
    }

    [Fact]
    public void Enqueue_behind_active_emits_PreloadOnly_and_updates_NextTrackId()
    {
        var p = Playing("a");            // last track, no next
        Assert.Null(p.NextTrackId);
        var kinds = new List<TrackChangeKind>();
        p.TrackChangeRequested += k => kinds.Add(k);
        p.Enqueue(new List<NowPlaying> { T("b") });
        Assert.Equal(new[] { TrackChangeKind.PreloadOnly }, kinds);
        Assert.Equal("a", p.Current!.TrackId);
        Assert.Equal("b", p.NextTrackId);
    }

    [Fact]
    public void PlayNext_behind_active_emits_PreloadOnly_and_inserts_next()
    {
        var p = Playing("a", "c");       // on a, next is c
        var kinds = new List<TrackChangeKind>();
        p.TrackChangeRequested += k => kinds.Add(k);
        p.PlayNext(new List<NowPlaying> { T("b") });
        Assert.Equal(new[] { TrackChangeKind.PreloadOnly }, kinds);
        Assert.Equal("b", p.NextTrackId);
    }

    [Fact]
    public void RemoveAt_ondeck_emits_PreloadOnly_and_repoints_next()
    {
        var p = Playing("a", "b", "c");  // on a, next is b
        var kinds = new List<TrackChangeKind>();
        p.TrackChangeRequested += k => kinds.Add(k);
        p.RemoveAt(1);                   // remove b (on-deck)
        Assert.Equal(new[] { TrackChangeKind.PreloadOnly }, kinds);
        Assert.Equal("c", p.NextTrackId);
    }

    [Fact]
    public void RemoveAt_before_cursor_emits_PreloadOnly_and_keeps_current()
    {
        var p = Playing("a", "b", "c");
        p.Next();                        // on b (index 1), next is c
        var kinds = new List<TrackChangeKind>();
        p.TrackChangeRequested += k => kinds.Add(k);
        p.RemoveAt(0);                   // remove a (before cursor)
        Assert.Equal(new[] { TrackChangeKind.PreloadOnly }, kinds);
        Assert.Equal("b", p.Current!.TrackId);
        Assert.Equal("c", p.NextTrackId);
    }

    [Fact]
    public void RemoveAt_current_emits_Fresh()
    {
        var p = Playing("a", "b", "c");
        var kinds = new List<TrackChangeKind>();
        p.TrackChangeRequested += k => kinds.Add(k);
        p.RemoveAt(0);                   // remove current a
        Assert.Equal(new[] { TrackChangeKind.Fresh }, kinds);
        Assert.Equal("b", p.Current!.TrackId);
    }

    [Fact]
    public void Clear_emits_Fresh_with_null_current()
    {
        var p = Playing("a", "b");
        var kinds = new List<TrackChangeKind>();
        p.TrackChangeRequested += k => kinds.Add(k);
        p.Clear();
        Assert.Equal(new[] { TrackChangeKind.Fresh }, kinds);
        Assert.Null(p.Current);
        Assert.Null(p.NextTrackId);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `dotnet test tests/Nugsdotnet.Tests/Nugsdotnet.Tests.csproj`
Expected: BUILD FAILS — `TrackChangeKind` does not exist, `NextTrackId` not found, `TrackChangeRequested` is `Action?` not `Action<TrackChangeKind>?`.

- [ ] **Step 3: Edit `PlayerService.cs` — add the enum**

Add after the `QueueOp` enum (around line 20):

```csharp
/// <summary>
/// Why a track change fired, so the layout knows whether to cold-load the
/// active element (<see cref="Fresh"/>), swap to the already-preloaded idle
/// element (<see cref="Advance"/>), or only re-point preload without touching
/// the audible element (<see cref="PreloadOnly"/>).
/// </summary>
public enum TrackChangeKind { Fresh, Advance, PreloadOnly }
```

- [ ] **Step 4: Edit `PlayerService.cs` — change the event signature and add `NextTrackId`**

Change line 66 from:

```csharp
    public event Action? TrackChangeRequested;
```

to:

```csharp
    public event Action<TrackChangeKind>? TrackChangeRequested;
```

Add `NextTrackId` next to the other computed properties (after `HasNext`, ~line 60):

```csharp
    /// <summary>The track that will play after the current one — what the idle
    /// element should preload. Null at the end of the queue.</summary>
    public string? NextTrackId => HasNext ? _queue[_index + 1].TrackId : null;
```

- [ ] **Step 5: Edit `PlayerService.cs` — parameterize `StartAt` and set kinds at every raise site**

Replace `StartAt` (lines 225-231) with:

```csharp
    private void StartAt(int index, TrackChangeKind kind)
    {
        _index = index;
        _ended = false;
        StateChanged?.Invoke();
        TrackChangeRequested?.Invoke(kind);
    }
```

Update the `StartAt` callers:
- `Play` (line 98): `StartAt(Math.Clamp(req.StartIndex, 0, _queue.Count - 1), TrackChangeKind.Fresh);`
- `Enqueue` idle-start (line 116): `StartAt(firstNew, TrackChangeKind.Fresh);`
- `PlayNext` idle-start (line 138): `StartAt(firstNew, TrackChangeKind.Fresh);`
- `JumpTo` (line 152): `StartAt(index, TrackChangeKind.Fresh);`
- `Next` (line 194): `StartAt(_index + 1, TrackChangeKind.Advance);`
- `Previous` (line 200): `StartAt(_index - 1, TrackChangeKind.Fresh);`

Update the `Enqueue` append branch (lines 119-122) to also signal preload:

```csharp
        else
        {
            StateChanged?.Invoke();
            QueueChanged?.Invoke(QueueOp.Enqueued);
            TrackChangeRequested?.Invoke(TrackChangeKind.PreloadOnly);
        }
```

Update the `PlayNext` insert branch (lines 142-145):

```csharp
        else
        {
            _queue.InsertRange(_index + 1, tracks);
            StateChanged?.Invoke();
            QueueChanged?.Invoke(QueueOp.PlayingNext);
            TrackChangeRequested?.Invoke(TrackChangeKind.PreloadOnly);
        }
```

Update `RemoveAt` (lines 166-189) so each branch passes a kind:

```csharp
        if (_queue.Count == 0)
        {
            _index = 0;
            _ended = false;
            StateChanged?.Invoke();
            TrackChangeRequested?.Invoke(TrackChangeKind.Fresh);  // emptied → layout stops
        }
        else if (index < _index)
        {
            _index--;                       // keep the cursor on the same track
            StateChanged?.Invoke();
            TrackChangeRequested?.Invoke(TrackChangeKind.PreloadOnly); // next may have shifted
        }
        else if (wasCurrent)
        {
            _index = Math.Min(_index, _queue.Count - 1);
            _ended = false;
            StateChanged?.Invoke();
            TrackChangeRequested?.Invoke(TrackChangeKind.Fresh);  // play whatever now occupies the slot
        }
        else
        {
            StateChanged?.Invoke();
            TrackChangeRequested?.Invoke(TrackChangeKind.PreloadOnly); // removed an upcoming track
        }
```

Update `Clear` (lines 203-210):

```csharp
    public void Clear()
    {
        _queue.Clear();
        _index = 0;
        _ended = false;
        StateChanged?.Invoke();
        TrackChangeRequested?.Invoke(TrackChangeKind.Fresh);  // nothing current → layout stops
    }
```

Update `HandleEnded` (lines 213-217):

```csharp
    public void HandleEnded()
    {
        if (HasNext) Next();
        else
        {
            _ended = true;  // queue finished — next enqueue/play-next restarts playback
            TrackChangeRequested?.Invoke(TrackChangeKind.PreloadOnly);  // clear the stale preload
        }
    }
```

- [ ] **Step 6: Keep `MainLayout.razor` compiling against the new signature**

In `MainLayout.razor`, replace the `OnTrackChangeRequested` handler (line 185) with a kind-aware shim that preserves today's single-element behavior until Task 5 wires the dual-element flow:

```csharp
    private void OnTrackChangeRequested(TrackChangeKind kind)
    {
        if (kind == TrackChangeKind.PreloadOnly) return;  // preload wired up in a later task
        _ = StartPlaybackAsync();
    }
```

(The `+=`/`-=` subscriptions at lines 87 and 234 now bind an `Action<TrackChangeKind>` automatically — no other change needed for them. `StartPlaybackAsync` is untouched in this task.)

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/Nugsdotnet.Tests/Nugsdotnet.Tests.csproj`
Expected: build succeeds; all `PlayerServiceTests` + `SmokeTests` pass (`Failed: 0`).

- [ ] **Step 8: Commit**

```bash
git add src/Nugsdotnet.UI/Services/PlayerService.cs src/Nugsdotnet.UI/Layout/MainLayout.razor tests/Nugsdotnet.Tests/PlayerServiceTests.cs
git commit -m "$(cat <<'EOF'
feat: classify track changes (Fresh/Advance/PreloadOnly) + NextTrackId

PlayerService now tells the layout why a change fired and what the idle
element should preload. MainLayout temporarily ignores the kind (no
behavior change) until the dual-element flow lands.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: Resolve-pick cache (server) — avoid re-probing on preload/replay/fallback

**Files:**
- Modify: `src/Nugsdotnet.Core/Nugs/StreamInspector.cs`
- Modify: `src/Nugsdotnet.Core/Api/Endpoints.cs:134-142`
- Test: `tests/Nugsdotnet.Tests/StreamInspectorTests.cs` (create)

**Interfaces:**
- Produces:
  - `void StreamInspector.CacheResolvedPick(string trackId, StreamPick pick)` — store with the existing 4 h TTL.
  - `StreamPick? StreamInspector.TryGetResolvedPick(string trackId)` — return a non-expired pick **without removing it**; null otherwise.
- Consumes: `StreamPick` (`Nugsdotnet.Core.Nugs`), `NugsClient.ResolveBestStreamAsync` (unchanged).

- [ ] **Step 1: Write the failing tests**

`tests/Nugsdotnet.Tests/StreamInspectorTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Nugsdotnet.Core.Nugs;
using Xunit;

namespace Nugsdotnet.Tests;

public class StreamInspectorTests
{
    private static StreamInspector New() => new(NullLogger<StreamInspector>.Instance);

    [Fact]
    public void TryGetResolvedPick_returns_null_when_absent()
    {
        var s = New();
        Assert.Null(s.TryGetResolvedPick("missing"));
    }

    [Fact]
    public void Cached_pick_is_returned_without_being_consumed()
    {
        var s = New();
        var pick = new StreamPick("https://cdn/x.flac16/file", 7, AudioFormat.Flac16);
        s.CacheResolvedPick("t1", pick);

        // Read twice — read-through must NOT be single-use (unlike the
        // /stream-info seed), so preload + the eventual play both hit it.
        Assert.Same(pick, s.TryGetResolvedPick("t1"));
        Assert.Same(pick, s.TryGetResolvedPick("t1"));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `dotnet test tests/Nugsdotnet.Tests/Nugsdotnet.Tests.csproj`
Expected: BUILD FAILS — `CacheResolvedPick` / `TryGetResolvedPick` do not exist.

- [ ] **Step 3: Add the read-through cache to `StreamInspector.cs`**

Add a field next to `_seededPicks` (after line 25):

```csharp
    // Resolved picks reused across requests (preload, eventual play, cold-load
    // fallback, replay) — read-through and NOT single-use, unlike _seededPicks
    // which the dashboard's /stream-info consumes exactly once.
    private readonly ConcurrentDictionary<string, (StreamPick Pick, DateTimeOffset ExpiresAt)> _resolvedPicks = new();
```

Add the two public methods after `StorePick` (after line 46):

```csharp
    /// <summary>Cache a freshly-resolved pick so preload + the eventual play
    /// (and any cold-load fallback / replay) reuse it instead of re-probing.</summary>
    public void CacheResolvedPick(string trackId, StreamPick pick) =>
        _resolvedPicks[trackId] = (pick, DateTimeOffset.UtcNow + Ttl);

    /// <summary>Return a cached pick if present and unexpired, WITHOUT removing
    /// it. Null when absent or stale (signed CDN URLs rotate within the TTL).</summary>
    public StreamPick? TryGetResolvedPick(string trackId)
    {
        if (_resolvedPicks.TryGetValue(trackId, out var e))
        {
            if (e.ExpiresAt > DateTimeOffset.UtcNow) return e.Pick;
            _resolvedPicks.TryRemove(trackId, out _);
        }
        return null;
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Nugsdotnet.Tests/Nugsdotnet.Tests.csproj`
Expected: `StreamInspectorTests` pass (`Failed: 0`).

- [ ] **Step 5: Wire the cache into the `/api/play` handler**

In `Endpoints.cs`, replace the resolve block (lines 134-142):

```csharp
            var session = await nugs.GetSessionAsync(ct);
            var pick = await nugs.ResolveBestStreamAsync(trackId, session, ct);
            if (pick is null)
            {
                return Results.NotFound(new ErrorResponse("no stream"));
            }
            // Seed the dashboard cache so a parallel /stream-info request reuses
            // this pick instead of re-probing all four platforms.
            inspector.StorePick(trackId, pick);
```

with:

```csharp
            var session = await nugs.GetSessionAsync(ct);
            // Reuse a recently-resolved pick (preload of N+1, a cold-load
            // fallback, or a replay) instead of re-probing all four platforms.
            var pick = inspector.TryGetResolvedPick(trackId)
                ?? await nugs.ResolveBestStreamAsync(trackId, session, ct);
            if (pick is null)
            {
                return Results.NotFound(new ErrorResponse("no stream"));
            }
            inspector.CacheResolvedPick(trackId, pick);  // read-through reuse
            // Seed the dashboard cache so a parallel /stream-info request reuses
            // this pick instead of re-probing all four platforms.
            inspector.StorePick(trackId, pick);
```

- [ ] **Step 6: Verify the server project builds**

Run: `dotnet build src/Nugsdotnet.Core/Nugsdotnet.Core.csproj`
Expected: `Build succeeded`. Then re-run `dotnet test tests/Nugsdotnet.Tests/Nugsdotnet.Tests.csproj` — still green.

- [ ] **Step 7: Commit**

```bash
git add src/Nugsdotnet.Core/Nugs/StreamInspector.cs src/Nugsdotnet.Core/Api/Endpoints.cs tests/Nugsdotnet.Tests/StreamInspectorTests.cs
git commit -m "$(cat <<'EOF'
feat: read-through resolve cache so preload doesn't re-probe

/api/play consults a non-expiring (TTL-bounded) pick cache before the
4-platform probe, so preloading N+1, cold-load fallback, and replays
reuse the resolved stream instead of re-probing.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: Streaming HttpClient timeout (server) — keep long-track preloads alive

**Files:**
- Modify: `src/Nugsdotnet.Core/ServiceCollectionExtensions.cs:18-21`
- Modify: `src/Nugsdotnet.Core/Nugs/NugsClient.cs:312-323` (`FetchAudioAsync`)

**Interfaces:**
- Consumes: nothing new.
- Produces: `FetchAudioAsync` no longer lets the 5-minute client timeout cancel a slow/early-opened preload body read.

**Why:** with `HttpCompletionOption.ResponseHeadersRead`, `HttpClient.Timeout` (5 min) bounds the *entire* body read. A preload connection opened while a >5-min FLAC plays would be cancelled mid-buffer → preload dies → audible gap. Fix: set the client timeout to infinite and bound only connect+headers per request.

- [ ] **Step 1: Remove the global 5-minute timeout from the typed client**

In `ServiceCollectionExtensions.cs`, replace lines 18-21:

```csharp
        services.AddHttpClient<NugsClient>(c =>
        {
            c.Timeout = TimeSpan.FromMinutes(5);
        });
```

with:

```csharp
        // No global timeout: audio bodies stream for the whole track (long FLAC
        // sets, plus a preload connection opened early while another track
        // plays). FetchAudioAsync bounds connect+headers per request instead.
        services.AddHttpClient<NugsClient>(c =>
        {
            c.Timeout = Timeout.InfiniteTimeSpan;
        });
```

- [ ] **Step 2: Bound only connect+headers inside `FetchAudioAsync`**

In `NugsClient.cs`, replace `FetchAudioAsync` (lines 312-323):

```csharp
    public async Task<HttpResponseMessage> FetchAudioAsync(
        string url, string? rangeHeader, CancellationToken ct = default)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Referer", PlayerReferer);
        SetUA(req, MobileUserAgent);
        if (!string.IsNullOrEmpty(rangeHeader))
        {
            req.Headers.TryAddWithoutValidation("Range", rangeHeader);
        }
        return await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
    }
```

with:

```csharp
    public async Task<HttpResponseMessage> FetchAudioAsync(
        string url, string? rangeHeader, CancellationToken ct = default)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Referer", PlayerReferer);
        SetUA(req, MobileUserAgent);
        if (!string.IsNullOrEmpty(rangeHeader))
        {
            req.Headers.TryAddWithoutValidation("Range", rangeHeader);
        }
        // The typed client has an infinite timeout (bodies stream for the whole
        // track). Bound only connect + response-headers here so a dead upstream
        // still fails fast; the caller's token governs the body copy afterward.
        using var headerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        headerCts.CancelAfter(TimeSpan.FromSeconds(30));
        return await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, headerCts.Token);
    }
```

Note: `SendAsync` with `ResponseHeadersRead` completes once headers arrive, so the linked CTS is disposed right after — it does not cancel the subsequent body `CopyToAsync` (which runs under the original `ct` in the `/api/play` handler). `FetchHeaderBytesAsync` (which reads a bounded 64 KB) is unaffected.

- [ ] **Step 3: Verify the Core project builds**

Run: `dotnet build src/Nugsdotnet.Core/Nugsdotnet.Core.csproj`
Expected: `Build succeeded`. Re-run `dotnet test tests/Nugsdotnet.Tests/Nugsdotnet.Tests.csproj` — still green (no behavioral test here; timeout behavior is verified in the manual matrix, Task 6).

- [ ] **Step 4: Commit**

```bash
git add src/Nugsdotnet.Core/ServiceCollectionExtensions.cs src/Nugsdotnet.Core/Nugs/NugsClient.cs
git commit -m "$(cat <<'EOF'
fix: don't let the client timeout cancel long/preload audio streams

Set the typed NugsClient timeout to infinite and bound only
connect+headers in FetchAudioAsync, so a preload connection opened
early during a >5-minute track is not cancelled mid-buffer.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: Dual-element JS state machine + MainLayout wiring

**Files:**
- Modify (full rewrite): `src/Nugsdotnet.UI/wwwroot/js/audio-interop.js`
- Modify: `src/Nugsdotnet.UI/Layout/MainLayout.razor`

**Interfaces:**
- Consumes: `TrackChangeKind`, `NextTrackId` (Task 2); `IMediaUrls.PlayUrl` (unchanged).
- Produces (JS, on `window.audioInterop`): `init(elA, elB, dotnetRef)`, `dispose()`, `playTrack(url)`, `preloadNext(url|null)`, `advanceToPreloaded(): Promise<bool>`, plus `toggle`/`stop`/`setCurrentTime`/`setVolume`/`seekProgressClick`/`isPaused` retargeted to the active/both elements. `bindGlobalKeys`, `lsGet`, `lsSet` unchanged.
- Produces (C#): `[JSInvokable] MainLayout.OnTrackEnded()`.

- [ ] **Step 1: Rewrite `audio-interop.js` as the two-element state machine**

Replace the **entire** contents of `src/Nugsdotnet.UI/wwwroot/js/audio-interop.js` with:

```javascript
// JS interop for the dual <audio> elements (gapless playback) + window-level
// keyboard shortcuts. Two elements ping-pong: one is active (audible), the
// other idle (preloading the next track). The active element's identity lives
// here in _els/_active — never inferred from the DOM (both share class
// "audio", which is used only for the CSS hide rule, never for resolution).
window.audioInterop = {
    _els: null,          // [elA, elB]
    _active: 0,          // index of the audible element
    _dotnetRef: null,
    _rebufferCount: 0,
    _statsEl: null,
    _statsListeners: null,
    _onEnded: null,
    _onError: null,

    _activeEl: function () { return this._els ? this._els[this._active] : null; },
    _idleEl: function () { return this._els ? this._els[1 - this._active] : null; },

    // ---- lifecycle -------------------------------------------------------
    init: function (elA, elB, dotnetRef) {
        if (this._els) return;                 // idempotent across reconnects
        this._els = [elA, elB];
        this._active = 0;
        this._dotnetRef = dotnetRef;
        this._rebufferCount = 0;

        const self = this;
        this._onEnded = function (e) {
            if (e.target === self._activeEl() && self._dotnetRef) {
                self._dotnetRef.invokeMethodAsync('OnTrackEnded').catch(function () { });
            }
        };
        this._onError = function (e) {
            if (e.target === self._activeEl()) {
                if (self._dotnetRef) self._dotnetRef.invokeMethodAsync('OnAudioError').catch(function () { });
            } else {
                // Idle/preload element failed (e.g. the next track is HLS → 415,
                // or no stream → 404). Swallow it; the next advance falls back
                // to a cold load that surfaces the error on the active element.
                e.target._preloadFailed = true;
            }
        };
        for (const el of this._els) {
            el.addEventListener('ended', this._onEnded);
            el.addEventListener('error', this._onError);
        }
        this._bindStats(this._activeEl());
    },

    dispose: function () {
        if (this._els && this._onEnded) {
            for (const el of this._els) {
                el.removeEventListener('ended', this._onEnded);
                el.removeEventListener('error', this._onError);
            }
        }
        this._unbindStats();
        this._onEnded = this._onError = null;
        this._dotnetRef = null;
        this._els = null;
    },

    // ---- playback control (ACTIVE element unless noted) ------------------
    playTrack: function (url) {
        const el = this._activeEl();
        if (!el) return;
        this._rebufferCount = 0;               // new track — reset rebuffer counter
        el._preloadFailed = false;
        el.src = url;
        const p = el.play();
        if (p && typeof p.catch === 'function') p.catch(err => console.warn('play() rejected:', err));
        this._bindStats(el);
    },

    // Point the IDLE element at the next track so it buffers ahead. Clears it
    // when url is null. Targets _idleEl() by index, so it can never disturb the
    // audible element.
    preloadNext: function (url) {
        const idle = this._idleEl();
        if (!idle) return;
        if (!url) {
            idle.removeAttribute('src');
            idle._preloadFailed = false;
            try { idle.load(); } catch (e) { }
            return;
        }
        idle._preloadFailed = false;
        idle.preload = 'auto';
        idle.src = url;
        try { idle.load(); } catch (e) { }
        // Nudge Chromium/WebView2 to actually buffer (preload=auto can otherwise
        // sit at metadata until currentTime is touched).
        const nudge = function () {
            try { idle.currentTime = 0; } catch (e) { }
            idle.removeEventListener('loadedmetadata', nudge);
        };
        idle.addEventListener('loadedmetadata', nudge);
    },

    // Swap the preloaded idle element to active and play it. Returns false if
    // the preload was not ready / play() failed, so .NET can cold-load instead.
    advanceToPreloaded: async function () {
        const incoming = this._idleEl();
        const outgoing = this._activeEl();
        if (!incoming || !incoming.src || incoming._preloadFailed) return false;
        if (!this._isReady(incoming)) return false;

        // Flip FIRST so clearing the outgoing element's src (which can emit an
        // 'error') is treated as an idle-element error and swallowed.
        this._active = 1 - this._active;
        this._rebufferCount = 0;
        if (outgoing) {
            try { outgoing.pause(); } catch (e) { }
            outgoing.removeAttribute('src');
            try { outgoing.load(); } catch (e) { }
        }

        let stalled = false;
        const onWaiting = function () { stalled = true; };
        incoming.addEventListener('waiting', onWaiting);
        try {
            const p = incoming.play();
            if (p && typeof p.then === 'function') await p;
        } catch (e) {
            incoming.removeEventListener('waiting', onWaiting);
            return false;   // play() rejected — .NET cold-loads on the new active element
        }
        this._bindStats(incoming);           // telemetry follows the audible element
        await new Promise(r => setTimeout(r, 60));   // let an immediate stall surface
        incoming.removeEventListener('waiting', onWaiting);
        return !stalled;
    },

    // Ready = buffered from the start, enough to play through the hop.
    _isReady: function (el) {
        if (!el || el.readyState < 3) return false;        // < HAVE_FUTURE_DATA
        try {
            const b = el.buffered;
            if (!b || b.length === 0) return false;
            if (b.start(0) > 0.05) return false;            // must cover the start
            const have = b.end(0);
            const dur = isFinite(el.duration) ? el.duration : have;
            return have >= Math.min(2, dur);                // ~2s, or whole short track
        } catch (e) { return false; }
    },

    toggle: function () {
        const el = this._activeEl();
        if (!el) return;
        if (el.paused) {
            const p = el.play();
            if (p && typeof p.catch === 'function') p.catch(() => { });
        } else {
            el.pause();
        }
    },
    stop: function () {
        if (!this._els) return;
        for (const el of this._els) {
            el.pause();
            el.removeAttribute('src');
            el._preloadFailed = false;
            try { el.load(); } catch (e) { }
        }
    },
    setCurrentTime: function (t) {
        const el = this._activeEl();
        if (el && isFinite(t)) { try { el.currentTime = Math.max(0, t); } catch (e) { } }
    },
    setVolume: function (v) {
        if (!this._els) return;
        const vol = Math.min(1, Math.max(0, v));
        for (const el of this._els) el.volume = vol;   // both, so volume survives a swap
    },
    seekProgressClick: function (clientX) {
        const el = this._activeEl();
        const bar = document.querySelector('.progress-bar-track');
        if (!el || !bar || !isFinite(el.duration) || el.duration <= 0) return;
        const r = bar.getBoundingClientRect();
        if (r.width <= 0) return;
        const frac = Math.min(1, Math.max(0, (clientX - r.left) / r.width));
        try { el.currentTime = frac * el.duration; } catch (e) { }
    },
    isPaused: function () {
        const el = this._activeEl();
        return el ? el.paused : true;
    },

    bindGlobalKeys: function (dotnetRef) {
        if (this._handler) {
            document.removeEventListener('keydown', this._handler);
        }
        this._handler = function (e) {
            const t = e.target;
            const tag = t ? t.tagName : '';
            const inField = t && (
                tag === 'INPUT' ||
                tag === 'TEXTAREA' ||
                t.isContentEditable === true
            );
            const activatable = t && (
                tag === 'BUTTON' || tag === 'SUMMARY' || tag === 'A' || tag === 'SELECT' ||
                (t.getAttribute && (t.getAttribute('role') === 'button' || t.getAttribute('role') === 'slider'))
            );
            if (e.key === '/' && !inField) {
                e.preventDefault();
                dotnetRef.invokeMethodAsync('OnGlobalKey', '/');
            } else if (e.key === ' ' && !inField && !activatable) {
                e.preventDefault();
                dotnetRef.invokeMethodAsync('OnGlobalKey', ' ');
            } else if ((e.key === 'n' || e.key === 'N') && !inField) {
                e.preventDefault();
                dotnetRef.invokeMethodAsync('OnGlobalKey', 'n');
            } else if ((e.key === 'p' || e.key === 'P') && !inField) {
                e.preventDefault();
                dotnetRef.invokeMethodAsync('OnGlobalKey', 'p');
            } else if ((e.key === 'd' || e.key === 'D') && !inField) {
                e.preventDefault();
                dotnetRef.invokeMethodAsync('OnGlobalKey', 'd');
            } else if (e.key === 'Escape' && inField) {
                t.blur();
            }
        };
        document.addEventListener('keydown', this._handler);
    },

    // --- localStorage (dashboard panel state persistence) ----------------
    lsGet: function (key) {
        try { return localStorage.getItem(key); } catch (e) { return null; }
    },
    lsSet: function (key, value) {
        try { localStorage.setItem(key, value); } catch (e) { /* private mode */ }
    },

    // --- dashboard audio telemetry (binds to the ACTIVE element) ----------
    _bindStats: function (el) {
        if (!el || !this._dotnetRef) return;
        this._unbindStats();
        const self = this;
        this._statsEl = el;
        let lastFire = 0;

        function snapshot() {
            let bufferedAhead = 0;
            try {
                const b = el.buffered, ct = el.currentTime;
                for (let i = 0; i < b.length; i++) {
                    if (b.start(i) <= ct && ct <= b.end(i)) { bufferedAhead = b.end(i) - ct; break; }
                }
            } catch (e) { }
            return {
                currentTime: el.currentTime || 0,
                duration: isFinite(el.duration) ? el.duration : 0,
                bufferedAhead: bufferedAhead,
                networkState: el.networkState,
                readyState: el.readyState,
                paused: el.paused,
                volume: el.volume,
                playbackRate: el.playbackRate,
                decodedBytes: el.webkitAudioDecodedByteCount || 0,
                rebufferCount: self._rebufferCount
            };
        }
        function fire() {
            if (!self._dotnetRef) return;
            self._dotnetRef.invokeMethodAsync('OnAudioStats', snapshot()).catch(function () { });
        }
        function throttled() {
            const now = Date.now();
            if (now - lastFire < 200) return;
            lastFire = now;
            fire();
        }
        function rebuffer() { self._rebufferCount++; fire(); }

        this._statsListeners = {
            timeupdate: throttled,
            progress: throttled,
            waiting: rebuffer,
            stalled: rebuffer,
            playing: fire,
            pause: fire,
            volumechange: fire,
            ratechange: fire,
            loadedmetadata: fire
        };
        for (const evt in this._statsListeners) {
            el.addEventListener(evt, this._statsListeners[evt]);
        }
        fire();   // immediate snapshot so the transport/dashboard re-sync at once
    },
    _unbindStats: function () {
        if (this._statsEl && this._statsListeners) {
            for (const evt in this._statsListeners) {
                this._statsEl.removeEventListener(evt, this._statsListeners[evt]);
            }
        }
        this._statsEl = null;
        this._statsListeners = null;
    }
};
```

Note the deliberate changes from the old file: the `error` listener is now bound per element in `init` (element-aware), **not** inside `_bindStats`; `setSrcAndPlay` is renamed `playTrack`; `unbindStats`/`bindStats(dotnetRef)` become internal `_unbindStats`/`_bindStats(el)`.

- [ ] **Step 2: Rewrite the playback wiring in `MainLayout.razor`**

**(a) Markup** — replace the single `<audio>` (lines 62-64) with two elements and no Blazor `@onended`:

```razor
            @* Hidden — the custom Transport above is the visible/accessible control surface.
               Two elements ping-pong for gapless playback; identity is owned by the JS
               state machine (audio-interop.js), the class is only for the CSS hide rule. *@
            <audio @ref="_audioA" class="audio" aria-hidden="true"></audio>
            <audio @ref="_audioB" class="audio" aria-hidden="true"></audio>
```

**(b) Fields** — add next to `_audio` (replace the `private ElementReference _audio;` line, ~line 78):

```csharp
    private ElementReference _audioA;
    private ElementReference _audioB;
    private string? _preloadedTrackId;
```

**(c) `OnAfterRenderAsync`** — replace the bind block (lines 96-103) so `init` owns telemetry binding:

```csharp
        if (!_keysBound && _session is { LoggedIn: true })
        {
            _selfRef ??= DotNetObjectReference.Create(this);
            await JS.InvokeVoidAsync("audioInterop.init", _audioA, _audioB, _selfRef);
            await JS.InvokeVoidAsync("audioInterop.bindGlobalKeys", _selfRef);
            await Dashboard.RestoreAsync(JS);
            _keysBound = true;
        }
```

**(d) Add the `OnTrackEnded` JSInvokable** — after `OnAudioStats` (after line 125):

```csharp
    /// <summary>The active &lt;audio&gt; element finished — advance the queue.
    /// (The Blazor @onended binding was replaced by a JS listener so it follows
    /// whichever element is currently audible.)</summary>
    [JSInvokable]
    public Task OnTrackEnded()
    {
        Player.HandleEnded();
        return Task.CompletedTask;
    }
```

**(e) Replace the track-change handler** — replace the Task-2 shim `OnTrackChangeRequested` and the old `StartPlaybackAsync` (lines 185-206) with the ordered handler + preload re-sync:

```csharp
    /// <summary>
    /// PlayerService raised a track change. Do the active-element op for the
    /// kind, then re-sync preload — in that order, so the swap settles before we
    /// re-point the (now) idle element (see spec §5). Sync handler that schedules
    /// the async work; async-void would silently swallow exceptions.
    /// </summary>
    private void OnTrackChangeRequested(TrackChangeKind kind) => _ = HandleTrackChangeAsync(kind);

    private async Task HandleTrackChangeAsync(TrackChangeKind kind)
    {
        try
        {
            switch (kind)
            {
                case TrackChangeKind.Fresh:
                    if (Player.Current is null)
                        await JS.InvokeVoidAsync("audioInterop.stop");
                    else
                        await JS.InvokeVoidAsync("audioInterop.playTrack", MediaUrls.PlayUrl(Player.Current.TrackId));
                    break;

                case TrackChangeKind.Advance:
                    var swapped = await JS.InvokeAsync<bool>("audioInterop.advanceToPreloaded");
                    if (!swapped && Player.Current is not null)
                        await JS.InvokeVoidAsync("audioInterop.playTrack", MediaUrls.PlayUrl(Player.Current.TrackId));
                    _preloadedTrackId = null;   // the swap consumed the preload
                    break;

                case TrackChangeKind.PreloadOnly:
                    break;                      // no active-element op
            }
            await SyncPreloadAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"playback failed: {ex.Message}");
        }
    }

    /// <summary>Point the idle element at the current NextTrackId, but only when
    /// it differs from what's already preloaded (preloadNext is idempotent).</summary>
    private async Task SyncPreloadAsync()
    {
        var next = Player.NextTrackId;
        if (next == _preloadedTrackId) return;
        _preloadedTrackId = next;
        await JS.InvokeVoidAsync(
            "audioInterop.preloadNext",
            next is null ? null : MediaUrls.PlayUrl(next));
    }
```

**(f) `DisposeAsync`** — replace the `unbindStats` call (line 237) with `dispose`:

```csharp
        try { await JS.InvokeVoidAsync("audioInterop.dispose"); } catch { /* circuit gone */ }
        _selfRef?.Dispose();
```

- [ ] **Step 3: Verify the UI project builds**

Run: `dotnet build src/Nugsdotnet.UI/Nugsdotnet.UI.csproj`
Expected: `Build succeeded` (no references to the removed `_audio`, `StartPlaybackAsync`, `setSrcAndPlay`, `bindStats`, or `unbindStats` remain). Re-run `dotnet test tests/Nugsdotnet.Tests/Nugsdotnet.Tests.csproj` — still green.

- [ ] **Step 4: Commit**

```bash
git add src/Nugsdotnet.UI/wwwroot/js/audio-interop.js src/Nugsdotnet.UI/Layout/MainLayout.razor
git commit -m "$(cat <<'EOF'
feat: dual-element gapless playback (JS state machine + layout wiring)

Two <audio> elements ping-pong: the idle one preloads the next track and
becomes active on advance. JS owns active/idle identity; ended/error are
gated to the active element; telemetry rebinds on swap. MainLayout runs
the swap then re-syncs preload, and moved track-end handling into JS via
OnTrackEnded.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: Manual verification matrix

**Files:** none (verification + any follow-up fixes, each committed separately).

**Tooling:** use the `run` skill (or `dotnet run --project src/Nugsdotnet.Server` for the web head) to launch the app, and the `verify` skill to drive it. The native head (WebView2) is launched via the MAUI `Nugsdotnet.App` on Windows.

- [ ] **Step 1: Build the whole solution (except MAUI, which needs the workload)**

Run: `dotnet build src/Nugsdotnet.Server/Nugsdotnet.Server.csproj` and `dotnet test tests/Nugsdotnet.Tests/Nugsdotnet.Tests.csproj`
Expected: both succeed.

- [ ] **Step 2: Web head — core gapless + control correctness**

Launch the web head and log in. Walk this checklist (spec §10), confirming each:
- Play an album/show with segues → **no audible gap** between tracks.
- After at least one automatic advance (so element B is active): **pause/resume, the seek slider, the volume slider, the dashboard click-to-seek, and Next/Previous all act on the audible track**; the dashboard telemetry + transport seek bar track the audible element (not frozen/stale).
- **Rapid double-Next** and **Next immediately after Play** (before preload finishes) → clean cold-load fallback, no double-advance or skipped track.
- Queue the next song via **"Play next"/"Add to queue" while a track plays**, then let the current track end → the queued track starts gaplessly.
- **Remove the on-deck track** from the queue while playing → playback continues; when the current track ends, the correct (new) next track plays.
- **Queue end** → playback stops cleanly; enqueuing afterward starts immediately.
- If the next track is HLS-only (415) or has no stream (404): **the current track keeps playing** (no spurious skip); the error only surfaces if you actually advance to it.

- [ ] **Step 3: Native head (WebView2) — preload + long-track + autoplay**

Launch the MAUI `Nugsdotnet.App` on Windows and confirm:
- With a real FLAC track, the idle element reaches buffered-ahead > 0 before the active track ends, then advances instantly (open dev tools / dashboard to observe).
- A **>5-minute track** preloads its successor without the preload dying mid-buffer (validates Task 4). If it dies, re-check the `FetchAudioAsync` timeout change.
- The programmatic `play()` on the second element after `ended` is permitted. If playback silently fails to start on advance (autoplay `NotAllowedError`), add `--autoplay-policy=no-user-gesture-required` via WebView2 `AdditionalBrowserArguments` in the MAUI WebView setup, then re-verify. (The `await`ed `play()` + cold-load fallback already prevents a hard failure; this removes the fallback gap.)

- [ ] **Step 4: Record results / fix and re-verify**

If any check fails, debug with the `systematic-debugging` skill, fix, commit the fix on this branch, and re-run the affected check. Do not mark the task complete until every §10/§11 check passes (or a deviation is explicitly documented).

- [ ] **Step 5: Commit any verification fixes**

```bash
git add -A
git commit -m "$(cat <<'EOF'
fix: address gapless playback verification findings

<describe what manual verification surfaced and how it was fixed>

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

(If verification passed with no changes, skip this commit.)

---

## Self-Review

**1. Spec coverage**

| Spec section | Task(s) |
|---|---|
| §3 two elements, JS owns identity | Task 5 (markup + JS `_els`/`_active`) |
| §4 three-value `TrackChangeKind` + table | Task 2 |
| §5 ordered handler (swap → re-sync), `_preloadedTrackId` | Task 5 (`HandleTrackChangeAsync`/`SyncPreloadAsync`) |
| §6 JS methods (init/playTrack/preloadNext/advanceToPreloaded/readiness gate/awaited play()/waiting guard/setVolume both/stop both/element-aware ended+error/telemetry rebind/dispose) | Task 5 |
| §7 PlayerService (event sig, `NextTrackId`, `StartAt(kind)`, raise sites) | Task 2 |
| §7 MainLayout (two elements, remove `@onended`, init lifecycle, `OnTrackEnded`, dispose) | Tasks 2 (shim) + 5 (full) |
| §8a required HttpClient timeout | Task 4 |
| §8b recommended resolve cache | Task 3 |
| §9 edge cases | Tasks 2 (state) + 5 (JS) + 6 (verify) |
| §10 unit tests | Tasks 1–3 |
| §10 manual matrix + §11 risks | Task 6 |

No spec section is left without a task.

**2. Placeholder scan:** No "TBD/TODO/handle appropriately" — every code step shows complete code; the only free-text is the manual checklist (Task 6), which is inherent to manual verification, and the commit-message body placeholder in Task 6 Step 5 (filled in only if fixes were made).

**3. Type consistency:** `TrackChangeKind { Fresh, Advance, PreloadOnly }` and `NextTrackId` (Task 2) are consumed identically in Task 5. JS function names (`init`, `playTrack`, `preloadNext`, `advanceToPreloaded`, `stop`, `dispose`) match between the JS definitions (Task 5 Step 1) and the C# `JS.Invoke*` calls (Task 5 Step 2). `advanceToPreloaded` returns a bool in JS and is awaited as `InvokeAsync<bool>` in C#. `CacheResolvedPick`/`TryGetResolvedPick` (Task 3) match between `StreamInspector` and the `Endpoints` call site.

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-06-17-gapless-playback.md`. Two execution options:

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration.

**2. Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints.

Which approach?
