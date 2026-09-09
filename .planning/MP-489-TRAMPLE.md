# MB489 investigation: Trample movement without attacks

## Result

Finding 10 is explained by the original game's **Disarm** condition on Brute (actor 4),
not by a lost VR target or an omitted movement callback. The game started Trample's merged
attack and refused it because the actor was disarmed. It also refused subsequent attacks
until the condition expired at the end of that actor's turn. No gameplay or input-routing
change is warranted by this evidence.

Both supplied `LogOutput.log` files identify **ModBuild 488 at line 17**. The detailed
chronology below comes from the respective `Player.log` files in the main checkout's
`.planning/debug/` and `.planning/debug/remote/`. Line numbers count LF separators, matching
`rg -n`; universal-newline readers can differ by two because the logs contain embedded CRs.

| Event | Peer Player.log | Host Player.log |
|---|---:|---:|
| Game adds negative token `Disarm` to `Brute(4)` | 218104 | 241463 |
| Combat log identifies Deep Terror (3)'s Attack 0 against Brute | 218109 | 241468 |
| Original `StartSecondMergedAbility` message | 256475 | 283152 |
| Original `PlayerIsDisarmed` messages for Trample | 256480, 256493 | 283157, 283170 |
| Combat log confirms Brute moved four hexes | 256775 | 283546 |
| Subsequent attack attempts produce `PlayerIsDisarmed` again | 263536, 268239 | 290226, 294609 |
| Game processes and removes `Disarm` from `Brute(4)` | 278563 | 306087 |

The Attack 0 still applied its condition. The token was present before Trample was selected;
it was not created by crossing those enemies. Both clients agree on its application,
blocked attacks, and removal. The reported ability to attack those enemies in the next
round is consistent with the recorded removal.

## Original entrypoints and the misleading action log

Read-only source references under `decompiled/`:

- `ScenarioRuleLibrary/ScenarioRuleLibrary/CAbilityAttack.cs:854` checks Disarm before
  processing an attack. At lines 2094–2100 it emits `CPlayerIsDisarmed_MessageData`; for a
  merged ability it cancels that attack and completes the current step. This preserves the
  movement part rather than forcing an attack through a condition that forbids it.
- `ScenarioRuleLibrary/ScenarioRuleLibrary/CAbilityMergedMoveAttack.cs:214` starts the
  copied attack on the selected movement path and emits `CStartSecondMergedAbility_MessageData`.
  The logs contain this message, so the follow-up attack did reach its original entrypoint.
- `ScenarioRuleLibrary/ScenarioRuleLibrary/CTokens.cs:151` processes condition durations and
  logs removals at line 186. The recorded removal above explains why the block did not persist.
- `GH.Runtime/FullAbilityCard.cs:650–688` derives `actionType` as the **other** action that it
  disables, then logs that variable in `after ability click for ...`. A log mentioning
  `DefaultMoveAction` therefore does not establish that the default move was selected.
  Peer line 254465 explicitly records the VR poke committing the big bottom half; the
  original merged-ability message and four-hex movement corroborate that selection.
  The analogous `DefaultAttackAction` message on a later attempt names the disabled default
  alternative, not proof of a default-attack click. Disarm blocks both attack forms.

## Warning visibility review

`GH.Runtime/Choreographer.cs:8068–8091` handles the blocked attack by showing the native
skip control, disabling confirmation, running the actor bar's `DisarmedWarnEffect`, and
showing the original initiative help box with `GUI_TOOLTIP_PLAYER_DISARMED`, formatted with
the localized actor name. Peer log lines 256483 and 263539 record that help box opening;
`AbilityUsageBlocked` is recorded at 256484–256485 and 263540–263541.

The VR `InitiativeTrackSurface` adopts the original **full** initiative transform; its fit
is scoped to the portrait row, but that is not an instruction to hide the sibling help box.
`DamageTooltipSurface` only separately adopts that box during a damage decision and restores
it afterwards. `ActorBars` retains the original conditions bar and does not suppress
`DisarmedWarnEffect`. No source-proven mod suppression of this warning was found in this
bounded review. These facts do not establish that the warning was comfortably readable in
the headset; there is no screenshot of this attack warning in the supplied evidence.

Condition localization is handled by the integrator's finding 8 work. The native
`UIOnAttackCondition.cs:18` localizes `"$" + condition.ToString() + "$"` through
`CreateLayout.LocaliseText`; `CreateLayout.cs:2007` resolves the enclosed key through
`LocalizationManager.GetTranslation`. The native condition key is therefore `Disarm`,
while the full attack-block explanation uses `GUI_TOOLTIP_PLAYER_DISARMED`.

## Validation and scope

This lane changes developer documentation only. No rule-library patch, condition removal,
synthetic attack, callback replay, or target whitelist was added. No regression test is
invented for correct original gameplay. The two independent hardware logs and original
source branches establish the cause; final round build and documentation gates are run
by the integrator alongside the actual fixes for the other findings.
