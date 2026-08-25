# Window materialise — windows form out of wind-borne flakes and blow away into them

**Lane:** `agent-ac2d785b932d5e0a6`, branched from `origin/dev` at `1ce7127d` (ModBuild 290).
**Status:** effect implemented and committed; **integration is NOT applied** — the two call sites live
in files another lane is rewriting, so they are delivered below as exact hunks.

> User, 2026-08-24: *"Ich möchte nicht mehr, dass die Fenster einfach aufploppen und urplötzlich
> wieder von einem Frame auf den anderen verschwinden. Ich möchte für ein Fenster das geschlossen
> wird oder verschwindet eine kleine Animation, genauso für ein Fenster das auftaucht. Ich stelle
> mir ein verschwindendes Fenster vor, das in Partikel von Wind verweht. Und Auftauchen eventuell
> andersrum: Partikel formen sich zu einem Fenster und 'materialisieren' es. Aber wichtig: Das Ganze
> soll 1s höchstens 2s gehen, es soll niemanden aufhalten, nur cool aussehen. Die Auftauch-Animation
> eventuell etwas schneller als die Verschwinden-Animation, da man hier schnell interagieren können
> soll."*

---

## 1. What was built

| File | What it is |
|---|---|
| `unity/GloomhavenVR.Assets/Assets/Bundle/Table/WindowMaterialise.shader` (+ `.meta`) | the flakes: one quad, one pass, no clock, no screen-space input |
| `src/GloomhavenVR/WorldUI/WindowMaterialiseField.cs` | the field, in C#. The shape constants live here and nowhere else |
| `src/GloomhavenVR/WorldUI/WindowMaterialise.cs` | the facade: config, shader, `PlayIn`/`PlayOut`/`Cancel`/`CancelAll`/`IsVanishing` |
| `src/GloomhavenVR/WorldUI/WindowMaterialise.Quad.cs` | the quad mesh, the mesh pool, the measured world-size log |
| `src/GloomhavenVR/WorldUI/WindowMaterialiseRunner.cs` | one playing effect; every interruption path |
| `unity/asset-preview/windowmaterialise_field.py` | the field a third time, in numpy float32 |
| `unity/asset-preview/windowmaterialise_preview.py` | frame strips, mp4s, the stereo audit, the endpoint audit |
| `unity/asset-preview/windowmaterialise_room.py` | Blender 4.2 / `BLENDER_EEVEE_NEXT` re-photograph at real world scale, plus a stereo pair |

**Two existing files were edited**, both additively and both outside the lane's forbidden set:

* `src/GloomhavenVR/Core/BundleShaders.cs` — one row in `Paths` plus one bullet in the doc. The wire
  test `BundledShaderVectors` **requires** this; a new bundled shader cannot be resolved without it.
* `src/GloomhavenVR/Defaults/Defaults.WorldUI.cs` — the four shipped defaults, appended as one
  block with the `// => [WorldUI] Key` annotations that `rebase-defaults.py` and `ConfigStepVectors`
  read.

The dials are bound from `WindowMaterialise.cs` against `WorldUIConfig.FileHandle`, so
`WorldUIConfig.cs` is **untouched** — the same arrangement `ModalFallback.9.Spawn.cs:1532` already
uses for `WindowLegibility`.

### How it works, in one paragraph

A dissolve that is only a shader can paint debris *over* a window but cannot remove the window, and
uGUI offers no per-pixel handle on a canvas without putting a material on every `Graphic`. So the
effect is two halves that share one field. **C# removes the window at ELEMENT granularity**: every
`CanvasRenderer` under the host gets its `SetAlpha` driven by `WindowMaterialiseField.PresenceOf`,
sampled at its four corners and centre, so the window disintegrates in a wave along the wind and a
big element dissolves across the whole sweep while a small one snaps. **The shader paints the
flakes** on one mod-owned quad parented to the host, at the same wave front, with a crumbling edge
layer and a plume layer that streaks along the wind, shears sideways and thins as it blows away. The
appear is the vanish played backwards, which is exactly what the user described.

