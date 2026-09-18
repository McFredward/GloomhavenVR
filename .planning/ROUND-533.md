# Build 533 — recovered action card must not return to the round dock

## Evidence and cause

The local hardware log is build 532 / assembly 1.0.5.0; supplied peer logs remain
historical build 500 and cannot validate this fix remotely. The maintainer clarified
that both outgoing flights worked: the previously burnt healing card reappeared
clean in the first recess after the second card recovered the lost pile.

`Player.log` records Freezing Nova moving Round -> Lost (4761), then Lost -> Hand
(4942), followed by Reviving Ether moving Round -> PermanentlyLost (5039). Native
`CardsActionControlller` retains its initialized `topCard` / `bottomCard` references.
`CollectRoundCards` supplements the real round list from that static pair, guarded
by `HasLeftTheRound`. That predicate rejected discarded/lost/permanently lost/active
membership but omitted Hand. Recovery therefore made the previously played card
eligible for the dock again. This is a source-proven admission defect consistent
with the reported sequence, not another failed burn flight.

## Change

Treat actual owner Hand membership as a departure from the round, including native
lost-card recovery, discard recovery and selection undo. The existing actual Round
and ExtraTurn checks retain precedence; no sticky history, ability-name special
case, game-state mutation, or new networking field is needed. `MoveAbilityCard`
removes the source membership before adding the destination; normal extra turns
move their cards from pending lists into `ExtraTurnCards` (native `GameState`,
around 1792). No hypothetical simultaneous Hand/ExtraTurn membership is required.

Unknown native transfer gaps retain the previous static-pair fallback. The card can
be selected normally into a later round. Foreign focus, initiative ordering and
long-rest empty docks remain unchanged. The existing per-widget refusal dedupe now
reports returned-to-hand refusals without attributing them to damage staging.

Remote owner slots are published from the actual local tray/half selection;
remote model-derived round cards use `RoundAbilityCards`, not this singleton.
Correcting local admission therefore removes the false published slot as well.
The integrator independently audited those networking consumers.

## Validation

`bash scripts/round-card-tests.sh` executes complete production `CollectRoundCards`,
`HasLeftTheRound`, and `OrderRoundPairByInitiative` methods with native-model doubles.
Cases cover the complete reported sequence, repeated rebuilds, focus exchange,
widget replacement, later reselection, round cards without a singleton pair,
foreign non-acting round cards, real extra turns, long rests, all destination piles,
unknown membership and card-owner precedence. The focused suite passes 50 runtime assertions and six negative controls exercising the
recovery gate, its caller, extra-turn handling, turn scope, long-rest exclusion,
and actual ownership. The worker Release build passes with zero warnings/errors. Integrated strict build and the complete gate suite are
recorded by the primary agent. Headset and current-peer pictures remain unverified.
