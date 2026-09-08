# Redundancy audit — one concept, built twice

> ### ⚠ PARTLY CONSUMED — audited 2026-09-08 against `dev` = `49ceab21` (ModBuild 483)
>
> **This is a live backlog with a stale head count.** The survey was taken 2026-09-05 at ModBuild
> 437; the 2026-09 refactor programme (ModBuild 481–482) then acted on it. Per that build's own
> note at the top of `src/GloomhavenVR/Net/NetProtocol.cs`: *"seven concepts that were built more
> than once are now built once — the owner's active-half pulse and every mirror's, the
> floated-window lookup, the moved-subtree layer record, the hover-window anti-churn watch, three
> describers, two frame readers, six constants. Three more are written up and NOT merged because
> they cross a lane boundary; the gaze-bias lean is the one that matters."*
>
> So **check each row against the tree before working it** — a good fraction are already closed,
> and the rows that survive are the ones that cross a lane boundary. The user's ruling behind the
> whole document has NOT expired: *"genau so etwas will ich im gesamten Mod so gut es geht
> vermeiden."*

**Survey only. No `.cs` file was changed by this pass, no behaviour was altered, `NetProtocol.ModBuild`
was not bumped. The only artefact of this work is this document.**

Written 2026-09-05 against `HEAD = de62336e` (ModBuild 437), in answer to the user's request of
2026-09-05, verbatim:

> *"genau so etwas will ich im gesamten Mod so gut es geht vermeiden. Bitte lass hier ein worker eine
> Bestandsaufnahme machen ob es noch andere Stellen im Mod gibt wo so eine Redundanz gebaut wurde"*

— written after reading, about the combat log's FOLGEN/FIXIERT buttons:

> *"Beim Kampflog sind FOLGEN und FIXIERT schlicht zweimal gebaut worden — zwei Autoren, zwei
> Vorstellungen davon, was Folgen heißt, dieselben zwei Wörter drauf."*

This is a standing preference, not a one-off. He said the same thing in other words about the props:

> *"versuche Redundanz im Code zu vermeiden, alles nochmal neu für die Props zu schreiben obwohl doch
> das meiste schon für die Figuren gebaut wurde und funktioniert"*

---

## 0. What was hunted, and why the existing instruments could not see it

**The defect class is NOT "duplicated lines".** It is: *two implementations of one concept that a
player experiences as one thing, which can therefore drift apart, and which the user will eventually
report as "warum verhält sich X nicht wie Y?"*

This distinction is the most important finding of the survey, because **the project has already
measured its own duplication and concluded there is almost none — with an instrument that is blind
to this class.**

`.planning/refactor/PLAN.md:17`:

> "Duplication is essentially absent. Across WorldUI a subsystem-wide scan found exactly **one** real
> verbatim pair, and the reviewer recommends **not** merging it."

`.planning/refactor/REVIEW-WorldUI.md:791`:

> "A normalised 8-line sliding-window scan across all 41 files found **no** duplicate longer than
> ~17 lines."

Both statements are true *of a normalised sliding-window text scan*, and both are irrelevant here.
Run that instrument over the three calibration cases and it scores **zero on all three**:

| calibration case | why a text scan cannot see it |
|---|---|
| FOLGEN/FIXIERT built twice | one implementation re-parents the object, the other re-derives a pose from stored offsets every tick. Not one shared line. |
| the grab bar built three times | one is `GameObject.CreatePrimitive(Cube)`, one is drawn-rod arithmetic, one is a second copy of that arithmetic. Two of three share text; the third shares none. |
| the close X built twice | a uGUI `ModalCloseButton` vs a red 3D `BoardButton` whose label is the letter `"X"`. Zero shared text. |

So the honest summary of the prior census is: **the mod has very little copy-paste and a substantial
amount of parallel construction.** This survey indexes the second thing. It is a different
measurement, not a contradiction of the old one.

### What is already defended, and what that leaves

This codebase is unusually self-aware about mirrored state: **159 self-declared "mirror of" mentions
across 72 files**, and **73 self-declared "faithful copy / verbatim copy / hand-copied / restated BY
VALUE" admissions**. Five checkers police them:

* `scripts/check-mirrors.sh` — **20 groups** of mirrored constants, machine-checked to hold one value.
* `scripts/check-remote-defaults.py` — 95 local/remote default pairs, green.
* `scripts/check-tune-fields.py`, `scripts/check-wire-coverage.py` — the wire surface, green.
* `scripts/check-options-coverage.py` — the "two doors" case for settings *placement*.

Those cover **named numeric and string constants, and where a settings row appears**. They do not and
structurally cannot cover:

1. a duplicated **mechanism**, with no constant involved — R1, R4, R6, R7, R9, R10;
2. a constant that is a **`Color`** — the extractor is a float/string parser — R26, R33;
3. a constant that is an **inline literal** rather than `const NAME = value` — R27, R28, R34;
4. a **policy** that one of N implementations simply does not implement — R1, R4, R39;
5. the **words** on a control, as opposed to whether the control exists — R2, R13, R18.

Every finding below sits in one of those five blind spots. That is not a coincidence; it is the shape
of what is left after five working checkers.

**Scale:** 577 `.cs` files, 477 068 lines, surveyed in seven concept lanes plus a cross-cutting pass.
§4 states coverage and blind spots honestly.

**⟳ Lanes moving underneath this document.** Three fix lanes were running while this was written:
`Core/WallFade/**`, `WorldUI/Options/**`, and `CombatLogSurface.cs` + `Cards/Tray/PlayTray.*`. Rows
touching those files are marked **⟳** and must be re-checked against the tree before action.

**How to use this document.** It is built to be actioned **one row at a time over several builds**.
Rows are ranked by "can I name the exact behaviour that differs?" — §1 rows can, §2 rows cannot yet.
A §1 row is worth ten §2 rows. Nothing here is urgent enough to justify a sweep.

---

## 1. Already divergent today

