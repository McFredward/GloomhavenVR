# Physical menu laser regression fixture

Run the default suite with a private ADB endpoint:

```sh
ADB_SERVER_SOCKET=localfilesystem:/tmp/ghvr-menu-laser.sock python3 scripts/menu-depth-laser/run.py
```

The runner compiles the current production depth sources, complete UguiPointer,
and unchanged RayUguiDriver Tick, pressed-drag, geometry and native-coordinate
kernels. Tracked controller input and unrelated game services are explicit
fixture inputs. Geometry, native GraphicRaycaster filtering, original pointer
callbacks, source camera draws, sprite mip receipts and shader assets execute
in Unity 2021.3.5f1 with OpenGLCore. This does not boot Gloomhaven or OpenXR.

The positive case exercises raised cards, alpha holes through cropped atlas
provenance, native masks and read-only input permission, real-Z confirmations,
story relief from the mesh actually submitted by Unity's native Graphic pipeline,
rotated/scaled/moving hosts, front/side/rear character meshes and presentation yaw.
Real Unity LODGroups and an unrelated nested native camera prove source-specific
renderer membership. Same-frame identity reuse and delayed component destruction
exercise observer retirement. The original Brute asset is loaded read-only through
its exact native prefab renderer; its 9,447 vertices and 13,421 triangles are checked
against the separately audited native asset. Its current posed skin is compared to
an independently baked Unity MeshCollider, with warm hit/miss timings, allocation,
triangle and shared skin-bake counts. A populated native canvas supplies 1,502 real
registered Graphics for the original raised-member query cost measurement.

The More Cards case reads the exact native inventory row prefab from level9 and
its sharedassets2 dependency. Its original transparent row Image/ExtendedButton,
non-raycasting CardHolder, nested AbilityCardUI button and MiniAbilityCard run with
actual GH.Runtime pointer/preview callbacks. Independent flat-raycaster and
physical-width checks precede the first raised centre hover. Fresh centre/edge/
label entry, continuous movement, row transitions, locked/unlocked clicks, native
navigation permission, masks, CanvasGroups, disabled/hidden controls and adopted
visual/owner Canvas boundaries are exercised. Valid native nested-child hits keep
their own priority. Detached full previews and ordinary mini input scopes stay
independent. Campaign/artwork bootstrap, decoration, desktop fitting, card admission
and completed head-capture boundaries are explicit fixture inputs; native highlight
uses its original immediate animation path.

Thirteen compiled causal variants independently remove physical depth, input mapping,
alpha, source camera identity, capture lease, pooled-witness retirement, receipt
expiry, input-root isolation, shared skin baking, native raised-member geometry,
More Cards row ownership, its cross-Canvas original raycaster lookup and the
original host-before-nested tie ordering. The latter compares raised selection
with the untouched flat query for independently established equal-order hits.
Each must fail its named assertion. An additional fault variant injects a throw
at the actual source Camera.Render call after one successful draw and asserts
receipt invalidation, observer lease cleanup and original camera restoration.
Compilation failure is never accepted as a successful causal control.

Runs retain copied sources, generated kernels, source/native/toolchain hashes,
resolved build dependencies, executed assembly hashes, raw logs, result manifest,
final camera PNGs and cost records under `.planning/debug/menu-depth-laser/`.
Project caches are removed through `cleanup_completed_run` after reaping owned
editor/compiler processes on passing, failing and interrupted runs.

The character hit uses the current input-frame mesh pose and the latest completed
source-camera renderer receipt, which expires after one subsequent frame. It does
not predict later Animator updates. Native character material alpha/discard shaders
are not evaluated on the CPU. Card/story alpha uses existing mip-bake pixels or
readable original textures; an unreadable uncached source retains native opaque
rectangle semantics without starting another readback. Timing under llvmpipe on
this build machine is bounded regression evidence, not headset frame-pacing or
Windows D3D11 visual acceptance.
