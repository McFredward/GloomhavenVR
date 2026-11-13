# Quest B623 — complete Campaign candidate

## Delivery status

The complete signed Campaign Player and Windows hardware archive were built on
2026-10-06. The actual Player, native Shader closure and delivered Compute gates
pass. The independent whole-artifact audit also passes; headset execution is
not yet verified. Runtime source remains frozen at `fa6c1f9a`; the captured host
and Editor corrections come from `796ea528d` and its recorded overrides.

| Artifact | Bytes | SHA-256 |
| --- | ---: | --- |
| `GloomhavenVR-Quest-B623.apk` | 2,494,100,526 | `ef954d63eb7f12eb284205817ac170c0c7559092ca18ee4f297100be5d6268f9` |
| `GloomhavenVR-Quest-content.zip` | 10,912,754,791 | `f6be86714e77536d38d00b3701ae5f64f720e357305deed22966ac9419eb668c` |
| `GloomhavenVR-Quest-B623-Windows.zip` | 13,408,142,006 | `e855b11c43c574e304a0f8c4a0dbf0a61c5be5bab533e061aad48ecba3b32201` |

The accepted build key is `1ac08393c2401fc0ff0e85806ecd6eb313c42e55563e86abf7ba8e5181f00186`.
All14 Player scenes are present, including the13 originals and Quest bootstrap.
Actual Vulkan delivery retains688 original Shader roots and51,564 original
aliases; the completed Player has zero native Shader compiler errors. All13
original ComputeShaders/36 Vulkan kernels pass actual executable-byte validation.
Native Build ID `d23d2717d3cae4f7` matches the signed APK and retained debug library;
1,150 actual native-source/managed-backup files are recorded for crash analysis.

The independent audit reads8,968 native payloads and confirms all6,531 original
public catalog roots,6,431 runtime aliases,601 procedural definitions,8 movie
bindings and19 external movies. It checks all688 actual Shader identities and
51,564 aliases in both Player and Addressables, both native variant collections,
and all2,316 public original Material roots. Windows ZIP64, all CRCs and the
packaged APK/content/50 installer-source hashes pass. This does not claim pixel
parity, an audit of every private Material object, or hardware gameplay success.

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

The declared expanded content is11,169,001,254 bytes. Allow at least30 GB free for
the first archive/extraction/install cycle; first-launch content preparation and
warm-launch timing must be measured separately. This candidate enables Jaws of
the Lion and Solo Scenarios from the maintainer's declaration. Alternative skins
remain unowned, and local identity is `Quest Local Test (DUMMY)`, Steam ID `0`.

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
