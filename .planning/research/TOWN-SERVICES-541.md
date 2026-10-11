# Town-service follow-up — build 541

## Hardware evidence

The current `.planning/debug/LogOutput.log` and `Player.log` identify ModBuild 540,
assembly 1.0.7.0. The remote log is build 500 and does not represent this test.
No current town-service screenshot was supplied.

At LogOutput lines 1546–1551 and 1763–1768, the merchant converts TownService.20,
.21 and .22 (Buy, Sell and All), then throws before .23. Lines 1576 and 1793 record
`TOWN SERVICE FALLBACK`, with the catalog constructor/rethrow and BuildMerchant on the
stack. The ordinary window is deliberate recovery from failed setup, not evidence that
the maintainer disabled the option. Native voice-chat teardown throws separately at
Player.log:7133; Hydra DNS failures also occur independently of the catalog stack.

## Cause and correction

The next constructor expression dereferenced `inventory._ownedFilter.transform`.
[Original serialized scene inspection](TOWN-541-NATIVE.md) confirms that both campaign
and Guildmaster desktop inventories have a null Owned pointer; both gamepad variants
have a valid original. All other constructor control/tooltip references resolve correctly.
Owned belongs to the game's controller-oriented merchant. Native mouse-input paths
exclude it in Awake and filter enumeration. Build 540 unconditionally required it,
although template registration already tolerated absent controls. The constructor now
moves Owned only if the original exists. It neither fabricates that filter nor changes
native selection or purchase permissions. Other original controls retain their existing
handoff and restoration. Publication uses those same actual controls and stable template
keys, so omitting a nonexistent filter does not synthesize a remote-only widget.

The old catalog fixture populated every control and could not reproduce the hardware
case. It now opens repeatedly with both present and absent Owned filters, exercises
paging, native selection, details and restoration, and checks the counter control
set used by publication. A compiled negative control restores the old unconditional access and must fail.
Game-controller state remains an explicit fixture; this does not execute the whole game.

The previous option was a checkbox in Boards → Boards & displays, below wrist-related
settings. No missing-curated-entry warning or source dependency hid its binding. A
separate first section now makes town services identifiable and explicitly names both
presentation modes. The persisted `WorldUI/ImmersiveTownServices` bool and its true default
remain unchanged; choosing ordinary windows still uses the existing reversible handoff.

## Validation and hardware follow-up

Focused catalog validation: 285 Unity assertions / 16 compiled negative controls.
Window/session interaction: 876 Unity assertions / 28 compiled negative controls.
Menu path and selection: 463 assertions / seven compiled negative controls, binding the
real curated tree, visibility filters, row dispatch and dropdown callbacks. Native UI
pixels and BepInEx persistence remain explicit menu-fixture boundaries. The menu suite
now runs in the local full gate and hosted dev CI.

The exact German menu path is **VR-Optionen → Tafeln → Händler, Tempel & Verzauberin →
Stadtbesuch-Modus**, with **Immersive NPCs** and **Originale Fenster** as the two values.

Final strict Release: zero errors and zero warnings. The complete local guard passes all
functional/source checks and 255,887 wire assertions. Its exit 1 is the expected historical
compiled difference against `080c505e9`: 0 moved, 104 changed, 95 added/removed. Surfaces
remain 626 config keys, 174 Harmony patches and 4,746 log tokens, with none removed.
Documentation parity (four language pairs), shell syntax and whitespace checks pass.
Both bundles are unchanged from build 540. This is an unreleased development correction.

- Open VR options and locate the dedicated town-service presentation choice under Boards.
- Open the merchant with immersive mode enabled: NPC, counter and original item cards
  should appear without `TOWN SERVICE FALLBACK`.
- Switch to original windows while open, then back; close/reopen and repeat. Current
  character, native prices and confirmations must remain valid.
- Check buying/selling, category changes, page navigation and remote observer presentation.

Successful headset output is not established by automated checks.

## Development package

`dist/GloomhavenVR-1.0.7.zip` contains ModBuild 541 from production source commit
`cd74a5ecb7232395e9ae353d387f0de5f4913804`. BuildInfo marks it as a dev build.
Archive CRC, required layout and Windows text-encoding checks pass. Both bundled asset
files are byte-identical to the committed build-540 bundles. Archive size: 181,133,886 bytes;
SHA256: `95afc91b49442c391320e3e6f46a8438e1cd47836583e27758414de5a74c05f6`.
The complete manifest and validation logs are retained in `.planning/debug/town-build-541/`.
Later documentation-only commits do not change the packaged production source.
