# Immersive town services: environment and multiplayer integration

Research and design proposal, 2026-09-20. Source baseline: `dev` `ff59a14e`,
ModBuild 537. No runtime behavior is changed by this document. The user requests
concepts and original portrait extraction first; asset production and implementation
remain subsequent work. Numerical art/reach targets below are proposals for prototype
validation, not measured hardware outcomes.

## Verified current foundations

| Subject | Source fact and location |
| --- | --- |
| Service identity | Native modes are `Merchant`, `Temple`, and `Enchantress`; their windows resolve from the HUD's serialized references, not names: `src/GloomhavenVR/WorldUI/MapRoom/GuildmasterDestinations.cs:691`. The current German enhancement-window caption documented from hardware is `MAGIERIN`: `src/GloomhavenVR/WorldUI/Composites/EnchantressComposite.cs:18`. |
| Environment choices | `Default`, `Cellar`, `SwampNight`, `OffBlack`: `src/GloomhavenVR/Core/Environment/SkyAlternative.cs:33`. Player labels are Standard/Default, Keller/Cellar, Nachtwald/Night forest, Aus (schwarz)/Off (black): `src/GloomhavenVR/Core/Loc/Loc.cs:970`. |
| Map environments | Both bundled rooms also surround the **3D map**, through `TableInFrontOfPlayer`: `src/GloomhavenVR/Core/Environment/SkyAlternative.cs:846`. Older scenario-only comments in defaults are stale. Main menu and the optional flat 2D map do not enter this room path. |
| Geometry | The bundled cellar is a candlelit stone room; the night forest is a moonlit clearing. Generators: `unity/GloomhavenVR.Assets/Assets/Editor/BuildEnvironmentRooms.cs`; authored clear-space diameters 6.5 and 9.0 are at lines 55–56. These are asset coordinates, **not** fixed physical room sizes. |
| Runtime anchoring | `SkyAlternative.cs:1612` sizes the clear space to 4.5 times the measured board extent. `:1622` uses measured Guildmaster furniture floor where available. Map subject is parchment bounds and yaw, never the local viewer's gaze: `:1780`. |
| Map scale and reach | `src/GloomhavenVR/WorldUI/MapRoom/MapRoomSeat.cs:39` targets 1.20 m map width; tabletop 0.78 m at `:55`, player edge standoff 0.45 m at `:63`. Game coordinates must be converted through the solved seat scale. |
| Guildmaster furniture | Native bench/barrel/table geometry supplies the floor: `src/GloomhavenVR/WorldUI/MapRoom/GuildmasterRoomGeometry.cs:16`. The right-hand button rail measures table and knife clearance at `:46`; map-switch buttons form their own vertical pair: `GuildmasterRoomLayout.cs:18`. |
| Existing service sharing | `src/GloomhavenVR/WorldUI/Modal/SharedWindows.cs:96` and `:230` list story, quest, encounter, video, reward kinds. Merchant/temple/enchantress windows currently fall through to `None`. They are **not already shared service workstations**. |
| Enhancement card selection | `UINewEnhancementWindow.CardsDisplay` owns the live list; its original transform is parked into the enchantress window and restored: `src/GloomhavenVR/WorldUI/Composites/EnchantressComposite.cs:25`. It must not be recreated from a guessed character-panel reference. |
| Service closure | `src/GloomhavenVR/WorldUI/Modal/ModalFallback.7.Close.cs:123` checks mandatory decisions, then `:168` calls `GuildmasterDestinations.LeaveMode`. Hiding a mesh or UIWindow alone does not exit the native mode or restore character selection. `GuildmasterDestinations.cs:1323` is the common close route. |
| Native transactions | Native buy/bless/enhance commands retain validation and replication. References: `decompiled/GH.Runtime/UIShopItemWindow.cs:214`, `UITempleWindow.cs:186`, `UINewEnhancementWindow.cs:488`. These read-only game sources live in the main checkout. |

The latest AGENTS.md rulings additionally require: MR backings only behind UI,
never in the play area; native gameplay ownership remains authoritative; complete
owner-to-peer visual parity includes intermediate motion, effects, text, geometry,
order and hover. A local environment choice does not authorize hiding service
content from observers.

## Recommended spatial concept

Keep the existing map as the meeting point. Add three recognizable service stations
at its perimeter, with life-size NPCs behind compact working surfaces. Selecting an
existing town button or pointing at the corresponding station opens exactly the
native service permitted by the game. The map remains available as the shared
orientation anchor. Do not move the player automatically on entry.

Separate **where a service lives** from **where a player can reach its controls**.
The station is a stable room feature, while each visiting player gets a physically
reachable transaction tray associated with that station and their controlled
character. A tray is a small piece of furniture carrying the actual item/card/token
interaction, not another flat monitor. It has a visible owner/character marker.
This supports seated play and simultaneous visitors without requiring anyone to
walk across their real room or contend for a single central purchase button.

