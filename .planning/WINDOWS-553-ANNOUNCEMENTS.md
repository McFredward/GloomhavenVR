# Build 553 — Native announcement continuation audit

Scope: dev `e63fb284`, without NPC work. This lane adds explicit level-up reveal input and
reviews native map introductions, personal-quest completion, retirement and location unlocks.
The reported user's run has no attached matching logs or screenshot; this is source evidence,
not a reproduced headset deadlock.

## Proven defect and implementation

`UILevelUpWindow.Awake` binds `nextCardTracker.onClick` to `OnCardShown`. That tracker is
`ClickTrackerExtended`, whose `Update` samples desktop mouse/gamepad actions; it does not
implement uGUI pointer clicks. The native sequence intentionally disables card inventory input
until all new level cards have been acknowledged. A converted, visible card therefore can wait
indefinitely while the VR pointer has no native confirmation input to deliver.

`AnnouncementContinue` and `AnnouncementContinueView` attach a clearly labeled, native-skinned
Continue button below the actual original card surface. They call the original tracker's
`ProcessClick`, including its sound and `SkipNextClick`, only after the native `enableTracker`
reveal-completion latch permits progression. The original `OnCardShown` immediately closes that
latch, then fades the card out and starts the next reveal. Both this native latch and a same-frame
input guard prevent a repeated click from bypassing animation. No window is hidden and no card
selection or level-up is committed by the bridge.

After the last reveal the native inventory becomes interactive and the added Continue disappears.
The player chooses the new ability with the existing card selection and confirmation UI.
`UILevelUpWindow.Close` must never be offered as a generic dismissal: its native hidden callback
ultimately invokes `NewPartyDisplayUI`'s level-up completion and is not an informational close.

The button follows the actual card rectangle in native UI coordinates, with the highlighter
rectangle as fallback while the card is being created. Its pixels participate in ordinary panel
fitting. It never measures its own geometry, uses no invisible full-window hit surface, and
honors native hover/pressed/disabled skin states. Existing EN/DE `Loc.RewardContinue` is reused.

Readiness deliberately uses `enableTracker`, not `nextCardTracker.enabled` alone: the native
controller-focus handler disables the tracker when another controller area receives focus,
without invalidating the revealed card. Direct VR pointer interaction with that visible card
must remain possible. Pause menu, desktop presentation, conversion shutdown, inactive source,
stale singleton, opening animation, true selection confirmation and non-owned online character
all reject progression.

## Native flow inventory

| Flow | Native progression | Review result |
|---|---|---|
| Level-up new-card reveal | `ClickTrackerExtended.ProcessClick` → `OnCardShown` → fade → next card | Fixed with explicit Continue; final card selection remains mandatory. |
| Level-up ability choice | `UILevelUpCardInventory.OnCardSelected` → `ConfirmSelectCard` → native confirmation → `GainCard` | Original choice and authority retained; no generic close or automatic selection. |
| New unlocked quest/location | `UIUnlockLocationFlowManager.continueButton` → `Continue` → next promise | Mouse-only Awake binding defect identified; repaired by the reward lane, not duplicated here. Original focus/reveal gate remains. |
| Personal-quest completed/progressed | `UICompletedPersonalQuestWindow.closeButton` → native `window.Hide` → `OnHidden` resolves promise | Native button listener is unconditional; button revealed only after completion animation. Preserve native action. |
| Retirement confirmation | `UIRetireCharacterConfirmationBox.confirmButton` → `Confirm` → confirm animation → `Hide` | Native listener unconditional; preserve actual confirmation, not generic dismissal. |
| Retirement story/rewards/unlocked locations | `UIRetirementManager` native promise chain through story, rewards, unlock manager | Use each original subsystem continuation. Never hide outer manager to skip promises. |
| General/level-up introductions | `UIIntroductionManager` → `LevelMessageUILayoutGroup` → `CloseButtonPressed` | Existing real Continue handles pagination and queue progression. Native dismiss-vs-action distinction retained. |
| Scripted scenario tutorial hints | `LevelMessagesUIHandler` and native `DismissTrigger` | Action-dismiss hints must complete their actual task; do not add blanket click-to-close. Existing stuck-state heal remains separate. |
| Character unlock cinematic | `CampaignUnlockCharacterRewardsProcess` → `VideoCamera.PlayFullscreenVideo` callback | Existing VR video skip must invoke original video completion even while native KeyActions are disabled. Sent to integrator for cross-lane check. |
| Initial campaign cinematic | `UIMapFTUEInitialStep` tracker/`Escape` → native video stop and fade callback | Existing VR video path, not the level-up adapter. |
| Ordinary further-ability-card browse | `UILevelUpCardInventory` native cards and navigation | Not an announcement; never gets Continue. |

Online level-up is entered only for the locally controlled character in
`NewPartyDisplayUI.OnLevelUpSelected`. The adapter rechecks that ownership at interaction time;
remote renderers do not execute native callbacks. No wire format or synchronization action changes.
Retirement's existing native ready-up and server-authoritative commit remain unchanged.

## Integration contract

Call `AnnouncementContinue.Tick(enabled)` before modal fitting/conversion each frame, with the
same valid VR conversion policy used for native reward input. Call `Tick(false)` on conversion
teardown/reset. Suppress generic X/raw-hide for the actual `UILevelUpWindow` throughout its reveal
and card-choice phases. Register `scripts/announcement-tests.sh` in local and hosted test inventories.
These integration files belong to the primary lane.

## Validation

- `scripts/announcement-tests.sh`: **345 assertions, six negative controls**.
- Production `AnnouncementContinue.cs` executes in the harness. Original native `ShowCard`,
  `OnCardShown`, `OnFinishedShowCards`, and tracker `ProcessClick` are compiled from recorded native
  methods and compared to the read-only decompile. `ProcessClick` accessibility is publicized as
  in production; its body is unchanged. Engine/rendering boundaries are explicitly stubbed.
- Covers two consecutive reveals, native animation waits, native skip-next-click, no card choice
  bypass, ownership, pause/desktop/teardown, stale/inactive sources, controller focus changes,
  64 fresh openings and 32 reopenings of the same pooled window.
- Mutations independently remove readiness, original ProcessClick forwarding, same-frame guard,
  ownership, reveal-only scope and native window-open gate. Every mutation must fail its named
  behavioral assertion, not merely fail to compile.
- Strict Release build: **zero warnings, zero errors**. `git diff --check` passes.

The tests do not prove headset placement/readability, actual hardware input or the unprovided
user run. Hardware follow-up: new-level reveal → all next cards → ability selection → confirmation;
repeat level-up; unlock several locations; complete/progress a personal quest and retirement;
run first introductions and a character-unlock video in singleplayer and with an owning peer.
