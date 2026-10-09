# NPC660 regression and failed-approach audit

This round addresses the maintainer's failing **Build658** paired test: remote
enchantress pictures delayed beyond10 seconds or absent, renewed sideways UI
drift, merchant return print/body separation, and a brief remote button-panel
disappearance. The maintainer explicitly confirms that enhancement overlays now
remain stable. Their depth/order repair is retained unchanged.

## Inputs and what can actually be concluded

Both startup banners identify assembly1.1.0.0, ModBuild658, source `e92ef8e53`,
built2026-10-09 18:45:20 UTC. Current integration starts at dev659 `c0b5e9a1b`;
659 changes pillars, not these NPC paths. Both supplied Player logs contain
Debug evidence. Treating the other player's Player log as merely Info would
miss actual publication and same-sequence recovery evidence. Immutable copies
of all four logs and their hashes live in the main checkout's
`.planning/debug/npc660/inputs/`. No new video or screenshot accompanies this
regression; the previous656 videos cannot show the new658 frames.

The first private enhancement transaction assembles24 originals/24262 decoded
bytes in0.696s and15 originals/27610 bytes in0.917s. It reaches23/23 required
originals, then **continues refusing an already loaded native texture**:
`texture|T_sphere_norm|512|512|4|10`. Later30/30 pictures refuse
`texture|Default-Particle|64|64|4|7`. Repeated complete frames cannot overcome a
poisoned asset registry. The log's post-admission wait age is not the interval
from the player's handoff, and decoded bundle size is not actual wire bytes.

There are also6.340s and7.938s assemblies, but their9862… stock marker identifies
the independent visitor-stock lane. Those values cannot be relabeled as this
private mage transaction's transport time. Conversely, real stock delay matters
for merchant/hand presentation and is not erased by a fast private test.

Both logs include exact native-basis refusals. The author also sends compact
originals followed by retained **same-sequence full recovery**, as intended by
658. For example, a compact1123-byte original becomes its8268-byte full source.
This disproves the blanket assumption that every blocked picture waits for the
ten-second periodic full refresh. Exact material identity and valid body
construction must work after that fallback arrives.

The paired logs also report `map.cardbody: Original physical prop is still
loading`. The actual local card factory explicitly supports a null optional
backing prefab and creates a valid procedural original. The publisher's prefab
requirement therefore rejects a real visible local card, not missing artwork.

Bounded observer visibility traces retain `headerVisible=True`,
`hostActive=True` for sampled buttons. They do not sample every rendered frame
or log world positions. They neither disprove the observed brief disappearance
nor identify its exact hardware frame. The separate remote native
ShopDiscount/merchant-refresh shutdown exception is after teardown and is not
claimed to explain these live enhancement waits.

## Why earlier fixes repeatedly passed without solving this case

The issue is not one timer repaired repeatedly. The production boundary has
several independent prerequisites, and earlier probes often made an unverified
prerequisite true before starting their clock or skipped its visible transition.
Those boundaries must be stated and challenged instead of treating a green
fixture as a correct headset picture.

