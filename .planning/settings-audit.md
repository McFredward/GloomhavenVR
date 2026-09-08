# VR settings audit — user-friendliness pass (ModBuild 222)

> ### ⚠ PARTLY STALE — audited 2026-09-08 against `dev` = `49ceab21` (ModBuild 483)
>
> **The RULINGS in this file still stand; the MENU TREE it proposes does not.** It was written
> against ModBuild 222 (2026-08-22), 261 builds behind HEAD, and the curated categories have since
> been restructured twice.
>
> * **Superseded by `.planning/refactor/MENU-STRUCTURE.md`** — that file is the current rationale
>   for which element belongs in which category, and `.planning/menu-audit/` is the five-part
>   census behind it.
> * Two later rounds also moved things this file describes:
>   [`menu-findability-2026-09-02.md`](menu-findability-2026-09-02.md) and
>   [`MENU-NEVER-DISTURBS-PLAY.md`](MENU-NEVER-DISTURBS-PLAY.md), both at ModBuild 340.
>
> What is still live is the user's four questions of 2026-08-22 and the policy they produced:
> delete any dial that can break the game, put anything you would not trust a user with into
> **Erweitert**, and do not give a setting a slider when it is really adjusted to two decimals.
> Its §7 open questions and its "standing ruling" callouts should be re-checked before acting,
> not assumed.

**Review document only. No source file was changed by this pass. Nothing was committed.**

Written 2026-08-22 against `HEAD = a858a8b` (ModBuild 222) in answer to the user's four questions of
2026-08-22:

> a) Lösche alle Einstellungen die das Spiel breaken könnten wenn die verändert werden. Etwas was das
> spiel kaputt macht wenn man es umstellt ist nicht optional und sollte daher nicht einstellbar sein.
> b) Prüfe jede Einstellung ob du sie User zutrauen würdest, wenn nicht gehören sie in Erweitert.
> c) Überprüfe die Kategorien und ordne sie eventuell neu wenn du denkst das es intuitiver und
> Userfreundlicher wäre.
> d) Prüfe für jede EInstellung die Bedienmöglichkeit, nicht jedes Felt macht sinn mit einer
> verschibaren Bar besonders wenn man bis auf die Kommastellen etwas anpassen will.

---

## 0. Method, coverage, and what is read vs inferred

**READ FROM SOURCE.** The inventory below was built mechanically, not by hand:

* every `.Bind(` call site in `src/` was parsed, including the interpolated per-board
  (`_{board}` → Oak/Steel/Bronze), per-pile (`_{pileNames[p]}` → Items/Discard/Burnt) and per-style
  (`{s}` → Glove/Plate/Arcane) families, and the `ComfortSettings.Bind<T>(key, …)` wrapper (and the one raw
  `_file.Bind(SectionName, …)` call beside it) that hides the section behind `SectionName`. **582 bound keys.**
* type, shipped default, `AcceptableValueRange`/`AcceptableValueList` and the English description come
  from the pristine config drop in `.planning/debug/default/*.cfg` (17 files, 655 lines — of which
  **105 are orphans**: keys BepInEx still round-trips into the file that nothing in ModBuild 222 binds
  any more, the residue of the 2026-08 dead-settings sweep).
* the widget each row gets is the *re-implementation* of `VROptionsTab.2.Rows.BuildRow`'s decision
  ladder (special row → bool → choice → bounded scalar slider → stepper), plus
  `VROptionsTab.4.Curated.HasSpecialRow` / `PrefersStepper`.
* the step is the re-implementation of `ConfigCatalog.ResolveStep` over `ConfigSteps.Explicit`,
  `ConfigSteps.Units`, `UnitScope` and `NiceStep`.
* curated placement is parsed straight out of `VROptionsTab.4.Curated.Curated`; the Erweitert topic is
  the re-implementation of `ConfigCatalog.TopicOf`.
* retired entries are those whose bound description starts with `LEGACY — no effect` / `RESERVED —` /
  `DEPRECATED —` (`ConfigCatalog.RetiredMarkers`); "not offered" is `ConfigCatalog.NotOffered`.

**VALIDATION.** The model says **541 offered / 31 retired / 10 not-offered**. The live ModBuild 222
session log says, in its own words:

```
[Config] In-VR config browser catalogued 542 entries from 18 config files
        (8 free-text/unsupported, shown read-only; 32 left out as retired …)
```

541 vs 542 and 31 vs 32 — the model is within two entries, and the read-only count (8) matches
exactly. Anything below that resolution is not claimed.

**COVERAGE GAPS, stated plainly.**

1. The cfg drop is dated 2026-08-15 (ModBuild ~219); HEAD is 222. **31 keys bound today are not in
   it** — `[PeerBoardFade]` ×6, `[MapRoom]` ×5, `[EnvSound] AmbienceBed`+`Gain`, `[Board]
   AutoFocusOnTurn`, `[Comfort] TurnStickVertical`, `[WorldUI]` window/map ×8, `[SquareCaps]`/
   `[TransientButtons]` ×8. Their type/default/range were read from the Bind call sites by hand
   instead; the table marks them.
2. A handful of shipped defaults have moved since the drop (e.g. `[Haunt] Frequency` is `0.5` in
   `Defaults.Core.cs` today, `0.2162736` in the drop). Where a number matters to an argument below I
   quote `src/GloomhavenVR/Defaults/Defaults.*.cs`, which is authoritative.
3. The gap between my 582 static keys and the runtime's 574 (542 + 32) is ~7 conditionally-bound
   entries (`[PeerBoardFade]` only binds when a peer board appears; `[SquareCaps]`/
   `[TransientButtons]` only on a legacy migration). Not investigated further.
4. Widget/step are **computed**, not observed in the headset. Everything I say about how a control
   *feels* at arm's length is marked as inference.

**INFERENCE IS MARKED.** Every claim below that is not read from a source file or a log carries
_(inferred)_.

**THE TUNED-VALUE TRAP WAS CHECKED.** `python3 scripts/rebase-defaults.py check` reports *"every cfg
value already equals the shipped default (511 entries checked)"* — the drop in `.planning/debug/default/`
**is** the shipped-defaults state, so nothing in it is an un-migrated tuning of his that a deletion
would silently discard (the `[Water] RippleSpeed` failure mode). Every key I propose deleting was
additionally grepped against that drop and against today's `Player.log`; none of them carries a value
the user set.

---

## 1. Inventory

582 bound keys. Columns:

* **Where it is shown** — `kuratiert: Tab ▸ Abschnitt` for the 100 hand-picked rows, `Erweitert ▸ Thema`
  for the rest (Erweitert is the catalog's own topic index, so everything not curated is there),
  `not offered` for retired markers and the `NotOffered` list.
* **Widget today** — what `BuildRow` actually builds.
* **Step** — one press of ◀/▶ per `ConfigSteps`. **Grey where the row is a slider: a slider ignores the
  step entirely** (see §5).
* **Values reachable** — for a stepper with a declared range, the number of presses to cross it; for a
  float slider `stufenlos` (`slider.wholeNumbers = item.Integral`, so a float bar has no grid at all);
  for an int slider the integer count.
* **Flags** — the verdict, cross-referenced in §2–§5.

| # | Section | Key | Type | Shipped default | Range | Where it is shown | Widget today | Step | Values reachable | Name (EN / DE) | Flags |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | Board | `AoeFlickThreshold` | Single | `0.6` | — | Erweitert ▸ Brett & Zielen | Stepper | 0.05 | unbegrenzt | AoE turn: stick min / AoE-Drehen: Schwelle |  |
| 2 | Board | `AoeRepeatInterval` | Single | `0.35` | — | Erweitert ▸ Brett & Zielen | Stepper | 0.05 | unbegrenzt | AoE turn: repeat (s) / AoE-Drehen: Takt (s) |  |
| 3 | Board | `AutoFocusOnTurn` | Boolean | `true` | — | Erweitert ▸ Brett & Zielen | Toggle | — | 2 | Follow the character at turn / Automatisch zum Character am Zug | →kuratiert |
| 4 | Board | `HoverHaptics` | Boolean | `true` | — | Erweitert ▸ Brett & Zielen | Toggle | — | 2 | Haptics on new target / Vibration bei Wechsel | →kuratiert |
| 5 | Board | `SnapToHexCenter` | Boolean | `false` | — | Erweitert ▸ Brett & Zielen | Toggle | — | 2 | Snap to hex centre / Auf Hexmitte einrasten |  |
| 6 | Board | `TouchRange` | Single | `0.1` | — | Erweitert ▸ Brett & Zielen | Stepper | 0.002 | unbegrenzt | Fingertip pick range (m) / Fingerreichweite (m) |  |
| 7 | Board | `TouchTilesWithFingertip` | Boolean | `true` | — | kuratiert: Komfort ▸ Hände & Zielen | Toggle | — | 2 | Touch hexes: fingertip / Feld mit Finger antippen |  |
| 8 | BoardButtons | `Depth` | Single | `0.014` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 0.005 | unbegrenzt | Confirm/Undo: depth (m) / Best./Zurück: Tiefe |  |
| 9 | BoardButtons | `Height` | Single | `0.065` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 0.01 | unbegrenzt | Confirm/Undo: height (m) / Best./Zurück: Höhe |  |
| 10 | BoardButtons | `Travel` | Single | `0.004` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 0.001 | unbegrenzt | Confirm/Undo: travel (m) / Best./Zurück: Hub |  |
| 11 | BoardButtons | `Width` | Single | `0.063` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 0.01 | unbegrenzt | Confirm/Undo: width (m) / Best./Zurück: Breite |  |
| 12 | BoardDashboard | `Depth` | Single | `0.014` | — | Erweitert ▸ Tasten | Stepper | 0.005 | unbegrenzt | Gear/pin: depth (m) / Zahnrad/Pin: Tiefe (m) |  |
| 13 | BoardDashboard | `Height` | Single | `0.035` | — | Erweitert ▸ Tasten | Stepper | 0.01 | unbegrenzt | Gear/pin: height (m) / Zahnrad/Pin: Höhe (m) |  |
| 14 | BoardDashboard | `PinWidth` | Single | `0.068` | — | Erweitert ▸ Tasten | Stepper | 0.01 | unbegrenzt | Gear/pin: width (m) / Zahnrad/Pin: Breite (m) |  |
| 15 | BoardDashboard | `Travel` | Single | `0.004` | — | Erweitert ▸ Tasten | Stepper | 0.001 | unbegrenzt | Gear/pin: travel (m) / Zahnrad/Pin: Hub (m) |  |
| 16 | ButtonAnim | `AppearParticles` | Boolean | `true` | — | Erweitert ▸ Tasten | Toggle | — | 2 | Dust on appear / Staub beim Erscheinen |  |
| 17 | ButtonAnim | `AppearSeconds` | Single | `0.15` | — | Erweitert ▸ Tasten | Stepper | 0.05 | unbegrenzt | Appear time (s) / Erscheinen (s) |  |
| 18 | ButtonAnim | `DisappearSeconds` | Single | `0.16` | — | Erweitert ▸ Tasten | Stepper | 0.05 | unbegrenzt | Disappear time (s) / Verschwinden (s) |  |
| 19 | ButtonAnim | `Enable` | Boolean | `true` | — | kuratiert: Tafeln ▸ Klick & Zeigen | Toggle | — | 2 | Key animation / Tasten-Animation |  |
| 20 | ButtonColors | `BoardCapTintB` | Single | `0.5` | — | Erweitert ▸ Tasten | Stepper | 0.01 | unbegrenzt | Confirm/Undo tint: blue / Bestätigen-Ton: Blau |  |
| 21 | ButtonColors | `BoardCapTintG` | Single | `0.5` | — | Erweitert ▸ Tasten | Stepper | 0.01 | unbegrenzt | Confirm/Undo tint: green / Bestätigen-Ton: Grün |  |
| 22 | ButtonColors | `BoardCapTintR` | Single | `0.5` | — | Erweitert ▸ Tasten | Stepper | 0.01 | unbegrenzt | Confirm/Undo tint: red / Bestätigen-Ton: Rot |  |
| 23 | ButtonColors | `ClusterCapTintB` | Single | `0.5` | — | Erweitert ▸ Tasten | Stepper | 0.01 | unbegrenzt | Round-btn tint: blue / Rundenknopf-Ton: Blau |  |
| 24 | ButtonColors | `ClusterCapTintG` | Single | `0.5` | — | Erweitert ▸ Tasten | Stepper | 0.01 | unbegrenzt | Round-btn tint: green / Rundenknopf-Ton: Grün |  |
| 25 | ButtonColors | `ClusterCapTintR` | Single | `0.5` | — | Erweitert ▸ Tasten | Stepper | 0.01 | unbegrenzt | Round-btn tint: red / Rundenknopf-Ton: Rot |  |
| 26 | ButtonColors | `DashCapTintB` | Single | `0.5` | — | Erweitert ▸ Tasten | Stepper | 0.01 | unbegrenzt | Gear/pin tint: blue / Zahnrad-Ton: Blau |  |
| 27 | ButtonColors | `DashCapTintG` | Single | `0.5` | — | Erweitert ▸ Tasten | Stepper | 0.01 | unbegrenzt | Gear/pin tint: green / Zahnrad-Ton: Grün |  |
| 28 | ButtonColors | `DashCapTintR` | Single | `0.5` | — | Erweitert ▸ Tasten | Stepper | 0.01 | unbegrenzt | Gear/pin tint: red / Zahnrad-Ton: Rot |  |
| 29 | ButtonColors | `LabelB` | Single | `0.878` | — | Erweitert ▸ Tasten | Stepper | 0.02 | unbegrenzt | Key label: blue / Tastenschrift: Blau |  |
| 30 | ButtonColors | `LabelG` | Single | `0.953` | — | Erweitert ▸ Tasten | Stepper | 0.02 | unbegrenzt | Key label: green / Tastenschrift: Grün |  |
| 31 | ButtonColors | `LabelOutline` | Boolean | `true` | — | Erweitert ▸ Tasten | Toggle | — | 2 | Key label: outline / Tastenschrift: Umriss |  |
| 32 | ButtonColors | `LabelOutlineB` | Single | `0.5` | — | Erweitert ▸ Tasten | Stepper | 0.01 | unbegrenzt | Label outline: blue / Schrift-Umriss: Blau |  |
| 33 | ButtonColors | `LabelOutlineG` | Single | `0.5` | — | Erweitert ▸ Tasten | Stepper | 0.01 | unbegrenzt | Label outline: green / Schrift-Umriss: Grün |  |
| 34 | ButtonColors | `LabelOutlineR` | Single | `0.5` | — | Erweitert ▸ Tasten | Stepper | 0.01 | unbegrenzt | Label outline: red / Schrift-Umriss: Rot |  |
| 35 | ButtonColors | `LabelOutlineWidth` | Single | `0.2` | — | Erweitert ▸ Tasten | Stepper | 0.01 | unbegrenzt | Label outline: width / Schrift-Umriss: Breite |  |
| 36 | ButtonColors | `LabelR` | Single | `0.984` | — | Erweitert ▸ Tasten | Stepper | 0.02 | unbegrenzt | Key label: red / Tastenschrift: Rot |  |
| 37 | ButtonColors | `LabelUnderlay` | Boolean | `true` | — | Erweitert ▸ Tasten | Toggle | — | 2 | Key label: shadow / Tastenschrift: Schatten |  |
| 38 | ButtonColors | `RestCapTintB` | Single | `0.5` | — | Erweitert ▸ Tasten | Stepper | 0.01 | unbegrenzt | Rest-key tint: blue / Rast-Tasten-Ton: Blau |  |
| 39 | ButtonColors | `RestCapTintG` | Single | `0.5` | — | Erweitert ▸ Tasten | Stepper | 0.01 | unbegrenzt | Rest-key tint: green / Rast-Tasten-Ton: Grün |  |
| 40 | ButtonColors | `RestCapTintR` | Single | `0.5` | — | Erweitert ▸ Tasten | Stepper | 0.01 | unbegrenzt | Rest-key tint: red / Rast-Tasten-Ton: Rot |  |
| 41 | Cards | `ActiveCardScale_Bronze` | Single | `1.07` | 0.25 … 3 | Erweitert ▸ Steuerbrett (pro Brett) | Slider | 0.05 | stufenlos | Active cards: size / Aktive Karten: Größe |  |
| 42 | Cards | `ActiveCardScale_Oak` | Single | `0.9999998` | 0.25 … 3 | Erweitert ▸ Steuerbrett (pro Brett) | Slider | 0.05 | stufenlos | Active cards: size / Aktive Karten: Größe |  |
| 43 | Cards | `ActiveCardScale_Steel` | Single | `0.82` | 0.25 … 3 | Erweitert ▸ Steuerbrett (pro Brett) | Slider | 0.05 | stufenlos | Active cards: size / Aktive Karten: Größe |  |
| 44 | Cards | `ActiveGridSpacing_Bronze` | Vector2 | `{"x":1.059999942779541,"y":0.699999988079071}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x2 | 0.005 | unbegrenzt | Active cards: grid step / Aktive Karten: Raster |  |
| 45 | Cards | `ActiveGridSpacing_Oak` | Vector2 | `{"x":1.059999942779541,"y":0.699999988079071}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x2 | 0.005 | unbegrenzt | Active cards: grid step / Aktive Karten: Raster |  |
| 46 | Cards | `ActiveGridSpacing_Steel` | Vector2 | `{"x":1.059999942779541,"y":0.699999988079071}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x2 | 0.005 | unbegrenzt | Active cards: grid step / Aktive Karten: Raster |  |
| 47 | Cards | `ActiveOffset_Bronze` | Vector3 | `{"x":0.0,"y":0.09000001102685929,"z":-0.004999998025596142}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Active cards: position / Aktive Karten: Position |  |
| 48 | Cards | `ActiveOffset_Oak` | Vector3 | `{"x":1.1175870645585562e-10,"y":0.0,"z":0.0}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Active cards: position / Aktive Karten: Position |  |
| 49 | Cards | `ActiveOffset_Steel` | Vector3 | `{"x":0.0,"y":0.0,"z":-0.03999999910593033}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Active cards: position / Aktive Karten: Position |  |
| 50 | Cards | `AssetOffset_Bronze` | Vector3 | `{"x":0.0,"y":-0.10999999940395355,"z":0.07999999821186066}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Board mesh: position / Brett-Mesh: Position |  |
| 51 | Cards | `AssetOffset_Oak` | Vector3 | `{"x":0.0,"y":0.0,"z":0.0}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Board mesh: position / Brett-Mesh: Position |  |
| 52 | Cards | `AssetOffset_Steel` | Vector3 | `{"x":0.0,"y":0.0,"z":0.0}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Board mesh: position / Brett-Mesh: Position |  |
| 53 | Cards | `AssetPitchDegrees_Bronze` | Single | `57` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 1 | unbegrenzt | Board mesh: pitch (°) / Brett-Mesh: Neigung (°) |  |
| 54 | Cards | `AssetPitchDegrees_Oak` | Single | `0` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 1 | unbegrenzt | Board mesh: pitch (°) / Brett-Mesh: Neigung (°) |  |
| 55 | Cards | `AssetPitchDegrees_Steel` | Single | `0` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 1 | unbegrenzt | Board mesh: pitch (°) / Brett-Mesh: Neigung (°) |  |
| 56 | Cards | `AssetRollDegrees_Bronze` | Single | `0` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 1 | unbegrenzt | Board mesh: roll (°) / Brett-Mesh: Rollen (°) |  |
| 57 | Cards | `AssetRollDegrees_Oak` | Single | `0` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 1 | unbegrenzt | Board mesh: roll (°) / Brett-Mesh: Rollen (°) |  |
| 58 | Cards | `AssetRollDegrees_Steel` | Single | `0` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 1 | unbegrenzt | Board mesh: roll (°) / Brett-Mesh: Rollen (°) |  |
| 59 | Cards | `AssetRotation_Bronze` | Vector3 | `{"x":0.0,"y":0.0,"z":0.0}` | — | **not offered** (retired marker) | Stepper x3 | 0.005 | unbegrenzt | _(kein Name — Menü zeigt den englischen Key)_ | retired |
| 60 | Cards | `AssetRotation_Oak` | Vector3 | `{"x":0.0,"y":0.0,"z":0.0}` | — | **not offered** (retired marker) | Stepper x3 | 0.005 | unbegrenzt | _(kein Name — Menü zeigt den englischen Key)_ | retired |
| 61 | Cards | `AssetRotation_Steel` | Vector3 | `{"x":0.0,"y":0.0,"z":0.0}` | — | **not offered** (retired marker) | Stepper x3 | 0.005 | unbegrenzt | _(kein Name — Menü zeigt den englischen Key)_ | retired |
| 62 | Cards | `AssetYawDegrees_Bronze` | Single | `0` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 1 | unbegrenzt | Board mesh: yaw (°) / Brett-Mesh: Drehung (°) |  |
| 63 | Cards | `AssetYawDegrees_Oak` | Single | `0` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 1 | unbegrenzt | Board mesh: yaw (°) / Brett-Mesh: Drehung (°) |  |
| 64 | Cards | `AssetYawDegrees_Steel` | Single | `0` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 1 | unbegrenzt | Board mesh: yaw (°) / Brett-Mesh: Drehung (°) |  |
| 65 | Cards | `Board` | ControlBoard | `Steel` | — | kuratiert: Brett & Karten ▸ Kontrollbrett | Dropdown (enum) | — | Liste | Control board / Kontrollbrett |  |
| 66 | Cards | `BoardMaxWidthMeters` | Single | `1.4` | 0.2 … 4 | Erweitert ▸ Karten & Fächer | Slider | 0.1 | stufenlos | Board: max width (m) / Brett: max. Breite (m) |  |
| 67 | Cards | `BoardMinWidthMeters` | Single | `0.18` | 0.05 … 1 | Erweitert ▸ Karten & Fächer | Slider | 0.02 | stufenlos | Board: min width (m) / Brett: min. Breite (m) |  |
| 68 | Cards | `BoardMoveMode` | BoardMoveMode | `Free` | — | kuratiert: Brett & Karten ▸ Kontrollbrett | Dropdown (preset) | — | Liste | Board: movement / Brett: Bewegung |  |
| 69 | Cards | `BoardPitchMaxDegrees` | Single | `45` | — | **not offered** (retired marker) | Stepper | 1 | unbegrenzt | _(kein Name — Menü zeigt den englischen Key)_ | retired |
| 70 | Cards | `BoardPitchMax_Bronze` | Single | `45` | -85 … 85 | kuratiert: Brett & Karten ▸ Kontrollbrett | Slider | 1 | stufenlos | Tilt limit up (°) / Neigungslimit oben (°) | →Erweitert; widget: Bar+Pfeile |
| 71 | Cards | `BoardPitchMax_Oak` | Single | `45` | -85 … 85 | kuratiert: Brett & Karten ▸ Kontrollbrett | Slider | 1 | stufenlos | Tilt limit up (°) / Neigungslimit oben (°) | →Erweitert; widget: Bar+Pfeile |
| 72 | Cards | `BoardPitchMax_Steel` | Single | `54.353` | -85 … 85 | kuratiert: Brett & Karten ▸ Kontrollbrett | Slider | 1 | stufenlos | Tilt limit up (°) / Neigungslimit oben (°) | →Erweitert; widget: Bar+Pfeile |
| 73 | Cards | `BoardPitchMinDegrees` | Single | `-45` | — | **not offered** (retired marker) | Stepper | 1 | unbegrenzt | _(kein Name — Menü zeigt den englischen Key)_ | retired |
| 74 | Cards | `BoardPitchMin_Bronze` | Single | `-45` | -85 … 85 | kuratiert: Brett & Karten ▸ Kontrollbrett | Slider | 1 | stufenlos | Tilt limit down (°) / Neigungslimit unten (°) | →Erweitert; widget: Bar+Pfeile |
| 75 | Cards | `BoardPitchMin_Oak` | Single | `-45` | -85 … 85 | kuratiert: Brett & Karten ▸ Kontrollbrett | Slider | 1 | stufenlos | Tilt limit down (°) / Neigungslimit unten (°) | →Erweitert; widget: Bar+Pfeile |
| 76 | Cards | `BoardPitchMin_Steel` | Single | `-31.067` | -85 … 85 | kuratiert: Brett & Karten ▸ Kontrollbrett | Slider | 1 | stufenlos | Tilt limit down (°) / Neigungslimit unten (°) | →Erweitert; widget: Bar+Pfeile |
| 77 | Cards | `BoardPosOffset_Bronze` | Vector3 | `{"x":0.0,"y":0.0,"z":0.0}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Board: position offset / Brett: Positionsversatz |  |
| 78 | Cards | `BoardPosOffset_Oak` | Vector3 | `{"x":0.0,"y":0.0,"z":0.0}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Board: position offset / Brett: Positionsversatz |  |
| 79 | Cards | `BoardPosOffset_Steel` | Vector3 | `{"x":0.0,"y":0.0,"z":0.0}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Board: position offset / Brett: Positionsversatz |  |
| 80 | Cards | `BoardScaleDefault04Applied` | Boolean | `true` | — | Erweitert ▸ Karten & Fächer | Toggle | — | 2 | Internal marker / Interne Marke | **DELETE** |
| 81 | Cards | `BoardScale_Bronze` | Single | `0.5426549` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 0.05 | unbegrenzt | Board: size factor / Brett: Größenfaktor |  |
| 82 | Cards | `BoardScale_Oak` | Single | `0.5426549` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 0.05 | unbegrenzt | Board: size factor / Brett: Größenfaktor |  |
| 83 | Cards | `BoardScale_Steel` | Single | `0.5426551` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 0.05 | unbegrenzt | Board: size factor / Brett: Größenfaktor |  |
| 84 | Cards | `BoardTilt_Bronze` | Single | `30` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 1 | unbegrenzt | Board: base tilt (°) / Brett: Grundneigung (°) |  |
| 85 | Cards | `BoardTilt_Oak` | Single | `30` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 1 | unbegrenzt | Board: base tilt (°) / Brett: Grundneigung (°) |  |
| 86 | Cards | `BoardTilt_Steel` | Single | `30` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 1 | unbegrenzt | Board: base tilt (°) / Brett: Grundneigung (°) |  |
| 87 | Cards | `BoardYaw_Bronze` | Single | `0` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 1 | unbegrenzt | Board: extra yaw (°) / Brett: Zusatzdrehung (°) |  |
| 88 | Cards | `BoardYaw_Oak` | Single | `0` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 1 | unbegrenzt | Board: extra yaw (°) / Brett: Zusatzdrehung (°) |  |
| 89 | Cards | `BoardYaw_Steel` | Single | `0` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 1 | unbegrenzt | Board: extra yaw (°) / Brett: Zusatzdrehung (°) |  |
| 90 | Cards | `BrowseFanOffset` | Vector3 | `{"x":0.0,"y":-0.3700000047683716,"z":-0.15000000596046449}` | — | Erweitert ▸ Karten & Fächer | Stepper x3 | 0.005 | unbegrenzt | Browse fan: offset (m) / Stapel: Versatz (m) |  |
| 91 | Cards | `CardDust` | Boolean | `false` | — | Erweitert ▸ Karten & Fächer | Toggle | — | 2 | Card dust burst / Karten-Staubwolke |  |
| 92 | Cards | `CardGrabSound` | String | `PlaySound_UICardTabSelect` | — | Erweitert ▸ Karten & Fächer | read-only text | — | — | Sound: card grabbed / Klang: Karte greifen | widget: Auswahlliste |
| 93 | Cards | `CardLerpSpeed` | Single | `14` | — | Erweitert ▸ Karten & Fächer | Stepper | 1 | unbegrenzt | Card flight speed / Kartenflug-Tempo |  |
| 94 | Cards | `CardPlaceSound` | String | `PlaySound_CardUI_SelectCard` | — | Erweitert ▸ Karten & Fächer | read-only text | — | — | Sound: card placed / Klang: Karte ablegen | widget: Auswahlliste |
| 95 | Cards | `CardSoundsEnabled` | Boolean | `true` | — | kuratiert: Brett & Karten ▸ Karten | Toggle | — | 2 | Card sounds / Karten-Geräusche |  |
| 96 | Cards | `CardTakeBackSound` | String | `PlaySound_UIUndoHex` | — | Erweitert ▸ Karten & Fächer | read-only text | — | — | Sound: card taken back / Klang: Karte zurück | widget: Auswahlliste |
| 97 | Cards | `CardWidth` | Single | `0.0635` | 0.03 … 0.15 | kuratiert: Brett & Karten ▸ Karten | Slider | 0.005 | stufenlos | Card width (m) / Kartenbreite (m) | widget: Bar+Pfeile |
| 98 | Cards | `ClusterOffset_Bronze` | Vector3 | `{"x":1.1175870645585562e-10,"y":0.009999999776482582,"z":0.019999999552965165}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Button cluster: position / Tastengruppe: Position |  |
| 99 | Cards | `ClusterOffset_Oak` | Vector3 | `{"x":1.1175870645585562e-10,"y":1.974403751603404e-9,"z":1.1175870645585562e-10}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Button cluster: position / Tastengruppe: Position |  |
| 100 | Cards | `ClusterOffset_Steel` | Vector3 | `{"x":0.0,"y":0.0,"z":-0.019999999552965165}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Button cluster: position / Tastengruppe: Position |  |
| 101 | Cards | `ClusterScale_Bronze` | Single | `1` | 0.25 … 3 | Erweitert ▸ Steuerbrett (pro Brett) | Slider | 0.05 | stufenlos | Button cluster: size / Tastengruppe: Größe |  |
| 102 | Cards | `ClusterScale_Oak` | Single | `0.9999999` | 0.25 … 3 | Erweitert ▸ Steuerbrett (pro Brett) | Slider | 0.05 | stufenlos | Button cluster: size / Tastengruppe: Größe |  |
| 103 | Cards | `ClusterScale_Steel` | Single | `1` | 0.25 … 3 | Erweitert ▸ Steuerbrett (pro Brett) | Slider | 0.05 | stufenlos | Button cluster: size / Tastengruppe: Größe |  |
| 104 | Cards | `ConfirmUndoInsetX` | Single | `0.014` | — | **not offered** (retired marker) | Stepper | 0.01 | unbegrenzt | _(kein Name — Menü zeigt den englischen Key)_ | retired |
| 105 | Cards | `ConfirmUndoOffset_Bronze` | Vector3 | `{"x":0.44699999690055849,"y":0.01099999900907278,"z":-0.007000000216066837}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Confirm/Undo: position / Best./Zurück: Position |  |
| 106 | Cards | `ConfirmUndoOffset_Oak` | Vector3 | `{"x":-0.00800000037997961,"y":0.0,"z":0.008999999612569809}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Confirm/Undo: position / Best./Zurück: Position |  |
| 107 | Cards | `ConfirmUndoOffset_Steel` | Vector3 | `{"x":0.4620000123977661,"y":0.006000000052154064,"z":-0.04699999839067459}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Confirm/Undo: position / Best./Zurück: Position |  |
| 108 | Cards | `DecisionGap_Bronze` | Single | `0.01733499` | 0 … 0.12 | Erweitert ▸ Steuerbrett (pro Brett) | Slider | 0.01 | stufenlos | Decision: text gap (m) / Entscheidung: Textlücke |  |
| 109 | Cards | `DecisionGap_Oak` | Single | `0.01367789` | 0 … 0.12 | Erweitert ▸ Steuerbrett (pro Brett) | Slider | 0.01 | stufenlos | Decision: text gap (m) / Entscheidung: Textlücke |  |
| 110 | Cards | `DecisionGap_Steel` | Single | `0.01801924` | 0 … 0.12 | Erweitert ▸ Steuerbrett (pro Brett) | Slider | 0.01 | stufenlos | Decision: text gap (m) / Entscheidung: Textlücke |  |
| 111 | Cards | `DecisionOffsetYRebased` | Boolean | `true` | — | Erweitert ▸ Karten & Fächer | Toggle | — | 2 | Internal marker / Interne Marke | **DELETE** |
| 112 | Cards | `DecisionOffset_Bronze` | Vector3 | `{"x":1.000000013351432e-10,"y":0.08500000834465027,"z":0.0}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Decision dock: position / Entscheidung: Position |  |
| 113 | Cards | `DecisionOffset_Oak` | Vector3 | `{"x":1.1175870645585562e-10,"y":0.08800000697374344,"z":1.000000013351432e-10}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Decision dock: position / Entscheidung: Position |  |
| 114 | Cards | `DecisionOffset_Steel` | Vector3 | `{"x":0.0,"y":0.10500001907348633,"z":0.0}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Decision dock: position / Entscheidung: Position |  |
| 115 | Cards | `DecisionScale_Bronze` | Single | `1.6` | 0.25 … 3 | Erweitert ▸ Steuerbrett (pro Brett) | Slider | 0.05 | stufenlos | Decision dock: size / Entscheidungsdock: Größe |  |
| 116 | Cards | `DecisionScale_Oak` | Single | `1.6` | 0.25 … 3 | Erweitert ▸ Steuerbrett (pro Brett) | Slider | 0.05 | stufenlos | Decision dock: size / Entscheidungsdock: Größe |  |
| 117 | Cards | `DecisionScale_Steel` | Single | `1.6` | 0.25 … 3 | Erweitert ▸ Steuerbrett (pro Brett) | Slider | 0.05 | stufenlos | Decision dock: size / Entscheidungsdock: Größe |  |
| 118 | Cards | `DevFakeHand` | Int32 | `0` | — | Erweitert ▸ Karten & Fächer | Stepper | 1 | unbegrenzt | Debug: test cards (n) / Debug: Testkarten (n) | **DELETE** |
| 119 | Cards | `ElementsOffset_Bronze` | Vector3 | `{"x":0.0,"y":0.0,"z":1.999999943436137e-9}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Elements: position / Elemente: Position |  |
| 120 | Cards | `ElementsOffset_Oak` | Vector3 | `{"x":0.0,"y":0.0,"z":0.0}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Elements: position / Elemente: Position |  |
| 121 | Cards | `ElementsOffset_Steel` | Vector3 | `{"x":0.0,"y":0.0,"z":-0.03999999910593033}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Elements: position / Elemente: Position |  |
| 122 | Cards | `ElementsScale_Bronze` | Single | `1` | 0.25 … 3 | Erweitert ▸ Steuerbrett (pro Brett) | Slider | 0.05 | stufenlos | Elements: size / Elemente: Größe |  |
| 123 | Cards | `ElementsScale_Oak` | Single | `1` | 0.25 … 3 | Erweitert ▸ Steuerbrett (pro Brett) | Slider | 0.05 | stufenlos | Elements: size / Elemente: Größe |  |
| 124 | Cards | `ElementsScale_Steel` | Single | `1` | 0.25 … 3 | Erweitert ▸ Steuerbrett (pro Brett) | Slider | 0.05 | stufenlos | Elements: size / Elemente: Größe |  |
| 125 | Cards | `FaceMipBake` | Boolean | `true` | — | Erweitert ▸ Karten & Fächer | Toggle | — | 2 | Smooth card textures / Kartentexturen glätten |  |
| 126 | Cards | `FanArcDegrees` | Single | `70` | — | **not offered** (retired marker) | Stepper | 1 | unbegrenzt | _(kein Name — Menü zeigt den englischen Key)_ | retired |
| 127 | Cards | `FanArcSweepDegrees` | Single | `103` | 20 … 180 | Erweitert ▸ Karten & Fächer | Slider | 2 | stufenlos | Fan: total arc (°) / Fächer: Gesamtbogen (°) |  |
| 128 | Cards | `FanCloseDuration` | Single | `0.12` | 0 … 0.4 | Erweitert ▸ Karten & Fächer | Slider | 0.01 | stufenlos | Fan: close time (s) / Fächer: Schließdauer (s) |  |
| 129 | Cards | `FanCurveByFill` | Boolean | `true` | — | Erweitert ▸ Karten & Fächer | Toggle | — | 2 | Fan: curve by hand size / Fächer: Bogen je Anzahl |  |
| 130 | Cards | `FanCurveMinCards` | Int32 | `3` | 1 … 12 | Erweitert ▸ Karten & Fächer | Slider | 1 | 12 (ganzzahlig) | Fan: flat up to N cards / Fächer: flach bis N |  |
| 131 | Cards | `FanCurvePower` | Single | `2` | 0.5 … 4 | Erweitert ▸ Karten & Fächer | Slider | 0.05 | stufenlos | Fan: bow exponent / Fächer: Bogen-Exponent |  |
| 132 | Cards | `FanEffectiveRadius` | Single | `0.2192` | 0.05 … 0.4 | Erweitert ▸ Karten & Fächer | Slider | 0.01 | stufenlos | Fan: radius (m) / Fächer: Radius (m) |  |
| 133 | Cards | `FanFaceViewer` | Single | `1` | 0 … 1 | Erweitert ▸ Karten & Fächer | Slider | 0.02 | stufenlos | Fan: cards face you / Fächer: Karten zu dir |  |
| 134 | Cards | `FanFlatCurvatureFactor` | Single | `0.55` | — | Erweitert ▸ Karten & Fächer | Stepper | 0.05 | unbegrenzt | Fan: arch factor / Fächer: Bogenfaktor |  |
| 135 | Cards | `FanFollowDeadzone` | Single | `0.004` | — | Erweitert ▸ Karten & Fächer | Stepper | 0.001 | unbegrenzt | Fan: follow deadzone (m) / Fächer: Totzone (m) |  |
| 136 | Cards | `FanFollowSmoothing` | Single | `16` | — | Erweitert ▸ Karten & Fächer | Stepper | 0.2 | unbegrenzt | Fan: follow smoothing / Fächer: Folge-Glättung |  |
| 137 | Cards | `FanGazeApexFollow` | Single | `1` | 0 … 1 | Erweitert ▸ Karten & Fächer | Slider | 0.02 | stufenlos | Fan: gaze lifts bow / Fächer: Blick hebt Bogen |  |
| 138 | Cards | `FanGazeBias` | Boolean | `false` | — | Erweitert ▸ Karten & Fächer | Toggle | — | 2 | Fan: gaze-follow yaw / Fächer: Blick-Drehung |  |
| 139 | Cards | `FanGazeSmoothing` | Single | `6` | 1 … 30 | Erweitert ▸ Karten & Fächer | Slider | 0.5 | stufenlos | Fan: gaze smoothing / Fächer: Blick-Glättung |  |
| 140 | Cards | `FanHideSound` | String | `PlaySound_UICardTabSelect` | — | Erweitert ▸ Karten & Fächer | read-only text | — | — | Sound: fan closes / Klang: Fächer schließen | widget: Auswahlliste |
| 141 | Cards | `FanHoverSplitScale` | Single | `1.4` | 0.5 … 3 | Erweitert ▸ Karten & Fächer | Slider | 0.05 | stufenlos | Fan: hover gap scale / Fächer: Hover-Lücke |  |
| 142 | Cards | `FanMaxHandForCurve` | Int32 | `10` | — | Erweitert ▸ Karten & Fächer | Stepper | 1 | unbegrenzt | Fan: full curve at N / Fächer: Bogen voll ab N |  |
| 143 | Cards | `FanOpenDuration` | Single | `0.14` | 0 … 0.6 | Erweitert ▸ Karten & Fächer | Slider | 0.01 | stufenlos | Fan: open time (s) / Fächer: Öffnen (s) |  |
| 144 | Cards | `FanOpenStagger` | Single | `0.02` | 0 … 0.08 | Erweitert ▸ Karten & Fächer | Slider | 0.002 | stufenlos | Fan: per-card delay (s) / Fächer: Kartenverzug (s) |  |
| 145 | Cards | `FanPalmOffset` | Single | `0.09` | — | Erweitert ▸ Karten & Fächer | Stepper | 0.01 | unbegrenzt | Fan: above palm (m) / Fächer: über Hand (m) |  |
| 146 | Cards | `FanPerCardStepDegrees` | Single | `14` | 2 … 40 | Erweitert ▸ Karten & Fächer | Slider | 1 | stufenlos | Fan: step per card (°) / Fächer: Winkel/Karte (°) |  |
| 147 | Cards | `FanRadius` | Single | `0.16` | 0.05 … 0.5 | Erweitert ▸ Karten & Fächer | Slider | 0.01 | stufenlos | Fan: radius (m) / Fächer: Radius (m) |  |
| 148 | Cards | `FanRadiusFactor_Burnt` | Single | `1.7` | 0.5 … 4 | Erweitert ▸ Karten & Fächer | Slider | 0.05 | stufenlos | Pile fan: radius (x) / Stapel: Radius (x) |  |
| 149 | Cards | `FanRadiusFactor_Discard` | Single | `1.7` | 0.5 … 4 | Erweitert ▸ Karten & Fächer | Slider | 0.05 | stufenlos | Pile fan: radius (x) / Stapel: Radius (x) |  |
| 150 | Cards | `FanRadiusFactor_Items` | Single | `1.7` | 0.5 … 4 | Erweitert ▸ Karten & Fächer | Slider | 0.05 | stufenlos | Pile fan: radius (x) / Stapel: Radius (x) |  |
| 151 | Cards | `FanRevealSound` | String | `PlaySound_EnemyCardDraw` | — | Erweitert ▸ Karten & Fächer | read-only text | — | — | Sound: fan opens / Klang: Fächer öffnen | widget: Auswahlliste |
| 152 | Cards | `FanSelectedPopForward` | Single | `0.035` | — | Erweitert ▸ Karten & Fächer | Stepper | 0.01 | unbegrenzt | Fan: card pop-out (m) / Fächer: Karte hervor (m) |  |
| 153 | Cards | `FanSideDepthCurve` | Single | `0` | -0.12 … 0.12 | Erweitert ▸ Karten & Fächer | Slider | 0.005 | stufenlos | Fan: depth bow (m) / Fächer: Tiefenbogen (m) |  |
| 154 | Cards | `FanSplitFalloff` | Single | `1.6` | — | Erweitert ▸ Karten & Fächer | Stepper | 0.05 | unbegrenzt | Fan: gap falloff / Fächer: Lücken-Abklang |  |
| 155 | Cards | `FanSplitMultiplier` | Single | `0.02` | — | Erweitert ▸ Karten & Fächer | Stepper | 0.005 | unbegrenzt | Fan: gap width (m) / Fächer: Lückenbreite (m) |  |
| 156 | Cards | `FanStepDegrees_Burnt` | Single | `10` | 2 … 30 | Erweitert ▸ Karten & Fächer | Slider | 1 | stufenlos | Pile fan: spread (°) / Stapel: Spreizung (°) |  |
| 157 | Cards | `FanStepDegrees_Discard` | Single | `10` | 2 … 30 | Erweitert ▸ Karten & Fächer | Slider | 1 | stufenlos | Pile fan: spread (°) / Stapel: Spreizung (°) |  |
| 158 | Cards | `FanStepDegrees_Items` | Single | `10` | 2 … 30 | Erweitert ▸ Karten & Fächer | Slider | 1 | stufenlos | Pile fan: spread (°) / Stapel: Spreizung (°) |  |
| 159 | Cards | `FanSwapArc` | Single | `0.06` | 0 … 0.25 | Erweitert ▸ Karten & Fächer | Slider | 0.005 | stufenlos | Swap: depth bow (m) / Tausch: Tiefenbogen (m) |  |
| 160 | Cards | `FanSwapDuration` | Single | `0.24` | 0.05 … 0.8 | Erweitert ▸ Karten & Fächer | Slider | 0.02 | stufenlos | Swap: flight time (s) / Tausch: Flugdauer (s) |  |
| 161 | Cards | `FanSwapOverlap` | Single | `0.66` | 0 … 1 | Erweitert ▸ Karten & Fächer | Slider | 0.02 | stufenlos | Swap: overlap / Tausch: Überlappung |  |
| 162 | Cards | `FanSwapSeedScale` | Single | `0.16` | 0.02 … 1 | Erweitert ▸ Karten & Fächer | Slider | 0.05 | stufenlos | Swap: start size / Tausch: Startgröße |  |
| 163 | Cards | `FanSwapSettleOvershoot` | Single | `1.5` | 0 … 3 | Erweitert ▸ Karten & Fächer | Slider | 0.05 | stufenlos | Swap: settle overshoot / Tausch: Nachschwingen |  |
| 164 | Cards | `FanSwapSpinDegrees` | Single | `58` | 0 … 180 | Erweitert ▸ Karten & Fächer | Slider | 5 | stufenlos | Swap: counter-roll (°) / Tausch: Gegendrehung (°) |  |
| 165 | Cards | `FanSwapStagger` | Single | `0.024` | 0 … 0.12 | Erweitert ▸ Karten & Fächer | Slider | 0.002 | stufenlos | Swap: wipe delay (s) / Tausch: Wischverzug (s) |  |
| 166 | Cards | `FanSwapTravel` | Single | `0.075` | 0 … 0.3 | Erweitert ▸ Karten & Fächer | Slider | 0.01 | stufenlos | Swap: gather point (m) / Tausch: Sammelpunkt (m) |  |
| 167 | Cards | `FanTiltFactor` | Single | `0.85` | — | Erweitert ▸ Karten & Fächer | Stepper | 0.05 | unbegrenzt | Fan: card tilt factor / Fächer: Kartenneigung |  |
| 168 | Cards | `GameCardParticles` | Boolean | `false` | — | Erweitert ▸ Karten & Fächer | Toggle | — | 2 | Game card particles / Karten-Partikel (Spiel) |  |
| 169 | Cards | `GenericButtonShape_Bronze` | ButtonShape | `Square` | — | Erweitert ▸ Steuerbrett (pro Brett) | Dropdown (enum) | — | Liste | Confirm/Undo: shape / Bestätigen/Zurück: Form |  |
| 170 | Cards | `GenericButtonShape_Oak` | ButtonShape | `Square` | — | Erweitert ▸ Steuerbrett (pro Brett) | Dropdown (enum) | — | Liste | Confirm/Undo: shape / Bestätigen/Zurück: Form |  |
| 171 | Cards | `GenericButtonShape_Steel` | ButtonShape | `Square` | — | Erweitert ▸ Steuerbrett (pro Brett) | Dropdown (enum) | — | Liste | Confirm/Undo: shape / Bestätigen/Zurück: Form |  |
| 172 | Cards | `GenericButtonSpacing_Bronze` | Single | `0.06` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 0.01 | unbegrenzt | Confirm/Undo: gap (m) / Best./Zurück: Abstand |  |
| 173 | Cards | `GenericButtonSpacing_Oak` | Single | `-0.008` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 0.01 | unbegrenzt | Confirm/Undo: gap (m) / Best./Zurück: Abstand |  |
| 174 | Cards | `GenericButtonSpacing_Steel` | Single | `0.01` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 0.01 | unbegrenzt | Confirm/Undo: gap (m) / Best./Zurück: Abstand |  |
| 175 | Cards | `HeldFaceBias` | Single | `65` | — | Erweitert ▸ Karten & Fächer | Stepper | 1 | unbegrenzt | Held card: face tilt (°) / Handkarte: Winkel (°) |  |
| 176 | Cards | `HeldForward` | Single | `0.005` | — | Erweitert ▸ Karten & Fächer | Stepper | 0.01 | unbegrenzt | Held (fallback): fwd (m) / Ersatzkarte: vor (m) |  |
| 177 | Cards | `HeldOffPalm` | Single | `0.0148` | — | Erweitert ▸ Karten & Fächer | Stepper | 0.0002 | unbegrenzt | Held (fallback): gap (m) / Ersatzkarte: Abstand (m) |  |
| 178 | Cards | `HeldPinchOffset` | Vector3 | `{"x":-0.054999999701976779,"y":0.03500000014901161,"z":0.0}` | — | Erweitert ▸ Karten & Fächer | Stepper x3 | 0.005 | unbegrenzt | Held card: pinch offset / Karte: Griffversatz |  |
| 179 | Cards | `HeldTiltDegrees` | Single | `20` | — | **not offered** (retired marker) | Stepper | 1 | unbegrenzt | _(kein Name — Menü zeigt den englischen Key)_ | retired |
| 180 | Cards | `HoverHintOffset_Bronze` | Vector3 | `{"x":0.0,"y":0.0,"z":0.0}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Tooltip area: position / Tooltip-Bereich: Position |  |
| 181 | Cards | `HoverHintOffset_Oak` | Vector3 | `{"x":0.11999999731779099,"y":1.999999943436137e-9,"z":0.0}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Tooltip area: position / Tooltip-Bereich: Position |  |
| 182 | Cards | `HoverHintOffset_Steel` | Vector3 | `{"x":0.0,"y":0.0,"z":0.0}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Tooltip area: position / Tooltip-Bereich: Position |  |
| 183 | Cards | `InitiativeOffset_Bronze` | Vector3 | `{"x":0.0,"y":0.20000000298023225,"z":-1.999999943436137e-9}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Initiative: position / Initiative: Position |  |
| 184 | Cards | `InitiativeOffset_Oak` | Vector3 | `{"x":0.0,"y":0.17000000178813935,"z":-0.04800000041723251}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Initiative: position / Initiative: Position |  |
| 185 | Cards | `InitiativeOffset_Steel` | Vector3 | `{"x":0.0,"y":0.20000000298023225,"z":-0.07000000029802323}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Initiative: position / Initiative: Position |  |
| 186 | Cards | `InspectScale` | Single | `1.6` | 0.5 … 4 | kuratiert: Brett & Karten ▸ Karten | Slider | 0.05 | stufenlos | Close-up: size / Nahansicht: Größe | widget: Bar+Pfeile |
| 187 | Cards | `ItemBerthGlow` | Single | `0.34` | 0 … 1 | Erweitert ▸ Karten & Fächer | Slider | 0.02 | stufenlos | Use berth: inner glow / Ablage: Innenleuchten |  |
| 188 | Cards | `ItemBerthPingReach` | Single | `1.5` | 1 … 3 | Erweitert ▸ Karten & Fächer | Slider | 0.05 | stufenlos | Use berth: ping reach / Ablage: Ping-Weite |  |
| 189 | Cards | `ItemBerthPingSeconds` | Single | `1.5` | 0 … 4 | Erweitert ▸ Karten & Fächer | Slider | 0.1 | stufenlos | Use berth: ping (s) / Ablage: Ping (s) |  |
| 190 | Cards | `ItemBerthRevealSeconds` | Single | `0.26` | 0.05 … 1 | Erweitert ▸ Karten & Fächer | Slider | 0.05 | stufenlos | Use berth: appear (s) / Ablage: Einblenden (s) |  |
| 191 | Cards | `ItemBerthRingThickness` | Single | `0.0042` | 0.001 … 0.02 | Erweitert ▸ Karten & Fächer | Slider | 0.001 | stufenlos | Use berth: outline (m) / Ablage: Umriss (m) |  |
| 192 | Cards | `ItemCardOffset_Bronze` | Vector3 | `{"x":0.0,"y":0.0,"z":0.0}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Item cards: position / Item-Karten: Position |  |
| 193 | Cards | `ItemCardOffset_Oak` | Vector3 | `{"x":0.0,"y":0.0,"z":0.0}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Item cards: position / Item-Karten: Position |  |
| 194 | Cards | `ItemCardOffset_Steel` | Vector3 | `{"x":0.0,"y":1.1175870645585562e-10,"z":0.0}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Item cards: position / Item-Karten: Position |  |
| 195 | Cards | `ItemCueBeatSeconds` | Single | `1.25` | 0.4 … 4 | Erweitert ▸ Karten & Fächer | Slider | 0.05 | stufenlos | Item cue: heartbeat (s) / Gegenstands-Hinweis: Herzschlag (s) |  |
| 196 | Cards | `ItemCueEmberRate` | Single | `22` | 0 … 60 | Erweitert ▸ Karten & Fächer | Slider | 1 | stufenlos | Item cue: embers/s / Gegenstands-Hinweis: Funken/s |  |
| 197 | Cards | `ItemCueEmberSize` | Single | `2.1` | 0.5 … 5 | Erweitert ▸ Karten & Fächer | Slider | 0.1 | stufenlos | Item cue: ember size / Gegenstands-Hinweis: Funkengröße |  |
| 198 | Cards | `ItemCueRingAlpha` | Single | `0.95` | 0 … 1 | Erweitert ▸ Karten & Fächer | Slider | 0.05 | stufenlos | Item cue: ring opacity / Gegenstands-Hinweis: Ring-Deckkraft |  |
| 199 | Cards | `ItemCueRingReach` | Single | `2.3` | 1 … 5 | Erweitert ▸ Karten & Fächer | Slider | 0.1 | stufenlos | Item cue: ring reach / Gegenstands-Hinweis: Ringweite |  |
| 200 | Cards | `ItemFanCloseDuration` | Single | `0.3` | 0.05 … 0.9 | Erweitert ▸ Karten & Fächer | Slider | 0.02 | stufenlos | Items: close time (s) / Gegenstände: Schließdauer (s) |  |
| 201 | Cards | `ItemFanCloseStagger` | Single | `0.032` | 0 … 0.2 | Erweitert ▸ Karten & Fächer | Slider | 0.005 | stufenlos | Items: close delay (s) / Gegenstände: Schließverzug (s) |  |
| 202 | Cards | `ItemFanOpenArc` | Single | `0.06` | 0 … 0.2 | Erweitert ▸ Karten & Fächer | Slider | 0.005 | stufenlos | Items: flight bow (m) / Gegenstände: Flugbogen (m) |  |
| 203 | Cards | `ItemFanOpenDuration` | Single | `0.34` | 0.05 … 0.9 | Erweitert ▸ Karten & Fächer | Slider | 0.02 | stufenlos | Items: open time (s) / Gegenstände: Öffnen (s) |  |
| 204 | Cards | `ItemFanOpenSpinDegrees` | Single | `52` | 0 … 180 | Erweitert ▸ Karten & Fächer | Slider | 5 | stufenlos | Items: unfold roll (°) / Gegenstände: Aufklapp-Drehung (°) |  |
| 205 | Cards | `ItemFanOpenStagger` | Single | `0.055` | 0 … 0.2 | Erweitert ▸ Karten & Fächer | Slider | 0.005 | stufenlos | Items: deal delay (s) / Gegenstände: Kartenverzug (s) |  |
| 206 | Cards | `ItemFanSeedScale` | Single | `0.12` | 0.02 … 1 | Erweitert ▸ Karten & Fächer | Slider | 0.02 | stufenlos | Items: start size / Gegenstände: Startgröße |  |
| 207 | Cards | `ItemFanSettleOvershoot` | Single | `1.4` | 0 … 3 | Erweitert ▸ Karten & Fächer | Slider | 0.05 | stufenlos | Items: settle overshoot / Gegenstände: Nachschwingen |  |
| 208 | Cards | `ItemUseSlotOffset_Bronze` | Vector3 | `{"x":0.014999999664723874,"y":-0.044999998062849048,"z":0.0}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Item-use slot: position / Item-Slot: Position |  |
| 209 | Cards | `ItemUseSlotOffset_Oak` | Vector3 | `{"x":0.004999999888241291,"y":-0.03500000014901161,"z":0.0}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Item-use slot: position / Item-Slot: Position |  |
| 210 | Cards | `ItemUseSlotOffset_Steel` | Vector3 | `{"x":0.014999999664723874,"y":-0.04999999701976776,"z":0.0}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Item-use slot: position / Item-Slot: Position |  |
| 211 | Cards | `ObjectivesOffset_Bronze` | Vector3 | `{"x":0.0,"y":0.03200000151991844,"z":-0.007000000216066837}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Objectives: position / Aufgaben: Position |  |
| 212 | Cards | `ObjectivesOffset_Oak` | Vector3 | `{"x":0.0,"y":0.026000000536441804,"z":0.0}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Objectives: position / Aufgaben: Position |  |
| 213 | Cards | `ObjectivesOffset_Steel` | Vector3 | `{"x":0.0,"y":0.03200000151991844,"z":-0.041999999433755878}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Objectives: position / Aufgaben: Position |  |
| 214 | Cards | `ObjectivesScale_Bronze` | Single | `0.95` | 0.25 … 3 | Erweitert ▸ Steuerbrett (pro Brett) | Slider | 0.05 | stufenlos | Objectives: size / Aufgaben: Größe |  |
| 215 | Cards | `ObjectivesScale_Oak` | Single | `0.95` | 0.25 … 3 | Erweitert ▸ Steuerbrett (pro Brett) | Slider | 0.05 | stufenlos | Objectives: size / Aufgaben: Größe |  |
| 216 | Cards | `ObjectivesScale_Steel` | Single | `0.95` | 0.25 … 3 | Erweitert ▸ Steuerbrett (pro Brett) | Slider | 0.05 | stufenlos | Objectives: size / Aufgaben: Größe |  |
| 217 | Cards | `ObjectivesWidth_Bronze` | Single | `0.8` | 0.5 … 3 | Erweitert ▸ Steuerbrett (pro Brett) | Slider | 0.05 | stufenlos | Objectives: width / Aufgaben: Breite |  |
| 218 | Cards | `ObjectivesWidth_Oak` | Single | `0.8` | 0.5 … 3 | Erweitert ▸ Steuerbrett (pro Brett) | Slider | 0.05 | stufenlos | Objectives: width / Aufgaben: Breite |  |
| 219 | Cards | `ObjectivesWidth_Steel` | Single | `0.8` | 0.5 … 3 | Erweitert ▸ Steuerbrett (pro Brett) | Slider | 0.05 | stufenlos | Objectives: width / Aufgaben: Breite |  |
| 220 | Cards | `PickBannerOffset_Bronze` | Vector3 | `{"x":1.000000013351432e-10,"y":0.05999999865889549,"z":0.0}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Status placard: position / Statustafel: Position |  |
| 221 | Cards | `PickBannerOffset_Oak` | Vector3 | `{"x":1.000000013351432e-10,"y":0.019999999552965165,"z":0.0}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Status placard: position / Statustafel: Position |  |
| 222 | Cards | `PickBannerOffset_Steel` | Vector3 | `{"x":0.0,"y":0.0949999988079071,"z":0.0}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Status placard: position / Statustafel: Position |  |
| 223 | Cards | `PileOffset_Bronze` | Vector3 | `{"x":0.0,"y":0.08000000566244126,"z":1.999999943436137e-9}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Piles: position / Stapel: Position |  |
| 224 | Cards | `PileOffset_Oak` | Vector3 | `{"x":0.0,"y":0.0,"z":0.0}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Piles: position / Stapel: Position |  |
| 225 | Cards | `PileOffset_Steel` | Vector3 | `{"x":0.0,"y":0.0,"z":-0.03999999910593033}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Piles: position / Stapel: Position |  |
| 226 | Cards | `PileScale_Bronze` | Single | `1` | 0.25 … 3 | Erweitert ▸ Steuerbrett (pro Brett) | Slider | 0.05 | stufenlos | Piles: size / Stapel: Größe |  |
| 227 | Cards | `PileScale_Oak` | Single | `1` | 0.25 … 3 | Erweitert ▸ Steuerbrett (pro Brett) | Slider | 0.05 | stufenlos | Piles: size / Stapel: Größe |  |
| 228 | Cards | `PileScale_Steel` | Single | `1` | 0.25 … 3 | Erweitert ▸ Steuerbrett (pro Brett) | Slider | 0.05 | stufenlos | Piles: size / Stapel: Größe |  |
| 229 | Cards | `PileSpacing_Bronze` | Single | `0.116` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 0.01 | unbegrenzt | Piles: gap (m) / Stapel: Abstand (m) |  |
| 230 | Cards | `PileSpacing_Oak` | Single | `0.116` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 0.01 | unbegrenzt | Piles: gap (m) / Stapel: Abstand (m) |  |
| 231 | Cards | `PileSpacing_Steel` | Single | `0.116` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 0.01 | unbegrenzt | Piles: gap (m) / Stapel: Abstand (m) |  |
| 232 | Cards | `PinOffset_Bronze` | Vector3 | `{"x":-0.019999999552965165,"y":-0.02199999988079071,"z":0.0}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Pin button: position / Fixier-Taste: Position |  |
| 233 | Cards | `PinOffset_Oak` | Vector3 | `{"x":1.1175870645585562e-10,"y":0.0,"z":0.0}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Pin button: position / Fixier-Taste: Position |  |
| 234 | Cards | `PinOffset_Steel` | Vector3 | `{"x":-0.019999999552965165,"y":-0.02199999988079071,"z":0.0}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Pin button: position / Fixier-Taste: Position |  |
| 235 | Cards | `ReadoutOffset_Bronze` | Vector3 | `{"x":-0.03999999910593033,"y":0.02199999988079071,"z":-0.004000000189989805}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Round readout: position / Runden-Anzeige: Position |  |
| 236 | Cards | `ReadoutOffset_Oak` | Vector3 | `{"x":0.00800000037997961,"y":-0.004000000189989805,"z":-0.024000000208616258}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Round readout: position / Runden-Anzeige: Position |  |
| 237 | Cards | `ReadoutOffset_Steel` | Vector3 | `{"x":-0.03999999910593033,"y":0.02199999988079071,"z":-0.04399999976158142}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Round readout: position / Runden-Anzeige: Position |  |
| 238 | Cards | `RestButtonDiameter_Bronze` | Single | `0.071` | 0.02 … 0.25 | Erweitert ▸ Steuerbrett (pro Brett) | Slider | 0.01 | stufenlos | Rest buttons: size (m) / Rast-Tasten: Größe (m) |  |
| 239 | Cards | `RestButtonDiameter_Oak` | Single | `0.091` | 0.02 … 0.25 | Erweitert ▸ Steuerbrett (pro Brett) | Slider | 0.01 | stufenlos | Rest buttons: size (m) / Rast-Tasten: Größe (m) |  |
| 240 | Cards | `RestButtonDiameter_Steel` | Single | `0.071` | 0.02 … 0.25 | Erweitert ▸ Steuerbrett (pro Brett) | Slider | 0.01 | stufenlos | Rest buttons: size (m) / Rast-Tasten: Größe (m) |  |
| 241 | Cards | `RestButtonInsetX` | Single | `0.024` | — | **not offered** (retired marker) | Stepper | 0.01 | unbegrenzt | _(kein Name — Menü zeigt den englischen Key)_ | retired |
| 242 | Cards | `RestButtonOffset_Bronze` | Vector3 | `{"x":-0.4449999928474426,"y":0.019999999552965165,"z":-0.016999999061226846}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Rest buttons: position / Rast-Tasten: Position |  |
| 243 | Cards | `RestButtonOffset_Oak` | Vector3 | `{"x":0.00800000037997961,"y":0.0,"z":-0.007000000216066837}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Rest buttons: position / Rast-Tasten: Position |  |
| 244 | Cards | `RestButtonOffset_Steel` | Vector3 | `{"x":-0.4399999976158142,"y":0.0,"z":-0.04699999839067459}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Rest buttons: position / Rast-Tasten: Position |  |
| 245 | Cards | `RestButtonShape_Bronze` | ButtonShape | `Round` | — | Erweitert ▸ Steuerbrett (pro Brett) | Dropdown (enum) | — | Liste | Rest buttons: shape / Rast-Tasten: Form |  |
| 246 | Cards | `RestButtonShape_Oak` | ButtonShape | `Round` | — | Erweitert ▸ Steuerbrett (pro Brett) | Dropdown (enum) | — | Liste | Rest buttons: shape / Rast-Tasten: Form |  |
| 247 | Cards | `RestButtonShape_Steel` | ButtonShape | `Round` | — | Erweitert ▸ Steuerbrett (pro Brett) | Dropdown (enum) | — | Liste | Rest buttons: shape / Rast-Tasten: Form |  |
| 248 | Cards | `RestButtonSpacing_Bronze` | Single | `0.026` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 0.01 | unbegrenzt | Rest buttons: gap (m) / Rast-Tasten: Abstand (m) |  |
| 249 | Cards | `RestButtonSpacing_Oak` | Single | `0` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 0.01 | unbegrenzt | Rest buttons: gap (m) / Rast-Tasten: Abstand (m) |  |
| 250 | Cards | `RestButtonSpacing_Steel` | Single | `-0.044` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 0.01 | unbegrenzt | Rest buttons: gap (m) / Rast-Tasten: Abstand (m) |  |
| 251 | Cards | `RevealEnterDegrees` | Single | `70` | 15 … 85 | Erweitert ▸ Karten & Fächer | Slider | 1 | stufenlos | Fan opens at roll (°) / Fächer öffnen ab (°) |  |
| 252 | Cards | `RevealExitDegrees` | Single | `5` | 5 … 80 | Erweitert ▸ Karten & Fächer | Slider | 1 | stufenlos | Fan closes at roll (°) / Fächer schließen ab (°) |  |
| 253 | Cards | `RevealIgnoreWhenGrabbing` | Boolean | `true` | — | Erweitert ▸ Karten & Fächer | Toggle | — | 2 | Fan: not while grabbing / Fächer: nicht im Griff |  |
| 254 | Cards | `RevealMode` | String | `tilt` | — | kuratiert: Brett & Karten ▸ Karten | Dropdown (curated) | — | Liste | Fan opens by / Fächer öffnen |  |
| 255 | Cards | `RoundButtonDiameter` | Single | `0.105` | — | **not offered** (retired marker) | Stepper | 0.01 | unbegrenzt | _(kein Name — Menü zeigt den englischen Key)_ | retired |
| 256 | Cards | `RoundButtonThickness` | Single | `0.012` | — | **not offered** (retired marker) | Stepper | 0.002 | unbegrenzt | _(kein Name — Menü zeigt den englischen Key)_ | retired |
| 257 | Cards | `SlotCardInset` | Single | `0.004` | — | Erweitert ▸ Karten & Fächer | Stepper | 0.001 | unbegrenzt | Slot card: lift out (m) / Slot-Karte: anheben (m) |  |
| 258 | Cards | `SlotOverlayOffset_Bronze` | Vector3 | `{"x":-0.0020000003278255464,"y":0.0029999995604157449,"z":-0.0010000000474974514}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Slot glow: position / Slot-Glühen: Position |  |
| 259 | Cards | `SlotOverlayOffset_Oak` | Vector3 | `{"x":0.0020000000949949028,"y":-0.0020000003278255464,"z":0.004000000189989805}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Slot glow: position / Slot-Glühen: Position |  |
| 260 | Cards | `SlotOverlayOffset_Steel` | Vector3 | `{"x":0.017999999225139619,"y":-0.0020000000949949028,"z":0.004000000189989805}` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper x3 | 0.005 | unbegrenzt | Slot glow: position / Slot-Glühen: Position |  |
| 261 | Cards | `SlotOverlayScale_Bronze` | Single | `1.65` | 0.25 … 3 | Erweitert ▸ Steuerbrett (pro Brett) | Slider | 0.05 | stufenlos | Slot glow + card: size / Slot-Glühen + Karte: Größe |  |
| 262 | Cards | `SlotOverlayScale_Oak` | Single | `1.9` | 0.25 … 3 | Erweitert ▸ Steuerbrett (pro Brett) | Slider | 0.05 | stufenlos | Slot glow + card: size / Slot-Glühen + Karte: Größe |  |
| 263 | Cards | `SlotOverlayScale_Steel` | Single | `1.9` | 0.25 … 3 | Erweitert ▸ Steuerbrett (pro Brett) | Slider | 0.05 | stufenlos | Slot glow + card: size / Slot-Glühen + Karte: Größe |  |
| 264 | Cards | `SlotOverlaySpacing_Bronze` | Single | `-0.007999999` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 0.01 | unbegrenzt | Slot glow: spacing (m) / Slot-Glühen: Abstand (m) |  |
| 265 | Cards | `SlotOverlaySpacing_Oak` | Single | `-0.008` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 0.01 | unbegrenzt | Slot glow: spacing (m) / Slot-Glühen: Abstand (m) |  |
| 266 | Cards | `SlotOverlaySpacing_Steel` | Single | `0.002` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 0.01 | unbegrenzt | Slot glow: spacing (m) / Slot-Glühen: Abstand (m) |  |
| 267 | Cards | `SpawnDownMeters` | Single | `0.32` | -0.5 … 1.5 | Erweitert ▸ Karten & Fächer | Slider | 0.01 | stufenlos | Board start: down (m) / Brett-Start: runter (m) |  |
| 268 | Cards | `SpawnForwardMeters` | Single | `0.28` | -0.5 … 1.5 | Erweitert ▸ Karten & Fächer | Slider | 0.01 | stufenlos | Board start: forward (m) / Brett-Start: vor (m) |  |
| 269 | Cards | `SpawnLeftOfHead` | Boolean | `true` | — | kuratiert: Brett & Karten ▸ Kontrollbrett | Toggle | — | 2 | Board starts on the left / Brett startet links |  |
| 270 | Cards | `SpawnSideMeters` | Single | `0.45` | 0 … 1.2 | Erweitert ▸ Karten & Fächer | Slider | 0.01 | stufenlos | Board start: left (m) / Brett-Start: links (m) |  |
| 271 | Cards | `TrayDown` | Single | `0.1451879` | — | Erweitert ▸ Karten & Fächer | Stepper | 0.01 | unbegrenzt | Board spawn: lower (m) / Brett-Start: tiefer (m) |  |
| 272 | Cards | `TrayFollow` | Boolean | `false` | — | kuratiert: Brett & Karten ▸ Kontrollbrett | Toggle | — | 2 | Board: follows you / Brett: folgt dir |  |
| 273 | Cards | `TrayForward` | Single | `0.6441641` | — | Erweitert ▸ Karten & Fächer | Stepper | 0.01 | unbegrenzt | Board spawn: forward (m) / Brett-Start: vor (m) |  |
| 274 | Cards | `TrayPitch` | Single | `45.13076` | — | Erweitert ▸ Karten & Fächer | Stepper | 1 | unbegrenzt | Board pitch (grab, °) / Brett-Neigung (Griff, °) |  |
| 275 | Cards | `TrayRight` | Single | `0.4250352` | — | Erweitert ▸ Karten & Fächer | Stepper | 0.01 | unbegrenzt | Board spawn: right (m) / Brett-Start: rechts (m) |  |
| 276 | Cards | `TrayScale` | Single | `2` | — | kuratiert: Brett & Karten ▸ Kontrollbrett | Stepper | 0.05 | unbegrenzt | Board: size / Brett: Größe |  |
| 277 | Cards | `TrayTilt` | Single | `30` | — | **not offered** (retired marker) | Stepper | 1 | unbegrenzt | _(kein Name — Menü zeigt den englischen Key)_ | retired |
| 278 | Cards | `TrayYaw` | Single | `27.56061` | — | Erweitert ▸ Karten & Fächer | Stepper | 1 | unbegrenzt | Board: yaw (°) / Brett: Drehung (°) |  |
| 279 | Cards | `WantedSlotHint` | Boolean | `true` | — | kuratiert: Brett & Karten ▸ Stapel & Hinweise | Toggle | — | 2 | Glow on expected slot / Erwarteter Slot leuchtet |  |
| 280 | Comfort | `DebugGizmos` | Boolean | `false` | — | Erweitert ▸ Bewegung & Welt | Toggle | — | 2 | Comfort debug overlay / Komfort-Debug-Overlay | **DELETE** |
| 281 | Comfort | `FlightDirection` | FlightDirectionSource | `Hand` | — | kuratiert: Komfort ▸ Fortbewegung | Dropdown (enum) | — | Liste | _(kein Name — Menü zeigt den englischen Key)_ |  |
| 282 | Comfort | `FlightEnabled` | Boolean | `true` | — | kuratiert: Komfort ▸ Fortbewegung | Toggle | — | 2 | _(kein Name — Menü zeigt den englischen Key)_ |  |
| 283 | Comfort | `FlightHand` | TurnHandChoice | `Right` | — | kuratiert: Komfort ▸ Fortbewegung | Dropdown (enum) | — | Liste | _(kein Name — Menü zeigt den englischen Key)_ |  |
| 284 | Comfort | `FlightMaxSpeed` | Single | `1.43362` | 0.2 … 3 | kuratiert: Komfort ▸ Fortbewegung | Slider | 0.5 | stufenlos | _(kein Name — Menü zeigt den englischen Key)_ | widget: Bar+Pfeile |
| 285 | Comfort | `FreeMovement` | Boolean | `true` | — | kuratiert: Komfort ▸ Fortbewegung | Toggle | — | 2 | Free movement / Freie Bewegung |  |
| 286 | Comfort | `KeepPlaceOnReorigin` | Boolean | `true` | — | kuratiert: Komfort ▸ Welt greifen | Toggle | — | 2 | Keep place on re-don / Platz nach Absetzen |  |
| 287 | Comfort | `RecenterHoldSeconds` | Single | `1` | 0 … 5 | kuratiert: Komfort ▸ Welt greifen | Slider | 0.1 | stufenlos | Recenter: hold time (s) / Zentrieren: Haltedauer (s) | widget: Bar+Pfeile |
| 288 | Comfort | `RotateEnabled` | Boolean | `true` | — | kuratiert: Komfort ▸ Welt greifen | Toggle | — | 2 | Rotate the world / Welt drehen |  |
| 289 | Comfort | `SavedScaleMultiplier` | Single | `1.649897` | — | Erweitert ▸ Bewegung & Welt | Stepper | 0.05 | unbegrenzt | Saved table scale / Gespeicherte Tischgröße | **DELETE** |
| 290 | Comfort | `ScaleEnabled` | Boolean | `true` | — | kuratiert: Komfort ▸ Welt greifen | Toggle | — | 2 | Resize the world / Welt skalieren |  |
| 291 | Comfort | `ScaleMax` | Single | `4` | 1 … 20 | kuratiert: Komfort ▸ Welt greifen | Slider | 0.05 | stufenlos | Zoom-in limit / Zoom-Obergrenze | widget: Bar+Pfeile |
| 292 | Comfort | `ScaleMin` | Single | `0.5` | 0.02 … 1 | kuratiert: Komfort ▸ Welt greifen | Slider | 0.05 | stufenlos | Zoom-out limit / Zoom-Untergrenze | widget: Bar+Pfeile |
| 293 | Comfort | `SmoothTurnSpeed` | Single | `90` | 30 … 270 | kuratiert: Komfort ▸ Drehen | Slider | 10 | stufenlos | Turn speed / Drehgeschwindigkeit | widget: Bar+Pfeile |
| 294 | Comfort | `SnapTurnDegrees` | Single | `45` | 15 … 90 | kuratiert: Komfort ▸ Drehen | Slider | 15 | stufenlos | Snap angle / Sprungwinkel | widget: Auswahl |
| 295 | Comfort | `TableScaleDefault25Applied` | Boolean | `True` | — | Erweitert ▸ Bewegung & Welt | Toggle | — | 2 | Internal marker / Interne Marke | **DELETE** |
| 296 | Comfort | `TurnHand` | TurnHandChoice | `Right` | — | kuratiert: Komfort ▸ Drehen | Dropdown (enum) | — | Liste | Turning hand / Dreh-Hand |  |
| 297 | Comfort | `TurnMode` | TurnMode | `Smooth` | — | kuratiert: Komfort ▸ Drehen | Dropdown (enum) | — | Liste | Turning / Drehen |  |
| 298 | Comfort | `TurnStickVertical` | Boolean | `false` | — | kuratiert: Komfort ▸ Fortbewegung | Toggle | — | 2 | Up/down on turn stick / Hoch/Runter am Drehstick |  |
| 299 | Comfort | `VerticalDrag` | Boolean | `false` | — | kuratiert: Komfort ▸ Welt greifen | Toggle | — | 2 | Drag vertically / Senkrecht ziehen |  |
| 300 | Comfort | `WorldGrabEnabled` | Boolean | `true` | — | kuratiert: Komfort ▸ Welt greifen | Toggle | — | 2 | World grab / Welt greifen |  |
| 301 | Compat | `DisableComponents` | String | `` | — | Erweitert ▸ System & Start | read-only text | — | — | Disabled components / Deaktivierte Teile | **DELETE** |
| 302 | Compat | `DisablePostProcessing` | Boolean | `true` | — | kuratiert: Grafik ▸ Darstellung | Toggle | — | 2 | Disable post-processing / Post-Processing aus | →Erweitert |
| 303 | Compat | `DisableVolumetricFog` | Boolean | `true` | — | kuratiert: Grafik ▸ Darstellung | Toggle | — | 2 | Volumetric fog off / Volumennebel aus | →Erweitert |
| 304 | Compat | `WallFade` | Boolean | `true` | — | kuratiert: Grafik ▸ Darstellung | Toggle | — | 2 | See-through walls / Wände durchsichtig |  |
| 305 | Core | `AutoRestartForGraphicsJobs` | Boolean | `true` | — | kuratiert: Grafik ▸ Leistung | Toggle | — | 2 | Restart automatically / Automatisch neu starten | →Erweitert |
| 306 | Core | `EnableGraphicsJobs` | Boolean | `true` | — | kuratiert: Grafik ▸ Leistung | Toggle | — | 2 | Threaded submission / Parallele Bildabgabe | →Erweitert |
| 307 | Core | `InitDelayFrames` | Int32 | `0` | — | Erweitert ▸ System & Start | Stepper | 1 | unbegrenzt | Start delay (frames) / Startverzug (Frames) | **DELETE** |
| 308 | Core | `RuntimePriority` | String | `auto` | — | Erweitert ▸ System & Start | Dropdown (curated) | — | Liste | Runtime try order / Runtime-Reihenfolge | **DELETE** |
| 309 | Core | `SkipRuntimeCandidates` | Boolean | `false` | — | Erweitert ▸ System & Start | Toggle | — | 2 | Single runtime attempt / Nur Standard-Runtime | **DELETE** |
| 310 | Dev | `Enabled` | Boolean | `false` | — | Erweitert ▸ Messung & Diagnose | Toggle | — | 2 | Developer mode / Entwicklermodus | **DELETE** |
| 311 | Dev | `InputDeviceDumpInterval` | Single | `0` | — | Erweitert ▸ Messung & Diagnose | Stepper | 0.05 | unbegrenzt | XR device log (s, 0=off) / XR-Geräte-Log (s, 0=aus) | **DELETE** |
| 312 | Dev | `Overlay` | Boolean | `true` | — | Erweitert ▸ Messung & Diagnose | Toggle | — | 2 | Dev overlay at start / Dev-Overlay beim Start | **DELETE** |
| 313 | Dev | `SimulateHands` | Boolean | `false` | — | Erweitert ▸ Messung & Diagnose | Toggle | — | 2 | Simulate hands (desktop) / Hände simulieren (PC) | **DELETE** |
| 314 | Elements | `EnvironmentResponse` | Boolean | `true` | — | kuratiert: Grafik ▸ Darstellung | Toggle | — | 2 | Elements affect surroundings / Elemente wirken auf Umgebung |  |
| 315 | Elements | `ResponseStrength` | Single | `1` | 0 … 2 | kuratiert: Grafik ▸ Darstellung | Slider | 0.05 | stufenlos | Element effect strength / Stärke der Elementwirkung | widget: Bar+Pfeile |
| 316 | EnvSound | `AmbienceBed` | Boolean | `true` | — | kuratiert: Grafik ▸ Darstellung | Toggle | — | 2 | Room tone / Grundgeräuschkulisse |  |
| 317 | EnvSound | `AmbienceBedGain` | Single | `1` | 0 … 2 | kuratiert: Grafik ▸ Darstellung | Slider | 0.05 | stufenlos | Room tone volume / Lautstärke der Grundkulisse | widget: Bar+Pfeile |
| 318 | EnvSound | `Enabled` | Boolean | `true` | — | kuratiert: Grafik ▸ Darstellung | Toggle | — | 2 | Environment sounds / Umgebungsgeräusche |  |
| 319 | EnvSound | `Gain` | Single | `1` | 0 … 2 | kuratiert: Grafik ▸ Darstellung | Slider | 0.05 | stufenlos | Environment volume / Lautstärke der Umgebung | widget: Bar+Pfeile |
| 320 | FigureGrab | `ArcaneHeldFaceYawDegrees` | Single | `-133` | — | **not offered** (retired marker) | Stepper | 1 | unbegrenzt | _(kein Name — Menü zeigt den englischen Key)_ | retired |
| 321 | FigureGrab | `ArcaneHeldOffsetForward` | Single | `0.05` | — | Erweitert ▸ Hände & Figuren | Stepper | 0.01 | unbegrenzt | Figure: forward (m) / Figur: vor/zurück (m) |  |
| 322 | FigureGrab | `ArcaneHeldOffsetSide` | Single | `0.03` | — | Erweitert ▸ Hände & Figuren | Stepper | 0.01 | unbegrenzt | Figure: sideways (m) / Figur: seitlich (m) |  |
| 323 | FigureGrab | `ArcaneHeldOffsetUp` | Single | `0.01` | — | Erweitert ▸ Hände & Figuren | Stepper | 0.01 | unbegrenzt | Figure: height (m) / Figur: Höhe (m) |  |
| 324 | FigureGrab | `ArcaneHeldRollDegrees` | Single | `0` | — | **not offered** (retired marker) | Stepper | 1 | unbegrenzt | _(kein Name — Menü zeigt den englischen Key)_ | retired |
| 325 | FigureGrab | `ArcaneHeldRotPitch` | Single | `17` | — | Erweitert ▸ Hände & Figuren | Stepper | 1 | unbegrenzt | Figure: pitch (°) / Figur: Neigung (°) |  |
| 326 | FigureGrab | `ArcaneHeldRotRoll` | Single | `0` | — | Erweitert ▸ Hände & Figuren | Stepper | 1 | unbegrenzt | Figure: roll (°) / Figur: Rollen (°) |  |
| 327 | FigureGrab | `ArcaneHeldRotYaw` | Single | `-133` | — | Erweitert ▸ Hände & Figuren | Stepper | 1 | unbegrenzt | Figure: yaw (°) / Figur: Drehung (°) |  |
| 328 | FigureGrab | `ArcaneHeldTiltDegrees` | Single | `17` | — | **not offered** (retired marker) | Stepper | 1 | unbegrenzt | _(kein Name — Menü zeigt den englischen Key)_ | retired |
| 329 | FigureGrab | `GloveHeldFaceYawDegrees` | Single | `-133` | — | **not offered** (retired marker) | Stepper | 1 | unbegrenzt | _(kein Name — Menü zeigt den englischen Key)_ | retired |
| 330 | FigureGrab | `GloveHeldOffsetForward` | Single | `0.05` | — | Erweitert ▸ Hände & Figuren | Stepper | 0.01 | unbegrenzt | Figure: forward (m) / Figur: vor/zurück (m) |  |
| 331 | FigureGrab | `GloveHeldOffsetSide` | Single | `0.03` | — | Erweitert ▸ Hände & Figuren | Stepper | 0.01 | unbegrenzt | Figure: sideways (m) / Figur: seitlich (m) |  |
| 332 | FigureGrab | `GloveHeldOffsetUp` | Single | `0.01` | — | Erweitert ▸ Hände & Figuren | Stepper | 0.01 | unbegrenzt | Figure: height (m) / Figur: Höhe (m) |  |
| 333 | FigureGrab | `GloveHeldRollDegrees` | Single | `0` | — | **not offered** (retired marker) | Stepper | 1 | unbegrenzt | _(kein Name — Menü zeigt den englischen Key)_ | retired |
| 334 | FigureGrab | `GloveHeldRotPitch` | Single | `17` | — | Erweitert ▸ Hände & Figuren | Stepper | 1 | unbegrenzt | Figure: pitch (°) / Figur: Neigung (°) |  |
| 335 | FigureGrab | `GloveHeldRotRoll` | Single | `0` | — | Erweitert ▸ Hände & Figuren | Stepper | 1 | unbegrenzt | Figure: roll (°) / Figur: Rollen (°) |  |
| 336 | FigureGrab | `GloveHeldRotYaw` | Single | `-133` | — | Erweitert ▸ Hände & Figuren | Stepper | 1 | unbegrenzt | Figure: yaw (°) / Figur: Drehung (°) |  |
| 337 | FigureGrab | `GloveHeldTiltDegrees` | Single | `17` | — | **not offered** (retired marker) | Stepper | 1 | unbegrenzt | _(kein Name — Menü zeigt den englischen Key)_ | retired |
| 338 | FigureGrab | `GrabFigures` | Boolean | `true` | — | Erweitert ▸ Hände & Figuren | Toggle | — | 2 | Grab figures / Figuren greifen | →kuratiert |
| 339 | FigureGrab | `HeldFaceYawDegrees` | Single | `-133` | — | **not offered** (retired marker) | Stepper | 1 | unbegrenzt | _(kein Name — Menü zeigt den englischen Key)_ | retired |
| 340 | FigureGrab | `HeldFigureInfo` | Boolean | `true` | — | Erweitert ▸ Hände & Figuren | Toggle | — | 2 | Figure: info on pickup / Figur: Info beim Aufnehmen |  |
| 341 | FigureGrab | `HeldOffsetForward` | Single | `0.05` | — | **not offered** (retired marker) | Stepper | 0.01 | unbegrenzt | _(kein Name — Menü zeigt den englischen Key)_ | retired |
| 342 | FigureGrab | `HeldOffsetSide` | Single | `0.03` | — | **not offered** (retired marker) | Stepper | 0.01 | unbegrenzt | _(kein Name — Menü zeigt den englischen Key)_ | retired |
| 343 | FigureGrab | `HeldOffsetUp` | Single | `0.01` | — | **not offered** (retired marker) | Stepper | 0.01 | unbegrenzt | _(kein Name — Menü zeigt den englischen Key)_ | retired |
| 344 | FigureGrab | `HeldTiltDegrees` | Single | `17` | — | **not offered** (retired marker) | Stepper | 1 | unbegrenzt | _(kein Name — Menü zeigt den englischen Key)_ | retired |
| 345 | FigureGrab | `HeldUpright` | Boolean | `true` | — | Erweitert ▸ Hände & Figuren | Toggle | — | 2 | Figure: hold upright / Figur: aufrecht halten |  |
| 346 | FigureGrab | `HeldUprightAtGrab` | Boolean | `true` | — | Erweitert ▸ Hände & Figuren | Toggle | — | 2 | Figure: upright on grab / Figur: aufrecht greifen |  |
| 347 | FigureGrab | `PickRadiusMillimeters` | Single | `40` | 5 … 130 | Erweitert ▸ Hände & Figuren | Slider | 5 | stufenlos | Figure: grab range at the hand (mm) / Figur: Greifradius an der Hand (mm) |  |
| 348 | FigureGrab | `PlateHeldFaceYawDegrees` | Single | `-133` | — | **not offered** (retired marker) | Stepper | 1 | unbegrenzt | _(kein Name — Menü zeigt den englischen Key)_ | retired |
| 349 | FigureGrab | `PlateHeldOffsetForward` | Single | `0.05` | — | Erweitert ▸ Hände & Figuren | Stepper | 0.01 | unbegrenzt | Figure: forward (m) / Figur: vor/zurück (m) |  |
| 350 | FigureGrab | `PlateHeldOffsetSide` | Single | `0.03` | — | Erweitert ▸ Hände & Figuren | Stepper | 0.01 | unbegrenzt | Figure: sideways (m) / Figur: seitlich (m) |  |
| 351 | FigureGrab | `PlateHeldOffsetUp` | Single | `0.01` | — | Erweitert ▸ Hände & Figuren | Stepper | 0.01 | unbegrenzt | Figure: height (m) / Figur: Höhe (m) |  |
| 352 | FigureGrab | `PlateHeldRollDegrees` | Single | `0` | — | **not offered** (retired marker) | Stepper | 1 | unbegrenzt | _(kein Name — Menü zeigt den englischen Key)_ | retired |
| 353 | FigureGrab | `PlateHeldRotPitch` | Single | `17` | — | Erweitert ▸ Hände & Figuren | Stepper | 1 | unbegrenzt | Figure: pitch (°) / Figur: Neigung (°) |  |
| 354 | FigureGrab | `PlateHeldRotRoll` | Single | `0` | — | Erweitert ▸ Hände & Figuren | Stepper | 1 | unbegrenzt | Figure: roll (°) / Figur: Rollen (°) |  |
| 355 | FigureGrab | `PlateHeldRotYaw` | Single | `-133` | — | Erweitert ▸ Hände & Figuren | Stepper | 1 | unbegrenzt | Figure: yaw (°) / Figur: Drehung (°) |  |
| 356 | FigureGrab | `PlateHeldTiltDegrees` | Single | `17` | — | **not offered** (retired marker) | Stepper | 1 | unbegrenzt | _(kein Name — Menü zeigt den englischen Key)_ | retired |
| 357 | FigureGrab | `StretchLimits` | Boolean | `true` | — | Erweitert ▸ Hände & Figuren | Toggle | — | 2 | Figure: size limits on/off / Figur: Größen-Grenzen an/aus |  |
| 358 | FigureGrab | `StretchReachMillimeters` | Single | `80` | 5 … 300 | Erweitert ▸ Hände & Figuren | Slider | 5 | stufenlos | _(kein Name — Menü zeigt den englischen Key)_ |  |
| 359 | FigureGrab | `StretchScaleMax` | Single | `3` | 1 … 8 | Erweitert ▸ Hände & Figuren | Slider | 0.1 | stufenlos | Figure: max size in hand / Figur: Maximalgröße in Hand |  |
| 360 | FigureGrab | `StretchScaleMin` | Single | `0.5` | 0.1 … 1 | Erweitert ▸ Hände & Figuren | Slider | 0.05 | stufenlos | Figure: min size in hand / Figur: Mindestgröße in Hand |  |
| 361 | General | `Enabled` | Boolean | `true` | — | Erweitert ▸ System & Start | Toggle | — | 2 | Enable VR mod / VR-Mod aktivieren | **DELETE** |
| 362 | General | `LogLevel` | VRLogLevel | `Trace` | — | Erweitert ▸ System & Start | Dropdown (enum) | — | Liste | Log detail level / Protokoll-Detailstufe | →kuratiert |
| 363 | General | `RuntimeOverride` | String | `` | — | Erweitert ▸ System & Start | read-only text | — | — | OpenXR runtime file / OpenXR-Runtime-Datei | **DELETE** |
| 364 | Hands | `ArcaneForwardOffset` | Single | `-0.009` | — | Erweitert ▸ Hände & Figuren | Stepper | 0.01 | unbegrenzt | Hand seat: forward (m) / Handsitz: vor/zurück (m) |  |
| 365 | Hands | `ArcaneGripPitchDegrees` | Single | `-45` | — | Erweitert ▸ Hände & Figuren | Stepper | 1 | unbegrenzt | Hand seat: pitch (°) / Handsitz: Neigung (°) |  |
| 366 | Hands | `ArcaneGripRollDegrees` | Single | `-109` | — | Erweitert ▸ Hände & Figuren | Stepper | 1 | unbegrenzt | Hand seat: roll (°) / Handsitz: Rollen (°) |  |
| 367 | Hands | `ArcaneGripYawDegrees` | Single | `-35` | — | Erweitert ▸ Hände & Figuren | Stepper | 1 | unbegrenzt | Hand seat: yaw (°) / Handsitz: Gieren (°) |  |
| 368 | Hands | `ArcaneLateralOffset` | Single | `0` | — | Erweitert ▸ Hände & Figuren | Stepper | 0.01 | unbegrenzt | Hand seat: sideways (m) / Handsitz: seitlich (m) |  |
| 369 | Hands | `ArcaneScale` | Single | `0.62` | — | kuratiert: Avatar & Mehrspieler ▸ Dein Auftritt | Stepper | 0.05 | unbegrenzt | Hand size / Handgröße |  |
| 370 | Hands | `ArcaneSpreadOffset` | Single | `0.05` | — | Erweitert ▸ Hände & Figuren | Stepper | 0.01 | unbegrenzt | Hand spacing (m) / Handabstand (m) |  |
| 371 | Hands | `ArcaneVerticalOffset` | Single | `-0.009` | — | Erweitert ▸ Hände & Figuren | Stepper | 0.01 | unbegrenzt | Hand seat: height (m) / Handsitz: Höhe (m) |  |
| 372 | Hands | `CurlInputFullAt` | Single | `0.85` | — | kuratiert: Komfort ▸ Hände & Zielen | Stepper | 0.05 | unbegrenzt | Full curl at grip value / Vollgriff ab Griffwert |  |
| 373 | Hands | `CurlMiddle` | Single | `95` | — | Erweitert ▸ Hände & Figuren | Stepper | 2 | unbegrenzt | Finger curl: middle (°) / Krümmung: Mitte (°) |  |
| 374 | Hands | `CurlProximal` | Single | `75` | — | Erweitert ▸ Hände & Figuren | Stepper | 2 | unbegrenzt | Finger curl: knuckle (°) / Krümmung: Wurzel (°) |  |
| 375 | Hands | `CurlTip` | Single | `65` | — | Erweitert ▸ Hände & Figuren | Stepper | 1 | unbegrenzt | Finger curl: tip (°) / Krümmung: Spitze (°) |  |
| 376 | Hands | `GhostHandOnFan` | Boolean | `true` | — | Erweitert ▸ Hände & Figuren | Toggle | — | 2 | Ghost hand on open fan / Geisterhand bei Fächer | →kuratiert |
| 377 | Hands | `GhostHandOnHeldCard` | Boolean | `true` | — | Erweitert ▸ Hände & Figuren | Toggle | — | 2 | Ghost hand on held card / Geisterhand bei Karte | →kuratiert |
| 378 | Hands | `GhostHandStrength` | Single | `0.55` | 0.05 … 0.95 | Erweitert ▸ Hände & Figuren | Slider | 0.02 | stufenlos | Ghost hand strength / Geisterhand-Stärke | →kuratiert |
| 379 | Hands | `GloveForwardOffset` | Single | `-0.054` | — | Erweitert ▸ Hände & Figuren | Stepper | 0.01 | unbegrenzt | Hand seat: forward (m) / Handsitz: vor/zurück (m) |  |
| 380 | Hands | `GloveGripPitchDegrees` | Single | `-51` | — | Erweitert ▸ Hände & Figuren | Stepper | 1 | unbegrenzt | Hand seat: pitch (°) / Handsitz: Neigung (°) |  |
| 381 | Hands | `GloveGripRollDegrees` | Single | `-109` | — | Erweitert ▸ Hände & Figuren | Stepper | 1 | unbegrenzt | Hand seat: roll (°) / Handsitz: Rollen (°) |  |
| 382 | Hands | `GloveGripYawDegrees` | Single | `-35` | — | Erweitert ▸ Hände & Figuren | Stepper | 1 | unbegrenzt | Hand seat: yaw (°) / Handsitz: Gieren (°) |  |
| 383 | Hands | `GloveLateralOffset` | Single | `-0.01` | — | Erweitert ▸ Hände & Figuren | Stepper | 0.01 | unbegrenzt | Hand seat: sideways (m) / Handsitz: seitlich (m) |  |
| 384 | Hands | `GlovePinkyCounterAbduction` | Single | `14` | — | Erweitert ▸ Hände & Figuren | Stepper | 0.2 | unbegrenzt | Glove: pinky angle (°) / Kleinfinger-Winkel (°) |  |
| 385 | Hands | `GloveScale` | Single | `1.12` | — | kuratiert: Avatar & Mehrspieler ▸ Dein Auftritt | Stepper | 0.05 | unbegrenzt | Hand size / Handgröße |  |
| 386 | Hands | `GloveSpreadOffset` | Single | `0.07` | — | Erweitert ▸ Hände & Figuren | Stepper | 0.01 | unbegrenzt | Hand spacing (m) / Handabstand (m) |  |
| 387 | Hands | `GloveVerticalOffset` | Single | `0.061` | — | Erweitert ▸ Hände & Figuren | Stepper | 0.01 | unbegrenzt | Hand seat: height (m) / Handsitz: Höhe (m) |  |
| 388 | Hands | `HandColor` | String | `D9C9B5` | — | **not offered** (NotOffered list) | read-only text | — | — | _(kein Name — Menü zeigt den englischen Key)_ | not offered |
| 389 | Hands | `HandStyle` | HandStyle | `Plate` | — | kuratiert: Avatar & Mehrspieler ▸ Dein Auftritt | Dropdown (enum) | — | Liste | Hand model / Handmodell |  |
| 390 | Hands | `LaserFingerOffsetMeters` | Single | `0.02` | — | Erweitert ▸ Hände & Figuren | Stepper | 0.005 | unbegrenzt | Laser start offset (m) / Laser-Startversatz (m) |  |
| 391 | Hands | `LaserFingerOrigin` | Boolean | `false` | — | kuratiert: Komfort ▸ Hände & Zielen | Toggle | — | 2 | Laser from fingertip / Laser ab Fingerspitze |  |
| 392 | Hands | `PlateForwardOffset` | Single | `-0.009` | — | Erweitert ▸ Hände & Figuren | Stepper | 0.01 | unbegrenzt | Hand seat: forward (m) / Handsitz: vor/zurück (m) |  |
| 393 | Hands | `PlateGripPitchDegrees` | Single | `-45` | — | Erweitert ▸ Hände & Figuren | Stepper | 1 | unbegrenzt | Hand seat: pitch (°) / Handsitz: Neigung (°) |  |
| 394 | Hands | `PlateGripRollDegrees` | Single | `-109` | — | Erweitert ▸ Hände & Figuren | Stepper | 1 | unbegrenzt | Hand seat: roll (°) / Handsitz: Rollen (°) |  |
| 395 | Hands | `PlateGripYawDegrees` | Single | `-35` | — | Erweitert ▸ Hände & Figuren | Stepper | 1 | unbegrenzt | Hand seat: yaw (°) / Handsitz: Gieren (°) |  |
| 396 | Hands | `PlateLateralOffset` | Single | `0` | — | Erweitert ▸ Hände & Figuren | Stepper | 0.01 | unbegrenzt | Hand seat: sideways (m) / Handsitz: seitlich (m) |  |
| 397 | Hands | `PlateScale` | Single | `0.62` | — | kuratiert: Avatar & Mehrspieler ▸ Dein Auftritt | Stepper | 0.05 | unbegrenzt | Hand size / Handgröße |  |
| 398 | Hands | `PlateSpreadOffset` | Single | `0.05` | — | Erweitert ▸ Hände & Figuren | Stepper | 0.01 | unbegrenzt | Hand spacing (m) / Handabstand (m) |  |
| 399 | Hands | `PlateVerticalOffset` | Single | `-0.009` | — | Erweitert ▸ Hände & Figuren | Stepper | 0.01 | unbegrenzt | Hand seat: height (m) / Handsitz: Höhe (m) |  |
| 400 | Hands | `PrimaryHand` | String | `Right` | — | kuratiert: Komfort ▸ Hände & Zielen | Dropdown (curated) | — | Liste | Dominant hand / Dominante Hand |  |
| 401 | Hands | `ScrollWithStickOnly` | Boolean | `true` | — | kuratiert: Komfort ▸ Hände & Zielen | Toggle | — | 2 | Scroll with stick only / Nur per Stick scrollen |  |
| 402 | Hands | `TestFist` | Boolean | `false` | — | Erweitert ▸ Hände & Figuren | Toggle | — | 2 | Debug: force fist / Debug: Faust erzwingen | **DELETE** |
| 403 | Haunt | `EasterEggs` | Boolean | `true` | — | kuratiert: Grafik ▸ Darstellung | Toggle | — | 2 | Creepy easter eggs / Grusel-Easter-Eggs |  |
| 404 | Haunt | `Frequency` | Single | `0.2162736` | 0 … 1 | kuratiert: Grafik ▸ Darstellung | Slider | 0.02 | stufenlos | Easter egg frequency / Häufigkeit der Easter-Eggs | widget: Bar+Pfeile |
| 405 | HexHighlight | `KillBorderFlame` | Boolean | `true` | — | Erweitert ▸ Brett & Zielen | Toggle | — | 2 | Fallback: flame off / Fallback: Randflamme aus | **DELETE** |
| 406 | HexHighlight | `KillCrosshair` | Boolean | `true` | — | Erweitert ▸ Brett & Zielen | Toggle | — | 2 | Fallback: crosshair off / Fallback: Fadenkreuz aus | **DELETE** |
| 407 | HexHighlight | `LogMaterialDump` | Boolean | `true` | — | Erweitert ▸ Brett & Zielen | Toggle | — | 2 | Log hex material / Hex-Material ins Log | **DELETE** |
| 408 | HexHighlight | `StableDepthBias` | Single | `0.0002` | — | Erweitert ▸ Brett & Zielen | Stepper | 5e-06 | unbegrenzt | Hex decal: depth bias / Hex-Dekal: Tiefen-Bias | **DELETE** |
| 409 | HexHighlight | `StableZTest` | Int32 | `4` | — | Erweitert ▸ Brett & Zielen | Stepper | 1 | unbegrenzt | Hex decal: ZTest / Hex-Dekal: ZTest | **DELETE** |
| 410 | HexHighlight | `SwapStableShader` | Boolean | `true` | — | Erweitert ▸ Brett & Zielen | Toggle | — | 2 | Stable hex shader / Stabiler Hex-Shader | **DELETE** |
| 411 | Keyboard | `AutoCapitalise` | Boolean | `true` | — | kuratiert: Tafeln ▸ Texteingabe | Toggle | — | 2 | Capitalise words / Wörter großschreiben |  |
| 412 | MapRoom | `CityIconScale` | Single | `1` | 0.5 … 4 | Erweitert ▸ Bild & Darstellung | Slider | 0.05 | stufenlos | 3D map: city map icons / Karte 3D: Symbole Stadtkarte |  |
| 413 | MapRoom | `GloomhavenIconScale` | Single | `1` | 0.5 … 4 | Erweitert ▸ Bild & Darstellung | Slider | 0.05 | stufenlos | 3D map: Gloomhaven marker / Karte 3D: Gloomhaven-Marker |  |
| 414 | MapRoom | `IconScale` | Single | `1` | 0.5 … 4 | Erweitert ▸ Bild & Darstellung | Slider | 0.05 | stufenlos | 3D map: world map icons / Karte 3D: Symbole Weltkarte |  |
| 415 | MapRoom | `PartyMarkerScale` | Single | `1` | 0.5 … 4 | Erweitert ▸ Bild & Darstellung | Slider | 0.05 | stufenlos | 3D map: party marker / Karte 3D: Gruppen-Marker |  |
| 416 | MapRoom | `PathWidthScale` | Single | `1` | 0.5 … 4 | Erweitert ▸ Bild & Darstellung | Slider | 0.05 | stufenlos | 3D map: route width / Karte 3D: Wegbreite |  |
| 417 | MixedReality | `Enabled` | Boolean | `false` | — | kuratiert: Grafik ▸ Mixed Reality | Toggle | — | 2 | Mixed Reality / Mixed Reality an |  |
| 418 | MixedReality | `HideSkyMeshes` | Boolean | `true` | — | **not offered** (NotOffered list) | Toggle | — | 2 | _(kein Name — Menü zeigt den englischen Key)_ | not offered |
| 419 | MixedReality | `KeyColor` | Color | `00FF00FF` | — | kuratiert: Grafik ▸ Mixed Reality | Dropdown (preset) | 0.05 | Liste | Key colour / Key-Farbe |  |
| 420 | MixedReality | `OpaquePreviewTiles` | Boolean | `true` | — | Erweitert ▸ Bild & Darstellung | Toggle | — | 2 | _(kein Name — Menü zeigt den englischen Key)_ | **DELETE** |
| 421 | MixedReality | `UnseenBackingDebugColors` | Boolean | `false` | — | Erweitert ▸ Bild & Darstellung | Toggle | — | 2 | _(kein Name — Menü zeigt den englischen Key)_ | **DELETE** |
| 422 | MixedReality | `UnseenRegionMembership` | Boolean | `true` | — | Erweitert ▸ Bild & Darstellung | Toggle | — | 2 | _(kein Name — Menü zeigt den englischen Key)_ | **DELETE** |
| 423 | MixedReality | `UnseenRimInset` | Single | `0.03` | — | Erweitert ▸ Bild & Darstellung | Stepper | 0.01 | unbegrenzt | _(kein Name — Menü zeigt den englischen Key)_ |  |
| 424 | MixedReality | `UnseenRimTopClearance` | Single | `0.025` | — | Erweitert ▸ Bild & Darstellung | Stepper | 0.005 | unbegrenzt | _(kein Name — Menü zeigt den englischen Key)_ |  |
| 425 | MixedReality | `UnseenSkirtScale` | Single | `1.2` | — | Erweitert ▸ Bild & Darstellung | Stepper | 0.05 | unbegrenzt | _(kein Name — Menü zeigt den englischen Key)_ |  |
| 426 | MixedReality | `UnseenWaferDrop` | Single | `0.02` | — | Erweitert ▸ Bild & Darstellung | Stepper | 0.0005 | unbegrenzt | _(kein Name — Menü zeigt den englischen Key)_ |  |
| 427 | Net | `Enabled` | Boolean | `true` | — | kuratiert: Avatar & Mehrspieler ▸ Zusammen spielen | Toggle | — | 2 | Multiplayer sync / Mehrspieler-Abgleich |  |
| 428 | Net | `MaskId` | Int32 | `2` | 0 … 2 | kuratiert: Avatar & Mehrspieler ▸ Dein Auftritt | Dropdown (preset) | 1 | Liste | Head mask / Kopfmaske |  |
| 429 | Net | `MaskSize` | Single | `1.29148` | 0.25 … 2.55 | kuratiert: Avatar & Mehrspieler ▸ Dein Auftritt | Slider | 0.05 | stufenlos | Mask size / Maskengröße | widget: Bar+Pfeile |
| 430 | Net | `MirrorEnabled` | Boolean | `false` | — | kuratiert: Avatar & Mehrspieler ▸ Dein Auftritt | Toggle | — | 2 | Mirror / Spiegel |  |
| 431 | Net | `NameTags` | Boolean | `true` | — | kuratiert: Avatar & Mehrspieler ▸ Zusammen spielen | Toggle | — | 2 | Name tags / Namensschilder |  |
| 432 | Net | `RemoteBoards` | RemoteBoardVisibility | `Always` | — | kuratiert: Avatar & Mehrspieler ▸ Zusammen spielen | Dropdown (enum) | — | Liste | Player boards / Mitspieler-Bretter |  |
| 433 | Net | `VersionGuard` | Boolean | `true` | — | Erweitert ▸ Mehrspieler | Toggle | — | 2 | Version handshake / Versionsabgleich |  |
| 434 | Optimize | `CacheTickDelegates` | Boolean | `true` | — | Erweitert ▸ Messung & Diagnose | Toggle | — | 2 | Cache tick delegates / Tick-Delegates cachen |  |
| 435 | Optimize | `FanRelayoutMinInterval` | Single | `0` | 0 … 0.2 | Erweitert ▸ Messung & Diagnose | Slider | 0.05 | stufenlos | Fan relayout min (s) / Fächer-Relayout (s) |  |
| 436 | Optimize | `FigureScanCache` | Boolean | `true` | — | Erweitert ▸ Messung & Diagnose | Toggle | — | 2 | Cache figure scans / Figuren-Scan cachen |  |
| 437 | Optimize | `HeadCullingMaskDrop` | String | `` | — | Erweitert ▸ Messung & Diagnose | read-only text | — | — | Camera: skip layers / Kamera: Ebenen aus | **DELETE** |
| 438 | Optimize | `HeadDepthPrepass` | Boolean | `false` | — | Erweitert ▸ Messung & Diagnose | Toggle | — | 2 | Keep depth prepass / Tiefen-Prepass behalten | **DELETE** |
| 439 | Optimize | `HeadMaskFromScenarioCamera` | Boolean | `false` | — | Erweitert ▸ Messung & Diagnose | Toggle | — | 2 | Camera mask from game / Kameramaske vom Spiel | **DELETE** |
| 440 | Optimize | `InitiativeDepthEvalInterval` | Single | `0` | 0 … 0.25 | Erweitert ▸ Messung & Diagnose | Slider | 0.05 | stufenlos | Row depth interval (s) / Reihen-Tiefenintervall (s) |  |
| 441 | Optimize | `LeanLogStrings` | Boolean | `true` | — | Erweitert ▸ Messung & Diagnose | Toggle | — | 2 | Skip unused log strings / Log-Strings sparen |  |
| 442 | Optimize | `MapIconCache` | Boolean | `true` | — | Erweitert ▸ Messung & Diagnose | Toggle | — | 2 | Cache map icons / Karten-Icons cachen |  |
| 443 | Optimize | `QuietDiagnostics` | Boolean | `false` | — | Erweitert ▸ Messung & Diagnose | Toggle | — | 2 | Quiet diagnostics / Diagnose-Zeilen dämpfen |  |
| 444 | Optimize | `RemoteContentInterval` | Single | `0` | 0 … 2 | Erweitert ▸ Messung & Diagnose | Slider | 0.05 | stufenlos | Remote board scan (s) / Mitspieler-Scan (s) |  |
| 445 | Optimize | `TooltipScanGate` | Boolean | `true` | — | Erweitert ▸ Messung & Diagnose | Toggle | — | 2 | Tooltip scan on demand / Tooltip-Scan bei Bedarf |  |
| 446 | Optimize | `WallFadeEvalInterval` | Single | `0` | 0 … 0.25 | Erweitert ▸ Messung & Diagnose | Slider | 0.05 | stufenlos | Wall check interval (s) / Wand-Prüfintervall (s) |  |
| 447 | PeerBoardFade | `ExitDwellMovedSeconds` | Single | `2.5` | — | Erweitert ▸ Mehrspieler | Stepper | 0.05 | unbegrenzt | Unfade dwell, moved (s) / Einblende-Wartezeit (s) |  |
| 448 | PeerBoardFade | `ExitDwellStationarySeconds` | Single | `7` | — | Erweitert ▸ Mehrspieler | Stepper | 0.05 | unbegrenzt | Unfade dwell, still (s) / Wartezeit, ruhig (s) |  |
| 449 | PeerBoardFade | `Mode` | PeerBoardFadeMode | `Off` | — | Erweitert ▸ Mehrspieler | Dropdown (enum) | — | Liste | Boards blocking the view / Boards vor dem Spielfeld | →kuratiert |
| 450 | PeerBoardFade | `OccludedAlpha` | Single | `0.25` | — | Erweitert ▸ Mehrspieler | Stepper | 0.05 | unbegrenzt | Faded opacity (0-1) / Rest-Deckkraft (0-1) |  |
| 451 | PeerBoardFade | `OffFraction` | Single | `0.05` | — | Erweitert ▸ Mehrspieler | Stepper | 0.05 | unbegrenzt | Unfade below coverage / Einblenden unter Wert |  |
| 452 | PeerBoardFade | `OnFraction` | Single | `0.12` | — | Erweitert ▸ Mehrspieler | Stepper | 0.05 | unbegrenzt | Fade at coverage / Ausblenden ab Deckung |  |
| 453 | Perf | `Allocations` | Boolean | `true` | — | Erweitert ▸ Messung & Diagnose | Toggle | — | 2 | Memory sampling / Speicher-Messung |  |
| 454 | Perf | `Attribution` | Boolean | `true` | — | Erweitert ▸ Messung & Diagnose | Toggle | — | 2 | Measure mod steps / Schritte einzeln messen |  |
| 455 | Perf | `CullSubmitSplit` | Boolean | `false` | — | Erweitert ▸ Messung & Diagnose | Toggle | — | 2 | Cull/submit split / Cull/Submit-Aufteilung |  |
| 456 | Perf | `Enabled` | Boolean | `true` | — | Erweitert ▸ Messung & Diagnose | Toggle | — | 2 | Enable measurement / Messung aktivieren |  |
| 457 | Perf | `FrameSplit` | Boolean | `true` | — | Erweitert ▸ Messung & Diagnose | Toggle | — | 2 | Frame decomposition / Frame-Zerlegung loggen |  |
| 458 | Perf | `SceneCensus` | Boolean | `true` | — | Erweitert ▸ Messung & Diagnose | Toggle | — | 2 | Renderer census / Renderer-Zählung |  |
| 459 | Perf | `SceneProfile` | Boolean | `false` | — | Erweitert ▸ Messung & Diagnose | Toggle | — | 2 | Scene profile lines / Szenen-Profil loggen |  |
| 460 | Perf | `SpikeBudgetFactor` | Single | `2` | 1.2 … 10 | Erweitert ▸ Messung & Diagnose | Slider | 0.2 | stufenlos | Spike threshold (factor) / Spike-Schwelle (Faktor) |  |
| 461 | Perf | `SpikeLines` | Boolean | `true` | — | Erweitert ▸ Messung & Diagnose | Toggle | — | 2 | Log frame spikes / Spike-Zeilen loggen |  |
| 462 | Perf | `SpikeMaxPerSecond` | Single | `2` | 0.1 … 20 | Erweitert ▸ Messung & Diagnose | Slider | 0.5 | stufenlos | Spike lines per second / Spike-Zeilen pro Sekunde |  |
| 463 | Perf | `SummaryIntervalSeconds` | Single | `30` | 5 … 600 | Erweitert ▸ Messung & Diagnose | Slider | 10 | stufenlos | Summary interval (s) / Messintervall (s) |  |
| 464 | Perf | `TopSteps` | Int32 | `6` | 1 … 20 | Erweitert ▸ Messung & Diagnose | Slider | 1 | 20 (ganzzahlig) | Top steps in log (N) / Top-Schritte im Log (N) |  |
| 465 | Perf | `XrStats` | Boolean | `true` | — | Erweitert ▸ Messung & Diagnose | Toggle | — | 2 | Query XR statistics / XR-Statistik abfragen |  |
| 466 | RenderQuality | `EyeResolutionScale` | Single | `1` | 0.5 … 2 | kuratiert: Grafik ▸ Darstellung | Slider | 0.05 | stufenlos | Resolution per eye / Auflösung pro Auge | widget: Bar+Pfeile |
| 467 | RenderQuality | `ForceAnisotropic` | Boolean | `true` | — | kuratiert: Grafik ▸ Darstellung | Toggle | — | 2 | Anisotropic filtering / Anisotrope Filterung |  |
| 468 | RenderQuality | `MsaaLevel` | Int32 | `8` | — | kuratiert: Grafik ▸ Darstellung | Dropdown (list) | 1 | Liste | MSAA level / MSAA-Stufe |  |
| 469 | RenderQuality | `PixelLightCount` | Int32 | `-1` | -1 … 8 | kuratiert: Grafik ▸ Darstellung | Slider | 1 | 10 (ganzzahlig) | Pixel lights (max) / Pixellichter (max) | widget: Pfeile |
| 470 | RenderQuality | `RebuildRigOnMsaaChange` | Boolean | `false` | — | Erweitert ▸ Bild & Darstellung | Toggle | — | 2 | Rebuild rig on MSAA / Rig-Neubau bei MSAA | **DELETE** |
| 471 | RenderQuality | `ViewportScaleFallback` | Boolean | `true` | — | Erweitert ▸ Bild & Darstellung | Toggle | — | 2 | Viewport-scale fallback / Viewport-Ersatzskala | **DELETE** |
| 472 | RestButtons | `Depth` | Single | `0.012` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 0.002 | unbegrenzt | Rest keys: depth (m) / Rast-Tasten: Tiefe (m) |  |
| 473 | RestButtons | `Height` | Single | `0.105` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 0.01 | unbegrenzt | Rest keys: height (m) / Rast-Tasten: Höhe (m) |  |
| 474 | RestButtons | `Travel` | Single | `0.004` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 0.001 | unbegrenzt | Rest keys: travel (m) / Rast-Tasten: Hub (m) |  |
| 475 | RestButtons | `Width` | Single | `0.105` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 0.01 | unbegrenzt | Rest keys: width (m) / Rast-Tasten: Breite (m) |  |
| 476 | Rig | `Experimental3DMap` | Boolean | `false` | — | **not offered** (retired marker) | Toggle | — | 2 | 3D campaign map (experimental) / 3D-Kampagnenkarte (experimentell) | retired |
| 477 | Rig | `ForwardRendering` | Boolean | `true` | — | kuratiert: Grafik ▸ Darstellung | Toggle | — | 2 | Forward rendering / Forward-Rendering | **DELETE**; →Erweitert |
| 478 | Rig | `MaskedReaimDeadband` | Single | `5` | — | **not offered** (retired marker) | Stepper | 0.1 | unbegrenzt | _(kein Name — Menü zeigt den englischen Key)_ | retired |
| 479 | Rig | `MaskedReaimGain` | Single | `0.15` | — | **not offered** (retired marker) | Stepper | 0.05 | unbegrenzt | _(kein Name — Menü zeigt den englischen Key)_ | retired |
| 480 | Rig | `MaskedReaimHeadRate` | Single | `30` | — | **not offered** (retired marker) | Stepper | 1 | unbegrenzt | _(kein Name — Menü zeigt den englischen Key)_ | retired |
| 481 | Rig | `SpawnInCircle` | Boolean | `true` | — | kuratiert: Avatar & Mehrspieler ▸ Zusammen spielen | Toggle | — | 2 | Free seat at the board / Freier Platz am Brett |  |
| 482 | Rig | `VoidColor` | Color | `000000FF` | — | Erweitert ▸ Bild & Darstellung | Stepper x4 | 0.05 | unbegrenzt | Void colour around menus / Leerraum-Farbe um Menüs | **DELETE** |
| 483 | Rig | `WorldTiltDegrees` | Single | `0` | 0 … 60 | **not offered** (retired marker) | Slider | 5 | stufenlos | _(kein Name — Menü zeigt den englischen Key)_ | retired |
| 484 | RoundButtons | `CapSize` | Single | `0.042` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 0.01 | unbegrenzt | Skip key: cap size (m) / Überspringen: Größe (m) |  |
| 485 | RoundButtons | `Depth` | Single | `0.015` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 0.005 | unbegrenzt | Skip key: depth (m) / Überspringen: Tiefe (m) |  |
| 486 | RoundButtons | `Height` | Single | `0.035` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 0.01 | unbegrenzt | Skip key: height (m) / Überspringen: Höhe (m) |  |
| 487 | RoundButtons | `OffsetX` | Single | `-0.045` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 0.01 | unbegrenzt | Skip key: sideways (m) / Überspringen: quer (m) |  |
| 488 | RoundButtons | `OffsetY` | Single | `0.26` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 0.01 | unbegrenzt | Skip key: up-board (m) / Überspringen: hoch (m) |  |
| 489 | RoundButtons | `OffsetZ` | Single | `0.005000001` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 0.01 | unbegrenzt | Skip key: proud (m) / Überspringen: heraus (m) |  |
| 490 | RoundButtons | `Shape` | ButtonShape | `Square` | — | Erweitert ▸ Steuerbrett (pro Brett) | Dropdown (enum) | — | Liste | Skip key: shape / Überspringen: Form |  |
| 491 | RoundButtons | `Travel` | Single | `0.008` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 0.002 | unbegrenzt | Skip key: travel (m) / Überspringen: Hub (m) |  |
| 492 | RoundButtons | `Width` | Single | `0.089` | — | Erweitert ▸ Steuerbrett (pro Brett) | Stepper | 0.01 | unbegrenzt | Skip key: width (m) / Überspringen: Breite (m) |  |
| 493 | SelectionReady | `Enabled` | Boolean | `true` | — | kuratiert: Brett & Karten ▸ Stapel & Hinweise | Toggle | — | 2 | Selection reminder pulse / Auswahl-Erinnerung |  |
| 494 | Sky | `Style` | SkyStyle | `Cellar` | — | kuratiert: Grafik ▸ Darstellung | Dropdown (preset) | — | Liste | Environment / Umgebung |  |
| 495 | SquareCaps | `Depth` | Single | `0` | — | **not offered** (NotOffered list) | n/a (not offered) | — | — | _(kein Name — Menü zeigt den englischen Key)_ | not offered |
| 496 | SquareCaps | `Height` | Single | `0` | — | **not offered** (NotOffered list) | n/a (not offered) | — | — | _(kein Name — Menü zeigt den englischen Key)_ | not offered |
| 497 | SquareCaps | `Travel` | Single | `0` | — | **not offered** (NotOffered list) | n/a (not offered) | — | — | _(kein Name — Menü zeigt den englischen Key)_ | not offered |
| 498 | SquareCaps | `Width` | Single | `0` | — | **not offered** (NotOffered list) | n/a (not offered) | — | — | _(kein Name — Menü zeigt den englischen Key)_ | not offered |
| 499 | Stereo | `RenderMode` | String | `MultiPass` | — | Erweitert ▸ Bild & Darstellung | Dropdown (list) | — | Liste | Stereo render mode / Stereo-Rendermodus | **DELETE** |
| 500 | TransientButtons | `CapSize` | Single | `0.042` | — | **not offered** (NotOffered list) | n/a (not offered) | — | — | _(kein Name — Menü zeigt den englischen Key)_ | not offered |
| 501 | TransientButtons | `OffsetX` | Single | `0` | — | **not offered** (NotOffered list) | n/a (not offered) | — | — | _(kein Name — Menü zeigt den englischen Key)_ | not offered |
| 502 | TransientButtons | `OffsetY` | Single | `0` | — | **not offered** (NotOffered list) | n/a (not offered) | — | — | _(kein Name — Menü zeigt den englischen Key)_ | not offered |
| 503 | TransientButtons | `Shape` | ButtonShape | `Round` | — | **not offered** (NotOffered list) | n/a (not offered) | — | — | _(kein Name — Menü zeigt den englischen Key)_ | not offered |
| 504 | WallFade | `ExitDwellMovedSeconds` | Single | `2.5` | — | Erweitert ▸ Bild & Darstellung | Stepper | 0.05 | unbegrenzt | Unfade dwell, moved (s) / Einblende-Wartezeit (s) |  |
| 505 | WallFade | `ExitDwellStationarySeconds` | Single | `3.6` | — | Erweitert ▸ Bild & Darstellung | Stepper | 0.05 | unbegrenzt | Unfade dwell, still (s) / Wartezeit, ruhig (s) |  |
| 506 | WallFade | `OffFraction` | Single | `0.2` | — | Erweitert ▸ Bild & Darstellung | Stepper | 0.05 | unbegrenzt | Unfade below coverage / Einblenden unter Wert |  |
| 507 | WallFade | `OnFraction` | Single | `0.1` | — | Erweitert ▸ Bild & Darstellung | Stepper | 0.02 | unbegrenzt | Fade at coverage / Ausblenden ab Deckung |  |
| 508 | WallFade | `StackedShellFade` | Boolean | `true` | — | kuratiert: Komfort ▸ Sichtbarkeit | Toggle | — | 2 | Fade fort superstructures / Festungs-Aufbauten ausblenden |  |
| 509 | WallFade | `SyncPeerFades` | Boolean | `true` | — | kuratiert: Avatar & Mehrspieler ▸ Zusammen spielen | Toggle | — | 2 | Walls: sync with teammates / Wände: mit Mitspielern synchron |  |
| 510 | WorldUI | `BarFixedSize` | Boolean | `true` | — | Erweitert ▸ Menüs & Tafeln | Toggle | — | 2 | Health bars: ignore distance / Balken: Abstand ignorieren | →kuratiert |
| 511 | WorldUI | `BarSizeScale` | Single | `0.7089906` | 0.25 … 3 | kuratiert: Tafeln ▸ Lebensbalken | Slider | 0.05 | stufenlos | Health bars: size / Lebensbalken: Größe | widget: Bar+Pfeile |
| 512 | WorldUI | `BarsOccluded` | Boolean | `false` | — | kuratiert: Tafeln ▸ Lebensbalken | Toggle | — | 2 | Health bars behind walls / Balken hinter Wänden |  |
| 513 | WorldUI | `ButtonCluster` | Boolean | `true` | — | kuratiert: Tafeln ▸ Tafeln & Anzeigen | Toggle | — | 2 | Wrist buttons / Handgelenk-Tasten |  |
| 514 | WorldUI | `CanvasScaleMm` | Single | `1` | 0.2 … 4 | Erweitert ▸ Menüs & Tafeln | Slider | 0.1 | stufenlos | Panel scale (mm/px) / Tafel-Maßstab (mm/px) |  |
| 515 | WorldUI | `CombatLog` | Boolean | `true` | — | kuratiert: Tafeln ▸ Tafeln & Anzeigen | Toggle | — | 2 | Show combat log / Kampflog anzeigen |  |
| 516 | WorldUI | `CombatLogFollowSeat` | Boolean | `false` | — | Erweitert ▸ Menüs & Tafeln | Toggle | — | 2 | Combat log follows you / Kampflog folgt dir |  |
| 517 | WorldUI | `CombatLogForward` | Single | `-0.234156` | — | Erweitert ▸ Menüs & Tafeln | Stepper | 0.01 | unbegrenzt | Combat log: forward (m) / Kampflog: vor (m) |  |
| 518 | WorldUI | `CombatLogRight` | Single | `0.941762` | — | Erweitert ▸ Menüs & Tafeln | Stepper | 0.01 | unbegrenzt | Combat log: right (m) / Kampflog: rechts (m) |  |
| 519 | WorldUI | `CombatLogScale` | Single | `0.67339` | — | Erweitert ▸ Menüs & Tafeln | Stepper | 0.05 | unbegrenzt | Combat log: size / Kampflog: Größe |  |
| 520 | WorldUI | `CombatLogUp` | Single | `0.458258` | — | Erweitert ▸ Menüs & Tafeln | Stepper | 0.01 | unbegrenzt | Combat log: height (m) / Kampflog: Höhe (m) |  |
| 521 | WorldUI | `CombatLogUserClosed` | Boolean | `true` | — | Erweitert ▸ Menüs & Tafeln | Toggle | — | 2 | Combat log closed by you / Kampflog vom Nutzer zu | **DELETE** |
| 522 | WorldUI | `DecisionDock` | Boolean | `true` | — | kuratiert: Tafeln ▸ Tafeln & Anzeigen | Toggle | — | 2 | Decision dock / Entscheidungsleiste |  |
| 523 | WorldUI | `DecisionPokeDeliberate` | Boolean | `true` | — | kuratiert: Tafeln ▸ Klick & Zeigen | Toggle | — | 2 | Decisions: firm press / Entscheidung: fest |  |
| 524 | WorldUI | `DesktopMirrorLeftEye` | Boolean | `true` | — | kuratiert: Grafik ▸ Monitor | Toggle | — | 2 | Monitor shows left eye / Monitor: linkes Auge |  |
| 525 | WorldUI | `DevForceConvert` | Boolean | `false` | — | Erweitert ▸ Menüs & Tafeln | Toggle | — | 2 | Dev: force conversion / Dev: Zwangsumwandlung | **DELETE** |
| 526 | WorldUI | `DevShowAllPanels` | Boolean | `false` | — | Erweitert ▸ Menüs & Tafeln | Toggle | — | 2 | Dev: show all panels / Dev: alle Tafeln zeigen | **DELETE** |
| 527 | WorldUI | `Dialogs` | Boolean | `true` | — | kuratiert: Tafeln ▸ Tafeln & Anzeigen | Toggle | — | 2 | Dialogs in VR / Dialoge in VR |  |
| 528 | WorldUI | `DragUnlockDegrees` | Single | `2` | — | Erweitert ▸ Menüs & Tafeln | Stepper | 0.5 | unbegrenzt | Unlock click at (°) / Klick lösen ab (°) |  |
| 529 | WorldUI | `DragUnlockSeconds` | Single | `0.15` | — | Erweitert ▸ Menüs & Tafeln | Stepper | 0.05 | unbegrenzt | Unlock click after (s) / Klick lösen nach (s) |  |
| 530 | WorldUI | `EnemyRevealBoardClearance` | Single | `0.1` | 0 … 0.5 | Erweitert ▸ Menüs & Tafeln | Slider | 0.01 | stufenlos | Enemy cards: clearance / Gegnerkarte: Abstand (m) |  |
| 531 | WorldUI | `HexHintDistance` | Single | `0.6` | 0.15 … 2 | Erweitert ▸ Menüs & Tafeln | Slider | 0.05 | stufenlos | Hex hint: distance / Feld-Hinweis: Abstand |  |
| 532 | WorldUI | `HexHintDrop` | Single | `0.12` | -1 … 1 | Erweitert ▸ Menüs & Tafeln | Slider | 0.05 | stufenlos | Hex hint: height / Feld-Hinweis: Höhe |  |
| 533 | WorldUI | `HexHintFollowView` | Boolean | `true` | — | kuratiert: Tafeln ▸ Klick & Zeigen | Toggle | — | 2 | Hex hint follows view / Feld-Hinweis folgt Blick |  |
| 534 | WorldUI | `HexHintSide` | Single | `0` | -1 … 1 | Erweitert ▸ Menüs & Tafeln | Slider | 0.05 | stufenlos | Hex hint: sideways / Feld-Hinweis: seitlich |  |
| 535 | WorldUI | `HoverInfoScale` | Single | `0.6` | 0.2 … 2 | kuratiert: Brett & Karten ▸ Stapel & Hinweise | Slider | 0.05 | stufenlos | Hover info size / Info-Karten: Größe | widget: Bar+Pfeile |
| 536 | WorldUI | `InitiativeDepthMaxSpreadPx` | Single | `15` | — | Erweitert ▸ Menüs & Tafeln | Stepper | 0.2 | unbegrenzt | Initiative: depth (px) / Initiative: Tiefe (px) |  |
| 537 | WorldUI | `LoadingIndicator` | Boolean | `true` | — | kuratiert: Tafeln ▸ Tafeln & Anzeigen | Toggle | — | 2 | Loading indicator / Ladeanzeige beim Laden |  |
| 538 | WorldUI | `ManualScreenChordSeconds` | Single | `2` | 0.3 … 6 | Erweitert ▸ Menüs & Tafeln | Slider | 0.1 | stufenlos | Rescue chord: hold (s) / Notgriff: halten (s) |  |
| 539 | WorldUI | `MapRoomHand` | Boolean | `true` | — | kuratiert: Grafik ▸ Darstellung | Toggle | — | 2 | 3D map: show your card hand / Karte 3D: Handkarten zeigen |  |
| 540 | WorldUI | `MapWindOpacity` | Single | `0.3` | — | Erweitert ▸ Menüs & Tafeln | Stepper | 0.05 | unbegrenzt | Map clouds opacity / Karte: Wolken-Deckkraft |  |
| 541 | WorldUI | `ModalStyle` | String | `window` | — | Erweitert ▸ Menüs & Tafeln | Dropdown (curated) | — | Liste | Window style / Fenster-Stil |  |
| 542 | WorldUI | `NeutraliseGrabPassBlur` | Boolean | `true` | — | Erweitert ▸ Menüs & Tafeln | Toggle | — | 2 | Windows: remove blur effect / Fenster: Weichzeichner entfernen |  |
| 543 | WorldUI | `PanelMipBake` | Boolean | `true` | — | Erweitert ▸ Menüs & Tafeln | Toggle | — | 2 | Smooth panel textures / Tafeltexturen glätten |  |
| 544 | WorldUI | `PanelMipLodOffset` | Single | `0` | -2 … 0 | kuratiert: Grafik ▸ Darstellung | Slider | 0.05 | stufenlos | Windows: filter sharpening / Fenster: Nachschärfen (Filter) | →Erweitert; widget: Bar+Pfeile |
| 545 | WorldUI | `PanelSupersample` | Boolean | `false` | — | kuratiert: Grafik ▸ Darstellung | Toggle | — | 2 | Windows: render sharp / Fenster: scharf zeichnen |  |
| 546 | WorldUI | `PanelSupersampleFactor` | Single | `2` | 0.5 … 2 | kuratiert: Grafik ▸ Darstellung | Slider | 0.05 | stufenlos | Windows: sharpness / Fenster: Schärfegrad | →Erweitert; widget: Bar+Pfeile |
| 547 | WorldUI | `PanelsFollowView` | Boolean | `false` | — | Erweitert ▸ Menüs & Tafeln | Toggle | — | 2 | Panels follow view (old) / Tafeln folgen Blick |  |
| 548 | WorldUI | `PokeClick` | Boolean | `true` | — | kuratiert: Tafeln ▸ Klick & Zeigen | Toggle | — | 2 | Poke to click / Antippen klickt |  |
| 549 | WorldUI | `PokePressDepthMm` | Single | `12` | 0 … 30 | Erweitert ▸ Menüs & Tafeln | Slider | 0.5 | stufenlos | Poke depth (mm) / Antipp-Tiefe (mm) |  |
| 550 | WorldUI | `ScreenDepthStrength` | Single | `1` | — | Erweitert ▸ Menüs & Tafeln | Stepper | 0.02 | unbegrenzt | 3D depth: strength / 3D-Tiefe: Stärke |  |
| 551 | WorldUI | `ScreenDistance` | Single | `1.6` | 0.3 … 8 | kuratiert: Tafeln ▸ 2D-Schirm | Slider | 0.1 | stufenlos | 2D screen: distance (m) / 2D-Schirm: Abstand (m) | widget: Bar+Pfeile |
| 552 | WorldUI | `ScreenLayerSplit` | Boolean | `true` | — | Erweitert ▸ Menüs & Tafeln | Toggle | — | 2 | Screen in two layers / Bildschirm zweilagig | **DELETE** |
| 553 | WorldUI | `ScreenParallaxScale` | Single | `6` | — | Erweitert ▸ Menüs & Tafeln | Stepper | 0.1 | unbegrenzt | 3D depth: parallax / 3D-Tiefe: Parallaxe |  |
| 554 | WorldUI | `ScreenWidth` | Single | `2.2` | 0.4 … 8 | kuratiert: Tafeln ▸ 2D-Schirm | Slider | 0.1 | stufenlos | 2D screen: width (m) / 2D-Schirm: Breite (m) | widget: Bar+Pfeile |
| 555 | WorldUI | `ShowIntro` | Boolean | `true` | — | kuratiert: Tafeln ▸ 2D-Schirm | Toggle | — | 2 | Show intro in VR / Intro in VR zeigen |  |
| 556 | WorldUI | `StereoScreen` | Boolean | `true` | — | Erweitert ▸ Menüs & Tafeln | Toggle | — | 2 | Screen with 3D depth / Bildschirm mit 3D-Tiefe |  |
| 557 | WorldUI | `SuppressPhysicalMouse` | Boolean | `true` | — | Erweitert ▸ Menüs & Tafeln | Toggle | — | 2 | Disable real mouse / Echte Maus deaktivieren | **DELETE** |
| 558 | WorldUI | `TravelButtonOffsetXWindowHeights` | Single | `0` | -0.25 … 0.25 | kuratiert: Grafik ▸ Darstellung | Stepper (PrefersStepper) | 0.01 | 51 | 3D map: travel button sideways / Karte 3D: Reise-Knopf seitlich | →Erweitert; widget: Pfeile m. Halten |
| 559 | WorldUI | `TravelButtonOffsetYWindowHeights` | Single | `0` | -0.6 … 0.6 | kuratiert: Grafik ▸ Darstellung | Stepper (PrefersStepper) | 0.01 | 121 | 3D map: travel button height / Karte 3D: Reise-Knopf Höhe | →Erweitert; widget: Pfeile m. Halten |
| 560 | WorldUI | `TrayNativeControls` | Boolean | `true` | — | Erweitert ▸ Menüs & Tafeln | Toggle | — | 2 | Real buttons on board / Echte Tasten am Brett |  |
| 561 | WorldUI | `VideoDepth` | Single | `0.8` | — | Erweitert ▸ Menüs & Tafeln | Stepper | 0.02 | unbegrenzt | Video: depth offset / Video: Tiefenversatz |  |
| 562 | WorldUI | `VideoDepthLayer` | Boolean | `true` | — | Erweitert ▸ Menüs & Tafeln | Toggle | — | 2 | 3D depth for videos / 3D-Tiefe bei Videos |  |
| 563 | WorldUI | `WindowLegibility` | Single | `1.25` | 1 … 1.75 | kuratiert: Grafik ▸ Darstellung | Slider | 0.05 | stufenlos | Window size / legibility / Fenster: Größe & Lesbarkeit | widget: Bar+Pfeile |
| 564 | WorldUI | `WristHud` | Boolean | `true` | — | kuratiert: Tafeln ▸ Tafeln & Anzeigen | Toggle | — | 2 | Wrist status display / Handgelenk-Anzeige |  |
| 565 | WristHud | `ArcanePalmFingerOffset` | Single | `-0.13` | — | Erweitert ▸ Hände & Figuren | Stepper | 0.01 | unbegrenzt | Wrist HUD: to fingers (m) / Arm-HUD: zu den Fingern (m) |  |
| 566 | WristHud | `ArcanePalmLiftOffset` | Single | `0.075` | — | Erweitert ▸ Hände & Figuren | Stepper | 0.01 | unbegrenzt | Wrist HUD: off palm (m) / Arm-HUD: Abstand Hand (m) |  |
| 567 | WristHud | `ArcanePalmPitch` | Single | `32` | — | Erweitert ▸ Hände & Figuren | Stepper | 1 | unbegrenzt | Wrist HUD: pitch (°) / Arm-HUD: Neigung (°) |  |
| 568 | WristHud | `ArcanePalmRoll` | Single | `-4` | — | Erweitert ▸ Hände & Figuren | Stepper | 1 | unbegrenzt | Wrist HUD: roll (°) / Arm-HUD: Rollen (°) |  |
| 569 | WristHud | `ArcanePalmSideOffset` | Single | `0.02` | — | Erweitert ▸ Hände & Figuren | Stepper | 0.01 | unbegrenzt | Wrist HUD: across (m) / Arm-HUD: quer (m) |  |
| 570 | WristHud | `ArcanePalmYaw` | Single | `1` | — | Erweitert ▸ Hände & Figuren | Stepper | 1 | unbegrenzt | Wrist HUD: yaw (°) / Arm-HUD: Gieren (°) |  |
| 571 | WristHud | `GlovePalmFingerOffset` | Single | `-0.05` | — | Erweitert ▸ Hände & Figuren | Stepper | 0.01 | unbegrenzt | Wrist HUD: to fingers (m) / Arm-HUD: zu den Fingern (m) |  |
| 572 | WristHud | `GlovePalmLiftOffset` | Single | `0.02` | — | Erweitert ▸ Hände & Figuren | Stepper | 0.005 | unbegrenzt | Wrist HUD: off palm (m) / Arm-HUD: Abstand Hand (m) |  |
| 573 | WristHud | `GlovePalmPitch` | Single | `0` | — | Erweitert ▸ Hände & Figuren | Stepper | 1 | unbegrenzt | Wrist HUD: pitch (°) / Arm-HUD: Neigung (°) |  |
| 574 | WristHud | `GlovePalmRoll` | Single | `4` | — | Erweitert ▸ Hände & Figuren | Stepper | 1 | unbegrenzt | Wrist HUD: roll (°) / Arm-HUD: Rollen (°) |  |
| 575 | WristHud | `GlovePalmSideOffset` | Single | `2.235174E-10` | — | Erweitert ▸ Hände & Figuren | Stepper | 0.01 | unbegrenzt | Wrist HUD: across (m) / Arm-HUD: quer (m) |  |
| 576 | WristHud | `GlovePalmYaw` | Single | `0` | — | Erweitert ▸ Hände & Figuren | Stepper | 1 | unbegrenzt | Wrist HUD: yaw (°) / Arm-HUD: Gieren (°) |  |
| 577 | WristHud | `PlatePalmFingerOffset` | Single | `-0.13` | — | Erweitert ▸ Hände & Figuren | Stepper | 0.01 | unbegrenzt | Wrist HUD: to fingers (m) / Arm-HUD: zu den Fingern (m) |  |
| 578 | WristHud | `PlatePalmLiftOffset` | Single | `0.075` | — | Erweitert ▸ Hände & Figuren | Stepper | 0.01 | unbegrenzt | Wrist HUD: off palm (m) / Arm-HUD: Abstand Hand (m) |  |
| 579 | WristHud | `PlatePalmPitch` | Single | `32` | — | Erweitert ▸ Hände & Figuren | Stepper | 1 | unbegrenzt | Wrist HUD: pitch (°) / Arm-HUD: Neigung (°) |  |
| 580 | WristHud | `PlatePalmRoll` | Single | `-4` | — | Erweitert ▸ Hände & Figuren | Stepper | 1 | unbegrenzt | Wrist HUD: roll (°) / Arm-HUD: Rollen (°) |  |
| 581 | WristHud | `PlatePalmSideOffset` | Single | `0.02` | — | Erweitert ▸ Hände & Figuren | Stepper | 0.01 | unbegrenzt | Wrist HUD: across (m) / Arm-HUD: quer (m) |  |
| 582 | WristHud | `PlatePalmYaw` | Single | `1` | — | Erweitert ▸ Hände & Figuren | Stepper | 1 | unbegrenzt | Wrist HUD: yaw (°) / Arm-HUD: Gieren (°) |  |

---

## 2. (a) DELETE — dials whose wrong value leaves a broken game

His test, applied literally: **is there a value inside the allowed range that leaves the player unable
to play, or unable to undo the change from inside the headset?** 39 offered entries fail it. They fall
into five families.

Two mechanisms are available for removing them, and neither costs an entry its cfg key:

* **`ConfigCatalog.NotOffered`** — a dictionary of `"Section/Key" → why`, already used for
  `Hands/HandColor`, `MixedReality/HideSkyMeshes` and the eight migration sources. The entry stays
  bound (so a hand-edit still works and the cfg file still round-trips) and simply stops being a row.
  **This is the right tool for anything that must remain an escape hatch.**
* **Unbind entirely and promote the value to a `const`** — right where the value is genuinely a
  constant that leaked into a config file.

### 2.1 Not settings at all — internal state and one-shot markers (5)

These are the archetype of "a constant that leaked into the config file". Three of them are literally
captioned *"Interne Marke"* / *"Internal marker"* with the bound description **"Do not edit."**, and
the menu shows them as ordinary toggles between real settings.

| Key | Name (EN / DE) | Harm | Value that causes it | Replacement |
|---|---|---|---|---|
| `[Cards] BoardScaleDefault04Applied` | Internal marker / Interne Marke | Re-arms a one-shot migration. On the next start, any `BoardScale_<board>` sitting at exactly 1.0 is rewritten to 0.4 (`CardsConfig.cs:1296-1310`) — the board changes size on its own. | `false` | Unbind from the menu (`NotOffered`); keep the key. |
| `[Cards] DecisionOffsetYRebased` | Internal marker / Interne Marke | Same shape: re-arms the `DecisionOffset_*.y` re-base (`CardsConfig.cs:1332-1345`). | `false` | `NotOffered` |
| `[Comfort] TableScaleDefault25Applied` | Internal marker / Interne Marke | Same shape: re-arms the 2.5× table-scale migration (`ComfortSettings.cs:414-427`). | `false` | `NotOffered` |
| `[Comfort] SavedScaleMultiplier` | Saved table scale / Gespeicherte Tischgröße | **Not a setting, an output.** It is *"written automatically after each two-grip scale gesture"*. A menu edit is overwritten by the next pinch and only takes effect on a rig rebuild — a control that visibly does nothing. | any | `NotOffered` |
| `[WorldUI] CombatLogUserClosed` | Combat log closed by you / Kampflog vom Nutzer zu | Runtime state of the log's own X button, and the curated row *"Kampflog anzeigen"* already owns it. Two rows for one thing, one of them named after the user's last click. | any | `NotOffered` |

### 2.2 Kill switches for the mod and the VR bootstrap (6)

All six are read once at plugin `Awake` / XR bootstrap (`ConfigCatalog.IsStartupOnly` says so by name),
so **the change lands at the next start — inside a headset that will no longer come up.** There is no
route back except editing a `.cfg` on the desktop.

| Key | Name (EN / DE) | Harm | Value | Replacement |
|---|---|---|---|---|
| `[General] Enabled` | Enable VR mod / VR-Mod aktivieren | *"Set to false to run the game completely vanilla (the mod does nothing)."* The **only** UI that could turn it back on is the VR options tab, which no longer exists. Unrecoverable from inside VR. | `false` | `NotOffered` — keep it as the cfg-file kill switch it is |
| `[Core] RuntimePriority` | Runtime try order / Runtime-Reihenfolge | Dropdown offering `steamvr` / `oculus` / `vdxr`. Choosing a runtime the machine does not have ends XR init: no session, no VR. | `steamvr` on a VDXR-only rig (the test rig) | `NotOffered` |
| `[Core] SkipRuntimeCandidates` | Single runtime attempt / Nur Standard-Runtime | *"Escape hatch"* by its own first word. Removes the candidate failover that makes VR come up at all on most setups. | `true` | `NotOffered` |
| `[Core] InitDelayFrames` | Start delay (frames) / Startverzug (Frames) | Int stepper with **no `AcceptableValueRange`**. Nothing stops a large value; the mod then never initialises. | any large int | `NotOffered` |
| `[General] RuntimeOverride` | OpenXR runtime file / OpenXR-Runtime-Datei | A filesystem path, shown **read-only** in the menu, so it is a dead row a player can neither use nor understand. | — (not editable in-menu) | `NotOffered` |
| `[Stereo] RenderMode` | Stereo render mode / Stereo-Rendermodus | Its own description: *"MultiPass … is the ONLY mode that renders correctly in this game"* — the shipped shaders carry no stereo variants (verified by disassembly, `tools/ShaderDisasm/`). `SinglePassInstanced` is a broken picture, applied at session creation. | `SinglePassInstanced` | Unbind; `const Mode.MultiPass` |

### 2.3 Render-path and input-integrity dials (16)

| Key | Name (EN / DE) | Harm | Value | Replacement |
|---|---|---|---|---|
| `[Rig] ForwardRendering` | Forward rendering / Forward-Rendering | **Currently a CURATED row on Grafik ▸ Darstellung.** Off restores the exact defect it exists to fix — *"transparent effects (fire/torch glow, hex selection ring, health bars) rendering THROUGH walls"* — **and** silently disables a second curated row: `[RenderQuality] MsaaLevel`'s own text says *"Requires the forward rendering path ([Rig] ForwardRendering)"*. It is also start-up-only. | `false` | Unbind; `const true` |
| `[WorldUI] ScreenLayerSplit` | Screen in two layers / Bildschirm zweilagig | Off restores the state the two-layer split was built to end: *"the menu went one-eyed"*. A one-eyed main menu is a broken game. | `false` | Unbind; `const true` |
| `[WorldUI] SuppressPhysicalMouse` | Disable real mouse / Echte Maus deaktivieren | Off lets the stale desktop mouse position *"hover or select map/menu elements behind your back"* — clicks the player neither made nor can see. | `false` | Unbind; `const true` |
| `[RenderQuality] RebuildRigOnMsaaChange` | Rebuild rig on MSAA / Rig-Neubau bei MSAA | Tears down and rebuilds the whole VR rig on every MSAA change. Its own text closes with *"the question this was originally written to settle … is answered … so this is no longer a diagnostic"*. | `true` | Unbind; `const false` |
| `[RenderQuality] ViewportScaleFallback` | Viewport-scale fallback / Viewport-Ersatzskala | Off removes the fallback that makes `EyeResolutionScale` (a curated row) work at all on providers that fix the swapchain at session start. The choice is already made *"by READING THE ALLOCATION BACK, not by guessing"* — there is nothing for a player to decide. | `false` | Unbind; `const true` |
| `[HexHighlight] SwapStableShader` | Stable hex shader / Stabiler Hex-Shader | Off restores the per-eye *"'reflection' that swims with head movement"* — a stereo-rivalry defect. It is also the gate the three `Kill*` fallbacks hang on. | `false` | Unbind; `const true` |
| `[HexHighlight] StableZTest` | Hex decal: ZTest / Hex-Dekal: ZTest | A raw `UnityEngine.Rendering.CompareFunction` **integer on an unbounded stepper**. Only 4 (LEqual) and 8 (Always) are meaningful; `0` = Never = **the hex highlight never draws**, i.e. you cannot see which field you are targeting. Eight of the nine reachable low values are wrong. | `0` | Unbind; `const 4` (8 stays the code's own fallback) |
| `[HexHighlight] StableDepthBias` | Hex decal: depth bias / Hex-Dekal: Tiefen-Bias | A depth-buffer epsilon of 0.0002, stepped in units of 0.000005. Not a preference in any sense a player can hold. | any | Unbind; `const 0.0002f` |
| `[HexHighlight] KillBorderFlame`, `KillCrosshair` | Fallback: flame/crosshair off | Both are explicitly *"FALLBACK (used only when the stable shader swap is off/unavailable)"*. With `SwapStableShader` becoming a constant they are unreachable by construction. | — | Unbind |
| `[HexHighlight] LogMaterialDump` | Log hex material / Hex-Material ins Log | A logging switch. | — | `NotOffered` |
| `[Rig] VoidColor` | Void colour around menus / Leerraum-Farbe um Menüs | Four R/G/B/A numeric steppers — the exact control shape `HasSpecialRow` was written to replace on `KeyColor`. Its own description says it exists *"For DEBUGGING"*. A bright void colour also defeats the Mixed-Reality key. | any non-black | Unbind; `const Color.black` |
| `[Optimize] HeadMaskFromScenarioCamera` | Camera mask from game / Kameramaske vom Spiel | Rewrites the head camera's culling mask from the game's ScenarioCamera, which *"deliberately excludes thirteen"* layers. A wrong mask removes scene content from the headset only. | `true` | `NotOffered` (perf experiment) |

Two more in the same family are **read-only rows** — the player can see them but not change them, so
they cannot break anything from the menu; they are pure noise and should go the same way:
`[Compat] DisableComponents` (*"comma-separated component type full names … to disable"*) and
`[Optimize] HeadCullingMaskDrop` (*"which layers are safe is a MEASUREMENT, not something that can be
guessed"*). `[Optimize] HeadDepthPrepass` joins them as a measured perf experiment.

### 2.4 Mixed-Reality internals whose own descriptions say they are not choices (3)

This is a straightforward bug, not a judgement call. Three `[MixedReality]` entries carry the sentence
**"not offered in the VR menu"** in their own bound description — and are offered anyway, because only
their sibling `HideSkyMeshes` was ever added to `ConfigCatalog.NotOffered`. They also have **no entry
in `Loc.ConfigNames`**, so a German menu shows them as `Opaque Preview Tiles`, `Unseen Region
Membership`, `Unseen Backing Debug Colors`.

| Key | Its own words | Harm |
|---|---|---|
| `[MixedReality] OpaquePreviewTiles` | *"PART OF MIXED REALITY, not a choice beside it (like HideSkyMeshes; not offered in the VR menu)"* | Off makes the fog-of-war stacks blend with passthrough — the defect MR exists to avoid |
| `[MixedReality] UnseenRegionMembership` | *"PART OF MIXED REALITY, not a choice beside it (like HideSkyMeshes; not offered in the VR menu)"* | same |
| `[MixedReality] UnseenBackingDebugColors` | *"DIAGNOSTIC, default off — turn this on only when asked for a screenshot"* | On, it *"deliberately makes the fog-of-war region look wrong"* — flat blue/magenta/red |

**Fix: three lines in `ConfigCatalog.NotOffered`.** The four remaining `Unseen*` geometry dials
(`UnseenSkirtScale`, `UnseenWaferDrop`, `UnseenRimInset`, `UnseenRimTopClearance`) are the same family
one level less absolute — millimetre-scale backing geometry with no localized name — and belong there
too, but they cannot break the game, so they are a §3 item rather than a §2 one.

### 2.5 Developer-only switches (9)

None of these is a preference and several are actively hostile if flipped by a player. They are not
gated behind `[Dev] Enabled` in the menu — every one is an ordinary row.

`[Dev] Enabled`, `[Dev] Overlay`, `[Dev] SimulateHands`, `[Dev] InputDeviceDumpInterval`,
`[WorldUI] DevShowAllPanels`, `[WorldUI] DevForceConvert`, `[Comfort] DebugGizmos`,
`[Hands] TestFist`, `[Cards] DevFakeHand`.

The two sharpest:

* **`[Hands] TestFist`** — *"force a FULL fist (curl 1.0 on all five fingers of both hands) regardless
  of controller input"*. On, **both hands are permanently clenched**: no pointing, no fingertip hex
  touch, no laser origin at the index tip. The mod's whole input vocabulary stops working and the row
  that did it is called *"Debug: Faust erzwingen"*.
* **`[Cards] DevFakeHand`** — spawns N dummy cards into the real card fan.

**Replacement: `NotOffered` for all nine.** They stay hand-editable, which is all a developer needs.

### 2.6 The three most dangerous, if only three are actioned

1. **`[General] Enabled`** — one toggle, in a menu that only exists in VR, that removes VR permanently.
2. **`[Rig] ForwardRendering`** — a *curated* row on the Grafik page whose off state reinstates a
   documented rendering defect and silently kills the MSAA row two lines above it.
3. **`[HexHighlight] StableZTest`** — an unbounded raw-enum integer whose `0` makes the targeting
   highlight invisible; the player has no way to know 4 and 8 are the only legal values.


---

## 3. (b) Would I trust a player with it? — moves between *kuratiert* and *Erweitert*

The test, in his terms, one line per setting: **what does a player gain by touching it, and what is the
worst that happens if they get it wrong?** Advanced is for "nothing they can perceive" or "confusing
but recoverable".

The curated list is **100 rows across 5 tabs**. That is not too many in itself; the problem is that a
handful of them are engineering trades wearing player names, and a handful of genuinely player-facing
settings are buried in Erweitert.

### 3.1 Move OUT of the curated tabs (10 rows, after §2 removals)

| Key | DE row name | Gain if you touch it | Worst case if you get it wrong |
|---|---|---|---|
| `[WorldUI] PanelSupersampleFactor` | Fenster: Schärfegrad | A little more window sharpness when leaning in | *"four times the memory for every doubling"*, 20–90 MB **per window**, up to 7 windows. VRAM exhaustion is not recoverable by looking at it. The **switch** (`Fenster: scharf zeichnen`) stays curated — the factor is its calibration. |
| `[WorldUI] PanelMipLodOffset` | Fenster: Nachschärfen (Filter) | Half a mip level of sharpness | Its own text: *"THE PRICE IS ALIASING"*, and ModBuild 204 took the shipped value back to 0 because the dial *"handed back roughly half of the one fix that closed the STILL case"*. A player has no way to see that trade. |
| `[Compat] DisablePostProcessing` | Post-Processing aus | Possibly nicer colour grading | *"PPv2 is unverified under stereo rendering"*, and it is **start-up-only** — the toggle appears to do nothing, which reads as a broken row. |
| `[Compat] DisableVolumetricFog` | Volumennebel aus | Fog back | Same start-up-only problem; the image effect is a known stereo hazard. |
| `[Core] EnableGraphicsJobs` | Parallele Bildabgabe | Nothing — it is already on | Off is *"the single largest performance finding of the whole project"* thrown away. It writes `boot.config` and needs a restart. |
| `[Core] AutoRestartForGraphicsJobs` | Automatisch neu starten | Skips one automatic relaunch | A row about a one-off boot behaviour the player will meet exactly once, before they ever open this menu. |
| `[WorldUI] TravelButtonOffsetXWindowHeights` | Karte 3D: Reise-Knopf seitlich | Moves one button on one screen of one optional feature | **The user asked for these by place, in his own words: _"Geb mir dann im debug menu die offsets um ihm zu verschieben - ich stell es selber ein."_** They are on the curated Grafik page instead. Same shape as the ruling *"Symbolgrößen gehören ins ERWEITERT Menü!"* that sent the five `[MapRoom]` dials back. |
| `[WorldUI] TravelButtonOffsetYWindowHeights` | Karte 3D: Reise-Knopf Höhe | same | same |
| `[Cards] BoardPitchMin_*` ×3 → **one row** | Neigungslimit unten (°) | The pitch window of one of three board-move schemes | Not dangerous — but three variants are listed for one visible row and the value is a hand-tuned `-31.067`. Keep them curated (the user asked where they live) **but** see §5: a bar cannot return that number. |
| `[Cards] BoardPitchMax_*` ×3 → **one row** | Neigungslimit oben (°) | same | same |

The net effect on the biggest page: **Grafik ▸ Darstellung drops from 25 rows to 17** before the
re-grouping in §4 splits it further.

### 3.2 Promote INTO the curated tabs (9 rows)

Each of these is perceivable, harmless when wrong, and currently only reachable by knowing which
Erweitert topic to open.

| Key | DE row name | Why a player wants it | Proposed home |
|---|---|---|---|
| `[Board] AutoFocusOnTurn` | Automatisch zum Character am Zug | The camera moving on its own is *the* classic VR-comfort complaint, and this is the switch for it. Currently only under Erweitert ▸ Brett & Zielen. | Komfort ▸ Fortbewegung |
| `[Board] HoverHaptics` | Vibration bei Wechsel | Controller rumble is a taste setting every game exposes. | Komfort ▸ Hände & Zielen |
| `[FigureGrab] GrabFigures` | Figuren greifen | A whole headline feature — picking miniatures up — with a single on/off that lives three levels deep. It is also the master the entire `[FigureGrab]` section folds under. | Brett & Karten ▸ Kontrollbrett (or a new "Figuren" section) |
| `[Hands] GhostHandOnFan` | Geisterhand bei Fächer | *"so the hand mesh stops covering card details"* — a directly visible readability choice. | Avatar ▸ Dein Auftritt |
| `[Hands] GhostHandOnHeldCard` | Geisterhand bei Karte | same | Avatar ▸ Dein Auftritt (folds under the row above) |
| `[Hands] GhostHandStrength` | Geisterhand-Stärke | The amount of the two above; already correctly clamped 0.05–0.95 so the hand can never vanish. | Avatar ▸ Dein Auftritt (folds) |
| `[WorldUI] BarFixedSize` | Balken: Abstand ignorieren | It is the third member of the health-bar family whose other two (`Größe`, `hinter Wänden`) are already curated in Tafeln ▸ Lebensbalken. A family that shares a heading should share a page. | Tafeln ▸ Lebensbalken |
| `[PeerBoardFade] Mode` | Boards vor dem Spielfeld | **New in ModBuild 222 and shipped `Off`.** A player whose view of the field is blocked by a team-mate's board has a fix and will never find it. | Avatar & Mehrspieler ▸ Zusammen spielen |
| `[General] LogLevel` | Protokoll-Detailstufe | Not a preference — a *support* control. Every bug report starts with "set this to Trace". Worth one row rather than a hunt. | Erweitert ▸ System (keep) **or** a single row at the bottom of Erweitert's landing page |

### 3.3 Everything else: fit for a player as it stands

The remaining ~88 curated rows pass the test. Spot-checking the ones a reviewer would query:

* `[MixedReality] Enabled` / `KeyColor` — MR is a headline feature and the key colour is already a
  named preset dropdown, not four RGB steppers.
* `[Elements] ResponseStrength`, `[Haunt] Frequency`, `[EnvSound] Gain`, `[EnvSound] AmbienceBedGain` —
  each is the *amount* of a feature the user personally asked for, each folds under its own switch,
  and 0 is a safe, meaningful end of every one of them.
* `[Comfort] ScaleMin` / `ScaleMax` — bounds on a gesture, both clamped, both recoverable by Recenter.
* `[Net] VersionGuard` stays in Erweitert (ruling 14 — an expert escape hatch). Confirmed correct.
* `[WorldUI] ModalStyle` (`window` / `screen`) stays in Erweitert. `screen` is the documented fallback
  path, not a look.

### 3.4 Two hygiene items that block (b) from being judged at all

1. **12 offered rows have no `Loc.ConfigNames` entry**, so a German menu captions them with the
   spaced-out English key. Four of them are *curated* rows on the Komfort tab:
   `[Comfort] FlightEnabled`, `FlightDirection`, `FlightHand`, `FlightMaxSpeed` — they carry
   hand-written curated captions so the **Komfort tab is fine**, but on **Erweitert ▸ Bewegung & Welt**
   the same four appear as `Flight Enabled`, `Flight Direction`, `Flight Hand`, `Flight Max Speed`.
   The other eight are `[FigureGrab] StretchReachMillimeters` and the seven `[MixedReality]` internals
   from §2.4 / §3.1.
2. **`[Compat] WallFade` is listed twice in `Curated`** — Komfort ▸ Sichtbarkeit *and* Grafik ▸
   Darstellung — deliberately, per a user ruling quoted in the source (*"the wall see-through … must
   be findable HERE, not only under Grafik"*). That is fine and should be **kept**, but it is the only
   duplicated row in the file and §4 must not quietly undo it.


---

## 4. (c) Categories — current vs proposed

### 4.1 Current structure (read from `VROptionsTab.4.Curated.Curated`, row counts measured)

```
Komfort                     (24 rows)
  Drehen                     4
  Fortbewegung               6
  Welt greifen               8
  Sichtbarkeit               2      <- Wände durchsichtig (+ Festungs-Aufbauten)
  Hände & Zielen             5
Grafik                      (30 rows)
  Darstellung               25      <- see below
  Mixed Reality              2
  Leistung                   2
  Monitor                    1
Brett & Karten              (18 rows)
  Kontrollbrett             11      (6 of them are the 3 board variants of one pitch pair)
  Karten                     4
  Stapel & Hinweise          3
Tafeln                      (16 rows)
  Tafeln & Anzeigen          6
  Lebensbalken               2
  2D-Schirm                  3
  Klick & Zeigen             4
  Texteingabe                1
Avatar & Mehrspieler        (12 rows)
  Dein Auftritt              7      (3 of them are the hand-style variants of one size row)
  Zusammen spielen           5
Erweitert                   (the catalog's own topic index — 540 entries in 11 live topics)
  Bewegung & Welt 21 · Hände & Figuren 83 · Karten & Fächer 98 · Menüs & Tafeln 56 ·
  Brett & Zielen 14 · Steuerbrett (pro Brett) 146 · Bild & Darstellung 41 · Tasten 29 ·
  Mehrspieler 13 · System & Start 9 · Messung & Diagnose 30 · Sonstiges 0
```

### 4.2 What is wrong with it

**Finding 1 — "Grafik ▸ Darstellung" is a 25-row grab-bag of four unrelated families.** This is
precisely the fault the 2026-08 audit (05, S7) fixed for Komfort — *"fourteen rows from three sense
families under one heading"* became Drehen / Fortbewegung / Welt greifen — and it has grown back on the
next tab over. The 25 rows are:

| family | rows |
|---|---|
| render quality | `EyeResolutionScale`, `MsaaLevel`, `ForceAnisotropic`, `PixelLightCount`, `ForwardRendering` |
| floated-window sharpness | `PanelSupersample`, `PanelSupersampleFactor`, `PanelMipLodOffset`, `WindowLegibility` |
| game-compat fixups | `DisablePostProcessing`, `DisableVolumetricFog`, `WallFade` |
| the world you stand in | `Sky/Style`, `Experimental3DMap`, `MapRoomHand`, `TravelButtonOffsetX/Y` |
| the world's mood | `Elements` ×2, `Haunt` ×2 |
| **sound** | `EnvSound/Enabled`, `Gain`, `AmbienceBed`, `AmbienceBedGain` |

**Finding 2 — the mod's entire audio surface is filed under "Grafik".** Five sound settings exist:
four `[EnvSound]` rows under **Grafik ▸ Darstellung**, and `[Cards] CardSoundsEnabled` under **Brett &
Karten ▸ Karten**. Nobody looks for a volume slider under Graphics. In Erweitert it is worse: because
`[EnvSound]` binds on `rig.cfg`, `ConfigCatalog.TopicOf` sends it to the topic literally named
**"Bild & Darstellung" / "Picture & rendering"**. There is no `Ton` topic anywhere in the menu.
_(read from `ConfigCatalog.TopicOf` + `Loc.cs cfg_topic_visual`)_

**Finding 3 — the environment family is split three ways in Erweitert.** `[MixedReality]` (9),
`[RenderQuality]` (6), `[WallFade]` (6), `[MapRoom]` (5), `[EnvSound]` (4) and `[Compat]` (3) each
clear `MinClusterSize` and get their own heading; `[Sky]` (1), `[Elements]` (2), `[Haunt]` (2) and
`[Rig]` (2) do **not** and are swept into the "Allgemein" collector by `FoldSmallGroups`. So the
environment *chooser* (`[Sky] Style`) ends up in a grab-bag two rows away from the environment's own
mood dials. _(computed from `ConfigCatalog.FoldSmallGroups` / `MinClusterSize = 3`)_

**Finding 4 — "Sichtbarkeit" is a two-row section that exists to satisfy one ruling.** It holds
`Compat/WallFade` (duplicated from Grafik by design) and `WallFade/StackedShellFade`. Two rows do not
need a heading of their own inside a five-section tab; they need a home where "seeing the board" is the
subject.

**Finding 5 — Erweitert's biggest page is "Steuerbrett (pro Brett)" at 146 entries**, which is correct
(it has a hand-arranged tree, `VROptionsTab.6.BoardTopic.cs`) and is not a problem. `Sonstiges` is
empty — good, the fallback never fires.

### 4.3 Proposed structure

Tab order keeps the audited principle (**body → picture → world → play surface → panels → social →
tuning**) and inserts one new tab where the two worst findings live.

```
1  Komfort                       body                          (23 rows)
     Drehen                       4   unchanged
     Fortbewegung                 7   + [Board] AutoFocusOnTurn                       (§3.2)
     Welt greifen                 8   unchanged
     Hände & Zielen               6   + [Board] HoverHaptics                          (§3.2)
                                      - "Sichtbarkeit" section dissolved              (Finding 4)
                                        [Compat] WallFade keeps its Komfort row, moved
                                        into "Hände & Zielen"? NO — see the ruling note below.

2  Bild                          picture only                  (11 rows)
     Darstellung                  4   EyeResolutionScale, MsaaLevel, ForceAnisotropic, PixelLightCount
     Fenster & Tafeln             2   PanelSupersample, WindowLegibility               (factor+mip → Erweitert, §3.1)
     Mixed Reality                2   unchanged
     Monitor                      1   unchanged
     Spielanpassung               2   DisablePostProcessing, DisableVolumetricFog      (or → Erweitert, §3.1)

3  Umgebung & Ton    ** NEW **   the world you are in          (13 rows)
     Umgebung                     3   [Sky] Style, [Elements] EnvironmentResponse, ResponseStrength
     Grusel                       2   [Haunt] EasterEggs, Frequency
     Ton                          5   [EnvSound] Enabled, Gain, AmbienceBed, AmbienceBedGain,
                                      [Cards] CardSoundsEnabled                        (Finding 2)
     Sichtbarkeit                 2   [Compat] WallFade, [WallFade] StackedShellFade   (Finding 4)
     Karte 3D                     2   [Rig] Experimental3DMap, [WorldUI] MapRoomHand

4  Brett & Karten                play surface                  (15 rows)
     Kontrollbrett                7   Board, TrayScale, TrayFollow, BoardMoveMode,
                                      Neigungslimit unten/oben, SpawnLeftOfHead
     Figuren                      1   [FigureGrab] GrabFigures                         (§3.2)
     Karten                       4   unchanged
     Stapel & Hinweise            3   unchanged

5  Tafeln                        panels                        (17 rows)
     Tafeln & Anzeigen            6   unchanged
     Lebensbalken                 3   + [WorldUI] BarFixedSize                         (§3.2)
     2D-Schirm                    3   unchanged
     Klick & Zeigen               4   unchanged
     Texteingabe                  1   unchanged

6  Avatar & Mehrspieler          social                        (16 rows)
     Dein Auftritt               10   + GhostHandOnFan, GhostHandOnHeldCard, GhostHandStrength  (§3.2)
     Zusammen spielen             6   + [PeerBoardFade] Mode                           (§3.2)

7  Erweitert                     the catalog's own topic index
     + one new topic: "Ton" — everything on the [EnvSound] and card-sound side,
       so the Erweitert index stops filing audio under "Bild & Darstellung"  (Finding 2)
     + [Sky]/[Elements]/[Haunt] move out of "Bild & Darstellung ▸ Allgemein" into
       a "Umgebung" heading — mechanically, by giving Umgebung its own ConfigTopic
       rather than by hand-listing keys                                        (Finding 3)
```

**Per-move justification, one line each:**

| Move | Why |
|---|---|
| Grafik → **Bild** (rename) + split | 25 rows of four families under one heading is the exact fault the audit fixed on Komfort. |
| `[EnvSound]` ×4 + `[Cards] CardSoundsEnabled` → **Umgebung & Ton ▸ Ton** | Volume settings do not live under "Graphics"; this is the mod's only audio surface and it currently has no home. |
| `[Sky] Style`, `[Elements]` ×2, `[Haunt]` ×2 → **Umgebung** | These are properties *of the world you chose*, not of how it is rasterised — the curated file already argues exactly this ("both are properties OF the environment the row above chooses") and then leaves them on a render page. |
| `[Compat] WallFade` + `StackedShellFade` → **Umgebung & Ton ▸ Sichtbarkeit** | Removes a two-row section from Komfort without losing it, and puts "seeing through walls" beside "which world am I in". |
| `[Rig] Experimental3DMap` + `MapRoomHand` → **Umgebung & Ton ▸ Karte 3D** | Same argument as Sky: which world you stand in. `MapRoomHand` continues to fold under it. |
| `PanelSupersampleFactor`, `PanelMipLodOffset` → **Erweitert** | §3.1 — memory and aliasing trades a player cannot see the price of. |
| `TravelButtonOffsetX/Y` → **Erweitert** | The user asked for them in the debug menu, verbatim; same shape as the `Symbolgrößen` ruling. |
| `AutoFocusOnTurn`, `HoverHaptics`, `GrabFigures`, `GhostHand*`, `BarFixedSize`, `PeerBoardFade/Mode` → **kuratiert** | §3.2 — perceivable, harmless, currently unfindable. |
| New **Ton** and **Umgebung** topics in Erweitert | Erweitert is the catalog's index; adding two `ConfigTopic` members is one table line each in `ConfigCatalog.TopicOf` and fixes the same misfiling one level down, permanently. |

**RULINGS THAT MUST SURVIVE THIS (checked, none violated):**

* *"Symbolgrößen gehören ins ERWEITERT Menü!"* — the five `[MapRoom]` size dials stay in Erweitert.
  This proposal does not touch them and explicitly forbids re-promoting them.
* *"'Wände mit Spielern synchronisieren' sollte genau da verortet sein"* — `[WallFade] SyncPeerFades`
  stays in Avatar & Mehrspieler ▸ Zusammen spielen. Unchanged.
* *"the wall see-through … must be findable HERE, not only under Grafik"* — **this proposal moves
  WallFade's Komfort row into the new Umgebung tab and keeps its second row where "Grafik" was.**
  That is a change to a standing ruling and is listed as an open question in §7, not decided here.
* *"Die Initativreihenfolge ausschalten zu können am Controllboard macht keinen Sinn"* — no readout
  toggle is re-introduced.
* *"Setze erstmal alle Vorschläge zu den Settings deinerseits so um"* — the tab-order principle
  (body → picture → … → rest) is preserved, with "world" inserted between picture and play surface.


---

## 5. (d) The control widget, per setting

His words: *"nicht jedes Feld macht Sinn mit einer verschiebaren Bar besonders wenn man bis auf die
Kommastellen etwas anpassen will."*

He is right, and there is evidence for it **in his own dropped config file**.

### 5.1 The evidence: what a drag bar wrote into the shipped defaults

Nine slider rows carry a value with five or more decimals in `.planning/debug/default/` — a shape no
human types and no stepper produces (a stepper always lands on a multiple of its step). Two of them are
the smoking gun:

```
[Cards] ActiveCardScale_Oak   = 0.9999998      (he was trying to get back to 1.0)
[Cards] ClusterScale_Oak      = 0.9999999      (he was trying to get back to 1.0)
[Cards] DecisionGap_Oak       = 0.01367789
[Cards] DecisionGap_Steel     = 0.01801924
[Cards] DecisionGap_Bronze    = 0.01733499
[Comfort] FlightMaxSpeed      = 1.43362        <- curated row
[Haunt] Frequency             = 0.2162736      <- curated row
[Net] MaskSize                = 1.29148        <- curated row
[WorldUI] BarSizeScale        = 0.7089906      <- curated row
```

`0.9999998` and `0.9999999` are not float round-trip noise: float32 stores `1.0` exactly. They are two
separate attempts to put a bar back on its default that **could not be made**. Several of these values
were then frozen into `Defaults.*.cs` by `scripts/rebase-defaults.py`, so the bar's inability to hit a
round number is now part of the shipped product.

**Mechanism, read from source:** `BuildSliderRow` sets `slider.wholeNumbers = item.Integral`. For a
`float` entry that is `false`, so **a float slider has no grid at all** — infinitely many reachable
values, none of them repeatable, and no way to return to one you liked.

### 5.2 The second finding: the hand-written step table is inert for two thirds of its entries

`ConfigSteps.Explicit` is 23 lines of individually-argued judgements ("snap turning steps 15° because
15/30/45/60/90 are the angles anyone actually wants"). But `BuildRow` gives any bounded scalar a
**slider**, and a slider never reads the step. **16 of the 23 written-down steps are dead code:**

| key | written step | widget today | used? |
|---|---|---|---|
| `Comfort/SnapTurnDegrees` | 15° | Slider | **inert** |
| `Comfort/SmoothTurnSpeed` | 10 °/s | Slider | **inert** |
| `Comfort/RecenterHoldSeconds` | 0.1 s | Slider | **inert** |
| `Comfort/ScaleMin`, `ScaleMax` | 0.05 | Slider | **inert** |
| `RenderQuality/EyeResolutionScale` | 0.05 | Slider | **inert** |
| `RenderQuality/PixelLightCount` | 1 | Slider (int) | **inert** |
| `Cards/InspectScale` | 0.05 | Slider | **inert** |
| `Cards/CardWidth` | 0.005 | Slider | **inert** |
| `WorldUI/HoverInfoScale` | 0.05 | Slider | **inert** |
| `WorldUI/ScreenWidth`, `ScreenDistance` | 0.1 m | Slider | **inert** |
| `WorldUI/BarSizeScale` | 0.05 | Slider | **inert** |
| `Net/MaskSize` | 0.05 | Slider | **inert** |
| `Rig/WorldTiltDegrees` | 5° | Slider | inert (row is parked anyway) |
| `Net/MaskId` | 1 | Preset dropdown | inert (correctly — it is a named list) |
| `Cards/TrayScale`, `Hands/{Glove,Plate,Arcane}Scale`, `Hands/CurlInputFullAt` | 0.05 | Stepper | **used** |
| `WorldUI/TravelButtonOffset{X,Y}WindowHeights` | 0.01 | Stepper (`PrefersStepper`) | **used** |

The file's own header says this table exists because a badly derived step *"shipped a dial the user
reported as having no effect"*. Half of it never runs.

### 5.3 The third finding: 15 steppers keep moving past a clamp the code applies at read time

These entries declare **no `AcceptableValueRange`**, so the stepper's arrows never stop — but the code
clamps the value when it reads it. Past the clamp, every press changes the number on screen and nothing
in the world. That is the exact `"der X-Offset hat keinen Einfluss"` failure the project has already
been reported for twice.

| key | declared range | clamp applied at read | curated? |
|---|---|---|---|
| `[Hands] GloveScale` / `PlateScale` / `ArcaneScale` | none | `Mathf.Clamp(…, 0.2f, 3f)` — `HandVisuals.cs:168` | **yes — "Handgröße"** |
| `[Cards] TrayScale` | none (description claims "0.5–2") | `Mathf.Clamp(…, 0.5f, 2f)` — `CardsConfig.cs:1642` | **yes — "Brett: Größe"** |
| `[Hands] CurlInputFullAt` | none | `Mathf.Clamp(…, 0.3f, 1f)` — `HandsConfig.cs:85` | **yes — "Vollgriff-Hilfe"** |
| `[Hands] CurlProximal` / `CurlMiddle` / `CurlTip` | none | `Mathf.Clamp(…, 0f, 130f)` | no |
| `[Hands] GlovePinkyCounterAbduction` | none | `Mathf.Clamp(…, −30f, 30f)` | no |
| `[Board] AoeFlickThreshold` | none | `Mathf.Clamp(…, 0.2f, 0.95f)` — `AoeControl.cs:197` | no |
| `[WorldUI] CombatLogScale` | none | `Mathf.Clamp(…, 0.5f, 2f)` | no |
| `[WorldUI] ScreenDepthStrength` / `ScreenParallaxScale` / `VideoDepth` | none | `Mathf.Clamp` in `FlatScreenStereo.2` | no |
| `[Cards] TrayPitch` | none | clamped against the live pitch window | no |

**Fix: declare the clamp as the bind's `AcceptableValueRange`.** One argument per Bind call; the
number is already written down two files away. It costs nothing and it makes the row honest — and it
also lets the row become a bar+arrows control (below), because `HasRange` is what `BuildRow` tests.

### 5.4 What the codebase can already build, and what is new work

| control | builder | status |
|---|---|---|
| Toggle (game's own switch, with "Ein/Aus" caption) | `BuildBoolRow` | ships |
| Dropdown over the entry's acceptable values | `BuildChoiceRow` | ships |
| Dropdown over a hand-written named list | `BuildPresetRow` | ships — used by `KeyColor`, `BoardMoveMode`, `MaskId`, `Sky/Style` |
| Slider (value readout bound to every label in the row) | `BuildSliderRow` | ships |
| Stepper `◀ value ▶`, step from `ConfigSteps`, arrow art harvested from the game's dropdown | `BuildStepperRow` / `BuildArrow` | ships |
| Read-only label | `ConfigKind.ReadOnly` | ships |
| **Bar + fine arrows in one row** | — | **NEW** — small: place the harvested `_sliderControl` and two `BuildArrow`s in the same `Option` holder's `HorizontalLayoutGroup`. Both pieces exist. |
| **Press-and-hold repeat on an arrow** | — | **NEW** — `BuildArrow` is a plain `Button.onClick`; a repeat needs a pointer-down/up timer component. `ConfigSteps` already notes the absence in prose. |
| **Numeric text entry** | — | **NEW and not recommended** — the mod has a VR keyboard (`[Keyboard] AutoCapitalise` exists), but typing a number at arm's length is worse than arrows for every case in this menu. |

### 5.5 Proposal, per widget class

**(1) Bounded number a player tunes to a value they want back → BAR + ARROWS (26 rows).**
The bar for the coarse gesture, the arrows for the last step, the number in between. This single new
row builder resurrects all 16 dead `ConfigSteps.Explicit` entries at once and answers the whole of his
question (d).

`RenderQuality/EyeResolutionScale` · `WorldUI/BarSizeScale` · `Net/MaskSize` · `WorldUI/ScreenWidth` ·
`WorldUI/ScreenDistance` · `Cards/CardWidth` · `Cards/InspectScale` · `WorldUI/HoverInfoScale` ·
`WorldUI/WindowLegibility` · `WorldUI/PanelSupersampleFactor` · `WorldUI/PanelMipLodOffset` ·
`Elements/ResponseStrength` · `EnvSound/Gain` · `EnvSound/AmbienceBedGain` · `Haunt/Frequency` ·
`Comfort/RecenterHoldSeconds` · `Comfort/ScaleMin` · `Comfort/ScaleMax` · `Comfort/SmoothTurnSpeed` ·
`Comfort/FlightMaxSpeed` · `Cards/BoardPitchMin_{Oak,Steel,Bronze}` ·
`Cards/BoardPitchMax_{Oak,Steel,Bronze}`

The two most urgent are the two whose shipped values he cannot currently reproduce:
`Cards/BoardPitchMin_Steel = −31.067` and `BoardPitchMax_Steel = 54.353`, on a bar spanning
−85 … +85 (171 whole degrees). One accidental brush and the tuning is gone with no way back.

**(2) Bounded number with ≤10 useful values → NAMED PRESETS or ARROWS, never a bar.**

| key | today | values | proposed | why |
|---|---|---|---|---|
| `[Comfort] SnapTurnDegrees` | Slider 15…90 | **6** at the written 15° step | **Preset dropdown** `15° / 30° / 45° / 60° / 90°` | `ConfigSteps` already says those are *"the angles anyone actually wants"*. A six-position bar is a dropdown drawn badly. `BuildPresetRow` ships. |
| `[RenderQuality] PixelLightCount` | Slider (int) −1…8 | **10** | **Stepper**, with −1 rendered as "Spiel-Standard" | `−1` means "the game's own value" — a bar cannot label a magic value at one end. |
| `[RenderQuality] MsaaLevel` | Dropdown (0/2/4/8) | 4 | **keep** | already correct — the acceptable-value list does the work |
| `[Net] MaskId` | Preset dropdown | 3 | **keep** | already fixed by user request 2026-08-09 |
| `[Cards] BoardMoveMode`, `[Sky] Style`, `[MixedReality] KeyColor` | Preset dropdown | — | **keep** | already correct |

**(3) Steppers that need too many presses → the same bar+arrows, or hold-to-repeat.**
`BuildArrow` does not repeat when held (`Button.onClick`), so a press is a press. The only offered row
that is genuinely painful is `[WorldUI] TravelButtonOffsetYWindowHeights`: **121 presses** to cross
−0.6 … +0.6 at its 0.01 step. Its sibling X is 51. Both are `PrefersStepper` **by explicit user ruling**
(*"sollen keine Schieberegler sein, sondern die Pfeile, wo man den echten Wert einfach einstellen
kann"*) — so **do not give these two a bar**. Give them hold-to-repeat instead, or narrow Y's range.

**(4) The 8 read-only text rows are dead controls.** A row the player can look at but not change is
worse than no row. Five of them are a closed set:

`[Cards] FanRevealSound`, `FanHideSound`, `CardGrabSound`, `CardPlaceSound`, `CardTakeBackSound` — the
values are game sound-method names (`PlaySound_EnemyCardDraw`, …). Give them an
`AcceptableValueList`/`CuratedChoices` entry and they become dropdowns for free (the mechanism exists —
`ConfigCatalog.CuratedChoices` already does this for `RuntimePriority`, `PrimaryHand`, `ModalStyle`,
`RevealMode`). The other three (`Compat/DisableComponents`, `General/RuntimeOverride`,
`Optimize/HeadCullingMaskDrop`) are free-form and belong in `NotOffered` (§2.3).

**(5) `[Rig] VoidColor` is four numeric steppers (R/G/B/A)** — the exact failure `HasSpecialRow` was
written for on `MixedReality/KeyColor`. Its own description says it exists *"For DEBUGGING"*. Delete it
(§2.3) rather than build it a fifth preset dropdown.

**(6) Toggles, dropdowns and the 56 Vector3 pose rows need no change.**
The Vector3 rows render as three stepper rows (`… · X`, `… · Y`, `… · Z`) at a uniform 0.01 m per press
thanks to `UnitScope.Component` — that is the correct control for a pose and the step work behind it is
already right.

### 5.6 Widget summary, offered rows only

| widget today | count | proposed change |
|---|---|---|
| Stepper (scalar) | 190 | 15 gain a declared range (§5.3); the rest keep the arrows — an unbounded number has no bar to sit on |
| Slider | 136 | 26 → bar+arrows; 1 → preset dropdown; 1 → stepper; the other 108 keep the bar (they are Erweitert tuning where a gesture is the right control) |
| Toggle | 119 | no widget change (23 of them deleted per §2) |
| Stepper ×2/×3/×4 (vector/colour) | 60 | no change (the one ×4 row, `[Rig] VoidColor`, is deleted per §2.3) |
| Dropdown (enum / list / preset / curated) | 26 | +1 (`SnapTurnDegrees`), +5 (card sounds); −4 deleted per §2 |
| Read-only text | 8 | 5 → dropdown, 3 → not offered |
| Stepper (`PrefersStepper`) | 2 | hold-to-repeat |


---

## 6. Ranked plan

### Tier 1 — do first: cheap, safe, and each one closes a real hole (half a day)

| # | Change | Cost | Risk |
|---|---|---|---|
| 1 | Add the **39 §2 entries to `ConfigCatalog.NotOffered`** (or unbind the 8 true constants). One dictionary, one line each with the reason written beside it — exactly the shape the file already has. | ~40 lines | **None.** Nothing is unbound that a cfg hand-edit still needs; the file round-trips unchanged. |
| 2 | **Declare the 15 missing `AcceptableValueRange`s** (§5.3) at their Bind sites, using the clamp the code already applies. | 15 one-argument edits | Very low. BepInEx clamps a persisted out-of-range value on load, and every one of these was already clamped at read — so no live behaviour changes. **Verify** no dropped cfg carries a value outside the clamp before shipping. |
| 3 | **Add the 12 missing `Loc.ConfigNames` entries** (§3.4) so no offered row shows an English key in a German menu. | 12 lines | None. |
| 4 | **Give the five card-sound strings a `CuratedChoices` entry** so they stop being dead read-only rows. | 1 switch arm | None — `CuratedChoices` only offers the set when the current value is a member. |

### Tier 2 — the widget work, which is the heart of (d) (1–2 days)

| # | Change | Cost | Risk |
|---|---|---|---|
| 5 | **New row builder: bar + fine arrows.** Place `_sliderControl` and two `BuildArrow`s in one `Option` holder; bind both to the same entry; keep `BindValueLabels` as the readout. Route the 26 §5.5(1) keys to it (a `PrefersBarAndArrows` table beside the existing `PrefersStepper`). | one builder + one table | Low, and **it is the change that makes 16 dead `ConfigSteps` entries live again.** Test the value label does not drift between the two inputs. |
| 6 | `SnapTurnDegrees` → preset dropdown; `PixelLightCount` → labelled stepper. | 2 table entries + a name list | None — `BuildPresetRow` ships. |
| 7 | Hold-to-repeat on `BuildArrow` for the two `PrefersStepper` rows (121 presses today). | small component | Low. Do **not** convert them to bars — standing user ruling. |

### Tier 3 — the re-grouping (§4), which needs a ruling before it is written (1 day)

| # | Change | Cost | Risk |
|---|---|---|---|
| 8 | Move the 10 §3.1 rows out of curated and the 9 §3.2 rows in. | edits to one array | Low — *"a curated row is an extra door, never a wall"*; nothing leaves the catalog. |
| 9 | New tab **Umgebung & Ton**; Grafik → **Bild** and split. | one array + ~6 Loc keys | Medium — it moves rows the user has already been shown. **Needs his ruling (§7).** |
| 10 | Two new `ConfigTopic` members (`Ton`, `Umgebung`) + their `TopicOf` lines and `Loc` labels. | ~8 lines | Low. `ConfigCatalog`'s own doc says the enum is menu-internal and safe to reorder; `MaxGroupsPerTopic` has headroom. |

### Risky / do not do without asking

* Anything that **narrows an existing range** downward past a value the user has set. Nothing in this
  document proposes one, but Tier-1 item 2 is the closest to it — check the live cfg first.
* Moving `[Compat] WallFade`'s Komfort row (§4.3) — it is there by a standing ruling.
* Re-promoting the five `[MapRoom]` size dials — explicitly forbidden by *"Symbolgrößen gehören ins
  ERWEITERT Menü!"*.
* Touching `[WorldUI] TravelButtonOffset{X,Y}`'s **values or ranges**. Three placements were rejected in
  a row; both defaults are 0 and must stay 0. Only their *location in the menu* and their *arrow
  repeat* are on the table.

### Housekeeping, unranked

* **`[PeerBoardFade]`'s six defaults are literals at the Bind call**, not in `Defaults.*.cs`
  (`PeerBoardFade.cs:58-83`). `scripts/rebase-defaults.py` will report every one of them **UNMAPPED**,
  so a tuned cfg drop for the new peer-board fade would silently fail to re-base. Six lines in
  `Defaults.Net.cs` fixes it. _(read from the script's own doc: "a cfg entry it cannot map to exactly
  one annotated Defaults line is REPORTED, not silently skipped")_
* **105 orphan keys** sit in the user's cfg files (50 in `worldui.cfg`, 18 each in `hands`/`wristhud`,
  …). They bind to nothing and are invisible in the menu, so this is cosmetic — but a `.cfg` a human
  reads is half dead settings.

---

## 7. Open questions for the user — only the ones that change the outcome

1. **Darf "Wände durchsichtig" (`[Compat] WallFade`) aus dem Komfort-Tab in einen neuen Tab
   "Umgebung & Ton" wandern?**
   Deine frühere Anweisung war: die Wand-Durchsicht ist komfortrelevant und muss **im Komfort-Tab**
   auffindbar sein, nicht nur unter Grafik. Mein Vorschlag gibt ihr eine eigene Sektion "Sichtbarkeit"
   im neuen Umgebungs-Tab. Wenn dir das zu weit weg ist, bleibt die Doppelzeile in Komfort stehen —
   sie kostet nichts, weil beide Zeilen denselben Wert schreiben.

2. **Soll es den neuen Tab "Umgebung & Ton" überhaupt geben, oder lieber nur eine Sektion "Ton"
   innerhalb von Grafik?**
   Das eigentliche Problem ist, dass deine fünf **Ton**-Einstellungen unter *Grafik* liegen
   (und im Erweitert-Menü sogar unter "Bild & Darstellung"). Das lässt sich billig lösen (eine neue
   Sektion) oder gründlich (ein eigener Tab, der auch Umgebung, Grusel und Karte 3D aufnimmt und den
   25-Zeilen-Block "Darstellung" halbiert). Ich empfehle den Tab; die Entscheidung ist deine.

3. **`[General] Enabled` ("VR-Mod aktivieren") — aus dem Menü nehmen, ja?**
   Das ist der einzige Schalter, der VR endgültig abschaltet, und danach gibt es kein VR-Menü mehr, in
   dem man ihn zurückstellen könnte. Mein Vorschlag: Schlüssel bleibt in der `.cfg` (dort ist er
   sinnvoll), Zeile verschwindet aus dem Menü. Falls du ihn im Headset behalten willst, brauchst du
   stattdessen eine Sicherheitsabfrage — das wäre neue Arbeit.

4. **Dürfen die vier Grafik-Schalter `Forward-Rendering`, `Post-Processing aus`, `Volumennebel aus`,
   `Parallele Bildabgabe` aus dem kuratierten Grafik-Tab verschwinden?**
   Alle vier wirken **erst nach einem Neustart** und drei davon machen im Aus-Zustand etwas kaputt, das
   der Mod bewusst repariert. Sie sind heute prominent platziert und sehen aus wie Qualitätsregler.
   Mein Vorschlag: `Forward-Rendering` ganz löschen (Konstante), die anderen drei nach Erweitert.

5. **Bar + Pfeile in einer Zeile — ist das die Bedienform, die du meinst?**
   Deine Frage (d) beschreibt genau dieses Problem, und deine eigene alte Anweisung zu den
   Reise-Knopf-Offsets sagt: *"die Pfeile, wo man den echten Wert einfach einstellen kann"*. Ich
   schlage vor, **beides** in eine Zeile zu setzen: Balken zum groben Ziehen, Pfeile für die letzte
   Kommastelle, Zahl dazwischen. Alternative wäre, die 26 betroffenen Zeilen komplett auf reine Pfeile
   umzustellen (einfacher zu bauen, aber grobes Verstellen dauert dann lange). Welche willst du?

