# Items (Gegenstände) in VR — Design Proposal

Status: proposal for the user to choose a direction. No implementation.
Author: research worker (isolated worktree).
Date: 2026-07-16.

The mod currently does **not** handle items at all. A grep for `Item`/`Equipment` in
the mod's own source (`src/GloomhavenVR/`) returns only two incidental references in
`WorldUI/ModalFallback.cs:237`. This is a genuinely new subsystem. This doc explains
how items work in Gloomhaven Digital, then proposes concrete VR integration options.

---

## 1. How items work in Gloomhaven Digital

### 1.1 Data model

`CItem` — `decompiled/ScenarioRuleLibrary/ScenarioRuleLibrary/CItem.cs:15`
(a `CBaseCard`, so it is literally a card in the game's model). Key enums, all defined
in that file:

- **Slot** `EItemSlot` (line 40): `Head, Body, Legs, OneHand, TwoHand, SmallItem, QuestItem`.
- **Usage** `EUsageType` (line 31): `Spent` (flip, refresh on rest), `Consumed` (used up
  for the scenario / permanently), `Unrestricted` (reusable, e.g. passive/every-turn).
- **Trigger** `EItemTrigger` (line 54, `[Flags]`): `PassiveEffect, AtStartOfRound,
  DuringOwnTurn, SingleTarget, SingleAbility, EntireAction, OnAttacked, AtEndOfTurn`.
  → this is *when* an item can be used; `PassiveEffect` items can never be clicked.
- **Runtime state** `EItemSlotState` (line 18): `None, Equipped, Useable, Selected,
  Locked, Consumed, Spent, Active`. This is what drives the icon look and whether the
  player may click it right now.
- **Rarity** (`Common/Rare/Relic`), **ItemType** (`Ability`/`Override`).

Static definition data — `ItemCardYMLData`
(`decompiled/ScenarioRuleLibrary/ScenarioRuleLibrary.YML/ItemCardYMLData.cs:6`):
`Name`, `Art` (string → sprite), `Cost`, `Slot`, `Usage`, `Trigger`, `Rarity`,
`Consumes` (list of elements the item needs), `Data` (abilities/overrides/shield/
retaliate values), `ValidEquipCharacterClassIDs`, `PermanentlyConsumed`, `Tradeable`.

Per-actor inventory — `CInventory`
(`decompiled/ScenarioRuleLibrary/ScenarioRuleLibrary/CInventory.cs:9`) is a classic
paper-doll: `HeadSlot`, `BodySlot`, `LegSlot`, `TwoHandSlot`, `OneHandSlots[]`,
`SmallItemSlots[]`, `QuestItemSlots[]`, plus `AllItems` (line 35) and `SelectedItems`
(line 57). `AddItem` (line 233) routes an item into the right slot; `ToggleItem`
(line 366) flips selected/used state. It also raises events the UI listens to:
`ItemAdded/ItemSpent/ItemConsumed/ItemUsable/ItemNoLongerUsable/…` (lines 59-77).

### 1.2 Equip vs. use — two distinct contexts

- **Equipping** (deciding *which* items a character carries) is a **menu / out-of-scenario**
  activity: shop (`UIShopItem*`), party inventory (`UIPartyItemInventoryDisplay`), and the
  character-sheet paper-doll `ItemsUI.cs:11` (fields `m_Head/m_Body/m_Legs/m_Hand1/m_Hand2/
  m_SmallItems[]/m_QuestItems[]`, `ItemsUI.cs:21-33`). Equipping is bound to the game window
  `UIWindowID.EquipmentItemsPanel`. Items enter the scenario already equipped.

- **Using** an item happens **in-scenario, during play**. The equipped items that are
  usable *this turn* surface on a dedicated horizontal icon bar.

### 1.3 The in-scenario "use item" flow (the important one for VR)

Central surface: **`UIUseItemsBar`** (a `Singleton`) — `decompiled/GH.Runtime/UIUseItemsBar.cs:13`.
It is a `container` (`RectTransform`, line 16) that pools `UIUseItemScenario` icon slots,
one per currently-usable item (`itemSlots` dict, line 24).

When it appears (trigger points):
- **Own turn, action selection** — `CardsHandManager.cs:1281-1301` calls
  `ShowUsableItems(playerActor, …)`. Gated on `PhaseManager.PhaseType ==
  ActionSelection` (`CardsHandManager.cs:1270`). This is the normal "use an item on my
  turn" case.
- **Turn choreography** — several `Choreographer.cs:3954+` calls show/hide the bar around
  sub-phases.
- **Reactively when attacked** — `TakeDamagePanel.cs:249` shows shield/retaliate items so
  you can respond to incoming damage (`OnAttacked` trigger).

The click-to-use sequence (all in `UIUseItemsBar.cs`):
1. `ShowUsableItems(actor)` (line 414) filters `actor.Inventory.AllItems` to items in
   `Useable/Selected/Locked` state and builds an icon per item (`AddItem`, line 59).
2. Player clicks an icon → the per-slot `onClickItem` runs → `SetUseItem(item)` (line 181).
3. If the item **consumes elements** or needs an **infusion**, the player must pick those
   first (`CanConsume`, line 251; `IsCurrentItemReadyToUse`, line 160; the element board
   `InfusionBoardUI`). Simple consumables/passives skip this.
4. Confirm via the shared **ReadyButton** (`SetActiveItemButtons`, line 119, wires
   `readyButton` + `UndoButton` + `SkipButton`).
5. Confirm → `UseItem()` (line 191) → **`new UseItemService(actor).UseItem(item, …)`**.

Backend of a use — `UseItemService.UseItem` (`decompiled/GH.Runtime/UseItemService.cs:18`):
- Rejects passive items and items not in `Useable/Selected` state (lines 29-38).
- If online, replicates the action: `Synchronizer.SendGameAction(GameActionType.UseItem, …,
  new ItemToken(item.NetworkID, slot, …, chosenElements, infusions))` (line 46-48).
  `ItemToken` (`decompiled/GH.Runtime/ItemToken.cs:7`) is the wire payload.
- Disables the ready/skip/undo buttons + the bar, then calls the model:
  **`ScenarioRuleClient.ToggleItem(item, owner)`**
  (`decompiled/ScenarioRuleLibrary/ScenarioRuleLibrary/ScenarioRuleClient.cs:978`), which
  queues a `CSRLToggleItemMessage`. This is the single authoritative "the item was used"
  mutation.

Net effect for VR: **to use an item correctly (including multiplayer sync), the mod only
needs to reach `UseItemService.UseItem` (or drive the existing `UIUseItemsBar` click).**
We should *not* re-implement `ToggleItem`/`ItemToken` ourselves.

### 1.4 Item visuals

Each item has full card art. `ItemCardUI.cs:229` (`CreateCard`) composes: background from
`YMLData.Art` via `UIInfoTools.Instance.GetItemBackgroundSprite(art)` + an addressable
sprite loader (`ItemCardUI.cs:450`), a slot-type icon (`GetItemSlotIcon`, line 232),
condition icons (spent/consumed), title, cost. The small bar icons (`ItemUI.cs`) use
pre-baked sprite arrays keyed by slot + state. So we can render an item either as a **full
card** (reuse the live `ItemCardUI` canvas) or as a **small state-icon** (the bar look).

---

## 2. The mod's physicalization toolkit (where items can plug in)

From the mod-side survey (`src/GloomhavenVR/`). The mod never edits game prefabs; it (a)
reparents live game canvases onto VR meshes, (b) converts a canvas to world-space in place,
or (c) reads game state and draws its own mesh/TMP UI. Everything is reversible on
`Shutdown()`.

- **Module system** — `Core/IVRModule.cs` (`Name/Init/Shutdown`); modules registered in
  order at `Plugin.cs:351` (`RegisterModules`) and inited at `Plugin.cs:365`. A new
  `Items/ItemsModule` slots in next to `CardsModule`.
- **Physical grabbable cards** — `Cards/VRCard.cs:17` (a `GrabbableBehaviour`) + the
  live-canvas trick `Cards/CardFace.cs:89` (`Adopt` reparents the game card's own uGUI
  canvas onto a VR mesh). Factory `Cards/VRCardFactory.cs:64`. Mesh `Cards/CardMesh.cs`.
  Playing a card back into the engine goes through the one-per-frame
  `Cards/CardActionQueue.cs:44` (`Enqueue`), pumped from `CardsDriver`. This exact pattern
  works for item cards, and `UseItemService.UseItem` is our "SelectCard" equivalent.
- **Control board / PlayTray** — `Cards/PlayTray.cs:52`, a tilted slab at chest height,
  parented to the rig/hands root (follows the player). It already hosts card slots, a rest
  zone (`Cards/RestControls.cs`), a round readout, native Confirm/Undo docks, the turn-flow
  button cluster, and several panel mounts (`InitiativeMount`, `ObjectivesMount`,
  `ElementMount`, `PileMount`, …, `PlayTray.cs:134-186`). **It has a first-class "add a new
  mount" pattern** (`BuildMounts` + width/height consts) and `BoardButton` +
  `RegisterLaserTarget(collider, IPokeable)` (`PlayTray.cs:339`) for poke/laser-clickable
  buttons. This is the natural home for an item belt.
- **Auto-converted world panels** — `WorldUI/CanvasConversion.cs:234` (`Convert` flips a
  canvas to `WorldSpace` + a dedicated UI camera, fully reversible) and the surface
  framework `WorldUI/Surfaces/WorldSurface.cs:12` (`FindTarget/Place/WantConverted`).
  `TrayMountedPanelSurface` (`WorldUI/Surfaces/TablePanelSurfaces.cs:40`) docks a converted
  canvas onto a PlayTray mount. The initiative track is done exactly this way
  (`InitiativeTrackSurface`). Converted canvases keep their own `GraphicRaycaster`, so the
  laser pointer (`Hands/Interact/UguiPointer.cs`) fires **real** uGUI clicks — no per-button
  rewrite. Native buttons can be restyled via `WorldUI/NativeButtonSkin.cs:41`.
- **Equip window is already half-handled** — `UIWindowID.EquipmentItemsPanel` is in the
  `ModalFallback` set (`WorldUI/ModalFallback.cs:237`), so the equip/inventory window already
  floats as a converted modal today. It just isn't a first-class docked surface yet.
- **Laser + grab interaction** — `Hands/Interact/IGrabbable.cs:19`,
  `ProximityGrabber.cs:18` (proximity highlight + grip/trigger grab + laser-pluck),
  `IPokeable`/`PokeInteractor` (fingertip poke). Any new physical item object gets grab +
  poke + laser for free by implementing these.
- **Wrist HUD** — `WorldUI/WristHud.cs:47` parents a small TMP panel to `hand.Rig.Wrist`,
  gated on a "watch-check" look-at, refreshed on game events. A wrist item quick-bar clones
  this pattern.

---

## 3. VR integration options

Four concrete options, ordered simple → ambitious. All of them drive the **same** game
`UseItemService.UseItem` / `UIUseItemsBar` flow, so item logic and multiplayer sync stay
correct for free. They differ only in the VR *presentation and gesture*.

Legend — Effort: S (days), M (~1–2 weeks), L (multi-week). "Touches" = mod files.

### Option A — Auto-convert the game's use-items bar (simple / low-risk)

**Where items live in VR:** the game's own `UIUseItemsBar` canvas, converted to a
world-space panel docked on a new `PlayTray` mount (e.g. bottom edge of the control board),
next to the initiative track. The equip window (`EquipmentItemsPanel`) stays as the existing
floating `ModalFallback` (or is promoted to its own docked surface).

**How the player uses an item:** point the laser at an item icon and pull the trigger →
real uGUI click → the existing element-pick / ReadyButton confirm flow runs on the converted
panel just like on a flat screen. Poke also works (`pokeable: true`).

**Maps to game flow:** 1:1 — it *is* the game bar. The bar auto-appears/disappears because
the game already calls `ShowUsableItems`/`Hide` at the right phases.

**Pros:** least code; guaranteed-correct game logic including element consume/infusion and
reactive shield items (`TakeDamagePanel`) with zero special handling; multiplayer sync free;
fully reversible. Ships items end-to-end fastest.
**Cons:** least immersive — a small flat icon strip on the board, not diegetic;
icons are tiny for laser aiming; no "hold the potion" fantasy.

**Effort:** S–M.
**Touches:** new `WorldUI/Surfaces/UseItemsBarSurface.cs` (a `TrayMountedPanelSurface`
targeting `UIUseItemsBar.container`), register it in `WorldUI/WorldUIModule.cs`
(`_slotSurfaces`), one new mount in `Cards/PlayTray.cs` (`ItemBarMount` + consts). Optionally
promote `EquipmentItemsPanel` from `ModalFallback` to its own surface.

### Option B — Physical item belt on the control board (recommended target)

**Where items live in VR:** a dedicated **item belt/bar** built as a first-class PlayTray
mount. The mod reads `actor.Inventory.AllItems` + each item's `SlotState` and draws one
physical **item chip** per equipped item (small slab with the item's `Art` on it, dimmed when
`Spent/Consumed`, glowing when `Useable`), using the same mesh/texture toolkit as cards but
smaller. Chips are laid out left→right grouped by slot type.

**How the player uses an item:** **poke** a glowing chip with a fingertip (or laser-click) →
if the item is a simple consumable/passive, the mod calls `UseItemService.UseItem(item)`
through a `CardActionQueue`-style serialized call; if the item needs elements/infusions, the
mod hands off to the game's element pick + ReadyButton confirm (reuse Option A's converted
sub-panel for that step, or drive `SetUseItem` + confirm on the board button cluster). Spent
chips visibly flip/dim; consumed chips play a small burn/fade (reuse `Cards/BurnCardFx.cs`).

