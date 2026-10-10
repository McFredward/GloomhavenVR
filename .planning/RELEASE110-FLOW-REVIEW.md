# Release 1.1.0 flow and presentation review

Review baseline: `dev` `ce637a1dc`, ModBuild 665, compared with release
`v1.0.8` (`4640fff2f`). This worker reviewed source only and did not modify
production code or run the complete gate. The review excludes the independent
NPC-666 offer-guide/hover/ring lane and Quest standalone work.

## Confirmed finding requiring a bounded repair

### F1: VR map hover can overwrite a protected native navigation state

Severity: high. The defect can affect both solo and multiplayer map flow and
occurs before any optional mod network packet is involved.

`MapLocationInteractor.SetHover` (`src/GloomhavenVR/WorldUI/MapRoom/MapLocationInteractor.cs:1013`)
forces a requested hover to null when the original map interaction mask becomes
locked. It correctly delivers `OnPointerExit` to the previous location, but
then calls `StateMachineEnterWorldMap` whenever that previous location exists
(`:1087`). `Release` also clears the hover through the same path (`:410`). Both
navigation helpers (`:1888`, `:1898`) check only singleton/state-machine
availability before entering `LocationHover` or `WorldMap`.

The original, read-only `GH.Runtime.dll` contains a stricter contract.
`Assets.Script.AdventureMap.MapLocationSelector.Update` performs these
navigation transitions only when the current native state is `LoadoutState`,
`LocationHoverState`, or `WorldMapState`. It deliberately does not perform them
from story, personal-quest choice, merchant, reward, travel, or other native
states. The VR replacement's own documentation says it reproduces this method
exactly, but the current-state predicate is missing. The omission predates the
1.1.0 delta; it remains relevant to the release-wide continuation review.

Concrete triggering sequence:

1. A VR laser or fingertip leaves a map location in `_hover`.
2. A native story, personal-quest choice, or another protected flow takes over
   navigation before the cached hover has cleared.
3. The next pointer exit, native map-lock transition, or room teardown invokes
   `SetHover(null, ...)`.
4. The VR adapter enters `WorldMap` over that protected current state.

This is more than an incorrect cosmetic state. The real native
`MapStoryState.Exit` restores party-tab input and enables shield input;
`PersonalQuestChoiceState.Exit` restores navigation locking and reenables its
disabled triggers. Forced state exit can therefore release continuation/input
ownership before the native flow completes. Other native exits assume their
original service widgets still exist. The VR adapter must not call them merely
because an old hover was cleared.

Recommended repair: gate the two navigation transitions on the original three
eligible current-state types immediately before `StateMachine.Enter`. Keep
native pointer cleanup, previews, independent floating town windows, map
switching, and actual clicks intact. Do not introduce a broad modal or service
input block. This needs no protocol changes and must not add any dependency on
modded peers; a flat host or client keeps original authority and continuation.

Focused validation should execute the production helper bodies against a
minimal navigation boundary: each of the three eligible native states permits
the corresponding transition; story, personal-quest choice, town services,
travel, reward, unknown, and absent navigation retain their current state.
Exercise both `SetHover` exit and `Release` cleanup, and include a causal
negative control with the predicate removed. No new complete-gate pass is
claimed for this review.

### Relation to the current paired logs

The supplied remote log identifies ModBuild 665. In
`.planning/debug/remote/LogOutput.log`, lines 6343, 6368, and 6410 report
`MapLocation hover enter threw (laser): Object reference not set to an instance
of an object`. The existing catch spans native pointer entry, capital hover,
icon reporting, and navigation entry, and records no stack or stage. Those
warnings establish a current map-hover failure, but do not establish the exact
throwing member. Finding F1 is independently source-proven; fixing it must not
be described as proving the complete cause of those three warnings. A later
bounded diagnostic can distinguish native pointer failure from navigation
failure if the warnings persist.

## Areas reviewed without another confirmed defect

| Area | Reviewed boundaries and conclusion |
| --- | --- |
| Scenario card faces, overlays, flights and scene preparation | Checked the changed preparation/factory lifetime and shared held-item pose paths. The existing scenario burn ledger and ordinary burn-flight sequencing are unchanged from 1.0.8. Preparation owns presentation/cache objects and does not write native game actions. No new concrete source defect was identified. |
| Board rules foldout and remote geometry | Read the original-widget foldout, authoritative rule-text/geometry records, remote frame application, objective seating and rendered hierarchy. Owner intermediate size/order and original content are retained; private goal text is not included in the public rules lane. Reviewed the existing actual Unity harness coverage without rerunning it. |
| Wrist board and owner configuration | Checked rig-relative anchoring, manual-grab precedence, restored ordinary seat state, remote lightweight rig state and visibility flags. Owner configuration drives remote presentation. No new concrete defect was identified. |
| Held figures and health bars | Reviewed the inert source-pose mirror, immediate native busy-action release boundary, bar pose/envelope lifetime, and depth material propagation to numbers/symbols. Clone gameplay controllers remain disabled; evaluated original poses are copied. No new concrete source defect was identified. |
| Character selection and assignment | Checked map-native owner resolution, offline ownership, nonempty selection restoration, exact requested-character verification, and modded-host request permissions. Unknown native ownership remains separate from an unassigned participant. No new defect was identified. |
| Native quest proposals and popup identities | Checked exact native proposal observation, separate original selected/multiplayer popup instances, stale cosmetic selection rejection, and teardown lifetime. Unknown initialization does not synthesize cancellation. |
| Quest readiness and departure | Checked the narrow original ready-toggle initialization option, existing quorum/ACK invocation, flat-host recovery permissions, callback identity, and original visible cancel control. No alternate mod quorum or synthetic flat-host continuation is introduced. |
| Story, encounter, rewards and introductions | Reviewed the changed modal/conversion/visibility boundary around existing original continuation, shared opening identity, native permission, and departure handling. Post-quest reward synchronization code itself is unchanged from 1.0.8. No additional confirmed defect was identified. |
| Optional retirement | Reviewed capture of the original HUD widget, console confirm adapter, restoration/fallback, and original pending promise. Native confirmation remains an actual player action; a missing mod peer is not a native retirement barrier. |
| New-save tutorial and non-immersive town window continuation | Reviewed exact tutorial step/callback capture, original close completion, and preserved town-window map-switch state. Native queue and promise callbacks are not automatically resolved by cosmetic state. |
| Laser/finger input | Reviewed visible UI admission and map interaction-mask boundaries. F1 is the confirmed missing navigation-ownership gate. Existing input checks preserve native gameplay permission; they are not a substitute for current-state ownership. |

These are source-review conclusions, not claims of successful hardware pictures
or actual packet delivery. Existing automated coverage was read where relevant;
it was not repeated here. Any authorized repair will record its own focused
test scope separately, while the integrator owns release-wide checks and the
final paired-device outcome.

## Review method

Read `AGENTS.md`, `CLAUDE.md`, `.planning/STATE.md`, the newest notes adjacent to
`NetProtocol.ModBuild`, and the native crossplay reviews. Compared changed
source paths against 1.0.8 and inspected the relevant production callers and
lifetimes. Decompiled original classes from the read-only
`ressources/GH_Data/Managed/GH.Runtime.dll` with the existing `ilspycmd` tool;
dummy asset-export classes were not treated as original behavior. Inspected the
current remote build banner and the map-hover failure context. No game reference
files, hardware logs, existing worker files, or production code were changed.
