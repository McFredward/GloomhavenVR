# Exact native UI read work removal

Prepared on `worker/frame-ui-work-20261005`, based on `dev` commit `01503d4c`
(Build625), with the parent's common configuration API preparation. No new build
number, wire record, NPC source or separate Frame binary belongs to this lane.

## Evidence and scope

The supplied Frame capture is Build624. Its three complete, tracked, post-native-
spinner windows still price `CanvasConversion.LateTick` at approximately 1.03 ms
per application frame when weighted by the 810 recorded frames. This scope is
inside the overall frame spans, not an additional cost. Source and fixture work
counts below are not a measured Steam Frame FPS gain.

Build612 already replaced repeated transform subtree discovery with
`UiHierarchyInventory`. The remaining hidden-window inventory still enumerated
the complete native `UIWindow.GetWindows()` registry for every converted panel,
then resolved ancestry for every unfamiliar entry. Each first veil also resolved
the same `CanvasGroup` component and alpha again for every sibling Graphic.
Simply caching the HashSet accessor would leave those costs in place.

`Optimize.SharedUiWindowReads` independently enables the new exact read lane.
The shared API seeds it on for both PC and Frame; it remains adjustable live and
has the same implementation in the common binary. Off retains independent
registry enumeration and the original per-graphic CanvasGroup walk. No visibility,
animation, content, interaction or UI sampling interval is reduced.

## Exact invalidation

Native `UIWindow.OnEnable` and `OnDisable` add/remove the window from one mutable
HashSet (`decompiled/GH.Runtime/UnityEngine.UI/UIWindow.cs:398`, `404`, `642`). The
production `LateTick` interleaves veil discovery with other panel services,
including native opening/materialise callbacks. A once-per-frame snapshot would
therefore miss a later panel's newly registered window in that same invocation.

The new lane opens a disposable scope around the actual complete `LateTick`.
It distributes current registered windows to all current roots with one ancestry
walk per registry member. Before each panel's discovery it reads the live registry
reference and a guarded, allocation-free field-reference getter for the HashSet
mutation version. The getter is admitted only after a private add/remove probe
proves that the selected runtime field changes. Unsupported runtimes retain the
original independent path. A registry Count shortcut is deliberately insufficient:
one removal plus one addition can keep the count unchanged.

`UiHierarchyInventory` also exposes a synchronous global event revision.
Native reparenting and activation invalidate routing even when HashSet membership
does not change. Registry edits recapture window components when required, which
also retains a UIWindow added to an existing transform and disabled again before
the next read. Ordinary unchanged passes reuse all warmed routing storage and
perform no component-inventory recapture. Disposable cleanup clears the shared
root/route references even when a native panel callback throws. Per-panel original
component inventories retain their existing lifetime and retire on panel release.

