#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
dotnet run --project "$repo_root/tests/GloomhavenVR.TownNativeVeilTests" --configuration Release
