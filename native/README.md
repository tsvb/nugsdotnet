<p align="center">
  <a href="https://github.com/tsvb/nugsdotnet/actions/workflows/native.yml"><img src="https://github.com/tsvb/nugsdotnet/actions/workflows/native.yml/badge.svg" alt="native CI"></a>
  <img src="https://img.shields.io/badge/.NET-10-efe4cf?style=flat-square&labelColor=15120D&logo=dotnet&logoColor=ffb22e" alt=".NET 10">
  <img src="https://img.shields.io/badge/WinUI%203-unpackaged-ffb22e?style=flat-square&labelColor=15120D" alt="WinUI 3">
</p>

<p align="center"><em>The Windows app — layout, data on disk, build, and tests.<br>
Product overview lives in the <a href="../README.md">repo README</a>.</em></p>

---

## ◖ Layout

Everything that ships is under `native/`. Three projects:

| Project | TFM | Role |
|---|---|---|
| `Nugsdotnet.Native.Core` | `net10.0` | Auth, session, catalog, stream resolver, JSON shaping, local stores. Testable on any OS. |
| `Nugsdotnet.Native` | `net10.0-windows10.0.19041.0` | WinUI 3 app: `HttpAudioStream`, `MediaPlayer`, XAML, view models, RECEIVER '74. |
| `Nugsdotnet.Native.Tests` | `net10.0` | xUnit for Core. Cross-platform. |

Core has no WinUI types. The app references Core only.

---

## ◖ On disk

| Path | What |
|---|---|
| `%LOCALAPPDATA%\nugsdotnet\session.bin` | Access + refresh tokens. DPAPI CurrentUser + app entropy (`NDS1`). Older blobs still load and rewrite in place. |
| `%LOCALAPPDATA%\nugsdotnet\accounts\{userId}\` | `stash.json`, `recents.json`, `playback.json` — scoped to the nugs account, not the Windows profile. |
| `%LOCALAPPDATA%\nugsdotnet\` (root) | Window bounds. One live login per Windows user. |

`userId` is the OIDC `sub`, sanitized so path segments cannot escape the accounts folder. Sign-out deletes `session.bin` and unbinds the account stores.

---

## ◖ Power on

**.NET 10 SDK** on Windows 10 2004+ / Windows 11 (x64 or arm64). Windows App SDK is NuGet — no extra workload.

```powershell
dotnet build native\Nugsdotnet.Native\Nugsdotnet.Native.csproj -c Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64
dotnet run --project native\Nugsdotnet.Native\Nugsdotnet.Native.csproj -c Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64
```

Sign in on the login card (email + password). Then: search → play a show → seek and volume. That path hits auth → catalog → stream-resolve → `HttpAudioStream` → `MediaPlayer`.

`Microsoft.WindowsAppSDK` is pinned at **2.2.0** in `Nugsdotnet.Native.csproj`. If restore misses a package, bump to the current stable — `MediaPlayer`, `MediaPlaybackList`, and `IRandomAccessStream` are stable across 1.x/2.x.

---

## ◖ Tests

Core tests do not need Windows:

```bash
dotnet test native/Nugsdotnet.Native.Tests/Nugsdotnet.Native.Tests.csproj -c Release
```

They cover token handling and error sanitizing, the HTTPS URL allowlist, bounded HTTP reads, catalog JSON shaping, stream-format preference, and the recents / stash / playback / account stores.

CI (`.github/workflows/native.yml`) runs that suite on Ubuntu and compiles the WinUI head on `windows-latest` (Release x64). The XAML compiler is Windows-only, so the Windows job is the compile gate for UI changes.

Releases: [`docs/RELEASING.md`](../docs/RELEASING.md).

---

<p align="center"><sub>Not affiliated with nugs.net. Personal use against your own subscription.</sub></p>
