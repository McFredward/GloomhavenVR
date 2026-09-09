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
- [Legacy element animation output](MP-487-ELEMENTS.md)

## Integration and validation

Work started from dev `7f7f9431cb79ac999249caa54149730238b401e2`; the user's default
VerticalDrag change is retained. Workers use fresh worktrees from that commit with
explicit file ownership. Root baseline: 774 types,625 configuration keys,150 surface
patches,4709 log tokens. Final integration passes all17 guard checkers and249484 wire
assertions. Strict Release has zero errors/warnings; EN/DE documentation checks and all16
metadata-only reference assembly checks pass. Config625 and Harmony107 classes/165 methods
remain unchanged. Log tokens4713 add four attributable diagnostics without removals.

The compiled comparison contains47 changed and14 added types, with no removed types.
Besides the reviewed feature changes, constant inlining accounts for version banners,
presence buffer allocation and native-board transport bounds. The guard's exit1 denotes
these intended compiled changes, not a failed checker. The user's VerticalDrag=true
remains unchanged. MB487 is a DLL-only update after the full MB483 install.

No two-headset retest was available. Verify front/back transitions when plucking active
and damage-pick cards, the complete covered short-rest burn, stable selection initiative,
nearest laser grab ownership, button sizing through repeated hides, and original bonus
previews/highlight/tooltips including hover effects and board movement.

## Additive wire changes

Record53 appends original element hierarchy output to unchanged52 within message10.
Complete board snapshots are bounded to40960 bytes and still fragmented into864-byte
transport events. Record54 carries the semantic event sequence and covered-burn flag;
provenance is latched before the owner closes the short-rest offer. Record55 carries
actual mandatory-highlight geometry, sprite identity and native Image/color settings.
It is sampled after the owner's native layout in LateUpdate and a changed snapshot
preempts the presence cadence. Presence maximum3970 stays below the existing4096-byte
assembly bound; worst-case3709 retains261 bytes of buffer margin. Prior record layouts,
GVR1 and version3 remain unchanged. Record56 adds a distinct tooltip namespace to the
existing native plume message: original emitter output is keyed by the bonus slot's
existing45 identity. The combined64-emitter limit and12288-byte transport bound remain
unchanged; worst-case9670 bytes includes the additional tooltip addressing.57 is next free.

The short-rest renderer retains an observed offer through delayed loss replication,
but clears obsolete candidates when the model returns them to hand/round/active cards.
A context never observed on the peer cannot be reconstructed before its provenance
arrives; the explicit flight flag remains authoritative once received. Hardware retesting
must include closing/rest-phase edges and packet delay rather than assuming a green
wire test establishes the headset picture.

## Final ownership review

Native `ObjectPool.RecycleCard` reparents ability widgets but leaves item widgets under
their current parent (`ObjectPool.cs:542–551`). Destroying a temporary borrow holder after
that return therefore destroys an item already stored back in the game's pool. The final
review found this in the new tooltip and the existing item-face/plume borrow paths.
All three now share `RemoteItemCardSource.ReturnBorrowed`: keep the card inactive and
move it back under the game pool before recycling and destroying the temporary holder.
Regression vectors bind the production return paths and reject the historical ordering.

The same review checked inactive initialization, detached bonus/ability presentation
data, private material ownership, asynchronous load cancellation, and exclusive front/back
activation. No additional concrete defect was found in those reviewed paths. The original
item effect's image arrays are serialized; the similar ability effect's runtime-only arrays
must not be used as evidence of a missing item rig.
