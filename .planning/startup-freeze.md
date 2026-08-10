# Startup freeze after the intro — measurement, what was done, what was declined

**Status:** measured and partially addressed (ModBuild 108). The remaining lever is a bundle
rebuild, which the **user declined on 2026-08-11** — see §5. Do not re-propose it without new
information.

User report (verbatim, 2026-08-11, hardware, ModBuild 107 / `b765a5b`):

> "Am Anfang wenn man das Spiel lädt gibt es immer noch eine kurze Phase in der das ganze Spiel
> hängt nach der Intro. Dann nach einer kurzen Freeze-phase in dem auch die VR hände hängen sieht
> man das Ladesymbol und dann das Hauptmenu - ist es möglich das Laden des Spiels hier so zu
> entkoppeln dass ich während dessen kein Freeze habe und stattdessen nur das Ladesymbol sehe und
> meine Hände weiterhin flüssig benutzbar sind?"

## 1. The boot timeline, from `Player.log` of that run

Wall clock is anchored on the EOS / LocalizationManager ISO timestamps; `RealTime 0 ≈ 20:31:27.4`.

| Wall clock | Player.log | Event |
|---|---|---|
| 20:31:31.0 | 216 | `[Bootstrap] Load scene started … RealTime:3.596742s`; mod frame 1 |
| 20:31:31.0–32.4 | 257, 461 | **frames 2–4 stall 1717 ms, of which the MOD is 1093 ms** — see §4 |
| 20:31:32.4–41.0 | 500, 503 | intro: 729 frames / 8.6 s, p50 11.04 ms — smooth |
| 20:31:41.0 | 533 | `[Bootstrap] Loaded scene in:10051ms`; scene activates |
| 20:31:41.0 | 540 | `Start SceneController` — **before** the freeze, not after |
| 20:31:41–~43.7 | 695–1518 | **the reported freeze** — see §2 |
| ~20:31:43.7 | 1517 | the game's own `Showing loading screen.` |
| 20:31:47 | ~1595 | `MainMenuUIManager OnEnable Start` |

Intro-end → menu is **6.0 s**: ~2.7 s hard freeze, then ~3.3 s already covered by a spinner.

## 2. The freeze, split in milliseconds

| frame | total | **mod** | what the GAME did in it |
|---|---|---|---|
| 759 | 569.00 | 17.40 | scene activation: `Awake`/`Start` of `Gloomhaven_unified`, Apparance start, FFSNetwork init |
| 838 | 25.12 | 1.19 | `Loading Global Data` |
| 840 | 1089.68 | 1.31 | Steam/checkpoints, **Guildmaster ruleset YML = 794 ms** |
| 849 | 933.04 | 1.29 | **Campaign ruleset YML = 884 ms** |
| 883 | 50.00 | 0.09 | `Showing loading screen.` |
| **Σ** | **2667 ms** | **21.28 ms (0.8 %)** | **YML ruleset parsing = 1678 ms = 62 %** |

The `[YML] … Duration:` lines are the game's own measurement, not an inference.

**Why the hands freeze:** Unity polls XR poses, runs the player loop and submits to the
compositor on the one thread the parse is blocking. There is no managed-code arrangement that
keeps them moving. OpenXR is clean throughout (`XR_SESSION_STATE_FOCUSED` from 20:31:32.36, no
swapchain complaint) — VDXR is already reprojecting, which is why the picture warps rather than
blacking out. There is no compositor-side win available.

## 3. What WAS done (ModBuild 108)

`WorldUI/LoadingIndicator.cs` grew a **boot clause**: the spinner now also arms while VR runs
and the game's own loading state is not yet reachable, covering the whole window instead of only
its tail.

