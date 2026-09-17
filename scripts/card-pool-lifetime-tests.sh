#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$root/tests/GloomhavenVR.CardPoolLifetimeTests/GloomhavenVR.CardPoolLifetimeTests.csproj"
dotnet run --project "$project" --configuration Release
fixture="$(mktemp -d)"
trap 'rm -rf "$fixture"' EXIT
python3 - "$root" "$fixture" <<'PY'
from pathlib import Path
import re, sys
root, dest=map(Path,sys.argv[1:])
base=root/'src/GloomhavenVR/Cards'
s=(base/'Art/NativeCardPoolLifetime.cs').read_text()
for name,old,new in [
 ('foreign-parent','face.SetParent(widget.transform, worldPositionStays: true);',''),
 ('dead-half','if (copy == null || IsIntact(copy.GetComponent<AbilityCardUI>())) continue;','if (copy == null || pool.Instances.Count > 0) continue;'),
 ('loss-tween','widget.CancelLostAnimation();',''),
 ('other-card','&& full.topActionButton.transform.IsChildOf(full.transform)','')]:
 assert s.count(old)==1,name
 (dest/(name+'.cs')).write_text(s.replace(old,new))
def code(path): return re.sub(r'/\*[\s\S]*?\*/|//[^\n]*','',path.read_text())
patch=code(base/'Patches/CardLifecyclePatches.cs')
module=code(base/'CardsModule.cs')
for target in ('ObjectPool_RecycleCard_NativeHierarchy','ObjectPool_GetCardInstance_NativeHierarchy'):
 assert 'PatchAll(typeof('+target+'))' in module,target
assert 'CardsSignals.RaiseCardRecycling(widget);' in patch
assert 'NativeCardPoolLifetime.ReturnToOwner(widget);' in patch
assert 'NativeCardPoolLifetime.PruneDamagedCopies(pool)' in patch
driver=code(base/'Driver/CardsDriver.2.Update.cs').split('internal static void ReleaseCardsBeforeSceneLoad()',1)[1]
assert driver.index('_factory.ReturnBorrowedFacesBeforeSceneLoad()') < driver.index('NativeCardPoolLifetime.ReturnSceneHands') < driver.index('driver.ClearLaserHover')
print('Native card pool lifetime: 6 source bindings passed.')
PY
for mutation in foreign-parent dead-half other-card loss-tween; do
    case "$mutation" in
        foreign-parent) expected='Dialog-owned face must survive' ;;
        dead-half) expected='Damaged recycled card must be removed' ;;
        other-card) expected='Serialized action references must not alias' ;;
        loss-tween) expected="Early native return must preserve the pool's loss-animation cancellation" ;;
    esac
    if dotnet run --project "$project" --configuration Release --property:LifetimeSource="$fixture/$mutation.cs" > "$fixture/$mutation.log" 2>&1; then
        echo "FAIL: $mutation escaped" >&2; exit 1
    fi
    if ! rg -Fq "$expected" "$fixture/$mutation.log"; then cat "$fixture/$mutation.log"; exit 1; fi
    echo "Native card pool lifetime negative control: $mutation rejected."
done
