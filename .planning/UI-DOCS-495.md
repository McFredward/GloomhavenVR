# ModBuild 495 — board tiles and player documentation

## User request (2026-09-10)

Select control boards with rendered tiles; improve all variant caption readability.
Consolidate controls/play navigation, show both dominant-hand mappings, remove install
and tutorial filler, and explain selection/action phases and character inspection briefly.

## Changes and review

- Cards/Board now enters the same picture-picker path as hands, masks and environment.
  Oak, Steel and Bronze retain enum identities, localized names, the existing Apply
  callback, per-variant settings rebuild and CardsConfig.Board.SettingChanged event.
  LocalRigSampler.LocalBoardStyle continues reading that same value for peers.
- Three 320x240 embedded thumbnails use tracked real board renders (227,988 bytes).
  The existing asset bundle is unchanged. Generator: unity/asset-preview/build_board_tiles.py.
- All tile captions have bright ivory/pale gold ink, 18-point preferred size and a
  12-point floor, an opaque backing and no overlap with the picture. Private TMP face
  tint is normalized; shared native materials are untouched. Authored foreground/background
  contrast is 15.65:1 selected and 16.53:1 unselected, not a headset measurement.
- README controls already linked to PLAYING.md; one guide link removes apparent duplication.
  EN/DE guide pairs show both main-controller layouts and the switch path. A/X pause/ping
  swap with dominance; default flight/turn sticks remain independently configured.
  Reeling uses the carrying hand's stick. Selection/action flow and inspection permissions
  are explicit. The release badge tracks the published release.
- The shared-window explanation includes the exact runtime badge and distinguishes
  local windows without that badge, visible only to their own player.
- Review corrected stale privacy prose and board-image captions for rest-button visibility,
  hidden selection initiative, immediate active-card inspection and actual destination piles.
- Removed the obsolete Cards/Board raw-enum-label exception from the options checker:
  eight existing entries remain; the new picker uses ControlBoards.DisplayName.

## Evidence and limits

Workers inspected rendered PNGs and verified deterministic regeneration. All linked local
player-document/image targets resolve; EN/DE localization checks pass. Strict Release has
zero errors and zero warnings. The integration guard result is recorded in STATE.md.

The change uses existing gameplay/config/synchronization paths. Headset readability, live
board switching and remote appearance after switching need hardware confirmation; neither
image inspection nor automated checks establish the final headset picture.
