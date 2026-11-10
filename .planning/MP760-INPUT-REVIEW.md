# Build760 input review

Worker base: `dev6a390f198`. This bounded input lane is part of the integrator's
ClassC union. The parent owns the final build/source/full-local gate and delivery.
Hardware inputs are pinned under main `.planning/debug/mp760/hardware759`;
both players ran759 and **remote is the host**.

## Starting-card overview: source-proven wheel interception

The user clarified that “zoom” meant **scrolling**. There is no new zoom feature
and no locomotion-priority override. The original `UICharacterCreatorCardsViewer`
puts full ability cards in a vertical `ExtendedScrollRect`. Its original `Show`
method spawns those cards into that list and enables the full-card presentation.

The original four full-card prefabs have twelve ordinary `EventTrigger` adapters
on the card or its ability buttons. They contain only PointerEnter, PointerExit,
Select and Deselect entries, with no Scroll entry. Nevertheless, `EventTrigger`
implements `IScrollHandler`. The previous `UguiPointer.Scroll` called
`ExecuteEvents.ExecuteHierarchy` from the hovered card graphic. Unity stopped at
the pointer-only adapter, which did nothing, so the actual ancestor list never
received the wheel. A list gap had no adapter and reached the original list.

The correction walks the original ancestor chain for the nearest active wheel
owner, skipping **only exact ordinary `EventTrigger` instances without a Scroll
entry**. Explicit Scroll entries, subclasses with custom behavior, other
`IScrollHandler` components and nested lists retain their original priority.
The original `ExecuteEvents` callback, `ScrollRect`, wheel response and per-hand
arbitration remain responsible for movement. Hover, click, drag and game choices
are unchanged. A component buffer is reused; no scene search is introduced.

`scripts/ugui-scroll760-runtime/run.py` compiles the actual production
`RayUguiDriver.TickStickScroll`, `UguiPointer.Scroll`/`FindScrollTarget`/`GetData` and
the complete `UiScrollFocus`. Real Unity `GraphicRaycaster` resolves a viewport
gap and a full-card descendant. Real `EventTrigger`, `ScrollRect`, hover callback
and nested list exercise the dispatch failure and correction. Tracked stick
values and the settled hover assignment are explicit fixture inputs.

Evidence in worker `.planning/debug/ugui-scroll760-runtime`:

| Run | Result |
|---|---|
| `run-f0m65jn4` | Production16 assertions passed; four causal controls passed: old empty-adapter interception, explicit Scroll entry, derived adapter, disabled handler. The deadzone control escaped because the fixture started at the clamped top and its erroneous reverse scroll was clamped away. |
| `run-o_ha_eqx` | Production16 assertions and the repaired deadzone control passed,10.86s. The deadzone test now starts at the midpoint. |
| `run-g5rhhcqy` | Fresh current shared-resolver source: production16 assertions and all five compiling causal controls passed,16.63s. |

The four retained controls terminate **before** the changed deadzone-test tail;
their executed source/input prefixes are unchanged. This is aggregate focused
coverage of16 production assertions and five compiling causal controls, not a
new complete gate. Native metadata audit binds both original campaign scenes,
four original full-card prefabs and all twelve pointer-only adapters. All run
processes were reaped and `cleanup_completed_run` removed regenerable caches.

Limits: this is actual Unity2021.3.5 UI behavior, not live OpenXR, Windows input
or a new hardware acceptance. It does not establish the cause of any earlier
turn deadlock. Original state, network authority and the protocol are untouched.

## Windowed native Options input: two independently reproduced coordinate faults

The user isolated the failure to windowed mode; returning to fullscreen repaired
their button. Hardware759 alone does not identify which native coordinate gate
failed. The source exposes two separate, reproducible dependencies:

1. Our laser already projects the physical hit into the event camera's pixels.
   The original `GraphicRaycaster` then calls `RelativeMouseAtScaled`, which may
   adjust window coordinates against the desktop again. The exact game's utility
   body reproduces the miss for a 1920×1080 window on a 3440×1440 desktop.
2. A world window can be visible through both eye frusta while lying outside the
   head camera's desktop-mono viewport. With fixed button geometry at 38 degrees,
   FOV60/aspect1 rejects it and aspect2.389 accepts it. Changing native window mode
   or resolution must not change the interactive limits of that physical pane.

`UguiRaycastCoordinates` uses an exception-safe, nested exact raycaster/event-data
lease. The native display call retains synthetic event-camera pixels only for
that exact query. Every other native or nested foreign query calls the original
typed delegate, retaining its exceptions. Two atomic transpiler seams preserve
the original graphic/mask/CanvasGroup/reversed/blocker/far-clip filters and the
actual original `ScreenPointToRay` argument. Unsupported call counts, exception
boundaries or IL prefixes leave the original method intact with a bounded warning.

