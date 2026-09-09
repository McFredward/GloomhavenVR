# MB487 native widgets and remote cap sizes

Both supplied `LogOutput.log` files report ModBuild 486 on line 17. The three supplied screenshots were inspected before assigning causes: `buttongrößen.jpg` shows unequal remote cap sizes; `bonussymbole_lokal.jpg` shows the shield-1 preview above the class icon, whereas `bonussymbole_remote.jpg` contains serialized `Ability Card Name` tooltip text and a differently shaped mandatory damage outline.

## Remote cap sizes

The local and remote constructors use the same `BoardAnchors.FitCapSize` and `CardMesh.BuildBeveledKeycap` inputs. The defect is later: `InertCap.SetShown` treated the still-active object during dissolve as logical visibility. Any other button-mask edge reasserted hide, restarting `RemoteCapFx.PlayDissolve`, which saved the already reduced transform scale into `_shownScale`. That permanently reduced subsequent appear/cancel poses. Different cap histories therefore produced different settled sizes without any differing size dial.

`RemoteCapVisibility` now tracks requested visibility independently. Repeated hides do not restart a transition, and `Init` alone captures the authored scale. The rendering animator also refuses a duplicate dissolve directly. The new regression vectors exercise unrelated button edges during hide, reversal before deactivation, repeated show, and independent roles. Local wire run: 238598 assertions (238556 baseline +42 new). Release compilation: zero warnings and errors. Shared test registration remains the integrator's file ownership.

## Bonus template content

Source-proven cause under investigation: the inactive original slot prefab bypasses `UIUseActiveBonusTooltip.Init/Reset` and `OnPointerExit`, leaving the original serialized tooltip's example descendants visible. The existing stage initializes `previewEffect` but does not initialize or hide those tooltip descendants. The hardware log confirms the serialized original prefab path (`USE BAR WIDGETS`, local lines 37073, 37296 and later), rather than the retired handmade replica. Tooltip hover content and the mandatory outline still require the final implementation/checkpoint below.

No headset or two-client runtime was available in this lane. Automated checks establish source behavior, not final pixel parity.
