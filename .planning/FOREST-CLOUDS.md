# Thin night cloud over the forest clearing

> **User request, verbatim.** "Im Wald will ich noch etwas anderes: Leichte Wolken, diese sollen
> realistisch wirken, niemals dicht sein und den Mond nie voll verdecken. Sie sollen sich leicht
> bewegen. Auch wichtig: Der Fokus ist auf dem Spiel selber, die Umgebungen sind nur Beiwerk, d.h.
> auch die Wolken sollen zwar so gut es geht aussehen aber performant sein und so gut es geht die
> Performance nicht reduzieren, suche nach Lösungen die das erfüllen. Mach dir renders davon, um
> dich selber davon zu überzeugen."

Five requirements, and the fifth outranks the other four. What shipped:

| # | Requirement | Where it is enforced | Number |
|---|---|---|---|
| 1 | realistisch | the ground-plane projection + moon-lit in-scatter | the renders, §7 |
| 2 | **niemals dicht** | `a = _CloudAlpha · ev · taper · cov`, three factors in [0,1] | ceiling **0.62**, measured peak **0.620** |
| 3 | **Mond nie voll verdecken** | a time-independent upper bound on the taper, §3 | floor **78.3 %**, measured **83.39 %** |
| 4 | leicht bewegen | additive uv wind, never a frequency | **0.055 °/s** at the moon's elevation |
| 5 | **performant** | a shell, not a dome; 71 fragment instructions; 2 fetches | **17 %** of what the sky it draws over already costs |

> **§10 supersedes the numbers in rows 2 and 3.** The 2026-09-02 hardware test asked for more
> visible clouds and the ceiling went 0.42 → 0.62. **Everything else in this document still holds
> as written** — the arguments in §2–§6 are about the arithmetic, and not one of them has a value
> in it. Where a section quotes an old number the current one is in §10's table beside it.

Everything here is reproducible from a clean tree; the commands are in §8.

---

## 1. What it is

One extra node, `CloudBand`, a child of `StarDome` in `Env_Swamp.prefab`. A 2 592-triangle cap of
the same unit sphere the star dome is, spanning elevation **11° to the zenith**, drawn by
`GloomhavenVR/EnvCloud` in queue `Background+7` — after the sky's continuous layer (`EnvStars`,
`+5`) and after the catalogue stars (`EnvStarPoints`, `+6`), so a wisp dims the stars behind it,
and before everything in `Geometry`, so the canopy still covers it.

**No runtime C# at all.** Nothing in `src/` changed. The band rides the sky branch for free:
`SkyAlternative`'s shell splitter only walks *direct* children of the shell root and only re-homes
four named room nodes, so anything under `StarDome` inherits the perceived-size-constant sky
anchoring by construction. It drifts on `_Time.y + _GhvrTimeOfs`, the mod's existing shared
environment clock, so **multiplayer is correct with zero new bytes on the wire** — the elected
owner's epoch already arrives on Net record 31 (`ExtIdEnvClock`) and every peer's clouds stand at
the same instant. It is local presentation only; nothing about it is replicated, and nothing about
it needs to be.

Three files are new: `EnvCloud.cginc` (the whole effect), `EnvCloud.shader` (the wrapper),
`EnvCloud_Noise.png` (512×512 **RG16**, 10 mips). Plus one mesh and one material.

### The construction, in one paragraph

The fragment takes `u`, the normalised **object-space** position of its point on the shell, and
projects it onto a horizontal slab at unit height: `pl = u.xz / max(u.y, sin 11°)`. That is the
real perspective of a flat cloud deck seen from underneath — features compress toward the horizon
as `1/sin²(elevation)` because they genuinely are further away — and it is most of why the result
reads as a layer rather than as a texture painted on a dome. Two scrolled samples of the noise
(coarse `.r`, fine `.g`, different speeds and bearings) make a density; a threshold and a hardness
turn it into coverage; an elevation envelope and a moon taper scale it; the product times a ceiling
is the opacity. The colour is single-scattered moonlight with a forward-peaked lobe, so the layer
is nearly black away from the moon and glows where it crosses in front of it.

It composites **premultiplied** (`Blend One OneMinusSrcAlpha`), which is not a style choice:
single scattering through a thin slab is `L_out = L_scatter + T·L_background` with `T = 1-a`, and
that is exactly what One/OneMinusSrcAlpha computes when `.rgb` already carries the scattered
radiance. A straight `SrcAlpha` blend would need the scatter divided back out by `a`, which
explodes at the wisp edges where `a → 0` — the classic dark-fringe artefact.

---

## 2. "Niemals dicht" — a ceiling, not a setting

```
a = _CloudAlpha · ev · taper · cov
```

`ev` is a `smoothstep`, `taper` is `1 - k·smoothstep`, `cov` is a `saturate`. All three are in
`[0,1]` for every input, so **`a ≤ _CloudAlpha` everywhere in the sky, at every instant, for every
possible content of the noise texture.** `_CloudAlpha` is 0.42: the thickest wisp this layer can
produce still passes 58 % of whatever is behind it. There is no parameter combination and no
moment at which it can become a deck.

