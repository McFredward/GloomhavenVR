# Build 629: native outcome and complete immersive-flight review

This review supersedes the merchant outcome row and the “convergent release”
claim in `NPC-626-FLIGHTS.md`. A successful sale is not a cancellation: its original
must end at the merchant. Confirmation presses alone do not establish an outcome.
The current paired hardware inputs identify Build 627 on both peers. No new flight
screenshots were supplied for this outcome extension.

## Source defects addressed

The earlier merchant implementation unconditionally called `ReleaseOffering`
when the native confirmation closed. That sent a sold owned copy back to the
seller's wrist before the inventory update removed it. It also erased a confirmed
request when the visitor immediately changed character or left. The request now
observes the original character's actual inventory result and preserves only its
specific original presentation until that result, or the existing eight-second
failure deadline. No game ownership is written by this presentation code.

The private town lane could retire a real return flight when its visitor started
interacting with a different NPC. Exact item and ability face/body originals now
remain in the existing independent cosmetic stock lane through their native
return clocks. This does not retain an old NPC's gameplay controller or lock.

Offered originals are prepared in that lane while still offered, explicitly
hidden, with inert native observer bindings constructed before reveal. A real
return's numeric root and record 107 then expose those same originals without
waiting for a second artwork snapshot. Preparing bytes without constructing the
inert receiver binding was insufficient: a first numeric update cannot create
an absent original.

A purchase introduces a new ownership reference. Its one canonical native
ItemChip is now prepared from the read-only stock model while the stock sample
is offered, outside the player's inventory/fan membership and with no collider.
Actual inventory success adopts that same chip and ItemCardUI onto the acquired
CItem reference. The native `ItemCardUI.UpdateState` only updates the original
card effects and UI `lastState`; its decompiled implementation has no ownership
mutation or trade callback. Adoption does not call Show, respawn the pool widget,
rebuild its hierarchy or add a stock “sold out” overlay. Cancellation destroys
this unowned preparation. Native inventory refresh duplicates are removed from
pending emergence/retirement lists before destruction; the next owner Tick
refreshes its inventory census before judging the acquired copy.

## Live flight inventory

| Cause and actual original | Native outcome / destination | Shared presentation |
| --- | --- | --- |
| Merchant stock released away from palm | Same borrowed stock sample returns to its cabinet slot | Existing token 0.35 s SmoothStep and record 107; canonical avatar transport remains the sole held author. |
| Borrowed stock purchase cancelled, replaced, refused or timed out | Borrowed sample returns to cabinet; prepared unowned ItemChip is destroyed | Original token return; no invented owned copy or player-fan arrival. |
| Borrowed stock purchase succeeds | Actual acquired CItem adopts the prepared native original and flies from merchant palm to its actual fan seat, or closed wrist | Existing ItemChip exponential glide / closed-fan back-ease collapse; cabinet sample restoration does not generate a simultaneous second return. |
| Owned sale succeeds | Exact owned original absorbs at merchant palm | Native ItemChip back-ease collapse; does not return to seller fan. Face/body persist through local chip retirement until the source completes. |
| Owned offer cancelled, replaced, refused or timed out | Same owned copy returns to its original fan/home | Existing open exponential glide or closed-fan collapse, including canonical face restoration and non-unit parent scale. |
| Confirmed merchant request followed by immediate walk-away or character switch | Await original character's inventory, then use the same success/cancellation outcome above | Bounded retained original, independent cosmetic lane, no old NPC lock or complete fan retained. |
| Enchantress offer cancelled, swapped or abandoned | Same still-owned ability returns to saved ordering/home | `ReturnPresentation` and `CardsDriver.ReturnTownOffering` use authentic 0.45 s `VRCard.FlyFromPile`, SmootherStep and native arc. Its launch freezes the actual world endpoint; no additional sampled-wrist path is introduced. |
| Enchantress enhancement succeeds | Native enhancement updates the offered original; it remains parked for another enhancement | No fictitious completion flight. Explicit withdrawal later uses its original return. |
| Enchantress character/loadout changes ownership source | Old character's card leaves through existing off-scenario vanish; never joins another character's fan | Existing source-owned retirement, not a false home destination. The retained face/body lane is independent of current private service generation. |
| Priestess purse release / donation / blessing | Accepted native return, sink and blessing lifecycle | Record 106 and existing per-render-frame holder path are unchanged. Local pre-drop ghost remains local by explicit user ruling. |
| Scenario action slot discard/burn | Actual native fate chooses discard or burnt pile | Existing `TryStartFlyToPile` / `TryStartBurnFly`, burn barrier, `ReportCardFx` exact source and completion provenance. No new fate/count heuristic. |
| Short-rest offer, redraw and burn | Discard → offered slot, replaced slot → discard, actual burn → burnt pile after native animation | Existing original clocks/source suppression. Only observer short-rest burn is covered; local controlled cards remain face-up. |
| Active arrival/removal/reflow | Native active home or actual terminal fate | Existing active source report and owner card-lerp speed. Historical “remote snap” ledger commentary does not describe current live code. |
| Scenario/map held ability/item cards and fan returns | Same actual original, holder or fan/home | Approved canonical avatar/card transport; no alternate sampled tracked-hand implementation. |

