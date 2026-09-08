# State — where the project stands

**Updated 2026-09-09 against `dev` = ModBuild 486.** The file this replaces had gone 168 builds
stale while still saying "read this first"; it is kept as `STATE-ARCHIVE-through-2026-08.md` for
its round-by-round narrative and for nothing else.

**Read in this order.** `CLAUDE.md` (rules, gates, working practice — the part that does not
change per build) → this file (where things stand and what is owed) → the build-note block above
`ModBuild` in `src/GloomhavenVR/Net/NetProtocol.cs`, newest first (what happened, per build) →
`.planning/INDEX.md` (which of the 50-odd planning docs are still live).

---

## 1. Position

- **`dev` = ModBuild 486.** `main` is the release branch and is behind on purpose.
- **483 IS A FULL INSTALL.** The asset bundle changed for the first time since ModBuild 368:
  74,943,671 → 74,943,763 bytes. Builds 369–482 were all DLL-only drops. A DLL-only install of
  483 shows neither of its two content changes, and the `ENV SKY BRANCH` log line says so out
  loud if it happens.
- **Hardware evidence now covers 482**, both clients at commit `6efa4acfc`. **483 through 486
  remain untested on hardware.** See [MP-ROUND-484.md](MP-ROUND-484.md) for the seven findings,
  fixes, transport contract and the unresolved underlying disconnect cause.
- **484 is DLL-only relative to 483.** Upgrading from the tested 482 requires the full 483 bundle.
- Gate readings at 486: 17 checkers green · wire **238,556** assertions · patch surface
  **107 classes / 165 methods** · config keys **625** · log tokens **4,709** ·
  instrument-writes baseline **61** · 0 errors, 0 warnings.

### Recent builds

| build | what it was | install |
|---|---|---|
| 480 | the review round he asked for BEFORE spending a hardware test. Five read-only review lanes, 19 defects, three new gates | DLL only |
| 481 | the 2026-09 refactor programme: five lanes over 626 files / 550k lines. Also found four gates that could not fail | DLL only |
| 482 | the four rulings he gave on 481's deferred list, one lane each | DLL only |
| 483 | his two hardware notes, both baked into the assets on his ruling "lieber sauber" | **full** |
| 484 | multiplayer pulse, flights, grabbing, rest controls/burns, original bonus widgets and bounded extras transport | DLL only after 483 |
| 485 | remote character-change animations for map-room hands and open discard/burnt browsers | DLL only after 483 |
| 486 | native animation transport and systematic board/card/window parity repairs | DLL only after 483 |

---

## 2. Owed to him, and what he has to judge

### 2a. He must look at this and say whether it is right

**The cellar's surround is now BLACK when zoomed out.** He reported two star skies in the cellar
and ruled the dome away. The dome was never visible from inside the room (the stone shell has a
closed ceiling, a capped stair shaft and capped rat holes); it was visible from OUTSIDE the shell,
in the zoomed-out pose where the room reads as a model in front of you. That surround is now the
`[Rig] VoidColor` clear. **This is a consequence of his instruction, not a defect** — but he has
not seen it yet, and it is one line to put back.

### 2b. Implemented in 484–486; hardware verification remains

- Explicit short-rest state now uses record 46 independently of sacrifice-seat record 39.
- Remote active-bonus rows now use original serialized game slot and picker prefabs, including
  owner subwidget state in record 47. The giant custom caption and plate widgets are removed.
  Build 486 adds native intermediate values in record 49, original auxiliary slot state in 50,
  actual card particle frames in 51 and original element-board frames in 52. The broader review
  also repairs card/fan motion, native pointer transitions, owner initiative depth and shared
  windows. See [MP-PARITY-486.md](MP-PARITY-486.md).
- Map-room fan exchanges now use the owner's map character key. Equal-sized hands refresh
  immediately; discard/burnt browsers re-emerge on character retargets. See
  [MP-FAN-485.md](MP-FAN-485.md).
- Local and remote card pulse/flight/rest repairs are integrated. The supplied disconnect is a
  confirmed transport receive timeout; its underlying cause remains unresolved.

### 2c. Deferred with their cost attached — these need HIS decision, not more work

From the 2026-09 refactor's reviews (`.planning/refactor-2026-09/REVIEW-*.md`):

- **Four records ride the send cadence, not the edge** — resolved in 482 for records 36/39/41/43.
  The remaining question is whether any OTHER record has the same shape.
- **The furniture's materials are never destroyed** (`REVIEW-net.md` N8). Needs an owned-materials
  design, not a minimal fix.
- **`RemoteContentSeconds`** resolved in 486: retained and marked INERT in both languages.
  Received/content edges drive the mirror immediately; a fixed recovery poll is not a content delay.
- **No negative cache in the figure resolver** (N9). Bounded; a retry window would be an invented
  tuning value.
- **The wall fade's `RescanCore` two remaining items**: a write-only field and a dead overload
  that carries the live one's evidence.
