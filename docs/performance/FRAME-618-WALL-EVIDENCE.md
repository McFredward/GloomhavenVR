# Build 617 Frame wall audit

This is an analysis-only audit of the new Steam Frame hardware capture. Both logs
identify ModBuild 617 / `e1da52eaa`, assembly 1.1.0.0. Unity reports Direct3D11,
Turnip Adreno 750. No production behavior, configuration, asset, shader or test
was changed in this audit. The reported wall pop is not declared fixed.

Immutable inputs and the reproducible parser/JSON evidence are in the main
checkout's gitignored `.planning/debug/frame618-review/{inputs,walls}/`.
Line references below count physical LF separators in Player.log, stripping CR
characters. LogOutput.log filters the Debug draw and ceiling records; Player.log
is necessary for this investigation. No new wall video or image accompanies
these inputs, so delivered inputs cannot establish the visible shader result.

## Actual camera delivery: what the trace establishes

Player.log contains 72 `DRAW DELIVERY` samples from six segments,12 per segment.
All 72 are HIGH, native `Amp_Basic_WallFade`, all from the left-eye head-camera
pre-render callback. All deliver the expected renderer-wide cutoff, native map,
integer/float gates, enabled renderer and `forceRenderingOff=False`. There are
zero reported late-to-draw changes and zero indexed property-block overrides.
The captured native material slots have empty keyword sets; the first rocky and
grassy material IDs are 388294 and 387950. `toggle=n/a` and `wallOn=n/a` in the
material slots are unexposed shader properties, not proof that a bound shader
uniform or the renderer-wide MPB gate is ignored.

The 72 samples contain 66 noise-map and six held-occluded-map deliveries:

| Episode / actual renderer | First outward samples | Held | Returning samples | Later outward samples |
| --- | --- | --- | --- | --- |
| 1 `CR_FR_Wall_Rocky_Verge_01#-278712` | frames 5172,5173,5174,5176 | 5191 | 6151,6152,6153,6155 | 6242,6243,6245 |
| 2 `FR_Wall_Grassy_Verge_Thin_Narrow_01#-286446` | 5172,5173,5174,5176 | 5191 | 6140,6141,6142,6144 | 6630,6631,6633 |
| 3 `CR_FR_Wall_Rocky_Verge_01#-235296` | 5263,5264,5265,5267 | 5282 | 6163,6164,6165,6167 | 6187,6188,6190 |
| 4 `CR_FR_Wall_Rocky_Verge_02#-243094` | 5263,5264,5265,5267 | 5282 | 6157,6158,6159,6161 | 6202,6203,6205 |
| 5 `CR_FR_Wall_Log_Structure_03#-252166` | 5263,5264,5265,5267 | 5282 | 6075,6076,6077,6079 | 6886,6887,6889 |
| 6 `FR_Wall_Grassy_Verge_Thin_Narrow_01#-291106` | 5263,5264,5265,5267 | 5282 | 5494,5495,5496,5498 | 6903,6904,6906 |

Every first outward sequence records fade/cutoff pairs
`.2425/.1289`, `.4262/.3402`, `.5654/.5002`, `.7506/.7132`, then
held `1/.5000`. Every return records `.7575/.7211`, `.5738/.5098`,
`.4346/.3498`, `.2494/.1368`. These are genuine native renderer reads, not
the previous LOW surrogate's pixel result. There are 44 bounded animation-clock
reports, and the first `.2425` fade agrees with the current 33.33ms clamp and
`.12s` exponential time constant. The historical explanation involving an
initial negative cutoff therefore does not explain this capture's first sample.

This excludes an observed renderer disable, indexed override, keyword swap or
cutoff/map overwrite between the mod's write and the sampled left-eye callback
for these six renderers. It does not measure GPU pixels, right-eye execution,
changes after this callback, or the unsampled LOW/toggle-native materials. The
scene's existing fade-on records explicitly include toggle-native masonry; no
such renderer is represented in the72 delivery samples.

## Source-proven diagnostic blind spot

`WallSegmentFade.DrawTrace.cs:174` suppresses a repeated fade bucket before
terminal completion is processed. Returning fade`.2494` and terminal fade 0 both
fall in bucket 5. The terminal MPB-clear/solid-restoration sample is suppressed,
the episode remains open, and `WasReturning` persists into the next outward
cycle. The twelve-sample cap is then spent on that later cycle. All six sequences
show exactly this shape: five outward/held, four returning, three later outward,
and no terminal `fade=0/block=False` sample.

This is a proven measurement defect, not a proven wall-rendering defect. A next
bounded capture needs a reserved terminal-sample budget, cleanup before bucket
deduplication and a reset or explicit separation of subsequent episodes. Merely
moving the terminal check cannot recover an endpoint after the cap is spent.
It also needs actual LOW/toggle-family coverage, rather than counting the HIGH
samples as evidence for every wall. Normal player logging should remain unchanged.

## Native HIGH shader discontinuity remains a candidate

The preserved original HIGH shader metadata and the production DXBC analysis
describe `clip=A*B-cutoff` when the gate is open, where
`A=max(M,S)+42*n*(1-max(M,S))` and `B=M>0 ? 1 : S`. The transition noise map
permits `M>0`; the held map makes `M=0` and changes `B` to the native foundation
and screen-edge term `S`. That is not the LOW shader's single monotonic map term.

