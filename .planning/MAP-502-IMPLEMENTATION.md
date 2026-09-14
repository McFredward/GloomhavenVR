# Build 502 — native party-container handover

Implementation lane starts from current `dev` commit `5aeb2cce` (build 501).
The fresh singleplayer evidence and old multiplayer recovery comparison are recorded
in `MAP-502-EVIDENCE.md`. This lane changes only native window ownership and its
production regression harness; the integrator owns diagnostic text and build notes.

## Defect and correction

The native `NewPartyDisplayUI.window` belongs to the inner `Party Display UI `.
The frozen story curtain instead contains its outer `New Party display`, a separate
`UIWindow` with ID `PartyPanel`. The old predicate admitted the inner owner and its
children but could never admit that captured parent. The actual ancestor refusal
walk then also withheld the inner battle-goal choices. Both tested singleplayer
quests reached native loadout interaction and released its hide request; the
existing popup prevented the separate empty-room fallback from resolving the trap.

`LoadoutWindowOwnership.IsCurrentContent` now additionally admits the nearest
`PartyPanel` window in the current owner's ancestor chain, by exact reference.
It first rejects non-PartyPanel candidates outside the owner's own subtree, avoiding
unnecessary hierarchy scans. This is a read of native identity and hierarchy, with
no cache, collection allocation, native Show call or gameplay-state write.

The existing gates remain necessary: active map room, initialized/live/open loadout
manager, live/open native party window, and absence of that manager's hide request.
The request continues to enforce the multiplayer waiting barrier. A common canvas,
an unrelated same-ID panel, a higher nested PartyPanel or a sibling outside the
native owner's subtree receives no independent exemption. Reparenting, replacing
or destroying the owner immediately changes the result. Normal conversion of the
admitted outer container still retains its original native contents and visibility.

## Validation

- Source-linked map-flow harness: **1,987 assertions**, previously 757.
- **Six negative controls**, previously five. Restoring the old owner/descendant-only
  predicate specifically fails the outer-container handover assertion. The broader
  ownership mutant specifically fails the unrelated same-ID panel assertion.
- The harness executes the original `StoryComposite.CurtainRefuses` and
  `ModalFallback.RefusedForTheMomentAbove` bodies. Its refusal-table stub explicitly
  isolates the existing story-curtain row; it does not claim to simulate other
  table rules or actual rendering.
- Two successive quest transitions use an outer PartyPanel and different inner
  owner, including intro/wait refusal, live handover across repeated frames,
  withheld-member counts and actual ancestor traversal. Additional inputs cover
  nested panels, non-window spacer transforms, same-ID unrelated panels, common
  ancestors, reparenting, replacement, missing/destroyed objects and map teardown.
- Strict Release build: **zero warnings and zero errors** after worker dependency
  initialization with `scripts/worktree-setup.sh`.
- Final harness run also reads the integrator's updated StoryComposite diagnostic.
  Full integration gates and the build-number change belong to the integrator.

These checks establish the corrected ownership and refusal behavior in source.
Headset confirmation that both singleplayer quest choices become visible and
start normally, followed by a multiplayer readiness replay, remains required.
