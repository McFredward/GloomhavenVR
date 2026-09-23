# Compact travelling merchant cabinet — build 548

The maintainer rejected build 547's enormous counters and inventory-dependent side returns.
The replacement has one fixed travelling cabinet, with two hand-cranked revolving card trays.
Stock uses the left tray; owned merchandise uses the right. Each shows 4 × 4 original physical
cards. A complete crank turn replaces cards only while the opaque tray back faces the visitor.
This is an animated physical mechanism, with no page buttons, screen navigation, or drawers.

## Geometry and interaction

- Cabinet width is 1.45 m; rounded crank handles extend the combined footprint to approximately
  1.51 m. The root placement audit should use half-width .81 m and depth -.78 .. +.53 m.
- Both trays are .63 m wide and .65 m tall, centred .77 m above the shared floor. The crown
  finishes at 1.15 m. Original card size remains .14 × .112 m maximum; 14 cm vertical spacing
  leaves room for the native price strip. Taking a card retains its original detail inspection.
- The original native coin/ledger work surface remains at .955 m; root integration retains
  the existing decoration/animation contact patch on that desk.
- Furniture never grows with stock. Full supplied-game 161 stock types and 512 distinct owned
  copies remain accessible; the latter require 32 tray positions.
- A grip or laser crank interaction starts one .85 s revolution. All turnover requests are
  blocked while that bank has a held or returning sample. Hidden entries lose canvases,
  body visibility and physical colliders, but preserve the pooled source and item identity.
- Native price, discount, affordability, multiplayer locks, stock ownership and explicit
  buy/sell confirmation remain the original transaction backend. Inspecting never transacts.
- Stock refresh never moves an existing card to another slot. If the last tray disappears,
  its position is retained until the next physical turn, including while a card returns.

The historical `TownServiceMerchantDrawer` and template addresses remain as transport API
names. `Root` is now the crank, `HousingRoot` the rotating opaque tray, and `Content` contains
its native card entries. Existing transform capture must publish both roots and exposed card
poses throughout the turn. `Extensions` is always empty. No wire-format change is required.

## Evidence

`author-town-furniture.py` rebuilds the authored cabinet and legacy inactive return template.
Only merchant FBXs changed; priestess/enchantress geometry is outside this change. A private
Blender render (`/tmp/town548-merchant-preview.png`) checks the cabinet proportions using
untextured card placeholders, not a claim about final Unity materials or headset readability.

Unity runtime suite passed 24,077 production assertions and 15 independent negative controls
(`/tmp/town548-merchant-catalog-final2/run-8tk9a_h8`). These cover original native transactions,
actual physical pickup at four rig scales, all 512 owned copies across complete tray cycles,
no held-card turnover, correct hidden-card hit targets, and swapping only behind the tray back.
The strict Release build also passed with zero warnings/errors before the final rounded-handle
and delayed empty-tray adjustments; integration runs the required final checks again.

Headset checks remain necessary: physical crank reach, readable lower trays, turning-rack
occlusion, original merchant contact animation, close-hand pickup and multiplayer intermediate
motion. The asset preview is not a substitute for these hardware checks.
