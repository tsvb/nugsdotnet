# Winget Distribution Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship `Nugsdotnet.App` as a winget-installable Windows app via an Inno Setup installer, a winget manifest, and a tag-triggered GitHub Actions release pipeline that installs locally now and can flip to public `winget-pkgs` submission later.

**Architecture:** A self-contained, unpackaged `dotnet publish` produces a no-prerequisites app folder. Inno Setup wraps it into a per-user installer. A GitHub Actions workflow (triggered by a `v*` tag) builds the installer, publishes a GitHub Release, and uses Komac to generate the winget manifest — always attaching it to the release (local install) and optionally opening a `winget-pkgs` PR when a `WINGET_TOKEN` secret exists (public listing).

**Tech Stack:** .NET 10 MAUI (net10.0-windows10.0.19041.0, win-x64), Inno Setup 6, ImageMagick (one-time ICO gen), Komac, GitHub Actions, winget manifest schema 1.6.0.

## Global Constraints

- **SDK/runtime:** .NET 10 (local: 10.0.301). TFM: `net10.0-windows10.0.19041.0`. RID: `win-x64`. Workload: `maui-windows`.
- **Packaging:** keep `<WindowsPackageType>None</WindowsPackageType>`. Build is **self-contained** (`-p:SelfContained=true -p:WindowsAppSDKSelfContained=true`).
- **Install:** per-user, no UAC (`PrivilegesRequired=lowest`). Install dir resolves to `%LocalAppData%\Programs\nugsdotnet`.
- **Identity (never change):** winget `PackageIdentifier` = `tsvb.nugsdotnet`; Inno `AppId` = `{{8B3F2A14-9C7D-4E6B-A1F0-5D2E7C9B4A60}}`.
- **Versioning:** the git tag `vX.Y.Z` is the single source of truth; the workflow strips the leading `v`. csproj default `ApplicationDisplayVersion` = `0.2.0`.
- **Manifest:** schema `ManifestVersion: 1.6.0`. Publisher: `Tim Vanbenschoten`. Repo URL: `https://github.com/tsvb/nugsdotnet`.
- **Architecture scope:** x64 only. **Signing:** deferred (no-op hook + documented Azure Trusted Signing path).
- **Local tooling gap:** no `choco` locally — install Inno Setup/ImageMagick with `winget`. CI runner (`windows-latest`) has `choco`.
- **Branch:** all work on `feat/winget-distribution` (already checked out). One commit per task.
- **Runtime UI caveat:** the dev/native MAUI window can't be screenshotted or driven by automation. Verify install *mechanics* (files, shortcut, uninstall registry key, process launch); confirming the UI actually renders is the user's manual check.

---

### Task 1: Bump version and verify the self-contained publish

**Files:**
- Modify: `src/Nugsdotnet.App/Nugsdotnet.App.csproj` (line 24, `ApplicationDisplayVersion`)

**Interfaces:**
- Produces: the publish output folder `src/Nugsdotnet.App/bin/Release/net10.0-windows10.0.19041.0/win-x64/publish/` containing `Nugsdotnet.App.exe` — consumed by Task 3 (Inno) and Task 5 (CI).

- [ ] **Step 1: Bump the display version**

In `src/Nugsdotnet.App/Nugsdotnet.App.csproj`, change:
```xml
<ApplicationDisplayVersion>1.0</ApplicationDisplayVersion>
```
to:
```xml
<ApplicationDisplayVersion>0.2.0</ApplicationDisplayVersion>
```

- [ ] **Step 2: Run the self-contained publish**

Run (pwsh or git-bash):
```
dotnet publish src/Nugsdotnet.App/Nugsdotnet.App.csproj -c Release -f net10.0-windows10.0.19041.0 -p:RuntimeIdentifier=win-x64 -p:SelfContained=true -p:WindowsAppSDKSelfContained=true -p:WindowsPackageType=None
```
Expected: build succeeds, exit code 0. First run is slow (restores RID-specific runtime).

- [ ] **Step 3: Verify the publish output is complete and self-contained**

