#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
work_dir=$(mktemp -d)
trap 'rm -rf "$work_dir"' EXIT
cp "$repo_root/tests/GloomhavenVR.RemoteBurnSequencingTests/"*.cs "$repo_root/tests/GloomhavenVR.RemoteBurnSequencingTests/"*.csproj "$work_dir/"
python3 "$repo_root/tests/GloomhavenVR.RemoteBurnSequencingTests/extract.py" "$repo_root" "$work_dir/Production.cs"
dotnet run --project "$work_dir/GloomhavenVR.RemoteBurnSequencingTests.csproj" --configuration Release
for mutation in active-layout recess-layout early-native wrong-provenance release-bypass discovery-order following-flight recovery-address pending-actor initial-history duplicate-terminal incoming-progress progress-is-release retire-with-release retire-before-native freeze-running lost-ui-gap lost-seed lost-membership clear-burn-output; do
  mkdir -p "$work_dir/source/src/GloomhavenVR/Net/Remote"
  cp "$repo_root/src/GloomhavenVR/Net/Remote/"*.cs "$work_dir/source/src/GloomhavenVR/Net/Remote/"
  cp "$repo_root/src/GloomhavenVR/Net/BurnReleasePolicy.cs" "$work_dir/source/src/GloomhavenVR/Net/"
  cp "$repo_root/src/GloomhavenVR/Net/CardAppearanceMirror.cs" "$work_dir/source/src/GloomhavenVR/Net/"
  python3 - "$work_dir/source/src/GloomhavenVR/Net" "$mutation" <<'PY'
