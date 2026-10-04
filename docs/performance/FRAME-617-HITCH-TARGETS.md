# Build 616 capture: remaining hitch targets

This is a read-only investigation of the new Steam Frame standalone run, not an
implementation or a headset improvement claim. Build 616 removes the defective
resident derivatives but leaves the scenario presentation paths below unchanged.
The next useful work is cold card/figure preparation and real wall publication
bursts. More polygon reduction alone cannot remove these measured CPU bursts.
The separate aggregate audit owns comparison statistics and the wall audit owns
fade diagnosis; this document identifies call paths and safe next steps.

## Evidence and boundaries

Immutable inputs and reproduction are in
`.planning/debug/frame617-review/hitches/`: `inputs.json`, `inputs/`,
`reproduce.py`, `causal-metrics.json` and `causal-excerpts.txt`. LogOutput SHA-256
is `c48108b494c24eeb709fbcf97bdcfc5f9d6baf26a9f69578f9c05aec0daa43f0`;
Player SHA-256 is
`c8eabb3d38b841aaaac2159b2227369d1584ecaa0281bd53ff4e56b554e3c2d7`.
The parser uses one-based Python `str.splitlines()` line numbers; embedded Unicode
line separators can make these differ from `rg` physical line numbers.

LogOutput identifies ModBuild 616, SteamVR/OpenXR 2.17.10 and MultiPass. This run
loads directly into Game/ProcGen; it does not establish a visit to the 3D map.
The scenario spinner hides at normalized line 1951. Exclude the preceding load
and qualify the first post-load publication interval rather than assigning its
1.6–5.3 second frames to steady interaction. Later options opening, slider
retuning and user-presence loss are separate events. In particular the final
197.61/555.44/489.60/985.57ms cluster follows `User presence lost` at line 7185;
there is no complete final measurement window after it.

The parser retains 288 actual SPIKE rows, including rows without the textual
`VERDICT`. Spikes are rate-limited observations, not a complete count of long
frames. Only LogOutput supplies numerical samples; Player corroboration is not
added as a second copy. Inclusive parent/child scope times must not be summed.
The log's “NOT the mod” label means *named scopes are a small fraction*, not proof
that no uninstrumented mod-triggered engine work occurred.

## Ranked implementation candidates

| Target | Measured evidence after loading | Safe boundary for a follow-up |
| --- | --- | --- |
| Ability hand switch preparation | Frame 5437: 311.95ms total, 193.73ms named mod, `Cards.Driver` 172.37ms. Window peak is 183.61ms. | Prepare shared class art/mips and blank wrapper resources during the spinner; retain native hand ownership and immediate focus/exchange semantics. |
| Figure ghost and first information panel | `Hands.NearGrip.Pickup` 109.10ms, nested ghost construction 55.89ms; `StatPanelSurface` peaks 65.93ms and later 98.54ms. | Prebuild inert visual templates and shared portrait/mip resources, then bind the current native pose at pickup. Never advance gameplay or expose a stale frozen idle pose. |
| Real wall publication | Stable rescans skip commits, but later `WallFade.Rescan` peaks 140.85ms; frames 6871/6996 contain 223.25/208.01ms rescans. | Eliminate unrelated publication invalidation first; prepare expensive memberships on bounded private state and publish atomically without losing native fade continuation. |
| VR options content reuse | Frames 6442/6470: `VROptionsTab.TabRoot` 118.94/109.80ms. | Reuse inactive row/view trees with live value rebinding and intact toggle/X/input state; avoid rebuilding every row on a tab revisit. |
| Audited actor-bar cadence | `ActorBars.Late` averages 1.77–1.96ms per measured frame; three ordinary heroes are refused solely by exact `CInteractableActor`. | Extend only the exact audited type, retaining all transition/topology/dissolve/unknown-writer guards. Establish actual admitted/skipped counters before claiming a benefit. |

### Card preparation: the existing warm-up does not cover all CPU work

The cold edge is visible at lines 4124–4161: native hand presentation switches
to the Summoner, nine cards exchange, new class default art and `ConsumeDark`
mips are baked, and frame 5437 contains the 172.37ms driver scope.

The scenario path **borrows the original full card**, not a front clone:
`CardsDriver.UpdateBody` calls synchronous `Rebuild` when dirty
([update](../../src/GloomhavenVR/Cards/Driver/CardsDriver.2.Update.cs), lines
674–677); `AdoptedCard` uses `VRCardFactory.GetOrCreate`
([rebuild](../../src/GloomhavenVR/Cards/Driver/CardsDriver.4.Rebuild.cs), line
2006); the factory creates a backing and attaches the live native widget
([factory](../../src/GloomhavenVR/Cards/VRCardFactory.cs), lines 70–78).
`CardFace.Adopt` then synchronously scans/bakes its sprites before enqueueing
its loader warm-up ([face](../../src/GloomhavenVR/Cards/Art/CardFace.cs), lines
190, 253 and 276). Shared mip cache misses can perform full-atlas
`Graphics.Blit`, `ReadPixels` and `GetPixels32`
([mip bake](../../src/GloomhavenVR/Cards/Art/CardFaceMipBake.cs), lines 710–766).
These are potential expensive terms inside the measured driver, not separately
measured attribution of all 172.37ms.

`CardArtPrewarm.DrainWarmQueue` starts at most two `ShowCard` loads per frame,
after adoption ([prewarm](../../src/GloomhavenVR/Cards/Art/CardArtPrewarm.cs),
lines 302–355). It does not amortize wrapper creation, adoption, silhouette/FX
maintenance or first synchronous mip preparation. Hardware confirms the first
two opens ready immediately (lines 2065/3913), but the third opens with 10/10
faces not ready and finishes after 209.7ms/two frames (line 4095). Class pins are
added on later switches, not all completed before the initial spinner ends.