All map card surfaces remain public. Scenario selection observer fans/held/placed
cards remain covered; action and damage-sacrifice choices remain open. Coverage
never hides local controlled-character cards. Card pre-drop guides and the temple
purse pre-drop ghost retain only the expressly approved local exceptions.

## Focused evidence and its limits

- Native Unity handoff/outcome scope: **207 assertions**, with actual production
  Tick ordering, merchant callback wrappers, inspection lifecycle, native chip
  return/getter/collapse methods and real Unity transforms. Three world scales
  (0.05, 1, 198.12), rotated wrist/shared frames, inventory success versus cancel,
  sale versus purchase, both immediate walk-away/character switch outcomes,
  delayed front readiness, two subsequent owner Ticks and restored grabbability.
  Four precise controls reject seller-fan sale, confirmed-reset cancellation,
  double purchase flight and replacement of the already prepared original.
- StockSync → actual capture → bounded send queue/fragment receiver → inert
  original binding → record-107 reveal: **480 assertions** including existing
  exact return and original-print observer probes. Offered merchant, mage and
  prepared purchased originals are exercised both with prior observer preparation
  and when the native return starts before any original reaches the observer.
  The small fixture's cold original delivery requires four turns normally, five
  with the concurrent early return; numeric reveal requires zero new artwork.
  The .35 s early flight remains live when the originals appear and keeps moving
  between packets. This is not a manually warmed RegisterTemplate/Receive test.
- Additional precise controls reject an absent inert hidden preparation and a
  second visible prepared offered original. Existing per-render motion, child
  rotation, held-duplicate and terminal-body controls remain in the affected suite.
- Full plugin strict compile passes with zero warnings/errors. The primary agent
  runs the complete gate once on the final integrated tree.

The handoff fixture explicitly supplies native inventory/confirmation outcomes
and pooled artwork construction. The observer fixture supplies the native return
sample at that boundary; the independent owner test binds the actual getter and
curve. Neither establishes hardware latency, actual frame pictures or delivery
of genuinely lost packets. The finite-queue fixture uses complete original
metadata without unrelated public-cabinet/background contention; that broader
first-visible delivery census is covered by the separate delivery worker.

Failures are retained as evidence, not counted as passed controls: an initially
unregistered sample, wrong stock fragment-lane seed, accidental mixed private/
stock queue fixture, missing fixture fields, and a cold-art fixture which became
warm through the new source preparation. The original source sampler eligibility
failure led to clearing its stale emerge author before a real inspection return.

Compact receipt paths in the worker's `.planning/debug/npc629-flights/`:

- `prepared-purchase-native/run-aigj0wad`: final 207-assertion owner scope and four controls.
- `purchased-observer/run-hr2sr4o3`: 480-assertion source-to-observer scope and source hashes.
- `prepared-control/run-01j6qzcj`: missing inert-preparation control.
- `prepared-duplicate-control/run-adx_beic`: prepared duplicate control.
- `purchase-build.log`: strict compile.
- `original-itemcardui.cs`: read-only decompile of shipped `GH.Runtime.dll` ItemCardUI.
