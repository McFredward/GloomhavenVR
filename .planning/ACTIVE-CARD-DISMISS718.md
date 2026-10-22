# Original active-card dismissal flights — Build718

The maintainer's 2026-10-22 follow-up requires the card placed in the physical
dismissal berth to fly into its native destination pile, locally and remotely.
Build716 introduced explicit placement and native confirmation; this repair
completes the departure without adding a gameplay command or another card.

## Source findings

The staged card remained a child of the item-use recess. When the original
confirmation removed its active membership, the dismissal pump cleared the
reservation and hid that parent. The pile rebuild could run one frame later;
its existing flight entry rejected a card inactive in the hierarchy. A flight
started before the clearing pump could likewise remain under the disappearing
parent. Native cancellation can refresh the active UI synchronously, so keeping
only a later active-grid address was insufficient.

Remote staging118 and a later native active-list removal can arrive in either
order. The legacy active-flight address describes the old active grid, not the
physical berth. A recent semantic flight also disappears after two seconds,
whereas native burn completion75 intentionally survives it. Exact provenance
and departure geometry must therefore survive both paths, without selecting a
replacement card by a changed active-list index.

## Implementation boundaries

Capture the original pool provenance and active-list source before invoking the
original accepted closure. Only native discard/lost/permanently-lost membership
releases the staged original. Transfer its current transform to a stable board
root before hiding the berth, retaining a mid-settle confirmation's actual
pose. Existing native burn-artwork completion and pile flights remain the
presentation owners. A cancelled or refused callback cannot invent a flight.

Additive paged122 attaches exact berth-relative launch geometry and immutable
native pool provenance to the unchanged62 recent-flight and75 durable-burn
cohort. Missing, conflicting or incomplete pages cannot select another original.
Existing endpoint grammar, visibility flags and old record bytes stay fixed;
wire version3 remains unchanged. Native gameplay and network authority remain
with the original callback and model lists.

The extra geometry is sampled once per departure. Repeating animation positions
are not streamed. The maximum presence envelope is bounded at12800 bytes, with
12515 actual worst case bytes and a12772-byte allocation buffer. Only presence
assembly above the former8000-byte boundary extends its lifetime from validated
encoded page count. The actual saturated32-stream plus town-lane fixture takes
9.5 seconds at both90 and18Hz; the finite maximum allowance is10.2 seconds.
Smaller presence retains five seconds; other stream/gameplay timeouts retain
their previous behavior. Advertising a large compressed raw length cannot
extend assembly time. Optional context is absent for ordinary flights.

Marked remote departures keep their exact original while native membership or
public artwork is delayed. A generic two-second cosmetic timeout cannot discard
the only discard flight. Original identity, actor/control, recovery and scenario
lifetimes still retire this bounded reservation. Detach the resident original
from the closing berth before its outer parent deactivates, stop its old settle
targets and release the renderer exactly when the first drawable flight takes
over. Preserve durable burn timing and recovery separately.

## Validation

The integrated first affected run passes all15 suites: native dismissal API and
physical flow, card binding/loss/round/scene/pool lifetime, burn layout/replay/
materials/item behavior, item placement, local/remote selection and presentation
send. The expanded local flow executes83 assertions and12 compiling causal
controls, including the production destination classifier, launcher and source
reporting with explicit engine/native adapters. Strict Release builds with zero
warnings/errors; the first source group passes16/16. Complete unfiltered golden
vectors pass306431 assertions, including frozen old bytes, exact122 cohort and
page permutations, terminal history expiry, malformed-data quarantine and
saturated assembly. Final remote proof and compiled review follow below.

The final private717-to718 compiled review contains26 changed types and two
added types, with no removal. Six differences contain only the inlined build
number; four contain only the inlined presence capacity/build number. The rest
implement the reviewed dismissal ownership, exact source context or bounded
assembly behavior. All669 configuration keys,242 patches and4816 log tokens
remain; bundle-format and source-surface checks pass. The final snapshot
includes the retained-panel guard before ordinary active-parent/glide writes.

Integrated existing remote burn sequencing and completion suites pass; the
final remote selection rerun passes. The first expanded remote run passed its
63 production assertions, then found a stale expected-message binding in the
retention negative control: the new earlier frozen-panel assertion correctly
rejected that mutation first. Production code did not change for this fixture
repair. Preserve that failed receipt and the final complete remote rerun.

The final unfiltered remote rerun passes63 production assertions and all34
compiling causal controls in113 seconds. Integrated affected coverage now has
18 distinct passing local suites across the retained focused runs, plus the
final remote-selection rerun, source16/16 and306431 unfiltered golden assertions.
The failed intermediate test-binding receipt is retained, never counted as a
pass. Final receipts, compiled/surface review, archived worker evidence and
deliverable hashes live below `.planning/debug/dismiss718/`; every completed
fixture run records canonical cache cleanup. This is focused coverage with
unchanged717 evidence, not a new full240-suite invocation.

Unchanged areas inherit the explicitly composed Build717 coverage described in
`MP717-VALIDATION.md`; this is not a new complete local suite invocation. Test
completion uses canonical cache cleanup, retaining logs, sources and result
receipts. Worker evidence is archived and byte-compared before removing only our
completed worktrees. Headset pictures and real multiplayer timing remain
unverified until the next hardware test.