For explicitly admitted **world** planes, the viewport result is intentionally
classified against the originally admitted host's committed interaction frame,
not desktop-mono bounds. The helper independently intersects the original raw
camera ray in the actual host's local coordinates, using inverse vectors so
negative/nonuniform scale is respected. Finite, forward, positive and in-frame
conditions are mandatory. No constant-inside value, clamping, new permission,
second graphic-filter evaluation or camera/projection/render write is used.
Nested raycasters carry the original host token; raised/native-remapped surfaces
and queries without that admission retain their original viewport classification.

Debug-only receipts sample changed window/camera configurations at most twice a
second and emit at most twelve reports, including actual native coordinate output
and available eye/mono classification. Ordinary hover adds no normal-level stream.
These receipts can distinguish the two branches in the next hardware run.

## Captured main-menu/map UI and original mouse module

FlatScreen's five explicit `RaycastAll` callers use the same exact-event-data
captured lease. Its eligibility comes from the actual current capture producer:
VR running, exact root ScreenSpaceCamera/event camera and its currently owned RT
or active scrub target. This supports canonical UI pixels that extend beyond the
physical desktop window, without changing physical mouse or ordinary canvases.

The original `PointerInputModuleExtended.GetMousePointerEventData(int)` keeps its
own event-data allocation, reset, processed position, delta, complete raycast list
and result selection. A single-call shim labels only its current module/current
EventSystem query when the actual added, enabled, current mod mouse owns that
**processed** position, is recent/unparked, and no native legacy mouse provider
owns the input. Queued next-update shadow coordinates are deliberately excluded.
Native unrelated modules and pointers retain their original route. Both
transpilers refuse unsupported IL prefixes and exception metadata atomically.

FlatScreen wheel dispatch shares `UguiPointer.FindScrollTarget` rather than
duplicating the empty-EventTrigger rule. Its original wheel speed and hand
arbitration remain unchanged. Original game ScrollRect drag uses event position
directly; original Slider/Scrollbar drag retains raw event position on primary
display0. Actual dragging therefore needs no extra global coordinate patch.

## Executed proof and limits

| Proof | Current result |
|---|---|
| `ugui-coordinates760-runtime/run-40rgeu_0` | Production56 assertions and all ten compiling causal controls passed,27.38s; source-stable. |
| `native-ui-pointer760-runtime/run-cou7pxv2` | Actual game native mouse method, FlatScreen click/wheel/drag and processed InputSystem ownership: production32 assertions passed,13.81s. Seven pre-existing causal controls passed in `run-ahdpkyj3` before its changed slider-settle tail; this earlier helper proof predates the added world-plane branch. |
| `native-ui-pointer760-runtime/run-4ezodf27` | Final world-helper/current native-prefix source: production33 assertions and the new compiling prefix-boundary causal control passed. The integrator executes all nine current common cases in the final union. |

The world proof runs the actual editor `GraphicRaycaster` with Harmony, the
production plane intersection, original native Button/CanvasGroup and the full
coordinate helper. The exact game's `RelativeMouseAtScaled` algorithm is
separately extracted and replayed at its call seam with explicit OS/display
inputs. The executed editor UI DLL differs from the game's UI DLL and has no
physics-blocking delegates; its blocker result is compared with the same original
editor behavior and a real collider. Actual framework assemblies, decompiled
originals, copied fixture/production sources, hashes, failures and cleanup receipts
are retained. The common proof calls the actual game's native mouse binary; its
capture lifecycle membership is an explicit input while the producer predicate
is compiled from main's actual source.

The backbuffer case's two 100-degree eye matrices are explicit visibility inputs,
not a live OpenXR device. Neither suite proves real Windows window-manager
mapping, the exact reported Options failure branch, or headset visual acceptance.
Camera and gameplay authority are unchanged. These focused proofs are not the
complete union gate, which the integrator runs once after source freeze.

Failed fixture attempts remain evidence: ambiguous initial Harmony target,
editor/game UI differences, missing Canvas settle for late-created controls,
a nullable negative-control mutation and omitted nested-canvas fixture membership
were corrected without widening production. Every completed run reaped its
processes and called `cleanup_completed_run`; only evidence and deliverable
assemblies remain. Main's display-lane `OwnsNativeUiPixels` API is a required
integration dependency; no additional CanvasScaler patch is introduced here.
