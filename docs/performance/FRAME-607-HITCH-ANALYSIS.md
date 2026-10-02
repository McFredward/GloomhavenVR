# Build 607: remaining Steam Frame hitches

Internal engineering notes, 2026-10-03. The supplied Player.log and LogOutput.log
both identify ModBuild 607, commit `5344a5504`. They describe the same run and
must not be counted as independent captures. Immutable inputs, hashes, parsed
windows and investigations are retained in
`.planning/debug/frame607-run-analysis/`.

## What the sliders establish

The settings are applied. At player/enemy detail 0/0 the native-body census
reports 36 assigned derivatives and 278,708 → 181,529 admitted body vertices
(34.87% fewer). At 0/100 it reports 13 derivatives and 248,312 vertices; at
100/0, 23 derivatives and 211,925 vertices. All eight admitted native LOD caps
are applied and 15 cloth solvers are disabled. The effect sweep changes 53 → 0
→ 39 → 53 masked renderers as density changes 0 → 100 → 25 → 0. These are
effective renderer/geometry observations, not FPS measurements.

There is no settled 100/100 timing block in this run. All four
`FIGURE-MEASURE begin` markers carry 0/0, effects 0, cloth false. The other
slider intervals are correctly marked `preparing`: VR Options remained open.
For example, revision 88 spans 314.6 seconds of preparation, with the options
canvas still active at age 367.4 seconds. Its X-close precedes the next steady
marker. Many apparently stationary intervals also contain untracked-headset
samples. Consequently these intervals do not isolate the figure sliders' FPS
benefit or establish the value of the 294 MB mesh package. They do not indicate
a readiness defect.

Window materialization was switched off during the run, with actual bypass
playout messages. Resolution remains 3408 × 3408 per eye. The wall cadence
reports consistently read 0.25-second evaluation and 4-second table rescan;
there is no wall-cadence edit in this capture. Actual GPU busy time is unavailable.

The offline pose parser originally omitted negative table-relative head heights.
Accepting signed heights recovers eight windows without relaxing any
preparation, tracking or comparison exclusions. None supplies the missing 100%
counterpart.

## Hitches after scenario loading

Loading intervals are excluded from the following examples. The frames are
individual interaction events, not a stationary quality comparison. Step scopes
are inclusive; parent and child times must not be added.

| Frame | Whole frame | Measured synchronous work |
| --- | ---: | --- |
| 16875 | 485.65 ms | Cards.Driver 375.05 ms during character/card presentation change |
| 17542 | 423.76 ms | Cards.VRCardArt 267.21 ms; new Summoner atlas/half mip captures |
| 17745 | 346.21 ms | Hands.VRHand.NearGrip 247.64 ms during trap pickup |
| 15320 | 293.00 ms | WallFade.Rescan 198.36 ms after a home ghost appears |
| 17928 | 263.83 ms | WallFade.Rescan 214.62 ms during ghost membership churn |
| 18168 | 260.59 ms | WallFade.Rescan 208.63 ms after a home ghost disappears |

Other large frames have little time in measured mod scopes. This does **not**
exclude mod-induced downstream rendering or allocation costs. The historical
SPIKE verdict that attributes every such remainder to the game/GPU/compositor
is stronger than the instrument can prove. No measured GPU bottleneck or memory
leak is established by these logs. Managed collection counters increase, but
allocation estimates and window-level collections do not identify a particular
frame's cause.

## Source-backed unnecessary work

Home ghosts copy the native visual hierarchy without native gameplay controllers.
Their child renderers retain native names and layers. The wall classifier knows
the mod name/layer conventions, but previously did not recognize the owning
`FigureVisualMirror`. Signature deltas therefore folded the drake's cloned body
and LOD children, the demon's cloned body and the trap's copied content into the
world signature. Their creation/destruction forced unrelated atomic wall commits.
Exact mirror-component ancestry distinguishes these visual copies from their
native counterparts. Genuine world geometry changes still require the commit;
a mesh using the wall-fade shader retains its conservative signature exception.

Two card diagnostics also perform unnecessary synchronous work. CardHalfTone's
global card census runs from the gameplay observation seam, including when its
eventual Debug output cannot print. FaceBlackout repeatedly prepares a complete
inventory while waiting for a first successful mute: after its first zero-mute
report, another zero result cannot be printed, but the old report-interest test
still requests the expensive geometry/probe inventory. These diagnostic costs
must be reduced without changing artwork, opacity correction or restoration.

Some first-use work is genuine: card mip creation uses synchronous GPU readback
and CPU texture conversion, while trap pickup builds a home visual and original
prop information. Removing diagnostic amplification and false wall invalidation
does not establish that those remaining first-use paths are fully prewarmed.
Matched hardware measurements are required after the source changes before any
claim that all hitches are resolved.