**Maps to game flow:** the chip's "use" gesture is wired to the same `UseItemService.UseItem`
the bar uses; state (`Useable/Spent/Consumed/Selected`) is read from `CItem.SlotState` and
refreshed on the `CInventory` events (`ItemSpent/ItemConsumed/ItemRefreshed`, mirrored by
`UIUseItemsBar.RefreshItem`).

**Pros:** diegetic and consistent with the ability-card/board aesthetic; big poke targets;
"the items live on my board" reads clearly; leaves the flat bar behind. Reactive shield items
can appear on the same belt during `TakeDamagePanel`.
**Cons:** must mirror the bar's state→look mapping and the element/infusion sub-flow (the
main added complexity); slightly more upkeep as game state changes.

**Effort:** M.
**Touches:** new `Items/ItemsModule.cs`, `Items/ItemsDriver.cs`, `Items/ItemBelt.cs`,
`Items/ItemChip.cs` (grabbable/pokeable), `Items/ItemsGameApi.cs` (read inventory + call
`UseItemService`); `Cards/PlayTray.cs` (new `ItemBeltMount` + consts); registration in
`Plugin.cs`. Reuse `CardMesh`, `NativeButtonSkin`/addressable art, `CardActionQueue`,
`BurnCardFx`.

### Option C — Grabbable item cards in a second fan (immersive / ambitious)