Run (git-bash):
```
ls "src/Nugsdotnet.App/bin/Release/net10.0-windows10.0.19041.0/win-x64/publish/Nugsdotnet.App.exe" \
   "src/Nugsdotnet.App/bin/Release/net10.0-windows10.0.19041.0/win-x64/publish/WebView2Loader.dll" \
   "src/Nugsdotnet.App/bin/Release/net10.0-windows10.0.19041.0/win-x64/publish/Microsoft.WindowsAppRuntime.Bootstrap.dll"
```
Expected: all three paths exist (proves the app exe, the WebView2 loader, and the bundled Windows App SDK runtime are present). If `Microsoft.WindowsAppRuntime.Bootstrap.dll` is missing, `WindowsAppSDKSelfContained` did not take — re-check Step 2 flags.

- [ ] **Step 4: Commit**

```
git add src/Nugsdotnet.App/Nugsdotnet.App.csproj
git commit -m "build: set display version to 0.2.0 for winget release"
```

---

### Task 2: Generate and commit the application icon (`.ico`)

**Files:**
- Create: `packaging/nugsdotnet.ico` (committed binary; CI never regenerates it)

**Interfaces:**
- Produces: `packaging/nugsdotnet.ico` — consumed by Task 3 (`SetupIconFile`, shortcut icon).

- [ ] **Step 1: Install ImageMagick locally (one-time, for generation only)**

Run (pwsh):
```
winget install --id ImageMagick.ImageMagick -e --accept-package-agreements --accept-source-agreements
```
Then open a **fresh** shell (PATH refresh) so `magick` resolves. Verify:
```
magick --version
```
Expected: prints `Version: ImageMagick 7.x ...`.

- [ ] **Step 2: Generate a multi-resolution ICO from the existing app SVG**

Run (git-bash) from the repo root:
```
mkdir -p packaging
magick -background none -density 384 "src/Nugsdotnet.App/Resources/AppIcon/appicon.svg" -define icon:auto-resize=256,128,64,48,32,16 "packaging/nugsdotnet.ico"
```

- [ ] **Step 3: Verify the ICO has all six frames and is not blank**

Run (git-bash):
```
magick identify "packaging/nugsdotnet.ico"
magick "packaging/nugsdotnet.ico[0]" "packaging/_icocheck.png"
```
Expected: `identify` lists six `ICO ... 256x256`, `128x128`, … `16x16` frames. Open `packaging/_icocheck.png` and confirm it shows the app glyph (not an empty/transparent square). Then delete the check file:
```
rm "packaging/_icocheck.png"
```

**Fallback if the extracted PNG is blank** (ImageMagick failed to rasterize the SVG): install Inkscape (`winget install --id Inkscape.Inkscape -e --accept-package-agreements --accept-source-agreements`), rasterize first, then pack:
```
inkscape "src/Nugsdotnet.App/Resources/AppIcon/appicon.svg" --export-type=png --export-filename="packaging/_icon256.png" -w 256 -h 256
magick "packaging/_icon256.png" -define icon:auto-resize=256,128,64,48,32,16 "packaging/nugsdotnet.ico"
rm "packaging/_icon256.png"
```

- [ ] **Step 4: Commit**

```
git add packaging/nugsdotnet.ico
git commit -m "build: add committed app icon for the installer"
```

---

### Task 3: Author the Inno Setup installer and verify a local per-user install

**Files:**
- Create: `packaging/installer.iss`

**Interfaces:**
- Consumes: publish folder from Task 1; `packaging/nugsdotnet.ico` from Task 2.
- Produces: `packaging/Output/nugsdotnet-<version>-x64-setup.exe` — consumed by Task 5 (CI) and referenced by Task 4's `InstallerUrl`.
- Compile interface: `ISCC.exe /DMyAppVersion=<v> /DPublishDir=<abs-or-rel path> packaging/installer.iss`.

- [ ] **Step 1: Install Inno Setup locally**

