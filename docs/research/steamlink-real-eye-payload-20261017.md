# Real-eye Steam Link payload: producer design review

Status: offline proposal, not a frozen protocol or a live source backend. The
worker starts at main `35870108a` (Build682). Existing synthetic source/runtime,
configuration, camera ownership and the native receiver are unchanged.

The maintainer subsequently requested removal of the production diagnostic
pattern/setting. Root owns that removal. The historical source contracts below
describe the stated baseline only; this new draft does not depend on the old
pattern, its class or its runtime. Existing proof may remain offline/history.

The maintainer has reported a visual pass for the separate synthetic native presenter,
including partial alpha and menu recovery. That acceptance does not establish
game-wide scene coverage, a real-eye atlas, codec edge quality or motion latency.
This note identifies the bounded next producer experiment and its remaining
integration decisions. No Frame action is needed to run that experiment.

## Existing source contracts

`PayloadOutput.OnRenderImage` admits only the actual rig head, built-in pipeline,
XR MultiPass, both target eyes and a known current eye. Unknown callbacks retain
the ordinary source blit. It currently substitutes an already prepared synthetic
texture; it never interprets `source.a` as scene coverage.

`RegistrationTextures` prepares both synthetic eyes in Update before publishing
the pair. Its three-slot pool retires a complete pair behind an actual graphics
fence after the prior blits. A callback commit is not GPU completion. The old
688x548 registration canvas and its straight-RGB presenter-v2 footer remain
separate protocols, with no changed defaults or relabeled images.

The fork's `docs/GAME-COVERAGE-PROTOTYPE-20261016.md` proves only four explicitly
owned shader families and fully covered interior pixels. Its over passes correct
`a*a + A*(1-a)` to `a + A*(1-a)`, additive passes retain RGB while leaving coverage
unchanged, and opaque passes write coverage 1. MSAA edges, subsequent stencil,
soft clipping, alpha feedback, UI intermediates, original game materials and a
Windows D3D11 runtime are not thereby admitted.

The ordinary `OnRenderImage` source is therefore **not** an accepted coverage
producer. A new packer accepts only an externally supplied, explicitly attested
same-eye contribution/coverage RT cohort. That attestation is a fixture input
contract, not a public boolean that authorizes arbitrary game sources.

## Required composition and transfer contract

The payload carries scene contribution `E` and scalar coverage `A`:

```text
out.rgb = E.rgb * originalTint * originalAperture
out.a   = clamp(A, 0, 1) * originalAperture
display = out.rgb + (1 - out.a) * camera.rgb
```

`E` is the sum produced by the admitted original scene blend operations over a
transparent clear. It includes ordinary premultiplied contributions and additive
emission, including nonzero RGB at A=0. Do not multiply E by A, divide E by A,
infer A from RGB, or discard zero-alpha RGB. The current synthetic straight-RGB
receiver multiplies by coverage and is incompatible with this contract. A new
explicit source/receiver profile and separate format signature are mandatory.
Invalid/ordinary/menu images must select the exact original native expression.

The first owned experiment uses linear floating-point E/A surfaces with known
numeric values. A production profile must additionally freeze color transfer:
linear scene contribution, the precise atlas storage encoding, Unity sRGB writes,
video conversion and the receiver's output attachment conversion. A floating-point
producer test cannot certify that chain. In particular, alpha is linear numeric
coverage even when RGB is stored with an sRGB transfer. Arbitrary HDR, negative
contributions, colored transmission and refraction are outside an SDR scalar-A
profile. Tone mapping cannot silently be applied independently after E/A capture.

## Proposed layout, pending root and receiver review

Recommended draft: `scene-contribution-atlas-v1`, revision1, **2112x2752** logical
pixels per eye, top-down pixel rectangles:

| Region | Rectangle / meaning |
| --- | --- |
| Scene contribution | (32,352,2048,2048), center exactly (0.5,0.5) |
| Coverage | one continuous 1024x1024 field, divided into four 1024x256 row tiles |
| Tile0 outer / data | (0,32,1056,288) / (16,48,1024,256), source rows 0..255 |
| Tile1 outer / data | (1056,32,1056,288) / (1072,48,1024,256), rows 256..511 |
| Tile2 outer / data | (0,2432,1056,288) / (16,2448,1024,256), rows 512..767 |
| Tile3 outer / data | (1056,2432,1056,288) / (1072,2448,1024,256), rows 768..1023 |
| Footer | y 2720..2751; twelve new-profile neutral 32x32 signature cells |
| Scene edge padding | 32-pixel side margins and available adjacent rows; edge extension only |

