# Recovered card presentation — build 531

## Evidence

The supplied local hardware log is build 530; the remote log is still build 500 and
cannot validate this change. The maintainer reports that Spellweaver's recovered
lost cards kept their burnt overlay. There is no new card screenshot for this report.

`Player.log` records `RecoverLostCards` at line 9937 and the combat message at 9939.
The native `CAbilityRecoverLostCards.ApplyToActor` moves Fire Orbs, Mana Bolt, Ride
the Wind and Flame Strike from Lost to Hand at 9950, 9967, 9984 and 10001. The move
stack traces are Debug evidence, not exceptions. Reviving Ether itself moves from
Round to Permanently Lost at 10094; it must keep its own burnt appearance. The
local mod log agrees: Spellweaver temporarily has no burnt cards at 1295, followed
by the four-card hand at 1310 and the newly lost recovery card at 1317.

## Presentation defect and repair

Native `FullAbilityCard.SetPile` (313–328) calls `CardEffects.RestoreCard` only when
its cached widget pile changes. The mod protected running/completed burns from
native redraw resets. A widget can therefore have already consumed its Hand edge
while original burn output remains protected. `BurnLookPolicy` deliberately did
nothing for Hand/Round, and recovery cleanup previously depended on another native
reset call. That combination leaves no writer to repair a genuinely recovered
original. The supplied logs prove recovery, but do not record the exact earlier
widget/cache writer or every material channel.

`BurnArtwork.ReconcileRecoveredAppearance` now observes original owner membership
before local burn policy and before network progress/material capture. A previous
burn departure plus actual Hand/Round membership, with neither lost list claiming
the card, retires old iterator, episode, spent-floor and model-history ownership,
then runs the native presentation reset. Missing owner metadata defers recovery rather than trusting a stale pile stamp.
No card ID, ability name or game-state
write is involved. Initial burns while the model still belongs to Hand/Round are
untouched. A later genuine burn of a recovered card starts normally.

Each original widget retains its own recovery evidence, so another widget consuming
the model-wide history does not strand a hidden face when the player returns to it.
A failed native reset retains a weak, original-card-bound retry latch and produces
one warning per failure episode. Pool retargeting and new native burn playback
supersede that latch. This prevents one malformed widget from aborting the whole
local/network presentation update and prevents a retry from cancelling a new burn.

Remote artwork faithfully samples the owner's originals. Previously, an old burnt
picture could be published with the **new Hand address**, making the incorrect
output valid for the remote mirror. Cleaning before capture fixes this source.
Existing remote binding validation, spent-history pruning, retained burn completion
pruning and missing-sample clearing already reject old Lost-list output after
recovery. Plume capture additionally requires the binding to remain the native
card's current smoke: a recycled smoke tail must not acquire the recovered Hand
address before the local binding's next tick. Wire grammar and identity rules do
not change.

## Validation and limits

- Production-linked burn replay: 552 runtime assertions, six source bindings,
  19 runtime mutation controls and three disconnected-binding controls.
- New cases: recovery without another native SetPile edge; stale serialized piles;
  repeated local/network sampling; Hand/Round precommit burns; same-card reburn;
  hidden duplicate originals; native reset failure/retry; failed reset followed by
  new burn; pooled rebind.
- Remote burn sequencing: 108 assertions and 20 rejected mutations, including
  recovered-source invalidation and maintaining genuine in-progress burn output.
- Retained burn completion: 33 assertions and 11 rejected mutations.
- Strict Release: zero warnings and errors. Integration runs the complete required
  checks, including the real-runtime wire suite.

Headset validation remains necessary: recover lost cards, inspect the local fan,
held and placed cards and the corresponding remote views, then burn one recovered
card again. Reviving Ether itself should remain lost. Repeat after changing focus
away from the recovering character. No supplied current-peer log proves the final
remote pixels.
