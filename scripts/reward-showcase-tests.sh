#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$repo_root/tests/GloomhavenVR.RewardShowcaseTests/GloomhavenVR.RewardShowcaseTests.csproj"
# Overrides let isolated review worktrees test another lane before integration; hosted
# and normal local gates always use their own committed production files.
continue_button_source="$repo_root/src/GloomhavenVR/WorldUI/Modal/RewardContinueButton.cs"
reward_source="${REWARD_SHOWCASE_SOURCE:-$repo_root/src/GloomhavenVR/WorldUI/Modal/RewardShowcase.cs}"
identity_source="${REWARD_IDENTITY_SOURCE:-$repo_root/src/GloomhavenVR/WorldUI/Modal/RewardShowcaseIdentity.cs}"
placement_source="${REWARD_PLACEMENT_SOURCE:-$repo_root/src/GloomhavenVR/WorldUI/Modal/RewardShowcasePlacement.cs}"
map_buttons_source="$repo_root/src/GloomhavenVR/WorldUI/Modal/RewardShowcaseMapButtons.cs"
poll_source="${REWARD_POLL_SOURCE:-$repo_root/src/GloomhavenVR/WorldUI/Modal/ModalFallback.10.CatchAll.cs}"
conversion_source="${REWARD_CONVERSION_SOURCE:-$repo_root/src/GloomhavenVR/WorldUI/Modal/ModalFallback.8.Convert.cs}"
lifecycle_source="${REWARD_LIFECYCLE_SOURCE:-$repo_root/src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.4.Lifecycle.cs}"
window_panel_source="${REWARD_WINDOW_PANEL_SOURCE:-$repo_root/src/GloomhavenVR/WorldUI/Modal/ModalFallback.3.WindowPanel.cs}"
screen_bind_source="${REWARD_SCREEN_BIND_SOURCE:-$repo_root/src/GloomhavenVR/WorldUI/Modal/ModalFallback.12.ScreenBind.cs}"
close_source="${REWARD_CLOSE_SOURCE:-$repo_root/src/GloomhavenVR/WorldUI/Modal/ModalFallback.7.Close.cs}"
mutation_dir="$(mktemp -d)"
trap 'rm -rf "$mutation_dir"' EXIT
python3 - "$poll_source" "$conversion_source" "$lifecycle_source" "$window_panel_source" "$screen_bind_source" "$mutation_dir" "$close_source" <<'PY'
import pathlib, re, sys
poll, conversion, lifecycle, window_panel, screen_bind, out, close = map(pathlib.Path, sys.argv[1:])
source = poll.read_text()
start = source.index('    private static void AddRewardShowcaseWindow(bool inScenario)')
end = source.index('\n    // ===', start)
def expression(path, declaration):
    matches = re.findall(re.escape(declaration) + r'\s*=>[^;]+;', path.read_text())
    assert len(matches) == 1, declaration
    return matches[0]
availability = expression(window_panel, 'internal static bool RewardPlacementFailed(UIWindow? window)')
conversion_policy = expression(screen_bind, 'private static bool ConvertBaseActive')
dismiss_source = close.read_text()
dismiss_start = dismiss_source.index('    private static void DismissTransient(UIWindow window)')
dismiss_end = dismiss_source.index('    /// <summary>Scratch for the dismiss-target', dismiss_start)
dismiss = dismiss_source[dismiss_start:dismiss_end]
fixture = 'using System;\nusing System.Collections.Generic;\nusing GloomhavenVR.Core;\nusing UnityEngine.EventSystems;\nusing UnityEngine.UI;\nnamespace GloomhavenVR.WorldUI;\ninternal static partial class ModalFallback\n{\n' + source[start:end] + '\n' + availability + '\n' + conversion_policy + '\n' + dismiss + '\nprivate static readonly List<Button> TransientButtonScratch = new();\n}\n'
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
native_source="$repo_root/decompiled/GH.Runtime/UIRewardsManager.cs"
if [[ -f "$native_source" ]]; then
    python3 - "$native_source" "$repo_root/tests/GloomhavenVR.RewardShowcaseTests/NativeRewardProcess.cs" <<'PYCOMPARE'
import pathlib, sys
a, b = (pathlib.Path(p).read_text() for p in sys.argv[1:])
start = a.index('\tprivate IEnumerator<float> ProcessRewards(')
end = a.index('\n\tprivate void InstanceOnEscMenuStateChanged', start)
assert a[start:end] in b, 'Native reward coroutine fixture diverged from the game reference'
PYCOMPARE
fi
if [[ -f "$repo_root/decompiled/GH.Runtime/UIGuildmasterAdventureRewardsManager.cs" ]]; then
    python3 - "$repo_root" <<'PYMAP'
