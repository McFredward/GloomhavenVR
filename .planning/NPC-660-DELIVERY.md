# Build660 original delivery and physical ability body audit

Worker base: dev659 `c0b5e9a1b`. This document records the delivery worker's
scope; integration checks and headset acceptance belong to the primary agent.

## Paired hardware evidence

The frozen inputs are in the main checkout's `.planning/debug/npc660/inputs`.
Both `LogOutput.log` banners identify **Build658, e92ef8e53**, built
2026-10-09 18:45:20 UTC. The host uses SteamVR/OpenXR, the peer VirtualDesktopXR.
The host log SHA256 is
`673204f311ef7610416bc2c0e916d69645fc0cb90ba674d33366b7c3e4379dea`;
the peer's is
`030a4bf5eb7e225f3b062db22d237f2970c7a6939438cd795a7b49cc45979713`.
No new screenshot/video accompanies this latest regression report. Older videos
cannot establish the latest run's exact rendered pixels or movement.

`scripts/npc660-delivery-runtime/analyze-hardware.py` retains exact lines,
input hashes, build banners, template rejection identities, required-picture
waits and bundle lane markers. Its output is
`.planning/debug/npc660-delivery/hardware-analysis.json` in this worker.

| Actual log flow | Implication |
| --- | --- |
| Host4904: private65531 bundle,24 members/24,262 decoded bytes,0.696s assembly;4924:15 members/27,610 bytes,0.917s | Atomic bytes can arrive before usable original assets. Neither timer begins at handoff or ends at headset rendering. |
| Host4911:23 required,17 received, module28 absent | This particular early wait is missing delivery. |
| Host4943–5033:23 required,23 received; module43 rejected for `texture\|T_sphere_norm\|512\|512\|4\|10`, ages2.011–7.062s | All required originals have arrived. Ambiguous native texture resolution is a permanent validation blocker, not a missing fragment. |
| Later30-required pictures have all30 originals but reject `texture\|Default-Particle\|64\|64\|4\|7` on module150/186 | Increasing packet priority cannot resolve this duplicate-wrapper identity. |
| Remote2947/2948: inventory compact1123B then full8268B; holder compact352B then full2963B at the same original sequences | Retained full recovery executes. Full bytes still need exact asset resolution. |
| Host5083/5111 and5466/5467:35-/36-required pictures admit/display at1.038s/3.472s respectively | These are successful individual pictures; the diagnostic age is a dependency/admission clock, not full handoff latency. |
| Remote2847/2848,3411/3412,3972/3973,4283/4284: `map.cardbody` publisher refuses an original physical prop | The valid ordinary procedural local card has no corresponding original-bank body. This is a separate source defect. |
| Host5468:7.938s assembly,15 members/18,135 bytes, sequence has stock bit63 | It belongs to the independent stock/return lane. Stream65534 alone does not identify private enchantress first-picture traffic. |

There are27 host and11 peer compact-native playback rejection reports. Several
exact owner/observer hashes disagree despite matching structures, including
face325 root `A991...` vs `3521...`, and FX overlay `F3AC...` vs `A2E4...`.
The parent identified first-borrowed template texture aliases as a source of
those hashes. Exact serialized native FX texture identities and their resolver
repair are the parent's separately tested scope. A received-original ACK
establishes retained metadata only, not successful material validation/rendering;
it must never be described as picture acceptance.

The remote teardown `MapParty.ShopDiscount` NullReference follows hosting/load
shutdown in `Player.log`; it does not establish the active enchantress blocker.
No latest hardware fast-motion samples establish the exact per-frame drift or
merchant split; those have separate source-bound reproductions owned by the
lifecycle/return workers.

## Why the earlier fixes did not finish this problem

The historical sources and their limits are different, cumulative causes. Their
passing results cannot substitute for an actual latest hardware acceptance.

| Builds / actual approach | Valid improvement and omitted causal workload |
| --- | --- |
| 622–623: priority, exact original prefab dependencies, compact native properties, frozen inactive templates | The early small synthetic geometry did not match25-/50-node native cards and26-node option rows. Later native density was better, but ordinary null backing was replaced by an injected prefab. |
| 625: prepared originals, reliable cabinet headers and preserved native rendering | Faster hash/capture and small clock bytes did not prove remote receipt, native asset availability or a complete rendered cold picture. Missing/ambiguous native wrappers remained possible. |
| 626: cumulative owner-property deltas rather than repeated full baselines, exact enhancement areas and numeric flights | A locally completed original send still could leave an observer with no retained baseline. The return proof supplied a hand-built body and omitted the normal `VRCard.Build(null)` body provider. |
| 629: required visible originals rather than the entire hidden prepared bank | Correct visibility removed unnecessary waits. It did not resolve current originals' absent assets or prove successful recipient retention. |
| 632: lossless shared-value tables and actual native density/partitions | Its fresh warm bases, direct reception and one `fast:false` capture omitted driver coalescing, old receipt debt and continuous first-picture capture. Lower packet size did not cover those conditions. |
| 638: actual scheduled reliable original transport and fresh transaction originals | The earlier send callback was corrected as a transport boundary. It remained distinct from every observer's actual stored-original acknowledgment and independently prepared assets. |
| 639: exact record112 receipts and repeated-offer repair | Receipts correctly name actual peer/session/source/baseline objects. Packed-atlas tests destroyed live owner wrappers and used only `CaptureMotion` during initial assembly; a later continuous case did not cover capture while first originals were fragmented. |
| 645: native root anchoring and independent numeric clocks | This addresses visual geometry. Its previous endpoint/total-angle assertions did not bound intermediate row drift or instantaneous ring velocity. It cannot make an unresolved first original renderable. |
| 646: preserve useful mixed full/delta dependencies, census-retained child ancestry | Older in-flight tests omitted actual cumulative artwork while the old complete bundle fragmented. Admission-age0 was incorrectly treated as end-to-end timing. Correct current-child retention and full image readback improve that boundary. |
| 655: allow useful immutable first-picture assembly through live hover census churn | The corrected capture workload reproduces canceled progress. Its card body is still hand-authored/injected and its templates have a controlled asset registration order. It does not exercise duplicate native FX wrappers or the valid absent optional body prefab. |
| 658: compact105 mage state, complete retained fallback, first-map preparation, pinned exact recovery | Prepared exact-basis timing passes; genuine cold/mismatch paths still missed the unchanged1s deadline in the published audit. Full recovery can execute and remain unrenderable when native texture resolution is permanently ambiguous. The old bank's body assumption is still false on the normal local procedural path. |

