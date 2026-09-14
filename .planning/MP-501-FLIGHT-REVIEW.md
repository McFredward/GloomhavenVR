# Independent review: card flight ownership

Reviewed 2026-09-14 against the evolving `codex/mp501-flights` lane based on
`7745d90c8`. Hardware evidence is in `MP-501-EVIDENCE.md`. This review concerns
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

## Validation

The reviewer independently ran `bash scripts/flight-timing-tests.sh` on the
candidate: **762 assertions**, with **five negative controls** failing at their
intended defects. The updated harness executes extracted production transfer and
landing orchestration as well as panel policies. Cases include focus away/back,
newer same-card arrival, foreign/exhausted completion, retirement after empty and
replacement acknowledgement, and the compact cursor's wire-empty case.

These are Unity renderer substitutes. Their successful execution establishes
the tested state transitions and ordering, not actual shader output, headset
timing or network packet delivery. The integrator owns final combined build and
gates; the next hardware test must still exercise the reported departures.
