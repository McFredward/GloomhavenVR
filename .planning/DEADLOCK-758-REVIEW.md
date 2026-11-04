# Single-player and multiplayer progression safety review

Base: dev `cd827d688`, Build757. Requested2026-11-04 after the unresolved
Issue27 turn-transition report. Classification C: review and potential repairs
span native UI lifecycle, input, multiplayer requests and presentation.

## Scope and method

Inventory every production C# source; trace progress-relevant callbacks, waits,
gates, leases, deferred work and teardown against the read-only native game.
Separate a legitimate mandatory decision, a missing player affordance, a mod
presentation/input lock and a native/network synchronization failure. For a
concrete repair, reproduce the old failure and retain a compiling causal control.
An existing green test or a source grep alone does not certify progression.

Independent worktrees start from the same integration base:

| Owner | Production scope | Detailed record |
|---|---|---|
| cards worker | Cards and Board | DEADLOCK-758-CARDS.md |
| windows worker | WorldUI except map/town and their explicit composites | DEADLOCK-758-WINDOWS.md |
| map worker | MapRoom, TownServices, Map/Quest/Loadout/Ready composites, Compat/Tutorial | DEADLOCK-758-MAP.md |
| integrator | Net, Core, Rig, Hands, Voice, remaining Compat, Plugin, defaults and integration | This record |

Workers own focused proof. The integrator reviews the final union, runs one
complete local gate with golden vectors, strict applicable configurations and
localization, and explains compiled/surface changes. Bounded fixture repairs
reuse only unchanged input-bound evidence; failed attempts remain failed.

## Authority and recovery constraints

Never fabricate readiness, skip rules, auto-confirm a choice, drop queued native
actions or rewrite saves to hide a stall. A repair may restore presentation
liveness after the original completed UI lifecycle, retain a reachable original
callback, or explain a pending action. Shared decisions and world geometry retain
their existing author and flat/unmodified-client compatibility. Town pre-drop
guides remain visitor-local. Ordinary windows stay fixed; the already approved
three-second out-of-playfield story recovery remains narrowly scoped.

The reported Issue27 incident is still unassigned to a captured log transition.
New independently proven failures must not be presented as its established cause.
No finite review can establish immunity to every game, runtime, hardware or
network failure; remaining limits must be stated rather than hidden by forced
continuation.

## Storage

The root filesystem starts with approximately15 GB free. Worker heavy reference
trees are read-only shared symlinks with task-private index exclusions. After
every test, reap owned processes and call canonical `cleanup_completed_run`;
retain original logs, results, source/input hashes, captures and current
deliverables. Do not touch other agents' worktrees or shared game references.

## Concrete findings and remedies

| Failure / misleading state | Source-proven cause | Remedy and preserved boundary |
|---|---|---|
| Later mod observers miss an unlock, hand or scene edge | One multicast invocation catches around the entire list; an earlier exception aborts later listeners | Isolate each observer in original order, including ModeChanged; weak cached snapshots preserve reentrancy and avoid per-notification allocation. Reuse bounded TickGuard reporting. |
| Card interaction remains busy after an aborted load | Clearing queued work drops the callbacks which release four driver admission flags | Explicit cancellation notifications release only those mod flags. Neither a native action nor its ordinary completion callback runs. |
| Long-rest continuation can wait on a canceled UI iterator forever | Original CancelAnimateCardLost stops the iterator that owns the final AnimatingLostCards=false write | Finish only successful original cancellation bookkeeping, guarded by its owned-running state and replacement generation. Preserve callbacks and native exceptions. |
| Enhancement sale never shows its confirmation after a peer begins joining | Native ConfirmSell writes its confirmation-open latch before the joining-player early return | Apply the identical refusal before the write; an explicit later retry still uses the original method and authority. |
| Mandatory confirmation remains at the converter origin | Conversion succeeds with no head camera and is never placed again | Retry only that opening's missing first placement. Settled/manual poses and original choice identity remain. |
| A loaded original window never becomes visible again | Global6Hz empty-content probe deadlines let the first empty window monopolize all checks | Track independent weak per-original deadlines for both refusal and hold sets. Native open/content and manual-close predicates remain. |
| Optional figure lesson asks for an impossible gesture | Lesson holds the native message chain while figure grabbing is disabled, including a live setting change | Skip admission or finish only this optional mod lesson through its existing original-message replay path. |
| Enchantress has no usable card chooser after optional fan failure | Configured intent still claims the original controller after the ability-hand capability has permanently failed | Restore the original enhancement window, retain a genuine pending confirmation, leave merchant items and temple offerings available. |
| Departed peer/held-object records survive cosmetic destructor failures | ForgetPeer/disable cleanup invokes failing Unity owners before identity retirement and later releases | Detach the old identity first and isolate each cleanup owner. Teardown snapshots exact avatar identities; reentrant replacements survive. No native gameplay action is synthesized. |
| Rest cue falsely suggests the last playable pair cannot be used | Cue reads only the shrinking hand, excluding already selected round cards | Local and remote call the same native actor predicate, including phase and extra-turn exceptions. |
| An otherwise empty board gives no instruction | Ordinary selection/action/target states have no specific banner fallback | Explain actual current input below mandatory item/card/panel/exhaustion prompts. Respect modal/burn/ownership/long-rest/EndTurn admission. Reuse the existing owner-authored mirrored banner. |
| A prolonged automatic barrier is indistinguishable from missing input | Existing queue recorder does not explain an empty-queue animation/ACK barrier | Passive20s action/turn/round wait notice plus one normal-level incident and a completion line. Read native local-ready state without accusing a participant or fabricating a receipt. |

