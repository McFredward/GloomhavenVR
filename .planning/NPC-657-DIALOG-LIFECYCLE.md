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
Quiet immersive controllers, native palm/confirmation masks and retiring masks
are excluded using the actual presentation ownership guards. Their native
continuations are not canceled or moved by this seat.

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

Final worker receipt `run-13z0agl6`: **42 runtime assertions and five causal
controls passed**. The actual published Build 656 source reproduces destination
migration. Omitting the host Hide binding, using slow host cancellation, or
omitting already-hidden Cancel/Confirm completion fails its specific native
continuation assertion. Coverage includes both destination-switch directions,
reopening, original size/anchors/home restoration, reset detachment, early
unconverted flat closing, quiet/retiring ownership and immediate singleton reuse
without losing or duplicating the old outcome.

Final Release compilation with the independently owned transfer API and input
changes: **zero warnings, zero errors**. Evidence lives in the worker's
`.planning/debug/flat-confirmation657/`, with source and native assembly hashes.
The integrator must bind both repaired runtime parts and run the focused combined
input check. No new full-gate or hardware acceptance is claimed.
