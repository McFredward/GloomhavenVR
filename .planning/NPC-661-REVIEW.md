# NPC 661: remaining Build 660 hardware defects

## Evidence boundary

The maintainer reports that Build 660 is substantially better, with four remaining
defects: enchantress body/front separation during hover, separation while the card
owner turns, up to three seconds before the complete remote card/UI, and a gray
merchant purchased-item return. The previously confirmed stable enhancement
overlay remains a required regression boundary.

Both players' Player.log and LogOutput.log identify ModBuild 660, source
12df1353f on dev, assembly 1.1.0.0, built 2026-10-09 21:01:35 UTC. The inputs were
copied before analysis to the main checkout's gitignored
`.planning/debug/npc661/inputs/`; its manifest records sizes and SHA-256 hashes.
The host has Debug town-motion traces; the other player's normal log level does
not provide the same detailed clock/capture stream.

Both new recordings are from the enchantress. There is no merchant purchase
recording in this evidence set. The merchant defect is the user's hardware
observation and must be reproduced through the actual item purchase lifecycle.

- `VirtualDesktop.Android-20261009-232038-0.mp4`: 8.266633 s, 1280x720, 30 fps.
  All five extracted keyframes and all 17 uniformly sampled frames were inspected.
  The card is already offered at the beginning. Rotation exposes the back and
  front simultaneously as displaced surfaces around three seconds; a broad
  plain physical surface overlaps only part of the printed front around 6.5 s.
  The recording establishes visible separation, not its exact transform error.
- `VirtualDesktop.Android-20261009-232128-0.mp4`: 8.811089 s, 1280x720, 30 fps.
  All five keyframes and all 18 uniformly sampled frames were inspected. The
  selected card approaches the offered slot around 1.5 s. The offered card/ring
  and option panel appear around 3.5 s, then the visible rows change/fill.
  Those subsequent changes require comparison with the owner-native state:
  original UI transitions or scrolling must not be counted as transfer delay.
  System overlays obscure the beginning/end. File names alone do not establish
  exact synchronization with game clocks.

## Why the previous proof did not finish this task

Build 660 made ability bodies from exact owner factory dimensions and retained
original silhouette updates. That proves construction and identity, but it does
not prove coherent motion of separately published body and front modules. The
offered-root interpolation path recognizes fronts and native holders; the body
is a separate source/module. Identical initial geometry cannot guarantee matching
per-render transforms when independent samples and clocks subsequently move it.

Build 660's prepared-native latency tests measure their declared preparation
boundary. They do not establish the time from actual first owner-visible content
through cold native admission, publication, transport, construction and final
observer rendering. The new test must measure that entire lifecycle and retain
the original owner's intermediate states.

Build 660's return-completion tests cover real native samplers, root rect
canonicalization and hand attachment, but explicitly use simplified print model
ports. A merchant purchase can change the original ItemCardUI construction and
ownership. Correct flight endpoints cannot prove that the purchased item's real
front remains drawable throughout that handoff.

## Work ownership

Independent geometry, merchant purchase rendering and first-picture delivery
workers start from current dev 12df1353f in separate initialized worktrees. The
integrator owns shared-file coordination, test registration, build metadata,
review, complete local checks and direct dev delivery. Source-proven changes,
runtime regression evidence and unverified headset outcomes remain distinct.

## Causal reproductions and integration review

The new geometry fixture captures the real procedural backing and printed
ability original through production publication, codec, receive, motion and
camera readback. Unchanged660 separates their world vertices by1.59154mm at the
first interpolated turn: the body rotates0.22 degrees while the print rotates
0.09 degrees. The new physical affinity uses the actual handoff's source pair
and the measured backing-to-print transform. It temporarily mounts the body on
the final rendered print, then restores its ordinary parent before the next
header/motion pass. The source matrix must be a finite factorable TRS; this does
not pretend that quaternion/lossy-scale division represents arbitrary shear.
Return, regrab, withdrawal and reoffer must supersede obsolete offered affinity.