---

## 2. THE INTEGRATION HUNKS — apply these after the other lane lands

All four are against `origin/dev` @ `1ce7127d`. **Re-check the anchors** — I quote the exact
surrounding lines rather than line numbers alone precisely because they will have moved.

### Hunk 1 — the appear. `src/GloomhavenVR/WorldUI/CanvasConversion.4.Lifecycle.cs`

In `CompleteReveal(ConvertedPanel panel)`. Anchor: **line 596 on `1ce7127d`**, the line whose own
comment (`:700-703`) calls it *"the ONE place a panel's visibility is ever switched ON"*.

```diff
         SetPanelRenderVisible(panel, visible: true, out int shownCanvases, out int shownRenderers);
+        // WINDOW MATERIALISE. Here and not at float-creation, because THIS is the frame the eye
+        // first sees the window — and this is a LateUpdate, so the alphas PlayIn writes land before
+        // MultiPass renders either eye. Called AFTER the reveal on purpose: the window is already
+        // visible, already raycastable and already a laser target when this runs, so the animation
+        // is decoration over a window that works rather than a gate in front of one. It cannot
+        // fail in a way this caller has to handle — with the effect off, the shader unresolved or
+        // anything thrown, the window simply stays as this line left it: fully shown.
+        WindowMaterialise.PlayIn(panel);
         panel.RevealPending = false;
         panel.RevealArmed = false;
```

**One judgement call for you.** `CompleteReveal` fires for *every* `ConvertedPanel`, not only for
floated modal windows. I have deliberately **not** scoped it, because every `ConvertedPanel` is a
world-space surface the player sees appear and the user's complaint was about "die Fenster" in
general. If you want it modal-only, the walk already exists two lines above (`RefuseEmptyFloat`
at `:579`) and the guard is `if (ModalFallback.IsFloated(panel))` — which does not exist yet and
would have to be added in a ModalFallback file I may not edit.

### Hunk 2 — the vanish. `src/GloomhavenVR/WorldUI/ModalFallback.4.Tick.cs`

In the per-tick prune loop. Anchor: **lines 2779-2780 on `1ce7127d`**, the `wp.Grab?.Destroy()` /
`CanvasConversion.Release(wp.Panel)` pair, immediately after `Converted.RemoveAt(i)` (`:2770`).

```diff
             wp.Grab?.Destroy(); // drop the mod-owned grab holder (sub-item B) before releasing the host
-            CanvasConversion.Release(wp.Panel); // restores the exact 2D home
+            // WINDOW MATERIALISE. The float has ALREADY left Converted, the grab bar and its
+            // collider have ALREADY gone, and PlayOut disables the host's GraphicRaycaster as its
+            // first act — so from this statement on nothing about this window is clickable and only
+            // its pixels linger. The one thing deferred is Release, which is what re-parents the
+            // GAME's window back to its 2D home.
+            //
+            // PlayOut is a TOTAL function: the callback runs exactly once on every path, and
+            // SYNCHRONOUSLY when the effect is off, unavailable or refuses — so with the dial off
+            // this is byte-for-byte today's behaviour with a call in front of it.
+            //
+            // AN EMPTY RELEASE IS NOT ANIMATED. A window the liveness rule is releasing is by
+            // definition drawing nothing, so there is nothing to blow away; animating it would only
+            // delay the release of a window that is already dark.
+            ConvertedPanel dying = wp.Panel;
+            if (wp.EmptyReleasePending)
+                CanvasConversion.Release(dying); // restores the exact 2D home
+            else
+                WindowMaterialise.PlayOut(dying, () => CanvasConversion.Release(dying));
```

### Hunk 3 — the one guard the deferral needs. `src/GloomhavenVR/WorldUI/ModalFallback.4.Tick.cs`

In the convert loop. Anchor: **line 2868 on `1ce7127d`**, `if (EmptyHeldNow(window)) continue;`.

