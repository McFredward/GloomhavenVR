# Tutorial controller visibility — build 518

## Scope and evidence

The maintainer reports one controller disappearing during the custom controls lesson in the
first tutorial and requests both models throughout, with only the applicable controls glowing.
The latest instruction supersedes the historical hand-only card/fingertip/prose step policy.
Worker base: `0bcdf72b` on dev; isolated worktree `fix/tutorial-controllers518`.
The available integration evidence remains local build 515 / historical remote build 500.
It does not establish the exact cause or frame of this newer controller disappearance.

## Source findings and change

- `ControlsTutorial.ApplyStep` still switched both models out on hand-only and prose rows.
  The retired `ShowsController` field and all per-step visibility branches are removed.
  Both models stay up from welcome through dismissal of the closing card. Existing step
  availability resolves the acting hands; both previous highlights are explicitly updated.
- `ControllerVisual.Show` instantiated the controller under the tracked device transform
  without assigning its render layer. Authored parts use layer 0 and names such as `body` and
  `trigger`. `WallSegmentFade.IsModObject` checks each renderer's own layer/name, not its
  ancestors. These parts therefore lacked the exclusion that protects mod visuals from
  scenery fading; layer 0 is also absent from some head-camera masks. Recursive `VRLayers.Apply`
  now protects the model and separately created anchor-only key markers. This is a concrete
  source defect and a possible one-sided disappearance mechanism, not a measured hardware cause.
- Hand hiding and ghosting enumerate `HandRig.Root` (`HandRoot`), while each controller is its
  sibling under `VRHand.transform`. They do not hide the controller itself in the current
  hierarchy. Tests retain this separation and restore only previously enabled hand renderers.
- A cached `_controllersUp` previously prevented recovery if only one model failed construction
  or a tracked hand was rebuilt. The running lesson now checks the pair without allocations
  on the normal path. Missing models retry at most twice per second, with missing-asset warnings
  once per visual. Replacement hands release the old visual and preserve the current key.
- The initial hand/controller transition remains animated. Existing stop/reset/shutdown paths
  remove both models and restore ordinary hand renderers. Lesson scope, native gameplay,
  completion predicates, input settings, multiplayer protocol and assets are unchanged.

## Validation

`scripts/tutorial-controller-tests.sh` compiles the complete production `ControllerVisual` and
`ControlsLesson` plus the exact production step/visibility methods in a lightweight renderer
host. It covers every lesson step, both primary handedness settings, both configured flight
and turn hands, no-key and unavailable steps, marker clearing, renderer layers, smooth entry,
hand restoration, re-entry, delayed one-sided model availability and tracked-hand replacement.
Negative controls restore disappearing models, the historical per-step hide policy, incorrect
highlight routing, missing recursive/marker layers, self-hiding, stale cached pair admission,
stale tracked-hand ownership and dropped highlight transfer. The historical-policy mutation
also removes running recovery, since recovery deliberately prevents that policy from hiding
models for a complete frame sequence.

Focused result: 1,019 runtime assertions, four source bindings and nine runtime negative controls.
Strict worker Release build passes with zero warnings and zero errors. Integration checks and
final build numbering are owned by the primary agent. Headset confirmation remains open.