Measured against that, over 60 instants spanning the whole drift cycle in a 90° view of the sky:
the single most opaque pixel anywhere reached **α = 0.402**, and at most **19.9 %** of the sky
carried more than 2 % opacity at any instant. The measured peak sits just under the ceiling because
the noise field's own maximum (0.723) does not quite drive `saturate((d − 0.54)·3.0)` to 1; the
bake log prints that whole distribution.

---

## 3. "Den Mond nie voll verdecken" — the structural argument

This is the requirement that has to be a property of the construction rather than of a lucky drift,
so here is the whole chain. Nothing in it is a function of time, of the noise's content, of the
wind, or of where the player stands.

**Step 1 — the taper.** `taper = 1 − (1 − _CloudMoonMin)·smoothstep(cosOut, cosIn, m′)`. It reaches
its floor `_CloudMoonMin = 0.35` **exactly** when `m′ ≥ cosIn`, and it is flat there — not a
minimum at a point, a plateau.

**Step 2 — the perturbation is bounded.** `m′ = dot(u, moonDir) + _CloudEdge·(n − 0.5)` where `n`
is a noise sample in `[0,1]`. The rim of the clear patch wobbles so it is not a stamped circle, and
the wobble is time-varying — but `n − 0.5 ∈ [−0.5, +0.5]` **always**, so

```
m′ ≥ dot(u, moonDir) − 0.5·_CloudEdge      for every fragment, at every instant.
```

That inequality is the load-bearing line. It does not care what the noise does.

**Step 3 — the moon's whole sprite is inside the plateau.** Over the moon,
`dot(u, moonDir) ≥ cos(MoonSpriteRad)`. The bake computes `cos(4.45°) − 0.005 = 0.99470`, which is
a perturbed angle of **7.26°**, comfortably inside the **9.0°** plateau — **1.74° of slack**, and
`MoonSpriteRad` is the sprite's *corner*, i.e. disc **plus halo**, not the 1.39° of it that is lit
rock. `AssertCloudsClearTheMoon()` recomputes this at every bake and throws if it stops holding, so
a later round that nudges a `Range()` default fails the bake instead of shipping a covered moon.

**Step 4 — the bound.**

```
a ≤ _CloudAlpha · _CloudMoonMin = 0.42 × 0.35 = 0.147     over the moon, for all t
transmitted fraction ≥ 85.3 %
```

**And the clear patch cannot drift off the moon.** `CloudBand` is a child of `StarDome` at
**scale 1** — the same 45 m radius, in the same object frame, as the dome that paints the moon
sprite. Whatever parallax the moon has as the player walks the clearing, the patch has *exactly the
same* parallax. Had the shell been given a different radius to buy cloud-vs-star depth, the two
would separate by `≈ (1/r_cloud − 1/r_dome) × head offset` — small, but a number that has to be
argued rather than an identity that cannot fail. The moon's bearing is read from
`EnvironmentsBuilder.MoonDir`, the same constant that aims the sprite, the moonlight shafts, the
trunk rim light and the canopy tear, so there is one number and it cannot be edited apart.

### And then it was measured anyway

An argument about a shader is an argument about what I *think* the shader says. So the shipped
program was read through the real rasteriser, over the **whole** drift cycle:

* the shell is isolated (dome, stars, room renderers off) and rendered twice per instant, over a
  **black** clear and a **white** clear, read back as raw linear floats. Premultiplied compositing
  gives `black → L` and `white → L + (1−a)`, so **`a = 1 − (white − black)`**, with no reliance on
  what `L` is. Clipped fragments leave the background untouched and come back as `a = 0`, correctly.
* the moon's disc is located **from the picture**, not from a formula: the pixels above half the
  peak in a clean 11° frame. 11 056 px of 512², an equivalent angular radius of **1.28°**.
* 240 samples, 6 s apart — the finest noise octave subtends 0.32° there and the field drifts
  0.055 °/s, so a feature crosses a given point in about 6 s. Sampling at the feature's own
  crossing time is the condition for not stepping over a peak.

| | measured | structural bound |
|---|---|---|
| worst single pixel on the disc | α **0.0534** (t = 42 s) → **94.66 %** transmitted | ≤ 0.147 → ≥ 85.3 % |
| worst disc **mean** | α **0.0196** → **98.04 %** of its clear brightness | — |

![moon transmittance across one full drift cycle](debug/renders/forest-clouds/cloud_moon_occlusion.png)

Table: `render/clouds/cloud_moon_occlusion.tsv`, 240 rows.

**And the series is not a sample — it is all of the behaviour.** Both winds are a whole number of
texture periods per `CloudPeriod` (layer A `(1,0)`, layer B `(2,1)` turns), so the entire field
returns to its `t = 0` state exactly at `t = 1440 s`; `CloudPeriod` divides `EnvStars`' `SKY_PERIOD`
of 2880 s, so the sky clock's wrap crosses it without a jump. Checked, not asserted: the isolated
shell at `t = 0` and at `t = 1440 s` differs by a maximum of **7.6 × 10⁻⁶** in linear radiance
across the whole frame (float rounding in `1440 × (1/1440)`, an eighth of one 8-bit step), while the
same instrument on the same frame **half** a period apart reads **2.0 × 10⁻²** — a **2 600×**
positive control, so the comparison is one that could have failed.

