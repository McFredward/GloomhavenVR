# Frame 617 wall follow-up: the reported pop remains open

## Actual Build 616 evidence

Both new hardware logs identify ModBuild 616. This report uses Python
`str.splitlines()` line numbering for Player.log; physical LF line numbers from
`rg -n` differ by two after the embedded separators. Input hashes and the
bounded extracted observations are recorded in
`.planning/debug/frame617-review/walls/wall-log-evidence.json`.

The previous simpler-shader admission defect is absent from the new native
material reports. The six toggle-native materials now retain
`Amp_Basic_N_MRAO`, `_WallFade_On=1`, `_WALLFADE_ON_ON` and their authored
`_Cutoff=0.5`. `CV_Floor_Basic_M` is no longer reported as a
`GloomhavenVR/ScenarioSimpleEnvironment` material. This establishes that the
616 admission change reaches this material population; it does not establish
correct pixels for every wall or explain the remaining report.

The bounded visual clock also reaches the headset: Player.log contains
31 `ANIMATION CLOCK` summaries, with 3,875 clamped rendered-frame samples over
the whole run and maximum elapsed frame 5,312.15 ms (including loading).
The last summary reports 134 clamped samples and maximum 985.57 ms. These are
wall-clock observations, not attribution of the stall to the wall subsystem.
`LogOutput.log` filters out these Debug lines; inspect Player.log as well.

There are 118 `ANIMATION` delivery samples; 56 report one or more mid-dissolve
wall renderers. The final `STEP` summary counts 161 block installs, zero material
swaps, 150 block clears and zero swap removals. Neither a numeric intermediate
fade nor a property-block install proves that the native shader renders an
intermediate picture. The user's headset observation therefore takes precedence
over the old diagnostic wording that predicts “nothing pops.”

## Why the passing 616 test was insufficient

The 616 environment fixture executes the real wall `Apply`, `EnsureTextures`,
shared scalar ramp and bounded wall clock, but its GL fragment surrogate models
only the **LOW above-foundation** `clip(1-map.r-cutoff)` branch. It does not execute
the original Windows shader bytecode, HIGH noise/foundation/vignette terms,
native toggle variants, the full procedural scene or the Frame's D3D11 driver.

The actual first scenario fade ON (normalized Player.log line 5945; physical
LF 5943) identifies `Amp_Basic_WallFade+Amp_Basic_N_MRAO(toggle-native)` as HIGH,
with twelve renderer members and four toggle-native members. Original read-only
shader extraction independently preserves the native HIGH directional fragment
program metadata and bytecode (blob 216). These programs were not executed by
the GL test. Source comments document additional native HIGH terms involving
world-space simplex noise, foundation height, screen radius and distance; the
LOW surrogate omits all of them. A native HIGH delivery failure remains a
candidate, not a proven cause.

There is also an arithmetic limitation in the historical `STEP` explanation.
At this run's frame times, the 33.33 ms visual cap yields first fade
`1-exp(-(1/30)/0.12) = 0.242535`, rather than the 90 Hz example near 0.09.
The real cutoff expression yields about **+0.128915**, rather than a negative,
visually inert first sample. Actual out-edge reports repeatedly read 0.243.
Even the LOW surrogate therefore cannot guarantee an unchanged first picture
at the Frame's cadence. This supports examining the visible onset, but cannot
by itself explain an entire wall vanishing in one picture. Shrinking the scalar
step speculatively would not establish that the native material channel works.

## Next bounded investigation

Do not classify the remaining pop as an approved performance optimization and
do not claim that the previous patch fixed the hardware picture. No new shader
replacement, geometry suppression, native callback suppression or wall decision
change is justified by these logs.

The next wall-specific capture should cover both disappearance and return, with
the actual LOW, HIGH and toggle-native families separated. Sample a bounded
number of complete fade episodes at the existing render boundary: renderer
identity and native material/shader/keyword, enabled/forceRenderingOff state,
fade, actual per-renderer map, cutoff and integer gate immediately before the
camera draws. Attribute intervening material/enable/property-block replacement
instead of assuming that the LateUpdate write survives until the draw. Keep
this at Debug, bounded by episodes and samples, without a scene sweep, new
camera, routine Info stream or GPU readback tax on ordinary frames.

A same-build headset recording with the window of one wall transition is needed
to join those delivery samples to **visible** intermediate pictures. Where a
local native Windows render can be reproduced, execute the original compiled
shader and actual material gates; a passing simplified GL fragment is insufficient.
Until that evidence exists, the remaining native path or driver behavior is open.
The source must continue preserving original floor/figure/light/gameplay and
shared card/UI clocks; no unconfirmed visual-parity exception is introduced.

## Work performed and limits

This follow-up is an evidence/source audit, not a new wall fix. There is no wall
runtime, environment-budget, native asset or test mutation in this lane. The
original native shader extraction is approximately 8 MB and lives only in the
ignored review folder; game references remain read-only. No repeated Unity gate
was needed for this documentation-only lane. Remaining performance causes are
evaluated separately in the integrated Frame audit.