```diff
             if (EmptyHeldNow(window)) continue;
+            // ITS FLOAT IS STILL DISSOLVING. A vanishing float leaves Converted at once but its
+            // Release — the call that re-parents the game's window back to its 2D home — is
+            // deferred behind the animation. Re-floating the window inside that gap would record
+            // the DYING HOST as its original parent and the pending release would then re-parent it
+            // into a destroyed object. The gap is bounded by the hard code ceiling (2.0 s), so this
+            // can never hold a window out indefinitely.
+            if (WindowMaterialise.IsVanishing(window.transform)) continue;
```

*(Written in the file's own one-line `if (...) continue;` style; reformat if the surrounding block
uses braces.)*

### Hunk 4 — bulk releases must not animate. `src/GloomhavenVR/WorldUI/ModalFallback.9.Spawn.cs`

At the top of `ReleaseAllWindows(string reason)` (`:1992`) **and** `ReleaseMapRoomFloats(string
reason)` (`:2091`):

```diff
+        // Scenario exit / VR off / map-room stand-down. Five windows dissolving into a scene that
+        // is being torn down is not a nicer teardown, it is a slower one — and these paths do not
+        // route through the prune loop, so they never start an effect themselves. This only ENDS
+        // effects that were already running, restoring every alpha they wrote.
+        WindowMaterialise.CancelAll(reason);
```

Nothing else. `CanvasConversion.Release`, `RefuseEmptyFloat` and the dead-panel prune need no change:
a panel destroyed under a running effect fires the runner's `OnDestroy`, which restores and runs the
pending callback.

---

## 3. The interruption matrix

Every way in and every way out. The invariant being defended is: **no path ends with a window that
is alive, listed, clickable and invisible.**

| Entry / event | What happens | Where |
|---|---|---|
| Window revealed | `PlayIn` writes frame 0 **synchronously** inside the reveal's own LateUpdate, so the first rendered frame is already frame 0 of the animation and never a full-alpha pop | `Runner.Begin` last line |
| Window dismissed | `PlayOut` disables `HostRaycaster` **first**, before any decision about whether the effect can run | `WindowMaterialise.DetachInput` |
| Effect dial OFF | `PlayIn` returns having written nothing; `PlayOut` runs the callback inline | `Enabled` guards |
| Duration below 0.05 s | treated as OFF, not as a fast animation | `Clamp()` returns 0 |
| Shader unresolved (bundle not loaded yet) | quad is not built; the element dissolve still runs, with no flakes. Only *successes* are cached, so the next window retries | `TryBuildQuad` → false |
| Host rect degenerate | `Begin` returns false → `PlayOut` runs the callback inline | `Begin` |
| Element walk throws | runner nulls its own callback, finishes, returns false; `PlayOut` runs the callback | `Begin` catch |
| Ramp completes | restore → report → callback | `Finish("completed")` |
| **Closed while materialising** | the close path calls `PlayOut`, which calls `Cancel` first → the appear's `Finish(restore)` puts every alpha back → the vanish starts from the restored state | `PlayOut` → `Cancel` |
| **Re-opened while dematerialising** | Hunk 3 refuses to re-float it until the vanish ends; the release then runs and the next tick converts it fresh, with a `PlayIn` | `IsVanishing` |
| Host **deactivated** under us | `OnDisable` → `Finish(restore)`. LateUpdate would never run again, so this is the only chance | `Runner.OnDisable` |
| Host **destroyed** under us (game tore the UI down, scene change, dead-panel prune) | `OnDestroy` → `Finish(restore)`; the pending release still runs | `Runner.OnDestroy` |
| Scene torn down / scenario exit / VR off | Hunk 4's `CancelAll` | `WindowMaterialise.CancelAll` |
| Dial switched off **mid-flight** | next LateUpdate finishes and restores | `LateUpdate` guard |
| Panel dies mid-flight | next LateUpdate finishes and restores | `LateUpdate` guard |
| `LateUpdate` throws | logged once, `Finish(restore)`; if the recovery throws too the component disables itself | `LateUpdate` catch |
| `_seconds` pathological / `unscaledDeltaTime` pathological | **watchdog** at `HardCeilingSeconds + 0.5 s` | `LateUpdate` |
| `Finish` called twice | idempotent (`_finished`) | `Finish` |
| Callback would run twice | the callback field is nulled before invocation and the `Begin`-failure path nulls it before finishing | `Finish`, `Begin` catch |

