#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$repo_root/tests/GloomhavenVR.MrBackingTests/GloomhavenVR.MrBackingTests.csproj"
layout="$repo_root/src/GloomhavenVR/WorldUI/MrBackingLayout.cs"
mutation_dir="$(mktemp -d)"
trap 'rm -rf "$mutation_dir"' EXIT
cp "$repo_root/tests/GloomhavenVR.MrBackingTests/"*.cs "$mutation_dir/"
cp "$project" "$mutation_dir/"
python3 - "$repo_root" "$mutation_dir" <<'PY'
from pathlib import Path
import re,sys
repo,out=map(Path,sys.argv[1:])
modal=(repo/'src/GloomhavenVR/WorldUI/Grab/GrabbableModal.cs').read_text()
start=modal.index('    internal static bool TryGetMrBackingRect(')
opening=modal.index('{',start);end=opening+1;depth=1
while depth:
    depth+=(modal[end]=='{')-(modal[end]=='}');end+=1
accessor='using UnityEngine; namespace GloomhavenVR.WorldUI; internal partial class GrabbableModal {\n'+modal[start:end]+'\n}'
(out/'accessor.fixture').write_text(accessor)
layout=(repo/'src/GloomhavenVR/WorldUI/MrBackingLayout.cs').read_text()
mutations={
 'host':('Rect content = ink;','Rect content = frame;'),
 'margin':('const float margin = 8f;','const float margin = 80f;'),
 'plate':('Mathf.Min(Mathf.Min(ink.yMin, frame.yMin), plateBottom)','Mathf.Min(ink.yMin, frame.yMin)'),
 'confirm':('_candidateSamples >= 2\n                    && (agrees || now - _candidateStarted >= Mathf.Max(duration, 0.05f))','true'),
 'sample':('if (sample != _sample)','if (true)'),
 'snap':('float ease = 1f - inverse * inverse * inverse;','float ease = 1f;'),
 'hidden':('if (!visible || !Usable(bounds))','if (!Usable(bounds))'),
 'ready':('fitApplied && Usable(frame)','Usable(frame)'),
 'starve':('(agrees || now - _candidateStarted >= Mathf.Max(duration, 0.05f))','agrees'),
}
for name,(needle,replacement) in mutations.items():
    assert layout.count(needle)==1,name
    (out/(name+'.fixture')).write_text(layout.replace(needle,replacement))
needle='&& !ModalFallback.AppearStillOwed(panel, out _)'
assert accessor.count(needle)==1
(out/'owed.fixture').write_text(accessor.replace(needle,''))
# Compile the real accessor and layout; bindings cover their integration with the production
# owner sample and both rendering loops. Strip comments so documentation cannot satisfy a gate.
def code(s):return re.sub(r'/\*.*?\*/|//[^\n]*','',s,flags=re.S)
mr=code((repo/'src/GloomhavenVR/WorldUI/MrBacking.cs').read_text())
mirror=code((repo/'src/GloomhavenVR/Net/Remote/RemoteWidgetMirror.cs').read_text())
m=code(modal)
def bindings(mr,m,mirror):
    assert m.index('_mrInkSampleFrame = now;')>m.index('bool measured = PanelInkBounds.TryMeasure(')
    assert m.index('_mrInkRect = measured')<m.index('Rect grown = ink.Rect;')
    assert 'MrBackingLayout.WindowRect(hostRect, ink.Rect, ink.Plates > 0, ink.PlateBottom)' in m
    assert 'visible &= ownerVisible;' in mr
    assert 'entry.BoundsVisible = PanelInkBounds.TryMeasure(panel' in mr
    assert 'visible &= entry.BoundsVisible;' in mr
    assert 'fitted = entry.Bounds;' in mr
    assert 'entry.NextSampleFrame = Time.frameCount + MrBackingLayout.SampleStrideFrames;' in mr
    assert 'sampleFrame = entry.SampleFrame;' in mr
    assert 'entry.Layout.Present(fitted, visible, sampleFrame, Time.unscaledTime,' in mr
    assert 'Fit(entry.Plate, host, shown.size, shown.center);' in mr
    assert 'Fit(e.Plate, anchor, shown.size, shown.center);' in mr
    assert 'sampled.BackingSampleFrame' in mr and 'MrBackingLayout.DurationSeconds' in mr and 'GrabBarTween.DurationSeconds' not in mr
    assert 'Panels[i].Layout.Reset();' in mr and 'e.Layout.Reset();' in mr
    assert 'if (!WorldUI.MrBacking.WantOpaque' in mirror
    assert '_mrNextSampleFrame = Time.frameCount + WorldUI.MrBackingLayout.SampleStrideFrames;' in mirror
    assert 'out WorldUI.PanelInkBounds.Ink ink, frameOverride: _mrFrame, excludedRoots: _mrExcluded)' in mirror
    assert 'WorldUI.MrBackingLayout.WindowRect(_mrFrame, ink.Rect, ink.Plates > 0, ink.PlateBottom)' in mirror
    assert 'WorldUI.TransientFamilies.Self(srcNodes[i])' in mirror
    assert 'WorldUI.TransientFamilies.IsDeclaredEffectQuad(srcNodes[i], _source)' in mirror
    assert 'BackingCenter => _mrBounds.center;' in mirror
    assert 'WorldUI.MrBackingLayout.ReadyForSample(_fitApplied, _mrFrame)' in mirror
    assert '_mrFrame = default;' in mirror and '_mrBounds = default;' in mirror
    assert '_mrInkPanel.Target = null!;' in mirror and '_mrSampleFrame = -1;' in mirror
bindings(mr,m,mirror)
for needle,replacement in [('visible &= ownerVisible;','visible = true;'),('fitted = entry.Bounds;','fitted = r;'),('Fit(entry.Plate, host, shown.size, shown.center);','Fit(entry.Plate, host, fitted.size, fitted.center);')]:
    try:bindings(mr.replace(needle,replacement),m,mirror)
    except AssertionError:pass
    else:raise AssertionError('MR binding mutation escaped: '+needle)
print('MR backing integration bindings: 25 assertions and three negative controls passed.')
PY
dotnet run --project "$project" --configuration Release --property:AccessorSource="$mutation_dir/accessor.fixture"
for mutation in host margin plate confirm sample snap hidden starve ready owed; do
    layout_source="$mutation_dir/$mutation.fixture"
    accessor_source="$mutation_dir/accessor.fixture"
    if [[ "$mutation" == owed ]]; then layout_source="$layout"; accessor_source="$mutation_dir/owed.fixture"; fi
    if dotnet run --project "$mutation_dir/GloomhavenVR.MrBackingTests.csproj" --configuration Release \
        --property:LayoutSource="$layout_source" --property:AccessorSource="$accessor_source" > "$mutation_dir/$mutation.log" 2>&1; then
        cat "$mutation_dir/$mutation.log"
        echo "FAIL: $mutation mutation escaped MR backing tests." >&2
        exit 1
    fi
    case "$mutation" in
        host|margin) expected='transparent host must not inflate';;
        plate) expected='ultrawide artwork below frame';;
        confirm) expected='first sample must not reveal';;
        sample) expected='same sample is not independent';;
        snap) expected='first plate grows rather than popping';;
        hidden) expected='invisible content cannot leave';;
        starve) expected='continuously changing native layout cannot starve';;
        ready) expected='unfitted clone cannot invent';;
        owed) expected='never-drawn map window stays withheld';;
    esac
    if ! rg -q "$expected" "$mutation_dir/$mutation.log"; then cat "$mutation_dir/$mutation.log"; exit 1; fi
done
echo 'MR backing negative controls: ten production mutations rejected.'
