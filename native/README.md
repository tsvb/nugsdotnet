# nugsdotnet — native Windows client

[![native CI](https://github.com/tsvb/nugsdotnet/actions/workflows/native.yml/badge.svg)](https://github.com/tsvb/nugsdotnet/actions/workflows/native.yml)

A **standalone, 100% native** Windows desktop client for nugs.net, built with
**WinUI 3 (Windows App SDK)**. It reimplements the nugsdotnet feature set from
scratch and has **zero dependency** on the Blazor/MAUI projects in the parent
repo — its own solution, its own services, no shared code or packages.

> **Status: feature-complete player** (Phases 1–2 and most of Phase 3 landed).
> Only installer/winget packaging remains on the roadmap. CI compiles the WinUI
> head on `windows-latest` and runs the Core tests on every push to `native/`.

## What's in the box

- **RECEIVER '74 skin** — the warm-VFD dark/amber identity shared with the web
  head, running edge-to-edge under a custom title bar (branded drag strip +
  faceplate-coloured caption buttons). Big Shoulders / Hanken Grotesk / DM Mono
  brand fonts are bundled.
- **Home dashboard** — time-of-day greeting, a **Recently Played** art-card
  rail (persisted locally, capped at 12), and a filterable faceplate-chip
  artist grid with live count.
- **Browse** — artist pages (releases as art cards, virtualized show list),
  set-grouped album pages with a live amber now-playing row, sectioned search.
- **Transport** — clickable album art (returns to the show), prev / **−15** /
  play / **+30** / next with real disabled states, scrub-safe seek slider,
  elapsed/−remaining mono counters, mute toggle, and a resolved-format badge
  (FLAC 16 / ALAC 16 / MQA 24…) that lights amber for lossless.
- **Dashboard inspector** (`Ctrl+D`) — a mini player (art, seek, transport),
  **SIGNAL PATH** metrics measured off the live stream (size, average bitrate,
  ranged-read I/O counters, format/tier/container), and an **UP NEXT** queue
  with click-to-jump.
- **Gapless playback** — the current track plays from a `MediaPlaybackList`
  while the next resolves in the background and pre-rolls; a `MediaEnded`
  fallback guarantees a missed look-ahead gaps instead of stalling.
- **System integration** — media keys and the Windows media flyout (SMTC) with
  full title/artist/show/art metadata.

### Keyboard shortcuts

| key          | action              |
| ------------ | ------------------- |
| `Ctrl+F`     | Focus search        |
| `Ctrl+Space` | Play / pause        |
| `Ctrl+→`     | Next track          |
| `Ctrl+←`     | Previous track      |
| `Ctrl+D`     | Toggle dashboard    |

## Why it's simpler than the web/MAUI heads

The Blazor heads need a loopback **Kestrel proxy** because a WebView can't set the
`Referer`/`User-Agent` headers the nugs CDN requires and can't stream Range/206
audio through C#. A native app has neither limit:

- **Audio** is fed to `Windows.Media.Playback.MediaPlayer` from a custom
  `IRandomAccessStream` (`Audio/HttpAudioStream.cs`) that issues ranged GETs with
  the required headers — the in-process equivalent of the proxy, **no server**.
- **Formats**: Media Foundation decodes FLAC/ALAC/AAC natively on Windows 10+, so
  the native head can play formats the web head punts on.
- **Gapless queueing** comes from `MediaPlaybackList` (implemented — one-track
  look-ahead pre-roll) — no hand-rolled dual-`<audio>` swap.

## Layout

| Project | TFM | Role |
|---|---|---|
| `Nugsdotnet.Native.Core` | `net10.0` | Platform-agnostic: auth, session store, catalog, stream resolver, JSON shaping. Unit-testable on any OS. |
| `Nugsdotnet.Native` | `net10.0-windows…` | WinUI 3 app: HTTP audio stream, `MediaPlayer` playback, XAML views + view models. |
| `Nugsdotnet.Native.Tests` | `net10.0` | xUnit tests for the pure Core logic. Runs cross-platform. |

Tokens are persisted to `%LOCALAPPDATA%\nugsdotnet\session.bin`, **DPAPI-encrypted**
(CurrentUser scope) — an upgrade over the original plaintext `tokens.json`.

## Build & run (Windows)

Requires the **.NET 10 SDK** on Windows 10 2004+ / Windows 11 (x64 or arm64). No
MAUI workload is needed — the Windows App SDK is pure NuGet.

```powershell
# credentials via environment (the login screen's default), or type them in the UI
$env:NUGS_EMAIL = "you@example.com"
$env:NUGS_PASSWORD = "your-password"

dotnet run --project native\Nugsdotnet.Native\Nugsdotnet.Native.csproj
```

Then: sign in → search a band/song → press ▶ on a result → confirm audio, seek, and
volume. That single path exercises auth → catalog → stream-resolve →
`HttpAudioStream` → `MediaPlayer`.

> **Package versions** in `Nugsdotnet.Native.csproj` (`Microsoft.WindowsAppSDK`
> 2.2, which pulls a matching `SDK.BuildTools` transitively) are best-effort
> pins. If restore can't find one, bump to the latest stable — the APIs used
> here (MediaPlayer, MediaPlaybackList, IRandomAccessStream, AppWindowTitleBar)
> are stable across 1.x/2.x.

## Test (any OS)

The Core logic tests don't need Windows:

```bash
dotnet test native/Nugsdotnet.Native.Tests/Nugsdotnet.Native.Tests.csproj
```

They cover `IdentifyFormat`, the stream-pick preference order, MIME mapping,
the defensive `NugsShape` JSON digging, and the `RecentsStore` merge/round-trip
behaviour behind the Home dashboard's Recently Played rail. The same suite runs
in CI (`.github/workflows/native.yml`) alongside a `windows-latest` job that
compiles the WinUI head — the XAML compiler is Windows-only, so that job is the
compile gate for UI changes.

## Extract into its own repository

The `native/` subtree is fully self-contained. To lift it into a standalone repo
with its own history:

```bash
# from the parent repo root
git subtree split --prefix=native -b native-only
# in a fresh empty repo:
git pull /path/to/this/repo native-only
```

(Or `git filter-repo --path native/ --path-rename native/:` for a full rewrite.)
Nothing in `native/` references the parent projects, so it builds unchanged once moved.

## Roadmap

- **Phase 1 (done)** — auth, search, single-track native playback, basic transport.
- **Phase 2 (done)** — artist landing + album detail; queue UI (the dashboard
  inspector's up-next with click-to-jump), enqueue/play-next + prev/next;
  keyboard shortcuts and System Media Transport Controls with full metadata;
  image loading (direct CDN GET + in-memory cache); the RECEIVER '74 re-skin,
  custom title bar, and Home dashboard with the recently-played rail.
- **Phase 3 (mostly done)** — stream-quality dashboard ✓ (the inspector's
  SIGNAL PATH section); true gapless via `MediaPlaybackList` ✓ (one-track
  look-ahead pre-roll, resolve-on-advance fallback). **Remaining:** Inno Setup
  installer + winget manifest mirroring the parent `packaging/` setup.
- **Ideas beyond Phase 3** — resume-on-launch (persist queue + position), HLS
  playback instead of auto-skip, remembered window/volume state.

Not affiliated with nugs.net. For personal use against your own subscription.
