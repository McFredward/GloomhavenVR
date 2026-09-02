# No options menu may disturb play — the ModBuild 340 defect, and the audit behind the fix

**User, 2026-09-02, hardware round on ModBuild 340, verbatim:**

> *"Wenn man die VR Optionen offen hat kommt die Kartenhand nicht. Bitte vergwissere dich, dass
> kein Optionsmenu - auch nicht die VR Optionen den Spielfluss stört."*

That is two requests. The first is a bug report. The second is an audit, and §3 below is the
answer to it: every gate in the mod that suppresses something because "a modal is open", with a
verdict per site for the three menus he means — the game's **Optionen**, the **pause/ESC** menu,
and the mod's own **VR Optionen**.

Evidence: `.planning/debug/second_logs/LogOutput.log` (ModBuild 340, 18.9 MB, DEBUG level).

---

## 1. The mechanism, confirmed — and the half the brief did not have

The brief's diagnosis was right and incomplete. The **cause** is the window id; the **symptom he
reported** arrives one step further downstream than the commit-block.

### 1a. The cause (confirmed)

`ModalFallback` classified an open window with

```csharp
// ModalFallback.7.Close.cs:578 (pre-fix)
private static bool IsBlockingWindow(UIWindow window) =>
    !NonBlockingMenus.Contains(window.ID)          // <- the whole of it
    && !MultiplayerRosterMenus.Contains(window.ID)
    && !MapRoomParallel(window)
    && !IsMapRoomHoverCard(window)
    && !ActionDismissedLevelMessage(window);
```

`NonBlockingMenus` (`ModalFallback.8.Convert.cs:854`, pre-fix) is a `HashSet<UIWindowID>`. The
mod's standalone VR settings window carries `UIWindowID.None` — deliberately, ModBuild 337 — so an
id-keyed set **cannot represent it at all**. Log, line 2617:

```
WINDOW IDENTITY 'GloomhavenVR.OptionsTabWindow' (ID None): path Persistent UI_unified/…;
rect 1164x1080; … nearest ancestor UIWindow <none>.
```

and line 2628/2632:

```
MODAL FALLBACK ASSERTED: windows=2, story=False, levelMsg=False, dialogPopup=False,
scenario=True, blocking=True, style=window, mode=CardSelection → ModalUI.
Modal commit-block ENGAGED — … Blocking window(s): 'GloomhavenVR.OptionsTabWindow' (ID None).
```

### 1b. The half that produces the reported symptom

`IsBlockingWindow` feeds **two** consumers, not one:

| consumer | path | effect |
|---|---|---|
| the commit gate | `BlockingWindowModalActive` (`ModalFallback.4.Tick.cs:188`) → `CardsDriver.2.Update.cs:703` | card/tray **commits** refused |
| **the mode machine** | `Tick`'s `wantLock` (`ModalFallback.4.Tick.cs:2659`) → `VRModeStateMachine.SetAuxModal` (`:3172`) → `VRMode.ModalUI` | **`PalmGate` is dropped** |

The second one is the report. `VRModeStateMachine.cs:246`:

```csharp
{ VRMode.ModalUI, Interactors.Poke | Interactors.Ray | Interactors.Grab },   // no PalmGate
```

`VRHand.SetInteractorPolicy` (`Hands/VRHand.cs:374`) therefore sets `PalmGate.Enabled = false`, and
`CardsDriver.UpdatePalmGate`'s `revealed` term is `gate.Enabled && …`, so `shouldOpen` is false
forever. Log, line 2633:

```
fan state: mode=CardsSelection, widgets=14, fanBuffer=8, gateEnabled=False, revealed=False,
open=False, boundHand=True (vrMode=ModalUI).
```

**"Die Kartenhand kommt nicht" is not "the cards refuse a grab" — it is "the fan never opens at
all".** Blocking the commits would have been survivable; losing the palm gate is not.

### 1c. And the gate outlived its edge

