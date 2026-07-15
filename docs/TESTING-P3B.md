# TESTING-P3B — Physical card hand (feat/cards) HMD checklist

> Prereqs: P1 rig + P2 hands validated (`TESTING-P1.md`, `TESTING-P2.md`).
> Config: `BepInEx/config/dev.gloomhavenvr.cfg` (`[General] Enabled = true`) and
> `BepInEx/config/dev.gloomhavenvr.cards.cfg` (`[Cards]` — created on first run).
> The fan opens on the **non-dominant** hand (opposite of `[Hands] PrimaryHand`).

## 0. Desktop smoke test (no HMD)

- [ ] `[Dev] Enabled = true`, `[Dev] SimulateHands = true`, `[Cards] DevFakeHand = 6`.
- [ ] Start the game to the main menu. F8 toggles sim hands; turning the fake
      non-dominant palm toward the camera opens a fan of 6 numbered placeholder cards.
- [ ] Log shows `Card hand installed …` and `Dev fake hand: 6 placeholder cards spawned.`
- [ ] Hold G (grip) near a card → it snaps to the hand; release over a tray slot →
      it parks there; release in the void → it flies back to the fan.
- [ ] F6 hot reload (ScriptEngine): no errors, fan/tray rebuild cleanly, no leaked
      `GloomhavenVR.*` objects from the previous load (check with UnityExplorer).

## 1. 2D suppression (in scenario, HMD on)

- [ ] Load any scenario. When card selection starts, the 2D hand window does NOT
      render on the desktop mirror (window alpha forced to 0) — but the game still
      behaves as if it were open (initiative track updates, Ready button reacts).
- [ ] The short-rest confirmation dialog and burn/redraw dialogs still appear
      (suppression lifts automatically while a game dialog sits inside the hand
      window — watch the log for `Hand suppression lifted…`).
- [ ] Quit to menu and back: suppression re-arms in the next scenario.
- [ ] Disable the mod (`[General] Enabled = false`): 2D hand is fully vanilla again.

## 2. Palm fan

- [ ] Round start (card-selection phase): turn the non-dominant palm toward your
      face → the hand pile fans out above the palm, cards face the HMD, slight
      overlap, readable at arm's length.
- [ ] Card faces are the game's REAL card renders (art, initiative number, XP icons,
      enhancements) — not screenshots; text stays crisp when leaning in.
- [ ] Rotate the palm away → fan hides (hysteresis: no flicker at the boundary).
- [ ] Move the hand near a fan card → it pops forward and scales up (haptic tick).
- [ ] Grip → the card snaps to the hand at inspect scale; readable; release in the
      void → it animates back into its fan gap (gap closes while it is out).

## 3. Play tray, initiative, swap

- [ ] Entering card selection places the tray in front of you at waist height
      (`[Cards] TrayForward/TrayDown/TrayRight/TrayTilt` adjust the pose).
- [ ] Drop a fan card onto slot 1 (left): the card parks; the initiative badge over
      slot 1 shows the card's initiative number; the (suppressed) game state gets
      the selection — verify via initiative track avatar showing that number.
- [ ] Drop a second card onto slot 2: badge keeps slot-1's initiative.
- [ ] **Order rule**: drop into slot 2 FIRST, then slot 1 → the badge/track must
      show the slot-1 card's initiative (driver auto-reconciles with the game's
      "first pick leads" rule using the game's own SwapInitiative).
- [ ] Physically swap: grab the slot-1 card, drop it on slot 2 → both cards trade
      places and the initiative track updates (check the number changes).
- [ ] Poke the initiative badge → same swap.
- [ ] Pull a card off the tray and release it away from the tray → it returns to
      the fan and the game deselects it (Ready button un-arms if <2 cards).
- [ ] Ready lamp (left of tray) turns green exactly when the game would allow
      confirm (2 cards or rest selected). Confirm itself is still the 2D Ready
      button / F9 (physical button is P3c).
- [ ] Selecting an invalid card (e.g. an Active card) bounces back to the fan with
      a log line `Select rejected …`.

## 4. Rests

- [ ] Short-rest token (yellow "ZZZ") is lit only when >1 discarded card and not
      yet rested. Poke it → the game's confirmation dialog appears; confirm →
      burn/redraw dialogs run; state matches a 2D short rest (check discard pile
      count in the deck viewer afterwards).
- [ ] Long-rest token (blue "99") toggles long rest: badge shows 99, ready lamp
      arms with only the rest selected. Poke again → untoggles.
- [ ] On the long-rested turn, the burn-a-discarded-card pick arrives as a fan of
      ONLY the discarded cards: poke one → game dialog confirms → long rest
      resolves (heal + discards return to hand).

## 5. Half selection (your turn)

- [ ] Turn start: the two played cards lie in front of you (fan/tray hidden or
      inert), scaled up, both halves poke-able.
- [ ] Poke the TOP half of card A → the game commits that action (targeting starts
      exactly like a 2D top-half click); after resolving, card A dims and only the
      OTHER half type on card B stays poke-able (game's Select2nd rules mirrored).
- [ ] Invalid halves are dimmed (dark overlay) and refuse the poke (no haptic click).
- [ ] Default options: the "ATK 2" / "MOV 2" chips beside each card play the
      default attack/move respectively, with the same validity dimming.
- [ ] Consume/infusion buttons ON the card face respond to direct fingertip pokes
      (uGUI poke surface path).
- [ ] Undo (2D button for now): after undo the halves re-arm correctly
      (`CardsActionControlller` phase restored — VR overlays follow).
- [ ] Long-rest turn: no half layout; the burn pick from §4 shows instead.

## 6. Consistency / stability

- [ ] Select/deselect spam (drop + retrieve 10× fast): no crash, no desync between
      tray occupancy and the initiative track; frame hitches on select stay ≤1
      frame (the SRL ack spin-wait lands on the queued frame — expected).
- [ ] Round ends → all VR cards disappear; next round rebuilds the fan from the
      new hand pile (played cards now missing).
- [ ] Scenario end / quit to menu mid-selection: no errors, no orphaned
      `VRCard_*` objects, 2D hand fully restored in the next 2D-only session.
- [ ] F6 hot reload mid-scenario: card faces return to the (suppressed) 2D hand
      before reload; after reload the fan re-adopts them.
- [ ] 30 min play: no per-frame GC spikes from the Cards module (Unity profiler,
      `CardsDriver.Update` / `VRCard.Update` alloc-free).

## Known limits (by design in P3b)

- Confirmations (short-rest yes/no, burn/redraw, lose-card) are the game's 2D
  dialogs — world-space versions are P3c.
- Ready/Undo/Skip remain 2D (physical cluster is P3c). The tray only READS the
  ready state (lamp).
- Extra-turn card selection and multi-hand (multi-merc tab) flows fall back to the
  active hand only; switching mercs mid-selection uses the 2D tabs for now.
- Text sizes/poses of badge, chips and tokens are first-pass values — tune the
  `[Cards]` config and report back.
