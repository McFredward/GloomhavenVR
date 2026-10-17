# Same-eye opaque-depth coverage runtime component

`Core/SteamLink/SceneEyeCoverage.cs` promotes the independently tested depth stamp
into a reusable component, with its actual shader at
`unity/Assets/Shaders/SteamLinkSceneCoverage.shader`. This checkpoint adds no
existing camera hook, option, shader-bank load, inventory entry or native sender
change. The integrator owns that explicit admission/enable lifecycle.

## API and ownership

Construct `SceneEyeCoverage(Shader)` cold. `Arm(Camera, ulong enableEpoch)` first
detaches any previous camera, validates the new admission, and attaches one
owned `AfterForwardOpaque` command buffer. `Disarm()` removes it immediately;
`Dispose()` additionally releases its commands and defers destruction of its
owned material through normal Unity lifetime. `Ready`, `Reason`, `ArmedCamera`,
`EnableEpoch` and `IsArmedFor(camera, epoch)` are bounded, allocation-free reads.
A destroyed camera is never Ready. Zero epochs and foreign cameras/epochs do
not satisfy `IsArmedFor`. No global callback subscription or scene scan exists.
The explicit owner must disarm before changing targets, path or clear policy.

Initial admission is Built-in Forward, Gamma, HDR disabled and SolidColor clear.
An explicit RT must already exist with depth >=16, alpha-capable
R8G8B8A8_UNorm/B8G8R8A8_UNorm, no sRGB/dynamic scaling, 2D single slice and
1/2/4/8 samples; bindTextureMS is refused. A null target means the caller's
already-admitted actual head CameraTarget, not an invented temporary RT or a
claim that every XR attachment is known. The component changes no camera clear,
color, HDR, path, target, MSAA, stencil, depth mode or global graphics setting.

Both commands inherit the exact current camera color/depth/MSAA attachments:

1. Fullscreen `RESET`: ColorMask A, no blend, depth test Always, alpha0.
2. Fullscreen `STAMP`: the same alpha-only state, logical ShaderLab Greater,
   alpha1, using a raw far-depth vertex (0 under UNITY_REVERSED_Z, otherwise1).

Unity maps the logical comparison to the backend depth convention; reversing
Greater again would be incorrect. Neither pass writes RGB/depth/stencil or uses
AlphaToMask. No depth texture, silhouette rerender, extra camera, Blit target
rebind, readback, per-eye material update, allocation, scan or logging is added.
The commands execute in every admitted eye's existing scene pass. The caller's
OnRenderImage hook must independently establish exact head, enable epoch,
MultiPass actual eye, original same-eye source/resolve and absence of a nested
foreign callback before passing its source to SceneEyeOutput. `Ready` certifies
only this attachment/lifecycle, not later transparent or game-wide coverage.

## Actual production-component GPU evidence

Private evidence root:
`.planning/debug/steamlink-eye-coverage-worker-20261017/`.
Final `run-o90mn6il` directly compiles the production C# file and shader, then
executes `Camera.Render` and reads the actual `OnRenderImage` source. Unity
2021.3.5f1 / Intel RPL-P / Vulkan / Gamma / R8G8B8A8_UNorm passes **24 cases /
69,849 checks**, including 15 lifecycle assertions and four deliberate depthless
exclusion controls, across actual 1/2/4/8 target and source sample counts.

Every RGB component across the entire 32x32 image has the identical float bit
pattern to an equivalent no-hook render. Opaque/cutout silhouettes,
AlphaToMask sample coverage and an original stencil-clipped surface plus a
stencil-reading late RGB suffix all retain their original appearance. Coverage
matches the independent original red silhouette with maximum error0, including
MSAA partial samples. The original alpha (.31 clear / unrelated material alpha)
fails that oracle. Disarm restores ordinary RGBA, another camera receives no
commands, an actually nested foreign render retains its original alpha, camera
replacement immediately detaches the old buffer, destruction invalidates Ready,
and disposed/zero-epoch/wrong-shader/HDR/clear/target routes refuse.

Two owned fixture setup failures remain retained: the first omitted
ExecuteInEditMode on its readback component, so no source arrived; the second
used numeric ColorMask7 (GBA) for its late RGB-only test surface instead of the
explicit Red|Green|Blue flags, unintentionally rewriting alpha. These were not
production shader failures. The final production sources are unchanged from
their early checkpoint. Editor compilation emitted nullable-context warnings
because the copied file is compiled outside the mod's nullable-enabled project;
no shader/C# errors occurred. This is not Windows D3D11, live XR or headset proof.
The older independent Gamma/Linear/non-reversed proofs retain their own scope;
they were not repeated or relabeled as this runtime-component run.