Line 9530 engages the commit-block a second time and **no `RELEASED` line follows it anywhere in
the remaining 9 400 lines**. This is the recurring bug class ([[gate-outliving-its-edge]]) and it
is why the symptom read as permanent rather than as "while the menu is up".

### 1d. Two further consequences of the same id, both live in the same log

* **The liveness rule was judging the settings window.** `ModalFallback.9.Spawn.cs:3164` exempted
  the ESC/Options family from the empty-window rule for the standing ruling *"es MUSS immer möglich
  sein das Optionsmenu zu öffnen"* — keyed on the four game ids, so it missed ours. Log:
  `MODAL LIVENESS ARMED: 'GloomhavenVR.OptionsTabWindow' (ID None) after 178 ms`.
* **The settings click exemption did not cover it.** `IsUnderFloatedPauseOrSettingsWindow`
  (`ModalFallback.7.Close.cs:1487`) is what `Patches/SettingsClickExemption.cs:211` consults to flip
  `InteractabilityManager`'s veto. Keyed on `Options`/`OptionsSubmenu`/`ESCMenu`, so while a
  scripted tutorial holds an interaction profile loaded, **every row in the VR settings panel eats
  its own click** — the 2026-08-02 defect (*"das Optionsmenue soll NIEMALS blockiert sein"*) one
  window over. Not in this log, because no tutorial was running; it is a real route.

---

## 2. Why the window keeps its `UIWindowID.None` — the alternative fix, weighed and rejected

Giving the clone `UIWindowID.OptionsSubmenu` would make every id-keyed rule in `ModalFallback`
answer correctly for it in one line. It is the wrong fix, and the decisive evidence is in the
repo, not in an argument:

```csharp
// ModalFallback.7.Close.cs:188 — runs on EVERY X-close
private static void ResetEscMenuToggleGroup(UIWindow window)
{
    UIWindowID id = window.ID;
    if (id != UIWindowID.Options && id != UIWindowID.OptionsSubmenu
        && id != UIWindowID.ViceOptionsSubmenu && id != UIWindowID.CompendiumPanel)
        return;
    …
    esc.toggleGroup.SetAllTogglesOff();
}
```

A mod window wearing `OptionsSubmenu` would run `ESCMenu.toggleGroup.SetAllTogglesOff()` whenever
the player pressed **our** X — taking the game's Options window down with it. That is precisely the
coupling ModBuild 336/337 removed, on the user's ruling:

> *"Ich will das es möglich ist das man die Optionen und die VR Optionen öffnet parallel ohne
> Probleme."*

Three further id-keyed registries the game keeps ONE of, all recorded in
`VROptionsTab.1.Inject.cs`'s class doc and none of them re-entered: `UIWindow.GetWindow(id)` /
`GetWindowsByID`, `UIWindowManager.extraSkipHideWindows` / `extraSkipShowWindows`, and the
escapable listener list.

**Verdict: teach the classifier about mod-owned menu windows; do not hand the window a game
identity it does not want.**

---

## 3. THE ENUMERATION — every gate keyed on "a blocking modal is open"

Verdict column: what SHOULD happen when an **options-family** window is open (game Optionen, ESC
menu, or VR Optionen). `✅ fixed here` = the site now answers correctly for all three because its
predicate moved onto `MenuWindowFamily`. `— n/a` = the site never fired for a menu even before.

### 3.1 `ModalFallback.BlockingWindowModalActive` — the commit layer