For example, with `S=0`, `M=.5` and `n=.03`, transition `A=1.13` and `B=1`, so a pixel
can survive through the transition cutoff. The held state instead gives `B=0` and
clips it at the authored`.5` cutoff. This mathematical example establishes that
correct numeric fade samples alone do not guarantee a smooth HIGH endpoint.
It does not establish the actual sampled M/S/noise values, active compiled
fragment branch, screen coverage or headset cause. No speculative shader or
cutoff change is justified from this capture alone. The original native HIGH
branch, its held transition and the missing return endpoint are the next visual
targets; the previous LOW-only pixel proof cannot close them.

## Recurring rebuild hitches: more than the safety ceiling

The spinner is hidden at Player.log 7300. Excluding its first subsequent budget
report 7319 because that report straddles preparation, and stopping before the
final user-presence loss at 21032, leaves 38 emitted windows 7556–20525 with 67
judged cycles: 46 skipped and 21 committed. Twenty commits were refused solely
because the scene signature moved;
one was the staleness ceiling. All other reasons are zero: no new table, reveal,
requested rebuild, material swap, board move, wall signature or AABB drift.
Counters are per emitted budget window, not repetitions of `LAST REFUSAL`.
Deliberate configuration changes and initial scenario setup are excluded. The
post-presence tail is not worn-headset gameplay evidence.

At least 13 post-load worn-headset SPIKE records explicitly name a costly
`WallFade.Rescan`: 12 ordinary and one ceiling,133.23–209.93ms. These are existing
inclusive scopes; they cannot be added to nested phase costs or interpreted as
the complete census of all commits. The largest is frame 9373:311.58ms whole
frame,209.93ms wall rescan. Selection/effect churn makes rebuilds much more
frequent than the one30-skip maintenance publication.

| SPIKE frame / Player line | Wall rescan | Associated exact scene delta |
| --- | --- | --- |
| 9373 /15775 | 209.93ms | line 15448: five path-particle births, zero figure movers |
| 9547 /16138 | 165.86ms | line 15915: the same five path-particle deaths |
| 10511 /18376 | 182.08ms | line 17872: nine path rows, including pooled active flips |
| 10611 /18655 | 163.96ms | line 18430: 20 folded summon/effect rows; 8 figure movers |
| 10806 /19326 | 173.87ms | line 18839: 57 folded effect rows; 45 figure movers |

The first two deltas name `Sparks`, `Rings`, `Waypoint_Node`, `Waypoint_Small`
and `Waypoint_Start`. Their row sum and xor both cross-check exactly. Seven
post-load delta records contain only folded path-marker changes; others name
native shield, projectile, summon, heal and attack effects. Some records name
only the first ten folded rows: totals are exact, the named sample is bounded.
Mod overlays and reticles are already reported exempt. These records do not
show a new native-named mod home-ghost ownership failure.

`IsActorParticleSignatureExempt` is already unconditional at
`WallSegmentFade.cs:5566`; it has no profile enable key. It requires an actual
active `ActorBehaviour` ancestor, excludes water and actual wall-fade shaders,
and deliberately does not admit detached native effects. `FigureExemptSkip=OFF`
is a different setting. Every budget window reports zero cycles that its
narrowing would have skipped: enabling that existing dial alone would remove
zero of these observed rebuilds, because births/deaths still fold identity.

The native game supplies a promising exact-owner boundary for path markers:
`WorldspaceStarHexDisplay` pools `GlobalSettings.m_WaypointHolder` under
`WaypointLine`, and `WaypointHolder.m_Prefabs` publishes the Start/Node/Small
visuals and their activation. Current `IsNativeHexSelectionVisual` recognizes
`HexSelect_Control` and its particle arrays, not this separate published family.
However, `ParticleSystemRenderer` is still a mountable type and the Mounted and
FreeStanding rider lanes can consume detached particles geometrically. A
name-based signature exemption, or one applied only to the hash, would lack
the consumer-closure proof. A safe next implementation should first establish
exact owner/reference rejection shared by every relevant collector, then prove
pool activation, birth/death and reuse cannot affect the wall table while real
scenery flames, water, floors, figures and gameplay remain unchanged. Detached
combat effects need their own equally exact ownership proof.

## What the ceiling now costs and why it stays

Player.log 11194 records one completed forced publication after 30 skips:
161.594ms phase total,3,967 native bounds reads and 10,414 reused reads.

| Existing phase | Time |
| --- | --- |
| `WallFade.Commit.WallCache` | 47.656ms |
| `WallFade.Commit.Mounted` | 43.156ms |
| `WallFade.Commit.Adopt` | 22.438ms |
| `WallFade.Commit.PropUnits` | 19.813ms |
| Remaining 21 phases | 28.531ms |

The first four account for 82.35%; WallCache and Mounted together 56.20%.
The enclosing rescan on frame 7719 is 164.07ms. Build 616's old ceiling window
reported 140.81ms worst commit. The new phase total is 20.784ms higher, but these
are different captures and scope boundaries, not a controlled regression
benchmark. Reused bounds comprise 72.42% of counted reads; that fraction is not
an elapsed-time saving measurement.

The guard still covers native renderer-enabled changes, incomplete material
eligibility, bounds and hierarchy/native membership inputs that the signature
does not completely close. Removing it would trade the measured hitch for
unproven stale presentation. Prioritize exact transient-owner closure to reduce
the 20 ordinary publications. For publications that remain necessary, WallCache
and Mounted are the measured next targets: narrower read reuse or private staged
construction must retain atomic publication and existing ownership/restoration
semantics. Yielding the current partially mutating collector is not a safe
replacement. This audit does not claim the ceiling has been optimized away.
