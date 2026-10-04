# Frame 616: preserve wall animation during environment optimization

## Hardware evidence and cause

The supplied Steam Frame capture is Build 615, commit `64db88dc9`. In
`Player.log`, line 14842 identifies `CV_Floor_Basic_M (VR simple environment)`
with shader `GloomhavenVR/ScenarioSimpleEnvironment` and `_WallFade_On=1` inside
the wall-fade path. The simple shader does not implement that native channel.
The floor material admission rule could replace this material, then copy the
native saved properties into a shader that cannot render their dissolve. An
authored floor identity alone was therefore not a safe simpler-shading veto.

The original game bundle independently confirms that `CV_Floor_Basic_M` is
authored with `_WallFade_On=1` and `_Cutoff=0.5`. Its bundle SHA-256 is
`6f1091e5ec51d09bdddbfd4f569ede1aaaaaf066382bbeba55932360e3e95830`;
material path ID is `4266299227312612754`. Actual Unity 2021.3.5f1 material
copying also proves that `HasProperty("_WallFade_On")` and `GetFloat` can retain
the saved value after assigning the simple shader. This was a source-proven
animation regression in the cheaper material path, not an approved visual
compromise.

Line 15407 reports 17 compatible surfaces, 17 unreadable original meshes,
zero combined chunks and one simpler material. Structural chunk substitution
did not occur in this capture; neither its savings nor its responsibility for
the reported pop can be claimed. Settled `HELD`/zero-noise samples alone also do
not prove that an earlier dissolve skipped intermediate pictures.

## Changes

Floor and structural material admission now preserve every live native wall
fade gate, local gate and wall-fade keyword. Immediately before a native
renderer-property write, owned floor materials are restored as well as owned
structural materials. In-place original-material changes are checked before
each camera culls: a newly enabled native channel retires the simpler material
before either eye renders. Non-fading compatible floor and masonry surfaces
remain eligible for the existing optimization.

This camera check visits only owned, already simplified surfaces. Its unchanged
path performs no scene/hierarchy query and allocates no managed material array;
it reads the retained original material properties/keywords. Its CPU cost is
included in `EnvironmentBudget.PreCull`. Preserving native animation takes
precedence over savings from an incompatible surface.

The existing elapsed-time wall clock had another independently reproduced
failure: with a 0.12-second time constant, a sufficiently long frame could
finish a new dissolve in its first displayed sample. This behavior predates
Build 615 and is not proof of the exact capture's cause. The wall-specific visual
step is now bounded to 33.33 ms. At 30 rendered frames/s or faster, timing is
unchanged; at slower frame rates, the dissolve takes longer in real time so
intermediate pictures remain visible. Coverage, dwell, decision frequency,
gameplay and the shared board fade clock keep their elapsed-time behavior.
Debug logs summarize clamped wall samples at most once per ten seconds; normal
player logging is unchanged.

The capture also contains Unity's invalid `OnPreCull`/`OnPostRender` message
warnings at environment-driver installation. Instance event handlers now have
`HandlePreCull`/`HandlePostRender` names so Unity does not mistake their
`Camera` parameters for unsupported MonoBehaviour messages. These warnings do
not establish the cause of later frame-time spikes.

## Validation and limits

The production environment driver, wall delivery method, noise textures,
shared fade ramp and wall visual clock run in actual Unity 2021.3.5f1. The
fixture imports the original material's saved gate/cutoff and native masonry
geometry. It verifies material restoration during native writes, late gate
changes, settings changes, multiple cameras, command-buffer renderer identity,
unchanged source flags and restoration after disabling optimization.

A stalled wall sample is rendered and read back, then at least five genuinely
decreasing pixel samples are required before the fully hidden endpoint. The
reverse transition requires at least five increasing samples before the fully
visible endpoint. The old unbounded clock and a binary occlusion-map mutation
both fail this visual check. Actual Unity `AddComponent` also rejects the
invalid-callback-name mutation through the captured engine warning.

The complete focused suite passes **342 production runtime assertions and 27
causal negative controls**. Strict Release compilation passes with zero
warnings/errors. Receipts, source hashes, native bundle provenance and failed
development runs are retained under the gitignored worker path
`.planning/debug/frame616-walls/`, with final receipt `run-jvkwyejq`.

The pixel test uses a documented GL surrogate of the native above-foundation
occlusion-map branch. It does not execute the game's original Windows shader
bytecode, full scene/gameplay or OpenXR. The fixture previously had a UV macro
compile defect that could render error-shader pixels; both native-surrogate and
production shader compilation are now checked explicitly before pixel tests.
The original shader's complete headset appearance and performance still need
hardware confirmation. This patch preserves animation; it does not claim an
FPS improvement or eliminate the capture's unrelated hitches.
