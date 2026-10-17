#!/usr/bin/env bash
set -euo pipefail
repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
exec python3 -B "$repo_root/tests/GloomhavenVR.BurnLayoutTests/run.py" "$repo_root"
