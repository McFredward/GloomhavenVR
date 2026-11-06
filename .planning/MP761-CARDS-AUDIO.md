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

New additive record124 states the owner's actual physical Hand-pick seats. Native Discard/Burn picks retain record39 identity. It reuses
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
Explicit releases repeat after use, without a timeout. A peer keeps its accepted sequence floor across native scenario replacement,
rejects old/duplicate reused revisions, and starts a new inbox on reconnect.
That floor alone cannot reject an unseen old N+1 packet. The record therefore
also carries the native serialized Seed plus a deterministic scenario-ID
fingerprint. Both inbox and consumer compare that read-only native namespace.
A different-namespace delayed N+1 cannot establish an old seat. Native state
serialization includes ID/Seed (ScenarioState.GetObjectData671/677); loading
restores them (733/758), and GHHostCallbacks.SendScenarioStateWhenReady311
streams that exact native state. The fingerprint is a namespace, not a unique
instance/transport epoch: an exact seed+ID saved-game replay is indistinguishable.
The local object edge conservatively clears accepted seats, retains revision
order, and the producer emits a new ordered tombstone even when no seat is held.
Existing transport peer/session/sample-time admission still owns connection order.

Approved public pick fronts feed the **existing** departed-face/flight cache,
using a separate memory from ordinary round-card latching. The ordinary empty
seat edge retires that memory; the flight can claim it before or after the next
board pass. No additional flight producer, gameplay selection, phase advance,
ACK or callback is introduced.

The final review found that an ordered124 identity alone could still lose to an
old record4 physical occupancy packet: Presence extras are arrival-deduplicated,
not sender-time ordered. Record124 therefore includes its own complete BoardSlots
physical mask and keeps the presented actor/context even when its Hand mask is
empty. Matching124 occupancy is adopted before the original empty/compaction
branches. Any later normal round placement changes that same124 revision too,
so a tombstone cannot hide subsequent ordinary cards. Record124 claims identity
only for widget.CardType==Hand, and never shadows legacy39 pile identity.
Hand-to-Discard commit transfers the previously approved Hand face separately to
the existing departure cache before view content changes. That permits a valid
flight before the local native discard model catches up, without drawing a stale
front on a new unknown occupant. Private phase, native actor/context loss and
unusable records immediately clear124-owned live and departed faces; ordinary
round latches retain their prior behavior. Body/material/MR shader state is unchanged.

Wire budget:28-byte header, plus8 bytes per named original;30..46 total bytes
including TLV. The documented maximum becomes12744, within unchanged12800
reassembly and864-byte datagram bounds. The allocation-only buffer grows46 to
13001, preserving its257-byte margin. Older readers skip124 by length. IDs116
through123 were already assigned and were not reused despite CLAUDE's old tally.

## Validation checkpoint