| # | site | suppresses | verdict for the options family |
|---|---|---|---|
| 1 | `Cards/Driver/CardsDriver.2.Update.cs:703` | master `_modalInputBlocked` latch (12 downstream sites below) | must NOT fire — ✅ fixed here |
| 2 | `Cards/Driver/CardsDriver.3.Laser.cs:2335` `BlockCardInteractions` | every non-held `VRCard` forced `PokeSelectEnabled=false; Grabbable=false` | must NOT fire — ✅ via #1 |
| 3 | `Cards/Driver/CardsDriver.2.Update.cs:755` | empty-fan placard hidden | ✅ via #1 |
| 4 | `Cards/Driver/CardsDriver.2.Update.cs:1119` | empty-fan hint never raised | ✅ via #1 |
| 5 | `Cards/Driver/CardsDriver.3.Laser.cs:429` | dominant-hand card contact arbitration skipped (no hand-driven lift) | ✅ via #1 |
| 6 | `Cards/Driver/CardsDriver.3.Laser.cs:471` | gate-hand contact arbitration skipped | ✅ via #1 |
| 7 | `Cards/Driver/CardsDriver.3.Laser.cs:1510` | tray/board element laser PRESS (END TURN, item use) | ✅ via #1 |
| 8 | `Cards/Driver/CardsDriver.3.Laser.cs:1695` | item-chip hand-fan trigger pluck | ✅ via #1 |
| 9 | `Cards/Driver/CardsDriver.3.Laser.cs:1827` | browse-arc click-away dismiss | ✅ via #1 |
| 10 | `Cards/Driver/CardsDriver.3.Laser.cs:2121` | item-chip pull-jerk rescue pluck | ✅ via #1 |
| 11 | `Cards/Driver/CardsDriver.3.Laser.cs:2145` | item-fan click-away dismiss | ✅ via #1 |
| 12 | `Cards/Driver/CardsDriver.3.Laser.cs:2187` | item-chip laser pluck | ✅ via #1 |
| 13 | `Cards/Driver/CardsDriver.5.Interactions.cs:94` | hand-to-hand card/chip transfer | ✅ via #1 |
| 14 | `Cards/Driver/CardsDriver.6.Flows.cs:976` `PumpLongRestTurn` | long-rest turn auto-advance deferred | must NOT fire (its own comment says so) — ✅ fixed here |
| 15 | `Cards/Driver/CardsDriver.6.Flows.cs:1046` `PumpSelectionHandSwitch` | selection hand-switch watchdog deferred — the pump written FOR the ruling *"Optionsmenü darf das Spielgeschehen nie beeinflussen"* | must NOT fire — ✅ fixed here (it was firing) |
| 16 | `Hands/Interact/RayInteractor.cs:735` | sets `_cardTrayCommitsSuppressed`; **log mirror only, no consumer** | ✅ fixed here |
| 17 | `Hands/Interact/RayUguiDriver.cs:468` | diagnostic text on the settings-exempt line | ✅ fixed here |

### 3.2 The `ModalUI` mode lock — the layer that cost the fan

| # | site | suppresses | verdict |
|---|---|---|---|
| 18 | `WorldUI/Modal/ModalFallback.4.Tick.cs:2651` → `:2659` `wantLock` → `:3172` `SetAuxModal` | **THE root of #19–#24** | must NOT assert for a menu — ✅ fixed here |
| 19 | `Core/Events/VRModeStateMachine.cs:246` (policy table) | `ModalUI` has **no `PalmGate`** ⇒ `VRHand.cs:374` disables it ⇒ **the card fan cannot open** | ✅ via #18 — *this is the reported symptom* |
| 20 | `Cards/VRCard.cs:394` `CanGrab` | new card grabs refused | ✅ via #18 |
| 21 | `Cards/Driver/CardsDriver.6.Flows.cs:1398` `BrowseAllowed` | pile-browse arc refuses to open | ✅ via #18 |
| 22 | `Cards/Driver/CardsDriver.2.Update.cs:341` | an OPEN pile browser is closed on the transition | ✅ via #18 |
| 23 | `Cards/Driver/CardsDriver.2.Update.cs:330` | `_half.InvalidatePlacement()` suppressed on a ModalUI round-trip | ✅ via #18 |
| 24 | `Cards/Patches/HandSuppressionPatches.cs:188` | keeps the 2D hand-canvas suppression latched (bounded: burn latch + 6 s cap) | ✅ via #18 |
| 25 | `WorldUI/FlatScreen/FlatScreen.4.Lifecycle.cs:125` | the whole flat 2D composite is gated inside the `ModalUI` branch | ✅ via #18 |

### 3.3 `HardCommitLockActive` — the board-click lock

