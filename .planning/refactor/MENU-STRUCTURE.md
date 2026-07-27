# VR-Menü — Struktur und Begründung

> Umbau der KURATIERTEN Kategorien des In-VR-Einstellungsmenüs
> (`SettingsPanel.*`). Der generische Konfigurations-Browser
> (Debug ▸ Alle Einstellungen, `SettingsPanel.8.ConfigBrowser.cs` +
> `ConfigCatalog.cs`) wird davon NICHT berührt — er ist die vollständige
> Rückfallebene, diese Kategorien sind der kuratierte Weg.
>
> Auslöser (User): *"Geh nochmal das VR Menü durch und entscheide, welche
> Elemente in welche Kategorien gehören … Aktuell sind manche Buttons nicht in
> Buttons zu finden etc."* und *"Es soll user-freundlich und übersichtlich sein.
> Und auch intuitiv zu bedienen."*

## 0. Regeln, unter denen das steht

1. **Nur echte Entscheidungen im User-Bereich.** Eine Zeile gehört nur dann in
   eine nicht-Debug-Kategorie, wenn der Spieler wirklich etwas abwägen muss.
   Justage (Offsets, Geometrie, Intervalle) und reine Messschalter gehören nach
   Debug. *(Regel des Users, an der die heutige "Leistung"-Kategorie gebaut ist.)*
2. **Ergänzend zum Spiel, nie ersetzend.** Kein zweiter Regler für etwas, das
   das Spiel selbst anbietet (Qualitätsstufe, Post-Process-AA, Aniso, Schatten,
   Texturen, V-Sync, FPS-Limit). Das Spiel hat **kein MSAA und nichts pro Auge**
   — genau diese zwei Lücken füllen wir.
3. **Lange Erklärungen gehören in den Hover** (`SettingsPanel.7.Tooltips.cs`),
   nicht in die Tafel.
4. **Nichts wird umbenannt, entwertet oder entfernt**: kein Config-Key, kein
   Default, kein Bind ändert sich. Dies ist eine Umsortierung der *Menü*-Ebene.
5. **Übersichtlich auch im Debug-Menü** (stehende Anweisung des Users).

## 1. Was heute schief ist

| Befund | Warum es weh tut |
| --- | --- |
| **"Anzeige" ist ein Sammelbecken.** Kampflog, Post-Processing, Welt-UI-Master, *Board: nur Fernstrahl*, Wände durchsichtig, Element-Hinweise, Infotafel-Größe, Mixed Reality, Key-Farbe. | Drei völlig verschiedene Themen (Bildberechnung / Mod-Tafeln / Eingabe) in einer Kategorie. Man muss alles lesen, um irgendetwas zu finden. |
| **"Board: nur Fernstrahl" steht unter Anzeige.** Der Eintrag schaltet das Fingerspitzen-Antippen ab und erzwingt den Fernstrahl (`[Board] ForceFarMode`). | Das ist eine **Eingabe**-Entscheidung, keine Darstellung. Wer sie sucht, sucht sie bei Händen/Zielen. |
| **Post-Processing (Anzeige) und MSAA/Renderauflösung (Leistung) sind getrennt.** | Es ist ein und dieselbe Frage: "wie sauber soll das Bild sein und was darf es kosten". Der Kommentar im Code dokumentiert sogar den Umzug von MSAA aus Anzeige nach Leistung — das Ergebnis ist, dass die Bildqualität heute auf zwei Reiter verteilt ist. |
| **"Dominante Hand rechts" steht unter Welt** (zwischen Tischhöhe und Bewegung). | Weder Welt noch Komfort im engeren Sinn — es ist die Grundeinstellung der Steuerung. |
| **Buttons sind nicht unter "Tasten" zu finden** (der wörtliche Vorwurf): Debug ▸ Board & Layout enthält **VR-Einstellungen** (= das Zahnrad-Plättchen) und **Pin** (= das FIXIERT-Plättchen). Deren *Kappengröße* liegt gleichzeitig unter Debug ▸ Tasten ▸ "Zahnrad & Fixiert". | Ein und derselbe Knopf wird an zwei Orten justiert: Position unter "Board & Layout", Größe unter "Tasten". Genau die Klasse Fehler, die der User benannt hat. |
| **Die Debug-Zeilen "Zeitgeber / CPU" hängen an der ganzen Kategorie**, nicht an einem Element (`BuildTimingCategory` setzt `GateCat(NavCat.Debug)`). | Acht Zeilen (3 Intervalle, 3 A/B-Schalter, Stereo-Modus, Hinweis) erscheinen auf **jeder** Debug-Seite — auch unter "Alle Einstellungen" und mitten in der Knopf-Justage. Das ist der größte einzelne Unordnungsposten im Debug-Menü. |
| **"Wand-Durchsicht" ist eine Unterkategorie mit genau einem Element.** | Eine Ebene Navigation ohne Auswahl. |

