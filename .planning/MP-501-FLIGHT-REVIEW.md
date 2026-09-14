# Independent review: card flight ownership

Reviewed 2026-09-14 against the `codex/mp501-flights` lane based on
`7745d90c8`, including the final ownership checkpoint `72c756a7`. Hardware evidence is in `MP-501-EVIDENCE.md`. This review concerns
source ownership and interleaved events; it does not certify headset pixels.

## Corrections prompted by independent review

- **Compaction skipped an unrelated card.** An arriving flight could own Slot0
  before the occupancy packet marked that slot occupied. Advancing the compact
  model cursor unconditionally then skipped the card belonging to Slot1. The
  suppression branch now advances only when the suppressed slot is wire-occupied.
- **Focus return lost the source fence.** Switching A → B cleared the panel fence,
  but a running A flight initially transferred its source only once. Returning to
  A could repaint its source. The flight now retains the actual transferred local
  card identity and reasserts that identity, never recapturing a replacement.
- **Reassertion could undo a legitimate acknowledgement.** Reasserting each tick
  initially recreated a fence after native seating acknowledged an empty slot or
  replacement card. A later ordinary hand placement of the same card could then
  remain invisible. Per-panel, per-owner retired generations now reject those old
  transfers after acknowledgement.
- **An older outgoing flight could override a newer incoming one.** Clearing the
  fence only at arrival start was insufficient: the older arc could recreate it
  before landing. Actor/slot generation ownership now rejects the older claim;
  incoming completion also prepares its own destination before seating it. A
  pending flight retains its original generation through retries.
- **A pre-empty slot could play a second appear animation at landing.** `Blank`
  can early-return while its materialise seed remains set after an ordinary
  covered-card vanish. `PrepareFlightArrival` now explicitly clears that seed,
  including the early-return case, so the flight lands without an extra dock fade.
- **Burn departures needed the same post-flight stale-state protection.** The
  existing burn mirror already blanked the source before showing its slab, but
  its temporary ownership ended with the arc. The lane adds the same generation
  fence at the burn's actual flight handover. Review additionally required this
  transfer to occur only when that flight is drawable; the owner-release event
  alone cannot justify hiding a source while public artwork is unavailable.
  The final `72c756a7` source was rechecked: `RemoteBurnFx.Tick` transfers only
  inside `showFlight && OwnsDisplayedBoard(b)`, immediately before the flight
  visibility write. `Handover` allocates its generation without blanking the
  source. This requested drawable guard is confirmed in the final checkpoint.

Normal short-rest random redraw excludes the first card from its candidates
(`CardsHandUI.PerformFinalShortRest`); the same-id overlap is an interleaving/rearm
test, not a claim that normal redraw randomly chooses the same card. Scripted
level-event overrides can select from the full discard population.

## Other checks

- Source capture/dressing precedes immediate source blanking, which precedes the
  flight object's visibility write. Ordinary no-flight departures retain their
  dock-vanish animation.
- Card and actor identity scope the stationary fence. A known replacement card
  rejects the old transfer. Anonymous covered cards retain their concealment.
- Incoming slot ownership prevents a second stationary copy during the arc.
  Completion is scoped to the same displayed source actor and rejects exhausted
  or hidden boards. Simultaneous slots retain independent ownership.
- Recovery addresses the authoritative restored hand list by actor, seat and
  count, then resolves the corresponding ordered fan cell locally. The count
  guard refuses an untrustworthy positional zip. No card identity is added to the
  wire. The world endpoint remains captured, matching local `FlyFromPile` rather
  than following later hand motion only on the observer.
- The actor/slot generation ledger clears on scenario-gate and avatar lifecycle
  teardown. The native board, fan, flight and burn tick order remains unchanged.

## Follow-up: native recovery home and active arrival

The final geometry pass compared `VRCard.FlyFromPile`, `CardFan.SetHome` layout,
and `CardFan.TrySeatArrival` against the observer. Local recovery captures the
pre-hover home position, parent-times-home rotation, and home scale. Sampling the
currently reflowing fan slab instead, or retaining board-plane rotation, therefore
changed the arc even when its card identity was correct. The follow-up retains the
remote layout output before hover and interpolation, including exchange scale;
closed-fan recovery resolves the palm seat and head-facing orientation. Its width
conversion accounts for the hand/rig scale independently of the board scale.

Independent review also identified a second handover issue: merely enabling the
hidden destination slab after the arc preserves its intermediate reflow and hover
pose. Native `VRCard.Update` explicitly settles at its home pose and scale. The
follow-up now settles only the completed card at its cached home position,
rotation, and scale, resets its pop, and then refreshes visibility. Actor identity
prevents a late completion from moving another displayed character's fan.

For active arrivals, a completed known flight must retire that card's model-first
arrival grace immediately and suppress a second materialise transition. An older
completion must also respect a newer active departure. This interleaving is
possible on the observer because `RemoteCardFx.Tick` pauses the arc clock while
public artwork is unavailable, although the owner has already landed and can use
the card. Native single-flight ownership alone cannot rule it out. The final follow-up
adds actor/card generations for active arrivals and departures, rejects stale
completion claims, and cancels superseded arrivals before their first drawable
frame. `CanDrawFlight` also requires the flight to remain active. A known running
flight now takes precedence over the seated-id shortcut, preventing a card that
settled during the artwork wait from repainting during the delayed arc.

## Validation

The reviewer independently ran `bash scripts/flight-timing-tests.sh` on the
ownership checkpoint: **762 assertions**, with **five negative controls** failing at their
intended defects. The updated harness executes extracted production transfer and
landing orchestration as well as panel policies. Cases include focus away/back,
newer same-card arrival, foreign/exhausted completion, retirement after empty and
replacement acknowledgement, and the compact cursor's wire-empty case.

The reviewer reran the final geometry/active follow-up harness: **781 assertions**
and the same **five negative controls** passed. Added cases execute extracted
home lookup and completion, parent-transformed position and rotation, exchange
scale without hover, late active grace, and active generation ownership. No
remaining source-backed blocker was found in these reviewed paths.

These are Unity renderer substitutes. Their successful execution establishes
the tested state transitions and ordering, not actual shader output, headset
timing or network packet delivery. The integrator owns final combined build and
gates; the next hardware test must still exercise the reported departures.