The merchant proof instantiates the serialized native ItemCard hierarchy and
the real Leather_Armor sprite, then executes the actual ItemChip purchase
preparation and return sampler. Preparation deliberately sets its enclosing
canvas scale to zero. With unchanged660, the first returning print retains that
zero scale while the physical body moves. Moving the parent's validity guard
after canvas restoration is necessary but insufficient: the complete canvas
recipe was suppressed by the existing live-return budget. The fix freezes each
member's existing Kind1 original root/canvas recipe alongside its Kind10 subset
and stages them until all originals are ready. Record113 is not reinterpreted.
Integrator review also identified the out-of-order subset case: root admission
must use its own member's packet sequence, not the cohort's maximum sequence.
Frozen-source acknowledgements must preserve a newer terminal root.

The strengthened cold latency fixture starts before the first owner render,
native-bank freeze/partition and asset preparation. Its initial result is2.780s,
with about1.22s spent in that fixture's preparation before delivery. Further
review finds that it freezes repeated UI rows under separate addresses, whereas
actual Sync publishes them against one shared original key/address. That result
reveals previously excluded fixture work; it does not measure the actual HUD
Initialize cost or establish the hardware delay's cause. Final comparison must
bind actual template sharing and concurrent stock traffic, with the same clock
boundary for production and old-source controls. Earlier0.83s prepared
measurements retain only their declared boundary and are not cold hardware proof.

An additive record114 uses existing message28 to request the exact rejected
private compact original. It is separate from112 retained-original receipts:
it grants no interaction and acknowledges no original. The owner verifies the
compatible requester, current private session, module, exact immutable baseline
and absence of that requester's valid receipt. Bounded repairs reuse the ordinary
transport/event budget. Driver coalescing retains a same-sequence complete
repair instead of discarding it behind the rejected compact packet. Immediate
repair runs independently of the15Hz source sampling gate; a late compact-send
callback cannot postpone an already requested repair.

These are source/runtime findings. The clips do not time-align their frames with
the logs, and no merchant clip was supplied. Neither a green native runtime
fixture nor its desktop latency establishes the final headset picture.

## Paired log timing and unresolved attribution

The host records private enchantress incoming originals of37 members/29835
bytes in0.757s and15 members/26861 bytes in0.983s. The independent stock-bit63
stream ranges from0.1s to5.397s; it must not be labeled the offered private card
without source/lane identity. A repeatedly refused compact native original at
`face.333|Bottom button:0/Content:0/Layout Parent:0/Row Container:4` has structure
1B23CA78, owner basis798023A253BCF25E and observer basis820A72B8FD734195. The
logs do not identify the differing property. A transaction reports43 missing of71
required originals with42 already received,1.014s waiting and1.286s admission;
the module number alone does not prove which physical surface was absent.

Current private model geometry is now owner-authored even when its original
prefab numbers differ. The compact policy still requires exact equality for
omitted content/material/sprite fields; a genuine content mismatch receives the
complete immutable original through114. Sending geometry corrects a reproduced
native-model assumption, rather than establishing the particular logged basis
difference's sole cause.

The corrected latency fixture shares the same original row bank as actual Sync
and binds actual stock publication, exact backing construction and the genuinely
stopped native sampler. The stricter one-second cold diagnostic still fails:
one actual-stock numeric run takes1.817s, including about0.596s in real asset
Scan,0.116s in selected-original Freeze/Partition/Register and0.009s in native
basis work. Full HUD Initialize and model population remain constructor ports.
Production Initialize already prepares assets during loading/reconnect, so this
deliberately cold scan is not evidence that every physical offer incurs that
cost. It is also not a green cold-latency or hardware acceptance result.

A prepared actual-stock numeric run completes in0.994s. A genuine omitted
material refusal takes1.075s with exact-request priority, compared with1.226s
without that priority in the matched source control. Both material runs fail the
unchanged one-second diagnostic. The reverse request and complete source need
additional existing send turns; no bandwidth/event limits are increased.
Correctness, exact reconstruction, admission retry and closed-session retirement
are gated separately. All original performance assertions and failed receipts
remain available; the complete local gate must not be described as certifying
universal subsecond first-picture performance.

