# Original card layout ownership and native message attribution

This implementation addresses the third target in the Build617 Frame hardware
review. The source establishes redundant presentation writes; it does **not**
establish that removing them eliminates the measured 207.26 ms native callback.
The next headset capture must measure the effect. Game data, native queue order,
the 8 ms outer message-loop budget, gameplay callbacks and wire records are unchanged.

## Remove the competing flat card-root writer

The shipped `FullAbilityCard.UpdateView(FullCardViewSettings)` stores its settings,
then its private overload calls `UpdateScale` and `UpdatePosition` in action
selection. The first private body only assigns `transform.localScale`; the second
only assigns `RectTransform.anchoredPosition` when the settings request it.
`CardsHandUI.UpdateView` visits every original widget, including full faces already
adopted into VR. The supplied action-selection receipt contains 24 widgets.

For an adopted face these flat poses compete with `CardFace.Maintain`, which
reasserts the existing host fit and centred root pose. They can temporarily change
the face and dirty its canvas before being repaired. The new prefixes suppress
**only those two private geometry bodies**, while all of the following remain:

- Public `UpdateView` and its original `ViewSettings` assignment.
- Native highlighting, hover events, validity, action phase and hand bookkeeping.
- Original child-widget, burn, pulse, animation, local/remote and input paths.
- The existing VR fan layout, card home motion and face-fit writer.

The guard requires live VR, the active hand-suppression module, the existing
adoption registry and a physical `VRCard` ancestor whose current `FullCard` is this
face and whose adoption is still live. Inactive ancestors are included: closed
fans remain physically adopted. A stale registry alone cannot suppress the native
writer after a dialog takes the face. Returned/yielded faces, remote prefab clones,
flat/VR-off and module teardown retain the original methods immediately. Original
map/2D/non-NPC windows are not suppressed by this change.

This removes up to two competing transform assignments per affected widget/view
refresh. It is a structural saving, not a measured claim about milliseconds or
GPU canvas work. In particular, the entire 207 ms native callback is not declared
fixed, and native Show/UpdateView may not be skipped as a substitute: they carry
the original continuation and half-selection state.

## Attribute the remaining native and fan work

The existing Debug-only native probe now selects original `ProcessMessage`, all
native hand Show/UpdateView/UpdateCards/half-controller Init overloads, active-bonus
and item-bar setup, infusion-board refresh and native full-card construction.
The actual Choreographer publisher assembly takes priority over a loaded type
with the same short name. Optional third-party callbacks retain their existing
explicit-name fallback and unavailable targets still report `n/a`.

For `ProcessMessage`, a fixed, bounded original-enum ledger preserves per-frame
message type, inclusive total/worst time, invocation count and failure count. A
finalizer returns the original exception without swallowing or replacing it.
Nested calls retain their own state. Off-main invocations retain native dispatch
but do not become main-thread frame costs. The ledger clears at the existing
frame boundary, continues after the first 120 summary frames, and is formatted
only by the existing rate-limited SPIKE emitter. There is no new ordinary-level
per-action/per-frame stream, no deep profiler mode, no card identity and no queue
throttling or message reordering.

Four existing-path scopes separate `Cards.Rebuild`, `Cards.AdoptedCard`,
`Cards.FanOpen` and `Cards.FanPublish`. These wrappers preserve calls and exception
flow. All native/scoped measurements are inclusive and nested; adding them or
subtracting them from a complete frame does not produce an exclusive CPU cost.
Native/engine work not selected by the probe remains unassigned. Missing evidence
must not be treated as zero cost or a proof of GPU/GC causality.

## Validation and boundaries

`scripts/check-native-card-layout-runtime.py` executes the **actual shipped
GH.Runtime FullAbilityCard** private transform writers and public UpdateView/hover
event in Unity 2021.3.5f1. Ownership/mode facts are external seams; settings, native
bodies, hierarchy and transforms are original. Production passes 13 assertions.
Seven causal controls fail their intended behavioral assertion: disabled suppression,
omitted inactive ancestors, stale parent, mismatched face, VR-off, module-off and
hidden original failure for invalid native settings.
The first real-body test caught the inactive-ancestor defect; failed receipts are
retained rather than described as passes.

The complete production native probe runs in the existing census fixture, with
external native dispatch shapes and real Harmony hooks/Unity frame boundaries.
It passes 1,433 production assertions plus 13 actual-engine-frame assertions;
four relevant controls catch omitted/retained message ledgers, swallowed original
failures and background work incorrectly charged to the main thread. Nested
dispatch, callback count preservation, normal-level disabling and post-120-frame
coverage are exercised. Its initial multi-variant failure exposed a fixture short-
name assembly ambiguity; the publisher assembly is now selected consistently.
The final bounded enum-size expression is rebuilt and production-rechecked.

Strict Release compilation has zero errors and warnings. The primary integrator
runs the complete required final-tree gate, source/patch/surface registry checks
and byte-exact wire vectors. Focused worker checks are not the full gate. Hardware
pixels, actual timing gains, multiplayer intermediate-state parity and the precise
remaining native expensive message still require the next supplied capture.
