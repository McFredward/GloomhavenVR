# Native world material quality runtime

The October 6 Frame633 capture has fully loaded three-room windows around 98–103 ms,
with measured mod scopes around 61–68 ms and terrain around 38 ms. CPU substitution
remains the largest measured mod cost; those scopes do not establish a native/GPU
time split. This adjustable material stage reduces native shader work and replaces only
owned material references on actual native renderers; it creates no geometry,
proxy renderer, static batch or altered visibility/controller/animation clock.
Original materials, textures, meshes, colliders and property blocks stay intact.

The common setting has stages 0 original, 1 simple lighting and 2 textured shading.
Stage 0 restores this owner's native references; separately configured terrain,
floor/wall and environment switches retain their independent effects.
The root owns config/UI/defaults and shader/bundle integration. HIGH/LOW native
families have independent source-derived UV, alpha and wall equations; numerical
route assignments are 1/2 N_MRAO,3/4 WallFade,5/6 Basic,9 Standard and10 legacy
Diffuse. Prop/character/foliage/water/particle/video families stay unsupported.
Emission, vertex animation, moss, Fresnel and unsupported native combinations
retain their original shader. An audited family alone is no world-scope proof.
The admission table uses the intersection of jointly compiled effect keywords
across all native shader objects sharing a name, not their declared-schema union.
Native Standard is admitted only in opaque mode 0 with its proven non-alpha program.
Actual named native pass flags and HIGH/Legacy fallback ShadowCaster plus LOW
CUSTOM_SHADOW_PASS are read live. A disabled native pass retains its native shader.

Native procedural scenario generation or actual MapChoreographer worldMap/cityMap
object references establish world provenance. An additional native décor producer
can register its actual root. Actor/UI/interactive/animated/unknown scripted
subtrees remain native even within these roots. Current component inventories,
parent chains, slot identities and renderer visibility are read before rendering.
Discovery is bounded and follows scene/settings entry and native producer events;
no complete scene inventory runs in a steady frame.

Private per-original variants refresh once per synchronous material-read pass.
Native MPBs remain on the actual renderer and their original subslots. Only changed
slot identities allocate/write an array; an unchanged frame does neither. Foreign
slot replacements survive conditional restoration. Original arrays are restored
synchronously before native writers/content cloning, Off and teardown. Rare
Off/teardown inventory also heals an unregistered clone which borrowed a private
variant; it does not edit prefab assets or native source materials.

The actual final Camera.FireOnPreCull Harmony postfix runs after all ordinary
pre-cull callbacks and before native culling. Final refresh therefore sees late
native uniform/keyword/component/MPB changes. Known existing terrain/environment
leases may own native source masks; foreign forceRenderingOff, disabled or inactive
sources remain native. Refusal notifications revoke an earlier substitute even
when its original source array never contained a world variant.

The root integrates source-change notifications and canonical material composition
with terrain and existing environment batches. Mutable eligibility is not retained
across eyes. Read-pass and notification state deduplicate work only. Debug rows
report requested/effective source slots and refusal causes, never completed GPU
draws or an inferred headset FPS gain.

Implementation checkpoints and focused engine/causal evidence are recorded below
as they complete. Real Frame appearance, all scenario environments, town/map
coverage, Windows shader execution and multiplayer remain hardware acceptance.

The independent asset bank is prepared through ConfigureAssetPreparation even
when terrain/mesh-bank settings are disabled. Missing/unavailable shader assets
retain original references. ConfigureBeforeVariantDisposal releases both active
and queued terrain/chunk/instance consumers before any private variant is destroyed.
A consumer cleanup exception retains the referenced private materials, restores
known native bindings, reports the failure and stops optional material adoption.
Current MeshFilter and mesh identities are read again at the final boundary;
replacement, missing geometry and deliberately empty material arrays notify earlier
consumers instead of retaining stale substitutes. Native tile required
ProceduralStyle/ApparanceEntity generators are explicitly admitted without changing
their callbacks. Experimental AnimateStyle, held props and unknown scripts stay native.

## Focused evidence

Worker checkpoints b758e07c0, b090acb6a, c5d8ed62a, c60a926b8 and 5a718a897
separate implementation changes from later fixture proof. Strict production builds
use the actual shipped Unity/game references and treat every warning as an error.
The real Unity2021.3.5 GL fixture executes actual native renderers, private material
copying, renderer/subslot MPBs, real Instantiate and Camera.Render with the unchanged
production Camera.FireOnPreCull Harmony postfix. Native controller/config/resolver
types and delivery-pixel shaders are explicit surrogate boundaries. The shader lane
separately proves production fragment/caster equations against native program evidence.

The production fixture covers six shader routes against all 64 joint effect-keyword
combinations, Standard opaque state/typed texture MPBs, native tile producer scope,
actual map field provenance, UI/actor exclusion, between-eye uniform/keyword/material
and component replacement, late listener pixel writes, clone restoration, borrowed
variant Off repair, source masks, proxy-only refusal, factory consumer teardown,
missing assets, VR teardown and failure retention. At 64 sources / 128 slots with two
shared originals, executed material copies are 2 per camera and settled array writes
are 0. These are operation counts, not a headset frame-time claim.

