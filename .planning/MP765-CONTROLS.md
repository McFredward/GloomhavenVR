# Build765 shared Results control requests

Worker `worker/mp765-controls`, based on current dev `83935831252223de76a5efa9e163d54420e8f0ba`.
Runtime checkpoint: `998c97ecb`. The integrator owns final strict/source/integration
checks and the unchanged Build764 complete-checkpoint evidence comparison. The
maintainer requested minimal affected checks for this follow-up; this lane runs
only the bounded request/dispatch/mailbox scope, with no Unity/full inventory or
whole-wire invocation. This is not a new complete-gate pass.

## Cause and owned change

The paired Build764 report reaches genuine final Results, but the visitor's
observer picture cannot run original gameplay controllers. The existing
EnemyInfoContinue/AssignmentChoice reliable host-request mechanism had no Results
operations. Missing native readiness visibility and observer proxies are repaired
in the separate Results lane; missing observer scrolling is a separate lane.
This lane does not independently assign those hardware causes from new screenshots.
Hardware intake and paired 764/128b6b6ca banner verification belong to the integrator.

`ScenarioResultsControlRequests` uses existing `SideActionRequest.Send` with
`canBeUnreliable:false`, `sendToHostOnly:true`, sentinel action 60123,
target `int.MaxValue`, marker `true`, and reserved operation tags 3/4/5 in `DataInt`.
All 32 opening bits occupy `DataInt2` via unchecked signed conversion; no key bits
are borrowed for the operation. No GVR/TLV/codec/message layout is added or edited.
The only transport dispatch addition is a branch in the existing receive prefix,
after existing request handlers and before cosmetic payload decoding.

The WorldUI mailbox contract is `SubmitContinuation(byte operation,uint opening)`;
operations are native constants ContinueReady 1, ReturnToMap 2 and RetryScenario 3.
The observer passes its displayed state's exact key. NetModule binds the mailbox
only after config, Harmony and successful receive-hook installation. Shutdown
clears only the exact delegate instance it installed, including a later module
binding the same static method during hot reload.

Both sender and receiver validate the current native online topology through
`NativeCheatQueue.ReadRoster(requireStable:false)`, without a character/participant
requirement. The sender must be a native client of a current compatible host.
The receiver must be the actual native host, never a client, and its live native
local player ID must match HostPlayerID. Both decline flat-net/desynchronized
sessions, key 0, stale openings, a different common author or a nonvisible native
Results scope. The receiver additionally requires a remote positive native sender
ID and `HasCurrentVersion(sender)`, bound to that exact live native entity and
VersionGuard session. The sender requires the same receipt for the host. This
read-only receipt helper is independent of the Cheats switch.

Only a full owned marker is handled; matching requests are consumed even when
refused. Unknown operations and other markers remain with their existing owners.
Current native Results permission is then delegated to
`ScenarioResultsNative.TryInvokeContinuation`, owned and separately tested by the
Results lane. Its original pointer callback retains visible, active, interactable,
input and native permission. Native toggle/active state rejects a second successful
click synchronously. There is no request latch that consumes an unsuccessful
press. The original host UI remains directly operable; an unmodded host receives
no forwarded request. No vote, game phase, completion, queue action or actor state
is synthesized.

## Focused qualification and declared ports

Command: `python3 scripts/check-scenario-results-control-runtime.py`.
Final evidence: main checkout
`.planning/validation/build765/controls-lane/run-516on3ed`.

The portable CLR fixture executes unchanged production
ScenarioResultsControlRequests, SideActionRequest and NativeCheatQueue, plus exact
whole-source projections of `FfsNetTransport.ReceivePrefix` and
`NetModule.Init/Shutdown`. Projection hashes, original source files, final source
pins, executable DLLs, logs and case results are retained. Current .NET SDK 8.0.423
details are preserved in the archive. No new Photon/Bolt compile reference exists.

Explicit ports are native entity/roster storage, native Results pointer
permission/latch, socket/install, unrelated existing request handlers, GameObjects
and logger. The native Results lane separately qualifies actual pointer callbacks
and readiness. This portable scope cannot establish head pixels, Unity input,
Photon delivery, serializer/golden compatibility or HMD acceptance.

Final owned invocation: **99 assertions and 14 compiling causal controls**, all
passing in **31.76s**. It covers all three operations, a visitor without characters,
reliable host-only sending, complete uint opening preservation, unchanged payload
and existing request routing, stale/zero openings, hidden native scopes, wrong
author/host/local ID, departure/reused IDs, missing receipts, new sessions,
flat-net/desync, retry after permission/send/callback failure, successful duplicate
refusal, and installed/disabled/failed/replaced/successor mailbox lifetimes.

Controls remove dispatch, exact opening, common author, native visibility, current
receipt, actual host, flat-net, native local host and complete marker checks;
truncate the key; permit an unmodded host; bind before receive installation;
unconditionally clear a replaced mailbox; or replace entity identity with mere ID
existence. Every control compiles and fails its intended assertion.

Three earlier failed attempts remain failed in the archive:

* `run-lctrp2c5` 1.88s: fixture project overwrote inherited NoWarn and surfaced old
  SideActionRequest XML-documentation CS1573 warnings as errors. Preserve inherited
  NoWarn before adding fixture-only unused-field/unreachable-code suppressions.
* `run-1x5llk2z` 12.90s: removing receipt permission correctly failed an earlier
  departed-peer assertion than the expected marker. Reorder refusal cases to
  isolate the missing-receipt cause; production behavior is unchanged.
* `run-msmnfcl7` 19.31s: removing native local-host equality survived because the
  two-player fixture's independent self-sender check still refused. The corrected
  case uses a third native visitor so it exercises the intended host-ID boundary.

Total owned test execution across all four attempts: **65.85s**. Every invocation
reaped its processes and called `cleanup_completed_run`, including all failures;
62 generated bin/obj directories were removed. Logs, source, executable/control
evidence and cleanup receipts remain. The main archive contains 1060 hash-verified
files before this planning note. The worker does not clean another lane's caches.
Root owns source-gate/strict-build and final integrated proof, then dev push and
worker retirement. No local ZIP, public release, device write or new hardware
acceptance is part of this lane.
