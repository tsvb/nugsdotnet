#!/usr/bin/env bash
# Cloud Agent install phase for nugsdotnet.
#
# Prepares the cross-platform slice of the repo that builds and tests on Linux:
# the platform-agnostic Nugsdotnet.Native.Core library and its xUnit suite
# (Nugsdotnet.Native.Tests). The WinUI head (Nugsdotnet.Native) is Windows-only
# — its XAML compiler and the Windows App SDK do not build on Linux — so it is
# intentionally left out of this phase; CI compiles it on windows-latest.
#
# Idempotent: safe to run repeatedly. The .NET SDK check self-heals if the base
# image/snapshot does not already provide it, but normally the SDK is present
# and this step is a no-op restore/build against warm caches.
set -euo pipefail

DOTNET_CHANNEL="10.0"
DOTNET_ROOT_DIR="/usr/share/dotnet"

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "[install] .NET SDK not found on PATH — installing channel ${DOTNET_CHANNEL}..."
  tmp_script="$(mktemp)"
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$tmp_script"
  chmod +x "$tmp_script"
  sudo "$tmp_script" --channel "$DOTNET_CHANNEL" --install-dir "$DOTNET_ROOT_DIR" --no-path
  sudo ln -sf "$DOTNET_ROOT_DIR/dotnet" /usr/local/bin/dotnet
  rm -f "$tmp_script"
fi

echo "[install] Using $(dotnet --version) from $(command -v dotnet)"

# Restore + build only the cross-platform projects (Tests pulls in Core).
TEST_PROJECT="native/Nugsdotnet.Native.Tests/Nugsdotnet.Native.Tests.csproj"

echo "[install] Restoring and building ${TEST_PROJECT}..."
dotnet build "$TEST_PROJECT" -c Release --nologo

echo "[install] Done. Run the suite with:"
echo "  dotnet test $TEST_PROJECT -c Release --nologo"