The 16-pixel tile halo extends the **global** coverage field. At internal row
boundaries it contains adjacent global rows, rather than repeatedly clamping a
tile's last row. Only the four outer edges of the complete coverage field clamp.
This preserves an ordinary bilinear footprint across a tile boundary; copying
only each tile's edge would avoid foreign colors but change the filter there.
The packer downsamples A before splitting it into tiles. Averaging A is an
explicit silhouette/edge-quality compromise, not exact full-resolution coverage.

Use source texel-center coordinates throughout. For `q` in the source image,
`p = q * 1024 - 0.5`; choose `t = min(floor(max(p.y,0)/256),3)`, then sample the
corresponding tile at its data origin plus `(p.x,p.y-256*t)+0.5`. Its halo supplies
neighboring rows for bilinear sampling. Do not clamp to a tile's data rectangle
before filtering. Clamp the original image domain only at global edges. Native
receiver branches retain their own current R1 chart; no new CPU eye routing table
or fitted chart is required by this proposal.

The alternative **2048x2688** keeps a 2048x2048 scene at (0,320), but cannot fit
two 1024-wide coverage tiles plus horizontal halos. A coherent version reduces
coverage to 992x992: four 992x248 interiors, each surrounded by 16 pixels to make
1024x280 outer tiles. This loses 6.15% of coverage pixels versus 1024x1024. It is a
valid lower-bandwidth profile only if separately named and signed; it must not
advertise 1024x1024 coverage. Unpadded 1024-wide tiles are not recommended.

The recommended canvas contains 5,812,224 pixels, 5.58% more than 2048x2688. At one
RGBA8 atlas per eye it uses 22.17MiB; three stereo slots use 133.03MiB before source
RTs, depth, driver storage or staging. RGBAHalf doubles those atlas figures. The
callback records one bounded full-atlas packing pass and one output pass per eye;
this is real GPU bandwidth, not zero cost. For an illustrative full-atlas
RGBAHalf read + RGBA8 atlas write + atlas read + RGBA8 destination write,
traffic is about 20.9 GB/s at 90 Hz for both eyes,
before duplicated samples, resolves and the ordinary scene itself.
Do not call that an actual timing or a hardware bandwidth prediction.

Logical atlas size alone does not preserve 2048x2048 scene detail. The current
callback still blits to the ordinary eye destination. For a 2048x2048 carrier,
the recommended central panel occupies only about 1986x1524 carrier pixels;
the 2048x2688 alternative occupies 2048x1560. A larger or differently shaped actual
carrier is a separate camera/Steam submission decision, not a property of an
allocated atlas RT. This worker does not change that camera decision.

Keeping the scene center at(.5,.5) avoids moving the nominal foveation center.
It does not prove unchanged optical sampling: mask tiles lie near the image edge
and may receive fewer foveated video samples. Neither sixteen source pixels of
halo nor a clean local GPU image bounds video motion compensation, chroma420,
codec ringing, foveation footprints or quantization. Guard cells must be separate
from payload and have a new signature binding the complete geometry, RGB/A
conventions, sample component and tolerance. It recognizes format, not body
authentication or liveness. No production codes are frozen in this note.

## Same-eye capture, lifetime and latency

All textures/materials/meshes/command buffers and shader variants are prepared
outside eye callbacks. Update polls actual fences and reserves an entire free
stereo slot; it skips the whole cohort if no pair is free. The eye callback may
validate scalar identity/shape, record packing from its actual attested source,
record output, and close its borrow. It performs no readback, source upload,
allocation, shader compilation, log formatting, scene traversal or extra render.
Never retain Unity's temporary `OnRenderImage` source beyond that callback.

An attested capture names the actual eye, app scheduling cohort, source identity,
extent/format/transfer, resolve/sample count, matching E/A origin and generation.
Those are app facts, not native decoded video-frame IDs. Unknown/mismatched eye,
ordinary alpha, array/double-wide textures, unsupported format/transfer, MSAA
without a proven same-source resolve, resize, camera swap or a source/destination
alias rejects the new payload and preserves the ordinary source blit.

There is an important difference from synthetic preparation: real left/right
images arrive sequentially. Publishing only a fully completed pair means waiting
for both callbacks; using that pair at the next cohort introduces at least one
app-frame of image latency. That is undesirable after the maintainer reported
head-turn lag. Presenting the current first eye immediately avoids that delay,
but cannot retract it if the second callback later fails. A pool/fence does not
solve that causality problem. Do not claim whole-pair failover for an immediate
path. The first prototype therefore tests a packer only, without selecting a
runtime publication policy. Root must explicitly decide that policy before a
real-eye runtime is attached.

Off/menu closes admission synchronously before destruction; no new valid-format
atlas is emitted. Reused native commands see ordinary source images and select
their unchanged expression through the current-image format guard. That recovery
occurs when the ordinary image is consumed, not before an already queued image
can arrive. Destruction remains deferred after admission closure; retired slots
cannot be written again until their actual queued consumers' fence has passed.

