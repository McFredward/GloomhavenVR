# Build631: native merchant card distance filtering

## Evidence and cause

The maintainer reports severe aliasing on immersive cabinet item cards at a
distance, while the close view is satisfactory. The supplied owner and observer
logs both identify Build627, not a new631 hardware result. The owner reaches the
existing384 MiB mip-cache ceiling and records107 refusal/budget lines, including
Jagged Sword, Long-Spear, Tower Shield, Boots of Striding and potion artwork. The
observer does not exhaust this cache in the supplied capture.

The catalog already rescans ItemCardUI and polls CardArtWatch. Repeating those
calls cannot repair permanently cached budget refusals. Separately, native town
observer Image playback resolves exact original sprites and used to assign those
mipless originals directly, unlike the existing scenario element/card filtering.
The original Iron Helmet and Jagged Sword bundles both contain512x497 RGBA32
textures with one mip level. Increasing eye resolution or mesh detail does not
address that missing texture minification data.

Read-only resources.assets inspection finds895 BattleOverlayCanvas sprite
members, all unrotated rect packing. Its555 untrimmed small members can use exact
regions; their theoretical combined mip cost is45.36 MiB. All895 regions would
cost72.15 MiB, compared with85.33 MiB for the whole atlas plus separate trimmed
copies. These are asset-census costs, not measured full-session memory savings.
Logs, input hashes, native geometry and decoded artwork remain under
`.planning/debug/card631/`.

## Implementation and parity

Small exact untrimmed regions now reuse the established full-readback/CPU-row-slice
path used by trimmed sprites. Already prepared whole-atlas copies are reused.
Oversized sources, unsupported packing, allocation failures and exhausted budgets
retain their safe original fallback; no new sub-rectangle GPU readback exists.
The384 MiB byte ceiling remains. The metadata runaway guard increases512 to2048
regions because the original UI atlas alone exceeds512 legitimate regions.

Mip zero retains source resolution. Trilinear mipmaps and existing anisotropic
filtering supply texture LOD as a card covers fewer eye pixels. Native pivot,
border, pixels-per-unit, physical layout and original asset identity remain.
There is no new mesh LOD, draw camera, setting, texture download or asset bundle.

The common card path filters both base and active Image override art, including
late override-only assignments. Unity's override getter falls back to the base;
an explicitly assigned override equal to the base must still be treated
independently. Reading the override after the base write handles both cases
without private-field reflection or inventing an override that masks later art.
Pool return restores exact original base/override artwork.

Town observers resolve the same original metadata and apply the common sampling
policy immediately. Registry/wire identities continue to normalize to originals;
no pixels or new record are transmitted. Cabinet, held, offered and returning
item images share this policy. Existing scenario card preparation, concealment,
animation, callback isolation and private-card naming rules remain. The existing
FaceMipBake presentation-quality switch keeps its established scope.

## Validation boundaries

Focused actual Unity2021.3.5/uGUI tests pass565 assertions, with five causal
controls. They include independent and equal-to-base overrides, later art,
original registry identity, more than512 cheap regions, the2048 guard and the
unchanged384 MiB fallback. Six perspective distance/tilt views per original item
are compared with an8x supersampled reference. Iron Helmet RMS falls4.398 to2.007;
Jagged Sword4.491 to2.248. Their close-view mean RGB differences are0.503 and0.419
on the0..255 scale, with no source-resolution reduction. Render images were
inspected alongside the numeric results. Existing actual packed-sprite scenario
preparation passes790 assertions, including35 pixel comparisons.

The focused observer proof links the actual production Image playback branch and
original registry. It is not a complete game prefab, multiplayer latency or
stereo-headset test; full native mirror coverage remains in its existing suites.
The final integrated gate and compiled scope are recorded in STATE.md and
`.planning/debug/card631/validation-ledger.json` after completion.

The next hardware check should compare cabinet card outlines/details close and
far, for both local and remote views, including category changes and held returns.
The cache remains bounded: arbitrary long-save content is not certified to fit
solely by these tests. A remaining budget warning would require new evidence;
it must never be hidden or interpreted as permission to corrupt artwork.
