# NPC669 authority repair: immersive visitors with a vanilla flat host

Worker base: `ca4f8ff7979f898cc7a209328f326be58170cc64`, Build668.
The integration agent owns the Build669 version note, native-bank initialization,
transport discovery delivery and complete integration validation.

## Confirmed gate and native routing

Build668's `TownServiceGrantSync.CoordinatorReady` required the native host to
advertise the exact mod build. Consequently a vanilla flat host could never
satisfy `CanUseImmersive`, even though its VR visitors have their own original
native merchant windows and purchase callbacks. This was an unconditional
presentation gate, independent of actual shop stock or whether a purchase had
been attempted. The supplied VR log identifies Build668 and player1 as flat.
Native rendering initialization is reviewed independently by the integration lane.

The previous class comment treated client-to-client routing as unproven. Reading
the shipped implementation resolves that claim:

- `FFSNet.Synchronizer.SendSideAction` creates `NetworkActionEvent` with
  `GlobalTargets.Others` or `OnlyServer`, and `ReliableOrdered` when
  `canBeUnreliable` is false. Its token keeps the original sending player ID.
- The shipped `Photon.Bolt.EventChannel.Read` feeds reliable events through its
  ordered receive buffer into `EventDispatcher.Received`.
- `EventDispatcher.Global_Others` queues the event to every connection other than
  `SourceConnection`, **before** calling `RaiseLocal` for a received event.
- The unmodified game's `NetworkCallbacks.OnEvent(NetworkActionEvent)` then calls
  `ActionProcessor.ProcessSideAction`; the sentinel target is not the flat host's
  own player ID, so its original method ignores the event. This does not cancel
  the Bolt forwarding that has already occurred.

Thus no modded relay, native Bolt patch or network-game-state mutation is needed.
The existing 28-byte message25 grant codec is unchanged. Guest-coordinator
requests and releases must use `Others`, because `OnlyServer` would stop at the
unmodified host. Compatible native-host coordination retains `OnlyServer` requests.

Read-only decompilation used ilspycmd8.2.0.7535 on the actual shipped `bolt.dll`:

| Input or retained receipt | SHA256 |
| --- | --- |
| `ressources/GH_Data/Managed/bolt.dll` | `9dae48e12d5320b91561411b0bc8a7e97bc47b5befbb7eb44ddcfd647ead0034` |
| `EventDispatcher.cs` | `e2b935c30053180fde99d657489653da18897c6c35709d0a758d5490ba17c3af` |
| `EventChannel.cs` | `584558cc33e1b385551ab5668fea933bbde4c9d5b44a3831c18aaa41970aa6d9` |

The ignored receipts are in this worker's
`.planning/debug/npc669-authority/`, retained for integration. Forwarding is at
`Global_Others` lines473–486; reliable receive is at `EventChannel.Read`
lines156–183. Native FFS sources were read at the main checkout's `decompiled/`.

## Coordinator, physical lifetime and native authority

The existing `VersionGuard.CollectContinuationPeers` supplies compatible peers
from its ordinary current native-player census. Select a compatible native host
first; otherwise select the lowest compatible player ID, including this client.
Retained build history alone cannot elect a departed participant, and avatar
staleness cannot elect a replacement. A native host keeps its initial immediate
self-grant path.

The first guest election and every coordinator change have a four-second commit
drain, equal to the maximum requester permission lifetime and shorter than the
coordinator's eight-second reservation. Immersive native originals remain
available during this interval. It is not a request to reopen the flat shop.
Reset the ledger, coordinator epoch, visible occupations, requester permission
and sent-request slots on a coordinator change. Keep the actual parked offer's
session and nonce, so the physical card and its source do not need replacement.
Only the elected compatible sender may author grants or denials; request/release
processing additionally requires the sending player to be in the live compatible
census and to match the message's player field.

`Unavailable` measures its initial unanswered interval from the later of the
physical offer's start and the coordinator drain's end. A card arriving partway
through the drain can otherwise hit the three-second fallback just after the
drain ends but before its next .8-second request interval. Valid self-grants keep
both response and grant clocks renewed. Received grants retain exact
session/nonce/sequence checks and expiry measured from request **send** time.
Queued old responses cannot restart an expired permission or shorten a newer one.

A single normal-level `Town-service coordinator:` report occurs only when the
selected ID changes; it identifies the selected player, native host and commit
drain. Stable ticks and avatar gaps do not format or emit repeated reports.

This mutex only regulates VR clients invoking their existing native callbacks.
The original game remains authoritative for purchases, donations and upgrades;
no native purchase validation is bypassed. Vanilla visitors continue their
ordinary native flow. Reliable standalone version discovery is handled by the
integration transport lane; the four-second initial settling interval does not
claim a fixed bound on arbitrary network delay or establish headset correctness.

## Focused proof and explicit boundaries

The grant lifecycle runner compiles complete production `GrantSync`, ledger and
codec. Its protocol constants now bind to the selected production source instead
of a historical fixture build. Separate collectible assembly contexts give each
simulated VR player its own actual static production state. Only connectivity,
player identity, native roster/version census, clock and reliable byte delivery
are fixture boundaries. Each sender serializes through the actual codec and each
receiver calls the actual production `Receive`. The relay port reproduces the
read-only shipped `Others` forwarding and the vanilla host's sentinel ignore;
it does not implement election or reservation decisions.

Coverage retains all49 original grant-lifecycle assertions and both original
compiled controls, then adds one/two/three VR guests with a flat host, simultaneous
NPC claims, coordinator self-renewal, exact release to the next visitor, avatar
gaps, real roster departure and epoch handoff with retained physical identity,
late peer discovery, a card arriving during drain, unrelated grant senders,
queued reliable responses, exact request-time expiry and change-only diagnostics.
The existing presentation-election fixture only gains the compatible-roster and
logging ports; its original39 assertions and all five controls are preserved.

Fresh worker validation and exact evidence directories are recorded below after
completion. This focused authority proof is not the complete integration gate,
a native render proof or a hardware playthrough.

Fresh worker receipts (all against the final production grant source):

| Check | Result | Ignored receipt |
| --- | --- | --- |
| Production grant lifecycle | **128 assertions; all11 compiled controls** | `lifecycle/run-__6v25v5/` |
| Production presentation election | **39 assertions; all5 original controls** | `presentation/run-jq9_6k_d/` |
| Source group | **16/16 PASS**,32.9s | `source-final/results.json` |
| Strict Debug | **0 errors,0 warnings** | `build-debug-final.log` |

The grant total comprises the unchanged49 original assertions and79 added
assertions. Its controls comprise both originals plus nine additions: native
host required, missing coordinator drain, guest requests sent only to server,
late-drain fallback timing, receive-time lease revival, older response overwrite,
per-frame coordinator reporting, own retired visible request, and unelected grant
sender. Each mutated production source compiles and reaches its exact runtime
failure; an earlier control-receipt mismatch was corrected to name the first
actual assertion, without removing an assertion or lowering a threshold.

The same-ID handoff case uses an actual recorded old coordinator reply after
2→3→2 elections while the physical offer retains its session/nonce. Retired own
request slots are rejected before visible occupation is assigned as well as
before commit permission. Foreign observer history continues to rely on elected
sender identity and the existing ordered lane; no unstated epoch order is
inferred from arbitrary unsigned nonce values.
