# Build 490: pre-hardware card presentation review

## Scope and evidence

The user requested another complete review of fronts/backs, card overlay animation and flights,
with particular emphasis on remote boards and local behavior as the comparison. Three isolated
workers started at dev97ad6c6a (489); the integrator reviewed shared sampling, transport and
cross-board event routing. The existing user README commit362dd4c9 was preserved.
Both latest hardware banners remain488 (main/remote LogOutput.log:17). No489/490 headset run
is inferred from source checks. Previous health screenshots were inspected in the preceding
investigation; no new card screenshot was supplied for this pre-test review.

## Coverage matrix

| Surface or transition | Front/back review | Overlay and flight review |
|---|---|---|
| Scenario fan, both held slots, placed recesses | Action open, selection covered on every peer regardless viewer ownership; actual held actor survives focus changes | Current AbilityCardUI model; pooled source/artwork upkeep; both held seats scoped to displayed actor |
| Active matrix and plucked active cards | Same phase rule; no former active-card exception | Native source appearance; stationary cell excluded during its burn; exact original actor/seat/count retained to true Discard/Lost/PermanentlyLost destination |
| Discard/burnt browsers and rest recovery | Same phase rule including borrowed held cards | Clear stale burn/ghost/flame/group output; use current model rather than the action-only FullAbilityCard field |
| Map fan, both held cards, loadout edits and character switches | Retained immutable map character/class-pool address and exact native held arc seats | Reuse successful original borrowed art instead of rebuilding each frame; no duplicate held fan slots or all-back fallback for two holds |
| Short-rest and damage burns | Explicit short-rest flight stays covered; action damage sacrifice stays open | Both native completion sequences respected; recovery/undo cancels; actor-scoped recess/active ownership; public artwork readiness gates travel |
| Foreign character views and teardown | Card appearance reflects the actual viewing board, selection rules still apply | Canonical owner's release reaches every remote board viewing the actor and the local read-only view; no rebroadcast; another hand's destruction cannot drop its pending hold |

## Additional shared defects corrected

- Mutable actor/list/seat/count addresses were re-resolved for old appearance frames. Receive-time
  model binding is now permanent per frame; immutable class-pool provenance rejects delayed
  same-count replacements even when the source list has already changed before receipt.
  Supply and borrowed cards retain their donor pool. New source identities also split send-queue
  boundaries. Recovery samples get a new timestamp so initial model lag can recover.
- Remote selection artwork previously inherited the viewer's right to inspect their own character.
  The peer-surface predicate removes that exception while preserving local inspection and the
  separate private-card naming predicate. Direct board visibility and initiative consumers follow it.
- The second held pose previously borrowed board-focus attribution; record66 now carries its own
  actor. Actual widget/chip ownership supplies both held sources. List-count and actor changes
  pre-empt presence cadence. A held old-character card cannot remove a new-character fan cell.
- New map provenance remains usable without legacy loadout36 after a held card is deselected.
  Both first/second receiver readiness gates were fixed after the integration reviewer caught
  the old dependency. Malformed or conflicting new provenance is rejected, not reinterpreted.
- Native resets restore graphic visibility/color/gradients, CanvasGroups and all material burn
  terms, including low-detail flames. Actual hidden ancestors and moving shader bounds are sampled.
- Burn claims no longer adopt another actor's release, suppress another character's recess, or
  survive actual recovery. Unresolved active departures cannot fall back to an unrelated recess.
  Public flights pause for original artwork instead of flashing a back or completing invisibly.
- The local recycled-card fallback now uses inert original native artwork, original captured width
  and the same world-up arc as normal cards. Synthetic fallback paint cannot overwrite native output.

## Wire and installation

GVR1/version3 and all previous record grammars remain unchanged. New66 is a four-byte second
held actor. New67 carries map character key, immutable pool seat/count and actual arc seat in
nine bytes, bound to the first rig pose or second extras pose. The two extras actor/map forms
are mutually exclusive. New68 pairs an appearance-frame index with donor actor and immutable
ordinary/supply-pool seat/count. These are positional addresses in existing replicated lists;
no card IDs, names, artwork or gameplay mutations are transmitted. Holes38/40/42 remain unused;
69 is next free.

Rig buffer159 bytes. Extras worst3837/buffer4096 leaves259 bytes, exceeding the largest
single-record257 margin without increasing4096-byte reassembly. Maximum full appearance frame
41318 bytes remains within45056. Existing bounded lossless fragmentation/compression and
six-stream scheduling continue to apply. DLL-only after the full483 asset installation.

## Validation and hardware acceptance

Final integration readings are recorded in STATE.md. Lane reports distinguish source proof,
negative controls and executable projection/binding/receipt vectors:
[faces](MP-490-FACES.md), [overlays](MP-490-OVERLAYS.md), [flights](MP-490-FLIGHTS.md).

A green automated suite does not prove headset pixels. The next test should exercise both
client roles, high/low native card effects, two simultaneous holds, character changes while
holding or burning, short/long rest recovery, active expiry into both piles, damage burns and
map loadout edits while holding. These are hardware acceptance cases, not deferred source fixes.
