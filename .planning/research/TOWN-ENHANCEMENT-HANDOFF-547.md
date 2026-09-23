# Actual hand-card offering — build 547

The enchantress now accepts the owner's existing map hand card on the animated
`ActivityOfferingPalm` marker. The old duplicate shop-card collection is removed.
An owned card approaching the palm opens the original service through its existing
guarded destination callback. A deliberate trigger release within 0.28 station
metres selects the matching original `UIEnhanceCardSlot`; selection never purchases.
Native selected-card enhancement hotspots remain adjacent on the workbench.

Ownership is resolved from `MapRoomHand`'s actual card provenance, current character,
current loadout and local multiplayer controller. Native character identity, slot
interactability and model identity are checked before selection and again after the
callback. Native confirmation/cost/transaction guards are unchanged.

The original VRCard remains grabbable on the palm. Reclaiming clears native options;
releasing it elsewhere returns it to the fan, including a closed fan, through the
existing arrival seat and flight. Walking more than 2.25 station metres away also
returns it. Changing character/loadout retires an obsolete offering instead of
inserting it into the new character's hand. Ordinary opening/input fades do not
cancel an offering. A narrow modal exception allows reclaiming only this owned
offering while its live native service is accepting input.

`OffScenarioFanCards` retains the full source population. Only the rendered fan's
adoption skips parked cards, preserving stable identity and letting the existing
`HandFanEnhancementRefresh` update purchased stickers on the actual offered face.
Map retirement preserves a parked or flying card until its owner returns it.

## Multiplayer integration API

`TownServiceRitual.Handoff` exposes `Card`, `NativeSource`, `Face`, `Zone` and
`CloneOf(original)`. The face is the actual VRCard's printed FullAbilityCard;
structural sibling mapping distinguishes same-named native enhancement nodes.
The integrator publishes its original widget appearance/body/pose and the palm
drop zone through town-service mirroring. The fan source count remains unchanged,
so the presence count plus existing named seat map can represent the parked gap.

`TownServiceEnhancementHandoff.Returning` is independent of ritual/window lifetime.
Each `ReturnPresentation` contains the actual `Card`, `Face`, `Body`, captured
`CardId`, `StationRoot`, `Session` and advancing `SessionAge`. No pooled native
widget survives in this record. Publish `face.<CardId>` from its actual FullAbilityCard
root directly, plus its body, until the actual flight/vanish completes. A window
close must retain this stream while records remain; another service may carry them
in its current shared map frame. Map-room/driver teardown clears the records.

The card remains excluded from the arc throughout its return, preventing a static
remote fan copy alongside the flight. The driver temporarily resolves its real
saved fan seat, removes it from the rendered arc, then re-adopts it on completion.
An ownership change during flight transitions into the normal retired-card fade.

## Validation and limits

`scripts/check-town-enhancement-handoff.py` compiles the production handoff and
runs it inside Unity 2021.3.5 at scales 0.05, 1, 2 and 198.12. It checks ownership,
distance, native disablement, callback races/failures, repeated releases, palm
following, reclaim, return, stale selection and opening fades. Negative controls
individually remove ownership, distance, native availability, callback-race,
return, reclaim and fade protections and must fail their targeted assertion.

The fixture uses real Unity transforms and Selectable, with explicit native
controller/model and fan-flight boundaries. It does not claim to run the game or
validate headset visuals. Production compilation additionally binds the actual
GH.Runtime methods and fields. Existing ritual transaction tests exercise native
deferred confirmation ownership. Hardware must still verify palm reach, native
card hotspot use, closed/open fan arrival and multiplayer pose/appearance parity.
