# Pre-1.1.0 network lifecycle review

Reviewed integration: `dev` at `ce637a1dc3fb609204701c701498a20cd7bdc0ae`,
ModBuild 665, against the v1.0.8 release contracts. This worker reviewed session,
transport, resident claims, native transaction authority and catalog admission.
The initial review phase changed no production code. The bounded, subsequently
authorized repair is recorded below. Concurrent Build 666 hover/ring/summon work
and the primary agent's shutdown cleanup are separate.

## Confirmed finding: resident presentation ownership does not converge

`TownServiceMirror.InteractionOwner` (lines 148–179) retains a live cached visual
lease before checking the current visitor set. Two visitors who open the same
resident's native workspace independently can each settle their own lease after
120 ms. If their peer manifests arrive later, both continue returning their own
player ID even after they have the same complete visitor set. The resulting
disagreement lasts until that owner's session closes, expires or changes.

The initial age ranking is receiver-local too: it combines a sender's session age
with time since this receiver's receipt. Removing the cached return alone would
not establish a common ordering across clients.

This changes executable rendering decisions. `TickRemote` (lines 1154–1180)
uses `InteractionOwner` to select the shared original native workspace.
`RetainIndependentVisitorOnly` retires the other visitor's shared modules while
preserving their personal card/purse modules. The split election therefore can
select different original widgets at one physical stand on different clients,
violating the shared presentation contract.

The gameplay reservation is a separate host-authorized mechanism.
`InteractionOwner` first returns `TransactionOwner`; the latter gives the host's
reliable grant precedence (lines 185–215). The permanent resident animation author
also uses a separate deterministic peer election. This finding does not prove a
double purchase, conflicting native callback, resident-pose divergence or a
hardware deadlock.

### Reproduction

A small .NET probe extracted the unchanged production `InteractionOwner` and
`LiveInteraction` methods. Only the clock, visitor arrival, local peer/session
ports and the pre-drop `TransactionOwner == 0` boundary were supplied by the
fixture. Both visitors began at time 100, settled at 100.13 and learned about
the other at 100.30. At time 101 the identical visitor sets still elected
opposite owners:

```text
client=1 electedBeforePeer=1 afterSameVisitorSet=1 oneSecondLater=1
client=2 electedBeforePeer=2 afterSameVisitorSet=2 oneSecondLater=2
CONFIRMED: identical complete visitor sets retain opposite NPC original-presentation owners.
```

Evidence: `.planning/debug/release110-net-review/claim-convergence/`. The source
hash and declared fixture boundary are recorded in `source-proof.json`. This
proves the production election defect, not the headset appearance.

### Bounded repair recommended

Use a stable peer-ID election over all live browsing sessions, reevaluated when
membership changes, while retaining the actual host-granted transaction's
precedence. Browsing must remain non-exclusive; personal fans, held items,
purses and return flights must remain independent. Keep existing stale/session
checks and cleanup. Test late admission, different arrival orders, peer leave,
session replacement, cross-resident independence and host grant takeover/release.

## Other reviewed paths

| Area | Result and boundary |
| --- | --- |
| Host-authorized native transactions | `TownServiceGrantLedger`, `TownServiceGrantSync` and the grant codec bind reservations to service, player, session and offer nonce. Release cannot erase a replacement offer. The client request timestamp prevents late replies from extending an old grant. Separate residents have independent reservations. Temple callbacks can require authorization without creating an exclusive priestess occupant. Focused production-code evidence is recorded below. |
| Stale avatar, leave and reconnect | `NetAvatarDriver.ForgetPeer`, its town-service partial, and `TownServiceMirror.RemovePeer`/`ResetNetwork` retire transient presentation, catalogs, voice/motion state and receive queues. Avatar staleness deliberately does not revoke the host ledger early: the host reservation outlives a client's shorter commit permission, avoiding a split-brain callback window. No additional source-proven defect was found in these paths. Native-host departure follows the native game's session teardown; no unsupported host-migration guarantee is asserted. |
| Shared merchant controls | `TownMerchantControlSync` forwards original category/page input to the current shared author, deduplicates sender sequence numbers and retains monotonically increasing local request IDs across resets. The public bridge does not require a selected character or a free offered-card transaction. Crank ownership expires and cached author changes seed the accepted native rack state. Existing control tests were inspected; they were not rerun by this review. |
| Native catalogs and first pictures | Catalog admission validates complete original references and hierarchy/header identity before promoting a bank. Manifests retain complete baselines separately from compact deltas. New session cleanup invalidates peer catalogs, including when no rendered module exists yet. Receipt/request paths distinguish an actual complete baseline receipt from a missing-original request. No confirmed cache reuse or false-receipt defect was found. |
| Reordered transport and bounded queues | Native original pages and grant/control messages use reliable ordered delivery; sampled motion remains separate. Fragment sequence admission prevents an older completed presence from overwriting newer presence. Pending native data keeps baselines separately from deltas and clears per peer/session. Review covered these source fences and queue bounds; it did not measure network throughput. |
| Unmodded peers | Native gameplay still uses the game's network flow. Mod extras identify their own sentinel packets. An unmodded native host cannot issue this mod's grant protocol; `CanUseImmersive` restores usable original town windows instead of granting a client local authority. This is the existing compatibility fallback. Unmodded peers are not forced to install the mod by the mod-version dialog. |
| Permanent resident author and offered focus | Resident animation author election is independent of the defective browsing-presentation lease. Merchant/enchantress transaction owners have exclusive offered-card focus; temple blessing focus is temporary and does not reserve the resident. Full visible pose/audio parity is covered by the other review lanes. |

