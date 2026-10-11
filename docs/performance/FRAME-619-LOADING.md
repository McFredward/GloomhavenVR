# Frame 619: finite scenario preparation and room-reveal coverage

## Evidence and limits

The immutable Build618 capture in `.planning/debug/frame619/inputs/` contains two
`Scenario interaction preparation timed out` receipts. Both hit the coordinator's
90-second cap, rather than publishing readiness. Native gameplay remained available.
The old log does not contain queue lengths, so it cannot identify one exact stalled
resource. Source inspection establishes two independent avoidable waits:

- Every already-cached sprite/reference and every original Consume/Infuse component
  consumed a whole alternating preparation frame. Up to 2048 original owners can
  be visited, although many carry the same authored sprite arrays.
- Stat portrait preparation started a fresh 30-second deadline per reference and
  blocked ready later portraits behind an unresolved first portrait.

The loading indicator also retained Unity's reduced asynchronous integration
priority throughout post-native preparation. That can prolong pending original
card/portrait requests after native loading has already completed.

These are source-proven corrections. Headset loading-duration and frame-time gains
require the next capture; the tests do not establish a hardware picture or FPS result.

## Preparation scheduling

Original widget fields, references and cached sprite wrappers now share a bounded
slice: at most 64 bookkeeping jobs or two milliseconds per tick. The same-frame
loader offer guard remains. An original atlas/region cache check distinguishes
cheap sprite-wrapper creation from potentially cold texture creation/readback;
at most one such cold operation or new card backing runs per tick. Native
packing, geometry, pixel, cache-budget and fallback decisions stay in the original
`CardFaceMipBake` implementation.

Stat artwork uses the same bounded metadata policy and one absolute 30-second
portrait wait for the entire pass. Pending references rotate behind ready later
art. All loading references use the existing shared GUID pin owner; no native
addressable request, Image assignment, character selection or synthetic Show runs.
Unused card reserves and parked/local/remote ghost lifetime remain unchanged.
Original async loading priority is restored when native loading ends, independently
of the remaining visual preparation spinner.

Actual scenery, figure-detail and environment build queues must settle before ghost
preparation seals source topology/masks. Their ordinary maintenance cadence does not
start loading. A Debug progress receipt appears at most once every ten seconds;
normal player logging retains the existing bounded readiness/failure context.
The 90-second cap remains a fail-open safety limit for genuinely unavailable work,
with exact pending-owner counts at Debug.

## Revealed rooms

The already-published native
`RoomVisibilityTracker.ProceduralMapTileVisibilityStateChanged` event starts a
scenario-only reveal pass immediately when a new room is shown. Hidden rooms,
map windows, camera movement and periodic refreshes do not re-arm the spinner.
No gameplay/input flag is held or written.

The pass waits for the revealed room's active original `ApparanceEntity.IsBusy`
owners, the exact original `MaterialLoaderData` completion arrays, and actual
pending visual budget queues. Two rendered frame boundaries let native visibility
activate its children; a final quiet discovery includes resources born at placement
completion. Renderer.enabled is not used as a load-completion proxy because detail
and other presentation systems legitimately own that bit.

After native content/material work settles, only new/replaced actor source roots
are appended to the existing ghost cache. Existing held/parked ghosts, shared art
pins, shared mips and unused card backings survive room reveal. The coordinator's
existing fail-open cap also bounds a failed original room resource. Scene/VR teardown
unsubscribes the native event and releases only the coordinator's own preparation
state. All clients independently follow their original local native load state;
card-face privacy and shared native presentation are untouched.

## Focused validation

Real Unity 2021.3.5 execution binds the production preparation and room coordinator:

- Card preparation includes 1024 repeated inactive original Consume owners. The old
  one-metadata-owner-per-frame policy fails the bounded-readiness control; actual
  authored arrays, GUID ownership, packed atlas pixels and local/remote front
  geometry still pass. Same-frame repetition and unbounded async waits are rejected.
- Original SpittingDrake Sleeping/Flying rig, local/remote ghost reuse and rendered
  pixels remain covered. Resetting the parked cache on room reveal fails the
  original local/remote reuse check. Serial portrait deadline renewal fails readiness.
- Complete room coordinator code follows native room events, original busy/material
  state and pending visual masks. Ignoring generation or treating enabled renderers
  as completed async requests fails isolated controls. No native load/input mutation
  is introduced.

Focused evidence is retained under the worker's `.planning/debug/` directories and
copied into the integration ledger. The integrated complete gate remains required.
