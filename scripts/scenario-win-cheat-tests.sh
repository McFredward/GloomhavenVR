#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
test_dir="$(mktemp -d)"
trap 'rm -rf "$test_dir"' EXIT
cp "$repo_root/tests/GloomhavenVR.ScenarioWinCheatTests/"*.cs "$test_dir/"
cp "$repo_root/tests/GloomhavenVR.ScenarioWinCheatTests/"*.csproj "$test_dir/"
source="${SCENARIO_WIN_SOURCE:-$repo_root/src/GloomhavenVR/WorldUI/Options/ScenarioWinCheat.cs}"
project="$test_dir/GloomhavenVR.ScenarioWinCheatTests.csproj"
dotnet run --project "$project" --configuration Release --property:ScenarioWinSource="$source"
for mutation in online duplicate; do
    python3 - "$source" "$test_dir/mutant.cs.txt" "$mutation" <<'PY'
from pathlib import Path
import sys
source = Path(sys.argv[1]).read_text()
old, new = {
    'online': ('if (FFSNetwork.IsOnline) return "cheat_win_online";', ''),
    'duplicate': ('ReferenceEquals(_requested, state) || ', ''),
}[sys.argv[3]]
assert source.count(old) == 1, 'Production mutation target changed'
Path(sys.argv[2]).write_text(source.replace(old, new))
PY
    if dotnet run --project "$project" --configuration Release --property:ScenarioWinSource="$test_dir/mutant.cs.txt" > "$test_dir/mutant.log" 2>&1; then
        cat "$test_dir/mutant.log"
        echo "FAIL: $mutation escaped scenario win cheat tests." >&2
        exit 1
    fi
    case "$mutation" in
        online) expected='refusal reason: cheat_win_online' ;;
        duplicate) expected='requested scenario remains unavailable' ;;
    esac
    if ! rg -qF "$expected" "$test_dir/mutant.log"; then cat "$test_dir/mutant.log"; exit 1; fi
    echo "Scenario win cheat negative control: $mutation rejected."
done
