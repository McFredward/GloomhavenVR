# Build738: first-frame level-up hover pose and stable confirmation rendering

## Hardware report and scope

The maintainer confirms the Build737 shadow, card-depth and level-up-list repairs.
The remaining report concerns a wrong first orientation when sweeping the laser
over level-up cards, and severe flicker of the original card-choice confirmation.
The latter also occurred before owner containment, while it was standalone.

Six immutable hardware log copies and hashes are retained under ignored
`debug/menu738/hardware-inputs` and `round-start.json`. Both fresh local banners
identify Build737, D3D11/RTX4090, Unity 2021.3.5f1. Remote717 remains stale. There
is no fresh screenshot/video. The supplied episode seats the confirmation once
at line 4629; repeated forced-hidden cleanup begins after closure at 4746. Those
5091 later cleanup reports do not prove repeated visible seating or explain the
reported flicker. Keep that distinction when reporting the cause.

Class B: two local level-up presentation defects, with no general capture,
allocator, wire or actor-authority changes. The fresh Build737 complete checkpoint
supplies the unchanged input-bound basis: 264/264 local suites, 306431 actual
goldens, 2747 pinned inputs, strict Debug/Release, metadata and 16 source gates. Execute
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
The generated patch inventory now records 220 classes/334 methods, up by exactly
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

Production is frozen at `cad48b9a2`. Compiled review confirms three changed existing
UI types, one added immediate-hover observer and nine ModBuild-only consumers;
no production type is removed. The WorldUIModule diff is exactly one scoped
registration after accounting for automatic local-variable numbering. No shared
capture, allocator, shader, actor or wire behavior changed.

The complete card suite passes 2777 assertions and 25 controls in 169.9 seconds:
all prior 2696/20 cases remain, plus 81/5 native hover checks. All twelve first
final RGB rasters match the independent window-oriented reference exactly.
All 45 inputs were stable during that run; final integration retains 44 exact
execution inputs and one uncompiled README-only trailing-blank-line correction.
The private native API port audits all 30345 method instruction streams and changes
only four LayoutRebuilder.Enable setters in two pool methods. Original game
references remain unchanged; original game-uGUI layout-gate timing is unverified.

The complete pure dialog suite passes 162 assertions/13 controls in 44.6 seconds
on eight exact source/fixture bindings plus the exact runner. The native containing
suite passes 157/18 in 55 seconds on 25 exact inputs. Each route has 24 final head
samples with zero RGB difference or lost bright glyphs against its clean reference;
the references retain at least 3441/3526 bright pixels. Old capture flags produce
subpixel/head-motion-dependent differences and lose up to 223/219 bright pixels;
omitting existing blur neutralization loses 30/31. No artificial time pulses or
tolerance relaxation were added. Native widget/session/resource/outer-scheduler
ports and the GL fragment witness are explicitly qualified. Native ExtendedButton
OnDestroy logs from unavailable game services occur at fixture teardown and never
substitute for the named causal-control assertions.

Root's final ten-consumer run retains nine passes and one fixture compile failure
in 218.317 seconds: the new performance input needed an explicit false initializer
under warnings-as-errors. Only that fixture declaration changed afterward. The
complete containing performance follow-up passes all eleven controls in 54.8
seconds. Its production assertion count is 1095 versus the preceding 1067 because
the same three-second run sampled 268 rather than 261 frames, each with four
unchanged steady-state checks. No case or threshold was removed. Cards, native
confirmation and pure dialog worker passes are reused after exact input comparison;
they are not redundantly re-executed merely for the integration SHA.

Strict actual Debug/Release and metadata Release pass with zero warnings/errors
on 1169 stable production inputs. All sixteen source gates pass in 21.4 seconds.
Bundles and bilingual-guide checks pass. Source surfaces retain 670 config keys;
263 Harmony entries and 4846 log tokens account for only the added bounded observer.
The verified 264-suite aggregate inherits unchanged Build737 complete-checkpoint
coverage and all 306431 wire goldens. All 261 other wire compile inputs are exact;
NetProtocol differs only by comments/build stamp. This is not a new complete-gate
invocation or a newly executed Build738 golden executable.

Ignored `debug/menu738/validation-final.json` binds the reports, input comparisons,
compiled scope and retained failures. Both clean owned worktrees are archived,
SHA256-verified and retired; per-run project caches are removed through the central
completion API. Preserve logs, source/input/capture/manifests and current artifacts.
Stop only the task-private ADB after the final owned run and clean untracked
bytecode. Delivery is `origin/dev` source; the Windows script builds its archive.
Actual native D3D11/HMD appearance remains a maintainer acceptance step.