For proximity play, a visitor can use the station counter itself. At a distance,
pointing brings the tray into a free space near the player's table edge with a
short visible motion. Its owner-authored pose is shared; other peers see the same
tray in the same world position. Manual repositioning wins over automatic layout.
The station and tray are world-anchored after placement, never periodically pulled
back into the player's gaze. A new tray may move older visibly overlapping UI only
under the established opening-time placement rule.

### Room-specific placement

| Presentation | Integration proposal |
| --- | --- |
| Cellar | Merchant: wooden trade counter and supply drawers; temple: a restrained candlelit donation stand; enchantress: a rune-working bench with card cradle. Put the three against **measured free perimeter sectors**, preserving existing shelves, window/door scenery and the clear approach to the map. Reuse current materials and lighting style without replacing room architecture. |
| Night forest | The same merchant at a portable supply table, temple attendant beside a small travel shrine, enchantress at a folding workbench. No new building or large landscape is required. Match ground contact to the actual clearing and keep trees/undergrowth out of the hand workspace. |
| Default game surroundings | Freestanding compact versions anchored to the map frame; no assumption that a cellar wall exists. Test against both native map scenes. |
| Off (black) | NPC plus functional furniture only, with legible material lighting. Respect the requested absence of scenery; do not silently load a full room. |
| Mixed Reality | Compact NPC/furniture silhouettes and transaction trays in passthrough, no enclosing walls, sky or giant rectangle. Backings may support readable UI cards/labels only. Never add a backing under a character, water, terrain or map interior. Match existing map table support and floor rules. |
| Optional flat 2D map | Retain the working native service windows initially; opening a native service must not fail because the 3D-map room is absent. If immersive services are later extended here, they need an explicit anchor independent of the parchment solver. |

Campaign and Guildmaster use the same service objects, but **different measured
layouts**. Guildmaster reserves its current right-side action rail, adjacent map
pair, knife clearance, bench and barrel footprints. Do not place a new counter
there merely because a campaign screenshot appears empty. Native availability,
unlocking and current mode decide whether a service may be used; the concept does
not introduce campaign-only services into Guildmaster or vice versa.

No final XYZ station coordinates should be committed before a scene capture of
both maps and each room. Use the parchment's stable forward/right frame and
obstacle bounds; layout measurement runs on placement or scene changes, not every
frame. Existing environment floor alignment must be reused, especially on a cold
Guildmaster MR start.

## Physical interaction language

All gestures have pointer/trigger equivalents and work one-handed. Grabbing or
previewing is reversible and never spends money by itself. A deliberate, labeled
physical confirmation seal commits the currently displayed native quote; its
touch/laser hover, disabled reason and character payer are visible. No controller
hold or gesture-speed challenge is required. Controls should be reachable without
leaning through the map; prototype at roughly 0.35–0.55 m forward reach and use
existing usable button dimensions as the minimum, then validate seated and standing.

- **Merchant:** browse the native item categories in a sample rack/catalogue;
  grab a sample or its original item card, inspect both rules and availability,
  place it in the purchase tray. A sell tray accepts a selected owned item and
  displays its native resale value. Tabs become dividers, pages remain accessible,
  and stock/price/ownership warnings remain readable. The merchant looks toward
  the selected sample and acknowledges a successful transaction with a handover.
- **Temple:** choose the recipient using a character token, inspect the native
  blessing offerings beside a donation bowl, stage the **quoted** gold as a pouch,
  and confirm. The attendant blesses the token after native acceptance. Donation
  total and devotion progression live on an engraved register; availability and
  effects remain native. Do not reduce the service to a hardcoded fixed donation.
- **Enchantress / Magierin:** take an eligible ability card from the original
  card selection, seat it in a rune cradle, touch its legal enhancement slot and
  select an available rune. A preview attaches to the original printed ability,
  while a cost slate shows all native restrictions and the exact quote. Confirm
  with a seal; the NPC performs a brief engraving gesture. Native removal/sale
  modes, where available, become a clearly distinct removal tool with their own
  quote and confirmation. Never assume enhancements are purchase-only.

The complete native feature matrix should be the implementation acceptance list;
the examples above define presentation, not an alternate ruleset. Original text,
icons, modifier descriptions, card widgets, tooltips and native confirmations are
reused on the objects. An immersive station is not permission to redesign or omit
gameplay information.

## Multiplayer and state ownership

Use a shared station ID plus per-visitor transaction session and controlled
character identity. Several players can browse the same service at once; each
retains their own native selection state. **Do not put a cosmetic queue in front
of a transaction the flat game allows concurrently.** Native stock, gold and
ownership validation resolve racing purchases. A stale VR preview refreshes or
rejects exactly as its native action does.