**Where items live in VR:** each equipped item becomes a full **item card** (`VRCard` +
`CardFace.Adopt` of the live `ItemCardUI` canvas) in a **second, shorter fan** offset from
the ability-card fan (e.g. lower-left, or on the non-dominant side). You can pull an item card
out to inspect the full art/text.

**How the player uses an item:** physically **grab** the item card and drop it onto a "use"
slot on the control board (mirrors how ability cards are played via `OnCardReleased` →
`CardActionQueue` → `SelectCard`), or grab-and-tap. Consumables burn on use (`BurnCardFx`).

**Maps to game flow:** the release/tap edge calls `UseItemService.UseItem(item)` via the
action queue; element/infusion picks still hand off to the game sub-UI (Option A panel) when
required.

**Pros:** most immersive and tactile; reuses the proven card pipeline (`VRCard`, `CardFace`,
`CardMesh`, `ProximityGrabber`, `CardActionQueue`) almost verbatim; great for inspecting
relics.
**Cons:** highest effort and most moving parts; item cards are not a natural "hand" you hold,
so the second fan can feel cluttered with many small items; the element-pick sub-flow is the
same open problem as B; equip management still lives in a menu. Some items (passives) are
never "used" — they'd be inspect-only, which needs clear affordancing.

**Effort:** L.
**Touches:** `Items/ItemsModule.cs` + driver, a second `Cards/CardFan.cs` instance (or a new
`Items/ItemFan.cs`), `Items/ItemCardAdopter.cs` (adopt `ItemCardUI`), a use-slot on
`Cards/PlayTray.cs`, plus the C-specific grab-to-use routing.

