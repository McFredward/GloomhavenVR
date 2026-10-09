# NPC658 native first-picture delivery

Latency worker, based on Build656 `bf3444cb502e1eff52bcf0e5194a255109879d36`,
rebased onto Build657 `a9af3c330f25ffb3f74ab32342f57604e0f7f890`.
Implementation checkpoints: `5b7dbcb5b`, then `5dccf90a5`. The parent owns
integration, the build stamp, gate registry, flight/overlay combination and push.

## Evidence and causal boundary

The frozen paired input is the main checkout's
`.planning/debug/npc658/inputs/input-manifest.json`. Both players report
ModBuild656 and assembly1.1.0.0 (each Player.log line40); the host's source banner
at line156 and remote's at line100 both report `bf3444cb5`, built
2026-10-09 14:50:15 UTC. Host Player.log contains Debug; remote logs and both
LogOutput.log files do not provide the corresponding frequent publication rows.

The frozen host Player.log SHA256 is
`b46dbfda3726a5dea328837e3d19e47a3e3cb811dd0ee7a08d6e411bc3399fba`;
remote Player.log is
`d929248a468b06eb88cdee36ca4a4a3528a82646d1675ec7f329903789fa40d3`.
Host line42612 is the relevant receiver observation:

```text
Native original bundle assembled: peer=2 stream=65531 sequence=639271550276927483 members=36 bytes=39433 assembly=2.129s.
```

Line42613 immediately admits required35/prepared60 at `age=0.000s`. That age
starts at dependency admission, after the2.129s assembly; it does not measure the
user's first handoff. Another urgent bundle, line44395, contains22 members/26278
bytes and takes1.526s. The parent retained the32-assembly distribution, including
background intervals up to4.873s. These byte counts describe the decoded pooled
container, not compressed event bytes. The frozen inputs do not establish
owner handoff-to-capture time, actual wire throughput, or final headset pixels.
Missing remote Debug rows do not establish that its owner publication was healthy.

The user videos were inspected by the flight and overlay lanes. Their source
causes and repairs are outside this document. The old NPC655 proofs used a small
hand-written offered print and prepared bases before their clock; their green
static deadline was not evidence that the Build656 hardware complaint was fixed.

## Source changes

The real mage source previously bypassed independently reconstructable native
metadata, so a complete first UI/card picture repeated the original property
tables in its full baseline. The new path sends a compact complete original
against an exact native basis, without depending on an earlier network baseline.
Owner text, font/style, root transform/active/graphic/group/mask and frame headers
remain explicit. Every changed child property or material is explicit. Every
unchanged omitted property, including numeric geometry, hidden flags, sprite
selection and image/text material descriptors, participates in the exact basis
hash before any painting. A differently localized text default is overridden;
a different omitted numeric/material value rejects the patch. No guessed
defaults, placeholder card or approximate material can satisfy it.

The normal and topology-rebuild source captures now both use
`Read(includeInactiveGraphics: service == 3)`. This fixes a separate production
vocabulary defect: bases included inactive FX/Infuse/image properties while the
owner omitted them, making an otherwise matching original impossible to compact.
Hidden active/enabled flags and original sprites remain hidden and unchanged.

Full source capture canonicalizes only the first10 numbers of root Transform.
`Binding.Apply` takes absolute root TRS from the owner frame header, consuming
the remaining rect fields from that property; the existing motion path already
uses this canonical prefix. Every root rect field and child TRS remains exact.
The helper copies the node/value/array before changing it, leaving sampler
caches and already emitted originals immutable. The topology replacement proof
checks the same canonical prefix and inactive field vocabulary on its first
replacement baseline.

The owner retains the exact immutable full original when a compact original
is queued. The actual last datagram callback, including pooled/compressed atomic
bundles, starts a150ms receipt grace. A validated matching existing112 receipt
from every compatible peer suppresses only that exact fallback. Missing,
unprepared or mismatching bases receive the retained full original with the
same source object, sequence and delta dependency. It bypasses re-compaction
and the old10-second repair interval. Encoding/enqueue exceptions preserve the
reference and bound retries to250ms; source replacement, reset and unregister
retire its lifetime. Native continuation never waits for a receipt.

`LocalModule.NativeRepair` now owns this nullable state. An intermediate
ConditionalWeakTable implementation produced reproducible Unity/Mono
InvalidCastException in this harness when unrelated diagnostic allocations were
removed. Those failures are retained; there is no evidence that the hardware
report was caused by that Mono exception. The old non-mage periodic timer is
still initialized lazily with its existing interval.

A same-sequence full repair must also survive the send queue. Generic
first/latest3 coalescing could evict it after several dependent artwork deltas
arrived while another bundle was fragmented. The private service3 queue now
prepends this exact retained source identity. The existing primitive's new
optional `preserveInFlight` argument leaves active fragment pages untouched;
ordinary migration callers retain their existing behavior. Pending storage
stays bounded to full+latest3, genuine newer-original replacement/removal still
wins, and generic `PresentationPending` behavior is unchanged.

