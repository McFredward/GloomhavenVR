# Window materialise — a window breaks into real debris in the room

**Lane:** `agent-a4dbc57e3f126b7b1`, branched from `origin/dev` at `e82ee8f2` (ModBuild 293).
**Status:** redesigned, committed on the lane branch. The four call sites were already wired in 293
and are **untouched**. The two durations live in `Defaults/Defaults.WorldUI.cs`, which this lane may
not edit — they are delivered as an exact hunk in §9.

> User, 2026-08-26, on what shipped in ModBuild 292/293:
> *"Ich mag die Fenster ein- und ausblend-Animation nicht. Ich will eher, dass es wirkliche
> Partikeleffekte in der 3D-Umgebung auslöst, aktuell ist es eher ein 2D-Effekt. Wichtig: Die
> Einblendanimation muss deutlich kürzer sein, am besten unter einer Sekunde. Die Ausblendanimation
> darf etwas länger dauern, aber auch nicht mehr als 2 Sekunden."*

> And, when the goal was described to him as debris that "beim Kopfneigen mitwandert":
> *"ja aber bitte ohne 'beim Kopfneigen mitwandern'. Der Effekt soll nicht an den Kopfbewegungen
> gebunden sein"*

---

## 1. He was right, and he was right about the mechanism

"Eher ein 2D-Effekt" is not an impression to be argued with; it is an accurate description of what
292/293 did. The debris was drawn on **one quad, parented to the window's host rect, in the window's
own plane, with `ZWrite Off`, at `Queue Transparent+550`**. Nothing it drew could be nearer or
further than the window, could be hidden by a table leg, or could separate from the pane when the
player leaned. It was a decal. The previous round's own writeup half-admits it: it records that
between p≈0.25 and p≈0.55 the window reads "washed-out-and-speckled rather than torn".

Two further defects in that quad, found while replacing it, both of which made it *more* of a decal
than its author knew:

* **It took `panel.HostGo.layer`.** For a **supersampled** panel that is the panel's own **private
  capture layer** — the capture camera's culling mask is exactly `1 << thatLayer`
  (`PanelSupersample.2.Capture.cs:849`). So on every supersampled window the flakes were rendered
  *into the window's own RenderTexture* and composited flat onto it. The most literal possible form
  of "painted on the pane". Fixed: the debris carrier goes through `VRLayers.Apply`, onto the mod
  layer, which every capture camera excludes and the head camera can never cull
  (`MaskNarrowingFloor`).
* **Its geometry log called world units metres.** `WindowMaterialise.Quad.cs:186-189` multiplied the
  rect by `lossyScale` and printed the product as "METRES in the room". `lossyScale` is world units
  per canvas unit; apparent metres is that divided by the rig's world scale. The line was therefore
  **9.57× too large in a scenario and 198× too large on the map-room table** — i.e. wrong in exactly
  the place a scaling log exists to be right.

---

## 2. What replaced it

| File | What it is |
|---|---|
| `unity/.../Bundle/Table/WindowMaterialise.shader` | **rewritten.** The shard trajectory, in the vertex stage. Same asset path and same shader name, so `BundleShaders.cs` and its wire test are untouched |
| `src/GloomhavenVR/WorldUI/WindowMaterialiseField.cs` | the erosion field, the **two fronts**, and every debris constant. The one file the mirrors read |
| `src/GloomhavenVR/WorldUI/WindowMaterialiseDebris.cs` | **new.** Shard seeding from the window's own elements, the two-renderer ladder split, the scale chain, the mesh gate |
| `src/GloomhavenVR/WorldUI/WindowMaterialise.Quad.cs` | **deleted** |
| `src/GloomhavenVR/WorldUI/WindowMaterialise.cs` | the facade. Unchanged surface: `PlayIn` / `PlayOut` / `Cancel` / `CancelAll` / `IsVanishing` / `IsAnimating` |
| `src/GloomhavenVR/WorldUI/WindowMaterialiseRunner.cs` | one playing effect; every interruption path; the measured cost line |
| `unity/asset-preview/windowmaterialise_field.py` | the field and the trajectory a third time, in numpy float32 |
| `unity/asset-preview/windowmaterialise_preview.py` | stage 1: the audits, the window textures, the shard simulation |
| `unity/asset-preview/windowmaterialise_room.py` | stage 2: Blender 4.2 / `BLENDER_EEVEE_NEXT`, a room with occluders, the shards as real geometry |
| `unity/asset-preview/windowmaterialise_compose.py` | stage 3: labelled strips, mp4s, the stereo and parallax pairs |

**No file outside this lane's ownership was touched.** `Defaults/`, `ConfigCatalog.cs`,
`ConfigSteps.cs`, `ConfigStepVectors.cs`, `ActorBars.cs` and `Board/FigureGrab/**` are untouched, and
so are the four existing call sites in `ModalFallback.4.Tick.cs`, `ModalFallback.9.Spawn.cs` and
`CanvasConversion.4.Lifecycle.cs`. `BundleShaders.cs` needed no change because the shader kept its
name and its path.

