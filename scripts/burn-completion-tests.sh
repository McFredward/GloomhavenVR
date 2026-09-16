#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$repo_root/tests/GloomhavenVR.BurnCompletionTests/GloomhavenVR.BurnCompletionTests.csproj"
source="$repo_root/src/GloomhavenVR/Net/CardAppearanceSampler.Burn.cs"
python3 - "$repo_root" <<'PYBIND'
from pathlib import Path
import sys
r=Path(sys.argv[1]); sampler=(r/'src/GloomhavenVR/Net/CardAppearanceSampler.cs').read_text()
assert sampler.index('AppendBurnFinals(states);') < sampler.index('if (states.Count > CardAppearanceState.CountMax)'), 'Final native output must join the actual sender sample before serialization'
a=(r/'src/GloomhavenVR/Net/Remote/RemoteAvatar.cs').read_text()
assert a.index('ApplyBurnCompletions(p.BurnCompletions);') < a.index('foreach (CardFlightEvent flight'), 'Durable releases precede legacy flight admission'
assert 'completions.Covers(sequence, endpoints, flags, source)' in a, 'Missing capture must retain the legacy release path'
assert '(initial || _burnProgressKeys.ContainsKey(entry.Key)) && (card == null || !_burnFx.HasObservedBurn(card))' in a, 'Joining must not invent a historical burn hold'
assert 'ObserveOwnerRelease(entry.ActorId, entry.Endpoints, entry.FlightFlags, entry.Source, entry.Time, card)' in a, 'Read-only local boards need exact native completion provenance'
assert 'PlayMirroredCardFlight(entry.Endpoints, entry.FlightFlags, entry.Source, entry.Time, PlayerId, card)' in a, 'Original board dispatch carries exact original, clock and peer'
assert 'ConsumesWireEvent(endpoints, flags, source, completionTime, presentationPlayer, originalCard)' in a, 'Burn ownership receives exact completion identity'
b=(r/'src/GloomhavenVR/Net/Avatar/NetAvatarDriver.CardAppearance.cs').read_text()
assert 'viewer.PlayMirroredCardFlight(endpoints, flags, source, completionTime, sender.PlayerId, originalCard)' in b, 'Foreign-focus boards retain the actual appearance publisher'
hook=(r/'src/GloomhavenVR/Cards/BurnArtwork.cs').read_text()
assert 'if (burnAnim && running) Net.CardAppearanceSampler.ObserveNativeBurnStart(__instance);' in hook, 'Native progress must be registered at the actual first running burn step'
assert 'bool firstStep = true;' in hook and 'firstStep = false;' in hook, 'Native progress registration is once per iterator'
layout=(r/'src/GloomhavenVR/Cards/Driver/CardsDriver.8.BurnSequencing.cs').read_text()
assert layout.index('ObserveForeignBurnProgress(widget);') < layout.index('FlushBurnHolds('), 'Progress must be observed while the native layout barrier still blocks Flush'
retire=(r/'src/GloomhavenVR/Cards/Driver/CardsDriver.4.Rebuild.cs').read_text()
assert 'Net.BurnReleasePolicy.RetireWithoutFlight(witnessedOriginal || durableNoFlight,' in retire and 'ClearBurnHold(widget); _activeExitOrigins.Remove(widget); _knownBurntWidgets.Add(widget);' in retire, 'No-flight completion retires exact local holds instead of fabricating a flight'
cleanup=(r/'src/GloomhavenVR/Cards/Driver/CardsDriver.2.Update.cs').read_text()
assert '_foreignBurnProgress.Clear();' in cleanup, 'Progress observations cannot retain old native widgets after teardown'
print('Burn completion production binding: sampler, legacy/history admission and canonical observer dispatch verified.')
PYBIND
dotnet run --project "$project" -c Release --property:SamplerSource="$source"
work_dir=$(mktemp -d)
trap 'rm -rf "$work_dir"' EXIT
for mutation in lost-final duplicate-live stale-address early-clock recovered-burn; do
  python3 - "$source" "$work_dir/Sampler.cs" "$mutation" <<'PY'
