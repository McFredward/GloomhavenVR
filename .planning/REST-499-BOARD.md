# Build 499: remote board flash investigation

## Scope and evidence

Reviewed from `dev` commit `317fc045`, in the isolated `rest499-board` worktree.
The lane owns remote board visibility, pose and peer fade code; it makes no
runtime change to these systems because the supplied evidence excludes their
ordinary transitions during the reported burn.

The main checkout's `debug/LogOutput.log` identifies ModBuild 498, assembly
1.0.0.0, at line 18. Its mixed-session census identifies the other participant as
build 498 at line 3552. `debug/remote/LogOutput.log` is **older build 491**, line
17, and is used only to revisit the earlier unresolved incident documented in
[MP-492-BOARD.md](MP-492-BOARD.md). It is not the other viewpoint of the current
session. No screenshot or video identifies an exact rendered frame for this
flash; old regression images cannot establish its current picture.

## Whole-board gates and fade are not the trigger in this capture

- `REMOTE BOARD VISIBILITY` records one root creation at line 7289, frame 12804,
  and its destruction request at session exit, line 22855, frame 56230. Both
  reference root `-619904`; there is no `ACTIVE_CHANGED` edge in between.
- The board uses `RemoteBoards=Always`. No style/tuning/size rebuild occurs at
  the long rest. The scenario gate remains open.
- The final peer fade episode engages at line 19533 and releases at line 19579.
  The long-rest burn is later, at line 21507. No new fade engagement or cull
  transition occurs during the burn. `PeerBoardFade.Apply` returns through
  `Release()` at solid alpha; it does not keep driving transparent materials
  or a hidden board during that interval.
- Burn/card follower roots are faded with the board but do not contribute to
  its occluder bounds. Board-descendant renderer bounds can affect a future
  coverage evaluation; this does not explain an incident with no fade edge.
- Board draw-order adoption registers the rendered members with the ordinary
  furniture cluster. It does not deactivate the root or repaint the board
  mesh as a burning card. No new cluster membership is reported between the
  burn's flat-screen show and hide edges.

These measurements exclude the instrumented lifecycle and peer fade decisions;
they do not assert that every board pixel was visible.

## Positive evidence: an unintended full-screen fallback during the burn

The main investigation identified the following current sequence. Independent
inspection of the compositor source confirms that this can cover a board whose
own object remains active:

| Local build-498 log line | Observation |
| --- | --- |
| 21479 | A local card reports `mode=ModalUI`. |
| 21481–21487 | The fallback creates a 2560×1440 render texture, places a 15.74×8.85-world-unit screen in front of the viewer, enables stereo and reports `FlatScreen shown`. |
| 21501–21505 | ScenarioCamera and UI Camera are captured into the screen; the scenario background clears opaque black and gets a stereo mirror. |
| 21506 | Frame 52372 costs 38.87 ms, including 11.77 ms in FlatScreen. |
| 21507 | The observer discovers the peer's long-rest burn of Grab and Go in recess 1. |
| 21534 | Frame 52402 costs 26.52 ms, including 11.35 ms in FlatScreen. |
| 21541 | `FlatScreen hidden`; camera capture is released. |
| 21561 | The burn flight leaves after a 1.97-second hold, following the owner's release event. |

`FlatScreen.2.CameraStack.EnsureBackQuad` assigns the opaque `_screenMaterial`
to the split background. `FlatScreen.4.Lifecycle.Show` selects
`Hidden/BlitCopy`, whose documented pass ignores alpha and uses ZTest Always /
ZWrite Off. The transparent glass UI layer does not make this background
transparent. `FlatScreen.1.Core.TickBackdropDepth` explicitly restores the
normal screen queue in scenarios so a summoned screen renders over the 3D
world. Placement puts the screen directly along the viewer's flattened gaze.

Consequently a wrongly summoned fallback replaces part of the headset picture
with a second camera view. The remote board can appear to disappear or flash
without a board visibility transition. The unwanted fallback is demonstrated
by the log and source; the exact per-eye appearance remains a hardware check.
The FlatScreen CPU cost also explains a concurrent short stall without proving
that a stall was the visual defect itself.

The existing `FlatScreen.WantVisible` comment already describes this family:
card burning creates a UI lock, but the animation belongs on the world card;
an empty ModalUI catch-all must not summon the desktop screen. The previous
protection reads `HandSuppression.BurnActive`, registered by local burn FX.
The root lane audits and repairs the multiplayer coverage of that protection.
There is no reason here to suppress native fire animation, change the board
fade default, or remove board presentation parity.

## The earlier unresolved incident has the same signature

The older remote build-491 log contains:

- 30407: ModalUI during the other player's long rest.
- 30411/30415: a 40.58×22.83-world-unit fallback screen is placed.
- 30416: FlatScreen shown.
- 30447: peer 1's Gnawing Horde burn is discovered.
- 30459: FlatScreen hidden.
- 30507: the burn flight leaves after a 1.97-second hold.

That is the same long-rest window recorded in MP-492-BOARD.md. In the same
older client's **own** long-rest window, line 33560 reports ModalUI but no
FlatScreen show occurs in lines 32700–33800. The distinction fits missing
remote coverage of a local burn suppression contract. It establishes that this
signature predates builds 493–498; it does not identify the first introducing
commit or prove which precise pixel the user noticed then.

## Validation and integration

This lane inspected the current and historical log windows, remote gate/pose/
fade source, compositor material/placement source, and relevant Git history.
No board runtime changes or speculative renderer suppression were made.
`git diff --check` passes. Runtime tests belong to the integration lane's
actual fallback-suppression fix; a documentation-only lane cannot claim a
headset correction.
