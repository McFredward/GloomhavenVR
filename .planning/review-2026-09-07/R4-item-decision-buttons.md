# R4 — Item decision buttons: are they necessary, could the item flow carry them, are they 1:1?

**Read-only review. Base: `origin/dev` `9f646c86`, ModBuild 479. All lines read 2026-09-07.**
Decompiled ground truth cited from `/home/claw/gloomhaven_vr/decompiled/` (not present in the
worktree — it is gitignored; `scripts/worktree-setup.sh` does not link it).

Maintainer's question, verbatim: *"Entscheidungsbuttons: Sind die Entscheidungsbuttons von Items
wirklich notwendig die angezeigt werden, oder können sie über den Item flow abgebildet werden (nur
bei 'aktivieren' eines Items). Sind sie 1:1 auf dem remote board."*

**Short answer.** (i) Everything that CAN go to the item flow already has — he ruled on this on
2026-08-24 and again on 2026-09-07 and the mod implements both rulings. (ii) What is left is five
widgets, and for each of them I can name the game state that only that widget can leave; one of
them is a genuine deadlock exit. (iii) They are **not** 1:1, and not by a small margin — the
mirrored bar is a mod-drawn quad row with a caption column that exists nowhere on the owner's
board. Record 45 fixed the *icon* on that quad; it did not make the quad a widget.

---

## Verification of the brief's load-bearing claim: CORRECT, and stronger than stated

The brief asked me to check "the watcher's own copy of that bar is EMPTY BY CONSTRUCTION".
It is, and `ShowOtherPlayer` does more than merely not raise the bars — it **hides them**.

`decompiled/GH.Runtime/TakeDamagePanel.cs:1102-1134`, whole body read today. It assigns fields,
finds the HUD controller, calls `ResetToggles()` at `:1122`, sets a help-box string, previews the
damage number, and ends on `myWindow.Hide(instant: true)` at `:1133`. `ResetToggles()`
(`TakeDamagePanel.cs:428-440`) contains `Singleton<UIUseItemsBar>.Instance.Hide()` at `:434` and
`Singleton<UIActiveBonusBar>.Instance.Hide()` at `:435`. So the watcher's bars are not merely
un-raised, they are actively cleared.

`decompiled/GH.Runtime/UIScenarioMultiplayerController.cs:212-249` routes a non-controlling client
there at `:242`, and `:242` is the **only** call site of `ShowOtherPlayer` in the tree.
`TakeDamagePanel.Show` has exactly two call sites — `UIScenarioMultiplayerController.cs:246`
(online) and `Choreographer.cs:5505` (offline) — and only `Show` reaches
`UIUseItemsBar.ShowItems` (`TakeDamagePanel.cs:249`) and
`UIActiveBonusBar.ShowReduceDamageActiveBonuses` (`:265`, `:269`).

**One correction to the brief and to four places in the source.** The branch is *not* on "the
attacked actor's `IsUnderMyControl`". It is on `actorToShowCardsFor ?? actorBeingAttacked` — the
**card owner** (`UIScenarioMultiplayerController.cs:216-218`): a `CPlayerActor` uses its own
`IsUnderMyControl` (`:238`), a `CHeroSummonActor` uses `Summoner.IsUnderMyControl` (`:233`), and a
`CEnemyActor` uses `FFSNetwork.IsHost` (`:229`). The conclusion is unaffected; the wording is wrong
in `Net/UseBarSlotIdentity.cs:29`, `Net/Remote/RemoteUseBarSymbols.cs:69-70`,
`Net/NetProtocol.cs` (record-45 block) and
`tests/GloomhavenVR.WireTests/UseBarSlotIdentityVectors.cs:10`.

