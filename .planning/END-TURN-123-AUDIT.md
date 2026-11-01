# Reported multiplayer End Turn stall after 1.2.3

2026-11-01. Source audit, not a reproduced hardware failure.

## Finding and limits

A four-player party reports repeated stalls after pressing End Turn while resuming
a previously started scenario. The old initiative entry stays yellow and nobody
can continue. Their subsequent Main Menu / Exit confirmations appear far away.
No logs, save, scenario/character/action identifiers or images accompany this
report. Do not treat the independently identified confirmation-placement defect
as proof of the cause of the turn stall.

The direct turn path has no identified regression between 1.2.1 and 1.2.3. The
symptom fits the native end-turn synchronization barrier, but does not distinguish
a peer's local animation/queue blocker from a missing peer acknowledgment, an
earlier end-of-ability barrier or a pending native choice. No speculative turn
advance, synthetic acknowledgment, timeout skip or rule-engine patch is justified.

## Compared versions and immutable source evidence

| Version | Commit | ModBuild |
|---|---|---|
| 1.2.1 | `7b173b376f2ab45c997a109cd67cfd6b3a10b5ff` | 721 |
| 1.2.2 | `7f6888707e541b487a8aa62cabb033df4fc6c0ec` | 751 |
| 1.2.3 | `b87477f8ae6247e524efc6dc128c09a25a6ae55d` | 753 |
| Audited dev | `5e0f4682b5a6bb64a1d073d5950e886638c408ba` | 753 |

The 1.2.2 -> 1.2.3 production diff contains only the project version, ModBuild
comments/stamp, and `MenuDepthCharacterProjection.cs` creator-page framing.
The broader 1.2.1 -> 1.2.3 interval includes the new menu rendering, confirmation
ownership and shared-window work, so those indirect paths were also reviewed.

An ignored `end-turn123/source-audit.json` records all four revisions and SHA256
for 97 unchanged production files: the entire Board, Cards/Driver and Cards/Tray
subtrees, CardsGameApi, FfsNetTransport, EnemyInfoContinue, AssignmentChoice,
TrayControlDockSurface, DecisionDockSurface, InitiativeReorderSlide and
InitiativePortraitPin. All 97 are byte-identical at all four revisions. This is
source identity evidence, not a claim that all dependencies or rendering paths
are unchanged.

The exact currently installed reference assemblies were read, never modified:

- `ressources/GH_Data/Managed/GH.Runtime.dll`:
  `fcfcd2e420299139b6cba9a5fedb52c9cae60bb5e1292adb84152c14d5e8f495`
- `ressources/GH_Data/Managed/ScenarioRuleLibrary.dll`:
  `79f3245261e0c78734536989dfbfc73443deeac50ecab6922c59ec47d25cc4c3`

Fresh ILSpy output for ReadyButton, Choreographer and CPhaseEndTurn agrees
byte-for-byte with the previously decompiled sources after removing only ILSpy's
two-line update notice. The original fresh output, normalized bodies, native
source hashes and comparison manifest remain in the ignored evidence directory.
The manifest SHA256 is
`e0abfeccb3b8366aa613e24db1ad994a741f49519e0b8e325b826174ee3b9f6d`.

## Native turn lifecycle

1. The board's unchanged `CardsGameApi.ClickReady` checks native permission and
   calls the original `ReadyButton.OnClickInternal`, including online
   `ConfirmAction` dispatch. It does not call the rule engine's next phase itself.
   Native ReadyButton refuses a currently requested generic confirmation.
2. The original ReadyButton consumes any queued alternative action. Otherwise it
   calls `StepComplete` for Continue states, or `Choreographer.Pass` for End Turn.
   Its native visual callback releases the UI lock before the delayed action.
   These methods and the board callers have not changed in this release interval.
3. Native `CPhaseEndTurn.OnNextStep` emits exactly one EndTurn message. The native
   `Choreographer` handler hides action UI and enters `WaitingForEndTurnSync`.
   The next actor does not begin immediately when End Turn is pressed.
4. That wait requires all original local work to finish: active bonus particles,
   unlocking doors, delayed loot drops, both message queues, all relevant actor
   idle states/FlowControl and level-event processing.
