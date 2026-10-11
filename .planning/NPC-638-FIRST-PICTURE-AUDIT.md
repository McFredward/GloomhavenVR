# NPC638 first-picture failure audit

Base: dev `a493956f6` (Build 637). The latest host and observer inputs both identify
Build 637. Production files are read-only in this audit lane.

## Hardware observations

In the host LogOutput, row 6352 starts an enhancement admission with 15 required
originals out of 63 prepared, and zero received. At row 6374, a 15-member 14,117-byte
original bundle finishes fragment assembly after 2.070 seconds. This measures
first fragment to final fragment, not first player action to final picture.
The admission then reports five received originals and `original-baseline` for
module 2, `enchant.inventory|`, at ages 2.016, 3.024, 4.028, 5.872 and 6.884 seconds.
Admission and actual display occur at age 12.754 seconds (rows 6495 and 6500).
A later 36-required/62-prepared picture also waits on the same named dependency.

This separates two costs: an actual packet-delivery interval and a substantially
longer missing-original dependency interval after a packet has assembled. It
falsifies the assumption that improving compression alone must solve this run.
The paired observer uses Info; its publisher does not log the exact source
baseline/sequence trace needed to reconstruct every earlier dependency. No
absence of Debug records is treated as evidence of an absent code path.

The observer also reports unavailable original native template bases for the
public rack, mount, body and crank and later `map.cardbody` physical-prop loading
errors. These are separate observed failures. Their addresses do not identify
module 2 of the failing enhancement picture; they must not be presented as the
proven cause of that 12.754-second stall.

## What the previous proof actually exercised

The Build 632 first-picture fixture improves the earlier six-widget test:
it exports the shipped 26-node enhancement-row hierarchy, builds 14 rows and 30
smaller partitions, and tests 44 visible members out of 66 prepared. It checks
owner-localized values against deliberately different observer defaults.

However its measured route is narrower than the production route:

1. It registers all 66 observer native templates before BeginSession. Their
   rendering resources are available before any measured packet arrives.
2. It drains an initial visit manifest and waits for the visitor lease before
   beginning the offered-picture measurement.
3. All source modules begin inactive and without a sampled baseline. The
   measured offer is a single capture of freshly activated originals.
4. `NativeSenderCapture629` invokes CaptureCore with `fast=false`. Real runtime
   uses the independent numeric-motion subscriber (`fast=true`) at 15Hz.
5. After that one capture, the test drains the queues. It does not continuously
   capture changing live originals throughout first-bundle fragmentation.
6. Expanded packets go straight to fixture Receive, which calls
   TownServiceMirror.Receive. The actual NetAvatarDriver.QueueTownService and
   ApplyTownServices receive/coalescing boundary is bypassed.
7. Real SourceEntry publication, native pool visibility, module retirement and
   re-registration are not part of the 44-member latency case.
8. The row font/shader uses an equivalent editor atlas, and the measured rows
   are about 8.3KiB, versus approximately 12.5KiB in earlier hardware.

Its 0.45/0.90-second result is valid for that exact warm-template, fresh-baseline,
lossless scheduling case. It is not evidence that a current live transaction
can never wait for an earlier named baseline. Earlier tests were not invalid
codec tests, but the hardware claim inferred from them exceeded their scope.

## Required replacement proof

A bounded new fixture must bind the actual production receiver queue, exercise
continuous production capture during first assembly, retain explicit named
complete-baseline dependencies, and test visibility/topology/retirement changes
without pre-constructing the final received picture. It must record first
assembled packet separately from first complete applied/active original output.
Causal controls must remove the implemented dependency repair and fail the same
visible-output bound. Source/font/asset limitations, loss and simulated scheduler
clock must remain explicit; a green editor run is not a headset or real-network
latency guarantee.

## Source-proven dependency defect and changed approach

A prior owner send establishes `module.Baseline` and, after the final fragment is
sent, `SnapshotSent` advances `NextBaseline` by 5–5.84 seconds. Send completion
was treated as sufficient to use that original as the next delta dependency. It
is not an acknowledgement that a newly joined, rebuilt or packet-losing observer
has that original. Entering a new visible transaction did not force its required
modules to send fresh complete originals. The fast numeric/same-artwork shortcut
could therefore keep emitting current motion while the current widget still
required an absent historical original. The receiver correctly withheld a
partial or incorrect native picture and waited for periodic complete repair.
This explains how good bandwidth/codec measurements coexisted with the reported
multi-second first-picture wait.