- Two holes found while removing the cellar dome, filed with arithmetic in
  `NEEDED-OUTSIDE-cellar-one-sky.md`: the stair alcove is placed from the UNSNAPPED hole while the
  wall is cut to the SNAPPED one, and `BuildShaft` has no floor.
- **A half-applied caption pairing, open since ModBuild 363.** `Cards/Piles/PileViewer.cs` applies
  `NativeButtonSkin.ApplyFont` to the three pile captions but never `StyleWorldReadableLabel`,
  while `Cards/Tray/PlayTray.4.Slots.cs` — the caption whose own doc says it is built to match
  those three exactly, *"the same muted parchment colour, the same native HUD font, and the SAME
  fit box and font ceiling"* — does call it. One line, and it reads as intentional, which is why
  it has survived: it changes how three captions LOOK, so it wants his eye, not a silent fix.
  Filed in `LANE-BOARDTEXT-357-NEEDED-OUTSIDE.md` §2.

### 2d. Owed on hardware — lines that have never printed

Grep tokens waiting for their first real reading. Several have been owed since 480.

`Remote BURN look` · `DOCK MIRROR` · `GATE 3` · `NOT ASKED` · `REMOTE GLOW BLEND` ·
`SHORT REST PILE COVER` · `HELD BAR HIDE REFUSED` · `BURN ANIM STUCK` · `BURN ANIM FLAG LATCHED` ·
`MAP STORY SEND RATE` · `MAP PLACARD SCALE` · `WALL COMMIT THREW` (absence is the good reading) ·
`PACKET REJECTED` (zero is the good reading) · `ENV SKY BRANCH` (cellar must read ABSENT) ·
`HELD-CARD EDGE PRE-EMPT` · `GLOVE NORMAL TAMED` (**gone** — the glove value is baked now, so
there is deliberately no line; the proof is the picture and the bundle size).

**Giant orange text identified:** MB482's census names the 19.87 m
`Furniture/UseBarsDrawer/UseBar0/Caption`. That replica was removed in 484. Confirm the original
widgets, their pickers, and their size in the next headset test.

---

## 3. Standing rulings that are easy to break by accident

The full set is in `CLAUDE.md`. These four have each been broken at least once *after* being
written down:

1. **The burnt fan is ALWAYS open** ("Beim Verbrennen EGAL AUS WELCHEM GRUND muss die Karte immer
   mit der Vorderseite sichtbar sein"). The **discard** fan is open too, **except during a short
   rest**, where the whole fan is covered. Long rest is the ACTION phase and is fully open.
   Write the rule as a CARD PROPERTY, never as a place — a rule written as a location cannot
   follow a card out of that location, and that exact shape has been the defect twice.
2. **Seeing a card's FACE and being allowed to NAME it in a prompt are two questions**, over one
   population. Merging them re-opens the ModBuild 477 identity leak.
   `scripts/check-card-identity-mask.py` fails the build if they become one predicate.
3. **The language on a peer's board is deliberately MIXED**: the sender's where the text itself
   travels, the viewer's where only a key does. It is filed in `Core/Loc/Loc.cs`. **This is not a
   gap; do not "fix" it.**
4. **The options button opens and closes the pause menu and touches nothing else.**

---

## 4. Subsystems with a closing account — read it before you touch them

| subsystem | read first | why |
|---|---|---|
| wall fade | `.planning/perf/WALL-FADE-CLOSEOUT.md` | closed on hardware; every dial is settled and the instruments that lied are listed |
| multiplayer 1:1 | `.planning/multiplayer/DESIGN-1TO1-RESIDUE.md` §6 | the topic is closed; two of its last three "debts" were false |
| the 2026-09 refactor | `.planning/refactor-2026-09/BRIEF.md` + the five `REVIEW-*.md` | what was found, what was deferred, and what the tooling could not see |
| static batching | `.planning/static-batching-removed.md` | tried and completely removed by user ruling |
| per-eye fade rivalry | `.planning/wall-fade-stereo-rivalry.md` | parked; unfixable on the game's masonry shader without losing the dissolve |

---

## 5. The shape of a round

1. Read his German report. Take the **symptom** as data; re-derive the cause.
2. Land any shared contract yourself, then dispatch lanes on **disjoint file sets**
   (`isolation: worktree`, at most five, each with at most one sub-worker).
3. Review every diff. Restrict each merge patch to the lane's OWNED paths.
4. Apply the cross-lane `NEEDED-OUTSIDE-*.md` items yourself.
5. Bump `ModBuild` **once**, with a build note that a stranger could act on.
6. Run the three gate commands from `CLAUDE.md`. Regenerate `docs/PATCH-INVENTORY.md` once, at
   the end, if any patch class moved.
7. Push to `origin/dev`.
8. Write him a German report: what was found, what was fixed, what he must judge, what is owed.