### How it works, in one paragraph

The window's own uGUI elements are still removed at **element granularity** — every `CanvasRenderer`
under the host gets its `SetAlpha` driven by `WindowMaterialiseField.PresenceOf`, sampled at its four
corners and its centre, so the window disintegrates in a wave along the wind and a big plate
dissolves across the whole sweep while a small label snaps. That half is sound, the user has never
complained about it, and it is kept. What replaced the flat plume is a mesh of **tetrahedral
shards**: a few hundred closed four-faced solids, each **torn out of a real, currently-visible
element of the window** in proportion to that element's area, each carrying the erosion threshold of
the exact point it came from — so a shard leaves in the frame its own patch of window goes dark.
Each has its own size, its own tumble axis, and its own **out-of-plane launch velocity**, which is
the entire redesign in one term. They are flown in the vertex stage as a closed function of one
uniform, they write depth, and they are drawn by **two renderers seated at +1 and −1 on the panel's
own distance ladder**, so the window is drawn between them.

---

## 3. The three questions the design had to answer

### 3.1 "It must be in the 3D environment" — how debris gets behind a window that writes no depth

`CanvasConversion.8.Order.cs:20-46` is a standing ruling with two deleted implementations behind it:
**NO DEPTH WRITING ANYWHERE BETWEEN PANELS — ORDER THEM INSTEAD.** Every converted host is a flat
plate that writes no depth, and panel-versus-anything composition is decided by a per-frame distance
ladder rewriting `Canvas.sortingOrder`. Unity resolves `sortingLayer → sortingOrder` **before**
`renderQueue` (measured on hardware; `RayInteractor.cs:363-372`).

Consequences, and they are the whole reason the renderer is split in two:

* A window can never occlude anything by depth. Ever.
* A single debris renderer would therefore be **entirely** in front of the window or **entirely**
  behind it, whatever its geometry said.
* And the repo contradicts itself about whether panels even depth-**test**:
  `OnTopUiGraphics.cs:18` says `unity_GUIZTestMode` resolves to LEqual on a world-space canvas;
  `ActorBars.cs:172` says it effectively resolves to Always. Both agree a per-material override beats
  the global, which means **the global is not something a decoration may rely on**.

So this design relies on it for nothing. Each shard is assigned to the **front** or the **behind**
half of the cloud **at build time, from its own launch velocity**, and the two halves go on two
renderers registered with `CanvasConversion.RegisterOrderFollower(panel, renderer, ±1)`. The window
is drawn between them. Because the partition is a per-shard constant computed on the CPU it is
identical in both eyes and never flips with head motion — a view-dependent split would have been both
a stereo hazard and precisely the head-binding the user refused. The ladder steps by 16
(`PanelOrderStep`), so ±1 stays inside this window's own slot.

Against the **room**, depth does all the work and needs no ladder: the mod's head camera renders
**forward** with strict queue order (`Plugin.ForwardRendering`, and the essay at
`VRRigDriver.HeadCamera.cs:339-357`), scene opaque geometry is in the depth buffer at queue 2000, and
the shards are `Queue Geometry+250`, `ZWrite On`, `ZTest LEqual`. A shard behind a wall is rejected
per pixel; a shard in front of one paints over it and writes depth. The shards also occlude **each
other** correctly, which is why they are opaque rather than blended — an alpha-blended cloud would
have needed per-shard depth sorting on the CPU every frame, which is the cost this design exists to
avoid.

### 3.2 "It must not be bound to head movement" — why solids and not billboards

Unity's default `ParticleSystemRenderMode.Billboard` orients every quad toward the rendering camera.
That is head-binding by definition: the debris would rotate as he turned his head, which is the thing
he refused. Under MultiPass it is worse — the orientation is recomputed **per eye**, which is this
project's known route to stereo rivalry, and the same reason the water surface was rebuilt with "NO
VIEW DIRECTION ANYWHERE IN IT".

So the shards are **solids with their own orientation and their own tumble**, and their appearance
from any direction is a consequence of where they are, not of where he is looking. This is option 1
of the integrator's own preference list ("real mesh particles — actual 3D shards with their own
orientation"); see §3.3 for why it is that shape without the `ParticleSystem` component.

**The effect reads no head pose and no camera transform anywhere.** That is checked mechanically,
not asserted:

* GPU: `render/windowmaterialise/camera_clock_audit.txt`, **28 identifiers, all absent, PASS.**
  Thirteen of them are the new head/camera group (`_WorldSpaceCameraPos`, `unity_CameraToWorld`,
  `unity_WorldToCamera`, `unity_CameraInvProjection`, `UNITY_MATRIX_V`, `UNITY_MATRIX_I_V`,
  `UNITY_MATRIX_VP`, `WorldSpaceViewDir`, `UnityWorldSpaceViewDir`, `ObjSpaceViewDir`,
  `_ProjectionParams`, `unity_StereoEyeIndex`, `unity_StereoMatrix`), ten are screen space, five are
  the clock.
