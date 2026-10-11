# NPC655 native return-flight review

Base: dev `deb989570` (Build654). The latest supplied paired hardware banners
name653. Neither log contains per-render flight output or complete numeric
packet timing. They corroborate the reported run, but cannot prove which packet
caused a particular visible hitch. The repairs below have independent causal
production-source evidence; headset smoothness remains hardware acceptance.

## Two defects missed by the previous proof

The real `StockSync.PublishMerchantReturns` supplies the destination hand to its
registered native card-return sampler, without registering that returning card
as a held prop. `CaptureMotionLane` therefore produced Hand0 roots alongside
Hand3/4 exponential return clocks. The bounded numeric budget admits the first
native clock only with a matching root/clock hand affinity. Those clocks could
never be admitted: remote merchant returns fell back to sampled artwork. The
646 moving-holder fixture manually called `RegisterMotionHand`; that extra
registration hid the actual stock-publisher omission.

Separately, `ReceiveMotion` replaced the flight's receive clock on every numeric
sample. Variable transit delay restarted the extrapolation from each arrival,
causing backwards/forwards position jumps even though the original evaluator ran
every render. Existing646 tests delivered samples synchronously without network
delay, so preserving the clock across newer artwork fixed their actual failure
but did not test this separate jitter defect.

The merchant source sampler is especially important: it publishes age0, current
pose and **remaining** exponential lifetime. Keeping its first endpoints or
pretending every later sample starts the original duration would also be wrong.
The new proof compiles that actual sampler and the actual active exponential
update statements from `ItemsPile.cs`, rather than constructing a fixed ideal
merchant receipt. Ability source sampling/update remains extracted from VRCard.

## Production repair and parity

Only `TownServiceMirror.Motion.cs` changes:

- When a card is returning and no current held-hand author exists, its verified
  native destination hand also authors the matching numeric root. Its world
  facing convention remains the original return convention. No new registration,
  hand guess, budget, packet or gameplay state is introduced.
- Each still-live native return keeps its source-to-observer timeline. Newer
  receipts retain their original endpoints, current pose, remaining lifetime,
  easing, arc, orientation and child offsets. They are evaluated at that shared
  source instant, so receipt jitter cannot restart the flight.
- A faster packet describing a future source instant waits until that instant.
  Detached front/body originals share the owner's clock mapping while any
  bounded return remains live. Their individual source receipts and child
  geometry remain separate; equal revision numbers never merge different cards.
  Idle periods permit a fresh mapping without retiming a visible flight.
- Session, service, claim, structure, native census, source withdrawal, stale
  expiry and new hand takeover retain their existing precedence. The646 protection
  against unrelated newer caption/artwork filtering is retained.

No per-frame allocation, extra native query, sender rate, wire layout, renderer,
asset, local gameplay curve or priestess purse playback changes. The mapping
check scans only the already bounded numeric slots on a **new return**, not every
render. One diagnostic is allowed per peer per5s at Debug, guarded before string
formatting; ordinary player logs do not gain a routine stream.

## Focused validation

Worker-local receipts live under `.planning/debug/npc655-flights*`. They retain
complete generated inputs/source hashes, builds, actual Unity results and CSV
per-render comparisons. Unity2021.3.5 uses actual Unity/uGUI transforms and
production capture, codecs, admission, source binding and final return writer.
The small source face/body hierarchy is constructed by the existing fixture;
this is not an original game-prefab visual comparison. External card construction,
config/hand adapters, deterministic source/render
clock and datagram arrival queue are explicit fixture boundaries. The active
merchant test initializer uses a0.55s receipt to retain a longer observable test
window; the shipped producer still supplies its original0.35s glide lifetime.
No native transaction completion callback or HMD picture is claimed.

| Receipt | Evidence |
| --- | --- |
| `npc655-flights-final/run-lqfq2gyy` |243 ability and229 actual merchant assertions;43 render comparisons each, seven delayed receipts. Maximum errors0.01955mm /0.03334mm. |
| Same, split original datagrams |257 ability and257 merchant assertions; front/body groups arrive independently,14 receipts,43 render comparisons. Maximum errors0.01949mm /0.03330mm. |
| Exact653 receive-clock controls |Ability jumps64.109mm and merchant61.464mm at render16 under the same varied nonzero packet delays. |
| Per-partition timeline control |Detached body differs1.598mm at render9 while its face remains correct; grouping metadata cannot replace a common source clock. |
| `npc655-flights-publisher/run-tct9i5k7` |75 assertions: actual StockSync publishes both verified Hand3 root/clock pairs immediately, then native ItemChip updates agree on24 observer renders. Removing the affinity repair emits zero matching roots/clocks. |
| `npc655-flights-existing/run-xfe4ipcz` |Prior646 ability500 /moving approved holder324 assertions pass, including newer artwork and true source removal. |
| `npc655-flights-real-clock/run-k_2hg3c7` |Existing real-clock prepared merchant/ability/cabinet/service-switch card-return480 assertions pass. |
| `npc655-flights-fast/run-66bwuwr5` |Existing full focused motion-fast production subset passes. |

The new suite additionally verifies immediate genuine regrab, actual source
removal, exact native face/body orientation and dimensions. Packet delays range
from22.2–111.1ms with reordering; source header/caption updates are independent.
A fixture control initially failed compilation because `if(false)` generated an
unreachable-code warning under strict settings. Its repaired runtime-impossible
hand condition is compiled and fails the intended native admission assertion;
the compilation failure is retained and not presented as causal hardware proof.

The clock proof predates the later bounded publisher-affinity repair and Debug
report; its unchanged clock code has inherited passing evidence. The publisher
proof exercises the new affinity path directly. This is focused/composite scope,
not a new full local gate. Final strict Release succeeds with zero warnings/errors;
the primary integration owns common source checks, goldens and final build scope.
No WAN timing guarantee or new flight-phase exception is introduced. The next
headset test checks both merchant and enchantress returns under actual paired
render/transport timing.
