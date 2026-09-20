# Native town services: functional coverage for an immersive VR design

Research at `dev` `ff59a14e`, ModBuild 537, 2026-09-20. No runtime change.
Source references below are relative to the main checkout's read-only `decompiled/`.
`GH.Runtime/` is abbreviated **GH**; other prefixes are written in full. Line numbers
refer to the supplied decompilation, not upstream source. This is a design coverage
inventory, not evidence of implemented or headset-tested interactions.

## Names and evidence limits

- The native controllers call the three services `Merchant`, `Temple`, and `Enchantress`.
  The primary agent visually verified **MAGIERIN** on the supplied German screenshot
  `.planning/debug/magierin.jpg`, and **Tempel der Großen Eiche** in the church text.
  Use those verified names when addressing the user. Do not identify the Magierin as
  a named story character without further evidence.
- Portrait sprites are serialized prefab references. The reviewed C# contains no
  reliable portrait texture name or exportable image. This report does not claim to
  have extracted images or verified English displayed titles from localization data.
- The temple uses data-driven blessing definitions. Actual available types, prices,
  quantities, durations and donation rewards must be read from the running game or
  original data, never reconstructed from tabletop assumptions.

## Common presentation and transaction contract

The three spaces can replace the service's flat composition with a counter, altar,
or workbench. Existing game cards, descriptions, numeric labels, symbols, stock,
ownership and native transaction results remain authoritative. Keep these original
widgets on tangible objects or readable labels instead of transcribing their content.

Selecting or placing an object creates a preview. A deliberate confirm gesture on a
clearly labeled seal confirms the same native action as the flat confirmation box;
cancel, returning the object, and closing the service remain available. No purchases
occur merely because a hand passes through a volume. Animation acknowledges the
result and must not become a prerequisite for the native callback or continuation.
Confirm and cancel must also work with laser input and limited physical reach.

Choose the acting character with the existing character selection, also represented
on the station by a portrait/name token. Gold source and affected character remain
visible at confirmation. In multiplayer, browsing and purchases must preserve native
permissions, current stock validation, join locks, and server-authoritative actions.
Remote observers see the owner's preview, hand movement, object state and NPC
animation. Flat peers keep the native flow. A decorative NPC cannot own a transaction
or block its completion. Interaction ownership must not permanently lock an entire
shop for other players; independent browsing should remain possible.

## Merchant coverage

| Native function / constraint | Evidence | Tangible VR representation |
|---|---|---|
| Buy and sell modes; selected character refresh; party gold versus character gold | GH/UIShopItemWindow.cs:69, GH/UIShopItemInventory.cs:649, GH/ShopService.cs:25 | Merchant counter with buy display and clearly separate sell tray. Character token and correct purse balance stay visible. |
| Full catalog, slot categories, Owned view where native input mode provides it; empty states | GH/ItemListingType.cs:1, GH/UIShopItemInventory.cs:802, :979, :482 | Indexed drawers/rack sections for head, body, hands, legs, small items, all and owned. Page or scroll through the full list; do not instantiate every stock item in the room. |
| Alphabetic ordering by localized name, grouped duplicate types, available/total quantities, zero-stock entries from party-owned stock | GH/UIShopItemInventory.cs:654, :692, :705 | One inspectable representative per native listing with quantity marker; native ordering is stable through paging and remote presentation. |
| Item description, rules, hover hints, new flags; bound character and equipped status | GH/UIShopItemInventory.cs:911, :936; GH/UIShopItemSlot.cs:541; GH/ShopService.cs:156 | Pick up an item card/display token for the original enlarged description; character badge and equipped marker stay attached. Both laser hover and hand inspection reveal the same information. |
| Discounted purchase price, exact sell price, affordability | GH/ShopService.cs:25, :42, :72, :183 | Price label and confirmation receipt show the native final price, discount and resulting payer. No bargaining mechanic or additional pricing rules. |
| Buy confirmation and cancel, with recipient; sale confirmation and cancel, with owner | GH/UIShopItemInventory.cs:1067, :1096 | Place the inspected item in the buy or sell tray, inspect the receipt, then press a purchase/sale seal. Returning it cancels without changing state. |
| Buy removes actual merchant stock, adds/binds item and can equip through the native route | GH/ShopService.cs:47; GH/UIShopItemInventory.cs:1166 | After native success, the purchased card moves to the character's inventory. Preserve native auto-equipment behavior rather than inventing unconditional direct equipment. |
| Existing adjacent equipment controls remain accessible; changes refresh shop markers | GH/UIShopItemWindow.cs:93, :122, :185 | The existing character equipment board can remain beside the counter; a later equipment dressing interaction must call its own original handlers. Shop research alone does not establish every equipment operation. |
| Quest items excluded; character item restrictions, tradeability, controller ownership | GH/ShopService.cs:104, :124; GH/UIShopItemSlot.cs:429 | Restricted items remain exactly as inspectable/disabled as native; explanatory labels carry the reason. Cannot sell another player's character-bound item by grabbing it. |
| Spectators cannot buy/sell; multiplayer host validates gold and exact stock item; join locks | GH/UIShopItemInventory.cs:1067, :1096, :1287, :1407; GH/UIShopItemWindow.cs:211 | Show pending/denied native result on the receipt. Do not hand over an authoritative item until acceptance. A cosmetic preview is not a stock reservation. |
| Unlocks, new-stock marker, first-visit introduction, FTUE, personal-quest checks at exit | GH/MerchantMode.cs:7, :32, :37; GH/UIShopItemWindow.cs:69, :110, :161 | Merchant greets on the native first visit. Original introduction text remains readable; dismissing it drives the existing continuation. Leaving the counter executes native exit even if the farewell animation is interrupted. |

