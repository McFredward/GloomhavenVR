# Persistent merchant cabinet and original item publication (build 549)

The public cabinet is a resident prop, independent of entering or closing the native shop.
`TownServiceMerchantRows` reads native `ShopService` stock without invoking `EnterShop`.
Native shop entry is deferred until a deliberate eligible release into the offered palm;
the original confirmation remains pending for a real player click. The hidden permission
context remains owned by its `TownServiceWindowMask`, never republished as a stock window.
Native controllers and callbacks are never installed on observer replicas.

The authored cabinet has one twelve-card cassette (four columns, three rows), six original
slot-category pictograms and a physical crank. A page transition withdraws the cassette,
closes the opaque bifold shutter, changes the page behind it, opens the shutter, and extends
the new tray. Owner and observer use the same 0.85-second `TownCassetteMotion` curve.
Category changes preserve this mechanism; there is no second selling rack or mode selector.
The crank collider derives from the actual authored handle mesh bounds.

Stock inspection uses the scenario item reading and grip solver. The existing
`CardsDriver.UpdateHeldCardTransfer` detector handles stock, scenario items and abilities,
including contact reach, modal/nearer-UI precedence, hysteresis and haptics. A refused
receiving hand attempts adoption back into the original hand before any safe return.
The owned item fan uses real `ItemsPile.ItemChip` instances through its inspection host.

## Independent multiplayer lifetimes

The existing private service lane remains available for owned item fans, palm targets and
native confirmation widgets. A second public lane carries the resident cabinet independently
of character selection, native shop lifetime or visits to the other two residents. Only its
current presentation author publishes visible cabinet modules. Empty local follower manifests
establish eligible participants without adding idle visitors or duplicate furniture.
A physical cabinet interaction promotes a new presentation author; native transaction
ownership remains independently checked. Inactive clients cannot block a live author.
Handoff adopts the currently displayed analytic clock rather than a stale packet phase.

Historical TLV78/84/85 layouts are unchanged. Additive TLV86 contains a discriminator,
mechanism/public-lane flags and the presentation claim ordinal. Private/public fragment
streams reserve independent bit-16 sequence namespaces while keeping historical low-16
module IDs and the existing global datagram budget. Both lanes request fresh baselines on
network refresh and retire on peer removal.

Owned item fans publish their actual original face and backing once, through the town lane.
The generic avatar held-card pose/shape route explicitly excludes these inspection chips.
Backing template identities carry exact face dimensions and prefab/procedural style; the
receiver invokes the same original ItemChip backing builder. Original contours retain late
silhouette completion. Opening or closing the native purchase context does not restart the
owned fan's publication lifetime.

## Validation boundaries

- Strict Release compilation: zero warnings and errors.
- Wire harness: 286,103 assertions, including an independent TLV86 golden vector,
  unchanged old bytes, invalid duplicate records, compressed/uncompressed interleaving
  of identical module IDs across both lanes, and opaque exchange timing.
- Unity mirror full suite: 230,553 assertions with presentation-corruption negatives.
- Unity original rack-clock suite: 15,936 assertions and seven causal/timing negatives.
- Unity catalog lifetime: 214,140 assertions and three identity-retention negatives.
- Unity public-catalog suite: private/public coexistence, author promotion/removal,
  independent native lifetime, exact closed-shop publication of 512 owned item surfaces,
  late cassette replay and clock-preserving handoff, with fault-injected negatives.
- Unity shared transfer detector: 100 assertions across four rig scales and all three
  transferable card kinds; five negatives cover dropped stock support, contact semantics,
  repeated haptics, nearer UI and modal arbitration. Tracked inputs and final adoption
  sinks are fixture boundaries; the detector and reach are production code.

These checks establish source behavior, transport and Unity rendering/lifecycle behavior.
They do not certify headset comfort, actual prop proportions, user-facing lighting or
network latency. The integrated asset build and hardware test remain the visual evidence.