One elected cosmetic author drives each NPC's attention and discrete animation
events. The NPC may acknowledge one visitor while others continue to browse or
confirm; the animation never locks gameplay. Each visiting owner publishes their
tray, previews, pointer/touch hover, tooltips, price/eligibility display, held
objects and intermediate animation state to observers. Observers have no native
controllers or purchase callbacks attached to the mirrored presentation.

Shared attention and handover are not enough: late joiners need a bounded current
snapshot, animation event IDs/start time and completion state. Duplicated or
reordered packets must not replay an engraving or restart an effect. An owner
disconnect, actor switch, service close or scene transition revokes only that
visitor's cosmetic session; accepted game actions remain native and are never
resent to reconstruct visuals. Integrate additive presentation records with the
existing protocol rather than creating a second gameplay transport. Reserve a
wire ID only when implementing, using the then-current free ID.

Flat/unmodded peers continue to use the original UI and gameplay network messages.
They cannot display a mod NPC; their confirmed transaction still updates the
native shared state. Do not invent their private hover or make them acquire a mod
lock. For VR peers, no convenient performance-based concealment exception is
assumed: the new owner-visible transaction presentation is mirrored in full.

## Native lifecycle and deadlock prevention

Keep the original service controller alive as the source of available operations,
selection, price and native continuation. The VR adapter translates a physical
intent into that controller's existing operation once, using current eligibility.
It does not spend gold, add a blessing or mutate a card directly.

First-visit introductions still display their actual continuation. Mandatory
confirmations cannot be discarded by closing the decorative station. An obvious
Back/Cancel control follows the native cancel route where supported; finishing
the visit runs mode Exit and restores character selection and the map surface.
For reference, `UITempleWindow.cs:136` disables its selection mode and hides its
introduction on Exit; `UINewEnhancementWindow.cs:256` unregisters ownership changes,
hides the card list and clears previews. Preserve those operations in full.

NPC animation is an observation of an accepted result, never the only route to
continue. User cancellation, a missing rig, interrupted tracking, a failed asset
load or a scene transition must not strand a native continuation. A bounded
fallback restores the original usable service view when immersive presentation
cannot be constructed. Do not treat disabling a UIWindow's root as merely hiding
pixels: it can stop native callbacks and reproduce the historical deadlocks.

## NPC asset brief

Build three distinct original full-body meshes from the extracted game portraits;
do not present inferred unseen anatomy/clothing as extracted reference. Provide
front, three-quarter and profile turnarounds for approval, then neutral-pose model,
UVs, materials and a consistent humanoid skeleton with separately articulated
fingers. Portrait costume, face silhouette, ornaments and carried props must stay
recognizable; an unavailable back view is an artist reconstruction.

Required clips: restrained idle/breathing, brief greeting, head/eye attention,
point to an offered object, accept/return an object, success acknowledgement and
return to idle. Merchant adds handover; temple attendant adds blessing; enchantress
adds inspect/engrave. Hand contact uses explicit prop sockets and a limited IK pass
so props meet the hands. Merchant/priestess feet stay grounded; the original enchantress
art suggests controlled hovering. Animated root motion must not move the station
unpredictably. See [original exports](TOWN-SERVICES-ART.md). No new dialogue or lip sync is necessary for the first
iteration; keep native voice/subtitles where present.

Start with a modest close-range production mesh, one shared body material plus
minimal eye/hair additions, baked normal detail and transparent surfaces kept
small. Establish triangle/texture budgets by measured four-player GPU/CPU cost,
not an unsupported fixed promise. LOD, visibility culling and shared materials
may reduce hidden work without changing anything visibly required by parity.
Stream all three service assets once per map lifetime, pool transaction props,
and release them on teardown. Avoid per-frame scene searches, material cloning,
full UI recapture and one RenderTexture per individual item.

## Prototype order and proof required

1. Greybox one reachable tray and NPC capsule against both maps and all environment
   choices, including seated use, MR, room changes and four visiting player slots.
2. Merchant interaction slice with real native buy/sell and cancellation, concurrent
   buyer race, no-new-asset fallback, repeated open/close and scene exit.
3. Temple plus enchantress using the complete native feature matrix; verify first
   visits, ownership switching, no-money/no-stock/no-valid-slot paths and callbacks.
4. Rigged NPC art and synchronized animation, followed by local/remote capture of
   identical selection, tooltip, effect and transaction timing; include late join
   and disconnect mid-animation. Profile a long four-player session and flat-peer
   compatibility before considering the replacement complete.

These steps describe future implementation. No claim of working immersive services,
headset correctness or verified four-player performance is made by this research.
