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
action restoration. The final 627 review reruns this source against actual Unity,
including inactive-host restoration and completed-camera counters. Historical
receipts below are superseded by the final focused receipts and the parent's
integration gate; neither asserts headset quality or FPS.

## Build627 admission review

An active original `LODGroup` selects among skins without changing each skin's
`Renderer.enabled`. A private renderer outside that group would draw all admitted
LOD skins together, ignore far culling and miss the native group's hidden
`ForceLOD` state. Original Drake groups have three authored levels. The review
therefore declines visible pose sampling for a captured active native LOD group,
rather than inferring another LOD selection. Disabling/re-enabling a captured group
is read live, including immediately before camera masking. The independent
offscreen idle option still uses native `CullUpdateTransforms`; enabling only the
unsupported visible option cannot silently enable that separate option.

Captured native Cloth also declines visible replacement, including disabled Cloth:
those skins are deliberately absent from the bar envelope, so replacing the other
pieces would be a partial body. Active Cloth immediately declines even the
offscreen shortcut; disabling it can restore only the independent offscreen path.
All checks use preparation-captured component identities; no steady subtree scan
or repeated bake retry is needed for these stable refusals. Unknown components,
events, constraints, native action states and held figures retain their original
paths.

`Figure.VisibleIdleLodRefused` and `Figure.VisibleIdlePhysicsRefused` count tracked
records carrying those unsupported sources while visible sampling is requested;
they are not unique actors, attempted bakes or measured CPU savings. LOD Debug
reports are deduplicated by actor identity across pose recapture, capped at 128 per
driver lifetime, and cleared on teardown. Normal player logging gains no recurring
stream. Late native sorting, motion-vector and dynamic-occlusion edits now revoke
the old proxy before that camera, preserving the original renderer state.

The positive native pose/pixel calibration explicitly disables the publisher's LOD
groups. Its original clips, bones and skin/material identities remain real; it
proves the supported no-LOD path and must not be presented as proof that native
LOD actors get this saving. Separate native tests keep the original LOD active,
exercise hidden ForceLOD, verify no sampling/retry starts, and re-enable it after a
successful pose to verify synchronous fallback. Cloth activation and every new
late renderer field have their own causal controls.

Current focused evidence: `native-actor-audit-runtime/run-5aqrou4u` passes 449
production assertions on the final source. Six new admission/late-write controls
pass at `run-kwea4eh3`; the 17 existing controls pass at `run-mo688vx2`, and two
new diagnostic/lifetime controls pass at `run-t6fncqf1`. The latter run's production
continuation exposed a fixture-only owner lookup: Unity's scheduled `Destroy`
left the previous diagnostic Driver on the host until end-of-frame, so a later
`GetComponent` read its retired dictionary. The fixture now retires that owner for
one actual Unity frame before resolving its replacement; the production-only
continuation passes without changing an admission or camera predicate. Compilation
failures are retained and never count as controls. The current local UI lane passes 1,651 production and 1,650 unsupported-version fallback assertions plus
11 controls at `shared-ui-window-runtime/run-mtq9v0_j`; the unchanged remote reader
passes 625 assertions, eight runtime controls and its actual scanner control at
`remote-mirror-read-runtime/run-yznrkmed`. Receipts and source hashes are private
under the worker's `.planning/debug`; the parent archives them with final evidence.

Original material lighting, near-view animation quality, simultaneous crowd cost,
both-eye headset output, Frame FPS and multiplayer headroom remain hardware tests.
Higher configured intervals deliberately make otherwise idle body motion less
smooth; 0 immediately restores normal per-frame presentation. No reduced-resolution
default or 2.5D scene is introduced.
