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
for file in ('MrBackingAnimation.cs','MrBackingLayout.cs','Materialise/WindowMaterialiseField.cs'):
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
code=re.sub(r'/\*.*?\*/|//[^\n]*','',runner,flags=re.S)
assert code.index('runner.CollectElements(host);')<code.index('MrBacking.BeginWindowMaterialise(panel);')<code.index('runner.Apply(0f);')
assert apply.index('MrBacking.ApplyWindowMaterialise(Panel, elementProgress);')<apply.index('cr.SetAlpha(')
assert finish.index('MrBacking.EndWindowMaterialise(Panel, Vanishing);')<finish.index('done?.Invoke();')
print('MR backing animation: 3 runner integration bindings passed.')
# Mutations prove runtime cases detect the two original classes of regression.
src=(out/'MrBackingAnimation.cs').read_text()
for name,needle,replacement in (
    ('tail','&& entry.Animation.Progress < 1f','&& true'),
    ('settle','entry.Layout.Settle(entry.Animation.Bounds, Time.unscaledTime);','entry.Layout.Reset();'),
    ('callback','catch { /* Best-effort cleanup of an already failed, mod-owned decoration. */ }','catch { throw; }')):
    assert src.count(needle)==1,name
    (out/(name+'.fixture')).write_text(src.replace(needle,replacement))
PY
dotnet run --project "$test_dir/GloomhavenVR.MrBackingAnimationTests.csproj" --configuration Release --verbosity quiet
cp "$test_dir/MrBackingAnimation.cs" "$test_dir/original.fixture"
for mutation in tail settle callback; do
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
echo 'MR backing animation: 3 negative controls rejected.'