import pathlib, sys
root = pathlib.Path(sys.argv[1])
fixture = (root / 'tests/GloomhavenVR.RewardShowcaseTests/NativeMapRewardButtons.cs').read_text()
for name, start, end in (
    ('UIGuildmasterAdventureRewardsManager', '\tpublic void Hide()', '\n\tprivate void OnDisable()'),
    ('UIUnlockLocationFlowManager', '\tpublic void Continue()', '\n\tprivate ICallbackPromise Focus(MapLocation')):
    native = (root / 'decompiled/GH.Runtime' / (name + '.cs')).read_text()
    assert native[native.index(start):native.index(end)] in fixture, name + ' native continuation fixture diverged'
PYMAP
fi
dotnet run --project "$project" --configuration Release \
    --property:RepositorySourceRoot="$repo_root/src/GloomhavenVR" \
    --property:ContinueButtonSource="$continue_button_source" \
    --property:RewardSource="$reward_source" \
    --property:IdentitySource="$identity_source" \
    --property:PlacementSource="$placement_source" \
    --property:MapButtonsSource="$map_buttons_source" \
    --property:CatchAllSource="$mutation_dir/RewardPoll.fixture"
cp "$repo_root/tests/GloomhavenVR.RewardShowcaseTests/"*.cs "$mutation_dir/"
cp "$project" "$mutation_dir/"
for mutation in missing-listener duplicate-listener reveal authority native-input gamepad-adapter map-scenario-gate hover-state identity-block placement-key poll-binding premature-reveal pending-pose screen-unavailable map-reward-binding map-location-binding map-duplicate-binding unlock-target postquest-reveal postquest-duplicate postquest-success intro-focus intro-block postquest-pose; do
    python3 - "$reward_source" "$identity_source" "$placement_source" "$mutation_dir" "$mutation" "$continue_button_source" "$map_buttons_source" <<'PY'
import pathlib, sys
reward, identity, placement, out = map(pathlib.Path, sys.argv[1:5])
sources = {'Reward': reward.read_text(), 'Identity': identity.read_text(),
           'PostQuest': (reward.parent / 'PostQuestRewardSync.cs').read_text(),
           'Introduction': (reward.parent / 'PostQuestRewardSync.Introduction.cs').read_text(),
           'MapButtons': pathlib.Path(sys.argv[7]).read_text(), 'Placement': placement.read_text(), 'Continue': pathlib.Path(sys.argv[6]).read_text(), 'Poll': (out / 'RewardPoll.fixture').read_text()}
mutations = {
    'postquest-pose': ('PostQuest', 'Ledger.MatchesPose(_opening, sender, localPlayerId, entry.Epoch, entry.Token)', 'true'),
    'postquest-reveal': ('PostQuest', '!window.isRevealing', 'true'),
    'postquest-duplicate': ('PostQuest', '_consumed = true;', '_consumed = false;'),
    'postquest-success': ('PostQuest', 'else _campaign!.rewardsWindow.OnContinueButtonClick();', 'else { NativeSucceeded(opening); _campaign!.rewardsWindow.OnContinueButtonClick(); }'),
    'intro-focus': ('Introduction', 'Time.frameCount - layout._focusedFrame < 2', 'false'),
    'intro-block': ('PostQuest', '|| IntroductionOpen', '|| false'),
    'unlock-target': ('Poll', '!ReferenceEquals(b, expected)', 'false'),
    'map-reward-binding': ('MapButtons', 'adventure.closeButton.onClick.AddListener(ConfirmAdventureRewards);', '{}'),
    'map-location-binding': ('MapButtons', 'locations.continueButton.onClick.AddListener(locations.Continue);', '{}'),
    'map-duplicate-binding': ('MapButtons', 'adventure.closeButton.onClick.RemoveListener(adventure.Hide);', '{}'),
    'missing-listener': ('Reward', 'rewards.continueButton.onClick.AddListener(rewards.OnContinueButtonClick);', '{}'),
    'duplicate-listener': ('Reward', 'rewards.continueButton.onClick.RemoveListener(rewards.OnContinueButtonClick);', '{}'),
    'reveal': ('Reward', '!rewards.isRevealing', 'true'),
    'authority': ('Reward', 'guild.interactionChecker != null ? guild.interactionChecker() : !FFSNetwork.IsClient', 'true'),
    'native-input': ('Reward', 'guild.isConfirmPressed = true;', 'guild.MoveToNextReward();'),
    'hover-state': ('Continue', 'SelectionState.Highlighted or SelectionState.Selected => NativeButtonSkin.FaceState.Accent,', 'SelectionState.Highlighted or SelectionState.Selected => NativeButtonSkin.FaceState.Idle,'),
    'map-scenario-gate': ('Reward', 'UIWindow? campaignWindow = CampaignWindow?.window;', 'if (Manager == null || !Manager.IsShown) return null; UIWindow? campaignWindow = CampaignWindow?.window;'),
    'gamepad-adapter': ('Reward', 'guild.isConfirmPressed = true;', 'guild.ConfirmPressed();'),
    'identity-block': ('Identity', '!choreographer.m_BlockClientMessageProcessing', 'false'),
    'placement-key': ('Placement', 'RewardShowcaseIdentity.ContentKey(window) != _key', 'false'),
    'poll-binding': ('Poll', 'RewardShowcase.Tick(inScenario && WorldUIConfig.ConversionActive);', 'RewardShowcase.Tick(false);'),
    'premature-reveal': ('Placement', 'return _mayReveal;', 'return _mayReveal || true;'),
    'pending-pose': ('Placement', '_poseReady && ModalFallback.TryGetGrabFor', '(_poseReady || true) && ModalFallback.TryGetGrabFor'),
    'screen-unavailable': ('Poll', '!ConvertBaseActive || Failed.Contains(window)', '(!ConvertBaseActive && false) || Failed.Contains(window)'),
}
part, before, after = mutations[sys.argv[5]]
assert sources[part].count(before) == 1, (part, before)
sources[part] = sources[part].replace(before, after)
for name, text in sources.items():
    (out / (name + '.mutant')).write_text(text)
