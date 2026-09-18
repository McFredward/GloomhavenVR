#!/usr/bin/env bash
set -euo pipefail
repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
work_dir=$(mktemp -d)
trap 'rm -rf "$work_dir"' EXIT
cp "$repo_root/tests/GloomhavenVR.BurnReplayTests/"*.cs "$repo_root/tests/GloomhavenVR.BurnReplayTests/"*.csproj "$work_dir/"
cp "$repo_root/src/GloomhavenVR/Cards/Art/BurnPlaybackTrace.cs" "$repo_root/src/GloomhavenVR/Cards/Art/NativeBurnEpisode.cs" "$repo_root/src/GloomhavenVR/Cards/Art/NativeBurnEnumerator.cs" "$repo_root/src/GloomhavenVR/Cards/Art/SpentBurnContinuity.cs" "$work_dir/"
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
for mutation in short-rest-preview historical-init historical-rebuild settled-history deferred-reset follower retire-primary retired-follower retargeted-follower recovered-follower implicit-recovery retry-recovery new-episode missing-owner retry-identity terminal-floor activation-floor; do
 python3 - "$work_dir" "$mutation" <<'PY'
from pathlib import Path
import sys
p=Path(sys.argv[1]);s=(p/'production.txt').read_text()
a,b={
'activation-floor':('beforeReset: false, nativeStep: running || Latched(__instance));','beforeReset: false, nativeStep: true);'),
 'terminal-floor':('beforeReset: false, nativeStep: running || Latched(__instance));','beforeReset: false, nativeStep: running);'),
'short-rest-preview':('if (!active && effect == CardEffects.FXTask.BurnCard && IsShortRestPreview(fx)) return false;', ''),
'historical-init':('history.Completed = history.LeftRecoveryPile = true;', 'history.LeftRecoveryPile = true;'),
'historical-rebuild':('if (burnAnim && initialCard != null && Durable(initialCard, initialOwner)', 'if (bool.Parse("false") && burnAnim && initialCard != null && Durable(initialCard, initialOwner)'),
'settled-history':('episode.Observe(initialCard, Recovered(initialCard, initialOwner), running: false);', ''),
'deferred-reset':('if (!running && episode.TakeDeferredRestore(Durable(card, owner))) __instance.RestoreCard();', ''),
'follower':('bool followsOriginal = burnAnim && initialCard != null', 'bool followsOriginal = bool.Parse("false") && burnAnim && initialCard != null'),
'retire-primary':('&& ReferenceEquals(history.Running, playback)) history.Running = null;', '&& ReferenceEquals(history.Running, playback)) { }'),
'retired-follower':('if (!stillOwned()) return false;', ''),
'retargeted-follower':('ReferenceEquals(currentCard, card) &&', ''),
'recovered-follower':('ReferenceEquals(ReadHistory(card, owner, resetting: false), history)', 'true'),
'implicit-recovery':('if (!departed) return;', 'if (bool.Parse("true")) return;'),
'retry-recovery':('departed |= pending != null && ReferenceEquals(pending.Card, card);', ''),
'new-episode':('if (burnAnim && !followsOriginal) RecoveryResets.Remove(__instance);', ''),
'missing-owner':('if (owner?.CharacterClass == null) return;', ''),
'retry-identity':('RecoveryResets.TryGetValue(fx, out var pending);', 'RecoveryResets.TryGetValue(fx, out var pending); if (pending != null) pending.Card = card;'),
}[sys.argv[2]]
assert s.count(a)==1;(p/'Production.cs').write_text(s.replace(a,b))
PY
 if dotnet run --project "$work_dir/GloomhavenVR.BurnReplayTests.csproj" -c Release > "$work_dir/negative.log" 2>&1; then cat "$work_dir/negative.log"; exit 1; fi
 case "$mutation" in
 activation-floor) expected='A legitimate deferred activation reset must not regain its old spent floor';;
 terminal-floor) expected='The terminal native step must not expose raw unspent artwork';;
 short-rest-preview) expected='Uncommitted short-rest hover must retain spent artwork without a premature flame preview';;
 historical-init|historical-rebuild) expected='First-seen already-lost widgets must settle native artwork without a historical replay';;
 settled-history) expected='Historical native settle must survive a standalone reset without blue flash';;
 deferred-reset) expected='A completed activation must permit the native clean active-card look';;
 follower) expected='A replacement widget must wait on the original burn without starting another ramp';;
 retire-primary) expected='Retiring the primary must release replacement waiters without another ramp';;
 retired-follower) expected='Retired follower iterators must stop without painting recycled artwork';;
 retargeted-follower) expected='Retargeted follower iterators must stop without altering the new card';;
 recovered-follower) expected='Original recovery must release waiters without painting a new lost state';;
 implicit-recovery) expected='Recovery without a native SetPile edge must restore the original before local draw and remote capture';;
 retry-recovery) expected='A later intact original must retry recovery after an earlier native reset failure';;
 new-episode) expected='A failed recovery retry must never cancel a new genuine burn of the same card';;
 missing-owner) expected='Unresolved owner metadata must not clear a real lost card from a stale Hand stamp';;
 retry-identity) expected="A pooled rebind must never apply another card's failed recovery to its new presentation";;
 esac
 if ! grep -Fq "$expected" "$work_dir/negative.log"; then cat "$work_dir/negative.log"; exit 1; fi
 echo "Burn history negative control: $mutation failed as expected."