## Final transport and lifecycle review

An accepted missing-original request retains its exact full source at the queue
head. The former compact packet can already be in flight and deliberately shares
that source identity. Its final fragment must therefore not retire the repair
marker while the matching complete original remains pending. Conversely, a newer
based revision must not keep the marker alive after the full original finishes.
The expanded real Driver/FFS proof exercises compact fragments, the requested
full source, the newer revision and eventual queue quiescence together.

That proof exposed a separate outer scheduler failure: an exhausted four-page
opening reservation could continue masking ordinary town turns when all other
ordinary streams were empty. The full repair completed, but its newer dependent
revision remained stored indefinitely. The scheduler now releases that finite
reservation when no opening page remains. Event size, send clocks, urgent debt
and ordinary fairness retain their previous bounds. The supplied recording does
not establish this conditional source failure as its unique cause; ordinary
presence traffic could also release the former wait.

Repair cooldowns now consume no encoding slot, and attempted candidates rotate
within the existing two-original/two-millisecond pass. At least one eligible
original is attempted before the elapsed-time limit. Closed sessions retire the
repair. Census and monotonic source-sequence guards reject delayed old-session
compact originals before they can replace a newer repair bank, including when a
future-session original arrives before its census. These paths neither accept
a transaction nor acknowledge an original on behalf of another player.

The actual Driver/FFS/global scheduler proof passes708 assertions. Restoring only
the old opening-counter assignment fails dependent-revision completion; restoring
only the old compact-completion rule fails the exact pending-source marker
assertion. Both controls compile and fail their specifically named assertions.
The independent portable opening-reservation vector also verifies that a complete
eight-page opening and its ordinary newer revision progress without any non-town
traffic. Existing wire version3 and records112/113 remain unchanged;114 is
additive and115 remains the next free record.

## Preserved worker evidence

Main-checkout `.planning/debug/npc661/` retains the immutable hardware inputs,
earlier failed hypotheses and source-bound controls. Geometry worker and final
integration archives contain9974 and3004 files; the final merchant archive
contains7490 members. The delivery archive indexes27607 file entries and1444
unique blobs, with cache roots excluded and symlink targets recorded without
following them. All three lanes' archives were fully SHA-256 verified. The
delivery payload digest is
`45a75ddaefe3baae19cfcdce56ad35b6fb944a4d72f9173a421d018346ec64c2`;
its index digest is
`6fc8ca113ac76fcb4faa69a506218545c04782f35a86002f1aa24917af2f2c83`.
Worker worktrees remain available. None of the failed latency receipts is
reclassified as a passing first-picture performance test.


## Complete-gate follow-up and deterministic return handoff

The complete local runner recorded all205 suites at source6e5c93b4d with
189 passes and16 failures (jobs8,1503.745s). Its failed run and logs are retained;
this is not a green complete-gate receipt. The golden-vector executable separately
passes300419 assertions at that source. Unchanged scopes may reuse that evidence;
affected repairs must state their own source/fixture hashes and focused coverage.

Several failures expose stale fixture bindings: current Driver helper bodies,
request-aware full-repair selection, real receipt/request cleanup, and exact
historical originals must compile together. Return fixtures must replay the actual
same-packet Kind1 companions beside113, rather than silently discarding a real
canvas dependency. The continuation ring test must start at a fresh Unity frame:
its old UnscaledTime origin could precede lengthy same-frame native preparation,
leaving only0.055s of a1.25s observation interval despite0.069s actual elapsed time.
The original duration, speed, visibility, geometry and travel assertions remain.
Receipt cleanup now binds actual request storage and reset/disconnect bodies;
377 assertions include selective peer removal and full scheduling-state cleanup.