| # | The concept, as a player would name it | A | B (and C…) | What the player actually sees differ | Rank |
|---|---|---|---|---|---|
| **R1** | *"Beim Loslassen zu dir drehen"* — a released window turning to face me | `WorldUI/Grab/GrabbableModal.cs:859` — gated by `WantsReFaceOnRelease()` (shared-window check, then `[WorldUI] WindowFacing`); rotates about the **ink** centre | `WorldUI/Surfaces/SurfaceGrabBar.cs:495` — **unconditional**, about the frame origin · `WorldUI/Surfaces/CombatLogSurface.cs:432` — **unconditional** ⟳ · `Cards/Tray/PlayTray.1.Core.cs:1155` — **no re-face at all** | He sets the dial to **"Nie"**. Modal windows keep the angle he dragged them to; **the combat log and every floating decision/reward panel still snap to face him.** Same at the default `LaserOnly`. `Loc.ConfigDescriptions.German.cs:2613` promises *"'Immer': beides (das bisherige Verhalten **aller Fenster**)"*. `WindowFacing` has exactly **one** reader in the whole mod (`GrabbableModal.cs:964`); `SurfaceGrabBar` was written 2026-09-03, twelve days after the dial landed, and never wired it. Both files also declare their own `ReFaceEpsilonDeg = 0.5f`. | **1** |
| **R2** | *"warum heißt dieselbe Einstellung auf zwei Seiten anders?"* | `WorldUI/Options/VROptionsTab.4.Curated.cs` — each row's `CaptionKey` → `Loc.Mod` | `WorldUI/Options/ConfigCatalog.cs:618` → `Loc.ConfigDisplayName` → `Core/Loc/Loc.ConfigNames.cs` ⟳ | **85 curated rows carry both names; 20 disagree.** `[Hands] LaserFingerOrigin` is *"Laser-Ursprung"* on the everyday page (`Loc.cs:835`) and *"Laser ab Fingerspitze"* under Erweitert (`ConfigNames.cs:171`) — **verified by reading both**. Also `CurlInputFullAt`, `HandStyle`, `SelectionReady/Enabled`, `MixedReality/Enabled` ("Mixed Reality" vs "Mixed Reality **an**"), `WorldUI/LoadingIndicator`, `BarsOccluded` ("Lebensbalken hinter Wänden" vs "**Balken** hinter Wänden"), `Rig/SpawnInCircle` ("**around** the board" vs "**at** the board"), `KeyColor` (color/colour), six `BoardPitchMin/Max_*` rows, and five more. **The project already knows this is the bug** and fixed four rows by hand — `Loc.ConfigNames.cs:161`: *"kept identical on purpose: **one row, one name, both doors**."* Nothing enforces it: `check-options-coverage.py` verifies a `CaptionKey` *resolves* and never compares it to `ConfigNames`. | **1** |
| **R3** | *"meine aktiven Karten"* — the active-card block beside a board | `Cards/Piles/ActivePileViewer.cs:41` — `Columns = 3`, list uncapped | `Net/Remote/RemoteActiveCards.cs:47` — `Columns = 2`; `:62` `MaxCards = 6`, clamped at `:112` | With 3 active cards the owner sees one row of three; **every teammate sees a 2+1 block** — one row taller, one column narrower, re-centred so its top edge lands at a different height against the board's other docks. **At 7+ active cards the peer never sees the 7th.** Directly violates the standing 1:1 ruling. The mirror's own comment at `:135` claims the layout is copied *"term for term … the same expression instead of two formulas that have to agree"* — and the one hand-copied number is the column count. Local went 3-wide on 2026-07-18 (`85bbb8ca`); the mirror was written eight days later with `Columns = 2`. | **1** |
| **R4** | *"der Laser darf nicht durch Sachen hindurch drücken"* | `Cards/Driver/CardsDriver.3.Laser.cs:1296` clamps at `FanOccluderDistance`; `WorldUI/MapRoom/MapLocationInteractor.cs:780` clamps at `SolidOccluderDistance` | `WorldUI/MapRoom/MapButtonRail.cs:1645` — honours **neither**, and does not check `HasFreshUiHit` either (`rg -c` over all three terms in that file returns **zero**) | The map room's hand fan **is** a `Cards.CardFan` (`MapRoomHand.1.Core.cs:169`), so both occluder terms are live in that room. Aim at a raised map-room hand card with a table cap behind it: the cap hovers, the beam is **clamped onto the cap and drawn straight through the card** (`:1673`), and the trigger presses the cap instead of taking the card. Verbatim the defect `CardsDriver.3.Laser.cs:1265-1290` records four hardware rounds spent on. Because it also ignores `HasFreshUiHit` it can overwrite another scan's clamp in the same frame, so it **flickers** rather than failing consistently. Same file also lacks the modal commit gate every other press path honours, with no comment saying why. | **1** |
| **R5** | *"ein Knopf, den ich drücke"* — one gesture, two commit principles | `Cards/Tray/PlayTray.7.Nested.cs:1918` — a board keycap fires at **90 % of 4 mm travel**, then a 0.4 s cooldown; a brush does nothing | `WorldUI/MapRoom/MapButtonRail.cs:2003` → `Press()` — a map cap fires on **8 mm fingertip contact**: no travel requirement, no cooldown. Its 7 mm travel is a post-hoc 70 ms animation *started by* the press (`:1731`) | Both are "a physical keycap lying flat on a table". **Brushing CONFIRM does nothing; brushing *Händler* opens the merchant.** | **1** |
| **R6** | *"hat dieses Fenster überhaupt einen Inhalt?"* — the appear dust and the grab rod | `WorldUI/Modal/ModalFallback.9.Spawn.cs:3066` `DrawsAnythingScriptSide` (`private static`) | `WorldUI/Surfaces/SurfaceMaterialise.cs:519` — a self-declared *"faithful copy"* **plus one extra term**, `CanvasChainDisabled` at `:551` (which exists nowhere else in the mod) | On a **modal** window whose canvas the game left switched off underneath it, the dust plays and the brass rod appears over nothing — the exact complaint *"kam die Animation das ein neues Fenster spawnt aber das 'Fenster' ist sofort wieder verschwunden"* and *"Wenn kein Fenster inhalt hat soll neben der Animation auch kein Greifbalken erscheinen."* On a **decision surface** with identical content, correctly nothing. The file names its own fix: *"REQUESTED CHANGE: make that method and `GroupChainAlpha` beside it `internal`, and this copy is deleted."* | **1** |
| **R7** | *"wo hört das Fenster auf?"* — where a mod control is parked against a window's content | `WorldUI/Conversion/PanelInkBounds.cs:546` — `enabled` ∧ `activeInHierarchy` ∧ `!cull` ∧ `color.a × GetInheritedAlpha() ≥ 0.05` | `WorldUI/MapRoom/MapTravelConfirm.cs:2231` — `enabled` ∧ `color.a > 0.02f`. **No inherited-alpha term at all** | The only painted-union instrument in the mod that does not multiply by `GetInheritedAlpha()`. A quest-info subtree faded by a `CanvasGroup` (the game does this on tab switches and scroll views) is invisible to the eye and **fully counted** here, dragging the measured bottom edge down and pushing the REISEN button off the drawn content. Exactly the defect `PanelInkBounds.cs:66-80` paid ModBuild 239 for — *"the bar was therefore placed at y=-930, 390 px under a window that ends at y=-540"*. **Its own doc at `:2210` claims a parity it does not have:** *"the same rule CanvasConversion's own host-rect fit applies."* **Found independently by two lanes.** | **1** |
| **R8** | *"das Menü sagt mir den falschen Standardwert"* | the shipped default, `Defaults/*.cs`, printed by `ConfigCatalog.Tooltip()` (`WorldUI/Options/ConfigCatalog.cs:1726`) as `Standard: <value>` | the **prose** of the same setting's own description, at its `Bind` site and in `Loc.ConfigDescriptions.German.cs` | **Seven settings state a default the mod does not ship**, one line above the real one. `[WorldUI] BarSizeScale` ships **0.80899** and its description says *"Default 1.0 = exactly the size before this dial existed… nothing changes until you tune it"* (**verified at `Defaults.WorldUI.cs:132` and `WorldUIConfig.cs:640`**). Also `[MapRoom] IconScale` (2.29637 vs "default 1"), `PartyMarkerScale` (2.76815 vs 1), `PathWidthScale` (2.72753 vs 1), `[Cards] RevealEnterDegrees` (70 vs "default 60"), `RevealExitDegrees` (5 vs "15° dead band under the 60° enter" — *both* numbers wrong, the real band is 65°), `[ButtonColors] LabelOutlineR/G/B` (0.5/0.5/0.5 vs "0.09 / 0.06 / 0.03 = dark umber"). The outline triple is the same drift `ButtonTuning.cs:82` already documents as fixed for the *constants* — the fix reached the code and not the sentence the player reads. Visible in the VR tooltip **and** in the `.cfg` the player edits. | **1** |
| **R9** | *"ein Ding in meiner Hand ist keine Kulisse"* | `Core/WallFade/WallSegmentFade.cs:7080` — five clauses, including `HeldProps.OwnsRendererOf` (added ModBuild 340 for *"in der Hand ist es garnicht oder nur immer ganz kurz für einen Frame sichtbar"*) ⟳ | `Core/MixedReality/MixedReality.cs:1757` — a hand copy with **four** clauses, no held term. `rg -c HeldProps` over that file returns **zero** | The MR copy's own doc at `:1739` says: *"Deliberate MIRROR of the wall system's guard … **Keep the two in step.**"* ModBuild 340 stepped one and not the other. Carry a chest or gold pile over an unexplored region for ~0.7 s (`RegionMembershipPass`, 60-frame cadence) and it fails the guard at `:1563`, gets `BuildUnseenUnderlay(…, forceDark: true)`, and acquires a **permanent dark backing plate that follows it home** — teardown at `:2050` fires only when the source renderer dies. A held *miniature* is immune (`SkinnedMeshRenderer`). **Severity caveat:** `[MixedReality] Enabled` ships `false` (`Defaults.Core.cs:39`, pinned by user ruling) — but MR see-through is the owner's own setup. | **2** |
| **R10** | *"das Leuchten, wenn meine Hand darüber fährt"* | `Board/FigureGrab/FigureGrabbable.cs:418` records hover separately (`_hovered[]`) and `:576 TickHighlightMode` drives **both** walk-in edges every frame | `Board/FigureGrab/GrabbableProp.cs:392` — a one-way gate, no hover record, no mode tick. `TickHighlightMode` has one definition and one call site, both in `FigureGrabbable.cs` | `FigureGrabConfig.HighlightAllowedHere` flips at runtime off `WallSegmentFade.WalkInsideEngaged`. Hold your hand over a chest and **step into the board**: the chest keeps glowing while a miniature under the other hand goes dark. **Step back out** under the same standing hover: the miniature re-lights without a re-hover, the chest stays dark until you move the hand away and back. Both edges diverge, in opposite directions. | **2** |
| **R11** | *"der Knopf, den ich drücke"* — press feedback on the two keycap families | `Cards/Tray/PlayTray.7.Nested.cs` (`BoardButton`) | `WorldUI/MapRoom/MapButtonRail.cs` (`Cap`) | **Six channels differ at once.** Travel 4 mm (config `[BoardButtons] Travel`) vs 7 mm hardcoded. Stroke: 35 ms attack → 30 ms detent → 120 ms release with 10 % rebound (`ButtonStroke.cs`) vs linear `MoveTowards`, no detent, no overshoot (`:272`). **Haptic on press: `ClickPulse` at the commit point (`:2132`) vs none — a laser press on a map cap is silent to the hand** (`rg -c SendHaptic MapButtonRail.cs` = 1, and that one is a *hover* tick). Click sound: none vs the game's own. Hover visual: **none — `StateColor()` has no hover term, so a board keycap does not change under the laser at all** vs a 45 % lerp toward `LabelColor` (`:994`). Disabled: collider stays live and the press logs `REJECTED` vs `Collider.enabled = live`, inert to finger and beam. Modal gate: suppressed under a blocking modal vs none, undocumented. | **2** |
| **R12** | *"derselbe kleine Pin"* — reach of the FOLGEN/FIXIERT pin | board's pin: `Cards/Tray/PlayTray.1.Core.cs:1345`, scanned by `CardsDriver.3.Laser.cs:1249`, reach **`Min(3 m × scale, fanOccluderSlack)`**, suppressed under a modal | combat log's pin: `WorldUI/Surfaces/CombatLogSurface.cs:1064`, reach **20 m** (`MaxCapLaserMeters:1242`), no modal gate ⟳ | Look and state are now correctly shared (`CreateFollowPin` / `ApplyFollowPinState`, 2026-09-05) — **input is not.** Step back 4 m from the table: the combat log's pin is still pressable; the visually identical pin 20 cm away on the board is not. | **2** |
| **R13** | *"heißt das Ding jetzt Brett oder Tafel?"* — one object, three German nouns, all inside the options menu | `Loc.cs:749` `control_board`, `:1234` `vr_sec_controlboard`, `:1708` `vr_ct_board` — all **"Kontrollbrett"** | `Loc.cs:878` `vr_var_board` — **"Kontrolltafel"**, the heading over the per-board rows (`VROptionsTab.5.Variants.cs:96`) ⟳ | `Loc.cs:1380` states the standing rule for this exact word class: *"'Bretter', not 'Boards' … everywhere else in the German menu the object is a Brett."* Same shape twice more: the decision dock is **"Entscheidungsleiste"** (`Loc.cs:848`, `ConfigNames.cs:715`), **"Entscheidungsdock"** (`Loc.cs:1550`, `ConfigNames.cs:550`) and bare **"Entscheidung"** (`ConfigNames.cs:549/551/753`); and a boolean's state word is **"Ein"** in `VROptionsTab.2.Rows.cs:2261` (with the ruling beside it — *"the game says 'Aus' in German, so a mod row beside it must too"*) and **"An"** in `ConfigCatalog.cs:1512`. | **2** |
| **R14** | *"das Namensschild"* — a free-floating world label | `Net/Avatar/OwnerTag.cs:107` and `Net/Remote/RemoteNameTag.cs:274` both call `BoardVisual.OrderWithPanels(...)` after billboarding | `Board/Patches/PingNameTag.cs:493` — billboards identically and **ranks nothing**. `rg -c 'OrderWithPanels\|FreeLabelOrder\|sortingOrder\|renderQueue'` over that file returns **zero** | A mod-owned world-space `TextMeshPro` left at default sortingOrder 0. `WorldUI/FreeLabelOrder.cs:90` exists because a converted panel is rewritten to sortingOrder ≥ 100 every frame *"and 0 loses to 100+ at every distance and every angle"*. Ping a hex behind the initiative row and the name label is painted over — while **that same player's** head tag composites correctly a metre away. The tag lives ~2 s, so it reads as a flicker. The two tags that were fixed were fixed for user report 2026-08-04; the third was never in the sweep. | **2** |
| **R15** | *"was ein toter Knopf sagt"* — a disabled control's caption | `Cards/Tray/PlayTray.7.Nested.cs:1125` — `tmp.color` is written **once at build** and never again (`rg '_label\.color'` over the file: **zero hits**; `UpdateColor()` at `:1558` touches only face, bevel and wall) | `WorldUI/MapRoom/MapButtonRail.cs:1003` — `LabelColor * 0.45f` when dead · a harvested uGUI button uses the game's own `disabledColor` | A dead CONFIRM key on the control board is a **full-brightness gold word on a dark-wood plate**; a dead cap on the map table dims its caption; a dead harvested button greys its text. Three answers to one question. | **2** |
| **R16** | *"zwei 'over budget'-Zahlen im selben Log"* | `Core/Perf/PerfMonitor.cs:674` — budget = `1 / live XR display refresh rate`; window = `[Perf] SummaryIntervalSeconds`, **default 30 s**, tunable 5–600 | `WorldUI/Sharpness/PanelSupersample.1.Core.cs:599` — `FrameBudgetMs = 1000f / 90f`, **hardcoded**; window **hardcoded 10 s** (`:406`) | On the user's rig (Quest 3 / Virtual Desktop at 72, 80, 90 or 120 Hz, and the runtime can lock to half rate) one log carries `over-budget 210/2700 (7.8%)` beside `MOTION BUDGET (this 10 s window, threshold 11.11 ms = one 90 Hz frame) … 480 over the threshold`. Two numbers that must agree, disagreeing by construction — by refresh rate, by a 3× window ratio, and again the moment anyone tunes the interval. `PerfConfig.cs:344` already states the rule the other instrument breaks: *"budget = 1 / actual refresh rate, read from the XR display — **not a hardcoded 72/90 Hz**"*. | **2** |
| **R17** | *"der Name eines Mitspielers"* — where the identity row sits | `Net/Remote/RemoteNameTag.cs:376` — measures the **rendered glyph run** (`MeasureInkWidth`) and centres `avatar + pad + inkWidth` | `Net/Avatar/OwnerTag.cs:157` — `totalWidth = avatarSpan + NameWidth`, label **left-aligned inside a fixed 0.16 m box** (`:160`) | `RemoteNameTag.cs:30` names B's layout as a **fixed defect**: *"Centring the avatar plus the fixed `NameWidth` container instead — **the old layout** — pushed every short name's visible ink left of the mask by half the container's unused tail"* (user report 2026-08-02). On a peer's board corner a short name still hangs ~5 cm left of its anchor, on a 0.64 m board whose anchor is already at x = −0.30 — while **the same person's head tag is centred correctly**. `git log --follow`: `e5a3aaf8` touched `RemoteNameTag.cs` and not `OwnerTag.cs`; every earlier tag commit touched both. | **2** |
| **R18** | *"ein langes deutsches Wort passt hier und dort nicht"* | `VROptionsTab.2.Rows.cs:296` — `fontSizeMin = Max(9f, authored × 0.78f)`, no wrap, **and** logs `OPTION NAME TOO LONG` when it still does not fit | `VROptionsTab.2.Rows.cs:1168` — `fontSizeMin = 9f`, wrapping **on** (justified in its own doc) · `WorldUI/Options/VariantTiles.cs:274` — `fontSizeMin = **10f**`, no wrap, **no probe** ⟳ | One standing ruling (*"The name must never be replaced by an ellipsis: shrink the glyphs instead"*), three implementations with three floors. The same over-long German word ("Panzerhandschuh", "Kontrolltafel") stops shrinking one point earlier on a variant tile than on the row beside it, and only path A tells the log when a name did not fit — the other two fail silently. | **2** |
| **R19** | *"das Gegenstands-Fächer dreht sich nicht mit"* | `Cards/Piles/PileBrowser.cs:449` — the billboard write sits **outside** the `if (_boardAnchored)` guard, so the discard/burnt arc re-faces on both paths | `Cards/Piles/ItemsPile.cs:951` — `FaceHead(_root)` is **inside** the guard; the head-fallback path (`PlaceAtHead()` at `:1189`, reached from `:618` when there is no board) never re-faces | `ItemsPile.FaceHead` is documented at `:956` as *"mirror of PileBrowser.Tick's facing math"* — an admitted copy that got one guard wrong. With no control board: open the discard browser and step sideways and the arc turns with you; open the item browser and step sideways and it goes **edge-on and stays there**. | **3** |
| **R20** | *"der Knopf leuchtet auf, wenn ich draufziele"* | `WorldUI/Grab/ModalCloseButton.cs:350` — flat grey multiply `0.9 / 1.05 / 0.75` | `WorldUI/Options/VariantTiles.cs:351` — brass `TileBorderOn/OnHot/Pressed` · `WorldUI/Options/VROptionsTab.2.Rows.cs:778` — a *different* gold `(0.50,0.40,0.18)` / `(0.68,0.55,0.24)` ⟳ | Three hand-authored `Button.colors` palettes, **all three with `fadeDuration = 0.08f`** and no shared constant — evidence the number was copied rather than shared. The close X brightens by a flat 5 % grey; a tile and a settings row each brighten toward a different gold. | **3** |
| **R21** | *"was tickt unter dem Finger?"* — hover haptics in the map room | `WorldUI/MapRoom/MapButtonRail.cs:1671` — the **laser** hover fires `HapticPreset.HoverTick` | the **fingertip** path (`OnPokeEnter:1992` → `SetPokeHover:1605`) fires nothing · `WorldUI/MapRoom/MapLocationInteractor.cs` fires nothing on **any** path (absent from the mod-wide `SendHaptic` census) | Hovering the *Händler* cap with the beam ticks; hovering the same cap with your finger does not. Picking a place on the map is haptically silent while a cap on the same table rim ticks. | **3** |
| **R22** | *"ein Finger-Tipp fühlt sich doppelt an"* | `Hands/Interact/PokeInteractor.cs:355` sends `ClickPulse` **unconditionally after** `OnPoke` returns | `Cards/Piles/PileViewer.cs:1300` and `Cards/VRCard.cs:2009` send their **own** `ClickPulse` too | A finger poke on the discard pile fires **two overlapping impulses**; a laser click on the same stack (`PileStack.LaserToggle:1317`) fires one. Mirror image: `BoardButton.OnPoke` (`:1980`) deliberately does nothing for an enabled cap, yet the interactor buzzes a full click at 8 mm anyway — the exact brush R5's depth-fire exists to make inert — and buzzes again at 90 % travel. | **3** |
| **R23** | *"welchen Wert hat das Ding, bevor irgendwas geladen ist?"* | `WorldUI/Buttons/ButtonTuning.cs:59-65` — five `Clamped()` pre-bind fallbacks | `Defaults/Defaults.WorldUI.cs:44-50` — the shipped defaults | `[BoardButtons] Width` 0.073 vs **0.063**, `Height` 0.073 vs **0.065**, `Depth` 0.036 vs **0.014**, `[BoardDashboard] Height` 0.030 vs **0.035**, `Depth` 0.030 vs **0.014**. The other seven constants in that same block were rewritten to *name* `Defaults.*` precisely to end this drift (`:76-88`); these five kept a stale literal, and the remote twin (`RemoteBoardFurniture.cs:245`) already reads `Defaults.*` — so local and mirrored pre-bind geometry disagree by up to 2.6×. **Observability is LOW:** every geometry consumer calls `Bind()` first, except the log line at `CardsDriver.1.Core.cs:645`, which would report 0.073 m for a 0.063 m cap if it ran pre-bind. Listed because it is the `[[a-clamp-fallback-is-not-a-default]]` lesson recurring in a file that already documents it. | **3** |
| **R24** | *"Namensschilder aus"* | `Net/Remote/RemoteNameTag.cs:175` is the **only** reader of `NetModule.NameTags` | `Net/Avatar/OwnerTag` is built unconditionally at `Net/Remote/RemoteControlBoard.cs:1643` | Turn `[Net] NameTags` off and every peer's username is still readable off their board corner. The English text says *"turn OFF to hide **all** tags"* / German *"AUS blendet **alle** Schilder ohne Neustart aus"* — but the sentence's first clause scopes it to *"above each remote VR player's head mask"*. **Ambiguous, and written down nowhere either way.** Confidence: the behaviour is certain, the intent is not — this may be a doc fix rather than a code fix. | **3** |
| **R25** | *"welche Kameras gibt es?"* | `WorldUI/FlatScreen/CameraInventory.cs:70` → `Camera.GetAllCameras` = **enabled only**; tags `[VR head]`/`[RT stack]`; `rect` at `F2`; `VRLog.Info` = the **Debug** tier | `WorldUI/EyeReachCensus.cs:405` → `FindObjectsOfType<Camera>(true)` = **includes disabled**, deliberately; tags `[VR HEAD]`/`[MOD]`/`[GAME]`; `rect` at `F3`; `VRLog.Note` = the **Info** tier | Developer-facing. The two lines report different counts of "the cameras", never say so, and are **never both visible**: at the shipped level only B prints; raise to Debug for A and you now see two disagreeing populations with no statement that one is enabled-only. The only place the difference is written down is B's doc — the wrong file for anyone reading A. Related: `EyeReachCensus.cs:715` prints *"N **active** renderer(s)"* where `N` is the raw array length (including renderers whose `enabled` is false), against `PerfSceneProfile`'s careful total/enabled/visible/inMask/submitted split. | **4** |