**Honest note on the size of the margin.** The measured worst case (5.3 % dimming) is far milder
than the bound permits (14.7 %). That is not conservatism in the bound; it is that dense cloud
happens to be rare *and* thinned near the moon, so the two effects multiply. A dimming of 5 % is
about at the edge of visible. If the user comes back wanting to actually *see* a wisp cross the
moon, the dial is `CloudMoonMin` — raising it to 0.6 gives a 25 % dimming and still leaves the moon
75 % transmitted, and the whole argument above survives unchanged because it never depended on the
value.

---

## 4. "Sich leicht bewegen"

The wind enters as an **additive uv offset**. Nothing multiplies a frequency — `_CloudAlpha`,
`_CloudCut` and `_CloudSharp` are pure amplitude, `_CloudWind` is pure rate. The frequency-scrub bug
class (a strength dial that scales a frequency riding the shared clock, correct only at `t = 0`)
cannot occur here because no dial touches a frequency.

At the moon's elevation (40°) layer A drifts **0.0548 °/s = 3.29 °/min**; a wisp takes **51 s** to
cross the moon's 2.8° disc. A real cirrus deck at 8 km in a 20 m/s wind subtends about 0.06 °/s at
that elevation, which is the number this was aimed at. Layer B runs at 0.72× that speed, 27° off
layer A's bearing, so shapes **evolve** rather than translating rigidly — rigid translation is the
tell that gives away every scrolling-texture sky.

The speed is elevation-dependent for free: `dθ/dt = (dpl/dt)·sin²(elevation)`, so the same air moves
slowly overhead and quickly near the horizon, exactly as a real deck does.

---

## 5. Cost — measured where I could, and named where I could not

**I cannot measure GPU time.** There is no headset in this loop and no GPU timer in the preview
station. What follows is instruction counts from the real compiler and fill fractions from the real
rasteriser, plus the arithmetic, and an explicit refusal to convert them into milliseconds.

### The program

Compiled by **Unity 2021.3.5f1** for `StandaloneWindows64`, Built-in RP, cold cache
(`Local cache hits 0`), `1 / 1 variants left after stripping`, `d3d11 (total internal programs: 2,
unique: 2)`, `vs_4_0`/`ps_4_0` at 638 and 2 570 bytes. `ShaderHasError` false and
`GetShaderMessageCount` 0 after import, after `ShaderUtil.CompilePass`, and after a real
`BuildPipeline.BuildAssetBundles`.

|  | vertex | fragment |
|---|---|---|
| `EnvCloud` | 8 math, 2 temps | **63 math, 4 temps, 2 textures, 0 branches** (71 disassembled instructions incl. 2 `sample`, 1 `discard_nz`) |
| `EnvStars` — the layer it draws over | 8 math, 2 temps | 362 math, 10 temps, **7 textures, 9 branches** |

**So the cloud fragment is 17.4 % of the sky shader's math and 28.6 % of its texture fetches.**
That is the most useful honest comparison available: `EnvStars` is already shipped and accepted, and
its dome is a *closed sphere*, so it rasterises 100 % of every frame in every view.

### The fill, measured

The cloud shell's screen coverage, measured by cloning the shipped material and forcing `a = 1`
everywhere, then counting the fragments it actually issues:

| view | shell rasterises | survives `clip()` → blend |
|---|---|---|
| level at the horizon, 90° (looking at the board) | **40.1 %** | 28.3 % |
| seated, 74°, toward the moon's bearing | **54.5 %** | 30.3 % |
| open sky, 75° | 79.1 % | 28.1 % |
| standing, up at the canopy tear, 26° | 100 % | 46.8 % |
| straight up, 90° (worst case) | 100 % | 27.7 % |

**A correction to my own first measurement, because it matters.** The first version of this measured
how much of the frame the clouds visibly *changed* — 1.48 % from the seat, with the canopy over it —
and that number is worthless as a cost input. The shell is in the **Background** queue and the trees
are in **Geometry**: the shell rasterises *first*. The canopy hides its result; it does not save it
a single fragment. What the GPU is billed for is the shell's screen coverage, which is a property of
the frustum alone. (The 1.48 % is still the right number for a different question — how much of what
the player sees the clouds actually alter from the seat — and it is why the effect is nearly
invisible while looking at the board, which is the point.)

The probe itself had to be replaced too: the first one swapped in a `Unlit/Color` white material and
reported **0.0 %** coverage in every view, silently, with no error in the log. It is now a clone of
the shipped material with its dials forced, so the probe uses the same vertex stage on the same mesh
as the thing it measures and cannot disagree with it about where the geometry lands.

### The arithmetic

Per-eye target 3072×3264 = 10.03 Mpx; two eyes, MultiPass, 20.05 Mpx per frame; 90 Hz.

* **worst case** (looking straight up, 100 % coverage): 20.05 Mpx × 71 instructions = **1.42 G
  instructions/frame ≈ 128 G/s**; 40.1 M bilinear fetches/frame ≈ **3.6 Gtexel/s**; blend on 27.7 %
  = 5.6 Mpx/frame of frame-buffer read-modify-write.
