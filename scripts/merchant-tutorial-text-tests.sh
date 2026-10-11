#!/usr/bin/env bash
# Bind the exact original merchant FTUE row, then execute production text patches and causal mutations.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
case "${1:-}" in
    ""|--production-only) ;;
    *) echo 'Usage: merchant-tutorial-text-tests.sh [--production-only]' >&2; exit 2 ;;
esac
mkdir -p "$root/.planning/debug"
evidence="$(mktemp -d "$root/.planning/debug/merchant-tutorial-text-XXXXXXXX")"
identity="$root/tests/GloomhavenVR.MerchantTutorialTextTests/native-config.json"
cp "$identity" "$evidence/native-config.json"
python="${UNITYPY_PYTHON:-$HOME/unitypy-venv/bin/python}"
if [[ ! -x "$python" ]]; then python="$(command -v python3)"; fi
# CI needs only the bounded, asset-free native row identity. When the actual game
# and UnityPy are available, independently verify that identity and its raw-object
# hash before using the fresh extraction. Do not silently accept extraction drift.
if [[ -f "$root/ressources/GH_Data/sharedassets1.assets" ]] \
    && "$python" -c 'import UnityPy; from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator' >/dev/null 2>&1; then
"$python" - "$root/ressources/GH_Data" "$identity" "$evidence/native-config.json" <<'PY'
import hashlib,json,sys
from pathlib import Path
import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
base,identity,destination=map(Path,sys.argv[1:])
generator=TypeTreeGenerator('2021.3.5f1'); generator.load_local_dll_folder(str(base/'Managed'))
env=UnityPy.load(str(base/'sharedassets1.assets'));env.typetree_generator=generator
obj=next(o for o in env.objects if o.path_id==2109)
tree=obj.read_typetree()
actual={'source':'sharedassets1.assets','path':2109,
 'sha256':hashlib.sha256(obj.get_raw_data()).hexdigest(),
 'config':{'Phase':tree['Phase'],'Steps':[{key:step[key] for key in
  ('LocalizationTextKey','LocalizationTextKeyController','LayoutType')} for step in tree['Steps']]}}
assert actual==json.loads(identity.read_text()), 'Original native merchant FTUE identity changed; review the new game source before updating the fixture.'
destination.write_text(json.dumps(actual,indent=2)+'\n')
print('Verified read-only original merchant FTUE asset identity against the portable fixture.')
PY
else
    echo 'Using portable extracted merchant FTUE identity; game assets/UnityPy unavailable.'
fi
project="$root/tests/GloomhavenVR.MerchantTutorialTextTests/GloomhavenVR.MerchantTutorialTextTests.csproj"
source="$root/src/GloomhavenVR/Compat/Tutorial/MerchantTutorialExitText.cs"
dotnet run --project "$project" --configuration Release -- "$evidence/native-config.json" | tee "$evidence/production.log"
if [[ "${1:-}" == --production-only ]]; then
    echo "PASS merchant tutorial text production; retained evidence: $evidence"
    exit 0
fi
python3 - "$source" "$evidence" <<'PY'
import hashlib,json,sys
from pathlib import Path
source,dest=map(Path,sys.argv[1:]);text=source.read_text()
mutations=[
 ('no-message-tag','=> MerchantTutorialExitText.Record(__instance, __result);','=> GC.KeepAlive(__instance);',
  'Original first-save merchant exit must explain both working VR close controls'),
 ('wrong-owner','if (ReferenceEquals(candidate, step))','if (true)',
  'Matching text keys from another producer must not become merchant exit text'),
 ('wrong-mode','(!MapRoomDriver.Active || !WorldUIConfig.ImmersiveTownServices.Value)','true',
  'Immersive resident onboarding does not teach a flat merchant exit'),
 ('wrong-native-producer','!ReferenceEquals(Singleton<MapFTUEManager>.Instance?.currentStep, buy)','false',
  'Only the current native shop promise can author its own instruction'),
 ('stale-source-repaint','&& IsOriginal(marker.Source, marker.Producer)','',
  "Retired source rows cannot keep rewriting a successor's native title"),
 ('no-language-title','=> MerchantTutorialExitText.ApplyTitle(__instance, __instance._message);','{}',
  'German language repaint must restore the merchant exit instruction'),
]
for name,before,after,expected in mutations:
 assert text.count(before)==1,name
 (dest/(name+'.cs')).write_text(text.replace(before,after))
 (dest/(name+'.expected')).write_text(expected)
(dest/'source-sha256.txt').write_text(hashlib.sha256(source.read_bytes()).hexdigest()+'\n')
PY
for mutation in no-message-tag wrong-owner wrong-mode wrong-native-producer stale-source-repaint no-language-title; do
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