* CPU: `grep -nE 'Camera|camera|eye|ViewDir|LookAt|transform\.forward|Screen\.'` over all four
  `WindowMaterialise*.cs` returns **three hits, all inside English string literals** in log text and
  config prose. The one live geometric input the C# reads that is not the panel's own transform is
  `PanelLayout.WorldScale` — the rig root's `lossyScale.x`, a **scalar**, not a pose.

**Two audit findings.** The previous round's audit reported "PASS — all 18 absent". One of those 18
was `SV_Position`, and it could never have fired: the grep is case-sensitive and every vertex shader
in existence declares `SV_POSITION`, so that entry tested nothing and padded the count. It is
replaced by `VPOS`, which is the semantic that actually is a hazard. Separately, the audit now states
what it deliberately does **not** look for — `UnityObjectToClipPos` (the mandatory clip transform)
and `UnityObjectToWorldNormal` (which reads `unity_WorldToObject`, an object matrix) — because an
audit that cannot say why something is allowed is one nobody trusts twice.

### 3.3 Scaling, and why there is no `ParticleSystem`

The brief allowed one if pooled and named `ParticleSystemRenderMode.Mesh` as the preferred shape.
This is that shape without the component, and the reasons are specific rather than general dislike:

1. **Scaling.** `ParticleSystemScalingMode.Local` ignores hierarchy scale by design and is the
   default; 37 of 43 of the game's own systems take it, the rig runs at ~9.57 world units per metre,
   and the map-room table runs at **198**. Every size here is authored in **apparent metres** and
   converted once from the panel's own measured `lossyScale` and the live rig scale — there is no
   mode to get wrong. The whole chain is logged, **change-gated on the chain itself** so the map-room
   table gets its own line rather than being hidden behind a once-per-process flag.
2. **Emission.** The point of the redesign is that a shard leaves from where the window actually
   broke up. That means seeding at a point inside a specific currently-visible `CanvasRenderer`'s
   rect and giving it that point's erosion threshold. A `ParticleSystem` can do the first only via
   one `Emit(EmitParams)` per particle and has nowhere to carry the second.
3. **Cost.** A `ParticleSystem` simulates every frame, per window, on the CPU. This costs **two
   `SetFloat`s and one `SetPropertyBlock`** per frame regardless of shard count (§5).
4. **Interruption.** A pooled system must be `Clear()`ed and reset on all fifteen rows of §4. A mesh
   on a mod-owned child dies with its carrier; "the host was destroyed under us" is `OnDestroy` and
   needs no reset at all.

**Pooling is still done** where it matters: `Mesh` is a native object, and the pool holds 8. The
material is shared across every effect; per-effect values ride a `MaterialPropertyBlock`. The vertex
buffers are static pooled `List<>`s.

---

## 4. The interruption matrix

The invariant: **no path ends with a window that is alive, listed, clickable and invisible.**

| Entry / event | What happens | Where |
|---|---|---|
| Window revealed | `PlayIn` writes frame 0 **synchronously** inside the reveal's own LateUpdate, so the first rendered frame is already frame 0 and never a full-alpha pop | `Runner.Begin` last line |
| Window dismissed | `PlayOut` disables `HostRaycaster` **first**, before any decision about whether the effect can run | `WindowMaterialise.DetachInput` |
| Effect dial OFF | `PlayIn` returns having written nothing; `PlayOut` runs the callback inline | `Enabled` guards |
| Duration below 0.05 s | treated as OFF, not as a fast animation | `Clamp()` returns 0 |
| **Intensity 0** | no mesh, no renderers, no `GameObject` beyond the runner's own carrier; the element dissolve runs alone as a clean directional wipe | `TryBuildDebris` → false |
| Shader unresolved (bundle not loaded yet) | no debris; the element dissolve still runs. Only *successes* are cached, so the next window retries | `TryBuildDebris` → false |
| Host rect degenerate | `Begin` returns false → `PlayOut` runs the callback inline | `Begin` |
| **No visible element to tear a shard from** | logged at Info, no debris, element dissolve runs | `BuildEmissionTable` → false |
| **Scale chain degenerate** (`lossyScale` ~0) | logged at Warn, no debris, element dissolve runs | `TryBuildDebris` |
| Element walk throws | runner nulls its own callback, finishes, returns false; `PlayOut` runs the callback | `Begin` catch |
| **Debris build throws** | logged at Error, `_debris` nulled, **the effect continues** with the element dissolve — a decoration failing may not cost the window its animation, let alone its release | `Begin` inner catch |
| Ramp completes | restore → report → callback | `Finish("completed")` |
| **Closed while materialising** | the close path calls `PlayOut`, which calls `Cancel` first → the appear's `Finish(restore)` puts every alpha back and returns both meshes → the vanish starts from the restored state | `PlayOut` → `Cancel` |
| **Re-opened while dematerialising** | the convert loop's `IsVanishing` guard refuses to re-float it until the vanish ends; the release then runs and the next tick converts it fresh | `IsVanishing` |
| Host **deactivated** under us | `OnDisable` → `Finish(restore)`. LateUpdate would never run again, so this is the only chance | `Runner.OnDisable` |
| Host **destroyed** under us | `OnDestroy` → `Finish(restore)`; the pending release still runs; both meshes go back to the pool | `Runner.OnDestroy` |
| Scene torn down / scenario exit / VR off | `CancelAll` at the top of `ReleaseAllWindows` and `ReleaseMapRoomFloats` | `WindowMaterialise.CancelAll` |
| Dial switched off **mid-flight** | next LateUpdate finishes and restores | `LateUpdate` guard |
| Panel dies mid-flight | next LateUpdate finishes and restores | `LateUpdate` guard |
| `LateUpdate` throws | logged once, `Finish(restore)`; if the recovery throws too the component disables itself | `LateUpdate` catch |
| `_seconds` / `unscaledDeltaTime` pathological | **watchdog** at `HardCeilingSeconds + 0.5 s` | `LateUpdate` |
| `Finish` called twice | idempotent (`_finished`) | `Finish` |
| Callback would run twice | the field is nulled before invocation and the `Begin`-failure path nulls it before finishing | `Finish`, `Begin` catch |
| **Ladder entries outlive the effect** | the two `OrderFollower` registrations self-prune the frame after their renderers are destroyed | `CanvasConversion.8.Order.cs:630` |

