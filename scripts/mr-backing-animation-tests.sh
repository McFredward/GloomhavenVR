#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source_root="${GHVR_MR_SOURCE_ROOT:-$repo_root}"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
test_dir="$(mktemp -d)"
trap 'rm -rf "$test_dir"' EXIT
cp "$repo_root/tests/GloomhavenVR.MrBackingAnimationTests/"* "$test_dir/"
python3 - "$source_root" "$test_dir" <<'PY'
from pathlib import Path
import re,sys
root,out=map(Path,sys.argv[1:]);ui=root/'src/GloomhavenVR/WorldUI'
for file in ('MrBackingAnimation.cs','MrBackingLayout.cs','Materialise/WindowMaterialiseField.cs','Conversion/MrBackingCaptureBounds.cs'):
    (out/Path(file).name).write_text((ui/file).read_text())
runner=(ui/'Materialise/WindowMaterialiseRunner.cs').read_text()
def method(text,signature):
    start=text.index(signature);opening=text.index('{',start);end=opening+1;depth=1
    while depth:
        depth+=(text[end]=='{')-(text[end]=='}');end+=1
    return text[start:end]
# Compile these exact production methods, not mirrored state machines. The remaining Unity
# shell is a narrow fault-injectable double; mesh geometry has its own production harness.
apply=method(runner,'    private void Apply(float k)')
finish=method(runner,'    internal void Finish(string reason, bool restore)')
(out/'RunnerProduction.cs').write_text('using System; using System.Collections.Generic; using UnityEngine; namespace GloomhavenVR.WorldUI; internal sealed partial class WindowMaterialiseRunner {\n'+apply+'\n'+finish+'\n}')
switch=(ui/'Materialise/WindowMaterialise.cs').read_text()
default_start=switch.index('    private static bool DefaultEnabled =>')
default=switch[default_start:switch.index(';',default_start)+1]
members=[default]+[method(switch,s) for s in (
    '    private static void EnsureBound()', '    private static void OnEnabledChanged(',
    '    internal static bool Enabled', '    internal static float AppearSeconds',
    '    internal static float VanishSeconds', '    internal static void Register(',
    '    internal static void PlayIn(', '    internal static void PlayOut(',
    '    internal static void Cancel(', '    internal static void CancelAll(')]
(out/'SwitchProduction.cs').write_text('using System; using BepInEx.Configuration; using GloomhavenVR.Core; using UnityEngine; namespace GloomhavenVR.WorldUI; internal static partial class WindowMaterialise {\n'+'\n'.join(members)+'\n}')
(out/'FrameDefaults.cs').write_text((root/'src/GloomhavenVR/Core/Startup/FrameDefaults.cs').read_text())
# Off blocks construction before any geometry capture in both opening and closing paths;
# the surface and MR decoration share exactly those existing entry points.
assert members[7].index('if (!Enabled)')<members[7].index('WindowMaterialiseRunner.Begin(')
assert members[8].index('if (!Enabled)')<members[8].index('WindowMaterialiseRunner.Begin(')
assert 'if (!Enabled || VanishSeconds <= 0f)' in switch
surface=(ui/'Surfaces/SurfaceMaterialise.cs').read_text()
assert 'WindowMaterialise.PlayIn(e.Panel);' in surface and 'WindowMaterialise.PlayOut(panel, () => OnEffectDone(v));' in surface
code=re.sub(r'/\*.*?\*/|//[^\n]*','',runner,flags=re.S)
assert code.index('runner.CollectElements(host);')<code.index('MrBacking.BeginWindowMaterialise(panel, runner._renderers, runner._origAlpha);')<code.index('runner.Apply(0f);')
assert apply.index('MrBacking.ApplyWindowMaterialise(Panel, elementProgress);')<apply.index('cr.SetAlpha(')
assert finish.index('MrBacking.EndWindowMaterialise(Panel, Vanishing);')<finish.index('done?.Invoke();')
print('MR backing animation: 3 runner integration bindings passed.')
print('Window materialization switch: 4 production entry-point bindings passed.')
# Mutations prove runtime cases detect the two original classes of regression.
src=(out/'MrBackingAnimation.cs').read_text()
for name,needle,replacement in (
    ('tail','visible &= MrBackingLayout.ReadyForSample(true, rect) && entry.Animation.Progress < 1f;',
     'visible &= MrBackingLayout.ReadyForSample(true, rect);'),
    ('settle','entry.Layout.Settle(entry.Animation.Bounds, Time.unscaledTime);','entry.Layout.Reset();'),
    ('callback','catch { /* Best-effort cleanup of an already failed, mod-owned decoration. */ }','catch { throw; }'),
    ('capture','        RefreshAnimationCapture(entry);',''),
    ('pending',' || entry.Animation.PendingGeometry','')):
    assert src.count(needle)==1,name
    (out/(name+'.fixture')).write_text(src.replace(needle,replacement))
switch_src=(out/'SwitchProduction.cs').read_text()
needle='        _enabled.SettingChanged += OnEnabledChanged;'
assert switch_src.count(needle)==1
(out/'toggle.fixture').write_text(switch_src.replace(needle,''))
frame=(out/'FrameDefaults.cs').read_text()
needle='internal const bool WindowMaterialise = false;'
assert frame.count(needle)==1
(out/'frame-default.fixture').write_text(frame.replace(needle,'internal const bool WindowMaterialise = true;'))
PY
dotnet run --project "$test_dir/GloomhavenVR.MrBackingAnimationTests.csproj" --configuration Release --verbosity quiet
cp "$test_dir/MrBackingAnimation.cs" "$test_dir/original.fixture"
for mutation in tail settle callback capture pending; do
    cp "$test_dir/$mutation.fixture" "$test_dir/MrBackingAnimation.cs"
    if dotnet run --project "$test_dir/GloomhavenVR.MrBackingAnimationTests.csproj" --configuration Release --verbosity quiet >"$test_dir/$mutation.log" 2>&1; then
        echo "ERROR: MR backing animation accepted mutation $mutation" >&2
        exit 1
    fi
    if grep -q 'error CS' "$test_dir/$mutation.log"; then
        cat "$test_dir/$mutation.log" >&2
        exit 1
    fi
done
echo 'MR backing animation: 5 negative controls rejected.'
cp "$test_dir/original.fixture" "$test_dir/MrBackingAnimation.cs"
cp "$test_dir/SwitchProduction.cs" "$test_dir/switch-original.fixture"
cp "$test_dir/FrameDefaults.cs" "$test_dir/frame-original.fixture"
for mutation in toggle frame-default; do
    if [[ "$mutation" == toggle ]]; then
        cp "$test_dir/toggle.fixture" "$test_dir/SwitchProduction.cs"
    else
        cp "$test_dir/switch-original.fixture" "$test_dir/SwitchProduction.cs"
        cp "$test_dir/frame-default.fixture" "$test_dir/FrameDefaults.cs"
    fi
    if dotnet run --project "$test_dir/GloomhavenVR.MrBackingAnimationTests.csproj" --configuration Release --verbosity quiet >"$test_dir/$mutation.log" 2>&1; then
        echo "ERROR: window materialization switch accepted mutation $mutation" >&2
        exit 1
    fi
    if grep -q 'error CS' "$test_dir/$mutation.log"; then
        cat "$test_dir/$mutation.log" >&2
        exit 1
    fi
done
echo 'Window materialization switch: 2 negative controls rejected.'