---

## 2. Identical today, structurally free to drift

No player can see these right now; they rank below §1 on purpose. Each is here because the mechanism
that would keep the copies equal **does not exist**, and in several cases the code's own comment
asserts a singularity that is already false.

| # | Concept | Sites | Why it will drift | Rank |
|---|---|---|---|---|
| **R26** | The FOLGEN/FIXIERT cap's **state**, on a peer's mirrored board | `Cards/Tray/PlayTray.7.Nested.cs:1316` `ApplyFollowPinState(follow)` vs `Net/Remote/RemoteBoardFurniture.cs:2003` `SetPinned(pinned)` — **inverted sense** — which hand-rolls label + `SetCapRole` + a direct `SetTint` instead of the shared state ladder | `PlayTray.7.Nested.cs:1246` says of its accent colour: *"**One literal for every follow/pin toggle in the mod.**"* That claim is false — `RemoteBoardFurniture.cs:468` declares the same `(0.58, 0.46, 0.26)` again. `check-mirrors.sh` cannot lint it (a `Color`), and that file's own palette note already concedes the limit: *"they are `Color` values, which `scripts/check-mirrors.sh` (a float/string extractor) cannot lint."* The 2026-09-05 round unified the two **local** pins; the peer's is a third. | **1** |
| **R27** | The house "is this graphic painting?" alpha floor, `0.05` | `CanvasConversion.3.Fit.cs:82` `FitMinAlpha` (**already `internal`**) · `RemoteWidgetMirror.cs:169` (linted with it) · `PanelInkBounds.cs:165` `FaintAlphaFloor` (**not linted**) · `EnemyRevealSurface.cs:308` `DrawAlphaFloor` (**not linted**) · `ModalFallback.9.Spawn.cs:3014` (**a bare inline `0.05f`, structurally unlintable**) | `check-mirrors.sh` has a group for exactly this and covers **2 of 5**. Its own header warns *"The review undercounted, which makes the lint worth more, not less."* It undercounted again. The inline one is sharpest: `DrawsAnythingLoose`'s doc at `:3002` says *"This project has shipped a second copy of a visibility test twice and both copies were weaker than the original"* — and it **is** that copy, with the constant frozen as a literal. | **1** |
| **R28** | Laser beam geometry | `20 m` reach at **6 sites** (`RayInteractor.cs:30`, `RayUguiDriver.cs:38`, `RayGrabDriver.cs:46`, `MapLocationInteractor.cs:698`, `CombatLogSurface.cs:1242`, and `MapButtonRail.cs:1636` as a **bare inline `20f`**); `0.005 m` occlusion epsilon at **6 sites** (same files + `CardsDriver.3.Laser.cs:1158`, `FlatScreen.6.Pointer.cs:68` inline) | No beam term is in any lint group. Retune reach in one place and it changes for four of six controls. | **2** |
| **R29** | *"Keine Handkarten"* — the empty-hand placard's words | `Cards/EmptyFanHint.cs:95` — `Loc.CurrentLanguage == "German" ? "Keine Handkarten" : "No hand cards"` | `Net/Remote/RemoteEmptyFanHint.cs:247` — the identical ternary, for a **peer's** hand | The string is **not in `Loc.cs` at all**; both copies inline the language test. Edit one and the words over your own hand differ from the words over your teammate's. This is the FOLGEN/FIXIERT shape one level down: same two words, two sources. | **2** |
| **R30** | Per-tick exception isolation | canonical `Core/Perf/TickGuard.cs:78` · deliberate fork `Net/Desync/DispatchGuard.cs:60` (justified, §3) · hand-rolled `Cards/Driver/CardsDriver.2.Update.cs:840` · hand-rolled `Board/FocusDriver.cs:242` · hand-rolled `WorldUI/Patches/EscMenuShowSafety.cs:132` | Three real defects, not just duplication. **(a)** `FocusDriver.Carrier` suppresses for ever after the first line (`_reported.Add(name)`) and never emits the `is still throwing (N time(s) so far)` line the project's own triage procedure counts throw storms off, so a carrier throwing every frame is invisible to that method. **(b)** Cards' counter is **per driver instance** and TickGuard's is static, so two lines with the same wording carry non-comparable `N`. **(c)** Cards keys the whole tick path where TickGuard keys per named sub-step, so two throwing subsystems inside Cards look like one. | **2** |
| **R31** | Reading a clamped config value | three byte-identical private `Clamped(...)` helpers: `Net/Board/PeerBoardFade.cs:184`, `WorldUI/Buttons/ButtonTuning.cs:546`, `Core/WallFade/WallSegmentFade.cs:721` ⟳ | The four-line warning — *"THE NUMBER INSIDE `Clamped()` IS THE PRE-BIND FALLBACK, NOT THE SHIPPED DEFAULT … confusing the two has cost this project two rounds, twice"* — lives on **one** of the three copies. See R23 for what happens on a copy that does not carry it. | **2** |
| **R32** | User-facing strings | `Core/Loc/Loc.cs` is the mod's string table; `Core/SelfUpdate/SelfUpdateText.cs:33` holds a **second one**, with its own `Pair()` and a `T()` byte-identical in logic to `Loc.Mod` (`Loc.cs:174`) | The file **flags itself as owed**: *"INTEGRATOR NOTE: these belong in `Core/Loc.cs`'s embedded table … They are here only because `Loc.cs` is not this lane's file. Lifting them is a copy of `Table` into `Loc.Build()` and a replacement of `SelfUpdateText.T` with `Loc.Mod`."* `"Abbrechen"` is now defined in both (`SelfUpdateText.cs:50 upd_cancel`, `Loc.cs:1864 ver_cancel`). Any future change to `Loc.Mod` — a new language, a font pass, a fallback fix — reaches one table. `check-docs-i18n.py` covers the **docs**, not the in-mod tables. | **2** |
| **R33** | The empty-hand placard's **look** | `Cards/EmptyFanHint.cs:39-44,220,237` vs `Net/Remote/RemoteEmptyFanHint.cs:56-62,228,246` | **8 hand-copied values** (`FadeSeconds 1.5`, `PlateAlpha 0.55`, `TextAlpha 0.95`, `Parchment`, `InkBrown`, plate `w×1.1 × h×0.42`, fit box `h×0.34`, fade curve `1−p²`), **zero lint coverage** — three are `Color`s and so unlintable by construction. Pairs with R29: same object, words *and* look both duplicated. | **2** |
| **R34** | The card hover pop, and the pile arc's bow | pop `0.012 m / +18 % / 8 s⁻¹` at 4 sites (`VRCard.cs:2194` inline, `ItemsPile.cs:4783`, `RemoteHandFan.cs:339`, `RemoteBrowserFan.cs:662`, `RemoteItemFan.cs:841`) · arc `0.55` / `0.85` inline at `PileBrowser.cs:506,508` and `ItemsPile.cs:1278,1280` vs the shared `RemotePileFronts.FanArchFactor/FanTiltFactor` | The **local** half is an inline literal, so `check-mirrors.sh` is blind to it by construction. `RemotePileFronts.cs:56` already consolidated the mirror side *because both mirrors had dropped the −12° reading pitch* — *"a peer's item fan and a peer's browse fan both stood 12 degrees more upright than the arcs their owner was reading"* — and concedes *"It is a LITERAL on the owner's side too."* Retune the owner and the two mirrors silently stay put. Also `SplitOffset` exists 4× (`FanSweep.cs:330` + three byte-identical private copies). | **2** |
| **R35** | Chrome parked beside a window (ENTER DUNGEON, REISEN, the intro hint, the story picture) | **five** private implementations: `LoadoutConfirmPark.cs`, `HintOnOwnerComposite.cs`, `StoryComposite.cs`, `EnchantressComposite.cs`, `MapTravelConfirm.cs`+`MapQuestReadyUp.cs` — each with its own painted-bounds sweep and its own park/unpark | Constants hand-copied with comments naming their source: `OffsetEpsilonPx = 0.5f` ×4, `ClaimGraceSeconds = 1.5f` ×2 (*"`MapQuestReadyUp`'s `ClaimGraceSeconds`, for its reason"*), `AnchorRefreshIntervalSeconds = 0.2f` ×2, and **three independent 24 px gaps**. Only `LoadoutConfirmPark` clips to uGUI masks and only it has an escape lane when the union fills the frame — the other four, in the same situation, clamp the control back onto the drawn content, which is the photograph (`fenster-abstand2.jpg`) `LoadoutConfirmPark` was rewritten for in ModBuild 382. The same shape as the grab-bar calibration case. | **2** |
| **R36** | Two inline EN/DE fallbacks that outlived their own delete instruction | `Cards/Driver/CardsDriver.6.Flows.cs:326` vs `Loc.cs:1315 item_fan_open_hint` · `Cards/Tray/PlayTray.5.Status.cs:589` vs `Loc.cs:1301 confirm_unready` ⟳ | Both keys now exist, so both fallbacks are dead second copies. `Flows.cs:322` says verbatim: *"**Delete this fallback once the key is in the table.**"* `confirm_unready` is drawn on a **keycap that is mirrored to peers**, so a divergence would show two boards with different wording. | **3** |
| **R37** | The hover-panel anti-churn watch | `WorldUI/Surfaces/StatPanelSurface.cs:91,107,908,916,1069` vs `WorldUI/Surfaces/PropInfoSurface.cs:57,65,275,…` — `Watch`, three constants, `DetachWatch`, `CountConversion`, `ScheduleRelease`; ~60 lines, still `diff = 0` | The 2026-08 review ruled **against** merging (the constants are per-surface tunables) and recommended the **zero-risk half** instead: *"add a cross-reference comment at each of the five duplicated members."* That was never done — `PropInfoSurface.cs:55-63` carries no reference to `StatPanelSurface`. **Note the good news in the same pair:** the rest of it *has* consolidated well — `PropInfoSurface` now **calls** `StatPanelSurface.TryComputeHeldPose`, `.SignFor`, `.StripLogicComponents`, `.BuildStaticCopy` — which is the direct answer to his earlier "props rewritten from scratch" complaint. | **3** |
| **R38** | A card held in the hand | reading pose: `Cards/VRCard.cs:1254` vs `Cards/Piles/ItemsPile.cs:6115` — a **verbatim copy** by its own admission, only the height term legitimately differing. Left-hand mirror rule spelled **four** times: `HeldPoseMirror.OffsetSign` (figures + props), `VRCard.cs:1262`, `ItemsPile.cs:6125`, `HeldCardGrip.cs:284` | The *rigid grip* half **is** shared and says why: *"the reading pose next door is duplicated between them … the 2026-08-09 report is what that cost: the left-hand mirror was fixed in one copy and not the other, and the item card sat 11 cm out for five days."* `CardGripPose.cs:51` records the mirror rule has *"NOW BEEN BROKEN TWICE IN THIS FILE'S SHORT LIFE."* The 2026-09-05 round gave figures and props one home (`HeldPoseMirror`); the cards did not join it. | **3** |
| **R39** | Enrolling a window as "open" | `ModalFallback.4.Tick.cs:2190`/`:2701` (ENROLLED) and `ModalFallback.10.CatchAll.cs:279` both ask `FloatRefusalTable.Refuses` | `ModalFallback.7.Close.cs:419 AddPollWindow` and `:425 AddGroupWindow` — **never ask** ⟳ | One policy, three implementations, one of which does not implement it. Already written down as a live diagnosis in a shipped log line at `WorldUI/Composites/QuestJourneyCurtain.cs:740`. | **3** |
| **R40** | Things crumbling to dust | `Cards/Art/CardDustFx.cs:89` — gated by `[Cards] CardDust`, **shipped OFF** by user ruling · `WorldUI/Buttons/ButtonTuning.cs:849` — structurally the same code, same two-burst shape, **no on/off dial at all** · `WorldUI/Materialise/WindowMaterialise*` — a third, deliberately different effect | `CardDustFx.cs:9` calls itself *"a deliberate sibling of `WorldUI.ButtonDissolveFx` — same look."* At shipped defaults cards vanish with no dust (he turned it off: *"read as a huge spark animation sweeping across the WHOLE board"*) while board buttons still puff with the same construction and **cannot be turned off**. Whether that ruling *should* extend is a user question, not a code question — but one look has two switches and one has none. | **3** |
| **R41** | Draw order inside one window | the ladder is **well owned** (`CanvasConversion.8.Order.cs`, one registration seam). But `PanelOrderStep = 16` is `private const`, so the offsets that must stay under it are ten separate literals: `GrabBarLayout.BarOrderOffset 4`, `ModalCloseButton.XOrderOffset 2`, `WorldTooltips 10`, `TablePanelSurfaces 12`, `WindowMaterialiseDebris ±1`, plus prose restatements in `WristHud.cs:65`, `FreeLabelOrder.cs:90`, `HandGhost.cs:133`, `BoardVisual.cs:105`, `MapRoomHand.3.Wrist.cs:128` | `2 < 4 < 10 < 12 < 16` is consistent today and there is no assert. A future offset ≥ 16 pierces the next panel silently. Cheapest fix in the document: make `PanelOrderStep` `internal` and derive the five real constants from it. | **4** |
| **R42** | Throttling a log line | **~20+** hand-rolled rate-limiters in at least four incompatible shapes (deadline, delta, change-gated signature, count-capped); `Core/VRLog.cs` offers no throttle | They disagree about what "changed" means, and only `QuestJourneyCurtain.cs:757` carries the `AuditHeartbeatSeconds` that handles the *held-instrument-reads-as-dead* hazard. The other change-gated censuses (`PeerBoardFade.cs:1583`, `SelectionReadyHighlighter.cs:223`, `ActorPropBody.cs:931`) have no heartbeat: a constant signature prints once and then reads as a stopped tick. | **4** |
| **R43** | "How many LOD groups are there?" | `Core/Perf/PerfSceneProfile.cs:1404` (30 s cadence; counts `active` **and** `enabled`) vs `Core/Perf/AutoLod.cs:613` (15 s cadence; `active` only) | Same `FindObjectsOfType<LODGroup>()`, two cadences, two near-identical prose sentences (*"— NONE, so lodBias and maximumLODLevel above are inert here…"*). Nothing tells a reader which of the two numbers in a log is fresher. | **4** |
| **R44** | Citations to code that no longer exists | `Net/Remote/RemoteItemFan.cs:126` and `RemoteBrowserFan.cs:84` declare `HandPalmOffset = 0.16f` with `// ItemsPile.HandPalmOffset` / `// PileBrowser.HandPalmOffset` | **Neither named constant exists any more** — `15286350` deleted them with the whole-fan trigger grab; the mirrors still carry the dead hand-held branch. Inert today (both wire bits are hard-false), but it is exactly the shape a future reader "fixes" by re-syncing to a constant that is gone. Also: `RemoteBrowserFan.cs:72 MaxCards = 16 // PileBrowser's own list capacity` — `PileBrowser.cs:66` is a `List` *initial capacity*, not a cap; and `RemotePickBanner.cs:126` still cites a 96-byte cap against a shipped 160. | **4** |
| **R45** | Two doors on "pick your environment" | `WorldUI/Options/VariantTilesTable.cs:79` — the live picture-tile strip: `ChooseSky()` sets `[Sky] Style` **and clears `[MixedReality] Enabled`**, *"otherwise the player picks 'Keller' and keeps looking at their living room"*, and offers an MR tile as a fifth environment | `WorldUI/Options/VROptionsTab.4.Curated.cs:1620` — the declared fallback dropdown: four sky values, **no MR clear, no MR option** ⟳ | B is genuinely unreachable today (`VariantTiles.cs:130` calls it *"the code path nobody reaches while this one builds"*), so this is latent. The finding is that **the declared safety net does not do what the live path does.** Same shape for the mask: the dropdown argues at length that an out-of-range `MaskId` must be *shown, not swallowed* — *"'cannot happen' is not a reason to display a lie"* — while the live tile strip (`VariantTilesTable.cs:196`) clamps and lights the last tile, i.e. displays exactly that lie. The reasoning survives only in the dead path. | **4** |

