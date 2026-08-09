# Zoomed-out overview performance — measurement and strategy proposal

**Source:** `.planning/debug/LogOutput.log` + `Player.log`, single-player session of 2026-08-09,
ModBuild 102, commit f200f7f. RTX 4090 / 7800X3D, Quest 3 over Virtual Desktop (VDXR), 90 Hz,
MultiPass, graphics jobs **ON** (`LogOutput.log:24`).

**User report:** far-zoomed overview of a large room with many enemies drops visibly; moving in
close runs distinctly smoother.

Everything in §1–§4 is read out of the log or the source and is marked **PROVEN** or **INFERRED**.
§5 is the strategy set.

---

## 1. What the log actually shows

The session contains two scenarios. The scenario change is at `LogOutput.log:10906`
(`room registry REVEAL re-anchor: 1→2 LOGICAL room(s)`, story modal at `:10969`), and the renderer
census doubles across it. Three states are cleanly separable:

| | **A — small room** | **B — big room, overview** | **B — big room, close in** |
|---|---|---|---|
| log line (SPLIT) | `:10815` | `:11954` / `:12282` / `:12568` | `:12902` |
| frames in 30 s | 2472 (**82 fps**) | 979 / 1003 / 989 (**33 fps**) | 1314 (**44 fps**) |
| frametime **p50** | **11.10 ms** | 28.76 / 27.74 / 27.50 | **21.56 ms** |
| logic p50 (Update→LateUpdate) | **3.48** | 14.63 / 13.53 / 13.07 | **8.65** |
| render loop p50 (cull+submit) | **1.73** | 3.02 / 3.46 / 3.57 | **2.84** |
| **main-thread work p50 (logic+render)** | **5.21** | **≈ 17.1** | **11.49** |
| blocked (remainder) | 5.61 | 9.46 / 9.51 / 9.64 | 9.03 |
| mod (instrumented scopes) | 1.56 ms/frame | 4.28 / 4.10 / 4.17 | 3.18 |
| **mod per SECOND** | **128 ms/s** | **138 ms/s** | **139 ms/s** |
| scene renderers / enabled / visible | 1583 / 1299 / 950 | 3051 / 2738 / 1925 | 3052 / 2783 / 2129 |
| uGUI graphics / enabled / mod-layer | 1553 / 1293 / 13 | 1907 / 1593 / 12 | 2017 / 1672 / 11 |
| logic **p95** | 14.95 | 38.25 / 37.28 / 36.63 | 32.89 |

Budget at 90 Hz is **11.11 ms**.

### 1.1 The overview / close-in split is real and is not a game-state artefact — **PROVEN**

`VRHeartbeat` prints frames-per-10-s plus the head pose every 10 s (`Core/VRHeartbeat.cs:41`,
`IntervalSeconds = 10f`). Within scenario B:

```
hb#144–156  32.1 – 38.7 fps   head y 7.1 – 17.4
hb#157      48.9 fps          pos(19.50, 2.02, 7.41)   ← low, inside the map
hb#158      48.4 fps          pos(12.23, 8.64, 0.94)
hb#159      48.0 fps          pos(15.82, 2.94, 6.03)   ← low, inside the map
hb#160      45.7 fps          pos( 5.76,11.44,-1.01)
hb#161      34.7 fps          pos( 2.72,16.96,-2.78)   ← high, back to overview
```

Competing causes checked and **neutralised**:

* **Board scale did not change.** The tooltip lines print it: `board scale 26.372` at `:11784`
  (33 fps stretch) and at `:12821`–`:13048` (48 fps stretch). So this is not a board-rescale effect.
* **Game state did not change.** `[Cards] fan state: mode=ActionSelection … ` and
  `[Board] [Focus] the game is waiting on NOBODY` hold across *both* the 33 fps and the 48 fps
  stretch (`:11840` … `:13035`). No enemy turn, no animation phase difference.
* **Scene content did not change.** Renderer census 3051 → 3052, enemy count unchanged.

The only variable left is where the head is. **The frame rate is view-dependent.**

### 1.2 Which layer owns the frame — **PROVEN**

The instrument's own verdicts:

* Scenario A: *"no single layer dominates (logic 39 %, render 15 %, blocked 46 %)"* — healthy.
* Scenario B overview: **"MAIN-THREAD LOGIC owns the frame (56–59 %)"**.

