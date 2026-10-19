# Real-game contribution and coverage audit

> Current direction, 2026-10-17: continue with real-game contribution/coverage.
> The earlier synthetic PC test source and its VR setting have been removed at
> the maintainer's request. Its historical evidence remains valid within its
> original scope; none of the proposals below is activated by that old setting.

Audit of main `35870108a`, ModBuild 682, using the current owned shader sources,
existing complete native material metadata, earlier bounded runtime histograms,
and the independently tested four-family prototype. No game asset, renderer,
shader, configuration, Frame file or Steam sender is changed. No GPU test is
repeated by this audit. The maintainer's subsequent synthetic streaming-MR,
intermediate-alpha and menu-OFF confirmation establishes the synthetic receiver;
it does not establish the game's alpha semantics.

## Recommended path

Keep the existing scene render and obtain two quantities from that same eye:
`E_rgb`, the scene's already composited contribution including emission, and
linear coverage `A`. The final operation is `E_rgb + (1-A)*camera_rgb`. Do not
multiply E by A again, or divide by A: additive emission can have E>0 at A=0.
The current synthetic straight-RGB receiver therefore needs a separately named
contribution convention before accepting this producer.

Use Unity-owned shader/material variants for understood transparent families.
Preserve RGB equations, ordering, stencil, clip, depth, MPBs and original
callbacks. A new native D3D11 hook is not needed for the owned families and is
not an existing small integration point. Do not enable a lower-lighting quality
setting solely to acquire its alpha 1 shader: that would silently change RGB.

For native opaque/cutout objects, a much smaller alternative to rewriting every
lit shader is now being independently tested: at `AfterForwardOpaque`, reset
only alpha, then stamp 1 where the **actual existing eye depth attachment** is
nearer than the far plane. Two fullscreen alpha-only draws are not extra scene
renders. Preserve the original opaque-prefix alpha until that boundary if its
RGB depends on it. This is a proposal pending the separate real-camera/depth/MSAA
proof, not an implemented production fallback.

The first strictly bounded real-image scope can be an existing **opaque game/map
screen panel**: preserve the base RT's RGB in an owned final quad, emit alpha 1
inside its real silhouette, and use already understood opaque board/hand/map
geometry. Internal UI alpha is then irrelevant to that opaque panel. Transparent
split glass, arbitrary native VFX, all 3D-map content and full scenario coverage
are not implied. That is a useful first real-game image integration; the full
3D scene additionally needs depth-stamp and transparent-pass admission.

## Evidence and its limits

- [Native catalog](../../tools/material-audit/catalog.json) and
  [individual material index](../../tools/material-audit/material-index.jsonl):
  3,286 serialized sources, 3,255 bundles, 9,197 Material objects, 760 Shader
  objects, 156 material families, zero object-read errors. All 9,142 non-null
  shader pointers resolve;55 null pointers remain explicit exclusions.
  `shaderResolutionComplete=false` is intentional. This is installed-content
  coverage, not runtime reachability or pass semantics.
- [Catalog scope and source proof](../performance/MATERIAL-CATALOG.md): only
  seven populated native families have reviewed world-simplification equations;
  their 1,044 offline candidates are not a coverage certificate. The other 149
  populated families remain separately unreviewed. Native lighting, vertex
  animation, clip and stripped variants must not be inferred from a family name.
- The current supplied main `LogOutput.log` has a **Build 680** banner. Its six
  bounded `Perf SCENE` histograms show actual presence in menu/map/scenario
  phases, not a complete current 682 per-draw trace. A map sample includes
  BoardLit, EnvRoom/Cutout, Overlay, EnvParticleAlpha/Add, EnvFlame, EnvGlow,
  MapUnlit and native `VFX/MapHatch_Shd`. Scenario samples include native
  `Amp_Basic_Foliage`, WallFade, N_MRAO, Unseen, Prop, Char/Char_2Side,
  `Custom/MeshDecal`, Standard and TMP. The largest recorded foliage candidate
  count is1,564 slots; this is not an actual draw count. The histograms explicitly
  truncate their long tail. The separate Frame log is Build 661 and is not used
  as proof of current streaming behavior.
- The four-owned-family Unity prototype independently observes the original
  alpha `.25/.375` versus correct `.5/.75` for one/two half-opacity over layers.
  Its Gamma/Linear and1/2/4/8-sample runs cover one fully covered interior pixel;
  they do not prove MSAA edges, Windows D3D11, all UI clipping/stencil or native
  shader families. Details are in fork
  `docs/GAME-COVERAGE-PROTOTYPE-20261016.md` and its retained receipts.