---

## 3. Recommendations — what to make the reference, and what it costs

Ordered as §1/§2. Each names the implementation that should win and the size of the change. The
project's own model for how to do this well is `Core/OcclusionFade.cs:21`, which is worth reading
before starting any of them:

> "What is genuinely the same is the DECISION between a stream of raw coverage fractions and a stable
> boolean … That is this file, and nothing else is. **THE DRIFT THIS ENDS.** `PeerBoardFade` was
> hand-copied from the wall's design … by 2026-08-27 the board still carried the collapsing
> `Min(Off, On)` low bar the wall replaced in ModBuild 252 … Every one of those is now structurally
> impossible to have on one side and not the other, because there is one body of code."

**Extract the decision, not the delivery.** That is the shape every recommendation below aims at.

**R1 — release re-face.** `GrabbableModal`'s is the reference: it is the only one that reads the dial
and the only one that respects the shared-window ruling. But do **not** merge the four
`OnGrabFinished` bodies — they legitimately differ in pivot (ink union vs frame origin) and one
correctly does nothing. Extract only the *policy*: a `WindowFacing.WantsReFaceOnRelease(bool shared)`
that all four consult, leaving each owner its own rotation arithmetic. Two of the four (`PlayTray`,
shared windows) will answer "no" and keep today's behaviour. **Cost: one small helper plus three
call-site guards.** This is the highest-value row in the document — a setting the user asked for
reaches one quarter of the objects it names.

