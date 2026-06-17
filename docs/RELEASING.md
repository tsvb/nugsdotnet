# Releasing nugsdotnet

Releases are cut by pushing a version tag. The
[`release` workflow](../.github/workflows/release.yml) builds the installer,
publishes a GitHub Release, and generates the winget manifest.

## Prerequisites for end users

- Windows 10 2004+ or Windows 11, x64 (runs on arm64 via x64 emulation).
- Evergreen **WebView2 runtime** — preinstalled on all current Windows. If the
  app launches and immediately exits, install it from
  <https://developer.microsoft.com/microsoft-edge/webview2/>.
- No .NET install needed — the build is self-contained.

## Cut a release

```
git checkout main
git pull
git tag v0.2.0      # must be vMAJOR.MINOR.PATCH; the leading v is stripped
git push origin v0.2.0
```

The workflow then:

1. Publishes the self-contained app and builds `nugsdotnet-0.2.0-x64-setup.exe`.
2. Creates the GitHub Release with the installer attached.
3. Generates the winget manifest with Komac and attaches
   `nugsdotnet-0.2.0-winget-manifests.zip` to the release.

The tag is the single source of truth for the version — it flows into the build
(`ApplicationDisplayVersion`), the installer filename, and the manifest. No file
needs editing to bump the version.

## Install locally (no public listing)

Download and unzip the manifests asset from the release, then:

```
winget install --manifest .\nugsdotnet-0.2.0-winget-manifests
```

Or just run the `…-x64-setup.exe` directly.

## Go public (optional)

To auto-open a PR to `microsoft/winget-pkgs` on each release:

1. Fork `microsoft/winget-pkgs` to your account.
2. Create a classic PAT with `public_repo` scope (access to your fork).
3. Add it as the repo secret **`WINGET_TOKEN`** (Settings → Secrets and
   variables → Actions).

With the secret present, the workflow's final step submits the PR; without it,
that step is skipped — so releases keep working privately until you opt in.

**Before going public,** replace `License: Proprietary` in
[`packaging/winget/tsvb.nugsdotnet.locale.en-US.yaml`](../packaging/winget/tsvb.nugsdotnet.locale.en-US.yaml)
with a real license (and add a `LICENSE` file to the repo) — `winget-pkgs`
moderators expect one. Note that public submission lists an unofficial
third-party nugs.net client, a different posture from "personal use only."

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

To exercise the workflow without a real `0.2.0` release, push a pre-release tag
and watch it, then delete it:

```
git tag v0.2.0-rc1 && git push origin v0.2.0-rc1
gh run watch
# confirm the release has both the setup.exe and the winget-manifests.zip
gh release delete v0.2.0-rc1 --cleanup-tag --yes
```
