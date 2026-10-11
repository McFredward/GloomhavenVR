#!/usr/bin/env bash
# Validate the production update archive and restart script, including Frame VR opt-in.
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$repo_root/tests/GloomhavenVR.SelfUpdateArchiveTests/GloomhavenVR.SelfUpdateArchiveTests.csproj"
dotnet run --project "$project" --configuration Release

scratch="$(mktemp -d)"
trap 'rm -rf "$scratch"' EXIT
python3 - "$repo_root" "$scratch/ApplyScript.cs" <<'PY'
from pathlib import Path
import sys
source = (Path(sys.argv[1]) / 'src/GloomhavenVR/Core/SelfUpdate/SelfUpdateApplyScript.cs').read_text()
needle = 'frameInstallation ? null : steamGameId'
assert source.count(needle) == 1, 'Frame restart causal mutation seam changed'
Path(sys.argv[2]).write_text(source.replace(needle, 'steamGameId'))
PY
if dotnet run --project "$project" --configuration Release \
    --property:ApplyScriptSource="$scratch/ApplyScript.cs" > "$scratch/control.log" 2>&1; then
    echo "FAIL: original flat Steam restart escaped the Frame update regression test." >&2
    exit 1
fi
if ! rg -qF 'System.InvalidOperationException: Frame restart retains the executable, cwd, opt-in' "$scratch/control.log"; then
    cat "$scratch/control.log"
    echo "FAIL: Frame restart control did not reach the retained-argument defect." >&2
    exit 1
fi
echo "Self-update archive causal control rejected: original flat Steam restart."
