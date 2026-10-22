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

Optional presence TLV118 carries flags (berth and approach preview), the stable
actorI32LE of the actual card producer, active ability-list population and staged
counted seat (255 for guide only). The remote
board moves its original active panel into the same berth, preserving world pose
and owner tuning, with the same caption and existing cap press stream. No second
card or gameplay command is introduced. Missing/malformed/actor- or count-mismatched state
cannot select a different card. The same actor qualification protects the berth,
preview, cap and caption during remote focus/burn retention. Version3 is unchanged; next free TLV119.

The writer maximum grows by nine bytes to7966 (worst documented presence7709),
still below the existing8000-byte bounded fragment envelope. Ordinary inactive
packets and original item26 remain byte-identical.

## Validation and limits

Focused production-bound fixtures cover native capture with actual Harmony and
simulated game callbacks (145 assertions, six causal controls), actual physical
flow/shared-berth methods (52 assertions, six controls), item drop/close/survivor
and active/item crossings (309 assertions, seven controls), and actual remote
receive/reparent/settle kernels (22 assertions, eleven controls). Engine boundaries
in these new fixtures are explicit; they do not establish headset pixels or real
network timing. New codec golden vectors cover32 assertions.

These fixtures join the normal local/CI inventories (236 local,107 CI,16 source).
Integrated final gate evidence is recorded below when complete. The pre-change
compiled baseline is private to `active-dismiss-baseline`, taken from dev3b6ded192;
the shared historical baseline is not overwritten. Logs/sources/manifests/results
are retained under `.planning/debug/active-dismiss716/` and each fixture's own
debug run. Canonical completion cleanup removes owned generated test caches.

## Integrated validation provenance

The complete local sweep at c12896bd5 recorded all236 suites:179 passed and57
failed. Fifty-five failures were existing town fixture port/control drift; their
production bindings match pre-feature dev3b6ded192. The repaired family is covered
by focused receipts for all55 suite IDs, retaining unchanged successful controls
when one stale mutation was repaired. The environment suite likewise required a
missing exact-terrain boundary port and an obsolete retired-tier hash control;
production11478 assertions plus all95 causal controls are covered across its
full and limited repair runs. No gameplay code was changed for those repairs.
The remaining original failure, the strict native-atlas timing check, passes its
serial actual-Unity rerun:4492 assertions, four causal controls,895 cold original
members in0.5541712 seconds under the unchanged1-second limit. The initial parallel
failure is retained; this does not establish its precise scheduling cause. This is composed validation,
**not a new single green236-suite run**. The original failed reports remain.

For the final actor-qualified implementation: strict Release has zero errors and
warnings, all16 source gates pass, and the complete direct golden-vector binary
passes300693 assertions. The focused52/6 local-flow and22/11 remote proofs cover
the final producer/consumer changes; unchanged145/6 native and309/7 item evidence
is retained. Bundle formats and surface comparison pass:669 config keys retained,
one registered patch added, no removed log marker. Private compiled comparison
covers1283 previous types and1284 current types:one added capture patch,23 changed
feature/version/buffer/metadata types, no removal or unrelated behavior change.
English/German guides and suite inventories also pass their focused checks.

Receipts are kept in `active-dismiss716/full-local-results.json`,
`validation-final.json`, `test-boundary-evidence/validation.json`, `actor-evidence/`,
`runtime-boundary-evidence/`, final source/golden/
strict/surface logs and `compiled-review-final.{json,diff}` below the ignored debug
root. Worker archives retain generated sources, native inputs and captures with
SHA256/compare receipts. A mistakenly duplicated initial full scheduler was
cancelled promptly; its interrupted receipt is retained, never counted as a pass.

Hardware remains to verify: finger/laser use on all three board types, reading
and cancelling/replacing staged summons, rectangular preview and native loss
flight, remote same-card motion, and item choice/damage/demand use with closed
fan. No headset acceptance is claimed from automated checks.
