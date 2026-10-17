# Quest B682 regressions — 2026-10-17

## Actual hardware evidence

`quest-capture-20261017T123858Z-94934176.zip` SHA-256:
`6bd7ee2918f879072e2715b53356caffad6d86eeee4bc52af44dcb740f7a9cc4`.
Installed/current build is B682, previous B681. APK SHA-256:
`ce6e4731e46f565342aeac5fc47d16d3bc7e77497fd5ad3b2b7fa467ad04fa6c`.
Both supplied screenshots `143705` and the subsequently added `143659` were
inspected. The former shows plain brown cards with retained text and control-board
art; the latter shows actual floor, door, figure and layered wall geometry with
missing-looking regions. The maintainer reports broken pre-menu spinner, absent
menu movie, black dialogs and initiative corruption. The hit marker appears in
Intro, then disappears despite enabled/layer-admitted CPU renderer observations.

The second world census is decisive: two completed tiles, two handles, active
engine, 28 generated root children, 331 enabled renderers, about 498k vertices and
four lights. B682's original lifecycle repair now produces content; preserve it.
The aggregate 76 unsupported materials includes inactive/pooled placeholders and
cannot establish 76 broken visible materials. The private explicit-draw floor
probe has no native per-object lighting, so its black result cannot establish a
lighting defect. The mono camera frustum is not the submitted XR eye frustum.

Actual translated Amp_Basic and WallFade vertex programs consume position,
normal, tangent and sometimes UV0, all present in the representative mesh. They
do not consume COLOR. Missing vertex color on most world meshes therefore does
not justify extending the small primitive repair to all procedural geometry.

## Targeted recovery

Revert only B682's global owned-quad/CommandBuffer copy submission to the B681
Graphics.Blit route with the existing ordinary 2D sampler. A new copy recipe
prevents reuse of B682-derived spinner/silhouette data. Keep the shared density,
full viewport, menu primitive, pile inputs, TMP observer and engine fixes.
Passing Linux Vulkan fixtures did not establish Android XR submission safety;
this regression invalidates the previous candidate's hardware qualification.

Read supported, CPU-readable original image pixels directly for optional Quest
mip filtering. The original character-portrait provider already uses GetPixels32.
When CPU pixels are unavailable, retain the exact original sprite/texture and its
geometry/UVs instead of caching an untrusted replacement. Desktop retains its
existing GPU path. Never guess source damage from failed readback samples or
change legitimate original trim metadata. No original game data is reconverted.

Retain the hit marker's original visual/input ownership. B681 already reported
it absent, so the B682 submission cannot be claimed as its sole cause. Observe
actual late renderer admission/depth facts within the existing two records and
qualify pointer/copy interaction in a focused GPU fixture. No speculative global
white-texture replacement or marker disappearance workaround is authorized.
World observations distinguish displayed materials from hidden placeholders and
room completion from display. They never force lights, visibility or gameplay.

## Validation and next hardware run

The final validation receipt binds exact current source, focused CPU/GPU boundary
and native-source tests, strict compilation, complete archive CRC and released
launcher/API metadata. Inherit unchanged host, density and prior native lifecycle
checks by exact source identity. No new full wire gate, exhaustive shader test,
full game export, local APK build or device end-to-end success is claimed.

Use the B683 Builder and **Mod Update** on the existing local APK/content bank.
The released inventory must classify this as compact-code with unchanged art and
native helper. Preserve saves/settings and avoid a new multi-GB data transfer.
Check intro/menu movie, pre-menu spinner and dialogs first, then laser marker,
upper card art, initiative portrait/frame and Tutorial environment. Capture at
Debug after native generation settles. Source repairs and Linux fixtures cannot
establish the final Quest image or mobile frame cost.

External compact evidence lives under
`/home/claw/quest3-local/build/evidence/B682-regressions-123858-20261017/`.
Reap only owned fixture descendants before completion cleanup. Remove this
round's clean integrated workers and duplicate staged ZIP after publication;
preserve current deliverables, supplied captures, logs/results, source snapshots,
owned game references, native symbols, pins and other agents' active work.
