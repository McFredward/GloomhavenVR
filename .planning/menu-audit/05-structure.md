# Menü-Audit 05 — Struktur, Menüführung, Übersichtlichkeit

Gegenstand: der Aufbau des VR-Optionen-Tabs (Cluster, Tabs, Navigation), nicht einzelne Regler.
Quellen: `src/GloomhavenVR/WorldUI/VROptionsTab.1–6.*.cs`, `ConfigCatalog.cs`,
`Core/Loc.ConfigNames.cs`, `Core/Loc.cs`. Stand: main @ 291d06d.

---

## 1. Ist-Zustand: der Navigationsbaum, wie der Spieler ihn erlebt

### 1.1 Einstieg

```
Optionen (Spielmenü, Hauptmenü ODER Pausenmenü)
└─ Tab "VR Optionen"                          (VROptionsTab.1.Inject.cs)
   ├─ Sub-Tab-Spalte links (ToggleGroup):
   │    1. Komfort          ← Startansicht (_curated = 0)
   │    2. Grafik
   │    3. Tafeln
   │    4. Avatar
   │    5. Mehrspieler
   │    6. Debug            ← Katalog-Index (View.AdvancedIndex)
   └─ Inhalt: gewählte Kategorie, scrollend
```

Debug ist von jeder kuratierten Seite genau 1 Klick entfernt; ein Thema darin 2 Klicks
(Debug → Themenliste → Thema, mit „‹ Debug“-Rückweg oben). Die Architektur ist gesund:
kuratierte Alltagsansicht obenauf, vollständiger Katalog darunter, nichts geht verloren
(Curated-Miss wird geloggt, BoardTopic hat ein Auffangnetz „Allgemein“).

### 1.2 Kuratierte Ansicht — vollständiges Inventar (VROptionsTab.4.Curated.cs)

**Komfort** (22 Zeilen)
- *Bewegung & Drehen* (14): Drehen, Sprungwinkel, Drehgeschwindigkeit, Dreh-Hand,
  Stick-Flug, Flugrichtung, Fluggeschwindigkeit, Flug-Hand, Freie Bewegung, Welt greifen,
  Senkrecht ziehen, Welt drehen, Welt skalieren, Zentrieren halten
- *Sichtbarkeit* (2): Wände durchsichtig, Wand-Fades der Mitspieler
- *Hände & Zielen* (6): Dominante Hand, Laser immer an, Feld mit Finger antippen,
  Nur per Stick scrollen, Laser-Ursprung, Laser-Kegel

**Grafik** (9)
- *Darstellung* (5): Post-Processing aus*, Volumennebel aus, Wände durchsichtig (Duplikat,
  bewusst), Forward-Rendering, Hauptmenü in VR
- *Mixed Reality* (2): Mixed Reality, Key-Farbe (Preset-Dropdown)
- *Leistung* (2): Parallele Bildabgabe, Automatisch neu starten

**Tafeln** (21 sichtbar)
- *Tafeln & Anzeigen* (10): Kampflog, Lebensbalken, Balken-Größe/Min/Max,
  Element-Hinweise, Handgelenk-Tasten, Dialoge in VR, Entscheidungsleiste, Gegnerkarten
- *Texteingabe* (2): Bildschirmtastatur, Wörter großschreiben
- *Karten & Brett* (9 sichtbar): Kontrollbrett, Brettgröße, Brett folgt dir,
  Brett-Bewegung (Dropdown), Neigungslimit unten/oben (pro Brett, Variantenfilter),
  Nahansicht, Fächer öffnen, Greif-Taste

**Avatar** (4)
- *Aussehen* (3): Handmodell, Kopfmaske (Dropdown), Maskengröße
- *Dich selbst sehen* (1): Spiegel

**Mehrspieler** (6, davon 2 Duplikate aus Avatar)
- *Zusammen spielen* (4): Mehrspieler-Abgleich, Freier Platz am Brett,
  Mitspieler-Boards, Namensschilder
- *Was andere von dir sehen* (2): Kopfmaske, Maskengröße

### 1.3 Debug-Ansicht — Themen in fest verdrahteter Reihenfolge (ConfigCatalog.ConfigTopic)

