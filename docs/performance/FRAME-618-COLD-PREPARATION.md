# Build617 hardware: interaction preparation and remaining cold work

This is an analysis of the Build 617 headset run, not a Build 618 binary or a hardware
fix claim. Both supplied sinks identify ModBuild 617 / assembly 1.1.0.0; their build
provenance is `e1da52eaa`. Immutable inputs and reproducible extraction are retained
under `.planning/debug/frame618-review/inputs/` and `resources/` in the main checkout.
There are no new screenshots in this evidence set. Loading and preparation stalls
are accepted by the maintainer and excluded from interaction optimization targets.

Preparation ran successfully, but it did not eliminate all first-use work. The
resource closure is incomplete: some native card element widgets keep private art
arrays outside the currently collected class skins and UIInfoTools configuration.
The first visible fan still coincides with 118.78 ms in `Cards.Driver`; a later action
rebuild costs 122.33 ms. Native stat presentation remains a separate cold path.

## What the hardware confirms

| Evidence | Player.log lines | Result |
|---|---|---|
| Preparation starts after native loading |6487–6489| Original native input remains available; mod preparation begins. |
| Card preparation finishes |7292|4 distinct class skins; 271/271 collected sprites visited; 29/29 unused backings; 0 failures; unavailable=False. |
| Preparation coordinator finishes |7293|38.82 s, without timeout or failure. |
| Loading indicator actually disappears |7295–7302|This later edge is the conservative start of ordinary play. |
| Prepared ghost acquisition |8135,8227,8287|SunDemon twice, then SpittingDrake: hits 1→3, misses 0, invalidations 0 at those points. |
| Reported hand-fan readiness |7406–10027|All 9 emitted fan-open reports have 0 unready faces and 0 ms/0 frames waiting. |

The completion records are Debug and appear in Player.log, but not in the Info-level
BepInEx LogOutput.log. Keeping only the latter would lose proof that preparation
completed. The completion count means every **collected** sprite was visited;
`WarmSprite` can reuse a prior bake or keep an already-mipped/unsupported original.
It is not a count of 271 new GPU bakes or proof that all native dependency types were
collected. Factory reservations create inert ordinary backings, not gameplay faces.

The prepared ghost reports retain their inactive construction receipts, which say
the original parked copy was not drawable. That historical receipt does not say
the acquired ghost is invisible. Acquisition explicitly rebinds the current source
bones, masks, blend shapes, root pose and scale before activating it. Headset pixels
and current render visibility cannot be inferred from that stored construction text.

## Timing comparison and limits

These are scoped durations and examples, not a matched-pose benchmark. VR head pose,
world scale, interactions and the runtime's selected refresh rate changed. The
earlier Build 616 measurements are retained in [FRAME-617-ANALYSIS.md](FRAME-617-ANALYSIS.md).

| Path | Build 616 evidence | Build 617 after preparation |
|---|---|---|
| Ordinary card/fan work |Cards.Driver 172.37 ms during class switch|First visible fan frame 5739: 225.31 ms total, 152.54 ms named mod, Cards.Driver 118.78 ms. Later frame 9306: 167.63 ms total, 136.17 ms named mod, Cards.Driver 122.33 ms during an action rebuild. |
| Figure pickup |109.10 ms; nested ghost construction 55.89 ms|A clean 30 s interval reports pickup 67.85 ms; three actual ghost acquisitions are prepared hits. Cold fallback still occurs for other actors. |
| Native stat presentation |65.93–98.54 ms|Frame 6463: 227.76 ms total, 149.33 ms named mod, StatPanelSurface 126.19 ms. A later clean 10 s interval still reaches 41.77 ms. |
| Mip work on new imagery |Cold class/element sprites were a target|A clean 30 s interval reports SpriteMipMiss 86.58 ms and nested AtlasMipMiss 86.48 ms; later new stat/enemy imagery reaches 94.16 ms in SpriteMipMiss. |

The first post-completion STEPS report (Player 7941–7943, 30.1 s/555 frames) straddles
preparation: it still contains Cards.ScenarioPreparation 174.04 ms and 112 sprite misses.
Its Cards.Driver 133.84 ms and SpriteMipMiss 174.02 ms cannot be called post-load maxima.
The directly emitted frame 5739 occurs after the loading indicator disappears and
remains valid evidence about that particular first fan opening.

The next interval (8629–8631, 30.0 s/603 frames) is entirely after preparation and reports
StatPanelSurface 126.19 ms, pickup 67.85 ms, 8 sprite misses and 2 atlas misses. The later
interval 12843–12845 reports one expensive sprite-miss frame (94.16 ms), with 6 misses
on that frame. The 191-frame interval 19514–19516 contains late pickup 17.14 ms, nested
ghost construction 12.62 ms and new portrait atlas 40.84 ms. None of these inclusive
parent/child scopes may be added together.