Serial reruns at unchanged production source pass the previously failing mage
first picture, unacknowledged reoffer, live mage census, full-card first picture,
native panel replacement, old-overlay control and scenario-scenery checks. The
original failing receipts remain evidence of the loaded complete run; these
focused passes neither rewrite that receipt nor establish headset latency.

A genuine return geometry error is now reproduced deterministically. The offered
source and observer clocks previously differed in the fixture, so its anisotropic
predecessor depended on editor load. With the same explicit clock on both sides,
the first native return picture separates by0.7141613mm at source6e5c93b4d. Its
new atomic canvas scale is isotropic, but the observer still adopts its older
anisotropic host scale/rotation before the external return clock. Root-scale
division cannot compensate arbitrary shear from that old parent. Removing only
the new owner-authored model105 geometry policy produces the identical failure;
that policy is not required for this cause.

The correction commits the already received atomic root/canvas target before the
native return sampler, then adopts the final external pose as before. It changes
neither native duration/easing nor child/color/alpha clocks. The geometry proof
passes3115 assertions with224 offered frames,896 camera readbacks,72 real native
return renders,46 held renders and21 new-card/census renders; seven compiled
causal controls include the exact old-host failure. The actual native merchant
proof also passes6815 assertions,2306 width/geometry assertions and all five
original engine controls on this integrated handoff correction.

The deterministic handoff follow-up and its preceding failures are preserved in
`.planning/debug/npc661/geometry-worker/geometry-native-target-6abaf162c.tar.gz`
(703741894 bytes,25303 verified entries,
SHA25654d12a723a14cbf9f74dc26dc66db15b93c3850c0a8de0312d5c8308df20f899).
Its manifest verifies every archived file/link; generated engine caches are
excluded. The archive includes the private source, original clock-dependent
failure, corrected-clock old-host failure, policy-only countercheck, seven
geometry controls, actual capacity payloads, and integrated merchant/646/motion
checks. Main-checkout focused receipts preserve the complete205-suite failure,
its nine successful serial repeats, the initially missing cleanup binding and
unused-field compile failure, and the final377-assertion receipt lifecycle proof.

## Final bounded return repair and composed validation

Two further sender defects appeared with the complete native canvas recipes.
Frozen6e needed eleven events for64 members; retaining full geometry with a
larger fitting prefix still needed ten. Record115 now represents duplicated
same-packet root/child IEEE bits losslessly, restores the original97/113 bytes
before normal validation, and leaves98 as an unchanged fallback. The actual Mono
capacity test completes all64 members in eight events of848,844,851,862,853,862,
846 and855 bytes. Ordinary-tail backoff also discarded an already proven-fit
cohort in mixed traffic; retaining that core restores progress without changing
the existing event/deadline/fairness assertions. Its isolated old-floor control
fails the original lifetime assertion. See
[NPC-661-RETURN-PACKING.md](NPC-661-RETURN-PACKING.md) for schema, CRC/bounds,
literal vectors, byte coverage and peer-build compatibility limits.

The receiver could discard a complete predecessor when a newer partial arrived
before the one render tick. It now retains at most one immutable complete prior,
with exact source identities, while the latest assembles. A late authoritative
terminal also inherited the expired active progress floor: the final native
root/print errors were25.886mm/63.353mm. Only an exact received terminal starts
the existing bounded .25s grace at full atomic activation; active flight progress
and outer stale/session cancellation remain unchanged. A complete future-mapped
terminal waits with its canvas/rect until the retained source instant. Real
64-part cold/lost delivery and an actual capped-owner-update hitch reproduce
both edges. See [NPC-661-RETURN-LIFECYCLE.md](NPC-661-RETURN-LIFECYCLE.md).

Final main-checkout receipts preserve the original failed complete205 run and
all subsequent failures. `composed-coverage-final.json` verifies206/206 local
suite IDs against SHA-checked passing logs from that run and explicitly focused
repairs. This is composed coverage, not a newly green whole local invocation.
The final production fingerprint is `04a626591` / source tree
`ff39aa5c5c49d68059198e6e9bf3cc1165e3c6c8`; later checkpoints change only fixtures,
registration and developer documentation.