## Temple coverage

| Native function / constraint | Evidence | Tangible VR representation |
|---|---|---|
| All available blessing definitions, selected recipient, exact prices | GH/UITempleWindow.cs:103; GH/TempleShopService.cs:138 | Altar with one offering plaque per native blessing and selected character token. Paginate if necessary; do not hardcode a single donation option. |
| Original blessing icon and tooltip quantity, unavailable explanation | GH/UITempleShopSlot.cs:126; GH/UITempleSlotTooltip.cs:50 | Inspectable blessing sigil with original description and modifier-card/count preview. Preserve native duration semantics in explanatory content. |
| Duplicate-condition eligibility, gold affordability, party/character gold | GH/TempleShopService.cs:59, :82, :143 | Each offering shows its actual availability and price. An already received blessing is not repurchased via a new animation. |
| Ownership and join validation | GH/TempleShopService.cs:143; GH/UITempleWindow.cs:231 | Only the controlling player confirms for that character. Others can observe the public ritual and resulting state. |
| Explicit confirmation/cancel including character, gold cost and blessing | GH/UITempleWindow.cs:155 | Put the selected offering token and a visual purse on the altar, then press a labeled donation seal. The purse depicts the exact native cost; no individual coin counting or arbitrary donation amount is required. |
| Donation applies native condition quantity/duration, pays gold, updates personal quest and save | GH/TempleShopService.cs:123; MapRuleLibrary/MapRuleLibrary.MapState/CTempleState.cs:88 | After acceptance, priest blesses the character token and modifier symbols appear; all effects reflect native state. Do not create new buffs or change persistence. |
| Total donated gold, devotion level and progress including level-crossing animation | GH/UITempleWindow.cs:103, :204; GH/TempleShopService.cs:19, :23, :39 | Small readable devotion ledger with native values, complemented by a light travelling around an altar ring. The ring never replaces numeric progress. |
| Devotion threshold rewards and their original notification/continuation | MapRuleLibrary/MapRuleLibrary.MapState/CTempleState.cs:124 | Priest presents the original reward notification as an inspectable parchment with an explicit acknowledgment. Crossing several thresholds retains every native reward. |
| Unlock, introduction, exit and personal-quest continuation | GH/TempleMode.cs:7, :35; GH/UITempleWindow.cs:117, :136 | Native unlock controls whether the shrine is usable. Introduction parchment and leave action remain immediately dismissible. |

No arbitrary donation slider, refund ritual or healing service is established by this
source review. These would add gameplay and are not part of the concept.

## Magierin / enhancement coverage

