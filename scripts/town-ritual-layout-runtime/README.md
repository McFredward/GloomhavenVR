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

The genuine Unity fixture compiles the production folio/purse layout and book ink
projector. It verifies the unchanged original enhancement folio sections, every
native blessing purse (up to seven without overlap), and restores the complete
native window if future/custom stock exceeds that supported bound.

It also reads the original Library book mesh through UnityPy, applies its native
rotation and the production decoration fit, and checks actual glyph corners against
Unity mesh raycasts. Ink sits within 1.5 mm of the curved pages. Remote projection
preserves the owner's second-visitor workspace pose and matches the owner's glyphs;
it cannot relocate text to the observer's NPC. Only read-only reference geometry is
exported into the temporary test directory, never committed or repackaged.

Temple purses are upright physical props above the owned offhand palm. Their native
localized blessing and price remain authoritative. The transaction harness separately
covers affordability, ownership, delayed confirmation, cancel/retry, native multiplayer
stock lag and per-character donation latches. Headset readability, the actual purse
pickup feel and book lighting still require integrated hardware confirmation.