These are independently reproduced source failure classes or bounded player
guidance. They are not twelve observed hardware incidents and are not proof of
the historical Issue27 cause.

## Integrator review coverage

The production inventory presently contains1170 C# files, excluding generated
bin/obj and noncompiled asset payloads:162 Cards/Board,294 window surfaces,
158 map/town/tutorial and556 remaining integration sources. The retained
inventory names every path and SHA256; counts include newly added helpers and
are finalized against the delivered tree. An inventory is coverage accounting,
not proof that every execution path was exercised.

The integration review traces event/mode composition, hand tracking and pointer
cancel/release, scroll-versus-locomotion ownership, rig rebuild/recenter,
session reset/rejoin/flat-net mode, version mismatches, original host-mediated
enemy/encounter/reward requests, shared window/quest/story authority, town lease
election/expiry and stale-offer refusal, normal-level anomaly isolation and the
original turn barriers. Incoming presentation loops are length/count bounded;
catalog parent chains reject cycles. Remote presentation never authorizes native
actions or waits for an unmodified peer's absent mod packet.

Startup receives a secondary review of all three Preload files, all seven
Core/Startup files, CoreModule, scenario preparation and room-loading owners.
Native XR initialization can itself block in a driver, but the mod's candidate
and relaunch loops are finite; absent dependencies retain flat fallback.
Cosmetic preparation never owns native input/loading readiness and retains its
finite lazy-presentation fallback. Rendering/LOD/vegetation/material helpers
are checked for loop termination, interaction-owner exclusion and restoration;
they are not represented as gameplay branch coverage.

Native EndTurn hides controls before the next actor begins and legitimately
waits for effects/queues/idle states and participant acknowledgments. Do not
replace that with a guessed timeout completion. The retained Issue27 client
record has36 completed native turn closures; video Round11 remains unmatched
to Resume14/new1–4. A new notice identifies a future long barrier even when the
native hand is temporarily absent; it does not prove which remote peer failed.

## Focused evidence before integration

- Event bus plus actual mode coordinator and bounded notice:54 assertions/four
  compiling controls. Initial fixture XML-reference compilation failure remains
  recorded; production plugin documentation is checked in the strict build.
- Cards/Board:433 assertions/16 compiling controls; complete original cancellation
  body with declared scheduler/drawing ports, actual queue and source-bound driver
  callers. No paired Photon or headset claim.
- Map flow:2024 assertions/nine compiling controls plus one source control;
  tutorial:51/nine plus17 source bindings and one source control.
- Native enhancement handoff:1517/80; quiet controller:181/17; supplemental native
  mode replay:225/three. Initial fixture-only facade failure is retained.
- Confirmation:388/31 portable and21/two original-UI/Unity/pixel checks;
  modal content recovery:188/ten portable actual-caller checks.
- Peer retirement:301/ten; complete actual driver methods and TickGuard with
  declared cosmetic/native subsystem ports, including historical757 method
  controls and same-ID replacement tests.

Final integrated strict builds, allsource/complete local suites and golden
vectors remain pending at this checkpoint. No partial worker result is labeled
a complete gate or hardware acceptance.
