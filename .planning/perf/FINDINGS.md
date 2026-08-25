# The VR judder investigation — closed

> **Answer: Unity submitted every draw call on one thread. `-force-gfx-jobs native` moves that onto
> worker threads. 45 Hz → 90 Hz, and the reported ghosting disappeared entirely.**
>
> Closed 2026-07-28. The mod now enables it by itself (`[Core] EnableGraphicsJobs`, default on,
> written into `boot.config` by the preloader — see §5).

## 1. The symptom

> "Ich habe das Problem besonders beim hin und her wippen, dass ein Nachruckeln/Ghosting erkennbar
> ist. Und das obwohl ich sehr potente Hardware hab (RTX 4090 + Ryzen 7800X3D, 64 GB)."

RTX 4090, 7800X3D, Quest 3 over Virtual Desktop. The world smeared and lagged during head
translation, and no graphics setting made a difference.

## 2. The measurement that mattered

`[Perf] SPLIT` decomposes the main thread into logic / render loop / blocked. Same scene, same
build, three states:

| | plain | + static batching | **+ graphics jobs** |
|---|---|---|---|
| head camera (main thread) | 21.5 ms | 13.4 ms | **1.45 ms** |
| render loop (main thread) | ~18 ms | 14.9 ms | **1.81 ms** |
| logic | ~1 ms | 1.10 ms | 1.10 ms |
| blocked (waiting on GPU) | ~12 % | 12 % | **74 %** |
| frame p50 | — | 17.5 ms | **11.14 ms** |
| display rate | 45 Hz | 45 Hz | **90 Hz** |
| spikes | — | — | **4 in 2668 frames** |
| GPU time | — | — | 8.73 ms (headroom) |

The main thread now spends three quarters of the frame **waiting**. That is the healthy state: it
means it is no longer the bottleneck.

## 3. Why everything before it failed

Every earlier lever reduced the **amount** of work, and each moved the frame by under 10 %:

| lever | result |
|---|---|
| eye resolution (11× sample-budget cut) | nil |
| MSAA 8× → off | nil |
| shadows off + lowest quality preset | nil |
| the game's own quality preset | nil |
| depth prepass off | ~6 % |
| head culling mask narrowed | marginal |
| per-pixel light cap | ~1 % |
| the mod's own CPU cost | 0.47 ms of 21.5 (2 %) |
| wall see-through off | nil |
| discarded desktop render skipped | helped, but not the wall |
| **static batching** (1666 renderers → 47 meshes) | ~8 ms — the best of them, and still not enough |

**The reason is one sentence:** the work was serialised on a single thread and the runtime had
locked the app to 45 Hz, so removing 10 % of it just meant the same thread finished 10 % sooner —
still past the 11.1 ms line, still 45 Hz. Only *crossing* the line changes anything, and none of
these was big enough. Graphics jobs crossed it by a factor of nine.

This is the general lesson worth keeping: **when an app is rate-locked, partial wins are invisible.**
Every measurement in rounds 1–6 was correct and every conclusion drawn from "it changed nothing" was
wrong, because "changed nothing" was the expected reading for *any* improvement that did not cross
the threshold.

## 4. Two dead ends, and why they are dead

- **Single-pass stereo** would halve the passes. Impossible here: the game's shaders declare
  `STEREO_INSTANCING_ON` in their keyword tables but ship **no compiled sub-program carrying it**,
  and a Unity player contains no shader compiler, so the variant cannot be created at runtime.
  Bytecode-patching it is a vertex input/output *signature* change across every variant, not a
  tweak. See `Core/StereoModeConfig.cs`.
- **GPU instancing** is already enabled on 1568 material slots and buys nothing, because instancing
  merges renderers sharing a material **and a mesh**. The dungeon is Apparance procedural geometry —
  nearly every renderer has a mesh of its own. (This is also exactly why static batching *could*
  help: it does not care that the meshes differ.)

## 5. What shipped

- **`[Core] EnableGraphicsJobs`** (default **on**) — the preloader writes `gfx-enable-gfx-jobs` and
  `gfx-enable-native-gfx-jobs` into `GH_Data/boot.config`. It cannot be done in-process: the
  engine picks its job mode before managed code exists. **Takes effect at the next game start.**
  Original backed up once to `boot.config.gloomhavenvr-backup`; setting the entry to `false` writes
  the keys back to `0`. An explicit `-force-gfx-jobs` in the launch options wins and the file is left
  alone.