The pure Graphic collection inside one veil reuses CanvasGroup component, parent,
alpha and ignore-parent reads for shared ancestors. It clears this table before and
after that loop, preserves the original leaf-to-root floating-point multiplication
order, and does not reuse any presentation property across panel/native callbacks.
The Unity 2021.3 bindings implement renderer alpha via its colour channel and expose
the direct cull property separately from uGUI's managed mask callback path; the
fixture also observes the native cull event during actual ownership writes.
See [Unity's CanvasRenderer bindings](https://github.com/Unity-Technologies/UnityCsReference/blob/2021.3/Modules/UI/ScriptBindings/CanvasRenderer.bindings.cs).

## Existing veil safety edges

One source-proven edge also needed correction: native pool insertion can put new
Graphic nodes under a UIWindow already veiled. The old discovery skipped the
entire window once `IsVeiled` was true. Topology/activation changes now admit only
missing Graphic identities into that existing veil before rendering. Settled
game-drawing exemptions are retained, so unrelated insertion cannot re-veil a
Graphic already handed back to the game. This safety applies with sharing on or
off. An entire native window reparented out of its panel releases the former
panel's hold in the same LateUpdate.

Existing per-renderer alpha/cull reassertion, foreign-write learning, first-show-
tween lift, materialise hold protection and explicit pooled-card release remain
live. No original native gameplay controller, callback, material, transform or
network snapshot is replaced by an inferred state.

## Validation and boundaries

The new `scripts/check-shared-ui-window-runtime.py` compiles the complete production
hidden-window veil, complete shared helper, complete hierarchy observer and actual
complete `LateTick` into Unity2021.3.5f1. It uses the publisher's genuine
`UnityEngine.UI.UIWindow` from `GH.Runtime`, real CanvasGroup/Graphic/CanvasRenderer components and actual
native OnEnable/OnDisable registry changes. Unrelated fit/seat/particle services
are explicit fixture boundaries. Native opening callbacks mutate later panels
inside the original loop; compilation failures never count as successful causal
controls.

The 20-panel/100-window fixture proves **2,000 to 100 registry-member reads** per
unchanged pass and **4,800 to 900 CanvasGroup component reads** during first veils.
It covers same-frame registry growth, equal-count replacement, native reparenting,
disabled component admission, late nested activation, existing-window Graphic
insertion, pooled hand-card renderer release, every intermediate foreign alpha,
first native show-tween lift, live original materials/content/rect/position,
ignore-parent groups, live Off, same-frame new panels, cold presentation reset,
warmed storage reuse and failure cleanup.

The complete final run passes **1,651 production assertions**, **1,650 unsupported-
version fallback assertions**, and **11 causal controls** (13 variants total).
The receipt is `.planning/debug/shared-ui-window-runtime/run-8m6ek1tf`.
The original inventory consumer passes **1,021 assertions and five causal controls**
at `.planning/debug/ui-inventory-runtime/run-t5njp3b_`; native town interaction passes
**7,886 assertions / 70 variants** at
`.planning/debug/town-service-interaction/run-ykhzauko`.
Strict Release passes with zero warnings/errors; all 11 locked frame orderings
and the partial initialization order check (42 types / 277 parts) pass.

The original-game dependency import also starts unrelated player-only input,
outline and debug-service bootstraps in the editor. Their retained editor log
contains missing outline shader/debug prefab and player input Editor-update errors.
The explicit UI runtime assertions and original UIWindow callbacks still execute;
these receipts therefore prove the UI read/ownership contracts, not a clean full
game/editor session or a performance timing measurement. No error is suppressed
in the retained log.

The existing UI inventory fixture continues to bind the original production
consumer and covers stable inventories, inactive pooling, depth restoration and
same-frame original sprite arrivals. The native town interaction suite separately
retains the existing merchant renderer ownership and service lifecycle proofs.
Frame order and partial static initialization order checks remain required.

The new suite must be registered in `scripts/test-suites.json` by the parent.
The integrated common tree still needs its complete final gate and golden vectors.
No headset, multiplayer timing or pixel result is established by these tests.
Warm storage identity and zero component-recapture counts are structural evidence;
they are not a calibrated measurement of zero managed allocation for the complete
production UI subsystem.

## Hardware comparison

Keep all other settings fixed and compare SharedUiWindowReads on/off after room
preparation has completed. Open original task-specific windows, insert/pool cards,
switch nested character views, and close/reopen windows during materialise.
Verify both eyes, every show/hide intermediate frame and native controls. Repeat
as Frame host/client with two and four players. Compare complete loaded FRAME
windows and the existing `WorldUI.HiddenWindowVeil` / `CanvasConversion.LateTick`
scope receipts; do not turn per-frame diagnostic detail into normal log streams.

## Integrated Build627 review

The complete current production consumer and its unsupported-runtime fallback were
rerun in Unity2021.3.5f1 after common 627 integration. Receipt
`shared-ui-window-runtime/run-mtq9v0_j` passes 1,651 production assertions, 1,650
fallback assertions and all 11 causal controls. Read/write ordering, pooling,
same-count registry mutations, same-frame hierarchy edits, renderer ownership and
disposable failure cleanup remain intact; no further local UI source change was
needed. This is source-bound work/parity evidence, not a Frame timing measurement.
The parent still runs the final complete integration gate on its final tree.