The existing bounded original-reference map also records ordinary private mage
originals. An original sent before an offer can later acquire urgent deltas;
its full repair uses the same dependency pin and preserves that current urgent
priority, even though its original header was ordinary. Both initial priority
states have actual fragmented/continuous-artwork coverage. Omitting ordinary
bookkeeping is a separate targeted negative control.

## Earlier preparation and remaining recovery limits

`WorldUIModule` calls ordinary `TownServiceSync.Prepare` during the active map
before handoff. That preparation previously skipped a client with local
immersion disabled and no currently observed visitor, making a first late
visitor start the originals on the offer frame. The skip is removed. Inactive
original preparation now runs throughout the active map for those observers too.

The initial `NativeTemplates.Initialize` return now calls the same existing
`PrepareEnhancementOriginals` helper as the reusable-bank path. Its two-item/2ms
budget, map/pool readiness checks, party census, retry bounds and inactive bank
remain unchanged. The first-map proof uses actual PrepareCore, the exact first
Initialize return tail, and the complete production helper. Bank creation,
native model and pool availability are explicit fixture ports, not simulated
gameplay controllers. With immersion off/no visitor, the first turn prepares
one original and subsequent bounded turns prepare the widget plus public face
and card bases; removing either the old skip repair or first call fails its
specific assertion.

This is earlier preparation, not a universal precondition that every native
basis exists before every offer. New party data, unavailable pools, newly
created banks, genuine differing original defaults and abrupt offers before
bounded preparation finishes remain eligible cold paths. The source preserves
an exact full recovery for them. The current1s strict deadline is still missed
by the measured unprepared start and mismatching-basis recovery below. Do not
describe the user's2–3s report as universally eliminated or a headset outcome
as confirmed.

## Native Unity reproduction

`scripts/npc658-latency-runtime/export-native.py` reads the shipped, read-only
`ressources/GH_Data/StreamingAssets/aa/StandaloneWindows64/misc_gui_assets_all.bundle`.
The AbilityCard container is `Assets/_AssetBundles/gui/AbilityCard.prefab`,
CAB-5b84b5b74775062cd7460715663ea0e6, pathID-7204637168609111676. It exports the
actual50-node Full subtree and native TextContainer, PreviewText,
EnhancementContainer, Enhancement, XPContainer and DurationRes prototypes.
Original packed action icons are loaded from the unchanged895-sprite atlas.

The generated challenge includes115 native card nodes,17 actual production
structural partitions (11 visible face parts),45 initially required UI/card
modules and82 prepared hierarchies. Native enhancement UI includes actual
26-node rows, inventory, ring, highlights, confirmation and item originals.
Dynamic action text/container dimensions are input models; original
MakeAbilityAction/CreateLayout gameplay constructors are not executed. Native
prefab cloning retains original descriptors without controller callbacks.

The source-bound path is actual CaptureCore at15Hz, actual production global
ExtrasSendScheduler (50ms,864-byte events), original value pooling/compression,
fragment assembly, QueueTownService/ApplyTownServices, validation/apply/mount,
Canvas rebuild and Camera.Render/ReadPixels. All eight other stream queues
remain saturated with their unchanged production capacities and seeded
payloads. Synthetic immutable noise is generated before the clock, then the
identical bytes are requeued on every turn; payload hashes are retained in
`saturated-payloads658.txt`. Reverse112 receipts use their own saturated actual
scheduler. Their queue lifetime continues into subsequent phases rather than
discarding a last queued receipt when a fixture helper changes phase.

Owner capture costs, decode, apply, Canvas rebuild and readback are timed.
Expected output comes from each actual current `module.Last.Nodes`, including
fresh owner TMP font/material/atlas aliases after rendering. A pre-render
font-material pointer is not silently relabeled as the received owner output.
The warm path uses actual original asset loading/preparation and initial owner
TMP draw before a handoff, consistent with the ordinary map loading stage.
The startup path begins its clock before asset reset/registration, template
scan/basis preparation and first TMP draw. Both modes still construct the
native/model scene before that clock: startup is unprepared presentation assets
and bases, not complete process boot or native bank creation time.

Unity2021.3.5 uses llvmpipe/OpenGL here. The fixture uses its TMP font plus full
DistanceField shader descriptors and existing adapters for game-only shaders.
It verifies original DTO content, hierarchy, geometry/state and real rendered
output; it does not establish original game-font pixel equality, Direct3D
behavior, Steam Frame performance, network loss behavior or headset correctness.

## Retained measurements and commands

All paths below are inside this worker's `.planning/debug/npc658-latency/`.
Each run retains bound source hashes, manifest, logs, costs and readback evidence.
The unchanged first-picture deadline is1s wall clock.