Katalog: **438 Einträge** aus ~19 cfg-Dateien (~80 stillgelegte werden ausgefiltert).
Zeilenzahlen ≈ Quelltext-Zählung, Varianten-Faltung (nur gewähltes Brett / gewählter
Handstil) bereits berücksichtigt:

| # | Thema (Label) | Einträge ≈ | Gruppen | Befund |
|---|---|---|---|---|
| 1 | Messung & Diagnose | ~30 | Messung [Perf], Optimierungen, Entwickler-Werkzeuge | reine Power-User-Seite, steht ZUERST |
| 2 | Bild & Darstellung | ~22 | RenderQuality, MixedReality, WallFade, Allgemein | enthält Alltagsregler (Auflösung/Auge, MSAA) |
| 3 | Bewegung & Welt | ~22 | Komfort (~20), Allgemein | fast 1-Gruppen-Thema |
| 4 | Hände & Figuren | ~45 gebunden / ~28 sichtbar | Hände, Figur-Offsets, Arm-HUD | Handgröße/Handsitz leben hier |
| 5 | Karten & Fächer | ~95 | Präfix-Cluster: Fan (~30), Item (~15), Card, Held, Tray, Spawn, … + Faltung | am Gruppen-Deckel (12), engl. Gruppennamen |
| 6 | Tasten | ~29 | ButtonColors (21!), ButtonAnim, BoardDashboard | eine 21-Zeilen-RGB-Wüste |
| 7 | Menüs & Tafeln | ~80 | Combat, Screen, Bar, Hex, … + große „Allgemein“-Falte (~30–40 Zeilen) | größter Grabbeltisch |
| 8 | Brett & Zielen | ~16 | Board, HexHighlight, SelectionReady | klein, ok |
| 9 | Steuerbrett (pro Brett) | ~143 gebunden / ~55 sichtbar | HANDGEBAUTER Baum (6.BoardTopic.cs) | Vorbild, gut |
| 10 | Mehrspieler | 7 | „Net“ (unlokalisierter Sektionsname) | 1-Gruppen-Thema |
| 11 | System & Start | ~9 | Allgemein | klein, ok |
| 12 | Sonstiges | 0 | — | wird ausgeblendet, ok |

### 1.4 Konkrete Schmerzpunkte

**S1 — Der Tab heißt „Debug“, enthält aber Alltagseinstellungen.** Der Nutzer sagt
wörtlich: alle wichtigen Einstellungen für den normalen User AUSSERHALB der Debug
Settings. Heute liegen im „Debug“ u. a.: **Auflösung pro Auge, MSAA-Stufe**
(RenderQuality), der **Schwebende 2D-Schirm** samt Breite/Abstand (WorldUI/FlatScreen*),
**Handgröße** (`Hands/*Scale`), die Panel-Schalter **Initiative-Leiste, Elemente-Tafel,
Aufgaben-Tafel, Statustafeln, Info-Karten, Tooltips, Handgelenk-Anzeige, Ladeanzeige**,
die **Klang-Schalter** (Fächer/Karten-Sounds) und **Zoom-Unter-/Obergrenze**
(Comfort/ScaleMin/Max). Ein normaler Spieler, der „die Auflösung höher stellen“ will,
muss durch eine Seite namens Debug → „Bild & Darstellung“ → Gruppe finden. Zudem
widersprechen sich Code-Doku („Erweitert“) und tatsächliches Label („Debug",
`cat_debug`).

**S2 — Diagnostics-first in der Themenliste.** `ConfigTopic` ordnet Messung & Diagnose
an Position 1 („measurement first, that is what a power user opens this pane for“).
Diese Begründung stammt aus der Zeit VOR der kuratierten Schicht. Heute ist der
Debug-Index die zweite Ebene für JEDEN, der etwas nicht im Alltags-Tab fand — der
landet zuerst auf Perf-Messschaltern. Die alltagsnächsten Themen (Bewegung, Tafeln,
Karten) stehen hinter der Messung.

