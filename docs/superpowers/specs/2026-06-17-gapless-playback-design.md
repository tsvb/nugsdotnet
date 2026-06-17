# Gapless Playback — Design Spec

**Date:** 2026-06-17
**Status:** Approved design, enriched by adversarial review (43 findings raised, 32 confirmed).
**Scope:** Eliminate the audible gap between consecutive tracks in both heads (Blazor web + MAUI native WebView2), so live-concert segues play seamlessly.

---

## 1. Goal & motivation

nugs.net catalogs **live concert recordings**, where one track routinely segues directly into the next. Today every track change incurs an audible gap: the `<audio>` element fires `ended`, .NET advances the queue, JS sets a new `src`, and only then does the server resolve the stream (a serial 4-platform probe) and begin buffering. That sum is comfortably ≥1s — most painful exactly at a segue.

The goal is **perceptual gapless playback**: the next track is resolved and buffered *ahead of time* so that when the current track ends it starts effectively instantly.

## 2. Locked decisions (do not relitigate)

These were chosen during brainstorming and are fixed for this spec:

- **Approach: perceptual gapless via two alternating `<audio>` elements with preload.** Not Web Audio (`decodeAudioData` would force whole-file downloads and lose Range streaming/seeking). Not crossfade (smears live segues).
- **Track advancement stays in .NET.** `PlayerService` remains the authority. A single JS→.NET→JS hop on track end is accepted; with FLAC (no encoder padding — the preferred/common format) the result is perceptually seamless.

## 3. Core model — two `<audio>` elements, ping-pong

`MainLayout` renders **two** hidden `<audio>` elements (A and B) instead of one. At any moment one is **active** (audible) and the other is **idle**, with its `src` pointed at the *next* queue track (`preload="auto"`) so its bytes are already buffered.

- While A plays track N, B preloads track N+1.
- On track end (or manual Next): B becomes active and starts from its buffer; A is paused/cleared and becomes the new idle element, which then preloads track N+2.

The big latency win — hiding the 4-probe resolve + initial buffering — comes entirely from preload. The residual JS→.NET→JS hop is small and acceptable per the locked decision.

> **Element identity is owned by the JS state machine, never by a CSS selector.** Today every interop function except `setSrcAndPlay` resolves its element with `document.querySelector('audio.audio')`, which returns the *first* match in DOM order. With two elements sharing that class, this silently targets the wrong element after the first swap. **This must change** (see §6). The `audio.audio` class is retained *only* for the CSS hide rule (`app.css`), never for element resolution.

## 4. The swap rule — three `TrackChangeKind`s

> **Change from the brainstormed design:** the kind enum gains a third value, `PreloadOnly`. The review found that keying preload re-sync off `TrackChangeRequested` alone misses the most common flows (PlayNext/Enqueue behind active playback, removing the on-deck track), which fire only `StateChanged`. Routing *all* preload changes through one ordered handler with an explicit kind fixes that and a swap-vs-resync ordering race in one move.

```
enum TrackChangeKind { Fresh, Advance, PreloadOnly }
```

| Trigger | Kind | MainLayout action |
|---|---|---|
| Natural end (`HandleEnded`→`Next`), **manual Next** | **Advance** | `advanceToPreloaded()` (instant); fall back to `playTrack` if it returns false |
| `Play`, `Previous`, `JumpTo`, idle-start Enqueue/PlayNext, removed-current | **Fresh** | `playTrack(active)`; or stop both if `Current` is null |
| Append Enqueue, insert PlayNext, RemoveAt of a non-current track, queue-end | **PreloadOnly** | No active-element op — only re-sync preload |

Every handler, regardless of kind, **finishes by re-syncing preload** (see §5). Manual **Next** is an `Advance` because the idle element already holds track N+1; `Previous`/`JumpTo` are `Fresh` (not preloaded).

## 5. Ordering invariant — swap first, then re-sync preload

`StartAt` raises `StateChanged` **then** `TrackChangeRequested` (PlayerService.cs:229-230). If preload re-sync rode on `StateChanged`, an `Advance` would recompute `NextTrackId` off the already-incremented index and re-point the idle element *before* the swap promotes it — clobbering the element about to become audible.

**Rule:** preload re-sync runs **only inside the `TrackChangeRequested` handler, after the active-element op completes**, never off `StateChanged`. `StateChanged` stays UI-render-only.

