# Build 607: remaining Steam Frame hitches

Internal engineering notes, 2026-10-03. The supplied Player.log and LogOutput.log
both identify ModBuild 607, commit `5344a5504`. They describe the same run and
must not be counted as independent captures. No new screenshots are present in
the supplied Steam Frame folder. Immutable inputs, hashes, parsed windows and
investigations are retained in
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
leak is established by these logs. Conditional SPIKE heap samples rise and fall
(533.7–644.1 MB across the relevant intervals, last observed 590.8 MB), rather
than proving continual accumulation. Managed collection counters increase, but
positive `GetTotalMemory` deltas are an allocation-pressure estimate, not exact
allocated bytes. Window-level collections do not identify a particular frame's
cause. Sixteen native Hydra SDK host-resolution exceptions also occur; they
do not establish a networking cause for these hitches. No GloomhavenVR error,
figure-driver failure or bank-resolution failure is logged.

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

The same capture also logs signature deltas from native `HexHighlight(Clone)`
selection roots. The previously recognized decal and child-emitter arrays miss
the emitter on the selector itself. Read-only prefab inspection confirms that
`resources.assets` HexHighlight GameObject 5746 owns HexSelect_Control 11576,
ParticleSystem 8156 and ParticleSystemRenderer 8674 on the same object
(asset SHA256 `bdbb12b62374aee0a00a07f07e162c7a558c052996ea7be360a2d59bd0c8c34a`).
Build608 recognizes that exact same-object particle ownership, rather than exempting
anything merely beneath a selector. Of twelve retained post-load signature-delta
reports after LogOutput line 7000, six describe visual-ghost churn, five selection
root churn and one genuine new environment content. Bounded diagnostic reports
are not a whole-run census or a predicted percentage improvement.

Mirror ancestry reads reuse the existing synchronous wall read-cache scopes.
Positive and negative entries are cleared at both boundaries; mutable hierarchy
ownership is never memoized across frames. No new steady scene census is added.

Two card diagnostics also perform unnecessary synchronous work. CardHalfTone's
global card census runs from the gameplay observation seam, including when its
eventual Debug output cannot print. FaceBlackout repeatedly prepares a complete
inventory while waiting for a first successful mute: after its first zero-mute
report, another zero result cannot be printed, but the old report-interest test
still requests the expensive geometry/probe inventory. These diagnostic costs
must be reduced without changing artwork, opacity correction or restoration.

Build608 gates the half-tone census before discovery at normal log levels. At
Debug, discovery, sampling and reporting occupy separate frames; sampling checks
a one-millisecond budget between faces and stops after at most eight faces.
A native operation cannot be preempted, so this is not a hard one-millisecond
duration guarantee. Unchanged results repeat only on a 30-second heartbeat.
The blackout path retains the first Debug inventory and the first later positive
correction while rejecting impossible bright/translucent candidates before
geometry/probe work. Existing tracked-mute restoration runs first.

The focused real-Unity card harness passes 486 assertions and ten deliberate
defect controls, including 227 scene faces, Debug cancellation/reset and native
color/art arrival/restoration. Repeated excluded-graphic maintenance performs
zero inventory entries, geometry queries and silhouette probes in those tested
paths. Unity Mono's allocation counter failed a 16 KB calibration; allocation
bytes remain unavailable rather than being reported as zero allocation. These
tests establish the work bypass and preservation contract, not a headset FPS gain.

Some first-use work is genuine: card mip creation uses synchronous GPU readback
and CPU texture conversion, while trap pickup builds a home visual and original
prop information. Removing diagnostic amplification and false wall invalidation
does not establish that those remaining first-use paths are fully prewarmed.
Matched hardware measurements are required after the source changes before any
claim that all hitches are resolved.

## Build608 validation and remaining hardware check

The final integrated tree at `06cdfa01` passes 14 source suites, all 103 local
suites, 286,760 wire/golden assertions, strict Release with zero warnings/errors
and five bilingual document pairs. Independent report and log-hash verification
confirms complete coverage. The local run took 689.2 seconds with eight jobs.
Against the preserved607 compiled snapshot, only CardFace, CardHalfTone and
WallSegmentFade behavior changes; eight other types change solely through the
inlined607 →608 build constant. No type or assembly reference is added/removed.
The guard's nonzero historical compiled-diff status is reviewed change evidence,
not a failed test. Source hashes do not drift during the final gate.

The exact wall classifier passes 19,479 assertions and 21 deliberate defect
controls. Actual Unity/native-Drake tests pass 184 assertions and 18 controls,
including sleeping/flying local/remote pose pixels, native-named mirror children,
inactive/dead rows, exact root selector emission and conservative foreign/native
world ownership. Final card diagnostics pass 486 assertions and ten controls.
Under parallel desktop test load, worst discovery/sample/report scopes are
0.426/5.792/11.642 ms; these are not Steam Frame measurements or a hard budget.
Normal diagnostics and repeated unprintable inventories are bypassed in the
actual tested work paths, not merely omitted from text output.

Full proofs live under `.planning/debug/frame608-final-validation/`; compact
wall/native topology proofs are in `.planning/debug/frame608-wall-ownership-worker/`.
Hardware should now recheck figure pickup/release, selection changes and first
character/fan presentation. For an isolated mesh timing comparison, close VR
Options after each setting and keep the same tracked view for at least 40 seconds
before the next change. Do not mix FX/scenery/cloth changes with the figure sweep.
The next capture must establish whether false wall commits disappear and whether
remaining expensive card frames are first-use captures, staged diagnostics or
another scope. No measured headset improvement is claimed before that capture.

## Next loading-time preparation target

CardArtPrewarm is fed by `CardFace.Adopt` and prepares concrete widgets that have
already reached the presentation path. It is not a scenario-wide party-hand or
mip-cache prefill. Its CONFIRMED message checks native image/loader readiness,
not complete mip-cache coverage. Both the new class atlas pin and its later
native-art arrival can therefore occur during the first character switch.

The native game creates hands for every non-summoned party character, not just
the selected hand. A future loading-time feed can inspect their existing widgets
as read-only sources after asynchronous construction. `cardsSpawned` is set
before the first yield and must not serve as a completed-construction signal.
Use the actual hand's skin: ordinary native initialization derives it from the
player's DefaultModel and CustomCharacterConfig, whereas preview initialization
uses the card's ClassModel. Activating hands or invoking native Show/Init solely
to warm a cache would run gameplay/UI callbacks and is not an acceptable shortcut.
