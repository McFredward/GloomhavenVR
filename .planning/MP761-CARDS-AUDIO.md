# MP761 cards and native figure audio review

Worker: `worker/mp761-cards`, base `f93ba368c` (runtime760). This lane is part
of the integrator's Class C multiplayer union, not an independent delivery.
Both paired hardware logs are build759; immutable input hashes are under main
`.planning/validation/build761/hardware759/inputs.json`.

## Forced two-card discard: established cause and bounded correction

The paired logs show `CardHandMode.DiscardCard`, native phase `Action` and
`CAbility.EAbilityType.DiscardCards`. Each owner's two native selections and
`CardsHandUI.OnLoseCardClick` commit succeeded. All four commits also produced
the existing local pile flights and remote wire-fx events. For example, local
19622/19625 match remote19797/19801; remote19292/19295 match local19096/19100.
The initial commit-frame silence was not evidence of a missing flight producer.

The actual presentation hole precedes that commit. Frozen record39 can name
only Discard/Burnt pile seats. Its sampler explicitly leaves hand-source pick
cards anonymous. The receiver then falls through to its ordinary round-card
walk, even though the physical recess holds an unrelated hand pick. That can
produce one back and one different public front; neither is the selected pair.
The original native selections are private local UI until the native commit,
so a receiver cannot reconstruct this pair from its ordinary round-card model.

New additive record124 states the owner's actual physical pick seats. It reuses
the original class-pool provenance resolver, without card IDs or names. That
provenance **is identity**: sampling must pass both existing public naming and
public face permission before even entering its lookup. Private selection only
carries anonymous occupancy or an empty tombstone. Owner local artwork is
unchanged. Receiver interpretation separately requires the displayed actor,
current native phase/round, current scenario lifetime and original roster/pool
membership. Missing public originals remain anonymous instead of borrowing an
unrelated round card. Existing39 bytes and semantics are untouched.

The owner advances a nonzero wrapping revision only when the physical picture
changes. Edges preempt the existing extras cadence; ordinary repeats are quiet.
Explicit releases repeat after use, without a timeout. A peer keeps its accepted
sequence floor across native scenario replacement, rejects old/duplicate reused
revisions, and starts a new inbox on reconnect. An old scenario's accepted
picture cannot be reused by a new native scenario object. Existing transport
peer/session admission remains responsible for connection identity.

Approved public pick fronts feed the **existing** departed-face/flight cache,
using a separate memory from ordinary round-card latching. The ordinary empty
seat edge retires that memory; the flight can claim it before or after the next
board pass. No additional flight producer, gameplay selection, phase advance,
ACK or callback is introduced. Body/material/MR shader state is unchanged.

Wire budget:15-byte header, plus8 bytes per named original;17..33 total bytes
including TLV. The documented maximum becomes12731, within unchanged12800
reassembly and864-byte datagram bounds. The allocation-only buffer grows33 to
12988, preserving its257-byte margin. Older readers skip124 by length. IDs116
through123 were already assigned and were not reused despite CLAUDE's old tally.

## Validation checkpoint

Focused wire proof `mp761-public-pick/run-6u84h8o3` compiled0W0E and passed82
assertions, including original39 vectors. The subsequent scenario-floor repair
is covered by `run-k4_j_gos`: actual plugin source-only Compile0W0E and focused
wire83 assertions passed. This compiles the full plugin without copying movie
content; it is not a complete strict-build/source/local-gate claim. Both runs
retain copied sources, manifests, assemblies, logs and canonical cleanup receipts.
The later accepted-picture Info receipt and golden phase literal remain pending
the next focused rerun. Functional producer/consumer/cancel/rejoin qualification
and final union validation are still required. No new hardware/Photon result
is inferred from these desk checks.

## Audio and remote body: investigation boundaries

The logs do not count individual native AudioObjects or prove a doubled audible
character confirmation. The board's confirmation path invokes the native popup
option once and adds no extra sound there. Original `AnimateCardsLost` plays one
`PlaySound_CardUI_BurnedCard` request per iterator, not one per selected card.
No speculative clip-wide deduplication has been added.

Source-confirmed figure localization hazards: `AudioEffectPlayer.PlayEffect`
uses listener-anchored playback; `FootstepSound` passes a GameObject as sourceID,
which is also listener-anchored; `ClockStone.PlayAudio` supports Global playback;
and `SFXOnEnable` explicitly detaches sources (`attachToParent:false`), so a
persistent source can remain at its spawn position after its figure moves.
The actual summon prefab/clip producer still needs an asset-bound audit before
assigning the reported one-ear cause or choosing a repair. A narrow actual-actor
producer scope and original pooled generation ownership are under review;
UI/listener/voice behavior, native clip/volume/pitch/loop/delay/rolloff and stop
callbacks must remain intact. No audio implementation is present in this checkpoint.

Remote recess-body source audit was sent to the MR lane: `RemoteBoardCard` wraps
all three back/fallback/body materials with `BoardVisual.Unlit`, which chooses
Sprites/Default, and uses a rectangular quad with silhouette alpha. This differs
from local `CardMesh`'s punched opaque Standard body. A blanket alpha/material
change would fill transparent corners or break materialisation. The MR lane owns
the corresponding design; this checkpoint changes no alpha or shader state.
