# The wall section that would not fade — `sollte_faden.jpg`

**Round:** 2026-08-25 · against ModBuild 290 (`.planning/debug/second_logs/LogOutput.log`, hardware)
**Files:** `src/GloomhavenVR/Core/WallSegmentFade.PropUnit.cs`,
`src/GloomhavenVR/Core/WallSegmentFade.Standing.cs`,
`src/GloomhavenVR/Core/WallStandingProp.cs`

## The report

> "In der Map fandet ein Wandteil nicht, siehe sollte_faden.jpg (das rechte Element). Eventuell
> weil da auch Kristall dabei ist? Das hier ist aber nicht der Kristall auf dem Boden der nicht
> faden soll (den gibts in dem Level auch), sondern das was wirklich als Wand vor den Figuren zu
> sehen ist mit dem Gestein daneben. Das sollte wie jedes andere Element auch faden."

The photograph: most of the ring has faded; a dark stone column with a large turquoise crystal
slab beside it stands between the player and the drake.

## What refused it, by name, and under which clause

The subject is `PCG_CV_Ice_Feature_Medium_02_PR` — 22 renderers, `unit y[-0.5..3.5]`, widest
4.0 wu, two instances (one under `Wall 1`, one under `Wall 3`). The ModBuild-290 log measures it
**1/22 written**:

```
TORN 'PCG_CV_Ice_Feature_Medium_02_PR' 1/22 written, unit y[-0.5..3.5] over floor 0.0, widest 4.0 wu
  — WRITTEN: 'CV_Ice_Crystal_Form_04' under 'Wall 1/Generated Content/PCG_CV_Ice_Feature_Medium_02_PR/CV_Ice_Crystal_Form_04 (2)'
    ← mounted dressing of 'Wall 1' fade 1.00
  — LEFT SOLID under the same root, named 6 of 21, dropped 15:
    CV_Pillar_Generic_01, CV_Ice_Form_01, CV_Ice_Form_03, CV_Ice_Pillar_01,
    CV_Ice_Crystal_Form_03, CV_Floor_Scatter_02
```

`CV_Pillar_Generic_01` / `CV_Ice_Pillar_01` are the dark column; `CV_Ice_Crystal_Form_03` is the
slab. The six that were printed are the photograph.

**THE DECIDING CLAUSE** is one line, and the log names it outright:

```
5 unit(s) refused this rescan —
  'PCG_CV_Ice_Feature_Medium_02_PR' (22 renderer(s), owner 'Wall 1' @fade 0.00) STAYS WHOLE AND SOLID
    — 'CV_Ice_Crystal_Form_04': FIGURE (never touched — round-7 ruling, Lights-rule severity);
      16 renderer(s) pulled back off that wall
  … the same feature under 'Wall 3', and 'PCG_CV_Ice_Bay_Small_01_PR' ×3 on 'CV_Ice_Crystal_Form_02'
```

`WallSegmentFade.PropUnit.cs`, `ResolvePropUnit` PASS 1:

```csharp
if (IsFigureOrActorRenderer(m))            // ← THE BLOCKER
{
    refusedBy = $"'{m.name}': FIGURE (never touched — round-7 ruling, …)";
    break;                                  // → RefusePropUnit: the WHOLE unit stays solid
}
```

All five refusals in the whole session are this arm and all five name a crystal that the wall
generator itself parented under `Wall N/Generated Content/`. The arm that fires is the
**`Animator`** arm of `IsFigureOrActorRenderer` — this tileset animates its crystals — not
`ActorBehaviour` and not `CInteractableActor`.

### Why the remedy that already exists could not reach it

Two remedies for exactly this shape were already in the tree and neither one is asked at this site:

* **ModBuild 266 (`IsWallGeneratedDressing`)** — "a piece the wall generator built is wall
  dressing whatever animates it" — was applied at four sites plus `PurgeFigureRenderers`. The
  whole-unit refusal is a fifth site nobody lifted.
* **ModBuild 275 (`[WALL-SECTION]`)** lives *inside* `IsStandingProp`. In `ResolvePropUnit` the
  raw `IsFigureOrActorRenderer` test is asked **before** `IsStandingFigureProp`, so the release
  could never be consulted. The same log's standing census does release these very assets
  (`'CV_Ice_Pillar_01' … [WALL-SECTION] RELEASED — the game uses this AS a wall section`) while
  the wall they belong to stays solid anyway. *A rule read too late never runs*, one gate earlier
  than the last four times.

### The second, smaller blocker — a fragment judged as a unit

Once the refusal is lifted, `IsStandingFigureProp` would still have held one member back, under
the ModBuild-275 term's third conjunct:

```
'CV_Ice_Crystal_Form_04' GEOMETRY half refused: FIGURE arm under a wall, but the unit does NOT
rise out of the ground band: top 0.9 wu over floor 0.0 ≤ 1.0 wu, height 1.0 wu over 1 renderer(s)
```

