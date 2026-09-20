# VR options toggle and native menu focus — build 537

## Hardware evidence

The maintainer's latest capture is build 536 / assembly 1.0.6.0. Local
`.planning/debug/LogOutput.log` contains 3,179 lines and `Player.log` 8,617 lines.
The remote capture remains historical build 500 and cannot verify this change.
No new screenshot was supplied for the menu report.

The previous repeat-opening failure is reported fixed. This capture instead shows
X closing the pane while the pause-menu row remains selected: LogOutput line 2339
reports the one-second closed-window latch repair (Player line 7116). Repeated X
closures are present; the menu was not suppressed by the old catch-all churn fuse.
Player line 8231 is native BoltVoiceChatService.OnDestroy during teardown, not a
menu-opening exception. Backend DNS failures also remain separate from this defect.

## Source cause and correction

Both injected row callbacks called the native host's SetFocused(false) when opening
VR settings. That API shades the other entries regardless of their usability. The
VR callbacks now leave native host focus and availability untouched. Existing
main-menu rival-window closure is retained; scenario/map windows remain independent.
Both menus use the same tested binding method, so their toggle behavior cannot drift.

Native UIWindow.Hide emits onHidden before updating its visual state. The original
submenu deactivates itself before forwarding OnHidden; the build-536 IsOpen guard
mistook that real close for a delayed notification. The corrected close handling is
documented in [CLOSE-537.md](CLOSE-537.md).

The row's close callback now uses native SetSelected(false), which updates both the
selection flag and ExtendedToggle value without firing a second close callback.
The native UIEventSyncExtensions.SetValue implementation temporarily suppresses
onValueChanged. A missed-close fallback reconciles both rows immediately, without
the former one-second grace. Its existing anomaly report remains once per session.
No diagnostic stream or normal-log volume increase was added.

## Verification

The existing production-bound options harness now executes the actual row binding,
close synchronization, fallback and main-menu rival closure in addition to the
build-536 retry/churn checks. It covers repeated X -> immediate reopen without a
Tick, normal two-press toggling, failed opens, native disabled/focused state
preservation, independent pause windows and main-menu arbitration. Negative controls
restore native focus shading, recursive close, delayed reconciliation and silently
unselected rivals. The independent close harness models native event ordering and
same-frame pending modal release.

Full integration validation is recorded below once complete. Hardware verification
of build 537 remains pending. Test main menu and map/scenario pause menu: toggle
open/closed repeatedly; close via X then immediately press VR Options once; verify
other usable entries retain their normal appearance and can be selected. The
hardware-confirmed short-rest correction is unchanged.