done

for binding in local capture plume; do
 mkdir -p "$work_dir/source/src/GloomhavenVR/Cards/Art" "$work_dir/source/src/GloomhavenVR/Net"
 cp "$repo_root/src/GloomhavenVR/Cards/BurnArtwork.cs" "$work_dir/source/src/GloomhavenVR/Cards/"
 cp "$repo_root/src/GloomhavenVR/Cards/Art/BurnLookPolicy.cs" "$work_dir/source/src/GloomhavenVR/Cards/Art/"
 cp "$repo_root/src/GloomhavenVR/Net/CardAppearanceSampler.cs" "$repo_root/src/GloomhavenVR/Net/CardPlumeSampler.cs" "$work_dir/source/src/GloomhavenVR/Net/"
 python3 - "$work_dir/source/src/GloomhavenVR" "$binding" <<'PYB'
from pathlib import Path
import sys
root=Path(sys.argv[1])
file,old={
'local':('Cards/Art/BurnLookPolicy.cs','BurnArtwork.ReconcileRecoveredAppearance(fx);'),
'capture':('Net/CardAppearanceSampler.cs','BurnArtwork.ReconcileRecoveredAppearance(full.cardEffects);'),
'plume':('Net/CardPlumeSampler.cs','!ReferenceEquals(smoke, effects._smokeEffect)'),
}[sys.argv[2]]
p=root/file;s=p.read_text();assert s.count(old)==1;p.write_text(s.replace(old,'false' if sys.argv[2]=='plume' else ''))
PYB
 if python3 "$repo_root/tests/GloomhavenVR.BurnReplayTests/extract.py" "$work_dir/source" "$work_dir/Disconnected.cs" > "$work_dir/binding.log" 2>&1; then cat "$work_dir/binding.log";exit 1;fi
 case "$binding" in
 local) expected='Local recovery binding missing';;
 capture) expected='Owner capture recovery binding missing';;
 plume) expected='Recovered plume ownership binding missing';;
 esac
 if ! grep -Fq "$expected" "$work_dir/binding.log"; then cat "$work_dir/binding.log";exit 1;fi
 echo "Burn recovery disconnected binding: $binding failed as expected."
done

rm -rf "$work_dir/source"
rm -f "$work_dir/Disconnected.cs"
cp "$work_dir/production.txt" "$work_dir/Production.cs"
cp "$work_dir/BurnPlaybackTrace.cs" "$work_dir/trace.txt"
for mutation in normal-level renderer-binding episode-cap session-cap repeated-events flame-rewind; do
 python3 - "$work_dir" "$mutation" <<'PYT'
from pathlib import Path
import sys
p=Path(sys.argv[1]);s=(p/'trace.txt').read_text()
a,b={
'normal-level':('!VRLog.WantsDebug || ', ''),
'renderer-binding':('graphic.canvasRenderer.GetMaterial()', 'graphic.material'),
'episode-cap':('trace.Lines >= EpisodeLineLimit', 'false'),
'session-cap':('s_lines >= SessionLineLimit', 'false'),
'repeated-events':('!trace.Events.Add((transition, effect, active))', 'false'),
'flame-rewind':('flameBaseAnim + .01f < trace.FlameBaseAnim\n                || flameDrawnAnim + .01f < trace.FlameDrawnAnim', 'false'),
}[sys.argv[2]]
# Keep the ordinary-event reserve expression intact when removing the final episode cap.
if sys.argv[2]=='episode-cap':
 s=s.replace(a+' ||','false ||').replace(a+')','false)')
else:
 assert a in s;s=s.replace(a,b)
(p/'BurnPlaybackTrace.cs').write_text(s)
PYT
 if dotnet run --project "$work_dir/GloomhavenVR.BurnReplayTests.csproj" -c Release > "$work_dir/trace-negative.log" 2>&1; then cat "$work_dir/trace-negative.log";exit 1;fi
 case "$mutation" in
 normal-level) expected='Normal logging must never record native burn tracing';;
 renderer-binding) expected="Debug tracing must observe the renderer's own rewind independently of the source material";;
 episode-cap) expected='A burn diagnostic episode must stay bounded';;
 session-cap) expected='A long Debug session must cap native burn trace output';;
 repeated-events) expected='Repeated native reset requests must be deduplicated';;
 flame-rewind) expected='Debug tracing must detect a flame-only rewind';;
 esac
 if ! grep -Fq "$expected" "$work_dir/trace-negative.log"; then cat "$work_dir/trace-negative.log";exit 1;fi
 echo "Burn trace negative control: $mutation failed as expected."
done
