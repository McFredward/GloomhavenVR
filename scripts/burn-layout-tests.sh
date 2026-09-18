#!/usr/bin/env bash
set -euo pipefail
repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
work_dir=$(mktemp -d)
trap 'rm -rf "$work_dir"' EXIT
cp "$repo_root/tests/GloomhavenVR.BurnLayoutTests/"*.cs "$repo_root/tests/GloomhavenVR.BurnLayoutTests/"*.csproj "$work_dir/"
python3 "$repo_root/tests/GloomhavenVR.BurnLayoutTests/extract.py" "$repo_root" "$work_dir/Production.cs"
dotnet run --project "$work_dir/GloomhavenVR.BurnLayoutTests.csproj" --configuration Release
for mutation in early-layout missed-model missed-native no-retry last-card-only missed-incoming historical-incoming missed-foreign missed-progress forgotten-model lost-is-recovered missed-recovery duplicate-original no-native-baseline completed-is-historical missed-completed-recovery; do
    cp "$work_dir/Production.cs" "$work_dir/original.txt"
    python3 - "$work_dir/Production.cs" "$mutation" <<'PY'
import pathlib,sys
p=pathlib.Path(sys.argv[1]);s=p.read_text()
old,new={
'completed-is-historical':('_completedBurnClaims.Contains(widget.AbilityCard)', '_knownBurntCards.Contains(widget.AbilityCard)'),
'missed-completed-recovery':('_completedBurnClaims.Remove(card);', '// omitted completed recovery'),
'forgotten-model':('widget.AbilityCard != null && _knownBurntCards.Contains(widget.AbilityCard)', 'false'),
'lost-is-recovered':('!character.LostAbilityCards.Contains(card)', 'true'),
'missed-recovery':('_knownBurntCards.Remove(card);', '// omitted recovery'),
'duplicate-original':('&& ReferenceEquals(held.AbilityCard, widget.AbilityCard)', '&& ReferenceEquals(held, widget)'),
'no-native-baseline':('foreach (CAbilityCard card in character.LostAbilityCards) _knownBurntCards.Add(card);', '// omitted baseline'),
'early-layout':('if (DeferLayoutForBurn()) return;','if (false) return;'),
'missed-model':('if (card.IsHeld || !IsFreshBurn(_boundHand, card)','if (true || card.IsHeld || !IsFreshBurn(_boundHand, card)'),
'missed-native':('_burnLayoutNativeActive |= artworkPlaying;', '_burnLayoutNativeActive |= false;'),
'no-retry':('_dirty = true;', '_dirty = false;'),
'missed-incoming':('bool incomingActive = IncomingHandBurnActive();', 'bool incomingActive = false;'),
'missed-progress':('Net.CardAppearanceSampler.ObserveBurnProgress(widget);', ''),
'missed-foreign':('Net.CardAppearanceMirror.OwnerBurnInProgress(incoming.PlayerActor)', 'false'),
'historical-incoming':('active |= BurnArtwork.Playing(BurnArtwork.EffectsOf(widget));', 'active |= widget.AbilityCard != null;'),
'last-card-only':('_burnLayoutNativeActive |=', '_burnLayoutNativeActive ='),
}[sys.argv[2]]
assert old in s;p.write_text(s.replace(old,new).replace('if (false)', 'if (bool.Parse("false"))').replace('if (true ||', 'if (bool.Parse("true") ||'))
PY
    if dotnet run --project "$work_dir/GloomhavenVR.BurnLayoutTests.csproj" --configuration Release > "$work_dir/negative.log" 2>&1; then cat "$work_dir/negative.log"; exit 1; fi
    case "$mutation" in
        completed-is-historical) expected='Initial historical seeding must not suppress a first round or active burn handover';;
        missed-completed-recovery) expected='Real recovery must also clear completed round or active flight claims';;
        forgotten-model) expected='Already-burnt browse cards must not restart a hold';;
        lost-is-recovered) expected='Replacing a completed lost widget must not replay its original burn';;
        missed-recovery) expected='Authoritative recovery must re-arm a real later burn';;
        duplicate-original) expected='Two widgets for one original must share one pending burn hold';;
        no-native-baseline) expected='Historical native loss with no initial widget must not become a fresh burn on later UI creation';;
        early-layout|missed-model) expected='Model-first loss must retain the outgoing layout';;
        missed-native) expected='Live burn must never admit sibling movement or replacement';;
        no-retry) expected='Retained card input must wait and rebuild must remain scheduled';;
        missed-incoming) expected='Incoming native burn must wait before replacing the outgoing character';;
        missed-progress) expected='Every incoming original widget must publish its owner progress before adoption';;
        missed-foreign) expected='Foreign incoming owner progress must retain the admitted local view when its native proxy is inactive';;
        historical-incoming) expected='Completed incoming burns must admit focus without replaying a historical loss';;
        last-card-only) expected='Live burn must never admit sibling movement or replacement';;
    esac
    if ! grep -Fq "$expected" "$work_dir/negative.log"; then cat "$work_dir/negative.log"; exit 1; fi
    mv "$work_dir/original.txt" "$work_dir/Production.cs"
    echo "Burn layout negative control: $mutation failed as expected."
done
