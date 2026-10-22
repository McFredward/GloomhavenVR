# Multiplayer latejoin selection presenter, Build717

The four-player hardware session ran release Build715 on all clients. The final
guest is `remote/1`: native `Player.log`45519 records player4 joining;
69167 records assignment of Elementalist/Cryonaris, controllable-1184212992.
The mod ownership census at69381 already resolves the correct local claimant4.
This is distinct from waiting for network ownership or card artwork to load.

At69192/69199 the native ownership callback nevertheless switches Berserker to
Berserker. At69380/69386 the VR board shows the guest's own hand through the
read-only selection floor and explicitly refuses placement. The eventual
recovery immediately follows `Tile handler select actor`120054 and native
`Switch hand Elementalist from Berserker`120055;120074/120078 change the same
hand to interactive. The supplied logs lack per-line wall-clock timestamps, so
these line references establish causal ordering, not an exact waiting duration.

The retained original `CardsHandManager.OnMyControllableOwnershipChanged` body
explains the gap: it clears native selection, then either keeps the old current
hand using the actor's stale `IsUnderMyControl` flag or selects the first living
hand without a local-ownership test. Existing `SelectionHandDrift` only repairs
an already locally owned native selection. A latejoin whose native selection
and presenter both remain foreign has no repair candidate and stays read-only
until a real native selection finally supplies one. Both watchdog method bodies
are byte-identical between release4f10ddd60 and worker-based4892a097; this is an
existing Build715 gap, independent of Build716's active-card dismissal.

Extend that existing native hand-switch watchdog only inside card selection.
When neither native selection nor presenter names a live owned hand, resolve the
first living locally owned `CardsSelection` hand. An already owned presenter, a
genuine deciding-hand flow, the original no-switch messages, control abilities,
missing native objects and non-selection phases retain their original authority.
Do not grant input to the presentation floor or widen `CurrentHand` ownership.
The original `SwitchHand` callback must succeed before the normal rebuild and
placement paths see an interactive hand. Recheck both native admission and modal
state inside the queued callback; an actor replaced before execution resolves
freshly, and a native hand switch that already won the race makes it a no-op.
The successful recovery emits one lifecycle report, not a per-frame trace.

`tests/GloomhavenVR.SelectionLateJoinTests/run.py` executes extracted production
`SelectionHandDrift`, `SwitchHandTo`, `PumpSelectionHandSwitch`, `CurrentHand`,
`PlacementIsOffered` and the complete `UpdateWantedSlots` method. Native game,
ownership-list, focus and Unity boundaries are explicit surrogates. It proves
47 assertions and six compiling causal controls: foreign-selection refusal,
candidate ownership, native phase, queued live revalidation, modal admission and
native decision retention. The scenario starts unassigned, gains ownership while
the native presenter stays foreign, offers no placement before the queued native
switch, and obtains mask3 immediately afterwards. It covers foreign/null native
selection, missing presenter, steady owned presenter, selected-character drift,
dead/missing hands, all three native message fences, control abilities, genuine
decisions, modal release, ownership/phase/actor changes while queued, and races
won by an original native switch. No new wire field is needed: the unchanged
owner-authored wanted-slot mask already supplies remote-board overlays.

The fixture does not run the native ownership callback itself, SRL ownership
replication, transport or headset rendering. The native decompile and supplied
paired logs establish the callback's output; the fixture executes the repair
from that boundary. Strict Release passes with zero warnings/errors. Focused
native card-selection regression evidence is recorded separately by the runner.
No new complete local gate or physical multiplayer acceptance is claimed here.

Worker evidence: `.planning/debug/mp717-latejoin/` (native decompiles, source
identity proof, exact hardware events, strict build) and
`.planning/debug/selection-latejoin/run-cxdbnqcm/` (47 assertions/six controls).
The first fixture control attempt did not compile due to an unconditional
mutation creating unreachable code; the corrected nonconstant control compiles
and fails at the intended assertion. Canonical completion cleanup removes each
finished fixture's caches while retaining source, manifest and logs.
