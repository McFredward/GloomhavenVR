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

## 0b. Hand visuals (test #13 upgrade)

- [ ] The procedural hands read as HANDS at a glance: rounded palm (no brick
      edges), knuckle ridge, thumb-base mound, fingers that taper toward the
      tips (thumb clearly thicker than the pinky), lighter nail hints on the
      backs of the fingertips.
- [ ] Pull the trigger / grip and watch the fingers curl: knuckles stay
      CONTINUOUS (joint spheres) — no gaps opening between segments.
- [ ] Bundle gloves (when present) still load and take priority; deleting the
      bundle falls back to the upgraded procedural hands with a log line.

## 1. 2D suppression (in scenario, HMD on)

- [ ] Load any scenario. When card selection starts, the 2D hand window does NOT
      render on the desktop mirror (window alpha forced to 0) — but the game still
      behaves as if it were open (initiative track updates, Ready button reacts).
- [ ] The short-rest confirmation dialog and burn/redraw dialogs still appear
      (suppression lifts automatically while a game dialog sits inside the hand
      window — watch the log for `Hand suppression lifted…`).
- [ ] Quit to menu and back: suppression re-arms in the next scenario.
- [ ] Disable the mod (`[General] Enabled = false`): 2D hand is fully vanilla again.

## 2. Palm fan (P7: roll-axis reveal, fan-hand exclusion, 3D cards, in-hand hold)

- [ ] Round start (card-selection phase): ROLL the non-dominant wrist so the palm
      turns up / toward you (supination only — `[Cards] SupinationThreshold` = 0.2)
      → the hand pile fans out above the palm.
