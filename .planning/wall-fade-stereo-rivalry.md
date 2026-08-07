# Wall fade: per-eye rivalry (PARKED 2026-08-08)

**Status: parked by user ruling.** The defect is real, understood down to the shader
instructions, and *not* fixable on the game's own masonry shader without changing the
approved look. The user chose to revert the measurement instrumentation and pick the topic
up later. Nothing about the fade look changed; ModBuild 79 renders exactly like ModBuild 77.

---

## The defect

At a particular distance and viewing angle — "kurz bevor es faded", with the wall somewhat
off-centre in the view — a wall can be **discarded in one eye and solid in the other**.
Binocular rivalry; physically unpleasant in VR.

User ruling (2026-08-07), treat at the severity of the Lights rule and the figure rule:

> "das darf niemals passieren. Entweder faded es auf beiden Augen oder gar nicht."

Second, equally hard ruling (2026-08-08), after a fix traded the look away:

> "es war vorher perfekt. Jetzt gibt es keine Animation mehr und es 'poppt' einfach auf und
> weg — das darf nicht sein. … Die Kompromisse die du alles eingegangen bist bin ich nicht
> gewillt einzugehen."

So both of these are hard constraints, and on this shader they are **mutually exclusive**
(proof below). Any future attempt must satisfy both or not ship.

## Why it happens — verified from the disassembled shader

The keep's masonry runs `Amp_Basic_N_MRAO`, blob 388, keyword `_WALLFADE_ON_ON`
(wall-fade block, lines 188–247 of the DXBC). Recovered `$Globals` names:

| Register | Name | Notes |
|---|---|---|
| `cb0[10].x` | `_EnableOcclusionMap` | undeclared global float — **MPB-settable** |
| `cb0[10].y` | `ToggleWallFade` (int) | |
| `cb0[10].z` | `_Cutoff` | |
| `t3` | `_TilesOcclusionMap` | sampled at **screen UV** (`v5.xy / v5.w`) |

Discard condition:

```
discard  ⟺  saturate(1 + T·(A·B − 1)) − _Cutoff < 0        // note: mad_SAT at line 244
A = max(M,S) + 42·ν·(1 − max(M,S))
B = (M > 0) ? 1 : S
S = smoothstep(saturate(3.33 · ((0.02·dist + screenRadial)^8 + (1 − worldY)/3)))
```

* `screenRadial` is measured from the centre of **each eye's own** screen.
* `dist` uses that eye's own `_WorldSpaceCameraPos`.
* Under **MultiPass** stereo each eye is a separate pass, so both differ per eye.
* The **8th power** turns a small disparity into a full 0→1 flip near the crossover.

Measured at the operating point: the asymmetric OpenXR frusta displace the same world
direction by **~0.16 screen widths** between eyes → **~0.07** in the vignette scalar `V`,
while the entire span between "fully discarded" (`V ≤ 0.789`) and "fully solid"
(`V ≥ 0.86`) is only **0.071**. The eyes are a *full band* apart. The `0.02·dist` term puts
that crossover at **22–45 wu**, i.e. squarely in the normal tabletop pose (hardware log:
headY 28.8, world scale ≈ 22). The rivalry is structural, not marginal.

Note the mod's own dissolve map is sampled at **screen UV** by the shader, so any
non-constant occlusion map is per-eye by construction too.

## Why no fix exists on this shader

* **`M < 1` anywhere** → `max(M,S)` reads `S` there → wherever the vignette drives `S → 1`
  the pixel is solid for every `_Cutoff < 1`; and `_Cutoff ≥ 1` erases the whole wall
  including its foundation. **Partial (per-pixel) and eye-identical are mutually exclusive.**
* **`M ≥ 1`** (reachable via `_EnableOcclusionMap`) removes the vignette — and with it the
  world-Y foundation gradient, because they share one `saturate` — and then the `mad_sat`
  clamps `A·B > 1` to exactly 1, so the discard degenerates to `1 − _Cutoff`: **one
  comparison per renderer**. That is the rejected EYE-LOCK behaviour (binary pop).
* The vignette coefficients are DXBC **immediates**; its per-eye inputs
  (`_WorldSpaceCameraPos`, `_ScreenParams`) live in the engine-owned `UnityPerCamera`
  cbuffer, which **no MaterialPropertyBlock can reach**.
* Near-miss worth remembering: on the *other* variant, `Amp_Basic_WallFade`, the `mad` is
  **not** saturated. There, `M ≥ 1` would have left a genuine view-independent per-pixel
  dissolve on the shader's own **world-space** noise (`ν > (M−c)/(M−1)`, ν ∈ [−0.875, 0.875]
  measured over 60k samples of the exact Ashima simplex). The masonry variant's `saturate`
  is precisely what closes that door.

## Options, ranked (none implemented)

1. **Mod-authored replacement masonry shader** (bundled, built with `/home/claw/unity-2021.3.5`).
   The only option that satisfies *both* hard constraints: same albedo/normal/MRAO, same
   world-Y foundation gradient, world-space noise dissolve, **no screen-radial term at all**.
   The round-11 dissolve-swap machinery (`WallSegmentFade.Dissolve.cs`) already performs
   material copies onto a template shader, so the delivery seam exists. Risk is PBR/lighting
   fidelity across every keep wall plus a bundle rebuild — not geometry.
2. **Straddle-gated hybrid.** Measure per wall (see below); keep today's exact delivery where
   both eyes provably agree, use a binary hide only for walls that would otherwise be
   one-eyed. Keeps the dissolve wherever it is already correct; pops only the offenders.
3. **Vignette guard ("or not at all").** Refuse to fade any wall not provably below the
   crossover in both eyes. Zero look change — but the estimate says at world scale ≈ 22
   essentially no wall qualifies (needs `d_max ≲ 21 wu`), i.e. wall see-through would stop
   working in the main pose. Measure before committing to this.
4. **Status quo** — what shipped: look untouched, rivalry remains. **This is the parked state.**

## What was tried and reverted (do not repeat blindly)

* **EYE-LOCK** (ModBuild 76, reverted in 77): forced `M ≡ 1` and `_Cutoff = 2`, collapsing the
  algebra so no per-eye term reaches the discard. Provably eye-identical — and provably
  binary: the per-pixel dissolve and the foundation gradient both died, single-renderer walls
  popped. Rejected by the user on sight. Branch `worktree-agent-a49b76be3abe69ebd`,
  commit `6a2046e`, if the analysis is ever wanted again.
* **Stereo measurement** (`WallSegmentFade.Stereo.cs`, ModBuild 78, reverted in 79): evaluated
  the shader's own discard scalar for both eyes over each fading wall's AABB every 2 s and
  logged `EYE-STRADDLE '<wall>' … L V/S | R V/S | ΔV ΔS` plus an `EYES DISAGREE` warning.
  Diagnostics only, zero pixels changed. Removed when the topic was parked; recover from
  branch `worktree-agent-a168fe1d87b7105d7`, commit `c72b422`, when work resumes — it is the
  instrument that turns option 2 or 3 from a guess into a measurement.

## If you pick this up again

Start by restoring the measurement (`c72b422`) and running one hardware session. The numbers
it prints — `ΔV` against that wall's `S_gone` / `S_solid` thresholds — decide between options
2 and 3 immediately, and quantify how often option 1's cost is actually justified.
