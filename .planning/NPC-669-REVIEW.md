# NPC669: immersive merchant with an unmodified flat host

## Evidence and confirmed causes

The user reports an empty immersive merchant cabinet for VR guests when the
native host runs the unmodified flat game. Frozen inputs are under ignored
`.planning/debug/npc669/inputs/`, with source sizes and SHA256 in its manifest.
The VR banner is ModBuild668 / `ca4f8ff79`; both game banners identify
1.1.8323.0. The supplied flat-host Player.log contains no GloomhavenVR lines.
The VR client broadcasts as player2, and the native host receives sentinel60123
side actions repeatedly. No native merchant purchase-validation callback is
reached in either supplied log. No new screenshot accompanies this report.

Two source-confirmed problems prevent a working retry:

1. `TownServicePublicMerchant.Tick` resets the public catalog when
   `TownServiceGrantSync.CanUseImmersive` is false. That permission originally
   required a compatible **native host**, although the public presentation
   author already supports a VR guest. Thus a vanilla host leaves the physical
   station without its native catalog, cards or category/page controls.
2. Later map entry records an empty-message original-preparation failure,
   followed by repeated immutable banner Icon texture conflicts. The first
   failure's cause is unproven. A failed `NativeTemplates.Initialize` retained
   partially registered native slot aliases; retry cleared its bank but retained
   its mirror asset registry. Native banner mode artwork can legitimately change
   before the retry. Re-registering the new original into the old immutable slot
   then throws and keeps Ready false. Strict identity refusal is correct; the
   retained failed-bank lifetime is wrong.

## Repair and multiplayer boundaries

A compatible native host remains the preferred VR coordinator. Otherwise the
lowest compatible player in the current native version census coordinates the
immersive callbacks. The native connected roster determines departure; a stale
avatar cannot elect another coordinator. Display preparation is permitted while
coordination settles. A non-native first coordinator and actual handoffs drain
old four-second client permission windows before granting another commit.
Request sequences, epochs and visible permission are retired on handoff while
the physical offer retains its exact session and nonce. The unavailable timeout
starts after coordination becomes available, preventing a parked offer from
falling back at the end of the drain before its next .8-second request.

Non-host requests/releases use the existing ReliableOrdered Others lane; only
the elected compatible coordinator can author grants or denials. This routing is
verified against the **shipped** bolt.dll: EventDispatcher.Global_Others queues
received events to connections other than their source before RaiseLocal.
EventChannel preserves ReliableOrdered delivery; the unmodified host's later
ActionProcessor sentinel-target rejection does not prevent forwarding. See
[NPC-669-AUTHORITY.md](NPC-669-AUTHORITY.md) for hashes and source boundaries.
No native networking patch is introduced.

The existing standalone version announcement now uses reliable delivery through
the same reflection send. Its original one-second cadence and shared bounded
scheduler budget remain intact. Head/hand/live motion remains on its original
unreliable path. Byte payloads, native target arguments and exception cleanup
are retained. The existing source-bound transport fixture executes the actual
SendToken and NextBatch methods; it proves buffer identity/channel selection,
not physical network latency.

Original confirmations still invoke `UIShopItemInventory.OnSelectedItemBuy` and
its original `BuyItem` callback. A VR guest emits the native
`AutoExecuteServerAuthGameAction(BuyItem, MapHQ)`. The unmodified host's native
`MPBuyItem` validates stock and actual gold, including when its own shop is closed.
No mod-side purchase, gold, ownership or gameplay-state write is introduced.

Native template preparation is now atomic. Failure and changed-HUD replacement
retire the old publisher/public-catalog, mirror and template bank through the
existing centralized town shutdown. Retry uses fresh immutable bindings; strict
same-key/different-original refusal is retained. The ready same-HUD fast path
keeps its current bank and IDs. Shutdown explicitly retires private, public and
stock lanes even when called from inside a public capture scope, restoring the
caller scope on return. Successful bank replacement stops the current capture
before it can publish disposed catalog/station arguments; the next regular tick
reacquires originals without another delay. Preparation recovery preserves the
existing error deduplication and rate caps for all publishers and the mirror;
actual map retirement starts a fresh diagnostic lifetime.
See [NPC-669-ORIGINALS.md](NPC-669-ORIGINALS.md).

ModBuild669 keeps Version3 and next-free118. No TLV, asset, configuration key,
input mapping, card layout, finite flight or intrinsic-effect change. The
accepted NPC666 effects and Build668's 23 cm enhancement-list placement remain.

## Validation record

Implementation lanes use separate initialized worktrees from dev
`ca4f8ff7979f898cc7a209328f326be58170cc64`. The integration compiled baseline is
private to its worktree; shared baseline symlinks are detached by the existing
baseline command, without changing the main checkout's comparison source.

The transport-focused proof passes **392 assertions and all7 executable compiled
negative controls**, including unreliable version discovery. Original channel,
exception, event-cap, cadence, compression and batch controls remain meaningful.
A first fixture build lacked a version-codec dependency; the finalized proof
uses its explicit payload boundary and actual scheduler buffer identity. No
production failure or relaxed assertion is inferred from that fixture error.

The initial gate attempt stopped in the 18-test runner inventory check before
any local runtime suite ran: the new lifecycle registration needed its matching
inventory entry and local count216. The corrected runner passes its original
18 tests with two intentional platform skips; no suite or assertion was removed.

Worker and final integration counts, complete-gate results and retained evidence
are recorded after the integrated checks. Automated source, native Unity and
codec proofs do not establish a new paired hardware outcome. The next hardware
test should include one and multiple VR guests under a vanilla host, browsing,
buying/selling, coordinator departure/rejoin and a map reload.
