# Native world material quality runtime

The October 6 fully loaded Frame capture remains dominated by native/render work
after the earlier presentation optimizations. This adjustable stage replaces only
owned material references on actual native renderers; it creates no geometry,
proxy renderer, static batch or altered visibility/controller/animation clock.
Original materials, textures, meshes, colliders and property blocks stay intact.

The common setting has stages0 original,1 simple lighting and2 textured shading.
The root owns config/UI/defaults and shader/bundle integration. HIGH/LOW native
families have independent source-derived UV, alpha and wall equations; numerical
route assignments are1/2 N_MRAO,3/4 WallFade,5/6 Basic,9 Standard and10 legacy
Diffuse. Prop/character/foliage/water/particle/video families stay unsupported.
Emission, vertex animation, moss, Fresnel and unsupported native combinations
retain their original shader. An audited family alone is no world-scope proof.

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
