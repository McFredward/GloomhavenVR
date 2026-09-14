# Map input and travel confirmation, build 500

The September 14 local hardware log is build 499, assembly 1.0.0.0, commit
`ed35cfb25` (`LogOutput.log:18,71`). The remote log is the older build 491 and is
not evidence of a second client in this session.

## Evidence and cause

The current log records a real location click after the party committed:

- `LogOutput.log:9625`: the right trigger dispatches `pointerClickHandler` and
  the native `MapLocation.IsSelected` becomes true.
- `:9627`: the journey is committed, story/loadout is standing, and
  `AdventureMapUIManager.IsLocked=True`.
- `:9630`: the adopted travel container passes its 500 ms reveal fallback.
- `:9645`: travel confirmation is placed inside the quest card.
- `:9788`: that quest card is rendered while `ActiveDisplay=BATTLE_GOALS`.

This is a source-proven input admission gap. Native
`AdventureMapUIManager.LockOptionsInteraction` raises `lockMapInteractionMask`
whenever its lock request collection is nonempty. Normal pointer input cannot
reach map locations through this full-screen blocker. Neither
`MapLocation.OnPointerClick` nor `MapLocation.IsSelectable` repeats this mask's
lock check. VR dispatches directly to those handlers and bypassed the blocker.
Only the VR *deselect* tick previously observed `IsLocked`.

The offline travel container also loses the original interaction mask when
adopted into the quest window. Its active hierarchy alone was therefore not a
valid indication that it should be offered: native `OnTravelButtonClick`
explicitly rejects clicks while `IsLocked`.

This lane explains the unexpected Reisen button and the map click that reopened
its quest card. The missing required picker is addressed separately by the
story/loadout lane; this report does not claim the button itself caused that
initial omission.

## Changes

`MapInputGate` reads the original manager's current lock verdict. It writes no
native state and holds no timer, cached lock or network action.

- Map ray picks and both fingertip/laser hover admission obey the mask. A null
  hover exit remains deliverable so a newly raised lock clears a live preview.
- Location dispatch checks the mask before haptics, the special capital route,
  or native pointer callbacks. Finger, laser and programmatic dispatch share it.
- Deselect and remote adoption check the same mask before changing the staged
  selection. Remote edges arriving in a locked phase are not replayed later;
  the existing remote receiver consumes each edge on arrival.
- The adopted offline travel container's existing alpha/raycast hold also
  requires an unlocked manager. It reappears when the native game unlocks it,
  provided its original container is active and the normal pose gate succeeds.
- Online confirmation retains its original ready-toggle visibility/state rule.
  `QuestConfirmToggle` already requires `EReadyUpToggleStates.Quests`, and
  `InitializeSelectQuestReadyUp` resets/hides that toggle before confirming
  travel. Other native ready-up purposes are not parked as quest confirmation.

## Validation and remaining hardware checks

Worker strict Release build: **0 errors, 0 warnings**. The integrator owns the
shared source-linked map-flow regression harness and complete integration gates.
No runtime patch class, game data, wire grammar, placement dial or native callback
was added or changed.

Replay story through battle goals, with deliberate laser/finger presses on map
icons during the lock. The required picker must remain accessible, no new quest
selection or travel control should appear, and unlocked map selection/travel must
still work. Repeat online with host/client quest readiness and normal capital
navigation. Automated source checks do not establish the headset picture.
