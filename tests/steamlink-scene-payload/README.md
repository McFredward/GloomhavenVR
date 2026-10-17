# Owned same-eye scene contribution atlas experiment

This is an inert draft. It does not attach to the rig, change a setting, classify
game materials, infer framebuffer coverage, identify a native frame or authorize
passthrough. `ScenePayloadDraft` accepts already resolved, externally controlled
linear RGBAFloat contribution/coverage surfaces, and records one GPU packing pass.
Its independent reconstruction is an owned test oracle, not the native receiver.

```sh
python3 -B tests/steamlink-scene-payload/run.py --cpu-only
python3 -B tests/steamlink-scene-payload/run.py
python3 -B tests/steamlink-scene-payload/run.py --gpu
```

The second command compiles the new class against actual Unity2021.3.5/net472
references and the owned fixture against the same Unity modules. The optional
third command requires the real editor, Xvfb and the Intel Vulkan ICD. It runs
finite Gamma and Linear correctness cases. It is not a GPU timing benchmark;
`--color-space Gamma|Linear` can isolate an affected case without replaying the
unchanged other case. Coordinate GPU use with other workers first.

The fixture paints two asymmetric analytic E/A surfaces, including opaque,
overlapping partial coverage and additive emission over transparent/glass areas.
Its contribution alpha is an unrelated decoy; only the separate plane's G is A.
It then packs a full 2112x2752 atlas, with the 2048-square scene centered and a
1024-square mask split into four row tiles with 16-pixel global-neighbor halos.
It decodes into a 2048-square float target and checks an independent CPU oracle
at global edges and all internal tile boundaries. Wrong internal halo clamping
and double premultiplication are real alternative GPU shaders and must fail.
The ordinary/off control copies the original E surface, including its unrelated
alpha; it does not reuse a previous atlas. Shape/alias rejection and caller
active-RT/sRGB-state restoration are also checked.

The draft footer deliberately differs from the current native synthetic source
signature. It cannot be fed to that receiver. Contribution RGB must never be
multiplied by A; a production receiver/profile must be separately reviewed.

Readback exists only inside the test: a chosen texel center is sampled into an
owned 32-square float probe RT, then a complete ReadPixels/Apply/GetPixel reads
that probe. The first direct partial-window reads from a large RT crashed the
local Unity Vulkan process within ReadPixels. Source pins, precise stage logs
and failure receipts are retained; no shader or game-source defect is inferred
from that boundary. Full-probe readback passes the actual GPU controls. The
producer itself contains no readback, upload, scene render, logging or timer.

The caller retains both source surfaces and the destination until actual GPU
consumers finish. This class has no stereo scheduler or eye admission capability.
It cannot prove externally supplied surfaces are the same eye/cohort, or retract
an already submitted left eye after a failed right callback. A previous-complete
pair policy would add latency and is deliberately not implemented. Callback
integration must establish those separate facts and preserve unknown-eye/menu
fallback. An allocated atlas also cannot enlarge an independently sized carrier.

Local full-size evidence: Gamma and Linear each pass 930 assertions /186 sampled
points, including 42 seam points, two zero-coverage emission points and four
causal mutant rejections. Those sampled float values match the oracle; this is
not full-image bit identity. No MSAA edges, native P010 conversion, codec,
foveation, XR callback or Windows D3D11 execution is established.

The runner pins its inputs, stores bounded result/source/failure evidence, kills
and reaps failed children, and invokes the shared completion cleanup API. Only
its exact disposable Unity cache trees may use an additional cleanup fallback.