Main-thread work (logic + render loop) is **17.1 ms** against an 11.11 ms budget in the overview.
That alone makes 90 Hz arithmetically impossible there, *before* any GPU consideration.

This is a **different wall** from the one closed in `.planning/perf/FINDINGS.md`. That investigation
found the render loop at ~18 ms on one thread; graphics jobs took it to 1.8 ms. This log confirms
graphics jobs are working (render loop 1.7–3.6 ms). The render loop is no longer the problem.
**Logic is.**

### 1.3 The frame is compositor-quantised — **PROVEN for A, INFERRED for B**

Scenario A's frametime p50 is 11.06–11.18 ms across twenty consecutive windows — that is 1 × 11.11,
to within noise, and `over-budget` sits at 47–50 % because the app is riding exactly on the line with
**zero headroom**. Scenario B close-in sits at 21.56 ms ≈ 2 × 11.11. The overview sits at 27.5–28.8 ms,
i.e. a mixture of 2× and 3×.

`FINDINGS.md` §3 already established this on this hardware, in one sentence worth repeating:
**when the app is rate-locked, partial wins are invisible.** Only *crossing* a multiple of 11.11 ms
changes the number the user feels. This governs everything in §5.

### 1.4 GPU time is **UNKNOWN** — and that is a real open risk

`FRAME` prints `gpu n/a (this runtime exposes no GPU-time counter)` and
`Unity FrameTimingManager n/a (this player was built without frame-timing stats)`. There is no GPU
number in this session at all. What we do know:

* Eye textures are **3072 × 3264 per eye**, **8× MSAA**, MultiPass = **160.4 Msamples/frame**
  (`Player.log:471`, `:726`, `:2816`). MSAA is asserted by the mod itself (`Player.log:220`,
  `[Rig] MSAA (re)asserted 0x → 8x`).
* The 2026-07 pixel-budget refutation (11× sample cut → nil; MSAA 8× → off → nil; shadows off → nil)
  was carried out at ~1600 renderers **and under a 45 Hz rate lock**, i.e. in exactly the regime where
  FINDINGS.md itself says any sub-threshold win reads as zero. The big room has **3049 renderers**.
* So: the CPU is provably over budget. Whether the GPU is a **second, independent wall** in a
  3000-renderer room has never been tested and cannot be read from this log.

### 1.5 One instrument defect worth naming

