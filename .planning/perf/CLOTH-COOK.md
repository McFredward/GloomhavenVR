# FigureGrab.HeldSize — the 172.93 ms frame (ModBuild 283 report, fixed in 284)

> "Ich habe in meinem neusten Test einen weiteres Performanceproblem festgestellt: **Wenn ich die
> Figuren in meiner Hand größer skaliere hat es immer angefangen zu hängen bzw. hatte ich kleinere
> Hänger.** Versuch das nachzuvollziehen. Logs liegen ab."

## What the log said

`.planning/debug/Player.log`, ModBuild 283, final 30 s window:

```
[Perf] STEPS 30.0s … FigureGrab.HeldSize 2.046ms avg, worst 172.93ms, 139.3ms/s, frames 2044 | …
[Perf] FRAME 30.0s n=2044 … p95 22.40 p99 178.39 max 183.57ms … over-budget 1693/2044 (82.8%)
                          … spikes 104 (49 lines rate-limited) … view height p50 9.1 (7.6..14.3)
```

Ten `SPIKE` lines name `FigureGrab.HeldSize` at 151.70–172.93 ms. **Ten is not the count.** That
window rate-limited 49 spike lines, and `p99 = 178.39` over `n = 2044` means ~20 frames sat above
178 ms. 20 × 171 ms ≈ 3.4 s against the step's 4.18 s window total — i.e. the spikes are essentially
the whole of it.

**The steady component is not a thing.** In the three preceding 30 s windows
`FigureGrab.HeldSize` does not appear in the `STEPS` top-6 **or** in the `TAIL` line, whose floor is
`1.0 ms/s` — so its cost with nothing being resized is under 0.011 ms/frame at 90 Hz, in windows
where `FigureGrab.OffsetAnchorSelect` (1.3–1.9 ms/s) shows figure-grab was active. The `2.046 ms
avg` is 4.18 s of spikes divided across 2044 frames, not a per-frame cost.

## Which call cost it

Measured, not reasoned at, in a Unity **2021.3.5f1 Linux standalone player** (the same editor the
bundle is built with), each call on its own frame, with a NULL control (empty timed body) and a
known-positive control (`AddComponent<Cloth>`, which must cook). At 3721 cloth vertices:

| call | ms |
|---|---|
| NULL control (instrument floor) | 0.0001 |
| `coefficients = next` (cloth enabled) | 0.0069 |
| `coefficients = next` (cloth disabled) | 0.0013 |
| `SetEnabledFading(false, .12)` | 0.0008 |
| `enabled = false` | 0.0023 |
| `ClearTransformMotion()` | 0.0006 |
| **`SetEnabledFading(TRUE, .12)`** | **19.37** |
| known-positive: `AddComponent<Cloth>` first cook | 19.2–20.3 |

Cost is linear in vertex count — **2.8 / 8.0 / 18.6 / 34.7 ms at 441 / 1681 / 3721 / 6561**, about
**5.3 µs per cloth vertex**. His 172.93 ms ÷ 3 cloths ÷ 5.3 µs puts his capes near **11 000
vertices** each.

**The attribution in the round brief was wrong.** It named `Cloth.coefficients` as the re-cook. The
coefficients write is three to four orders of magnitude cheaper than the enable. The mechanism:
`SetEnabledFading(false, …)` lets the component go `enabled = false` **by itself** once the blend
finishes (measured: it reads `False` 40 frames later with nobody writing it), so
`SetEnabledFading(true, …)` is an **enable transition** and PhysX re-cooks the fabric on the main
thread. The `if (!c.enabled) c.enabled = true;` backstop underneath it measured 0.0031 ms — it
looked innocent only because the line above had already paid.

**Dropping the `enabled` writes and keeping the fades does nothing** — measured at 20.45 ms,
unchanged, for exactly the reason above. The `enabled` writes were never the cost.

## The replacement, and why it is the same picture

Writing `maxDistance = 0` pins every particle onto its skinned position. Four arms, same authored
slack, root scaled 1.345×, drift against the authored mesh vertices (which for one identity bone
*are* the skinned positions), sampled at +1/+2/+5/+15/+45/+90 frames:

| arm | +1f | +2f | +5f | +15f | +45f | +90f |
|---|---|---|---|---|---|---|
| A cloth DISABLED (shipped behaviour) | 0.48790 | 0.48790 | 0.48790 | 0.48790 | 0.48790 | 0.48790 |
| B cloth PINNED, `maxDistance = 0` | 0.48788 | 0.48793 | 0.48790 | 0.48790 | 0.48790 | 0.48790 |
| C as B plus `ClearTransformMotion()` | 0.48788 | 0.48793 | 0.48790 | 0.48790 | 0.48790 | 0.48790 |
| D SIMULATING, coefficients untouched | 0.40827 | 0.54187 | 0.52367 | 0.41744 | 0.43625 | 0.42217 |

