# nugsdotnet — native Windows client

A **standalone, 100% native** Windows desktop client for nugs.net, built with
**WinUI 3 (Windows App SDK)**. It reimplements the nugsdotnet feature set from
scratch and has **zero dependency** on the Blazor/MAUI projects in the parent
repo — its own solution, its own services, no shared code or packages.

> **Status: Phase 1 vertical slice.** Sign in → search → play a track natively.
> Artist/album browsing, the full queue UI, keyboard shortcuts, the quality
> dashboard, and the installer/winget packaging are later phases (see *Roadmap*).

## Why it's simpler than the web/MAUI heads

The Blazor heads need a loopback **Kestrel proxy** because a WebView can't set the
`Referer`/`User-Agent` headers the nugs CDN requires and can't stream Range/206
audio through C#. A native app has neither limit:

- **Audio** is fed to `Windows.Media.Playback.MediaPlayer` from a custom
  `IRandomAccessStream` (`Audio/HttpAudioStream.cs`) that issues ranged GETs with
  the required headers — the in-process equivalent of the proxy, **no server**.
- **Formats**: Media Foundation decodes FLAC/ALAC/AAC natively on Windows 10+, so
  the native head can play formats the web head punts on.
- **Gapless queueing** comes free from `MediaPlaybackList` (later phase) — no
  hand-rolled dual-`<audio>` swap.

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

> **Package versions** in `Nugsdotnet.Native.csproj` (`Microsoft.WindowsAppSDK`,
> `Microsoft.Windows.SDK.BuildTools`) are best-effort pins. If restore can't find
> one, bump to the latest stable `1.x` — the APIs used here are stable across 1.x.

## Test (any OS)

The Core logic tests don't need Windows:

```bash
dotnet test native/Nugsdotnet.Native.Tests/Nugsdotnet.Native.Tests.csproj
```

They cover `IdentifyFormat`, the stream-pick preference order, MIME mapping, and
the defensive `NugsShape` JSON digging.

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
- **Phase 2 (done)** — artist landing + album detail; queue UI (dashboard
  inspector with up-next jump), enqueue/play-next + prev/next; keyboard
  shortcuts (KeyboardAccelerators) and System Media Transport Controls with
  full metadata; image loading (direct CDN GET + in-memory cache); RECEIVER '74
  re-skin, custom title bar, Home dashboard with recently-played rail.
- **Phase 3** — stream-quality dashboard **(done — the inspector's STREAM
  QUALITY section)**; true gapless via `MediaPlaybackList`; Inno Setup
  installer + winget manifest mirroring the parent `packaging/` setup.

Not affiliated with nugs.net. For personal use against your own subscription.
