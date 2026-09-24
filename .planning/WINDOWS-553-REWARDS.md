# Reward and post-quest continuation review — dev build 553

Base: `e63fb284` (dev 1.0.7 / ModBuild 546). This lane contains no NPC changes.

## Evidence limits

The current input is a player's description of several unclosable post-quest windows,
including an NPC reward/new quest and a level-up/new card. No matching log, screenshot,
save or exact native window identity was supplied. The review therefore establishes
source behavior and regression tests; it does not identify the specific player's
root cause or claim a reproduced headset fix.

## Audited continuation families

| Native family | What actually releases progression | Review result |
| --- | --- | --- |
| `CampaignScenarioRewardManager` / `UICampaignRewardWindow` (chest, trap and goal-chest rewards) | Original Continue → `CampaignRewardsManager.Confirm` → deferred Finish → owning actor's `ConfirmReward`, then previous action phase | Existing VR callback repair and reveal/interactability/authority gates retained. A generic Hide is not equivalent. Existing executable tests retain the exact native input coroutine and ownership cases. |
| `UIRewardsManager` (Guildmaster, single scenario, tutorial and map event reward groups) | Native `isConfirmPressed` latch → `ProcessRewards` → native `ProcessNextReward` replication / `EndProcess` → `onProcessEnded` | Existing explicit Continue is correct. Never call `MoveToNextReward` as local input, never call `ConfirmPressed` (gamepad long-press adapter outside Guildmaster), and never hide the window to skip the iterator. Tests cover multiple rewards, multiple groups, owning clients, observing clients and map mode without a scenario manager. |
| `UICampaignAdventureRewardsManager` / `CampaignRewardsManager` (quest completion, training rewards and map rewards) | Native Continue → applied effects chain → final `onClosed` | Existing campaign native Continue is already repaired. Preserve its one-shot native `onConfirmed` and animation gating. |
| `UIGuildmasterAdventureRewardsManager` (quest/adventure reward popup) | Original close button → `Hide` → each queued character unlock video → window Hidden transition → `onClosed` | Added exact original callback repair for windows initialized before VR in gamepad mode. No custom close or video skipping. Native original Hide is executed verbatim by the new test fixture. |
| Campaign prosperity increase / newly unlocked shop stock | Reward reveal → `UIIntroductionRewardsProcess` → `MapStoryController` wealth dialog → promise resolution | Absence of Continue on the background reward window is intentional here. Adding one would skip the foreground introduction/story dependency. The story/introduction review belongs to the integrating and announcement lanes. |
| `CampaignUnlockQuestRewardsProcess` / `UIUnlockLocationFlowManager` | For each location, focus completion enables the exact Continue, which resolves its promise; final focus restores HUD/camera and completes reward processing | Added original callback repair for pre-VR gamepad initialization. Root fixes the body-click catcher to target only the controller's exact Continue, rather than an arbitrary first active Button. Tests execute the root's production dispatch and retain camera-focus/ancestor/active/subtree gates. |
| `CampaignUnlockCharacterRewardsProcess` | Each native `VideoCamera.PlayFullscreenVideo` completion resolves a promise and restores input | Existing video skip/normal end must enter native completion; no reward-side change. Guildmaster queued-video regression verifies two consecutive videos complete before window closure. |
| `UICompletedPersonalQuestWindow` | Native completed animation shows original close button; window Hidden transition resolves promise and removes HelpBox | Native button is always wired, independent of input mode. No callback repair needed. Do not force completion while its reveal animation is running. |
| `UIDistributeRewardManager`, `UIDistributeReward` and distribution processes | Required allocations make native Confirm available; host Confirm resolves promise and replicates `DistributeUIConfirm`, then service Apply restores HUD and interaction | These are mandatory decisions, not informational popups. Existing AssignmentWindows conversion and native Confirm must remain; neither body click nor generic X may bypass required allocations or multiplayer authority. No missing unconditional button binding found. |
| `UIResultsManager`, `UINewAdventureResultsManager`, `UIResultsButtonOption` (win, defeat, retry, resignation, tutorial/custom scenario end) | Registered native Return/Retry callbacks and multiplayer ready-up sequence | Real mouse buttons are bound on every Register; results animation controls the buttonsContainer. Existing no-X policy retained. Native client wait-for-host HelpBox is intentionally informational; readiness/host selection must not be bypassed. |

## Changed input bindings

`RewardShowcase.Tick` now independently checks the map adventure rewards and location
unlock singleton, even if no campaign/scenario reward window exists. It removes and adds
only the same original native runtime delegate once. Other callbacks are preserved,
native buttons remain responsible for hover/interactability, and repeated ticks,
conversion shutdown/re-entry and native reopen do not multiply listeners.

`InputModeGuard` already prevents gamepad initialization in ordinary active VR. The two
new bindings address windows that were initialized before activation, consistent with
the existing campaign repair; they are **not proof** that gamepad mode caused the report.
There are no new normal-level log streams, game-state writes, forced acknowledgments,
changes to original multiplayer authority, or new remote presentation divergence.

## Regression evidence

`tests/GloomhavenVR.RewardShowcaseTests` executes production RewardShowcase and the
integrator's complete `DismissTransient` method with explicit Unity/pointer boundaries.
It compares retained native `UIRewardsManager.ProcessRewards`, Guildmaster `Hide` and
location `Continue` bodies with the read-only decompilation when available.

New cases cover existing mouse bindings versus absent gamepad bindings; preserving
unrelated listeners; disabled native controls; queued unlock videos; completed callbacks;
reopen/re-entry; unrelated earlier Buttons; ancestor-disabled, component-disabled,
inactive and foreign-subtree Continue; and exact permitted pointer dispatch. Mutations
remove each repaired binding, duplicate a native listener, and permit the wrong Button.

Focused results: **351 assertions / 18 rejected negative controls** (previously 314 / 14).
Strict Release compile: **0 errors / 0 warnings**. Whitespace check passes. Test output:
`/tmp/windows553-reward-tests3.log`; compile output: `/tmp/windows553-reward-build.log`.

Integration is required before running without overrides because the exact-target
`DismissTransient` implementation belongs to the root lane. Until then:

```sh
REWARD_CLOSE_SOURCE=/home/claw/gloomhaven_vr/src/GloomhavenVR/WorldUI/Modal/ModalFallback.7.Close.cs \
  bash scripts/reward-showcase-tests.sh
```

Hardware checks still needed: Campaign and Guildmaster quest completion with consecutive
rewards/new quest unlocks; level/prosperity introductions; treasure reward; results Return
and Retry; host/client observation and control; subsequent repeated openings. Automated
callback evidence does not prove readable layout, laser reachability or complete native
transition animation in a headset.