A and B agree to five decimals; D does not. A pinned cloth is indistinguishable from a disabled one
across a transform scale — which is the entire property the disable was bought for — at the price
of a coefficients write instead of a fabric cook. (D is also the ModBuild 137 defect stated as a
number: up to 0.080 off the reference, ~1.2× the rescaled slack, oscillating rather than settling.)

The pin engages in **one** frame (drift 0.04880 → 0.00121 at +1f, flat for 52 more), which is why
the shipped code ramps it over `FadeSeconds` instead of writing it once.

## End to end

The shipped algorithm transcribed into the same player and run head to head against the one it
replaces, same 3721-vertex cloth, scripted stretch (exponential 1 → 1.345 over 45 frames, then 60
quiet), third arm = cloth simply disabled for the whole gesture:

| arm | worst frame | total (105 f) | uploads | ENABLE transitions | worst drift |
|---|---|---|---|---|---|
| REFERENCE (disabled) | 0.45 ms | 0.47 ms | 0 | 0 | 0.00000 |
| OLD (137–283) | **29.12 ms** | 32.17 ms | 2 | **1** | 1.72018 |
| NEW (284) | **0.97 ms** | 3.77 ms | 27 | **0** | 0.06880 |

The NEW arm's 0.97 ms is its cold first upload; across the 43 frames it does any work the
distribution is **p50 0.101 ms, p90 0.119 ms**. Thirteen times the uploads, an eighth of the total,
zero enable transitions, and it holds the cape **25× closer** to the skinned pose during the resize
than the mechanism it replaces (the OLD arm's 1.72 is the frame its re-cook lands on and re-anchors
the cape in one step). Settled coefficients equal `pristine × the factor that actually settled` to
within 1.0e-9.

**These are desktop Linux milliseconds, not Quest 3 over Virtual Desktop.** Read the ratio, which is
a property of the algorithm; expect both columns larger on his rig, and roughly 3× larger again
because his capes are ~11k vertices rather than 3721.

## A latent bug found on the way

**Unity's "unconstrained" sentinel in a `ClothSkinningCoefficient` is `float.MaxValue`, not
`Infinity`.** Read back from a freshly added Cloth: every one of 1681 / 3721 / 6561 default
coefficients came back as `3.402823E+38` for both `maxDistance` and `collisionSphereDistance`, and
**none** as `Infinity`. Builds 137–283 guarded with `float.IsInfinity(max) ? max : max * factor`,
which therefore did not catch it and multiplied `float.MaxValue` by the factor — **overflowing to a
real `+Infinity`** on every unpainted vertex. Both values read as "unconstrained" to the solver so
nothing visibly broke, but the guard was not doing what its comment claimed. Fixed.

## Re-running the harness

`cloth-cook-harness/` holds the two files. To reproduce:

```
mkdir -p /tmp/clothbench/Assets/Scripts /tmp/clothbench/Assets/Editor
cp .planning/perf/cloth-cook-harness/ClothBench.cs /tmp/clothbench/Assets/Scripts/
cp .planning/perf/cloth-cook-harness/Builder.cs   /tmp/clothbench/Assets/Editor/
/home/claw/unity-2021.3.5/Editor/Unity -batchmode -nographics -quit \
    -projectPath /tmp/clothbench -executeMethod Builder.BuildLinux -logFile /tmp/clothbench/build.log
cd /tmp/clothbench && xvfb-run -a ./Build/clothbench -batchmode -logFile ./run.log
grep '\[BENCH\]' /tmp/clothbench/run.log
```

Two things that cost time and are worth not rediscovering:

* `-batchmode -nographics` **segfaults** this player before the first scene loads (NullGfxDevice).
  Run it under `xvfb-run -a` with `-batchmode` only.
* `Cloth.vertices` and `SkinnedMeshRenderer.BakeMesh` both report in the **scaled** frame. An early
  version of the drift metric normalised only one of them and reported the transform scale itself
  as cloth drift — which made a working pin look as broken as no pin at all. Normalise both, or
  compare two arms in the same frame and never an arm against an absolute.

## What still needs his hardware

The ramp is a **constraint** ramp, not the position-space blend `SetEnabledFading` performed. Both
take 0.12 s and neither can be seen from here. If it reads wrong on a figure held at arm's length,
the dial is `FigureCloth.FadeSeconds`, not the mechanism.

The number to read in his next log is the one that was already there:
`[Perf] STEPS … FigureGrab.HeldSize … worst`. It was **172.93 ms**. Beside it, two new rows say what
replaced it — `[Perf] STEPS FigureGrab.Cloth.Seed` (the upload cost) and `[Perf] COUNTS
FigureGrab.ClothSeeds` (how many uploads, and how many landed on the worst single frame), the
latter registered to **print even at zero** so an absent row cannot be mistaken for a silent
instrument.