## 2. Vollständige Inventur

Legende der Spalte **Art**: `E` = echte Entscheidung des Users ·
`J` = Justage/Tuning · `M` = Messung/Diagnose · `A` = Aktion · `H` = Hinweiszeile.

### 2.1 Heute "Welt"

| Zeile | Bindung | Art | Neu |
| --- | --- | --- | --- |
| Tischgröße | `Comfort.SetScaleMultiplier` | E | **Komfort ▸ Tisch & Welt** |
| Drehen (Modus + Grad) | `[Comfort] Turn`, `SnapTurnDegrees` | E | **Komfort ▸ Bewegung** |
| Tischhöhe | `[Comfort] TableHeightOffset` | E | **Komfort ▸ Tisch & Welt** |
| Welt-Neigung | `[Rig] WorldTiltDegrees` | E | **Komfort ▸ Tisch & Welt** |
| Freie Bewegung | `[Comfort] FreeMovement` | E | **Komfort ▸ Bewegung** |
| Welt greifen / rot / scl | `[Comfort] WorldGrabEnabled/RotateEnabled/ScaleEnabled` | E | **Komfort ▸ Bewegung** |
| Neu zentrieren · Board zurückholen | Aktionen | A | **Komfort, ganz oben** (meistgebraucht; der Board-Rückruf ist die Notbremse) |
| Dominante Hand rechts | `[Plugin] PrimaryHand` | E | **Komfort ▸ Hände & Zielen** |

### 2.2 Heute "Anzeige"

| Zeile | Bindung | Art | Neu |
| --- | --- | --- | --- |
| Kampflog anzeigen | `CombatLogSurface.UserVisible` | E | **Tafeln** |
| Post-Processing aus* | `DisablePostProcessing` | E | **Grafik ▸ Darstellung** |
| Welt-UI-Flächen | `[WorldUI] Master` | E | **Tafeln** (der Hauptschalter, deshalb zuletzt) |
| Board: nur Fernstrahl | `[Board] ForceFarMode` | E | **Komfort ▸ Hände & Zielen** |
| Wände durchsichtig | `[Compat] WallFade` | E | **Grafik ▸ Darstellung** |
| Element-Hinweise | `[WorldUI] ActionElementHints` | E | **Tafeln** |
| Infotafel-Größe | `[WorldUI] HoverInfoScale` | E | **Tafeln** (direkt unter den Hinweisen — dieselbe Mouseover-Familie) |
| "* wird beim nächsten VR-Start aktiv" | — | H | **Grafik ▸ Darstellung**, direkt unter der `*`-Zeile, auf die sie sich bezieht |
| Mixed Reality | `MixedReality.Enabled` | E | **Grafik ▸ Mixed Reality** |
| Key-Farbe | `MixedReality.KeyColor` | E | **Grafik ▸ Mixed Reality** |

### 2.3 Heute "Avatar"

| Zeile | Bindung | Art | Neu |
| --- | --- | --- | --- |
| Kopfmaske | `[Net] MaskId` | E | **Avatar ▸ Aussehen** |
| Maskengröße | `[Net] MaskSize` | E | **Avatar ▸ Aussehen** |
| Hände (Stil) | `[Hands] HandStyle` | E | **Avatar ▸ Aussehen** |
| Kontrollbrett | `[Cards] Board` | E | **Avatar ▸ Aussehen** |
| Spiegel | `[Net] MirrorEnabled` | E | **Avatar ▸ Aussehen** (zeigt den eigenen Avatar) |
| Mitspieler-Boards + Hinweis | `[Net] RemoteBoards` | E + H | **Avatar ▸ Mehrspieler** |

