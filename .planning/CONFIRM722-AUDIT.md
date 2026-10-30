# Generic confirmation ownership audit — Build722 worker lane

Base: `d4a888357` (dev, ModBuild721). Hardware input is the maintainer's
2026-10-26 `LogOutput.log`, whose banner is **ModBuild719 / assembly1.2.1**.
No new remote log or screenshot was supplied for this level-up report. The
quoted card-list overlap is an independent user report, not a second observed
headset trace. The primary lane owns the level-up inventory/layout correction.

## Confirmed defect and correction

`WorldUI/Surfaces/DialogSurface.ConvertNow` always converted the scenario
`UIConfirmationBoxManager.Instance.CurrentBox` into a separate HMD-relative
panel. It never asked which original native screen requested the box. The
supplied log records that independent conversion at lines7377,7445 and8177.
The same routing necessarily affected other native screens using that manager.

`ConfirmationOnOwner.Origins.cs` now observes five original manager entrypoints:
generic, warning, cancel-only, spend and cancel-active-ability confirmations.
Sixteen exact synchronous native producer methods establish a prefix/finalizer
scope. The scope records the actual component and its native containing window
at the original request; it is not inferred from a later focused/open window or
translated text. Reuse of a pooled confirmation clears its previous request,
including when the next request has no known producer. Nested scopes restore
the previous producer even if the original call throws.

`ConfirmationOnOwner` looks for the nearest **live actual conversion containing
that producer**. It retires the exact old standalone conversion before recording
the restored native home. The same original root is seated in the producer's
native subview within that main panel, using its measured frame, native controls,
`TransferSeatedSubtree` and `MovedSubtreeLayers`. The receiving host's modal
raycaster stays usable; native disabled/interactable state remains authoritative.
Layers are walked only on initial adoption or real layer drift, not every tick.
Existing original rect/rotation/scale/layout/sibling state is restored on release.
A rejected Unity reparent retains its restoration record rather than silently
losing ownership. No native callback, Hide, Cancel, game state or wire operation
is dispatched by this presentation helper.

Both original managers are maintained: the scenario manager and the distinct
`UIConfirmationBoxManager.MainMenuInstance` used by ESC/menu prompts. Root owns
the pre-conversion/reset hooks and explicit patch registration. DialogSurface
also maintains the helper at its ordinary tick and yields its dedicated float
when the same original is successfully seated.

## Native producer inventory

| Original flow | Native evidence | Result |
|---|---|---|
| Level-up card choice | `UILevelUpWindow.ConfirmSelectCard`, lines132–152 | Exact producer scope; embed its original generic confirmation in the containing main screen. Native owner continues its own callbacks/Hide. |
| Free and paid level reset | `UIResetLevelUpWindow.ResetAbilities`, lines120–184 | Same ownership correction covers both generic and spend prompts. Native reset cost, joining-player guards and server-authored ResetCharacter action remain. |
| Perk prerequisite warning and enable confirmation | `UIPerksInventory.OnSelectedPerk`, lines250–300 | Both attach to the originating perks screen rather than a second HMD panel. Native prerequisite/point checks remain. |
| Duplicate equip/bind warning | `UIPartyItemInventoryDisplay.OnItemSelected`, lines397–411 | Attach the generic warning to the originating inventory screen. Actual item confirmation still follows existing `MapDialogSeat`. |
| Locked party member warning | `UIAdventurePartyAssemblyRoster.OnSelectedCharacter`, lines264–270 | Attach the warning to its originating roster. No locked-member permission change. |
| Epic account logout | `UIOptionsWindow.AskLogOut`, line338 | Exact options producer; attach when an actual compatible containing world panel exists. |
| Exit to main menu / quit game | `ESCMenu.LoadMainMenu` and `ExitGame`, lines389–469 | Exact ESC producer; both native generic confirmations retain their existing destructive-action callbacks and input guards. |
| Disable map tutorial | `UIMapEscMenu.TurnOffTutorial`, lines199–206 | Exact ESC map producer; native tutorial completion is untouched. |
| End multiplayer session | `UIMultiplayerEscSubmenu.ShowConfirmationBoxToEndSession`, lines506–511 | Exact submenu producer; original online/startup gates and end/cancel callbacks remain. |
| Quit dungeon / restart scenario / restart round / restart turn / skip mission / skip tutorial | Six methods in `UIScenarioEscMenu`, lines405–528 | Exact originating ESC producer scopes. No change to action validation, native navigation, authoritative network commands or softlock report choices. |

The table covers **16 producer methods**; a row may contain several related
native prompts. A source scope is not a promise that arbitrary scene lifetimes
can be merged: see the safety boundary below.

## Other confirmation families reviewed

