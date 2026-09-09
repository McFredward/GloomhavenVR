# Build 490: card face lifecycle review

Base: `97ad6c6a` (dev, ModBuild 489). Worker checkout: `mp490-faces`.
This is a source review before the next hardware test. Both supplied current LogOutput banners
still report ModBuild 488; there is no ModBuild 489/490 headset evidence to validate these changes.
No screenshot of a new face failure was supplied for this review. Earlier health screenshots
are unrelated to these source findings.

## Findings repaired

1. **A held card followed board focus instead of its own actor.** The receiver resolved
   `DisplayedActor`, compared it with the atomic rig actor, and blanked an otherwise valid held
   card when the player switched characters. It now resolves the actor carried by that pose slot.
   The integrator supplies independent actor attribution for the second held slot as well.
2. **The full widget does not own the current hand-card model.** Native `AbilityCardUI.Init`
   assigns its own `abilityCard`, while `FullAbilityCard.Init` assigns its model only when the
   card enters action selection. `FullAbilityCard.DeInit` does not clear that model. Reading
   `full.AbilityCard` for native appearance therefore yielded null on never-played cards or an
   earlier model after reuse. Hand fans, pick fans, discard/burnt browsers and held cards now bind
   appearance using the resolved `AbilityCardUI.AbilityCard` (or explicit active/map model).
3. **Pooled ability fronts rebuilt every frame.** Held map/active surfaces repeatedly call
   `ShowFullFace`. Without a live widget its pool fallback cleared and rebuilt the clone every
   frame, repeatedly restarting asynchronous artwork. A weak per-art cache now requires both
   the same model reference and the actual still-shown clone key; unchanged borrowed fronts keep
   their clone and continue mip/art upkeep. Missing full widgets can use the existing model pool
   fallback without manufacturing a wrong model.
4. **Two map holds covered the whole public fan.** The previous single-seat accessor explicitly
   refused two held cards. Both seats now participate in projection. The newer record67 path also
   carries whether each held card remains in the owner's actual native fan slots.
5. **Held map cards lost their source after a character switch or loadout edit.** Local map cards
   deliberately survive in the hand, even when retired or deselected from the loadout. Looking up
   those cards through the current `_cards`/`_loadout` could not name them. Each built map VRCard
   now keeps a weak binding to its original character and class-pool model. Additive67 carries a
   character key, pool seat/count and actual fan slot; no card name/ID is transmitted. Receivers
   resolve the exact named character and pool, independently of current fan focus.
6. **A map republish could retain a hidden held slot absent from the new loadout.** The peer now
   removes held models from the loadout projection and inserts only those hidden slots explicitly
   present in the owner's fan. This preserves the actual positions of all visible survivors,
   including two holds and an empty loadout. Ambiguous, duplicate and incompatible addresses reject
   the projection. An old-character hold cannot hide a slot in the new character's fan.
7. **An active burn could duplicate the stationary cell.** The active matrix now consults the
   burn renderer's actual visual ownership before drawing that cell. The flight lane implements
   the ownership predicate; the integrator connects it through the avatar.
8. **Viewer ownership reopened remote selection fronts.** `ShowRoundCardFronts` includes local
   entitlement, which is appropriate for the local physical hand. Reusing it for peer artwork let
   an owner see fronts on another player's covered board when that peer inspected their character.
   `ShowPeerCardFronts` and all `CardFaces` paths now cover online selection independently of viewer
   ownership. Outgoing fan animations use the same rule. Local own-card visibility and permission
   to transmit private names remain separate. The integrator updates direct board consumers.

## Coverage

Reviewed remote hand and modal pick fans, both held slots, active cards, discard/burnt/item
browsers, original ability/item front sources, outgoing/incoming character exchanges and the
shared phase gate. Checked selection/action transitions, active/hand/pile pickup and return,
short/long rest recovery, damage-sacrifice visibility, same-count reuse, source readiness, map
character switches and held loadout removal. Flight and overlay internals are reviewed in their
separate worker lanes; cross-file source/model findings were shared with both.

The current rule remains action fronts, remote selection backs, and covered remote short-rest
burn flights even across a phase edge. Locally controlled cards always retain their original fronts. Damage sacrifices during the action phase remain open. No new
exception is introduced. Generic unresolved public-flight/held readiness is handled by the
integrator and flight lane without flashing a covered back as a loading placeholder.

## Validation and limits

`CardFaceLifecycleVectors` includes native-model/focus/cache call-site regression guards with
negative controls and executable `MapCardFaceProjection` tests: two plucks in every ordered pair
of an eight-card loadout, kept/removed held slots, two removed held cards, an empty loadout,
duplicate source/arc rejection and mismatched counts. The tests exercise the production projection.

A private copy of integration dependencies passed strict Release with **0 errors / 0 warnings**.
At that snapshot the complete wire suite passed **251,531 assertions** with one root-owned source
assertion failing; the integrator subsequently corrected that source path and reported a green
composite suite. The final peer-policy source guard is included after that private run. Final
complete gate results belong to the integration build notes. All temporary copied dependencies
were restored, and no worker branch was pushed.

These checks establish source behavior and wire invariants. They cannot prove the first rendered
headset frame, asset arrival latency, or perceived timing. Those remain hardware verification.

## User clarification: concealment is remote only

The initial final-review interpretation applied short-rest flight coverage to local cards as well
as remote copies. The user explicitly corrected that interpretation: **the locally controlling
player always sees the original card fronts, including short-rest burn flights**. Only remote
presentation is concealed under the phase and short-rest rules. The earlier local coverage change
is therefore reversed; it is not an approved exception or a pending hardware choice.

`VRCard.FlyToPile` has no concealment flag, and the local card's original face/materials/groups are
not replaced for secrecy. Ordinary animation fading remains intact. Regrab cancellation still
clears the preceding flight callback. Local fallback burn slabs and three launch sites are handled
by the flight lane; remote short-rest provenance remains in the network report. The remote
selection viewer-ownership fix remains valid. Regression guards now reject a local concealment
override, with an injected negative control. Final integration gates cover this correction.

The last bounded map review also removed the old 500 ms membership cache for unheld fans.
Same-character, same-count loadout replacement now reads the bounded replicated list on the next
draw; per-card print keys still avoid rebuilding unchanged artwork. A source regression guard
and a negative control restoring the delayed gate accompany this change.