Three properties make the restore trustworthy rather than approximately trustworthy:

* **The element field is exact at both ends.** `Presence` is *exactly* 1 at progress 0 and *exactly*
  0 at progress 1 for every UV. Verified mechanically over 4000 random elements — the preview prints
  `max error 0.000e+00` at all four corners of the two directions.
* **The debris is exact at three of those four corners, and deliberately not at the fourth.**
  A vanish leaves *exactly* zero shard size at k=1 (the tail fade's `smoothstep` returns exactly 1 at
  its upper edge) and an appear leaves *exactly* zero at k=1 (`SizeEnvelope(0)` is exactly 0). An
  **appear at k=0 must have a full cloud in the air**: an appear that begins with an empty screen is
  a window that is live and clickable while showing the player nothing, which is the defect the
  previous round's first preview strip caught. The audit asserts a *lower* bound there. This is worth
  spelling out because the first run of that audit called the effect broken for having it.
* **Alphas are written as `original × presence`, not as `presence`.** An element the game had put at
  0.3 never jumps to 1.0, and at progress 0 the write is literally the number that was already there.

**Interactivity is unchanged and is still reached by construction, not by a catch.** `PlayIn` is
called *after* the reveal, so the window is visible, raycastable and a laser target before this
feature hears about it; `PlayOut` disables the raycaster in its first statement, before any decision
about whether anything can be drawn; `PlayOut` remains a **total function** whose callback runs
exactly once on every path and *synchronously* whenever the effect cannot run. With the dial off,
windows show and hide exactly as they do today and not a single alpha is written.

---

## 5. Measured cost

Measured in a real Unity 2021.3.5f1 Linux player (Mono2x, development build) under `xvfb-run`, with
the shipped arithmetic lifted verbatim, real `CanvasRenderer`s under a real world-space `Canvas`, 300
frames after a 20-frame warm-up, each bench alone in its own player invocation. Full output and
method: `render/windowmaterialise/measured_cpu_cost.txt`. **This is a desktop box, not a Quest 3 and
not his PCVR machine — the ratios are the algorithm's property, the absolute numbers are an order of
magnitude, and the shipped `Report` line is what settles it on his hardware.**

### Per frame — the element half is the whole of it

| CanvasRenderers | mean ms | worst ms | % of 11.11 ms |
|---|---|---|---|
| 64 | 0.023 | 0.032 | 0.2 % |
| 200 | 0.114–0.124 | 0.152–0.173 | 1.1 % |
| 400 | 0.199–0.224 | 0.306–0.388 | 2.0 % |
| 764 | 0.366–0.374 | 0.576–0.581 | 3.4 % |
| 1200 | 0.555–0.622 | 1.066–1.160 | 5.3 % |

Linear over two independent runs at **0.45–0.51 µs per element per frame** (R² 0.99), fixed term
indistinguishable from zero. **This is roughly twice the 0.265 µs the previous round recorded**, and
the honest reading is that the two numbers are not comparable: different box, and this loop now also
evaluates `Progresses` per frame. The shape — linear, no fixed cost, no per-frame allocation — is the
claim, and it holds.

### Per frame — the debris half does not scale with the debris

| shards | verts | mean ms | worst ms |
|---|---|---|---|
| 90 | 1080 | 0.0005 | 0.028 (first timed frame) |
| 420 | 5040 | 0.0004 | 0.0007 |