The new private metadata receipts are in
`.planning/debug/steamlink-game-coverage-worker-20261017/`: all 156 native family
counts/alpha-related saved fields, all 34 owned shader source hashes/state-line
references, and the exact older log hash/banner/histograms/RT facts. No shader
body, game asset or new large extraction is copied there. The proposed admission
manifest is explicitly inactive/default-deny: it records per-family candidate
policies and the missing scope/backend/edge obligations, not runtime grants.

## Why the existing alpha is unreliable

The normal head restores opaque `Plugin.VoidColor`; PC MR explicitly makes its
key alpha 1 (`Core/MixedReality/MixedReality.cs:647`), while native standalone's
transparent clear is a different branch. The head inherits HDR permission and
owns forward/MSAA configuration (`Rig/VRRigDriver.HeadCamera.cs:422`). An RGBA
format only establishes storage. It says nothing about the value accumulated by
its draws, intermediate cameras, masks, postprocessing or resolves.

For source opacity a and accumulated A, over needs `A'=a+(1-a)*A`.
Shared `SrcAlpha/OneMinusSrcAlpha` produces `a*a+(1-a)*A`. Even drawing onto
opaque A1 can reduce it below1. Additive passes can write A1 across an otherwise
empty billboard; opaque shaders can return texture alpha unrelated to coverage.
A transparent clear alone fixes none of these cases.

Saved material properties can be stale, unused baggage. For example, the native
catalog's `UI/Default` material contains `_SrcBlend=One,_DstBlend=Zero`, whereas
its known UI program is premultiplied. `SimpleParticleAlphaDFade` likewise has
stale Standard fields alongside separate `SrcMode/DstMode`. A property existing
or a material queue value does not establish which state the compiled pass uses.
Adding `_SrcBlendAlpha` to a Material without a corresponding shader property
cannot rewrite its fixed pass. Changing the RGB source factor to One would alter
straight-alpha RGB, not merely repair alpha.

## Owned families: precise candidate policies

There are34 owned `.shader` sources. The table is a source audit and conditional
repair plan, not34 newly executed pixel proofs. Runtime blend/queue settings and
which shader is actually bound remain part of each admission.

| Actual families / source path below `Assets/Bundle` | Existing behavior and required coverage rule |
| --- | --- |
| `Environments/EnvParticleAlpha`, `HealthBars/ActorBarSprite` | Straight over; retain RGB SrcAlpha/OneMinusSrcAlpha and use separate alpha One/OneMinusSrcAlpha. These are among the existing four tested families. Preserve native clipping/masks. |
| `Environments/WaterVR` and `WaterVR.cginc` | Both tessellated/non-tessellated paths use dynamic RGB blend factors. The installed own-water driver explicitly selects straight over; fragment alpha is tint alpha times vertex alpha. The same alpha-only repair is plausible for that exact state. Native watery material names alone do not authorize it. Fallback Overlay needs the same review. |
| `Table/Overlay`, colored pass of `Table/HexDecalStable` | Dynamic straight over is common; repair only that observed state. Overlay also serves depth-only/color-disabled, additive and special depth-reset uses; classify each use. HexDecal's ColorMask0 pass must remain color/coverage-neutral. |
| `Table/BoardLit`, `TownServices/Shaders/TownNpc`, `Environments/EnvHaunt` | Multiple runtime modes. Opaque/cutout survivors return1 in their normal branches. Board/hand/card fades and TownNpc `_TownTransparent` can select straight over and require separate alpha factors. Never decide solely from the shader name/default queue. |
| `Environments/EnvParticleAdd`, EnvGlow, EnvFlame, EnvDrip | Existing additive RGB uses SrcAlpha/One. Preserve that energy multiplication and use alpha Zero/One to preserve prior coverage. Do not force emission RGB to0 at A0. Only EnvParticleAdd is in the prior four-family GPU proof. |
| `Environments/EnvBeam`, EnvShaft, EnvStarPoints | Additive One/One with alpha 1, including zero-RGB miss/padding paths. Alpha Zero/One avoids filling the billboard with false coverage while retaining its existing RGB. |
| `HealthBars/ActorBarUI`, ActorBarSDF, ActorBarMobileSDF; `Environments/EnvCloud` | Already use premultiplied One/OneMinusSrcAlpha. Suitable algebra when their output alpha and masks are retained. UI/TMP clips, softness, underlay and glow must be preserved; glow RGB need not increase A. The old fixture does not certify the complete TMP/stencil feature space. |
| `TownServices/Shaders/TownFlame`, TownCornea | Explicit existing core/emission separation. TownFlame's covering core emits premultiplied RGB+coverage; its glow and TownCornea emit RGB with A0. Preserve that material-defined policy, not a scene-brightness/key-color inference. No general colored glass/refraction claim follows. |
| `Environments/EnvPuddle` | Multiplicative pass DstColor/Zero and additive reflection pass One/One, both output alpha 1. They modify existing scene color, not an independent opaque silhouette. Preserve A for both passes under an explicit decal/reflection policy. Do not mistake this two-pass material for ordinary glass. |
| `Environments/EnvRoom`, EnvRoomCutout, EnvGround, EnvStars, EnvCritter, ScenarioSimpleEnvironment, ScenarioCheapTerrain, WorldSimpleMaterial; `Table/MapUnlit`, WindowMaterialise; `TownServices/Shaders/TownEye` | Source-defined surviving main fragments emit1. Preserve original discard/dissolve and geometric/sample coverage. ShadowCaster is not their main-view silhouette. Ordinary sky/background geometry must still be suppressed for MR; correct A1 does not make it a desired backdrop. |
| `Head/HeadUnlit` | Opaque RGB returns sampled/tint alpha. Force surviving output alpha 1 only in an explicitly opaque variant, as in the previous four-family proof. Blend-only changes cannot turn arbitrary source alpha into 1. |

