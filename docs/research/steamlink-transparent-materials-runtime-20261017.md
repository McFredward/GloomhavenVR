# Head-only owned transparent coverage materials

`SceneTransparentMaterials` adds actual scoped material integration for fifteen
known mod shader families. It is not a native/game-wide alpha certificate and
changes no game material, original shader or original asset file. Root owns the
camera callback registration, scene shader-bank builder and final source hook.

## Source-bound derivative bank

`tests/steamlink-transparent-materials/variants.json` pins the exact source SHA,
original/derived names and counted textual changes for eleven fixed families
plus Overlay, BoardLit, TownNpc and both WaterVR passes. Every derivative is
named `<original shader>.SceneCoverage`. Root stages the original source and
sibling includes into a separate small bank; complete original fragment/vertex
bodies, pass order, clipping, depth, stencil and RGB factors are retained.

Fixed derivatives implement the previously prepared twelve-state patch:
straight over retains SrcAlpha/OneMinusSrcAlpha RGB and uses alpha
One/OneMinusSrcAlpha; additive/reflection/multiply passes retain their original
RGB and use alpha Zero/One. No premultiplication is added to their fragment code.
Additive E.rgb is intentionally nonzero at A0.

The four dynamic sources gain only hidden `_SceneCoverageSrcAlpha` and
`_SceneCoverageDstAlpha` properties and separate alpha blend references. After
copying current properties, the helper classifies actual `_SrcBlend/_DstBlend`
from those exact owned sources:

- 5/10 or1/10: alpha1/10; original RGB factors remain unchanged.
- 5/1 or1/1: alpha0/1, retaining emission energy without fake coverage.
- 1/0: unchanged alpha1/0 only for early (`renderQueue<=2500`) BoardLit FadeAlpha1
  or TownNpc's explicit opaque fragment branch. This does not certify late
  opaque geometry. Other opaque/depth-only uses can remain original and rely on
  the separately admitted depth prefix; they are not guessed from stale fields.
- Other modes, including destination-alpha RGB feedback and unclassified
  multiply states, remain the original material. Fixed EnvPuddle is the explicit
  reviewed existing-surface multiply policy; a generic Overlay multiply is not.

Unknown native shaders never receive clones. Missing/unsupported variants or
pass-count mismatch also leave original slots. MaterialPropertyBlocks retain
ordinary colors/textures untouched; a renderer-wide or slot block overriding
original/new blend-state properties causes conservative original-material use.
The helper does not write or replace any property block.

## Camera scopes and lifetime

Construct with `Func<string,Shader?> findVariant` and explicitly `BindHead(head)`
cold. The resolver returns loaded bundle Shader objects by original shader name;
`Shader.Find` is not reliable for this bank. `RefreshCold()` runs outside every
camera callback, scans active renderers, warms material clones and cached
renderer membership, and prunes dead/inactive/no-longer-referenced sources. A
source whose Shader changes retires/rebuilds its exact clone on that cold tick.
The caller's maintenance tick can run about once a second; this is not per-eye
scene discovery or a claim that newly spawned renderers were already admitted.

Feed **all** actual `Camera.onPreRender/onPostRender` calls through
`EnterHead(camera)` / `ExitHead(camera)`. The eight-entry stack admits only the
explicit head. Foreign nested rendering restores originals first; returning to
an outer head reapplies current source properties. Each source has one cached
clone; `CopyPropertiesFromMaterial` runs immediately before head entry/reentry,
then dynamic alpha factors are reset. Shared source materials are never edited.
Renderer lists preserve slot count/order and null slots; restore substitutes
only a currently recognized owned clone. A foreign replacement wins over stale
ownership, including slot-count changes. This is ordinary implicit Renderer
material binding, not a rewrite of explicit native command-buffer materials.

`DisarmImmediate()` restores immediately even inside an active head scope and
clears camera authority. `Dispose()` then destroys only owned clones, through
normal Unity deferred destruction. A bad callback order, stack overflow or
unexpected >64-slot shape restores known clones and faults the epoch until a
cold refresh/bind. Root must stop packing on `Ready=false` and use its ordinary
fallback. There is no helper timer, file IO, GPU readback, extra scene camera or
per-frame logging.