Run (pwsh):
```
winget install --id JRSoftware.InnoSetup -e --accept-package-agreements --accept-source-agreements
```
Verify (the compiler installs to Program Files (x86)):
```
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" /?
```
Expected: prints Inno Setup Compiler usage.

- [ ] **Step 2: Write the installer script**

Create `packaging/installer.iss`:
```iss
; nugsdotnet installer — per-user, no UAC. Version/publish dir are passed by the
; build (/DMyAppVersion=, /DPublishDir=). Defaults below are for local manual runs.
#ifndef MyAppVersion
  #define MyAppVersion "0.2.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\src\Nugsdotnet.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish"
#endif

#define MyAppName "nugsdotnet"
#define MyAppPublisher "Tim Vanbenschoten"
#define MyAppURL "https://github.com/tsvb/nugsdotnet"
#define MyAppExeName "Nugsdotnet.App.exe"

[Setup]
; AppId is the winget/identity anchor — never change it.
AppId={{8B3F2A14-9C7D-4E6B-A1F0-5D2E7C9B4A60}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=Output
OutputBaseFilename=nugsdotnet-{#MyAppVersion}-x64-setup
SetupIconFile=nugsdotnet.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
```

- [ ] **Step 3: Compile the installer**

Run (pwsh) from repo root:
```
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" /DMyAppVersion=0.2.0 "/DPublishDir=$PWD\src\Nugsdotnet.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish" packaging\installer.iss
```
Expected: ends with `Successful compile`; produces `packaging/Output/nugsdotnet-0.2.0-x64-setup.exe`.

- [ ] **Step 4: Install silently and verify install mechanics**

Run (pwsh):
```
Start-Process -Wait -FilePath ".\packaging\Output\nugsdotnet-0.2.0-x64-setup.exe" -ArgumentList "/VERYSILENT","/SUPPRESSMSGBOXES","/NORESTART"
Test-Path "$env:LocalAppData\Programs\nugsdotnet\Nugsdotnet.App.exe"          # -> True
Test-Path "$env:AppData\Microsoft\Windows\Start Menu\Programs\nugsdotnet.lnk"  # -> True
Get-ChildItem "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall" | ForEach-Object { $_.GetValue('DisplayName') } | Select-String nugsdotnet  # -> matches
```
Expected: first two return `True`; the registry query lists a `nugsdotnet` uninstall entry (this is what winget reads to track install state).

- [ ] **Step 5: Confirm the installed exe launches, then hand UI check to the user**

