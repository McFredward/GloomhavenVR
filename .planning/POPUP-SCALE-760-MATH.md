# Build 760 physical-window normal review

Classification: C, because the correction is shared capture geometry. This
worker's focused proof qualifies the math; the primary agent owns the final
strict build, source gates and complete integrated checkpoint. No worker push,
release or unrelated native/window-placement change.

## Current hardware evidence

The November 5, 20:11 log identifies ModBuild 759. Its four original ESC
confirmation adoptions are at lines 5826, 6544, 6848 and 7135. The original
confirmation is revealed in each case and the Build 759 ownership exemption is
present; the old repeated blackout does not recur after that reveal.

| Opening | Live rig metric | Rounded host pixel metric | Historical axis cross magnitude |
|---|---:|---:|---:|
| 5826 | 1.17 | .001 | approximately 1e-6 |
| 6544 | 24.88 | .026 | approximately 6.76e-4 |
| 6848 | 8.92 | .009 | approximately 8.1e-5 |
| 7135 | 2.77 | .003 | approximately 9e-6 |

These are world units per tracked meter and per native pixel, respectively;
neither is a native choice or networking permission. The exact first host metric
in its hit-rect diagnostic is about .0012. Unit basis axes transformed by that
host have lengths .0012 each, so their cross has length about 1.44e-6.

The actual installed Unity 2021.3.5 `Vector3.Normalize` returns zero unless its
input magnitude is greater than 1e-5. `MenuDepthDisplay.RenderHead` passed that
raw scaled-axis cross to both filtered eye resolves and the final head carrier.
At the small host metrics it therefore supplied a zero plane normal even though
the window itself remained valid, enabled and correctly placed.

For native flat UI with no sampled depth write, `MenuDepthDisplay.shader` must
reconstruct the glass depth. Its `dot(plane-eye, normal) / dot(ray, normal)` becomes
0/0 with this invalid normal. The nonfinite depth explains the scale-sensitive
failure mechanism; the observed D3D11 headset picture remains hardware evidence,
not something the worker's graphics-free math proof reproduces. The external
grab bar is outside that captured window photograph and can remain visible.

## Correction

`MenuDepthProjection.PlaneNormal(Transform)` computes one finite unit winding
normal. Each transformed axis is rescaled by its largest absolute component and
normalized by explicit magnitude division, then the cross receives the same
treatment. This avoids Unity's absolute normalization cutoff and squared-length
underflow/overflow. The helper retains negative XY winding, nonuniform scale and
parent-induced shear. Null/destroyed, zero or nonfinite geometry returns the
existing zero sentinel; no new fallback state is invented.

`MenuDepthDisplay.RenderHead` computes this once and shares it across its three
consumers. The older per-window stereo render and its capture-facing decision
use the same helper. `FlatScreen.8.MenuDepth` likewise shares one computed normal
between its two head resolves and final head carrier. No camera plane, viewport,
window pose, native callback, shader or wire record is modified.

## Related-source audit

All WorldUI `Vector3.Cross` call sites were inspected. The three identical unsafe
raw `TransformVector(right/up)` sites in `FlatScreen.8.MenuDepth` were the only
additional direct head-raster sites. Its axes describe the whole screen quad,
so it is not the small pixel-host geometry in this report, but it now uses the
same geometric contract. Other critical projection/carrier paths already
normalize their axes before crossing; `TablePanelSurfaces` divides by explicit
edge lengths. Local mesh triangle normals in debris/book assets and NPC anatomy
directions have different geometry and are outside this bounded capture fix.
The separate `RayUguiDriver` full-rectangle-area hazard was not changed or used
to explain these logs.

The earlier `Popup759` fixture actually used the faithful `.00105 × rig` host
metric. It is inaccurate to attribute its green result to a unit-scale fixture.
It checked final OpenGL color but omitted the finite/unit normal and valid
glass-depth invariants. This worker adds the backend-independent actual-Unity
math witness; the other worker owns complete head-coordinator and final-eye
consumer proof, including explicit material-normal/depth coverage.

## Focused proof and limits

New suite ID: `window-plane-normal`; command
`scripts/window-plane-normal-tests.sh`. It compiles the complete projection file,
then runs actual Unity transforms and `Vector3.Normalize` without graphics. The
unused projection methods' XR observation is an explicit compile boundary; the
normal helper has no substitute engine or geometry implementation.

Production passed **295 assertions**, including the four hardware host scales,
17 scales from 1e-8 through 1e8, rotated/nonuniform/reflected/sheared bases, tiny
and very large finite direction inputs, zero axes and nonfinite rejection. An
independent double-precision geometric reference checks winding and unit length.
All **six compiling controls** failed at their intended boundary: original raw
cross, per-axis Unity cutoff, omitted safe rescaling, rotation-only winding,
invalid sentinel loss and accepted nonfinite input. Seven strict compilations
completed with zero warnings/errors.

Evidence: `debug/window-plane-normal/run-o1foacqc`, 13.64 seconds for the focused
invocation. A preceding fixture compile failed because its complete-file
reference list initially omitted `UnityEngine.VRModule` for the unused XR entry
points; this reference-only failure is retained under `run-prev0001` and was not
a production error. Both runs used canonical completion cleanup. The passing
run removed 1,294,459 cache bytes with no skips.

This proof does not claim GPU shader output, D3D11 behavior, actual HMD pixels,
native scenario bootstrap or multiplayer synchronization. Current consumer
coverage and the final full checkpoint must be integrated by the primary agent.
