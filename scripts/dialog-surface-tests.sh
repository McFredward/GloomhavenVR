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
owner_source="$repo_root/src/GloomhavenVR/WorldUI/Composites/ConfirmationOnOwner.cs"
origin_source="$repo_root/src/GloomhavenVR/WorldUI/Composites/ConfirmationOnOwner.Origins.cs"
layer_source="$repo_root/src/GloomhavenVR/WorldUI/Composites/MovedSubtreeLayers.cs"
common=(--configuration Release --property:OwnerSource="$owner_source" --property:OriginSource="$origin_source" --property:LayerSource="$layer_source")
python3 - "$repo_root" <<'PY'
from pathlib import Path
import sys
source=(Path(sys.argv[1])/'src/GloomhavenVR/WorldUI/Modal/ModalFallback.10.CatchAll.cs').read_text()
start=source.index('private static void TickCatchAll(')
assert source.index('AddPollWindow(Surfaces.DialogSurface.FallbackWindow);',start) < source.index('if (UnknownShown.Count == 0)',start), 'Failed dedicated confirmation must reach modal fallback even without unknown windows'
PY
dotnet run --project "$project" "${common[@]}" --property:DialogSource="$source"
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
    if dotnet run --project "$project" "${common[@]}" --property:DialogSource="$test_dir/mutant.cs.txt" > "$test_dir/mutant.log" 2>&1; then
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

# Run the actual production owner/origin kernels; a stubbed True/False seam cannot
# prove reversible ownership or safe reuse of the pooled native confirmation.
for mutation in double-capture stale-producer lost-home collapsed-frame; do
    python3 - "$owner_source" "$origin_source" "$test_dir" "$mutation" <<'PYMUT'
from pathlib import Path
import sys
owner=Path(sys.argv[1]).read_text(); origins=Path(sys.argv[2]).read_text()
out=Path(sys.argv[3]); mutation=sys.argv[4]
if mutation=='double-capture':
    a='if (window == null || !ModalFallback.ReleaseForComposite(window)) return false;'
    assert a in owner; owner=owner.replace(a,'if (window == null) return false;')
elif mutation=='stale-producer':
    a='Requests.Remove(box);'
    assert a in origins; origins=origins.replace(a,'if (CurrentScope != null) Requests.Remove(box);')
elif mutation=='lost-home':
    a='rect.SetParent(seat.Home != null ? seat.Home : null, worldPositionStays: false);'
    assert a in owner; owner=owner.replace(a,'// regression: original native parent was not restored')
else:
    a='rect.sizeDelta = seat.Frame;'
    assert a in owner; owner=owner.replace(a,'rect.sizeDelta = Vector2.zero;')
(out/'owner.fixture').write_text(owner); (out/'origins.fixture').write_text(origins)
PYMUT
    if dotnet run --project "$project" --configuration Release --property:DialogSource="$source" \
        --property:OwnerSource="$test_dir/owner.fixture" --property:OriginSource="$test_dir/origins.fixture" \
        --property:LayerSource="$layer_source" > "$test_dir/mutant.log" 2>&1; then
        cat "$test_dir/mutant.log"
        echo "FAIL: $mutation escaped confirmation ownership test." >&2
        exit 1
    fi
    case "$mutation" in
        double-capture) expected='late semantic adoption must release the old standalone capture before recording native home' ;;
        stale-producer) expected='unowned reuse of a pooled dialog must retain standalone fallback instead of stale level-up ownership' ;;
        lost-home) expected='native hide must restore the original confirmation parent without a lingering claim' ;;
        collapsed-frame) expected='centering must preserve measured native frame, not collapse stretched anchors' ;;
    esac
    if ! rg -qF "$expected" "$test_dir/mutant.log"; then cat "$test_dir/mutant.log"; exit 1; fi
    echo "Confirmation owner negative control: $mutation rejected."
done
