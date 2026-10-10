# Pre-1.1.0 network lifecycle review

Reviewed integration: `dev` at `ce637a1dc3fb609204701c701498a20cd7bdc0ae`,
ModBuild 665, against the v1.0.8 release contracts. This worker reviewed session,
transport, resident claims, native transaction authority and catalog admission.
This is the review phase: no production code was changed. Concurrent Build 666
hover/ring/summon work and the primary agent's shutdown cleanup are separate.

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
worker. Passing grant assertions establish the tested authorization/lifetime
properties; they do not establish instant native widget delivery or visual
correctness. No production fix is included in this report yet.
