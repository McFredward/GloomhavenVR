# Quest ping animation regression: native visibility ownership (2026-10-19)

Base `17cab93ae` (B712); isolated worker `quest713-ping`. The user reports that
B712 lost the ordinary rising ping animation while the tile artifact remained.
This change restores original native rendering; it does not claim an Adreno
pixel-artifact repair.

## Reproduced cause

Both exact original `Ping Highlight` ParticleSystems serialize `cullingMode: 2`,
which is `ParticleSystemCullingMode.Pause`. Unity2021.3 defines this as stopping
simulation while offscreen ([Unity API](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/ParticleSystemCullingMode.Pause.html)).
B712 replaced their visible draw with MeshRenderer BakeMesh snapshots and set
`ParticleSystemRenderer.forceRenderingOff=true` during head culling. A visible
replacement mesh does not make the original ParticleSystem visible to Unity.

A real Unity2021.3.5f1 Vulkan autoplay fixture runs the exact two-system original
prefab and the exact B712 production helper side by side with deterministic
seeds, one real automatically rendered camera and unmodified Pause culling. The
native baseline advances from0.17s to1.17s while B712 remains at0.1049s/0.0234s;
both B712 original renderers report `isVisible=false`, and the later second
emission never arrives. This reproduces the missing animated rise without
inventing a shader, particle simulation, emitter or frame clock.

The B712 static native fixture explicitly called `Simulate(.5f)` before taking
manual-camera snapshots. That proved already-paused pixel parity and missed the
visibility-owned simulation clock completely. The new automated autoplay control
rejects the exact B712 source at `AUTO_CLOCK_RESULT same=False`.

## Production change

Remove both BakeMesh/proxy meshes, upload buffers, per-camera callbacks and
forceRenderingOff writes. Retain the previously qualified narrow native
POSITION/COLOR/UV stream request and restore original streams at pooling/reset,
only when the current streams still match the helper's own request. Bind
admission to the exact assigned native ping owner and two expected billboard
material families; later material/shader/render-mode changes retain native
ownership. Desktop remains inert. Original ParticleSystem, renderer, simulation,
visibility-based culling, lifetime, emissions, curves, material, textures,
blend/depth and shader remain original. No other particles or hex overlays are
changed. No native culling override or manual simulation is added to production.

The existing B712 failure-log grep token is retained through the rollback.
There is no replacement geometry, per-frame copy, shader remapping, asset-bank
change or new original conversion requirement.

## Focused evidence and limits

Evidence is retained under
`/home/claw/quest3-local/build/evidence/quest713-loading-review-20261019/ping/`.
The original canonical source project is borrowed read-only, hashed before and
after each fixture run. The small fixture copies only the exact ping prefab,
material/texture closure and already-recovered original native shader programs.
Its unrelated game MonoBehaviours are removed; native particle fields and curves
are retained. The1.2s observation stays inside the native2s ping lifetime.

Validation covers30 native-render/ownership assertions,229 visible native pixels
with maximum RGB difference0, and six actual autoplay observations through the
original delayed emission and lifetime. Repaired and original clocks, particle
counts and source renderer visibility match. Controls reject missing stream
request, broken pooling restore, wrong shader admission, changed live shader
contract, and the exact B712 baked-visibility implementation.

All owned Unity processes and their descendants are terminated/reaped before
`cleanup_completed_run` removes their generated Library caches. Logs, source
snapshots, pixels, original-input hashes and results remain. No full game
conversion, new movie/depth processing or APK build ran here.

Native Vulkan evidence establishes the simulation regression and preserves
native host pixels. It does not establish the Quest's tile picture. That original
Adreno artifact is explicitly still open; do not describe this rollback as a
validated fix for its colored tile fragments. The next headset capture should
confirm the restored native rise independently of that artifact.
