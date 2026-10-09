# Build 660 native return geometry review

Source checkpoint: `472290be1` plus the final restriction to declared native
VRCard/ItemChip owner types, based on current dev Build659 plus the separately
owned lifecycle pose/layout baseline and exact procedural-body factory. The new
hardware inputs frozen at `.planning/debug/npc660/inputs` identify **Build658
e92ef8e53 on both peers**. Both Player logs retain Debug town-service traces;
the LogOutput files omit those detailed rows. The
logs establish this run and its native lifecycle, but contain no per-surface
render output and do not uniquely identify the reported merchant split's time.
No new video is assigned to that specific split. The earlier Build656 video is
historical evidence, not a timestamp correlation with these new logs.

## What earlier repairs proved, and what they missed

| Repair | Actual useful coverage | Remaining assumption exposed here |
| --- | --- | --- |
| 629 | Real merchant inventory outcome and coordinator ordering; successful sale versus cancellation, prepared purchase original adoption, independent return lane | Native outcome correctness does not establish every observer frame's physical geometry. Its observer flight sampler and native owner outcome proof are separate boundaries. |
| 646 | Native easing evaluated every render and moving approved destination holder; genuine source removal | The moving-holder fixture explicitly registered a held-hand author that the actual return publisher did not register. Rigid print roots and synchronous receipts missed the publisher-affinity and jitter defects repaired in655. |
| 655 | Actual active ItemChip exponential sampler/update, destination-hand admission from StockSync, retained source clock under variable delay and independent legacy107 body/front datagrams | It extracted the active glide branch but omitted ItemChip's subsequent ordinary settle branch. Its finite comparison ends before a long post-arrival period and keeps root layout fixed. The declared Build656 legacy sender is intentionally separate from the current113 sender. |
| 658 | Current113 atomic member geometry, first hidden preparation, large physical groups, capped native source-age rates, real shipped AbilityCard/action partitions | Its actual native comparison stops at frame42, before the .55s return ends, then checks completion/retirement separately. Source/observer readbacks are performed after an explicitly declared pose normalization. Root anchor/pivot/extent stays constant. Native offered withdrawal is a separate manually driven boundary. |

These fixes remain useful; the failure was treating their limited model/render
boundaries as sufficient evidence for the whole native source lifecycle. A
correct curve and coherent transform origin do not prove the printed plane is
correct, or that the same result survives the first ordinary root after arrival.
The now-stable offered overlay depth/material behavior is preserved.

## New causal failures

1. `PreserveReturningCardHeader` correctly freezes artwork pose during an active
   flight. On sampler termination, the quiet immutable header could take the
   `ReferenceEquals(previous, frame)` shortcut. A stale in-flight Kind1 root then
   retargeted the already settled observer backwards. A direct actual VRCard
   sampler reproduces this beyond native completion; the narrow old-terminal
   control fails at frame54 with a0.265mm root displacement. This control alone
   does not establish the size or precise timing of the reported headset split.
2. Actual `ItemChip.PrepareInspectionReturn` invokes
   `CanonicalizeHostedFace`, changing a pooled face's pivot, anchors and local
   pose. The existing113 child records carry ten TRS floats, not root rect
   layout. With the old(.2,.8) pivot and the new(.5,.5) source pivot, the printed
   centre is **56.56mm wrong on the first received frame6**, although the
   transform origin and body trajectory are right. The test calls that actual
   source method; it does not manually correct observer geometry.
3. Per-render native return painting overwrote physical host/root TRS, while
   generic UI interpolation retained older enclosing-canvas/root targets.
   After the native glide ended, independently interpolated parent/child roots
   stopped cancelling. Removing the new reconciliation produces **72 different
   pixels out of977** at merchant frame61 in the unadjusted owner/observer
   render comparison. Earlier source isolation also measured37.99mm printed
   centre drift at that boundary.
4. After native arrival, a destination-hand registration could fall back to
   Hand0 while the local ItemChip remained in its real destination rig. Moving
   that rig after frame75 reproduced a0.539mm gap on its first moved frame76.
   The return reference must retain that verified affinity while it still
   describes the same source, hand and actual physical hierarchy.