The final run passed production 466 assertions and 26 independently compiled causal
controls, including live FORWARD, fallback ShadowCaster and LOW CUSTOM_SHADOW_PASS
disable/re-enable, plus per-slot video texture fallback. Every control failed its
specific runtime assertion; no compilation failure was accepted. Strict Release and
Debug each passed with zero warnings/errors. Fourteen read-only source gates passed
again after the final runtime changes. The integrated Core/environment/
terrain bridges additionally have 16 read-only binding contracts and 8 in-memory source
controls; these are wiring evidence, not execution of the original game controllers.

All attempts retain source snapshots, compiled assemblies, manifests, output and
Unity logs under .planning/debug/world-material-*. The initial strict fixture compile
failure, native MPB field-initializer failure, missing surrogate keyword tuple,
required-component surrogate setup failure and destroyed fixture proxy callback
failure remain archived as failures. No compile failure is counted as a causal pass.
Unity Library/Temp caches are removed after each owned invocation; dependencies and
raw evidence are retained. The full integration gate, build packaging and hardware
acceptance belong to the primary agent.

The final worker proof is .planning/debug/world-material-final-controls/run-hlcdvd_g
(27 cases, unchanged source-hash manifest), with integrated source bindings from the
parent candidate. Final source-gate results are in world-material-final-source-gates/
results.json; strict build logs are world-material-final-release.log and
world-material-final-debug.log. Actual delivery pixels ran on GL llvmpipe; production
shader pixels, actual game scene/controller execution, Frame timing and multiplayer
are deliberately separate acceptance domains.

## Frame636 native coordinator repair

The supplied Frame636 screenshots retain detailed stone/wood textures in the pale
areas; this is not evidence of a missing texture. The fully loaded log has requested
world mode2 but zero native-world owner candidates, while the earlier terrain factory
can still use private variants for its first64 admitted sources per camera. Its
frustum/priority selection changes with the view. That permits one original wall to
alternate between native lighting and the lighter private shader despite unchanged
material-quality settings. Shader ambient presentation and terrain preparation cost
are separate integration changes owned by the primary/shader lanes.

Read-only native evidence proves why global adoption failed. Every one of the15
procedural and114 editor map roots has the exact six-component native tuple
ApparanceEntity/ProceduralMapTile/ProceduralStyle/RoomVisibilityTracker/
ProceduralMapConfig/ApparanceMap. The authored ProcGen Maps root is level8 pathID2:
ProceduralScenario/ProceduralStyle/ProceduralPlacementNotifierHandler/
LightShadowsModifierController. The latter two root coordinators and the data-only
map/config components were unknown Behaviour vetoes. A generic surrogate scenario
without these required components could not detect that production refusal.

The repair admits only these four exact coordinator classes; subclasses and all other
unknown behaviours remain excluded. Original code confirms data references, placement
completion bookkeeping and registered-light shadow policy, with no renderer vertex
animation. Native callbacks and light state remain authoritative. No global scene-name
permission or arbitrary Behaviour exemption is added. The authored ProcGen Maps root
already supplies native ProceduralScenario ancestry, including in the additive scene.
Actor/interactive children, late unknown scripts, reparenting, animated styles, held
props, source mesh/slot/pass changes and foreign source masks retain their fresh
per-camera checks. All supported native sources can now retain their chosen shading
independently of a geometry substitute's camera limit or frustum membership.

ConfigureAmbientWeight supplies a live0..1 shader weight through
_GHVRWorldAmbientWeight, with default1 and finite/clamped input. Native tint/texture
data is not rewritten. The primary owns its adjustable percent setting and the shader
lane owns native SH lighting; this runtime fixture verifies binding/lifecycle only.
Renderer-wide MPBs are fetched once per source in the synchronous slot loop; each
subslot remains separately read. A64-source/two-slot fixture proves64 wide-block reads
and128 independent slot-block reads, replacing128 repeated wide reads. Original
material copies remain2 and settled arrays remain unwritten. This is bounded operation
evidence, not a headset timing claim. Debug FactoryVariantRefreshes additionally counts
the actual factory refreshes that happen before the final owner pass resets its local
coverage counters; it does not represent visible GPU draws.

Focused validation is source-bound under .planning/debug/frame636-runtime. Native
scope reparsing passes839 assertions and4 causal contract controls against all129
map roots plus the addressed ProcGen root, exact object bytes, script references and
reviewed original class/load source hashes. The stripped scene's MonoBehaviour header
is decoded only for its script reference; custom trailing payload is retained by its
complete raw hash. Controller execution remains an explicit surrogate boundary.

Production Unity2021 passes559 assertions, including cold native-tuple discovery,
actor/interactive/unknown child refusal, exact-type subclass refusal, late reparent,
ambient changes, actual additive unload/reseed and the MPB operation counts. Strict
Release passes with zero warnings/errors. The first focused38-variant attempt passed
production557 assertions and32 causal controls, but five controls hit an unintended
fixture assertion: one new earlier ambient check changed ordering and four mutations
accidentally granted permission to the fixture's unknown script. Those harness issues
were repaired without changing production bytes. Production and the five affected
controls passed again. The37-control coverage combines those32 inherited passes with
five affected reruns; it is not a fresh complete38-variant or149-suite run. All failed
attempt outputs/manifests/assemblies are preserved. The integrated binding checker
adds an ambient config contract/control only when the primary's new CoreModule binding
is present; this worker cannot certify a config it does not own. Actual Frame pixels,
room-wide timing and multiplayer remain hardware acceptance.