Two properties make the restore trustworthy rather than approximately trustworthy:

* **The field is exact at both ends.** `Presence` is *exactly* 1 at progress 0 and *exactly* 0 at
  progress 1 for every UV, because the front travels from `-Softness` to `1 + Softness` while the
  threshold is confined to 0..1. Verified mechanically over 4000 random elements — the preview
  prints `|1 - presence(p=0)| max 0.000e+00, |presence(p=1)| max 0.000e+00 -> EXACT`.
* **Alphas are written as `original × presence`, not as `presence`.** An element the game had put at
  0.3 never jumps to 1.0, and at progress 0 the write is literally the number that was already
  there. The explicit restore is therefore belt-and-braces rather than the mechanism.

---

## 4. Measured cost

**The CPU half is measured, in the real Unity runtime, at the game's own editor version
(`/home/claw/unity-2021.3.5`, batchmode, `xvfb-run`).** Real `CanvasRenderer`s under a real `Canvas`,
the shipped `PresenceOf` arithmetic, the real native `SetAlpha` interop, 300 frames after a 20-frame
warm-up:

```
   64 CanvasRenderers: mean 0.0157 ms, worst 0.0450 ms per frame  (0.1 % of 11.11 ms)
  200 CanvasRenderers: mean 0.0519 ms, worst 0.0585 ms per frame  (0.5 % of 11.11 ms)
  400 CanvasRenderers: mean 0.1047 ms, worst 0.1669 ms per frame  (0.9 % of 11.11 ms)
  764 CanvasRenderers: mean 0.2025 ms, worst 0.2363 ms per frame  (1.8 % of 11.11 ms)
 1200 CanvasRenderers: mean 0.3192 ms, worst 0.3732 ms per frame  (2.9 % of 11.11 ms)
```

Linear at **≈ 0.265 µs per element per frame**, which is what it should be: five `smoothstep`s and
one native call, with the noise evaluated once per element at effect start and never again.

* **Per animating window per frame:** 0.10 ms at 400 elements, 0.20 ms at 764 (the largest window
  census this project has recorded).
* **Worst plausible simultaneous case:** the map room's arc holds five windows and the supersampler's
  effective cap is seven. Seven windows at 400 elements each = **0.73 ms/frame, 6.6 % of budget**,
  and only while they are all animating (≤ 1.0 s). In practice windows open and close one at a time;
  five at 400 = 0.52 ms.
* **The shipped code measures itself.** Every effect prints its own mean and worst frame with the
  element count that produced them (`WINDOW MATERIALISE VANISH on '…' … per-frame cost MEASURED at
  X ms mean, Y ms worst`). The numbers above are from this machine's CPU, not the user's; the log
  line is what settles it on his.

**Allocation.** Per effect: one `GameObject` + `MeshFilter` + `MeshRenderer`, one
`MaterialPropertyBlock`, and three `List<>`s **taken from a static pool** (8 renderer lists, 16 float
lists) and returned on finish. The `Mesh` comes from a pool of 8. The material is **shared across all
effects** — per-effect values ride the property block. Hunk 2 allocates one closure per window close.
Nothing allocates per frame. `FindObjectsOfType` is not used anywhere in this feature; the element
walk is one `GetComponentsInChildren` into a pooled list, once per effect.

