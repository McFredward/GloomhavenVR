# Build764 late visitor / native victory admission

Class C: native story lifecycle, network recipient admission and native queue locks.
Worker branch `worker/mp764-results`, based on dev `fb03d1e64` (ModBuild763).
Root integrates the final union, owns ModBuild764 and the complete local/source/wire gate.

## Hardware causality

Input is the immutable paired capture in main checkout
`.planning/validation/build764/hardware763/{local,remote}`. Both banners identify
763/fb03d1e64; local is SteamVR/OpenXR, remote VirtualDesktopXR. This is a producer
admission defect before final Results rendering, not evidence of a missing result
capture or of a dropped DebugWin packet.

* Host read and completed the native scenario intro offline (local LogOutput
  lines1965/2695). Hosting/join followed: Player.log action state18671 is
  `ProcessFreely@StartOfRound`; visitor joins20637.
* Visitor opens its real native intro (remote LogOutput1996/1997; second
  load10542/10543). There is no successful native story completion until the
  session is left (9199 /22871). The shared reveal waits for the host that
  already finished; the visitor's original StoryController remains visible to
  native logic even though its VR surface is unrevealed.
* Host native DebugWin actions1/5 are emitted (local Player56854/123882).
  Visitor receives and queues them (remote Player43193/104532), but neither is
  dispatched. Native StoryController.TryBlockedUpdate owns both the genuine
  ActionProcessor lock and Choreographer update blocker. Queue delivery alone
  cannot progress through these locks.
* Remote already receives Results data but reports that the original Results
  opening has not appeared yet (remote LogOutput7791/18728). Refusing a fake
  Results surface here is correct. The same original story lock prevents card
  selection readiness after late join; root owns that downstream proof.

Earlier picture-only repairs could not cure a missing native producer opening.
Normal completed-story history intentionally cannot grant new recipients: doing
so would dismiss future/repeated messages. This fix preserves that general rule.

## Production change and authority

Only MapStoryLifecycle, its new ScenarioStart partial, and three additive ledger
helpers change. Results, movies, cheats, actor state, wire83 grammar, generic
binding/FIFO rules and native methods remain unchanged.

A bounded memo records the latest genuine native ScenarioStart occurrence, even
before multiplayer starts. It is scoped to the weak identity of the actual
ScenarioManager.Scenario and native CurrentGameState.Scenario. The exact native
CLevelTrigger constructor tuple must match the original first-round start event;
UI, dismissal, room and end messages never qualify. It records completion only
from the successful original OnFinishShow postfix. Native callback exceptions
are not success.

A finished memo retains immutable semantic/content/page-count proof and an exact
ledger token, never an old native callback/scene graph. Thus native message GC
cannot erase successful proof. Repeated same-semantic native starts replace the
prior proof. New scenario, unload, non-scenario state and full reset invalidate it.

A late enrollment requires all of:

1. Current active native multiplayer **host** and actual local native ID.
2. Existing current-version receipt for the same live native peer object and
   VersionGuard.SessionEpoch, plus existing continuation roster membership.
3. A codec-valid reciprocal live request addressed to this host, in the sender's
   currently accepted epoch/token and the most recent matching occurrence.
4. Exact semantic/run identity, original content hash, page count and one-way
   scenario-start direction. Request must still be unfinished.
5. A successful latest original start in this exact current native episode.

Cheap request/memo predicates precede reflected native roster access. One thousand
ordinary83 packets perform zero native player reads. Enrollment is noted once at
normal log level; existing native continuation exceptions retain bounded warnings.
No per-packet normal diagnostics were added.

The existing ledger then gives that visitor only the completed matching native
start. The visitor uses its original UICharacterStoryBox.ShowLine -> OnFinishShow
-> ShowNext -> TryUnblockedUpdate. This releases real native locks, reapplies its
controlled-character state and sends the original reliable GameLoadedAndClientReady.
The existing native dispatcher can then process queued DebugWin and create the
original victory/results UI; no UI clone, forced phase, automatic game decision
or replacement gameplay action was introduced.