Run (pwsh):
```
$p = Start-Process -PassThru "$env:LocalAppData\Programs\nugsdotnet\Nugsdotnet.App.exe"
Start-Sleep -Seconds 6
$p.HasExited   # -> False (process stayed up = it launched without immediately crashing)
Stop-Process -Id $p.Id
```
Expected: `HasExited` is `False`. **Then ask the user to confirm the window actually renders** (automation can't see the MAUI window). If it exits immediately, the most likely cause is a missing Evergreen WebView2 runtime — note it for `docs/RELEASING.md`.

- [ ] **Step 6: Uninstall to leave a clean machine**

Run (pwsh):
```
Start-Process -Wait -FilePath "$env:LocalAppData\Programs\nugsdotnet\unins000.exe" -ArgumentList "/VERYSILENT"
Test-Path "$env:LocalAppData\Programs\nugsdotnet\Nugsdotnet.App.exe"   # -> False
```
Expected: returns `False`.

- [ ] **Step 7: Commit** (also ignore the build output dir)

Add `packaging/Output/` to `.gitignore`, then:
```
git add packaging/installer.iss .gitignore
git commit -m "feat: add Inno Setup per-user installer"
```

---

### Task 4: Author the winget manifest template and validate it

**Files:**
- Create: `packaging/winget/tsvb.nugsdotnet.yaml` (version manifest)
- Create: `packaging/winget/tsvb.nugsdotnet.installer.yaml`
- Create: `packaging/winget/tsvb.nugsdotnet.locale.en-US.yaml`

**Interfaces:**
- Consumes: identity/version constants from Global Constraints; installer filename from Task 3.
- Produces: a `winget validate`-clean manifest directory — the canonical metadata Komac reuses in Task 5. `InstallerUrl`/`InstallerSha256` are placeholders the pipeline overwrites per release.

- [ ] **Step 1: Confirm validation fails before the files exist (baseline)**

Run (pwsh):
```
winget validate --manifest packaging/winget
```
Expected: FAIL — `The directory does not exist` (or no manifest found). This confirms the next steps are what make it pass.

- [ ] **Step 2: Write the version manifest**

Create `packaging/winget/tsvb.nugsdotnet.yaml`:
```yaml
# yaml-language-server: $schema=https://aka.ms/winget-manifest.version.1.6.0.schema.json
PackageIdentifier: tsvb.nugsdotnet
PackageVersion: 0.2.0
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.6.0
```

- [ ] **Step 3: Write the installer manifest**

Create `packaging/winget/tsvb.nugsdotnet.installer.yaml`. `InstallerUrl`/`InstallerSha256` are placeholders (Komac overwrites them per release); the all-zero SHA is format-valid so `winget validate` passes:
```yaml
# yaml-language-server: $schema=https://aka.ms/winget-manifest.installer.1.6.0.schema.json
PackageIdentifier: tsvb.nugsdotnet
PackageVersion: 0.2.0
InstallerType: inno
Scope: user
InstallModes:
  - interactive
  - silent
  - silentWithProgress
UpgradeBehavior: install
Installers:
  - Architecture: x64
    InstallerUrl: https://github.com/tsvb/nugsdotnet/releases/download/v0.2.0/nugsdotnet-0.2.0-x64-setup.exe
    InstallerSha256: 0000000000000000000000000000000000000000000000000000000000000000
ManifestType: installer
ManifestVersion: 1.6.0
```

- [ ] **Step 4: Write the locale manifest**

Create `packaging/winget/tsvb.nugsdotnet.locale.en-US.yaml`:
```yaml
# yaml-language-server: $schema=https://aka.ms/winget-manifest.defaultLocale.1.6.0.schema.json
PackageIdentifier: tsvb.nugsdotnet
PackageVersion: 0.2.0
PackageLocale: en-US
Publisher: Tim Vanbenschoten
PublisherUrl: https://github.com/tsvb
PackageName: nugsdotnet
PackageUrl: https://github.com/tsvb/nugsdotnet
License: Proprietary
ShortDescription: A fast personal local web client for nugs.net.
Description: >-
  A personal local web client for nugs.net with fast search, better queue and
  playlist UX, and global keyboard shortcuts. Runs against your own nugs
  subscription; downloads, redistributes, and strips no content.
Moniker: nugsdotnet
Tags:
  - nugs
  - music
  - audio
  - blazor
  - maui
ManifestType: defaultLocale
ManifestVersion: 1.6.0
```
> NOTE: `License: Proprietary` is a placeholder. Public `winget-pkgs` submission expects a real license — see the open item in `docs/RELEASING.md`.

- [ ] **Step 5: Verify validation now passes**

Run (pwsh):
```
winget validate --manifest packaging/winget
```
Expected: `Manifest validation succeeded.` (A warning about the installer hash/URL not being reachable is acceptable — validate checks structure, not download.)

- [ ] **Step 6: Commit**

```
git add packaging/winget/
git commit -m "feat: add winget manifest template (tsvb.nugsdotnet)"
```

---

### Task 5: Add the tag-triggered GitHub Actions release pipeline

**Files:**
- Create: `.github/workflows/release.yml`

**Interfaces:**
- Consumes: `packaging/installer.iss` (Task 3), the publish command (Task 1), the package identifier (Task 4).
- Produces: on a `v*` tag — a GitHub Release with the installer + a manifests zip; and, when `WINGET_TOKEN` is set, a `winget-pkgs` PR.

- [ ] **Step 1: Write the workflow**

Create `.github/workflows/release.yml`. Note the `has_token` step: GitHub does not allow the `secrets` context inside an `if:` condition, so the secret is mapped to a step output first.
```yaml
name: release
on:
  push:
    tags: ['v*']

permissions:
  contents: write

jobs:
  release:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4

      - name: Derive version from tag
        id: ver
        shell: pwsh
        run: |
          $v = "${{ github.ref_name }}".TrimStart('v')
          "version=$v" >> $env:GITHUB_OUTPUT

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Restore MAUI workload
        run: dotnet workload restore src/Nugsdotnet.App/Nugsdotnet.App.csproj

      - name: Publish (self-contained, win-x64)
        run: >
          dotnet publish src/Nugsdotnet.App/Nugsdotnet.App.csproj -c Release
          -f net10.0-windows10.0.19041.0 -p:RuntimeIdentifier=win-x64
          -p:SelfContained=true -p:WindowsAppSDKSelfContained=true
          -p:WindowsPackageType=None
          -p:ApplicationDisplayVersion=${{ steps.ver.outputs.version }}

      - name: Install Inno Setup
        run: choco install innosetup --no-progress -y

      - name: Build installer
        shell: pwsh
        run: >
          & "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
          /DMyAppVersion=${{ steps.ver.outputs.version }}
          "/DPublishDir=${{ github.workspace }}\src\Nugsdotnet.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish"
          packaging\installer.iss

      - name: Sign installer (placeholder hook — signing deferred)
        shell: pwsh
        run: Write-Host "Signing deferred. See docs/RELEASING.md (Azure Trusted Signing) to enable."

      - name: Create GitHub Release
        uses: softprops/action-gh-release@v2
        with:
          files: packaging/Output/nugsdotnet-${{ steps.ver.outputs.version }}-x64-setup.exe
          generate_release_notes: true

      - name: Install Komac
        shell: pwsh
        run: |
          $url = "https://github.com/russellbanks/Komac/releases/latest/download/komac-x86_64-pc-windows-msvc.exe"
          Invoke-WebRequest -Uri $url -OutFile "$env:RUNNER_TEMP\komac.exe"
          "$env:RUNNER_TEMP" >> $env:GITHUB_PATH

      - name: Generate winget manifests (always)
        shell: pwsh
        run: |
          $url = "https://github.com/tsvb/nugsdotnet/releases/download/${{ github.ref_name }}/nugsdotnet-${{ steps.ver.outputs.version }}-x64-setup.exe"
          & "$env:RUNNER_TEMP\komac.exe" update tsvb.nugsdotnet `
            --version ${{ steps.ver.outputs.version }} `
            --urls $url `
            --dry-run --output "$env:RUNNER_TEMP\winget-out"
          Compress-Archive -Path "$env:RUNNER_TEMP\winget-out\*" -DestinationPath "$env:RUNNER_TEMP\nugsdotnet-${{ steps.ver.outputs.version }}-winget-manifests.zip"

      - name: Attach manifests to release
        shell: pwsh
        run: gh release upload ${{ github.ref_name }} "$env:RUNNER_TEMP\nugsdotnet-${{ steps.ver.outputs.version }}-winget-manifests.zip"
        env:
          GH_TOKEN: ${{ github.token }}

      - name: Check for winget submission token
        id: tok
        shell: pwsh
        env:
          WINGET_TOKEN: ${{ secrets.WINGET_TOKEN }}
        run: |
          if ([string]::IsNullOrEmpty($env:WINGET_TOKEN)) { "present=false" >> $env:GITHUB_OUTPUT }
          else { "present=true" >> $env:GITHUB_OUTPUT }

      - name: Submit to winget-pkgs (only when token present)
        if: steps.tok.outputs.present == 'true'
        shell: pwsh
        env:
          GITHUB_TOKEN: ${{ secrets.WINGET_TOKEN }}
        run: |
          $url = "https://github.com/tsvb/nugsdotnet/releases/download/${{ github.ref_name }}/nugsdotnet-${{ steps.ver.outputs.version }}-x64-setup.exe"
          & "$env:RUNNER_TEMP\komac.exe" update tsvb.nugsdotnet `
            --version ${{ steps.ver.outputs.version }} `
            --urls $url `
            --submit
```

- [ ] **Step 2: Verify the workflow YAML parses**

Run (git-bash — node is available via the repo's `node_modules`):
```
node -e "const fs=require('fs');const s=fs.readFileSync('.github/workflows/release.yml','utf8');const m=s.match(/\t/);if(m)throw new Error('tab char in YAML');console.log('no tabs; bytes='+s.length)"
```
Expected: prints `no tabs; bytes=...` (YAML forbids tabs; this catches the most common breakage). If `python` is available, additionally run `python -c "import yaml,sys;yaml.safe_load(open('.github/workflows/release.yml'));print('yaml ok')"`.

- [ ] **Step 3: Commit**

```
git add .github/workflows/release.yml
git commit -m "ci: add tag-triggered winget release pipeline"
```

> **End-to-end verification of this workflow requires pushing a real tag** (creates a public GitHub Release). That is an outward-facing action — do it only with explicit user approval, as a final smoke test (see Task 6 runbook). It is intentionally NOT part of this task's automated steps.

---

### Task 6: Write the release runbook (`docs/RELEASING.md`)

**Files:**
- Create: `docs/RELEASING.md`

**Interfaces:**
- Consumes: everything above. No code consumes this; it documents operation.

- [ ] **Step 1: Write the runbook**

Create `docs/RELEASING.md`:
````markdown
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
that step is skipped. **Before going public**, replace `License: Proprietary` in
`packaging/winget/tsvb.nugsdotnet.locale.en-US.yaml` with a real license (add a
`LICENSE` file to the repo) — `winget-pkgs` moderators expect one, and note this
publishes an unofficial third-party nugs.net client.

## Enable code signing (optional, removes SmartScreen warning)

Unsigned installers trigger a SmartScreen "unknown publisher" prompt. To fix:

1. Set up **Azure Trusted Signing** (~$10/mo, no hardware token):
   <https://learn.microsoft.com/azure/trusted-signing/>.
2. Replace the "Sign installer (placeholder hook)" step in
   `.github/workflows/release.yml` with the `azure/trusted-signing-action`,
   signing `packaging/Output/nugsdotnet-<version>-x64-setup.exe` **after** the
   build step and **before** "Create GitHub Release".
````

- [ ] **Step 2: Verify internal links resolve**

Run (git-bash):
```
ls .github/workflows/release.yml packaging/winget/tsvb.nugsdotnet.locale.en-US.yaml
```
Expected: both paths exist (the doc's relative links point at real files).

- [ ] **Step 3: Commit**

```
git add docs/RELEASING.md
git commit -m "docs: add release runbook for winget distribution"
```

---

## Post-plan smoke test (user-gated, outward-facing)

After Tasks 1–6, the local artifact + manifest are fully verified. The only thing
unverifiable without side effects is the live CI run. With user approval, cut a
throwaway release to exercise the pipeline:

```
git push origin feat/winget-distribution
git tag v0.2.0-rc1 && git push origin v0.2.0-rc1
```
Watch the run with `gh run watch`. Confirm the release has both the `…setup.exe`
and the `…winget-manifests.zip`. Delete the pre-release tag/release afterward if
it was only a test.

## Self-Review

- **Spec coverage:** build profile (T1), Inno installer (T3), icon (T2), manifest (T4), pipeline incl. Komac always-generate + token-gated PR + signing hook (T5), versioning (T1/T5), RELEASING.md incl. go-public + signing + WebView2 + license (T6). All spec sections map to a task.
- **Placeholders:** the all-zero SHA and `License: Proprietary` are intentional, documented placeholders (Komac overwrites the SHA; license is a flagged open item), not plan gaps.
- **Type/identity consistency:** `tsvb.nugsdotnet`, AppId `{8B3F2A14-…}`, exe `Nugsdotnet.App.exe`, publish path, and installer filename `nugsdotnet-<v>-x64-setup.exe` are identical across T3/T4/T5/T6.