## Native scene families and unresolved paths

| Real content | Established fact / current bound |
| --- | --- |
| N_MRAO HIGH/LOW, WallFade HIGH/LOW, Basic HIGH/LOW, Standard | Existing WorldSimpleMaterial routes 1/2/3/4/5/6/9 equations and stripped-keyword limits are separately documented. Native main passes have opaque/cutout semantics in admitted states; the owned replacement emits1. Retain source/MPB/global/fade ownership and existing quality choice. No permission to replace all native materials. |
| Characters, two-sided characters, LOW characters; props and foliage | Native catalog and old runtime histograms prove these are substantial real content. Their `_SrcBlend` baggage or opaque queue does not certify their complete fragment alpha, vertex animation, additional light/outline/death passes or actual depth writes. Existing world simplification deliberately excludes them. Same-eye actual-depth stamping could avoid rewriting their main opaque shader, subject to its separate proof and late/depthless exclusions. |
| `Amp_Basic_Unseen`, map hatch/panning/cloud families | Real map/scenario presence. Unseen carries native opaque and additional pass behavior; map symbols may be late command-buffer draws. They are not represented by the seven-family world allowlist. Do not substitute silhouette/name guesses or stamp a shader's data RT. |
| Native particles/VFX | Examples include1,000 ParticleTrailShd,880 DistortionParticles,780 KriptoFX Particle,605 SimpleParticleAlphaDFade and244 ParticleMasterUnlitAdd material objects. Counts are asset copies, not current draws. Some soft-particle/depth/occlusion equations are documented, but no universal alpha policy exists. Exact original pass state, output semantics and feedback must be established before an owned alpha-only derivative can be admitted. |
| Native distortion/refraction | `KriptoFX/RFX4/DistortionParticles` is documented as GrabPass distortion. Sampling an earlier scene RT's alpha or warping already composited RGB invalidates simple destination-alpha changes. Scalar camera coverage cannot reproduce arbitrary real-world refraction or colored transmission. Remain unsupported until their actual pass-specific behavior is modeled. |
| Native decals, trails, light/beam/multiply families | Their modes differ; some modulate an existing surface, some emit light, some cover, some use camera depth or occlusion maps. A render queue/name is not the needed proof. All unclassified pass variants stay outside the real-coverage certificate. |

A Unity-owned derivative of a **locally read original shader** could retain exact
compiled RGB programs and alter only reviewed serialized alpha blend state,
without writing the game's files or installing a D3D hook. That is an option,
not existing runtime infrastructure. It needs exact per-source/pass identity,
owned output/cache/loading, original bytecode/dependency retention, mutation and
lifetime controls, and a bounded original-vs-derivative GPU proof. The retired
ShaderOcclusionPatcher demonstrates the state/program distinction; its in-place
`patch` path is explicitly forbidden for current installs and is not proposed.
Alpha-only state edits still cannot force arbitrary opaque PS alpha 1.

## UI, map modes and offscreen composition

The 3D map is not just three owned NPC bodies: it contains native map meshes,
late icons/particles, owned scenery, cards/board/hands and original UI modules.
`FlatScreenStereo.3.Map.cs` replaces known map quadrants with owned MapUnlit, but
creates map icons using native Unlit/Transparent or Sprites/Default. Its
`AfterForwardAlpha` command buffer explicitly clears **depth only** before icon
draws. A late stamp cannot assume that original depth survived.

