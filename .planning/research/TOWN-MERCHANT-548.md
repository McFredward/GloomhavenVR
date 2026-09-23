# Compact travelling merchant cabinet — build 548

The maintainer rejected build 547's enormous counters and inventory-dependent side returns.
The replacement has one fixed travelling cabinet, with two hand-cranked revolving card trays.
Stock uses the left tray; owned merchandise uses the right. Each shows 4 × 4 original physical
cards. A complete crank turn replaces cards only while the opaque tray back faces the visitor.
This is an animated physical mechanism, with no page buttons, screen navigation, or drawers.

## Geometry and interaction

- Cabinet width is 1.45 m; rounded crank handles extend the combined footprint to approximately
  1.51 m. The root placement audit should use half-width .81 m and animated depth -.92 .. +.53 m.
- Both trays are .63 m wide and .65 m tall, centred .77 m above the shared floor. The crown
  finishes at 1.15 m. Original card size remains .14 × .112 m maximum; 14 cm vertical spacing
  leaves room for the native price strip. Taking a card retains its original detail inspection.
- The original native coin/ledger work surface remains at .955 m; root integration retains
  the existing decoration/animation contact patch on that desk.
- Furniture never grows with stock. Full supplied-game 161 stock types and 512 distinct owned
  copies remain accessible; the latter require 32 tray positions.
- A grip or laser crank interaction starts one .85 s revolution. All turnover requests are
  blocked while that bank has a held or returning sample. Hidden entries lose page visibility and physical colliders, but preserve their original
  enabled canvases, pooled source and item identity for bounded hidden prewarming.
- Native price, discount, affordability, multiplayer locks, stock ownership and explicit
  buy/sell confirmation remain the original transaction backend. Inspecting never transacts.
- Stock refresh never moves an existing card to another slot. If the last tray disappears,
  its position is retained until the next physical turn, including while a card returns.

The historical `TownServiceMerchantDrawer` and template addresses remain as transport API
names. `Root` is now the crank, `HousingRoot` the rotating opaque tray, and `Content` contains
its native card entries. `Extensions` is always empty. Native transform capture publishes both
roots and a shared physical card mount, with original face/body/price children.

## Multiplayer mechanical presentation

Additive TLV85 carries an explicit owner turn epoch, elapsed time, lead angle and bounded
current/next/previous page membership; original TLV78 bytes, TLV84 batching and protocol3
remain unchanged. Per-member stamps retain the rack/page, epoch, detached state and original
ancestor alpha independently of the local page-visibility gate. Original widget contents and
material state are still captured through the native snapshot path, with no gameplay callbacks.

The publisher keeps at most three trays warm per rack. The observer presents each page only
when the complete original face/body/price/mount group exists, swaps behind the opaque back,
keeps the actual outgoing page when owner epochs were skipped, and reconstructs the full turn from the explicit clock so a coalesced0-to360 snapshot cannot
alias a stationary rack. Late joiners adopt the received phase. Up to four pending revolutions
are retained in order; dependency waits and catch-up are cosmetic only. A three-second missing
baseline timeout discards obsolete queued waits and retries only the latest owner state on
the normal baseline heartbeat; it never blocks
native transactions or other town windows. Held-card stamps supersede old queued motion,
restore the body page gate immediately, and preserve the new hand pose. Native visibility,
renderer enablement, and independent window/service fades remain authoritative.

Stable trays reuse membership arrays and stamps; runtime body renderer arrays are cached.
The publisher retains native module IDs for the four actual presentation roots of each live
physical catalog entry, even outside the three warm pages. This does not publish hidden pages.
Ownership ends on catalog disposal; destroyed roots and unrelated popups are removed. A new
physical mount receives fresh IDs even when the native pool reuses the same item-card object,
so delayed snapshots cannot target a replacement borrower. Session/reset clears the cache.
The production publication lifetime fixture executes 1,100 page turns over 96 cards: 384 total
IDs, 192 warm modules, no growth from repeat cycles. Its three negative controls cover hidden
source retirement, pooled replacement ID reuse, and retaining an unrelated popup mounted
under a physical card (`/tmp/town548-catalog-lifetime-final2/run-fz0v6sue`, 214,140 assertions).
No additional per-frame log stream is introduced.

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

The follow-up compiled rack-clock suite passes at rig scales .05,1,2 and198.12, with six
independent negative controls (`/tmp/town548-rack-clock-final2/run-e_am3bnp`). It covers
missing last baselines, late joining mid-turn, native card corners moving with their rack,
native ancestor fades, hidden renderer restoration on pickup, manual crank lead after a prior
turn, consecutive queued/reordered turns, and skipped epochs without a front-facing swap.
The full production mirror suite also passes (`/tmp/town548-rack-mirror-full2/run-rf2ifw3e`).
The updated catalog gate passes all16 variants, including stock shrink during a turn
(`/tmp/town548-rack-catalog-final/run-v44rjkco`). Wire golden vectors independently specify
both TLV85 variants, preserve all original TLV78 bytes, and exercise immutable deltas and
unchanged fragmentation. These are source/runtime checks, not headset/network measurements.

Headset checks remain necessary: physical crank reach, readable lower trays, turning-rack
occlusion, original merchant contact animation, close-hand pickup and multiplayer intermediate
motion. The asset preview is not a substitute for these hardware checks.
