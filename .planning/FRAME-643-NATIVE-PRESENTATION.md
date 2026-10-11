# Frame 642 hardware follow-up: native menu and attachment presentation

The paired Frame/PC inputs both identify ModBuild 642, commit `cf0da0bb8`.
The maintainer reports that the repeated cold-menu ghost is gone, but one visible
flash remains after the initial menu load. Four Frame screenshots also show
opaque columns and shelf-like masonry attachments beside already fading walls;
the equivalent PC test fades those pieces. The presentation repairs preserve the current
material/geometry budgets and the shared PC/Frame binary. They add no quality
setting, room limit, animation throttle, asset-bank change or wire-layout change.

## Native fade capability

The private world shader exposes a union of native properties, including `_Tint`,
`_Color` and `_Cutoff`, without the original shader name and material wall gates.
Reading that private schema classified opaque native masonry as an alpha rider.
It then skipped the native map/cutoff dissolve path. The input audit records zero
toggle-native admissions on Frame versus seven on PC; this supports the source
finding, but a screenshot cannot identify an exact renderer instance.

Wall discovery, attachment channel classification, donor selection and swap
sources now resolve the current canonical native material in every renderer slot.
Existing synchronous shader/material caches operate on that source. No mapping
is retained across frames and inspection does not restore bindings. Native writes
still revoke active substitutes before delivery; swap restoration stores native
sources rather than disposable private variants. Predictions follow the same
native-first capability order as actual delivery. Authored opt-outs, floor and
held-prop protection, original textures and smooth dissolve delivery remain.

## First native menu render

Build 640's final capture guard only handled cameras already in `CapturedSet`.
Both supplied runs create/capture `MainMenuVideo` after the first menu show.
A camera created or enabled after the ordinary camera census could therefore
render its first image with a backbuffer target, stereo enabled and the floating
screen included in its mask. Later images were already protected by 640.

While native screen capture is active, the final culling boundary now adopts
an unknown enabled backbuffer camera through the existing stack policy before
its first render. That policy retains original camera identity, base clears,
split routing, stereo mirrors and release state. The head camera, foreign render
targets and disabled manual cameras remain outside that admission. Ordinary
known-camera rendering retains its dictionary lookup. This closes a source-proven
first-render window; actual headset causality and visual acceptance remain open.

## Saved eye resolution survives a full viewport

The Frame inputs save0.8 but later report native eye viewports 1.0 at 3408x3408.
The existing bounded repair stops after three spaced resets; API acceptance did
not persist. Reserving `max(1, saved resolution)` therefore defeated the saved
reduction on this provider. The stopped-display startup now selects the saved
capacity itself after successful initialization and before `StartSubsystems`.
A full viewport then uses the smaller target. Actual accepted capacity is still
read back, and a refused startup allocation retains the provider's capacity.

Live changes retain the existing viewport-only path, slider quiet period,
camera/deferred guards, bounded repair and no allocation fallback. A request
above the startup capacity remains saved until restart, including 0.8→1.0.
The default remains 0.8 for Frame/standalone and 1.0 for PC; the existing resolution
row controls the tradeoff in every profile. Startup source-bound XR models can
prove the call policy, not real provider allocation, FOV or headset stability.

## Validation and hardware limits

The focused Unity fixtures execute source-extracted production paths with real
cameras, materials, property blocks and pixel output. Their named adapters do not
recreate the entire game scene or an attached Steam Frame compositor. A passing
fixture is not a headset picture acceptance. The final handoff records exact
source hashes, focused assertion/control results, strict builds, source gates,
wire golden vectors and the compiled scope. Unchanged suites inherit the checked
642/641 evidence; no new complete local-suite pass is claimed.

The reproducible input hashes, loaded-window audit and proof receipts are kept in
the main checkout's `.planning/debug/frame642-followup/`. Repeat the cold start
before loading a scenario, then check the supplied wall views in both eyes with
the same material settings and at partial as well as complete fade. Loading
stalls are outside this performance review.

## Loaded performance evidence

The later one-room Frame window averages 38.70ms/frame (17.10ms mod), with 0.765ms
of optional cache preparation still present. Six native-loaded, figure-steady
five-room windows average 77.26ms/frame (43.40ms mod); the later subset is 75.30ms
(41.68ms mod). These are completed game-frame intervals, not compositor FPS.

Against the earlier 638 five-room capture, world preparation falls 10.58→5.85ms
at nearly unchanged 255 candidates/frame. A closer height/distance pair supports
the same direction (10.39→6.59ms, −36.5%). Terrain 12.85ms and wall maintenance
10.12ms remain major costs. Views, fade states and thermals were uncontrolled;
the complete frame means do not establish a net 642 performance gain. Terrain's
new cap break was not exercised: fallback/deferred counters are zero. The actual
boundaries of each timer/counter and the chosen windows are recorded in the audit.

PC uses native material/terrain settings, so its 29.16ms five-room mean is not an
optimization A/B. The remote input is still 638; no new multiplayer-performance
conclusion is supported. The next performance target remains terrain per-eye
preparation and wall decision/reclaim/census work, with native fading retained.