**Render targets.** The effect needs none. It works identically whether or not the panel is
supersampled: a supersampled panel's canvas is re-captured every frame
(`PanelSupersample.SyncVisibility`, `CaptureIntervalFrames` = 1), so the element alphas propagate
into the RT; a non-supersampled panel draws them directly. The flake quad sits on `HostGo.layer`
(the mod layer the head camera draws and the game's UI camera cannot), so it is outside every
supersample capture camera's private layer and cannot leak into a neighbour's target. **No new
`PANEL SUPERSAMPLE`-scale memory is taken.**

**GPU.** Four `vnoise` evaluations per fragment (≈ 16 hash folds) over a quad about 1.6× the panel's
area, for ≤ 1 s, in two eyes. **I could not measure this** — see §7.

---

## 5. The stereo argument

The trap: a dissolve driven by **screen-space** noise gives each eye a different threshold for the
same surface point. It looks perfect on a monitor and perfect in each eye separately, and reads as
flicker in the headset. This project's own history — *"aliasing is per-eye"*, *"the flicker is
elements toggling"* — is exactly that.

**Every quantity in the fragment stage is a function of `i.uv` — the interpolated PANEL UV — plus
per-draw uniforms.** Both eyes rasterise the same triangle and interpolate the same UV to the same
surface point, so both eyes compute a bit-identical value. That is the same argument
`HexDecalStable.shader` makes at its head, and the same reason neither file uses an instancing macro:
multipass is per-eye-correct by construction as long as nothing stale is read.

Two rendered eye images cannot prove this — a screen-space dissolve passes that test. What settles it
is that **no per-eye input reaches the field**, and that is checked mechanically. The preview writes
`stereo_audit.txt`, which greps the shipped shader (comments stripped) for 18 per-eye-unstable
identifiers:

```
VPOS  SV_Position  _ScreenParams  ComputeScreenPos  ComputeGrabScreenPos  GrabPass
_CameraDepthTexture  unity_CameraInvProjection  unity_CameraToWorld  _WorldSpaceCameraPos
unity_StereoEyeIndex  _Time  _SinTime  _CosTime  unity_DeltaTime  ddx  ddy  fwidth
```

Current result: **PASS — all 18 absent.** The C# half is per-window, not per-eye, by definition, and
it runs in `LateUpdate`, which MultiPass renders both eyes *after*.

**The frequency-scrubbing trap falls out of the same property.** There is no clock in the shader at
all. The one time-like input is `_Progress`, written per frame by C#, used only as a **position** (the
erosion front) and an **amplitude** (plume travel, `_Drift · p^1.5`). No dial multiplies a frequency,
because no frequency exists. `WindowMaterialiseIntensity` scales a master alpha and nothing else.

---

## 6. The renders — look at these before anything ships

Produced by `python3 unity/asset-preview/windowmaterialise_preview.py`, then
`xvfb-run -a /home/claw/blender-4.2/blender --background --python unity/asset-preview/windowmaterialise_room.py -- render/windowmaterialise`.

| Path (under `render/windowmaterialise/`) | What |
|---|---|
| `vanish_strip.png`, `appear_strip.png` | 12 frames evenly across the FULL duration, labelled with elapsed time and progress |
| `vanish.mp4`, `appear.mp4` | 60 fps at the real shipped durations (1.00 s / 0.50 s) |
| `room_vanish.png`, `room_appear.png` | Blender EEVEE_NEXT, the panel at its real **0.56 × 0.36 m** at **0.90 m** from the eye |
| `room_stereo_pair.png` | left and right eye of one mid-vanish instant, 63 mm IPD |
| `stereo_audit.txt` | the mechanical per-eye-input audit |

`render/` is **not** committed (11 MB of PNG/mp4) and is not in `.gitignore` either — the
images live on this worktree's disk at
`/home/claw/gloomhaven_vr/.claude/worktrees/agent-ac2d785b932d5e0a6/render/windowmaterialise/`.
Both scripts regenerate everything in about a minute; `measured_cpu_cost.txt` beside them is the
Unity benchmark output quoted in §4.

### What I changed because the previews said to

Three rounds, each driven by a defect visible in the strip rather than by taste:

