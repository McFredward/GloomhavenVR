# Local burn driver review — build 517

Base: `1915fd42` (`dev`, ModBuild 516). Owned scope: `Cards/Driver`, the existing
burn-layout harness and this report. This lane does not change native card effects,
original artwork, gameplay callbacks, wire grammar, or the release branch.

## Source-proven defect

The local flight watcher remembered `AbilityCardUI` widgets, then cleared its complete
baseline and rebuilt it from the current pile-widget snapshot on every tick. A rebuilt
widget for the same lost native card, or one transiently absent pile-widget list, could
therefore qualify a historical burn as fresh again. Separate wrappers for one original
could also acquire separate holds. These are repeat-hold/flight hazards. The driver has
no native burn start/reset calls, so this finding alone does not explain the reported
return to blue and restarted burn timeline; native effect ownership is reviewed separately.

Completed claims now use the original `CAbilityCard`. They survive pooled widget
replacement and missing UI samples. Only authoritative removal from both native lost
lists re-arms the original for a real later burn. A newly presented hand seeds its
historical baseline from native lost and permanently-lost models even if its UI widgets
have not been built yet. Existing owner matching still prevents another character's
historical card from becoming a fresh local burn.

Pending flight holds retain the original widget and position, but new widgets for that
same original and actor cannot create a second hold. Round/active exits, the live-card
burn path and the fallback slab claim the original immediately on launch. Native effect
and owning-hand completion remain the release authority; no animation is shortened by
a timeout. Character admission, damage batches, short/long rest and played lost actions
continue through the existing shared layout barrier.

## Integrator review follow-up

The wrapper-based previous dock/active sets do not prove that a round/active flight is
unique. Unlike the generic fresh-burn producer, this path intentionally accepts first
handoffs while the watched hand changes. Rejecting every historical known claim there
would break that behavior. A separate completed-presentation claim now rejects a second
wrapper only after a real flight/completion consumed that original. Historical baseline
seeding does not count as completion; real recovery clears both kinds of claim. The
pending-hold flush also discards an already completed original before releasing a flight.

## Verification

- Existing production burn-layout harness: **219 runtime assertions**, up from 204.
- **16 runtime negative controls**, including seven new faults covering forgotten model
  identity, treating a still-lost card as recovered, missing real recovery, duplicate
  holds for a rebuilt widget, absent historical native baselines, confusing historical
  baselines with completed presentations and missing completed-claim recovery.
- Eight added production bindings cover authoritative baseline admission, retained claims,
  the common flight hold, pending claims, and immediate round/active and slab claims.
- Scenarios include widget replacement during/after a burn, missing UI observations,
  permanent loss, actual recovery followed by another burn, initial hidden historical
  models and different-character ownership. Existing native completion, character change,
  incoming foreign owner progress and unbounded live-animation waits remain covered.
- Strict Release build: zero warnings and zero errors. The root integration runs the full
  required suite after merging all independent lanes.

This is source and automated evidence. It does not establish a headset result or that
these driver hazards caused the user's latest short-rest symptom.

## Historical consumed-item host hook

A separate read-only item audit found another replay entry: `ItemsPile.Populate` can
recreate a chip for the same consumed original, while native `ItemCardUI.OnReturnedToPool`
resets `lastState` to `None`. The new host's `Show()` then requests the consumed effect
again. Native `ItemCardEffects` uses a 0.001-second ramp here, so this is chiefly a fresh
material/smoke start rather than the two-second ability burn. Remote item clones already
paint the settled state without calling `Show`/`UpdateState`.

`TryHostRealCard` now calls `ItemBurnPlayback.ObserveInitialState(cardUI)` immediately
after assigning `cardUI.item` and before `Show`. The native playback lane owns the helper,
its original no-animation material initialization, and its tests. This hook does not alter
retained-host `TickUseFx` updates or infer a new consumption from widget creation. This
checkpoint requires that companion helper; the integration build validates both together.