Focused wire proof `mp761-public-pick/run-6u84h8o3` compiled0W0E and passed82
assertions, including original39 vectors. The subsequent scenario-floor repair
is covered by `run-k4_j_gos`: actual plugin source-only Compile0W0E and focused
wire83 assertions passed. This compiles the full plugin without copying movie
content; it is not a complete strict-build/source/local-gate claim. Both runs
retain copied sources, manifests, assemblies, logs and canonical cleanup receipts.
The namespace-fence run `run-141jp65c` repeats full plugin Compile0W0E and
passes100 focused wire assertions (including original39, unseen old N+1,
namespace mismatch, same-save conservative clear, reconnect and revision order).
An initial runner attempt lacked dotnet on PATH and exited before compilation;
its failure and canonical cleanup are retained in `run-wpaxut5o`. Functional producer/consumer/cancel/rejoin qualification
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
The hardware summon is PlagueRatID. The immutable original
npc_plaguerat_assets_all.bundle has an enabled p_PlagueRat_Idle SFXOnEnable:
PlaySound_PlagueRatIdle, StopOnDisable=true, PlayOnce=false. Its source-ordered
AudioController item is LoopSubitem, one stereo14s clip, Pan2D0 and no source
override. Idle_SFX category points to original Audio3DSourceIDLE (GameObject5651,
AudioSource7782): spatialBlend1, custom rolloff, min4/max30, doppler0. The detached
position therefore proves a stationary 3D loop after figure movement; it does not
prove which ear a particular headset heard. Evidence JSON records native raw
prefixes, object/asset hashes and original serialized source values. A narrow actual-actor
producer scope and original pooled generation ownership are under review;
UI/listener/voice behavior, native clip/volume/pitch/loop/delay/rolloff and stop
callbacks must remain intact. NativeFigureAudio/NativeFigureAudioPatches now wrap the four original figure
producers (SFXOnEnable, FootstepSound, AudioEffectPlayer, SFX-only PlayAudio).
Admission requires VR and an exact actual ActorBehaviour ancestor with a live
native Actor association. Original PlayAudioSubItem receipts identify every real
source/layer generation, including unscoped reuse of the same object/item/clip in
the same ClockStone timestamp. Binding happens after original AudioItem overrides.
The original source follows its exact emitter and authored local offset; global
figure requests use the emitter position. The implementation changes only world
position: no reparent, stop, duplicate request, clip, pan, blend, volume, pitch,
loop, delay, distance, doppler or rolloff writes. Actor reassignment, destroyed or
inactive producer/source, VR-off and actual reuse revoke only the follow receipt.
The original producer still owns native StopOnDisable and other stop behavior.

Native Unity proof `figure-audio-runtime/run-a3sf_ppg` passes21 production
assertions using unchanged publisher SFXOnEnable/AudioEffectPlayer/AudioController/
AudioObject and real Unity AudioSources. Synthetic stereo probe waveform replaces
the copyrighted clip; native delayed start, subitem loop, original stop and source
settings execute. The old detached-position and stale-actor controls fail exactly.
Its pool-generation control failed at the stronger later same-frame assertion,
so its expected failure was corrected without changing production or case inputs.
Bounded `run-5i6dhxg4` passes that control and the additional VR-off control. Earlier
nullable fixture compilation and a pool-reuse oracle error are preserved: native
useExistingAudioObj deliberately ignores worldPosition, so the oracle now snapshots
its actual original transform before verifying stale follow cannot move it. No
production workaround was added for that fixture error. All processes are reaped
and canonical cleanup receipts retained. This is not physical ear/HMD output,
full gameplay actor animation, original waveform quality or Photon qualification.

Functional public-pick `run-2qtcfzgf` compiles the actual native PhaseType enum,
exact production sampler/provenance/codec/inbox, remote resolver and existing
flight-cache methods.35 assertions and6 causal controls pass: exact two Hand
originals, cancel/tombstone/rejoin, private naming separation, donor supply pool,
actor/phase/round/context/roster refusal, before/after-empty flight order, consumed
memory and ordinary-round control. Native actor/widget/ownership/render inputs
are explicit ports; native selection callbacks and final pixels are not executed.
This predates the final hand-only/atomic occupancy review and will be extended.

Remote recess-body source audit was sent to the MR lane: `RemoteBoardCard` wraps
all three back/fallback/body materials with `BoardVisual.Unlit`, which chooses
Sprites/Default, and uses a rectangular quad with silhouette alpha. This differs
from local `CardMesh`'s punched opaque Standard body. A blanket alpha/material
change would fill transparent corners or break materialisation. The MR lane owns
the corresponding design; this checkpoint changes no alpha or shader state.


## Final cards checkpoint

`public-hand-pick-runtime/run-1595sflw`:46 production assertions plus10 exact
causal controls pass, with native PhaseType/CardPileType declarations imported.
The added coverage includes Hand-only versus legacy pile ownership, ordered fill
and empty overriding older ordinary occupancy, subsequent ordinary placement,
Hand-to-Discard departure before model catchup, private/actor/scenario invalidation
before the next board pass and invalid-record departure clearing. Native widget,
ownership and renderer inputs remain explicit ports. Adoption guards bind the
actual production occupancy and departure calls, not an unused helper.