1. **The first strip had dead frames at both ends.** The 1.00 s vanish was visually over by 0.63 s
   and the 0.50 s appear showed *literally nothing* for its first 0.13 s — a window that is live and
   clickable while the player sees an empty space. Causes: an eased progress ramp compounding with
   the plume's own `p²` travel, a plume lifetime (`_AgeSpan` 0.30) too short for the debris to
   outlive the window, and a tail fade applied in both directions. Fixed by a **linear** ramp, a
   separate and much longer `_PlumeSpan` (1.5), `_Drift` 0.45 → 0.80, and `_TailFade` as a
   per-direction uniform: 1 for a vanish (must end at nothing), 0 for an appear (must *begin* at a
   cloud). **This is the one place the two directions are not mirror images, and they must not be.**
2. **The second strip's tail was a rectangle of TV snow.** A field of dots at any density reads as
   static, not as motion, and `inRect` drew it with a ruler-flat bottom edge. Fixed by streaking the
   noise **along the wind** by a factor that grows with age (`_Streak` 2.6, in the wind-aligned
   frame), thinning the plume as it blows (`_Thin` raises the flake threshold with age), a
   noise-modulated soft plume boundary, and a sideways shear (`_Spread`).
3. **A full-rect background plate cross-faded as one block.** Judging an element by its centre alone
   made the window's biggest `CanvasRenderer` hold, then fade over about a third of the duration, all
   at once — so the element wave read as *detail on top of a plain cross-fade*, which is the
   "aufploppen" being replaced, just slower. Fixed by sampling each element at its four corners and
   centre and averaging (`PresenceOf`). Cost: 5 smoothsteps instead of 1, measured above.

### My honest verdict on the look

**The vanish:** the window is eaten from the upwind side while pale debris streaks off it, and the
last third is a thinning wisp cloud that disperses. I think this is a fair match to *"in Partikel von
Wind verweht"*, and I would send it to the user.

**The appear:** a wispy cloud that condenses into the window; the window is legible from about
0.32 s and complete at 0.42 s of the 0.50 s. This matches *"Partikel formen sich zu einem Fenster"*
well.

**Where it falls short, and I would rather say it than have it come back:**

* **The large background plate still cross-fades rather than eroding.** Averaging over its extent
  spread the fade across the whole sweep, which is much better than a block, but it is a fade. This
  is inherent to element granularity: uGUI gives one alpha per `CanvasRenderer` and the plate is one
  `CanvasRenderer`. Between roughly p=0.25 and p=0.55 the window reads as *washed out + speckled*
  rather than *torn*. If that is not good enough, the only real fix is to capture the panel into a
  render target and dissolve the RT per-pixel — one camera and one RT per animating window. That
  machinery already exists (`PanelSupersample`), but it is capped at seven private layers, its
  targets run to 90 MB for a full-size window, and it would make the effect *depend* on a subsystem
  that legitimately refuses windows. I judged that the wrong trade for decoration; it is a real
  option and I am not hiding it.
* **The debris reads slightly more like smoke wisps than like discrete flakes** at the tail. That is
  the streaking from fix (2) — the deliberate price of not looking like static.
* **The panel in every render is a SYNTHETIC stand-in.** There is no game install on this machine.
  Its element rectangles drive the dissolve exactly as real `CanvasRenderer`s do, so what the strips
  show about the *mechanism* is faithful; what they show about the *content* is a guess. Say so when
  forwarding them.

---

## 7. WHAT I COULD NOT VERIFY WITHOUT HARDWARE

Read this section before treating anything above as proven.

1. **The shader has never been compiled.** No Unity project in this repo imports
   `Assets/Bundle/Table/` for compilation, and the bundle is built by the integrator. A syntax error,
   an interpolator-count problem or a `pow`-of-negative edge case would first show up at bundle-build
   time. **The bundle will change** (it is 69,615,927 bytes today and has been DLL-only-installed
   since 250), and it must be built with `/home/claw/unity-2021.3.5` — a 2021.3.45-compiled shader
   renders **pink** in game.
2. **The GPU cost of the flake quad is unmeasured.** Four `vnoise` evaluations (≈16 hash folds) per
   fragment over ~1.6× the panel's area, in two eyes, for ≤1 s. My expectation is that it is small
   against an 11.11 ms budget on a PCVR GPU, but that is an expectation, not a measurement. If the
   ModBuild that lands this shows a frame-time spike while a window animates, the first knob is
   `_FlakeDensity`, the second is dropping the plume's `inRect` shear, and `WindowMaterialiseIntensity 0`
   removes the quad's contribution entirely without touching the element dissolve.