- **`[Core] AutoRestartForGraphicsJobs`** (default **on**) — closes and reopens the game on the one
  boot where the keys are newly written, so "takes effect at the next game start" costs the player
  nothing. A detached `cmd` waits `RelaunchCommand.DelaySeconds` for this process to be gone before
  starting the game, because a process cannot restart itself and a single-instance check would
  answer the overlap by killing the *new* process. Steam is relaunched through
  `steam://rungameid/$SteamGameId` so the overlay and playtime counter attach normally. Four brakes
  against a loop: the config entry, an env var on the relaunched process, a persistent counter
  capped at 2, and the counter being cleared *only* by booting with jobs actually on. The command
  string is source-linked into `scripts/wire-tests.sh` — a quoting defect there is undiscoverable in
  place, since the process that would report it is the one being killed.
- **Startup diagnostic** — `OpenXRBootstrap` reports whether graphics jobs are on, from the command
  line or from `boot.config`. There is no runtime API for it, so the line states its evidence rather
  than a verdict it cannot support.
- **The discarded desktop render** is skipped unconditionally now — it was a switch only because
  "expected to be invisible" was not yet "measured".
- **Static batching** stays, off by default. It works (1666 renderers → 47 meshes, submitted
  material slots 1208 → ~132, 97 MB, one 60 ms load hitch, fully reversible) and it was worth ~8 ms
  *before* graphics jobs. Whether it is worth anything *after* is **unmeasured** — with the main
  thread at 1.8 ms there is almost nothing left there to win. See `STATIC-BATCHING.md` §0.

## 6. Still open

- **Is static batching worth keeping at all now?** One clean A/B answers it: graphics jobs on,
  batching off for a minute, on for a minute. If the difference is nil, it should be deleted rather
  than left as a setting nobody can reason about.
- **Stability of graphics jobs over a long session** — threaded submission with VR can be finicky.
  Watch for crashes, flicker, wrong rendering, and whether it survives scene loads and headset
  doff/don.
- **`STALLS: 9 logic frames over 100 ms`** appeared in one window. Almost certainly scene loading,
  but unconfirmed.

## 7. Instrumentation — kept, and why

`[Perf] FRAME / STEPS / SPIKE / SPLIT / SCENE / GFX / MARK` stay on by default. They cost a few
microseconds per frame and they are the only reason this was solvable at all: the decisive step was
`SPLIT` separating *main-thread render loop* from *blocked*, which is what turned "the GPU counter
says we are at the frame interval" into "the main thread owns the frame". A regression here would
otherwise be invisible again.

Two instrument defects found the hard way and worth remembering:

- **`FRAME`'s `gpu` counter reads the frame interval whenever the runtime is rate-locked** and is
  blind to everything upstream. Four rounds were read off it before `SPLIT` existed.
- **`eyeTextureDesc.msaaSamples` describes the submitted swapchain image**, which is single-sample by
  definition. A `VERDICT` line inferring "MSAA is not binding" from it was wrong, was believed, and
  cost a whole round. It is retracted at `Rig/RenderQuality.cs`.

---

# The wall-fade hitches — ModBuild 278 round (open)

> **Where it stands:** the frame now *lands* on budget (p50 11.10 / 11.72 / 11.98 ms across three
> 30 s windows of ModBuild 277) and the user says so — *"Die Ruckler sind deutlich weniger
> geworden, aber immer noch ein wenig vorhanden."* What is left is the tail: p99 26.5–29.9 ms,
> max 125–148 ms. The max is one thing and one thing only, and it is named below.

## 1. The two "Abtastraten", and why the distinction is the whole round

The user asked (2026-08-25) for the sampling frequency to be settable:

> "Würde es helfen hier die Abtastrate, also Frequenz in dem gecheckt wird ob eine Wand etwas
> verdeckt, etwas zu verringern? Am Besten lass sie in den Einstellungen selber einstellen können."

There are **two** cadences in this subsystem, his sentence describes one of them, and his symptom
is caused by the other. Both are dials as of ModBuild 278.

| | what it is | cost shape | ModBuild 277 measurement |
|---|---|---|---|
| `[WallFade] RescanIntervalSeconds` | rebuild the **table** of which renderer belongs to which wall; ends in one atomic commit frame | rare, enormous | 33 commits, mean worst-commit **85.6 ms**, max **134.0 ms** |
| `[WallFade] EvalIntervalSeconds` | **check** whether a wall hides the floor you are looking at (`UpdateSampleVisibility` + `BlockedFraction`) | constant, small | part of a ~43 ms/s per-frame residue — see section 3 |

**The commits are the Ruckler.** Ten frames in the ModBuild 277 log ran 78–148 ms with the mod
owning 74–142 ms of them, every one led by `WallFade.Rescan` / `WallFade.Commit.*`. The prior
round report is unambiguous: *"Die kurzen Hänger sind noch da — und es liegt definitiv an der
Wandausblendung. Ich habe sie im Test testweise deaktiviert und die Hänger waren weg."*

## 2. Why the commits happen at all — 28 of 33 on one term

PERF S5 skip works. Totalled over all 38 `SKIP` clauses in the log:

```
80 of 113 judged cycles SKIPPED the commit outright.  WHY A CYCLE COMMITTED:
  2 no table yet        1 room reveal        0 asked for       0 dissolve material swap
  0 board moved        28 SCENE SIGNATURE MOVED    2 wall signature moved
  0 STALENESS CEILING   0 segment AABB drift
```

**28 of 33.** The refusal own text ends by naming the next question — *"IF THIS IS THE COUNT
THAT DOMINATES … the next round question is WHICH renderers, not whether to skip"* — and
nothing shipped could answer it. ModBuild 278 ships the instrument that does
(`Core/WallSegmentFadeCulprits.cs`, log line `SIGNATURE CULPRITS:`).

Two things about that table constrain what the next round may conclude:

- **`asked for` = 0 across the entire log.** All six sites that zero `_nextRescan` for a mid-fade
  regeneration fired exactly never in this session. That counter is now also the falsifier for
  the cadence dial (section 5).
- **`STALENESS CEILING` = 0 and `segment AABB drift` = 0.** The fail-safes never fired, so the
  signature has no *demonstrated* blind spot. Nothing here argues it is complete — only that
  nothing has caught it being incomplete.

## 3. What the per-frame half actually costs — read inclusively

`WallFade.Late` wraps the whole tick, and `PerfMonitor` attributes nested scopes **individually,
not exclusively** (`EndStep` folds the full duration into the name; only the mod-*total* avoids
double counting). So `WallFade.Late 94.3 ms/s` is not the decision cost — it contains every
other `WallFade.*` scope. The last 30 s window (2524 frames, ~84 fps), taking `STEPS` and
`STEPS TAIL` together:

```
WallFade.Late      94.3 ms/s   (inclusive of everything below)
  Rescan           27.2        of which Commit.WallCache 9.4, PropUnits 8.4, Mounted 6.6, Stacked 1.2
  PathAudit         8.4
  FastReclaim       7.5        of which FastSweep 4.9
  Prepare           4.1
  Classify          2.2
  Survey / Census / Sweep      each below the 1.0 ms/s print cut
                  ------
  level-1 children ~51 ms/s   =>  Late-EXCLUSIVE  ~43 ms/s  (~0.43 ms/frame)
```

That ~43 ms/s is the decision **plus** the per-frame fade ramp, the material writes,
`ApplyCornerPieces`, the fade-write census and the inside/walk-in tests. **Only the decision half
is gateable.** Any claim that `EvalIntervalSeconds` recovers "94 ms/s" would be attributing the
commit own cost to the dial that cannot touch it.

**One clean natural experiment supports the residue being real.** A different 30 s window reads
`WallFade.Late 1.054 ms avg, worst 8.15 ms, 71.2 ms/s, frames 2027`. `Late` is inclusive, so a
worst frame of 8.15 ms means **no commit landed in those 30 seconds at all** — and the subsystem
still cost 71.2 ms/s. The per-frame cost is not an artefact of the spikes.

## 4. How high may `EvalIntervalSeconds` go? — the derivation

The code comment at the gate argues the interval is safe "by construction" because the decision
is already an EMA + Schmitt trigger + second-scale dwell. That is an argument. Here is the
measurement of the argument.

**The EMA claim is TRUE and was verified, not trusted.** The step is
`fracStep = 1 - exp(-evalDt / FractionTauSeconds)` with `evalDt` the time since the last
*evaluation*, applied as `Smooth += (fraction - Smooth) * fracStep`. That is the exact
zero-order-hold discretisation of `ds/dt = (x - s)/tau`: for a piecewise-constant input the
trajectory at the sample instants is **exact for any step size**. The filter own dynamics are
therefore interval-invariant; what a longer interval changes is only the *sampling* of a moving
input. (The `Mathf.Min(evalDt, 0.5f)` clamp cannot bind: the dial ceiling is 0.25 s.)

**The binding constraint is the shortest dwell, not the EMA.** Shipped dwells:
`EnterDwellSeconds = 0.20 s` (before a wall may go transparent — a `private const`, not a dial)
and `2.50 / 7.00 s` before it may come back. With interval `T`, `PendingSince` is stamped at an
evaluation instant and the flip is only *tested* at evaluation instants, so the number of
consecutive agreeing evaluations a state change requires is `n = ceil(dwell / T)`:

| T | n for the 0.20 s dwell | worst added latency (detect + quantise) |
|---|---|---|
| 0 (every frame @ 90 Hz) | 18 | 0 |
| 0.02 | 10 | 0.04 s |
| **0.05** | **4** | **0.10 s** |
| 0.10 | 2 | 0.20 s |
| 0.20 | **1** — the dwell stops debouncing | 0.40 s |

**So the honest answers are three, and they are different questions:**

1. *"The largest T at which no fade decision changes at all"* is **T = 0**. Any T > 0 moves *when*
   a decision lands by up to `2T`. The code comment real claim — *which* walls fade is
   unchanged — is the right claim and it holds up to (3).