One renderer, 0.9 wu tall — a knee-high crystal at the FOOT of a feature the same log measures,
through the FLOOR arm, as `foot -0.5 wu … top 3.5 wu, height 4.0 wu over 22 renderer(s)`. **Two
units, one physical wall feature.** The FIGURE arm's root is `FigurePropRootOf` (the nearest
`Animator` ancestor), which for these crystals is the crystal's own node, so the conjunct read a
FRAGMENT's height. This is the ModBuild-266 finding one arm further on: *the rule was judging a
piece of a wall*. Left unfixed it would have shipped a torn prop — the column faded, a stub at its
base still standing — which is the "Zwischending" the user has already ruled out.

## The fix

1. **`WallSegmentFade.PropUnit.cs`** — the whole-unit FIGURE refusal is lifted for a member the
   wall generator built, via a new `IsWallBuiltUnitMember`:
   * a wall inside the member's own **bounded four-level window** (`WallInUnitWindow`), **and**
   * no `ActorBehaviour` / `CInteractableActor` anywhere above it — the round-7 ruling as an
     **absolute veto**, character for character as `IsWallGeneratedDressing` writes it.
2. **`WallStandingProp.IsWallBuiltSection` + `WallSegmentFade.Standing.WallSectionGeometry`** —
   the ground-band conjunct is asked of `max(fragment top, enclosing wall-built unit top)`. The
   enclosing unit comes from the same bounded four-level prop-unit walk the FLOOR arm already
   uses; `max`, never a substitution, so a figure root that spans more than its prop unit keeps
   its own number.

No new constant, no name match, no tileset, no size and no coordinate. `FootBandWU` (1.0 wu),
`PropUnitMaxDepth` (4) and the actor veto are all incumbents.

## How the floor formation stays protected

The protected object is **not** an asset name. This level parents the *same asset name* twice:

| | protected floor formation | the wall furniture in the photo |
|---|---|---|
| path | `Generated Content/Full/PCG_CV_Ice_Clutter_Floor_0N_PR/CV_Ice_Crystal_Form_02 (…)` | `Wall 1/Generated Content/PCG_CV_Ice_Feature_Medium_02_PR/CV_Ice_Crystal_Form_04 (1)` |
| four-level window | **no wall** (`Full` and `Generated Content` are SIBLINGS of `Walls/`) | `Wall 1` at depth 3 |
| ModBuild-290 roll-call | `'CV_Ice_Clutter_01' FLOOR arm, no wall above … PROTECTED` | `'CV_Ice_Crystal_Form_04' FIGURE arm, under a wall … PROTECTED` |

Three independent guards keep the floor formation:

1. `IsWallBuiltUnitMember` returns **false** for it — its bounded window finds no wall. The
   unbounded `GetComponentInParent<ProceduralWall>()` climb inside `IsWallGeneratedDressing`
   *would* have answered YES for it (this is written down in `WallInUnitWindow`'s own doc), which
   is precisely why the new term does **not** reuse that predicate.
2. `IsWallBuiltSection` conjunct 2 (`!wallCut`) refuses it before the ground-band line is reached.
   Conjunct 3 is the only clause this round touched.
3. `CV_Ice_Clutter_01` takes the **FLOOR** arm, and neither change is on the FLOOR arm at all.

Ground cover the tileset *does* parent under walls (`PCG_FR_Floor_Grass_Hex_Half_PR`,
`PCG_CR_Floor_BaseHex_Plain`) is safe by construction: the four-level walk resolves either no unit
(a one-renderer wrapper) or a unit that is itself ground cover, topping out ~0.2 wu. Widening the
measured unit lifts the verdict only where the unit a fragment belongs to *actually* rises out of
the ground band — which is what "the game is using this as a wall section" means.

Figures: `Choreographer` parents every spawned figure to the BOARD root (three spawn paths, all
three checked in `IsWallGeneratedDressing`'s note), so no hero, monster or summon is inside a
wall's four-level window, and the actor veto refuses it a second time if one ever were.

## The instrument

The brief's requirement was: make the log able to say, for this segment, **which** renderer or
classification refused it, **by name**, and under **which clause**.

* Every `STAYS WHOLE AND SOLID` row now names **which arm** of `IsFigureOrActorRenderer` fired,
  **on which GameObject**, and the member's bounded ancestry —
  `'CV_Ice_Crystal_Form_04': FIGURE (…) via FIGURE arm: Animator on 'CV_Ice_Crystal_Form_04 (1)'; NO wall inside … @ Wall 1/Generated Content/…`.
  ModBuild 290 printed only `FIGURE`, which is a predicate's return value, not a cause: an
  `Animator` on a crystal and an `ActorBehaviour` on a monster were indistinguishable in every
  field the line carried, and they demand opposite treatment.
* A new `WALL FURNITURE DOES NOT REFUSE ITS UNIT` clause on the `PROP UNIT` line counts and names
  every lift, with the same ancestry column. Its own counter and its own roster, never folded into
  the refusal count beside it.
* `IsWallBuiltSection`'s sentence now says **which unit** carried the ground-band verdict, how many
  renderers it owns, and both tops — so a refusal can no longer print a number without saying the
  number came off the wrong body.

### The falsifier

**The PATH in those rows, never the asset name.** A `WALL FURNITURE` or `[WALL-SECTION]` row whose
ancestry contains no `Wall` node — a hero, a monster, a summon, a floor hex, a
`PCG_*_Clutter_Floor_*` member — means the bounded window is not the discriminator and the lift
must be **withdrawn, not retuned**.

Note that the ModBuild-275 falsifier as written ("the `[WALL-SECTION]` roster naming … the crystal
formation or a light shaft") **already fires in the ModBuild-290 log, on correct behaviour**, and
it is wrong as written for exactly the reason above: it names an asset family where the ruling is
about a location. It is restated in the code as a path test.

### Expected reading of the next hardware log

* `PROP UNIT`: `unit(s) refused this rescan` for `PCG_CV_Ice_Feature_Medium_02_PR` and
  `PCG_CV_Ice_Bay_Small_01_PR` → **0**; `WALL FURNITURE DOES NOT REFUSE ITS UNIT` → **≥ 5**, every
  row's path containing `Wall N/Generated Content/`.
* `FADE WRITE`: `PCG_CV_Ice_Feature_Medium_02_PR` reads **22/22 written**, not `1/22`.
* `STANDING PROP` roll-call: `'CV_Ice_Crystal_Form_04' … [WALL-SECTION] RELEASED`, and
  `'CV_Ice_Clutter_01' FLOOR arm, no wall above … PROTECTED` **unchanged**.
* `LEFTOVER OVER A FADED WALL`: the `WALL MEMBER` class (which must read zero) loses its
  `PCG_CV_Ice_Feature_Medium_02_PR` rows.

## WHAT I COULD NOT VERIFY WITHOUT HARDWARE

Everything below is reasoned from the ModBuild-290 log and the source; none of it has been run.

1. **That the feature actually fades.** The build machine cannot open this scene. The chain
   (refusal lifted → standing rule releases → `CollectWallFadeInfo` collects → wall's own fade
   channel) is traced through source, not executed. The one link with residual risk is whether
   every one of the 21 currently-solid renderers carries a wall-fade channel; the log says the
   named members do (`HAS a fade channel`), but 15 of 21 names were dropped by the census cap. If
   some do not, they land in `_unitStageDress` and get a dissolve record instead — the intended
   path, but an untested one for this asset family.
2. **That nothing else in this or another scene is released.** The lift is scene-generic by
   design. The ModBuild-290 log contains exactly five refusals and all five are this subject, so
   in *this* scene the blast radius is those five units. I cannot enumerate the effect on the
   forest, crypt or ruins tilesets — the new census roster exists so the next log can.
3. **The exact identity of "der Kristall auf dem Boden".** I matched it to
   `PCG_CV_Ice_Clutter_Floor_0N_PR` under `Generated Content/Full/` from the ModBuild-290
   roll-call and from `WallInUnitWindow`'s own documented path. If the object he means is a
   *different* floor formation that happens to sit under a wall subtree, this fix would fade it.
   The guard is that the roll-call prints its path — one grep on the next log settles it.
4. **Whether the large arched block on the left of the photograph is a doorway.** It reads as one
   (`CV_Doorway_04 (1)` appears in the same log's ray census) and doorways never fade by standing
   ruling, so I treated it as correct and out of scope. He said "das rechte Element"; if he also
   wants the left one, that is a different rule.
5. **The `wallCut` seeding order.** `MeasureStandingUnit` is now reachable for a prop-unit root
   from the FIGURE branch as well as the FLOOR branch, and the memo is first-writer-wins on
   `wallCut`. Both arms pass the same value (the renderer's parent's window answer) for a root
   reached from the same renderer, so this is not a new behaviour class — but it *is* a new
   caller into a first-writer-wins memo, and only a log can show that no root ever changes value.
6. **Cost.** The new work is one extra bounded walk per figure-armed unit member at the refusal
   site and one extra memoised unit measurement per wall-section candidate — three units in the
   whole ModBuild-274 scene by that log's own count. Not measured on the Quest 3.

## Gates (this branch)

| gate | value |
|---|---|
| build | 0 errors / **4** warnings |
| wire tests | **150837** assertions (unchanged — no vectors added) |
| patch surface | 80 classes / 132 methods |
| frame order | 7 |
| mirrors | 18 |
| remote defaults | 76 |
| refasm | 16 |
| `rebase-defaults.py check` | exit 0 |

No wire change. No `ModBuild` bump. No config key. Multiplayer: local scene-hierarchy reads only —
nothing networked, no peer-visible state, and the decision is derived identically on every peer
from the same scene, so a mismatch is impossible by construction.