import pathlib,sys
root=pathlib.Path(sys.argv[1]); mutation=sys.argv[2]
file,old,new={
'lost-ui-gap':('Remote/RemoteBurnFx.cs','        _pruneScratch.Clear();\n        foreach (CAbilityCard card in _known)','        _known.Clear();\n        _pruneScratch.Clear();\n        foreach (CAbilityCard card in _known)'),
'lost-seed':('Remote/RemoteBurnFx.cs','            foreach (var card in cards.PermanentlyLostAbilityCards) _known.Add(card);',''),
'lost-membership':('Remote/RemoteBurnFx.cs','                || (!cards.LostAbilityCards.Contains(card) && !cards.PermanentlyLostAbilityCards.Contains(card))',''),
'clear-burn-output':('Remote/RemoteCardArt.Native.cs','            if (_nativeOutputApplied && RetainNativeOutputDuringBurn()) return;','            if (bool.Parse(\"false\") && _nativeOutputApplied && RetainNativeOutputDuringBurn()) return;'),
'retire-with-release':('BurnReleasePolicy.cs',' && !hasOwnerRelease',''),
'retire-before-native':('BurnReleasePolicy.cs',' && !nativePlaying',''),
'initial-history':('Remote/RemoteAvatar.cs','if ((initial || _burnProgressKeys.ContainsKey(entry.Key)) && (card == null || !_burnFx.HasObservedBurn(card)))','if (bool.Parse("false") && (initial || _burnProgressKeys.ContainsKey(entry.Key)) && (card == null || !_burnFx.HasObservedBurn(card)))'),
'incoming-progress':('Remote/RemoteBurnFx.cs','if (IncomingBurnPending) return RemoteBoardFocus.ActorById(_watchActor);',''),
'progress-is-release':('Remote/RemoteAvatar.cs','if (entry.InProgress || entry.NoFlightCompleted) continue;',''),
'duplicate-terminal':('Remote/RemoteAvatar.cs','if (_burnCompletionTimes.TryGetValue(entry.Key, out float seen) && seen >= entry.Time) continue;',''),
'active-layout':('Remote/RemoteActiveCards.cs','if (_owner.HoldsBurnCardLayout)','if (bool.Parse("false") && _owner.HoldsBurnCardLayout)'),
'recess-layout':('Remote/RemoteControlBoard.cs','if (_owner.HoldsBurnCardLayout && ReferenceEquals(actor, _latchedActor))','if (bool.Parse("false") && _owner.HoldsBurnCardLayout && ReferenceEquals(actor, _latchedActor))'),
'early-native':('CardAppearanceMirror.cs','progress >= 1f','progress >= 0f'),
'wrong-provenance':('CardAppearanceMirror.cs','!ReferenceEquals(card, CardAppearanceProvenance.Resolve(current))','bool.Parse("false") && !ReferenceEquals(card, CardAppearanceProvenance.Resolve(current))'),
'release-bypass':('Remote/RemoteBurnFx.cs','burn.OwnerReleased = true;','burn.OwnerReleased = true; Handover(burn, "early");'),
'recovery-address':('CardAppearanceMirror.cs','(NetProtocol.HeldFaceList(state.FaceCode) == NetProtocol.HeldFaceListHand','(bool.Parse("true") || NetProtocol.HeldFaceList(state.FaceCode) == NetProtocol.HeldFaceListHand'),
'pending-actor':('Remote/RemoteBurnFx.cs','pending.ActorId != _watchActor','bool.Parse("false") && pending.ActorId != _watchActor'),
'freeze-running':('Remote/RemoteCardFx.cs','        // Already launched flights keep moving, just like their local VRCard counterparts.','        if (_owner.HoldsBurnCardLayout) return;'),
'following-flight':('Remote/RemoteCardFx.cs','        if (_owner.HoldsBurnCardLayout) return false;',''),
'discovery-order':('Remote/RemoteAvatar.cs','        _burnFx.PreparePresentation();',''),
}[mutation]
p=root/file;s=p.read_text();assert old in s;p.write_text(s.replace(old,new))
PY
  if python3 "$repo_root/tests/GloomhavenVR.RemoteBurnSequencingTests/extract.py" "$work_dir/source" "$work_dir/Production.cs" > "$work_dir/mutant.log" 2>&1; then
    if dotnet run --project "$work_dir/GloomhavenVR.RemoteBurnSequencingTests.csproj" --configuration Release >> "$work_dir/mutant.log" 2>&1; then
      cat "$work_dir/mutant.log"; echo "FAIL: $mutation escaped" >&2; exit 1
    fi
  fi
  case "$mutation" in
    lost-ui-gap) expected='Pile rebuild must not replay an original burn';;
    lost-seed) expected='Historical originals seed even before their UI exists';;
    lost-membership) expected='Stale lost UI cannot burn a recovered original';;
    clear-burn-output) expected='Missing lost-list samples must not clear a painted burn to blue';;
    retire-with-release) expected='An actual terminal release must retain its native flight path';;
    retire-before-native) expected='No-flight retirement still waits for native presentation completion';;
    incoming-progress) expected='Explicit owner progress retains the previous card actor during an incoming switch';;
    progress-is-release) expected='Owner progress must never dispatch a completed burn or flight';;
    initial-history) expected='Joining after historical losses must not create an orphan burn claim';;
    duplicate-terminal) expected='Adopted historical terminal state stays inert on repetition';;
    active-layout) expected='Pending burn must not compact the active grid';;
    recess-layout) expected='Pending burn must not replace the first recess';;
    early-native) expected='Release must wait while native interpolation still contains pre-completion output';;
    wrong-provenance) expected='Same seat or id cannot substitute another original card';;
    release-bypass) expected='Receiving release must not bypass native playback';;
    discovery-order) expected='substring not found';;
    following-flight) expected='Following flights must wait for the earlier native burn';;
    recovery-address) expected='Event before model holds the prior actor without treating old Round membership as recovery';;
    freeze-running) expected='Already launched remote flights keep moving during a later burn';;
    pending-actor) expected='Unrelated focus must never be retargeted by a pending release';;
  esac
  if ! grep -Fq "$expected" "$work_dir/mutant.log"; then cat "$work_dir/mutant.log"; exit 1; fi
  echo "Remote burn sequencing negative control: $mutation failed as expected."
done
