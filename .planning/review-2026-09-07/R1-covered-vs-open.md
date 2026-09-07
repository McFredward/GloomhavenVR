# R1 — "Die Karten ausschließlich in der Entscheidungsphase verdeckt und sonst offen sichtbar?"

Review lane R1, read-only. Base `origin/dev` @ `9f646c86`, ModBuild 479.
Every `file:line` below was read on 2026-09-07 against that commit. Game citations are
`/home/claw/gloomhaven_vr/decompiled/` (NOT present in a worktree — see correction C4).

---

## THE ANSWER

**Yes, with two exceptions, and one of them is a defect.**

Every surface that can draw a peer's ability card routes through `RevealGate.CardFaces`, and the
only thing that covers a card is the game's own `SelectAbilityCardsOrLongRest` window
(`RevealGate.cs:50`). Outside it, every surface draws the real front. Inside it, the two
card-property exemptions (`IsPubliclyRevealedCard`, `IsDiscardedCard`) re-open exactly the
populations the user ruled on. I traced all 16 face call sites and found no site that covers a card
the rulings say is open, and no site that opens a card the rulings say is covered.

The two exceptions to "sonst offen":

1. **A FALSE BACK, and it is a defect (F1).** A peer's mirrored DISCARD fan goes to **all backs**
   for the whole of that peer's short rest — and again during a long-rest burn pick. Not a secrecy
   verdict: the length belt refuses, because the owner's own arc drops the card the board is
   holding and the mirror's model walk does not. This is the ruling of 2026-09-07 evening item 3
   ("Die Fächer der piles … immer mit Vorderseiten … ohne Ausnahme") failing on the exact flow the
   user was looking at when he gave it.

2. **A correct back the user should know about.** During a short rest the sacrifice in the recess is
   covered — that is item 6 working — but it is covered *as an identity-known back*, and the rest of
   that character's discard fan stays open (subject to F1). So a peer sees every discard card except
   the one the game singled out. That is the design and it is right.

---

## FINDINGS, RANKED

### F1 — CONFIRMED — a peer's discard fan is ALL BACKS for the whole of their short rest

- `src/GloomhavenVR/Net/Remote/RemotePileFronts.cs:920-971` (`Resolve`) walks
  `CardsGameApi.GetPileWidgets(hand, burnt:false, …)` = `DiscardedAbilityCards`, filtered ONLY by
  `CardsGameApi.PileWidgetIsArcMember` (`src/GloomhavenVR/Cards/CardsGameApi.cs:4017` —
  `widget != null && widget.AbilityCard != null && !widget.IsLongRest`).
  `src/GloomhavenVR/Net/Remote/RemotePileFronts.cs:604-616` then refuses every front when
  `modelCount != _arts.Count` (`Gate.CountMismatch`).
  `_arts.Count` is the OWNER's wire count: `RemoteBrowserFan.cs:969-986` → `_owner.PileBrowseCardCount`
  → `NetAvatarDriver.cs:2190` ← `NetAvatarDriver.cs:1175` `browseNow.Cards.Count`.
- **Mechanism.** The owner's arc is `CardsDriver.6.Flows.cs:3207` —
  `if (BoardOwnsCardVisual(card) || CardEnRouteToPile(card) || _fanBuffer.Contains(card)) continue;`
  — and `BoardOwnsCardVisual` (`CardsDriver.6.Flows.cs:2927-2933`) lists
  `|| ReferenceEquals(card, _shortRestCard)` **by name**. `CardsHandUI.PerformShortRest`
  (`decompiled/GH.Runtime/CardsHandUI.cs:751-765`) indexes `DiscardedAbilityCards` and **removes
  nothing**, so the sacrifice is still in the model list the mirror walks while the owner's own arc
  has already dropped it. Owner sends N-1; mirror resolves N; belt trips; whole fan goes to backs.
  The belt block's own comment (`RemotePileFronts.cs:589-600`) names this residue and calls it a
  transient — for a short rest it is not a transient, it **stands for the whole decision**.
