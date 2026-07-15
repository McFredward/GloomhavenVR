#!/usr/bin/env bash
# Build GloomhavenVR (both projects) — Linux/macOS/WSL.
#
# Usage: scripts/build.sh [Debug|Release]   (default: Release)
#
# Requirements:
#   - .NET SDK 8+ (looked up on PATH, falls back to ~/.dotnet)
#   - GameManaged pointing at the game's Managed folder — set via
#     Directory.Build.props.user (see README, "Developer setup").
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFIG="${1:-Release}"

# Prefer a user-local SDK install if dotnet is not already on PATH.
if ! command -v dotnet >/dev/null 2>&1 && [[ -x "$HOME/.dotnet/dotnet" ]]; then
    export DOTNET_ROOT="$HOME/.dotnet"
    export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"
fi

if ! command -v dotnet >/dev/null 2>&1; then
    echo "error: dotnet SDK not found (PATH or ~/.dotnet). Install .NET SDK 8+." >&2
    exit 1
fi

dotnet build "$ROOT/GloomhavenVR.sln" -c "$CONFIG"

echo
echo "Artifacts:"
echo "  plugin   : src/GloomhavenVR/bin/$CONFIG/net472/GloomhavenVR.dll"
echo "  preloader: src/GloomhavenVR.Preload/bin/$CONFIG/net472/GloomhavenVR.Preload.dll"
echo
echo "Install into a game dir: plugin -> BepInEx/plugins/GloomhavenVR/, preloader -> BepInEx/patchers/"
echo "(Windows: scripts/deploy.ps1 -GamePath 'C:\\...\\Gloomhaven' does the copy.)"
