#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$repo_root/tests/GloomhavenVR.RewardShowcaseTests/GloomhavenVR.RewardShowcaseTests.csproj"
# Overrides let isolated review worktrees test another lane before integration; hosted
# and normal local gates always use their own committed production files.
reward_source="${REWARD_SHOWCASE_SOURCE:-$repo_root/src/GloomhavenVR/WorldUI/Modal/RewardShowcase.cs}"
identity_source="${REWARD_IDENTITY_SOURCE:-$repo_root/src/GloomhavenVR/WorldUI/Modal/RewardShowcaseIdentity.cs}"
placement_source="${REWARD_PLACEMENT_SOURCE:-$repo_root/src/GloomhavenVR/WorldUI/Modal/RewardShowcasePlacement.cs}"
poll_source="${REWARD_POLL_SOURCE:-$repo_root/src/GloomhavenVR/WorldUI/Modal/ModalFallback.10.CatchAll.cs}"
conversion_source="${REWARD_CONVERSION_SOURCE:-$repo_root/src/GloomhavenVR/WorldUI/Modal/ModalFallback.8.Convert.cs}"
lifecycle_source="${REWARD_LIFECYCLE_SOURCE:-$repo_root/src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.4.Lifecycle.cs}"
mutation_dir="$(mktemp -d)"
trap 'rm -rf "$mutation_dir"' EXIT
python3 - "$poll_source" "$conversion_source" "$lifecycle_source" "$mutation_dir" <<'PY'
import pathlib, sys
poll, conversion, lifecycle, out = map(pathlib.Path, sys.argv[1:])
source = poll.read_text()
start = source.index('    private static void AddRewardShowcaseWindow(bool inScenario)')
end = source.index('\n    // ===', start)
fixture = 'using UnityEngine.UI;\nnamespace GloomhavenVR.WorldUI;\ninternal static partial class ModalFallback\n{\n' + source[start:end] + '\n}\n'
(out / 'RewardPoll.fixture').write_text(fixture)
# The actual production enrollment is executed above, not reproduced in the harness.
# Keep the surrounding teardown and before-reveal binding connected as well.
reset = source[source.index('    private static void CatchAllReset()'):start]
assert 'RewardShowcase.Tick(false);' in reset
conversion = conversion.read_text()
placement = conversion.index('if (RewardShowcasePlacement.ApplyInitialPose(window, grab))')
registration = conversion.index('Converted.Add(wp);', placement)
assert 'wp.SpawnAnchor = default;' in conversion[placement:registration]
assert 'wp.PoseRePlaceDone = true;' in conversion[placement:registration]
lifecycle = lifecycle.read_text()
reveal = lifecycle[lifecycle.index('    private static void CompleteReveal(ConvertedPanel panel)'):]
guard = reveal.index('if (!RewardShowcasePlacement.TryReveal(panel))')
visible = reveal.index('SetPanelRenderVisible(panel, visible: true,')
assert guard < visible
hold = reveal[guard:reveal.index('\n        //', guard)]
assert 'SetPanelRenderVisible(panel, visible: false);' in hold and 'return;' in hold
PY
dotnet run --project "$project" --configuration Release \
    --property:RewardSource="$reward_source" \
    --property:IdentitySource="$identity_source" \
    --property:PlacementSource="$placement_source" \
    --property:CatchAllSource="$mutation_dir/RewardPoll.fixture"
cp "$repo_root/tests/GloomhavenVR.RewardShowcaseTests/"*.cs "$mutation_dir/"
cp "$project" "$mutation_dir/"
for mutation in missing-listener duplicate-listener reveal authority native-input identity-block placement-key poll-binding premature-reveal pending-pose; do
    python3 - "$reward_source" "$identity_source" "$placement_source" "$mutation_dir" "$mutation" <<'PY'
import pathlib, sys
reward, identity, placement, out = map(pathlib.Path, sys.argv[1:5])
sources = {'Reward': reward.read_text(), 'Identity': identity.read_text(),
           'Placement': placement.read_text(), 'Poll': (out / 'RewardPoll.fixture').read_text()}
mutations = {
    'missing-listener': ('Reward', 'rewards.continueButton.onClick.AddListener(rewards.OnContinueButtonClick);', '{}'),
    'duplicate-listener': ('Reward', 'rewards.continueButton.onClick.RemoveListener(rewards.OnContinueButtonClick);', '{}'),
    'reveal': ('Reward', '!rewards.isRevealing', 'true'),
    'authority': ('Reward', 'guild.interactionChecker != null ? guild.interactionChecker() : !FFSNetwork.IsClient', 'true'),
    'native-input': ('Reward', 'Guildmaster!.ConfirmPressed();', 'Guildmaster!.MoveToNextReward();'),
    'identity-block': ('Identity', '!choreographer.m_BlockClientMessageProcessing', 'false'),
    'placement-key': ('Placement', 'RewardShowcaseIdentity.ContentKey(window) != _key', 'false'),
    'poll-binding': ('Poll', 'RewardShowcase.Tick(inScenario && WorldUIConfig.ConversionActive);', 'RewardShowcase.Tick(false);'),
    'premature-reveal': ('Placement', 'return _mayReveal;', 'return _mayReveal || true;'),
    'pending-pose': ('Placement', '_poseReady && ModalFallback.TryGetGrabFor', '(_poseReady || true) && ModalFallback.TryGetGrabFor'),
}
part, before, after = mutations[sys.argv[5]]
assert sources[part].count(before) == 1, (part, before)
sources[part] = sources[part].replace(before, after)
for name, text in sources.items():
    (out / (name + '.mutant')).write_text(text)
PY
    case "$mutation" in
        missing-listener) expected='gamepad-created campaign button receives one native listener' ;;
        duplicate-listener) expected='campaign close and reopen retain exactly one native binding' ;;
        reveal) expected='campaign reveal animation gates input' ;;
        authority) expected='guild authority matches native processing predicate' ;;
        native-input) expected='Presentation must not bypass native reward input' ;;
        identity-block) expected='unblocked stale activation cannot identify current reward' ;;
        placement-key) expected='pose for previous chest cannot move next chest reward' ;;
        poll-binding) expected='gamepad-created campaign button receives one native listener' ;;
        premature-reveal) expected='follower cannot reveal its local seat before the elected pose arrives' ;;
        pending-pose) expected='unsettled elected source pose cannot release follower first reveal' ;;
    esac
    if dotnet run --project "$mutation_dir/GloomhavenVR.RewardShowcaseTests.csproj" --configuration Release \
        --property:RewardSource="$mutation_dir/Reward.mutant" \
        --property:IdentitySource="$mutation_dir/Identity.mutant" \
        --property:PlacementSource="$mutation_dir/Placement.mutant" \
        --property:CatchAllSource="$mutation_dir/Poll.mutant" > "$mutation_dir/mutant.log" 2>&1; then
        cat "$mutation_dir/mutant.log"
        echo "FAIL: $mutation escaped reward showcase test." >&2
        exit 1
    fi
    if ! rg -qF "$expected" "$mutation_dir/mutant.log"; then
        cat "$mutation_dir/mutant.log"
        exit 1
    fi
    echo "Reward showcase negative control: $mutation rejected."
done
