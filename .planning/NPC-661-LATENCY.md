# NPC661 latency investigation

Both supplied hardware log banners identify Build660 `12df1353f`. The host
debug stream observes the other peer's enchantress, whereas the remote log is
Info-only. No owner-visible timestamp or paired video clock is available for
that enchantress. The supplied video was inspected in all 18 balanced frames;
progressive panel population may also be actual owner UI updates and is not
assigned a network cause without corresponding owner evidence.

## Exact original rejection

The host repeatedly refuses a compact original for the `face.333` bottom-button
Row Container: owner basis `798023A253BCF25E`, observer basis
`820A72B8FD734195`, structure `1B23CA78`. One actual transaction reports module
72 missing and finally admits after 1.286 seconds. The available Info owner
stream does not establish which physical output generated module 72.

Source inspection establishes that a compact refusal previously waited for the
complete outbound atomic bundle callback, a further 150 ms receipt grace,
another 15 Hz capture turn, and the queued full original. Record 114 now requests
only the genuinely refused exact private original. It neither credits a receipt
nor changes native template validation. The owner retains the same immutable
full original and sequence, ignores stale/wrong-owner/withdrawn/already-received
identities, and encodes at most two requested originals or 2 ms after its first
encoding per render pass. Both directions retain existing transport budgets.

The first-arrival admission clock now survives required-census changes. A
separate census age identifies subsequent actual owner membership changes; the
bounded significant-anomaly budget is retained across a transaction.

## Honest clock boundary

The new `scripts/npc661-latency/run.py` reuses the maintained actual native
exporter, capture, scheduler, fragment, receive, validation, apply and rendering
bindings. Its primary clock starts before the first owner draw and all actual
selected-original freeze/partition/registration and basis preparation. It never
restarts after preparation and preserves the existing one-second assertion.
The secondary `--prepared-control` labels its excluded preparation explicitly.

Initial cold run `run-dwf3v53g` **fails** the unchanged one-second threshold:
first owner draw 0.113 s, native bank frozen at 0.585 s, subsequent asset/basis
preparation 0.636 s, complete rendered observer 2.780 s. The prior prepared
boundary omitted roughly 1.22 s before delivery in this run. The separate
prepared request run `run-pckyn0ta` also **fails**, at 1.179 s. These are measured
Unity editor/llvmpipe fixture times, not inferred hardware elapsed times.

The native serialized hierarchies and every original material/graphic property
remain real. Native gameplay `MakeAbilityAction`, model/container population and
full HUD Initialize are explicit constructor ports, and the inherited physical
body fixture remains a separate limitation. A passing prepared boundary does
not prove the whole cold hardware lifecycle. Further pipeline work and causal
controls remain required; this checkpoint does not claim the latency target is
met or that a headset outcome has been verified.

## Corrected bank and causal source repairs

The initial expanded fixture froze each repeated enhancement option row under a
different address. Actual `Sync.Publish` uses the shared `enchant.row|` native
part for all those source instances. Correcting that fixture boundary preserves
all 82 source hierarchies and 45 required originals, but removes duplicated
canonical row banks. Consequently the initial 2.780 s is not an actual
`HUD.Initialize` measurement, and its 1.22 s preparation cannot be assigned to
hardware. No production improvement is credited to this fixture correction.

Model card partitions now explicitly carry every owner's child Transform in
record 105. Actual native model generation computes Row Container rects and TRS;
an unchanged owner's local default must not imply equality with a separately
generated observer default. Original binding/structure and all omitted material
and sprite fields remain exactly verified. A real 32-node native partition is
6,219 bytes full and 2,288 bytes compact. The old policy counter-control fails
the assertion that every model child carries authored geometry. A different
omitted material still refuses before paint and receives the identical full
source and sequence; no guessed native material is accepted. The legacy NPC658
mismatch adapter now challenges that still-omitted material, retaining its
existing full-source equality and strict deadline assertions.

An accepted record 114 request marks only the exact current private original at
actual enqueue. Its existing module queue can supply urgent pages before a new
bundle of speculative fallback originals. This keeps atomic bundles, pending
delta dependencies, module/session retirement, the 16-page opening limit,
two-urgent/one-background arbitration, and the global 864-byte/50-ms budget.
Plain originals and template/address matches do not manufacture such a marker.

The runtime now also invokes actual `Sync.TickStockCore`, native `Publish`, the
actual stopped `VRCard.TryTownReturnMotion`, and the exact procedural backing
factory. It captures 18 hidden complete stock originals, totaling 36,922 raw
bytes. Stock retains full record 103, its independent lane, and its ordinary
repair semantics. All measurements below include that concurrent stock source.

| Boundary / challenge | First complete | Events / wire bytes / members | Result |
|---|---:|---:|---|
| Prepared, different child numeric geometry | 0.994271 s | 16 / 13,003 / 46 | Strict 1 s passes |
| Prepared, different omitted material, requested priority | 1.075182 s | 18 / 14,165 / 47 | Strict 1 s fails |
| Same prepared material/stock, old priority source control | 1.226482 s | 21 / 17,113 / 50 | Expected strict failure |
| Cold selected-source bank, numeric geometry | 1.817150 s | 14 / 11,711 / 47 | Strict 1 s fails |

In the material/stock comparison the initial atomic picture arrives near 0.74 s
in both policies. The old policy completes a later three-member/10,679-byte
fallback bundle after a further 0.257 s assembly; the new policy supplies the
requested 6,219-byte original independently. Two extra 50-ms events compared to
the numeric case and the actual reverse-request/full-original/apply lifecycle
remain. Timing stages overlap; their CPU times must not be added to manufacture
an exact explanation of the final 75 ms.

