# Build738: first-frame level-up hover pose and stable confirmation rendering

## Hardware report and scope

The maintainer confirms the Build737 shadow, card-depth and level-up-list repairs.
The remaining report concerns a wrong first orientation when sweeping the laser
over level-up cards, and severe flicker of the original card-choice confirmation.
The latter also occurred before owner containment, while it was standalone.

Six immutable hardware log copies and hashes are retained under ignored
`debug/menu738/hardware-inputs` and `round-start.json`. Both fresh local banners
identify Build737, D3D11/RTX4090, Unity2021.3.5f1. Remote717 remains stale. There
is no fresh screenshot/video. The supplied episode seats the confirmation once
at line4629; repeated forced-hidden cleanup begins after closure at4746. Those
5091 later cleanup reports do not prove repeated visible seating or explain the
reported flicker. Keep that distinction when reporting the cause.

Class B: two local level-up presentation defects, with no general capture,
allocator, wire or actor-authority changes. The fresh Build737 complete checkpoint
supplies the unchanged input-bound basis:264/264 local suites,306431 actual
goldens,2747 pinned inputs, strict Debug/Release, metadata and source16. Execute
affected containing suites/controls and dependent consumers on the final union;
do not repeat the complete checkpoint solely for this bounded repair. Escalate
if implementation requires a broader shared mechanism.

## First visible hover card

Original `UILevelUpCardHighlighter.HighlightCard` calls
`UILevelUpCardHolder.PlaceNewCard(immediately:true)`. Its original pool resets
local position and scale but retains world rotation when reparenting. A yawed
converted window therefore receives the inverse local yaw. The ordinary budgeted
flatten discovery later corrects it; rapid pointer transitions expose the delay.

Correct only the exact newly placed card root synchronously at that native UI
seam, inside an actually live converted window. Gate on running VR, conversion,
manual-screen exclusion, immediate hover and the original holder parent. Preserve
the original pool, position, scale, fades, callbacks and delayed reveal animation.
Do not patch the global ObjectPool or increase recurring flatten work.

`MenuDepthLevelUpHoverObserver` is registered through the existing WorldUIModule.
The generated patch inventory now records220 classes/334 methods, up by exactly
one observer/method. Performance fixture ports expose only the same live-panel
and manual-screen inputs required by this added source; they do not alter oracles.

The regression must execute the original Highlighter/Holder/Spawn/Recycle route,
rapid pooled swaps and first final head pixels against an independent window
basis. An editor/game uGUI API mismatch is an explicit test port: a private
GH.Runtime copy removes only the unavailable LayoutRebuilder.Enable setters,
with before/after instruction hashes. Original game references remain read-only;
editor layout timing is not claimed as original game timing.

## Confirmation-specific rendering difference

Both the persistent owner member and the older standalone DialogSurface omit
`useModLayer`. The default false bypasses `PanelSupersample.Eligible`, filtered
window capture and existing scoped GrabPass neutralization. The logs contain no
supersampling report for that member, while its owner and ordinary modal windows
use the normal filtered route.

The exact native generic/MainMenu confirmation prefabs include a full-stretch
UI_Blur image using Custom/SimpleGrabPassBlur and the original UIBlurDisabler.
The shader has three GrabPasses and three color passes with ZWriteOn/LEqual and
opaque replacement blending. A zero-alpha Graphic is not sufficient to make
this custom shader invisible. Flat screen-space assumptions do not transfer to
coplanar world-space text/buttons and the eye framebuffer.

Opt these two local conversion consumers into the existing mod capture layer;
do not change the shared renderer, shader bank or material assets. Preserve the
original dialog, actor permissions, button callbacks, modal input, persistent
scene ownership and subsequent close/reopen. Prove the mechanism with pinned
production eligibility/camera/projection/material/resolve/display kernels and
consecutive final head pixels. Native shader raster states may use a declared
GL fragment stand-in; do not call those original D3D11 FX pixels.

## Validation and delivery

The separate after-close loop is now source-proven: real ConvertedPanel.IsAlive
means only Target!=null, and the native target intentionally survives release.
ReleaseAt tested that property as though it described the conversion lease, so
it retained the same seat indefinitely. After Release, test the existing
PanelAlive predicate (host plus active registry). The subsequent original-home
ReferenceEquals guard still retains a refused restoration. Both pure/native
fixture ports now model the real Target-only contract; a refused detach/retry,
50 closed ticks without another release and a fresh native reopen are covered.
This repair is local to the confirmation owner and is separate from the visible
GrabPass/depth flicker.

Implementation and focused proof remain in two disjoint worktrees from published
`9a3736d4f`. Root owns registration, fixture compatibility outside those lanes,
the build stamp, final integrated checks and documentation. Record final counts,
input bindings, failures, elapsed validation and hardware limits after source
freeze. Preserve every failed attempt and source/capture/input/log artifact.
After every run use central completion cleanup or `cleanup_completed_run`, then
archive/hash-verify/retire the two clean owned worktrees. Stop only private ADB.
Delivery is `origin/dev` source; the Windows installation script builds its archive.
