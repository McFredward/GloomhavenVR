# Savegame tutorial continuation — build 507

## Evidence and cause

The supplied local LogOutput.log:17 and Player.log:40 identify build 506, assembly
1.0.1.0. The remote directory still contains build 500; it is not a second view of
this test. Both `kleiner_hinweis.jpg` and `nach_händler.jpg` were inspected.

- LogOutput.log:161 explicitly withholds the Movie click. The video's identity-only
  UIWindow deliberately has no running gameplay controller and never enters native
  Start/open state. UguiPointer incorrectly applied its pooled-native-window guard
  to this mod-owned live surface.
- The character screenshot shows a native single-line HelpText reduced into a narrow
  party column. Its desktop width drives uniform downscaling, making the text tiny.
- LogOutput.log:276 records an old converted host as a hint's home; line 291 reparents
  it again. Converted can lose its entry before the old PlayOut finishes. That pending
  release callback still owns the ActivePanels entry and can steal an adopted hint.
- The merchant did exit normally: LogOutput.log:470 delivers WorldMap from its X
  button; line 471 records Merchant -> WorldMap with LEFT=True. Calling ExitShop in
  addition would not address this evidence. Player.log:4959-4961 records Finish Step
  VisitMerchant, Start Step BuyItem and Show Step BuyItem, with no later completion.
  The screenshot still displays the instruction to click WorldMap.

The off-bar VR dispatcher called UIGuildmasterButton.Select/Deselect. Their native
UIEventSyncExtensions.SetValue temporarily replaces the toggle event list with an
empty event, then invokes only the button's own mode callback. The separate native
UIMapFTUECompleteStepToggleListener never receives the WorldMap selection. The view
changes, but BuyItem remains active, retaining its help and blocking tutorial progress
and subsequent quest/travel availability. This causal mechanism is source-proven;
full headset progression after the change is still unverified.

The active Adventure controls reported outside the panel fit are not independent
proof of a crop defect: the coverage logger also counts controls whose presentation
is intentionally withheld by native travel/tutorial locks. No unconditional travel
visibility, timeout completion or map-input unlock has been added. Native
InteractWithMap grants quest-list access; its QuestManager -> MapChoreographer
selection route advances the quest introduction, and the native FTUE Finish clears
the map lock. The physical map-icon path must not bypass that tutorial contract.

## Changes

The pointer guard admits only the exact live movie identity and image. Laser/poke
clicks use the native continuation: campaign Escape, or the hero decoder's original
EndReached callback. A raw decoder Stop is insufficient because it omits native
input restoration and reward continuation. Stale surfaces cannot skip newer clips;
remote cosmetic decoders cannot invoke owner gameplay callbacks.

HelpText reflows the original TMP and border using the authored font size, native
padding and TMP's preferred-height measurement. Original layout settings are restored
when the composite releases the message. Illustrated/page-based messages retain
their layout. Pending standalone dissolves are cancelled and released before owner
adoption, including the gap where only ActivePanels still owns the conversion.

The off-bar dispatcher now sets the original Toggle.isOn with notification. It
notifies deselected owned siblings first and emits one true event for the target,
including stale-on targets. Native mode and tutorial listeners both run. Ordinary
pointer dispatch, native input permissions and tutorial/travel lock ownership remain
with the game.

## Validation

All 17 source checkers and 253,674 wire assertions pass, together with existing
production suites. Movie ownership/pointer/skip coverage passes 66 assertions and
six negative controls; hint provenance/transfer/reflow passes 38 assertions and five
negative controls. The new production off-bar dispatcher harness passes 10 assertions
and two negative controls for silent selection and silent sibling deselection. It
runs in local wire checks and both CI workflows. These harnesses substitute Unity
APIs; they do not execute the entire native campaign tutorial.

The retained build-502 compiled comparison reports 30 changed types, 13 additions,
no removals. Relative to the reviewed build-506 set, UguiPointer and MapButtonRail
join the changed types and HintTextReflow is added; existing movie/hint/modal types
and build constants change as intended. Guard exit 1 reflects those reviewed compiled
differences, not a failed test. Surfaces are 625 config keys, 161 patch signatures,
4,723 log tokens, unchanged from 506.

Strict Release passes with zero warnings/errors. Bilingual documentation and whitespace
checks pass.

## Hardware acceptance

Replay the supplied savegame: click the introduction video with laser and poke;
check that playback stops and native progression/input resumes. Repeat with normal
movie completion, consecutive hero movies and a remote viewer. Check readable
German character hints, advancing pages and repeated owner-window transfers.

Open the merchant for the first time, return through the X and physical WorldMap
button in separate runs, and verify BuyItem finishes. Follow the native quest-list
instruction through the quest introduction and confirm that the quest start control
appears and works. Test single-player first, then multiplayer. Existing Info logs
establish the present causes; a fresh build-507 log is required if progression still
stops, with Debug enabled for that reproduction if possible.

Version remains 1.0.1; ModBuild 507 is DLL-only relative to build 483. All VR peers
need 507. This round updates dev only; no main/tag/release change.