**Second correction, this one with teeth (see F1).** The same paragraph generalises further than it
should in the other direction: for the bars the record *does* carry, the local resolve is not always
dead. `Choreographer.cs:11678` calls
`Singleton<UIActiveBonusBar>.Instance.ShowActiveBonus(actor, AdjustInitiative)` **on every client,
with no `IsUnderMyControl` test** (the gate at `:11685` is only on the ready button's
interactability). So bar 0 *is* populated on a watcher for the initiative-adjustment prompt, and the
zero-wire resolve worked there on ModBuild 478.

---

# FINDINGS

## F1 — CONFIRMED — record 45 deletes the local fallback for the WHOLE bar, not just the slots it names

**(a) Where.** `src/GloomhavenVR/Net/Remote/RemoteBoardFurniture.cs:3628-3670`.

**(b) Mechanism that actually runs.** Per slot `s` of a mirrored row:

```
:3634  if (k >= wireIds.Length || wireIds[k] == UseBarSlotIdentity.NoIdentity)
:3636      continue;                 // <- barNamed NOT incremented for this slot
:3637  barNamed++;
...
:3661  if (barNamed > 0)             // ONE named slot anywhere in the bar is enough
:3663      why = resolved > 0 ? None : NoWireSlots;
:3667  else if (bar >= 0)
:3669      resolved = RemoteUseBarSymbols.Resolve(bar, actor, symbols.Length, _symbolScratch, out why);
```

A single named slot suppresses `RemoteUseBarSymbols.Resolve` for **every** slot of that bar. The
comment at `:3660-3666` states this deliberately ("Slots they named that would not resolve stay
ANONYMOUS — never filled in from the local bar"), but it treats "the owner named nothing for this
slot" and "the owner named something I could not resolve" as one case. They are not: the first is
the sender's own withholding, and the local arm is exactly the source that was answering it.

The sender withholds an id *by design* for one model type:
`src/GloomhavenVR/Net/UseBarSlotSymbol.cs:98-99` (`TakesPlainIcon` is false for
`CForgoActionsForCompanionActiveBonus`) applied at `:142-144`. And that row is **kept on the bar** by
the split: `CardsGameApi.BonusNeedsFurtherOption` returns true for it
(`src/GloomhavenVR/Cards/CardsGameApi.cs:1500`), so `EnforceActiveBonusSplit` does not hide it
(`src/GloomhavenVR/WorldUI/Surfaces/UseBarsSurface.cs:1608-1610`). A mixed bar — one ordinary bonus,
one forgo bonus — is therefore the *normal* product of the mod's own split.

**(c) FAILURE SCENARIO.** A co-player is in `CheckForInitiativeAdjustments` with two rows on
`UIActiveBonusBar`: an ordinary toggle bonus and a forgo-actions-for-companion bonus.
`Choreographer.cs:11678` has raised that bar on the maintainer's machine too, for that same actor,
so on **ModBuild 478 both icons drew** through `RemoteUseBarSymbols.IconOf`
(`RemoteUseBarSymbols.cs:339-351`, reading `UIUseActiveBonus.icon` — populated regardless of bonus
type). On **ModBuild 479** the sender names slot A and withholds slot B, `barNamed == 1`, the local
arm never runs, and the maintainer sees **one icon and one anonymous brown tile where last build
showed two icons**. The `DOCK MIRROR` line will report `1 mirrored use-bar slot(s) … 1 of them
resolved from the OWNER'S OWN record-45 id`, which reads like success.

The same shape follows from every *resolve* refusal, all reachable:

- **Song bonuses.** The bar adds them via `CActiveBonus.FindAllApplicableSongActiveBonuses` →
  `CCharacterClass.FindAllSongActiveBonuses` (`decompiled/…/CCharacterClass.cs:987-1000`), a
  different walk from the receiver's candidate set
  `CharacterClassManager.FindAllActiveBonuses(actor)` (`decompiled/…/CharacterClassManager.cs:93-125`),
  whose clauses need `actor == bonus.Actor` or aura range. A Soothsinger's song on an ally's bar ⇒
  `NoModelMatch`.
- **Scenario-modifier AURA bonuses.** In the bar's set (`CharacterClassManager.cs:219-222`, clause
  `IsAura ? ValidActorsInRangeOfAura.Contains(actor) : x.Actor == actor`) but **not** in the
  receiver's untyped overload (`:119-122`, clause `x.Actor == actor` only) ⇒ `NoModelMatch`.