The 2D map/menu path is a different composition. `FlatScreen.2.CameraStack.cs`
creates `_uiRt` with transparent clear and a Sprites/Default/UI fallback glass
quad; the base RT is opaque black. The older680 log actually reports both as
ARGB32/R8G8B8A8_UNorm at 1280x720, base depth 24 and UI depth 32, aa 1. Current source
uses RenderTextureFormat.Default rather than an unconditional alpha-format
promise; exact runtime formats must be observed/refused if unsuitable.

Original non-immersive NPC windows and converted board canvases retain original
UI/TMP/custom materials. ActorBarDepthShaders selects four owned derivatives for
health bars only; it is not a global UI coverage fix. A transparent UI RT can
already contain wrongly accumulated alpha before a final quad samples it.
Correcting the head's last blend cannot recover those lost semantics. Conversely,
a deliberately opaque final panel can emit A1 without claiming its internal RT
alpha is coverage. If a transparent intermediate already contains premultiplied
E, its final consumer must retain E directly and accumulate A with premultiplied
over. A straight-alpha Overlay consumer would multiply E by A again; changing
only its alpha factors would leave that RGB error intact. Conversely an actual
straight texture still needs its original source-alpha RGB multiplication. The
current glass chooses Sprites/Default or UI/Default: verify the selected native
program/texture convention rather than inferring a compatible composition from
those names or a transparent clear. Preserve original UI clipping/stencil/
ColorMask0 masks and font glow. No new opaque backing is authorized inside the play area; the user's
existing UI-only backing rule still applies.

## Depth-stamp and feedback admission requirements

1. Use the exact head eye color+depth attachments and sample layout. Generic
   `_CameraDepthTexture` is not enough: the owned world shader explicitly notes
   native opaque fallback/LOW shadow casters differ from main-view wall/alpha
   clipping (`WorldSimpleMaterial.shader:333`). A new silhouette rerender would
   reintroduce that mismatch.
2. Explicitly handle actual reversed-Z, near/far values, MSAA boundary samples,
   AlphaToMask/alpha-to-coverage, surviving cutouts, stencil and same-target alpha
   resolve. Far-plane surfaces cannot be distinguished from empty depth by a
   strict far test. They require exclusion/other evidence.
3. Depthless color draws, overwritten/cleared depth, late opaque/command-buffer
   draws and manually issued Graphics.DrawMesh routes need explicit handling.
   Reset+stamp at one event is not a complete generation of future draws.
4. Preserve the original opaque RGB prefix and certify transparent suffixes.
   RGB blend factors using DstAlpha/OneMinusDstAlpha/SrcAlphaSaturate, framebuffer
   fetch, GrabPass/intermediate alpha sampling and unknown image effects can
   change RGB when A changes. Separate alpha factors are safe only after these
   dependencies are excluded or modeled for the admitted scope.
5. Data textures are not coverage: `_TilesOcclusionMap.a` encodes projected depth.
   Never apply scene-alpha fixes to that texture, shadow/depth captures, TMP/font
   masks, native card artwork or UI stencil setup merely because they use RGBA.
6. PPv2 and volumetric fog are disabled by CompatModule, reducing known image
   effects. This does not certify other mods, native offscreen passes, deferred
   UI/map cameras or final blits. Inspect actual RenderTexture format/color-space
   and intermediate read/write chains.

## Next concrete gates

Retain the existing four-family proof. Add only the independently owned depth/
edge controls now in progress, then a tightly scoped UI composition proof with
actual UI/TMP/mask materials and the real transparent glass path. Establish a
bounded current transparent/late-draw inventory outside eye callbacks. Unknown
materials/pass feedback must refuse the real-coverage profile; merely disabling
all currently unknown effects is a visible compromise requiring explicit scope,
not a silently successful general solution.

After an admitted scope exists, take E and A from the same actual MultiPass eye
source/resolve at PayloadOutput's admitted OnRenderImage point, pack them into
one immutable/fenced image with the separately declared contribution profile,
and retain ordinary/menu-OFF output. No CPU pixel readback, per-frame full-scene
scan, extra camera scene render or native sender modification is required by
that architecture. Current-R1 same-image sampling can preserve native eye routing;
a separate physical-eye CPU uniform join is not intrinsically necessary. It
does not remove same-source/eye/resolve/lifetime and color-transfer obligations.
Lower-resolution/tiled coverage must continue global mask rows through internal
halos; clamping each tile independently would alter bilinear filtering. Codec
and mask-resolution edge compromises remain separate from render correctness.