| Family | Existing owner / behavior | Decision |
|---|---|---|
| Merchant Buy/Sell and party inventory Bind/Equip | `UIItemConfirmationBox.ShowConfirmation`; `MapDialogSeat` records `BoxConfirmationType` and seats Buy/Sell in the merchant; General retains its native party home. Singleton replacement is already observed. | Keep existing semantic seating; do not add a competing owner. |
| Enchantress / temple confirmation | `UIEnhancementConfirmationBox.ShowConfirmation`; `MapDialogSeat` captures its native raising mode at Show and preserves native closure, while immersive `TownServicePresentation` owns the same original in palm presentation. | Keep existing reversible seating and immersive ownership, which already address this family. |
| Delete character | `UICampaignAdventurePartyAssemblyWindow.ConfirmDeleteCharacter` serializes `UIAdventureCharacterConfirmationBox`; its confirm callback runs after the original hide transition. | Actual native descendants already obey `RendersInsideFloatedAncestor`. No evidence here proves an incorrectly separated sibling; do not invent another delete confirmation or run its callback from presentation. |
| Multiplayer assign/kick/report | `UIMultiplayerSelectPlayerScreen` and `UIMultiplayerMerchantSlot` explicitly reference `UIMultiplayerConfirmationBox`. Native action buttons/callbacks and navigation govern their flow. | Same actual-ancestor rule. Serialized references alone do not establish world hierarchy or authorize moving an unrelated online action. |
| Character creation completion | `UICharacterCreatorConfirmationBox.Show` is a sprite-load/animation completion screen, not a Yes/No choice; it temporarily owns native input and resolves after its original hide. | Keep the native completion flow and ancestor handling; never fabricate a confirmation or skip the animation callback. |
| Retirement | `UIRetirementManager` sequences completed personal quest, native ready-up/story, `UIRetireCharacterConfirmationBox` and rewards; online observers have different native authority. | Keep native sequencing and actual-ancestor behavior. The global retirement warning raised by `UIMapMultiplayerController` has no proven local screen producer and retains standalone continuation. |
| Sign-in / sign-out / disconnect / platform error | `UIMultiplayerSignInConfirmationBox`, `SignOutConfirmationBox`, `PlatformNetworking`, join-session disconnect callbacks | Global/lifecycle dialogs can appear with no surviving screen. Retain their native independent continuation; never attach to an arbitrary menu merely because it is open. |
| Map tutorial switched to multiplayer | `MapFTUEManager.OnSwitchedToMultiplayer` first finishes the tutorial and then raises an informational confirmation. | The tutorial producer is already departing; retain independent original notification. |
| Main-menu save deletion / DLC warning / modding/ruleset/export validation | `GLOOM.MainMenu.UILoadGameWindow`, `UICreateGameDLCStep`, `MainOptionExtras`, `UIListModsView`, `UIRulesetView`, `UIRulesetsWindow`, `UICreateModView`, `UIExportModdingManager`, `ModdingCampaign`, `ModdingGuildmaster`, `LevelEditorMainMenu` | These native menu/editor flows remain inside their existing captured flat menu, outside the in-table conversion gate. Do not create VR world windows or change asynchronous validation/controller lifetimes. |
| Cancel persistent ability / forbidden cancellation | `PersistentAbilitiesUI.OnCancelActiveAbility` overloads, native `ShowCancelActiveAbility` | Scenario board/physical berth operations do not establish a live floated window producer. Preserve existing original native confirmation/berth authority rather than attaching to an unrelated screen. |

## Lifetime and multiplayer limits

- A real converted panel must exist. Without one, the original dedicated/generic
  fallback remains available; an unconverted native window never suppresses the
  player's only continuation surface.
- A persistent singleton must not be reparented into a shorter-lived scene.
  Different scene identities are refused and retain the safe standalone fallback.
  Current logs do not identify the original confirmation's scene, so they cannot
  prove that every ESC variant seats on real hardware. The actual level-up asset
  scene and visible geometry still need headset/native validation by the integrator.
- Owner `IsOpen=false` is not a cancellation permission: native navigation may
  deliberately dim/hide its parent while a prompt is up. This helper does not
  cancel or confirm on that flag. `UILevelUpWindow.OnHidden` and
  `UIResetLevelUpWindow.Hide` retain their own native pending-dialog cleanup.
- Conversion disablement, desktop rescue, destroyed producers and shutdown
  restore presentation only. New callbacks or transaction/network commands are
  never synthesized. Independent managers keep separate seats and pooled requests.
- No wire record, shared-window pose rule, actor ownership, private card rule,
  host/client input authority or unmodded peer behavior is changed. Existing
  multiplayer UI/actions remain native; this lane changes their local conversion
  attachment, not their decisions or replication.

## Focused verification

The expanded existing `dialog-surface` fixture compiles the production
`DialogSurface`, `ConfirmationOnOwner`, producer-origin patches and
`MovedSubtreeLayers`. The boundary fake models native hierarchy, rects, layers,
separate managers and conversion registration; it does **not** claim to render
actual native artwork or prove headset readability.

- **153 assertions**, including exact producer reuse, native-parent dimming,
  reentrant pooled ownership replacement, both managers, restore/re-enable,
  late standalone handoff, scene refusal, rejected reparent retention and modal
  raycaster routing.
- **10 causal negative controls**: six existing dedicated-dialog regressions;
  new double capture, stale producer, lost native home and collapsed frame.
- Strict worker Release build: **0 warnings / 0 errors**.
- Initial development failures were a nullable local/fixture boundary mismatch,
  a missing shell dotnet path and a mutation that originally failed at a different
  boundary (duplicate weak-table key) rather than its intended assertion; all were
  corrected before the passing final run. No failed run is reported as passing.

No complete runtime-suite invocation or new headset/multiplayer visual pass is
claimed by this worker. Root performs integrated checks, patch registration and
Build722 publication. Owned temporary fixture projects are removed after reaping;
logs/source/results are retained for integration.