The scene census counts visibility as `Renderer.isVisible` (`Core/PerfFrameSplit.cs:728`), which in a
scenario is decided **by the head camera alone** — the game's own cameras are retargeted to a sink and
have their culling mask zeroed for the duration of their own render
(`WorldUI/FlatScreen.3.Desktop.cs:244`, `OnScrubPreCull`). So the "visible" count *is* in principle a
zoom axis. But it is sampled **once per 30 s window** ("the per-frame cost of this walk would itself be
a stutter"), so it is a single instantaneous sample and shows no usable trend — 1925 in an overview
window, 2129 in a close-in one. This is why §5 opens with a measurement round.

---

## 2. The known fixed-rate sweeps — are they implicated?

Asked explicitly; answered from source plus the STEPS lines.

### `WallFade.Late` — **NOT per-frame constant; it is a 2 s burst that scales with SCENE SIZE**

| | small room | overview | close in |
|---|---|---|---|
| ms/s | 40.7 | 56.6 | 47.2 |
| avg per frame | 0.494 | 1.734 | 1.078 |
| worst frame | 50.8 ms | 67.0 ms | 65.7 ms |

The median cost on sampled spike frames is **0.11–0.14 ms**, against a mean of 1.73. So ~94 % of
`WallFade.Late`'s total sits in a rare tail: **≈ 52 ms/s is burst**, delivered as 50–97 ms events.
At 90 Hz one such event is 5–9 dropped frames.

Source (`Core/WallSegmentFade.cs`): `RescanIntervalSeconds = 2f` (`:428`), and `Rescan` is *also*
forced immediately whenever a tracked renderer went null mid-fade (`:1660`, `Stacked.cs:383`,
`Mounted.cs:466`, `Body.cs:206`, `Stacked.cs:1058`) or a room reveals. `Rescan` itself contains
**three full-scene `FindObjectsOfType` walks** — `TilesOcclusionVolume` (`:1719`),
`UnityGameEditorDoorProp` (`:1923`) and **every `Renderer` in the scene** (`:1945`) — plus
segments × candidate passes run **4 rounds** (`Stacked.cs:174`), a double ground-strip, and a
segments² gate-link pass (`Gate.cs:571`).

Two more uninstrumented burst sources sit inside the same scope:

* `FastReclaimRegeneratedShell` — `FindObjectsOfType<MeshRenderer>()` **every 0.25 s while any wall
  is held faded** (`Stacked.cs:453`). Walls are faded precisely in the overview, so this one *is*
  zoom-correlated.
* the wall heartbeat/census block (`:874`–`:998`) — re-arms on any ±5 segment-count change, calls
  `FindObjectsOfType<MeshRenderer>(includeInactive: true)` (`:2613`) and builds a ~40-line string.
  **It is not gated by `QuietDiagnostics`.**

**Scaling:** with total scene renderers and with tracked wall segments. **Not** with enemy count, and
**not** with what is currently visible. It rose 40.7 → 56.6 ms/s exactly as the scene grew from 1583
to 3051 renderers.

**Existing knob:** `[Optimize] WallFadeEvalInterval` (default 0 = every frame) throttles **only the
cheap half** — the ≤ 96-sample visibility pass (`MaxTotalSamples = 96`, `:427`) and the ~16-ray
per-segment coverage test. It does **not** gate `Rescan`, the heartbeat census, `Apply*`,
`ApplyCornerPieces` or `FastReclaim`. Turning it on is worth ~10 ms/s, not 50.

### `Compat.LoaderHeal` — **fixed-rate, 1 Hz, scene-size-scaled, zoom-independent**

22.3 ms/s (small room) → 23.0 ms/s (big room). Flat. `Core/MaterialLoaderHeal.cs:380`: the Update
early-outs until `ScanInterval = 1f` (`:333`), then does a scene-wide
`FindObjectsOfType<ProceduralMapTile>()` (`:427`) plus a `GetComponentsInChildren<MaterialLoader>` per
tile (`:433`) and a walk of every `LoadersData` entry. **One ~23 ms hitch every second** — two dropped
frames per second at 90 Hz, from a sweep the file's own comment calls "largely redundant" now that the
Harmony registry at `:411` exists. All intervals are `private const`; there is no config knob.

### `UnseenTiles` — **fixed-rate burst**

10.4–10.7 ms/s in every window. `UnseenTiles.Rescan` reports `20.2 ms avg, 15 frames per 30 s` — i.e.
essentially the *entire* cost is a 20 ms hitch every 2 s. The per-frame remainder is ~0.2 ms/s.

### `Surface:InitiativeTrackSurface` and `CanvasConversion.Order` — **genuinely per-frame, and they scale with enemy count**

These two are the mod's real per-frame load: 0.786 + 0.312 = **1.10 ms every frame** in the big room.
Because they are per-frame, their ms/s figure *rises* as the frame rate rises — at 90 Hz they would
cost ~99 ms/s. See §5-S2 for the named defects.

---

## 3. The mod's total share — **PROVEN**, and it is larger than the headline suggests

| | small room | overview | close in |
|---|---|---|---|
| mod, per second | 128 ms/s (**12.8 %** of wall clock) | 138 ms/s (**13.8 %**) | 139 ms/s (**13.9 %**) |

The mod burns roughly one eighth of every second, in **every** state. At 90 Hz that is **1.4–1.5 ms of
an 11.11 ms budget**; in the big room, scaled to 90 Hz, it would be **≈ 2.6 ms/frame** (≈ 1.65 ms of
genuine per-frame work plus ≈ 0.95 ms/frame of amortised bursts).

**Caveat, stated plainly:** the `mod` figure counts only instrumented scopes — 20 `PerfMonitor.Scope`
sites and 38 `TickGuard.Run` sites. The mod's **87 `[HarmonyPatch]` bodies are not counted**; they
execute inside game methods and are attributed to the game's "logic". The heaviest per-frame patched
paths are singletons (`WorldspaceStarHexDisplay.Update`, `Controller.CommonLoop`) or are *prefix-skips*
that remove game work (`WorldspaceDisplayPanelBase.TrackCharacter`/`LateUpdate` for adopted bars), so
the blind spot is probably small — **probably, not proven.**

---

## 4. Why zoomed out is worse

### 4.1 What it is **not** — each of these is eliminated, not merely untested

* **Not the mod's own sweeps.** Mod work per second is **138 ms/s in the overview and 139 ms/s close
  in** — identical. The mod does not scale with zoom. *(PROVEN, §1 table.)*
* **Not draw-call submission.** The render loop moves only 2.84 → 3.35 ms p50 across the boundary;
  the head camera 2.35 → 2.87 ms. That is 0.5 ms of the 6 ms delta. Graphics jobs already moved
  submission off the main thread. *(PROVEN.)*
* **Not overdraw / fill / MSAA / shadows,** at least not as the *zoom* mechanism: `blocked` is
  **9.03 ms close in and 9.46 ms in the overview** — flat. If fill were the zoom driver, `blocked`
  would be where the 6 ms landed. *(PROVEN that it is not the delta. Whether the GPU is a separate
  absolute ceiling in the big room is §1.4 and remains open.)*
* **Not canvas rebuilds.** Canvas rebuild lands in `blocked` (Unity rebuilds in
  `PostLateUpdate.PlayerUpdateCanvases`, after the logic span and before the render span — the
  instrument documents this at `PerfFrameSplit.cs:705`). `blocked` is flat. *(PROVEN.)*
* **Not the fog-tile order driver.** `UnseenTiles` work counters *fell* in the big room
  (9096 → 1470 hexes/s) and its ms/s is flat. *(PROVEN.)*

### 4.2 What it is — **INFERRED, by elimination, and testable in one config change**

The +6 ms lands entirely in the **logic span**, and the mod's measured share of that span is flat.
The logic span covers all game `Update`, all of `PreLateUpdate` — which is where Unity runs
**Animator** and **ParticleSystem** simulation — and all `LateUpdate`.

The only Unity mechanisms that make *Update-phase* cost depend on where you stand are
visibility-gated simulation: `Animator.cullingMode`, `ParticleSystem` culling mode Automatic, and
game scripts gated on `Renderer.isVisible`.

And here is the part that makes this a **mod-side** problem rather than a game one:

* the mod's head camera carries a **blanket culling mask `0xFFFFFFFF`** — all 32 layers
  (`LogOutput.log:2494`);
* the game's own `ScenarioCamera` carries **`0x700FFF17`** and deliberately excludes thirteen layers
  (`LogOutput.log:498`);
* and in a scenario that ScenarioCamera is retargeted to a sink **and mask-zeroed for the duration of
  its own render**, so it contributes nothing to `Renderer.isVisible`
  (`WorldUI/FlatScreen.3.Desktop.cs:244`).

**So in VR the head camera is the sole arbiter of what Unity considers visible, and it sees more
layers and a far wider swathe of the board than the flat game ever rendered.** Pull back for the
overview and a large block of animators, particle systems and VFX that the flat game would have culled
stop being culled — and all of that simulates in the measured logic span.

This is an inference. It is also **cheap to falsify**: `[Optimize] HeadMaskFromScenarioCamera = true`
is an existing config switch that needs no build (§5-S4a).

**What this log cannot do** is separate "more animators" from "more particle systems" from "game
scripts", and it has no zoom axis of its own (§1.5). That is what §5-S0 fixes.

---

## 5. Strategies

Target framing, so the numbers below mean something:

* **To hold 90 Hz in the overview**, main-thread work must drop from **17.1 → 11.11 ms/frame**:
  **−6.0 ms/frame**. Of that, at most ~2.6 ms/frame is inside the mod at all.
* **To hold 90 Hz close in**, main-thread work must drop from **11.49 → 11.11 ms/frame**:
  **−0.4 ms/frame at the median** — plus the tail, because logic p95 is 32.89 ms there and a
  90/45 oscillation feels worse than a steady 45.
* **To make 45 Hz solid and hitch-free in the overview** requires no median saving at all — only the
  removal of the periodic 20–100 ms bursts that currently drive logic p95 to 36 ms.

That last one is achievable with zero look risk, and the close-in 90 Hz case is very likely achievable
with zero look risk. The overview at 90 Hz is **not** reachable by mod-side work removal alone.

---

### S0 — Measurement round *(mandatory, free, no look risk)* — **rank 1**

**Config only, no build.** In `dev.gloomhavenvr.perf.cfg`:

| entry | now | set to | what it buys |
|---|---|---|---|
| `[Perf] CullSubmitSplit` | `false` | **`true`** | splits the head camera's 2.87 ms into *cull* (scales with renderer count and mask) vs *submit* (scales with material slots). These have opposite levers and we are currently guessing which. **Yes — this must be on for the next session.** |
| `[Perf] SceneProfile` | `false` | **`true`** | the `[Perf] SCENE` + `[Perf] GFX` lines: per-layer renderer counts *by name*, whether the head camera renders each layer, material-slot totals, LOD bias, live light census. This is the required input for S4b, and it is the only way to price S3. |
| `[Optimize] QuietDiagnostics` | `false` | **`true`** | takes the mod's own diagnostic string building out of the capture. |
| `[Optimize] WallFadeEvalInterval` | `0` | `0.05` | free ~10 ms/s; see §2. Cannot change which walls fade (the decision already runs through a Schmitt trigger with second-scale dwell). |

**Instrument gaps config cannot close.** Two additions, ~30 lines, worth making before the next
capture:

1. **`PerfMonitor.Scope("WallFade.Rescan")` around `WallSegmentFade.Rescan`** — today the 50–97 ms
   rescans are invisible *inside* `WallFade.Late` and can only be inferred from a mean/median gap.
   Same for `FastReclaimRegeneratedShell` and the heartbeat census block.
2. **A zoom axis on the `FRAME` line** — head height above the board plus the head camera's own
   visible-renderer count, sampled per frame and reported as p50. Without it, every zoom claim in this
   document rests on correlating 10 s heartbeats against 30 s windows.

*I did not add these to the source; they are only useful in a built, shipped mod, and this is an
analysis task. They are one small commit whenever you want the next capture to be decisive.*

---

### S1 — Delete the mod's periodic full-scene sweeps *(zero look risk)* — **rank 2**

**The single largest proven mod cost, and the direct cause of the perceived judder.**

Proven cost in the big room, per second:

| sweep | ms/s | shape |
|---|---|---|
| `WallFade` Rescan (inside `WallFade.Late`) | ≈ 52 | 50–97 ms every ~2 s |
| `Compat.LoaderHeal` | 23 | one ~23 ms sweep every 1 s |
| `UnseenTiles.Rescan` | 10 | 20 ms every 2 s |
| `FastReclaimRegeneratedShell` | not separately measured | full-scene `FindObjectsOfType<MeshRenderer>` **4× per second while walls are faded** |
| wall heartbeat/census block | not separately measured | full-scene walk + 40-line string, re-arms on segment churn |
| **total** | **≈ 89 ms/s = 8.9 % of wall clock** | delivered as 20–100 ms hitches |

**What changes**

* Replace the four scene-wide `FindObjectsOfType<Renderer / MeshRenderer / ProceduralMapTile /
  TilesOcclusionVolume>` walks with the Harmony registries that already exist — `MaterialLoaderHeal`'s
  own comment says its registry has made its `FindObjectsOfType` "largely redundant".
* Amortise the segment × candidate adoption passes over frames against a work budget (N candidates per
  frame) instead of one burst; the four `StackMaxRounds` passes are the worst offender.
* Trigger `Rescan` on a scene *version stamp* (renderer count / generator revision) rather than a 2 s
  timer, so a static scenario rescans once and then not at all.
* Put the wall heartbeat/census block behind `QuietDiagnostics`.
* Stagger `FastReclaim` and drop it to the fade-edge instead of a 4 Hz poll.

**Expected saving (arithmetic):** conservatively 60 of the 89 ms/s → **−0.67 ms/frame at 90 Hz**. The
real prize is the tail: logic p95 should fall from 33–38 ms toward its own p50, and the mean/median gap
on `WallFade.Late` (1.73 vs 0.14) should collapse. Combined with S2 this is very likely enough to snap
the **close-in** case from 45 Hz to 90 Hz — that case is only 0.4 ms over budget at the median.

**Look risk: none.** Identical results, computed more cheaply or later.
**Implementation cost:** 2–3 days. Medium regression risk inside the wall system, which is however the
best-instrumented subsystem in the mod.
**Verification on hardware:** `[Perf] STEPS` ms/s for `WallFade.Late`, `Compat.LoaderHeal`,
`UnseenTiles`; `[Perf] SPLIT` logic p95 vs p50; `[Perf] FRAME` `spikes` count. With S0's new
`WallFade.Rescan` scope this becomes a direct before/after read.

---

### S2 — Make the world-space UI cost stop scaling with enemy count *(zero look risk)* — **rank 3**

Proven cost: `CanvasConversion.Order` **0.786 ms/frame** + `Surface:InitiativeTrackSurface`
**0.312 ms/frame** = **1.10 ms of every frame**, both O(number of panels / initiative entries) — and
each enemy contributes exactly one `ActorBar` panel and one initiative entry. At 90 Hz these two alone
would be **99 ms/s**.

Named defects, from source:

* `ActorBars.LateTick` writes `SetPositionAndRotation` **and** `localScale` on every bar every frame
  with **no change gate** (`WorldUI/ActorBars.cs:352-353`). With ~20 enemies that dirties 20
  world-space canvas transforms per frame for nothing — and canvas rebuild lands in `blocked`.
* `TickPanelOrder` calls `PanelEyeDistance` per panel per frame = **two Unity transform interop calls
  per panel** (`CanvasConversion.8.Order.cs:446-457`), then hashes the whole ordering via
  `GetInstanceID()` per panel **every frame** purely to decide whether to emit a log line that is
  throttled to 1.5 s anyway (`:496`).
* `TickFurnitureOrder` is O(groups × panels) (`CanvasConversion.9.Furniture.cs:181-243`);
  `BuildSeenThroughLadder` is an insertion sort over P+G (`CanvasConversion.9b.SeeThrough.cs:196`).
* `OrderAboveDistance` is another O(P) linear scan, called from four *further* per-frame paths
  (`WristHud.cs:328`, `CardGlow.cs:186`, `BoardVisual.cs:78`, `WorldTooltips.cs:1151`).
* `NormalizeDepth` runs a **full DFS of every initiative portrait subtree every frame**, 2–4 interop
  calls per node (`Surfaces/TablePanelSurfaces.cs:961-1035`), with no interval knob.
* The per-bar 2 s depth rescan starts at `NextDepthScan = 0` for **every** bar
  (`ActorBars.cs:154`), so all ~20 bars rescan **in the same frame** — a self-inflicted synchronised
  hitch. Staggering it is a one-line change.
* The ladder's own design comment says "~30 live panels is the realistic maximum"
  (`CanvasConversion.8.Order.cs:180`). Twenty enemies plus the fixed panels is at that limit.

**Expected saving:** change-gating the transform writes, hashing only when the diagnostic is actually
due, caching the eye distance, and staggering the rescans should take 1.10 → ~0.6 ms/frame,
i.e. **−0.4 to −0.5 ms/frame**, plus ~20 fewer canvas transform dirties per frame out of `blocked`.

**Look risk: none.** **Implementation cost:** ~1 day, low risk.
**Verification:** `[Perf] STEPS` avg-per-frame for `CanvasConversion.Order`,
`Surface:InitiativeTrackSurface`, `ActorBars.Late`; `blocked` in `[Perf] SPLIT`.

---

### S4 — Narrow what the head camera declares visible *(needs your eyes — this is the only lever aimed at the zoom scaling itself)* — **rank 4**

Mechanism in §4.2. Three levers, cheapest first:

**a) `[Optimize] HeadMaskFromScenarioCamera = true` — config only, no build, free experiment.**
Seeds the head camera's mask from the game's own `0x700FFF17` instead of the blanket `0xFFFFFFFF`,
dropping thirteen layers the flat game never rendered. The mod layer and the UI layer are added back
unconditionally and can never be dropped. The log names every dropped layer *with its renderer count*
at the moment it drops it, so a regression is one line to read.
*Expected:* cull time down, submitted volume down, and — if §4.2 is right — **logic down**, because
those layers' animators and particle systems go back to being culled.
*Look risk:* **real but bounded and instantly reversible** — something the mod legitimately needs may
go invisible. This is why it defaults off. It needs one look through the headset, not a code review.
*This is the highest-information experiment available and it costs nothing but a config edit.*