from pathlib import Path
import sys
s=Path(sys.argv[1]).read_text()
a,b={
'lost-final':('states.Add(final.State);','{}'),
'duplicate-live':('if (present) continue;','if (present) { }'),
'stale-address':('var relocated = final.State.Copy();','var relocated = final.State;'),
'early-clock':('return Time.unscaledTime;','return Time.unscaledTime - 1f;'),
'recovered-burn':('|| final.Actor.CharacterClass.HandAbilityCards.Contains(final.Card)','')
}[sys.argv[3]]
assert s.count(a)==1
Path(sys.argv[2]).write_text(s.replace(a,b))
PY
  case "$mutation" in
    lost-final) expected='Completed native pixels survive departure of the visible wrapper';;
    duplicate-live) expected='Visible original wins over its retained completion without duplicate address';;
    stale-address) expected='Address refresh must not mutate previously published snapshots';;
    early-clock) expected='Completion reports the actual owner appearance clock';;
    recovered-burn) expected='Recovery retires old burn completion before card reuse';;
  esac
  if dotnet run --project "$project" -c Release --property:SamplerSource="$work_dir/Sampler.cs" > "$work_dir/output" 2>&1; then
    cat "$work_dir/output"; echo "FAIL: $mutation survived" >&2; exit 1
  fi
  if ! rg -Fq "Unhandled exception. System.Exception: $expected" "$work_dir/output"; then
    cat "$work_dir/output"; exit 1
  fi
  echo "Burn completion negative control: $mutation rejected."
done

for mutation in proxy-progress stopped-progress early-progress-clear noflight-retention noflight-recovery normal-flight-as-noflight; do
  python3 - "$repo_root/src/GloomhavenVR/Net/CardAppearanceSampler.Progress.cs" "$work_dir/Progress.cs" "$mutation" <<'PYPROGRESS'
from pathlib import Path
import sys
s=Path(sys.argv[1]).read_text()
a,b={
'noflight-retention':('!tracked.ExpectedFlight && NetCardFx.CompleteBurnWithoutFlight', '!tracked.ExpectedFlight && bool.Parse("false") && NetCardFx.CompleteBurnWithoutFlight'),
'noflight-recovery':('if (cards.HandAbilityCards.Contains(tracked.Card) || cards.RoundAbilityCards.Contains(tracked.Card)', 'if (bool.Parse("false") || cards.RoundAbilityCards.Contains(tracked.Card)'),
'normal-flight-as-noflight':('!tracked.ExpectedFlight && NetCardFx.CompleteBurnWithoutFlight', 'NetCardFx.CompleteBurnWithoutFlight'),
'early-progress-clear':('if (running || expected) continue;', 'if (running) continue;'),
'proxy-progress':('if (!CardsGameApi.ControlsActor(actor)) return;', ''),
'stopped-progress':('bool running = BurnArtwork.Playing(BurnArtwork.EffectsOf(tracked.Full))', 'bool running = bool.Parse("true") || BurnArtwork.Playing(BurnArtwork.EffectsOf(tracked.Full))')
}[sys.argv[3]]
assert s.count(a)==1
Path(sys.argv[2]).write_text(s.replace(a,b))
PYPROGRESS
  case "$mutation" in
    noflight-retention) expected='Offscreen completion survives even when every in-progress snapshot was coalesced away';;
    noflight-recovery) expected='Actual model recovery retires durable no-flight completion';;
    normal-flight-as-noflight) expected='A normal adopted burn must not be reclassified as an offscreen no-flight completion';;
    early-progress-clear) expected='A normal visible burn keeps progress between native completion and its real flight report';;
    proxy-progress) expected='A read-only proxy cannot publish owner burn progress';;
    stopped-progress) expected='Actual native completion clears an unadopted incoming burn without a flight';;
  esac
  if dotnet run --project "$project" -c Release --property:SamplerSource="$source" --property:ProgressSource="$work_dir/Progress.cs" > "$work_dir/output" 2>&1; then
    cat "$work_dir/output"; echo "FAIL: $mutation survived" >&2; exit 1
  fi
  if ! rg -Fq "Unhandled exception. System.Exception: $expected" "$work_dir/output"; then
    cat "$work_dir/output"; exit 1
  fi
  echo "Burn completion negative control: $mutation rejected."
done