The runner freezes all source hashes through child reap, retains each result,
source and failure log, calls the shared completion-cleanup API, then removes
only its exact owned Unity project. Every run's project caches are gone. No
native game assets were extracted or changed. Run with:

```sh
python3 tests/steamlink-eye-coverage/run.py \
  --output .planning/debug/steamlink-eye-coverage-worker
```

## Prepared owned alpha-state patch (not applied)

`owned-transparent-alpha-state.patch` and its source-pinned JSON manifest are
retained in the private evidence root. `git apply --check` succeeds against the
exact current sources; **no existing shader is modified by this checkpoint**.
The candidate changes only twelve fixed blend states in eleven owned files:

| Source families | Original RGB retained | Separate alpha candidate |
| --- | --- | --- |
| EnvParticleAlpha, ActorBarSprite, HexDecalStable colored pass | SrcAlpha / OneMinusSrcAlpha | One / OneMinusSrcAlpha |
| EnvParticleAdd, EnvGlow, EnvFlame, EnvDrip | SrcAlpha / One | Zero / One |
| EnvBeam, EnvShaft, EnvStarPoints | One / One | Zero / One |
| EnvPuddle multiply pass | DstColor / Zero | Zero / One |
| EnvPuddle reflection pass | One / One | Zero / One |

The straight-over alpha recurrence becomes `a+(1-a)*A`; additive/reflection and
surface darkening preserve A while keeping their RGB contribution, including
emission at A0. HexDecalStable's actual current colored pass is fixed straight
over; its separate ColorMask0 prepass is untouched. EnvPuddle samples its own
surface textures and produces multiplicative/additive RGB, not scalar physical
glass transmission; this policy does not promise real-world refraction.
Source auditing is not a new pixel proof of all twelve source families, nor
permission to change shared/data-target or feedback consumers globally. Use
explicitly owned coverage variants/admission if a family has other consumers.
The prior four-family evidence remains limited to those families.

Property-controlled Overlay, BoardLit, TownNpc, EnvHaunt and both WaterVR passes
cannot safely receive one blanket fixed alpha rule. A cold per-use classifier
must inspect the actual shader pass, material/MPB state and consumer:

- Known straight-over use: preserve RGB factors; alpha One/OneMinusSrcAlpha.
- Known additive/emission or existing-surface multiply: preserve RGB; alpha Zero/One.
- Already-premultiplied over (e.g. EnvCloud/TMP/core TownFlame): retain its output
  RGB/alpha equations and One/OneMinusSrcAlpha. Do not multiply E by A again.
- Known opaque/cutout prefix: leave its original alpha until the depth stamp.
  Opaque late draws need explicit A1 semantics, not that earlier stamp.
- ColorMask0/stencil/data passes stay untouched. DstAlpha/SrcAlphaSaturate,
  alpha-sampling feedback, GrabPass/refraction, unknown state or consumers refuse.

The patch does not change these dynamic states, add material properties, replace
native shaders or silently classify a material from its name/queue.

## Required integration limits

The caller must separately admit or fix transparent/UI suffixes. This component
cannot detect an unclassified future native draw. Depthless visible color, far
plane geometry indistinguishable from clear, late opaque draws after the stamp,
cleared/overwritten depth, non-clear camera stacks, unknown sky/background or
command-buffer targets are outside its coverage certificate. The four actual
Depthless controls intentionally show opaque RGB with A0 rather than pretending
that path is correct. Late depth clearing does not undo the already written
prefix A, but any following color still needs its own valid coverage semantics.

Native distortion/GrabPass, shader RGB factors depending on destination alpha,
alpha-aware postprocessing and corrupted intermediate UI alpha can change RGB
when alpha changes; do not admit them merely because ColorMask A preserves RGB
at these two commands. Final UI panels that are intentionally opaque may use
A1; transparent panels need the actual premultiplied intermediate E/A convention.
Keep the user's UI-only backing rule; add no MR backing inside the play area.

Root integration must restore every camera/material state when disabled/menu,
retain ordinary fallback on any invalid eye/source/epoch, preserve normal
unmodded/multiplayer presentation, and pair E.rgb plus A from the same admitted
eye source in the frozen contribution carrier. No scene-alpha or all-game
certificate follows from the mere presence of an RGBA target or Ready flag.
