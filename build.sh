#!/usr/bin/env bash
# Build and package Multiplayer Leaderboard.
#
#   ./build.sh "/path/to/Beat Saber"      # explicit game installation
#   BEATSABER_DIR="/path" ./build.sh      # same, through the environment
#   ./build.sh                            # only works when this folder is inside the game
#
# The game must have BSIPA and BSML installed; those assemblies are referenced, not shipped.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GAME="${1:-${BEATSABER_DIR:-}}"

if [ -z "$GAME" ]; then
  if [ -f "$HERE/../Beat Saber.exe" ]; then
    GAME="$(cd "$HERE/.." && pwd)"
  else
    echo "usage: $0 \"/path/to/Beat Saber\"" >&2
    echo "   or: BEATSABER_DIR=\"/path/to/Beat Saber\" $0" >&2
    exit 1
  fi
fi

if [ ! -f "$GAME/Beat Saber.exe" ]; then
  echo "error: Beat Saber.exe was not found in $GAME" >&2
  exit 1
fi

DOTNET="${DOTNET:-}"
for candidate in \
  "$HERE/.tools/dotnet/dotnet" "$HERE/.tools/dotnet/dotnet.exe" \
  "$HERE/../.tools/dotnet/dotnet" "$HERE/../.tools/dotnet/dotnet.exe"; do
  if [ -z "$DOTNET" ] && [ -x "$candidate" ]; then
    DOTNET="$candidate"
    break
  fi
done

if [ -z "$DOTNET" ] && command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks >/dev/null 2>&1; then
  DOTNET="dotnet"
fi

if [ -z "$DOTNET" ]; then
  echo "error: .NET SDK not found. Install the .NET SDK (with the .NET Framework 4.7.2" >&2
  echo "targeting pack on Windows) or place a private SDK in .tools/dotnet." >&2
  exit 1
fi

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

echo "== game:   $GAME"
echo "== dotnet: $DOTNET"
echo

echo "== build plugin"
"$DOTNET" build "$HERE/src/MultiplayerLeaderboard/MultiplayerLeaderboard.csproj" \
  -c Release -p:BeatSaberDir="$GAME" -v m

echo
echo "== package"
mkdir -p "$HERE/dist"
cp "$HERE/src/MultiplayerLeaderboard/bin/Release/MultiplayerLeaderboard.dll" \
   "$HERE/dist/MultiplayerLeaderboard.dll"
cp "$HERE/INSTALL.txt" "$HERE/dist/INSTALL.txt"
cp "$HERE/UNINSTALL.txt" "$HERE/dist/UNINSTALL.txt"
cp "$HERE/README.md" "$HERE/dist/README.md"
cp "$HERE/docs/TESTING.md" "$HERE/dist/TESTING.md"
cp "$HERE/docs/UI-ARCHITECTURE.md" "$HERE/dist/UI-ARCHITECTURE.md"

# The manifest is embedded in the DLL. A loose copy next to it makes BSIPA log a duplicate and
# refuse to run the plugin, so never ship one.
rm -f "$HERE/dist/manifest.json" "$HERE/dist/MultiplayerLeaderboard.manifest"

echo
echo "== standalone check"
# The mod may use BSML for the standard Mods menu, but it must not depend on Kick Mod, Ping Mod
# or another gameplay plugin.
for forbidden in KickMod PingMod SiraUtil BS_Utils; do
  if grep -a -q "$forbidden" "$HERE/dist/MultiplayerLeaderboard.dll"; then
    echo "error: MultiplayerLeaderboard.dll mentions $forbidden; this mod must stay independent" >&2
    exit 1
  fi
done
echo "no reference to another plugin in MultiplayerLeaderboard.dll"

if grep -a -q "C:\\\\\\\\Users\\\\\\\\" "$HERE/dist/MultiplayerLeaderboard.dll"; then
  echo "error: the DLL contains an absolute Windows user path" >&2
  exit 1
fi
echo "no absolute Windows user path in MultiplayerLeaderboard.dll"

echo
echo "dist/MultiplayerLeaderboard.dll ready:"
if command -v sha256sum >/dev/null 2>&1; then
  sha256sum "$HERE/dist/MultiplayerLeaderboard.dll"
else
  shasum -a 256 "$HERE/dist/MultiplayerLeaderboard.dll"
fi
