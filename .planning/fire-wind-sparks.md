# The wind stops touching the flame — ModBuild 151

USER, hardware, on ModBuild 150, and it is an instruction rather than a report:

> "Die Windinteraktion mit dem Feuer zieht diese lange Streifen, es ist extremer wenn Wind an
> ist — sonst ist das trotzdem noch zu sehen. **Lösch die bisherige Implementierung dahingehend
> und mach stattdessen Funken, die in die Richtung wehen.** Das wird besser aussehen."

Two rounds had already tried to *tune* these terms. This one deletes them.

## The log says the resting terms are implicated, not only the Air-scaled ones

`.planning/debug/Player.log` is the ModBuild 150 session (`:40`). The element timeline is
unambiguous and it is the A/B the verdict was formed from — he was in the **wood**
(`ENV ROOM KIND _GhvrIndoor = 0`, `:2641`, and the fire session at `:6594` follows it):

| shared clock | state |
|---|---|
| 43.89 s | Fire=Strong, **Air Inert 0.00** |
| 68.97 s | Fire+Air |
| 79.34 s | Air back to **0.00** |
| 81.78 s | Fire+Air again |
| 88.59 s | Air back to 0.00 |
| 89.75 s | all down |

Air is **exactly 0.00** during the "sonst" stretches — no waning latch, no residual ramp
(`:6847`, `:6902`, sig 486 = Fire only). So "sonst ist das trotzdem noch zu sehen" indicts the
terms whose Air coefficient is zero: the **resting draught**. On the bonfire path those were the
shared candle-family lean (`_GustDir * _Gust * gustMul * g * h*h`, gustMul = 1 at Air 0, i.e.
3.5–5.0 cm of per-card shear along the room's wind) and the puff drift's constant `0.35 * rise`.
Both are deleted. Tuning the Air coefficients again could not have touched either.

## What was deleted

Everything that made a **bonfire card's geometry a function of `_GustDir`**, Air-scaled and
resting alike. After this round `grep _GustDir EnvFlame.shader` reaches nothing inside
`_Bonfire > 0.5`.

| term | was | now |
|---|---|---|
| whole-fire lean | `p.xz += _GustDir.xz * 0.85 * pair.air * (0.15+0.85*hFire) * (...)` | gone |
| rim tear's Air factor | `_Flare * (1 + 1.9*pair.air)` | `_Flare` |
| `tall`'s Air term | `+ 0.06 * e.air` | gone |
| candle-family lean, **bonfire only** | `p.xz += _GustDir.xz * _Gust * gustMul * g * h*h` | gone |
| puff drift | `drift = fp.z * age * (0.35 + 0.80*pair.air)`, `p.xz += _GustDir.xz*drift` | gone |
| puff climb's Air term | `+ 0.45 * pair.air` | gone |
| puff alpha's Air envelope | `max(cardA, ... 0.42+0.30*air .. 0.80+0.16*air ...)` | gone |
| `swayMul` on a bonfire | `sway = wave * _Sway * (1+1.3*e.air) * h*h` | `swayW` (Air-free) |

### Kept, and why

* **`GhvrFireDepth`** — the flicker swings ±88 % instead of ±45 % under Fire+Air. An amplitude on
  a waveform, no geometry, and the user's own "flackert wenn Wind an ist".
* **`GhvrFireHz`** — already the identity since ModBuild 149 (an element may not multiply a
  frequency). Untouched.
* **The candle path's whole lean**, `sway * swayMul` and `_GustDir * _Gust * gustMul`. A candle
  flame is a 2 cm teardrop: its entire drawn height is smaller than the amplitudes being argued
  about on the fires, so it has no long axis to smear along. And the draught response is a
  feature the user asked for by name — "Im Keller sollte es noch mehr wie ein Windzug wirken der
  insbesondere aus dem Fenster kommt" (ModBuild 142); it is why `_AirGust` is derived per candle
  from its distance to the window at all.
* **The `sway` WAVEFORM on a bonfire, without its Air scaling.** This one is a measurement and
  not a taste — see "the term that had to come back" below.
* The four pairings that are not the wind (smoke/steam/smoulder/dark), which are products of Fire
  with Light/Ice/Earth/Dark and have never been implicated.

### Why no coefficient could have worked

The deleted family translates six to eight flat additive cards, 2–3× taller than they are wide,
along **one** fixed world direction by an amount monotone in each card's own age. Their ages are
spread, so at every instant they lie on a straight line, evenly spaced, with a brightness
gradient along it. That *is* a drawn stroke; shortening the line shortens the stroke, it does not
stop it being one. The way to remove a streak made of aligned cards is to stop aligning them.

## What the sparks do instead

The bundle ships **no MonoBehaviours**, so nothing can change a Shuriken emitter's rate, speed or
lifetime at runtime. The only element-aware thing in a particle's path is its material
(`EnvParticleAdd` / `EnvParticleElem.cginc`), which can collapse a quad and scale colour and
alpha — nothing else.

So the wind response is a **second population**, not a change to the first. Every burning site
keeps its `Sparks` emitter unchanged (it is the part he praised, twice: "Die Funken gefallen mir
gut") and gains a `GustSparks` one whose particles always blow hard downwind and whose material
keeps them near-invisible until Air rises. The Air-up look is then exactly the Air-down look
**plus** a downwind trail — the superset the round asked for, with no discontinuity anywhere: the
reveal is a smooth multiply that follows ElementMood's own 1 s ramp.

| | cellar (DraftDir −0.89,−0.46) | wood (ForestWind −0.82,−0.57) |
|---|---|---|
| resting `Sparks`, unchanged | 4 emitters, 64 alive, 0.10–0.30 m/s, ~0.35 m of travel | 3 emitters, 56 alive, 0.12–0.38 m/s |
| `GustSparks` | 4 emitters, 74 alive, 8.5–11.5/s | 3 emitters, 68 alive, 6.5–8.5/s |
| lifetime | 1.0–2.2 s | 1.6–3.6 s |
| downwind speed | 0.85–1.70 m/s | 1.30–2.80 m/s |
| downwind travel | 0.9–3.7 m, **mean 2.0 m** (5.8× the resting reach) | 2.1–10.1 m, **mean 5.3 m** |
| rise | 0.20–0.55 m/s (less than the resting ones: buoyancy spent on the horizontal) | 0.20–0.70 m/s |

**The reveal, in numbers.** `EnvParticleAdd` premodulates (`c.rgb *= c.a`) and then blends
`SrcAlpha One`, so a particle's drawn energy goes as **alpha squared**. With `_Tint.a` = 0.13 and
`_ElemAlpha` = 6.40 the alpha runs 0.13 → 0.962 and the energy ratio is 7.40² × the 1.35 colour
gain = **74×**: 1.4 % of peak at Air 0, 100 % at Air 1.

**Why the gate is FIRE and not AIR.** `_ElemOwn` is a dot product over the six strengths, so it
can express "owned by Air" but not "owned by Fire **and** Air" — there is no product of two
elements anywhere in `EnvParticleElem.cginc`. Gating on Air would have put a shower of embers over
an unlit crate whenever the room was infused with Air alone. Gating on Fire is exact in the
direction that matters, and the Air half rides the modulation, which does not have to be a switch.

**No stretched billboard.** `Env_Streak` in Stretch mode scales a sprite along its own world
velocity — the documented escape hatch under the no-billboarding ruling, and exactly the mechanism
that made "extrem lange Strahlen". Every sprite here is `Env_Spark`, radially symmetric, aspect
**1:1 at every speed and from every angle**. The bound below is a property of the asset, not a
tuning result. A line of separate round dots reads as a *trail*, which is a population; a stroke
is one long drawn shape. That distinction is the whole design.

## The measurements

Instrument: `ENV_PREVIEW_FIRELOOP="60,0.0333,<air>"` — 60 frames 33 ms apart, spanning 1.965 s.
Two additions to the harness this round, both necessary:

* **the emitters are stepped with the clock.** `_GhvrTimeOfs` is read by every Env* *shader*;
  Shuriken reads none of it and there is no game loop in batch mode, so until now every frame of
  every fire series showed the sparks **frozen**. `FireSeries` now calls
  `ps.Simulate(6 + t, true, restart: true)`. Restart, not advance: with the seeds pinned that is a
  pure function of `t`, so a `ENV_PREVIEW_VIEWS`-filtered run is comparable with a full one.
* **the air level is in the tag** (`pl` = Air 0, `pla` = Air > 0), because the round's whole claim
  is a comparison of two series at the same offsets and one tag silently overwrote the other.

Baseline: ModBuild 150 re-baked and re-rendered **with the same instrument**, so the three columns
below differ only in the shader and the emitters.

Wood, `env_swamp_FireSnag`, fire crop y[600,720] x[300,720]:

| build | air | step SSD median | max | max/med | SSD vs frame 0, mean | min | dip | **flame-body centroid shift vs its own Air 0** |
|---|---|---|---|---|---|---|---|---|
| ModBuild 150 | 0 | 0.00117 | 0.00166 | 1.42× | 0.00238 | 0.00132 | 1.80× | — |
| ModBuild 150 | 1 | 0.00248 | 0.00602 | 2.42× | 0.00872 | 0.00267 | 3.26× | **(−17.94, −1.16) px** |
| 151 first cut | 0 | 0.00118 | 0.00168 | 1.43× | 0.00214 | 0.00063 | 3.38× | — |
| 151 first cut | 1 | 0.00190 | 0.00407 | 2.14× | 0.00476 | 0.00114 | 4.19× | (+0.52, −0.68) px |
| **THIS ROUND** | 0 | 0.00117 | 0.00165 | **1.40×** | 0.00231 | 0.00119 | **1.94×** | — |
| **THIS ROUND** | 1 | 0.00195 | 0.00416 | **2.14×** | 0.00488 | 0.00181 | **2.70×** | **(+0.44, −0.70) px** |

### (1) No drawn feature's aspect ratio exceeds the stated bound

Drawn features are isolated with a **top-hat** (luminance minus its own 19 px local background),
so the smooth lit stone and the halos are excluded and only what is *painted on* is measured;
aspect is the PCA ratio with a 1/12 px² pixel-quantisation variance floor.

| | features | mean | p99 | worst |
|---|---|---|---|---|
| wood, Air 0 | 1469 | 2.45:1 | 7.07:1 | 16.89:1 (17 px) |
| wood, Air 1 | 1483 | **2.45:1** | 8.00:1 | 17.83:1 (19 px) |
| cellar, Air 0 | 10803 | 2.31:1 | 7.45:1 | 17.76:1 (64 px) |
| cellar, Air 1 | 10329 | **2.31:1** | 7.70:1 | 18.09:1 (63 px) |

**Bound: p99 ≤ 8.0:1, mean 2.3–2.5:1**, and the number that matters is that **Air changes it by
nothing** — Δmean ≤ 0.01, Δp99 ≤ 0.93. The worst single component is a 17–64 px, one-pixel-wide
top-hat sliver at ≤18:1; it is present *identically* with Air off, so it is a rendering-scale
residue of the erosion rifts and not a wind artefact. The bake's own authored bound is unchanged:
slimmest quad 2.28:1 as built, 3.05:1 after `lick`.

### (2) No monotone-then-discontinuous term

That is what a teleport back to a birth point looks like, and it shows in the **step** series as a
single frame whose difference dwarfs its neighbours. The worst step is **1.40× the median at Air 0
and 2.14× at Air 1** — better than ModBuild 150's 1.42× / 2.42×, and nowhere near an outlier.

### (3) The silhouette no longer moves as a body when Air rises

The flame body's luminance centroid over the 60-frame mean:

* ModBuild 150: **−17.94 px in screen x**. The wind's screen-x component in this view is −0.640,
  i.e. downwind is to the left — so the fire leant downwind as one object. That is the fault.
* This round: **+0.44 px**, forty times smaller and inside the flicker noise. Vertically −0.70 px.
  rms extent −0.93 % × +1.27 %; drawn energy −0.2 %. Cellar `FireCrate`: **(+0.14, +0.61) px**,
  extent −0.38 % × +0.76 %.

This is exact by construction and not merely small: after the deletions the only reader of
`pair.air` inside `_Bonfire > 0.5` is `GhvrFireDepth`, which multiplies a brightness. The flame
cards' vertices are **bit-identical** at Air 0 and Air 1.

And the sparks *do* move. The max-projection of (Air 1 − Air 0) over the 60 frames shows, in both
rooms, a fan of **discrete round dots** streaming out of each fire — up and to the **left** in the
wood, up and to the **right** in the cellar, matching the wind's screen-x components of −0.640 and
+0.421. Dots, not strokes.

### The term that had to come back

The first cut deleted the whole shared `sway`/lean line from the bonfire path. The step series
stayed clean, but the picture began returning close to its own first frame at 1.10 s and 1.80 s —
self-similarity 0.00132 → **0.00063** against a series mean of 0.00214, a dip of 3.38× where
ModBuild 150 had 1.80×.

The cause: `sway`'s two components run at **0.91 Hz and 1.48 Hz**, and they are the only
frequencies in the bonfire path that are not near-multiples of the fire's own four bands
(4.60 / 2.81 / 7.96 / 1.08 Hz). An incommensurate slow signal is what stops an ensemble of
near-harmonic bands looking periodic, and this is the only one a bonfire has. Restoring the
waveform **without** `swayMul` (so Air still touches nothing) put the dip back to 1.94× at Air 0
and to **2.70× at Air 1, better than ModBuild 150's 3.26×**, while leaving the body shift at
+0.44 px.

> **The rule, written down:** deleting a term deletes its FREQUENCY as well as its amplitude. When
> the term you are removing is the only incommensurate one in a bank of near-harmonic bands, the
> ensemble acquires a period that nobody added. Check the self-similarity curve, not only the step
> curve, after any deletion.

## Cost

`Player.log`'s `[Perf] SPLIT` for the 30 s window containing the fire session (`:6674`, the wood):
frametime p50 **11.15 ms**, logic (Update→LateUpdate) p50 **0.80 ms**, render loop p50 0.57 ms,
**blocked on the GPU/compositor 9.35 ms (84 %)**. Shuriken simulates on the CPU inside `logic`.

The gust emitters roughly double the fire's particle load — cellar 4 emitters / 64 alive →
8 / 138, wood 3 / 56 → 6 / 124 — and they simulate and keep a draw call **in every frame the room
exists, including with Air and Fire down**, because the bundle has no MonoBehaviours to switch them
off. 68–74 extra particles with Low-quality noise is tens of microseconds against a 0.80 ms logic
p50 and 9.35 ms of blocked time; the three extra draw calls per room land in a 0.57 ms render loop.
It is a real, permanent cost and it is stated in both bake logs.

**`ArtE` was NOT re-derived, and does not need to be.** The drawn mass of flame is bit-identical:
the bake prints 582 quads / 11.08 m² (cellar) and 456 quads / 22.66 m² (forest) before and after,
because no card geometry, no atlas, no erosion constant and no `FireMesh` number was touched.
