# Anleitung: Richtige Handmodelle einbauen (einmalig, ~45–60 min, Windows)

Die prozeduralen Hände sind ein Platzhalter. Richtige, animierte Handmodelle
(die SteamVR-Handschuhe von Valve, BSD-3-lizenziert) brauchen **einen einmaligen
Unity-Editor-Schritt auf deinem Windows-PC** — geriggte/geskinnte Meshes lassen
sich nur als Unity-AssetBundle ausliefern, nicht zur Laufzeit laden. Danach
lädt der Mod die Handschuhe automatisch; Code-Änderungen sind keine nötig.

Referenz (englisch, mit allen Details): `unity/HANDS.md` und `unity/HARVESTING.md`.

## Schritt 1 — Unity installieren (~20 min, einmalig)

1. [Unity Hub](https://unity.com/download) installieren und mit einem (kostenlosen)
   Unity-Konto anmelden (Personal-Lizenz).
2. Im Hub: **Installs → Install Editor → Archive** → im
   [Download-Archiv](https://unity.com/releases/editor/archive) die Version
   **2021.3.45f2 (LTS)** wählen (2021.3.x ist Pflicht — Bundles anderer
   Major-Versionen lehnt das Spiel ab).
3. Bei den Modulen **"Windows Build Support (Mono)"** anhaken. Nichts weiter nötig.

## Schritt 2 — Companion-Projekt öffnen (~5 min)

1. Im Hub: **Projects → Open** → Ordner `unity/GloomhavenVR.Assets` aus dem
   Repo-Checkout wählen.
2. Erster Import dauert ein paar Minuten. Meldungen über neu generierte
   Projektdateien sind normal (einmal committen ist nett, aber optional).
3. Prüfen: **Edit → Project Settings → Player → Other Settings → Color Space**
   muss **Linear** sein (sollte es schon sein).

## Schritt 3 — SteamVR-Handschuhe importieren (~10 min)

1. Das [SteamVR Unity Plugin, Release 2.8.0](https://github.com/ValveSoftware/steamvr_unity_plugin/releases/tag/2.8.0)
   herunterladen (`.unitypackage`). Lizenz: BSD-3-Clause — Weitergabe erlaubt.
2. **Nicht** das ganze Package importieren! In Unity: **Assets → Import Package →
   Custom Package** → im Import-Dialog **alles abwählen** und nur diese Pfade anhaken:
   - `Assets/SteamVR/Models/vr_glove_left_model_slim.fbx` (bzw. `vr_glove_*` Modelle)
   - `Assets/SteamVR/Models/Materials/` (die zugehörigen Handschuh-Materialien + Texturen)
   - **Keine** Skripte, **keine** Prefabs mit Script-Referenzen (die brechen ohne das Plugin).
   Die genaue Dateiliste steht in `unity/HANDS.md` §1.
3. Die zwei Handschuh-Prefabs anlegen (Details + Anker-Namen in `unity/HANDS.md` §2):
   - FBX in die Szene ziehen, unter `Assets/Bundle/Hands/` als Prefab speichern:
     `VRHand_L.prefab` und `VRHand_R.prefab`.
   - Im Prefab leere GameObjects als Anker anlegen (exakte Namen!):
     `Anchor_Wrist`, `Anchor_Palm`, `Anchor_IndexTip`, `Anchor_Grab` sowie pro Finger
     `Anchor_<Thumb|Index|Middle|Ring|Pinky>_<Root|Mid|Tip>` an den Knochenpositionen.
     **Abkürzung:** Fehlende Anker synthetisiert der Mod automatisch an
     Standardpositionen — fürs erste reichen die Skelett-Knochen des FBX
     (`finger_index_0_r` …), die der Mod als Fallback erkennt. Einfach erst mal
     ohne Anker bauen und schauen, wie es aussieht.
4. Beide Prefabs im Project-Fenster anwählen → unten im Inspector bei
   **AssetBundle** das Label `gloomhavenvr` vergeben (steht meist schon am Ordner).

## Schritt 4 — Bundle bauen und einspielen (~5 min)

1. Menü **GloomhavenVR → Build AssetBundles** (vom Projekt mitgeliefert).
   Ergebnis: `Build/Bundles/gloomhavenvr.bundle`.
2. Diese Datei nach
   `C:\...\Gloomhaven\BepInEx\plugins\GloomhavenVR\gloomhavenvr.bundle` kopieren.
3. Spiel starten. Im Log muss stehen:
   `[Hands] Left: glove prefab loaded from bundle.`
   Fehlt das Prefab im Bundle, sagt das Log exakt, welche Pfade gesucht wurden —
   dann Schritt 3.3 (Prefab-Pfade `Assets/Bundle/Hands/VRHand_L.prefab`) prüfen.

## Bonus (empfohlen, +10 min): Echte XR-DLLs ernten

Wenn Unity schon offen ist: Menü **GloomhavenVR → Harvest RuntimeDeps**
(nach einem Dummy-Windows-Build, Anleitung `unity/HARVESTING.md`). Das ersetzt
unsere provisorisch kompilierten `Unity.XR.*`-DLLs durch die editor-gebauten —
eliminiert eine ganze Klasse potenzieller Subtilitäten.

## Ebenfalls im selben Rutsch möglich: Karten- & Tisch-Assets

Der Mod bevorzugt automatisch Bundle-Assets, wenn vorhanden:
`CardBacking.prefab`, Tray-/Button-Meshes (Vertrag:
`unity/GloomhavenVR.Assets/Assets/Bundle/Table/README.md`), empfohlene
CC0-Quellen in `unity/CARD-ASSETS.md` (KayKit, Kenney — Download, ins Projekt
ziehen, Prefab benennen, Bundle neu bauen, fertig).

## Was du NICHT tun musst

- Keine Code-Änderungen, kein Rebuild des Mods — der Bundle-Loader ist fertig.
- Kein erneuter Unity-Durchlauf bei Mod-Updates: Das Bundle bleibt gültig,
  bis du selbst Assets änderst.
