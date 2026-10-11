#!/usr/bin/env bash
# Bind the complete native close method and explicit-close scope; no Unity session is needed.
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
python3 "$repo_root/tests/GloomhavenVR.TownWindowCloseTests/run.py" "$@"