5. Applying a newly received rect dependency before a larger replacement cohort
   finishes changes the printed plane while its body still uses the prior child
   geometry. The24-part proof and old-staging control reproduce that partial
   update. An initial follow-up also exposed an old cached **host** pivot being
   restored after detached-root normalization; the reconciliation now takes the
   normalized host geometry and reasserts only the binding root's atomic layout.

## Source repair

The capture lane publishes one final exact physical Kind8 receipt when an
actual native source finishes near its native deadline, then refreshes ordinary
roots even if artwork is quiet. The final receipt contains the actual component
root and all existing child geometry at one source instant. It uses existing
record113 and existing Kind8/Kind1 semantics; native callbacks, inventory,
duration, easing and event budget are untouched.

Native completion requires the same registered sampler/hand, actual original
ancestry and available source. A held VRCard, held/offered ItemChip, changed
sampler/hand or detached destination-rig hierarchy cannot manufacture an arrival.
Post-arrival hand affinity has the same checks and publishes a live ordinary
root immediately when revoked. Normal held-hand authors take precedence.

Live RectTransform root anchors, pivot and extent use the existing18-float Kind2
Transform property. The sender freezes this exact entry with its corresponding
return snapshot and includes it in that part's atomic bounded packet selection.
Budget trimming cannot separate the dependency and return subset. Ordinary turns
cannot steal that dependency while its native return is active. It remains dirty
for later cohorts to recover loss, without changing the existing864-byte event.

The receiver defers this root layout while the replacement cohort is incomplete
or ahead of its retained source clock. Every still-valid prior physical picture
continues. Completed child geometry and layout activate together. A final native
picture reconciles only the two physical nodes, host and binding root, with the
generic interpolation state. Active/alpha/color/renderer state and every other
native child clock remain unchanged. The first ordinary root after flight flushes
only those physical pose/layout channels to its exact native target.

The new `map.cardbody.<widthBits>.<heightBits>.p|` procedural body family joins
hidden return preparation alongside legacy `map.cardbody|`. The actual local body
factory/owner-size provenance belongs to the separate delivery repair.

## Runtime proof and explicit boundaries

Run `python3 scripts/npc660-return-runtime/run.py`. The positive uses production
capture, current atomic budget, codecs, receiving, inert binding, final return
writer, actual production CardMesh/CardBody and real Unity2021.3.5 rendering.
It compiles the actual VRCard sampler and capped native update statements, actual
ItemChip sampler, active exponential update **and post-glide settle branch**, and
actual `CanonicalizeHostedFace` method. Exact generated inputs and raw source
hashes accompany every receipt.

The fixture supplies model/configuration initialization, simplified print widgets,
VRHand poses and deterministic source/transport time. The glide initializer is
.55s for observability; the shipped merchant still uses its own .35s duration.
Gameplay completion callbacks and the full live game are not reproduced by this
one fixture. Its owner-side mutation is the actual native method on a real
RectTransform, not an invented observer correction. The actual shipped prefab
partition proof remains the complementary658 scope; simplified print readbacks
do not claim to cover every game artwork/material family.

Four owner/observer scenarios cover both source sampler types and0/6-frame
receipt delay for150 render frames each. From the first numeric receipt through
arrival and long afterwards, every visible frame checks absolute physical body,
front origin, printed centre, body/front relationship and visibility at50µm
tolerance. The actual destination hand moves after arrival. Both zero-delay
scenarios also perform **290 owner/observer Camera.Render/ReadPixels pairs**
with unchanged physical poses, yielding580 PNGs. The comparison admits a small
declared raster threshold; gray body alone cannot satisfy printed-centre and
image checks. The fixed-delay trajectory oracle follows the represented source
time and the actual approved moving holder, without correcting the observer.