A follow-up should gather the controlled scenario roster's class sprites while
loading, populate existing shared replacements/pins, and prepare reusable blank
backings without changing native CharacterFocus or showing another hand. Other
characters' native widgets may not yet exist; warming a fabricated gameplay
widget or invoking `Awake` manually is unsafe. The existing `WarmSprites` cache
primitive is reusable, but a preparation coordinator must wait for real art
readiness. Keep bounded fallback for new/summoned/unprepared content. Add nested
timings for resource misses/adoption before asserting which term improved.

### Pickup and stat panels: preserve immediate interaction

`ProximityGrabber` wraps `Held.OnGrab` in the pickup scope
([grabber](../../src/GloomhavenVR/Hands/Interact/ProximityGrabber.cs), line 676).
`FigureGrabbable.OnGrab` asks `FigureGhosts.NotifyHeld` before moving the actor
([figure](../../src/GloomhavenVR/Board/FigureGrab/FigureGrabbable.cs), line 639).
`NotifyHeld` synchronously builds the visual mirror; the nested construction
scope begins at `FigureOverlay.BuildFrozenGhost`
([overlay](../../src/GloomhavenVR/Board/FigureGrab/FigureOverlay.cs), line 112).
The nested 55.89ms is part of 109.10ms pickup, not an additional cost.

The first stat show converts the original hierarchy and immediately scans its
mips ([stat panel](../../src/GloomhavenVR/WorldUI/Surfaces/StatPanelSurface.cs),
lines 446–491). Its own scope also includes other watch/copy work, so the 65.93
and 98.54ms peaks do not isolate a single bake or conversion operation. The
second hand additionally builds an inert original visual snapshot, stripping
logic under an inactive holder (lines 613–644).

Load-time preparation can cache per-native-source visual skeleton/render slots,
depth materials and shared stat-panel imagery. Bind/evaluate the **current** idle
or condition pose at first hold, and invalidate on native mesh/skin changes or
scene teardown. Do not call native `Show` with artificial actors just to warm the
panel, run clone gameplay controllers, delay grip response, or drop original
information. Verify both held sides and remote pickup using these same caches.

### Wall bursts are not all the same event

The 140.81ms commit reported at line 5753 follows options/tab creation and the
wall roster changing from 4693 to 4700, before the first vegetation retune. It
is a candidate for unrelated-renderer invalidation; the hardware roster count
alone does not prove the exact invalidation predicate. Later commits reported at
6172/6415 follow 0→5→10% vegetation retuning, with rosters growing to 5015/5826.
These are real visibility/configuration edges. Frame 6871 is 294.18ms total with
264.27ms named mod (`WallFade.Rescan` 223.25ms, nested WallCache 84.74ms);
frame 6996 is 290.95ms with 224.99ms named mod (`Rescan` 208.01ms).
Most stable rescans still report zero commit cost. A slower sweep frequency
alone cannot remove expensive admitted publication or cold resources.

The 592.48ms frame 6762 follows the first retunes but names only 45.41ms of mod
work. `SceneryBudget.Update` takes approximately 4–11ms on nearby frames;
re-enabled native renderers can trigger engine work outside the scope. This is
coincidence/source plausibility, not a measured explanation of the missing
547ms. Treat retune preparation separately from normal interaction and examine
publication reuse rather than changing fade animation to popping.

### Steady CPU and optional compromises

The current actor admission intentionally rejects unknown skeleton writers.
Exact native `CInteractableActor` has empty `Awake`/`Update`, resolves its actor
in `Start`, and forwards a user click to the tile callback; its `CInteractable`
base hover methods are empty. Neither implementation writes a bone, Animator,
constraint, skin, root pose or scale. This makes an exact-type allow-list addition
a defensible follow-up for the Berserker, Summoner and Brute in this capture.
The Elementalist still has `SummonAppear`; do not infer that a disabled component
is safe, since native reveal can start its coroutine separately. Unknown
subclasses, active material/vertex dissolve, blended states and constraints stay
immediate. Raising the existing check interval only benefits admitted rigs;
removing guards to make the slider look effective is not an optimization.

Reusing stable native UI inventories and rows remains useful: several small
per-frame scopes together form repeatable CPU load. Use the existing cached
inventory/event seams, not stale snapshots of native choices/tooltips/layout.
For Frame compromises prefer existing lower scenery, actor detail/effect and
resolution settings, or a safely audited optional idle/cadence budget. Resident
simplification is explicitly abandoned; do not restore removed NPC derivatives.
No new visible feature reduction is justified solely by this log.

## Remaining attribution limits

Loaded FRAME lines contain approximately 1.5–3.4MB/s of allocation and several
collections, but no per-frame GC pause clock. The options/retune windows at
5917/6346 have **zero** generation-count increases despite substantial wall
and CPU bursts. A memory leak or GC origin for those bursts is not established.
There is ongoing allocation worth auditing after cold resource work, not proof
that the growing heap itself causes the hitches.

Before options, the rendered camera inventory has the head's two eye passes.
Options adds two useful UI capture cameras; those render actual visible pause
and settings panels. Their named average camera spans are small compared with
the main-thread logic spans. This capture does not show a second scenery camera
leak. Do not disable captures needed to preserve original UI content.

The runtime's XR “gpu” value tracks the frame interval/wait and is explicitly
marked unusable as GPU busy time. Unity FrameTimingManager yields no samples.
Long unbracketed/native logic intervals cannot be assigned to GPU saturation,
GC, asset decompression or CPU thermals from these logs alone. A bounded Debug
attribution of native/cold resource calls and completion of the scene inventory
would make the next optimization decision stronger. It must not become a large
normal-level per-frame diagnostic stream.