**4.7× the shards changes it by nothing** — the 420 row is fractionally *cheaper*, which is the
signature of a quantity that is not a function of the variable. Two `SetFloat`s and two
`SetPropertyBlock`s: **0.4 µs, 0.004 % of the budget**, however many shards are in the air. This is
the design's central cost claim and it is now measured rather than argued. (One correction to the
`Runner` docstring, found by the harness: it is **two** `SetPropertyBlock`s, not one — the halves are
two renderers.)

### Per window opening — the one-off, stated separately because it behaves differently

> **BENCH B is being re-measured after the vertex-buffer split; the table below is the pre-split
> measurement and the numbers will drop. See `measured_cpu_cost.txt` for the shipped figures.**

| shards | mean ms | worst ms | µs/shard | allocation per repeat |
|---|---|---|---|---|
| 90 | 0.93 | 0.94 | 10.3 | **0 B** |
| 290 | 2.28 | 2.30 | 7.9 | **0 B** |
| 420 | 3.17 | 3.46 | 7.6 | **0 B** |

The stage breakdown is why the split happened: the **emission table is flat at 0.32 ms** and is a
function of the 400 *elements* rather than the shards; the **seeding loop is exactly linear at
2.73 µs/shard**, constant to three digits over a 4.7× range; and the **mesh writes dominated above
~150 shards** — 1.70 ms at 420, about half of it the second mesh re-uploading a buffer identical to
the first's. That measurement is what produced commit *"each shard vertex is uploaded once, not
twice"*.

**Allocation: steady state is zero.** Across 180 builds not one showed a positive
`GC.GetTotalMemory(false)` delta. The buffers are constructed at full capacity (12 × 420), so the
largest window this feature will ever build cannot grow them, and `Mesh` comes from a pool of 8. The
one exception was measured rather than assumed: the **cold first build of a session costs 8.07 ms and
12,288 B** — JIT plus two `Mesh` constructions, once, ever.

### The worst plausible simultaneous case

The map room's arc holds five windows and `PanelSupersample`'s effective cap is seven. Seven windows
at 400 elements each, all animating on one frame:

* **mean 1.39–1.57 ms → 13–14 % of budget**; worst frame 2.14–2.72 ms → 19–24 %.
* The debris half contributes 7 × 0.4 µs = **0.003 ms**. It does not enter.

The larger figure is the one-off side and it deserves naming rather than burying: a single window
*opening* costs `CollectElements` (400 × 4.35 µs = 1.75 ms — this is **pre-existing**, 292/293 paid
it too) **plus** the shard build, in the frame the window opens. That frame is already doing a full
canvas conversion. Seven simultaneous *opens* would drop a frame, but the arc does not produce that —
windows open one at a time. If it ever did, the stage breakdown names the target directly.

**What is NOT measured, and said so rather than implied:** the canvas colour re-batch that `SetAlpha`
provokes inside Unity (outside both this stopwatch and the shipped `Report` line), the GPU side,
MultiPass, IL2CPP, and the real `Image`/`TMP_SubMeshUI` element mix.

---

## 6. Stereo

Two eye images cannot prove this and nobody should be asked to accept them as proof — a screen-space
dissolve passes that test, and so does a billboard. What settles it is that no per-eye input reaches
the effect, and §3.2 records the mechanical check of that in both the shader and the C#.

**What the redesign changed for the better, and it should be stated rather than assumed:** genuine
world-space geometry is per-eye correct *by construction*. Both eyes rasterise the same triangles at
the same world positions, because every vertex position is a function of that vertex's own attributes
and of per-draw uniforms and of nothing else. The effect it replaces was also per-eye correct, but
only because it never left the window's plane — which is exactly what the user objected to. This
design gets the same property for a better reason.

**The one per-eye risk that geometry does not remove is spatial**, and it is answered in C# rather
than in the shader: sub-pixel geometry aliases differently in each eye whatever the shader does, and
this project has read exactly that as stereo rivalry before. So the shard size floor is **4 mm**
apparent — about **0.25°** at 0.9 m, roughly ten headset pixels — with a ceiling of 22 mm (1.40°).
The floor and the resulting angular sizes are printed in the shipped geometry log line, so a hardware
run confirms them on his machine rather than on mine.

### The shader has now actually been compiled — and the binary is the better proof

"The shader has never been compiled" was item 1 of the previous round's own unverified list. It has
been, in a throwaway project at `/tmp/wm-shadercheck`, and this is worth more than the grep:

* **Clean.** `ShaderUtil.ShaderHasError` false, `GetShaderMessageCount` 0, after import, after
  `ShaderUtil.CompilePass`, and after a real `BuildPipeline.BuildAssetBundles(...,
  StandaloneWindows64)`. Zero warnings, zero errors, no fxc diagnostic anywhere in the editor log.
* **By 2021.3.5f1**, verified from the log header and `Application.unityVersion` — the version that
  matters, because a shader compiled by 2021.3.45 renders **pink** in this game.
* **A real Win64 variant**, not an import parse: `1/1 variants left after stripping`, `d3d11 (total
  internal programs: 2, unique: 2)`, `vs_4_0` and `ps_4_0` bytecode of 2890 and 726 bytes.
