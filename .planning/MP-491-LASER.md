# ModBuild 491: foreground grab-bar ownership

## Evidence and cause

Both supplied `.planning/debug/LogOutput.log` and `remote/LogOutput.log` identify
ModBuild 490 on line 17. The host log's `LASER BAR OWNERSHIP` at lines 3091 and
3171 confirms that the previous native-window prepass ran and refused widgets
behind the bar. This proves that particular path was active; it does not prove
that every independent map picker observed the same blocker. Host lines 4072 and
4089 also demonstrate the policy-off left-hand aim route for map hover.

Source inspection found two separate ways to bypass that prepass:

- `MapLocationInteractor.PickFrom` raycasts only map-location layers. Its existing
  solid-occluder distance covers the fan/control board, not a window's trigger
  collider. A correct visual beam clamp therefore did not remove the farther
  map hover or prevent direct `MapLocation` dispatch.
- Map hover can come from the other hand when the primary ray stands down.
  `TriggerEdgeHand` previously dispatched that shared hover without validating
  the hand pulling the trigger. A primary window carry could thus inherit an
  offhand map target. Suppressing the far beam's click does not suppress this
  direct native method call.

The logs do not contain per-frame collider distances for the map path, so the
exact reported icon cannot be attributed from the log alone. These are
source-proven bypasses, not a claim to have replayed the hardware interaction.

## Change and coverage

`RayGrabDriver` now shares its narrow visible-bar geometry with independent
pointer consumers. Occlusion does not require that the querying hand is primary,
policy-enabled or allowed to grab that bar. Disabled/inactive collider geometry
is ignored; the existing dedicated bar collider retains precedence over the
larger palm grab zone. The original native-window prepass and its diagnostic
markers remain intact.

The map picker limits both icon-pad and authored-box candidates by the querying
hand's nearest bar, native UI hit, solid surface and reach. Ties belong to the
foreground blocker. Map/table deselection applies the same bar geometry, even
before another driver has published its visual beam clamp. Hover exits through
the existing native `SetHover(null)` path. Click dispatch re-picks the clicking
hand's own target instead of borrowing the other hand's hover.

A per-hand carry-frame stamp blocks the full held gesture and its release frame,
including the point after `ProximityGrabber` clears `Held`. It also stops an idle
offhand from taking over map hover while the primary hand carries a window.
The next frame is eligible again without an arbitrary timeout. Explicit clicks
still require a target from the clicking hand; offhand policy-only aim remains
available when the primary hand otherwise has no live beam.

The parallel-path review found the same omitted trigger geometry in
`MapButtonRail.TickLaser`, `CombatLogSurface.TickCapLaser`, and the flat-menu
screen-plane picker. All now check the foreground bar before hover/press and
respect carry/release ownership. Existing cap hover exits clear the tint;
flat-menu suppression immediately parks the virtual mouse offscreen through its
existing bridge (synchronous device position plus queued state). An active
fingertip retains its pixel, and native world-panel pointer events are separate. Already latched screen gestures retain their release unless
an actual window carry takes ownership. Physical fingertip paths are unchanged.

No game-state mutation, Harmony patch, configuration option or wire change was
added. The new geometry query is allocation-free and traverses the same existing
registered-grabbable collection as the native-window prepass.

## Validation

- `bash scripts/ci-build.sh Release`: passed, 0 errors and 0 warnings.
- Existing `bash scripts/wire-tests.sh`: 251572 assertions passed before root
  registration of the new vectors.
- Private runner source-linking the production `LaserPointerPolicy` and both
  laser suites: 64 assertions passed (including the follow-up parking guards). No shared test registration was modified.
- The new `MapLaserOwnershipVectors` exercises near/far/equal depth, open rays,
  solid/native-UI blockers, three world scales, held/release/next-frame behavior,
  fallback arbitration and hand-specific activation. Behavioral negative controls
  reproduce both the missing-bar term and the borrowed-hand target. Source guards
  connect those predicates to the independent hover/dispatch paths and reject
  removal of the actual map re-pick or blocker arguments.
- `git diff --check`: passed.

Root integration owns full registration and the final repository gates. Unity
collider intersections, visible hover exits and pointer event order still need
hardware confirmation: put a bar in front of a map icon, quest widget, table cap
and flat-menu button; hover, hold/move, and release it; then move the bar away and
verify each formerly blocked target works. Repeat with an idle offhand aimed at
another icon and with handedness swapped.

## Follow-up review: stale flat-menu cursor

Root review found that the original `HideReticle` return stopped new cursor drives
but left the previous screen pixel live until `VirtualMouse`'s two-frame idle grace
expired. The carry check also followed `TryGetPick`, which already returns false
while holding a window. The follow-up moves ownership before that return, after
active-fingertip arbitration, and immediately parks on a measured bar/native-panel
veto or carry/release ownership. Cancelling the screen latch also ends an active
map pan and native drag without delivering `DirectClick`. Existing native panel
pointers are not cancelled. The source guards reject a disconnected parking call,
protect fingertip ordering, and verify the abort has no click path.

Follow-up validation repeated the strict Release build (0 warnings/errors), the
existing wire suite (251572 assertions), and the focused runner (64 assertions);
all passed.
