# Burn flight visibility — build 532

## Hardware evidence and limits

The current local `LogOutput.log` is build 531 / assembly 1.0.5.0 (line 17).
It records two completed native burn holds and live-card flight launches for
`ABILITY_CARD_RevivingEther`: lines 1276–1278 and 2035–2037. `Player.log` records the
model moving the card from Round to PermanentlyLost at lines 10232 and 16909.
The recovery results themselves return to Hand separately. Both Lost and
PermanentlyLost already target the Burnt pile; this is not a missing card-specific
rule or a recovery mistakenly applied to the recovery ability itself.

The user's missing visible flight is therefore not explained by an absent launch.
The logs contain no per-flight surface/tween state sufficient to attribute the
exact missing headset picture. A flight ledger entry establishes a producer call,
not visible pixels or successful arrival. Historical remote build 500 logs cannot
establish build-531 peer behavior.

## Source-proven defects addressed

`LaunchBurnFlight` and the pile watcher accept an active, unheld, non-flying card
without excluding an already-running dock vanish. The normal rebuild burn helper
has a vanishing guard, but that guard does not cover those other launch paths.
`VRCard.FlyToPile` previously canceled only an appear, leaving a vanish's reduced
canvas alpha, hidden body and obsolete park callback in place. Its transform could
therefore fly while its surface remained faded; the old vanish would resume after
flight ownership ended. `FlyFromPile` restored alpha but had the same stale vanish
and hidden-body lifetime gap.

Both entry paths now take exclusive presentation ownership: retire the obsolete
appear/vanish and vanish callback, restore the mod-owned face alpha and body, and
then capture/start the flight. Native burnt materials, the completed burn look,
card identity, native game callbacks and authoritative piles remain untouched.
A bounded report (at most 12 per process, requiring mod Debug verbosity) records
actual vanish-to-flight handovers for hardware attribution; ordinary logging gains
no routine stream.

Separately, `RefreshBurnLayoutBarrier` observed native burn playback while blocking
`FlushBurnHolds`, but initialized each hold's `ArtworkSeen` to false. Because the
individual flight gate was not polled until the global barrier cleared, a full
observed two-second burn could be falsely reported as `START GRACE`. The barrier now
retains that observation with the original hold timestamp, including a timeline
that starts after the model loss. This changes diagnostic attribution, not burn
completion timing.

## Validation

- Production-extracted burn-layout harness: 228 assertions, 16 negative controls.
- Production-extracted flight timing/lifecycle harness: 818 assertions, 13 negative
  controls. New cases cover zero/partial/full face alpha, appearing and vanishing
  combinations, body restoration and obsolete callback retirement. Source bindings
  require both entry paths to prepare the surface before claiming their tween.
- Strict Release build: zero warnings and errors.

Final visible Reviving Ether departure and owner/peer parity require another
headset check. The present evidence does not prove that the reachable vanish
handover defect was the sole cause of either reported occurrence.