- **Failure scenario.** Online scenario. Co-player takes a short rest; the sacrifice lies in his left
  recess while he decides accept-or-redraw. He raises his DISCARD pile fan to see what he has spent.
  On my Quest 3 his mirrored discard fan shows N-1 **card backs** for as long as that fan is up,
  while on his own board every one of them is a front.
- **Second trigger, same line.** `BoardOwnsCardVisual` also lists `_fieldCards` (the pick drop
  field). A **long rest's burn step** is `CardHandMode.LoseCard` over the DISCARD pile, so laying the
  chosen card in the field drops it from the owner's arc and trips the same belt — in the phase the
  user has ruled must be "alles offen".
- **REACHABLE ON HARDWARE THIS ROUND: YES.** No new build needed to observe it; the co-player only
  has to open his discard fan during a short rest or a long-rest burn.
- **The reading that convicts, and BOTH halves are already shipped** (anchor every grep on the
  literal `GloomhavenVR] `):
  - **Owner side, and it states the defect in one line:**
    `[Cards] Pile fan content (Discard): borrowed <N-1> of <N> pile card(s) into the arc; 1 left on
    the control board` (`CardsDriver.6.Flows.cs:3259`). A non-zero `left on the control board` while
    a discard fan is open IS the divergence — nothing else needs to be correlated to see it.
  - **Observer side:** a `[Net] PEER CARD FACE CENSUS` row reading `pile browse[pN] 0 FRONT / M BACK`
    whose rule string is the `Gate.CountMismatch` sentence ("…a different LENGTH from the arc they
    are looking at…"), beside the owner's `Pile-browse SENT: Discard fan open, <N-1> card(s)`.
  - A `Gate.BurnException` row carrying fronts is the fan working.
- **I READ THE SHIPPED LOGS. F1 IS CONFIRMED IN CODE AND HAS NEVER BEEN SAMPLED — and the reason is
  itself measurable.** Across the two ModBuild **478** drops (`.planning/debug/Player.log` and
  `.planning/debug/remote/Player.log`), anchored on the literal `GloomhavenVR] `:
  - **17 of 17** `[Cards] Pile fan content` lines read **`0 left on the control board`**. The
    divergence F1 needs has not occurred once: nobody opened a pile fan while a card's visual was on
    the board. So F1 is not falsified by these logs — it is **unasked** by them.
  - **0 of 167** `PEER CARD FACE CENSUS` prints contain the `Gate.CountMismatch` sentence for a pile
    browse. The belt has never fired on this surface in a shipped log.
  - The 100 all-backs pile-browse ticks that ARE in those logs
    (`pile browse[p2] 0 FRONT / 4 BACK` ×53, `pile browse[p1] 0 FRONT / 5 BACK` ×47) name a
    *different* rule — `RevealGate.ShowRoundCardFronts(actor)=false — the game's own secret
    SelectAbilityCardsOrLongRest phase` — which is the **ModBuild 478 pre-fix behaviour** the user
    reported as item 3 and which ModBuild 479 fixed. **Do not read those rows as evidence about the
    current build**: they measure the code the 479 pile-fan change replaced.
  - ⇒ The next hardware round is the first that can answer F1 at all, and one deliberate action
    answers it.

### F2 — CONFIRMED — the one guard against the ModBuild 477 leak returning never runs in CI

- `.github/workflows/ci.yml:147-152` and `.github/workflows/release.yml:220-224`:
  **`dotnet build` only.** Both steps print a notice saying the assertions are not executed.
- `tests/GloomhavenVR.WireTests/CardIdentityMaskVectors.cs` is registered
  (`tests/GloomhavenVR.WireTests/Program.cs:208`) but lives in the same executable as the golden wire
  vectors, which cannot run on a hosted runner (they need the game's real `UnityEngine.CoreModule.dll`
  for `Mathf` banker's rounding).
- **Mechanism.** `CardIdentityMaskVectors` is a **pure source lint** — it `File.ReadAllText`s
  `src/GloomhavenVR/Net/RevealGate.cs` and `DecisionLabelMask.cs` and runs three regexes
  (`CardIdentityMaskVectors.cs:118, 153, 179`). It touches no Unity type and would run happily on any
  runner. It is blocked purely by co-location.
- **Failure scenario.** Someone implements the obvious one-liner — folding `IsDiscardedCard` into
  `PeersMayNameOurCard` — every gate in the repository goes green, the build ships, and
  `DecisionLabelMask.AddCovered` (`DecisionLabelMask.cs:390`) stops masking the short-rest
  sacrifice. On the next hardware round every peer's mirrored decision row reads
  `Verbrennen "Zusatzdolch"` inside the secret window: ModBuild 477 item 7, live again, with the
  file's own doc comment still claiming the lint prevents it.
- I ran the lint's three regexes by hand against the shipped source: **it is GREEN today**
  (`PeersMayNameOurCard` = `PeersSeeOurCardFronts || IsPubliclyRevealedCard(actor, cardInstanceId)`;
  `PileFrontsReach` = `!= SacrificedCard && != BoardPickSeat`; `AddCovered` calls
  `PeersMayNameOurCard` and none of the three forbidden names). The rule holds — nothing enforces it.
- **REACHABLE ON HARDWARE THIS ROUND: NO** (it is a process defect, not a picture). Cheap to close:
  split the source lints into their own project, or gate them behind a `--lints-only` argument the CI
  step passes.

### F3 — PLAUSIBLE — extension record 7 is a second unmasked prose channel, and its send log makes the same false claim record 12 made

- `src/GloomhavenVR/Net/Avatar/NetAvatarDriver.cs:2282-2292` publishes `PlayTray.PickBannerText`
  verbatim. It is the ONLY string channel of the five that is neither masked nor identity-gated:
  `DecisionLinesText` and the two cap labels go through `DecisionLabelMask`
  (`NetAvatarDriver.cs:1077, 1896-1897`), `BoardTooltipText` is gated at
  `WorldTooltips.ContentPublicToPeers`, `DecisionNamesText` is gated in `DamageTooltipSurface`.
  Record 7 has no gate at all.
- Its send log asserts *"an actor and a count, NO card identity"* (`NetAvatarDriver.cs:2298`).
  **That is the identical assertion record 12's send log made before the 477 leak**, in the identical
  place, about a string it does not inspect.
- The mechanism that could make it false is in the mod's own comment:
  `src/GloomhavenVR/Cards/Driver/CardsDriver.6.Flows.cs:816-821`
  ```
  // The popup's own game-localized title IS the ask ("Choose a hero to prevent the
  // damage…" / the redistribute card's name); the mod adds only the wayfinding.
  string hint = Core.Loc.Mod("panel_float_hint");
  line = !string.IsNullOrEmpty(title) ? title + " — " + hint : hint;
  _tray.SetPickStatus(line, null, null);
  ```
  `title` is `CardsGameApi.DistributePanelState`'s out-parameter — a `UIScenarioDistributePointsManager`
  popup title — and the comment says it can be a card's name.
- **Why PLAUSIBLE and not CONFIRMED.** I traced the four other banner composers
  (`CardsDriver.6.Flows.cs:740, 821, 1048, 1247`) and they carry only an actor label, a verb, counts
  and Loc keys — clean. For this one I did not establish (a) whether the popup title is an ABILITY
  card of a player (the sanctioned secret) or a scenario/monster card (already public), nor (b)
  whether a distribute-points panel can be open while `IsSecretSelectionPhase` is true. Both are one
  read of `DistributePanelState` and one of `UIScenarioDistributePointsManager` away.
- **Failure scenario, if both hold.** A distribute-damage popup naming a player ability card is open
  during the selection window; record 7 publishes that title unmasked; every peer's mirrored board
  reads the card's name on the placard above it, with the sender's log line asserting no identity
  travelled.
- **REACHABLE ON HARDWARE THIS ROUND: PARTLY.** The channel is live every round. Grep the owner's
  `Pick banner SENT:` lines against `PHASE=` in the census — a banner quoting a card name while
  `RevealGate.PeersSeeOurCardFronts` is shut is the convicting reading. There is **no log line today
  that pairs record 7 with the phase**, which is why this cannot be settled from the existing logs.

---

## CENSUS — every place in `src/` that decides FRONT vs BACK

### How I enumerated

The mirrored (peer) side has exactly **one physical mechanism**:
`Cards.CardMesh.SetBodyFrontFace(Transform, bool showsBack)` (`Cards/Art/CardMesh.cs:553`) plus
`RemoteCardArt.ShowFront` / `HideFront`. `grep -rn "SetBodyFrontFace\|ShowFront\|ShowFace\|SetAnonymousBack"
src/ --include=*.cs` returns 6 writers and they are the whole set:
`RemotePileFronts.cs:383`, `RemoteHeldCardFace.cs:763`, `RemoteCardFx.cs:770`,
`RemoteBurnFx.cs:1016`, `RemoteHandFan.cs:1150/2010`, `RemoteBoardCard.cs:410`.

Every one of those is fed by `RevealGate`. `grep -rn "RevealGate\.CardFaces("` returns **16
decision sites** across 7 files; that grep plus the 6 writers is the closure — no writer is reached
without a `CardFaces` verdict on the same path.

**The local side has no BACK at all.** `CardFace.Adopt` (`Cards/Art/CardFace.cs:179`) re-hosts the
game's own `FullAbilityCard` rect; there is no back mesh and no hiding path on that pipeline. A local
card is therefore FRONT or NOT DRAWN, never BACK. `CardFan.FanMode.Picture`
(`Cards/CardFan.cs:509-513`) changes only `Grabbable`/`InspectOnly` — see correction C1.

### The table

Rows = surface. `F` = FRONT, `B` = BACK, `–` = not drawn. Population in brackets.

| Surface (file) | Selection phase | Short rest | Long rest | Action phase | Damage-negation burn | Held in hand | Discard fan | Burnt fan | Items fan | Active pile | In flight to pile | Map room | Half-loaded save |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **Peer hand fan** `RemoteHandFan.cs:1594` `[Selectable/PickFan]` | **B** | **B** | **F** | F | F (hand pick) | n/a | n/a | n/a | n/a | n/a | n/a | **F** `[MapLoadout]` | – |
| **Peer held card** `RemoteHeldCardFace.cs:211,275` `[Selectable/ItemCard]` | **B** | **B** | **F** | F | F once in `LostAbilityCards` | — | **F** (`IsDiscardedCard`) | **F** (`IsPubliclyRevealedCard`) | **F** `[ItemCard]` | **F** (`IsPubliclyRevealedCard`) | n/a | **F** | **B** |
| **Peer round recess** `RemoteControlBoard.cs:2739` `[Selectable]` | **B** (anon) | **B** | **F** | F | **F** | n/a | n/a | n/a | n/a | n/a | n/a | – | – |
| **Peer sacrifice recess** `RemoteControlBoard.cs:2579` `[SacrificedCard]` | n/a | **B** (identity-known) → **F** on accept | **F** | F | **F** | n/a | n/a | n/a | n/a | n/a | n/a | – | – |
| **Peer pile-browse fan** `RemotePileFronts.cs:481,666` `[Selectable]` | **F** per card | **B — see F1** | **F**¹ | F | F | n/a | **F** | **F** | n/a | n/a | n/a | – | – |
| **Peer item fan** `RemotePileFronts.cs:481` `[ItemCard]` | **F** | **F** | **F** | F | n/a | **F** | n/a | n/a | **F** | n/a | n/a | – | – |
| **Peer active matrix** `RemoteActiveCards.cs:408` `[AlreadyPublic]` | **F** | **F** | **F** | F | F | **F** | n/a | n/a | n/a | **F** | n/a | – | – |
| **Peer flight slab (departure)** `RemoteCardFx.cs:504` `[Selectable]` | **B** | **B** (no departed face) | **F** | F | **F** | n/a | **F** | **F** | n/a | **F** | **F** | – | – |
| **Peer flight slab (arrival)** `RemoteCardFx.cs:576` `[BoardPickSeat]` | **B** | **B** | **F** | F | **F** | n/a | n/a | n/a | n/a | n/a | **F** | – | – |
| **Peer burn slab** `RemoteBurnFx.cs:624` `[Selectable / AlreadyPublic]` | **F** | **F** | **F** | F | **F** | n/a | n/a | **F** | n/a | n/a | **F** | – | – |
| **Peer legacy recess** `RemoteControlBoard.cs:2457` `[Selectable]` | **B** | **B** | **F** | F | **F** | n/a | n/a | n/a | n/a | n/a | n/a | – | – |
| **LOCAL hand fan / recesses / piles** `CardFace.Adopt` | **F** (own) / **–** (foreign, C1) | F | F | F | F | F | F | F | F | F | F | F | – |

¹ except when the burn pick's field occupancy trips F1's belt.

**Per-surface yes/no to the maintainer's question** — is this card covered *only* in the decision
phase and open otherwise? Peer hand fan: **yes**. Peer held card: **yes**. Round recesses: **yes**.
Sacrifice recess: **yes** (covered in the short rest, which *is* the decision phase; opens the instant
the accept commits it). Item fan / active matrix / burn slab: **yes — never covered at all**, by
ruling. Flight slabs: **yes**. Burnt fan: **yes — never covered**. **Discard fan: NO — it is also
covered during a short rest and a long-rest burn pick, and that is F1.**

---

## THE THREE THINGS YOU ASKED ME TO VERIFY

### 1. The `PeersMayNameOurCard` / face split — HONOURED AT EVERY CALL SITE. Clean.

Five naming sites, and every one decides a **sentence**, never a picture:

| Site | Decides |
|---|---|
| `Net/DecisionLabelMask.cs:390` (`AddCovered`) | which of our card NAMES are stripped from a wire wording |
| `Net/DecisionLabelMask.cs:220,460,534` | the mask's fast path + its own log lines |
| `WorldUI/Tooltips/WorldTooltips.cs:975` | whether the hovered card's tooltip TEXT may be transmitted |
| `WorldUI/Tooltips/WorldTooltips.cs:864,985` | the phase fallback for a hover with no nameable card, + log |
| `WorldUI/Surfaces/DamageTooltipSurface.cs:564,567` (`CardIsPublic`) | the mandatory-use hint's card-name KEYS |
| `Net/Avatar/NetAvatarDriver.cs:2343,2368,2639` | log lines only |

Sixteen face sites (`CardFaces`), and not one of them decides a text row. **No site asks the naming
question to decide a face, and no site asks the face question to decide a label.** I also checked the
argument of every `CardFaces` call: all pass a real `CAbilityCard.CardInstanceID`; the single
`int.MinValue` is `RemoteActiveCards.cs:409`, where the population is `AlreadyPublic` and the id is
never consulted (deliberate, and the doc says so).

### 2. `PileFrontsReach`'s scope — EXACTLY the ruling. No wider, no narrower. Clean.

`RevealGate.cs:883` refuses `SacrificedCard` and `BoardPickSeat` and nothing else. I checked the
short-rest sacrifice on **all four** surfaces that could draw it, and the naive widening you warned
about cannot arrive by any of them:

- **The recess** — `RemoteControlBoard.cs:2579` declares `SacrificedCard`. `PileFrontsReach` false ⇒
  `IsDiscardedCard` never asked ⇒ `FaceRule.SelectionPhaseCovered`. Covered.
- **The fly-in (Discard→Slot0)** — `RemoteCardFx.cs:576` declares `BoardPickSeat`. Covered.
- **The fly-out on a redraw (Slot0→Discard)** — declares `Selectable` (`RemoteCardFx.cs:504`), which
  DOES get the discard exemption. It is nevertheless safe, and I traced why: the sacrifice branch
  nulls `_latchedFaces[i]` (`RemoteControlBoard.cs:2600`), `NoteRecessDeparture` takes only that
  latch as its argument (`:2218`), so `_departedFace[slot]` is never stamped for a sacrifice recess
  and `ResolveFace` returns a BACK before `CardFaces` is reached. **This is the one place the scope
  is held by an unrelated mechanism rather than by the term** — worth knowing, because a future
  change to the latch could open it.
- **The round-card walk** — cannot name the sacrifice at all: `OrderRoundCards` reads only
  `RoundAbilityCards`, and during a short rest `PerformShortRest` has run `DeselectAllCards`, so
  `modelCount(0) != occupiedCount(1)` ⇒ `compact=false` ⇒ the walk is refused and the recess falls to
  its latch (cleared by `!showFronts`, `RemoteControlBoard.cs:2412-2421`) ⇒ `SetAnonymousBack`.

The exemption reaches `Selectable`, `AlreadyPublic`, `ItemCard`, `DecisionRowWording` and `PickFan`.
For `Selectable` that is what delivers item 3 to the held card and the pile arcs; for `PickFan` it is
a discard-pile browse the owner is stepping through, which is the ruling.

### 3. A face decided by LOCATION rather than by card property — three, all currently sound; one worth watching

- **`RemoteHeldCardFace.cs:262-276`.** The card-property exemptions are asked **only** when the wire's
  `HeldFaceList` byte says Active, Burnt or Discard. That byte is the sender's read of
  `widget.CardType` / `VRCard.PileOrigin` (`LocalRigSampler.cs:458-595`) — i.e. **where the card was
  lying**, not what it is. A card whose model has committed to `LostAbilityCards` while its widget
  still reads `CardType.Hand` would be sent as `HeldFaceListHand`, the peek would be skipped, and the
  burn ruling would not reach it. Safe today because such a card is dropped from the hand by
  `CardsGameApi.HandFanMember` (which folds in `ClassifyHandExit`), so the seat resolves to -1 and the
  sender names nothing — a back, which is the safe direction. **This is the single most likely place
  for the next "ich sehe nur die Rückseite" report.**
- **`RemoteBurnFx.cs:622-628`** falls back to the exempt population `AlreadyPublic` when the card id
  is unresolvable. That is a PROVENANCE argument, and I verified it rather than trusting it: `Present`
  is reached only from `RemoteBurnFx.cs:462`, inside the `GetPileWidgets(hand, burnt:true)` walk
  (`CardsGameApi.cs:3972-3987` = `LostAbilityCards` + `PermanentlyLostAbilityCards`), narrowed by
  `PileWidgetIsArcMember`. The construction holds. **Sound.**
- **`RemotePileFronts.cs:481`** picks the population from `content == Content.Items` — a place. Sound,
  because the two arcs it separates are an item arc and an ability arc, which is a KIND distinction
  wearing a place's clothes; and the file explicitly removed the `content ==` test from the ability
  side (`:504-517`), which was the fix.

---

## WHAT I CHECKED AND FOUND CLEAN

Stating these because a clean reading sizes the test.

- **The face/naming split** — 5 naming sites, 16 face sites, no crossover (verification 1 above).
- **`PileFrontsReach` scope** — exact, on all four sacrifice surfaces (verification 2 above).
- **`CardFaces` arguments** — every site passes a real `CardInstanceID`; no card TYPE id anywhere; the
  one `int.MinValue` is deliberate and inert.
- **`DecisionLabelMask` coverage** — it masks BOTH the translated title (`Loc.Game(name)`) and the raw
  YML key (`DecisionLabelMask.cs:396-408`), which is the 477 fix; it walks `HandAbilityCards`,
  `DiscardedAbilityCards`, `RoundAbilityCards`; a blind read (`complete == false`) **withholds** the
  whole wording rather than publishing it (`:227-238`), and a throw does the same (`:262-273`).
  Both cap labels (record 13 bits 0 and 2) go through the same mask (`NetAvatarDriver.cs:1896-1897`).
  I did not find a fourth ability-card list that a burn/lose prompt could draw its subject from.
- **`RemoteBrowserFan` and `RemoteItemFan` own no face decision** — both delegate to
  `RemotePileFronts`, so there is exactly one owner of the pile-arc face. Confirmed by grep: neither
  file contains a `RevealGate` call or a `SetBodyFrontFace`.
- **`PeerCardFaceCensus` covers all 8 surfaces it declares.** `HandFan`, `HeldCard`, `RoundSlots`,
  `ActiveMatrix`, `FlightSlab`, `BoardPickSeat` report directly; `PileBrowse` and `ItemFan` both
  report through `RemotePileFronts.Census` (`:296`), which picks the surface by content. Every row
  carries the deciding rule string and a `PeakBacks`/`PeakRule` pair, so a surface that is wrong for
  one second between cadence ticks does not read as healthy. **The instrument is adequate for F1** —
  it will print the `CountMismatch` rule verbatim.
- **The local board cannot draw a foreign hand inside the secret window**, and the guarantee is real
  but INDIRECT — see correction C1.
- **`RevealGate` degradation** — every predicate degrades to "show less" as documented; `InMapPhase`
  keeps `InScenario` inside its `try` (`:390`), and `IsSecretSelectionPhase` reads
  `PhaseManager.PhaseType`, which needs no save data. I found no negated conjunction that folds to
  SHOW on a missing read.

---

## CORRECTIONS TO THE BRIEF

**C1. "Vanilla's own reveal rule hides a remote actor's card fronts" is FALSE, and the mod's local
board relies on input guards rather than on any gate in the card pipeline.**

`RevealGate.cs:10` claims the mod is "identical to the vanilla client's reveal rule
(`AbilityCardUI`)". Read at source, vanilla's four cited lines
(`decompiled/GH.Runtime/AbilityCardUI.cs:980, 1024, 1100, 1188`) do **not** hide a front. They only
(a) force `fullAbilityCard.DisplaySelected(false)` and (b) swap the mini card's display mode to
`unselectedCardType`. Vanilla's secret is *which two cards are SELECTED*, not the hand contents —
and `CardsHandManager.ShowTabs` (`decompiled/GH.Runtime/CardsHandManager.cs:718-726`) activates the
character tabs **precisely when** `PhaseType == SelectAbilityCardsOrLongRest`, with
`CardsHandTabs.UpdateTabsInteraction` (`CardsHandTabs.cs:132-139`) making every non-dead tab
clickable **with no ownership test at all**. In the flat game you can tab to a teammate's hand during
the selection phase and read it.

This matters for the mod because `CardFan.FanMode.Picture` — whose comment at
`CardsDriver.4.Rebuild.cs:1489-1490` reads *"a hand whose FRONTS may not be drawn at all … Inert end
to end"* — **does not hide any face**. `CardFan.StampMode` (`Cards/CardFan.cs:509-513`) sets only
`Grabbable=false; InspectOnly=false`, and `FillHandFan` (`CardsDriver.4.Rebuild.cs:1910-1934`) adopts
every hand widget's `FullAbilityCard` with no reveal-gate term whatsoever. That comment is an
assertion in the source and it is false about the faces.

The picture is nevertheless safe, and I traced all three seams that keep it so:
1. `CharacterFocus.Refusal` (`Board/CharacterFocus.cs:274`) refuses a focus during the secret window,
   so `PresentedHandCore` falls back to the game's own hand.
2. `SelectionGuardPatches` (`Board/Patches/SelectionGuardPatches.cs:145-150`) refuses a portrait click
   on a foreign-controlled character via `CardsGameApi.IsForeignControlledSelect`.
3. `Choreographer_TileHandler_OwnershipGuard` (`SelectionGuardPatches.cs:365-395`) blocks the
   tile-click `SwitchHand` in `WaitingForCardSelection`.
   Plus `HandSuppression` holds the 2D hand window at `blocksRaycasts=false`
   (`Cards/Patches/HandSuppressionPatches.cs:32-36`), so the vanilla tabs are unreachable in VR.

**This is a NOTE, not a finding — I could construct no reachable failure scenario.** But it is worth
recording that the safety of the local board rests entirely on three input guards, and that the
comment claiming the fan itself is inert would let the next reader delete one of them.

**C2. `CardIdentityMaskVectors` does not fail the build.** See F2. Your brief states it does; it is
compile-only in both workflows.

**C3. `RemoteBrowserFan.cs` and `RemoteItemFan.cs` are in your scope list but contain no face
decision.** Both delegate to `RemotePileFronts`. Not a criticism of the list — worth saying so the
next lane does not re-read 215 KB for nothing.

**C4. `decompiled/` is gitignored and is therefore ABSENT from a worktree.** Ground truth had to be
read from `/home/claw/gloomhaven_vr/decompiled/` in the main checkout. Worth putting in the next
brief, since "cite `decompiled/GH.Runtime/file:line`" reads as though the directory is present.

---

## NOTES (no failure scenario)

- **N1.** `RemoteHandFan.cs:1690` gates the whole per-card exemption fallback on `pickFan`, justified
  at `:1682-1687` by *"the game moves a card's widget out of `CardPileType.Hand` in the same step it
  commits the model."* The mod's own item-10 belt exists because that is not always true —
  `CardsDriver.4.Rebuild.cs:1940` opens with *"does the RULES MODEL already say this card has left the
  hand, whatever the widget's `CardType` still claims?"*. The two statements contradict each other.
  Inert today (the divergent card is dropped by `HandFanMember` and the length belt handles the rest),
  but the justification is weaker than it reads.
- **N2.** `PileFrontsReach(population) && IsDiscardedCard(...)` is spelled **twice** —
  `RevealGate.cs:1219` (`CardIsPubliclyVisible`) and `RevealGate.cs:1379` (the `out rule` overload).
  Identical today. Nothing pins the *routing*: `CardIdentityMaskVectors` pins the `PileFrontsReach`
  expression's contents but never checks that anyone calls it, so deleting the guard from
  `CardIsPubliclyVisible` passes every gate and opens the sacrifice on the three-argument path.
- **N3.** `RevealGate.cs:686-687` (inside `PeerCardPopulation.PickFan`'s flow list) still asserts
  *"items … are drawn by `RemoteItemFan` through `ShowRoundCardFronts`"*. That is the sentence the
  `ItemCard` member (`:465-489`) was added to correct, and the correction did not reach this copy.
  Documentation only.
- **N4.** `RemoteControlBoard.cs:960-966`'s census rule string names `ShowRoundCardFronts` while the
  per-recess face is actually chosen by `CardFaces`. The file acknowledges this at `:1541-1550`.
  Harmless, but it is a rule string that can disagree with its own numerator.

---

## SIZING THE NEXT HARDWARE TEST

One round can settle everything above:

1. **F1.** Co-player takes a short rest and **opens his discard fan** while the sacrifice is in the
   recess. Then repeats it during a **long-rest burn pick** with the chosen card laid in the field.
   Read: the owner's `Pile fan content (Discard): borrowed X of Y … Z left on the control board`
   (`Z > 0` is the defect on its own), and the observer's `pile browse[pN]` census row. Costs two
   extra actions in a rest the tester already does.
   I already checked the existing drops: **17/17 read `0 left on the control board`**, so they are
   silent on F1 rather than exonerating. The hardware action is required.
2. **F3.** No new action needed — grep the owner's `Pick banner SENT:` lines for a card name and pair
   them by timestamp with a census row reading `POLICY=` inside the secret window. If a
   distribute-points panel never came up, the answer is "not put" and not "clean".
3. **F2** needs no hardware.
4. **The falsifier for the whole review**, already shipped and worth grepping every round:
   ```
   grep -a 'GloomhavenVR] \[Net\] PEER CARD FACE CENSUS' Player.log | grep 'POLICY=FRONTS' \
     | grep -v 'and 0 showing a BACK right now' | grep -c 'ShowRoundCardFronts(actor)=false'
   ```
   A non-zero reading falsifies `FaceRule.ActionPhaseOpen`'s standing claim that a long rest never
   evaluates inside the secret window. It read ZERO across ModBuild 472's pair of logs; if it ever
   reads non-zero, the term to add is `CCharacterClass.LongRest && !ImprovedShortRest`
   (`RevealGate.cs:1274-1279`).