**R2 — one row, one name.** `Loc.ConfigNames.cs:161` already states the rule and four rows already
obey it. Do **not** merge the two tables — the everyday caption is deliberately shorter than the
browser's descriptive name for several rows, and that is a feature. Instead extend
`scripts/check-options-coverage.py` with a fifth check: for every curated row that also has a
`ConfigNames` entry, either the two strings are equal or the pair is listed in a
`DELIBERATELY_DIFFERENT` table with its reason. That turns 20 silent disagreements into 20 decisions,
each of which is then a one-line edit. **Cost: ~40 lines of Python and one triage pass.**

**R3 — the active-cards grid.** `ActivePileViewer` is the reference by the 1:1 ruling. Two options,
and the cheap one is right: promote `ActivePileViewer.Columns` to `internal` and have
`RemoteActiveCards` read it, exactly as `RemoteBoardFurniture.PinIdleColor` was changed from a
hand-copied literal into a call into `PlayTray.BoardIdleColor` for the same reason. `MaxCards = 6`
is a separate decision — it is a wire-buffer bound, so either raise it with the record's cap or make
the truncation *visible* rather than silent. **Cost: one visibility change plus one field; the
`MaxCards` half needs a wire check.**

**R4 — the map-room cap laser.** `CombatLogSurface.TickCapLaser` is the reference: it is the same
`Collider.Raycast` shape and it honours `SolidOccluderDistance` (`:1087`). The two methods are
already a documented deliberate pair (§4), so this is not a merge — it is **adding the missing veto
to one of them**, three lines, plus a decision about the modal commit gate that `CombatLogSurface`
documents omitting and `MapButtonRail` omits silently. **Cost: small, and the highest
correctness-per-line in the document.**

