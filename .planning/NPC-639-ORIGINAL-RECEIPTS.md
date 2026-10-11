# NPC639: received-original metadata receipts

Base: `819a9a9ee`. Worker checkpoint `68fe41a45` adds only the receipt codec and
receipt mirror partial. Driver routing, reliable publication, receipt peer collection,
capture reuse and lifecycle hook integration belong to the primary agent.

## Why reuse requires a real receipt

A locally completed fragmented send proves neither remote receipt nor remote retention.
The old owner-side completion bit cannot authorize a subsequent cumulative delta.
Every current compatible positive peer must explicitly acknowledge the exact retained
full original `(owner, service, session, lane, module, sequence)`. An absent peer
collector, zero compatible peers, a new peer, reset, retired source, changed original
object or another private session conservatively requires a complete original.

Record112/message28 carries at most 20 identities and 220 bytes. The receiver records
an outgoing receipt only after the complete original is actually in its
`ReceivedBaselines`; waiting native-template expansion and cumulative deltas cannot
produce one. This is metadata retention only: it does not prove rendering or grant a
gameplay permission. Reliable admission exceptions retain the pending receipt.

Owner receipts are bound to the existing source and immutable baseline objects.
Foreign-owner and old-session valid broadcasts are ignored without warning or credit;
malformed packets and incompatible senders are rejected. Pending owners and retained
source/peer metadata are bounded. Retired source receipts are pruned at capacity.
Exact baselines may survive an inactive same-session manifest only when the advertised
module census still retains them; clearing the actual baseline must never be disguised
as continued receipt. The primary integration supplies that manifest retention policy.

## Focused evidence

`scripts/check-npc639-original-receipts.py` compiles the actual new codec and mirror
partial, complete actual frame and packet-header source, and extracts the required
actual protocol constant declarations verbatim. The fixture replaces only engine-facing
owner/receiver storage and compatible-peer discovery. It checks independent literal
bytes, bounded malformed and duplicate records, reserved IDs, 4/5 peers, two owners,
stale/forged identity, new/reconnected peers, reset, exact object replacement,
20-entry batches, 100 repeated same-session transactions and retirement bounds.

Production passed **365 assertions** in
`.planning/debug/npc639/receipts/run-5n3vyudg/production/result.log`.
Three compiled causal controls reached their intended semantic failures: inferring all
observers from one receipt (same run), acknowledging an unstored original
(`run-kbrjl1dk`), and inheriting receipt from another same-sequence original object
(`run-q1fsv_jt`). Only failed test-control cases were rerun; unchanged production and
first-control evidence was retained. The script supports `--case` for focused reruns.

This is not a new complete wire-gate pass, a full game/Photon run, a render proof or a
claim of a measured one-second headset outcome. Integration still needs the actual
capture/receive lifecycle and transport proof, followed by paired hardware evidence.

## Compatibility handshake ordering repair

Checkpoint `c02fabc59` retains pending real receipts until compatible-peer discovery
succeeds and includes their original owner. Originals may arrive before the build
handshake completes; publishing and discarding a one-shot receipt during that gap can
lose the only acknowledgement when the owner rejects an unknown sender. Missing or
failed discovery and an absent owner now preserve the pending metadata. Once the
owner becomes compatible, the next capture publishes the exact retained identities
once. Incoming receipt compatibility validation is unchanged.

The revised actual-code production proof passes **375 assertions** in
`.planning/debug/npc639/receipts/run-dagag2p0/production/result.log`. Its fourth
compiled causal control removes owner-membership gating and fails at the intended
unknown-owner pending-retention assertion. Only revised production plus this new
control ran; the preceding three unaffected controls retain their recorded evidence.
The complete native-picture proof's role fixture already supplies compatible owner
IDs before receipt capture; this small prelude does not require repeating its passing
timing cases. These metadata checks do not independently prove remote handshake or
Photon callback ordering.
