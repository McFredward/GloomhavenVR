#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
source="${1:-$repo_root/src/GloomhavenVR/WorldUI/Surfaces/DialogSurface.cs}"
test_dir="$(mktemp -d)"
trap 'rm -rf "$test_dir"' EXIT
cp "$repo_root/tests/GloomhavenVR.DialogSurfaceTests/"*.cs "$test_dir/"
cp "$repo_root/tests/GloomhavenVR.DialogSurfaceTests/"*.csproj "$test_dir/"
project="$test_dir/GloomhavenVR.DialogSurfaceTests.csproj"
dotnet run --project "$project" --configuration Release --property:DialogSource="$source"
for mutation in scenario-only edge-only no-fallback no-release native-hide no-retry; do
    python3 - "$source" "$test_dir/mutant.cs.txt" "$mutation" <<'PY'
from pathlib import Path
import sys
s=Path(sys.argv[1]).read_text()
pairs={
 'scenario-only': ('bool inRoom = VRModeStateMachine.TableInFrontOfPlayer', 'bool inRoom = Choreographer.s_Choreographer != null && VRModeStateMachine.TableInFrontOfPlayer'),
 'edge-only': ('if (_pendingShow || _panel == null)', 'if (_pendingShow)'),
 'no-fallback': ('FallbackWindow = box.GetComponent<UIWindow>();', 'FallbackWindow = null;'),
 'no-release': ('CanvasConversion.Release(_panel);', '{}'),
 'native-hide': ('private void ReleasePanel()\n    {', 'private void ReleasePanel()\n    {\n        if (_attached != null) _attached.CurrentBox.IsOpen = false;'),
 'no-retry': ('_conversionFailure = null;', '{}'),
}
a,b=pairs[sys.argv[3]]; assert a in s
Path(sys.argv[2]).write_text(s.replace(a,b))
PY
    if dotnet run --project "$project" --configuration Release --property:DialogSource="$test_dir/mutant.cs.txt" > "$test_dir/mutant.log" 2>&1; then
        cat "$test_dir/mutant.log"
        echo "FAIL: $mutation escaped dialog surface test." >&2
        exit 1
    fi
    case "$mutation" in
        scenario-only|edge-only) expected='already-open map confirmation converts without scenario choreographer' ;;
        no-fallback) expected='failed dedicated conversion publishes exact original fallback window' ;;
        no-release) expected='desktop rescue releases only presentation' ;;
        native-hide) expected='desktop rescue releases only presentation' ;;
        no-retry) expected='failed opening transfers ownership once without per-frame retry or log flood' ;;
    esac
    if ! rg -qF "$expected" "$test_dir/mutant.log"; then cat "$test_dir/mutant.log"; exit 1; fi
    echo "Dialog surface negative control: $mutation rejected."
done
