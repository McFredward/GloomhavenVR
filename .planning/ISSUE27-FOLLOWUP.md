# Issue27 follow-up and independent static-copy inertness repair

2026-11-04. Base `583c2282e7cb12a8fc12f4cba78311d3b5232ce4` (qualified756).
Class B: two existing held-info snapshot consumers; no gameplay/network authority
or shared mirror, renderer coordinator, rule-engine or acknowledgment changes.

## What the supplied evidence establishes

Canonical original inputs are retained under
`.planning/debug/github-issues-20261103/hardware/capture-220837Z/`.
The Player ZIP is read as a stream; its original is never extracted or modified.
Both logs identify released1.2.3/ModBuild753, commit
`b87477f8ae6247e524efc6dc128c09a25a6ae55d`.

- `LogOutput.log`, 25,013,189 bytes:
  `35f4ab7dd5a5a28230921a17b215fd06fa2b1866fd2ea20e601d9ce1c1e10f23`.
- `PlayerLog.zip`, 9,872,487 bytes:
  `bcbdff397a082d2145bdaf445cdd5dbb69e0435f6965c3f402d98c7b8b01c0cc`.

All52 native ReadyButton bodies have a real delayed-action successor within2–6
Player.log lines:19 `EPASSMESSAGE`,23 `ESTEPCOMPLETEMESSAGE`, nine
`ETILESELECTEDMESSAGE`, one `OnRecoverCardClick`. This checks the callback's
effect, not merely the pre-callback `STEP COMPLETE` message. RecoverCard's native
stack at65099–65121 includes `HideDelayed`, `ConsumableStateTimeline` and
`ButtonFXActive`. All15 EndTurn bodies reach Pass; none has
`EndAbilityAnimSync` as its recorded last-message state.

All83 end-ability synchronization entries, local-ready messages and all-ready
completions pair without a pending cycle. All36 end-turn and three end-round
cycles likewise complete. These successful cycles do not rule out a failure in
another run or an unclicked invisible choice.

The resumed scenario's final EndTurn#301 at94854 reaches all-ready95164,
EndRound95180 and native successful CampaignDefined_ScenarioEnd completion99221
(`ScenarioCompletionSuccess:True`, `UserQuitManually False`). The next scenario's
final EndTurn#817 at1122459 reaches all-ready1122714, end-round all-ready1122904,
Autosave and SelectAbilityCardsOrLongRest1123089/1123093. A later explicit
MainMenu confirmation1127594 precedes UserQuit1127892. Neither transition leaves
an observable prior-turn barrier pending.

Follow-up2026-11-04: an explicit MainMenu quit does not establish that the prior
gameplay was healthy. It can itself be recovery from an unusable choice. The
last unresolved required input in this recording is Round4 ability-card
selection or long rest: native phase SelectAbilityCardsOrLongRest at1123091,
PlayerToSelectAbilityCardsOrLongRest1123095, ActionProcessor ProcessFreely at
StartOfRound1123359–1123360. No later selection confirmation or gameplay phase
advance is recorded before the pause/MainMenu flow. The local Mindthief,
Sir Josh A Lot/player10, has ownership and an interactive fan reported at
1123150/1123157–1123158; short/long rest are both offered and neither selected
at1123159. These are model/permission observations, not proof of usable rendered
controls. The log does not identify one final pending player. Missing or blocked
selection/readiness remains a valid candidate despite all earlier ACK cycles
having completed. Normal voluntary quitting and quitting because that input was
unusable cannot be distinguished from the menu click alone.

The13.6-second video instead displays Round11; these logs show resumed Round14
followed by a new scenario's Rounds1–4. No EndTurn click or leave/control-handover
recovery is filmed. The captured video stall cannot securely be assigned to this
log run. The report remains valid evidence of a problem; its cause is unproven.

Native `Choreographer.OnControllableOwnershipChanged` transfers processor state
to the new controller, dismisses popups, cancels dialog choices and refreshes
action controls. Ownership handover can therefore restore choices independently
of an ACK barrier. Do not synthesize ACKs or force progression to mimic recovery.

## Clone audit and bounded repair

The1214 UIWindow-removal warnings do not alone prove a surviving live controller.
`PersistentAbilityPopup` and its base `SlotPopup` both declare
`RequireComponent(UIWindow)`, explaining the repeated dependency name. The
existing RemoteWidgetMirror strips under an inactive parent in three reverse
passes: this known Popup→Window chain clears on a subsequent pass. Its exact
warning caller is not logged. Eleven audited clone/caller files are identical
between tagged1.2.1 and1.2.3; this does not establish a new1.2.3 turn regression.

