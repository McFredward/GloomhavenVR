# Scene-mask spatial edge reconstruction

This focused Unity 2021.3.5 fixture compares the actual Windows shipping
`scene.bundle` against the previous owned atlas shader, renamed
`SceneWindowEdge/LinearControl`. It renders real `UI/Default` RawImages over the
same opaque `Overlay` plate state used by MR windows, through the production
`SceneEyeCoverage` and `SceneEyeOutput` components. The test control is an owned
source shader, not a replacement for a game shader or receiver.

```sh
python3 tests/steamlink-window-edges/run.py --output /private/evidence
```

The six physical-plate cases include subpixel translations, oblique/rotated
geometry and actual 4x MSAA. Reported 10–90% edge widths compare the same current
source under both producer kernels. Separate cases cover constant partial
opacities, a smooth spatial gradient at multiple fade amounts, byte-identical
RGB samples, and the actual `MrBackingMaterialise` erosion mesh with the shipped
coverage variant. Saved PNGs are source/mask alpha rasters, not headset images.

The new interpolation uses `f*f*(3-2*f)` between the unchanged four coverage
cells. It remaps spatial interpolation weights, never opacity itself. Cell
values, constant partial opacity and the [0,1] range remain unchanged. The kernel
has no extra texture reads, render targets or per-window scans. It cannot restore
information already lost in the 1024² mask reduction, the encoder, or the native
sampler. This is not a promise of a one-pixel headset edge or a codec/latency
measurement.

The frozen v1 signature preimage retains its historical wording “bilinear
reconstruction.” Its layout, numericGcoverage, contribution-RGB convention and
format guard remain the same; current source reconstruction is recorded by the
bank provenance and this fixture rather than changing the public receiver
format for a filter improvement.

The old `PanelSupersample` comments describe a historical SrcAlpha blend. The
exact official 2021.3.5 built-in shader instead uses premultiplied RGB and
`Blend One OneMinusSrcAlpha`; changing that blend was therefore rejected as an
unsupported cause. The standalone source inspection pins the official archive
`874a63954fc8df9e99d850c14bd372c3f527f3b2c5631d729845987f8d427c0e`
and UI/Default member
`e10f436278b514397e17482f7fc4e27e71c2b11d39485cf11a648198e2393b12`.

All inputs and the bank are frozen until the owned child exits. The runner uses
`cleanup_completed_run` after reaping, and removes only its private generated
Unity project. It preserves source copies, hashes, logs, results and captures.
This Linux/Vulkan raster proof does not establish Windows/D3D pixels, actual XR
callbacks, video compression, or the final Steam Frame picture.

Two optional offline comparisons are retained for the rejected alternatives:

```sh
python3 tests/steamlink-window-edges/run.py --subset comparison --output /private/nearest
python3 tests/steamlink-window-edges/run.py --subset unsharp --output /private/unsharp
```

Those modes do not alter the shipping bank. They compare full nearest spatial
interpolation, a nearest-only-for-exact-binary-neighborhood hybrid, and locally
clamped unsharp(k1/k2) against linear and the production kernel. Their output
includes50% silhouette positions under 32 real 4x MSAA subpixel poses. They are
measurements, not authorizations to ship those modes. In the initial Intel run,
nearest sharpened more but increased position error; the hybrid failed to help
oblique MSAA edges and introduced an extra switching step. Unsharp increased
motion error and tripled the source-alpha read count. These alternatives remain
explicitly test-only.

`--subset clipping` runs only the actual RectMask2D hard/soft clipping and fade
delta. `--subset edge-witness --expect-linear-rejection --bank /private/old.bundle`
accepts only the expected edge-width assertion failure from a previous linear
bank; an unrelated compiler/runtime failure does not count as a causal control.

Initial measured result (Intel RPL-P/Vulkan, 3408² carrier, 2048² source): the
six stable/oblique/MSAA plate cases averaged 3.282→3.101 source pixels for the
10–90% edge, 5.52% less. Constant partial opacity differed by 0 codes; smooth
gradient/fade differed by at most 1/255. This is a modest improvement, not a
complete fix for the reported feathering.32additional moving-edge poses showed
that nearest and unsharp narrowed edges more but increased source-relative
silhouette movement; neither became production code.

The materialisation bodies are externally compiled unchanged with Roslyn/net472
against the actual Unity 2021.3.5 API, because the Editor's C#8 frontend cannot
parse production file-scoped namespaces. Only a private friend-assembly attribute
is added for the fixture. Both source copies, compiler output and produced
assembly are retained. Two initial fixture compile failures are preserved in
private evidence; they establish no raster result.