| Native function / constraint | Evidence | Tangible VR representation |
|---|---|---|
| Character selection and every owned ability card, including cards outside the active hand | GH/UINewEnhancementWindow.cs:219; GH/UIPartyCharacterEnhancementAbilityCardsDisplay.cs:83 | Personal indexed card case at the workbench. Lay any native eligible owned card on a central reading mat; keep its original artwork and text. |
| Card selection/inspection; top/bottom ability lines, summon rows, area hexes, legal empty or occupied slots | GH/UINewEnhancementWindow.cs:338, :361, :396; GH/MapPartyEnhancementShopService.cs:91 | Touch a highlighted native enhancement location/row on the enlarged card. Valid rune tokens appear in a nearby tray; no free placement onto illegal points. |
| Legal compatible enhancements filtered by unlocked shop stock | GH/MapPartyEnhancementShopService.cs:125 | Tray contains the exact native compatible rune set. Locked rune types remain absent/locked in line with the native presentation. |
| Hover preview, revert preview on change, original affected card symbols | GH/UINewEnhancementWindow.cs:585, :598; GH/MapPartyEnhancementShopService.cs:91, :120 | Bring a rune near a valid card point to preview it. Remove it to revert. Preview never writes the authoritative enhancement list. |
| Price total and detailed calculation: base, multitarget factor, previous enhancements, card level | GH/EnhancementBuyPriceCalculator.cs:25, :56 | A quotation strip next to the mat shows the native total and expandable breakdown. Avoid a hardcoded price label on a reusable rune. |
| Enhancement capacity; campaign counts distinct enhanced cards while Guildmaster counts individual enhancements | GH/CMapCharacterService.cs:340; GH/EnhancementBuyPriceCalculator.cs:71; GH/UIPartyCharacterEnhancementAbilityCardsDisplay.cs:191 | Capacity tokens beside the selected character reproduce the native rule and counter. A campaign card already enhanced may accept another enhancement without consuming another card-capacity slot. |
| Buy confirmation/cancel; insufficient gold or points; no empty slot; join locks | GH/UINewEnhancementWindow.cs:488 | Preview rune snaps gently onto the mat, then the player deliberately presses a labeled engraving seal. Show gold and capacity costs before acceptance. Handing the rune to the NPC alone cannot spend money. |
| Sell/remove enhancement only where native configuration permits it; refund from paid price and mode-specific percentage | GH/MapPartyEnhancementShopService.cs:18, :34, :131; GH/CMapCharacterService.cs:319; GH/UINewEnhancementWindow.cs:550 | Separate removal tray/tool with native refund quote. Confirm before removing a rune. Never present a universal reset or promise a full refund. |
| Native sell and buy modes, selection retained where possible, fully enhanced state | GH/UINewEnhancementWindow.cs:352, :460, :624, :642 | Use labeled engraving/removal tools or a two-position selector; show only operations the current mode permits. Unavailable operation stays unavailable. |
| Ownership changes, native host validation and exact enhancement identity | GH/UINewEnhancementShopSlot.cs:202; GH/UINewEnhancementWindow.cs:722, :727, :777 | Only the owner's hand/laser can confirm. Observer previews follow owner presentation; ownership loss cancels pending local gestures and rechecks permissions. |
| First introduction, new-stock acknowledgment, unlock and personal-quest checks at exit | GH/UINewEnhancementWindow.cs:187, :477; GH/MapPartyEnhancementShopService.cs:25; GH/EnchantressMode.cs:7, :40 | Magierin introduces her service with original text, then performs a short inscription animation on accepted actions. Exit executes native flow immediately and releases the presentation safely. |

## Implementation implications for the eventual feature

1. Preserve public service availability from the native mode/quest state. Existing
   map scenes and Guildmaster both use these services; do not use the environment
   selector as a gameplay unlock, or infer availability merely from an NPC being visible.
2. Build NPC gesture/face rigs and low-cost idle loops independently from gameplay.
   Idle, greeting, look-at, inspect, successful exchange/blessing/inscription, refusal,
   and farewell can be cosmetic states. No animation event may be the sole call to
   commit, cancel or acknowledge a native modal.
3. Reuse current map-window lifecycle, original transaction confirmations and source
   controllers initially, translating VR gestures to their existing entry points.
   Calling lower-level services directly would bypass UI, networking and validation.
4. Test campaign/Guildmaster, party/character gold, each enhancement mode, owned versus
   unowned character, observers/flat peers, simultaneous last-stock purchase, ownership
   changes, joining peer, cancel/close at every preview/confirmation, devotion threshold
   rewards, native introductions, and repeated entry. This coverage is required before
   replacing the current flat composition for a player build.

Native source inventories do not guarantee prefab completeness. A later runtime
hierarchy inspection must account for serialized header/help text, tooltips, navigation
tabs and any expansion/mod-added definitions before declaring full feature parity.
