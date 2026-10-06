# Optional visible scenario idle poses

The Build 627 Gaming-PC capture explains why the first visible-idle implementation
saved no work: all sixteen tracked animators were already authored as
`CullUpdateTransforms`, twelve actor checks per frame found captured Cloth, and
five found native LOD groups. All visible sample/source counters stayed zero.
Those are actor-check totals, not proof of CPU time or independent unique actors.
The fully loaded three-room experience is the priority; preparation can happen
while loading. No hardware FPS outcome exists for the changes below.

## Independent setting

`Optimize.VisibleIdleAnimationIntervalSeconds` remains adjustable in the common
PC/Frame binary. Zero keeps original presentation, PC fresh default is zero and
Frame fresh default is 0.2 seconds. Saved choices remain intact. It is independent
of `OffscreenIdleAnimation`. The interval is a minimum pose hold; a loaded crowd
may sample later because only two actors warm and two actors bake per frame.
Baking stops after a two-millisecond slice, but one atomic actor bake cannot be
preempted. Actors awaiting a slot retain their existing complete pose.

`Optimize.VisibleIdleDisabledClothApproximation` is a separate explicit compromise:
PC fresh default is false, Frame fresh default is true; Fastest chooses true and
other presets choose false. With the option off, an actor with captured Cloth
keeps its whole native body and animation path. With it on, ONLY disabled Cloth
may join the complete skeletal bake. This can replace an immediate or
garment-dependent frozen solver deformation with the undeformed skeletal shape.
It never resets solver particles, native coefficients or gameplay clocks. Active
solvers always remain native. Both live option-off and an interval of zero restore
original masks/tables/mode before the next native camera culls.

The native animation clock continues through Unity's own state machine. There is
no manual Animator stepping, component disabling, speed change, network-state
write or NPC resident clock change. The existing exact native publisher/state
component audit still excludes events, unknown writers, transitions, locomotion,
held actors on either peer and unsafe native dissolve. Native actions/holding,
option zero, host disable and teardown return original presentation immediately.
Both originally AlwaysAnimate and originally CullUpdateTransforms rigs can use
visible poses; restoration returns the exact original mode. Existing authored
CullUpdateTransforms is never counted as a newly applied offscreen mode saving.

## Complete body and current figure detail

A separate positively owned idle skin list includes disabled cloth surfaces only
when their explicit approximation is enabled.
The existing bar envelope continues to exclude cloth and retains its old geometry.
An enabled live cloth solver retains the entire native body and animation path.
An unknown garment which cannot join the positive body list rejects the complete
replacement rather than leaving that garment on a different pose. Skinning-quality
or global influence-policy changes invalidate an old sampled shape.

Fastest/Frame figure detail replaces earlier native LOD table entries with a
coarser original body and masks omitted fine surfaces. Only masks proven to belong
to that CURRENT owned table are excluded from the idle snapshot. Arbitrary foreign
masks remain unsupported. `ScenarioFigureDetailBudget.BeginLodMaskRead` coalesces
native table verification within ONE pure CopyIdleSkinSources invocation. Its
verdict is discarded before any frame, callback or camera. A foreign table between
two invocations is verified again; no cross-frame ownership cache exists.

## Native LOD and render ownership

The same native LODGroup selects the private baked body renderers through a
camera-only renderer-table lease. The group itself, bounds, reference point,
original transition heights and hidden ForceLOD state remain original. No inferred
screen-height selection, cloned LODGroup, forced LOD override or native hierarchy
change is used. Exact original tables and render flags return after each camera,
before native actions/content cloning, and on failure. A late foreign table is
preserved, rather than overwritten during owner restoration. Nested cameras
suspend and then reacquire only a still-valid outer lease.

Crossfade groups remain native: their per-renderer fade progression has no exact
private-proxy proof. This is an explicit unsupported path, not a silent fixed-LOD
compromise. Native command-buffer consumers keep original renderer identities.
Original meshes, materials, material slot order, enabled flags, colliders and native
objects are unchanged. Private meshes/renderers live below the mod host.

