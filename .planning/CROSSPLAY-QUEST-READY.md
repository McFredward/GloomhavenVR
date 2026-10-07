# Flat/VR crossplay quest readiness review, 2026-10-08

Worker base: `dev` at `819a9a9ee`. Scope: `MapQuestReadyUp`,
`ReadyToggleParkClaim`, their causal native-source runtime fixture. This lane
does not modify wire fields, authoritative game state, the native all-ready
handshake, scene travel, or the native readiness participant population.

## Source-proven failures

1. `TickPendingClientPrompt` discarded the exact native quest-preview callback
   whenever `UIReadyToggle.IsVisible` was true. Native
   `UIReadyToggle.Initialize(show:false)` retains `_requestVisible`; its
   subsequent native `SetInteractable(true)` can reveal a prior or early VR
   request before the new native preview callback runs. The dispatcher therefore
   inferred an answered prompt from a visible reused singleton. That inference
   skipped the native quest selection/card and cancellation control.
2. `MapQuestReadyUp.Reset` erased pending native callbacks on VR room teardown.
   The native map controller may survive a room rebuild, and its hidden-HUD
   prompt can be captured before the next room activation. Reentering that room
   had no remaining prompt to answer; reconnecting happened to raise a fresh one.
3. The desktop presenter's native installed click is a wrapper:
   `HideMultiplayerQuestPreview(); onConfirmCallback?.Invoke()`. Capturing the
   bare argument bypassed the native hover-preview close, allowing a second
   stale native quest card after accepting the proposal.
4. A park claim trusted any parked object even when it differed from the
   current native singleton. Claims also remained good for a destroyed target
   until their clock expired. Both now require the actual current live target.

## Resulting behavior

Each captured native proposal is driven once through its original installed
click continuation on an online client in the active VR room. The callback is
validated against the original native controller and stable quest identity,
the Quests toggle state, and the existing point-of-no-return guard. Missing
native initialization waits; cancelled/replaced scenes and committed/other
readiness decisions refuse the callback. Visibility no longer proves that the
proposal has been answered.

A pending prompt survives room presentation reset and is revalidated before
use. Native desktop `OnClicked` and console `Confirm` postfixes consume the exact
pending callback when the player answers it normally in 2D. Consequently room
entry cannot replay an already answered native prompt or disturb its readiness.
The desktop dispatcher captures the actual native installed wrapper; optional
metadata fallback performs native public hover-preview cleanup before the
original supplied continuation.

Native ready-up remains entirely manual and uses the original `ReadyUp` entry
point. Unready on actual quest replacement still uses the game's own validated
local-player withdrawal, with `autoValidateUnreadying:true` for a full ready set.
Visibility blocks, participant assignment, all-ready hiding, controller/quest
state, and the native host's controllable-state ACK/`ReadyProceed` remain native.

## Integration requirements outside this lane

- Register `MapQuestReadyUp.Install()` at WorldUI startup, before the first
  native map proposal. The former registration only on the first room Engage
  could miss proposals raised before that activation. Root owns this wiring.
- Select the actually shown native selected/multiplayer quest window for the
  confirm parker. The two native roots can share the QuestPopup ID and cannot
  be distinguished by that ID or client/host role alone. Root owns Modal/window
  routing and multiplayer presentation authority.
- Derive the effective quest decision from native host proposal/local native
  quest state as well as mod map-selection records. A Flat host has no mod
  selection broadcast. Initial native proposal observation must not withdraw
  existing readiness; replacement/cancellation may. The selection worker owns
  that observation contract.
- Update patch inventory for two new native click seams and register the new
  focused runtime script in the integrated suite registry. Root owns both.

## Evidence and limits

`check-map-quest-ready-runtime.sh`: 224 assertions, 25 two-through-four-player
Flat/VR membership layouts, seven runtime causal controls. It compiles both full
production classes and unchanged native visibility/initialization/press and
desktop/click method bodies. Its explicit scene/transport boundaries are
documented in `tests/map-quest-ready-runtime/README.md`. No automatic acceptance,
gameplay collection write, second handshake, or mod packet is introduced.

Final strict Release compilation passes with 0 errors/0 warnings. Final source
checks pass 14/15; the expected remaining patch-inventory failure requires the
integrator's generated documentation update for the two added click seams.
Receipt: `.planning/debug/test-runs/20261008-000127-f52e571c/results.json`.
Focused existing suites also pass: map-flow 2,012 assertions/nine causal controls
and map-button 14 assertions/two controls, 2/2 partial scope.
Receipt: `.planning/debug/test-runs/20261008-000258-c5605c07/results.json`.
A mistakenly broad refactor command earlier completed the source group (15/15)
but then started the entire runtime group and was cancelled; no complete-gate
pass is claimed. Its unrelated town-service-mirror
fixture failed compilation on existing missing `SpriteKey`/`ScanPackedSprites`
extraction bindings at this worker base. The primary integrator runs the final
combined checks and records any unrelated base failures separately.

No supplied paired-log build banners or screenshots establish this user's
reported sequence, and no new headset/session result is claimed. This proves
the source causes and their dispatcher repairs. Native real multiplayer host
ACKs, loading, party changes during ready-up, campaign/Guildmaster picture,
and headset usability still need an actual integration hardware test. Native
participant/connecting-player refusals remain visible game rules rather than a
mod-created ready-up bypass.

Native departure ordering remains a separate audit: quest initialization sets
`validateReadyUpOnPlayerLeft:false`, and `UIReadyToggle.OnPlayerLeft` removes the
departed ready/awaited member then returns without launching a newly sufficient
quorum's ACK coroutine. This fixture does not claim that native progression
corner as proven; the primary audit is tracing the surrounding native
player-registry callbacks. It must not be repaired by writing `PlayersReady` or
adding a mod-owned proceed handshake.