## Bounded next experiment

After review, add only new `ScenePayload*` classes and owned fixtures. Keep the
existing runtime/config/flat-screen/test-manifest paths unchanged. A pure CPU
oracle checks geometry, all tile transitions, halo construction, top-down versus
Unity UV conversion, contribution at A=0 and signature/profile separation.

An owned Unity2021.3.5 GPU fixture paints realistic externally provided E/A RTs
using opaque/over/additive layers; it does not call these game-wide coverage.
Pack with a new owned material, decode back to a float RT, and compare to an
independent ordinary E/A oracle. Include all tile seams and global edges,
half-opacity overlap, additive-over-transparent/glass, asymmetric eye images,
both color spaces, deliberate double-premultiply/wrong-row mutants and Off/menu
fallback. ReadPixels is allowed only in the isolated fixture after GPU completion,
never in the producer. Compile the new class against actual net472 Unity APIs.
Use focused runs and preserve failures; do not rerun untouched old GPU suites.

The first experiment can establish pack/decode correctness and bounded recording
cost. It cannot admit unclassified game shaders, native codec/foveation quality,
the actual Windows headset picture, a native frame identity or a latency fix.

## Primary API references

- [Unity2021.3 Graphics.Blit](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/Graphics.Blit.html): shader-mediated copy/scaling, sRGB state, and same-source/destination hazards.
- [Graphics.CopyTexture](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/Graphics.CopyTexture.html): format/platform and region constraints; not a general scaled packer.
- [GraphicsFence](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/Rendering.GraphicsFence.html): actual GPU ordering/completion, distinct from CPU callback completion.

## First review evidence

The private draft-geometry oracle completed 50,466 assertions over both layouts,
including every mask x position at fractional samples around all three internal
row boundaries and all global edges. The maximum difference from independently
sampling the unpacked field is below 2.5e-13; this is CPU floating-point model
evidence only. Clamping each internal tile separately, rather than carrying
global neighbors into its halo, produces 9,216 detected wrong samples in the
1024-square layout. An independent zero-coverage additive control rejects double
premultiplication. No actual GPU or game source is exercised by these checks.

Source, draft geometry, results and completion cleanup receipt remain private in
`.planning/debug/steamlink-scene-payload-worker-20261017/`. The receiver review
agrees that global halo continuation and a new contribution-RGB protocol are
required. It separately notes already explicit semantics in selected owned
UI/TMP/ActorBarUI and town flame/cornea families; that is a starting inventory,
not permission to infer the remaining game shaders' coverage.

## Implemented owned GPU prototype

The reviewed next experiment is now implemented only in the new, unhooked
`ScenePayloadDraft` class and `tests/steamlink-scene-payload/`. It accepts exact
linear, single-sample 2048-square RGBAFloat E/A surfaces, uses the draft layout,
and reconstructs into a 2048-square target. It neither allocates eye RTs nor owns
a camera, pair scheduler or production coverage capability. Its private draft
footer is deliberately unrecognized by the existing native receiver. The class
is compiled against the actual Unity2021.3.5/net472 API with no warnings/errors.

Actual full-size Intel Vulkan Gamma and Linear runs each pass 930 assertions,
186 independently checked source coordinates,42 seam coordinates, two
zero-coverage emission coordinates and four real GPU mutant rejections. At those
points the measured float RGB/A error is 0; no full-image bit-identity, filtered
edge/MSAA, codec, native or Windows execution is claimed. A source-bound CPU
subset passes 251 assertions, including 126 seam samples and 54 detected wrong
internal-halo samples. Its independent broader draft review remains 50,466 checks.

Initial attempts retain four SIG11 failures. Precise final stage markers place
the native failure inside ReadPixels of a partial window of a large RT; all
owned RT creation, Paint/Pack/Decode and actual completion fence had succeeded.
This does not identify an underlying driver or Unity cause. The successful test
uses an explicit GPU probe of the chosen texel center into a complete 32-square
float RT and reads that complete RT, as in the working small coverage harness.
No production packing shader or source class was changed to accommodate the
readback failure. There is no readback in the producer.

Successful GPU evidence is `run-wb74251x`, with separate Gamma/Linear result and
stage logs. Actual net472 typing is retained in `run-bn5y6pwx`; its unrelated
initial fixture warning-as-error failure was fixed only in the fixture. Every
process was reaped before invoking shared completion cleanup; owned regenerated
project caches are removed while all source, result and failure evidence remains.
This is source/host-GPU preparation, not a new live-backend admission or a fix
for the maintainer's reported head-turn latency.
