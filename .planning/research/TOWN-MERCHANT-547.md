# Build 547 — open merchant counter and physical inspection

The maintainer rejected filing drawers and reported merchandise disappearing when picked
up. No new hardware logs accompany this revision. The previous implementation's pickup
tests stubbed both `TownServiceToken` and `CardGripPose`, so they could not detect its
world/local unit mismatch.

## Source findings

`TownServiceToken.OnGrab` measured the physical card's height using world-space corners,
then fed that height to the anchor-local reading-pose solver. The subsequent
`GrabAnchor.TransformPoint` scaled the offset a second time. The archived hardware run
used a world scale of 198.12, enough to move a gripped sample far outside the player's
view. The integrator's `3c10cc36` corrects the measurement in grab-anchor space.

The actual `GH.Runtime` implementations of `ObjectPool.SpawnCard/GetCardInstance`,
`ItemCardUI.Show/OnRemovedFromPool/OnReturnedToPool` and
`UIPartyItemInventoryTooltip.Build/Show/Hide` were inspected. A borrowed visible card
is removed from the pool before the detail tooltip borrows its own card. Native Show
activates the card and artwork lifecycle. These paths do not explain a stolen or
recycled held sample; the coordinate error does.

## Presentation

- No active drawer, pagination, category buttons or closed stock compartment remains.
- The main curved counter exposes 192 distinct stock entries in 24 columns and eight
  terraces. Cards occupy their own full face footprint, including the native price row.
  The rear edge clears the resident's working ledger and coins.
- Owned copies occupy open, authored side returns, 64 per module. More modules are
  added when needed; overflow stock uses the same mechanism. Every native item copy
  keeps its identity, including the complete Guildmaster roster's 512 owned items.
- Surviving physical cards retain their positions during stock changes. Picking up
  remains free inspection. Only the original eligible buy/sell drop path can transact.
- Return furniture uses `Counter/CounterReturn`, at an authored floor origin; its
  material fade and exact transform are available for ordinary town mirror publication.

`TownServiceMerchantLayout` is the geometry contract for the generated furniture.
Main card centers: x=(column−11.5)×.15, y=row×.025,
z=−1.32+row×.13+.16×(x/1.725)^2, relative to the .970-m worktop.
Return card centers: x=(column−3.5)×.15, y=row×.025, z=−.455+row×.13.
The main counter needs 3.8 m width and extends from z−1.45 through +.4.
Returns measure 1.30×1.15 m and expand alternately left/right without entering the NPC.

## Verification

Focused Unity 2021.3.5 catalog harness: **6,945 assertions**, **13 negative controls**.
It now compiles the actual production token and reading-pose solver. Cases cover both
hands at scales .05, 1, 2 and 198.12; real held transforms/canvas activation; inspector
details available for publishing; cancellation; native transaction routing boundaries;
all 673 maximum stock/owned card colliders; stock stability; disposal and input rollback.
Native controllers and network service state remain explicit fixture boundaries; this
test does not claim to execute a running game's transaction or headset rendering.

Strict Release build: **zero warnings, zero errors**. Generated furniture, full observer
publication and a hardware test still require integration validation.