**S3 — Grabbeltisch-Gruppen in großen Themen.** Die automatische Präfix-Gruppierung +
Faltung (MinClusterSize 3, MaxGroupsPerTopic 12) erzeugt in „Menüs & Tafeln“ (~80
Einträge) eine „Allgemein“-Gruppe mit geschätzt 30–40 Zeilen (alle 2er-Cluster: Flat,
Drag, Poke, Video, Click, Dev, Manual, Keyboard, …) und in „Karten & Fächer“ (~95) das
Gleiche. Genau dieses Muster hat der Nutzer beim Steuerbrett schon einmal reklamiert
(„Aktuell sucht man dort immer rum…“) — die Antwort war der handgebaute Baum in
`VROptionsTab.6.BoardTopic.cs`. Menüs & Tafeln und Karten & Fächer haben dieselbe
Krankheit und noch keinen Baum.

**S4 — Englische Gruppennamen im deutschen Menü.** `GroupWord()` liefert das führende
Key-Wort roh: „Fan“, „Item“, „Held“, „Tray“, „Screen“, „Combat“, „Bar“ als
Zwischenüberschriften einer deutschen Seite. `SectionLabel()` lokalisiert nur
Sektionsnamen; für Präfix-Cluster gibt es keine Tabelle. Ebenso: Thema „Mehrspieler“
zeigt als einzige Gruppe „Net“.

**S5 — Zwei Mini-Tabs mit Überlappung.** Avatar hat 4 Zeilen, Mehrspieler 6 — davon
sind Kopfmaske + Maskengröße in BEIDEN gelistet. Zwei Sub-Tabs für zusammen 8
unterschiedliche Einstellungen, mit Duplikat als Navigationskrücke.

**S6 — „Tafeln“ mischt Publikum und Gegenstand.** Der Abschnitt „Karten & Brett“
(Kontrollbrett-Wahl, Brettbewegung, Kartenfächer-Öffnung, Greif-Taste) liegt unter der
Überschrift „Tafeln“. Wer einstellen will, wie sein Kartenfächer aufgeht, sucht nicht
unter Tafeln. Umgekehrt fehlen unter Tafeln die halbe Panel-Familie (Initiative,
Elemente, Aufgaben, Statustafeln, Tooltips, Handgelenk-Anzeige) — die liegen im Debug.

**S7 — 14er-Block ohne Binnengliederung.** Komfort ▸ „Bewegung & Drehen“ reiht 14
Zeilen aus drei Sinnfamilien (Drehen ×4, Stick-Flug ×4, Welt-Griff ×6) unter einer
Überschrift. Sektionen sind billig (eine Ebene unter dem Tab) — hier fehlen zwei.

**S8 — Ein-Gruppen-Themen.** „Bewegung & Welt“ (fast alles in einer „Komfort“-Gruppe)
und „Mehrspieler“ (7 Zeilen, Gruppe „Net“) tragen eine Navigationsebene, die nichts
unterteilt. Kein Beinbruch, aber bei einer Neuordnung der Themen zusammenlegbar.

---

## 2. Ziel-Vorschlag: Normal-User zuerst

Alles Folgende ist mit der bestehenden Maschinerie umsetzbar (CuratedCategory-Array,
CuratedSection, per-Key-Captions, Themen-Enum-Reihenfolge, SectionLabel-Switch) —
Ausnahmen sind am Ende markiert.

### 2.1 Alltagsansicht (Sub-Tab-Spalte), neue Ordnung

1. **Komfort** — Binnengliederung statt 14er-Block:
   - *Drehen* (4): Drehen, Sprungwinkel, Drehgeschwindigkeit, Dreh-Hand
   - *Fortbewegung* (5): Stick-Flug, Flugrichtung, Fluggeschwindigkeit, Flug-Hand, Freie Bewegung
   - *Welt greifen* (5+2 neu): Welt greifen, Senkrecht ziehen, Welt drehen, Welt
     skalieren, Zentrieren halten — **neu dazu: Zoom-Untergrenze, Zoom-Obergrenze**
     (Comfort/ScaleMin/Max; direkte Nachbarn von „Welt skalieren“)
   - *Sichtbarkeit* (2): unverändert
   - *Hände & Zielen* (6): unverändert
