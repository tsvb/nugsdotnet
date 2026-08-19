# Releasing nugsdotnet

Push a `v*` tag. The [`release` workflow](../.github/workflows/release.yml)
publishes a self-contained WinUI build, builds the Inno Setup installer, opens
a GitHub Release, and attaches winget manifests.

## Who can run it

| | |
|---|---|
| **OS** | Windows 10 2004+ or Windows 11 |
| **Arch** | x64 installer (arm64 runs it via x64 emulation) |
| **Runtimes** | None — .NET and the Windows App SDK are bundled |

## Cut a release

```powershell
git checkout main
git pull
git tag v0.3.0      # vMAJOR.MINOR.PATCH — the leading v is stripped
git push origin v0.3.0
```

The tag is the only version input. It flows into the assembly (`-p:Version`),
the installer filename, and the winget manifests. No file needs a bump.

The workflow then:

1. Publishes `native/Nugsdotnet.Native` self-contained (`win-x64`) and builds
   `nugsdotnet-0.3.0-x64-setup.exe`.
2. Creates the GitHub Release with that installer attached.
3. Fills [`packaging/winget/`](../packaging/winget/) with this release's URL and
   SHA-256, then attaches `nugsdotnet-0.3.0-winget-manifests.zip`.

The installer is **per-user** (no UAC). `AppId` is stable so upgrades replace
the previous install in place. The app directory is cleared on upgrade so
stale assemblies do not linger; user data is left alone:

| | |
|---|---|
| Session | `%LOCALAPPDATA%\nugsdotnet\session.bin` |
| Stash / recents / playback | `%LOCALAPPDATA%\nugsdotnet\accounts\{userId}\` |

## Install from a release

Download and run `nugsdotnet-<version>-x64-setup.exe`, or unzip the manifests
asset and:

```powershell
winget install --manifest .\nugsdotnet-<version>-winget-manifests
```

The repo is public, so `InstallerUrl` is anonymously downloadable.

## Public winget catalog (optional)

`winget install nugsdotnet` from the default source needs a one-time PR to
`microsoft/winget-pkgs`. MIT `LICENSE` is already in the manifest.

1. Create a **classic PAT** with `public_repo` scope.
2. Store it as the Actions secret **`WINGET_TOKEN`**.

With the secret present, the last release step runs `wingetcreate submit` on
the generated manifests (forks `winget-pkgs` and opens the PR). Without it,
that step is skipped. The PR fires on the next tag; to submit the **current**
version, re-run the latest `release` workflow after adding the secret.

That listing is an unofficial third-party nugs.net client.

## Code signing (optional)

Unsigned installers get a SmartScreen “unknown publisher” prompt.

1. Set up [Azure Trusted Signing](https://learn.microsoft.com/azure/trusted-signing/).
2. Replace the “Sign installer (placeholder hook)” step in
   [`.github/workflows/release.yml`](../.github/workflows/release.yml) with
   `azure/trusted-signing-action`, signing
   `packaging/Output/nugsdotnet-<version>-x64-setup.exe` **after** “Build
   installer” and **before** “Create GitHub Release”.

## Smoke-test the pipeline

Push a pre-release tag, watch Actions, then delete the tag and release:

```powershell
git tag v0.3.0-rc1
git push origin v0.3.0-rc1
# confirm the release has setup.exe + winget-manifests.zip
# then delete from the Releases page (or: gh release delete v0.3.0-rc1 --cleanup-tag)
```