PY
    case "$mutation" in
        postquest-pose) expected='stale pose cannot bind repeated native reward group' ;;
        postquest-reveal) expected='remote postquest completion waits for native reward reveal' ;;
        postquest-duplicate) expected='repeated remote and local postquest input cannot double callback' ;;
        postquest-success) expected='failed native callback never publishes completion' ;;
        intro-focus) expected='remote hint uses native button callback before exposing reward continuation' ;;
        intro-block) expected='original reward cannot close through its still-active introduction' ;;
        unlock-target) expected='transient body click cannot choose an unrelated active button while native Continue is disabled' ;;
        map-reward-binding) expected='map reward gets exactly one native listener plus unrelated callback' ;;
        map-duplicate-binding) expected='conversion reactivation neither loses nor duplicates original map callbacks' ;;
        map-location-binding) expected='unlock location gets exactly one native listener' ;;
        missing-listener) expected='gamepad-created campaign button receives one native listener' ;;
        duplicate-listener) expected='campaign close and reopen retain exactly one native binding' ;;
        reveal) expected='campaign reveal animation gates input' ;;
        authority) expected='shared observer cannot enqueue native reward continuation' ;;
        native-input) expected='Presentation must not bypass native reward input' ;;
        hover-state) expected='pointer hover paints native highlighted sprite and tint' ;;
        map-scenario-gate) expected='map event reward receives its VR Continue without scenario manager' ;;
        gamepad-adapter) expected='explicit VR input works in tutorial and guild modes without physical gamepad edge' ;;
        identity-block) expected='unblocked stale activation cannot identify current reward' ;;
        placement-key) expected='pose for previous chest cannot move next chest reward' ;;
        poll-binding) expected='gamepad-created campaign button receives one native listener' ;;
        premature-reveal) expected='follower cannot reveal its local seat before the elected pose arrives' ;;
        pending-pose) expected='unsettled elected source pose cannot release follower first reveal' ;;
        screen-unavailable) expected='manual desktop reward source is unavailable for shared floating placement' ;;
    esac
    if dotnet run --project "$mutation_dir/GloomhavenVR.RewardShowcaseTests.csproj" --configuration Release \
        --property:RepositorySourceRoot="$repo_root/src/GloomhavenVR" \
    --property:ContinueButtonSource="$mutation_dir/Continue.mutant" \
        --property:PostQuestSource="$mutation_dir/PostQuest.mutant" \
        --property:IntroductionSource="$mutation_dir/Introduction.mutant" \
        --property:RewardSource="$mutation_dir/Reward.mutant" \
        --property:IdentitySource="$mutation_dir/Identity.mutant" \
        --property:PlacementSource="$mutation_dir/Placement.mutant" \
        --property:MapButtonsSource="$mutation_dir/MapButtons.mutant" \
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
