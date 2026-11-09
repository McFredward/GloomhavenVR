# Build768 Results original asset audit

Base dev: `5dff1344c`, Build767. This worker owns the new Results-only original
texture catalog, its generator and read-only native-content review. Existing
registry/Results integration and real Unity consumer tests belong to the
integrator and Results worker. No game/reference file, shader or artwork is
changed by this lane; no complete gate or headset/network run is claimed.

## Repeated failure and why single-name admission was insufficient

The paired current logs both identify Build767. Visitor scroll input resolves
and reaches the host, but its next complete picture is refused at
`texture|noise_01|256|256|3|9`. The previous fixture traversed only four serialized
Image hierarchies (eight materials/fourteen texture dependencies). The native
`UIFX_MaterialFX_Control` creates material instances and replaces wave, sparks
and bloom materials from `EffectDataBase` during runtime initialization and
state changes. A small cold prefab bank was not a complete dependency census.

`noise_01` is resources.assets/301, 256x256 RGB24, nine mips, not CPU-readable.
Its 262,143 original pixel/mip bytes hash to
`c11de33bd1a6d2898327ac079d0ffcfb38d42556cd10dbb5550398dd9a69bae5`.
It is referenced by twenty-eight materials, including square/circle buttons in
usable, active, locked, mandatory, used and bloom states. Required GUI materials
include sharedassets2/9,10; sharedassets4/14,15; sharedassets6/11,12,13,17,25.

The native source audit reads all twenty-eight original loose serialized
`.assets`/scene files, 1,396 Texture2D objects and 339 materials without a read
failure. It is explicitly this native corpus, not a claim to inspect arbitrary
third-party/procedural assets or every addressable model bundle. All 339
material contracts reference 206 distinct Texture2D objects; formats are
Alpha8, RGB24, RGBA32, ARGB32, DXT1, DXT5 and BC6H. There are no Cubemap,
Texture3D or RenderTexture references in this material set. A shader-family
subset would contain only 119 GUI/UI/TMP materials and seventy-eight textures;
the catalog deliberately does not depend on that hand-selected family filter.

## Catalog boundary and collision policy

The generated `ScenarioResultsOriginalTextures` contains 1,375 exact descriptor
keys from all 1,380 descriptor groups. It is an explicit per-registry Results
opt-in; the ordinary registry retains its previous behavior. It ships only key
strings, no source texture pixels, and does not change the wire layout.

Offline generation hashes all 531,175,536 original pixel/mip bytes, all native
source files and immutable sampling/color-space properties. Multiple originals
in a descriptor group qualify only if their original raw content and sampling
fingerprints are identical. Five groups remain refused:

| Descriptor | Reason |
| --- | --- |
| Black_Backdrop,1920x1080,DXT5,one mip | Two different original payloads |
| decal_mask,1024x1024,DXT5,eleven mips | Three different original payloads |
| Font Texture,0x0,RGBA32,one mip | Thirteen deferred mutable legacy font atlases |
| Font Texture,128x256,Alpha8,one mip | Two mutable atlases, despite identical initial content |
| Font Texture,64x128,Alpha8,one mip | One mutable legacy atlas |

The actual descriptor is name, width, height, format and mip count. Catalog
membership trusts an audited native source identity; it **does not compare live
GPU contents** or prove that an arbitrary other mod has not forged that identity.
The native serialized Texture2D schema has no `imageContentsHash` field, and all
reported texture is not CPU-readable. No runtime image-hash/readback evidence
is available in these logs. Runtime readback, image transmission or a new render
path is neither added nor implied. Generated/unknown/different descriptors and
conflicting original data must not gain a broad name-only exemption.

The generated file is 72,116 source bytes. Its deterministic generator reads
read-only game assets and checks native grouping, rejecting empty streams and
mutable legacy font textures. Developer regeneration/check commands:

```sh
python scripts/generate-results-native-texture-catalog.py --report <evidence.json>
python scripts/generate-results-native-texture-catalog.py --check --report <evidence.json>
```

