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

## Findings and final validation

In progress. Concrete findings, fixes, coverage inventory, test counts, failed
attempts, final-input bindings and unverified headset outcomes are recorded here
before delivery. This initial plan is not a completed review or passing gate.