* **the common case** (level, at the board, 40.1 % coverage): 0.57 G instructions/frame, 1.4
  Gtexel/s, blend on 28.3 %.

Relative to the sky that is already there: the clouds add **7.0 %** (level) to **17.4 %**
(straight up) of `EnvStars`' fragment math, and **11.5 %** to **28.6 %** of its fetches. The two
texture fetches are from one 512×512 RG16 (512 KB + mips ≈ 683 KB) which stays resident in texture
cache; the noise texture is the only new bundle byte and it is **0.98 %** of a 69.5 MB bundle.

### Why a shell and not one more term in `EnvStars`

Folding `GhvrCloudLayer` into `EnvStars`' fragment would cost no draw call and no blend — that is a
real alternative and it is worth stating plainly that this worktree does not own that file. But it
is not obviously the cheaper one:

* `EnvStars`' dome is a closed sphere and rasterises **100 % of every frame**. Folded in, the cloud
  term would run on every sky pixel in every view, including all of them below the ridge where it is
  guaranteed to be zero. The cap runs on 40–100 %, and it is at its cheapest precisely when the
  player is looking at the board, which is where the user's fifth requirement points.
* what the cap pays instead is a frame-buffer blend on 28–47 % of the frame.

Those two are within a factor of about two of each other and **I cannot rank them without a GPU
timer**. The maths lives in `EnvCloud.cginc` rather than inline in the shader exactly so that the
fold-in is a two-line change (`#include` + one call) for whoever owns `EnvStars.shader`. See §9.

---

## 6. Stereo — the constant-buffer proof

The rule this project has paid for: anything derived from screen space, from the view vector, or
from a camera position is a *different image in each eye* under MultiPass and reads as rivalry. The
grep-for-identifiers version of that check is worth little. Here is the compiled binary's own
constant-buffer table.

```
-- Vertex shader for "d3d11":
// Stats: 8 math, 2 temp registers
Constant Buffer "UnityPerDraw"  (176 bytes) on slot 0 {  Matrix4x4 unity_ObjectToWorld at 0  }
Constant Buffer "UnityPerFrame" (368 bytes) on slot 1 {  Matrix4x4 unity_MatrixVP     at 272 }

-- Fragment shader for "d3d11":
// Stats: 63 math, 4 temp registers, 2 textures
Set 2D Texture "_CloudTex" to slot 0
Constant Buffer "$Globals" (192 bytes) on slot 0 {
  Vector4 _GhvrElemB, _CloudWind, _CloudTint, _CloudMoonDir
  Float   _CloudScale _CloudRatio _CloudMix _CloudCut _CloudSharp _CloudAlpha
          _CloudElevLo _CloudElevHi _CloudMoonMin _CloudMoonIn _CloudMoonOut
          _CloudEdge _CloudScatBase _CloudScatFwd _CloudScatPow _GhvrTimeOfs
}
Constant Buffer "UnityPerCamera" (144 bytes) on slot 1 {  Vector4 _Time at 0  }
```

And the fragment's input signature:

```
// Name          Index  Mask  Register  SysValue  Format  Used
// SV_POSITION       0  xyzw         0       POS   float
// TEXCOORD          0   xyz         1      NONE   float   xyz
```

Read carefully:

* **No `_WorldSpaceCameraPos`.** It lives in `UnityPerCamera` at offset 16 and would appear in that
  block if anything read it. Only `_Time`, at offset 0, is bound.
* No `unity_CameraToWorld` / `unity_WorldToCamera`, no `UNITY_MATRIX_V`, no `unity_StereoEyeIndex`,
  no `unity_StereoMatrixVP`, no `unity_StereoWorldSpaceCameraPos`.
* **`SV_POSITION`'s `Used` column is empty** — the fragment stage does not read its own screen
  coordinate at all. That is the machine-checked form of "no screen-space dither, no screen-keyed
  pattern"; `EnvStars`, by contrast, does use `i.pos.xy` for its 1-LSB gradient dither.
* The only view-dependent quantity anywhere is `unity_MatrixVP` in the **vertex** stage, taking an
  object position to clip space. That is the mandatory transform; it carries no eye state into the
  effect's own arithmetic. Every vertex position is a function of that vertex's own attributes.
* `_GhvrElemA` is absent because the element block reads only Light/Dark; the compiler stripped the
  rest.

**Three caveats, unsmoothed.**

1. This is the non-stereo variant (`Keywords: <none>`, one variant total). Under **MultiPass** the
   per-eye VP arrives through that same `unity_MatrixVP` slot, so the set above is what ships; a
   **Single-Pass-Instanced** setup would add `unity_StereoMatrixVP` and this dump would not have
   caught it. The rig runs MultiPass, so the caveat is closed here.
2. **`tex2D`'s LOD selection is screen-space, and therefore per-eye.** The mip level is chosen from
   the screen-space derivative of `uv`, and those derivatives are computed in each eye's own
   projection. This is the one asterisk on "nothing here is screen-keyed", and it is inherent to any
   mipmapped fetch — the shipped `EnvStars` haze veil has exactly the same property. The practical
   size of it: for a surface at 45 m with a 63 mm IPD the two eyes' projected scales differ by
   ~0.14 %, i.e. a LOD difference of ~0.002 levels, and a fractional LOD difference produces a
   smooth blend difference rather than a pattern difference. I believe it is unobservable; I have
   not observed it.
