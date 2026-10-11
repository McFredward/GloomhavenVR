# Corrected Build 587 peer trace — town multiplayer follow-up

## Evidence

The 29 September host and replacement `debug/remote` logs both report ModBuild 587.
The initially supplied Build 582 peer files did not belong to this run. The host and
peer load the same mod build and report the same 74,942,975-byte asset bundle. Their
BepInEx `GH` build stamps differ (14 July and 30 July 2026); that alone does not
identify which original game asset, if any, differs.

The peer's `LogOutput.log` first fails in `TownServiceMirror.TickRackClocks` at
`TownServiceMirror.Racks.cs:169`, where `GameObject.SetActive` is called on a dead
observer child. Subsequent failures escape the per-module catch because its
cleanup calls `SetActive` on that same destroyed host (`TownServiceMirror.cs:919`).
The peer reports 25 rate-limited `ApplyPending threw` windows totaling 7,334
failed calls; the host reports none. Other network phases continue, but town
presentation replay is skipped on those frames. A focused Unity negative control
reproduced the first failure before the repair.

The peer also reports eight `Original town-service template differs between
peers` failures for remote module `-1/2`, beginning before the rack exception.
The old report contains no template address or differing node key. This is a
structural check in `TownServiceBinding.Validate`, independent of the atlas
resolution errors addressed in Build 589. The peer reports 29 ambiguous
`AbilityCardSpriteAtlas` captures; the host reports 264 ambiguous
`Sarala-Regular SDF Atlas` and 40 `AbilityCardSpriteAtlas` captures. The
Build 589 descriptor fix remains justified, but it cannot cure a different
template structure.

The peer has no assigned character for the first 30 map-selection samples.
The native assignment later grants two characters, and the selection census
then shows a selected character. Its earlier `no owned map card` messages are
therefore expected for that first interval. After assignment, later enchantress
approaches still log `destination=Merchant`, despite 10/10 local ability-card
fronts, and no enchantress native visit opens. The peer also never opens a
Temple native visit after the merchant. The host does open Temple and physically
grabs the purse twice; the report that no purse appeared anywhere does not
describe every host visit.

The peer's `ARC ORDER NOT APPLIED` census does not by itself show a visible
map-fan ordering defect. All corresponding `MIRRORED ARC ORDER` rows have
`model=0`: that diagnostic samples the scenario `AbilityCardUI` buffer, which
the map fan does not use. The map fan builds its visible models in the owner's
record-44 order through `_mapArc`; the peer also reports visible map fronts.
The diagnostic must distinguish these two rendering paths before claiming a
1:1 ordering failure.

## Source corrections in Build 590

- Retire destroyed remote module hosts and children when their parent is replaced;
  rebuild them from the retained owner frames. A dead rack or body renderer must
  not throw out the other network presentations in the same frame. The per-module
  failure cleanup is safe for Unity-destroyed objects.
- Apply the enchantress's own local attention condition to her handoff. An offered
  hand in the merchant overlap must have a card-drop cue when that visitor owns
  a card; a parked merchant deal is still protected.
- A previous native Merchant/Enchantress destination can outlive its private fan.
  Walking closest to the priestess now opens Temple without first requiring a
  purse at the bowl, while an active offer remains protected. The enchantress
  cannot immediately steal the resulting Temple destination back on proximity
  alone.
- A bounded template-mismatch report now includes the template address,
  sender/observer structure signatures and node counts, and first different
  binding key. The structural equality guard stays intact; applying original
  values to a different hierarchy would be a new 1:1 defect.
- The card-fan order diagnostic is scoped to the scenario widget buffer; map
  fronts report their own map-arc order result. This changes logging only, not
  card order or reveal policy.

The old trace cannot numerically establish why the peer's gray offered item
appeared in the wrong position. The retained-frame recovery removes a proven
cause of missing remote modules, but a two-headset pose comparison remains
necessary. The template mismatch also remains unrooted until its new diagnostic
names the actual original widget. Do not label either headset outcome fixed on
the basis of a source check alone.

## Verification and next hardware pass

Focused Unity negative controls cover destroyed rack descendants and both
overlapping NPC handoff cases. The final integrated tree passed 14/14 source
checks, 80/80 runtime suites, 286,609 wire assertions, the strict Release build
with zero warnings/errors, and the EN/DE documentation check. The compiled-form
guard's exit code 1 records expected changes against its older pre-NPC baseline;
all test groups passed. These checks do not verify headset pixels.

Test Build 590 on both clients: switch every cabinet category, inspect card
fronts and page animation from both sides, take and offer a card, then move
between all three residents. Preserve both logs if the new template diagnostic
appears. Compare the offered item's world position from both viewpoints; it
remains unproven by the Build 587 trace.
