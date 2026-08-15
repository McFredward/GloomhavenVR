# The fire's loop — measured, ModBuild 150

User, hardware, on ModBuild 149:

> "Das Feuer zieht in einem Loop in eine Richtung, glitcht dann zurück und beginnt diesen Loop
> von vorne — das sieht nicht gut aus."

This file records how the loop was found, because the finding is not the one the round expected
and the instrument had to be built first.

## The instrument

Neither shipped fire series can settle a claim about a *period*:

| series | step | span | resolves |
|---|---|---|---|
| `pf` FAST | 55 ms | 0.385 s | the 8 Hz band, nothing below 2 Hz |
| `ps` SLOW | 240 ms | 1.68 s | the 1.1 Hz swell, aliases everything above 2 Hz |

`ENV_PREVIEW_FIRELOOP="count,step[,air]"` (PreviewEnvironments.cs) renders a uniform series of
`count` frames `step` apart on the fire views, tagged `_pl<ms>`. It is off by default. The runs
below are `60,0.0333` — 60 frames 33 ms apart, spanning 1.965 s, which contains a whole puff
cycle (1.818 s) and seven erosion scroll cycles (0.275 s each).

## The measurement

For each frame, the sum of squared differences against **frame 0**, over a crop containing only
the fire. A picture that repeats with period T has a sharp minimum at t = T.

Forest, burning snag (`env_swamp_FireSnag_pl*`), crop y 150-650, x 300-720, Fire at full,
Air down:

| | series mean SSD | min in 1.6-2.0 s | dip |
|---|---|---|---|
| ModBuild 149 | 0.00538 | **0.00167 at t = 1.765 s** | **3.22x** |
| ModBuild 150 | 0.00080 | 0.00072 at t = 1.798 s | 1.11x |

`1 / _PuffHz = 1 / 0.55 = 1.818 s`. The minimum sits on it. **The whole fire repeated at the
puff period.**

## The cause

`EnvFlame.shader`, the vertex shader's puff branch:

```
age = frac(bt * (_PuffHz / max(_FireHz, 0.01)) + v.fp.w);
```

`bt = t * GhvrFireHz(...)`, so the rate cancels and `age` advances at exactly `_PuffHz` for
**every puff card in every fire in both rooms**. `v.fp.w` spreads the *phases*, which is what
makes the population look unsynchronised in a still — and does nothing whatever to the *period*
of the ensemble, which is what the eye finds after two seconds.

Inside one cycle each card's motion is monotone and then discontinuous:

* `p.y += climb * age` — rises 0.38..0.68 fire-heights (ModBuild 149 values),
* `p.xz += _GustDir.xz * drift`, `drift = fp.z * age * (0.35 + 0.80 * air)` — travels downwind,
* `age` wraps 1 -> 0 and the card is back at its birth point.

That is "zieht in eine Richtung ... glitcht dann zurück ... beginnt von vorne", one card at a
time, six to eight cards per fire, all on one clock.

## The fix

Two lines, and they are the same rule twice:

1. **Per-card rate.** `age` now advances at `_PuffHz * (0.72 + 0.56 * tp)`, `tp` being the card's
   own turn (COLOR.r, already spread by Hash3). Cycles run 1.42..2.53 s; the mean is unchanged, so
   "a fire sheds something about every quarter second" still holds. The ensemble has no period.
2. **The envelope no longer reaches the wrap.** The card is out by 0.55 of its cycle in still air
   (0.96 at full Air), so nothing carries alpha at `age = 1` where the teleport happens.

The geometry half of the same fix — birth height and rise cut so the card never clears the flame
body — is in `EnvRoomBuilder.FireMesh` and is what answers the user's *other* item ("Diese
tanzenden Feuer die einfach darüber schweben").

## The erosion scroll — checked, and it is NOT the loop

The round's first suspect was `EnvFlame.shader`'s `texPh = frac(bt * _ErodeParams.z + tp * 0.61)`,
consumed as `ev.y = i.pr.w - i.uv.y * tileV`. Three findings:

* **The wrap distance is 1.0 of texture V and cannot be anything else.** `texPh` is `frac`'d in
  the vertex shader, and `tileU` / `tileV` multiply the *card's* uv — the wrap is an offset
  **added** to that, never a factor on it. So ModBuild 149's `tileV` 0.72 -> 1.35 could not have
  broken the wrap, and no future retune of it can.
* **1.0 is an exact period of both octaves of the field.** `fire_atlas_pipeline.erosion_field()`
  builds R at 1 tile over the 512 image and G as an integer 4x4 tiling of a downsample — periods
  1 and 1/4, both dividing 1.
* **But the image is not exactly tileable.** Measured on the shipped `fire_atlas_alb.png`: the
  mean |step| across the V seam against the mean |step| between ordinary rows is **R 1.49x,
  G 0.88x**. The G octave is exactly seamless (any integer tiling is); the R octave inherits
  whatever seam the imported source .tga has, and it carries 74 % of the field. That is a real
  artefact — a small hardening of the pattern climbing every card, once per 0.275 s, i.e. at
  3.6 Hz — but at 3.6 Hz it is a shimmer, not a "loop", and it is an order below what the SSD
  series shows.

`EnvRoomBuilder.FireErosionWrapGate()` now checks both halves every bake and prints both ratios.
**The R seam is not fixable from the fire lane's files.** The fix is one edit in
`Assets/Editor/fire_atlas_pipeline.py`'s `erosion_field()`: build R the way G is already built (an
integer tiling of a downsample, which is seamless by construction), or cross-fade the coarse
octave with a rolled copy of itself. The atlas then has to be regenerated and ArtE re-derived,
because the field's statistics move.

## The general rule, written down

> A scrolling or wrapping field is seamless only if its wrap distance is an exact period of
> everything that reads it — **and a POPULATION that shares one wrap period is itself a reader.**
> Spreading phase hides the synchrony; only spreading the *rate* removes the period.

The first half is guarded by `FireErosionWrapGate`. The second half has no guard and cannot
easily have one; it has this file and the block in `EnvFlame.shader` instead.