Every VersionGuard session edge retires ledger bindings, tokens, peer state,
retry state and local player ID. Pending memo entries retain only their weak
original opening identity with token zero; they grant **nothing** before exact
native success. This covers a read-only query observing the epoch before a late
native success postfix, including synchronous successor opening before that
postfix. Completed immutable episode proof also survives, with token zero.
Unknown local identity cannot resolve pages or accept poses until a new packet
context supplies its current ID. Root additionally calls the existing ForgetPeer
on genuine native peer departure, so same-process/same-ID reconnects cannot reuse
retired one-to-one bindings. Ordinary snapshot staleness is not native departure.

Unmodded/flat host retains the existing original placement-surrogate election and
manual local story continuation; it needs no wire83 receipt or invented native
protocol. Ordinary cheat permissions remain unchanged: the borrowed read-only
NativeCheatQueue.HasCurrentVersion helper is independent of CheatsEnabled, and
progression repair also works with Cheats disabled.

## Validation and declared ports

Owned suite `scenario-start-visitor-runtime` executes actual full production
MapStoryLifecycle/partial/ledger/codec/run identity/version receipt helper, original
native StoryController ShowImmediately/OnFinishShow/ShowNext/TryBlockedUpdate/
TryUnblockedUpdate, original native action queue/state/dispatch bodies, original
ClientDebugWin/WinScenario/WinScenarioCoroutine, original start-trigger constructor,
original content hashing and actual common initial-author/spawn bridge. Native
binaries and existing excerpt manifest are hash-checked; generated source,
executed DLL, logs, inputs and cleanup receipts are retained.

Explicit ports: native scene/model identities; clock/scheduler/socket/roster;
first-page animation/reveal drawing; final Results.Show drawing/complete-level
callback boundary. There is no Photon session, HMD, real GPU capture or new final
Results pixel certification here. Existing final Results presentation remains in
root's complete union; root's separate selection companion executes the actual
native completion consumer and card/fan/overlay recovery. Automated green is not
new hardware acceptance.

Final owned scope:

* `scenario-start-visitor-runtime`: **87 assertions, 16 compiling causal controls**.
  Complete owned invocation62.03s. Production covers offline hosting, exact successful native closure, original
  locked queued DebugWin -> native result producer, no repeated terminal callback,
  17 refusal conditions with empty-ledger checks, ordinary end/room/repeated and
  unfinished stories, GC, cheap ordinary traffic, unmodded surrogate/manual path,
  Cheats disabled, same-ID/new-entity reconnect, old packet rejection, host/guest
  session boundaries, pre-first-packet ID0, actual callback failure, query before
  finish both offline/online, and a genuine successor that stays blocked.
* Existing `map-story-lifecycle`: **293 assertions, 10 compiling controls** and
  story/pose binding source checks (26.92s). General recipient/FIFO/epoch/page laws survive.
* Strict `ci-build.sh Release`: **0 warnings, 0 errors**,11.35s.
* No worker all-local/wire rerun. Root owns the required final class-C union once.

Controls revert offline provenance, visitor enrollment, actual host check, current
native receipt, episode identity, exact native trigger, actual native success,
reciprocal addressing, cheap gate ordering, immutable GC proof, flat-host surrogate,
session reset, token retirement, pending weak provenance, direction precheck and
local-ID retirement. Each compiles and fails its intended causal assertion.

Development failures retained honestly: initial strict build lacked the explicit
ScenarioRuleLibrary using (1 error); first fixture imports lacked PresenceState;
next lacked several declared native model ports and contained an unused copied
private helper. One early semantic test accidentally reused guest epoch/token1
for two different native requests; corrected to a single monotonic original
ledger instead of weakening production. These failed attempts are not passes.
Reviewer-found GC, pre-mutation direction, cold local-ID and query-before-finish
edges now have direct regression/control coverage. Cache cleanup runs on passing,
failed and interrupted standalone attempts after owned processes are reaped.

Portable evidence and exact final elapsed figures are archived separately under
main `.planning/validation/build764/results-lane`; manifests bind all retained
members and source inputs. No public ZIP/release or device write is part of this lane.
