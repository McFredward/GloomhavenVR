#!/usr/bin/env bash
set -euo pipefail
repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
work_dir=$(mktemp -d)
trap 'rm -rf "$work_dir"' EXIT
cp "$repo_root/tests/GloomhavenVR.TutorialControllerTests/"*.cs "$repo_root/tests/GloomhavenVR.TutorialControllerTests/"*.csproj "$work_dir/"
python3 "$repo_root/tests/GloomhavenVR.TutorialControllerTests/extract.py" "$repo_root" "$work_dir"
dotnet run --project "$work_dir/GloomhavenVR.TutorialControllerTests.csproj" --configuration Release
for mutation in missing-right always-controller recovery-override wrong-highlight missing-layers missing-marker-layer self-hide cached-pair stale-hand lost-highlight model-loss instant-hand; do
    cp "$work_dir/ControlsTutorial.cs" "$work_dir/tutorial.original"
    cp "$work_dir/ControllerVisual.cs" "$work_dir/visual.original"
    cp "$work_dir/ControlsLesson.cs" "$work_dir/lesson.original"
    python3 - "$work_dir" "$mutation" <<'PY'
from pathlib import Path
import sys
root=Path(sys.argv[1]); mutation=sys.argv[2]
name, old, new = {
    'model-loss': ('ControllerVisual', 'private void RestoreHandAfterModelLoss()\n    {\n        ShowHand();', 'private void RestoreHandAfterModelLoss()\n    {'),
    'instant-hand': ('ControllerVisual', 'BeginSwap(0f);', 'Hide();'),
    'cached-pair': ('ControlsTutorial', 'if (!changed && (!visible ||', 'if (!changed && (bool.Parse("true") ||'),
    'stale-hand': ('ControllerVisual', 'IsBoundTo(VRHand? hand) => ReferenceEquals(_hand, hand)', 'IsBoundTo(VRHand? hand) => true'),
    'lost-highlight': ('ControlsTutorial', '_right.Highlight(key);', ''),
    'missing-right': ('ControlsTutorial', '_right.Show();', ''),
    'always-controller': ('ControlsLesson', 'ShowsController = showsController;', 'ShowsController = true;'),
    'recovery-override': ('ControlsTutorial', 'SetControllersVisible(ControlsLesson.Steps[_index].ShowsController, \"lesson recovery\");', 'SetControllersVisible(true, \"lesson recovery\");'),
    'wrong-highlight': ('ControlsTutorial', '_right?.Highlight((verdict.Hands & ControlsLesson.LessonHands.Right) != 0 ? key : null);', '_right?.Highlight(key);'),
    'missing-layers': ('ControllerVisual', 'VRLayers.Apply(_model);', ''),
    'missing-marker-layer': ('ControllerVisual', 'VRLayers.Apply(sphere);', ''),
    'self-hide': ('ControllerVisual', 'Transform? handRoot = _hand.Rig?.Root;', 'Transform? handRoot = _hand.transform;'),
}[mutation]
p=root/(name+'.cs');s=p.read_text();assert s.count(old)==1;s=s.replace(old,new)
p.write_text(s)
PY
    if dotnet run --project "$work_dir/GloomhavenVR.TutorialControllerTests.csproj" --configuration Release > "$work_dir/negative.log" 2>&1; then
        echo "FAIL: tutorial controller regression $mutation escaped coverage" >&2; exit 1
    fi
    case "$mutation" in
        model-loss) expected='A lost controller must immediately restore the hand on every recovery path';;
        instant-hand) expected='Returning to hands must animate instead of popping';;
        missing-right|cached-pair|stale-hand) expected='Controller steps must keep both models visible';;
        always-controller|recovery-override) expected='Hand tasks must restore both ordinary hands without controller recovery overriding them';;
        wrong-highlight|lost-highlight) expected='Only the configured hand and requested key may glow';;
        missing-layers) expected='Every controller descendant must be excluded from scenery and camera culling';;
        missing-marker-layer) expected='Anchor-only key markers must use the mod layer';;
        self-hide) expected='Neither side may lose both its hand and controller during a transition or recovery';;
    esac
    if ! rg -Fq "Unhandled exception. System.Exception: $expected" "$work_dir/negative.log"; then cat "$work_dir/negative.log";exit 1;fi
    echo "Tutorial controller negative control: $mutation rejected."
    mv "$work_dir/tutorial.original" "$work_dir/ControlsTutorial.cs"
    mv "$work_dir/visual.original" "$work_dir/ControllerVisual.cs"
    mv "$work_dir/lesson.original" "$work_dir/ControlsLesson.cs"
done
