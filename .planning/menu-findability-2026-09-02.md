# Options menu — findability round, 2026-09-02

Base: `origin/dev` @ `7f48eda1`, ModBuild 340. ModBuild is **not** bumped by this lane.

The user's report after testing ModBuild 340, verbatim, items (a)–(c) plus the closing paragraph:

> a) Das Cheats-Menu sollten über die cfg aktiviert werden können (per default AUS)
> b) Wenn ich in einem Sub-Menu in 'Erweitert' bin und dann links wieder auf den 'Erweitert' Knopf
>    drücke erwarte ich, dass ich wieder zu der Übersicht des Erweitert-Menus komme. Aktuell komm
>    ich nur zurück wenn ich ganz oben auf '< Erweitert' drücke - ich will es links aber aber auch
>    zusätzlich.
> c) Wenn du Zeilenumrüche machst achte darauf, dass nicht ein einziges Symbol eine Zeile bekommt,
>    dass sieht doof aus aktuell gibt es ein Tab der heißt ('Umgebung // & // Ton) // repräsentieren
>    hier Zeilenumbrüche. Achte auf die Userfreundlichkeit der Menus. Weiterhin finde ich den offset
>    für die healthbar nicht - daraus resultiert ein weiterer Task: Die Optionen sollten immer
>    sonnvoll in Kategorien geclustert sein, so dass man sie schnell finden kann! Überarbeite das
>    nochmal.

---

## 1. The measurement, before anything was changed

Everything below is from `scripts/check-options-coverage.py --report` (added this round) and
`scripts/refactor-guard.sh check`, run at `7f48eda1`.

| Quantity | Before | After |
|---|---|---|
| Config keys the refactor guard counts (`.Bind("Sec","Key"`, both literal) | 396 | 397 |
| Config keys the mod ACTUALLY binds (literal + section-supplying helper + expanded per-variant) | 604 | 605 |
| Curated rows in the everyday view | 105 | 106 |
| Distinct curated `(Section, Key)` | 102 | 103 |
| Curated tabs (plus "Erweitert") | 6 (+1) | 6 (+1) |
| Curated sections | 25 | 25 |
| Keys reachable ONLY through the Erweitert catalog index | 502 | 502 |
| Deliberate second doors (one key, two tabs) | 3, undocumented as a set | 3, listed with their ruling |
| Curated families with an UNCURATED sibling | 96 | 95 |
| Deepest click path to a setting | 2 clicks + scroll past 8 headings into an unnamed "Allgemein" collector | unchanged in shape, one key fewer in the collector |

**Why 396 is not the real surface.** `scripts/check-surface.py` — which is what the guard's
`configKeys` number comes from — matches only `.Bind("Section", "Key"`, both arguments literal.
That is correct for its job (diffing two snapshots for a *removal*) and blind to two shapes:

* a per-module helper that supplies the section itself (`Rig/ComfortSettings.Bind<T>` →
  23 `[Comfort]` keys), and
* interpolated keys — `$"{style}Scale"`, `$"BoardPitchMin_{board}"` — ~186 keys across
  `[Cards]`, `[Hands]`, `[WristHud]`, `[FigureGrab]`.

The new checker resolves both (loop variable over an enum via `Enum.GetValues(typeof(X))`, or over a
literal `string[]`), which is why it sees 604 where the guard sees 396. An interpolation it cannot
resolve degrades to a **wildcard for its section**, never to "this key does not exist" — a checker
that guesses "absent" fails the build on correct code and is switched off within a week.

**The defect, located.** `[WorldUI] BarHeightOffset` is bound in `WorldUI/ActorBars.cs:302` on the
`bars` module. It appeared in **neither** curated list **nor** the hand-arranged topic tree
(`VROptionsTab.7.TopicTrees.cs`, node `vr_pt_bars`). A key that tree does not name does not land one
heading over — it lands in the `Allgemein` collector at the *bottom* of Erweitert ▸ Menüs & Tafeln,
which is the exact grab-bag that tree was written to empty. Meanwhile its three siblings
(`BarSizeScale`, `BarFixedSize`, `BarsOccluded`) sat under a heading literally called
"Lebensbalken" — on the **Tafeln** tab, i.e. filed by which code owns the key rather than by what
the player is looking at.

---

## 2. What changed

### (a) The cheats page is behind a .cfg key, default OFF

`[Cheats] Enabled` on its own file, `dev.gloomhavenvr.cheats.cfg`. Default `false`
(`Defaults/Defaults.WorldUI.cs`, annotated `// => [Cheats] Enabled`).

Its own file rather than a line on a shared one, in order of weight: (1) it is where somebody
*looks* — scan the config folder for "cheats", find a file with one key; (2) the cheats feature is
built to be deleted, and a key on a shared file leaves a dead line in a player's `.cfg` forever
while a whole file is one deletion; (3) `[General]` lives on the main plugin config owned by
`Plugin.cs` (another lane), and `[WorldUI]` would file a cheat switch under the menu system that
happens to draw it.

With it off: no link on the Erweitert index (`BuildCheatsIndexLink` returns 0 rows, so the index is
one row shorter, never a row that opens onto nothing), and `BuildCheatsPage` falls back to the index
if `_view` is stale — the page cannot be reached by any route. The key is in
`ConfigCatalog.NotOffered`: a gate that can be opened from inside the room it locks is not a gate,
and the request was explicitly that this be a `.cfg` decision.

`ConfigCatalog.EnsureBound` force-binds it so the file EXISTS the first time the options window
opens. A gate whose file is never written has no handle.

The removal instructions at the top of `VROptionsTab.Cheats.cs` gained a step 5 naming the three
lines to delete.

### (b) The left column is "up one level"

`SelectTabRoot(index)` in `VROptionsTab.3.Content.cs`, stated as a rule that holds for every tab:

> **Pressing a tab in the left column shows that tab's ROOT page, whether or not it is the tab you
> are already in.**

From a topic page, the trigger page or the cheats page, pressing "Erweitert" lands on the Erweitert
index. Pressing a curated tab you are already on is a no-op *because a curated tab has no deeper
page* — and if one ever grows a sub-page it inherits the behaviour from this method rather than
from a new branch. The "‹ Erweitert" breadcrumb is untouched: he asked for the left column
*zusätzlich*.

**The fix is one deletion, and the obvious reasoning about it is wrong.** It looks as though
pressing the lit toggle in a group with `allowSwitchOff = false` cannot raise an event at all. An
`IPointerClickHandler` component was written on that premise, and then deleted after decompiling
the game's own `UnityEngine.UI.dll` (`ilspycmd -t UnityEngine.UI.Toggle ressources/Managed/UnityEngine.UI.dll`):

```csharp
private void Set(bool value, bool sendCallback = true)
{
    if (m_IsOn != value)
    {
        m_IsOn = value;
        if (m_Group != null && m_Group.isActiveAndEnabled && IsActive()
            && (m_IsOn || (!m_Group.AnyTogglesOn() && !m_Group.allowSwitchOff)))
        {
            m_IsOn = true;
            m_Group.NotifyToggleOn(this, sendCallback);
        }
        PlayEffect(...);
        if (sendCallback) { ...; onValueChanged.Invoke(m_IsOn); }   // ← runs
    }
}
```

`OnPointerClick` flips the field, so `Set(false)` is entered with `m_IsOn == true`; the group clause
forces it back to `true`; `onValueChanged.Invoke(true)` runs. **The press was arriving all along**
and the listener's own `index == SelectedTabIndex` early-return was swallowing it. Removing that
guard is the entire mechanism. Shipping the extra handler would have meant a second handler firing
on every real tab switch, justified by a claim the shipped assembly disproves.

### (c) A break may never strand a lone symbol

`VROptionsTab.2.Rows.NoOrphanCaption`, applied inside `FitTabCaption` — the one choke point through
which both the sub-tab column and the Erweitert topic-link rows pass their captions.

> Per authored line, a word is WEAK when it is one character long, or at most two characters with no
> letter and no digit in it. Every weak word is fused to the word BEFORE it — to the word AFTER it,
> if it opens the line — with U+00A0, which TMP will not break at.

Backwards by default because that is the convention the hand-authored captions already use and the
one German and English typography share: "Brett &" then "Karten", never "Brett" then "& Karten".

**Why an authored break was not already protection**, which is the part worth keeping: the Loc string
*already* said `"Umgebung &\nTon"` — two lines, ampersand deliberately kept with the word before it.
The 210 px column could not hold the line "Umgebung &" at the fitted size, so TMP broke the authored
line *a second time*, at its own space, and produced a third line holding one character. TMP will
always break a line further if the line does not fit; the only thing it will not break is a
non-breaking space. So the rule has to live in the layout — the next long label would repeat it, and
no reviewer can see a wrap fault by reading a Loc string.

The tab was **also** renamed, because the rule stops the ampersand being alone and only a shorter
name stops the column having to shrink the caption at all: `cat_environment` is **"Umgebung"** /
**"World"**, one word. Sound keeps its own heading *inside* the tab, one level cheaper than a tab
and exactly where the 2026-08-22 audit put it. Its first section was renamed **"Schauplatz"** /
**"Scenery"** so a section no longer repeats its own tab's name.

**Verification.** The three methods were lifted *verbatim* out of the shipped source (extracted by
text range, not retyped — a test that runs a copy proves nothing about the method), compiled into a
throwaway net8.0 console app and run over every tab and topic caption the menu draws, plus the
shipped `"Umgebung &\nTon"`:

```
-- THE REPORTED CAPTION --
  ok   cat_environment (as shipped)   in='Umgebung &<NL>Ton'  out='Umgebung<NBSP>&<NL>Ton'
  ok   cat_environment (as shipped) — no breakable piece is a lone symbol
  ok   cat_environment (no authored break) — out='Umgebung<NBSP>& Ton'
-- 30 further captions -- all ok
-- PROPERTIES --
  ok   idempotent            ok   weak word opening a line fuses forward ('‹ Erweitert')
  ok   'Karte 3D' NOT weak   ok   authored break preserved     ok   null-safe
ALL PASS
```

The "no breakable piece is a lone symbol" assertion splits the *output* the way TMP is allowed to
split it — at ordinary spaces and authored newlines, never at U+00A0 — and fails if any resulting
piece is a weak word. That is the property, not a spot check of one string.

What this canNOT say: how the column *renders*. A caption that passes the string test can still be
shrunk toward the 9 pt floor by the fitter. See §5.

### (d) Clustering — the health-bar family, and the rule that generalises

`Lebensbalken` moved **Tafeln → Brett & Karten**, and sits directly under `Figuren`.

The argument, stated so it can be checked rather than taken: the bars float above the **figures** on
the **board**. Their heading was on "Tafeln", the tab for the panels and windows the mod draws —
which is filing by implementation (`ActorBars` is `WorldUI` code, its keys are in the `[WorldUI]`
section) instead of by the object the player is looking at. On Brett & Karten the heading sits one
line below the switch for picking those same figures up, so somebody who thinks *"die Balken über
den Figuren sitzen zu hoch"* reads the tab that names the board, then the heading that names the
bars. Nothing was lost: all four keys remain on Erweitert ▸ Menüs & Tafeln ▸ Lebensbalken.

`[WorldUI] BarHeightOffset` was added in **both** places — the curated row and the topic tree. Its
caption is written on the entry (`"Lebensbalken: Höhe"` / `"Health bars: height"`) rather than left
empty, because the empty fall-through is the catalog display name and this key has no
`Loc.ConfigNames` entry either: the fall-through would have been "Bar Height Offset", the camel
humps spaced out, in German too. Its hint deliberately falls through to the bound description — five
sentences of German that already explain world units, the sign convention and that the value rides
on top of the per-figure head measurement. **No value changed anywhere in this round.**

**The rule, now machine-checked.** `scripts/check-options-coverage.py`, four checks:

1. **Advertised but absent** — every `(Section, Key)` named in the curated list or either topic tree
   must be a key the mod binds. A typo is silent at runtime: `Lookup` returns null and
   `BuildCurated` skips the row, so the menu advertises a setting it never draws.
2. **Two doors** — a key curated twice is a decision or a copy-paste. The three deliberate ones (the
   wall see-through trio, by explicit user ruling) are listed WITH the ruling; anything else fails.
3. **A caption that names nothing** — a non-empty `CaptionKey` Loc.cs does not hold makes `Loc.Mod`
   return the id, so the row is captioned with a programmer's string. Empty is the documented
   fall-through and is allowed.
4. **A split family** — if a config section has curated members sharing a leading word, every
   sibling with the same section and leading word must be curated too. **This is the ModBuild 340
   defect stated as a rule.**

Check 4 fires 96 times when applied cold, because the everyday view has always been a hand-picked
list over a ~600-key tuning surface and most of those siblings genuinely belong one level deeper.
Making it a hard gate would mean writing 96 justifications in one sitting — 96 pieces of prose
written at the moment of *least* knowledge about each key, which this project has learned to
distrust. So `KNOWN_ORPHANS` is **frozen at the state the rule was written against** and the gate is
on the **delta**, exactly the way `check-instrument-writes.py` freezes its baseline: a key added to
a curated family tomorrow without a row fails the build. The list may only shrink; regenerate it
after a deliberate restructure with `--bless` and say so in the commit. `BarHeightOffset` has been
deleted from it, with a tombstone comment — 96 → 95.

**What is deliberately NOT checked**, and this matters: "every key is reachable". It is TRUE BY
CONSTRUCTION — Erweitert is the catalog's own index, and the catalog is a reflection walk over the
live BepInEx registry (`ConfigCatalog.Rebuild`), so a bound entry appears there whether anyone
remembers it or not. The only keys that leave are the ones `ConfigCatalog` deliberately withholds
(`NotOffered`, `RetiredMarkers`), each with its reason beside it. Checking a tautology would have
produced a green light on exactly the build he reported.

---

## 3. Falsifying the checker, both directions

Each fault was injected into `VROptionsTab.4.Curated.cs`, the checker run, and the file restored.

| Injected | Fired | Message |
|---|---|---|
| `BarSizeScale` → `BarSizeScaleX` | yes, 1 | `CURATED KEY DOES NOT EXIST: [WorldUI] BarSizeScaleX` |
| caption `vr_o_barsize` → `vr_o_barsize_nope` | yes, 1 | `CAPTION KEY NOT IN Loc: 'vr_o_barsize_nope'` |
| `[Cards] CardWidth` curated a second time | yes, 1 | `CURATED TWICE WITHOUT A REASON` |
| `[Optimize] HeadDepthPrepass` curated (a family with two uncurated siblings) | yes, 2 | `SPLIT FAMILY: [Optimize] HeadCullingMaskDrop`, `HeadMaskFromScenarioCamera` |
| **nothing — the tree restored** | **no, exit 0** | `options coverage: … no NEW split family.` |

A first attempt at the fourth test curated `[Voice] Enabled` and did **not** fire, correctly: its
leading word "Enabled" has no siblings in `[Voice]`, so there is no family to split. Worth recording
— a test that passes for the wrong reason would have made the rule look stricter than it is.

---

## 4. What is still wrong, measured and not fixed

Not fixed this round, and each is a number rather than an impression:

* **502 keys are reachable only through Erweitert.** That is the correct design (a curated row is an
  extra door, never a wall) but the *raw* side still has a grab-bag: any key the hand-arranged topic
  trees do not name falls into an unlabelled `Allgemein` collector at the bottom of its page. That
  collector is where `BarHeightOffset` was found. Emptying it means hand-listing dozens of keys per
  topic and is a round of its own.
* **95 curated families still have an uncurated sibling.** The frozen backlog. Each one is a
  candidate for the same report he filed; the list is the work queue, in `KNOWN_ORPHANS`.
* **`scripts/check-surface.py` undercounts the config surface by ~208 keys** (396 vs 604). Harmless
  for its own job — it diffs two snapshots taken the same way, so a removal still shows — but any
  future consumer of that number should read this paragraph first.

---

## 5. What only hardware can answer

1. **The "Umgebung" tab renders on ONE line at a readable size.** The string rule is proven; the
   *fit* is not. `FitTabCaption` auto-sizes down to a 9 pt floor, and no offline test can see a
   210 px column at the headset's DPI. The log line
   `VR options tab: sub-tab column built, 7 tab(s) — …` (`// HW-VERIFY`, `VRLog.Note`) prints the
   final captions with `<NBSP>`/`<NL>` made visible, which narrows the question to "is it legible"
   rather than "did the rule run".
2. **Which VFX/labels the fitter shrinks.** Same line, same caveat.
3. **The up-one-level press.** `VR options tab: left column pressed on the tab already open (#N) —
   went up one level, AdvancedTopic -> AdvancedIndex` (`// HW-VERIFY`, `VRLog.Note`). If the press
   does nothing on hardware and that line is absent, the click is not reaching the toggle at all —
   a different fault from the one fixed here, and the line is what tells them apart.
4. **The cheats gate.** `Cheats page gate: [Cheats] Enabled = False (dev.gloomhavenvr.cheats.cfg) —
   no link on the Erweitert index; the page cannot be opened.` (`// HW-VERIFY`, `VRLog.Note`).
   Without it, "the page is gone" and "the page failed to build" read identically, and the point of
   the gate is that ABSENCE is the correct outcome.