**R5 / R11 / R15 / R21 / R22 — the two keycap families.** These are one finding seen five ways:
`PlayTray.BoardButton` and `MapButtonRail.Cap` are two implementations of "a physical keycap on a
table". They are **not** cheap to merge — `MapButtonRail` deliberately samples the game's art and
drops `NativeButtonSkin.CreateFace` on a user ruling (§4), and its caps wrap game
`UIGuildmasterButton`s rather than mod-owned meshes. The realistic move is a **written contract**
rather than shared code: one document (or one doc comment referenced by both) fixing the answers to
travel/commit rule/press haptic/hover visual/disabled look/modal gate, then bringing each family to
it one channel at a time. Start with the two that are pure omissions and need no design decision:
the **press haptic on map caps** (R11) and the **disabled caption dim on board caps** (R15). The
**commit rule** (R5) is a genuine design question for the user, not a bug to fix silently.

**R6 — "does this window draw anything".** The fix is written in the source and is the right one:
make `ModalFallback.DrawsAnythingScriptSide` and `GroupChainAlpha` `internal`, delete
`SurfaceMaterialise`'s copy, and **carry the copy's extra `CanvasChainDisabled` term into the
shared method** — the copy is the more correct of the two. **Cost: two visibility changes, one
deletion, one term moved.** The precedent is exact: `FitMinAlpha` was promoted to `internal` in
ModBuild 291 for this reason, its doc saying *"the two must use ONE floor or the rule that hides a
window and the rule that brings it back could disagree about the same graphic."*

**R7 — the ink sweep.** `PanelInkBounds` is the reference and says so at length. `MapTravelConfirm`'s
sweep should gain the `GetInheritedAlpha()` factor; whether it should adopt the whole of
`PanelInkBounds` is a bigger question, because it legitimately clips to `RectMask2D` where
`PanelInkBounds` does not. Do the one term first — it is the term with the named defect behind it —
and leave the mask question alone. **Cost: one multiply, plus deleting the false parity claim at
`:2210` or making it true.**

**R8 — tooltips stating a wrong default.** Seven prose edits, no code change. The deeper fix is to
stop writing the number in prose at all, since `ConfigCatalog.Tooltip()` already prints
`Standard: <value>` from the live entry one line below. **Cost: seven sentences in two files
(English bind site + `Loc.ConfigDescriptions.German.cs`), and a note in `scripts/rebase-defaults.py`
that a re-base invalidates prose it does not rewrite.**

**R9 — the figure guard.** `WallSegmentFade`'s is the reference. Its own doc explains why the held
term must come *before* the memo, so a literal copy is wrong; but `HeldProps.OwnsRendererOf` is
already `internal` and already called across a module boundary. Add the one clause to
`MixedReality.IsFigureOrActorRenderer` and update the "keep the two in step" comment to say what
changed. **Cost: one clause.** Better still, since both files call the same public helper and the
guard is four ancestor walks, this pair is a genuine candidate for one shared predicate — but
`WallSegmentFade`'s memo fast path means the shared thing would be the *clause list*, not the method.
⟳ WallFade is moving; check first.

**R10 — prop vs figure highlight.** `FigureGrabbable`'s is the reference; it is the one with both
edges. `TickHighlightMode` is already written and has one call site — giving `GrabbableProp` a hover
record and calling the same method is the whole change. This is the exact complaint he made about
props ("alles nochmal neu für die Props geschrieben") in its last unfixed corner: the 2026-09-05
round unified the held pose and left the highlight edges behind. **Cost: one field and one call.**

**R12 — the pin's reach.** The board's `Min(3 m, fanOccluderSlack)` is the considered number; the
combat log's 20 m is inherited from `MapButtonRail`'s table caps, where 20 m is right because a map
table is looked at from across the room. Since `CreateFollowPin` now owns the *control*, the reach
should travel with it: give the shared factory a reach parameter with the board's value as the
default. **Cost: one parameter.** ⟳

**R13 / R18 / R29 / R32 / R36 — the wording layer.** Five rows, one theme: the mod has four parallel
EN/DE tables and a handful of inline ternaries, and there is no check that one concept has one word.
Take them in this order, cheapest first: **R36** (delete two fallbacks whose own comments say to —
zero risk), **R29** (move *"Keine Handkarten"* into `Loc.cs` and have both sites call `Loc.Mod` —
this is the FOLGEN/FIXIERT shape exactly and costs one key), **R32** (lift `SelfUpdateText.Table`
into `Loc.Build()`, a mechanical move the file itself specifies), **R13** (three vocabulary
decisions, then a handful of string edits), **R18** (one shared `FitCaption(label, floor, wrap)`
that keeps three floors as arguments and gains the missing probe on two paths).

**R14 — the ping name tag.** `OwnerTag`/`RemoteNameTag` are the reference and already share
`BoardVisual.OrderWithPanels`. `PingNameTag` needs the same two lines. Note the exemption list at
`BoardVisual.cs:158` names the owner tag as already driven "by somebody else" — the ping tag is not
in that list and is driven by nobody. **Cost: two lines plus a renderer cache.**

**R16 / R25 / R42 / R43 — the instrument layer.** These are developer-facing and should be batched.
`PerfMonitor` is the reference for anything frame-budget shaped: expose `PerfMonitor.BudgetSeconds`
and `SummaryIntervalSeconds` and have `PanelSupersample` read them instead of hardcoding 90 Hz and
10 s. For the censuses, the cheap and durable fix is not to merge them but to make each line **state
its own population rule** in the line itself, which is already the house style in
`PerfSceneProfile`'s SCENE line. **Cost: one accessor pair, then wording.**

**R17 — the owner tag's centring.** `RemoteNameTag`'s ink measurement is the reference and its doc
names the old layout as the defect. `MeasureInkWidth` is a static helper on a `Net.Remote` type;
either promote it or move it beside `TmpFit`. **Cost: one helper move plus ~10 lines in `OwnerTag`.**

**R19 — the item browser's facing.** `PileBrowser`'s guard placement is the reference. Move
`FaceHead(_root)` out of the `if (_boardAnchored)` block in `ItemsPile.Tick`. **Cost: one line.**
This is the smallest fix in the document with a named observable symptom.

**R20 / R26 / R27 / R28 / R31 / R33 / R34 — the constant layer.** All seven are the same
recommendation: **`scripts/check-mirrors.sh` is the right tool and its coverage has fallen behind its
own subject matter.** Rather than merging code, do three things. (a) Add the pairs it *can* already
lint — the 20 m and 0.005 beam terms (R28), the three `Clamped` helpers' host constants (R31), the
`0.05` floors that are named constants (R27, 2 of the 3 unlinted ones). (b) Teach it to extract
`Color` literals, which unlocks R26 and R33 and the `DisabledColor`/`ConfirmedColor` pair the
`RemoteBoardFurniture` palette note already flags as knowingly-still-copies. (c) For the ones that
are **inline literals** and therefore structurally unlintable — `ModalFallback.9.Spawn.cs:3014`'s
`0.05f`, `MapButtonRail.cs:1636`'s `20f`, `VRCard.cs:2194`'s pop terms, `PileBrowser.cs:506`'s
arc factors — the fix is to give each a name *first*; that alone converts them from invisible to
lintable and costs nothing behaviourally. R20's `fadeDuration = 0.08f` is the same move.

**R23 — the five stale clamp fallbacks.** Point them at `Defaults.*` exactly as the seven constants
beside them already are (`ButtonTuning.cs:76-88`). **Cost: five lines.** Low urgency, near-zero risk.