3. `if (e.live > 0)` compiled **flat**, not as a dynamic branch (0 branches in the stats). So the
   element block costs ~4 ALU unconditionally rather than nothing, which is a correction to
   `EnvElement.cginc`'s rule 2 as it applies *here*. The zero state is still bit-identical, because
   `max(1 + 0 − 0, 0) = 1` exactly and multiplying by 1.0 is exact — but the *cost* claim in that
   rule does not hold for this shader and should not be repeated for it.

### The stereo pair

`render/clouds/cloud_stereo_L.png` / `_R.png` — parallel, IPD 63 mm, no toe-in, at the canopy-tear
framing. Mean `|L − R|` over the frame in linear radiance: **1.228 × 10⁻³** with clouds versus
**1.100 × 10⁻³** on the shipped sky alone — a ratio of **1.12×**. Neither is zero and neither should
be: a dome at 45 m has real parallax at 63 mm (about 0.08°, 2–3 px at this fov). The meaningful
quantity is whether the clouds *raise* the shipped sky's own figure, and 1.12× on a layer that
covers half the frame is what "adds no rivalry of its own" looks like.

---

## 7. The renders

Written by `Assets/Editor/PreviewClouds.cs` — a **new** station, deliberately not an edit to the
shared `PreviewEnvironments.cs` (another lane is in that file this round). ARGBHalf linear target,
manual `.gamma` encode, no exposure and no tonemap; 1280×720. All of it lands in

```
/home/claw/gloomhaven_vr/.claude/worktrees/agent-ae79fd723c7c05ad2/render/clouds/
```

| file | what |
|---|---|
| `cloud_before_sky.png` / `cloud_after_sky.png` | **the pair to look at.** 75° of open dome, identical camera, identical clock, one node toggled |
| `cloud_before_canopy.png` / `cloud_after_canopy.png` | standing, up through the canopy tear at the moon |
| `cloud_before_seat.png` / `cloud_after_seat.png` | the seat at the rig's real scale (2.30 m), the README framing |
| `cloud_cycle_t*.png` | 16 frames evenly across the full 1440 s drift cycle |
| `cloud_loop_t0_isolated.png` / `_tP_isolated.png` | the shell alone at `t = 0` and `t = 1440 s` |
| `cloud_stereo_L.png` / `_R.png` | the parallel 63 mm pair |
| `cloud_moon_occlusion.png` / `.tsv` | the occlusion series, plotted and tabulated |
| `cloud_cellar_unchanged_*.png` | the cellar, on record as untouched |

Two of them and the plot are committed under `.planning/forest-clouds/`; the rest are ~20 MB and
stay on the worktree's disk, following the precedent of `render/windowmaterialise/`.

**My own verdict on the look, since the brief asks for one rather than a defence.** The wide sky
frame is the one that convinced me: thin streaks lying along the wind, stars visibly blotted where a
wisp crosses them, and a lit cloud edge hard against the moon with the disc itself clean. The
streaks read as cirrus and not as noise, which is the anisotropic lattice (3×7 and 5×11) doing its
job — the features are elongated along layer A's wind axis, which is what high thin cloud actually
does. The isolated-shell frame shows the in-scatter concentrated around the moon and dying away
outward, which is the forward-scattering lobe behaving as designed and is the single largest
contributor to it looking lit rather than painted.

Where I would temper it: **from the seat, with the canopy overhead, the clouds are nearly invisible**
— 1.48 % of the frame changes. That is correct behaviour for a forest clearing and it is exactly
what "die Umgebungen sind nur Beiwerk" asks for, but it does mean the feature mostly exists for the
moment the player looks up. If that reads as too little on hardware, the honest levers are
`CloudCut` (how much of the sky is cloud at all, currently 34 % of the field) and `CloudAlpha`
(the ceiling), in that order.

### Exposure

