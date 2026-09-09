# Build 490: pre-hardware card presentation review

## Scope and evidence

The user requested another complete review of fronts/backs, card overlay animation and flights,
with particular emphasis on remote boards and local behavior as the comparison. Three isolated
workers started at `dev` commit `97ad6c6a` (489); the integrator reviewed shared sampling, transport and
cross-board event routing. The existing user README commit `362dd4c9` was preserved.
Both latest hardware banners remain 488 (main/remote LogOutput.log:17). No 489/490 headset run
is inferred from source checks. Previous health screenshots were inspected in the preceding
investigation; no new card screenshot was supplied for this pre-test review.

## Coverage matrix

Local controlled-character cards remain open throughout. Phase-based concealment in this matrix
refers to remote presentation; the short-rest exception also applies only remotely.

| Surface or transition | Front/back review | Overlay and flight review |
|---|---|---|
| Scenario fan, both held slots, placed recesses | Action open, selection covered on every peer regardless viewer ownership; actual held actor survives focus changes | Current AbilityCardUI model; pooled source/artwork upkeep; both held seats scoped to displayed actor |
| Active matrix and plucked active cards | Same phase rule; no former active-card exception | Native source appearance; stationary cell excluded during its burn; exact original actor/seat/count retained to true Discard/Lost/PermanentlyLost destination |
| Discard/burnt browsers and rest recovery | Same phase rule including borrowed held cards | Clear stale burn/ghost/flame/group output; use current model rather than the action-only FullAbilityCard field |
| Map fan, both held cards, loadout edits and character switches | Retained immutable map character/class-pool address and exact native held arc seats | Reuse successful original borrowed art instead of rebuilding each frame; no duplicate held fan slots or all-back fallback for two holds |
| Short-rest and damage burns | Local controlled cards always open; remote short-rest flights covered; action damage sacrifice open | Both native completion sequences respected; recovery/undo cancels; actor-scoped recess/active ownership; public artwork readiness gates travel |
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
- The second held pose previously borrowed board-focus attribution; record 66 now carries its own
  actor. Actual widget/chip ownership supplies both held sources. List-count and actor changes
  pre-empt presence cadence. A held old-character card cannot remove a new-character fan cell.
- New map provenance remains usable without legacy loadout record 36 after a held card is deselected.
  Both first/second receiver readiness gates were fixed after the integration reviewer caught
  the old dependency. Malformed or conflicting new provenance is rejected, not reinterpreted.
- Same-size map loadout edits now refresh on the next draw instead of retaining stale fronts
  for the old 500 ms resolve cadence; unchanged cards retain their original artwork cache.
- Native resets restore graphic visibility/color/gradients, CanvasGroups and all material burn
  terms, including low-detail flames. Actual hidden ancestors and moving shader bounds are sampled.
- Burn claims no longer adopt another actor's release, suppress another character's recess, or
  survive actual recovery. Unresolved active departures cannot fall back to an unrelated recess.
  Public flights pause for original artwork instead of flashing a back or completing invisibly.
- The local recycled-card fallback now uses inert original native artwork, original captured width
  and the same world-up arc as normal cards. Synthetic fallback paint cannot overwrite native output.
- User clarification during this review (2026-09-09): concealment is exclusively remote.
  Local cards of controlled characters always show their original fronts, including short-rest
  burn flights and recycled-card fallbacks. The interim local-cover implementation was removed;
  explicit short-rest flags still cover remote flights across phase changes. Selection already
  limits local focus to controlled characters, so no additional local secrecy gate is needed.
- Held-card diagnostics count only the body actually drawn. Waiting for a public front no longer
  reports a visible back when the renderer is withheld, or leaves the previous front count active.

## Wire and installation

GVR1/version 3 and all previous record grammars remain unchanged. New record 66 is a four-byte second
held actor. New record 67 carries map character key, immutable pool seat/count and actual arc seat in
nine bytes, bound to the first rig pose or second extras pose. The two extras actor/map forms
are mutually exclusive. New record 68 pairs an appearance-frame index with donor actor and immutable
ordinary/supply-pool seat/count. These are positional addresses in existing replicated lists;
no card IDs, names, artwork or gameplay mutations are transmitted. Holes 38/40/42 remain unused;
69 is next free.

Rig buffer 159 bytes. Extras worst 3,837/buffer 4,096 leaves 259 bytes, exceeding the largest
single-record 257 margin without increasing 4,096-byte reassembly. Maximum full appearance frame
41,318 bytes remains within 45,056. Existing bounded lossless fragmentation/compression and
six-stream scheduling continue to apply. DLL-only after the full 483 asset installation.

## Validation and hardware acceptance

Final integration: all 17 checkers pass, 251,572 wire assertions (+535 from build 489), strict
Release has 0 errors and 0 warnings, docs i18n and all 16 metadata-only reference assemblies pass.
Config keys (625), Harmony surface (150; 107 registered classes / 165 methods), log tokens
(4,714), instrument-writes baseline (61), and the 74,943,763-byte bundle remain unchanged.
The compiled comparison against `97ad6c6a` contains 38 intended changed types and 4 new types,
none removed. Unedited source types differ only through inlined build/buffer constants.
The guard exits 1 for these intentional compiled differences after all 17 checkers pass.

The first integrated gate caught four historical log tokens removed with the corrected local
fallback diagnostic. They are retained as explicitly resolved historical terms, preserving
existing hardware searches without falsely claiming the former violation still occurs.

Lane reports distinguish source proof,
negative controls and executable projection/binding/receipt vectors:
[faces](MP-490-FACES.md), [overlays](MP-490-OVERLAYS.md), [flights](MP-490-FLIGHTS.md).

A green automated suite does not prove headset pixels. The next test should exercise both
client roles, high/low native card effects, two simultaneous holds, character changes while
holding or burning, short/long rest recovery, active expiry into both piles, damage burns and
map loadout edits while holding. These are hardware acceptance cases, not deferred source fixes.