5. Once locally ready, a client sends `EndOfTurnClientReady`. The host adds its
   own readiness and waits for every participant in `m_WaitForEndOfTurnList`.
   The native host coroutine has no online timeout. One peer which never reaches
   local readiness, or an acknowledgment which never reaches the host, holds the
   entire party. `EndOfTurnAllPlayersReady` then releases the original phase.

Native source landmarks in GH.Runtime/Choreographer.cs: EndTurn handler
9366-9415, local readiness predicates 2760-2870, local/host acknowledgment
14192-14260. GH.Runtime/ReadyButton.cs:245-378 owns permission, dispatch,
alternative action and delayed callback. ScenarioRuleLibrary/CPhaseEndTurn.cs
owns the original one-message phase and synchronization continuation.

The unchanged native initiative selection is presentation, not sufficient evidence
of a specific wait state. A yellow square alone cannot identify which of these
barriers stopped advancing; the original next-turn actor selection has not yet
replaced the old selection at either synchronization barrier.

## Investigated alternatives and discrimination

| Candidate | Source support | What would distinguish it |
|---|---|---|
| A participant is locally blocked before end-turn ACK | Native barrier explicitly waits for effects, actor idle/FlowControl, queues and events. Fits party-wide stall. | That peer's native `[END TURN SYNC]` blocker report; host may be waiting for its ACK. |
| Host waits for a missing participant ACK | Native host coroutine waits indefinitely online after local readiness. Fits no further available buttons. | All peers' local-ready lines compared with host acknowledgment lines and participant departure/rejoin history. |
| Earlier end-of-ability animation/ACK barrier | `WaitingForEndAbilityAnimSync` is separate and can leave the current initiative selected. | Choreographer wait state, last message and end-ability acknowledgment lines. |
| A mandatory damage, infusion, item or active-bonus choice is not visible | Native end-turn effects can involve action choices before the final EndTurn phase. | Actual phase/last message, shown/requested choice and expected callback. No direct generic confirmation is raised by Pass, CPhaseEndTurn or CPhaseEndTurnLoot. |
| New generic confirmation ownership interrupted the turn | New persistent-ability producers are relevant indirect scope, but are invoked for explicit cancel/short-rest actions. The global tween prefix only clears a matching mod request flag. | A pending request and exact producer/callback at the failure; no causal source sequence currently identified. |
| Initiative surface rebinding stopped the native turn | Changed surface owns exact singleton, input and reversible row cosmetics; original slide/pin implementations are unchanged. | Actual singleton replacement/exception evidence. No new gameplay callback or native ACK manipulation found. |
| Mod transport swallowed native end-turn ACK | Exact FfsNetTransport and control-request dispatch sources are unchanged; the prefix passes ordinary native action types through. | Native side-action diagnostics, if any, plus all peers' logs. Source diff alone does not rule out external packet/peer failures. |

The existing FigureStallWatchdog addresses a separately proven held-mini/attack
flow failure in two earlier native waits. Its implementation is unchanged; it is
not evidence that this newly reported end-turn stall has that cause, and should
not be broadened to force the current barrier open.

## Evidence to retain if it happens again

Normal logs already contain useful original diagnostics; Debug is not required
for the native end-turn blocker. After roughly 30 seconds the native local wait
reports `[END TURN SYNC] Stuck at WaitingForEndTurnSync` and names the outstanding
conditions. A peer already locally ready can instead be waiting at the host ACK
barrier and produce no local-blocker report. Keep host **and every participating
peer's** Player.log and BepInEx log before relaunching. Compare the same turn's
local-ready, host-ACK and next-turn lines; a host log by itself may only show the
missing peer's symptom.

Keep the relevant pre-failure scenario save and identify scenario, characters,
last action/end-turn effects, owner of the pressed End Turn, and any recent
disconnect. A user-safe reproduction would replay that save with the same party
and actions, then compare tagged builds with the same inputs. None of those
hardware replays has been performed here.

The far-away exit confirmation is tracked by the independent confirmation owner
review. Making that original modal usable allows normal recovery without
pretending to repair or bypass an unknown gameplay synchronization failure.

## Validation record

Class A, investigation/developer content only. No production or test sources
changed and no runtime suite/build claim is made. Checked 97 source identities,
13 native source hashes, two actual reference assembly hashes and three fresh
decompilations; reviewed the affected lifecycle/patch callers and final document.
No generated Unity project, build cache or live test process was created.
Hardware failure remains unverified and the turn issue remains unexplained.