2. **Grafik** — **neu dazu in *Darstellung*: Auflösung pro Auge, MSAA-Stufe**
   (RenderQuality; die zwei Regler, die jeder Headset-Besitzer sucht; Neustart-Hinweis
   trägt die Hover-Notiz bereits). Rest unverändert.
3. **Brett & Karten** *(neuer Tab; heutiger Abschnitt „Karten & Brett“ aus Tafeln)*
   - *Kontrollbrett* (6): Kontrollbrett, Brettgröße, Brett folgt dir, Brett-Bewegung,
     Neigungslimit unten/oben
   - *Karten* (3+Sound): Fächer öffnen, Greif-Taste, Nahansicht — **optional dazu die
     5 Klang-Schalter** (Cards/Fan*Sound, Card*Sound) als *Klänge*-Block
4. **Tafeln** — wird die EINE Heimat aller Anzeigen:
   - *Tafeln & Anzeigen*: heutige 10 **plus Initiative-Leiste, Elemente-Tafel,
     Aufgaben-Tafel, Statustafeln, Info-Karten (Hover), Tooltips am Finger,
     Handgelenk-Anzeige, Ladeanzeige** (alles reine An/Aus-Schalter, WorldUI)
   - *2D-Schirm* (4, neu): Schwebender 2D-Schirm, 2D-Schirm automatisch,
     Breite, Abstand (WorldUI/FlatScreen*, ScreenWidth, ScreenDistance)
   - *Texteingabe* (2): unverändert
5. **Avatar & Mehrspieler** *(Zusammenlegung, → offene Frage F1)*
   - *Dein Auftritt* (4): Handmodell, Kopfmaske, Maskengröße, Spiegel
   - *Zusammen spielen* (4): Mehrspieler-Abgleich, Freier Platz am Brett,
     Mitspieler-Bretter, Namensschilder
   - Die Maskenduplikate entfallen — ein Tab, keine Krücke mehr nötig.
6. **Erweitert** *(umbenannt von „Debug“, → offene Frage F2)* — der Katalog-Index.

Begründung der Reihenfolge: Körper (Komfort) → Bild (Grafik) → Spielgerät (Brett &
Karten) → Anzeigen (Tafeln) → Sozial (Avatar & MP) → Rest (Erweitert). Der Spieler
arbeitet sich von „mir wird schlecht / ich sehe schlecht“ zu „Feintuning“ vor.

### 2.2 Themen-Reihenfolge im Erweitert-Index (nur Enum-Reihenfolge ändern)

Alltagsnah zuerst, Messung ans Ende — der Index wird von Normal-Usern betreten, die im
Alltags-Tab nicht fündig wurden:

1. Bewegung & Welt
2. Hände & Figuren
3. Karten & Fächer
4. Menüs & Tafeln
5. Brett & Zielen
6. Steuerbrett (pro Brett)
7. Bild & Darstellung
8. Tasten
9. Mehrspieler
10. System & Start
11. Messung & Diagnose
12. Sonstiges

(Die Perf-Pins innerhalb der Messung bleiben — wer die Seite öffnet, bekommt weiterhin
CullSubmitSplit zuerst.)

Hinweis: `ConfigTopic` ist ein Enum, dessen Werte nur menü-intern sind; die Reihenfolge
zu ändern ist eine Zeile pro Mitglied plus die `TopicCount`-Invariante. Kein
Persistenz-Risiko (nichts speichert Topic-Ordinale in cfg — geprüft: nur
`VROptionsTab._category` zur Laufzeit).

### 2.3 Gruppen-Chirurgie im Erweitert-Bereich

- **Menüs & Tafeln und Karten & Fächer bekommen handgebaute Bäume** nach dem Muster von
  `VROptionsTab.6.BoardTopic.cs` (Auffangnetz „Allgemein“ inklusive). Vorschlags-Tops
  für Menüs & Tafeln: Tafel-Schalter / Kampflog / Lebensbalken / 2D-Schirm & 3D-Tiefe /
  Klick & Zeigen / Feld-Hinweis / Fenster & Dialoge / Allgemein. Für Karten & Fächer:
  Fächer-Form / Fächer-Verhalten / Animationen (Tausch, Öffnen) / Gegenstände /
  Gehaltene Karte / Brett-Start / Klänge / Allgemein. Das ist dieselbe bereits gebaute
  Maschinerie (BoardTree-Struktur ist generisch genug zum Kopieren), aber ~1 Datei je
  Thema Fleißarbeit.