| # | site | suppresses | verdict |
|---|---|---|---|
| 26 | `WorldUI/Modal/ModalFallback.7.Close.cs:1389` (def.) | `ErrorModalOpen \|\| (IsResultsPanel(id) && IsBlockingWindow(w))` | — n/a: no menu id is a results panel, so a menu never raised it, before or after |
| 27 | `Board/BoardClickDriver.cs:730` `RequestClick` | board hex/actor click injection **and its haptic** | — n/a (via #26) |
| 28 | `Hands/Interact/RayInteractor.cs:736` | log mirror only | — n/a |

### 3.4 `WindowModalActive` / `Converted.Count` — "any window floats", NOT a blocking test

| # | site | changes | verdict |
|---|---|---|---|
| 29 | `WorldUI/FlatScreen/FlatScreen.4.Lifecycle.cs:139` | flat screen suppressed while a window floats as a world panel | correct as-is: a floating menu SHOULD own its own panel. Unreachable for a menu now anyway (it sits inside the `ModalUI` branch, #25) |
| 30 | `ModalFallback.7.Close.cs:31` | escape chord armed only while something floats | correct: the chord must work for a floated menu |
| 31 | `ModalFallback.4.Tick.cs:3143` `KeepMenusUnclipped` | MR sky/backdrop made non-occluding | correct: it exists FOR floated menus |
| 32 | `ModalFallback.8.Convert.cs:423`, `.10.CatchAll.cs:1629` | spawn stagger index | cosmetic, correct |
| 33 | `ModalFallback.6.MenuGuard.cs:480` `ShouldSwallowMenuTabClick` | swallows a pause-menu tab re-click whose window is already open | correct: it is the menu's own behaviour, not a play gate |
| 34 | `Composites/{StoryComposite,LoadoutConfirmPark,QuestJourneyCurtain}.cs` via `CountFloatsOtherThan` | composite claim/park gating | correct: "is any other window standing" is the right question |

### 3.5 The classification rules themselves — where the family is decided

| # | site | decides | verdict / action |
|---|---|---|---|
| 35 | `ModalFallback.7.Close.cs:597` `IsBlockingWindow` | **the whole of §3.1 and §3.2** | ✅ now `MenuWindowFamily.IsPlayerMenu(window)` |
| 36 | `ModalFallback.9.Spawn.cs:2907` liveness exemption | the ESC/Options family is never judged dark | ✅ now `MenuWindowFamily.IsEscOptionsFamily(window)` — the log shows ours was being armed |
| 37 | `ModalFallback.7.Close.cs:1471` `IsSettingsSurface` | laser fall-through past a nearer blocking float onto the settings panel | ✅ now `MenuWindowFamily.IsSettingsWindow(window)` |
| 38 | `ModalFallback.7.Close.cs:1530` `IsUnderFloatedPauseOrSettingsWindow` | flips `InteractabilityManager`'s click veto (`Patches/SettingsClickExemption.cs:211`) | ✅ now `IsSettingsWindow(window) \|\| ID == ESCMenu` |
| 39 | `ModalFallback.8.Convert.cs:696` `Sticky` | float survives the game hiding the window | **stays game-ids only** (`IsGameOwnedMenu`). ModBuild 337 took our row out of the ESC `ToggleGroup`, so there is nothing to survive — and only `UserClosing` drops a sticky float, so a teardown that does not go through `CloseFloatedWindow` would leave an empty frame. *"Es darf niemals leere Fenster geben."* |
| 40 | `ModalFallback.7.Close.cs:1095` `RendersInsideFloatedAncestor` | "the parent wins" exemption | **stays game-ids only.** The exemption is about `MainOptionOptions` re-parenting the options window at RUNTIME. For ours the hierarchy is the RIGHT answer in both modes: standalone it has no ancestor `UIWindow`; in `VROptionsTab`'s fire-exit mode it really IS a sub-view of the Options window and must be drawn inside it, not floated on top of it |
| 41 | `ModalFallback.10.CatchAll.cs:494` `CatchAllEligible`, `:1268` `IsRetractableSubView`, `.4.Tick.cs:2096` (refusal text) | same exemption, three more expressions | **stays game-ids only**, same reason as #40 — and `IsRetractableSubView`'s own doc requires it to mirror `CatchAllEligible` term for term |
| 42 | `ModalFallback.7.Close.cs:188` `ResetEscMenuToggleGroup` | runs `ESCMenu.toggleGroup.SetAllTogglesOff()` on an X-close | **must stay id-keyed and must NOT include ours.** This is §2's evidence — including it re-creates the exact 337 coupling. Comment added saying so. |
| 43 | `ModalFallback.8.Convert.cs:38` `IsFullScreenMenu`, `:80` `WantsTransparentBackground` | opaque full-window backing hidden; height cap | **stay id-keyed.** These are presentation rules about the GAME's full-screen backing plate. Ours is a 1164×1080 centre-anchored panel whose background IS its panel; `keepBackgroundHidden` would hide the thing the player is looking at. Its content-fit exemption is already handled by name at `:318` (now `IsModOwned`) |

### 3.6 Game-state blockers (not the mod's modal layer) — listed for completeness

| # | site | suppresses | verdict |
|---|---|---|---|
| 44 | `Cards/CardsGameApi.cs:2115` `IsCardCommitBlocked` | wanted-slot overlays / snap glow / pick banner, via `CardsDriver.6.Flows.cs:41` | correct already: reads `UIResultsManager.IsShown`, `ScenarioEnded`, `StoryController`. **`ESCMenu.IsOpen` is deliberately excluded** and documented at `:2102` |
| 45 | `Board/AoeControl.cs:246`, `Board/BoardClickDriver.cs:793` | AoE thumbstick rotation / injected click while `TimeManager.IsPaused` | correct: verbatim vanilla replication, not a mod gate |
| 46 | `WorldUI/Patches/EscMenuInputBlock.cs:151` | suppresses the game's `UI_PAUSE` auto-show (the mod owns the X) | correct, unrelated |

**Nothing in `src/` reads `ESCMenu.IsOpen` as a live play gate.** Every occurrence is a
decompile citation in a comment.

---

## 4. Where the family is decided now

`src/GloomhavenVR/WorldUI/Modal/MenuWindowFamily.cs`, a standalone `internal static class` (not a
partial of `ModalFallback`, so `check-partial-order.py`'s initialiser-order hazard cannot arise).
It takes a **`UIWindow`, not a `UIWindowID`** — that is the whole point: a window with no id has to
be representable.

```
IsModOwned(window)              registration first, the stamped name as a fallback
IsPlayerMenu(window)            game ids OR mod-owned   — the PLAY-FLOW question
IsGameOwnedMenu(window)         game ids only           — the GAME-BOOKKEEPING question
IsEscOptionsFamily(window)      ESC/Options + mod-owned — the "always openable" ruling
IsSettingsWindow(window)        Options/OptionsSubmenu + mod-owned
```

**Why two membership predicates rather than one.** The old `NonBlockingMenus` set was asked two
different questions and answered both with the same membership, which is how a per-site verdict
silently becomes a policy. #39–#41 are facts about the GAME's window machinery (a `ToggleGroup`
we are not in, a parent the game re-parents at runtime); #35–#38 are about play. Naming them apart
is what lets a future reader see that #39 is a decision and not an omission.

**Why registration and not only a name.** `VROptionsTab.Inject` calls `RegisterModMenu` the moment
the clone exists (before the detach can fail, so the fire-exit mode is classified too), and
`VROptionsTab.Forget` — which every teardown path including `Shutdown`'s `finally` runs through —
calls `ForgetModMenu`. The registry is compacted on write, so a destroyed window cannot linger
([[gate-outliving-its-edge]]). The name test stays as a fallback because a missed registration must
not cost the player his card hand a second time.

---

## 5. Proof the ModBuild 337 decoupling does not return

The two windows must still open in parallel and independently. Nothing in this change touches any
of the three couplings 337 removed:

| coupling 337 removed | still removed? |
|---|---|
| the ESC menu's `ToggleGroup` (our cloned row's `ExtendedToggle` taken out of the group, in `VRMenuEntry`) | **still removed.** Correction, ModBuild 349 — this row said `VRMenuEntry.cs` is not modified by that build, and from 349 on it is ([[audit-is-a-snapshot]]: a review's claims go stale in two builds). `Detach` is byte-identical and there is still no write to `toggle.group` or to `UIWindow.ID` anywhere in the mod. What 349 added is `WorldUI/Modal/MenuExclusivity.cs` — a per-PLACE arbitration rule — plus `VRMenuEntry.TickMainMenuExclusivity`/`ClearRivals`, which perform the MAIN MENU's own one-at-a-time behaviour explicitly, over rows they only read and close through the game's public `Deselect()`. In a scenario and in the map room the rule returns `Parallel` and none of it runs. |
| the escapable listener list (`LeaveSharedStacks`) | **untouched** — `VROptionsTab.LeaveSharedStacks` is unmodified |
| the `ControllerInputAreaManager` stack (`LeaveInputAreaStack`) | **untouched** |

And the positive argument: the coupling would come back through `UIWindow.ID`, and the ID is
**not changed** — `git diff` contains no assignment to `.ID` anywhere. The two edits in
`VROptionsTab.1.Inject.cs` are a `RegisterModMenu` call and a `ForgetModMenu` call; neither writes
anything to a game object. `ResetEscMenuToggleGroup` (#42) — the one method that would take the
game's Options window down with ours — is still keyed on the four game ids and now carries a
comment saying why it must stay that way.

Independently: the mod's window is now `Sticky = false` exactly as before (#39), so its close path
is byte-identical, and it is exempt from the ESC menu's single-window arbitration for the same
reason it always was — it is not in that group.

---

## 6. Residue — honest, and not fixed here

| route | status |
|---|---|
| `RefuseEmptyFloat` (`ModalFallback.9.Spawn.cs:2221`) has **no** menu-family exemption: a menu window that reaches its reveal edge with nothing drawn is released and refused until it closes and re-opens | **OPEN, pre-existing, symmetrical.** It applies to the game's own Options window identically, so it is not a mod-window-specific hole. The ModBuild 291 liveness exemption (#36) deliberately covers the *post-reveal* rule only. Worth a decision, not worth guessing at in this build |
| `RayInteractor._cardTrayCommitsSuppressed` / `_boardClickCommitsSuppressed` are computed but read by nobody — the enforcing reads are independent evaluations in `CardsDriver` and `BoardClickDriver`, at a different point in the frame | **OPEN, cosmetic.** The logged policy can disagree with the enforced one for a single frame. Outside this lane's paths |
| `docs/PATCH-INVENTORY.md` line references have shifted | **pre-existing** — verified by running `patch-inventory.sh check` on the base commit `7f48eda1`, where it warns identically |

---

## 7. What the next hardware round must read

Three lines were promoted **Info → Note** (Info tier, printed at the shipped default). The TEXT is
byte-identical in every case — only the tier moved ([[quiet-log-silenced-the-backlog]]) — and each
carries a `// HW-VERIFY` marker, so `scripts/check-hw-verify.py` will fail if a later build demotes
one.

* `[WorldUI] MODAL FALLBACK ASSERTED/RELEASED: …` — `ModalFallback.4.Tick.cs`, change-gated on
  `wantLock`. **`blocking=True` with only menus open is the failure.**
* `[Cards] Modal commit-block ENGAGED/RELEASED — …` — `CardsDriver.2.Update.cs`, edge-gated.
  **An `ENGAGED` naming `'GloomhavenVR.OptionsTabWindow'` is the failure.** An `ENGAGED` with no
  matching `RELEASED` is the gate outliving its edge.
* `[WorldUI] MENU FAMILY: …` — new, two sites (registration, first float). **Its ABSENCE means the
  registration never ran**, and the classification is riding on the name fallback alone.
