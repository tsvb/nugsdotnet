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
3. Generates the winget manifest from the checked-in template in
   `packaging/winget/` — filling in the release URL and the installer's SHA256 —
   and attaches `nugsdotnet-0.2.0-winget-manifests.zip` to the release. (Komac
   is used only for the optional public submission below, not here.)

The tag is the single source of truth for the version — it flows into the build
(`ApplicationDisplayVersion`), the installer filename, and the manifest. No file
needs editing to bump the version.

## Install locally

Download the manifests asset from a release, unzip it, then:

```
winget install --manifest .\nugsdotnet-<version>-winget-manifests
```

Or just download and run the `…-x64-setup.exe` directly. The repo is public, so
the manifest's `InstallerUrl` is anonymously downloadable.

## Publish to the public winget catalog (optional)

The repo is public, so the only thing left to make `winget install nugsdotnet`
work from the **default** source is the auto-submit to `microsoft/winget-pkgs`:

1. Add a **`LICENSE`** file and replace `License: Proprietary` in
   [`packaging/winget/tsvb.nugsdotnet.locale.en-US.yaml`](../packaging/winget/tsvb.nugsdotnet.locale.en-US.yaml)
   with its SPDX id — `winget-pkgs` moderators expect a real license.
2. Fork `microsoft/winget-pkgs` to your account.
3. Create a classic PAT with `public_repo` scope and add it as the repo secret
   **`WINGET_TOKEN`** (Settings → Secrets and variables → Actions).

With the secret present, the release workflow's final step opens the PR; without
it, that step is skipped. Note this publicly lists an unofficial third-party
nugs.net client — a different posture from "personal use only."

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
