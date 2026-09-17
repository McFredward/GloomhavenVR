#!/usr/bin/env bash
set -euo pipefail
repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
work_dir=$(mktemp -d)
trap 'rm -rf "$work_dir"' EXIT
cp "$repo_root/tests/GloomhavenVR.BurnReplayTests/"*.cs "$repo_root/tests/GloomhavenVR.BurnReplayTests/"*.csproj "$work_dir/"
cp "$repo_root/src/GloomhavenVR/Cards/Art/NativeBurnEpisode.cs" "$repo_root/src/GloomhavenVR/Cards/Art/NativeBurnEnumerator.cs" "$work_dir/"
python3 "$repo_root/tests/GloomhavenVR.BurnReplayTests/extract.py" "$repo_root" "$work_dir/Production.cs"
dotnet run --project "$work_dir/GloomhavenVR.BurnReplayTests.csproj" -c Release
cp "$work_dir/NativeBurnEpisode.cs" "$work_dir/original.txt"
for mutation in replay completed recovery identity; do
 python3 - "$work_dir" "$mutation" <<'PY'
from pathlib import Path
import sys
p=Path(sys.argv[1]);s=(p/'original.txt').read_text()
a,b={
'replay':('_running || _completed','false || _completed'),
'completed':('_completed && (!resetting || durable)','false && (!resetting || durable)'),
'recovery':('recovered && _leftRecoveryPile','false && _leftRecoveryPile'),
'identity':('!ReferenceEquals(_card, card) ||','false ||'),
}[sys.argv[2]]
assert s.count(a)==1
(p/'NativeBurnEpisode.cs').write_text(s.replace(a,b))
PY
 if dotnet run --project "$work_dir/GloomhavenVR.BurnReplayTests.csproj" -c Release > "$work_dir/negative.log" 2>&1; then cat "$work_dir/negative.log"; exit 1; fi
 case "$mutation" in
 replay) expected='Repeated native refresh must retain the first burn iterator and paint';;
 completed) expected='Completed lost burns must never replay or turn blue';;
 recovery) expected='Actual model recovery must restore normal artwork';;
 identity) expected="Recycled widget identity must not inherit another card's burn";;
 esac
 if ! grep -Fq "$expected" "$work_dir/negative.log"; then cat "$work_dir/negative.log"; exit 1; fi
 echo "Burn replay negative control: $mutation failed as expected."
done
cp "$work_dir/original.txt" "$work_dir/NativeBurnEpisode.cs"
cp "$work_dir/Production.cs" "$work_dir/production.txt"
for mutation in historical-init historical-rebuild settled-history deferred-reset follower retire-primary retired-follower retargeted-follower recovered-follower; do
 python3 - "$work_dir" "$mutation" <<'PY'
from pathlib import Path
import sys
p=Path(sys.argv[1]);s=(p/'production.txt').read_text()
a,b={
'historical-init':('history.Completed = history.LeftRecoveryPile = true;', 'history.LeftRecoveryPile = true;'),
'historical-rebuild':('if (burnAnim && initialCard != null && Durable(initialCard, initialOwner)', 'if (bool.Parse("false") && burnAnim && initialCard != null && Durable(initialCard, initialOwner)'),
'settled-history':('episode.Observe(initialCard, Recovered(initialCard, initialOwner), running: false);', ''),
'deferred-reset':('if (!running && episode.TakeDeferredRestore(Durable(card, owner))) __instance.RestoreCard();', ''),
'follower':('bool followsOriginal = burnAnim && initialCard != null', 'bool followsOriginal = bool.Parse("false") && burnAnim && initialCard != null'),
'retire-primary':('&& ReferenceEquals(history.Running, playback)) history.Running = null;', '&& ReferenceEquals(history.Running, playback)) { }'),
'retired-follower':('if (!stillOwned()) return false;', ''),
'retargeted-follower':('ReferenceEquals(currentCard, card) &&', ''),
'recovered-follower':('ReferenceEquals(ReadHistory(card, owner, resetting: false), history)', 'true'),
}[sys.argv[2]]
assert s.count(a)==1;(p/'Production.cs').write_text(s.replace(a,b))
PY
 if dotnet run --project "$work_dir/GloomhavenVR.BurnReplayTests.csproj" -c Release > "$work_dir/negative.log" 2>&1; then cat "$work_dir/negative.log"; exit 1; fi
 case "$mutation" in
 historical-init|historical-rebuild) expected='First-seen already-lost widgets must settle native artwork without a historical replay';;
 settled-history) expected='Historical native settle must survive a standalone reset without blue flash';;
 deferred-reset) expected='A completed activation must permit the native clean active-card look';;
 follower) expected='A replacement widget must wait on the original burn without starting another ramp';;
 retire-primary) expected='Retiring the primary must release replacement waiters without another ramp';;
 retired-follower) expected='Retired follower iterators must stop without painting recycled artwork';;
 retargeted-follower) expected='Retargeted follower iterators must stop without altering the new card';;
 recovered-follower) expected='Original recovery must release waiters without painting a new lost state';;
 esac
 if ! grep -Fq "$expected" "$work_dir/negative.log"; then cat "$work_dir/negative.log"; exit 1; fi
 echo "Burn history negative control: $mutation failed as expected."
done