- [ ] **Roll axis only (test #10 fix)**: pitching the arm up/down or pointing it at
      your face with the palm still sideways must NOT open the fan; conversely,
      once the palm is rolled up the fan must stay open at ANY arm pitch (point the
      supinated hand down/forward/up — the fan stays).
- [ ] Roll the palm back down past vertical → fan hides late (wide hysteresis, no
      flicker); while the dominant laser is on the fan it must NOT hide at all.
- [ ] `[Cards] RevealMode = always`: the fan is out for the whole selection phase with
      no gesture; `tilt` restores the roll gesture.
- [ ] **No self-highlight (test #10 fix)**: hold the fan open and keep the fan hand
      still, dominant hand far away → NO card highlights, NO pops, NO haptics — ever.
      The fan-owning hand can never highlight/grab/poke a card; two cards must never
      alternate highlights on their own.
- [ ] Sweep the DOMINANT hand across the fan: highlight moves in discrete steps with
      one tick per change; the current card keeps its highlight until a neighbor is
      clearly better (2.5 cm margin) — no oscillation even between two touching cards.
- [ ] **Real 3D cards (test #10 fix)**: cards have visible thickness (~1.5 mm),
      rounded corners, a dark thin edge, and an OPAQUE decorative back — look at the
      fan from the far side: you see card backs, not mirrored faces or see-through
      quads.
- [ ] Card faces are the game's REAL card renders; the live face fills the front
      almost edge-to-edge (thin dark rim only, no fat black border).
- [ ] **Laser pluck (dominant hand)**: point the laser at a fan card → it pops
      forward + scales (single tick), the beam clamps to the card (no pass-through);
      the hover is sticky — small aim wobble on the card must not flip the pop to a
      neighbor; pull the TRIGGER → the card flies into the dominant hand.
- [ ] **Proximity pluck** with the dominant hand still works: reach in, grip → grab.
- [ ] **Pinch grip (test #12) + readable at rest (test #13)**: the grabbed card is
      held BETWEEN THUMB AND INDEX — its lower edge sits at the midpoint of the
      thumb tip and index tip (sampled at grab time), enlarged
      (`[Cards] InspectScale`). With the controller in a RELAXED grip (hand in
      front of the chest, no wrist twist) the card stands up out of the pinch,
      top toward the thumb side, FACE toward your eyes — readable immediately,
      like really holding a playing card (`[Cards] HeldFaceBias`, default 65°;
      `HeldTiltDegrees` is legacy/unused). It still rotates 1:1 with the wrist:
      twist the wrist → the card twists with it, NO auto-facing, no floating in
      front of the hand.
- [ ] **Both hands**: grab with the left hand too — the card mirrors correctly
      (top toward the left thumb, face toward you), never mirrored text or
      facing away.
- [ ] Fine-tune the pinch with `[Cards] HeldPinchOffset` (GrabAnchor-local meters:
      +Y out of the palm, +Z along the fingers). `HeldOffPalm/HeldForward` are only
      the fallback for rigs without finger joints (procedural AND bundle gloves
      always have `Anchor_{Thumb|Index}_Tip` — missing joints are synthesized, see
      `unity/.../Bundle/Hands/README.md`).
- [ ] Release (trigger-up for a laser pluck, grip-up for a proximity grab) in the
      void → the card animates back into its fan gap; release over a board slot (card
      or hand above the slot) → it parks there.
- [ ] **Slot snap (test #13)**: while holding a card NEAR a slot (card center OR
      the holding hand within ~12 cm), the target slot shows a gold glow frame
      and a haptic tick fires once as the target slot changes; release → the
      card "zaps" into the glowing slot with a quick lerp + click haptic. Every
      drop writes a `Slot check: …` log line with per-slot distances and
      ACCEPT/REJECT.

## 3. Control board (P7 redesign — see .planning/research/CONTROLBOARD.md)

- [ ] Entering card selection places the CONTROL BOARD in front of you at chest
      height, tilted toward you like a card-table edge / lectern
      (`[Cards] TrayTilt` = 30° from horizontal; `TrayForward/TrayDown/TrayRight`
      adjust). It reads as a desk, NOT a floating vertical panel.
- [ ] Board layout is self-explanatory: REST zone (left, labeled, two captioned
      tokens) | two large framed card slots, left one captioned INITIATIVE with a
      gold number badge above it | CONFIRM + UNDO buttons (right).
- [ ] Labels are localized (game language ≠ English → CONFIRM/UNDO/LONG REST texts
      follow the game where keys exist).
- [ ] **Steady labels (test #13 fix)**: "ZZZ", "99" and the badge number/"-" sit
      fully ON their discs, do not flicker or "clip" while moving the head (they
      were z-fighting their plates), and the badge number changes without a
      visible re-layout blink.
- [ ] Drop a fan card onto slot 1 (left): the card parks; the badge shows its
      initiative number; the game state gets the selection (initiative track avatar
      shows that number). Free re-arranging: grab back / move between slots at will
      until confirm.
- [ ] Drop a second card onto slot 2: badge keeps slot-1's initiative.
- [ ] **Order rule**: drop into slot 2 FIRST, then slot 1 → the badge/track must
      show the slot-1 card's initiative (driver auto-reconciles with the game's
      "first pick leads" rule using the game's own SwapInitiative).
- [ ] Physically swap: grab the slot-1 card, drop it on slot 2 → both cards trade
      places and the initiative track updates (check the number changes).
- [ ] Poke the initiative badge → same swap. Laser + trigger on the badge → same.
- [ ] Pull a card off the board and release it away → it returns to the fan and the
      game deselects it; laser + trigger on a slotted card also plucks it back.
- [ ] **CONFIRM button**: dark while <2 cards; green exactly when the game would
      allow confirm (2 cards or rest selected); its label mirrors the game's Ready
      button ("End selection" etc., localized). POKE it → cap presses in, click
      haptic, the round commits exactly like the 2D Ready button. Next selection
      phase: LASER + trigger on it → same.
- [ ] **UNDO button**: lit only when the game's own Undo is available; poke/laser
      → same effect as the 2D Undo.
- [ ] Every board press writes a log line (`Board: … pressed`), and confirm/undo
      log whether the game accepted them.
- [ ] Selecting an invalid card (e.g. an Active card) bounces back to the fan with
      a log line `Select rejected …`.
- [ ] While the laser points at any board element the beam clamps to it and the
      trigger must NOT also click the game board behind it.

## 3b. Tray v3 (test #14 fixes: drops, buttons, grab, initiative strip)

- [ ] **One drop = one placement**: play a full selection phase while moving,
      world-grabbing and scaling the diorama a lot. `Board: card placed…` /
      `Drop (…)` lines appear ONLY when you really release a card — no bursts of
      placements while nobody is dropping anything (the old sync path re-placed
      and re-logged both slots on every rebuild and yanked HELD cards onto the
      slots, which then self-accepted on the next unrelated grip release).
- [ ] Each real release logs exactly one line:
      `Drop (Right): slot1 0.08 m, slot2 0.31 m, radius 0.12 m → play into slot 1.`
      (distances/radius are world units — they scale with the diorama).
- [ ] A card you are HOLDING is never ripped out of the hand by a rebuild
      (badge press, selection event, window churn) — it stays pinched until YOU
      release it.
- [ ] **Button presses always answer in the log**: every CONFIRM/UNDO attempt
      writes `Board: BoardButton_… pressed (source=poke|laser, …)` or
      `… press REJECTED (source=…, …) — disabled: active=… interactable=… state=…`.
      A dead-silent button press is a bug.
- [ ] CONFIRM works even though the 2D UI stack is hidden in VR: the gate now
      mirrors the game's own `ReadyButton.OnClick` guard (enabled + no warning
      mask + interactable) and deliberately ignores the 2D canvas alpha.
- [ ] **Tray handle (Controllboard)**: a brass bar hangs under the tray's bottom
      edge. Grip it (proximity, like grabbing a card) → the tray follows your
      hand (position + yaw; tilt stays). The world grab must NOT engage while
      the bar is highlighted/held — and gripping empty air must never move the
      tray.
- [ ] Grip the bar with BOTH hands and spread/pinch → the tray resizes,
      clamped 0.5×–2×; cards, buttons and strip scale with it.
- [ ] Release → `Tray layout persisted: …` in the log; leave card selection and
      come back (and restart the game): the tray reappears in your adjusted
      position/rotation/size (`[Cards] TrayForward/TrayDown/TrayRight/TrayYaw/
      TrayScale` in `dev.gloomhavenvr.cards.cfg`).
- [ ] ~~**Initiative strip**~~ (test #14) — REPLACED in test #15 by the game's
      real initiative track docked on the tray (see §3c).

## 3c. Tray dashboard (test #15: real UI on the tray, follow-toggle, modal grab)

Layout — the tray is now the central dashboard, visible for the WHOLE scenario
(not only during card selection): TOP edge = the game's REAL initiative track
(converted canvas); LEFT = converted objectives panel + rest zone; CENTER = the
two card slots + initiative badge; RIGHT = CONFIRM / UNDO / SET (settings gear);
bottom-right frame corner = the PIN follow-toggle; bottom edge = the brass grab
handle. The element board stays a separate world panel.

- [ ] **Real initiative track on the tray**: portraits (not text chips) sit right
      above the tray's top edge, sized to the tray width. The old free-floating
      initiative panel over the table is GONE. The old text strip is gone too.
- [ ] The track shows the vanilla **'?' for players who have not locked in**
      (online: other players during selection; initiative 0) — this is the game's
      own display (`InitiativeTrackActorAvatar` / `InitiativeTrackPlayerAvatar`),
      not a re-implementation.
- [ ] Initiative swaps by poking the track's avatars still work (host raycaster).
- [ ] **Objectives on the tray**: the mission objectives panel hangs off the
      tray's LEFT edge and reads at tray scale.
- [ ] Grab the handle, move / two-hand resize the tray: initiative track AND
      objectives follow every move and scale with the tray (≤1 frame lag while
      dragging is OK). Same after diorama world-grab/zoom.
- [ ] The tray (with track + objectives) stays up in TableIdle / other players'
      turns / half selection — CONFIRM/UNDO labels and enabled states keep
      mirroring the game.
- [ ] **Drops accept what glows (test #15 fix)**: hold a card over a slot until
      the gold glow shows, then release — the drop MUST accept, even if the
      release gesture moved your hand. The log line now carries the rule:
      `Drop (…): … rule=highlight → play into slot 1.` (`rule=radius` = fallback
      capture at 0.25 m real; `rule=none` = return to fan).
- [ ] **PIN follow-toggle**: poke (or laser-click) the small PIN button on the
      bottom-right frame corner. Label flips FOLLOW ↔ PINNED (accented while
      pinned); persisted as `[Cards] TrayFollow`.
      - FOLLOW (default): tray moves with you (world grab, snap turn, recenter)
        and re-places at the configured head offsets on mode entry.
      - PINNED: the tray keeps its exact current world pose — walk/teleport/turn
        away and back: it has not moved. Round changes do NOT re-place it.
      - Toggle back to FOLLOW → it re-anchors at the configured rig offsets.
- [ ] **Tray grab during dialogs (ModalUI)**: open any dialog (short-rest
      confirm, story box). Gripping the handle still moves/resizes the tray;
      the PIN/SET/CONFIRM/UNDO pokes still respond. Cards do NOT react to grabs
      (fan closed, slotted cards refuse the pluck) until the dialog closes.
- [ ] SET (gear) on the tray toggles the same in-VR settings panel as the
      table-edge gear / short A-X chord.
- [ ] Hot reload (F6) mid-scenario: dashboard rebuilds, track/objectives re-dock,
      pinned pose stays pinned (world), converted panels release/re-convert
      cleanly (2D intact after VR off).

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

## 5b. Laser vs. world panels / board (P6)

- [ ] Reticle keeps a CONSTANT apparent size while zooming the diorama in/out (it
      used to balloon when zoomed out); same for the beam thickness.
- [ ] Open the settings panel (physical SET button): the dominant laser clamps to the
      panel, hovering widgets highlights them, TRIGGER clicks them — no more
      pass-through. Same for converted world dialogs.
- [ ] **Dot everywhere (test #13 fix)**: on a FLOATED modal dialog (story box —
      1920 px wide at host scale 0.7), sweep the laser across the WHOLE panel:
      the dot + clamped beam stay visible edge to edge, including the right
      half (the ModalUI visuals cone used to hide them past ~25° off the panel
      center while clicks kept landing). The log prints one
      `Ray-uGUI canvas …: world rect …` line per registered canvas for
      verification.
- [ ] While the beam is clamped to a panel or fan card, the trigger must NOT also
      fire a board click behind it (nearest UI wins).
- [ ] A fan card or miniature physically in front of a panel blocks the panel hover
      (no clicking through objects).

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

## Known limits (by design in P3b/P7)

- Confirmations (short-rest yes/no, burn/redraw, lose-card) are the game's 2D
  dialogs — world-space versions are P3c.
- CONFIRM and UNDO are physical on the board now; SKIP and the in-turn item/bonus
  bars remain 2D (the board is a persistent dashboard since test #15; slots are
  interactive only during card selection — see CONTROLBOARD.md §5-§8).
- Extra-turn card selection and multi-hand (multi-merc tab) flows fall back to the
  active hand only; switching mercs mid-selection uses the 2D tabs for now.
- Text sizes/poses of badge, captions, buttons and tokens are first-pass values —
  tune the `[Cards]` config and report back.
- The procedural card back is a placeholder pattern; drop-in bundle assets and
  their licenses are listed in `unity/CARD-ASSETS.md`.