- **Tasten ▸ ButtonColors (21 Zeilen R/G/B einzeln)**: mindestens per Zwischenüberschrift
  in „Schrift“, „Best./Zurück“, „Zahnrad/Pin“, „Runden“, „Rast“ teilen — oder als
  Farb-Presets zusammenfassen (das wäre neue UI-Maschinerie, siehe 2.4).
- **Mehrspieler + System & Start** könnten in der Liste bleiben wie sie sind (klein,
  aber ehrlich benannt); Zusammenlegen lohnt den Bruch der Modul-Zuordnung nicht.

### 2.4 Braucht NEUE Maschinerie (getrennt ausgewiesen)

- **Lokalisierungstabelle für Präfix-Gruppennamen** (S4): ein `GroupWordLabel(word)`-
  Switch analog `SectionLabel` („Fan“→„Fächer“, „Item“→„Gegenstände“, „Held“→„Gehaltene
  Karte“, „Tray“→„Brett“, „Screen“→„2D-Schirm“, „Combat“→„Kampflog“, „Bar“→„Balken“,
  „Net“→„Mehrspieler“). Kleine, neue Codefläche — nötig nur für Themen, die KEINEN
  handgebauten Baum bekommen.
- **RGB-Farb-Presets für ButtonColors**: neuer Kontrolltyp (Preset-Zeile existiert,
  aber das Zusammenfassen von 3 Einträgen in 1 Zeile nicht). Optional.
- Alles andere in 2.1–2.3 ist reine Datenpflege in bestehenden Tabellen.

---

## 3. Namenspass (Anzeigenamen / Captions)

Das „Figur: …“-Muster (FigureGrab) ist das Vorbild: *Objekt: Wirkung (Einheit)*,
konsequent gleiche Objektbezeichnung innerhalb einer Familie. Verstöße:

| Key | Heute (DE) | Vorschlag | Grund |
|---|---|---|---|
| `cat_debug` | „Debug“ | **„Erweitert“** | Code-Doku nennt die Ansicht selbst so; „Debug“ schreckt Normal-User ab und widerspricht dem Nutzerauftrag (F2) |
| `vr_o_barsize` / `vr_o_barsizemin` / `vr_o_barsizemax` | „Größe der Lebensbalken“ / „Balken: Mindestgröße“ / „Balken: Maximalgröße“ | **„Lebensbalken: Größe / Mindestgröße / Maximalgröße“** | drei Namensformen für eine Familie auf EINEM Bildschirm |
| `WorldUI/Master` | „Physische Oberfläche“ | **„Alle VR-Tafeln“** | Deutsch weicht vom Englischen („All world panels“) ab und ist opak |
| `Net/RemoteBoards` + `remote_boards` | „Mitspieler-Boards“ | **„Mitspieler-Bretter“** | Denglisch; überall sonst heißt es Brett |
| `WallFade/SyncPeerFades` + `wallfade_sync` | „Wand-Fades der Mitspieler“ | **„Wände: mit Mitspielern synchron“** | „Fades“ ist Jargon; Wirkung unklar |
| `vr_o_recenterhold` | „Zentrieren halten“ | **„Zentrieren: Haltedauer (s)“** | liest sich als Schalter, ist eine Dauer |
| `vr_o_raycone` | „Laser-Kegel“ | **„Laser: Fangkegel (°)“** | sagt nicht, was der Kegel tut |
| `vr_o_inspectscale` / `Cards/InspectScale` | „Nahansicht“ | **„Nahansicht: Größe“** | Zeile ist ein Größenregler, kein Schalter |
| `Cards/HeldForward` vs `Cards/HeldOffPalm` | „Karte (Ersatz): vor (m)“ vs „Ersatzkarte: Abstand (m)“ | einheitlich **„Ersatzkarte: vor (m) / Abstand (m)“** | zwei Objektnamen für dieselbe Familie |
| `Cards/TrayYaw`,`TrayScale`,`TrayFollow`,`BoardMoveMode` | „Brett-Drehung“, „Brettgröße“, „Brett folgt dir“, „Brett-Bewegung“ | einheitlich **„Brett: Drehung (°) / Größe / folgt dir / Bewegung“** | Bindestrich-, Kompositum- und Satzform gemischt; Doppelpunkt-Muster wie überall sonst |
| `disable_post` „Post-Processing aus\*“ vs `vr_o_fog` „Volumennebel aus“ | Asterisk nur bei einem von zwei Neustart-Pflichtigen | Asterisk bei BEIDEN entfernen | der Hover trägt den Neustart-Hinweis bereits; ein unerklärtes `*` |
| `head_mask` (EN) | „Head Mask“ | „Head mask“ | Groß-/Kleinschreibung einheitlich zu allen anderen EN-Captions |
| `BoardDashboard/Height`,`Depth`,`Travel` | „Fixiert-Taste: Höhe“ / „Zahnrad/Pin: Tiefe“ / „Zahnrad/Fixiert: Hub“ | ein Objektname, z. B. **„Zahnrad/Pin: …“** durchgängig | drei Bezeichnungen für dieselbe Tastengruppe in vier Zeilen |
| `vr_sec_mp_presence` | „Zusammen spielen“ | ok, behalten | — |
| Gruppen „Fan/Item/Held/Tray/Screen/Combat/Bar/Net“ (automatisch) | englische Rohwörter | siehe 2.4 GroupWordLabel | deutsche Menüseite, englische Überschriften |

