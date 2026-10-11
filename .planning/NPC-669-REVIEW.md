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

The authority worker passes **128 lifecycle assertions/all11 compiled controls**
and the retained presentation proof **39/all5**. Actual production grant contexts
cover one and multiple VR guests with a vanilla host, native-host preference,
roster-based departure, later compatible joins, reordered old grants, the full
permission drain and queued requests. The original-bank worker passes **222
assertions/all21 controls** and preparation658's original **7/both controls**.
The integrated default mirror suite retains all20 historical controls; the
worker default proof has232405 assertions. Source-bound Unity boundaries and
the actual shipped Bolt relay are documented in the two lane reports.

The private compiled comparison has **1254 -> 1254 types**, no added/removed
types, exactly five implementation types (`FfsNetTransport`,
`TownServiceGrantSync`, `TownServiceMirror`, `NativeTemplates`, `TownServiceSync`)
and six consumers changed only by the inlined ModBuild668 -> 669 constant.
The original guard snapshot function is reused unchanged for this independent
comparison. Debug and Release strict builds pass with **0 errors/0 warnings**;
direct goldens pass **300510 assertions**, documentation and bundle checks pass,
and all three public surfaces remain **666/235/4795**. The first direct apphost
launch lacked its .NET runtime environment; the same compiled golden DLL passes
when launched with the installed dotnet runtime. This was an invocation error.

The original complete216 local attempt saved **31 passes, four failures and181
unreached suites**. One failure was the historical early-Awake fixture ordering
described in the originals report. The other three explicitly report ENOSPC:
enhancement-handoff compilation, figure-mesh copies and figure-distance startup.
The runner then could not create another isolated scratch directory. All failed
logs are retained. Completed generated binaries are deduplicated only after
their process group exits, using byte-identical SHA256-checked hard links;
worker caches are compacted only after verified archival. Hardware inputs and
unrelated worktrees remain untouched. The185 failed/unreached suites resumed at
jobs2 and saved183 passes plus the two historical fixture failures described
below. Both full affected focused suites pass after repair. The final composed
coverage is **216/216 = retained31 + resumed183 + focused2**. Its receipt verifies
all four reports against the exact216-suite manifest and every saved log SHA256.
Source16 passes independently. This is complete composed coverage, not a new
successful complete-gate wrapper invocation; all failed reports remain retained.

The resumed run exposed two historical fixture dependencies. The Build656
latency control lacked the new bank API while compiling its retained old mirror;
explicit dormant ports now fail fast if unexpectedly called. NPC666's cold
merchant hover probe sampled before the actual transaction lease existed,
despite InteractionOwner already choosing that browsing visitor. Unchanged dev
reproduced the latter failure identically. Both full affected suites pass their
focused repairs with all original controls and thresholds. Product source and
the accepted intrinsic effects remain unchanged by these fixture corrections.

The maintainer's explicit storage request also adds completion cache cleanup to
the local runner. It runs after the suite group is killed/reaped and its log is
closed, including failure and interruption, while the suite lock is still held.
One unambiguous local run and physical .csproj/Unity project markers are required;
wrapper/inner proof references must form one physical ancestor chain, and only
the deepest explicitly evidenced run is selected. Discovery never follows other
generated child runs; wrapper pins protect the inner proof as well. There is no
global hardware-log sweep. Only that run's .NET bin/obj and Unity
Library/Temp/Obj qualify. Logs, original source/manifest/capture trees, symlinks,
.keep pins and hardware/remote/Steam Frame data remain protected. Small cache
receipts are copied byte-for-byte and SHA256-verified before deletion; unsafe,
unreadable or oversized evidence leaves the corresponding cache intact. The
previous success-only pruning of marked older runs remains distinct. Root
validation passes all30 retention tests and all16 ordinary runner tests, with
the two unchanged opt-in SDK checks skipped. Cases exercise actual runner
success, failure and SIGTERM cleanup as well as evidence-preservation boundaries.

Old generated caches in the main checkout and our completed NPC661/665/666
worktrees were also removed after checking active process paths and retaining
reports. Their four receipts record 109945479168 bytes of allocated cache data
removed (about102.4 GiB). Unrelated active worktrees, read-only game references,
hardware inputs and failed-run sources remain available. The already-running
older runner was covered by a separately bound completion monitor. Its final
receipt records222 completed invocations,106 runs with cache removal, and
33.42 GiB of logical cache bytes removed. This logical count is distinct from
the allocated-byte old-cache receipts above. Every saved suite log SHA256 remains
unchanged. The final receipt is retained with the integration evidence.

Automated source, native Unity and codec proofs do not establish a new paired
hardware outcome. The next hardware test should include one and multiple VR
guests under a vanilla host, browsing, buying/selling, coordinator departure/rejoin
and a map reload.
