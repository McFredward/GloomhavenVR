# Hand Fan Reorder — Research + Implementation Plan

Feature: drag a card already in the hand fan to a **different position within the fan**.
While the held card hovers **between two fan cards**, an **overlay** appears at that gap
and the fan **opens a gap** to make room for the overlay + incoming card; **releasing**
there commits the new position and it **persists** across rebuilds.

Status: RESEARCH + PLAN only. No `src/` changes made.

---

## 1. How the systems work today (with citations)

### 1.1 Fan layout + where the order comes from

- The fan owns an ordered `List<VRCard> _cards` — `Cards/CardFan.cs:17`. This is the
  single source of layout order; `Relayout` walks it by index.
- Layout math: `CardFan.Relayout` — `Cards/CardFan.cs:339-421`. Per-card angle is
  `start + step*i` (`:399`) where `step = min(stepCap, maxArc/(n-1))` (`:358`) and
  `start = -step*(n-1)*0.5` (`:359`); position is on an arc of `radius`
  (`FanEffectiveRadius`, `:351`,`:404-406`) with a Z-stagger of `ZStagger*i` (`:318`,`:406`)
  and a per-card tilt `-angle*tiltFactor` (`:403`). Each card is placed with
  `card.SetHome(_root, pos, rot, 1f, instant)` (`:414`).
- The fan is **fed** by the driver, not self-sourced: `CardFan.SetCards(List<VRCard>)`
  (`Cards/CardFan.cs:83-91`) replaces `_cards`. The driver calls it from
  `CardsDriver.Rebuild` — `Cards/CardsDriver.cs:1591` (`_fan.SetCards(_fanBuffer)`).
- `_fanBuffer` is built in `Rebuild` from the game's live hand widgets:
  `CardsGameApi.GetCards(hand, _widgetBuffer)` (`Cards/CardsDriver.cs:1399`) then filtered
  into `_fanBuffer` in widget order (`:1431-1438`, the `CardType == Hand` loop).
- `CardsGameApi.GetCards` returns `hand.cardsUI` **in game order** —
  `Cards/CardsGameApi.cs:129-138`.
- **The game owns and re-sorts that order.** `CardsHandUI.SortCards()` calls
  `cardsUI.Sort()` — `decompiled/GH.Runtime/CardsHandUI.cs:1190-1194`, invoked on nearly
  every hand mutation (`:596`,`:624`,`:1042`,`:1103`,`:1124`,`:1163`,`:2582`).
  **Conclusion for Q5:** the hand order is NOT player-authored in the base game and is
  re-sorted every rebuild, so (a) a VR reorder has **no gameplay effect** (initiative is
  decided by the two `selectedCardsUI` entries at play time, `CardsHandUI.cs:2691-2696`,
  not by hand order), and (b) any VR order **must be re-applied by the mod after reading
  the game order**, or the next `Rebuild` will revert it.

### 1.2 Grabbing / holding a fan card

- `VRCard` is a `GrabbableBehaviour` — `Cards/VRCard.cs:17`. `OnGrab` keeps the world
  pose and flies the card into the hand (`:458-483`); `OnRelease` raises `Released`
  (`:508-520`) which the driver handles.
- On grab the driver removes it from the fan so the gap closes: `HookCard`/grab path calls
  `_fan.Remove(card)` — `Cards/CardsDriver.cs:1671`; `CardFan.Remove` closes the gap by
  re-laying out (`Cards/CardFan.cs:94-105`).
- While held: `card.IsHeld` (`Cards/VRCard.cs:129`); `Relayout` skips held cards
  (`Cards/CardFan.cs:394`). The held card + its holder are found with
  `HeldCard(out holder)` — `Cards/CardsDriver.cs:1311-1327`.
- Release routing: `OnCardReleased` — `Cards/CardsDriver.cs:1676-1861`. A fan card released
  **in the void** (no slot) hits the final `else` → `_fan.Add(card)` (`:1859`), which today
  simply **appends to the end** (`CardFan.Add`, `Cards/CardFan.cs:108-114`). This is the
  exact seam the reorder feature slots into.
- **No fan origin index is tracked today** (only the tray tracks `_snapHighlightSlot`).
  This feature adds an insertion index — directly analogous to the just-shipped
  control-board "return card to origin slot" work (item 2, `Cards/CardsDriver.cs:1737-1746`).

### 1.3 The control-board slot overlay (the model to mirror)

