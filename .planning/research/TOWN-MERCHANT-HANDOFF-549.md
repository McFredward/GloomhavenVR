# Merchant palm handoff and owned-item fan (build 549)

## Behavior and source ownership

Approaching the resident replaces the locally controlled selected character's ability fan
with `CMapCharacter.AllCharacterItems`: equipped and bound items, preserving individual
object identity and including readable nontradeable items. There is no count cap.

The proximity volume uses the actual face rig eye point and optical front, the same
2.4 m enter / 2.9 m exit attention distances, and the same native obstruction mask.
Each owner inside that volume may inspect their cards; another nearby player winning
the shared resident's gaze does not withdraw their hand. The resident's actual final-IK
`ActivityOfferingPalm` determines the zone, never a duplicated authored hand coordinate.

Inspection uses the existing `ItemsPile.ItemChip` native face/backing, card grip and
hand-to-hand transfer, fan shape, sweep arbitration, laser pluck, emergence and return
animations. The host does not publish scenario `ItemsPile.Current` or `RecessOwner`,
does not invent a scenario actor, and never invokes an item-use gameplay route.
Closing fan surfaces remain published through their complete return; held cards survive
wrist closure. The map fan resumes on leave, owner switch, option/context loss or reset.

Approach and pickup never open a native service or transact. Only a valid release into
the actual palm requests the original merchant window. The pending request waits for its
inventory to be bound to the same character and session, then invokes the existing native
transaction adapter to open the ordinary explicit confirmation. Inventory eligibility is
revalidated on release and on native dispatch. Leaving cancels only the exact confirmation
callback this handoff created. Other confirmations remain untouched.

## Integration hooks

`TownServiceMerchantHandoff.Tick()` after resident population preparation, `LateTick()`
after activity IK, and `Reset()` before population teardown. The catalog's three release
callbacks are assigned only for a live local inspection. Its `HeldOfferAvailable` controls
the buy mark; actual owned ItemChips control the sell mark. The town source publisher reads
`Active`, `Session`, `SessionAge`, `StationRoot`, `Zone` and `OwnedChips`. `NativeItemCard`,
`InspectionMount` and `InspectionBody` are the original visible outputs, not recreated UI.

The inspection backing template factory calls the same original ItemChip backing builder;
its caller must use the source item's measured face width/height to preserve geometry.

## Verification and boundaries

- `scripts/check-town-merchant-handoff.py`: 87 real Unity runtime assertions and six
  negative controls. Final evidence: `/tmp/town549-handoff-attention/run-j6whdwxr`.
- Production controller, inspection lifecycle and map ownership/suspension code are compiled
  directly. The actual attention method is compiled unchanged against Unity camera/physics.
- Tested 31 equipped/bound card copies including repeated IDs, held-card persistence,
  closing-source retention, exact sale identity, delayed/mismatched native inventory,
  purchase vs sale, authority refusal, leave/character/reset, unrelated confirmation,
  front/distance/hysteresis/occlusion, .05 / 1 / 198.12 scale and 100x glove armature.
- Six mutations must fail: bypass ownership, bypass palm, cap inventory, dispatch into
  another character's inventory, open native service on approach, drop closing sources.
- Native game models/transaction adapter and the established ItemChip rendering/grabbing
  are explicit boundaries in this focused controller suite. The strict complete plugin
  build verifies actual integration types; it does not prove headset appearance or native
  multiplayer callbacks. Cards lane validates the original confirmation adapter and wire.
- Ordinary logging adds no per-frame or per-card stream. Inventory snapshots poll at 5 Hz;
  overlay eligibility caches for 120 ms, with uncached checks at the actual release.