- **Summon bonuses** and `Ambiguous` (16-bit collision) — both already named in
  `UseBarSlotSymbol.ResolveOutcome`'s own text (`UseBarSlotSymbol.cs:193-202`).

**Direction of failure is safe** — anonymous, never a wrong symbol. This is a *fidelity* regression,
not a correctness one.

**Reachable on hardware this round:** YES for bar 0 outside the damage prompt (initiative
adjustment, end-of-action toggles). NO for the prevent-damage prompt — the one he reported — where
the local arm never worked anyway, so there is nothing to lose there.

**The minimal shape of a fix** (not applied — this is a read-only lane): make the fallback per-slot
rather than per-bar, i.e. run the local resolve for the slots where the wire arm produced nothing.
Keeping "never mix a wire symbol with an inferred neighbour" as a *log* concern rather than a
suppression would cost one field and no wire bytes.

---

## F2 — CONFIRMED — the mirrored use-bar row is not 1:1, and carries a caption the owner never sees

**(a) Where.** `src/GloomhavenVR/Net/Remote/RemoteBoardFurniture.cs:3396-3535`, constants at `:3290`
(`UseBarRowH = 0.040f`), `:3305` (`UseBarTile = 0.026f`), `:3314` (`UseBarTileGap = 0.006f`), `:3319`
(`UseBarCaptionW = 0.150f`), `:3323`/`:3327` (invented tile and plate colours). Caption text:
`:3464-3469` via `UseBarCaption` `:3332-3338`, strings `src/GloomhavenVR/Core/Loc/Loc.cs:1358-1361`
("Aktive Boni", "Gegenstände", …).