### 2.4 Heute "Leistung"

| Zeile | Bindung | Art | Neu |
| --- | --- | --- | --- |
| Abschnitt "Schärfe gegen Flüssigkeit" | — | H | **Grafik**, erster Abschnitt |
| Grafik-Voreinstellung | `RenderQuality` (abgeleitet) | E | **Grafik ▸ Schärfe gegen Flüssigkeit** |
| Renderauflösung (pro Auge) | `[RenderQuality] EyeResolutionScale` | E | dito |
| Kantenglättung (MSAA) | `[RenderQuality] MsaaLevel` | E | dito |
| "Schatten, Texturen, Licht → Optionen › Grafik" | — | H | **Grafik, ganz unten** (Wegweiser gehören ans Ende, nicht in die Mitte) |
| "Messung + interne Schalter: Konfigurationsdatei" | — | H | dito |

### 2.5 Heute "Debug"

| Block | Art | Neu |
| --- | --- | --- |
| Unterkategorie-Auswahl, Board-Anzeige, Element-Auswahl | Navigation | unverändert |
| Alle Einstellungen (12 Themen des Config-Browsers) | E/J/M gemischt | **Debug ▸ Alle Einstellungen** — unverändert (fremdes, fertiges Stück) |
| Board, Aufgaben, Elemente, Initiative, Rundenanzeige | J | **Debug ▸ Board & Layout** |
| VR-Einstellungen (Zahnrad-**Position**), Pin (**Position**) | J | **Debug ▸ Tasten** — es sind Knöpfe (der Kern des User-Vorwurfs) |
| Stapel, Aktiv, Overlays, Entscheidung, Gegenstand benutzen, Gegenstandskarte | J | **Debug ▸ Karten & Stapel** |
| Rast, Generisch, Zugleiste, Rundenknöpfe, Zahnrad & Fixiert, Knopf-Farben | J | **Debug ▸ Tasten** |
| Hände-/Figuren-Offsets, Handgelenk | J | **Debug ▸ Hände & Offsets** |
| Wandüberblendung (4 Bruchteile) | J | **Debug ▸ Leistung & Effekte** |
| Zeitgeber/CPU-Intervalle, 3 GPU-A/B-Schalter, Stereo-Modus | J/M | **Debug ▸ Leistung & Effekte ▸ Zeitgeber / CPU** — bekommt endlich ein eigenes Element, statt auf jeder Debug-Seite mitzulaufen |

## 3. Die neue Struktur

Fünf Reiter, nach Häufigkeit der Benutzung sortiert, jeder mit einem kurzen,
im Sidebar lesbaren Namen (die Sidebar ist 132 px breit — lange Namen brechen um
und werden abgeschnitten, deshalb ein Wort pro Reiter):

```
Komfort   Neu zentrieren · Board zurückholen
          — Tisch & Welt —      Tischgröße · Tischhöhe · Welt-Neigung
          — Bewegung —          Drehen · Freie Bewegung · Welt greifen (rot/scl)
          — Hände & Zielen —    Dominante Hand rechts · Board: nur Fernstrahl

Grafik    — Schärfe gegen Flüssigkeit —  Voreinstellung · Renderauflösung · MSAA
          — Darstellung —       Post-Processing aus* · Wände durchsichtig · (*-Hinweis)
          — Mixed Reality —     Mixed Reality · Key-Farbe
          Wegweiser: Optionen › Grafik · Konfigurationsdatei

Tafeln    Kampflog anzeigen · Element-Hinweise · Infotafel-Größe · Welt-UI-Flächen

Avatar    — Aussehen —          Kopfmaske · Maskengröße · Hände · Kontrollbrett · Spiegel
          — Mehrspieler —       Mitspieler-Boards (+ Hinweis)

Debug     Alle Einstellungen · Board & Layout · Karten & Stapel ·
          Tasten · Hände & Offsets · Leistung & Effekte
```

### Warum diese Schnitte

- **Nach Absicht, nicht nach Subsystem.** "Ich sitze unbequem" → Komfort.
  "Das Bild ruckelt / flimmert" → Grafik. "Diese Tafel stört / ist zu klein" →
  Tafeln. "Wie sehe ich aus" → Avatar. "Ich justiere" → Debug.