## Fresh focused verification

`python3 scripts/check-town-grant-lifecycle.py --output-dir
.planning/debug/release110-net-review/grants` passed **49 assertions against
production grant code**. Both negative controls were detected:
`stale-own-visible` and `temple-occupation`. Evidence is in
`.planning/debug/release110-net-review/grants/run-aq0o1khv/`.

The separate unchanged-production election probe reproduced the finding above.
No full Unity suite, wire gate or four-player hardware run was performed by this
worker during the review phase. Passing grant assertions establish the tested
authorization/lifetime properties; they do not establish instant native widget
delivery or visual correctness.

## Authorized repair and focused evidence

Browsing now chooses the lowest live player ID on every observation. A cached
first-arrival owner and receiver-local session ages cannot rank the shared native
presentation. Initial browsing visibility is immediate. The ungranted transaction
presentation fallback uses the same deterministic ranking and reevaluates changed
membership/session identity, retaining its existing bounded 120 ms cosmetic
settle. An actual reliable host grant still takes precedence immediately. The
matching host grant remains the only online native callback permission. Temple
transactions never become exclusive occupants. No protocol, packet layout,
character ownership, guide, fan/purse partition, ring or hover code changed.

The old Unity `SharedInteraction` fixture explicitly asserted that a later
lower-ID visitor could not preempt an active browsing lease. That assertion
enforced the defective first-arrival cache rather than proving distributed
convergence. Its two affected owner expectations now select the lower-ID visitor,
and explicit local browsing departure precedes the unchanged remote-close checks.
The existing highest-player-wins counterfactual now binds to the shared live-ID
condition; its designated causal assertion is unchanged.

Fresh evidence after the repair:

- `check-town-presentation-election.py`: **39 assertions**, compiling unchanged
  production election/identity/affordance/reset methods and complete production
  grant sync/codec/ledger. Covers delayed/reversed arrivals, age skew, close,
  disconnect membership, stale/session-zero admission, replacement session,
  independent residents, non-exclusive browsing, reliable grants and temple
  callback permission. All **five causal negative controls** are detected:
  original browsing cache, original receiver-age ranking, stale transaction cache,
  ignored actual host grant and exclusive temple occupation. Evidence:
  `.planning/debug/release110-net-review/election/run-697arz6t/`.
- `check-town-service-mirror.py --suite shared-interaction
  --no-negative-controls`: production **passes in Unity 2021.3.5f1 / llvmpipe**,
  exercising original native workspace capture, codec/playback, source ownership,
  independent visitor partitions and its existing render comparisons. Evidence:
  `.planning/debug/release110-net-review/mirror/run-xuwoa7tu/`.
- The repaired Unity `highest-player-wins` negative control compiled and failed
  exactly at its designated player-ID election assertion. Production was not
  repeated for this focused counterfactual. Evidence:
  `.planning/debug/release110-net-review/mirror-negative/run-8wegu3ek/`.

The existing 49-assertion grant lifecycle proof above is inherited for unchanged
grant sources; it was not redundantly rerun. This worker did not repeat the full
gate or establish four-player headset correctness. Large generated Unity project
artifacts are removed after retaining compact hashes, fixture manifests, result
logs and comparison evidence.
