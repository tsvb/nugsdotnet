# Winget distribution design

**Date:** 2026-06-17
**Status:** Approved (design); pending implementation plan
**Scope:** Package `Nugsdotnet.App` (Windows MAUI Blazor Hybrid desktop app) for
distribution via the Windows Package Manager (winget).

## Goal

Let the app be installed with `winget install`. Produce an installable artifact
and a winget manifest that work **locally today** (no public listing required),
while leaving public submission to `microsoft/winget-pkgs` as a single optional
flip we can do later. The whole flow is driven by a GitHub Actions release
pipeline so that cutting a release is one tagged push.

## Context

- `Nugsdotnet.App` is a .NET 10 MAUI Blazor Hybrid desktop app, Windows-only for
  Phase 1 (`src/Nugsdotnet.App/Nugsdotnet.App.csproj`).
- It currently builds **unpackaged** (`<WindowsPackageType>None</WindowsPackageType>`).
  The `Platforms/Windows/Package.appxmanifest` is still the untouched MAUI
  placeholder and is not used.
- No CI, no git tags, no installer scripts exist yet. Repo:
  `https://github.com/tsvb/nugsdotnet`.
- Credentials (nugs.net) are supplied at runtime via user-secrets / env /
  appsettings. Nothing sensitive is baked into the build — safe to distribute.

## Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Distribution target | Build to install **locally now**; public winget-pkgs submission is an optional later flip | User wants to keep the "personal use" posture open while having the mechanics ready |
| Installer technology | **Inno Setup** wrapping a self-contained publish | Works with the existing unpackaged build, no MSIX signing prerequisite, winget knows `inno` silent switches natively |
| Build profile | **Self-contained**, bundling .NET + Windows App SDK runtimes | Zero prerequisites for users; accepts a larger payload as the tradeoff |
| Architecture | **x64 only** for now | Matches current build (`win-x64`); arm64 can be added later |
| Install scope | **Per-user** (`PrivilegesRequired=lowest`) | No UAC prompt; best winget UX |
| Package identifier | **`tsvb.nugsdotnet`** | Matches GitHub handle/repo |
| Versioning | Driven by the git tag `vX.Y.Z` | Single source of truth for build + installer + manifest |
| Code signing | **Deferred**, with a documented hook | Avoid cost/complexity now; Azure Trusted Signing drop-in later |
| Automation | **Full GitHub Actions pipeline** | Releases become one `git tag` push |

## Architecture / components

### 1. Build profile

Self-contained, unpackaged publish of `Nugsdotnet.App`. Keeps
`WindowsPackageType=None`; bundles both the .NET runtime and the Windows App SDK
runtime so users need no prerequisites:

```
dotnet publish src/Nugsdotnet.App/Nugsdotnet.App.csproj -c Release \
  -f net10.0-windows10.0.19041.0 -p:RuntimeIdentifier=win-x64 \
  -p:SelfContained=true -p:WindowsAppSDKSelfContained=true
```

- Output folder (`bin/Release/net10.0-windows10.0.19041.0/win-x64/publish/`) is
  the installer's input.
- **Not bundled:** Evergreen WebView2 runtime — present on all Win10 2004+/Win11
  machines. Documented as a requirement, not installed.
- **Tradeoff:** self-contained MAUI publish is typically ~200–400 MB on disk;
  the compressed installer is expected ~100 MB. This is the cost of "no
  prerequisites." Trimming is left **off** (BlazorWebView/MAUI rely on
  reflection and trim poorly).

### 2. Installer — Inno Setup (`packaging/installer.iss`)

- `PrivilegesRequired=lowest` → installs to `%LocalAppData%\Programs\nugsdotnet`,
  no UAC.
- Stable `AppId` GUID, generated once and never changed — winget's identity
  anchor via the uninstall registry entry.
- Start-menu shortcut; automatic uninstaller registered in Add/Remove Programs
  (the registry entry winget uses to track install state).
- Version injected at compile time: `iscc /DMyAppVersion=<version> ...`.
- Output: `nugsdotnet-<version>-x64-setup.exe`. Compression lzma2/max.
- Silent switches are the Inno defaults winget already knows
  (`/VERYSILENT /SUPPRESSMSGBOXES /NORESTART`), so the manifest needs no custom
  `InstallerSwitches`.

### 3. Icon asset (`packaging/nugsdotnet.ico`)

Inno needs a real `.ico` for the setup icon and the Start-menu shortcut. Generate
a multi-resolution `.ico` from the existing `Resources/AppIcon/appicon.svg` and
commit it. (Generation tool decided at plan time — e.g. ImageMagick/Inkscape, or
a committed static file.)

### 4. winget manifest (`packaging/winget/`)