**b) `[Optimize] HeadCullingMaskDrop = "<layer names>"` — the surgical version of (a),** once S0's
`[Perf] SCENE` line has printed the per-layer census so the choice is a measurement rather than a guess.

**c) `Camera.layerCullDistances` on the head camera for VFX/particle layers only.** Torch flames, glow
billboards and decals (the log shows KriptoFX `BFX_Decal` material work) stop being submitted **and
stop simulating** past N metres. At overview distance those effects are a few pixels across.
*Look risk:* **this is a genuine trade and needs your ruling.** `layerCullDistances` pops at the
boundary, which the project's look rules forbid — so it would have to be paired with a distance fade on
the affected materials, or set so far out that nothing legible is ever affected. New code, ~1 day.

---

### S6 — Bound the GPU in a 3000-renderer room *(measurement only)* — **rank 5**

The 2026-07 pixel-budget refutation is not transferable to this room (§1.4). One A/B — MSAA 8× → 2×
for a single 30 s window in the big room, everything else identical — bounds the GPU and answers
whether it is a **second wall** that would cap the overview at 45 Hz even after S1, S2 and S4 all land.
Cheap, and it closes the one genuine unknown in this analysis. Not a shipping change.

---

### S3 — Stop creating ~178 per-instance materials per enemy bar *(small look risk)* — **rank 6**

