# Original scenario card resource preparation

This implements the card-art closure identified in the Build617 Frame capture,
without changing native card creation, focus, disclosure, overlays or gameplay.
The integration build number and final complete-gate evidence belong in STATE.md.
Source-proven relocation of cold jobs is not a measured headset improvement.

## Native dependencies and ownership

`ConsumeElement` owns serialized private `elementSprites` and
`highlightElementSprites`; `InfuseElement` owns private `elementSprites`.
Their original Init/selection paths assign these arrays. They are separate from
`UIInfoTools.darkConfig`, which the older fixture represented successfully while
the actual ConsumeDark dependency stayed cold in hardware.

During the existing scenario spinner, preparation now reads the two exact native
`CreateLayout` asset references: `misc_gui/ConsumeButton` and `misc_gui/InfuseElement`
in `gui`. The native asset manager returns original prefab references, without
instantiation. Their inactive typed components and already-loaded original
ConsumeElement/InfuseElement owners are collected once per type. Each subsequent
tick reads one borrowed owner's sprite fields; sprite identity deduplicates art.
No native Awake, Start, Init, Show, focus, hand synthesis or Image assignment runs.

Native `CreateLayout.ProcessAreaEffect` also obtains Grey, Red and Dot from
`UIInfoTools.AreaEffectSpriteAtlas`, previously outside the collector. That original
atlas is now visited during loading. `GetSprites` returns temporary clones, so each
clone warms the existing shared heavy atlas/readback/region cache and then disposes
only its temporary source/replacement metadata. Pending owned clones are released
on reset, cancellation and fault completion. Original Images, borrowed sprites and
local/remote live replacements are never disposed by this job. Ordinary native
`GetSprite` clones subsequently reuse those heavy caches; their inexpensive
per-source wrapper may still be newly created, so not every SpriteMipMiss counter
is expected to disappear.

All work keeps the existing one expensive sprite/backing job per tick, 2,048-resource
bound, same-frame admission, finite shared CPU/VRAM budgets and lazy native fallback.
FaceMipBake off creates no mip bake, including for temporary atlas clones. Class art
and element/area art precede unrelated shared UI chrome. Original typed discovery
includes inactive templates; it does not traverse arbitrary sprite assets or run
continually during gameplay.

Other preparation owners can enqueue already-authored original sprites or native
`ReferenceToSprite` references through guarded `IncludeOriginalSprite` and
`IncludeOriginalReference`. These borrow the same deduplicated pin owner; null and
calls outside a running preparation pass are inert. The bridge never owns native
AssetReference operation handles. Stat preparation may use its existing panel-mip
policy independently of the card-mip setting.

## Validation and its limits

The production preparation, mip/pin cache and factory files execute in actual Unity
2021.3.5 frames. The fixture now uses the actual serialized private array shapes on
inactive owners and two lazily provided original prefab references. It additionally
imports and packs a real native SpriteAtlas; no fake atlas or mip cache supplies the
result. Production passes **1,277 runtime assertions**, with four class skins, five
inactive element owners, six private-array sprites, three packed area sprites,
21 backing reservations and 35 actual GPU pixel comparisons. GUID requests include
subsequent delayed, missing and cancellation exercises, not only initial preparation.

The affected suite contains production plus **24 causal negative controls**. These
cover missing arrays, active-only discovery, omitted exact prefab/infuse references,
native initialization, bridge bounds, atlas cold work, clone/metadata retention,
the mip toggle, existing class/card pool and pin ownership, cancellation and timeouts.
Strict Release compiles against the real game with zero errors/warnings.

The initial full affected-suite attempt exposed fixture issues: deliberately leaked
atlas clones contaminated later variants, and a metadata cardinality assertion
obscured the earlier missing-element control. The runner now retires each case's
new native sprites while preserving pre-existing imported atlas art. Metadata checks
reject foreign temporary keys rather than incorrectly requiring all art to be ready
at that intermediate assertion. The eight affected failures were resumed with
production and the leak control; unrelated successful controls were not repeated.
Original failed receipts remain under `.planning/debug/frame618-implementation/cards/`,
alongside final production and bounded-resume results, source hashes and actual native
source exports. The primary integrator still owns the complete final local gate.

The 118–122 ms fan peaks also include native construction/adoption work. The previous
factory already had all 29 party backings prepared in hardware; adding another
special-purpose pool would not be justified by this evidence. This change removes
demonstrated missing cold image dependencies from ordinary interaction, but does not
claim that the whole fan hitch, every unseen portrait or every future native widget
is eliminated. Headset pixels, real D3D/Turnip performance and multiplayer timing
still need hardware observation. Concealment remains remote-only and follows the
existing phase rules; all 3D-map surfaces stay public.