- **Arming edge:** `Bootstrap._loadScene != null`, read by reflection. In `ShowSplash` the game
  assigns that field in the statement right after the intro completes, so it *is* the edge
  "intro over, unified load begun". It is a state poll, not an event, so it is correct whenever
  the mod starts looking. Rejected on evidence: the active scene name still reads `Intro` for
  the whole window (the game's `UnloadSceneAsync(Intro)` is refused), and no scene event fires
  at this edge — `sceneLoaded` for the unified scene only fires 8 s later, at the end.
- **Retirement** is a one-way `BeforeIntro → Covering → Done`, `Done` absorbing, so a later
  `SceneController.OnDestroy` cannot re-arm it mid-session. **Note the deliberate deviation from
  the original brief**, which said to latch off once `SceneController` exists: the log refutes
  that edge — `Start SceneController` is line 540, *before* all three stall frames, so retiring
  there would drop the spinner exactly at the freeze it exists for. Retirement is instead the
  first of: the ordinary poll going true (the seamless handover, and the expected path),
  `MainMenuUIManager.Instance` existing, or a 90 s runaway cap. The last two are pure safety: a
  clause stuck ON would also hold `FlatScreenSuppressed` and hide the game forever.

**What the user actually gets, stated without varnish:** ~11 s that were an empty void now carry
a running spinner — and the spinner itself still stands still for 569 ms, 1090 ms and 933 ms at
the three main-thread frames, with the hands frozen alongside. **The freeze is not one
millisecond shorter.** This changes what he looks at, not whether the main thread stalls, and he
was told so before it was built.

Cosmetic consequence: during the boot window the spinner is the procedural ring, not the game's
art — `LoadingScreen` is a serialized reference *inside* the scene being loaded, so there is
nothing to read yet. Marked provisional and dropped on the next hide, so every later load shows
the game's own spinner; the swap never happens on screen.

## 4. The SECOND stall — ours, and not the one he reported

Frames 2–4, under the black Unity splash: 1093 ms of a 1717 ms stall is the mod, essentially all
of it `Hands.Rig` = **981.48 ms** (`[Perf] STEPS`: that step averages 1.340 ms over 733 frames,
worst 981.48 — a one-off).

**It is one LZMA inflate.** Parsing the shipped `prebuilt/gloomhavenvr.bundle` directly: the
`0x243` flags field describes the 88-byte blocks-info DIRECTORY (LZ4HC); the PAYLOAD is a
**single block, 29,416,152 B compressed → 57,998,411 B uncompressed, compression type 1 = LZMA**
(nodes `CAB-a8d3…` 12.7 MB + `.resS` 45.3 MB). Self-consistent: 29,416,152 + the 160-byte header
= the file's exact 29,416,312 bytes. That is what `BuildAssetBundleOptions.None` produces in
`unity/GloomhavenVR.Assets/Assets/Editor/BuildBundles.cs`. For an LZMA bundle `LoadFromFile`
cannot read on demand — it inflates all 58 MB into memory before returning, single-threaded, on
the calling thread. At the 50–70 MB/s one LZMA decoder manages, that is 0.8–1.2 s.

Addressed in ModBuild 108 by `HandVisuals.Prewarm()`: `LoadFromFileAsync` from
`HandsDriver.Awake` (chainloader time, before frame 0) performs the same inflate on Unity's
loading thread; `GetBundle` later blocks on the remainder only. No visible behaviour change —
`Build` still returns a complete `HandRig` synchronously on the same frame as today. Saving is an
**estimate**: residual ~300–400 ms if the loading thread gets ~600 ms of slack, zero if it gets
the whole window. Three `PerfMonitor` scopes (`Hands.BundleLoad` / `Hands.PrefabLoad` /
`Hands.GloveSpawn`) now split the step so one log line settles it.

Two latent bugs fixed with it: `HandVisuals` called `LoadFromFile` unconditionally while
`WorldUIAssets` and `VRCardFactory` both probe `GetAllLoadedAssetBundles()` first (it worked only
because it happened to be first — any future warm-up would have hit Unity's duplicate-load
refusal and silently dropped the hands to procedural), and `UnloadBundle` called `Unload()`
unconditionally, which would have pulled the other two consumers' assets out from under them once
adoption succeeded.

Bounded hazard, stated in the code: while the prewarm is in flight the bundle is in no registry,
so the other consumers' adoption probes cannot see it. Unity exposes no way to observe an
in-flight load, so the window is bounded instead — chainloader time to the first pump that sees
`isDone`, at the latest the hand build in frame ~4, while both other consumers only reach the
bundle inside a scenario hundreds of frames later.

## 5. DECLINED BY THE USER — do not re-propose without new information

`BuildAssetBundleOptions.None` → `ChunkBasedCompression` in
`unity/GloomhavenVR.Assets/Assets/Editor/BuildBundles.cs` would make `LoadFromFile` a header read
and inflate only the blocks actually touched — removing the §4 stall **at the source** and the
58 MB resident cost with it. It is a one-word change plus a bundle rebuild with
`/home/claw/unity-2021.3.5` (the wrong editor silently produces a bundle that loads nothing) and
a refreshed identity in `refactor-guard`.

Put to the user on 2026-08-11 with that trade-off. His answer, verbatim:

> "Dokumentier was du bisher gemacht hast und lass aber erstmal davon ab. Ich denke für die 2s
> lohnt sich der Aufwand nicht"

This document is that record. If it is ever revived, note that the prewarm in §4 becomes harmless
and near-free rather than redundant — the two are complementary, not alternatives.

## 6. What the next hardware log settles

- `[Hands] PREWARM realized: done=… progress=… at frame N` — `done=True` means the async load beat
  the hand build and the 981 ms one-off is gone entirely; `done=False` says how much remained.
- `[Perf] SPIKE frame N: … worst steps: …` — `Hands.BundleLoad` is the residual inflate;
  `Hands.PrefabLoad` + `Hands.GloveSpawn` are the deserialization/upload/instantiate cost that no
  prewarm can move.