`ActorBars.ScanBarGraphics` creates `new Material(src)` for **every** `Graphic` under a bar
(`WorldUI/ActorBars.cs:583`) in order to force `unity_GUIZTestMode = LEqual`. The log confirms the
count: `bar depth-test: forced unity_GUIZTestMode=LEqual on 178 graphics` — per bar, ~20 bars in this
room. Per-instance materials **break uGUI canvas batching**, so those graphics become their own draw
calls, twice per frame under MultiPass.

Fix: one shared material per *(source material, ztest)* pair rather than one per Graphic, or restrict
the override to the graphics that were actually bleeding through walls.

**Expected saving:** *not* on the main thread — graphics jobs already moved submission off it, and the
render loop is only 3.35 ms. This is a GPU / draw-call / memory win, and it **cannot be quantified from
this log** because `[Perf] SCENE` (which counts material slots) is switched off. That is precisely why
S0 comes first. Ranked below S1/S2 for that reason, not because it is small.
**Look risk:** low but real — the depth override is the fix for bars bleeding through walls; a
de-duplication bug reopens it.

---

### S5 — Animation LOD for distant figures *(largest remaining lever, largest look cost)* — **rank 7, only on your ruling**

If S4a shows the +6 ms is animator-driven, the direct fix is to run distant enemies' `Animator`s at a
reduced rate (`animator.enabled = false` plus a manual `Animator.Update(dt)` every 2nd or 3rd frame
beyond N metres). With ~20 enemies that is a 50–66 % cut of the animation share — potentially several
milliseconds, i.e. the only mod-side lever large enough to matter for the overview at 90 Hz.

