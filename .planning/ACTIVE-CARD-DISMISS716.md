# Physical active-card dismissal — Build716

The maintainer's 2026-10-22 interaction decision replaces the proposed held-card
button with the existing item-use berth: release an active rectangular ability
card into that destination, then explicitly press End effect / Dismiss. Reuse its
landing preview, settle, placement sound/haptic, native keycap and press feedback.
Successfully placing an item also closes its fan without retiring its decision.

## Gameplay boundary

`CardsGameApi` reads the original active model, exact native actor/widget/hand,
local control, native cancellation permission and CannotCancel. Online offers
also preserve the native selection/current-caster window which flat hand routing
implicitly provided before VR independent character inspection. Do not copy the
persistent-icon target-owner rule into whole-card cancellation: a whole owned
card can include an aura affecting another character.

Only the synchronous `UIConfirmationBoxManager.ShowCancelActiveAbility` called
by our own `DiscardActiveAbilityCard` request is captured. No popup is accepted
automatically: the physical berth is that pending confirmation's presentation.
Its explicit cap invokes the exact original affirmative closure, including native
UI refresh, summon cancellation, choreography continuation and original game
multiplayer replication. No model, inventory, pile, ForceEnter or summon state is
written by the mod. Taking the card back cancels the unconfirmed reservation.

Original identities, native phase/mode and current permission are rechecked on
confirmation. The physical card remains staged after acceptance until its native
active membership ends. Scene-load release, board rebuild, teardown, lost
permission and character changes cannot leave a stale confirmation or parked
invisible staged card. Keycap callbacks retain their exact decision; in-place
cap updates replace the bound callback when the action changes even if its label
does not. Old callbacks cannot accept the replacement card.

## Shared presentation

The existing berth frame already has ability-card proportions. The ability-card
landing preview uses its full rectangular metric and the same item tint/Z,
1.04×0.94 fit and soft reveal. Existing item art and tuning are retained.
One berth author prevents alternating item/active pumps from hiding or relabeling
it. An accepted item drop yields the active reservation before installing its
own slot and cap; rejected item drops do not cancel anything.

Optional presence TLV118 carries only flags (berth and approach preview), active
ability-list population and staged counted seat (255 for guide only). The remote
board moves its original active panel into the same berth, preserving world pose
and owner tuning, with the same caption and existing cap press stream. No second
card or gameplay command is introduced. Missing/malformed/count-mismatched state
cannot select a different card. Version3 is unchanged; next free TLV119.

The writer maximum grows by five bytes to7962 (worst documented presence7705),
still below the existing8000-byte bounded fragment envelope. Ordinary inactive
packets and original item26 remain byte-identical.

## Validation and limits

Focused production-bound fixtures cover native capture with actual Harmony and
simulated game callbacks (145 assertions, six causal controls), actual physical
flow/shared-berth methods (47 assertions, six controls), item drop/close/survivor
and active/item crossings (309 assertions, seven controls), and actual remote
receive/reparent/settle kernels (14 assertions, six controls). Engine boundaries
in these new fixtures are explicit; they do not establish headset pixels or real
network timing. New codec golden vectors cover26 assertions.

These fixtures join the normal local/CI inventories (236 local,107 CI,16 source).
Integrated final gate evidence is recorded below when complete. The pre-change
compiled baseline is private to `active-dismiss-baseline`, taken from dev3b6ded192;
the shared historical baseline is not overwritten. Logs/sources/manifests/results
are retained under `.planning/debug/active-dismiss716/` and each fixture's own
debug run. Canonical completion cleanup removes owned generated test caches.

Hardware remains to verify: finger/laser use on all three board types, reading
and cancelling/replacing staged summons, rectangular preview and native loss
flight, remote same-card motion, and item choice/damage/demand use with closed
fan. No headset acceptance is claimed from automated checks.
