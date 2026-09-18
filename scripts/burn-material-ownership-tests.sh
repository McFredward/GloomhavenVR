#!/usr/bin/env bash
set -euo pipefail
repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
work_dir=$(mktemp -d)
trap 'rm -rf "$work_dir"' EXIT
cp "$repo_root/tests/GloomhavenVR.BurnMaterialOwnershipTests/"*.cs "$repo_root/tests/GloomhavenVR.BurnMaterialOwnershipTests/"*.csproj "$work_dir/"
python3 "$repo_root/tests/GloomhavenVR.BurnMaterialOwnershipTests/extract.py" "$repo_root" "$work_dir/Production.cs"
dotnet run --project "$work_dir/GloomhavenVR.BurnMaterialOwnershipTests.csproj" -c Release
cp "$work_dir/Production.cs" "$work_dir/original.txt"
for mutation in native-field native-component registry parent remote-hold; do
 python3 - "$work_dir" "$mutation" <<'PY'
from pathlib import Path
import sys
p=Path(sys.argv[1]);s=(p/'original.txt').read_text()
a,b={
'native-field':('face.cardEffects != null || ',''),
'native-component':('face.GetComponent<CardEffects>() != null','false'),
'registry':(' || CardFace.OwnerOf(face) != null',''),
'parent':('return face.GetComponentInParent<AbilityCardUI>(includeInactive: true) == null;', 'return true;'),
'remote-hold':('if (!HasCardFxHold(face))', 'if (true)'),
}[sys.argv[2]]
assert s.count(a)==1
(p/'Production.cs').write_text(s.replace(a,b))
PY
 if dotnet run --project "$work_dir/GloomhavenVR.BurnMaterialOwnershipTests.csproj" -c Release > "$work_dir/negative.log" 2>&1; then cat "$work_dir/negative.log"; exit 1; fi
 if ! grep -Eq 'must retain its native animated material|Owner-driven remote burn output must retain its material' "$work_dir/negative.log"; then cat "$work_dir/negative.log"; exit 1; fi
 echo "Burn material ownership negative control: $mutation failed as expected."
done
