#!/usr/bin/env bash
# Execute native item DTO/codec/sampling/rendering/lifecycle against observable Unity and model seams.
set -euo pipefail
repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
python3 "$repo_root/tests/GloomhavenVR.ItemAppearanceTests/transport_bindings.py" "$repo_root"
work_dir=$(mktemp -d)
trap 'rm -rf "$work_dir"' EXIT
cp "$repo_root/tests/GloomhavenVR.ItemAppearanceTests/"*.cs "$repo_root/tests/GloomhavenVR.ItemAppearanceTests/"*.csproj "$work_dir/"
cp "$repo_root/src/GloomhavenVR/Net/ItemAppearance"*.cs "$work_dir/"
cp "$repo_root/src/GloomhavenVR/Net/UseBarAnimationPlaybackClock.cs" "$repo_root/src/GloomhavenVR/Net/Remote/NativePlaybackWrites.cs" "$work_dir/"
python3 - "$repo_root/src/GloomhavenVR/Net/CardAppearanceState.cs" "$work_dir/CardAppearanceNode.cs" <<'PYTHON'
import pathlib,sys
source=pathlib.Path(sys.argv[1]).read_text()
pathlib.Path(sys.argv[2]).write_text(source[:source.index('internal sealed class CardAppearanceState')])
PYTHON
python3 - "$repo_root/src/GloomhavenVR/WorldUI/Sharpness/PanelGraphicMaterial.cs" "$work_dir/PanelGraphicMaterial.cs" <<'PYTHON'
import pathlib,sys
source=pathlib.Path(sys.argv[1]).read_text();start=source.index('    internal static Material? Read(');brace=source.index('{',start);end=brace+1;depth=1
while depth:depth+=(source[end]=='{')-(source[end]=='}');end+=1
pathlib.Path(sys.argv[2]).write_text('using TMPro; using UnityEngine; using UnityEngine.UI; namespace GloomhavenVR.WorldUI; internal static class PanelGraphicMaterial {\n'+source[start:end]+'\n}')
PYTHON
dotnet run --project "$work_dir/GloomhavenVR.ItemAppearanceTests.csproj" --configuration Release
for mutation in early-terminal passive-tmp-read stale-terminal no-binding-retry material-endpoint; do
    cp "$work_dir/ItemAppearanceMirror.cs" "$work_dir/mirror.backup"
    cp "$work_dir/ItemAppearanceSampler.cs" "$work_dir/sampler.backup"
    cp "$work_dir/ItemAppearanceBindings.cs" "$work_dir/bindings.backup"
    python3 - "$work_dir" "$mutation" <<'PYTHON'
import pathlib,sys
root=pathlib.Path(sys.argv[1]); name,old,new={
    'early-terminal':('ItemAppearanceMirror.cs','entry.TerminalTime >= 0 && entry.Presented < entry.TerminalTime','false'),
    'passive-tmp-read':('ItemAppearanceBindings.cs','WorldUI.PanelGraphicMaterial.Read(graphic)','graphic.material'),
    'stale-terminal':('ItemAppearanceSampler.cs','if (entry.TerminalPinned)','if (entry.TerminalPinned && completed)'),
    'no-binding-retry':('ItemAppearanceMirror.cs','if (entry.Item == null) entry.Item = Resolve(RemoteBoardFocus.ActorById(state.ActorId), state);',''),
    'material-endpoint':('ItemAppearanceBindings.cs','V(8+f)','0f'),
}[sys.argv[2]]
p=root/name;source=p.read_text();assert old in source;p.write_text(source.replace(old,new))
PYTHON
    if dotnet run --project "$work_dir/GloomhavenVR.ItemAppearanceTests.csproj" --configuration Release > "$work_dir/negative.log" 2>&1; then cat "$work_dir/negative.log"; exit 1; fi
    case "$mutation" in
        early-terminal) expected='Terminal arrival alone cannot release an unpainted original burn';;
        passive-tmp-read) expected='Passive TMP capture must never read allocating material getter';;
        stale-terminal) expected='Terminal output must be retained before collapse mutates original graphics';;
        no-binding-retry) expected='Model arrival must retry unresolved item binding';;
        material-endpoint) expected='Original native grey and burn values must be copied without settled endpoints';;
    esac
    if ! rg -Fq "$expected" "$work_dir/negative.log"; then cat "$work_dir/negative.log"; exit 1; fi
    mv "$work_dir/mirror.backup" "$work_dir/ItemAppearanceMirror.cs"
    mv "$work_dir/sampler.backup" "$work_dir/ItemAppearanceSampler.cs"
    mv "$work_dir/bindings.backup" "$work_dir/ItemAppearanceBindings.cs"
    echo "Item appearance negative control: $mutation failed as expected."
done
