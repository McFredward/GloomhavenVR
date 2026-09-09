# MB489 active departures and burn completion

## Source-proven changes

- `TryStartFlyToPile` previously admitted only previous round-recess and pick-field cards.
  A card leaving the active column could not use that authoritative destination switch.
  The driver now records the old active list before rebuilding and routes its actual
  `DiscardedAbilityCards`, `LostAbilityCards`, or `PermanentlyLostAbilityCards` membership.
  Cards still active or recovered to the hand do not get a pile flight. The source remains
  captured while a card is held or waits for its burn artwork.
- The existing `Active` semantic endpoint identifies the departure area. The new positional
  `CardFlightSource` supplies the original actor and old seat/count, never card identity.
  The integrator carries this alongside the same semantic sequence in additive61 and
  recent-event history62. This also preserves the actor when focus changes during a burn.
- `RemoteActiveDepartures` remembers original cell geometry and the locally resolved model
  card. Receivers match the actor, seat/count and actual destination; simultaneous cards do
  not consume an arbitrary neighbor's front. Burn claims retain the same source address.
- A burn flight now waits for both the actual full-card FX and its owning hand's native
  `AnimateCardsLost` lifecycle (`AnimatingLostCards && animatedLosingCard`). No wall-clock
  ceiling cuts across a running native timeline. The startup grace still handles a widget
  whose animation never started. The cancelled-but-latched hand flag does not hold forever.
- The hold pump continues across displayed-character changes. A focus change no longer
  flushes an unfinished burn or lets the ordinary zone sweep park its card. The peer also
  waits for the owner's addressed release rather than generating a release from focus or
  a local timeout. The bounded repeated event history is integrated by the primary agent.
- Flight and active-card art bind to the other lane's original-owner appearance output;
  a fixed two-second receiver guess no longer outranks the actual owner shader state.

## Evidence and limits

Both latest hardware banners are MB488 (line17 in each LogOutput.log). Peer
`remote/LogOutput.log:31366` records ShieldBash leaving after 0.68 unscaled seconds with
`_GreyOut=1`; the original `BurnCardTimeline` advances on the game clock, so this reading
alone does **not** prove premature local FX completion. The peer fallback used a separate
fixed two-second unscaled ramp. Its incomplete picture could therefore remain while the
owner correctly released. The native appearance stream addresses that discrepancy; the
separate loss-lifecycle hold closes a source-proven omission in the local completion gate.
The supplied logs do not directly measure every native loss flag at the release edge.

Relevant read-only game sources: `GH.Runtime/CardEffects.cs:505–619` and
`GH.Runtime/CardsHandUI.cs:1004–1115`. The latter has its own highlighting, card movement,
reordering and final callback after the full-card shader handle may already be cleared.
No gameplay state, native rules, card destination, or network gameplay action is rewritten.

## Verification

`BurnFlightCompletionVectors` exercises startup, finished, running, slow and paused native
animation cases, validates positional-source boundaries, and checks that production active
exits reach the model destination switch and focus flushes consult the completion gate.
Full compilation of the final checkpoint requires the primary agent's transport signatures
and the card-appearance lane's native art API. The earlier pre-integration checkpoint built
with zero warnings/errors; final merged gates and headset confirmation are the integrator's
responsibility. Automated checks do not establish visual parity on hardware.