Sequence inside the handler:
1. Do the active-element op for the kind: `Fresh`→`playTrack` (or stop if `Current` null); `Advance`→`await advanceToPreloaded()` with `playTrack` fallback; `PreloadOnly`→nothing.
2. On a successful `Advance`, the JS swap has settled active/idle identity; set `_preloadedTrackId = null` in .NET.
3. Recompute desired `NextTrackId` (track at `Index+1`, or null). If it differs from `_preloadedTrackId`, call `preloadNext(url|null)` and update `_preloadedTrackId`.

**Defense in depth (JS):** `preloadNext` must refuse to touch an element whose current `src` equals the active element's `src`, so even a mis-timed call can never disturb audible playback.

## 6. JavaScript interop (`audio-interop.js`)

Becomes a two-element state machine. **All seven `document.querySelector('audio.audio')` call sites are removed** (toggle:18, stop:28, setCurrentTime:35, setVolume:39, seekProgressClick:45, isPaused:54, bindStats:111).

State on the `audioInterop` object: `_els = [elA, elB]`, `_active` (index), `_dotnetRef`, plus existing telemetry fields. Private `_active()` / `_idle()` accessors.

Methods:

- **`init(elA, elB, dotnetRef)`** — store both refs + dotnetRef + `_active = 0`. `addEventListener('ended', ...)` and `('error', ...)` on **both** elements. Bind telemetry to the active element (see `bindStats` below). Idempotent (guard against re-init on reconnect).
  - **`ended` handler:** forward to `OnTrackEnded` **only if `e.target === _active()`**.
  - **`error` handler:** forward to `OnAudioError` **only if `e.target === _active()`**. An idle-element error (e.g. the next track is HLS-only → server 415, or no-stream → 404) is swallowed and sets `_preloadFailed = true` so the next `advanceToPreloaded` returns false and .NET cold-loads — which re-fires the error on the now-active element and surfaces it normally. (Without this gate, an idle preload error would skip the *currently playing* track.)
- **`playTrack(url)`** — `Fresh` load on the active element: set `src`, `play()`, reset `_rebufferCount = 0`. Ensure telemetry is bound to the active element.
- **`preloadNext(url|null)`** — point the **idle** element's `src` at `url` with `preload="auto"` (or clear it and `load()` when null). After `loadedmetadata`, set `idle.currentTime = 0` to nudge Chromium/WebView2 into actually buffering. **Idempotent**; refuses to touch an element whose `src` equals the active element's `src`. Never `play()`s.
- **`advanceToPreloaded()`** — promote idle→active. Steps:
  1. **Readiness gate** (return `false` if not met → .NET cold-loads): `idle.readyState >= 3` (HAVE_FUTURE_DATA) **and** `idle.buffered.length > 0` **and** the first buffered range covers the start (`buffered.start(0) <= ~0.05`) with at least ~1.5–2 s buffered (or full duration if shorter). Reuse the existing buffered-range scan (audio-interop.js:121-128). Also return `false` if `_preloadFailed` is set for that element.
  2. `pause()` + clear `src` on the outgoing element (so its own pending `ended` cannot re-fire and double-advance).
  3. Flip `_active`. `play()` the new active element; **`await` the play() promise** — on rejection (autoplay `NotAllowedError`, transient `AbortError`) return `false` so .NET falls back (do **not** swallow with `p.catch`).
  4. Attach a one-shot `waiting` guard for ~250 ms after `play()`; if it fires before `playing`, treat as a late miss and signal fallback.
  5. Reset `_rebufferCount = 0` (a new track is starting; `Advance` does not route through `setSrcAndPlay` where the reset currently lives).
  6. Rebind telemetry to the new active element (see below).
  7. Return `true` only if all of the above succeeded.