- **"Leistung" geht in "Grafik" auf.** Beides sind Aussagen über dasselbe Bild;
  getrennt zu halten hat genau den Effekt erzeugt, dass Post-Processing und MSAA
  in verschiedenen Reitern landeten. Der Abschnitt heißt weiterhin
  "Schärfe gegen Flüssigkeit" und steht **zuoberst** in Grafik, damit wer wegen
  der Bildrate kommt, ihn zuerst sieht. Der Verweis auf *Optionen › Grafik*
  bleibt — wir ergänzen das Spiel, wir doppeln es nicht.
- **"Tafeln" statt eines zweiten "Anzeige".** Jede Zeile darin ist wörtlich eine
  Tafel/Fläche des Mods (Kampflog, Element-Hinweis, Infotafel, Welt-UI-Flächen);
  der Name ist damit prüfbar statt vage, und die Verwechslung *Anzeige* ↔ *Bild*
  entfällt.
- **Kein Reiter mit einer einzigen Zeile**, aber auch keiner, der zum Sammelbecken
  wird: der kleinste (Tafeln) hat vier Zeilen, der größte (Komfort) neun.
- **Abschnittsüberschriften statt weiterer Reiter.** Innerhalb eines Reiters
  trennt `Section()` die Gruppen; das kostet 24 px pro Gruppe und spart eine
  Navigationsebene.
- **Reihenfolge innerhalb einer Gruppe = Häufigkeit.** "Neu zentrieren" ist die
  meistbenutzte Schaltfläche des ganzen Menüs und steht deshalb in Komfort ganz
  oben; der Welt-UI-Hauptschalter steht in Tafeln ganz unten, weil er alles
  darüber abschaltet.

### Debug im Besonderen

- **Tasten enthält jetzt alle Knöpfe** — inklusive Zahnrad- und Pin-Plättchen,
  deren Position vorher unter "Board & Layout" lag. Reihenfolge: erst die Knöpfe,
  die man beim Spielen ständig drückt (Generisch = Bestätigen/Rückgängig, Rast,
  Zugleiste, Rundenknöpfe), dann die Dashboard-Plättchen, dann die Farben.
- **"Zeitgeber / CPU" ist ein eigenes Element** statt kategorieweit sichtbar.
  Damit zeigt jede Debug-Seite genau das, was ihr Element betrifft.
- **"Wand-Durchsicht" und "Zeitgeber / CPU"** teilen sich die Unterkategorie
  **"Leistung & Effekte"** — beides sind Regler, mit denen man einen guten
  Vorgabewert *sucht*, und keiner davon ist mehr allein auf einer Ebene.
- **"Alle Einstellungen" bleibt erste Unterkategorie** und damit das, worauf
  Debug öffnet — bewusste frühere Entscheidung des Parallel-Umbaus, hier nicht
  angetastet.

## 4. Bewusst NICHT verschoben (Fragen an den User, keine Platzierungsfragen)

Diese Zeilen sind heute in Debug, sind aber keine Justage, sondern echte
Vorlieben. Sie hochzuziehen wäre eine **Tier**-Änderung, keine Umsortierung —
deshalb hier zur Entscheidung statt einfach getan:

1. **Geisterhand + Geist-Stärke** (`[Hands] GhostHandOnFan/GhostHandStrength`,
   heute Debug ▸ Hände-Offsets ▸ Kartenfächer). "Blendet die Hand aus, während
   der Fächer offen ist" ist eine reine Geschmacksfrage — Kandidat für
   **Komfort ▸ Hände & Zielen** oder **Tafeln**.
2. **Aufrecht** (`FigureGrabConfig.HeldUpright`, Debug ▸ Figuren-Offsets). Ein
   Modus-Schalter, kein Offset — Kandidat für **Komfort ▸ Hände & Zielen**.
3. **Blick-Neigung** (`[Cards] FanGazeBias`, Debug ▸ Hände-Offsets). Ebenfalls
   ein Modus-Schalter über der Fächer-Justage.
4. **Debug öffnet auf "Alle Einstellungen"** (dem vollständigen Browser). Wenn
   das für den Einstieg zu dicht ist, wäre "Board & Layout" die kuratierte
   Alternative — betrifft aber die Arbeit des Parallel-Workers.
