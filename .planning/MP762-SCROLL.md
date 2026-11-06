# Build762 shared viewport input

The maintainer requires each participant to scroll a shared multiplayer window
while all participants see the same content position and motion. Ordinary private
windows retain their native local input behavior.

## Scope and authority

The seven existing shared subjects are scenario story, public map story, quest
confirmation, encounter/city preflight, movie, chest reward and scenario Results.
Only an actual native ScrollRect or Scrollbar within an admitted public root is
addressable. A composed map-story host may contain private loadout/battle-goal
controls: its exact original dialog subtree is the public root, never the whole
composed window. Existing live session and3D-map participation gates remain.

The existing common placement author coordinates kinds1/5/6/7. For map-only
kinds2/3/4, a nonparticipating2D host cannot scroll a private window on behalf of
VR visitors. The smallest compatible participating3D peer supplies only that
kind's scroll authority; placement authority and all native gameplay authority
remain unchanged. Remote participation uses the existing fresh in-room census.

ReliableOrdered requests reuse the merchant-control transport route. Requests
target a native host directly when it is the coordinator; a compatible VR
surrogate uses Others. A request names a public kind, exact native path/type,
source epoch, opening, original component generation, requester and monotonic
nonce. Wheel deltas retain native wheel units. Pointer coordinates are normalized
against the visibly hit original control frame, then projected through the
author's actual event camera before original ScrollRect/Scrollbar callbacks.
Bare-scrollbar fallback uses its ordinary normalized value. Only these known UI
operations run; no native gameplay command or arbitrary callback is transmitted.

Requests are consumed before hidden private client controls mutate. Every peer
can wheel-scroll; pointer down/drag/up are authored once, including the original
scrollbar repeat-click/handle behavior. A newer gesture or wheel supersedes an
old gesture; departure/reset releases the original active gesture. Native
raycaster, active, interactability and CanvasGroup permissions are rechecked at
execution. A nearer original pointer-down handler retains its existing priority.

## Picture and continuity

Canonical source positions use the original native scroll bounds and four content
corners, not recursive scene bounds. Receiver positions interpolate continuously
without inventing independent inertia. Results' original-widget picture already
interpolates its own matching content, scrollbar and clipping outputs.

Results observers use metadata-only input over the exact common viewport and
scrollbar geometry. Their private native rows can differ and never establish
hit geometry. No cloned scroll/gameplay controller runs. Own input groups bypass
only inert-clone group blocking; source permissions and original viewport masks
remain authoritative. Only canvases containing these own inputs gain a missing
raycaster and register with the existing nested-pointer census. A picture lease
unregisters them when that exact picture retires. Unchanged frames reuse inputs.

Original dialog/page keys, encounter ID/screen keys and quest subject keys scope
native page resets. Exact captured physical/native window receipts also retain
Hide notifications whose shared-kind lookup has already retired; the receipts
and scratch component lists release at native hiding/reset. Closing/reopening,
source election and same-path component
replacement invalidate old requests. Initial canonical states and heartbeats
support late join; failed sends keep pending work. Continuous wheel/drag input is
bounded/coalesced at the existing15Hz presentation cadence, not delayed by an
idle queue consuming its first send opportunity. Private/offline input is local.

## Verification boundaries

The dedicated Unity producer links complete production coordinator/codec/proxy
sources and actual pointer dispatch. Original Unity UI, real raycasts, source
camera projection, clipping, group gates and nested registration are exercised.
Three independent coordinators preserve separate process state. Native singleton,
window and network registries are explicit test ports; full native Results
presentation and real transport have separate integration obligations.

The independent byte golden fixes message31/TLV126 and its full grammar. Maximum
two360-byte UTF8 paths plus79fixed bytes fit one799-byte event below the existing
864-byte event budget. No image payload, presence expansion or legacy TLV change.
Developer validation receipts retain original failed experiments and exact
compiling counterfactuals. No live Photon, Windows D3D11 or headset result is
inferred from these automated checks.
