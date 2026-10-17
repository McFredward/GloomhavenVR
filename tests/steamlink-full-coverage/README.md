# Full-resolution independent scene coverage

This new focused Unity2021.3.5/Vulkan fixture compares the actual shipping
`GloomhavenVR/SteamLinkSceneFullAtlas` shader with the unchanged legacy C1 shader,
through production `SceneEyeOutput`. It uses ordinary scene quads and partial
transparent surfaces, with no window marker, plate restoration or alpha threshold.
The same source2048² RGBA contains contribution RGB and independent coverage.

```sh
python3 tests/steamlink-full-coverage/run.py --output /private/evidence
```

The default loads both shader assets from the **same current shipping bank** and
reuses that AssetBundle container. `--old-bank /private/historical.bundle`
optionally compares a retained original bank; `--production-root` selects a
separate implementation checkout. Neither option is required after integration.
`--loader-only` is one real raster case for the default same-bank path.

The profile JSON is copied, pinned and used to generate independent fixture
constants. Structural checks reject any overlap between outer halo rectangles,
contribution RGB or format cells, with a deliberately overlapping negative
control. Quarter-turn1 is explicitly inverted as
`atlas=origin+(sourceLocalY,2048-sourceX)`; the renderer and test decoder do not
infer its direction from an ambiguous rotation label.

The45raster cases cover:

- Steps from0 to.25/.5/.75/1, independent constant glass values0/.25/.5/.75/1,
  a two-axis opacity gradient, and positive additive contribution with A0.
- Exact3168² virtual-canvas output and the real3408² carrier size.
- All four strip orientations, global clamping, both sides of all row seams,
  the16pixel halos, and all12unchanged-profile format codes.
- A plain non-window silhouette, rotated thin geometry, actual4xMSAA and
  32subpixel moving-edge phases. Source and decoded alpha are measured rather
  than assuming that every pixel is opaque.

At exact3168output, halo checks include the outermost virtual texel center.
At3408, the last outer-edge pixel can bilinearly cross outside the defined halo
rectangle; those checks move one virtual texel inward so their entire sample
footprint remains defined. Ordinary decoder data points remain16pixels away
from that boundary. The first overstrict3408expectation and its failed result
are preserved privately; it was a fixture-boundary error, not a shader change.

Initial actual Intel RPL-P/Vulkan evidence: the complete45case matrix passed
850checks, including616halo and24format-code checks. Maximum resampled halo
error was0.462/255. At exact3168, the1.0step changed from2.306to0.800source pixels
for the10–90% edge, matching the original source. The corresponding full RGBA
and the full exact gradient image were byte-identical to their source images.
Across32moving/MSAA edges at3408, average width decreased2.807→1.907pixels
(32.1%); maximum50% silhouette position error decreased0.359→0.192pixels.
A thin oblique shape's center coverage improved0.878→0.965 against source1.
Partial glass, gradient values and additive A0contribution survived unchanged
at the measured interior points.

Different canvas packing changes the carrier sampling phase: at3408,
physical-edge RGB pixels are not globally byte-identical to the source or old
layout. Full-image saved-raster comparisons record this explicitly. This is a
resolution/sampling change, not a promise of identical physical-edge colors.
No native fragment, video decoder, XR eye or headset picture is executed here.
The independent native GPU lane covers its own decoder scope.

An optional small CPU-only supplement performs RGB→limited-range BT709 NV12→
lossless H264420→RGB, using the actual3408carrier capture:

```sh
python3 tests/steamlink-full-coverage/run.py --codec --output /private/evidence
```

It requires ffmpeg with libx264 and Pillow. Runtime range/matrix metadata are
specified for **both input and output**. This is a neutral owned conversion,
not SteamLink's actual codec/settings. Initial corrected result:4824numeric-G
points and12format centers differed by at most1code. An earlier missing raw
input color-space declaration caused a3–4code green bias; that failure and its
causal YUV-byte analysis remain preserved. No production color policy changed.

Every child is bounded and reaped before cleanup. The central retention API is
called, and the exact generated Unity project/rawNV12buffer are removed without
sweeping other work. Frozen source, bank/profile hashes, results, logs, encoded
supplement and actual raster PNGs remain. No old GPU suite is rerun by this test.
