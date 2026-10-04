# Build 620: restore the approved wall transition

The latest PC capture in `.planning/debug/frame620/inputs/LogOutput.log` and
`Player.log` identifies ModBuild 619. The maintainer reports a new square-patch
wall OUT/IN animation and says switching the Standalone profile does not change
it. This capture has no `DRAW DELIVERY` pixel/material samples; the visible
report and source changes are separate evidence.

## Cause and change

Build 619 introduced 64 point-filtered binary 64×64 solid/held maps for the
verified HIGH/toggle-native branches. Coarse screen-space cells are an intended
consequence of that delivery, but the resulting appearance was not approved.
Graphics profiles did not control this path, so selecting another profile
could not restore the previous animation.

Build 620 removes the bank, its route discriminators, one-time upload stage and
teardown state. Every wall route returns to the pre-619 bilinear Perlin map and
continuous cutoff sweep. Walls retain `Lerp(-0.15, 1, fade)`; native/swapped
mounted props retain their distinct `Lerp(-0.05, 1, fade)`. Fade clocks, local
and remote state transitions, original materials, held authored cutoffs,
foundation bands, and conservative mixed/LOW/themed admission remain unchanged.
Only the two original textures remain: the continuous noise map and the small
constant held map.

The source-proven `_EnableOcclusionMap=1` MPB delivery remains. Original HIGH and
N_MRAO bytecode uses this binding; their native flat-camera command-buffer
producer may be suspended in VR. The historical aliases remain for other native
families. No replacement shader or new visual-style option is added.

A private comparison receipt normalizes comments/whitespace and compares
`Apply`, `EnsureTextures`, `DriveNativeProp` and `CollectWallFadeInfo` against
`c108555cd` (Build 618). All four match exactly, allowing only the two explicit
native map-enable writes in the delivery methods.

## Focused verification

- Environment graphics production: **626 assertions** on Unity 2021.3.5f1 / GL
  llvmpipe. Actual production Apply/EnsureTextures/DriveNativeProp execute.
  Original HIGH and toggle-native branch surrogates exercise wall/prop OUT/IN,
  held and solid endpoints, authored cutoffs, native foundation, material identity,
  shared bilinear noise and explicit native map enable. Existing LOW coverage
  proves multiple rendered intermediate frames, including after a stalled frame.
- Six causal controls cover missing wall/prop native enable, replacing the cutoff
  sweep, using point-filtered square texels, a stalled frame finishing the ramp,
  and replacing intermediate noise with the binary held map. One expectation-only
  resume accepts the earlier existing LOW pixel failure produced by the cutoff
  mutant; the original failed attempt is retained. Passing cases were not repeated.
- Wall read facts: **19,504 assertions and 28 causal controls**. Only assertions
  and two controls for the deleted rank-bank discriminator are removed; the
  original mixed-material, native LOW/themed admission and ownership guards remain.
- Strict Release: zero errors/warnings. Partial static order, diagnostic writes,
  and the 655-key / 209-patch / 4,790-token source surface were checked.

Small private source hashes, exact commands/logs, comparison receipts and pixel
curves/images are retained under `.planning/debug/frame620/walls/`. This worker
coverage is partial; the primary agent runs the complete integrated gate.

## Hardware boundary

The GL HIGH fixture uses the exact documented native clip equation with an
explicit valid simplex-noise sample; it does not execute original Windows
shader bytecode, native lighting/artwork or a headset. That equation may retain
large areas until the endpoint for some samples. The receipt therefore proves
restoration of the historical inputs, not universal smooth HIGH pixels. The
previous Build 619 rank-mask pixel test established its chosen pattern, not
approval of that pattern. The maintainer's PC test rejects it and supersedes
that design. Recheck the restored OUT/IN appearance on PC and Frame.