* **71 vertex math ops, 11 fragment math ops, 5 and 2 temp registers, 2 interpolators** past
  position. That is a very small shader.
* **And the constant buffers settle §3.2 at the binary level.** The compiled program references
  `UnityPerDraw` (`unity_ObjectToWorld`, `unity_WorldToObject` — object matrices) and `UnityPerFrame`
  (`unity_MatrixVP` — the mandatory clip transform) **and nothing else**. No camera position, no view
  matrix, no eye index appears in either stage. A grep proves an identifier is absent from the
  source; this proves nothing equivalent reached the binary.

Two incidental notes, neither a defect: `pow(age, 1.35)` compiles to log/mul/exp without fxc's usual
`X3571` negative-base warning, because `age` is `saturate`d upstream; and the shader reads
`TEXCOORD0.zw` only — the shard's birth UV in `.xy` is carried for the numpy mirror and is unused by
the GPU. Two floats per vertex of dead bandwidth, kept deliberately so the mirror and the vertex
layout stay the same shape.

**The frequency-scrubbing trap falls out of the same property.** There is no clock in the shader at
all. The one time-like input is `_Front`, written once per frame by C# and used only as a position
from which each shard's age is derived. `_SizeScale` is a pure amplitude. No dial multiplies a
frequency, because no frequency exists.

---

## 7. The renders

> **FILLED FROM THE BLENDER STAGE.**

---

## 8. WHAT I COULD NOT VERIFY WITHOUT HARDWARE

Read this before treating anything above as proven.

1. **That the effect does not move with the head.** This is the user's own new requirement and **no
   render on this machine can show it** — a head-bound effect is perfectly correct in every still.
   It is settled by §3.2's mechanical audit and by nothing else. If it turns out to move with him,
   the audit is wrong or something outside these four files is orienting the carrier, and the first
   thing to check is that nothing has reparented the debris under a head-following transform.
2. **The two-renderer ladder split, in the real draw order.** The behind/front bracketing rests on
   this repo's own measured claim that Unity sorts `sortingLayer → sortingOrder` before
   `renderQueue` (`RayInteractor.cs:363-372`, from a hardware test) and on `RegisterOrderFollower`
   keeping both halves in step. I have not seen it. **The symptom if it is wrong is unmistakable and
   worth naming in advance: every shard would be on the same side of the window.** Half the cloud
   vanishing behind an opaque window is the effect working, not failing.
3. **Whether panels depth-test at all.** Deliberately not relied on (§3.1), because the repo
   contradicts itself about `unity_GUIZTestMode`. If they *do* test LEqual, the debris additionally
   occludes the window per pixel where it is nearer, which is a bonus, not a requirement.
4. **Known and accepted ordering imperfections**, both inherited from how this whole subsystem works
   rather than introduced here: (a) a game transparent that writes no depth (fire glow, a health bar)
   at `sortingOrder` 0 draws before the shards, so a shard behind such a graphic still paints over
   it — the same trade the mod's own laser and reticle make at order 5000; (b) a *different* panel
   more than one ladder step further away draws before the shards, so a shard genuinely behind that
   other window would paint over it. Shards live within ~0.3 m of their own window and for under a
   second, so this needs two windows nearly co-planar to be visible at all.
5. **GPU cost.** The vertex stage runs ~3,500 vertices per animating window with a Rodrigues rotation
   and three sines each; the fragment stage is two dot products. My expectation is that this is
   negligible against 11.11 ms on a PCVR GPU, and it is far cheaper than the four `vnoise`
   evaluations per fragment over 1.6× the panel area that the old quad did — but it is an
   expectation, not a measurement. **If a frame-time spike appears while a window animates, the first
   knob is `WindowMaterialiseIntensity` (0 removes the geometry entirely without touching the element
   dissolve) and the second is `DebrisPerSquareMetre`.**
6. **Stereo comfort, as opposed to stereo correctness.** §6 proves no per-eye input reaches the
   effect. It does not prove the *result* is comfortable: a 4 mm shard at 0.9 m is ~0.25°, which
   should be far above the aliasing regime, but this project has been wrong about exactly that before.
   One hardware look settles it, and the shipped log line prints the angular sizes on his machine.
7. **Real windows.** I have not seen this on a window with TMP sub-meshes, a `ScrollRect`, a nested
   adopted canvas, or the `Custom/SimpleGrabPassBlur` under the item confirmation box. Two specific
   predictions I would like falsified: a graphic with a non-stock material that ignores vertex alpha
   will not fade (it will simply stay put and then go with the release — it looks wrong on that one
   element, nothing breaks); and `CanvasRenderer.SetAlpha` is a channel uGUI itself does not write, so
   I expect no write war.