- Build: `PlayTray.BuildSlotHighlights` — `Cards/PlayTray.cs:1703-1730`. A gold additive
  `Quad`, sized `CardWidth*1.24 × CardHeight*1.24` (`:1719`), parented to the slot, placed
  at `SlotOverlayOffset + SlotGlowBaseZ` (`:1720`, `SlotGlowBaseZ = -0.006`,
  `Cards/CardsDriver.cs`-side const in PlayTray `:2410`). Material via
  `MakeGlowMaterial(gold)` — `PlayTray.cs:2706-2723` (additive `One/One`, ZWrite off,
  transparent queue; falls back to `Sprites/Default`).
- Toggle: `PlayTray.SetHighlightedSlot(int)` — `Cards/PlayTray.cs:1733-1744` (dedupes,
  activates only the target quad). A pulsing teal "wanted" variant exists too
  (`BuildWantedHighlights`/`SetWantedSlots`, `:1751-1802`, `SlotPulse` `:1805`).
- Driver telegraph while a card is held: `UpdateSlotHighlight` — `Cards/CardsDriver.cs:1243-1309`.
  It resolves the target slot with `_tray.SlotNear(...)` (`:1287`), calls
  `_tray.SetHighlightedSlot(slot)` (`:1302`), fires a debounced `HoverTick` haptic on edge
  (`:1306`), and caches `_snapHighlightCard`/`_snapHighlightSlot` (`:1298`,`:1305`) so that
  **"what glows is what drops"**: `OnCardReleased` reads `_snapHighlightSlot` first
  (`:1731`, `highlightSlot`). This is the pattern to mirror for the fan gap.

### 1.4 Hover-split machinery (reuse for the gap)

- `CardFan.SetHovered(int)` (`Cards/CardFan.cs:136-143`) records a hovered card index and
  relayouts; in `Relayout` the non-hovered cards slide sideways by `SplitOffset(i-hovered)`
  (`:411-412`), a **gaussian in slot-distance** `sign(d)*exp(-(d/falloff)^2)*mult*scale`
  (`SplitOffset`, `:433-441`) using `FanSplitMultiplier`/`FanSplitFalloff`/`FanHoverSplitScale`.
- The hover **source** is driven by `CardsDriver.UpdateFanHoverSplit` — `:912-941` (laser
  primary, proximity fallback), pushed via `_fan.SetHovered`. There is also a fingertip
  hover path internal to the fan (`UpdateFingertipHover`, `Cards/CardFan.cs:164-213`).
- **Reuse:** the same gaussian/side-split logic becomes the gap-open animation, except the
  pivot is a *gap between* indices rather than a hovered card: cards with `i < gap` slide
  toward one side and `i >= gap` toward the other, opening a card-width slot.

### 1.5 Persistence key