2. *"The largest T at which the hysteresis mechanism survives"* is **T <= 0.10 s**: above
   `dwell/2` the 0.20 s dwell degenerates to a single confirming sample, which is no debounce.
3. *"The largest T whose added latency stays inside the fade own visual smear"* is **T <= 0.06 s**,
   from `2T <= FadeTauSeconds (0.12 s)`.

**RECOMMENDATION — `0.05` (20 Hz), and it is a recommendation, not a shipped change.** It clears
all three bars: four confirming samples, 0.10 s worst-case added latency (inside the 0.12 s ramp
constant), and `T/tau_EMA = 0.33` so the smoothed trajectory is sampled three times per time
constant. The shipped default stays **0** in both `[WallFade] EvalIntervalSeconds` and
`[Optimize] WallFadeEvalInterval` — changing it silently on a surfacing commit would also
override whatever a returning tester already has in his `perf.cfg`.

**What 0.05 buys, bounded rather than promised:** at ~84 fps it removes 76 % of the decision
own share of the ~43 ms/s residue. If the decision were *all* of that residue the saving would be
~33 ms/s (~0.39 ms/frame); it is not all of it, so the true figure is lower. That is between
1.7 % and 3.5 % of an 11.11 ms budget — worth having, and **not** what the user is reporting.

## 5. The cadence dial own trap, and its falsifier

`_cycleOpenedEarly` detects an *asked-for* cycle by comparing the gap since the last cycle against
`RescanIntervalSeconds - 0.05`. With the interval a live dial, that comparison must read the value
the cycle was **scheduled** with (`_scheduledRescanInterval`), not the live one. Reading live is
wrong in both directions; one direction is catastrophic — if the gap were ever routinely *below*
the compared value, every cycle would read as asked-for, every cycle would commit, and the 71 %
skip would fire never, silently.

**The falsifier is shipped and it is the `asked for` counter on the `SKIP` clause.** It read 0
across the whole ModBuild 277 log and must still read 0 in a session where nothing regenerates,
whatever the dial is set to. The clause now also prints the scheduled interval beside the live
one, so a divergence is visible in a window where no cycle was asked for.

## 6. What raising `RescanIntervalSeconds` does and does not buy

It divides the **number** of ~90 ms commits and shortens **none** of them. And it divides them by
*less* than the ratio, because the churn is bursty rather than continuous: 71 % of cycles already
find nothing, so doubling the period does not simply halve the commits — it halves the
*opportunities*. How much of that turns into fewer commits depends on the churn timescale,
which is exactly what the section 2 instrument measures. **Do not tune this dial past 4.0 before
reading a `SIGNATURE CULPRITS:` line.**

The cost is decision latency, and it is already printed: the `SKIP` clause
*"DECISION LATENCY — the table in force stood at most X s without a rebuild"*.

## 7. Still open

- **Which renderers move the scene signature.** The whole point of the round. If they are
  particle systems, torch flames toggling `activeInHierarchy` or figures animating renderers in
  and out, the commits are paid for nothing and the signature should hash a narrower set. If they
  are wall masonry, the skip is already right and the next attack is the commit own
  ~91 ms (`WallCache` 35.2 avg / 57.9 worst, `PropUnits` 31.5 / 40.9, `Mounted` 24.7 / 26.1).
  **The instrument first output is a hypothesis** — its NULL and known-positive controls run in
  the wire suite, but nothing has yet validated it against a real scene.
- **The ~91 ms commit itself.** Untouched this round. The three phases live in
  `WallSegmentFade.PropUnit.cs` and `WallSegmentFade.Mounted.cs`, and the fade behaviour there is
  now reported correct — so any change needs a gate proving the resulting segment table is
  *identical*, not "looks right". No cheap such gate exists yet; see section 8.
- **`PathAudit` at 8.4 ms/s** is a pure diagnostic and the second-largest per-frame cost in the
  subsystem. It is suspended inside the walk-in mode as of ModBuild 278 but runs everywhere else.
  `[Optimize] QuietDiagnostics` already switches it off entirely.

## 8. Why A5 (the commit own cost) was not started

The brief allowed it if a cheap gate could prove the rebuilt segment table identical. It cannot be
built cheaply from where the code stands: the two expensive phases (`EnforcePropUnitCohesion`,
`CollectWallMountedProps` — 54 of the 91 ms) **mutate segments in place**, moving renderers between
owners and hiding and restoring pieces. A comparison gate would need a serialisable canonical form
of the whole table (per segment: owner key, ordered renderer instance IDs, bounds, the mounted and
stacked member sets, the run assignment) captured before and after, which is a new instrument of
its own — and by this project own rule its first output would be a hypothesis needing its own
controls. That is a round, not a task inside one. Left undone deliberately.