8. **The supersample interaction, in the direction I changed it.** Moving the carrier to the mod layer
   is what stops the debris being captured into a supersampled panel's own RenderTexture. I reasoned
   it from `PanelSupersample.2.Capture.cs:849` (`cam.cullingMask = 1 << e.Layer`) and
   `VRRigDriver.HeadCamera.cs:94` (`MaskNarrowingFloor`); I have not watched it. **`VRLayers.Apply` is
   a no-op while VR is not running**, so on the desktop dev path the carrier keeps
   `panel.HostGo.layer` and the old behaviour — which is correct there, because desktop cameras do not
   render the mod layer.
9. **The map-room table at 198 wu/m.** The scale chain is derived from the panel's own measured
   transform and should be right there by construction, and the log line is change-gated on the chain
   so it will print its own second line the first time a map-room window animates. Unverified.
10. **Multiplayer.** Asserted, not tested: **local presentation only.** No wire field, no packet
    change, no `ModBuild` bump. A window's pose sync, its shared-window identity and its arc seat are
    untouched, and the seat is released on the same tick as today because the float leaves `Converted`
    before the animation starts. A peer with the dial off sees today's behaviour while their partner
    sees debris.
11. **The panel in every render is a SYNTHETIC stand-in.** There is no game install on this machine.
    Its element rectangles drive the dissolve and seed the shards exactly as real `CanvasRenderer`s
    do, so what the renders show about the MECHANISM is faithful; what they show about the CONTENT is
    a guess. Say so when forwarding them.

---

## 9. The durations — the hunk for `Defaults/Defaults.WorldUI.cs`

### First, the correction: his complaint was not about the configured number

What actually shipped in 292/293 was **appear 0.50 s, vanish 1.00 s**, both already inside the bounds
he restated ("unter einer Sekunde" / "nicht mehr als 2 Sekunden"). So *"die Einblendanimation muss
deutlich kürzer sein"* is about how it **feels**, and shortening the number alone would not have
answered him. The reason it felt long is the one the previous writeup already names: for a large part
of the ramp the window was neither properly there nor properly gone.

**That is fixed structurally, not by the clock.** The element front now completes in the first
`ElementSpan = 0.58` of the duration, and only the debris uses the rest. So:

| | 292/293 shipped | proposed | the number that matters |
|---|---|---|---|
| appear, total | 0.50 s | **0.35 s** | |
| appear, **window solid and legible** | 0.50 s | **0.20 s** | 2.5× sooner |
| vanish, total | 1.00 s | **0.90 s** | |
| vanish, **window fully gone** | 1.00 s | **0.52 s** | 1.9× sooner |

The debris that runs on after those moments blocks nothing: on an appear the window has been
clickable since frame one and is now also fully drawn; on a vanish the raycaster was disabled in
`PlayOut`'s first statement. Both numbers sit on the config step grid (0.05: 0.35 = 7 steps, 0.90 =
18 steps) and both remain clamped to `HardCeilingSeconds = 2.0` in code on every read.

Vanish 0.90 rather than something shorter deliberately: he explicitly allowed the vanish to be the
longer one, the window itself is gone at 0.52 s, and the remaining 0.38 s is the debris actually
having room to fly — cutting it would put the effect back to a puff that ends before it reads.

### The hunk

```diff
--- a/src/GloomhavenVR/Defaults/Defaults.WorldUI.cs
+++ b/src/GloomhavenVR/Defaults/Defaults.WorldUI.cs
@@
     // ---- WorldUI/WindowMaterialise*.cs ---------------------------------------------
-    // User, 2026-08-24: "Ich moechte nicht mehr, dass die Fenster einfach aufploppen und urploetzlich
-    // wieder von einem Frame auf den anderen verschwinden. ... Ich stelle mir ein verschwindendes
-    // Fenster vor, das in Partikel von Wind verweht. ... Aber wichtig: Das Ganze soll 1s hoechstens
-    // 2s gehen, es soll niemanden aufhalten, nur cool aussehen. Die Auftauch-Animation eventuell
-    // etwas schneller als die Verschwinden-Animation, da man hier schnell interagieren koennen soll."
-    //
-    // THE TWO DURATIONS ARE HIS TWO SENTENCES, and the ORDER between them is the requirement, not the
-    // numbers: appear must be the shorter one. 0.50 / 1.00 puts the pair inside his "1s, hoechstens
-    // 2s" with the vanish AT his stated 1 s and the appear at half of it, so the window is fully
-    // legible about a fifth of a second after it is already clickable. Both are additionally clamped
-    // to WindowMaterialise.HardCeilingSeconds = 2.0 IN CODE on every read, so a hand-edited cfg
-    // cannot make a window slow to appear -- which is the one thing this feature may never do.
+    // User, 2026-08-26, on ModBuild 292/293: "Wichtig: Die Einblendanimation muss deutlich kuerzer
+    // sein, am besten unter einer Sekunde. Die Ausblendanimation darf etwas laenger dauern, aber
+    // auch nicht mehr als 2 Sekunden."
+    //
+    // HIS COMPLAINT WAS NOT ABOUT THESE NUMBERS, AND THAT IS THE POINT OF THIS COMMENT. 292/293
+    // shipped 0.50 / 1.00 -- already inside both bounds he restated. What felt long was that for a
+    // large part of the ramp the window was neither properly there nor properly gone. So the fix is
+    // structural (WindowMaterialiseField.ElementSpan = 0.58 finishes the WINDOW in the first 58 % of
+    // the duration and leaves the rest to debris that blocks nothing) and the numbers below only
+    // follow it:
+    //
+    //   appear 0.35 s -> the window is solid and legible at 0.20 s   (was 0.50 s)
+    //   vanish 0.90 s -> the window is fully gone at 0.52 s          (was 1.00 s)
+    //
+    // THE ORDER BETWEEN THEM IS STILL THE REQUIREMENT, not the numbers: appear must be the shorter
+    // one, because that is the one a player is waiting on. Both are additionally clamped to
+    // WindowMaterialise.HardCeilingSeconds = 2.0 IN CODE on every read, so a hand-edited cfg cannot
+    // make a window slow to appear -- the one thing this feature may never do. Both sit on the 0.05
+    // config step grid (7 and 18 steps).
     internal const bool WindowMaterialise = true;                    // => [WorldUI] WindowMaterialise
-    internal const float WindowMaterialiseAppearSeconds = 0.5f;      // => [WorldUI] WindowMaterialiseAppearSeconds
-    internal const float WindowMaterialiseVanishSeconds = 1f;        // => [WorldUI] WindowMaterialiseVanishSeconds
+    internal const float WindowMaterialiseAppearSeconds = 0.35f;     // => [WorldUI] WindowMaterialiseAppearSeconds
+    internal const float WindowMaterialiseVanishSeconds = 0.9f;      // => [WorldUI] WindowMaterialiseVanishSeconds
     internal const float WindowMaterialiseIntensity = 1f;            // => [WorldUI] WindowMaterialiseIntensity
```

