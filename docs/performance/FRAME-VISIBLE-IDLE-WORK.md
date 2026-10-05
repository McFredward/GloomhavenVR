# Optional visible scenario idle poses

The October 5 Build624 capture remains near 100 ms per application frame in the
late three-room window. Native callback probes do not explain the complete engine
work/wait residual; they do not establish actual GPU busy time. This option targets
audited visible idle transform work, without changing the native animation clock.
No hardware FPS saving is inferred from the source or fixture receipts.

## Independent setting and ownership

`Optimize.VisibleIdleAnimationIntervalSeconds` is available in the common PC/Frame
binary and VR Graphics settings. PC fresh default is 0 (original every-frame
presentation); Frame fresh default is 0.2 seconds. Saved choices remain intact.
The setting is independent of `OffscreenIdleAnimation`; either can be enabled
alone. The visible interval is a minimum hold duration, not a promise that a loaded
scene can sample every actor at precisely that frequency.

Only the existing positively audited native event-free idle loop is admitted:
exact publisher component types, known state callbacks, one original loop layer,
no events, transitions, actions, movement or local/remote hold. Unknown/eventful
rigs keep their original path. Town resident synchronization is outside this owner.
The native Animator still advances its original state/root-motion clock. No manual
Animator.Update stepping, speed changes, component disabling or gameplay writes
are used.

A private BakeMesh result under the mod host stands in for the original body skins
between samples. Its original material references, transform, renderer flags and
bounds remain compatible. Native source hierarchy, MeshFilter/sharedMesh, material
slots and renderer.enabled remain untouched. For one admitted head-camera render,
an owned forceRenderingOff lease masks the original skins while their private
MeshRenderers draw. PostRender and interrupted-render recovery restore those masks.
Native command-buffer Renderer consumers retain the originals. The other optional
environment owner's exact private buffer is distinguished from a native consumer.

One original camera render warms visibility before the following native Animator
evaluation; LateUpdate then bakes that genuinely updated pose. At most two actors
may be warming a native pose simultaneously. Remaining due actors retain their
existing snapshots while waiting, rather than all returning to expensive native
pose evaluation at once. A rotating bake lane admits at most two actors and stops
after a measured 2 ms per LateUpdate; a single atomic BakeMesh cannot be preempted.
Each actor is capped at 60,000 source vertices. Unsupported sources keep originals.
Frame defaults request the strongest optional idle compromise; action motion remains
native and immediate.

Native MF.AnimatorPlay and locomotion seams restore synchronously before dispatch.
Update and camera admission both check held/moving ownership, including late remote
grabs. The native content-cloning seam releases camera masks before Instantiate.
Foreign renderer/material/mesh/probe state, property blocks, lightmaps and sheared
transforms decline the substitute. A fault restores owned state and disables this
optional path with one bounded normal-level report. Debug performance counters expose
`Figure.VisibleIdleSampling`, `Figure.VisibleIdleBakes`, and `Figure.VisibleIdleBake`.

## Runtime evidence and remaining limits

The fixture imports publisher components, original Spitting Drake geometry,
117 bones and native idle/action clips into actual Unity2021.3.5f1. A separate
calibration distinguishes real native bone transform writes from state-clock
advancement: camera masking with CullUpdateTransforms stops the former while the
latter continues. Production camera callbacks retain visible body pixels and
match an original GPU-skinned silhouette at the same pose. The original shader
lighting is not established by the fixture's explicit Unlit color material.

The production/causal suite covers native component admission, unsafe callbacks,
active death dissolve, local/remote holding, native action faults, foreign culling
edits, mask restoration, absent proxy pixels, source mesh corruption and immediate
action restoration. Before the interruption, the bounded-request source passed
423 production assertions and sixteen causal controls (17 variants). That receipt
precedes final inactive-host restoration and completed-camera counter changes.
The final fixture compiles those additions, but current permissions block Unity
before assertions run. Historical success is not certification of the final
combined tree; see [the final receipt](FRAME-625-IMPLEMENTATION.md).

Original material lighting, near-view animation quality, simultaneous crowd cost,
both-eye headset output, Frame FPS and multiplayer headroom remain hardware tests.
Higher configured intervals deliberately make otherwise idle body motion less
smooth; 0 immediately restores normal per-frame presentation. No reduced-resolution
default or 2.5D scene is introduced.