`mp761-public-pick/run-v8v720q9`: full plugin source-only Compile0W0E and101
focused wire assertions pass, including unchanged39 vectors and byte-exact124
46-byte maximum. The immediately preceding run caught namespace validation
clearing an already accepted newer receipt before checking revision order;
valid old/duplicate packets are now refused before context invalidation. Its
failed wire evidence is retained in `run-axl_8m_k`. This is not the integrator's
complete strict build/source/local gate and does not claim native callback,
Photon delivery or HMD picture qualification. A further audio hierarchy correction
is in progress; the prior ancestor-only audio fixture is inadequate for actual
native rat admission and is superseded below when complete.


## Audio hierarchy correction and final evidence

The initial21-assertion ancestor fixture did **not** represent the actual native
model hierarchy and cannot establish the PlagueRat repair. Source review caught
this before acceptance: Choreographer.CreateCharacterActor sets
CharacterManager.CharacterActor before initializing the model (820/821), creates
ActorPrefab later (883), assigns CharacterActorGO (885), parents that prefab under
the animator (889), and only then calls ActorBehaviour.SetActor (930). Model SFX
and ActorBehaviour are siblings. The async creation path preserves that ordering.

The final admission follows the exact native CharacterManager ancestor, its
CharacterActorGO pointer and ReferenceEquals(CharacterActor, ActorBehaviour.Actor).
The original direct-ancestor case still works where actually present. A receipt
created during native model initialization may wait for that exact future native
actor prefab, but writes **no** source position while pending. It has no timer,
scene/ID/name search, replay or gameplay write. A pooled stale pointer never
supplies another actor's identity. Once bound, pointer/actor replacement revokes
that original playback generation. Preview/UI models without a native actor
prefab remain unchanged. Emitter position/offset still comes from the actual
model component, never the gameplay actor's location.

`figure-audio-runtime/run-0aciv_05` passes30 production assertions and6 compiled
causal controls in real Unity using original publisher audio producers/runtime.
The fixture now constructs the exact source-bound native sibling topology/order
and checks deferred native actor creation, pooled stale pointers, manager/actor
reassignment, destroyed-before-binding, pool reuse before binding and inert
preview models. The old ancestor-only control fails at the real emitter follow
assertion. Native Choreographer/CharacterManager source hashes and order anchors
are retained; their full gameplay creation callbacks are not executed. Synthetic
stereo waveforms replace the original clip; physical HMD ear balance remains a
hardware limit. All21 original Idle_SFX prefixes were also audited:20 are native
LoopSubitem, one is DoNotLoop, none uses a loop-sequence reauthoring path.

The requested Debug-only forced-pick audio audit now correlates up to12 original
DiscardCard OnLoseCardClick scopes, at most8 detailed native AudioItem requests
per scope plus start/end counts. It logs actual item/source identity and settings;
all audio calls and native exception objects are unchanged. It deliberately does
not attribute later asynchronous audio to a completed callback. The hardware logs
show one native commit per owner operation and no AudioObject/request census.
They also contain two UICardTabSelect fan-close requests around the host's commit
(remote19242/19320), separated by a real Inspect-fan reopen (19261/19263).
That is a concrete different cue/lifecycle, not proof of a doubled character
confirmation. No broad clip deduplication or claimed fix for that unknown was added.

With the audit installed, all6 causal controls still fail as intended in
`run-6lfitnn8`; production reached a metadata-fixture setter error after the30
source-follow assertions. The sole repair replaces field reflection with the
actual native currentMode property setter, in the previously unexecuted audit
suffix. Bounded production rerun `run-wodwh9t_` passes35 assertions, including
exact real AudioController item/source receipt, one playback only, original
exception identity, normal-log no-op and VR-off no-op. The six controls stop
before this repaired suffix and retain identical executed source/oracles. All
processes are reaped and canonical caches cleaned. Earlier fixture compilation,
wrong native reuse-position assumption and topology-factory spelling failures
remain preserved; none was accepted as a causal control or hardware success.
