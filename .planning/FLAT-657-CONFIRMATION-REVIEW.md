# Build657 window-mode donation confirmation

Base: `bf3444cb5` (Build656). The supplied October9 local logs identify
Build656, German PCVR, in the native window mode. The remote folder still
contains the preceding Build653 run; it is not paired evidence for this report.
Frozen local logs and their hashes remain in `.planning/debug/npc657/inputs/`.

## Findings

The temple and enchantress share the game's `UIEnhancementConfirmationBox`
singleton. Its authored home is below the enchantress window. The VR seat moved
its Transform into the temple but left the previous converted window's canvas
and nested-pointer records attached to the same object. Subsequent conversion
maintenance and release could still write that foreign canvas. The current log
records the temple host and shared child with different sorting orders at
lines9066–9067; pointer traces do not record either donation button being hit.
The logs alone cannot distinguish every raycast rejection, but they establish
the stale ownership that the repair and real Unity raycast proof address.

A second input defect survives ownership transfer alone. `UguiPointer.Beats`
compares a converted host with its stable conversion tier1000, but an ordinary
nested dialog with its live sorting order. The supplied log has a temple host
at196 and a confirmation child at148. Binding the production stable-order lookup
in the actual Unity proof reproduces the inaccessible button: the host background
wins. Explicit transferred ordinary dialog canvases therefore receive their
physical host's dialog input tier. Their draw order, native raycast filtering,
unrelated nested canvases, close buttons and dropdown overlays remain unchanged.

The second reported defect is directly visible in the log. Line8848 seats the
temple request; line9053 hands the still-open prompt back when the destination
becomes None; line9214 assigns the same pending request to the enchantress.
`MapDialogSeat` used the current destination every tick instead of retaining
the original raiser. Native `UITempleWindow.Exit` does not cancel this shared
confirmation, so its donation callbacks remained live during that migration.

A related lifecycle hazard exists even after retaining the raiser: the next
native `ShowConfirmation` resets the shared box's transition listeners. A host
close must finish its outstanding native confirm/cancel continuation before
another destination can reuse the singleton. This also applies when the user
already pressed a button and its native fade has started.

## Repair and compatibility review

- Capture the actual temple/enchantress raiser at the native Show edge, with a
  single late-discovery fallback. A later destination change cannot relabel it.
- Bind the raising window's native OnHide before conversion is ready. Closing it
  completes the native cancellation before the HUD enters another destination.
  An already-started confirm/cancel fade completes its chosen native callback;
  it is not replaced with a different outcome. Ordinary button fades remain.
- Transfer original canvas, camera, sorting, layer, flattening, reveal and order
  ownership at the two explicit seat/home reparent edges. Register the native
  buttons immediately with their physical host and remove the old registration.
  Original snapshots follow the object and restore at its actual native home.
- Give explicitly seated ordinary dialog canvases the current host's stable
  dialog input tier. A live draw-order value must not let the background defeat
  a visible confirmation button. Native overlay and close-button tiers remain.
- Respect active quiet controllers and owned/retiring immersive confirmations.
  A real zero-alpha station mask retains the native root without a flat-seat
  pose, layer or parent write. The original seat snapshot is restored only after
  that mask actually releases it. Terminal module shutdown restores stations
  before the modal seat; ordinary mode/options changes do not call that reset.
  Shared merchant item confirmations use the same bounded conversion transfer;
  native party-inventory confirmations retain their normal home.

The original widgets, callbacks, navigation and transaction methods remain the
source of truth. Button interaction and native raycast/availability gates are
retained. No global gate bypass, payment rewrite, networking command, native
network version, image, configuration or asset change is introduced. Each flat
client keeps its own native window presentation; gameplay donations and
purchases still use the game's original host-validated multiplayer actions.
Immersive public content and its existing parity exceptions are unchanged.

## Validation scope

Final integrated evidence is under `.planning/debug/npc657/integration/`:

- `focused-final/results.json`: all nine selected scopes pass, in73 seconds.
  These are `flat-confirmation-lifecycle`, `seated-subtree-input`,
  `conversion-rollback`, `modal-close`, `card-loss-modal`,
  `town-window-map-switch`, `town-window-close`, `town-service-setting` and
  `town-depth-order`.
- Native lifecycle:51 ordinary and11 real-mask assertions, with six causal
  controls. The installed game's unchanged Show/Hide/confirm/cancel methods
  are bound to real Unity components. HUD/navigation/controller/audio sinks,
  conversion lookup and deterministic tween completion are declared ports.
  The zero-duration port follows the installed TweenRunner's synchronous
  completion. This does not execute a game payment or a connected native HUD.
- Input transfer:42 actual Unity assertions and nine causal controls. Real
  Button/Image/GraphicRaycaster/EventSystem results are arbitrated by the
  production pointer methods and actual stable-order lookup. Existing release
  writer operations are explicit fixture boundaries, not the entire conversion
  lifecycle. Confirmation/Cancel, original geometry/camera rollback, old-owner
  detachment, native disablement and X/dropdown tiers are covered.
- The first input fixture used a false stable-order lookup and passed despite
  the remaining background-priority defect. Binding the actual lookup exposed
  that defect; its omitted-tier control now fails at the real button-picking
  assertion. Likewise, an ownership Boolean could not detect stealing a root
  from its mask: the final fixture includes the production mask and rejects
  that old implementation. Superseded worker receipts remain separately archived.
- All16 source scopes pass. Runner inventory/unit checks pass:18 cases,
  two platform-specific skips. Local inventory is190; CI remains95.
- Strict Debug and Release solution builds pass with warnings treated as
  errors; the private Release snapshot uses the repository's strict property.
  Direct wire goldens pass299961 assertions. The full wire wrapper and full
  190-scope local gate were deliberately not repeated for this bounded repair.
- `compiled-comparison.json`:1247 types before and after. Behavior changes
  are confined to MapDialogSeat, CanvasConversion and UguiPokeSurfaces; eight
  other changed types contain only the exact656→657 build-token replacement.
  The private comparison does not overwrite the shared refactor baseline.
- Surface comparison retains665 config keys,235 Harmony methods and4795 log
  tokens. No removed/reworded public surface, asset or wire-layout change.

Unaffected Build656 and earlier scopes inherit their recorded passing evidence;
this is focused integration, not a fresh complete local gate. Hardware inputs
remain unchanged. Compact worker receipts are retained before removing only the
two completed owned worktrees and generated test caches after the dev push.

The native raycast/callback proofs establish the repaired input and lifecycle
causes, not headset acceptance or a connected Bolt test. The next headset check
is donation Confirm and Cancel, immediate temple/enchantress switching, and
reopening the same prompt; immersive NPC interaction should retain its usual
native mask and confirmation behavior.