The previews are known to be **brighter than the headset** (this project's own standing note), so
none of the above is an absolute-brightness claim. What makes these frames trustworthy is that every
judgement is a **before/after with one node toggled** — same camera, same clock, same encode — so
the exposure cancels out of the comparison entirely.

---

## 8. Reproducing all of it

```sh
# 1. bake (no -quit; BuildAll exits itself; -nographics is fine for the bake)
/home/claw/unity-2021.3.5/Editor/Unity -batchmode -nographics \
  -projectPath unity/GloomhavenVR.Assets -buildTarget Win64 \
  -executeMethod GloomhavenVR.EnvironmentsBuilder.BuildAll -logFile env-build.log
grep -E 'CLOUD NOISE|Cloud shell winding|CLOUD/MOON GUARANTEE|CLOUDS:|band-limit' env-build.log

# 2. renders + measurements (WITHOUT -nographics — rendering needs a device)
CLOUDS_PREVIEW_OUT=$PWD/render/clouds xvfb-run -a /home/claw/unity-2021.3.5/Editor/Unity \
  -batchmode -projectPath unity/GloomhavenVR.Assets -buildTarget Win64 \
  -executeMethod GloomhavenVR.CloudsPreview.RenderAll -logFile cloud-preview.log
grep -E 'CloudPreview\] (LOOP|STEREO|MOON|NEVER|SHELL|FILL|CELLAR|moon disc)' cloud-preview.log

# 3. the constant-buffer proof (harness at /tmp/cloud-shadercheck, see below)
xvfb-run -a /home/claw/unity-2021.3.5/Editor/Unity -batchmode \
  -projectPath /tmp/cloud-shadercheck -buildTarget Win64 \
  -executeMethod ShaderCheck.Run -logFile /tmp/cloud-shadercheck/run.log
sed -n '/-- Vertex shader for "d3d11"/,/Shader Disassembly/p'   /tmp/cloud-shadercheck/Compiled-GloomhavenVR-EnvCloud.shader
awk  '/-- Fragment shader for "d3d11"/,0'                       /tmp/cloud-shadercheck/Compiled-GloomhavenVR-EnvCloud.shader | head -30
```

The shader-check harness is **not in the repo** — it is a throwaway 2021.3.5f1 project
(`ProjectVersion.txt` pinned, linear colour space, no SRP package, `Assets/Editor/ShaderCheck.cs`
calling `ShaderUtil.CompilePass`, `ShaderData.Pass.CompileVariant` and `ShaderUtil.OpenCompiledShader`
by reflection). It was cloned from `/tmp/wm-shadercheck`, the one the window-debris round left
behind. If it is gone, recreate it by copying that project, swapping the shader in
`Assets/Bundle/Environments/` (`EnvCloud.shader` + `EnvCloud.cginc` + `EnvElement.cginc`), and
`sed`-ing `ShaderPath`/`ExpectedName`. **Somebody should commit that harness** — it has now been
recreated twice from scratch for two different shaders.

The plot is regenerated from the TSV with:

```python
# pip install pillow ; python3 plot.py cloud_moon_occlusion.tsv out.png 0.853
import sys; from PIL import Image, ImageDraw
src, dst, bound = sys.argv[1], sys.argv[2], float(sys.argv[3])
rows = [l.split() for l in open(src) if l and not l.startswith(('#', 't_s'))]
t  = [float(r[0]) for r in rows]
tm = [1.0 - float(r[1]) for r in rows]   # worst-pixel transmittance
te = [1.0 - float(r[2]) for r in rows]   # disc-mean transmittance
W, H, L, R, T, B = 1000, 470, 90, 24, 62, 56
im = Image.new('RGB', (W, H), (18, 19, 24)); d = ImageDraw.Draw(im)
y0, y1 = bound - 0.02, 1.012
X = lambda v: L + (W - L - R) * v / max(t)
Y = lambda v: T + (H - T - B) * (y1 - v) / (y1 - y0)
d.rectangle([L, T, W - R, H - B], outline=(70, 74, 86))
for g in [0.85, 0.88, 0.91, 0.94, 0.97, 1.00]:
    if y0 <= g <= y1:
        d.line([L, Y(g), W - R, Y(g)], fill=(44, 47, 56))
        d.text((10, Y(g) - 6), f'{g*100:5.1f}%', fill=(150, 155, 168))
for k in range(0, 7):
    x = X(max(t) * k / 6)
    d.line([x, T, x, H - B], fill=(44, 47, 56))
    d.text((x - 16, H - B + 8), f'{max(t)*k/6:.0f}s', fill=(150, 155, 168))
d.line([L, Y(bound), W - R, Y(bound)], fill=(220, 80, 70), width=2)
d.text((L + 8, Y(bound) + 6), f'structural floor {bound*100:.1f}%  (1 - CloudAlpha x CloudMoonMin)', fill=(230, 110, 100))
d.line([(X(a), Y(b)) for a, b in zip(t, te)], fill=(120, 200, 255), width=2)
d.line([(X(a), Y(b)) for a, b in zip(t, tm)], fill=(255, 205, 110), width=2)
d.text((L + 8, 10), 'moon transmittance across one FULL drift cycle (240 samples, 6 s apart)', fill=(235, 238, 245))
d.text((L + 8, 28), 'orange = worst single pixel on the disc   blue = disc mean', fill=(180, 186, 200))
d.text((L + 8, H - 22), f'measured minimum {min(tm)*100:.2f}%  -  the moon is never covered', fill=(255, 205, 110))
im.save(dst)
```

---

## 9. Changes I did not make, and would like made

1. **`.gitignore`** (not owned by this lane) — add `render/clouds/` and `cloud-preview.log`, beside
   the existing `render/windowmaterialise/` and `env-preview*.log` entries. Without them the 20 MB
   of renders and the batch log show up as untracked noise in every `git status`.
2. **`EnvStars.shader`** (not owned) — the fold-in, if the integrator wants to trade the blend for
   ALU below the ridge. It is literally `#include "EnvCloud.cginc"` plus, after the moon block:
   ```hlsl
   float4 cl = GhvrCloudLayer(u, t);
   col = cl.rgb + col * (1.0 - cl.a);
   ```
   and then deleting the `CloudBand` node from `AddNightSky`. §5 explains why I do not think it is
   obviously cheaper; it is a measurement on hardware, not an argument.
3. **Commit the shader-check harness** somewhere under `tools/` — see §8.

---

## 10. WHAT I COULD NOT VERIFY WITHOUT HARDWARE

Named plainly, because each of these is a place this could still be wrong.

1. **GPU time. There is none in this report.** No headset, no GPU timer, no frame capture. §5 is
   instruction counts, fetch counts and measured fill fractions, plus a ratio against a shader that
   is already shipped. Nobody should read "17 % of `EnvStars`" as "0.x ms" — the conversion needs a
   capture on the actual PC driving the Quest 3 over VDXR.
2. **Actual MultiPass rendering.** The stereo pair is two cameras 63 mm apart in a preview scene,
   not the XR runtime's stereo path, and the constant-buffer dump is the non-stereo variant (§6,
   caveat 1). The argument that it is per-eye correct rests on the geometry — every shaded value is
   a function of an object-space position — and the dump confirms the fragment binds no eye state.
   It has not been *seen* in a headset.
