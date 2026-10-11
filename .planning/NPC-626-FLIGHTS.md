# Build 626: original card flight review

This is a source and runtime-boundary review, not headset acceptance. The paired
hardware input is Build 625 on both peers. No new screenshots were supplied.
The priestess/purse path is accepted by the maintainer and is preserved.

## Findings fixed in this round

- A lifted merchant stock sample was excluded from its visitor-original lane while
  held. Its first return therefore raced admission of a new original during a
  0.35-second flight. The same original is now prepared while held; the existing
  avatar transport remains its sole visible held author. Releasing reveals the
  prepared original. The original endpoint remains registered for its existing
  short grace interval before retirement.
- Merchant and enchantress card returns had numeric pose samples, but no actual
  native flight clock. World-pose interpolation between town events could neither
  reproduce the original curve nor stay smooth between events. Record 107 now
  carries the source-owned age, duration, endpoints, curve and exact child transform.
  The observer advances these original parts on every render frame.
- A static original could publish a return clock without a numeric root author.
  The first real sender/receiver proof failed at this boundary. A return now
  authors its matching root even when the warmed source has not moved.
- A closed item-fan collapse stores a local scale under its fan parent. Treating
  this as a world scale reduced the observer card incorrectly at non-unit world
  scales. Capture now includes the actual parent's scale.
- New return clocks and their matching roots get bounded, coherent priority
  admission before routine material/hover values. Only a new revision has this
  privilege; later age samples retain ordinary finite turns. Lossless packing
  trims a first clock/root pair together. Event size and cadence stay 864 bytes
  and 15 Hz.

No rendered card image is sent by the new record. Original face/body identity
continues through the existing template/asset metadata path. The record contains
38 floats and identity/affinity fields, and leaves purse record 106 byte-exact.

## Inventory of live flight paths

| Native trigger | Owner presentation | Observer presentation and source reviewed |
| --- | --- | --- |
| Round/action card leaves its slot | `CardsDriver.TryStartFlyToPile`, `VRCard.FlyToPile` choose the actual discard/burn fate | Existing `ReportCardFx` source provenance and `RemoteCardFx` history/owner curve. Burn ownership, source suppression and completion history remain unchanged. |
| Burn after card effect, damage choice, short or long rest | `TryStartBurnFly`, `LaunchBurnFlight`, native burn barrier, fallback `BurnSlab` | Existing owner-authorized release/completion path, covered-burn provenance and source generation checks. No new count-based or time-based second flight is introduced. |
| Event-selection commit and revision | `FlyLockedPicksToPile`, `DrainPickReturnFlight` | Existing slot/pile/fan semantic endpoints and source identity, with the same native return seat and curve. |
| Short-rest offered card and redraw | `FlyFromPile` to its offered slot; `FlyShortRestCardToDiscard` for the replaced original | Existing `Discard -> Slot0` / `Slot0 -> Discard` events and source matching. Local controlled cards stay visible; remote short-rest burn flights retain the covered flag. |
| Active-card arrival, removal and reflow | Actual active source supplied by `CardsDriver`, then active home movement | Existing `ReportCardFx(..., Active, source)` plus `RemoteActiveCards.Move` with the owner's card lerp speed. The old ledger comments about a snap are historical and do not describe current live code. |
| Scenario and map ability/item fans and held cards | Existing `VRCard`, `ItemChip`, fan home/emerge/collapse and pinch hold paths | Existing canonical avatar/card identity transport. Town stock preparation is masked while this transport holds its one visible original. No alternate tracked-hand smoother was introduced. |
| Merchant stock release outside the purchase hand | Actual `TownServiceToken.BeginReturn`, original 0.35-second SmoothStep back to its cabinet slot | Same source and same endpoint, now exact record 107 per render frame. A held original is already prepared when possible; it retires after the original endpoint grace. |
| Merchant buy/sell/cancel, manual reclaim, replacement or leaving | `TownServiceMerchantHandoff.ReleaseOffering`: stock calls `ReturnOffering`; owned chip calls `ItemsPile.ResumeInspection` | Both terminal and replacement paths converge on the same originals. Stock uses its actual SmoothStep; owned item uses actual exponential home glide or closed-fan back-ease collapse. New revision, held/offered state and root-hand affinity prevent replay over a later pickup. |
| Enchantress cancel, returned choice, replacement and leaving | `ReleaseOffering` / `BeginReturn` adopt `ReturnPresentation`; `CardsDriver.ReturnTownOffering` gives the original `VRCard.FlyFromPile` home flight | The actual returning face/body is published with captured card identity. Record 107 reads that card's real elapsed/duration, SmootherStep and `VRCard.FlyArcOffset`; printed child geometry stays attached throughout its rotation and glide. |
| Priestess purse release, acceptance and blessing | Existing actual purse return/sink/settlement clock | Accepted record 106 and its lifecycle are untouched. Existing per-frame approved holder interpolation remains the author. |

Map surfaces are public locally and remotely. Scenario visibility still follows
the current contract: action choices face-up; selection observer fan/held/placed
cards covered; remote short-rest burn flights covered. None of these observer
rules conceal local controlled-character cards. Town pre-drop guides remain
visitor-local as explicitly approved; actual held/offered/returning objects stay
shared.

## Evidence and limits

- The actual stock token grab/release, merchant-palm cancellation, original Tick,
  exact face geometry and endpoint retirement are compared at intermediate ages
  under world scales 0.62, 1 and 2.6. A new pickup invalidates the previous clock.
- The mirror proof uses real template registration, native capture, codec,
  receiver admission and render-frame playback. It verifies prepared-held
  suppression, first release, original child position/rotation and reordered
  old held metadata. Independent causal controls remove per-frame playback,
  corrupt original child orientation or restore the held duplicate and must fail
  the corresponding boundary assertion.
- The codec has an independent Python-struct golden for record 107 and preserves
  the independent record-106 golden. Busy live/fan/recovery contention admits
  three first original-part clocks within the first two 15 Hz turns, each with
  its matching root, and still progresses all 120 ordinary bindings.
- Source-bound owner phase/getter probes distinguish an actual local curve from
  merely testing the analytic helper against itself. Original game callbacks
  remain external boundaries to these focused probes; their convergent release
  paths are inventoried above and are also covered by the existing full gate.
- Truly lost/cold original artwork still needs metadata admission. Warming a held
  original reduces the known first-return race but does not guarantee delivery
  of packets that are lost. An immediate pickup/release under real hardware
  contention is a necessary headset check; synthetic passing checks cannot
  establish headset picture or network timing.

Focused results before integration:

| Check | Result |
| --- | --- |
| Actual stock token plus source-bound VRCard/ItemChip phase/getters | 279 assertions passed; ability arc, open exponential glide and closed collapse include non-unit parent scale and rotated hand/shared frames. |
| Original mirror return and complete native print sender/observer | 207 assertions passed; three flight causal controls rejected at their intended boundary. |
| Compiled wire harness, including 106/107 independent goldens and busy admission | 308,352 assertions passed. |

The primary agent runs the complete gate on the integrated tree. Worker attempts
that failed before these results are retained: missing fixture declarations,
wrong fixture method indentation and an existing-RectTransform misuse were
compilation/setup failures, not successful negative controls. The first
no-per-render-frame return failure was a production boundary defect and directly
led to the matching numeric-root fix. An accidentally started old broad gate was
interrupted rather than represented as complete validation.
