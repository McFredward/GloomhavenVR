# Unconfirmed card selection: remote publication and application

Run `python3 scripts/check-card-selection-remote-runtime.py` locally. This suite
requires the game's executable `UnityEngine.CoreModule.dll`, just like the golden
wire vectors. It deliberately does not run against hosted metadata-only references.

The fixture compiles the complete unchanged production wire sources selected by
the central wire project. It also compiles exact source excerpts for:

- `PlayTray.SetWantedSlots`, `WantedSlotMask`, `OccupiedSlotMask` and
  `SlotHoldsCard`;
- `NetAvatarDriver.SampleBoardUi` and its real record-4 send assignments;
- physical `LocalBoardSlots` card/widget resolution and slot-order sampling,
  followed by the existing record-18 send assignments;
- the matching `RemoteAvatar` board, fan and slot-order receive assignments;
- `RemoteControlBoard.OrderRoundCards`;
- `RemoteBoardFurniture.TickWire`'s wanted/snap decisions and its complete
  `SetWanted`/`SetSnap` methods;
- `RevealGate.ShowPeerCardFronts` and the linked production face policy.

Only method accessibility and the source sampler's active-hand acquisition are
adapted. The latter is an explicit hand argument from the fixture. Logger calls
are sinks. Native hierarchy objects, card/widget/model identities and time are
explicit portable boundaries; original production code decides occupancy, record
contents, ordering and activation. An activation write is not a pixel measurement.
This suite does not claim native card selection callbacks, real packet timing,
actual disconnection teardown, Unity rendering, headset/network execution or
gameplay continuation. The separate native selection fixture covers the native
contract, and unchanged existing rendering suites retain their own earlier scope.

Coverage includes all 48 wanted/occupied/snap combinations, held-before-reparent
withdrawal, reinsert/replacement, eight delayed-native-model frames, fan insertion
and reorder, focus away/back, explicit empty state, a dropped first packet followed
by a full snapshot, fresh receiver/board reconstruction, held rig pose and release,
and twelve swaps with both recesses occupied. The swap test samples actual fixture
widget identities through production `LocalBoardSlots`, sends existing record 18,
then checks production `OrderRoundCards` against the owner's physical pair before
and after independently changed native initiative. No card identity is added to
the wire. The source face gate stays closed remotely during selection and the
production local-ownership policy stays open for the owner's own cards.

Causal controls break the owner wanted/occupancy read, copied packet overlay byte,
receiver mask adoption, remote owner-mask authority, overlay reopening, held-card
occupancy exclusion, cleared occupancy adoption, swap publication/application and
remote selection-face ownership. Compilation errors are failures, never passing
controls. Every stopped run calls the canonical retention API, including failures.

## Source audit of the unchanged wider path

The reported log failure was local: a null Guildmaster character display name
interrupted takeback before native deselection was queued. The production repair
belongs to the driver; this worker found no separate network schema defect.

`TickExtrasSend` samples the physical board before its rate gate. Wanted/occupancy
changes and slot-order changes preempt the ordinary extras interval. Every ordinary
snapshot repeats those facts while its source exists; an all-zero mask still has
the occupancy validity bit. The receive path adopts masks without a viewer dial.
`RemoteControlBoard.Tick` calls `OrderRoundCards` and `SeatSlots` every frame.
`SeatSlots` takes a valid owner occupancy nibble before the lagging round-card
model; its empty-recess branch clears that card and its former face latch. This
prevents delayed native replication from recreating a withdrawn remote card.
Those outer cadence/seating bindings are checked against source; the fixture does
not execute their complete update loops.

Both-occupied swaps do not rely on a changed occupancy mask. Existing record 18
states which physical recess contains the replicated initiative card. The receiver
orders the replicated pair by initiative and then applies that bit. Separately,
the fan's actual count and record-44 seat permutation travel in the same extras
snapshot; `fanPresentationChanged` also preempts the cadence. The fan resolves the
displayed actor, validates list lengths/permutations and clears its order latch on
close or changed membership. Rig held-card pose/state travels independently and
is sampled from the actual held object, retaining its original actor across focus.

Original native decoration uses `CardAppearanceSampler`'s call to
`CardsDriver.CopyVisibleCards` and its source provenance, sampled independently of
the wanted/occupancy nibble.
`TickCardAppearanceSend` sends changed originals and periodic complete refreshes.
`RevealGate.CardFaces` gates the remote fan/held/recess surfaces with the same
selection rule; local controlled cards do not acquire remote concealment.
`ForgetPeer`/`RemoteAvatar.Destroy` dispose the fan, board and held artwork, and
board teardown disposes furniture. A new session resets the sent board, order,
focus and fan presentation latches. These lifecycle and full-artwork findings are
source review, not a newly executed multi-client/renderer proof.
