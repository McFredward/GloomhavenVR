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
