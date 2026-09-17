# Tutorial controller visibility and defeat retry — build 518

## Requested behavior and evidence

The maintainer reports a controller disappearing during the first tutorial's custom
VR controls lesson. Both controller models must stay visible throughout that lesson;
only the buttons relevant to the current task should be highlighted.

After defeat, Retry must return each participant and their control board to their
original scenario starting position. Moving, turning or zooming during the failed
attempt must not redefine that starting position. Every multiplayer participant must
take this path, including clients that respond through the native ready-up flow.

Available hardware logs still identify local release 1.0.3 / build 515 and historical
remote build 500. These are not current build-517 reproductions. Source findings and
automated checks below must not be read as headset verification.

## Integration review

Native defeat callbacks use either `UIManager.RestartScenarioFromInitial` or
`RegenerateAndRestartScenarioKeepGoldAndXP`. Multiplayer result windows deliver
the retry callback through `ShowReadyUpRetryToggles` on each client; changing only
the host's button handler would miss this route.

The existing rig stream samples the actual head, hands and control-board pose together.
Remote boards consume that owner pose; there is no reason to independently calculate
an observer's replacement seat or add a new network message for the restored position.

The tutorial no longer swaps controller models out for individual prose/card/fingertip
steps. Both models remain through the closing card; the existing availability logic
selects only the applicable hand/key highlights. Models and separate key markers receive
the VR layer recursively. This excludes authored layer-0 parts from scenery fading and
camera masks that do not include that layer. A missing model or replaced tracked hand
can recover during the running lesson, with bounded construction retries and no extra
allocations for an unchanged healthy pair.

Tutorial lane report: [TUTORIAL-CONTROLLERS-518.md](TUTORIAL-CONTROLLERS-518.md).
Its focused checks pass: 1,019 runtime assertions, four production bindings and nine
negative controls. The exact cause of the observed one-sided disappearance is not
established by the available logs.

Retry retains the original head position, yaw, rig scale and board world pose per local
scenario lifetime. A later automatic arrival correction carries the original board
baseline with it; it does not capture a board the player has since dragged. Physical
headset movement is compensated when restoring the rig. Saved zoom and moved peer
positions are not inputs to the retry placement.

Both native defeat restart endpoints arm restoration for every participant. A normal
round reload retains the baseline without asking for a defeat-retry teleport. Map/menu
destinations retire it; ordinary B+Y retains its existing behavior.

Board capture and restore require completed native loading, the current scenario owner
and an anchor under the current rig. Pending restoration survives an absent/new/held
board. The original world position, orientation and size are restored after adoption,
and pin bookkeeping is re-authored so later maintenance cannot undo the placement.
This also covers `SpawnLeftOfHead=false`, changed saved offsets and changed head pitch/roll.

The independent source review identified and closed outgoing-board admission, delayed
arrival correction and round-reload lifetime gaps. See [RETRY-START-518.md](RETRY-START-518.md)
for the implementation lane and focused regression coverage.

Retry focused checks pass: 433 runtime assertions, 17 production bindings and 15 negative
controls. The integration review also records both new SceneController patches in the
network-action safety ledger. The dynamic restart patch declares its receiver type so
the generated inventory and desync guard can see it. The test adapter now includes the
corresponding real Harmony attribute constructor; no runtime assertions were removed.

Final integration results are recorded after the complete guard and strict Release build.
Headset retest should include every tutorial step, left/right primary controller, a defeat
after moving/turning/zooming and dragging the board, repeated Retry, round reload followed
by defeat, pinned/follow mode and all connected participants.
