# ModBuild 485 — remote fan character changes

The follow-up request asks for the local open-fan character-change animation to appear on
other players' boards too. Implementation starts from shipped `dev` f7c22ffc, ModBuild 484.
The supplied hardware evidence still comes from ModBuild 482 on both clients.

## Evidence and cause

The local `Fan EXCHANGE` entries at host LogOutput lines 5224, 5502 and 5607 and peer line
2350 are immediately identified as `map-room hand`, all 10-to-10 card exchanges. The host's
scenario exchange at 11005, 8-to-8 cards, already has a corresponding remote entry at 7213.
Therefore the claim that remote exchange never existed is false; the missing map path is
specific and observable.

Local scenario exchanges use `CharacterFocus.PresentedActorId`; map-room exchanges use
`OffScenarioFanSwap`, set by `MapRoomHand.Publish`. The remote animation already implements
the two overlapping waves but only watched scenario focus record 22. The map character key
was already transmitted in record 20 and used to resolve map card fronts, not to start motion.

## Changes

- Use the owner's record-20 map character key to start the existing remote exchange. Keep
  map keys and scenario actor IDs in separate identity domains; no hashing or card identities.
- Invalidate same-count map card resolution immediately on a key change, so new incoming
  cards do not temporarily reuse the old loadout. Keep outgoing fronts under their existing gate.
- Evaluate the exchange before zero-count closing: changing to an empty hand must still
  gather away the old cards, rather than consuming them in an unrelated close animation.
- Publish record 20 immediately when its character key changes. Its old cadence comment
  explicitly assumed that no receiver acted on the key's edge; that assumption is now obsolete.
- Discard/burnt browsers use the local stack-emerge animation when retargeted, including
  equal pile kinds and card counts. Front caches clear on that edge; privacy is unchanged.
  Item browsers already close locally on character changes, so no exchange is added there.

No new wire fields or config values. No authoritative game-state or asset changes. This is a
DLL update after the full 483 bundle. Pure browser-mode changes are outside this character-change
request. ModBuild 484's separate disconnect diagnosis and native bonus-animation verification
limits remain as documented in [MP-ROUND-484.md](MP-ROUND-484.md).

## Validation

All 17 guard checkers pass, including **213,722 assertions** (18 new exchange cases).
Compiled comparison against f7c22ffc: nine changed types and one new helper. The five
otherwise unchanged consumers contain only the inlined ModBuild 484-to-485 value. No config,
patch registration or log-marker surface changes; the bundle remains 74,943,763 bytes.
Release build: zero warnings and zero errors. Documentation i18n passes.

Headset checks: switch equal-sized map hands, change to an empty
hand, switch quickly during an exchange, repeat in the scenario, and retarget an open discard or
burnt browser. Compare both peers' motion and ensure secret card faces stay covered.