The previous Mono `ConditionalWeakTable.GetOrCreateValue` fixture failure and
fixed public constructor are documented in658. The latest paired files contain
no matching exception; this is not attributed to the reported regression.
Likewise, actual ordinary repair debt is inspected separately from the visible
opening reservation; no scheduler limit is enlarged in this worker's fix.

## Exact procedural-body correction

`MapRoomHand.BuildCard` normally calls `VRCard.Build` with a null optional backing
prefab. The existing local `BuildProceduralBacking` builds a valid ability
`CardMesh` with its original shared edge/back materials. Previously
`OriginalMapBacking` looked only for `CardsDriver.CardBackingPrefab` or the
optional `Assets/Bundle/Table/CardBacking.prefab`. Its absence is normal and is
not evidence that the local physical original is loading.

The new `TownServiceAbilityBody` addresses the actual original backing base
width/height by exact float bits plus its actual procedural/bundle family.
`VRCard.Build` records that family directly from `backingPrefab == null`, with
readonly accessors; it is not inferred from a mesh name. Local construction and
the inactive canonical bank invoke the **same unchanged factory**. Observer
card-size settings are never read. The bundled family still requires its actual
native prefab; the legacy address remains supported.

Freezing and inert observer cloning re-register the exact `CardMesh.AttachBody`
consumer. Unity cloning otherwise copies today's mesh without joining the
original later contour/resize registry. The real physical body and original
materials are preserved, and genuine original contour completion reaches all
three consumers. No new gameplay controller, replacement card UI, bitmap
transmission or arbitrary original asset allowance is introduced.

The shared decorative pattern is actual `CardMesh.GetBackTexture`,128x128
RGBA32/mips8,Trilinear/aniso8, shared across card kinds/sizes. Body silhouette
completion changes geometry; the exported `.QuadMask/.QuadBack` textures are
for separate quad consumers. The first generated body/inspection size could
previously choose this pattern's original template alias. The parent supplies
an exact factory-reference canonical key, tested separately and integrated
into the extended body proof below.

## Focused evidence and explicit boundaries

`scripts/npc660-delivery-runtime/run.py` binds complete actual CardMesh,
CardContour, ability factory/helper, template freeze/resolve, capture,
codecs/fragments, scheduler, receiver, material validation/apply and real
`Camera.Render/ReadPixels`. Eight other scheduler queues stay saturated. Event
bytes remain864 and ordinary cadence50ms. It uses two actual owner dimensions,
different from a default observer setting. Canonical bank preparation never
activates its native source. Geometry/material identities and world poses are
checked before camera-only image isolation. Late contour consumers are checked
against the complete original engine.

The fixture's external card construction port sets owner fields as
`VRCard.Build` does; the entire game card driver, native face initialization,
hardware network and full peripheral `VRCard.Build` lifecycle are not executed.
This is a physical-body proof, **not** the whole115-node native face/options
picture. Old manual-body tests remain separate fixtures and retain that limit.
The native FX wrappers and45/82 first-picture workload are separately owned
integration scopes. No1s universal cold-network or headset result is inferred.

| Checkpoint evidence in this worker | Outcome |
| --- | --- |
| `body/run-em7n39b_` | Real null-prefab body at0.244s/0.059s including rendered readback; zero differing pixels at both owner sizes. Three controls fail exactly for missing original, wrong owner dimensions and omitted late consumer. |
| `body-origin/run-mn3bqjaj` | Repeated production and all three controls pass after replacing mesh-name inference with direct Build provenance. |
| `legacy-delivery/run-dkw2_dky` | Actual623 delivery/rejected publication/census/hidden work: production plus six existing controls pass. |
| `legacy-publisher-fixed/run-iuh8f6jg` | Actual622 cold original assets/native dynamic publisher: production plus nine existing controls pass; all original semantic assertions retained. |
| `legacy-counter/run-qpgby1cw` and exact659 `baseline-counter/run-ussh5d1e` | Both fail the existing expectation of two private `merchant.counter` copies. Current native-author policy explicitly permits only the elected counter author. No assertion or product behavior is weakened to conceal this pre-existing failure. |
| `.planning/debug/npc660-delivery-debug.log` | Strict Debug source build passes with zero warnings/errors at the initial source checkpoint; integrated final builds belong to the parent. |

Initial proof compile failures (missing unrelated cap enum/layout and scenario
fade compile ports), the first wrongly targeted dimension control that correct
clone rebind repaired itself, and the publisher fixture's missing MeshFilter
Unity-fake-null failure are retained. The dimension control now changes the
actual owner key; direct construction provenance removes the fake-null/name
heuristic. These fixture setbacks are not attributed to the hardware report.

Useful original source/fixture hashes, exact binaries, screenshots, failed
controls and complete local output remain under this worker's gitignored
`.planning/debug/npc660-delivery/`. These are focused checks, not a new complete
integration gate or proof of headset correctness.
