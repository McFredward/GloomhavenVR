# Full native ritual stock geometry

Run `python3 scripts/check-town-ritual-layout.py` with the read-only original
`ressources/GH_Data` available. It reads all 15 base/DLC rule archives and calls the
shipped assembly's pure `CAbility.GetValidEnhancements` query. No game objects,
gameplay actions, native windows or save data are involved in that census.

The shipped data contains at most 31 unique ability cards per class, two numeric
enhancement slots on one ability/line and two area enhancement hexes. The native
option query returns at most 14 options across all 1,378 enum combinations with
empty existing-condition sets. Native `GetEnhancementLines` groups by both ability
object and line: multiple attacks/moves on a card do not add into one shop filter.
Mouse mode can therefore show 28 options. Gamepad deduplicates buy types and can
append at most two sell slots, remaining below that bound. Both shipped temple
definitions have one active blessing; commented-out definitions are not counted.

The genuine Unity fixture compiles `TownServiceRitualLayout` and checks complete
rotated faces with 3 mm physical thickness, worktop support, parallel-face clearance,
book/selected-card/rear-prop clearance and apron-mounted modes. It covers all native
counts plus the helper's larger 64-card/48-rune/7-offering budgets. Future/custom
overflow throws for complete native-window fallback; it never omits a stock entry.

The helper's return values are relative to Ritual's `(0,.978,-.08)` mount. The
integrator must apply all three fields: position, rotation and face size. Native
coins retain their existing child rotation of -90 degrees, so the offering's
90-degree root rotation lays the actual coin flat.

The diagnostic PNG uses simple coloured face volumes and reserved book geometry,
not finished game artwork. It proves spatial packing; hardware grab accessibility
and final native inscription legibility still require the integrated render/test.