3. **Stereo rivalry itself.** §5 proves no per-eye input reaches the field, which is the mechanism.
   It does not prove the *result* is comfortable — a spatially high-frequency pattern can still
   alias per-eye at a given angular size. `_FlakeDensity` 34 cells across the short side is roughly
   10 mm cells on a 0.36 m panel, ~0.6° at 0.9 m, which should be far above the aliasing regime. But
   this project has been wrong about exactly that before, and one hardware look settles it.
4. **The Python preview is a transcription of the shader, not the shader.** It reads the constants
   out of the C# file (and hard-fails if it cannot find them), and the arithmetic is line-for-line in
   float32 — but no test compares a Unity-rendered frame with a numpy-rendered one, because I cannot
   render the former. A divergence would show as "the headset looks different from the strip".
5. **The wire-test gate could not be run here** — see §8.
6. **Real windows.** I have not seen the effect on a window with TMP sub-meshes, a `ScrollRect`, a
   nested adopted canvas, or the `Custom/SimpleGrabPassBlur` under the item confirmation box. Two
   specific predictions I would like falsified on hardware:
   * a graphic with a **non-stock material that ignores vertex alpha** will not fade (the GrabPass
     blur is such a graphic). It will simply stay put and then vanish with the release. Nothing
     breaks; it looks wrong on that one element.
   * `CanvasRenderer.SetAlpha` is a channel uGUI itself does not write (`CanvasGroup` uses
     `SetInheritedAlpha`, `Graphic` uses `SetColor`), so I expect no write war. If some game code
     *does* use it, we win anyway — we write last, in `LateUpdate` — but that element's own animation
     would be suppressed for the duration.
7. **The empty-window rule.** I coded against an **assumption**, stated rather than against today's
   predicate, because that rule is being rewritten in a parallel lane: *an appear holds a window
   below the drawing threshold for at most `AppearSeconds`, which is hard-capped at 2.0 s, so it
   cannot complete a 2.0 s empty-window timer; and a vanish leaves `Converted` in the frame it
   starts, so the rule never sees it.* Hunk 2 additionally refuses to animate a release the rule
   itself triggered. `WindowMaterialise.IsAnimating(panel)` is offered if the rewritten rule wants an
   explicit test. **If the new rule shortens its dwell below 2.0 s, or starts measuring panels that
   have left `Converted`, re-check this.**
8. **Multiplayer.** Asserted, not tested: **local presentation only.** No wire field, no packet
   change, no `ModBuild` bump. The effect writes element alphas and one mod-owned quad on the local
   client; a window's pose sync, its shared-window identity and its arc seat are untouched, and the
   arc seat is released on the same tick as today because the float leaves `Converted` before the
   animation starts. A peer sees their own animation, on their own client, at their own configured
   duration — and a peer with the dial off sees today's behaviour while their partner sees flakes.
   `check-wire-coverage.py` exits 0 and does not list these dials (they are not board dials).

---

## 8. Gates

Run on this worktree at the final commit.

| Gate | Baseline in the brief | Result |
|---|---|---|
| `scripts/build.sh` | 0 errors / exactly 4 warnings | **0 errors, 4 warnings** ✅ |
| `scripts/patch-inventory.sh check` | 80/132 | **80 classes, 132 methods** ✅ |
| `scripts/check-frame-order.sh` | 7 | **7** ✅ |
| `scripts/check-mirrors.sh` | 18 | **18** ✅ |
| `scripts/check-remote-defaults.py` | 76 | **76** ✅ |
| `scripts/check-refasm.py` | 16 | **16** ✅ |
| `scripts/check-bundle-format.sh` | 69,615,927 bytes | **69,615,927** ✅ (unchanged — the shader is a source file; the bundle changes when *you* rebuild it) |
| `scripts/check-wire-coverage.py` | — | exit 0 ✅ |
| `scripts/wire-tests.sh` | 150837 | **COULD NOT RUN — environmental** ⚠️ |
| `scripts/rebase-defaults.py check` | — | **COULD NOT RUN — environmental** ⚠️ |

