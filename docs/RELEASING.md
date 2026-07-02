# Releasing nugsdotnet

Releases are cut by pushing a version tag. The
[`release` workflow](../.github/workflows/release.yml) publishes the native
app self-contained, builds the installer, publishes a GitHub Release, and
generates the winget manifest.

## Prerequisites for end users

- Windows 10 2004+ or Windows 11, x64 (runs on arm64 via x64 emulation).
- Nothing else — the build is self-contained (.NET and the Windows App SDK are
  bundled; no WebView2, no runtime installs).

## Cut a release

```
git checkout main
git pull
git tag v0.3.0      # must be vMAJOR.MINOR.PATCH; the leading v is stripped
git push origin v0.3.0
```

The workflow then:

1. Publishes `native/Nugsdotnet.Native` self-contained (win-x64) and builds
   `nugsdotnet-0.3.0-x64-setup.exe`.
2. Creates the GitHub Release with the installer attached.
3. Generates the winget manifest from the checked-in template in
   `packaging/winget/` — filling in the release URL and the installer's
   SHA256 — and attaches `nugsdotnet-0.3.0-winget-manifests.zip` to the
   release.

The tag is the single source of truth for the version — it flows into the
assembly (`-p:Version`), the installer filename, and the manifest. No file
needs editing to bump the version.

Upgrades replace the retired MAUI-era install in place: the installer keeps
the same `AppId`, and clears the app directory on install (the exe name
changed across the MAUI→native transition). User data is untouched — session,
recents, and playback state live in `%LOCALAPPDATA%\nugsdotnet`.

## Install locally

Download the manifests asset from a release, unzip it, then:

```
winget install --manifest .\nugsdotnet-<version>-winget-manifests
```

Or just download and run the `…-x64-setup.exe` directly. The repo is public, so
the manifest's `InstallerUrl` is anonymously downloadable.

## Publish to the public winget catalog (optional)

`winget install nugsdotnet` from the **default** source needs a one-time PR to
`microsoft/winget-pkgs`. The repo is public and a `LICENSE` (MIT) is in place
and reflected in the manifest, so all that's left is the token:

1. Create a **classic PAT** with `public_repo` scope.
2. Add it as the repo secret **`WINGET_TOKEN`** (Settings → Secrets and
   variables → Actions).

With the secret present, the release workflow's final step downloads
`wingetcreate` and submits the complete generated manifests — it forks
`microsoft/winget-pkgs` for you and opens the PR. Without the secret, that
step is skipped. The PR fires on the next release tag; to submit the
**current** version, re-run the latest `release` workflow run after adding
the secret.

Note this publicly lists an unofficial third-party nugs.net client.

## Enable code signing (optional, removes SmartScreen warning)

Unsigned installers trigger a SmartScreen "unknown publisher" prompt. To fix:

1. Set up **Azure Trusted Signing** (~$10/mo, no hardware token):
   <https://learn.microsoft.com/azure/trusted-signing/>.
2. Replace the "Sign installer (placeholder hook)" step in
   [`.github/workflows/release.yml`](../.github/workflows/release.yml) with the
   `azure/trusted-signing-action`, signing
   `packaging/Output/nugsdotnet-<version>-x64-setup.exe` **after** the "Build
   installer" step and **before** "Create GitHub Release".

## Smoke-testing the pipeline

To exercise the workflow without a real `0.3.0` release, push a pre-release
tag and watch it, then delete it:

```
git tag v0.3.0-rc1 && git push origin v0.3.0-rc1
# watch the run on the Actions tab
# confirm the release has both the setup.exe and the winget-manifests.zip
# then delete the release + tag from the Releases page (or gh release delete)
```
