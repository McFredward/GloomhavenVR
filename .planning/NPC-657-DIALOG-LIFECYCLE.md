# Build 657: native map confirmation lifecycle

## Evidence and correction

The latest owner log is Build 656. `UIEnhancementConfirmationBox` is the same
native singleton for temple donations and enchantress purchases. The original
`MapDialogSeat` inferred its raising destination from the currently selected HUD
mode on every tick. A temple prompt therefore moved to the enchantress when the
player changed destinations, carrying the temple content and callback with it.
`UITempleWindow.Exit` hides the temple window but does not close this prompt.

The seat now records its destination at the native confirmation window's Show
edge. It also handles a first observation of an already-open singleton, without
later interpreting a mode switch as a fresh request. Closing the recorded flat
host finishes the original native decision before the HUD enters another mode:

- An open, undecided prompt uses native `Hide(instant: true)` and the original
  hidden cancellation callback. This clears the temple's pending decision through
  the game's own continuation.
- If Confirm or Cancel already started its ordinary fade, public native
  `StartAlphaTween(0, 0, true)` finishes that existing chosen outcome. It does not
  fire another OnHide, restore navigation twice, or replace confirmation with
  cancellation.

This atomic completion matters because a new native `ShowConfirmation` resets
this singleton's transition listeners. Waiting for an outgoing fade while another
NPC immediately raises a new prompt can discard the old callback. Ordinary
button-driven fades retain their behavior while their host stays open.

Flat host cancellation is bound before its first conversion if necessary.
Quiet immersive controllers and native palm/confirmation or retiring masks are
excluded using the actual presentation ownership guards. An existing flat seat
keeps its original snapshot without changing any parent, pose or layer while that
owner holds the root. Calling `Release` while the owner holds it would pull the
root out of its zero-alpha mask. After the actual mask releases the native root,
ordinary native-home restoration resumes. Native continuations are not canceled
by this ownership handoff.

Parking and returning a subtree call the new conversion transfer API at the two
reparent edges. The independent conversion/input work owns the exact adopted
canvas, raycaster and snapshot handoff; this lifecycle fixture declares that API
as a port rather than claiming to prove the laser path itself.

## Focused verification

`scripts/check-flat-confirmation657-runtime.py` compiles the production seat and
unchanged methods extracted from the installed game's `GH.Runtime.dll`:
`UIWindow.Show/Hide/EvaluateAndTransitionToVisualState/OnTweenFinished` and the
shared confirmation's sprite Show, Hide, native Confirm, reset and navigation
continuation. Native Unity 2021.3.5 transforms, CanvasGroup, Button and UnityEvent
are used. HUD lookup, navigation/audio/controller sinks, conversion lookup/API
and deterministic tween completion are explicit fixture ports. Zero-duration
completion follows the installed TweenRunner's synchronous Finished behavior.
This is not an end-to-end headset or network test.

Initial worker receipt `run-13z0agl6`: **42 runtime assertions and five causal
controls passed**. The actual published Build 656 source reproduces destination
migration. Omitting the host Hide binding, using slow host cancellation, or
omitting already-hidden Cancel/Confirm completion fails its specific native
continuation assertion. Coverage includes both destination-switch directions,
reopening, original size/anchors/home restoration, reset detachment, early
unconverted flat closing, the quiet-controller port and immediate singleton reuse
without losing or duplicating the old outcome.

Final Release compilation with the independently owned transfer API and input
changes: **zero warnings, zero errors**. Evidence lives in the worker's
`.planning/debug/flat-confirmation657/`, with source and native assembly hashes.
The integrator must bind both repaired runtime parts and run the focused combined
input check. No new full-gate or hardware acceptance is claimed.

## Ownership proof correction

Integration review caught a fixture blind spot: the earlier `Owned` boolean did
not reparent the native root as `TownServiceWindowMask` does. Its two ownership
assertions therefore did not establish that a masked root stays masked. Those
claims are superseded by the focused real-mask case, not treated as inherited
geometry evidence.

The new fixture compiles the production `TownServiceWindowMask`, creates its
actual zero-alpha CanvasGroup wrapper and exercises the original native callback
inside it. Ownership is checked before zero native visibility, so a closed
retiring prompt cannot be handed back prematurely. The test covers extra owned
ticks, host closing, unchanged callback/pose/layer, actual three-tick retirement,
mask disposal and exactly one native-home restoration. Receipt `run-vpv_2p_b`
passes the initial ten wrapper assertions and causally rejects the prior
`526957a40` release-on-ownership implementation. Final receipt `run-ytapbxkd`
passes **eleven real-mask assertions and that causal control**, including an
explicit arbitrary mask-owned pose/layer assertion. No full-suite or Release
rerun is claimed for this limited guard correction; unchanged native lifecycle
and cancellation controls retain their earlier evidence.

The ownership deferral applies to active and retiring presentation ticks. The
terminal `WorldUIModule.Shutdown` resets station/palm ownership before
`ModalFallback.Detach` invokes `MapDialogSeat.Reset`; the terminal hand-back still
restores the authored native source. Ordinary options or destination changes do
not invoke this terminal reset path.