The exact hardware logs establish the missing named dependency, but do not prove
which earlier original packet was lost or retired. The causal fixture below
reproduces the same failure shape without asserting a particular network loss
rate. Native original fragment transport was also previously unreliable; the
independent transport lane verifies its bounded reliable-original repair. This
fixture itself does not exercise Steam sockets or their reliability flags.

A second source-proven lifecycle defect let a hidden dynamic original remain a
complete `SourceEntry` while its published module parts were not marked seen.
`TickCore` removed their registrations, and later revealing the same source
re-registered the same ID. Retaining existing hidden complete publishers avoids
retiring an identity which a current or queued original still needs.

The repair changes the dependency contract: each new visible required mount or
transaction edge emits one current complete original, retaining efficient
cumulative deltas afterwards. It also preserves hidden complete source
registrations. This is a different repair from retuning compression or packet
priority alone; it removes the requirement for an older unacknowledged original
at the moment the player first needs the picture.

## Focused causal controls and candidate results

The new fixture binds the production fast capture, the send scheduler with
background queue competition, fragment assembly/value pool, the actual
`NetAvatarDriver.QueueTownService`/`ApplyTownServices`, original admission and
active native clone output. It continuously captures during first delivery. Its
14 exported full native enhancement rows and 30 smaller original text partitions
form 44 current required members. It never inserts ready received baselines or
final remote clones.

| Case | Original dev637 | Candidate |
| --- | --- | --- |
| Warm owner, observer missing previous original | 110 events, 5.50011s simulated send; actual visible output at 6.214s; exact latency assertion fails; named-baseline blocker persists beyond 5s | 12 events, 0.600012s simulated send; actual Unity output at 1.295s; no named-baseline wait once the coherent picture assembles |
| Hidden actual dynamic publisher then reveal | Exact module-retention assertion fails after actual Publish/Tick removal | 41 assertions; same registered module survives hiding; reveal has a current complete original |
| Genuine observer template withheld until assembled current original waits | Separate cold-template condition; no claim that this caused hardware's baseline blocker | 136 assertions; same original sequence47 admitted 52ms after `ResolveTemplate` readiness; no later periodic original required |

The normal candidate dependency case passes 129 assertions. The historical
negative controls fail the same bounded output/lifecycle assertion, rather than
passing because of an unrelated build exception. Delayed-template publication
runs the same actual receiver route; only the fixture's two-machine template-bank
availability boundary is switched. It proves retention and replay after native
readiness, not how long a real asset bundle takes to load on a headset.

The immutable proof directories and exact SHA256 values for bound production,
receiver source and fixture inputs are recorded in
[`evidence.json`](../scripts/npc-first-picture638-runtime/evidence.json).
The current dependency and delayed-template proofs compile the same candidate
`TownServiceMirror.cs` SHA256:
`8c0f7bcaee16e1878649f85d29a74985ef8fe8c8abfc0465bf7d31cb24abc65a`.
The final historical control and candidate use identical `FirstPicture638.cs`
SHA256 `dbcf52e418418dc8b3cdab14909887779517ee46fd8a7f5b590e8916260cbfeb`.
The old failing dependency uses:
`686d002bf3b6fd401b1fe32041d9ed2419b85646b449d4fa35259d2a94601002`.
The lifecycle proof binds actual publication/removal methods; only the native
controller's pool-membership adapter is fixture-owned.

## Practical limits

The scheduler's deterministic 50ms clock is distinct from the actual Unity
engine/capture/clone time. The candidate's 0.600012s send schedule is not a claim
of a 0.6s headset first picture: this llvmpipe editor run took 1.295s. The earlier
1.224s candidate failed a real-time <=1s assertion; that receipt is retained, and
the final proof separates delivery budget from engine time explicitly. The
current no-wait-after-assembly assertion remains strict; no deadline is loosened
to conceal an unresolved original-baseline retry.

The exporter preserves the shipped hierarchy and native layout/material/sprite
descriptors, but uses an equivalent editor TMP atlas/shader. Its rows are about
8.3KiB; it does not recreate the exact larger binary font atlas in hardware. It
does not prove headset pixels, WAN RTT, frame rate or simultaneous four-player
behavior. Earlier complete-gate evidence is inherited for unchanged areas; this
is a focused dependency/lifecycle proof, not another complete-gate run.
