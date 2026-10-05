#!/usr/bin/env bash
# Bind the exact original merchant FTUE row, then execute production text patches and causal mutations.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
evidence="$(mktemp -d "$root/.planning/debug/merchant-tutorial-text-XXXXXXXX")"
python="${UNITYPY_PYTHON:-$HOME/unitypy-venv/bin/python}"
"$python" - "$root/ressources/GH_Data" "$evidence/native-config.json" <<'PY'
import hashlib,json,sys
from pathlib import Path
import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
base,destination=map(Path,sys.argv[1:])
generator=TypeTreeGenerator('2021.3.5f1'); generator.load_local_dll_folder(str(base/'Managed'))
env=UnityPy.load(str(base/'sharedassets1.assets'));env.typetree_generator=generator
obj=next(o for o in env.objects if o.path_id==2109)
tree=obj.read_typetree()
assert tree['Phase']==10 and len(tree['Steps'])==1 and tree['Steps'][0]['LocalizationTextKey']=='FTUE_9.3'
destination.write_text(json.dumps({'source':'sharedassets1.assets','path':2109,
 'sha256':hashlib.sha256(obj.get_raw_data()).hexdigest(),'config':tree},indent=2)+'\n')
PY
project="$root/tests/GloomhavenVR.MerchantTutorialTextTests/GloomhavenVR.MerchantTutorialTextTests.csproj"
source="$root/src/GloomhavenVR/Compat/Tutorial/MerchantTutorialExitText.cs"
dotnet run --project "$project" --configuration Release -- "$evidence/native-config.json" | tee "$evidence/production.log"
python3 - "$source" "$evidence" <<'PY'
import hashlib,json,sys
from pathlib import Path
source,dest=map(Path,sys.argv[1:]);text=source.read_text()
mutations=[
 ('no-message-tag','=> MerchantTutorialExitText.Record(__instance, __result);','=> GC.KeepAlive(__instance);',
  'Original first-save merchant exit must explain both working VR close controls'),
 ('wrong-owner','if (ReferenceEquals(candidate, step))','if (true)',
  'Matching text keys from another producer must not become merchant exit text'),
 ('wrong-mode','MapRoomDriver.Active && !WorldUIConfig.ImmersiveTownServices.Value','MapRoomDriver.Active',
  'Immersive resident onboarding does not teach a flat merchant exit'),
 ('wrong-step','buy.Step != EMapFTUEStep.BuyItem','false',
  'A non-BuyItem serialized producer cannot inherit pending BuyItem text'),
 ('no-language-title','=> MerchantTutorialExitText.ApplyTitle(__instance, __instance._message);','{}',
  'German language repaint must restore the merchant exit instruction'),
]
for name,before,after,expected in mutations:
 assert text.count(before)==1,name
 (dest/(name+'.cs')).write_text(text.replace(before,after))
 (dest/(name+'.expected')).write_text(expected)
(dest/'source-sha256.txt').write_text(hashlib.sha256(source.read_bytes()).hexdigest()+'\n')
PY
for mutation in no-message-tag wrong-owner wrong-mode wrong-step no-language-title; do
    if dotnet run --project "$project" --configuration Release \
        --property:MerchantTutorialTextSource="$evidence/$mutation.cs" -- "$evidence/native-config.json" \
        > "$evidence/$mutation.log" 2>&1; then
        echo "FAIL merchant tutorial text: $mutation escaped." >&2; exit 1
    fi
    expected="$(cat "$evidence/$mutation.expected")"
    if ! rg -Fq "Unhandled exception. System.Exception: $expected" "$evidence/$mutation.log"; then
        cat "$evidence/$mutation.log"; exit 1
    fi
    echo "PASS merchant tutorial text negative control: $mutation"
done
echo "PASS merchant tutorial text; retained evidence: $evidence"