Nicht anfassen: die kuratierten Captions sind insgesamt gut (kurz, Verb-/Objektform,
konsistent mit der Namenstabelle); die `Loc.ConfigNames`-Tabelle ist zu ~95 % sauber im
*Objekt: Wirkung (Einheit)*-Muster.

---

## 4. Offene Fragen an den Nutzer (nur echte 50/50-Strukturentscheidungen)

- **F1:** Sollen „Avatar“ (4 Zeilen) und „Mehrspieler“ (6 Zeilen) zu EINEM Tab
  „Avatar & Mehrspieler“ zusammengelegt werden, oder bleiben es zwei Tabs?
- **F2:** Soll der Tab „Debug“ in „Erweitert“ umbenannt werden, oder ist „Debug“ als
  Name für dich gesetzt?
- **F3:** Soll „Karten & Brett“ ein eigener Alltags-Tab „Brett & Karten“ werden, oder
  bleibt der Abschnitt unter „Tafeln“?
- **F4:** Ist die Handgröße für dich eine Alltagseinstellung (dann in den Avatar-Tab)
  oder Kalibrierung (dann bleibt sie unter Erweitert ▸ Hände & Figuren)?
- **F5:** Sollen die Klang-Schalter (Fächer/Karten-Sounds) in den Alltags-Tab
  „Brett & Karten“, oder bleiben sie unter Erweitert?

---

## Anhang: Umsetzungsaufwand je Vorschlag

| Änderung | Mechanik | Aufwand |
|---|---|---|
| Tab-Reihenfolge/-Zusammenlegung, neue Sektionen, neue kuratierte Zeilen | `Curated`-Array + Loc-Keys | Datenpflege |
| „Debug“→„Erweitert“ | 1 Loc-Zeile (`cat_debug`) | trivial |
| Themen-Reihenfolge Erweitert-Index | `ConfigTopic`-Enum umsortieren | klein |
| Neue Curated-Zeilen (Auflösung, MSAA, Panels, 2D-Schirm, Zoomgrenzen) | `CuratedEntry` + Caption-Keys (Namen existieren in ConfigNames bereits) | Datenpflege |
| Handbäume für Menüs & Tafeln, Karten & Fächer | Kopie des BoardTree-Musters | ~1 Datei je Thema |
| GroupWordLabel-Lokalisierung | neuer kleiner Switch in ConfigCatalog | klein, neue Fläche |
| ButtonColors-Presets | neuer Kontrolltyp | größer, optional |
