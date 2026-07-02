<p align="center">
  <img src="docs/assets/banner.png" alt="nugsdotnet — a personal hi-fi front-end for nugs.net live music" width="100%">
</p>

<p align="center">
  <a href="https://github.com/tsvb/nugsdotnet/actions/workflows/native.yml"><img src="https://github.com/tsvb/nugsdotnet/actions/workflows/native.yml/badge.svg" alt="native CI"></a>
  <img src="https://img.shields.io/badge/.NET-10-efe4cf?style=flat-square&labelColor=15120D&logo=dotnet&logoColor=ffb22e" alt=".NET 10">
  <img src="https://img.shields.io/badge/Windows-x64%20%7C%20arm64-9a8b6e?style=flat-square&labelColor=15120D&logo=windows&logoColor=efe4cf" alt="Windows">
  <img src="https://img.shields.io/badge/WinUI%203-100%25%20native-ffb22e?style=flat-square&labelColor=15120D" alt="WinUI 3 native">
  <img src="https://img.shields.io/badge/license-MIT-ffb22e?style=flat-square&labelColor=15120D" alt="MIT license">
</p>

<p align="center"><em>A personal hi-fi front panel for <a href="https://nugs.net">nugs.net</a> live music — a <b>100% native WinUI 3</b> Windows app. Fast search, a real queue, gapless playback, keyboard-first. No WebView, no proxy, no DRM games.</em></p>

---

## ◖ Why this rig exists

The official nugs UI has slow search, weak queue/playlist UX, and no keyboard
shortcuts. nugsdotnet is a faster front panel for the same catalog, run against
**your own subscription**. It streams what you're entitled to and nothing more —
no content is downloaded, redistributed, or stripped of DRM. Personal use only.

### Spec sheet

| | |
|---|---|
| **App** | WinUI 3 (Windows App SDK 2.x) · unpackaged, self-contained |
| **Runtime** | .NET 10 · Windows 10 2004+ / Windows 11, x64 or arm64 |
| **Audio** | FLAC 16/44 preferred, ALAC / MQA / AAC fallbacks · **gapless** via `MediaPlaybackList` look-ahead |
| **Auth** | nugs password grant · tokens DPAPI-encrypted at rest |
| **Identity** | RECEIVER '74 — warm-VFD dark/amber, custom title bar, brand type |

---

## ◖ Signal path

No proxy, no WebView: the app talks to nugs directly. Auth and catalog calls go
straight to the API, and audio feeds `MediaPlayer` from an in-process
`IRandomAccessStream` that issues ranged CDN GETs with the required
`Referer`/`User-Agent` itself — the whole reason the retired web heads needed a
loopback server, done in-process.

```
  nugs.net API + CDN
        │
  ┌─────┴──────────────────────────────────────────────┐
  │ Nugsdotnet.Native.Core      auth · catalog · picks │   net10.0, tested on any OS
  ├────────────────────────────────────────────────────┤
  │ Nugsdotnet.Native           WinUI 3 front panel    │   HttpAudioStream → MediaPlayer
  └────────────────────────────────────────────────────┘
```

| Project | Role |
|---|---|
| [`native/Nugsdotnet.Native.Core`](native/Nugsdotnet.Native.Core) | Platform-agnostic: auth, DPAPI session store, catalog, stream resolver, recents |
| [`native/Nugsdotnet.Native`](native/Nugsdotnet.Native) | The WinUI 3 app: views, playback, imaging, RECEIVER '74 theme |
| [`native/Nugsdotnet.Native.Tests`](native/Nugsdotnet.Native.Tests) | xUnit suite for Core — runs cross-platform, gates CI |

---

## ◖ Power on

Requires the **.NET 10 SDK** on Windows. No MAUI workload, no MSIX — the
Windows App SDK arrives via NuGet.

```powershell
# credentials via environment (the login screen's default), or type them in the UI
$env:NUGS_EMAIL = "you@example.com"
$env:NUGS_PASSWORD = "your-password"

dotnet run --project native\Nugsdotnet.Native\Nugsdotnet.Native.csproj
```

The full tour — dashboard home, transport, the `Ctrl+D` mini-player inspector
with live stream metrics, gapless internals, roadmap — lives in
[`native/README.md`](native/README.md).

### Front panel — keyboard shortcuts

| key          | action           |
| ------------ | ---------------- |
| `Ctrl+F`     | Focus search     |
| `Ctrl+Space` | Play / pause     |
| `Ctrl+→`     | Next track       |
| `Ctrl+←`     | Previous track   |
| `Ctrl+D`     | Toggle dashboard |

Media keys and the Windows media flyout work too (SMTC with full metadata).

---

## ◖ On the dial — roadmap

Phases 1–3 are landed (player, browse, dashboard, gapless, SMTC). Remaining:

- **Installer + winget** — per-user Inno Setup + manifest for the native app.
- **Ideas** — resume-on-launch, HLS playback instead of auto-skip, remembered
  window/volume state.

---

<details>
<summary><b>◖ History</b> — the web &amp; MAUI era</summary>

<br>

This repo originally hosted three heads: a Blazor WebAssembly web app, a .NET
MAUI Blazor Hybrid desktop app, and this native head. The WinUI client was the
goal all along — once it reached feature parity (and then some), the
Blazor/MAUI projects were retired. They live on in git history through the
`v0.2.1` tag and its releases, alongside the design notes under
[`docs/superpowers/`](docs/superpowers).

</details>

---

<p align="center"><sub>
Built with .NET 10 · WinUI 3 · Windows App SDK — for personal use against your own nugs.net subscription. Not affiliated with nugs.net.
</sub></p>
