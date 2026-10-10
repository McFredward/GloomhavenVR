# Build664 approved defaults review

Base: `dev ca190750a2a77b1a90e199e4d280f2e25645f44a` (Build663).
The maintainer approved the paired configuration recommendations on 2026-10-10,
overriding the proposed 15 FPS threshold with 10 FPS and PC MSAA4 with MSAA8.
The following messages approve the PC wrist position on both PC and Frame, even
while wrist attachment is disabled. No other submitted personal calibration is
copied into defaults.

## Inputs and decisions

The preserved inputs are `.planning/debug/default/pc_config` and `frame_config`:
19 mod configuration files and one BepInEx file per platform. The read-only
audit, source references and input hashes remain in
`.planning/debug/defaults-audit-20261010/`. Its original recommendations are
historical evidence; the explicit approvals above supersede its 15 FPS/MSAA4
and wrist-calibration recommendations.

| Setting | Fresh PC | Fresh Frame |
|---|---|---|
| MSAA | 8 | 0 |
| Automatic wall-hiding threshold | 10 FPS | 10 FPS |
| Scenario surroundings | SwampNight | OffBlack |
| Wall visibility evaluation interval | 0, effective 0.05 s | 0.25 s |
| Health bars occluded by walls | On | On |
| Forced texture-streaming disablement | On | Off |
| Wrist-local position in metres | (-0.166, 0.010, -0.036) | (-0.166, 0.010, -0.036) |

Wrist attachment remains Off and rotation remains (0, 0, 90) degrees. Both
platforms bind the position through the same `Defaults.WristBoardOffsetMeters`;
Frame has no separate calibration override. Previously saved values survive.
Shared interaction defaults, Info logging and hidden cheats remain unchanged.

Fresh mod settings match High-End PC on PC and Standalone on Frame. Explicit
profile actions now also reset sky, wall visibility/evaluation/rescan policy,
the legacy interval and independent pillar LOD. PC profiles retain the readable
texture policy instead of disabling its existing streaming guard. Bar occlusion
is a shared startup default; profiles preserve that separate player choice.
Profiles do not reset personal offsets, rotation, board pose or world scale.
The German and English UI-maintenance descriptions now both state the existing
0.15 s Frame default.

Native saved/cloud graphics quality remains authoritative at startup. Explicit
profile buttons still select the original Fastest/Simple/Good/Fantastic game
quality. This is mod-side profile alignment, not automatic native Fantastic
selection on every PC launch. Quest activation remains with its separate agent;
the shared Standalone action is available without adding Quest detection here.
The complete current policy is in [STEAM-FRAME-DEFAULTS.md](STEAM-FRAME-DEFAULTS.md).

## Validation and limits

This is focused validation of a bounded defaults/profile change, not a new
complete local gate. Unchanged NPC, rendering and transport behavior inherits
the recorded Build663 evidence.

- Graphics profiles: 328 assertions exercise the unchanged production action
  with source-derived fresh configuration bindings. The dummy config boundary
  does not establish on-disk BepInEx persistence or headset appearance. Final
  receipt: `defaults664-profiles-final/run-pdp3c0an/result.txt`.
- Wall options: all 16 production/causal runtime variants pass, including real
  BepInEx binding, saved reload, ranges and native menu paths. Receipt:
  `defaults664-tests/wall-options.log` and `wall-options/run-j65lt92m`.
- Actual Unity wrist/menu proof: 324 assertions and three causal controls pass
  with all three original board prefabs, authored Glove wrists, real sliders,
  Update/LateUpdate placement and original remote pose state. Receipt:
  `defaults664-wrist/run-j1uzdddz/results.json`. This validates the geometry path,
  not anatomical comfort for every player.
- All 16 source suites pass. Direct wire goldens pass 300,419 assertions. The
  direct golden executable is not the complete `scripts/wire-tests.sh` gate.
- Strict Debug and Release builds pass with zero warnings/errors. After the last
  German description correction, Release and the player-settings lookup were
  rerun; the latter passes 2,305 assertions across 670 bound keys and EN/DE help.
  Other unchanged passing checks were reused rather than repeated.
- Config keys, patch inventory and log tokens remain 666/235/4,795 with no
  additions/removals. No wire layout, bundle, input configuration or game data
  changed. The compiled review is retained in `defaults664-compiled/scope.json`.

Receipt paths above are relative to the main checkout's gitignored
`.planning/debug/`. The private Build663 compiled baseline was created in the
documentation worker's worktree without writing through shared guard symlinks.
The comparison explains defaults/profile and description changes separately
from ModBuild consumers and branch stamps: 1,252 retained types, nine explained
settings/description units, eight build-only consumers and one branch stamp;
no added or removed types. No headset FPS improvement is
claimed. Saved configurations retain their old values unless the player changes
them; updated fallback defaults are not a forced migration.
