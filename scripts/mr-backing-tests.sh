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
 'scope':('if (painted && !fitScoped)','if (painted)'),
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
scope=(repo/'src/GloomhavenVR/WorldUI/MrBackingScope.cs').read_text()
needle='|| ReferenceEquals(node, contentRoot) || node.IsChildOf(contentRoot);'
assert scope.count(needle)==1
(out/'scope-siblings.fixture').write_text(scope.replace(needle,
    '|| !ReferenceEquals(node, contentRoot) || node.IsChildOf(contentRoot);'))
needle='|| ReferenceEquals(node, contentRoot) || node.IsChildOf(contentRoot) || contentRoot.IsChildOf(node);'
assert scope.count(needle)==1
(out/'scope-ancestor.fixture').write_text(scope.replace(needle,
    '|| ReferenceEquals(node, contentRoot) || node.IsChildOf(contentRoot);'))
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
    assert 'out WorldUI.PanelInkBounds.Ink ink, frameOverride: _mrFrame, excludedRoots: _mrExcluded,' in mirror
    assert 'WorldUI.MrBackingLayout.WindowRect(_mrFrame, ink.Rect, ink.Plates > 0, ink.PlateBottom,' in mirror
    assert 'WorldUI.TransientFamilies.Self(srcNodes[i])' in mirror
    assert 'WorldUI.TransientFamilies.IsDeclaredEffectQuad(srcNodes[i], _source)' in mirror
    assert 'BackingCenter => _mrBounds.center;' in mirror
    assert 'WorldUI.MrBackingLayout.ReadyForSample(_fitApplied, _mrFrame)' in mirror
    assert '_mrFrame = default;' in mirror and '_mrBounds = default;' in mirror
    assert '_mrInkPanel.Target = null!;' in mirror and '_mrSampleFrame = -1;' in mirror
bindings(mr,m,mirror)
ink=code((repo/'src/GloomhavenVR/WorldUI/Conversion/PanelInkBounds.cs').read_text())
initiative=code((repo/'src/GloomhavenVR/Net/Remote/RemoteInitiativeTrack.cs').read_text())
elements=code((repo/'src/GloomhavenVR/Net/Remote/RemoteElementStrip.cs').read_text())
local_surfaces=code((repo/'src/GloomhavenVR/WorldUI/Surfaces/TablePanelSurfaces.cs').read_text())
assert 'Panel.FitContentRoot = InitiativeTrack.Instance.initiativeTrackHolder as RectTransform;' in local_surfaces and 'Panel.FitContentRoot = InfusionBoardUI.Instance.elementsHolder as RectTransform;' in local_surfaces
assert '!MrBackingScope.Valid(target, contentRoot)' in ink
assert '!MrBackingScope.Visit(t, contentRoot)' in ink
assert 'if (MrBackingScope.Paint(t, contentRoot)' in ink
assert 'else if (contentRoot == null && !ReferenceEquals(graphic, panel.ContentGraphic)' in ink
assert mr.count('contentRoot: panel.FitContentRoot') == 2
assert 'fitScoped: panel.FitContentRoot != null' in mr
assert '!MrBackingScope.Paint(g.transform, contentRoot)' in mr
assert '_mrCloneContentRoot = sourceRoot != null ? CloneOf(sourceRoot) : null;' in mirror
assert '_mrContentRootStamp != RebuildStamp || !ReferenceEquals(sourceRoot, _mrSourceContentRoot)' in mirror
assert '_mrSourceContentRoot = null;' in mirror and '_mrCloneContentRoot = null;' in mirror
assert 'if (contentRoot == null)' in mirror
assert 'contentRoot: contentRoot, visibleWitnesses: _mrVisibility.Witnesses) && ink.Valid;' in mirror
assert 'fitScoped: contentRoot != null' in mirror
assert 'out _, _mrExcluded, contentRoot);' in mirror
assert 'backingContentRoot: source => source.GetComponent<InitiativeTrack>()?.initiativeTrackHolder' in initiative
assert 'backingContentRoot: source => source.GetComponent<InfusionBoardUI>()?.elementsHolder' in elements
for needle,replacement in [('visible &= ownerVisible;','visible = true;'),('fitted = entry.Bounds;','fitted = r;'),('Fit(entry.Plate, host, shown.size, shown.center);','Fit(entry.Plate, host, fitted.size, fitted.center);')]:
    try:bindings(mr.replace(needle,replacement),m,mirror)
    except AssertionError:pass
    else:raise AssertionError('MR binding mutation escaped: '+needle)
assert re.search(r'_mrBoundsVisible\s*&& _mrVisibility.VisibleNow', mirror)
assert 'float WorldUI.MrBacking.IFadedBacking.BackingAlpha => _mrVisibility.AlphaNow;' in mirror
assert '_mrVisibility.Reset();' in mirror
assert 'visibleWitnesses: holdMrPicture ? null : _mrVisibility.Witnesses' in m
assert 'bool holdMrPicture = _mrInkValid && WindowMaterialise.IsAnimating(_panel);' in m
assert 'if (!holdMrPicture)' in m
assert 'internal static float GetMrBackingAlpha(ConvertedPanel panel)' in m
print('MR backing integration bindings: 48 assertions and three negative controls passed.')
PY
dotnet run --project "$project" --configuration Release --property:AccessorSource="$mutation_dir/accessor.fixture"
for mutation in host margin plate confirm sample snap hidden starve ready owed scope scope-siblings scope-ancestor; do
    layout_source="$mutation_dir/$mutation.fixture"
    accessor_source="$mutation_dir/accessor.fixture"
    scope_source="$repo_root/src/GloomhavenVR/WorldUI/MrBackingScope.cs"
    if [[ "$mutation" == scope-siblings || "$mutation" == scope-ancestor ]]; then layout_source="$layout"; scope_source="$mutation_dir/$mutation.fixture"; fi
    if [[ "$mutation" == owed ]]; then layout_source="$layout"; accessor_source="$mutation_dir/owed.fixture"; fi
    if dotnet run --project "$mutation_dir/GloomhavenVR.MrBackingTests.csproj" --configuration Release \
        --property:LayoutSource="$layout_source" --property:AccessorSource="$accessor_source" --property:ScopeSource="$scope_source" > "$mutation_dir/$mutation.log" 2>&1; then
        cat "$mutation_dir/$mutation.log"
        echo "FAIL: $mutation mutation escaped MR backing tests." >&2
        exit 1
    fi
    case "$mutation" in
        host|margin) expected='transparent host must not inflate';;
        plate) expected='ultrawide artwork below frame';;
        confirm) expected='first sample must not reveal';;
        sample) expected='same sample is not independent';;
        snap) expected='corrected backing shrinks visibly instead of popping';;
        hidden) expected='invisible content cannot leave';;
        starve) expected='continuously changing native layout cannot starve';;
        ready) expected='unfitted clone cannot invent';;
        owed) expected='never-drawn map window stays withheld';;
        scope) expected='scoped initiative backing must fit original portrait width';;
        scope-siblings) expected='parent layout graphics must not paint the row backing';;
        scope-ancestor) expected='scope ancestors must still be visited for native clipping';;
    esac
    if ! rg -q "$expected" "$mutation_dir/$mutation.log"; then cat "$mutation_dir/$mutation.log"; exit 1; fi
done
echo 'MR backing negative controls: thirteen production mutations rejected.'