The cold run's owner first draw is 0.104325 s; its actual selected native
Freeze/Partition/registration finishes at 0.220278 s, an additional 0.115953 s.
The subsequent actual asset scan costs 0.595857 s; native basis preparation costs
only 0.008805 s. Capture costs 0.056513 s, receive/apply CPU 0.306090 s, and the
first complete atomic assembly arrives at 1.573389 s. This corrects the earlier
assumption that the approximately 0.65 s was mainly basis hashing.

The scan includes the real dormant native atlas and exact packed-member geometry
digests. Production `NativeTemplates.Initialize` already invokes preparation
during loading and reconnect, and `Assets.Scan` has a two-second guard. The
fixture deliberately starts with an empty registry after the owner's first draw;
that is an explicitly stronger cold diagnostic, **not** proof that a normal
hardware offer executes this scan. Full HUD/model initialization remains a port.
No broad asset scan suppression or cross-lane original cache is implemented.

Actual stock begins preparing this ability after `Handoff.Card` is assigned on
offering (`TownServiceEnhancementHandoff` adoption paths). There is no analogous
held-ability stock branch before offering; merchant owned chips have their own
preparation path. A stock copy is not automatically an original receipt for the
private lane. Any future reuse would require explicit peer, template, source,
binding and omitted-property equality; name or address equality is insufficient.

## Focused correctness evidence

`requests-queue-final/run-0zqgfjp_` passes 681 assertions with actual driver
request/repair calls before the blocked 15-Hz gate, actual FFS admission and
bounded metadata/presentation queues. It verifies incompatible/stale/withdrawn
requests, native close/reopen, at least one repair after an expired pre-encode
CPU budget, same-sequence compact/full receive coalescing, exact full-source
values, genuine receipt suppression, no invented ordinary marker, dependency
retention, single full completion, finite marker drain and idle quiescence.
The prior old CPU-guard counter-control fails the required progress assertion.
The frozen compiled sources, source hashes, DLLs, native screenshots/readbacks
and failures are retained under `.planning/debug/npc661-latency/`.

Debug compilation passes with zero warnings/errors. These are focused worker
checks, not a new full integration-gate or headset outcome. The cold and true
material-refusal strict performance failures remain open and recorded.

Independent review found two further source failures before integration. Retry
cooldown previously consumed encoding slots, so a failing source could prevent
later requested originals from ever being attempted. Eligible repairs now skip
cooldown before consuming the two-original/2-ms budget, and attempted candidates
rotate. The actual driver/FFS proof persistently refuses the first two admissions
while the third succeeds before their cooldown expires; the old fairness source
control fails that progress assertion.

Native expansion also preceded census admission. A late refused old-session
compact could overwrite the unprepared module and reset the entire newer
request bank. Census/source dominance now runs before retention, and each peer's
private request bank retains its highest monotonic source sequence when sessions
differ. This also protects a newer uncensused session from a delayed *different*
module of the preceding session. Tests replay new census 670/sequence 5000,
current rejected originals, old session 661, and future 671/module 1/sequence
5002 followed by old 670/module 2/sequence 5001. Both current batch and newer
future identity survive. The frozen older production methods in
`OldRequestRetention661.cs` are negative source-control inputs only. The old
retention source control fails the current-batch preservation assertion.
The final combined request, transport queue, failure-fairness and cross-module
session proof `requests-final-crossmodule/run-3mh8hfgl` passes 692 assertions.
Its old fairness and old retention controls fail their specifically named
progress/current-batch assertions. A separate owner-bank control removes only
the sequence dominance guard, preserving the newer census/module guards, to
challenge the uncensused cross-module case independently.

## Compact-flight completion and bounded reservation retirement

An already fragmented compact packet and its exact full repair share the same
immutable owner object. Completing that older compact packet must not retire a
known request while the matching full source is still the pending head. The
queue now tests that exact head identity; a later dependent delta does not keep
the marker alive after the full source itself completes. Marked module IDs do
not enter a new ordinary bundle, while previously started background atomic
bundles remain intact. Only the accepted exact-source classifier may give an
ordinary merchant confirmation original the same bounded dependency pin.

The expanded real driver/FFS test gives one native TMP widget legitimate long
text, making its compact source span multiple compressed module pages. This is
an explicit transport challenge, not a hardware card-density performance input.
It starts that compact assembly, queues its newer based revision, admits the
exact request/full repair, and verifies all three finish once with normal marker
retirement. No packet content, owner identity or acknowledgment is substituted.
The existing active-module cursor is a declared state port for the separately
tested already-started lane.

That challenge also exposed an outer scheduler failure: after four opening pages,
an exhausted reservation could keep skipping every regular town turn when all
other ordinary streams were empty. The requested full originals completed, but
a newer delta remained pending while the actual FFS scheduler returned idle.
The root-owned scheduler now resets that finite opening counter when no reserved
page remains, preserving its event budget, clocks, urgent debt and ordinary
fairness. This conditional source failure is not attributed to the supplied
hardware recording; ordinary presence traffic may also release the old wait.

The combined proof `requests-fixed-opening-flight/run-_jg8_2ml` passes 708
assertions through the actual FFS/global scheduler, including the previous
failure/fairness/session guards. Queue identity/readbacks are retained alongside
the initial expanded failures, where the based delta was correctly still stored.
Those failed runs are not counted as successful marker counter-controls.
Restoring only the old scheduler counter assignment in
`requests-old-opening-reset/run-o2ukpb7b` fails the unchanged dependent-revision
completion assertion. Restoring only the old compact-completion rule in
`requests-old-compact-marker-completion/run-iqtda6oj` fails the assertion that
the matching pending full source still owns its request marker. Both controls
compile and fail their specifically named production assertions.