3. **Per-eye mip selection** (§6, caveat 2). Estimated at ~0.002 LOD levels of difference between
   eyes and therefore invisible; not measured, and unmeasurable without a headset.
4. **Whether 0.055 °/s reads as "leicht" in VR.** It matches real cirrus and it is barely
   perceptible frame to frame while being obvious over half a minute — on a monitor. Angular motion
   is judged differently at 90 Hz with a head that moves.
5. **Absolute brightness.** The previews are brighter than the headset; every conclusion here is
   from a before/after pair where that cancels, but "how visible are the clouds, really" is not
   settled by these frames.
6. **Aliasing in the headset.** The band-limit argument (finest octave at 0.32° = 9 headset px per
   period, Nyquist needs 2; texels below the pixel pitch handled by the trilinear mip chain, with
   the elevation envelope already at zero there) is arithmetic against an *assumed* 104°/3072 px
   angular pitch. A 30 % error in that assumption changes no conclusion, but only hardware settles
   whether the low-elevation edge of the shell sparkles.
7. **Multiplayer.** The clouds inherit `_GhvrTimeOfs` and add nothing to the wire, so two peers
   *must* agree by the same argument that makes the stars agree. Not tested with two clients.
8. **The cellar.** The clouds ship forest-only precisely *because* I could not verify it — the
   frames I shot of the cellar were not looking at the window. The cellar's sky is bit-identical to
   the previous build, which is the safe state, not a verified one.

---

## 10. The intensity round — 2026-09-02

> **User, hardware test, verbatim.** "Die Wolken im Waldgebiet sehe ich so gut wie garnicht. Nur
> ganz leicht. Das kann ruhig intensiver sein."

He is not describing a preference; he is reading the histogram in §2 correctly. At `CloudCut 0.54`
**two thirds of the sky carried no cloud at all**, and of the third that did, the 90th-percentile
pixel reached **α = 0.041** — one 8-bit step over the sky it lies on. "So gut wie garnicht" is what
that looks like.

### What moved, and in the order this document itself named

§7 said, before the test ever happened: *"the honest levers are `CloudCut` (how much of the sky is
cloud at all) and `CloudAlpha` (the ceiling), in that order."* That is exactly what was pulled.

| | was | now | why |
|---|---|---|---|
| `CloudCut` | 0.54 | **0.485** | coverage: 34.3 % → **59.4 %** of the noise field carries cloud |
| `CloudSharp` | 3.0 | **3.6** | keeps the wisps' edges from washing out as the threshold drops |
| `CloudAlpha` | 0.42 | **0.62** | the ceiling — the second lever, not the first |
| `_CloudScatBase` | 0.020 | **0.028** | ambient in-scatter, so a wisp far from the moon reads as a *cloud* and not as a hole in the star field |
| `CloudMoonMin` | 0.35 | **0.35** | **unchanged.** He relaxed the intensity; he did not withdraw the moon |

### What that did, measured on the shipped material

Bake log (`CLOUD NOISE`), which now prints **opacity** and not only coverage — the old line said
"34.3 % of the field carries any cloud" and told nobody that the veil was one 8-bit step deep,
which is precisely the thing that had to be re-measured after the complaint:

| percentile of the noise field | α before | α now |
|---|---|---|
| p50 | 0.000 | 0.010 |
| p90 | **0.041** | **0.242** |
| p99 | 0.165 | 0.477 |
| field max | 0.241 | 0.586 |

And through the real rasteriser (`CloudsPreview.RenderAll`, the same instrument as §3 and §5):

| | before | now |
|---|---|---|
| most opaque pixel anywhere, 60 instants over the full cycle | α 0.402 | **α 0.620** |
| share of sky over 2 % opacity at any instant | 19.9 % | **44.4 %** |
| **frame the clouds change, from the seat, room standing** | 1.48 % | **2.90 %** |
| frame the clouds change, unobstructed 75° sky | ~28 % | **49.6 %** |

The **veil the player actually looks at is six times more opaque and covers two thirds more sky.**

