# GloomhavenVR — Installationsanleitung

<p align="center">
  <a href="INSTALL.md"><img src="docs/img/flag-en.png" width="24" alt="English">&nbsp;English</a>
  &nbsp;&nbsp;|&nbsp;&nbsp;
  <img src="docs/img/flag-de.png" width="24" alt="Deutsch">&nbsp;<b>Deutsch</b>
</p>

Alles, was du brauchst, um von einem ganz normalen Gloomhaven bis an den VR-Tisch zu kommen — und
alles, was du danach noch brauchen könntest. Wenn du noch nicht weißt, worum es hier überhaupt
geht, fang beim [README](README.de.md) an.

- [Was du brauchst](#was-du-brauchst)
- [Die OpenXR-Laufzeit deines Headsets festlegen](#die-openxr-laufzeit-deines-headsets-festlegen)
- [1. BepInEx installieren](#1-bepinex-installieren)
- [2. Die Mod installieren](#2-die-mod-installieren)
- [3. Kontrollieren, ob du an der richtigen Stelle entpackt hast](#3-kontrollieren-ob-du-an-der-richtigen-stelle-entpackt-hast)
- [Der erste Start, und warum sich das Spiel einmal selbst neu startet](#der-erste-start-und-warum-sich-das-spiel-einmal-selbst-neu-startet)
- [Updates](#updates)
- [Einstellungen](#einstellungen)
- [Fehlersuche](#fehlersuche)
- [Ein Problem melden](#ein-problem-melden)
- [Deinstallieren](#deinstallieren)

---

## Was du brauchst

| | |
|---|---|
| **Spiel** | Gloomhaven (digital) für den PC, v1.1.x — Steam oder GOG. Die Mod ist gegen v1.1.8307.0 gebaut, den letzten Patch, den das Spiel bekommen hat. |
| **Betriebssystem** | Windows, und das Spiel läuft unter **D3D11** (die Windows-Voreinstellung dieser Engine). |
| **Headset** | Jedes PC-VR-Headset mit einer **OpenXR-Laufzeit** — das ist die Software, die die App deines Headsets mitinstalliert, damit PC-Spiele mit ihm reden können. Entwickelt und gespielt auf einer **Quest 3 über Virtual Desktop (VDXR)** bei 90 Hz. Quest Link / Air Link, Steam Link und SteamVR benutzen dieselbe Schnittstelle und sollten funktionieren, sind aber nicht ausprobiert. |
| **Controller** | Zwei getrackte Controller mit Thumbsticks (Touch-Layout: Tasten A/B und X/Y, Trigger, Grip). |
| **Loader** | **BepInEx 5.4.23.5 (x64)** — der übliche Mod-Loader für Unity-Spiele. Den installierst du einmal selbst, in Schritt 1. |
| **Spielfläche** | Room-Scale oder im Stehen. Wenig Platz reicht — du kannst fliegen, dir die Welt heranziehen und dich jederzeit neu zentrieren. |

> **Das ist eine PC-VR-Mod.** Sie läuft nicht allein auf einem Standalone-Headset — das Spiel läuft
> auf deinem PC, und du streamst das Headset dorthin oder hängst es per Kabel an, genau wie bei
> jedem anderen PC-VR-Titel.

---

## Die OpenXR-Laufzeit deines Headsets festlegen

Aktiv sein kann immer nur eine OpenXR-Laufzeit, und **vor** dem Spielstart muss das deine
sein. Stell sie in der App ein, mit der du streamst:

- **Virtual Desktop** — in den Streamer-Einstellungen von Virtual Desktop auf deinem PC **VDXR**
  als OpenXR-Laufzeit auswählen.
- **Quest Link / Air Link** — Meta-Quest-Link-App → Einstellungen → Allgemein → OpenXR-Laufzeit →
  *„Meta Quest Link als aktiv festlegen“*.
- **Steam Link / SteamVR** — SteamVR-Einstellungen → OpenXR → *„SteamVR als OpenXR-Laufzeit
  festlegen“*.

Wenn sich etwas anderes die Laufzeit immer wieder zurückholt, kannst du eine nur für dieses Spiel
festnageln — siehe [Fehlersuche](#fehlersuche).

---

## 1. BepInEx installieren

1. **`BepInEx_win_x64_5.4.23.5.zip`** von der
   [Release-Seite von BepInEx 5.4.23.5](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5)
   herunterladen.
2. In den **Gloomhaven-Installationsordner** entpacken — den Ordner, in dem `GH.exe` liegt.
   (Steam: Rechtsklick auf das Spiel → Verwalten → Lokale Dateien durchsuchen.)
3. Das Spiel einmal ganz normal starten und wieder beenden. Prüfen, ob es jetzt
   `BepInEx/LogOutput.log` gibt. Dass diese Datei auftaucht, ist der Beweis, dass es läuft.
4. Empfohlen: `BepInEx/config/BepInEx.cfg` öffnen und
   `[Chainloader] HideManagerGameObject = true` setzen.

---

## 2. Die Mod installieren

Hol dir **`GloomhavenVR-<version>.zip`** von der
[Releases-Seite](https://github.com/McFredward/GloomhavenVR/releases) und entpack es in denselben
Gloomhaven-Ordner; das Verzeichnis `BepInEx/` wird dabei mit dem zusammengeführt, das schon da ist.

---

## 3. Kontrollieren, ob du an der richtigen Stelle entpackt hast

Es gibt genau eine Sache zu prüfen, und zwar nur, dass zwei Ordner existieren:

```
Gloomhaven/                      ← der Ordner mit GH.exe darin
├── GH.exe
├── INSTALL-DEUTSCH.txt          ← die beiden kamen aus dem Zip der Mod
├── INSTALL.txt                  ←   (dieselbe Kurzanleitung auf Englisch)
└── BepInEx/
    ├── plugins/GloomhavenVR/    ← beide müssen da sein
    └── patchers/GloomhavenVR/
```

Wenn einer der beiden `GloomhavenVR`-Ordner fehlt, hast du an der falschen Stelle entpackt oder das
Zusammenführen ist nicht passiert. Dann lieber das Zip noch einmal entpacken, statt Dateien von
Hand herumzuschieben.

---

## Der erste Start, und warum sich das Spiel einmal selbst neu startet

1. Starte die Streaming-App deines Headsets und setz das Headset auf, oder lass es wach auf dem
   Kopf.
2. Starte das Spiel so, wie du es immer startest.

**Beim allerersten Start nach dem Installieren oder Updaten schließt sich das Spiel und geht von
selbst wieder auf — einmal.** Das ist so gewollt und kein Absturz.

Warum: Die Mod schaltet eine Windows-Rendering-Einstellung ein, die Zeichenarbeit von dem einen
Thread wegnimmt, der im Szenario der Flaschenhals ist. Auf dem Testrechner ging das Bild damit von
17,5 ms auf 11,14 ms und das Headset von festgenagelten 45 Hz auf saubere 90 Hz, und das Schmieren
bei Kopfbewegungen war weg. Die Einstellung steht in einer Datei, die die Engine liest, *bevor* es
überhaupt Mod-Code gibt — sie kann also immer nur für den *nächsten* Start gelten. Statt dir zu
sagen, du sollst beenden und neu starten, macht die Mod es für dich.

Eine Schleife kann daraus nicht werden: Das neu gestartete Spiel ist als solches markiert, und ein
Zähler stoppt nach zwei Versuchen. Es passiert, bevor ein Spielstand oder eine Kampagne geladen
ist, es ist also nichts von dir in der Schwebe.

Du willst das lieber selbst in der Hand behalten? Beides geht:

- `[Core] AutoRestartForGraphicsJobs = false` in `BepInEx/config/dev.gloomhavenvr.cfg` setzen und
  das Spiel selbst neu starten, wenn das Log dich darum bittet.
- `[Core] EnableGraphicsJobs = false` setzen und stattdessen `-force-gfx-jobs native` in die
  Startoptionen des Spiels eintragen. Das wirkt schon ab dem allerersten Start, und es wird
  überhaupt keine Spieldatei geschrieben.

**Das ist die einzige Spieldatei, die die Mod jemals schreibt**: zwei `key=value`-Zeilen, die zu
`GH_Data/boot.config` dazukommen, jede andere Zeile bleibt erhalten, und das Original wird einmal
nach `boot.config.gloomhavenvr-backup` kopiert. Falls das Spiel irgendwann gar nicht mehr startet,
kopier dieses Backup von Hand über `boot.config` — dabei kann die Mod dir nicht helfen, denn an
diesem Punkt läuft sie nie.

Rückgängig machen kannst du es später auf zwei Wegen: entweder `[Core] EnableGraphicsJobs = false`
setzen (die Mod schreibt die zwei Zeilen beim nächsten Start zurück und rührt sonst nichts an),
oder das Backup selbst über `boot.config` kopieren. Der zweite Weg funktioniert unabhängig davon,
ob die Mod überhaupt noch installiert ist.

---

## Updates

**Die Mod aktualisiert sich selbst, und sie fragt dich vorher.** Das solltest du lesen,
bevor es passiert — ein Fenster, mit dem du nicht gerechnet hast, sieht aus wie ein Fehler.

Einmal pro Sitzung, während du in VR im Hauptmenü stehst, schaut die Mod auf ihrer eigenen
Releases-Seite nach. Gibt es eine neuere Version, schwebt ein Panel mit der Überschrift **„Update
verfügbar“** vor dir, das dir zeigt, welche Version du hast, welche es gibt und wie groß der
Download ist. Zwei Tasten: **Ignorieren** und **Updaten**. Ignorieren schließt das Panel, und für
diese Sitzung passiert nichts weiter. Ein Szenario zu starten schließt es ebenfalls einfach.

Wenn du auf Updaten drückst, wird aus demselben Panel ein Fortschrittsbalken, und es sagt dir:

> GloomhavenVR *x.y.z* wird installiert. Das Spiel schließt sich und kommt von selbst zurück,
> sobald die Dateien ersetzt sind. Bitte schließe es nicht selbst, solange der Balken läuft.

Und genau das passiert dann auch — herunterladen, Archiv prüfen, entpacken, Spiel schließen,
Dateien tauschen, solange sie niemand offen hält, Spiel wieder starten. Der Download ist rund
70 MB, wie lange es dauert, hängt also an deiner Leitung. **Deine Spielstände, deine Kampagne und
deine Einstellungen bleiben unberührt.**

Was du dazu wissen solltest:

- **Wenn es schiefgeht, hat sich nichts geändert.** Das Panel sagt dir in klaren Worten, was
  passiert ist, und die Version, die du hattest, ist weiterhin installiert. Du kannst die neue
  Version jederzeit von Hand einspielen, genauso wie du die erste installiert hast.
- **Die alte Version bleibt liegen, bis die neue sich bewährt hat**, nämlich indem sie erfolgreich
  startet — in `BepInEx/GloomhavenVR-update/`. Dieser Ordner wird beim nächsten erfolgreichen
  Start für dich gelöscht. Falls ein Update je etwas verschlimmert: in
  `BepInEx/GloomhavenVR-update/backup/` liegen die zwei Ordner aus Schritt 3 genau so, wie sie
  waren — kopier sie über die installierten.
- **Wenn sich das Spiel nicht innerhalb von 20 Sekunden von selbst schließt**, bittet dich das
  Panel, es selbst zu schließen. Es kommt dann mit der neuen Version zurück.
- **Geprüft wird nur in VR**, nur im Hauptmenü und nur, wenn das Internet erreichbar ist. Eine
  Sitzung am flachen Bildschirm prüft nie.
- **Im Mehrspieler brauchen alle dieselbe Version.** Sind zwei Spieler auf unterschiedlichen,
  blockiert die Mod die Sitzung mit einem Versions-Hinweis, statt sie still schiefgehen zu lassen.
  Aktualisiert gemeinsam.

---

## Einstellungen

Alles ist im Headset einstellbar. Öffne das **Optionen**-Fenster des Spiels — aus dem Hauptmenü
oder aus dem Pausenmenü im Szenario — und geh auf den Reiter **VR Optionen**. Er steht neben den
Reitern des Spiels und sieht aus wie sie.

Die Reiter, in der Reihenfolge, in der du ihnen begegnest:

| Reiter | Was drinsteht |
|---|---|
| **Komfort** | Drehen, Fortbewegung, Welt greifen (samt Zoom-Grenzen), Sichtbarkeit, Hände und Zielen |
| **Bild** | Darstellung, Fenster & Tafeln, Mixed Reality, der Desktop-Monitor |
| **Brett & Karten** | Kontrollbrett, Figuren, Karten, Stapel & Hinweise |
| **Tafeln** | Tafeln & Anzeigen, Lebensbalken, der 2D-Bildschirm, Zeigen & Klicken, Texteingabe |
| **Avatar & Mehrspieler** | Dein Aussehen — Handstil und Kopfmaske, je drei — und das Zusammenspielen |
| **Umgebung & Ton** | Umgebung, „Grusel“ (die Erscheinungen), Ton, Sichtbarkeit, die Kampagnenkarte |
| **Erweitert** | Der tiefe Zwilling von allem oben, dazu ein Browser über jede einzelne Einstellung |

Änderungen wirken sofort und werden für dich gespeichert.

**Sprache:** Der Text der Mod folgt der Sprache des Spiels und wird in **Englisch und Deutsch**
ausgeliefert. Jede andere Spielsprache fällt auf Englisch zurück. Die Reiternamen oben sind die,
die ein deutscher Spieler sieht; auf Englisch heißen dieselben Reiter Comfort, Picture, Board &
cards, Panels, Avatar & multiplayer, World & sound und Advanced.

Alles gibt es auch als schlichte Textdateien unter `BepInEx/config/`, benannt
`dev.gloomhavenvr*.cfg` (eine pro Bereich). Du musst sie nicht anfassen — gedacht ist das Menü im
Headset — aber es gibt sie, und jede Einstellung bringt ihre eigene Erklärung in der Datei mit.

**Um das Spiel völlig unverändert zu spielen**, setz `[General] Enabled = false` in
`BepInEx/config/dev.gloomhavenvr.cfg`. Das ist besser als deinstallieren, wenn du nur eine Weile
Vanilla willst.

---

## Fehlersuche

**Headset schwarz, oder das Spiel läuft einfach flach auf dem Monitor.**
Trag `-force-d3d11` in die Startoptionen des Spiels ein. OpenXR am Desktop braucht D3D11.

**Das Spiel sagt, VR konnte nicht starten, weil etwas fehlt.**
Ein Teil des Zips ist nicht im Gloomhaven-Ordner gelandet. Entpack
`GloomhavenVR-<version>.zip` noch einmal dorthin und lass Windows die Ordner zusammenführen.

**Es wird die falsche Headset-Laufzeit gewählt, oder gar keine.**
Setz `[General] RuntimeOverride` in `BepInEx/config/dev.gloomhavenvr.cfg` auf die JSON-Datei
deiner Laufzeit, zum Beispiel
`C:\Program Files (x86)\Steam\steamapps\common\SteamVR\steamxr_win64.json`.
Bleibt das Feld leer, erkennt die Mod die aktive Laufzeit und geht der Reihe nach die
installierten durch.

**Das Spiel startet nach der Installation überhaupt nicht mehr.**
Kopier `GH_Data/boot.config.gloomhavenvr-backup` über `GH_Data/boot.config`. Das ist die Datei
genau so, wie sie war, bevor die Mod je gelaufen ist.

**Ein Update hat die Mod kaputtgemacht.**
Kopier die zwei Ordner aus `BepInEx/GloomhavenVR-update/backup/` zurück über die installierten —
siehe [Updates](#updates).

---

## Ein Problem melden

Das Log liegt hier:

```
<Gloomhaven-Ordner>/BepInEx/LogOutput.log
```

Schick **diese ganze Datei**, dazu:

- was du gerade gemacht hast, als es passiert ist (welches Szenario, welcher Bildschirm, allein
  oder im Mehrspieler),
- dein Headset und die App, mit der du gestreamt hast (Virtual Desktop, Quest Link, SteamVR),
- ob die anderen Spieler in der Sitzung die Mod hatten.

**Bevor du es nachstellst, setz `[General] LogLevel = Debug`** und schick *dieses* Log. In der
Voreinstellung (`Info`) schreibt die Mod nur ein paar Dutzend Zeilen pro Sitzung — was sie ist,
dass VR hochgekommen ist, und alles, worauf du reagieren könntest. Das ist für das Spielen richtig
und für einen Bericht viel zu wenig. Bei `Debug` schreibt jedes Teilsystem seinen eigenen
laufenden Kommentar, Tausende von Zeilen, und genau das macht einen Bericht beantwortbar. So oder
so nennen die ersten Zeilen die genaue Version, die bei dir läuft, und das ist das Nützlichste
darin.

Was schon bekannt ist und sich nicht zu melden lohnt, steht unter
[bekannte Einschränkungen](docs/PLAYING.de.md#bekannte-einschränkungen).

---

## Deinstallieren

Lösch diese zwei Ordner:

```
BepInEx/plugins/GloomhavenVR/
BepInEx/patchers/GloomhavenVR/
```

Wenn irgendwann ein Update gelaufen ist, lösch auch `BepInEx/GloomhavenVR-update/`, falls es noch
da ist.

Optional kannst du auch entfernen, was die Mod für OpenXR abgelegt hat — ohne die Mod tut es
nichts:

```
GH_Data/Plugins/x86_64/UnityOpenXR.dll
GH_Data/Plugins/x86_64/openxr_loader.dll
GH_Data/UnitySubsystems/UnityOpenXR/
```

Und stell `GH_Data/boot.config` aus `boot.config.gloomhavenvr-backup` wieder her, wenn du die
Rendering-Änderung loswerden willst. Wenn du ganz `BepInEx/` löschst, ist auch der Loader weg.

---

Die Kurzfassung dieser Seite liegt als `INSTALL-DEUTSCH.txt` im Release-Zip und wird aus
[`packaging/INSTALL.de.txt.in`](packaging/INSTALL.de.txt.in) erzeugt.
Die Mod aus dem Quelltext bauen: [`docs/DEVELOPING.md`](docs/DEVELOPING.md) (nur auf Englisch).