The final affected nine-scope run recorded seven passes and two stale controls:
the historical658 clock lacked the new cohort ABI, and the collapsed-canvas
mutation still targeted an old indentation after activation moved to a helper.
Neither a compile failure nor its resulting NullReference is accepted as a causal
control. The historical clock now retains its exact old behavior with new ABI
ports that throw if executed in its clock-only scenario. The canvas mutation
guards each dependency before removing it. Their focused final reruns pass.
The premature return invocation during a runner merge retained its SyntaxError;
after resolution the actual native+all-nine-controls run passes normally.
An initially literal `{output}` argument was also corrected: this runner executes
literal arguments rather than expanding that token. That proof remains preserved,
and the final normal evidence-path rerun passes. Assertions/tolerances were not
relaxed for any of these repairs.

Final integrated focused receipts include:

- `final-composed-affected/results.json`: seven passes/two repaired stale controls;
  includes geometry,646/655 returns, visitor motion/depth and actual requested
  original recovery. Geometry retains3115 assertions and seven causal controls.
- `final-composed-flight-binding/results.json`:658 scope passes coherent,
  prepared, capacity, native and clock cases plus six source controls. Native
  print/body asserts8597, capacity221 and slow-clock946.
- `final-composed-lifecycle-merchant/results.json`: actual merchant passes6815
  artwork and2306 width/geometry assertions, plus five causal controls; retains
  the earlier premature return-run failure separately.
- `final-composed-lifecycle-packing/results.json`: both final scopes pass. Native
  lifecycle has22174 assertions/nine controls; packing has221 engine and1880 byte
  assertions with compiled historical-size and old-core-floor controls.
- `final-composed-source/results.json`:16/16; strict Debug/Release0 warnings/errors;
  `final-composed-wire.log`:300419 portable assertions. Docs5 pairs/10 files pass;
  config665/patch235/log4795 surfaces are unchanged. Unchanged asset bundle/mesh
  checks reuse the previously passing integrated evidence.

The private unmodified guard snapshot and independent compiled review find10
changed behavioral C# units, five propagated-build units, three new units and
no removed or unexplained units. Snapshot counts are1249→1252 generated C# files
including assembly attributes (1248→1251 without that attribute file), not CLR
metadata type counts. The final late delta is confined to Mirror, Budget, Codec
and the new protocol constant. Full Mirror contains24 changed/22 added visible
methods or explicit constructors,25 added fields and the audited initialization;
Sync changes only its actual `RegisterOfferedPhysical` handoff. Original motion,
binding neutralization and depth-order units remain compiled-identical to660.
This combines independently executed gates with the private snapshot; it does
not claim a new official whole `refactor-guard` invocation.

Packing follow-up evidence is fully verified in
`delivery-worker/return-packing-followup` (payload90358478 bytes,
SHA25653c2b8e7b8a5f249d8a5e1a46c6d5f6584560f93e713620b4953db9392bb28a3).
Return lifecycle evidence is fully verified in
`merchant-worker/npc661-return-lifecycle-6f418f899-compact.tar.gz`
(79576711 bytes,37918 files,
SHA2565ea2ece8597c2e420c74b1191ce3603287b1d3cf3ea01c0eae89313438554f75).
Earlier failed worker archives remain unchanged and all worktrees are retained.
The final seven named integration runtime proofs and retained failing/successful
runner receipts are independently archived in `integration-final-named-evidence`
(payload121620799 bytes,4865 unique blobs,36244 reconstructed files,
SHA256dfcd15e724ee37f2538c38d90bf8ffa5b59060e4425a87afb6cd47d69942be7f).
Full streaming verification checks every source/readback/compiled payload blob;
engine caches and external symlink traversal are excluded.

No new paired headset footage exists for661. The strict cold1s diagnostic and
prepared material1s diagnostic remain failures as recorded above. Exact repair
and queue-progress fixes address reproduced source causes; no universal subsecond
complete-picture guarantee or hardware success is asserted.