### Option D — Wrist / pocket quick-use bar (immersive, complementary)

**Where items live in VR:** a small item quick-bar parented to the wrist
(`WorldUI/WristHud.cs` pattern: parent to `hand.Rig.Wrist`, reveal on a watch-check look-at).
Shows the currently-usable items as a compact strip you glance at by turning your wrist.

**How the player uses an item:** tap/poke a wrist button, or laser-click it →
`UseItemService.UseItem`. "Reach to your belt/wrist" fantasy.

**Maps to game flow:** same `UseItemService.UseItem` call; reveal driven by the same
`ShowUsableItems`/state events.

**Pros:** always available regardless of where you're looking; strong immersion; small,
focused surface; complements A/B (doesn't replace equip menus).
**Cons:** limited space for many items; discoverability (players must learn the wrist
gesture); element/infusion sub-flow still needs the game sub-UI; competes for wrist space with
the existing `WristHud` stats panel.

**Effort:** M.
**Touches:** new `Items/WristItemBar.cs` (clone `WorldUI/WristHud.cs`), `Items/ItemsModule.cs`
+ driver + `Items/ItemsGameApi.cs`. No PlayTray changes required.

---

## 4. Recommendation

**Ship Option A first, then evolve into Option B as the target experience. Treat C and D as
optional polish.**

Rationale:
- **A is the safe floor.** It makes items fully functional in VR (including the tricky bits:
  element consume/infusion, reactive shields, multiplayer sync) with the least code and the
  least risk, because it reuses the game's own bar and confirm flow. Even if we go no further,
  players can use items. This also de-risks the hard sub-problem (element picks) that B/C/D all
  otherwise have to solve.
- **B is the right destination.** The item belt on the control board matches the mod's
  established aesthetic (cards + board mounts), gives big poke targets, and reads as
  "my items live here." It reuses A's converted panel purely for the rare element-pick step,
  so B doesn't have to re-implement that flow.
- **C/D are enhancements**, not prerequisites. C is the most immersive but the priciest and
  most cluttered for many small items; D is a lovely complement but limited in capacity. Do
  them only if playtesting shows demand.

Equipping (choosing items) should stay a **menu activity**: promote the existing
`EquipmentItemsPanel` `ModalFallback` (`WorldUI/ModalFallback.cs:237`) to a proper docked
`WorldSurface` so it reads cleanly in VR, but do not physicalize the paper-doll — it's an
out-of-scenario management screen, not a per-turn interaction.

### Phased plan

- **Phase 0 — spike (S):** stand up `Items/ItemsModule` (mirror `CardsModule`), read
  `CardsGameApi.ActionSelectionHand()` → `actor.Inventory.AllItems`, log each item's
  `Slot/Usage/Trigger/SlotState`. Confirms we can read the live inventory and hook the phases.
- **Phase 1 — Option A (S–M):** `UseItemsBarSurface` docked on a new `PlayTray.ItemBarMount`;
  verify laser-click uses an item (incl. a potion that consumes an element, and a shield during
  `TakeDamagePanel`) in single-player and multiplayer.
- **Phase 2 — Option B (M):** physical item belt with state-driven chips + poke-to-use, handing
  off to the Phase-1 panel only for element/infusion picks. Add burn/flip feedback. Promote the
  equip window to a docked surface.
- **Phase 3 — optional (M–L):** add Option D wrist quick-bar and/or Option C item-card fan based
  on playtest feedback.

---

## 5. Open questions for the user

1. **Primary use gesture:** laser-click (precise, matches current UI interaction) vs. fingertip
   poke (immersive, needs bigger targets) vs. grab-and-drop (Option C only)? This decides the
   affordance size of item chips.
2. **Equipping:** keep it as a converted menu (recommended), or do you eventually want a physical
   paper-doll / "put the helmet on" treatment?
3. **Element / infusion picks:** for items that consume elements, is it acceptable to briefly pop
   the game's converted element-board sub-panel (Option A style) even inside the physical belt
   (Option B), or do you want that step physicalized too (more work)?
4. **Reactive shield items** (used when attacked, `TakeDamagePanel`): surface them on the same
   item belt, or as a separate transient prompt near the damaged figure?
5. **Wrist competition:** if we later add a wrist item bar (D), it shares wrist space with the
   existing `WristHud` stats — one combined wrist panel, or separate wrists?
6. **Target scope now:** just get items *working* (Phase 1) and stop, or commit to the diegetic
   belt (Phase 2) as the shipping experience?

---

## Appendix — tiny (non-committed) module scaffold sketch

Illustrative only — not added to the build. Mirrors `Cards/CardsModule.cs`.

```csharp
// Items/ItemsModule.cs  (SKETCH — do not commit as-is)
internal sealed class ItemsModule : IVRModule
{
    public string Name => "Items";
    private GameObject _driverGo;

    public void Init()
    {
        ItemsConfig.Bind();                       // like CardsConfig.Bind()
        if (!VRSession.IsRunning && !Plugin.DevMode.Value) return;
        // PatchAll(...) if we need Harmony hooks on ShowUsableItems phases
        _driverGo = new GameObject("VR.ItemsDriver") { hideFlags = HideAndDontSave };
        UnityEngine.Object.DontDestroyOnLoad(_driverGo);
        _driverGo.AddComponent<ItemsDriver>();    // reads inventory, builds belt/surface
    }

    public void Shutdown()
    {
        if (_driverGo) UnityEngine.Object.Destroy(_driverGo); // OnDestroy restores state
        _driverGo = null;
    }
}

// The one game-facing call the driver ultimately makes to "use" an item:
//   new UseItemService(actor).UseItem(item, networkActionIfOnline: true, infusions);
// (matches UIUseItemsBar.UseItem -> UseItemService.cs:18 -> ScenarioRuleClient.ToggleItem)
```