Camera listener order matters. Restoring originals from onPreRender is too late
when the native culling pass has already omitted their masked renderers. The
shared Harmony postfix on Unity 2021's real managed Camera.FireOnPreCull runs after
ALL onPreCull listeners and before native culling. It validates late SetLODs,
cloth activation and renderer edits there. The old late-table picture was black
(0 body pixels versus 113), even though a weak full-frame threshold passed. The
new early boundary restores actual original body pixels before culling. Missing
native action/clone/final-cull seams disable the optional owner and keep native
continuation. FigureVisualMirror.CloneVisual has its own synchronous restoration
prefix so held/observer visual copies cannot capture private LOD references.
The same final boundary also rechecks late local/remote/prop holds, newly added
native renderer consumers and the live interval once per camera. Unmasked actors
do not perform the additional material/table audit. Diagnostic
`Figure.VisibleIdleClothApproximation` counts actual completed masked bodies,
uses RegisterDebug, and does its extra census only when both attribution and
Debug logging are active.

## Evidence and limits

The exact source/fixture hashes and raw receipts are retained in the worker's
`.planning/debug/idle-steady-*` directories and archived by the integrator. The
final worker ledger is `.planning/debug/idle-steady-final-validation.json`:

| Focused check | Final result |
| --- | --- |
| Native actor/render runtime | 41 production/causal variants; 656 production assertions |
| Figure-detail runtime | 31 production/causal variants; 146 production assertions |
| Figure-detail source boundaries | 412 assertions |

The final 512x512 late-LOD control renders 2232 original body pixels against 2232
reference pixels, with zero differences. Worker native runtime projects compile
with warnings as errors; the integrator runs the complete Release/Debug assembly
and final repository gate with the actual shared configuration/counter helpers.

The native fixture uses the publisher's real 117-bone SpittingDrake geometry, imported
original avatar/clips and ten original mandatory script types; it executes actual
Unity 2021 rendering and actual Harmony methods. The native original GPU skin pose
must first be resolved by its real Camera.Render before comparing a freshly baked
SAME pose. A held interval sample is deliberately older than a subsequently
resolved original pose and cannot serve as a same-pose control.
The image-kernel calibration samples the current pose directly in the fixture;
real warmup, rotating crowd scheduling, native clock advancement and suppressed
bone writes are proved separately through the production owner.

The new 512x512 pixel oracle uses body pixels, a positive visible-body control and
a five-percent body-relative tolerance (minimum three pixels). Historical 536/537
assertion runs using a whole-frame tolerance are NOT exact native-LOD/late-fallback picture proof. The sharper production probe
passes the native ForceLOD levels, automatic distance selection/far
culling, current capped original topology, Bone1/Bone2/Bone4 influence policies,
foreign tables, native clone restoration and nested cameras. Late SetLODs
rendered 110 original body pixels versus 110 control pixels with zero differences in
the earlier 128x128 calibrated probe; final 512x512 results pin their own source hashes.
Subsequent final fixture additions cover policy invalidation, early live-cloth
fallback and scoped mask ownership; their complete current counts are recorded in
the final validation ledger, not inferred from an earlier receipt.

The native Cloth fixture first simulates thirty real frames with the actor clock
paused ONLY for image calibration, verifies actual particle displacement, then
disables the solver. Its 11,462 mesh vertices versus 9,244 welded solver particles
rule out direct Cloth.vertices assignment. In the immediate Disable-frame,
the original frozen surface differs from BakeMesh (712 changed pixels in the
diagnostic probe); after a native warm frame this Drake returns to skeletal shape.
This is evidence of a possible immediate/garment-dependent loss, not a claim of
permanent frozen Drake deformation. On/Off images, live-Off recovery and the real
Cloth approximation warm/clock/bone-skip lifecycle are separate oracles.
Assertions raised in native Camera callbacks are retained and rethrown by the
runner after Camera.Render, because Unity otherwise logs and swallows them.

Separate lifecycle tests prove actual native bone writes stop between samples
while the original clock advances, including native authored-culling and capped
LOD paths. Frame figures still require headset validation with their actual
materials, shadows, both eyes, original disabled cloth meshes, actions and local/
remote holding. Unlit same-pose calibration does not prove native Windows shader
appearance, GPU busy time, headset FPS or multiplayer headroom.

Native GetLODs allocates arrays. Scoped body reads avoid repeated verification
for fine skins sharing one group, but camera admission/restoration still requires
current table verification. This adds native calls and allocations; never remove
those safety reads or claim bone savings prove a net win. The next settled Frame
A/B must compare frame distributions, visible sample/source counters, camera/bake
scope costs and GC, at one setting at a time before the combined profile.