**(b) Mechanism.** On the **owner's** board the widget is the game's own `UIUseActiveBonus` /
`UIUseItemScenario`, converted in place by `UseBarsSurface`'s `BarDock`
(`src/GloomhavenVR/WorldUI/Surfaces/UseBarsSurface.cs:313-317`) at the dock's own fit scale, with its
real `ExtendedButton` hover/press grow, no caption and no backing plate. On the **peer's** board it
is a 26 mm unlit quad (`:3511-3513`), an accent rim quad (`:3505-3509`), a `SpriteRenderer` icon
(`:3520-3527`), a 150 mm text column and a full-width plate. Nothing here is a `RemoteWidgetMirror`
clone; the source says so itself at
`src/GloomhavenVR/WorldUI/Surfaces/UseBarsSurface.cs:1548-1555` ("this row is NOT a
RemoteWidgetMirror clone. There are exactly five of those … this row is drawn by
RemoteBoardFurniture.SetUseBars, a mod-drawn quad drawer that cannot render at identity scale").

**(c) FAILURE SCENARIO.** A co-player gets the initiative-boots ± prompt. The owner sees the boots'
picker widget in the game's own art. The maintainer sees a brown square with an icon in it, an
accent rim, and the words **"Aktive Boni"** in a column beside it — a string that appears on no other
player's screen. Breaches CONTENT (the caption, the plate, the tile art), SIZE (26 mm against
whatever the dock's fit produced), POSITION (a caption column shifts every tile right) and ANIMATION
(the real widget grows on hover via `ExtendedButton.highlightScaleFactor`; the mirror toggles a rim
quad).

**Reachable on hardware this round:** YES — every prompt that keeps a bar row.

**Contrast, and this is the useful part of the answer.** The take-damage decision **row** *is* a real
clone (`RemoteDecisionWidgets` over `RemoteWidgetMirror`,
`src/GloomhavenVR/Net/Remote/RemoteDecisionWidgets.cs:26-38, 75-79`). So his 2026-08-09 complaint
about the damage buttons is answered. The bar hanging *below* that row is the part that is still a
lookalike, and record 45 improved only its icon.

---

## F3 — PLAUSIBLE — the receiver's slot walk is not the sender's walk, and the comment says it is

**(a) Where.** `src/GloomhavenVR/Net/Remote/RemoteUseBarSymbols.cs:218-227` against
`src/GloomhavenVR/WorldUI/Surfaces/UseBarsSurface.cs:2119-2131`.

**(b) Mechanism.** The comment at `RemoteUseBarSymbols.cs:221-223` reads "THE SENDER'S OWN WALK,
verbatim: active children only, non-slot children skipped." The sender's walk has a third term the
receiver's does not: `if (_owner.IsPlainRenderHidden(child)) continue;`
(`UseBarsSurface.cs:2125-2127`). A render-hidden plain item slot is still `activeSelf` — the
predicate's own doc says exactly that (`UseBarsSurface.cs:1398-1400`) — so the receiver counts a slot
record 25 dropped.

**(c) FAILURE SCENARIO.** The receiver's own `UIUseItemsBar` is up for the board actor with a plain,
render-hidden slot present. Gate 3 compares N+1 local slots against record 25's N and refuses; the
tiles go anonymous and the log prints `GATE 3 (slot count) … a stale or mid-rebuild local bar; the
next cadence tick normally clears it` (`RemoteUseBarSymbols.cs:173-176`) — the wrong diagnosis, and
it will cost a round to re-ask, exactly the failure the ModBuild-479 refusal-arm work was done to
prevent.

Fail-closed (honest blank). Requires gate 2 to pass for bar 3 first, which it cannot in the
prevent-damage prompt.

**Reachable on hardware this round:** LOW. Real, but narrow.

---

# THE BUTTON CENSUS, and the design answer

How I enumerated: (1) every `Show*` entry point of `UIUseItemsBar` / `UIActiveBonusBar` /
`UIUseAbilitiesBar` / `UIUseAugmentationsBar` and every caller of them in the decompiled tree; (2)
both mod-side splits (`EnforceItemsSplit`, `EnforceActiveBonusSplit`,
`ItemsPile.EnforceChoiceSlotSplit`) and their keep/hide predicates in `CardsGameApi`; (3) every wire
record in the item family (4, 7, 12, 13, 14, 23/24, 25, 26, 28, 29, 33, 35, 45) traced sender →
`PresenceState` → `RemoteAvatar` → the drawer that paints it; (4) the game's own item-decision
raisers outside the bars (`ItemCardRefreshPicker`, `ItemRewardLosePicker`, `UIElementPicker`,
single-target item pick, `UIItemConfirmationBox`).

**The governing ruling already exists and is implemented.** 2026-08-24, verbatim in
`UseBarsSurface.cs:167-172`: *"Alle Gegenstände sollen nur über die gebaute Item-Interaktion nutzbar
sein. Die jeweiligen Symbole sollen daher nicht erscheinen. Dort sollen NUR die Entscheidungen
erscheinen, die neben dem eigentlichen Auslösen des Gegenstands an Entscheidungen getroffen werden
müssen."* And 2026-09-07 item 6a/6b, quoted at `UseBarsSurface.cs:1537-1547`.

| # | Widget | Game class | Raised by | Could the ITEM FLOW carry it? | What would be lost |
|---|---|---|---|---|---|
| 1 | Plain item slot | `UIUseItemScenario` | `UIUseItemsBar.ShowUsableItems` (Choreographer ×14, `CardsHandManager.cs:1281-1301`) | **ALREADY DOES.** Render-hidden by `UseBarsSurface.EnforceItemsSplit:1207-1258`; answered by card → recess → USE cap (`ItemsPile.cs:2578-2612`) | nothing |
| 2 | Item-backed, option-less, OPTIONAL bonus row (the "Brille") | `UIUseActiveBonus` | `UIActiveBonusBar.ShowActiveBonus` | **ALREADY DOES.** Hidden by `EnforceActiveBonusSplit:1608-1613` on `CardsGameApi.BonusIsPlaceable:1528-1533`; answered by `ItemsPile.ConfirmPendingBonus:3700-3740` → `CardsGameApi.ClickActiveBonusSlot` | nothing |
| 3 | Take-damage OnAttacked shield / retaliate item slot | `UIUseItemScenario` | `TakeDamagePanel.cs:249` | **ALREADY DOES — and correctly.** `ItemsPile.HandleTakeDamageDrop:4644-4699` clicks the **game's own bar slot** via `CardsGameApi.LiveItemsBarSlot` + `ClickItemsBarSlot`, so it reaches `TakeDamagePanel.ToggleShieldItem` (`TakeDamagePanel.cs:264, 878-902`) | nothing — **but note the trap**: the game's *naive* item flow, `UIItemScenario.OnPointerDown` → `UseItemService.UseItem` (`decompiled/…/UIItemScenario.cs:193-196`), is the **wrong action** here. It *uses* the item; it does not toggle it into the damage calculation. The mod does not take that route. |
| 4 | Item element / infuse sub-picker | `UIUseConsumeInfuseSlot.elementPicker` | the mod itself: `ItemsPile.TickChoiceDecision:3113-3125` clicks the slot after the card is placed | **NO** | **The picked element.** `UseItemService.UseItem` passes `infusions: null`, so no card gesture can answer a `Consumes: Any`. The item flow already *raises* this widget; the widget is where the answer lives. |
| 5 | Initiative-boots ± picker | `UIUseActiveBonus` + `initiativeOption` | `Choreographer.cs:11678` | **NO** | **The ± direction.** A signed number has no card gesture. Kept by `BonusNeedsFurtherOption` (`CardsGameApi.cs:1500-1502`). Confirmed live in his own log: `bonus-bar split KEPT the decision-area row for 'ITEM_NAME_BootsofSpeed'`. |
| 6 | Forgo-which-ability / choose-ability | `CForgoActionsForCompanionActiveBonus`, `CChooseAbilityActiveBonus` | `Choreographer.cs:8386`, active-bonus bar | **NO** | **Which ability.** Same predicate, `CardsGameApi.cs:1499-1502`. |
| 7 | **MANDATORY** item-backed bonus | `UIUseActiveBonus` | `TakeDamagePanel.cs:265/269` | **NO — this is the deadlock case** | **The toggle `TakeDamagePanel.CanTakeDamage()` (`:847-876`) demands before the panel will close.** There *is* a replacement exit for the simple form — `TakeDamagePanelSafety.AutoUseMandatoryActiveBonuses` (`src/GloomhavenVR/WorldUI/Patches/TakeDamagePanelSafety.cs:144-197`) auto-clicks it on the confirm — but that helper **gives up and says so** (`:170-178`) when the mandatory bonus *also* needs a manual option/element pick. For that bonus the decision-area row is the sole exit. The fail-open keep at `CardsGameApi.BonusIsPlaceable` is correct and must not be "tidied". |
| 8 | Aura / character-ability / summon bonus rows | `UIUseActiveBonus` | active-bonus bar | **N/A — not item-originated** | No card exists to place. Kept by his own ruling item 6b: *fidelity* is owed, not removal. |
| 9 | Item-demand banner + item pickers | mod placard over `ItemCardPicker` | `ItemCardRefreshPicker.Show`, `ItemRewardLosePicker.Show` | **ALREADY IS the item flow** (`ItemsPile.TickDemandPick`, cards laid into the recess) | nothing |

**So: every item decision that CAN be carried by the item flow already is.** The five that remain
(#4–#7 plus the not-item #8) each leave a piece of game state no card gesture encodes, and #7 is
load-bearing against the deadlock class.

### Inverse check — an item decision the game raises with NO mod widget?

I found none.

- `ItemCardRefreshPicker` / `ItemRewardLosePicker` — covered by `ItemsPile.TickDemandPick`, the pick
  banner (record 7) and `MandatoryDecisionTerm.ItemCardPicker`
  (`src/GloomhavenVR/WorldUI/Modal/MandatoryDecisionTerm.cs:63-65`).
- `UIElementPicker` for an item consume — converted live in the items dock
  (`UseBarsSurface.cs:32-49`), reachable by poke and laser.
- Mandatory prevent-damage bonuses — the row is kept, and `TakeDamagePanelSafety` is the second exit.
- **Single-target item pick** (`Choreographer.cs:10704-10710`, gating Ready through
  `CAbility.IsWaitingForSingleTargetItem`) — this is a **board target selection** (AOE lock /
  `ActorsToTarget.Count > 1`), answered by picking a figure on the board, not by a decision-area
  widget. No widget is owed. `CardsGameApi.ItemActionResolving`'s fourth clause
  (`src/GloomhavenVR/Cards/CardsGameApi.cs:1378-1381`) reads exactly this state and holds the card in
  the recess while it lasts, which is the right behaviour.
- `UIItemConfirmationBox` (buy/sell/bind) — campaign map, out of scope for the scenario decision area.

---

# RECORD 45 REVIEW — code that has never executed

**Verdict: the record is correct.** I found no defect in the codec, the addressing, the cap, the fold
or the truncation handling. `scripts/wire-tests.sh` runs clean (210,164 assertions;
`scripts/worktree-setup.sh` is required first — the wire-test script fails with a setup message, not
a test failure, in a fresh worktree). `scripts/build.sh Release` is clean, 0 warnings.

| Item | Verdict |
|---|---|
| Encode/decode round trip | **Correct.** Writer `PresenceState.cs:3080-3107`; reader `:4468-4518`. Payload `1 + 3n ≤ 49` fits the length byte; the writer's bound `i + 2 + payload <= buffer.Length` (`:3080`) is checked before any write. |
| `[bar:3\|slot:5]` addressing | **Correct.** One packer, two unpackers, all in `NetProtocol.cs:23188-23197`; the ends are pinned by both a 256-pair sweep and three explicit constants in `UseBarSlotIdentityVectors.AddressingKeepsItsEnds`. `UseBarsCount = 4` and `UseBarsMaxSlots = 8` (`NetProtocol.cs:24742, 24748`) fit 3 and 5 bits with room. |
| 16-entry cap, and *why* 16 not 32 | **Correct, and the arithmetic checks out.** `UseBarSlotIdentityMaxEntries = 2 * 8` (`:23164`). 1747 + 51 = 1798 worst case; margin under `MaxSize` 2100 is 302 > 257 (board tuning, the largest single record). At 32 entries: 1798 − 51 + 99 = 1846, margin 254 — one byte inside the rule, exactly as documented at `PresenceState.cs:1579-1595`. Clamped on both ends (`:3322`, `:4489-4492`). |
| The fold's collision refusal | **Correct.** Exactly-one-match on both paths — `ResolveBonusIcon` `UseBarSlotSymbol.cs:295-304`, `ResolveItemIcon` `:337-346`. Zero matches → `NoModelMatch`, ≥2 → `Ambiguous`, both return null and both leave the slot anonymous. Nothing picks a "best" candidate. |
| Truncation | **Correct.** Interleaved whole entries mean the bound is one division: `fits = (end - j) / 3` (`:4488`), then `n = min(stated, cap, fits)`. A record cut mid-entry loses whole entries, never produces a half-read id. |
| Peer below `UseBarSlotIdentityMinPeerBuild` | **Correct.** The receiver never *requires* the record; absence clears rather than latches (`RemoteAvatar.cs:1825-1841`, with the reasoning stated) and the local resolve takes over. The build constant (479, `NetProtocol.cs:23180`) is used only to distinguish "cannot name" from "named none" in the log (`RemoteBoardFurniture.cs:3722-3727`) — the right lesson from the ARC-ORDER-line correction. |
| Absence is unsayable | **Correct.** `UseBarSlotIdsPayload` returns 0 when every id is `NoIdentity` (`PresenceState.cs:3332-3338`), so a pre-479-shaped packet is byte-identical. |
| Ids are in the change gate | **Correct**, on both sides: `UseBarsSurface.Publish:832-840` and `NetAvatarDriver:1845-1852`. Without this a re-decorated slot would keep the old symbol; both places call it out. |
| Stale-tail scrub | **Correct.** `UseBarsSurface.SampleWire:805-811` zeroes ids past `count` for every bar, including the exception and empty-bar-drop paths. |
| Cross-machine stability of the ids | **Correct, and it is the game's own identity.** `(Ability.Name, BaseCard.ID)` and `CItem.NetworkID` are what `TakeDamagePanel.ProxyTakeDamage` (`decompiled/…/TakeDamagePanel.cs:1152-1153`) matches its own `ActiveBonusesToken` / `ItemsToken` on. |

**One design consequence I would not call a defect but the maintainer should know:** the receiver's
candidate set `CharacterClassManager.FindAllActiveBonuses(owner)` is a superset of the bar's set for
the ordinary character-class clause (the type-filtered overload `CharacterClassManager.cs:192-223` is
strictly narrower than `:93-125` on that clause), but **not** for song bonuses or scenario-modifier
aura bonuses — see F1. Those refuse honestly; the cost is only that F1 then blanks the rest of the
bar with them.

### "Bars 1 and 2 carry no identity" — INVISIBLE to him, and I can prove it

- **Bar 1 (abilities).** The local resolve genuinely works: `UIUseAbilitiesBar` is raised on every
  client with no `IsUnderMyControl` gate on the `Show` call — `Choreographer.cs:8331`
  (`ShowInfuseAbilities`), `:8386` (`ShowChooseAbility`), `:8503` (`ShowGenericInfusion`). The gate at
  `:8342-8349` / `:8395` only chooses the `ActionProcessor` state. Gate 2 therefore passes on a
  watcher, and these prompts are sequential in the Choreographer message queue, so the singleton
  cannot be holding two players' bars at once.
- **Bar 2 (augments).** `UIUseAugmentation` carries no icon field at all, so the **owner** draws no
  icon either. A blank mirrored tile is the 1:1 answer.

So the 16-entry cap costs nothing visible today, and the "raise the cap and `MaxSize` in the same
commit" note is the right guard for the day either changes.

---

# NOTES (no failure scenario)

**N1 — a citation that would send the next round to the wrong file.**
`src/GloomhavenVR/Net/Remote/RemoteUseBarSymbols.cs:100-104` justifies bars 1/2 carrying no identity
with "the abilities bar, which the game genuinely does raise on every client — the
`Choreographer.CheckForInitiativeAdjustments` path above is real". `CheckForInitiativeAdjustments`
raises **`UIActiveBonusBar`** (bar 0), not `UIUseAbilitiesBar` — `Choreographer.cs:11678`. The
conclusion is true, but on different lines (`:8331`, `:8386`, `:8503`). This is the "an assertion in
the source is a hypothesis" pattern: a confident comment protecting a claim that was never checked
against the file it names.

**N2 — a shipped log line that now reads false.** `UseBarsSurface.Publish` still prints `NO slot
identity — the game's use slots carry no label at all, only card ART, which never rides this wire`
(`UseBarsSurface.cs:866-869`). Record 45 now rides beside record 25 on the same publish. The sentence
is defensible ("record 25 carries none") but a grep of `USE BARS: wire drawer published` during a
record-45 session reads as a denial.

**N3 — micro, and I checked it rather than assumed it.** The mirrored tile shrinks to fit
(`RemoteBoardFurniture.cs:3491-3498`) while the symbol is scaled against the *unshrunk* `UseBarTile`
(`:3700`). With `DecisionMountWidth = 0.42` (`Cards/Tray/PlayTray.1.Core.cs:470`),
`UseBarCaptionW = 0.150`, `UseBarTile = 0.026`, `UseBarTileGap = 0.006`, the shrink only fires at
**8** slots and then by 2.4 %. Not worth a build.

---

# WHAT I CHECKED AND FOUND CLEAN

- **The brief's "impossible by construction" claim** — correct, and understated: `ShowOtherPlayer`
  *hides* both bars, it does not merely fail to raise them (`TakeDamagePanel.cs:1122` → `:434-435`,
  `:1133`).
- **Record 45's whole codec** — see the table above. Every arm checked against the shipped bytes, not
  against its own doc. `scripts/wire-tests.sh` green (210,164 assertions); `scripts/build.sh Release`
  green, 0 warnings.
- **Sender/receiver fold symmetry** — both directions call the same `ActiveBonusId` / `ItemId` /
  `TakesPlainIcon` methods (`UseBarSlotSymbol.cs:55-99`), so a future edit moves both.
- **`_symbolScratch` bounds** — `Sprite?[UseBarsMaxSlots]` (`RemoteBoardFurniture.cs:3558`) against a
  row whose slot count is clamped to `UseBarsMaxSlots` at build time (`:3455`) and a wire loop bounded
  by `s < UseBarsMaxSlots` (`:3632`). No overrun.
- **`_shownSymbolKey` collision** — `-((refusedWhy * 8) + wireWhy + 1)` (`:3707`); `refusedWhy` has 6
  values, `wireWhy` 7, multiplier 8. No aliasing.
- **The tuning-change rebuild path I suspected** — `UseBarStructure` (`:3351-3366`) does not include
  `DecisionScale` directly, but it includes `DecisionRowHeight`, which *is*
  `DecisionButtonH * _decisionTuning.DecisionScale` when no widget row is up (`:722-725`), quantised
  at 0.1 mm. A live re-tune therefore *does* move the key and rebuild the rows. Not a defect.
- **`use_bar_bonuses`** — the bar-0 caption key exists (`Core/Loc/Loc.cs:1358`). Not a raw key leak.
- **`Cards/Patches/DamageFlowPatches.cs`** — read; it is entirely the ability-card burn/lose commit
  flow. No item path passes through it.
- **The mandatory-bonus deadlock exit** — `TakeDamagePanelSafety.AutoUseMandatoryActiveBonuses` reads
  live panel state each pass, is bounded at 8 passes, uses the game's own `UIUseActiveBonus.Toggle()`,
  and stands down for proxies (`!ThisPlayerHasTakeDamageControl`, `:142`). Its give-up branch
  (`:170-178`) is the reason widget #7 must keep its row.
- **`.planning/deadlock-class-audit.md`** — re-read; its A.4 entry for the item lose/refresh pickers
  still matches the shipped code, and the mod covers both.

---

## What I would put in front of him first

1. **F1** — because it is a *new* regression shipped by the record that was meant to fix 1:1, it
   bites in a prompt he plays every round, and its own log line reads as success.
2. **F2** — because it is the honest answer to his third question. The damage buttons *are* 1:1 (they
   are real clones). The bar under them is not, and no wire record can make a mod-drawn quad with a
   caption column into the game's widget. That is a `RemoteWidgetMirror` job, and it is the sixth
   clone the mod does not have.
3. The design answer to (i)/(ii): **nothing left is removable.** Five widgets, five named pieces of
   game state, one of them a deadlock exit.
