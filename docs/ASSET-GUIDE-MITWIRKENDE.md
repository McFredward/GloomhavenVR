# GloomhavenVR — Asset-Anleitung für Mitwirkende (Blender)

Kurzanleitung für die Bearbeitung der 3D-Assets des Mods. Zielgruppe: jemand mit
Blender-/Unity-Erfahrung, der die Assets verbessern will, ohne die Mod-Pipeline zu kennen.
**Grundregel: Dateinamen, Pfade und Knochen-/Anker-Namen sind Verträge — Inhalt darf sich
ändern, Namen nie.**

---

## 1. Was du brauchst

- **Blender 4.x** (wir arbeiten mit 4.2 — andere 4er sind okay, solange der FBX-Export binär ist).
- Ein Bildbearbeitungsprogramm für die PNG-Texturen (Krita/Photoshop/GIMP, egal).
- **Unity brauchst du NICHT.** Die AssetBundles baut ausschließlich unsere Pipeline mit
  exakt Unity 2021.3.5f1 — eine andere Editor-Version erzeugt Bundles, die das Spiel
  stillschweigend verwirft. Deshalb: FBX/PNG zurückgeben, nie fertige Bundles.

## 2. Was du bekommst

Ein Zip des Ordners `unity/GloomhavenVR.Assets/Assets/Bundle/` mit drei Asset-Familien:

| Ordner | Inhalt | Dateien |
|---|---|---|
| `Hands/` | 3 Handpaare (Stile: Standard „Glove", Panzer „Plate", Arkan „Arcane"), je L+R | `VRHand[Stil]_{L,R}_rig.fbx` + `VRHand[Stil]_albedo.png` + `VRHand[Stil]_normal.png` (Plate zusätzlich `VRHandPlate_mrs.png`) |
| `Head/` | 3 Kopfmasken (Avatar-Köpfe im Multiplayer) | `Mask_{0,1,2}.fbx` + `Mask_{0,1,2}_albedo.png` |
| `Table/` | Kontrollbretter (3 Stile: Oak, Steel, Bronze) | siehe die Namenstabelle in §4 — die Dateinamen verraten den Stil **nicht** |

Der Ordner `Bundle/` enthält noch weitere Unterordner (`Controllers/`, `Environments/`, `UI/`,
`Test/`). Die sind **nicht** Teil des Auftrags — `Controllers/` sind fremdlizenzierte
glTF-Modelle, der Rest wird aus Code bzw. Shadern erzeugt.

Die `.prefab`/`.mat`/`.shader`-Dateien liegen mit im Zip, damit du Materialzuordnungen
sehen kannst — **bitte nicht bearbeiten**, die regeneriert unsere Pipeline.

## 3. Import in Blender

- FBX-Import mit Standardeinstellungen. Einheit ist **Meter**, reale Weltgröße.
- **Kein „Apply Transform" beim Import und kein Apply von Scale/Rotation auf Armatures.**
  Die Hand-Rigs tragen absichtlich eine 100×-Armature-Skalierung aus der Toolchain —
  wird die „aufgeräumt", laden die Hände im Spiel in falscher Größe.
- Custom Split Normals sind bei den Händen **bewusst gesetzt** (handgefixte Flächen).
  Nicht pauschal „Clear Custom Split Normals“ / neu berechnen, außer die Änderung an
  genau dieser Stelle ist dein Ziel.

## 4. Harte Verträge pro Asset-Familie

### Hände (`Hands/`)
- Das Skelett enthält **19 Vertragsknochen**, die der Mod per Name auflöst — Namen und
  Hierarchie exakt erhalten:
  `Anchor_Wrist, Anchor_Palm, Anchor_IndexTip, Anchor_Grab`, plus je Finger
  `Anchor_<Finger>_{Root,Mid,Tip}` für Thumb/Index/Middle/Ring/Pinky.
  Fehlt einer, fällt das Spiel **nicht** auf prozedurale Hände zurück: `HandVisuals`
  synthetisiert den fehlenden Anker kommentarlos an einer geschätzten Standardposition
  (`FillMissingAnchors`). Das Ergebnis ist eine Hand, die *falsch* greift/zeigt, ohne jede
  Fehlermeldung — also schwerer zu finden als ein Totalausfall. Auf die prozeduralen
  Notfall-Hände fällt der Mod nur zurück, wenn das **Prefab selbst** nicht aus dem Bundle
  lädt.
- **Fingerachsen — das ganze Knochen-Frame ist der Vertrag, nicht nur der Roll:** Die
  Fingerkrümmung rotiert um die lokale **+X-Achse** jedes Fingerknochens
  (`FingerCurler` schreibt `Quaternion.Euler(maxWinkel * curl, 0, 0)` auf die
  authored local rotation; einzige Ausnahme ist der **Wurzelknochen des kleinen Fingers**,
  der zusätzlich ein curl-gekoppeltes lokales Z bekommt — die Gegen-Abduktion des
  Handschuhs. Die X-Achse ist davon unberührt.) Damit das stimmt, muss pro Fingerknochen gelten:
  **lokal +X = Scharnierachse, lokal +Y = Fingerrichtung.** Ein Blender-Bone-*Roll*
  allein reicht dafür nicht — Roll dreht die Beugeachse nur innerhalb der Ebene
  senkrecht zum Knochen. `unity/hand-prep/aim_curl_axes.py` richtet die Frames
  entsprechend aus (Kopf-/Mesh-Positionen bleiben unangetastet); auf den ausgelieferten
  Rigs ist der Scharnier-vs-Finger-Fehler dadurch 0,00° auf allen fünf Fingern.
  Knochen neu ausrichten ⇒ vorher Bescheid sagen (das hat uns einmal eine
  „Horrorfilm-Faust" beschert).
- **UV-Atlas beibehalten.** Alle Stile teilen sich ein Atlas-Layout; an Inselgrenzen
  mindestens ~8 px Gutter lassen (Mip-Bleeding), Inseln nicht verschieben, sonst passt
  die bestehende Albedo nicht mehr.
- Texturen: `*_albedo.png` + `*_normal.png` (Plate zusätzlich `*_mrs.png`), gleiche oder
  höhere Auflösung, Format bleibt PNG.

### Masken (`Head/`)
- Reale Metergröße (~Kopfgröße), **+Z = Blickrichtung, +Y = oben**, Pivot am
  Augen-Mittelpunkt, keine Collider, unlit-tauglich (Albedo trägt alles).

### Kontrollbretter (`Table/`)
- **Welche Datei welcher Stil ist, steht nirgends im Dateinamen.** Zwei der drei tragen
  einen Hash, und das FBX des Standard-Bretts heißt anders als seine Texturen. Die
  Zuordnung ist ein Vertrag (`unity/…/Editor/BuildBoard.cs`, `Cards/VRCardFactory.cs`):

  | Stil im Menü | FBX | Texturen / Material / Prefab |
  |---|---|---|
  | **Oak** (Eiche, Standard) | `PlayTray_prepped.fbx` | `PlayTray_{albedo,normal,mrs}.png`, `PlayTray.mat`, `PlayTray.prefab` |
  | **Steel** (Stahl) | `PlayTray_9capjqp6.fbx` | `PlayTray_9capjqp6_{albedo,normal,mrs}.png` … |
  | **Bronze** | `PlayTray_16vm268h.fbx` | `PlayTray_16vm268h_{albedo,normal,mrs}.png` … |

  Die Hashes sind **Namen und damit Verträge** — nicht „aufräumen“ und nicht nach dem
  Stil umbenennen. Die `Keycap{Oak,Steel,Bronze,Grain}_*.png` daneben sind die Tastenkappen,
  eine eigene Familie, und folgen nicht dieser Aufteilung.
- Anker-/Kind-Objekte im FBX (Slots, Knöpfe usw.) sind Positionsverträge — Namen und
  Pivots erhalten. Geometrie/Textur frei verbesserbar.
- Texturen: `_albedo` + `_normal` + `_mrs` (Metallic/Roughness/Smoothness-Packung; liegt
  bei **allen drei** Brettern, nicht nur beim Standard-Brett). Normal-Maps im
  Unity-Standard (OpenGL, Y+).

## 5. Rückgabeformat

- **Binary-FBX + PNG, exakt gleiche Dateinamen und Ordnerstruktur wie erhalten.**
- Gern zusätzlich die `.blend` (hilft bei Rückfragen), aber die FBX ist der Master.
- Eine kurze Änderungsnotiz pro Datei: *was* geändert, *warum*, bekannte offene Punkte.
- Bitte **nicht**: GLB/OBJ/unitypackage, umbenannte Dateien, „aufgeräumte" Hierarchien,
  neue Materialien/Shader ohne Absprache.

## 6. Rückweg & Integration

1. Zip an den Maintainer zurück.
2. Der Maintainer legt es datiert ab und stößt die Integration an.
3. Unsere Pipeline verifiziert dann automatisch: Re-Import-Vergleich (Vertex-/UV-Diff),
   Auflösung aller 19 Vertragsknochen, Bundle-Build mit dem exakten Editor, Testrender
   aus Spielperspektive. Was durchfällt, kommt mit konkreter Fehlerbeschreibung zurück —
   nichts landet ungeprüft im Mod.

## 7. Häufige Stolperfallen (alle schon passiert)

- Falsche Unity-Version fürs Bundle → lädt still ins Leere. (Darum baut ihr keine Bundles.)
- Armature-Scale „normalisiert" → Hände 100× zu klein.
- Split-Normals neu berechnet → der alte Handflächen-Knick ist wieder da.
- UV-Inseln „optimiert" → Albedo-Atlas passt nicht mehr, Texturen zerreißen.
- Knochen umbenannt → der Mod synthetisiert den Anker still an einer Standardposition; die
  Hand greift falsch, und nichts im Log sagt warum (siehe §4).