Two24-part real bounded-sender scenarios change the root layout during a flight.
One deliberately drops the changed front/layout partition and delays selected
partitions by12 render frames to reorder receipts. They assert prior geometry
survives partial replacement and a subsequent complete snapshot repairs it.
Native near-end regrab at frame48 and post-arrival ownership change at frame78
assert no synthetic terminal flight and immediate genuine cohort cancellation.
A separate reflection readback verifies root reconciliation preserves every
non-pose root state, every other child state and all property/child clocks, then
checks alpha and child movement actually continue.

The separate merchant `--suite flight-outcomes` proof binds the actual production
coordinator, inventory outcome, native return/getter/collapse and same-original
purchase adoption:207 assertions plus four controls pass. It supplies native
inventory results at its declared model boundary. This establishes transaction
routing in combination with the direct presentation proof; it does not claim
that the new fixture executes one uninterrupted full game purchase plus network
renderer or proves a headset frame.

## Worker validation receipts

Receipts are private `.planning/debug/` data; archival copies are made under the
main checkout's `.planning/debug/npc660/returns-worker`. Failed hypotheses and
fixture compilation errors are retained separately, not relabelled as passes.

| Receipt | Result |
| --- | --- |
| `npc660-returns/run-56vhi8ao` | Initial quiet-header terminal red, actual source through completion. |
| `npc660-returns/run-iiobmyx7` | Actual pooled canonicalization red on first frame6,56.56mm print-centre error. |
| `npc660-returns/run-d81osl3r` | Atomic root-layout repair passes first frame; native-end external tween red at frame61. |
| `npc660-returns/run-1jci8iby` / `run-q4ke_8lo` | Real hand movement exposes post-arrival hand-affinity loss at frame76; latter includes real body/readbacks. |
| `npc660-returns/run-c8evk9rk` / `run-ob3_pp20` | Larger staged dependency exposes cached host-pivot restoration; follow-up old-pivot assertion after already repaired baseline was a declared fixture error. |
| `npc660-returns/run-8ookz8df` |2451 assertions; four precise old-terminal/layout/pose/staging controls fail at their required assertions. Includes loss/reorder and interruption. |
| `npc660-returns/run-yj8a7iqa` |2550 assertions after adding explicit child/color/alpha clock isolation, same frozen production source. |
| `npc660-returns/run-1_j3emhz` |All four controls pass independently against that expanded proof. |
| `npc660-returns/run-ghnf73hf` |2554 assertions and all four controls on final source; actual registration targets VRCard or ItemChip and unknown Components cannot synthesize completion. |
| `town-service-mirror/run-km7xjegq` |Motion-fast853 assertions and five controls. |
| `town-service-mirror/run-_gf_57so` |Offered orientation602 assertions and nine controls; overlay geometry and independent spin clocks remain correct. |
| `town-merchant-handoff/run-mfpabdn7` |Actual merchant outcome207 assertions and four controls. |
| `town-motion-wire/run-c4o7vdrh` |15294 portable motion assertions; existing event budget and goldens pass. |
| `test-runs/20261009-215440-2e0487ca` |Source16/16 complete source group. |
| `npc660-return-known-native-strict.log` |Final strict plugin/preloader Debug build, zero warnings/errors. |

The initial shared motion binder omitted actual `VRCard.Holder`; adding the
read-only state port fixes compilation. The older655/658 source-shaped runners
then needed removal of their duplicate Holder/control-board/path ports. Those
adapter failures remain raw evidence and are not production causality. The old
655 budget also needs the new purely internal ReturnLayout state port;658's
old body-cache boundary needs its duplicated MyPluginInfo/PeerBoardFade ports
removed. These failures are recorded in `npc-return655/run-iue6ayrr` and
`npc658-flights/run-ck71gcrd`. Parent
owns those shared runner changes and any subsequent integrated old scopes.

This is worker-focused/composite evidence. It is not a new complete local gate,
a WAN guarantee, or confirmation that the user's headset regression is resolved.
The primary agent owns final combined validation, stamp, release summary and dev
push; the paired hardware picture remains the final acceptance evidence.
