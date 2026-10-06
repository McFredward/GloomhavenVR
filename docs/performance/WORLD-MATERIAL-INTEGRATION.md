# Whole-game material simplification

This lane follows the user's October 6 request to implement configurable material
compromises across the complete base game and DLC corpus. The audited lane is
integrated in `dev` build636 after the original enchantress overlay repair in635.
See [the integration review](FRAME-636-INTEGRATION.md) for final scope, focused
follow-up checks and hardware limits.

## Player-visible behavior

The same PC/Frame binary exposes `[Optimize] WorldMaterialQualityModeCount` live:

- **0:** restore this owner's native references; the existing individual floor/wall
  shading settings continue to apply.
- **1:** original color textures, tint and fade with SH ambient and one simple
  diffuse main light. Normal/MRAO, gloss, reflections, extra lighting passes and
  received shadow lighting are omitted.
- **2:** original textured color, tint, dimming, fade and fog without surface lighting.

Fresh Frame and Standalone profiles select **2**; ordinary PC profiles select **0**.
Saved values remain under the existing BepInEx Bind contract. The existing quality
profile action sets the stage explicitly; players can edit it independently.
Stages1/2 take precedence over the two older shading switches. Geometry/detail,
submission, FX and other graphics choices stay independently configurable.

This is a native-renderer material compromise. Original mesh geometry, collision,
transforms, enabled state, room reveal and controllers remain authoritative.
Existing configured coarse 3D geometry may still submit a private proxy, using the
same new material. No new flat board, hidden room or whole-scene proxy layer exists.
Actors, held/interactive objects, cards, UI, water, video, particles, animated and
unknown effect families remain native. Unsupported states retain originals.

## Complete native inventory and conservative admission

The inventory covers **3,286 serialized sources**, including **all3,255 catalogued
bundles**, **9,197 materials**, **760 shader objects** and **156 material families**.
JoTL and Solo dependency closures are checked. All9,142 non-null shader PPtrs resolve
by source/file/path identity;55 explicitly null shader references are excluded.
177 malformed particle/VFX metadata values remain explicitly recorded.
**1,044 offline candidates** are metadata screening, not live scope or draw counts.

Production admits only audited native static world scope plus positively implemented
shader states. Equal names, property sets and pass counts do not identify stripped
program availability: the admitted keyword combinations are the **intersection**
across all native objects sharing a family. This conservative rule can retain a
full-bundle state that a stripped root copy cannot implement. Unknown states stay
native. The shader audit proves16 exact native objects/268 bounded Windows programs
and36 byte-identical core payload pairs. Rendered GL references reconstruct their
retained parameter equations; they do not execute the original Windows shader.

Native tile scope includes the required `ProceduralStyle` and `ApparanceEntity`
producers. Experimental `AnimateStyle` remains excluded and freshly checked.
Actual `MapChoreographer.worldMap/cityMap` references can register map scope;
material-family names alone cannot promote UI, actors or arbitrary scene roots.

See [the complete catalog](MATERIAL-CATALOG.md),
[the shader contract](WORLD-MATERIAL-SHADERS.md) and
[the material ownership lifecycle](WORLD-MATERIAL-RUNTIME.md).

## Source ownership and integration

Private variants replace only this owner's individual native material-slot references.
Native originals are immutable. Each synchronous pass refreshes a distinct original
once; stable arrays are neither allocated nor assigned every eye. Native per-renderer
and per-slot property blocks remain live. Eligibility is read again at each actual
camera invocation, including current parents, components, mesh/filter, material
references, keywords and unsupported effect state.

The existing native load, visibility, material-ready and Apparance clone hooks route
through `ScenarioEnvironmentBudget`. Restoration precedes native writes/cloning;
ready/placement callbacks enqueue bounded discovery. World source notifications
invalidate earlier terrain/chunk/instance consumers without recursively restoring
new bindings. Factory-only consumers are released **before** variant destruction.
Foreign current slots/masks are retained. Failure retains native continuation.

World uniforms commit at the actual `Camera.FireOnPreCull` Harmony postfix, after
all ordinary native camera callbacks and before culling; existing environment final
validation follows. Late writes can revoke an earlier terrain proxy in the same
frame. The independent environment bank is loaded even if old terrain/mesh choices
are off; a missing package retains original rendering with bounded context.

## Validation and hardware limits

Final combined receipts and compiled review are recorded in the source-bound handoff.
Focused worker evidence includes full native inventory hashes, real Unity renderer
and material lifecycle tests, rendered shader causal controls and the existing
terrain/environment bridge cases. An assertion count for source operations or owned
leases is not a GPU timing or number of visible draws.

The packaged Unity2021.3.5f1 Windows64 bank is **56,804,117 bytes**,
SHA256 `c012d139af3a9712e196d4f67f79a72109fbadf333388aa2d9c87ce42e76577d`.
It contains both environment shaders and the unchanged3,170 immutable streams.
Two sequential same-worktree packs are byte-identical; fresh-machine reproducibility
is not established. Both internal logs survive. The new shader has zero D3D11
warnings; the pre-existing CheapTerrain warning remains a separate historical path.
The main and town bundles are unchanged. Deliver the new bank beside the new assembly.

The audit discovered a legacy CheapTerrain LOW world-Y/object-Y discrepancy. It is
recorded separately and this lane does not certify or rewrite that historical path.
The new world shader uses the native source-proven equations.

For hardware, compare **0/1/2 after full loading**, with all three rooms open and
other settings fixed; also capture a different biome and JoTL/Solo content. Inspect
UV/color, wall fades, fog, shadows and complete room geometry, then repeat multiplayer.
Grep `worldMaterialMode`, `World material coverage`, `WorldMaterial.PreCull` and
`WorldMaterial.*` for requested/effective coverage, refusal reasons and CPU cost.
A stage change alone does not prove that a surface entered the supported scope.
Original game lighting/HMD pixels, net gameplay and Frame FPS remain hardware-open.