These commands require UnityPy and local game references. The C# header records
the complete raw/sampling/source-file corpus fingerprint; no timestamp enters
its deterministic output. A changed original corpus requires deliberate review.

## Excluded dependencies were reviewed, not silently deferred

A separate audit scans 19,136 native image-like components (including Image and
RawImage) and all 2,206
native sprites with zero parse errors. The Results hierarchies in scenes6/7
contain 205 Image and 177 TMP-text components, no RawImage and no legacy Text.
TMP font-specific atlas aliases are registered before material capture; mutable
legacy Font Texture groups therefore are not current Results text dependencies.

The three decal textures belong only to Ooze_Green_Decal_7/8/9_Mat under
KriptoFX/BFX/BFX_Decal. They have no native GUI/material or Results-image use.
Of 206 native material textures, 187 qualify; the nineteen deliberate refusals
are sixteen mutable legacy font objects and those three world decals. A test
must account for these contracts explicitly rather than inventing pixels for
zero-sized fonts or pretending that all 339 materials qualify alike.

Black_Backdrop needs stronger handling: both Results scene roots reference
**sprite904 / texture62 in sharedassets1.assets**. The exact Image components
are level6/6529 and level7/6640 at:

`Overlay Canvas/Game Elements/Game scenario screen/MainGUILevel/GameGUILevel/New Scenario Results Panel`

The other original sprite1120 uses texture282. The two sprites have identical
name, full1920x1080 rect and texture rect, zero border/offset, and centered pivot,
so the old descriptor-based SpriteKey can alias them silently despite different
pixels. No1120 reference exists anywhere inside either Results hierarchy.
No native material texture slot references either backdrop, and Results has no
RawImage requiring that ambiguous texture descriptor. Native Results managers,
UIFX helpers, ExtendedButton, HeaderHighlightText and GUIAnimator do not replace
this root sprite or assign/load a black-backdrop texture at runtime.

The integrator's narrow immutable native-root **sprite alias**, before discovery
and capture, is therefore required in addition to the static catalog. There is
no reason to admit the ambiguous texture descriptor or give it an arbitrary
canonical texture. Review of the integrated helper finds no source blocker:
CollectRoots always starts with the native Results Window; mip-baked sprites
are unwrapped; first registration retains the original; later same native-slot
wrappers map to the live canonical alias; retired canonical objects can be
replaced; Reset clears the receipt with the registry. This relies on audited
native source ownership/field stability, not a live content-hash comparison.

## Exact-source decision proof and retained limits

The real generated production class compiles unchanged in a CLR proof whose
independent C# policy groups every original row by raw and sampling fingerprints.
It checks 4,183 assertions over all 1,396 originals/1,380 groups, reproduces every
complete descriptor, admits1,375 and refusesfive, including the content-identical
but mutable legacy atlas. Wrong names, case, dimensions, format and mip count
remain refused. Removing only noise_01 and broadly admitting descriptors each
compile successfully and fail the intended causal marker. All three variants
run in8.460s with zero compile warnings/errors. This is catalog decision proof,
not native engine getters, material binding, shaders, network timing or headset
appearance. The separate Results lane must qualify those consumers.

The initial raw-data audit failed because UnityPy treated an empty deferred
font stream as a directory. The corrected audit records/excludes zero-dimensional
font streams without manufacturing pixels. That failed producer and receipt are
retained separately from successful checks. An exploratory full-sprite JSON
serialization also exposed binary vertex fields; the retained exporter records
those fields' byte counts/hashes instead of dumping bytes or dropping geometry.
No production change followed either audit-format failure.

Canonical completion cleanup removed six generated bin/obj directories,
1,409,727 bytes, with no skipped paths. Read-only audits create no project cache.
All original source/profile hashes, raw-group decisions, collision/image owners,
original sprite metadata/PNG witnesses, exact executed CLR sources/DLLs, failures,
results and cleanup receipts are retained in the verified main-checkout archive
`.planning/validation/build768/assets-lane`. This lane does not claim hardware
acceptance or a green complete local gate.
