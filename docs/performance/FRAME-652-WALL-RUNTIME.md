# Build652 wall visibility runtime

The maintainer explicitly authorizes instant complete wall removal as an optional
quality compromise (2026-10-09). This is an exception to the ordinary animated
fade rule, not a change to Regular mode. All platforms use the same binary.

`PerfConfig.WallVisibilityMode` selects0 Regular,1 Hide all,2 Auto. The controls
and persisted defaults are owned by the separate options/config lane. The
threshold defaults to15 application FPS: the supplied651 Frame run's first room
was approximately19–22FPS, while its fully opened board was approximately8–9FPS.
These are observed log windows, not controlled identical-view benchmarks.

Auto accumulates `Time.unscaledDeltaTime` once per application frame, only in a
loaded Game scenario, after native/scenario interaction preparation and room
reveal finish. Focus loss and open VR Options reset observation. A half-second
post-loading grace excludes the first delta belonging to the old loading edge.
A continuous two-second window below the selected threshold latches Hide all for
this scenario. Each delta contributes at most250ms: a single long hitch cannot
fill the window, but sustained very slow loaded gameplay is still recognized.
Changing the mode resets the latch; Regular immediately restores the original
view-dependent path when its existing see-through toggle is enabled. No XR stats,
profiler queries, timer object, per-eye observer or diagnostic scan measure FPS.

## Exact membership and exclusions

Membership comes from the existing final wall table, not renderer names or a new
shader inventory. Every eligible segment supplies its wall renderers, foliage,
siblings, body, stacked shell, mounted props and unit dressing. Shared corner
pieces join unless either neighboring segment is protected. DoorRoot and gate-
column segments supply a separate exact protected set, so an attachment shared
with an ordinary wall also remains. The final write boundary reuses the existing
actor, floor, held-prop, water, arch and arch-mounted-effect guards. No Light,
GameObject activation, collider or gameplay state is changed by this lane.

Masks use `Renderer.forceRenderingOff`. Native `enabled`, materials and alpha are
not changed to implement hiding. Existing animated fades are restored once before
acquiring masks. The previously authored/foreign force-off flag is retained and
restored when ownership ends. Native decompiled GH.Runtime has no force-off writes;
mod render/scenery budgets still require explicit cooperation described below.

## Work removed while hidden

The hidden branch runs before the ordinary tick instrumentation. It skips normal
coverage/evaluation, ramp/appliers, FastReclaim, periodic sweep/classification,
wall audits, wall camera draw traces and wall sender table walks. Existing peer
fade sets are cleared on entry, incoming peer decisions are ignored, and outgoing
wall keys return an empty set immediately. The local performance policy is never
broadcast to other players.

The settled path reads room/wall counters. When nobody holds a prop, held-state
observation is two managed registry-count reads; occupied hands compare the tiny
existing exact visual-root set. This catches same-count swaps without inspecting
all wall renderer ancestors every frame.

True native room/content changes still need discovery. The root integration calls
`NotifyPerformanceContentChange()` and `NotifyPerformanceRendererReady(renderer)`
from the existing native lifecycle hooks. They coalesce a revision without doing
a scan. A material-ready notification also reasserts an already owned mask. The
existing sliced collector runs only after actual loading has finished, uses a
fresh snapshot and banks the revision at cycle beginning. Changes arriving during
the cycle force one follow-up cycle rather than being accidentally acknowledged
by an older snapshot. Failures abandon scratch state and back off for two seconds.

Several existing adoption predicates inspect `IsActuallyDrawing`, including
force-off. Only during each synchronous lifecycle collector stage our masks are
temporarily restored to their previous flags and reasserted in `finally` before
normal rendering. Masks remain present during the frames between sliced stages.
The completed collector reconciles exact leavers/new members and current held
exemptions. Temporary collector releases do not run foreign restore callbacks.
No recurring hidden-mode whole-scene fallback timer was added.

## Render-budget and scenery contract

`PerformanceWallsHidden` is a cheap policy flag. `IsPerformanceHidden(renderer)`
is an exact managed membership query. The root's material/terrain/environment
paths must restore a current substitute before acquiring a new wall mask, then
skip hidden members and preserve those masks during their own lease restitution.
The runtime calls existing `ScenarioEnvironmentBudget.BeforeNativeRendererWrite`
only at mask acquisition and actual final restoration, not for settled members.

`RetainPerformanceMaskOnForeignRelease(renderer)` lets an exact cooperating
scenery owner release its old hide claim without clearing a wall mask. The saved
prior force-off flag loses that released foreign claim. After final wall ownership
is removed, `ConfigurePerformanceMaskRestored(Action<Renderer>)` lets scenery
re-evaluate its current desired hide state for that one renderer. The callback
runs after original flag restoration and never during temporary collector stages.

Mode changes, scenario identity changes, driver disable and teardown all restore
owned masks. Additive scene arrivals invalidate discovery but do not end a
current scenario's Auto latch. The existing native collector retains door/gate
classification across all game/DLC themes; no test-scenario-specific catalog or
renderer admission was introduced.

## Worker checks and boundaries

The worker uses the options lane's two final config/default source files only as
uncommitted compile dependencies; they are not in the runtime commit. Strict
Debug and Release builds accept zero warnings/errors. Existing wall maintenance
and wall-read-facts checks retain their original assertions and negative controls.
A private test compiles the complete actual `PerformanceWallClock` source and
passes527 assertions; five planted frame uniqueness, loading/focus, hitch-cap,
window-duration and inverted-threshold defects fail at causal assertions.

Private receipts are under the worker's `.planning/debug/frame652-wall-runtime/`
and `.planning/debug/wall-maintenance-trace/`; the integrator archives them before
removing the worker worktree. The separate runtime/integration proof ledger must
state its actual native/Unity modeled boundaries. These focused worker checks do
not prove new headset pixels, FPS, multiplayer transport behavior or the complete
repository gate. Source removal of wall work supports the expected benefit;
hardware confirmation with the same loaded closed-options view remains necessary.
