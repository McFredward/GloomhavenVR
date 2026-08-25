# cloth-cook-harness

A standalone Unity 2021.3.5f1 Linux player that measures `UnityEngine.Cloth` behaviour outside the
game, so that claims in `src/GloomhavenVR/Board/FigureGrab/FigureCloth.cs` are measurements rather
than reasoning. Build and run instructions are at the end of `.planning/CLOTH-DURING-SCALE.md`.

## THIS DIRECTORY SUPERSEDES `.planning/perf/cloth-cook-harness/`

That directory holds the ModBuild 284–286 copies of `Builder.cs`, `ClothBench.cs`,
`ColliderSyncBench.cs`, `CookCostBench.cs` and `ScaleShapeBench.cs`. The copies here are those files
plus two fixes and three new benches, and **running the old copies will silently produce the wrong
thing**: `ClothBench` there boots by default and calls `Application.Quit`, so any `--bench=` run
against it exits 0 with none of the requested output. The old directory should be deleted; this lane
did not own it and therefore did not.

## The benches

| flag | file | what it answers |
|---|---|---|
| *(none)* | `ClothBench.cs` | the ModBuild 284 end-to-end algorithm comparison |
| `--bench=cost` | `CookCostBench.cs` | what each individual Cloth call costs |
| `--bench=sync` | `ColliderSyncBench.cs` | whether assigning collider arrays cooks |
| *(none)* | `ScaleShapeBench.cs` | the SETTLED shape across a rescale, ModBuild 286 arms |
| `--bench=drivers` | `CookDriversBench.cs` | **what actually drives the cost of a cook** — one Cloth property at a time at a fixed vertex count, plus a live-property block |
| `--bench=dir` | `DirectionArmsBench.cs` | **both directions** of a rescale, with a NULL and a POSITIVE control, self-intersection counts and rendered wireframes |
| `--bench=gesture` | `GestureBench.cs` | **the two state machines over a whole gesture**, sampled every frame |
| `--bench=live` | `LiveCookBench.cs` | **what a cook costs at 81–3364 particles** (his capes are 82–143, and every previous curve started at 441), and whether the ModBuild 290 `stretchingStiffness` remedy survives fourteen different authored cloth configurations |
| `--bench=factor` | `FactorArmsBench.cs` | **the direction arms at 1.345 / 2.5 / 3.5**, both directions. Every table before ModBuild 291 was measured at 1.345 because one ModBuild 289 log settled there; he drives to the 2.503 gesture clamp |
| `--bench=throttle` | `ThrottleBench.cs` | **a throttled mid-gesture re-cook** — "inklusive der Reaktion" — against the shipped 289 and 290 state machines, with a JITTER column for the one-frame skinned-pose pop each cook costs |

## Two traps

* `ClothBench` and `ScaleShapeBench` boot by DEFAULT. **Both now stand down for ANY `--bench=`
  flag.** `ScaleShapeBench` carried an explicit allow-list through ModBuild 290, which is the worst
  shape an instrument guard can have: a bench added later is silently pre-empted and the run looks
  like a clean success with a full page of the wrong output.
* `-batchmode -nographics` segfaults this player before the first scene loads. Run under
  `xvfb-run -a` with `-batchmode` only.
* **Run `--bench=live` on its own.** It is the only bench here that measures TIME, and a second
  player running beside it inflates every cell. The shape benches can share a machine; that one
  cannot.

`shots/` holds the contact sheets referenced from `.planning/CLOTH-DURING-SCALE.md`; the benches
regenerate the individual PNGs into a `shots/` directory next to the built player.