**Look risk: high by this project's own standard.** Figures are explicitly out of bounds for the
visibility system, and animation that steps is the "popping" the look rules forbid — even if it only
steps at distance. It is MP-safe (purely local, no wire traffic) and fully reversible, but it is a
look decision, not a technical one.

---

## 6. Summary for the decision

**Safe — do without you seeing any visual difference:**
**S0** (measurement, config only), **S1** (kill the periodic full-scene sweeps),
**S2** (per-panel UI work). Together ≈ **−1.1 ms/frame** at 90 Hz plus the removal of the 20–100 ms
hitches. Expected result: **close-in goes from 45 Hz to a stable 90 Hz**, and the overview becomes a
**solid, hitch-free 45 Hz** instead of a juddering one. This is what I would build first.

**Needs your decision — trades look for speed:**
**S4a** (narrow head culling mask — free experiment, small and reversible look risk),
**S4c** (VFX distance culling — real look change, must be faded not popped),
**S5** (animation LOD for distant figures — real look change on figures),
**S3** (bar material de-duplication — small regression risk on the wall-bleed fix).

**Measurement only, no shipping change:** **S6** (bound the GPU in the big room).

**The one thing I cannot promise:** **90 Hz in the far overview of a 3000-renderer room.** That needs
−6.0 ms/frame of main-thread work, and at most ~2.6 ms/frame of it is inside the mod at all. The rest
is view-scaled simulation in the game's own `Update`, reachable only through S4/S5 — i.e. only by
changing what the head camera declares visible, which is a look decision. A stable, hitch-free 45 Hz
there is achievable with no look change at all.