- **`toggle` / `setCurrentTime` / `seekProgressClick` / `isPaused`** → operate on `_active()`.
- **`setVolume(v)`** → set **both** elements (so volume doesn't jump on swap).
- **`stop()`** → pause + clear **both** elements.
- **Telemetry** — replace the one-shot `bindStats` with an element-explicit primitive. `bindStats(el, dotnetRef)` (or `rebindStats(el)`): detach listeners from the previous `_statsEl`, attach to the passed element, fire an immediate snapshot so the Transport/dashboard re-sync at once, keep `_statsRef` alive. Drop the `_statsBound` hard guard (or scope it per element). `advanceToPreloaded` and `playTrack` rebind to the active element. The closures must capture the passed `el`, not re-run `querySelector`.
- **`dispose()` / teardown** — remove the `ended`/`error` listeners from both elements and null `_dotnetRef`. Called from `MainLayout.DisposeAsync` (before `_selfRef.Dispose()`) so no queued event invokes a disposed ref after circuit teardown.

## 7. C# changes

### PlayerService (`src/Nugsdotnet.UI/Services/PlayerService.cs`)

- **Event signature:** `event Action? TrackChangeRequested` → `event Action<TrackChangeKind>? TrackChangeRequested`.
- **`StartAt(int index)` → `StartAt(int index, TrackChangeKind kind)`** — the sole raise point for Play/Next/Previous/JumpTo/idle-start. It raises `TrackChangeRequested?.Invoke(kind)`. Callers pass the kind:
  - `Next()` → `Advance` (the *only* `Advance` caller).
  - `Play()`, `Previous()`, `JumpTo()`, idle-start `Enqueue`/`PlayNext` → `Fresh`.
- **Direct raise sites** pass kinds explicitly:
  - `RemoveAt` empty-queue (line ~171) and removed-current (~183), `Clear()` (~209) → `Fresh`. (`Fresh` with `Current == null` means *stop both elements*, not `playTrack`.)
  - `RemoveAt` remove-before-current (`index < _index`, ~173-177) and remove-upcoming (`else`, ~185-188) → **`PreloadOnly`**.
  - Append `Enqueue` (~118-122) and insert `PlayNext` (~140-145) → additionally raise **`PreloadOnly`** (keep their existing `StateChanged` + `QueueChanged` for re-render and toast).
  - `HandleEnded()` queue-end branch (`else _ended = true`, ~216) → raise **`PreloadOnly`** so the layout clears the now-stale preload (`NextTrackId` is null at the last track). `HandleEnded` otherwise unchanged (still calls `Next()`).
- **Expose `NextTrackId`** — `Queue[Index+1].TrackId` when `HasNext`, else null.

> Note: a `Fresh`/`Advance`/`PreloadOnly` raised with `Current == null` (emptied/cleared) means the layout stops both elements and clears preload. The handler must treat null `Current` as stop, never `playTrack`.

### MainLayout (`src/Nugsdotnet.UI/Layout/MainLayout.razor`)

- Render two `<audio>` elements (`_audioA`, `_audioB`); **remove the Blazor `@onended`** (line 64 — ownership moves into JS).
- Subscribe `TrackChangeRequested` with the new `TrackChangeKind` arg; implement the §5 ordered handler.
- **`init` lifecycle:** call `audioInterop.init(_audioA, _audioB, _selfRef)` **inside the existing `if (!_keysBound && _session is { LoggedIn: true })` guard** in `OnAfterRenderAsync` (lines 96-103), reusing the same `_selfRef`. Not on raw `firstRender` — the `<audio>` elements only render inside the logged-in branch, so their `ElementReference`s are invalid until then. `init` now owns the initial telemetry binding (folding in the former standalone `bindStats` call at line 100); `bindGlobalKeys` remains a separate call.
- **`[JSInvokable] OnTrackEnded()`** → `Player.HandleEnded()` (mirrors the existing `OnAudioStats`/`OnAudioError` pattern).
- `OnAudioStats`/`OnAudioError` keep working — they now run against whichever element JS reports as active. `OnAudioError`→`Player.Next()` (Advance + cold-load fallback) is unchanged and acceptable; error-driven skips simply don't get the gapless benefit.
- Track `_preloadedTrackId`; re-sync per §5.
- **`DisposeAsync`** also calls `audioInterop.dispose()` before disposing `_selfRef`.

## 8. Server changes

> **Change from the brainstormed design:** the design said "no server change required." The review found one **required** change and one **recommended** one.

### 8a. (Required) Decouple the streaming body budget from `HttpClient.Timeout`

`AddHttpClient<NugsClient>` sets `Timeout = 5 min` (`ServiceCollectionExtensions.cs:18-21`). With `HttpCompletionOption.ResponseHeadersRead` (`NugsClient.FetchAudioAsync`) that 5-minute clock bounds the **entire body read**. A preload connection for track N+1 is opened while N is still playing; if N is a >5-min FLAC (common for live shows), the preload stream times out mid-buffer → `advanceToPreloaded` returns false → cold-load fallback → audible gap on exactly the long tracks gapless is meant to help.

**Fix:** for the `/api/play` streaming path, set `Timeout = InfiniteTimeSpan` (either on the typed client, or via a separate streaming `HttpClient` registration so catalog/auth keep a bounded timeout), and bound only connect+headers in `FetchAudioAsync` via a `CancellationTokenSource` that covers up to `ResponseHeadersRead`; let the body `CopyToAsync` run under the request's own `CancellationToken`.

(Connection concurrency is *not* a problem: no `MaxConnectionsPerServer` is configured, so the default is unbounded; two concurrent Range streams on the pooled client is supported usage.)

### 8b. (Recommended, part of this work) Read-through resolve cache

`/api/play` runs `ResolveBestStreamAsync` on every hit — a serial probe of platforms `{1,4,7,10}` (`NugsClient.cs:214-231`) before any byte flows. The existing `StreamInspector` seed is write-only for `/play` and single-use for `/stream-info`, so preload, replays, and cold-load fallback each re-probe. Preload's time-to-first-byte therefore includes 4 serial round-trips — most acute on the native loopback.

**Fix:** add `StreamInspector.TryGetFreshPick(trackId)` — a non-removing read-through that returns a cached `StreamPick` within the existing 4 h TTL (respecting signed-URL rotation). `/api/play` consults it before probing. This makes preload timely and cold-load fallback/replay instant. Not on the gapless correctness critical path (the cold-load fallback guarantees correctness regardless), but strongly recommended so preload is actually ready in time.

## 9. Edge cases

| Case | Handling |
|---|---|
| Preload not ready at advance | Readiness gate (§6) returns false → cold-load via `playTrack`. Never worse than today. |
| `play()` rejected (autoplay/abort) | `await`ed in `advanceToPreloaded`; rejection → false → fallback. |
| Idle-element error (HLS 415 / 404) | Swallowed; sets `_preloadFailed`; advance falls back and surfaces the error on the active element. Does **not** skip the current track. |
| Manual Next before preload finished | Advance gate fails → cold-load fallback. |
| PlayNext/Enqueue behind active playback | `PreloadOnly` → preload re-sync points idle at the new next track. |
| Remove the on-deck (preloaded) track | `PreloadOnly` → re-point preload. |
| Remove a track before the cursor | `PreloadOnly` → re-sync (NextTrackId usually unchanged; harmless idempotent check). |
| Queue end | `HandleEnded` queue-end branch raises `PreloadOnly`; `NextTrackId` is null → `preloadNext(null)` clears idle. |
| Remove-current / Clear / emptied queue | `Fresh` with null `Current` → stop both + clear preload. |
| Seek / pause | Active element only. |
| Volume | Both elements. |
| Rebuffer counter | Reset on every track start, including `Advance`. |
| AAC/ALAC (MP4) priming/padding | Sub-perceptible gap may remain for those formats; FLAC (preferred/common) is clean. Accepted. |

## 10. Testing

**No test project exists today.** Add `Nugsdotnet.UI.Tests` (xUnit). `PlayerService` has no DI dependencies — `new PlayerService()` works directly; assert `TrackChangeKind` by subscribing to `TrackChangeRequested` and capturing the arg, and read `NextTrackId`/`Index`/`Current` directly.

**Unit tests (PlayerService):**
- Kind classification: `Play`/`Previous`/`JumpTo` → `Fresh`; `Next` and `HandleEnded`→`Next` → `Advance`; append `Enqueue`, insert `PlayNext`, `RemoveAt` non-current, queue-end → `PreloadOnly`.
- `NextTrackId` across the StateChanged-only mutations (the bug-prone core): `RemoveAt` of the on-deck track (`index == _index+1`), `RemoveAt` before cursor (`index < _index`), `RemoveAt` far-upcoming (`> _index+1`, unchanged), `PlayNext` insert, `Enqueue` append on the last track, `HandleEnded` at queue end (→ null).
- Idle-start paths: `Enqueue`/`PlayNext` when `Current` is null or `_ended` → `Fresh` start.

**Manual matrix (JS/audio — verified via the `verify`/`run` skill):**
- Play an album with segues → no audible gap.
- After an `Advance` swap: seek, pause/resume, volume, and skip all act on the **audible** element; dashboard telemetry + Transport seek bar track the audible element.
- Rapid double-Next; Next immediately after Play (before preload resolves) → clean cold-load fallback, no double-advance/skip.
- Preload-element 415/404 (next track HLS-only) → current track keeps playing, no spurious skip.
- Queue end → idle element cleared.
- **Native (WebView2):** with a real FLAC track, the idle element opens a second loopback connection and reaches `bufferedAhead > 0` before the active track ends, then advances instantly. Confirm a >5-min track's preload survives (validates §8a).

## 11. Verification notes / risks

- **WebView2 autoplay:** the native head uses `AddMauiBlazorWebView()` defaults with no autoplay-policy override. The original Play click grants sticky per-document autoplay that normally covers a second element, but verify a second element's programmatic `play()` after `ended` is permitted on WebView2 (loopback origin). If not, add `--autoplay-policy=no-user-gesture-required` via WebView2 `AdditionalBrowserArguments`. The `await`ed `play()` + fallback (§6) makes any failure graceful regardless.
- **WebView2 preload semantics:** `preload="auto"` may not advance `readyState` without touching `currentTime`; hence the `currentTime = 0` nudge in `preloadNext`. The readiness gate plus `waiting` guard prevent a marginal buffer from producing a silent gap.

## 12. Considered and rejected

The review's 11 rejected findings (recorded for due diligence):

- **Loopback `Port == 0` race** — impossible: `LoopbackServer.StartAsync` is awaited synchronously in `MauiProgram` *before* the MAUI app/WebView is built, so `Port` is bound before any component or JS runs.
- **`StorePick` seed race (preload vs dashboard)** — benign: the separate 4 h `_cache` layer serves built specs without re-reading the single-use seed; worst case is one extra re-probe, never wrong data. (Two findings.)
- **`webkitAudioDecodedByteCount` reset on swap** — benign: the only consumer is `EffectiveBitrate`, a bytes/time *ratio*; both reset together on the new track, so it stays self-consistent. The raw value is never displayed.
- **"Manual Next advances to wrong track" / "re-sync re-points the active element"** — not reachable: JS owns idle-element identity (.NET only passes a URL), the readiness gate + fallback guard a stale preload, and `_preloadedTrackId` is a string, not an element ref. (The §5 ordering rule + JS guard make this airtight.) (Two findings.)
- **"HandleEnded leaves stale telemetry at queue end"** — benign: preload was already cleared when the last track became active; matches today's stop behavior.
- **Several testing-prose nits** — already covered by §10 or low-value. (Four findings.)

---

*This design preserves the locked architecture; §§4–8 are realization detail surfaced by adversarial review. Implementation should land the JS two-element machine, the `@onended` removal, and `OnTrackEnded` atomically (removing `@onended` without the others breaks advancement).*

## 13. Addendum (2026-06-17) — JS hot-path for natural-end advance

Native verification confirmed gapless preload + autoplay work, but left a *tiny* residual gap on auto-advance: the JS→.NET→JS round-trip that decision #2 ("advancement in .NET") accepted. To remove it, the **natural-end advance now happens in JS**, with .NET following — a contained revision of decision #2. **Manual Next/Previous/JumpTo and all queue edits stay fully .NET-driven** (unchanged).

- **JS `_onEnded`** (active element only): calls a shared synchronous **`_doSwap()`** (the swap core extracted from `advanceToPreloaded`: readiness gate → flip active index → pause/clear outgoing → rebind telemetry → `play()` the preloaded element). On success it fires-and-forgets the play promise and calls **`OnAdvancedViaPreload`**; if `_doSwap` returns false (preload not ready / queue end) it falls back to the existing **`OnTrackEnded`** cold path.
- **`PlayerService.AdvanceFromPreload()`**: advances the cursor (`_index++`, clears `_ended`) and raises `StateChanged` + **`TrackChangeRequested(PreloadOnly)`** — never `Advance`, since the audio already swapped in JS. The existing `PreloadOnly` handler then re-points the idle element at the new next track (the `_preloadedTrackId` diff naturally targets N+2).
- **`MainLayout.OnAdvancedViaPreload()`** `[JSInvokable]` → `Player.AdvanceFromPreload()`.
- **`advanceToPreloaded`** (still used by manual Next / cold-load fallback) is refactored to call `_doSwap()` then `await` the play promise + run the stall guard. Both paths share `_doSwap` (DRY).

Result: the audio path on natural end has zero interop hops → effectively sample-seamless for FLAC. Unit-tested via `AdvanceFromPreload` (advances + emits `PreloadOnly`; no-op at queue end); JS verified manually on the native head.
