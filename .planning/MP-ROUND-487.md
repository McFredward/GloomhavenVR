# Multiplayer round 487 — MB486 hardware findings

## Evidence and scope

The supplied host and peer `LogOutput.log` banners both report ModBuild486 (line17).
Evidence resides in the main checkout's ignored `.planning/debug/` and
`.planning/debug/remote/`. Inspected `buttongrößen.jpg`, `bonussymbole_lokal.jpg`, and
`bonussymbole_remote.jpg`. The user's repeated remote filename is resolved by these
actual local/remote screenshot files, without assuming the two pictures are equivalent.

The bonus comparison shows an original shield `1` locally and stale prefab tooltip
content (`Ability Card Name`, twice) remotely. The local damage-button border is
rectangular; the remote border appears oval. The separate remote button screenshot
shows inconsistent cap proportions. Screenshots establish these differences, not their
implementation causes.

The user explicitly supersedes older face exceptions: action-phase cards are open;
selection-phase fans, held cards and placed cards are covered; short-rest burn flights
are covered. Damage sacrifice within the action phase remains open. Permission to name
private cards remains a separate decision.

## Laser ownership

Source-proven double-dispatch path: `VRHand.UpdateBody` ticks `RayUgui` before `RayGrab`.
The visible bar is a trigger collider on the mod layer and is absent from the physics
pick. uGUI previously dispatched pointer-down to a farther canvas before the bar driver
resolved its nearer hit. Native widgets may activate immediately on pointer-down;
cancelling after the bar grabs cannot reverse the gameplay/UI callback.

The shared read-only `TryPickBar` query now rejects a farther canvas before hover,
scroll or press dispatch, after settings redirection. The actual bar carry still starts
in its existing tick; nearer widgets, solid board/card/physics occlusion and near-palm
grab priority retain their existing checks. A latched uGUI press owns its whole gesture
and prevents a second laser grab while dragging. Dedicated narrow bar colliders remain
the hit target; the generous palm zone must not block unrelated canvas widgets.

`LASER BAR OWNERSHIP` is an edge-only next-test attribution line. The source guard tests
pre-dispatch ordering with removed-prepass and early-pointer-down negative controls.
The strict build and first test run passed (238566 assertions before worker integration).
Unity collider geometry and actual input delivery still require headset validation.

## Held-card addressing

The active-list sender previously required `widget.CardType == Active` before it even
searched `CharacterClass.ActivatedCards`. A newly active card whose widget type lagged
returned code0, which legitimately forced the peer to draw a back. The sampler now
checks confirmed model membership first. A model hand member similarly cannot be sent
to the discard/lost branch because of a stale widget type. This changes positional
address resolution, never transmits card identities and never substitutes for RevealGate.

## Worker reviews

- [Card visibility and membership](MP-487-CARDS.md)
- [Initiative stability](MP-487-INITIATIVE.md)
- [Original widgets and button geometry](MP-487-WIDGETS.md)

## Integration and validation

Work started from dev `7f7f9431cb79ac999249caa54149730238b401e2`; the user's default
VerticalDrag change is retained. Workers use fresh worktrees from that commit with
explicit file ownership. Root baseline: 774 types,625 configuration keys,150 surface
patches,4709 log tokens. Final gate readings and hardware limitations will be recorded
here after integration. MB487 is a DLL-only update after the full MB483 install.
