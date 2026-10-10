# NPC665: retain native summon layout while masking its duplicate print

## Input and scope

Both frozen player logs in main `.planning/debug/npc665/inputs` identify ModBuild664.
The supplied `zauberin_overlay_offset.jpg` was inspected before assigning a cause:
small summon stat highlights sit above their printed cells, while the broad action
rectangle is correctly aligned. Loading, physical mesh/front coupling and stable
live overlay visibility are accepted hardware behavior and are outside this repair.

This worker changes only `TownServiceNativeEnhancementCardMask` production code.
The existing whole-card fit, row rectangles, ring, motion, fonts, materials,
interaction callbacks and transport budgets retain their existing implementations.
This is a source/runtime repair; the new headset result remains unverified.

## Native dependency and minimal repair

The actual game `SummonContainer.prefab` is imported from
`misc_gui_assets_all.bundle`, root pathID `-6366866636496485946`. Its native
VerticalLayoutGroup and ContentSizeFitter use the enabled SummonName TMP's
preferred layout height. The game's `CreateLayout.CreateSummon` populates that
label and the four native cell targets:

| Native target | Enhancement line |
| --- | --- |
| SummonLT | SummonHealth |
| SummonLB | SummonAttack |
| SummonMT | SummonMove |
| SummonMB | SummonRange |

The old mask disabled every pooled printed Graphic except native area graphics.
Disabling the title TMP also removes its ILayoutElement contribution. A layout
rebuild then moves the small stat boxes upward; the independently adopted physical
FullAbilityCard keeps its enabled title and its original cell positions. Broad
manually placed RowContainers do not depend on this title height. The exact old
Build664 mask reproduces this difference: all four cell centres shift upward by
approximately 22.35 card units in the editor-font fixture. That value is a measured
fixture result, not a hardcoded correction or a claimed headset/font metric.

The mask now identifies the layout title through the actual typed
`SummonContainer.SummonNameText` reference, keeps its original enabled state and
suppresses only its CanvasRenderer alpha. The existing late/pre-render fit path
reapplies that suppression after native writes. Restore reinstates the captured
renderer RGBA and original enabled state exactly. Native Graphic colour remains
untouched, including genuine subsequent colour changes. No cross-card semantic
matching or Y translation is needed.

Both existing native-area exemptions now include inactive ancestors. Otherwise
an initially inactive summon's real area images were incorrectly classified as
printed artwork and stayed disabled when the branch became active. This uses the
same real component type, without object-name or address guesses.

## Proof and boundaries

Repeatable maintained suite:

```sh
python3 scripts/npc665-overlay-runtime/run.py --source-root . --controls
```

Requested registry ID: `npc665-native-summon-overlay`; root owns registration.
Its default output is worktree-local `.planning/debug/npc665-overlay`.

The runner exports the actual serialized summon hierarchy/layout properties and
binds the game's unchanged `UIEnhancementButtonHighlight.Highlight`,
`IsInteractable` and `SetInteractable` method bodies from GH.Runtime.dll. The
production mask, Sync.Tick/Publish registration, capture, codec, original admission
and Binding.Apply run in Unity 2021.3.5. It checks:

- all four native stat target centres and highlight corners before/after layout
  rebuild and native enhancement refresh; two physical scales (.6/1.7);
- original and longer German labels, renderer/Graphic colour writes, initially
  inactive→active and originally disabled label branches;
- broad action rectangle geometry unchanged locally and remotely;
- actual local/remote renderer alpha 0, GPU no-ink readbacks of the hidden title,
  positive visible physical-title readbacks and genuine disabled-title no-ink;
- native area images survive inactive→active, exact renderer/enabled restoration
  and a second pooled mask lifetime.

The pre-existing `.0003*physicalPrintHeight` depth separation is included in corner
expectations; planar target geometry remains exact. This suite uses the editor
TMP font atlas and a declared FullAbilityCard outer-root/row-placement boundary.
Native Highlight input/navigation state is a boundary; no purchase callback runs.
The prepared template registry supplies one exact subtree per module. This is
not complete HUD initialization or original native font/shader pixel proof.

Final evidence (worker private debug):
`npc665-overlay-final/run-s46chjwo/proof/run-x9qb0901`.
Production passes 818 assertions; all 12 hidden local/remote label images are
uniform background, and physical-label positive controls pass. Five independently
compiled controls reach the expected named runtime failures:

1. Exact published 664 mask: native summon stat layout moves upward.
2. Remove includeInactive area exclusions: newly active area ink is disabled.
3. Remove late/pre-render suppression: native colour overwrite draws pooled ink.
4. Remove renderer restoration: the original renderer colour is lost.
5. Ignore received Renderer alpha: remote pooled title is drawable.

Source hashes, original serialized data, native method bodies, compiled sources,
DLLs, layout CSVs, receipts and PNG readbacks are retained beside the run.

## Disproved renderer diagnostic

The first pixel probe rendered the label on layer 9. The common VRLayers fixture
puts *all* remote canvases on layer 9, including the genuinely visible physical
card; per-leaf layer changes also fail to isolate uGUI's canvas batches. Its PNG
therefore contained other legitimate ink. The apparent renderer-only failure was
not evidence that TMP resets renderer alpha. Those failures are preserved in
`npc665-overlay-final/run-d_2lmll7`, `run-p80p0d3n`, `run-cta9r3kc` and
`run-aw_l5bvt`; their alpha-control conclusions are rejected.

The corrected readback assigns the selected complete canvas its own layer,
reversibly hides its other graphics, and checks a visible physical-title control.
In `run-10c88trw/proof/run-vz051576`, the renderer-only variant successfully
completed every assertion and was reported as an **escaped diagnostic control**.
That is a preserved disproval, not a green negative control. The temporary
additional Graphic-alpha approach was removed. Final production uses only the
smaller, causally supported renderer suppression.

## Focused validation

Final new suite: 818 assertions +5 expected runtime controls, GPU readbacks above.
Source group: 16/16 passed; strict Debug final build: 0 warnings/errors.
Existing offered-orientation suite passed production plus 9 controls. Existing
handoff suite passed 1515 assertions plus 78 controls after its TMP boundary was
corrected to actual Graphic inheritance. Those unchanged non-summon scenarios
retain valid evidence when the temporary Graphic-alpha branch was removed.
This is focused validation, not a fresh complete local gate.
