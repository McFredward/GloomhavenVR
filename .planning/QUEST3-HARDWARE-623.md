# Quest B623 — complete Campaign candidate

## Delivery status

The complete Campaign Player is currently building. There is no validated B623
APK or Windows archive at this checkpoint. Replace this paragraph with the actual
signed APK, adjacent content-bank and Windows archive identities only after all
native build/delivery gates complete. The runtime source remains frozen at
`fa6c1f9a`; separately recorded Editor/host corrections do not change that source.

## Intended scope and installation

This candidate includes the original complete content closure and Campaign
runtime, base game, selected purchased Jaws of the Lion and Solo Scenarios,
current VR mod assets, local native saves, and the original session-code network
route. Guildmaster and Workshop remain unavailable with explanatory tooltips;
their native save/join admission paths are guarded as well. Local identity in this
private build is explicitly a dummy. No provider cloud/profile/friends services
are enabled. This scope describes implementation, not verified headset behavior.

The Windows archive contains an APK and its adjacent complete content ZIP. Extract
the whole archive and run `scripts/install-quest-wireless.cmd`; retain both files together. The
installer provisions local Python and Android platform-tools, uses the existing
wireless ADB settings, transfers the declared content bank and installs the
matched APK without clearing app data. A complete-content first installation
transfers substantially more data than the previous menu-only test. Preserve
existing save backups. `scripts/collect-quest-logs.cmd` records the actual installed
identity, logs and runtime diagnostics; use it after each hardware run.

## First Campaign hardware run

1. Install the complete current archive. Confirm B623 and the package's installed
   hash/content identity. Observe first launch and then a warm launch without
   clearing app data. Record Intro picture/sound, ambient menu imagery, aliasing,
   repeated hitches and whether native Intro-to-menu flow completes.
2. Create a separate Campaign test save. Complete character selection/loadout,
   enter Scenario1, inspect the complete room/terrain, animated textured figures,
   cards and original windows. Confirm VR movement/height, laser/fingertip input,
   keyboard text and mixed-reality passthrough.
3. Perform a complete turn: ability selection, initiative, movement, attack,
   damage, enemy behavior and round continuation. Open another room and check
   procedural environment construction and original effects. Continue through
   scenario completion or a controlled retry; verify the return to Campaign.
4. Save through the original game, quit and relaunch. Verify campaign state,
   characters, equipment, progression and current scenario resume. Collect before
   and after logs. Test save export/import separately on disposable saves.
5. Check the purchased DLC menu/access paths, unavailable buy buttons with the
   PC-buy/rebuild explanation, and Guildmaster/Workshop availability hints. An
   imported Guildmaster save or Guildmaster room must also remain rejected.
6. After solo flow works, test session-code joining in both directions with a
   matching PC build, once with its VR mod and once without it. Exercise room
   admission, Campaign/save exchange, a full action/round, reconnect and two-way
   voice. Record both hosts' logs and build banners.

Do not attribute a headset success to Editor/compiler proofs. The previous
hardware-confirmed audio, keyboard and Quest options behavior is the baseline;
full Campaign execution, Android native procedural bridge, final pictures,
frame times, save round trips, server admission and voice still require the
integrated headset test. Report the first failing transition with a capture;
there is no need to retest unrelated desktop/NPC suites for this port.