| Case | Complete rendered picture | Evidence | Result |
| --- | ---: | --- | --- |
| Prepared exact native source |0.8275588s |`final-warm/run-j63gtz9h` |Strict pass;38,743 assertions;13 events/10531 actual wire bytes; first assembly0.5747121s |
| Exact Build656 latency files, same challenge |1.238811s |`final-old/run-gbw3hnjn` |Expected unchanged deadline failure;21 events/17753 actual wire bytes |
| Unprepared basis/assets/first TMP startup |1.605361s |`final-startup/run-dx2i8krg` |Expected unchanged deadline failure; first assembly1.3522664s |
| Genuinely different omitted32-node face geometry |1.230527s |`final-mismatch/run-zru92is7` |Pre-paint rejection, exact once-only6219-byte full source recovery, all visible source output; expected unchanged deadline failure |
| First-map/no visitor/local immersion off |Bounded first and subsequent turns |`final-preparation/run-q46trgg0` |7 assertions plus2 causal controls |
| Missing ACK/basis and continuous queued artwork |2.1828285s for adversarial queued debt |`queued-repair/run-u4sykeh9` |2,110 suite assertions;43 bounded events;40 artwork revisions; exactly one full; replacement/removal and older callback order pass |
| Initially ordinary original, later urgent artwork |2.3221853s for adversarial queued debt |`ordinary-repair/run-ln9hw0qs` |2,353 suite assertions covering both starts;46 bounded events;31 artwork revisions; exactly one full and no priority downgrade |

The old evidence directory was initially generated at the worktree root and
then moved intact into `final-old`; its original absolute manifest path is
retained. The measurements above preceded only the subsequently tested queue
retention/topology-catch repairs, which do not weaken the timer or wire budget.

Commands (optional `--native-dir .planning/debug/npc658-native` reuses exported
read-only originals):

```sh
python3 scripts/npc658-latency-runtime/run.py --latency658-native --live-hover-census --required35
python3 scripts/npc658-latency-runtime/run.py --latency658-native --latency658-old --live-hover-census --required35 --expect-incomplete
python3 scripts/npc658-latency-runtime/run.py --latency658-native --latency658-startup --live-hover-census --required35 --expect-incomplete
python3 scripts/npc658-latency-runtime/run.py --latency658-native --latency658-mismatch --live-hover-census --required35 --expect-incomplete
python3 scripts/check-town-native-state623.py --preparation658-only
python3 scripts/check-town-native-state623.py --negative-control queued-native-repair
```

`--expect-incomplete` accepts only the named unchanged1s assertion failure;
unexpected asset, equality, callback or reconstruction failures remain failures.
Costs and meaningful output/recovery checks persist before the negative deadline.
The existing transport proof also passed375 assertions and all six causal
controls (`transport/run-tzn2a9iy`). This is a focused worker check, not a new
complete local gate or hardware result. The parent runs the integrated required
gate and golden wire vectors after combining all lanes.

Final native semantic/repair controls (`final-controls/run-o3xdning`) passed
2,123 production assertions and nine independently targeted source mutations:
owner text, explicit owner root state, omitted numeric hash, overridden material
coverage, cold dependency replay, retained full repair, cumulative baseline,
failed publication and queued full dependency retention. The source replacement
topology check is included. Existing lifecycle checks
(`final-lifecycle/run-s4_sgo9g`) passed30 assertions and both disconnect/reset
causal controls. Its Part adapter now supplies the production preparation API's
nullable ring root and zero rate; these fields are ports for absent ring motion,
not substituted artwork or changed source behavior. Strict Debug build passed
with zero warnings/errors after the source checkpoint.

## Audit of setbacks

Exploratory receipts remain under the worker debug directory. They include the
bare51-node print's early pass/old failure, the initial2.3669s unprepared/native
run, narrow1.02–1.36s misses, pre-draw TMP expected-material mismatch, incorrect
poison lookup, Mono weak-table exceptions, concurrent-render timing miss, the
reverse receipt queue lifetime defect, and a mismatching-geometry fixture that
initially changed a property already explicitly overridden. That last fixture
could legitimately reconstruct; it was corrected to change an actually omitted
property, without weakening the basis check. The full action payload exposed
the real inactive-vocabulary production defect, fixed above.

The first queued repair proof stopped sending after full arrival before its
now-reordered dependent delta was delivered; the corrected proof continues the
same actual bounded transport until that delta arrives and lets the existing
color interpolation finish. `queued-repair/run-ltrcklfz` and `run-_90eegd9`
retain those fixture failures. The passing continuous regression does not remove
or relabel them. None of these failed receipts is substituted for a passing
hardware outcome or silently excluded by changing the strict deadline.

The first final semantic-control run (`final-controls/run-a2ws1w5y`) exposed two
control targeting gaps. Removing mandatory root ownership did not necessarily
fail the old bad-packet check because its changed coverage still rejected the
packet. A direct assertion now requires every nonmaterial owner root property
in the actual compact output; the control fails there. The cumulative-baseline
control initially reached the new repair scenario first and failed its earlier
ACK safety check. Its existing cumulative capture test now runs first, so the
same mutation fails its intended actual hover-baseline assertion. Production
checks and the unchanged deadline were preserved; the failed receipt remains.