STEPS average/worst and COUNTS `total` reset per measurement window; COUNTS rounds
the per-second rate, so `0/s (total 8)` is not zero work. Bake ordinals and reported
ghost hit/miss counters accumulate for their cache lifetime instead. MARK prints
before its closing FRAME/STEPS set; treating it as the new interval start would
misclassify the first mixed report. The bounded SPIKE emitter lists only its top
scopes and omits rate-limited frames. An absent Acquire scope is not a measured 0 ms.
Atlas-miss time includes synchronous bake/readback work and possible driver waiting;
it is not a hardware GPU-busy counter.

## The remaining native card-art closure

Thirty new texture/region bakes are reported after the loading indicator disappears:
27 sprite regions and 3 textures. These include 13 early element/state sprites at
Player 7482–7807: CreateEarth_Highlight, ConsumeAir, Grey(Clone), Red(Clone), Dot(Clone),
CreateAir_Highlight, **ConsumeDark**, CreateDark_Highlight, ConsumeEarth, CreateIce_Ice,
ConsumeAny, ConsumeIce and CreateLight_Highlight. ConsumeDark was an explicit
Build 616 target and is still first baked after preparation (7616).

Read-only decompilation of the shipped GH.Runtime.dll establishes why:

- `ConsumeElement` owns serialized private `Sprite[] elementSprites` and
  `highlightElementSprites`; its native Init/selection paths assign those sprites.
- `InfuseElement` owns its own serialized private `Sprite[] elementSprites` and
  assigns them in Init/Enable/PickElement.
- `ScenarioCardPreparation` reads AbilityCardUISkin, UIInfoTools, ElementConfigUI
  and EffectInfo fields. It does not visit these original widget arrays.

The current runtime fixture's boundary has an ElementConfigUI containing useIcon
and UIInfoTools.darkConfig. Its element control proves that configuration path; it
does not reproduce these native private widget arrays. A green fixture therefore
did not establish the missing native closure.

The next preparation change should read those serialized references on original
existing/inactive widget or prefab templates during loading, fill the same shared
pin/mip cache, and test against the real native field shapes. Do not invoke native
Awake/Start/Init/Show to discover art, synthesize a gameplay hand, select a character
or weaken remote reveal rules. Class switches, original live assignments and native
callbacks must retain their current owners. Unseen portraits and genuinely new
summons still need an immediate valid fallback.

## Figure and stat work which remains cold

The three positive prepared-hit receipts do not cover every later pickup. Elementalist
(8406), Brute(8461), and late SlimeSpirit(19288) report ordinary GHOST construction.
The latter actor appears after the original 17-actor inventory grows to 18 and loads
additional figure-mesh parts, so a new actor is a valid explanation for its fallback.
No exact missing-entry or invalidation reason is logged for the two heroes.

Source permits a separate, plausible cause: the distance-detail policy can replace
the original sharedMesh as head distance changes and chooses the near cap for a held
actor. Prepared-source validation correctly rejects changed mesh/material/LOD/bone
identities. A constant actor count consequently does not imply a reusable prepared
visual. FigureGhosts.Clear is tied to FigureGrabDriver teardown, and no such teardown
is established between preparation completion and these pickups. Do not remove
validation to improve a hit count; add a bounded Debug miss reason and refresh only
unused parked copies after genuine original identity changes.

Stat preparation currently warms only already-assigned native Image/RawImage art;
it does not build a native stat surface or request every unseen portrait. New
Portrait_Sundemon, Portrait_SpittingDrake, Portrait_Elementalist, enemy-card imagery,
and late Portrait_Slimespirit therefore retain cold work. Moving safe native asset
references into loading is useful, but it cannot justify claiming the entire 126 ms
native stat/conversion cost eliminated. Profile its actual clone/conversion/layout
substeps before changing that path.

## Cache and failure evidence

CardFaceMipBake has no eviction path. This run emits no mip VRAM-ceiling warning,
transient CPU-cache-budget report, card/figure preparation failure or timeout. At
the tail, the shared bake cache reports 92 textures and 191 sprite regions, about 323 MB
of its 384 MB VRAM budget; retained CPU atlas pixels reach about 249 MB of 256 MB. Newly
seen sources and uncollected dependency types explain the observed misses; an
eviction or memory leak is not established.

Player.log does contain 8 Hydra GetEnvironmentInfo DNS errors across the run. They
belong to the game's SDK and do not identify a preparation failure or establish
their contribution to these scoped interaction peaks. No new resource exception
provides a cause for the remaining ordinary-play stalls. This is a single-player
capture: shared cache/source code serves remote consumers, but no new multiplayer
1:1 timing or pixel result is supplied here.
