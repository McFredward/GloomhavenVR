# Town-service mirror validation

## Reproduction

Run `python3 scripts/check-town-service-mirror.py --source-root /path/to/current/checkout`.
Use `--suite basic` for the original-root-Canvas checkpoint, `--no-negative-controls`
for diagnosis, and `--output-dir` to retain a run elsewhere. Unity defaults to
`/home/claw/unity-2021.3.5/Editor/Unity`; override with `--unity` or `UNITY_PATH`.
The runner requires dotnet, xvfb-run, and a working OpenGL software or hardware driver.
All generated projects, production copies, hashes, assemblies, PNGs, measurements and
logs live under gitignored `.planning/debug/town-service-mirror/run-*`.

The harness compiles complete production assets, binding, frame, codec, delta,
material, mirror and neutralization sources against real Unity 2021.3.5 engine/UI/TMP.
Only the external logger, head-camera provider and original texture/sprite lookup
adapters are fixtures. Original UI controllers are fixture MonoBehaviours with real
Awake/OnEnable counters. No fake Canvas, renderer, TMP, transforms or codec is used.

A fresh isolated Unity project imports TMP's bundled essential resources, enters
Play Mode, and runs each independently compiled case through engine frames. It
renders owner and observer into 512x384 render textures. Comparison requires visible
UI, average summed RGBA byte error <= .02/pixel and maximum <=20; disabled-Canvas
cases instead require completely blank images. The small tolerance accounts for
camera/scale floating point differences; missing masked widgets cannot pass.
Each negative control must compile and fail its named assertion. Compilation errors
and unrelated exceptions never count as a detected mutation.

## Checkpoint evidence (2026-09-21)

`run-e7jcex17` passed basic production and six independent negative controls under
Unity 2021.3.5f1 / llvmpipe LLVM20.1.2. Source SHA256 identities are recorded in the run.
Coverage includes TMP/legacy text, rich text, colors, filled sprites, raw textures,
CanvasGroup/inherited alpha, RectMask2D padding/softness, stock stencil Mask, root
pose/scale, raycast suppression and inert template/observer instantiation.

`run-7k1nodb3` passed 198 assertions extending this with sibling reorder, newly added
Shadow, disabled Canvas, cumulative packet loss, four independent owner images,
reopen/late old session, owner close, peer removal and timeout isolation. Four-owner
unchanged playback allocated zero bytes across 1000 ticks (0.6248ms total in that run).
This is a tiny synthetic CPU sample, not an in-headset frame-time guarantee.

`run-63j4pgtu` additionally verified nine negative controls but deliberately failed
the added standalone-section case described below. The full suite remains a red
regression detector until that production defect is corrected. Extended source-read,
changed capture/codec/receive/apply, inactive and closed-owner benchmarks are included;
latest values belong to each run's `cost.txt` and require a successful run for context.

## Source-proven defects found by actual images

1. An extra observer wrapper Canvas changed RectMask2D's root pixel coordinate
   system: a cyan masked widget disappeared entirely. Original clip rect was
   (-142,-86,100,72); observer became approximately (-1.15,-1.13,.90,.84), cull=true.
   Removing the unnecessary Canvas or matching its pose/scale/rect restored the
   image (summed RGBA error reduced from 1,623,486 to 1,511). Production now avoids
   redundant root/child-module Canvases and aligns needed standalone wrappers.
2. The first correction is insufficient for a standalone subsection whose original
   outer Canvas is larger and whose own local animation scale is .8. Actual images
   in `run-63j4pgtu` differ by 367,752 summed RGBA byte values. This requires preserving
   the original outer Canvas coordinate frame, not substituting the section's frame.
   The responsible production worker has this reproducible case.

## Bounds

The fixtures exercise stock Unity widgets and real rendered output, not every native
game prefab, VR stereo rendering, multiplayer transport, asynchronous art readiness,
or hardware tracking. Current production rejects custom Graphic subclasses before
activation; that protects callback inertness but does not demonstrate visual support
for those native subclasses. Hardware and native prefab parity remain separate checks.
