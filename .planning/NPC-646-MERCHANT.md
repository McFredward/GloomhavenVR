# NPC646 merchant cabinet captions

## Request and scope

The October8 Build645 paired test asks to remove the repeated title/item mini-icon
beneath each physical stock card and make the price, native reputation triangle
and available quantity centered and readable. No new screenshot/video accompanies
this report. The primary agent froze the current host/peer logs and verified both
Build645 banners; this lane does not claim a headset picture from those logs.

Only the cabinet's original `UIShopItemSlot` presentation clone changes.
`TownServiceCatalog.Entry` applies `TownServiceMerchantCaption` after the existing
mirror update and before original presentation capture, including bank preparation.
The original backend row, item card, native economic text, sprites, affordability
colours, discount direction, quantity and active warning state remain unchanged.
Native controllers/callbacks are not added to observer clones. There is no new
caption widget, economic calculation, transaction callback, protocol or bundle.

The shared serialized Content branch contains the duplicated title, mini-icon and
its name/icon shine/warning copies; only the presentation clone hides it. Original
price, warning price, gold images, quantity and reputation triangle retain their
native assets/data. Their rectangles form a centered single-line group; 36px text
replaces20px text (7.5mm rather than4.17mm em at the cabinet's settled0.5/2400
meters/pixel fit). The strip mount moves from-.053m to-.074m so it clears the card's
lower edge. Clone/node/TMP references are cached per mirror rebuild; steady writes
are change-gated. Source refresh cannot permanently restore the duplicate title
or shrink the displayed text because the exact final clone pass reapplies layout.

## Campaign/Guildmaster provenance

The original level4 `UIGuildmasterHUD` Mono10746 references `UIShopItemWindow`
Mono10759; its item inventory is Mono13424, whose slot prefab is
`sharedassets4.assets` Mono1484/GameObject330. The exported prefab has27 nodes and
6 serialized materials, including actual gold and reputation sprites. The field
PPtrs independently resolve to the helper's native paths. Level4 Mono14065 is the
unrelated party item inventory and points to another prefab; it is not a second
merchant-mode variant.

Both map modes route Merchant through `GuildmasterDestinations.ModeWindow` to
that same HUD `shopWindow`. `NativeTemplates.Initialize` uses its same inventory
and slot prefab; `TownServiceMerchantRows.Create` instantiates that exact prefab
without a mode-specific selection. The exported receipt records the serialized
HUD/shop/inventory/slot chain and native field paths, with source hashes. This is
source/serialized provenance, not a newly executed Campaign/Guildmaster gameplay
or tutorial acceptance test.

## Bounded validation

Final actual Unity2021.3.5/llvmpipe proof:
`merchant-caption646/run-uej1x2ft` — **4889 assertions**, production plus all3 causal
controls pass. The proof imports the original27-node hierarchy, rects, colours,
shared material values and actual sprites; it uses an equivalent editor TMP font
atlas, so it is not an exact game-font screenshot comparison.

It runs96 price/quantity/discount/affordability combinations through the real
caption helper and exact source-bound `Entry.SuppressNativeBacking` method, then
through production capture, retention, codec and observer binding. Each economic
state is checked locally and remotely (192 presentations). Price-warning visibility
also alternates. All native intermediate rectangles, text, affordability colour,
gold reference and discount-arrow scale/visibility agree;4 clone rebuilds repeat
those checks. The existing source-to-clone mirror/lookup is an explicit fixture
port, implemented using production property capture/binding. Native shop economic
initialization is a declared boundary; the fixture supplies its output values and
does not claim to execute a purchase.

The three controls restore20px text, restore the duplicated name/icon, or omit the
actual final Entry application. Each fails its intended requirement. Native
quantity/price glyph counts and preferred widths prevent silent clipping/shrinking.
The final readbacks have1161 white and1973 gold pixels, with26/460800 pixels differing
between local and observer at the fixed comparison threshold. The worker personally
viewed before/local/observer readbacks; the integrator independently viewed them.

Existing catalog focused production plus `proud-card-glass` control:
`town-service-catalog/run-8sgj69kf` — **23974 assertions**, both variants pass. This
retains the broader original catalog, physical pickup, page/layout, cancellation,
native-bank preparation and transaction checks; its fixture has no real caption
hierarchy and is not a visual caption proof. The binder adds the actual helper;
existing assertions, deadlines, boundary project and mutants are untouched.

Strict Release on the final source:0 warnings/errors. The integrator owns combined
checks and Build646 stamping. No new complete172-suite or HMD/WAN pass is claimed.

## Rejected fixture hypotheses and retained failures

The initial extracted nested Entry method used the older four-space extraction
seam and accidentally included later methods; the compile failed. The bounded
extractor now consumes the original eight-space method ending exactly.

The reusable native-row loader originally manufactured separate unnamed Sprite
objects for repeated serialized gold PPtrs; the asset bank correctly rejected their
ambiguous provenance. The caption loader now retains one actual Sprite per exported
native pointer/image. A subsequent adapter used mutable capture arrays directly;
applying a later sample against the same array hid changes. It now retains the
original property frame, matching the real transport's ownership contract.

The first all-green matrix reported identical readbacks that were actually blank:
the camera excluded the enclosing canvas ancestor. Personal image inspection caught
that false green. The corrected camera includes the full canvas ancestor and the
final proof requires visible ink, white quantity glyphs and gold price glyphs before
pixel parity can pass. Failed logs, generated sources and the rejected blank images
are retained as evidence; their green pixel equality is not inherited.