**Until that hunk is applied, `Defaults.WorldUI.cs` still reads 0.50 / 1.00.** The renders in §7 were
produced with `WM_APPEAR_S=0.35 WM_VANISH_S=0.90`, which the preview prints loudly and stamps into
`sim/meta.json` as `"overridden": true` — a strip labelled with a number nothing ships is worse than
no strip.

`WindowMaterialiseIntensity` is unchanged at 1.0 but its **meaning** has changed: it now scales shard
SIZE rather than a painted amplitude, and **0 skips building the geometry entirely** rather than
drawing an invisible quad. Its config description was updated accordingly. The other two dials'
descriptions were rewritten for the same reason. **No default value other than the two durations
changes**, so `check-remote-defaults.py` and `rebase-defaults.py` see nothing move.

---

## 10. Gates

Run on this worktree at the final commit. **Every one matches the brief's baseline.**

| Gate | Baseline | Result |
|---|---|---|
| `scripts/build.sh` | 0 errors / exactly 4 warnings | **0 / 4** |
| `scripts/wire-tests.sh` | 150861 | **150861** |
| `scripts/check-mirrors.sh` | 18 | **18** |
| `scripts/check-frame-order.sh` | 7 | **7** |
| `scripts/patch-inventory.sh check` | 80/132 | **80 classes, 132 methods** |
| `scripts/check-remote-defaults.py` | 76 | **76** |
| `scripts/check-refasm.py` | 16 | **16** |
| `scripts/check-bundle-format.sh` | 69,536,022 bytes | **69,536,022** (unchanged — the shader is a source file; the bundle changes when the integrator rebuilds it) |
| `scripts/check-wire-coverage.py` | — | exit 0 |

**`wire-tests.sh` RUNS on this worktree, and the previous round's writeup said it could not.** The
difference is `scripts/worktree-setup.sh`, which links `ressources/` (the game's Managed folder) and
`Directory.Build.props.user` in from the main checkout — without it `$(GameManaged)` resolves to
`libs/RefAsm` and the test binary dies with `BadImageFormatException`. A fresh worktree cannot build
at all until that script is run, and the previous lane appears to have concluded from the failure
that the gate was environmental. It is not; it is one command.

**The assertion count did NOT move**, which is the interesting part: `BundledShaderVectors` checks the
`BundleShaders.Paths` table against the real `.shader` files in both directions, and the shader kept
its name and its path, so nothing there changed. `ConfigStepVectors` sweeps `Defaults/` for
`// => [Section] Key` annotations and no annotation was added or removed. Applying the §9 hunk changes
two default *values*, not the set of keys, so it should not move the count either — but that is the
integrator's to confirm, since the hunk is not applied here.

`scripts/patch-inventory.sh check` also prints its usual standing warning that
`docs/PATCH-INVENTORY.md` line references have shifted. It prints that on untouched `origin/dev` too;
no patch was added, removed or unregistered by this lane.

`scripts/rebase-defaults.py check` reports pending cfg-drop differences (WristHud plate offsets and
similar). It reports the same list on untouched `origin/dev`; nothing in it belongs to this lane.
