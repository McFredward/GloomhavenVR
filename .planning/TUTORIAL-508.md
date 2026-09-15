# Tutorial layout and preparation hint — build 508

## Hardware evidence

Local LogOutput.log:17 and Player.log:40 identify ModBuild 507, assembly 1.0.1.0.
The remote logs are still build 500 and cannot establish current multiplayer behavior.
All three supplied screenshots were inspected: `ausserhalb_der_box.jpg`,
`falscher_hinweis.jpg` and `spawnposition.jpg`. The user confirms the deadlock is gone;
Player.log:7155 completes BuyItem and line 7394 records Finished FTUE.

### Small, disjoint HelpText

The first screenshot has a short empty frame left of the text. LogOutput.log:1676
and 1720 report hint scales 0.452 and 0.509. The native hierarchy measured at line
2383 contains BG (820x43) and HelpText (800x23) as siblings under LabelArea. Build
507 resized LabelArea as though it were the frame and retained the children's old
anchors and offsets. The original frame stayed wide and the union still scaled down.

The correction seats the original sibling frame and TMP together, wraps using native
font metrics and preserves the original padding/depth. It saves/restores both rects'
anchors, pivots, offsets and sizes, as well as the affected layout writers. It does
not create replacement widgets, change localized text or affect illustrated messages.

### Preparation instruction during story

LogOutput.log:2766 assigns the message to QuestManager's serialized questIntroduction
producer, anchored to Quest Log Manager. Lines 2919 and 2957-2958 show the story hiding
the party UI and the introduction losing its owner. The screenshot asks to inspect
cards and inventory, which are not accessible in this VR story phase. This is the
quest-selection introduction, not the battle-goal explanation.

The later battle-goal picker already starts its own introduction (LogOutput.log:4095,
4174; UIBattleGoalPickerWindow.Display -> ShowIntroduction). The user explicitly
permits omitting the earlier instruction if rearranging it causes difficulties. We
use this specific exception rather than moving a second interactive introduction
into the same serialized queue at battle-goal selection. The native battle-goal hint
remains on the character UI.

QuestPreparationHint suppresses only QuestManager.questIntroduction, by exact live
reference and only with VR running. It intercepts UIIntroduceBase.Show before a
process promise or message exists and honors any passed continuation once. The
native caller passes no callback and still marks its own introduction as done. No
save flag, tutorial completion, map lock or travel permission is written by the mod.
The existing provenance prefix runs before this skipping prefix, so its finalizer
can restore the enclosing producer scope. Flat mode, other producers and the native
battle-goal queue retain their normal path.

### Party corner placement

The initial sidebar placement at LogOutput.log:1470 follows its corner seat (22mm
correction). After battle-goal hint adoption at line 4174, corner re-seating at line
4246 measures a polluted union approximately x=-2056..-102 instead of native party
x=-982..-88, moving the host by 1.072m. Fit already excludes adopted hints, but the
corner placement path consumed the broader union intended for interaction.

Corner placement and handover now exclude adopted hints from their content bounds.
The regular union still includes the hint and its controls for hit testing/chrome;
measuring a hint itself remains valid. This separates owner positioning from an
attached annotation without hiding content or changing manual placement.

## Validation

The production HelpText helper is exercised with sibling BG/text geometry, different
parent dimensions and anchor/pivot rect arithmetic. The placement harness exercises
both the actual ink walker and drawn-content union. The exact-producer prefix suite
checks callback preservation, unrelated/battle-goal hints, scene identity and flat
mode. Each suite includes deliberately reintroduced defects that must fail.

Final gate counts and compiled review will be recorded after integration.

## Hardware acceptance

With all VR peers on build 508, replay first character creation and check that the
initial hint is readable and fully inside its frame. Advance through each character
hint and close/reopen the UI to verify native layout restoration. Confirm the party
UI remains at the same left table corner before/after tutorial adoption and when
battle goals appear; manually moved windows must retain their user position.

Select the first quest: no cards/inventory instruction should overlap the story.
After story completion, the original battle-goal instruction must appear inside the
character UI, remain dismissible, and allow goal selection and quest start. Test
both normal continuation and video skip. Automated checks cannot confirm the final
headset picture or the complete native tutorial state machine.

Version remains 1.0.1; build 508 is DLL-only relative to bundle build 483. No wire,
main, tag or release change is included.