**R24 — the name-tag setting's scope.** Ask the user, then either add the gate to `OwnerTag` or
narrow the setting's description. Do not guess. This is a doc fix or a two-line code fix depending on
one answer.

**R30 — the tick guards.** Do not merge them; `DispatchGuard`'s fork is correctly argued (§4) and
the hand-rolled ones sit in files with their own lifetimes. Fix the three defects instead:
give `FocusDriver.Carrier` a repeat line in the house wording, make Cards' counter static, and key it
per sub-step. **Cost: ~15 lines across two files**, and it restores a subsystem to the project's
own throw-storm triage method.

**R35 — the five window-chrome parks.** The largest item here and the one to schedule rather than
squeeze in. `LoadoutConfirmPark` is the most advanced (masks, escape lane) and is the reference. But
its own comment refuses to extend `ClipToMasks` to its union, so the shared core is **not** the
sweep — it is the *park/unpark state machine plus the claim grace*, which is where the hand-copied
constants live. Extract that; leave five bounds sweeps alone until R7 settles what a bounds sweep
should be.

**R37 / R38 / R39 / R40 / R41 / R44 / R45 — the tail.** Each is a single small edit and none has a
named symptom: add the cross-reference comment the 2026-08 review already specified (R37); let the
two card reading-pose sites join `HeldPoseMirror` (R38); route `AddPollWindow`/`AddGroupWindow`
through the refusal table or document why not (R39); decide whether the button dust needs the dial
the card dust has (R40 — a user question); make `PanelOrderStep` `internal` (R41); delete the dead
citations (R44); reconcile the tile strip and its dead fallback, or delete the fallback (R45).

---

## 4. Deliberate — leave alone

**This section protects the next person from "fixing" these.** Each looks duplicated and is not,
with the reason quoted from the source.

1. **`SurfaceGrabBar` vs `GrabbableModal` as two drag owners** — `WorldUI/Surfaces/SurfaceGrabBar.cs:18`:
   *"So there is no second drag implementation in this mod: there is one core with a fourth owner."*
   and: *"It was the first choice and it is structurally unavailable to a surface, for one reason that
   is not a matter of taste: `GrabbableModal.EnsureFrame` registers every holder it builds in
   `GrabbableModal.LiveHolders`, and `ModalFallback.SweepOrphanChrome` … destroys every holder in that
   list."* — **The mechanism split is justified; R1's missing dial is not covered by this.**

2. **`CombatLogSurface.TickCapLaser` copying `MapButtonRail.TickLaser`** — `CombatLogSurface.cs:1042`:
   *"**THE SHAPE IS `MapButtonRail.TickLaser`'s, for its reason.** A geometric `Collider.Raycast` over
   this surface's own cap needs no physics layer and no mask, so it cannot disturb anybody else's pick
   mask, and it is owned by the object that owns the cap's lifetime … The tray registration is GONE
   rather than kept alongside: two scans over one collider would fight for the hover."* — the
   strongest reasoning in the file. Note only that it hand-copies two constants (R28).

3. **No modal commit gate on the combat-log pin** — `CombatLogSurface.cs:1055`: *"**NO COMMIT GATE,
   DELIBERATELY.** The board's own laser path suppresses presses while a blocking modal is open,
   because tray keycaps call game APIs directly and END TURN is not undoable. This cap touches no game
   state at all: it flips a BepInEx config key."*

4. **`Net/Desync/DispatchGuard` forking `TickGuard`** — `DispatchGuard.cs:29`: *"WHY NOT `TickGuard`.
   That one feeds every step to `PerfMonitor` for the per-frame STEPS ranking, whose whole meaning is
   'the mod's per-frame cost'. Patch bodies fire on the game's cadence, not ours … Identical isolation,
   no perf coupling."*

5. **`scripts/check-mirrors.sh` as a lint instead of a shared constant** — its own header: *"The
   failure mode is not 'there are two constants'. It is 'someone tunes one copy'. A shared constant
   fixes that; so does this, at nil risk and with the layering intact. Each site keeps its own doc
   comment explaining what the number means THERE, which a shared constant would have flattened."*

6. **Map caps dropping `NativeButtonSkin.CreateFace`** — `MapButtonRail.cs:793`, on his own report
   *"die Symbole haben nun einen viereckigen Rahmen statt direkt auf dem button zu sitzen"*. The
   divergence from `BoardButton` was asked for. Also `:40`: *"Sampling beats re-implementing here …
   a copy of an animation drifts from it, a sample cannot."*

7. **`WorldUI/SelfUpdateDialog` as a second hand-built dialog beside `Net/VersionDialog`** —
   `SelfUpdateDialog.cs:27`: *"**WHY THIS IS A SECOND CLASS AND NOT A REUSE OF `Net/VersionDialog`.**
   That is the right machinery and this is built out of the same parts … but its shape is fixed:
   exactly two buttons at hard-coded anchors … A progress bar, a button row that changes between two
   buttons and one, and a label that updates every frame do not fit inside it, and widening its
   surface would change a dialog that fires during a MULTIPLAYER JOIN — the one moment nothing should
   be experimented on."*

8. **Two tooltip families** — `WorldUI/Tooltips/TooltipOnWindow.cs:16`: *"**THERE IS NOT ONE TOOLTIP
   MECHANISM IN THIS GAME, THERE ARE TWO, AND ONLY ONE OF THEM WAS EVER COVERED.**"* The duplication
   is the *game's*, and the mod covers both on purpose.

9. **`VROptionsTab.Cheats.Text(en,de)` duplicating `Curated.Say(en,de)`** — `Curated.cs:110`: *"the
   duplication is deliberate: that file is built to be DELETED in one step, and a dependency from the
   permanent curated tree onto a temporary file would break the build the day the cheats page is
   removed."*

10. **Three UTF-8 truncation codecs** — `Net/PresenceState.cs:2910`: *"The pick-banner / board-tooltip
    codecs above predate this type and keep their shipped field pairs untouched (wire-test vectors pin
    their behaviour)."* Note only that a future improvement to the shared codec would not reach the two
    most player-visible strings.

11. **The peer's placard is worded in the *viewer's* language** — `RemoteEmptyFanHint.cs:33`:
    *"DERIVED — the TEXT: composed from THIS client's localization, not the sender's … Consequence,
    deliberate: a German player's placard reads 'No hand cards' to an English peer."* Related standing
    ruling in `.planning/STATE.md`: the language on a peer's board is **mixed on purpose** — sender's
    where the text itself travels, viewer's where only a key does. **This is not a gap; do not "fix" it.**

12. **`RemotePileFronts.FanArchFactor` as a literal rather than the hand-fan dial** —
    `RemotePileFronts.cs:78`: *"DELIBERATELY A LITERAL, NOT `Defaults.FanFlatCurvatureFactor`. That
    entry is the HAND fan's dial … pointing this constant at that entry would make a hand-fan retune
    silently reshape two arcs the owner's own retune leaves alone."*

13. **Two elections for figures vs props** — `GrabbableProp.cs:294`: *"A figure's election is run
    centrally … because a figure carries a mod-owned reach extension, a busy predicate, a stretch
    gesture and a refusal probe that all have to agree within one frame. A prop has none of that."*

14. **`HeldFigures` vs `NetHeldFigures`** — `HeldFigures.cs:31`: *"DO NOT MERGE WITH NetHeldFigures…
    Sharing one store would couple local grab lifetime to the wire."*

15. **`PropHeldPose` as its own nine-key dial set** — `PropHeldPose.cs:16`: *"ModBuild 349 gave props
    the figure hold by READING THE FIGURE'S KEYS … which was right for 'behave like a figure' and
    wrong for 'and be adjustable on its own'."*

16. **`WallSegmentFade`'s pre-bind fallbacks differing from its shipped defaults** —
    `WallSegmentFade.cs:508`: *"0.10 here is the PRE-BIND fallback and NOT the shipped default … the
    two have been allowed to drift apart and this accessor is only reached before Bind()."* One
    caveat: the block header at `:464` still reads *"fall back to the shipped defaults"*, which is
    untrue of all four — a comment fix, not a code fix. ⟳

17. **`EyeReachCensus.MinVisibleAlpha = 0.02f`** — *"Deliberately LOWER than `CanvasConversion.FitMinAlpha`
    (0.05): the rim is translucent and the point of this pass is not to miss it."* Likewise
    `PanelSupersample.1.Core.cs:635`'s deliberately looser plate test.

18. **Four `CanvasRenderer` alpha veils** (`9d` FlashVeil, `9e` HiddenWindowVeil, `9g` SubViewSeatVeil,
    `11` PreConvertHide) — each answers a different cause and each carries its own hardware evidence.
    The four-way split is justified; only the missing 9d→materialise-runner plumbing is a hazard
    (§6).

19. **Two curated doors for `[Compat] WallFade`, `[WallFade] StackedShellFade`, `WalkInStandDown`** —
    `scripts/check-options-coverage.py:335`, user ruling: findable under Komfort **and** with the
    world block.

20. **No `[DefaultExecutionOrder]` between beam producers and consumers** —
    `.planning/refactor/FRAME-ORDER.lock`: *"Freezing an order the code does not depend on is a
    behaviour change wearing a tidy-up costume."*

---

## 5. Method, coverage, and the blind spot

### What was searched

Seven concept lanes ran in parallel over the whole of `src/GloomhavenVR/`, plus a cross-cutting pass
by the integrator. Every finding above was **re-verified against source by the integrator** before it
entered this document; the counts and file:line references in §1 and §2 are read, not relayed.

