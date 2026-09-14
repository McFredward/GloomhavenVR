# Multiplayer 501: atomic card-flight ownership

Base: `7745d90c` (dev, build 500). This lane changes presentation only. The semantic version
remains 1.0.0; the integrator owns build 501, final checks and publication to dev.

## Evidence and cause

Both supplied LogOutput logs identify build 500 / `7745d90c`. Every one of the nine
`FLIGHT FACE` starts reports its source recess still occupied: main lines 9742, 10511,
17509; remote lines 6853, 7079, 10970, 11125, 13666, 13839. One of these is Slot0 → Active;
the others go to Discard. A mask is not a measurement of final headset pixels, but source
inspection provides the missing mechanism:

- RemoteCardFx starts a separate slab during extras packet application. Its live-latch
  artwork lookup can succeed before RemoteControlBoard seats the new occupancy.
- That successful lookup transferred the artwork reference, not ownership of the stationary
  renderer. The old renderer remained enabled until its independent seating update.
- Even when seating noticed the departure, RemoteBoardCard.Set(null) began the normal 0.30 s
  dock-vanish animation. Thus waiting for an empty mask alone could not remove the overlap.

## Changes

A drawable semantic flight captures/dresses its source before transferring the stationary
renderer immediately through Blank. A local card/actor fence prevents cached/stale seating
from recreating the same source, including after the arc ends. Empty seating and a different card retire that departure generation; a focus change clears
only the panel fence, allowing an unacknowledged flight to reassert its retained identity on
return. Actor/slot generations are assigned at event receipt, including pending flights, so a
new incoming flight supersedes older departures permanently. Explicit incoming ownership and
landing rearm the seat without depending on receipt of an intermediate empty snapshot. Transfers do not hide a
known replacement card. Anonymous covered sources retain their covered state and use the
same empty/focus/incoming handover lifecycle; no private card identity is added to the wire.
Ordinary departures without a flight keep the original dock-vanish animation.

Burn flights use the same source fence only after owner release and once their flight artwork
is drawable; the native stationary burn hold and release scheduling remain unchanged.
Active departures also transfer the actual active panel immediately. Slot arrivals, including
short-rest redraws, own their destination while in flight. Active/recess landing refresh happens
in the completion frame, scoped to the source actor and a non-exhausted displayed character.
This avoids a second stationary card during the arc and a frame with neither card at landing.
The normal animation ticks/board refresh order is retained. Fences precede native Set cache
checks; hand-fan visibility refresh runs on flight edges, with the existing layout pass covering
intermediate frames.

The review also found that Discard → HandFan recovery asked the departed-recess face cache and
never excluded its destination fan cell. Recovery now carries the actor and native restored
hand-list seat/count using the existing flight-source record. The receiver checks both count
and index, resolves that card locally, finds its current ordered fan seat and withholds the
corresponding slab until landing. A closed fan still receives a visible flight to the live palm
anchor; an open fan's world endpoint is captured once, matching VRCard.FlyFromPile rather than
retargeting it when the hand moves. Short-rest arrival artwork refusal no longer falls through
into an unrelated departed-recess identity. All remaining sender call sites now carry their
actual character actor, retaining provenance across focus changes and character mirrors.

## Reviewed flight families

| Family | Local ownership | Remote handover |
| --- | --- | --- |
| Round card → discard/burn | Same VRCard leaves its seat; burn completion gates native burn exits | Exact source fence; burn mirror retains owner release signal |
| Round card → active | Same VRCard flies to the active cell | Source transfers immediately; active cell owns only after landing |
| Active card → discard/burn | Captured original active seat and native pile fate | Exact active panel transfers; pending source waits for model destination |
| Short-rest offer/redraw | Same VRCard flies discard → recess / recess → discard | Destination/source ownership; original covered remote policy unchanged |
| Damage/long-rest burn | Native burn completes before local flight reports release | Existing BurnReleasePolicy claims and release event remain authoritative |
| Pick-page discard/restart recovery | Same VRCards fly to discard and back into the hand | Actor provenance; returned hand seat resolved and excluded during recovery |
| Hand/held/dock return and pile browsers | Existing local pose/reflow ownership | Existing held-seat exclusion/return reflow remains separate; no extra generic flight |

Source does not emit generic HandFan-origin events. Local controlled cards remain face-up;
remote selection concealment, remote covered short-rest burns and public 3D-map presentation
are unchanged. Owner burn-wait and flight duration/easing are unchanged. There is no new delay,
network tick, allocation per card per frame, TLV or packet grammar.

## Validation and limits

The source-extracted production transfer/seating prefixes execute against a renderer test
host: 782 assertions cover both seats, front/back states, 180 stale refreshes, another actor,
replacement cards, anonymous cards, empty acknowledgement and cancellation of an already
running dock crumble. Production integration assertions verify call order, arrival
ownership, recovery count/address resolution, frozen recovery endpoint and exhausted/focus
cleanup. Actual extracted flight and board orchestration also exercises focus-away/back, overlapping
same-card arrival/departure generations, native acknowledgement retirement and suppressed
wire-empty Slot0 preserving Slot1. Eight negative controls remove immediate transfer, permit
stale repaint, remove arrival ownership, permit an older generation to reclaim a newer card,
ignore retirement, substitute the drawn pose for the home, retain active arrival grace after
actual landing, and let an older active generation reclaim a newer card. Each fails at its
intended defect. The renderer host is not Unity and
these tests do not prove final headset geometry or shader pixels.

The worker strict Release build has zero warnings/errors. The integrator runs all required
checks after merging. Hardware replay must cover paired Slot0/Slot1 exits, active arrivals and
exits, redraw/accept/cancel short rests, damage and long-rest burns, open/closed recovery,
character switching, and delayed/coalesced snapshots. Current evidence establishes the prior
race and native vanish overlap, not the final hardware outcome.

## Final recovery geometry and active landing review

Recovery now captures the fan layout's exact local home position, rotation and scale before
hover pop and standing reflow, then composes them through the actual fan parent once. An open
fan therefore uses its home rotation rather than board rotation; a closed fan uses the same
head-facing palm frame as CardFan.TryResolveArrivalPose. Width converts the captured pile width
into hand-parent scale and follows that parent during the arc, retaining native exchange home
scale without hover enlargement. On landing only the corresponding card snaps to its current
home position/rotation/scale, clears pop and becomes visible. Other fan seats remain untouched.

Actual active landing now overrides only that card's model-first fallback deadline and skips a
second materialize ramp. Previously seated active cards also yield to a subsequently drawable
incoming flight. Actor/card generations prevent an older artwork-delayed arrival or departure
from reclaiming a newer transition, including its first drawable frame. Other missing events
retain the existing bounded arrival grace. The new extracted geometry, landing and generation
checks specifically distinguish a drawn/reflow pose from the native home, verify rotation/scale
and same-frame handoff, and exercise late active grace plus superseding active transitions.
