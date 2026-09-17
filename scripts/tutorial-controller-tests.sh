#!/usr/bin/env bash
set -euo pipefail
repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
work_dir=$(mktemp -d)
trap 'rm -rf "$work_dir"' EXIT
cp "$repo_root/tests/GloomhavenVR.TutorialControllerTests/"*.cs "$repo_root/tests/GloomhavenVR.TutorialControllerTests/"*.csproj "$work_dir/"
python3 "$repo_root/tests/GloomhavenVR.TutorialControllerTests/extract.py" "$repo_root" "$work_dir"
dotnet run --project "$work_dir/GloomhavenVR.TutorialControllerTests.csproj" --configuration Release
for mutation in missing-right hand-only-step wrong-highlight missing-layers missing-marker-layer self-hide cached-pair stale-hand lost-highlight; do
    cp "$work_dir/ControlsTutorial.cs" "$work_dir/tutorial.original"
    cp "$work_dir/ControllerVisual.cs" "$work_dir/visual.original"
    python3 - "$work_dir" "$mutation" <<'PY'
from pathlib import Path
import sys
root=Path(sys.argv[1]); mutation=sys.argv[2]
name, old, new = {
    'cached-pair': ('ControlsTutorial', 'if (!changed && (!visible ||', 'if (!changed && (bool.Parse("true") ||'),
    'stale-hand': ('ControllerVisual', 'IsBoundTo(VRHand? hand) => ReferenceEquals(_hand, hand)', 'IsBoundTo(VRHand? hand) => true'),
    'lost-highlight': ('ControlsTutorial', '_right.Highlight(key);', ''),
    'missing-right': ('ControlsTutorial', '_right.Show();', ''),
    'hand-only-step': ('ControlsTutorial', 'SetControllersVisible(true, step.Id);', 'SetControllersVisible(step.Action != ControlAction.CardTake, step.Id);'),
    'wrong-highlight': ('ControlsTutorial', '_right?.Highlight((verdict.Hands & ControlsLesson.LessonHands.Right) != 0 ? key : null);', '_right?.Highlight(key);'),
    'missing-layers': ('ControllerVisual', 'VRLayers.Apply(_model);', ''),
    'missing-marker-layer': ('ControllerVisual', 'VRLayers.Apply(sphere);', ''),
    'self-hide': ('ControllerVisual', 'Transform? handRoot = _hand.Rig?.Root;', 'Transform? handRoot = _hand.transform;'),
}[mutation]
p=root/(name+'.cs');s=p.read_text();assert s.count(old)==1;s=s.replace(old,new)
if mutation == 'hand-only-step':
    # Restore the old per-step visibility policy and old lack of running recovery together.
    s=s.replace('SetControllersVisible(true, "lesson recovery");', '')
p.write_text(s)
PY
    if dotnet run --project "$work_dir/GloomhavenVR.TutorialControllerTests.csproj" --configuration Release > "$work_dir/negative.log" 2>&1; then
        echo "FAIL: tutorial controller regression $mutation escaped coverage" >&2; exit 1
    fi
    case "$mutation" in
        missing-right|hand-only-step|cached-pair|stale-hand) expected='Both controller models must remain visible throughout every lesson step';;
        wrong-highlight|lost-highlight) expected='Only the configured hand and requested key may glow';;
        missing-layers) expected='Every controller descendant must be excluded from scenery and camera culling';;
        missing-marker-layer) expected='Anchor-only key markers must use the mod layer';;
        self-hide) expected='Hand hiding must not hide its controller sibling';;
    esac
    if ! rg -Fq "Unhandled exception. System.Exception: $expected" "$work_dir/negative.log"; then cat "$work_dir/negative.log";exit 1;fi
    echo "Tutorial controller negative control: $mutation rejected."
    mv "$work_dir/tutorial.original" "$work_dir/ControlsTutorial.cs"
    mv "$work_dir/visual.original" "$work_dir/ControllerVisual.cs"
done