Standard 3-file manifest as the canonical identity/metadata template:

- `tsvb.nugsdotnet.installer.yaml` — `InstallerType: inno`, `Scope: user`,
  `Architecture: x64`. Per-version fields (`InstallerUrl`, `InstallerSha256`)
  are filled by the pipeline, not by hand.
- `tsvb.nugsdotnet.locale.en-US.yaml` — name, publisher, description, license,
  homepage, tags.
- `tsvb.nugsdotnet.yaml` — version manifest.

### 5. Release pipeline (`.github/workflows/release.yml`)

Trigger: push a tag matching `v*`. Runner: `windows-latest`. The tag (minus the
leading `v`) is the version for the build, the installer, and the manifest.

Steps:

1. `actions/setup-dotnet` (.NET 10) → `dotnet workload restore <App.csproj>` →
   `dotnet publish` (self-contained, as above).
2. `choco install innosetup` → `iscc /DMyAppVersion=<version> packaging/installer.iss`.
3. Create the GitHub Release (e.g. `softprops/action-gh-release`) and attach the
   setup exe.
4. **Komac** generates the complete manifest from the release asset and:
   - **always** writes the manifests to a folder and attaches them to the
     release → enables `winget install --manifest <dir>` for anyone without a
     public listing (the "local now" path);
   - **conditionally** opens the PR to `microsoft/winget-pkgs` **only if** a
     `WINGET_TOKEN` repo secret exists (the "public flip" — dormant until the
     token is added).

Result: `git tag v0.2.0 && git push --tags` produces a fully installable
release. Going public later = add one repo secret.

### 6. Signing (deferred)

Unsigned installer → users see a SmartScreen "unknown publisher" warning;
winget-pkgs accepts unsigned but moderators prefer signed. A clearly-marked
no-op signing step sits in the workflow as a hook. `docs/RELEASING.md` documents
the **Azure Trusted Signing** (~$10/mo, no hardware token) drop-in for when the
warning becomes worth removing.

### 7. Versioning

The git tag is the single source of truth. The workflow strips `v` from
`vX.Y.Z`. The csproj default `ApplicationDisplayVersion` is bumped to `0.2.0`
(matching the roadmap) for local/dev builds; tagged builds override it via
`-p:ApplicationDisplayVersion=<version>`.

## Files created / changed

- `packaging/installer.iss` (new)
- `packaging/nugsdotnet.ico` (new)
- `packaging/winget/tsvb.nugsdotnet.installer.yaml` (new)
- `packaging/winget/tsvb.nugsdotnet.locale.en-US.yaml` (new)
- `packaging/winget/tsvb.nugsdotnet.yaml` (new)
- `.github/workflows/release.yml` (new)
- `docs/RELEASING.md` (new — tag→release runbook, "go public" steps, signing path)
- `src/Nugsdotnet.App/Nugsdotnet.App.csproj` (version bump)

## Data flow

```
git tag vX.Y.Z ──► GitHub Actions (windows-latest)
                     │
                     ├─ dotnet publish (self-contained, win-x64)
                     │     └─► publish/ folder
                     ├─ iscc installer.iss ──► nugsdotnet-X.Y.Z-x64-setup.exe
                     ├─ create GitHub Release + attach setup.exe
                     └─ Komac
                          ├─ always: generate manifests ──► attach to release
                          │            (winget install --manifest works)
                          └─ if WINGET_TOKEN: PR ──► microsoft/winget-pkgs
                                                       (public listing)
```

## Error handling / edge cases

- **Missing WebView2 runtime** (rare on modern Windows): app fails to start.
  Documented as a prerequisite in `docs/RELEASING.md`. Optional future: an Inno
  check that offers the Evergreen bootstrapper.
- **No `WINGET_TOKEN`:** the public-PR step is skipped, not failed — the release
  still builds and publishes installer + manifests.
- **Unsigned binary:** SmartScreen warning expected; documented.
- **Public-listing policy:** `microsoft/winget-pkgs` moderators review
  submissions. This is an unofficial third-party nugs.net client requiring the
  user's own subscription; submission is deliberately gated behind the
  `WINGET_TOKEN` flip so it is an explicit, deliberate act.

## Non-goals (YAGNI)

- MSIX packaging / Microsoft Store listing.
- arm64 build.
- Per-machine (admin) install.
- Code signing in the first cut (hook only).
- Auto-update mechanism beyond what winget provides.
- A self-hosted private winget REST source.

## Open items for the implementation plan

- Exact `.ico` generation approach (tool vs committed file).
- Whether to pin Komac via its GitHub Action or install the CLI directly.
- Final manifest metadata copy (description, tags, license string).