The routine admitted <=64-slot path reuses lists, arrays for exact lengths and
a cached dictionary value view; no managed allocation or scene scan is added.
Unity's public `GetSharedMaterials(List)` can allocate on an unexpected foreign
shape growth. That exceptional path may also allocate one exact restore array,
preserves foreign slots and closes the epoch; it is explicitly not an absolute
no-allocation promise for arbitrary future external mutations. No private Unity
engine delegates/hooks were introduced to hide this boundary.

## Actual shipping-bank execution

Private evidence:
`.planning/debug/steamlink-transparent-materials-worker-20261017/`.
The frozen shipping bank has SHA256
`b4aab0c2045d7140f37c43c1d86cf1a2de5d531e37e9b501a7aec2411fbcf38e`.
The fixture loads that actual Windows-targeted bank (17 shaders + AssetBundle),
not locally recompiled replacement variants. Unchanged original shader sources
are separately imported for comparison. Host execution is Unity2021.3.5f1,
Linux Intel RPL-P Vulkan, Gamma; it is not Windows D3D11 or XR execution.

`run-k26mvdku` passes **6 actual raster cases / 6,198 checks, including41
lifecycle checks**. Actual EnvParticleAlpha, EnvParticleAdd and dynamic Overlay
are rendered in one/two layers on 16x16 ARGBFloat targets. Every RGB float bit
matches the original shader render. Straight-over alpha changes from .25/.375
to .5/.75; additive alpha becomes0 while RGB energy remains unchanged. The same
Renderer in an actually nested foreign Camera retains complete original RGBA,
head borrowing resumes afterwards, and OFF retains the original pixels.

`run-d7ubs5ar` is a **lifecycle-only delta**,31 checks/22 lifecycle assertions;
the six passing raster cases were not repeated. It adds concrete source Shader
change/return, cold cache pruning, destruction and real borrowed-state reset
controls. It strengthens the earlier shape-growth fixture's reset to an actual
one-slot Renderer before testing disarm/disposal. The actual Mono allocation
counter is available: after128 warm entries,256 head enter/exit pairs allocate
**0 managed bytes**. Native internal allocation/performance is not inferred.
Mismatched callbacks, nested restoration, manual foreign writes, slot resizing,
state-MPB refusal, harmless color MPBs and unknown shader exclusion are covered.

Strict standalone compilation against the actual net472 reference assemblies
and UnityEngine.CoreModule with nullable enabled and warnings as errors passes
with0 warnings/errors. Its annotation-only follow-up makes the proven live
entry and Unity's legitimate null material slots explicit; shader/GPU behavior
is inherited, not a new full raster run. The final lifecycle run compiles that
exact final component.

Two pre-raster setup failures remain retained. Root's first shipping bank had
17 Shader objects but no AssetBundle object because the builtin AssetBundle
module was disabled; Unity refused loading it. Root fixed module declaration
and requires parsed metadata before publication. A subsequent fixture used
Shader.Find for loaded variants and got missing results; retaining the actual
LoadAllAssets Shader objects in the cold resolver fixes that fixture boundary.
Neither failure is a game/Frame pixel result. All child processes are reaped,
the shared cleanup API runs and only owned stopped Unity projects/cache files
are removed; small banks, source hashes, logs and failed attempts remain.

Run all new material controls, or only the bounded lifecycle delta:

```sh
python3 tests/steamlink-transparent-materials/run.py --output .planning/debug/materials
python3 tests/steamlink-transparent-materials/run.py --lifecycle-only \
  --output .planning/debug/materials-lifecycle
```

## Remaining real-image boundaries

This integrates known owned transparent Renderer slots into the real head path;
it does not fix native particles, native UI/TMP intermediates with already lost
alpha, GrabPass/distortion/refraction, unknown alpha-aware postprocessing,
shader RGB depending on destination alpha, or explicit late command-buffer
materials. A correct depth prefix plus these fifteen known families must not be
reported as all-game coverage. Dynamic/future shaders and newly discovered
renderers remain original until independently admitted. Other camera callbacks
must always be forwarded even if foreign or nested; registration cannot be
limited to the head alone. Root must restore all camera/MR state and stop the
carrier on disable/menu/epoch/head change or a helper fault.
