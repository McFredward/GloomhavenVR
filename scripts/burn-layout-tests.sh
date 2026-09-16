#!/usr/bin/env bash
set -euo pipefail
repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
work_dir=$(mktemp -d)
trap 'rm -rf "$work_dir"' EXIT
cp "$repo_root/tests/GloomhavenVR.BurnLayoutTests/"*.cs "$repo_root/tests/GloomhavenVR.BurnLayoutTests/"*.csproj "$work_dir/"
python3 "$repo_root/tests/GloomhavenVR.BurnLayoutTests/extract.py" "$repo_root" "$work_dir/Production.cs"
dotnet run --project "$work_dir/GloomhavenVR.BurnLayoutTests.csproj" --configuration Release
for mutation in early-layout missed-model missed-native no-retry last-card-only; do
    cp "$work_dir/Production.cs" "$work_dir/original.txt"
    python3 - "$work_dir/Production.cs" "$mutation" <<'PY'
import pathlib,sys
p=pathlib.Path(sys.argv[1]);s=p.read_text()
old,new={
'early-layout':('if (DeferLayoutForBurn()) return;','if (false) return;'),
'missed-model':('if (card.IsHeld || !IsFreshBurn(_boundHand, card)','if (true || card.IsHeld || !IsFreshBurn(_boundHand, card)'),
'missed-native':('_burnLayoutNativeActive |= BurnArtwork.Playing(BurnArtwork.EffectsOf(card.FullCard))','_burnLayoutNativeActive |= false && BurnArtwork.Playing(BurnArtwork.EffectsOf(card.FullCard))'),
'no-retry':('_dirty = true;', '_dirty = false;'),
'last-card-only':('_burnLayoutNativeActive |=', '_burnLayoutNativeActive ='),
}[sys.argv[2]]
assert old in s;p.write_text(s.replace(old,new).replace('if (false)', 'if (bool.Parse("false"))').replace('if (true ||', 'if (bool.Parse("true") ||'))
PY
    if dotnet run --project "$work_dir/GloomhavenVR.BurnLayoutTests.csproj" --configuration Release > "$work_dir/negative.log" 2>&1; then cat "$work_dir/negative.log"; exit 1; fi
    case "$mutation" in
        early-layout|missed-model) expected='Model-first loss must retain the outgoing layout';;
        missed-native) expected='Live burn must never admit sibling movement or replacement';;
        no-retry) expected='Retained card input must wait and rebuild must remain scheduled';;
        last-card-only) expected='Live burn must never admit sibling movement or replacement';;
    esac
    if ! grep -Fq "$expected" "$work_dir/negative.log"; then cat "$work_dir/negative.log"; exit 1; fi
    mv "$work_dir/original.txt" "$work_dir/Production.cs"
    echo "Burn layout negative control: $mutation failed as expected."
done