![the clearing's canopy tear, clouds off](debug/renders/forest-clouds/cloud_before_canopy.png)
![the clearing's canopy tear, clouds on](debug/renders/forest-clouds/cloud_after_canopy.png)

That pair is the answer to the complaint: same camera, same clock, one node toggled, at the pose he
plays from. Before, the tear shows a moon and nothing else; after, there are lit streaks across it.

### "Den Mond nie voll verdecken" — still enforced, now as a number

`CloudAlpha` multiplies **both** the sky and the moon, so "make the clouds stronger" is precisely
the edit that can erode this requirement without touching anything whose name mentions the moon.
The plateau check in §3 cannot catch it: `CloudAlpha` does not appear in the plateau condition at
all, so that check passes unchanged at *any* ceiling up to 1.0 — including one that blacks the moon
out. So the bake now carries a **second, separate throw**:

```
CloudMoonFloor = 0.75           // the moon's transmitted fraction may never go under this
if (1 - CloudAlpha * CloudMoonMin < CloudMoonFloor) throw ...
```

0.75 is not a number invented to fit today's values: it is the far end of the range **this document
already argued for** in §3's honest note ("raising `CloudMoonMin` to 0.6 gives a 25 % dimming and
still leaves the moon 75 % transmitted").

| | before | now |
|---|---|---|
| structural bound `CloudAlpha × CloudMoonMin` | 0.147 | **0.217** |
| ⇒ transmitted floor, all t, all viewpoints | 85.3 % | **78.3 %** (clears the 75 % gate by 3.3 points) |
| measured worst single pixel on the disc | 94.66 % | **83.39 %** |
| measured worst disc **mean** | 98.04 % | **90.24 %** |

![moon transmittance across one full drift cycle](debug/renders/forest-clouds/cloud_moon_occlusion.png)

§3's own honest note said "a dimming of 5 % is about at the edge of visible. If the user comes back
wanting to actually *see* a wisp cross the moon…". He came back. A wisp now takes the moon down by
up to **17 %** at its worst pixel and the moon is still plainly the moon.

### Cost — what changed and what did not

**The shader did not change.** Same 71 fragment instructions, same 2 texture fetches, same 0
branches, same mesh, same draw call, same bundle bytes. Three float uniforms moved and the fragment
has no branch on any of them, so requirement 5 is untouched *by construction* on the shader side.

**One thing does cost more, and it is the blend.** More of the shell survives the shader's own
half-an-8-bit-step `clip()`, so more fragments reach the frame-buffer read-modify-write:

| view | shell rasterises (unchanged) | survives `clip()` → blend, before → now |
|---|---|---|
| level at the horizon, 90° (looking at the board) | 40.1 % | 28.3 % → **38.0 %** |
| seated, 74°, toward the moon | 54.5 % | 30.3 % → **47.2 %** |
| open sky, 75° | 79.1 % | 28.1 % → **52.7 %** |
| straight up, 90° (worst case) | 100 % | 27.7 % → **49.4 %** |

The **first** column is what the fragment program is billed for and it is a property of the frustum
alone — it did not move by a pixel. The second is blend bandwidth: at worst 49.4 % of 20.05 Mpx per
frame at 90 Hz, i.e. 9.9 Mpx/frame of RMW against 5.6 before. That is the honest price of the
change and it is the only one; I still cannot convert it to milliseconds without a GPU timer.

### One number got worse and it is not obviously nothing

The stereo probe (§6) reads mean `|L − R|` at **1.818 × 10⁻³** with clouds against **1.100 × 10⁻³**
on the shipped sky alone — a ratio of **1.65×**, up from 1.12×. That is expected and probably
benign: a dome at 45 m has real parallax at 63 mm, the layer is now much more opaque, and a more
opaque layer necessarily makes more of the frame differ between the eyes. It is *smooth* parallax on
a *smooth* gradient, which fuses; it is not a screen-keyed pattern, and the constant-buffer proof in
§6 is unchanged — the fragment still binds no eye state and still does not read `SV_POSITION`. But
1.65× is a real rise on a number this project watches, and it belongs on the hardware-test list
rather than in a footnote.

### Reproducing §10

```sh
# bake, then grep the two lines this section's numbers come from
xvfb-run -a /home/claw/unity-2021.3.5/Editor/Unity -batchmode -nographics \
  -projectPath unity/GloomhavenVR.Assets -buildTarget Win64 \
  -executeMethod GloomhavenVR.EnvironmentsBuilder.BuildAll -logFile env-build.log -quit
grep -aE 'CLOUD NOISE|CLOUD/MOON GUARANTEE' env-build.log

# the renders and the measurements (WITHOUT -nographics, no -quit)
CLOUDS_PREVIEW_OUT=$PWD/render/clouds xvfb-run -a /home/claw/unity-2021.3.5/Editor/Unity \
  -batchmode -projectPath unity/GloomhavenVR.Assets -buildTarget Win64 \
  -executeMethod GloomhavenVR.CloudsPreview.RenderAll -logFile cloud-preview.log
grep -aE 'CloudPreview\] (LOOP|STEREO|MOON|NEVER|SHELL|FILL|CELLAR|moon disc)' cloud-preview.log
```

The plot is regenerated from the new TSV with §8's script, with the floor argument changed from
`0.853` to `0.783`.
