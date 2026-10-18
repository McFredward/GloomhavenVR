# Native Steam Link v2 eye divergence

## Hardware report and current evidence

After Build697 and the persistent public dual receiver3261e8d, the maintainer
reports different apparent scale/depth between the two eyes, and passthrough
present in only one eye. This rejects697's new format as a usable headset result.
The earlier850 producer/168 native GPU checks were not an end-to-end stream test.

Fresh main `Player.log` identifies697 and five preparation epochs. Every epoch
logs successful Left and Right4040-square UNorm sources with8xMSAA and v2 output.
No ordinary-image refusal is logged. These are first-success-per-eye receipts,
not a complete per-frame eye-pair trace. They do not certify ongoing eye parity.

Read-only Frame collection confirms the exact31960-byte dual candidate, ready
source, six pipeline replacements from six attempts, no definition-history
failure and a healthy unchanged service invocation with no restarts. The patch's
CPU status does not record the GPU's per-fragment v1/v2/original decision. Those
facts exclude an old/missing receiver as the explanation; they do not prove that
both eyes recognized every transmitted frame.

`vrlink.txt` records1152x4608 C01Q decoder output, many FEC retries, and frame2671
accepted before its late metadata at02:06:11.94. This is real transport evidence,
not proof of a particular per-eye R1 chart, quantizer or corruption of a guard.
The raw4040-square eye render target is not the decoded stream's pixel density.
No new Frame modification, capture, diagnostic installation or restart is made
during this analysis. Existing files are retained privately in the receiver repo
under `build/private-eye-divergence-698-20261018/`.

## Reproduced failure mechanism and limit

V2 checks twelve32-square grey codes near the top-left corner, all within
two8-bit codes. If a fragment rejects them, its exact original fallback displays
the still-packed image. One accepted eye extracts the2048-square scene from the
3168-square carrier; one refused eye retains the packed carrier. That creates
different scale (3168/2048 =1.546875) and removes camera blending in the refused
eye. It matches the report without requiring different Unity eye resolutions.

An owned, bounded codec surrogate starts from4040-square atlases and downsamples
to a1152-square chart. At identical HEVC QP23, the v2 grey-background control
passes (maximum2-code guard error), while the v2 white-background control fails
cell4 (maximum6.552-code error). Both corresponding legacy v1 controls pass.
The full24-case surrogate varies v1/v2, three backgrounds, H264/HEVC and QP23/32.
It demonstrates that the new guard is fragile under lossy transport, not that
the real Steam Link encoder used this QP, exact chart or failed cell. Existing
logs cannot identify the actual failing eye/guard value. Do not widen tolerance,
reuse a previous mask, or declare the hardware trigger proven from this result.

The measured guard vectors were subsequently executed by the exact shipping
31960-byte dual fragment against two R1 charts in one1152x4608 analytic source.
The passing v2 case outputs scene sample0.700000/coverage0.5; its failing partner
outputs0.809373/coverage1, bit-identical to the original fallback. Both v1 cases
remain0.700000/0.5. Four focused GPU cases pass40 reproduction checks with zero
Vulkan validation errors. An earlier retained capture supplies the R1 chart
parameters for this surrogate; they are not measurements of the current run.
This is a known-bug reproduction, not a passing delivery of v2.

## Why the previous checks missed this

- The producer fixture uses one2048-square source, direct Left calls,3168/3408
  carriers and4xMSAA. It does not execute paired XR callbacks or the actual codec.
- The native fixture backpaints perfect float guards into a selected R1 chart.
  Even its quarter-chart cases do not exercise peripheral video downsampling.
- The owned NV12/H264 supplement is lossless. It does not test the new small,
  closely spaced format cells under lossy encoding or native foveation.
- A format rejection was a passing exact-original fallback control. No coupled
  stereo-quality assertion rejected one eye taking that path for a valid stream.

More assertions over these unchanged assumptions could not close this gap.
Keep source, synthetic codec, native shader and real headset evidence distinct.

## Bounded containment

Build698 selects the retained legacy v1 packer again. It preserves the immutable
v2 profile, shader and tests for investigation, without advertising that format
as a validated production improvement. The current public dual receiver accepts
v1; no Frame update/restart is necessary for this source-side containment.
No window tags, alpha threshold, second camera, new mask queue, transport setting
or extra user option is added. The original soft-edge issue remains open.

Focused source/paired rendering evidence and final actual build/resource
receipts belong beside the main hardware inputs in debug/steamlink-698-composed.
This does not repeat the full suite or establish a new headset outcome. Reuse
unchanged697 bank/native evidence and696 movie/UI/wire evidence honestly.

The bounded new producer control passes128 checks through the unchanged actual
shipping bank in Unity2021.3.5/Intel Vulkan. It makes exactly two independent
4040-square Left/Right source calls with actual8xMSAA, validates24 format cells,
eight RGB probes and two coverage probes, and retains the left output unchanged
after drawing the right. Maximum RGB error is0; maximum coverage error0.000559.
It is a direct paired-call proof, not an XR callback or real-codec proof. The
source profile check also executes a v2-selection mutant and requires refusal.
The final source-comment clarification changes no executed instruction/constant.
Source/result/cleanup evidence stays in main debug under
`steamlink-eye-divergence-698-producer/run-ns4e8__g/`.
