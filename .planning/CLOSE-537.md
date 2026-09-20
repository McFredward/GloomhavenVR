# VR options close lifecycle — build 537

The maintainer's build-536 test reports an extra pause-menu click after closing the
VR settings with its X. Main logs show repeated X closes followed by a second close
of an already-hidden pane (for example lines 2496–2520). Main/pause focus shading is
handled separately in the integration change to `VRMenuEntry`.

## Source-proven close defect

`UIWindow.Hide(bool)` invokes `onHidden` before changing `m_CurrentVisualState` to
Hidden. The original `UISubmenuGOWindow.OnCompleteHidden` deactivates its GameObject,
then invokes the submenu `OnHidden` event. Consequently `VROptionsTab.NotifyHidden`
runs while native `IsOpen` still returns true. Build 536's `IsOpen` guard discards
that real close notification; it does not establish that this is a delayed event.

The guard now accepts the current pane's inactive close edge. It still rejects an
old clone and an active, reopened pane. The callback is removed before invocation,
so a duplicate close runs it once and a throwing consumer cannot prevent the
native event's remaining listeners or its state transition. The row callback in
`VRMenuEntry` uses silent deselection to avoid recursively calling Hide from inside
native Hide.

An additional source-proven rapid-toggle edge exists before the next modal release
tick: `UserClosing` still refers to the previous opening and the release loop's
gap-close would hide the explicitly reopened pane. `PrepareModMenuReopen` clears
that flag and the old pre-roll only for the live registered mod menu, before Show.
Ordinary native windows keep their close behavior. If release already started,
the existing `ResolveVanishForReopen` path restores the old host before conversion;
this change introduces no new animation cancellation or release ownership.

## Targeted verification

`scripts/vr-options-close-tests.sh` compiles six actual production methods plus an
assertion on the reopen-helper call order. Its native lifecycle fixture models
`UIWindow.Hide`'s callback-before-state order and the submenu's deactivation before
notification. It performs 256 X/toggle/native-Hide cycles, immediate reopening,
stale clone/event rejection, duplicate callbacks, exception isolation and pending
release/ordinary-window isolation. Result: 1,796 runtime assertions and six rejected
negative controls, including a mutation restoring build 536's failing guard.

This is source/runtime evidence. The new menu behavior still needs headset testing
in the main menu, campaign/Guildmaster map and scenario pause menu, including X then
immediate reopen and repeated toggle clicks. Short-rest playback is unchanged.
