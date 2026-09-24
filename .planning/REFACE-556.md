# Animated release facing — ModBuild 556

## Evidence and cause

The newest local `LogOutput.log` is ModBuild 555. It records a combat-log laser
release at line 4310: `WindowFacing = LaserOnly`, `laser carry = True`, and the
policy's verdict `RE-FACING`. The owner then called `FaceHead`, which wrote the
final rotation immediately. A normal floated window in the same run reports its
150 ms release turn at line 19858. The local control-board grab owner only called
`PersistPoseToConfig` on release; it did not compute a facing target. The supplied
remote logs still belong to ModBuild 500 and cannot verify the new board motion.

## Correction

Both local owners use `WindowReFacePolicy` and the existing `WindowReFaceTween`,
whose duration and cubic ease are shared with the grab bar. The combat log turns
around its visible host centre; the board's root is its visible centre. Target
heading is captured once on release, so subsequent head movement does not steer
the turn. Board yaw is changed without replacing the user's current pitch/roll.

The two owners continue to follow their parent while the short animation runs.
New grabs and explicit placement take ownership from the tween. Final positions
and board yaw are persisted when the turn completes. The board's existing atomic
rig-pose packet samples the same local root and gives peers its intermediate
rotation; no new wire record or remote-only animation is introduced. The combat
log remains each player's private window.

## Verification

The focused owner builds and the integrated strict Release build passed with
zero errors and warnings. `refactor-guard.sh check --summary` passed every
source and production runtime gate, including 254,965 golden wire assertions,
1,322 window-reflow assertions, 84 shared-reflow assertions, 1,216 board-refresh
assertions, 433 retry-start assertions and 245 pick-tray assertions. Its expected
status is 1 because compiled form differs from baseline `080c505e9`: 114 changed
types and 115 added/removed entries across intervening builds. The EN/DE docs
check, workflow lint and whitespace check passed. No headset result exists yet.

In hardware, laser-grab and release each object at a visible angle in both Follow
and Fixed modes. The turn should have the same quick feel as an ordinary window,
without a snap or a swing of the visible centre. Move the headset/rig during the
turn, interrupt it with a second grab, and check the saved pose after rebuilding
or hiding and reopening. Hand grabs should retain the chosen `WindowFacing`
policy. In multiplayer, observe the board from another headset throughout its
turn and compare the intermediate motion with the owner's board.