- Stable per-card identity: `AbilityCardUI.CardInstanceID` (int) —
  `decompiled/GH.Runtime/AbilityCardUI.cs:24` (also on `CAbilityCard`,
  `Cards/CardsGameApi.cs`/`CAbilityCard.cs:1261`). Reachable from a `VRCard` via
  `card.GameCard.CardInstanceID` (`VRCard.GameCard`, `Cards/VRCard.cs:54`). This is the key
  the persisted VR order is stored against (survives the game's re-sort within a session).

---

## 2. Recommended approach (per research question)

- **Q1 — where the fan order lives:** keep `CardFan._cards` as the render list (unchanged),
  but make the *driver* the owner of a persisted VR order. Add a
  `List<int> _fanOrder` (CardInstanceIDs) in `CardsDriver`. In `Rebuild`, after the
  `_fanBuffer` is populated in game order (`:1438`) and **before** `_fan.SetCards`
  (`:1591`), stable-reorder `_fanBuffer` to match `_fanOrder` (any card whose id is not yet
  in `_fanOrder` keeps game-relative order and is appended, then registered). This is the
  only place the game re-sort is overridden.
- **Q2 — grab:** unchanged; reuse the existing grab → `_fan.Remove` → held → `Released` →
  `OnCardReleased` seam. The feature only adds *where* a void-release lands.
- **Q3 — overlay:** mirror `BuildSlotHighlights` + `MakeGlowMaterial`. Since
  `MakeGlowMaterial`/the Overlay shader helper live in `PlayTray`, **extract a tiny shared
  glow-quad helper** (e.g. `Cards/CardGlow.cs` or a `static` in `CardMesh`) that both
  `PlayTray` and `CardFan` call, so the fan gap overlay looks identical to the board slot
  overlay (gold, additive, card-shaped). The fan overlay is a single quad child of the fan
  `_root`, positioned at the computed gap arc-slot pose.
- **Q4 — insertion detection + gap:** per frame while a fan card is held, map the held
  card's world position to the nearest **inter-card gap** `0..n` and open that gap.
  Add to `CardFan`: `int NearestGap(Vector3 worldPoint)` and `SetInsertionGap(int gap)`
  (mirrors `SetHovered`). `Relayout` gains a gap branch that shifts `i<gap` and `i>=gap`
  apart (reusing `SplitOffset`-style falloff so neighbours part smoothly) and places the
  overlay quad at the gap slot pose. The driver adds `UpdateFanInsertion()` right beside
  `UpdateSlotHighlight()` (`Cards/CardsDriver.cs:622`) — resolve gap, push it to the fan,
  haptic on edge, and cache `_insertHighlightCard`/`_insertGap` for "what glows is what
  drops."
- **Q5 — persistence + commit:** on release into a gap, `OnCardReleased` inserts the held
  card's `CardInstanceID` into `_fanOrder` at the gap index (removing any prior entry),
  then `_dirty = true` to rebuild — the reorder step in `Rebuild` then reproduces the new
  order every subsequent frame/rebuild. No game call at all (no gameplay effect).

---

## 3. Staged implementation plan

This is **one coherent Cards workstream** (see §4). Stages are sequential; each is
independently testable in VR.

### Stage A — Persisted VR fan order (foundation, no visible change yet)
1. `CardsDriver`: add `readonly List<int> _fanOrder = new();` and a helper
   `ReorderFanBuffer()` that stable-sorts `_fanBuffer` by `_fanOrder` index (unknown ids
   keep game order, appended and registered).
2. Call `ReorderFanBuffer()` in `Rebuild` just before `_fan.SetCards(_fanBuffer)`
   (`Cards/CardsDriver.cs:1591`).
3. Add a debug/dev hook to prove persistence (e.g. reverse order once) — verify the fan
   keeps the VR order across natural rebuilds and across the game's `SortCards`.
4. Clear/prune `_fanOrder` on scenario teardown / hand change (where `_factory.Clear` /
   `ReleaseHand` run) so stale ids don't accumulate.
   *Risk:* CardInstanceID reuse across scenarios — prune ids not present in the current
   hand each Rebuild.

### Stage B — Shared glow-quad helper + fan insertion overlay
1. Extract `MakeGlowMaterial` + the card-shaped glow quad builder from `PlayTray`
   (`:2706`, `:1703-1730`) into a shared static (`Cards/CardGlow.cs`), keep `PlayTray`
   calling it (behaviour identical).
2. `CardFan`: build one hidden gold overlay quad child of `_root`; add
   `SetInsertionGap(int gap)` (dedupe + relayout) and hide when `gap < 0`.
3. `Relayout`: when `_insertGap >= 0`, compute the gap slot pose (interpolate the arc
   angle between neighbours `gap-1`/`gap`, or extend past the ends for `gap==0`/`gap==n`),
   place + show the overlay there.

### Stage C — Gap-open animation
1. `CardFan.Relayout`: when a gap is active, offset cards `i < gap` and `i >= gap` away
   from the gap centre by a falloff push (reuse/parallel `SplitOffset`, sized ~one card
   chord so a full card fits). Tunable via a new `FanInsertGapWidth` config (seed to the
   card chord) or reuse `FanHoverSplitScale`.
2. Ensure held-card exclusion still holds (`:394`) and the last-card collider reset (`:417`)
   is unaffected.

### Stage D — Held-card → gap detection + telegraph
1. `CardFan.NearestGap(Vector3 worldPoint)`: project the point into fan-local space,
   compare against per-card arc x (or angle) to pick the nearest of `n+1` gaps. Clamp when
   the point is outside the fan span (feeds the open-question about drop-to-end vs cancel).
2. `CardsDriver.UpdateFanInsertion()` (called at `:621-622` region): only when the fan is
   open, a fan-originating card is held, and (per Q-open) the mode allows reordering —
   resolve gap via `NearestGap(held.transform.position)`, push `_fan.SetInsertionGap(gap)`,
   debounced `HoverTick` haptic on edge, cache `_insertHighlightCard`/`_insertGap`.
   Clear the gap (`SetInsertionGap(-1)`) whenever no eligible card is held.
3. Guard precedence: if the held card is over a **board slot** (existing
   `UpdateSlotHighlight` resolves a slot), the slot telegraph wins and the fan gap clears —
   so slot-play and fan-reorder never both glow.

### Stage E — Release-to-commit
1. `OnCardReleased`: before the final void `else` (`Cards/CardsDriver.cs:1857-1860`), if
   this card was fan-originating and `_insertHighlightCard == card` with `_insertGap >= 0`,
   commit: update `_fanOrder` (remove card id, insert at gap), `ClickPulse` haptic,
   `_fan.Add(card)` (or rely on the rebuild), set `_dirty = true`. "What glows is what
   drops," identical to the slot rule (`:1731`).
2. Decide the no-gap release per the open question (cancel → original index vs append).

### Stage F — Polish + config
1. New `CardsConfig` entries under "Cards" (live-tunable via the in-VR "Fan" debug
   category like the other `Fan*` entries, `Cards/CardsConfig.cs:555-611`): gap width,
   overlay size/colour if it should differ from the slot overlay.
2. Log lines mirroring the existing `Drop (...)`/`Rebuild:` diagnostics for the reorder
   commit.

---

## 4. Workstream shape (important)

Do **NOT** parallelize this. Every stage touches `Cards/CardFan.cs` and
`Cards/CardsDriver.cs` (the fan order list, gap layout, per-frame detection, release
routing all interlock), with a smaller shared edit to `Cards/PlayTray.cs` (extract the glow
helper) and possibly a new `Cards/CardGlow.cs`. Parallel workers would collide on
`CardFan`/`CardsDriver` exactly as the memory note warns. Run it as **one sequential Cards
workstream**, Stage A→F, committing per stage.

**Files:**
- `Cards/CardsDriver.cs` — `_fanOrder` + `ReorderFanBuffer` (Rebuild), `UpdateFanInsertion`,
  release-to-commit in `OnCardReleased`, prune on teardown.
- `Cards/CardFan.cs` — `SetInsertionGap`, `NearestGap`, gap branch in `Relayout`, overlay
  quad lifecycle.
- `Cards/PlayTray.cs` — extract `MakeGlowMaterial`/glow-quad into shared helper (no
  behaviour change).
- `Cards/CardGlow.cs` (new, optional) — shared overlay quad/material factory.
- `Cards/CardsConfig.cs` — new `Fan*` gap/overlay tunables.
- `Cards/VRCard.cs` — likely untouched (held world position + `GameCard.CardInstanceID`
  already exposed); add a getter only if needed.

---

## 5. Risks / unknowns needing hardware

- **Gap mapping feel:** projecting a held card (which billboards to the head,
  `VRCard.TickHeldPose` `:493-506`) onto the fan arc to pick the nearest gap needs on-device
  tuning — the held card and the fan can be at different depths. May need to project onto
  the fan plane and compare local-x rather than raw distance.
- **Overlay depth/readability** in unlit MR/void scenes — the slot overlay solved this with
  additive + no RenderOnTop; the fan overlay must sit proud of the cards (z-stagger) without
  z-fighting the neighbours.
- **Gap width vs full hand:** a full hand already spends its arc budget (`step` clamps at
  `maxArc/(n-1)`, `:358`); opening a full card-width gap may overflow the arc. Decide whether
  to compress the rest or temporarily widen the sweep.
- **CardInstanceID lifetime** across save/load or scenario switch — prune per Rebuild;
  confirm ids are stable within a session on hardware.
- **Interaction with the game's own reorder animation** (`cardsReorderingOffsetTime`,
  `CardsHandUI.cs:112`, `:1076`) — our order is applied after adoption so it should be
  invisible to the game, but verify no re-adopt flicker.

---

## 6. Open design questions for the user

1. **Release outside any gap:** should releasing away from the fan **cancel** (snap back to
   the card's original position) or **drop to the end**? (Current void-release appends;
   cancel is more forgiving.)
2. **When is reordering allowed?** Only when idle / during `CardsSelection`, or also during
   pick modes (LoseCard/Discard/Recover) and while the fan is shown read-only? Recommend:
   only when the fan is grabbable (CardsSelection), to avoid fighting the pick flows.
3. **Overlay look:** identical to the gold board-slot overlay, or a distinct **thin
   insertion bar** between cards (clearer "insert here" semantic than a full card ghost)?
4. **Gap-open feel:** how wide the gap (full card vs half), and eased vs snappy — and should
   the whole fan compress to keep a full hand on-arc, or briefly widen the sweep?
5. **Which hand:** free/dominant hand only (consistent with all other card interaction,
   `VRCard.InteractionBlockedHand` `:25`)? Assumed yes.
6. **Persistence scope:** session-only (in-memory `_fanOrder`, reset each scenario) or
   remembered across save/reload (needs a stable cross-session key — CardInstanceID may not
   survive a reload)? Recommend session-only for v1.