**The two that could not run, and why it is not my change.**
`tests/GloomhavenVR.WireTests.csproj:309` references `$(GameManaged)\UnityEngine.CoreModule.dll`
*"so Mathf's rounding matches the shipped build exactly"*. There is no game install on this machine,
so `$(GameManaged)` resolves to `libs/RefAsm`, the project builds against a metadata-only stub, and
the binary dies at startup with `BadImageFormatException: Reference assemblies cannot be loaded for
execution`. That is true on a clean `origin/dev` checkout here too. `rebase-defaults.py check` exits
with `no cfg directory at .planning/debug/default` — that directory is git-ignored and holds the
user's tuned cfg drops.

**What you must re-check on a machine that has the game**, since I could not:

* `BundledShaderVectors` — I added the `Paths` row and the shader file declares exactly
  `Shader "GloomhavenVR/WindowMaterialise"`; no bare `Shader.Find("GloomhavenVR/…")` appears anywhere
  in my code. I believe this passes, but the assertion count **will move** off 150837.
* `ConfigStepVectors` — it sweeps `Defaults/` for `// => [Section] Key` annotations, so my four new
  entries are automatically under the step rule. Worked through by hand against `ConfigSteps.cs`:
  * `WindowMaterialise` — bool, integral, exempt.
  * `WindowMaterialiseAppearSeconds` / `…VanishSeconds` — end in `Seconds` → step **0.05**; declared
    range 0.05–2.0 → scale 2.0 → bounds `[0.008, 0.5]`. ✅
  * `WindowMaterialiseIntensity` — no unit-word suffix → magnitude derivation `scale/50` = 0.04 →
    `NiceStep` → **0.05**; range 0–2 → bounds `[0.008, 0.5]`. ✅

  None of the four carries a `_tag`, so each is its own family and its own declared range is the
  scale. The assertion count moves here too.

---

## 9. The dials

`[WorldUI]` in `dev.gloomhavenvr.worldui.cfg`. All four appear in the VR options browser
automatically — `ConfigCatalog` is a reflection walk over the `ModuleConfig` registry, so nothing
needs registering.

| Key | Default | Range | Step | What |
|---|---|---|---|---|
| `WindowMaterialise` | `true` | bool | — | master switch. **OFF restores exactly today's behaviour**: not a single alpha is written and the release happens in the same frame it does today |
| `WindowMaterialiseAppearSeconds` | `0.50` | 0.05–2.0 | 0.05 | shorter than the vanish, per the user's own sentence |
| `WindowMaterialiseVanishSeconds` | `1.00` | 0.05–2.0 | 0.05 | at his stated 1 s |
| `WindowMaterialiseIntensity` | `1.00` | 0–2 | 0.05 | flake amplitude. **0 leaves a clean directional wipe with no flakes**; also the escape hatch if the quad turns out to cost GPU time |

**Both durations are clamped to `HardCeilingSeconds = 2.0` in code, on every read** — not at bind
time, so a value dragged in the VR menu is clamped too. No cfg value can make a window slow to
appear. Below 0.05 s the effect is OFF rather than fast, which is the right reading of such a value.

The shape of the effect — wind direction, front softness, raggedness, flake density, drift, streak,
spread, thinning — is **deliberately not dialled**. `PanelSupersample`'s own ruling applies: the user
gets one switch and one factor, not fifteen knobs. They are constants in
`WindowMaterialiseField.cs`, and the preview reads them out of that file so a tuning pass cannot make
the strips stale.

No German localisation was added (`Loc.ConfigNames.cs`, `Loc.ConfigDescriptions.German.cs`). Missing
entries degrade safely to the key plus the bound English description. Adding them is a small,
conflict-prone edit to two shared files and is left to the integrator.
