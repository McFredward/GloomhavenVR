# LANE-MAPHOVER-363 — what this lane needs from files it does not own

> ### ✅ CONSUMED — audited 2026-09-08 against `dev` = `49ceab21` (ModBuild 483)
>
> All three items landed. `[MapRoom] HoverAnimation` is bound at `src/GloomhavenVR/Plugin.cs:704`
> with the default this lane asked for, and both Loc rows exist —
> `Core/Loc/Loc.ConfigNames.cs:296` and `Core/Loc/Loc.ConfigDescriptions.German.cs:1289`. Nothing
> here is outstanding; the file is kept as the record of the request and its reasoning.

Branch: `lane/maphover-363`, based on `c72bcc53` (ModBuild 363).

Request (user, 2026-09-03, verbatim):

> "Bitte deaktiviere die animationen für das mouseover im Kartenraum wenn ich über ein
> Kartensymbol hovere - an der Stelle möchte ich es nicht."

The lane ships one new config key, **`[MapRoom] HoverAnimation`** (bool, default `false` = his
request). It is curated onto **Umgebung & Ton ▸ Karte 3D**, directly under `[WorldUI] MapRoomHand`,
with an EMPTY caption key — i.e. the row is captioned by the entry's localized display name and
described by the German description table, exactly like `MapRoomHand` above it.

`Core/Loc/**` is owned by another lane. Everything below is the full patch text for it.

**Nothing here is required for the build to be green.** `check-options-coverage.py` passes as
shipped, because an empty caption key is the documented fallback. Without these two entries the row
simply reads `Hover Animation` (the spaced-out key — `ConfigCatalog.Describe`'s fallback) and
carries the ENGLISH `ConfigDescription` from `Plugin.cs`. That is readable but it is English in a
German menu, which is the exact thing `Loc.ConfigNames.cs` exists to prevent.

I checked first whether an existing map-room family could carry this without touching Loc: it
cannot. The "Karte 3D: …" caption family lives entirely in `Loc.ConfigNames.cs`, and the curated
`sec_map3d` section's own rows already use the empty-caption / display-name shape — so extending
that pattern IS adding a display-name line. One entry in each of the two tables is the minimal ask.

---

## 1. `src/GloomhavenVR/Core/Loc/Loc.ConfigNames.cs`

Insert immediately AFTER the existing `["MapRoom/PathWidthScale"]` entry (currently the last of the
five "Karte 3D: …" size names, just before the `["WorldUI/MapRoomHand"]` comment block).

```csharp
            // THE ONE MAP-ROOM ROW THAT IS NOT A SIZE (ModBuild 364, user 2026-09-03: "Bitte
            // deaktiviere die animationen für das mouseover im Kartenraum wenn ich über ein
            // Kartensymbol hovere - an der Stelle möchte ich es nicht."). Same "Karte 3D: …"
            // family as the five dials above, because the player finds it under the same heading;
            // the second half says WHICH thing rather than which map, for the same reason
            // PartyMarkerScale and PathWidthScale do. 26 characters including the prefix, inside
            // the ~28 the caption column keeps before it clips.
            ["MapRoom/HoverAnimation"] =
                Pair("3D map: symbol animation", "Karte 3D: Symbol-Animation"),
```

## 2. `src/GloomhavenVR/Core/Loc/Loc.ConfigDescriptions.German.cs`

Insert immediately AFTER the existing `["MapRoom/PathWidthScale"]` entry, i.e. still inside the
`// ---- [MapRoom] ----` block and before `["Rig/WorldTiltDegrees"]`.

**Collapsed length: 611 characters**, inside `ConfigCatalog.MaxDescriptionChars` = 620, so it is not
clipped mid-word in the headset. Measured, not estimated: the string was run through
`ConfigCatalog.Collapse`'s exact whitespace rule (`ConfigCatalog.cs:1751-1769`). The shipped English
`ConfigDescription` in `Plugin.cs` was cut to the same budget and measures 611 as well. **If you
re-word either text, re-measure it** — the block comment above this entry records what happened the
last time a measurement outlived its string.

```csharp
            // DER EINZIGE [MapRoom]-EINTRAG, DER KEINE GRÖSSE IST (ModBuild 364, user 2026-09-03:
            // "Bitte deaktiviere die animationen für das mouseover im Kartenraum wenn ich über ein
            // Kartensymbol hovere - an der Stelle möchte ich es nicht."). Der erste Satz sagt, dass
            // die Vorgabe AUS ist und dass das so gewünscht war — eine Geschmacksentscheidung
            // gehört an die Stelle, an der sie zurückgenommen werden kann. Der zweite sagt
            // ausdrücklich, was NICHT verschwindet (Hervorhebung, Quest-Karte, Klick), weil genau
            // diese Sorge einen Spieler den Regler wieder anschalten ließe. Eingesammelte Länge
            // nach ConfigCatalog.Collapse: 611 (Grenze 620), gemessen, nicht geschätzt.
            ["MapRoom/HoverAnimation"] =
                "MOUSEOVER-ANIMATION eines Ortssymbols im 3D-Kartenraum. Standardmäßig AUS, wie "
                + "gewünscht: Ein Symbol, auf das du zeigst, springt nicht mehr. Das Anvisieren "
                + "bleibt unverändert — das Symbol wird weiterhin hervorgehoben, seine Quest-Karte "
                + "erscheint weiterhin darüber und der Trigger wählt es aus. Weg ist nur die "
                + "Bewegung: Das Spiel vergrößert das Symbol beim Anvisieren um 20% und startet "
                + "einen Partikeleffekt darauf; beides wird sofort zurückgenommen. Der unbewegte "
                + "Teil — Leuchtring und hellere Grafik — bleibt, damit du siehst, worauf du "
                + "zeigst. Einschalten holt sie zurück. Wirkt sofort, nur im 3D-Kartenraum.",
```

---

## Nothing else is needed outside this lane's files

* `WorldUI/Composites/**`, `WorldUI/Modal/**`, `Board/FigureGrab/**`,
  `WorldUI/Surfaces/PropInfoSurface.cs`, `WorldUI/Tooltips/**` — **untouched**.
* No wire field, no `NetProtocol` change, no `NetProtocol.ModBuild` bump (per lane instructions).
* No `ScenarioRuleLibrary`, Photon Bolt or `FFSNet.NetworkManager` patch.

## Two files outside the literal ownership list WERE edited, deliberately

* `src/GloomhavenVR/Defaults/Defaults.Plugin.cs`, not `Defaults.WorldUI.cs`. One `const bool`.
  The other five `[MapRoom]` defaults all live in `Defaults.Plugin.cs`, next to the `Config.Bind`
  calls that consume them; putting the sixth in `Defaults.WorldUI.cs` would split one family across
  two files. One line added, none moved.
* `src/GloomhavenVR/Plugin.cs`. The `[MapRoom]` section is bound there and only there, so the key
  cannot exist anywhere else. One `ConfigEntry<bool>` field plus one `Config.Bind` call, both
  appended after their five siblings; nothing existing was moved or reworded.

`src/GloomhavenVR/WorldUI/Options/VROptionsTab.4.Curated.cs` and
`src/GloomhavenVR/WorldUI/WorldUIModule.cs` are inside `WorldUI/**` and not on the do-not-own list;
each takes one appended entry.