* **Pose / anchoring** — follow-pin, seat placement, billboarding, clamp-into-view, recentring, world
  scale. `HeadFacing.cs` and `PanelPlacement.cs` traced to all their callers.
* **Window chrome / lifecycle** — bars, closes, plates, ink-vs-frame, show/hide gates, sorting ladders,
  registration, park/unpark.
* **Buttons and press surfaces + laser/poke** — a full enumeration of **25 independent per-object
  laser scans** and their veto sets; five distinct poke commit rules.
* **Grab / carry + fade / dissolve / reveal** — figures, props, panels, cards, world grab; four alpha
  veils and the materialise runner.
* **Local ↔ remote mirrors** — all of `Net/Remote/**` against its local counterparts, plus the four
  checkers and a mechanical same-name/different-value constant diff.
* **Settings and localisation** — all **411** `Bind()` calls, all 540 `Defaults` constants, all 43
  `Clamped()` fallbacks, all 166 `AcceptableValueRange` declarations against their prose, 105 curated
  rows against the 405-entry `ConfigNames` table, and the Loc `Pair` tables diffed both ways
  (same-EN/different-DE and the reverse).
* **Instruments** — `Core/Perf/**`, `Core/Diagnostics/**`, `Net/Desync/**`, `VRLog`, the censuses.

Cross-cutting indexes built by the integrator: 743 class names; 159 self-declared "mirror of"
mentions across 72 files; 73 self-declared verbatim-copy admissions; every `Loc` key referenced by
two or more non-`Loc` files; every German-character string literal outside `Core/Loc/**`; the 20
`check-mirrors.sh` groups.

### Positive results worth recording

Several families came back **consolidated**, and saying so is part of an honest survey:

* **World-scale conversion.** One exchange rate. Every converter reads `rigRoot.lossyScale.x`; no
  second `100×` armature converter outside the one documented opt-out.
* **Grab and carry.** One `PanelGrabHandle`/`IPanelGrabOwner` seam with four implementers; one
  `HeldPoseMirror` now serving figures **and** props (the 2026-09-05 round was real — this is the
  direct answer to his earlier props complaint, and only the cards and the highlight edges are left).
* **Haptics.** One helper. `VRHaptics.Play` has exactly one call site.
* **Object finding.** One `Core/SceneRegistry.cs`; `MaterialLoaderHeal`'s older private registry has
  been folded into it. No duplicate registries found.
* **Shaders.** Zero `Shader.Find` violations of the bundle rule.
* **Backing plates, button skin, options row building, the close path, the draw-order ladder,
  "is this a mouseover subtree"** — all single-owner.
* **The wire.** One pose sampler, one `InterpolationSharpness`, no duplicate quantisation or
  dead-reckoning, and the "no per-sub-feature sync setting" ruling holds: only two `[Net]` dials
  reach a Remote renderer, both viewer-side visibility.
* **The close X** (calibration case) is genuinely resolved in this tree.

**So the honest headline is: the mod is more consolidated than a reader of the FOLGEN/FIXIERT report
would expect — but the residue is not small, and it clusters exactly where no checker looks.**

### The blind spot, stated plainly

**Every lane indexed on vocabulary — log strings, comments, and constant names.** That is what makes
this codebase tractable, because it documents its own mirrors 159 times. It is also the method's
weakness: **a duplicated computation that shares no vocabulary and no constant name does not surface
this way.** R7 was found only because its author wrote a parity claim in a comment that turned out to
be false. There are probably more of those, and this survey cannot bound how many.

Two further honest limits:

* **Large files were read where the call graph led, not end to end.** `ArcSeats.cs` (5 656 lines),
  `ModalFallback.9.Spawn.cs` (4 032), `RemoteBoardFurniture.cs` (4 791), `RemoteAvatar.cs`,
  `RemoteControlBoard.cs`, `RemoteMapRoom.cs`, `CanvasConversion.3.Fit.cs` (6 327) and the 28-file
  `Core/WallFade/**` family were outlined and grepped rather than read whole.
  `Net/NetProtocol.cs` (21 847 lines) was excluded by design — it is a prose ledger, not a source of
  implementations.
* **Nothing here was verified at runtime.** Every divergence is read from source. The ones marked
  rank 1–2 have a mechanism a reader can follow to a symptom; none has been reproduced on hardware.

### What a follow-up pass should do

1. **`RemoteBoardFurniture.cs`** — named by the mirror lane as *"the densest hand-copy surface in the
   family"* and only grepped. Read it whole.
2. **`ArcSeats.cs` + `ModalFallback.9.Spawn.cs` vs `PanelPlacement`** — spawn-time seat and
   anti-overlap placement is a plausible second duplication axis nobody chased. Note the related
   observation that **nothing in `WorldUI/Surfaces/**` goes through a seat registry at all** —
   `FloatingDecisionSurfaces.cs:219`, `DialogSurface.cs:98` and `UseBarsSurface.cs:1782` each write
   `head.position + head.forward * d`, so a confirmation box and a floated modal can land in the same
   spot, which `ArcSeats` would never allow between two modals.
3. **A computation-shaped scan.** Something that hashes normalised *expression trees* rather than
   text would catch the class this survey's vocabulary index misses. That is a tool-building task,
   and it is the only way to raise confidence in the completeness of §1.
4. **Re-check every ⟳ row** against the three moving lanes before acting on it.

---

## 6. BROKEN, not merely duplicated

Found during the survey, **not touched**, listed here so they can be triaged separately. These are
defects, not redundancies.

1. **`Board/FigureGrab/ActorPropBody.cs:933` — the cadence gate is defeated in the steady state and
   the log budget never expires.** `_nextCensus` is advanced only on the branch that logs:

   ```
   904  if (_censusLogsLeft <= 0) return;
   908  if (now < _nextCensus) return;
   ...  // walks ScenarioManager.CurrentScenarioState.Props
   933  if (signature == _lastCensusSignature) return;   // returns WITHOUT advancing _nextCensus
   935  _nextCensus = now + CensusIntervalSeconds;
   936  _censusLogsLeft--;
   ```

   In the normal case — signature stable, which is the steady state of every scenario — the deadline
   is never pushed, so from 2 s into the board the props walk runs **every frame for the rest of the
   session**, and `_censusLogsLeft` is never decremented so the 12-log budget never expires. The
   caller is per-frame (`FigureGrabDriver.cs:535`, inside `TickGuard.Run("FigureGrab.Registry", …)`).
   The walk is small, so this is a slow leak rather than a hitch — but it defeats both stated bounds.
   **The sibling census 200 lines away in the same driver step (`FigureGrabDriver.cs:744`) gets it
   right**, advancing `_nextPropCensus` *before* its change gate. **Verified by reading both.** A
   sweep of every `if (now < _nextX)` / `_nextX =` pair in `src/` found this to be the only site with
   the ordering inverted.

2. **`Core/Diagnostics/ViewConeProbe.cs:358` — the watchdog escalation is inert.** `VRLog.Warn` and
   `VRLog.Info` gate **identically** on `Level >= VRLogLevel.Debug` (`Core/VRLog.cs:152` and `:162`,
   verified). The `if (watchdog) Warn else Info` changes the BepInEx severity glyph and nothing about
   whether the line appears. At the shipped `LogLevel = Info` the watchdog fires, spends one of its
   four `MaxWatchdogReports`, and prints nothing a default-level log carries — for the fault whose
   own class doc says it *"was true for 19,853 consecutive frames of his log with nothing anywhere
   saying so."* Should be `VRLog.Alert`.

3. **`WorldUI/Patches/EscMenuShowSafety.cs:140` and `:155` — a swallowed exception is silent at the
   shipped default.** Same mechanism as (2), under a doc at `:126` that requires the opposite: *"a
   swallowed throw must never be silent, because the swallow is a symptom report, not a fix."* The
   failure mode is the pause menu becoming permanently unopenable, which is a standing user ruling
   and therefore player-facing — the documented definition of `Alert`.

4. **`Board/FocusDriver.cs:242` — no repeat line on a persistently throwing carrier.**
   `_reported.Add(name)` suppresses for ever after the first line. Every sibling guard emits
   `is still throwing (N time(s) so far)`, and the project's documented triage procedure counts throw
   storms off that line, so a focus-cue carrier throwing every frame is invisible to it. (Also listed
   as R30(a), because the guard is duplicated as well as wrong.)

Two smaller ones, recorded without a claim that they fire today:

5. **`CanvasConversion.9d.FlashVeil.cs` is outside the veil↔materialise reconciliation chain.**
   `9e` exports `PreVeilAlpha` and `9g` exports `PreSeatVeilAlpha`, both consulted by
   `WindowMaterialiseRunner.cs:359-360`; `9d` exports nothing and nobody asks it. If a materialise
   appear starts while `9d` holds a subtree at 0, the runner captures `orig = 0` and restores 0 — a
   window alive, interactive and invisible. `PreVeilAlpha`'s own doc still says *"For the **one other**
   mod writer of `CanvasRenderer.SetAlpha`"*; there are three. Reachability is narrow (9d holds only
   the frames before `Start()`), so this is a hazard, not a demonstrated defect.

6. **`WorldUI/Options/VariantTiles.cs:356` sets `colors.disabledColor = TileBorderOff`** — byte-identical
   to the un-selected *normal* colour, i.e. a disabled tile would be indistinguishable from an
   available un-picked one. Nothing sets `interactable = false` today, so it cannot be seen.