An independent source-proven hole exists in
`StatPanelSurface.StripLogicComponents`: its previous singleton-only pass left
ordinary native popup controllers alive; their required UIWindow could not be
removed, yet its removal was counted. The held prop snapshot uses the same
helper. Both copies could subsequently activate non-presentation controllers.
The method predates1.2.1. Neither consumer's snapshot-creation marker appears in
the supplied logs, so this repair is explicitly **not a demonstrated fix for27**.

Both consumers now require successful inertness admission before publishing a
copy. Only exact engine uGUI/TMP drawing and stock layout primitives remain;
current geometry, viewport/content offsets, sprites, materials and text stay.
Native/custom controllers, UIWindow, input/scroll event emitters and animation
drivers are removed while inactive. RequireComponent dependents go before their
dependencies; only actual successful removals count. Any residual unknown
component rejects and discards the still-inactive copy. Active input is rejected
without deactivation or component mutation.

A rejected figure source/actor is attempted once per native source and secondary
hold sequence. Its existing Reconcile path cannot requeue the same refusal each
frame. Prop refusal is retained for its source/window/side/content tuple until
explicit release/registry teardown or a changed source/content. No retry timer
or repeated anomaly stream is introduced. Both paths retain their live original.

## Validation scope and inheritance

`scripts/check-static-copy-inertness-runtime.py` executes the complete actual
BuildStaticCopy and SnapshotDisplacedCard admission methods and their shared
helper against real native ActorStatPanel, UITextInfoPanel,
PersistentAbilityPopup, UIWindow and Singleton types in actual Unity play mode.
The original native TMP font/SDF and declared visual hierarchy witnesses provide
final pixels and exact image/font/material/current-layout identity checks.
One destruction-endpoint refusal is injected to exercise residual rejection,
bounded reports, release/reacquire and source replacement for both consumers.
The legacy removal body is retained as a compiling causal control; it must fail
on the real surviving native Popup controller, never on compilation.

Native scene bootstrap, unchanged addressable portrait detachment, diagnostic
strings and final conversion are explicit fixture ports. This does not reproduce
the reporter's scenario, Photon session or headset appearance.

The existing figure-overlay-runtime extracts only the StatPreparation section of
StatPanelSurface. That exact section is unchanged from qualified756:
`32c85723fb6a20ac091a947fbd0cb4c2b105a091d69265ce38ee61986f861764`.
All remaining fixture/production/dependency input bindings must remain exact to
reuse its756 entry. The parent approved that reuse; this unrelated preparation
path is not rerun. Parent integration owns strict builds and all source suites.

Native reference SHA256 values remain:

- GH.Runtime.dll:
  `fcfcd2e420299139b6cba9a5fedb52c9cae60bb5e1292adb84152c14d5e8f495`.
- ScenarioRuleLibrary.dll:
  `79f3245261e0c78734536989dfbfc73443deeac50ecab6922c59ec47d25cc4c3`.

Focused real-Unity results:

| Run | Result | Recorded run lifetime |
|---|---|---|
| `run-r3g7jazu` | Fixture compilation failed: inherited XML-documentation warnings were errors. Explicitly suppress that fixture-only warning; no control accepted. | 3.50s |
| `run-eidczeda` | Legacy-body control correctly rejected a surviving original Popup; production and four controls failed final pixels because the fixture had made the source itself inactive. Black captures and failures retained. | 72.16s |
| `run-zdfp59rc` | **64 production assertions + four compiling controls pass** after modelling the shown source correctly: activeSelf=true under an inactive parent. | 23.43s |
| `run-w_uvhghk` | **Legacy-body control passes again** with the corrected source bootstrap, failing at the exact surviving original Popup assertion. | 15.81s |

Lifetimes above are recorded from run-file creation/completion metadata and
include setup/build/import, not headset frame timings. Only the missing groups
were repeated. The final two runs qualify all six cases; this is explicit
aggregate coverage, not a fresh single full invocation. The activation boundary
is pinned to actual `CanvasConversion.1.Core.cs`'s
`target.SetParent(hostRect, worldPositionStays: false)`: conversion does not
force a hidden target active. No production activation change was made. Source,
figure and prop final captures were also viewed; all64 checks include exact
pixels and authored geometry/identity preservation. The compiler, source and
native reference hashes, captured images, original failures and canonical
completion cleanup receipts remain with the proof. No hardware acceptance or
complete-gate rerun is implied by this bounded repair.
