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

An earlier complete run passed production 459 assertions and 22 independently compiled
causal controls. The final pass-state/video additions are recorded below after their
27-variant run. Fourteen read-only source gates passed. The integrated Core/environment/
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
