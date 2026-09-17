# Scenario retry arrival — build 518

## Evidence and cause

The report requests the original scenario starting location for the player and control board
on every defeat Retry, independently for all multiplayer participants. Available hardware logs
remain local build 515 and remote build 500; they do not verify this change in a headset.

Native `Choreographer` defeat callbacks use `UIManager.RestartScenarioFromInitial` for tutorials
and `UIManager.RegenerateAndRestartScenarioKeepGoldAndXP` otherwise. Both reach `SceneController`
presentation methods and a replacement scenario scene. Multiplayer ready-up invokes the same
callback separately on each participant; observing only the host's results-button handler would
miss non-hosts. No rules, network transport or authoritative gameplay state are patched.

Before this change, `BuildRig` cleared the remembered arrival angle, adopted the latest saved
pinch zoom, and solved a new ring against peers' current positions. Even an unchanged board
could therefore produce a different radius or table side on Retry. A plain B+Y recenter was
insufficient: it recomputes radius at current scale and the control board uses mutable layout
settings (including `TrayOffset` when `SpawnLeftOfHead` is disabled).

## Implementation

- Capture the actual initial head world position, horizontal facing, scale and arrival angle.
  Initial board-pending fallback is replaced by each actual automatic spawn-ring refinement.
  Physical headset translation/yaw at Retry are compensated using the original head pose.
- Preserve this local baseline across the explicit native defeat-retry callbacks. A new native
  `Choreographer` identity admits restoration, including direct Scenario-to-Scenario replacement.
  Late tiles or peer packets cannot trigger another seat solve after the restored pose.
- Ordinary round restart preserves the baseline without requesting a retry teleport. Its normal
  placement remains independent; a later defeat Retry still returns to the original level start.
- Map/menu/error scene destinations retire an interrupted retry and its baseline. Health/camera
  rebuilds of the same native scenario do not replace the captured original.
- Capture the board's actual world position, orientation and size. Later drags, layout settings,
  physical head pitch/roll and B+Y cannot rewrite it. Automatic ring refinement transforms the
  existing original board pose with the original head correction; it never samples a moved board.
- Restore the board after native loading and after `EnsureBuilt` binds it to the current native
  scenario and rig anchor. An outgoing, missing or held board cannot consume pending restoration.
  First placement and the regular board watchdog complete deferred restores. Following and
  pinned parents retain the original world size; pin origin/version and rig-local pose are
  reauthored before subsequent carry. The old arrival guard cannot re-solve the restored board.
- Existing manual B+Y behavior and presentation wire format remain unchanged. Actual head and
  board transforms continue through the existing per-player multiplayer pose stream.

## Validation

`scripts/retry-start-tests.sh` links the production lifetime, rig restoration and board
restoration implementations against narrow engine/scene stubs. Tests exercise four independent
participants, repeated retries, both defeat callbacks, preserve-only round restart, changed zoom,
physical headset offset/yaw, first and corrected arrival, moved boards, following/pinned parents,
late/recreated/held/outgoing boards, native loading and map/menu cancellation. Source bindings
verify integration into native callback registration, rig first-pose handling and tray adoption.
Negative controls remove lifetime guards, pose compensation, board restore/writes, native-owner
and loading admission, pin recache and original-board carry.

Focused checks pass: 433 runtime assertions, 17 production source bindings and 15 runtime
negative controls. Shell syntax, partial initialization order and whitespace pass. The strict Release
build passed with zero warnings/errors. Independent read-only review checked native callback
coverage, original-baseline lifetime and board ownership boundaries. Hardware validation remains
necessary: repeat a defeat Retry after moving/scaling the rig and board, with every VR client on
build 518; compare starting locations and verify a preceding round restart does not replace them.