| Earlier approach | Useful change | Assumption or missing challenge exposed afterwards |
| --- | --- | --- |
|600–612 native descriptor/original registration|Finite audited duplicate wrappers, sprite catalogs, template aliases and model-aware artwork|The finite registry did not include every dormant FX dependency; first-borrow paths were still not canonical identities for shared factory textures. More retries cannot resolve an already ambiguous descriptor.|
|622 prewarming, priority, late native capture|Shared original cabinet/fan path, original shader resolution, final-write ordering and retained banks|Early fixtures used empty purse/axis-aligned aura/light modules; bank iterator and driver wait handling initially did not execute their claimed challenge. Capture ordering proves capture, not later observer layout interpolation. Failures were retained and controls improved.|
|623 compact native metadata and15Hz capture|Reduced repeated property tables and CPU work, exact original preparation/repair|Matching local prepared defaults were assumed by small fixtures. A compact packet requires the same omitted-value basis; receipt alone does not establish it.|
|625 cold retention and robust material basis|Mandatory owner state, retained cold patches, genuine complete fallback, reconnect cleanup|Local preparation can remain incomplete; full fallback still needs resolvable native assets and matching actual topology. Missing original material identity cannot be solved by waiting for a larger packet.|
|629/632 visible census and pooled110 originals|Fixed the logged44-original/28.557s transfer; actual partition relation, canvas geometry and first-picture delivery|The imported row/font boundary was8.3KiB versus12.5KiB hardware, not full game-font output. The position guard ran at binding, while later RectTransform animation could move it again. The body fixture supplied a prefab absent from the valid local null-prefab path.|
|638 first-picture retention/receipts|Preserved exact opening originals through newer census/deltas and actual delivery|Older first-picture probes had pre-registered templates, a one-shot capture, a simulated clock or bypassed the real queue/apply/render path. The subsequent638 hardware wait still exceeded its fixture result.|
|639 packed native sprites, receipts, stronger first render|Real895-member atlas/packed UV proof; native delivery and actual rendered readiness|Loaded Sprite census had hidden cold atlas members. The stronger proof still used declared card-model/text constructors and lacked the two current GPU-only duplicate FX objects and real null-prefab body provenance.|
|645 ring clocks and binding drift repair|Preserved original intrinsic effect speed/plane; stopped binding-time recentering|Sparse angle/root probes hid intermediate velocity/child behavior. A simple motion-coordinate hypothesis passed old source and was correctly rejected then; the new failing case changes anchors/pivot at constant sampled position and is a different challenge.|
|646 census retention and flight/spawn work|Kept exact ready native children through pooled holder retirement|The test animated the root child while its enclosing Canvas host stayed static. SetParent(true) preserves the current image but does not rebase a running host tween's retained coordinate frame.|
|655 continuous census and destination-hand returns|Preserved immutable first pictures while real hover capture continues; source timeline across jitter|Earlier probes destroyed owner sprites, stopped capture during transfer, injected a flight or built small print models. More continuous/native tests improved this but did not exercise the new real material/body failures.|
|658 exact105 basis/full112 recovery, atomic113 flights|Reduced warm prepared picture to0.828866s under its recorded boundary; fixed hardware-confirmed overlay disappearance|The native-basis bank was prepared against the fixture's matching asset world. Flight readbacks skipped initial frames, ended before the real0.55s source termination, and manually retired the offered representation. Matching root TRS did not prove matching RectTransform print geometry.|

Detailed original receipts and their limitations remain in
[NPC-622-REVIEW.md](NPC-622-REVIEW.md), [NPC-623-REVIEW.md](NPC-623-REVIEW.md),
[NPC-625-REVIEW.md](NPC-625-REVIEW.md), [NPC-632-REVIEW.md](NPC-632-REVIEW.md),
[NPC-638-FIRST-PICTURE-AUDIT.md](NPC-638-FIRST-PICTURE-AUDIT.md),
[NPC-639-PROOF-AUDIT.md](NPC-639-PROOF-AUDIT.md),
[NPC-645-PROOF-AUDIT.md](NPC-645-PROOF-AUDIT.md),
[NPC-646-ADMISSION.md](NPC-646-ADMISSION.md),
[NPC-655-REVIEW.md](NPC-655-REVIEW.md) and
[NPC-658-REVIEW.md](NPC-658-REVIEW.md). Prior failing controls/compilation and
composed gate reports are history, not rewritten into successful full runs.

## This repair's actual boundaries

The asset registry identifies the two audited serialized originals and gives
the exact shared procedural backing texture a factory-based identity. Its new
Unity proof loads genuinely distinct GPU-only wrappers from unchanged game
objects; removing either repair reproduces the exact logged resolver failure.
See [NPC-660-ASSETS.md](NPC-660-ASSETS.md).

The local null-prefab card is now a legitimate publishable original. Source
dimensions and construction family address the exact existing VRCard factory;
observer clones remain registered for its real later contour completion. This
changes neither local pixels nor the player's card-size setting.

The observer interpolator preserves the separately authored position while
anchors/pivot/size change, and rebases a running enclosing-host tween before
retained census reparenting. New per-render world-path and button-ink assertions
fail independently when either protection is removed; the previous actual
native census proof remains unchanged and passes. See
[NPC-660-LIFECYCLE.md](NPC-660-LIFECYCLE.md).

The return lane challenges actual VRCard/ItemChip source samplers from the first
frame through termination and hand continuation. It must preserve root layout,
physical geometry and final enclosing-canvas motion as well as common TRS/clock.
Its review records both the newly reproduced first-frame separation and stale
terminal-state failure. No new flight duration, approximate body or snapping
viewer-facing pose can count as the repair.

These are source/runtime-proven failures and corrections. The supplied logs
prove the asset and null-prefab failures; they do not establish that each new
geometry reproduction is the unique cause of each hardware frame. Universal
internet first-picture latency and the final HMD image remain separate evidence.
The final integrated validation and exact pushed build are recorded below after
all source changes and checks finish.
