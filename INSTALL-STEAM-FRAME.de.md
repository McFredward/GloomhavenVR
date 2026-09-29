# GloomhavenVR — Installation auf Steam Frame

<p align="center">
  <a href="INSTALL-STEAM-FRAME.md"><img src="docs/img/flag-en.png" width="24" alt="English">&nbsp;English</a>
  &nbsp;&nbsp;|&nbsp;&nbsp;
  <img src="docs/img/flag-de.png" width="24" alt="Deutsch">&nbsp;<b>Deutsch</b>
</p>

<p align="center">
  <img src="docs/img/divider.png" width="600" alt="">
</p>

Diese Anleitung gilt, wenn Gloomhaven **direkt auf der Steam Frame** läuft. Startest du das Spiel
auf einem Windows-PC und streamst es zum Headset, nutze die [PC-VR-Installationsanleitung](INSTALL.de.md).

## Was du brauchst

| | |
|---|---|
| **Spiel** | Gloomhaven aus Steam auf der Frame installiert |
| **Steuerung** | Zwei getrackte Controller mit Thumbsticks |
| **Downloads** | BepInEx 5.4.23.5 für Windows x64 und das aktuelle GloomhavenVR-Release-ZIP |

<p align="center">
  <img src="docs/img/divider-small.png" width="340" alt="">
</p>

## 1. Spiel installieren und Dateien herunterladen

1. Installiere **Gloomhaven** aus deiner Steam-Bibliothek auf der Frame und wechsle in den
   **Desktop-Modus**.
2. Lade in Chrome oder einem anderen installierten Browser **`BepInEx_win_x64_5.4.23.5.zip`**
   vom [BepInEx-Release](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5) herunter.
3. Lade **`GloomhavenVR-<version>.zip`** vom
   [aktuellen Mod-Release](https://github.com/McFredward/GloomhavenVR/releases/latest) herunter.

Nimm das **Windows-x64**-Archiv von BepInEx: Gloomhaven läuft über Proton als Windows-Spiel.

<p align="center">
  <img src="docs/img/divider-small.png" width="340" alt="">
</p>

## 2. Beide Archive entpacken

Öffne beide ZIP-Dateien in **Dolphin** und entpacke ihren **Inhalt** nach:

```text
/home/steamos/.local/share/Steam/steamapps/common/Gloomhaven
```

Das ist der Ordner mit `GH.exe`. Führe die `BepInEx`-Ordner zusammen, falls Dolphin fragt. Liegt
deine Steam-Bibliothek woanders, findest du den Ordner über **Gloomhaven → Verwalten → Lokale
Dateien durchsuchen**. Dolphin blendet `.local` zunächst aus; mit **Strg+H** werden versteckte
Ordner sichtbar.

Prüfe, ob diese beiden Ordner nun im Gloomhaven-Ordner liegen:

```text
BepInEx/plugins/GloomhavenVR/
BepInEx/patchers/GloomhavenVR/
```

<p align="center">
  <img src="docs/img/divider-small.png" width="340" alt="">
</p>

## 3. Steam-Startoptionen setzen

Öffne in Steam **Gloomhaven → Eigenschaften → Allgemein → Startoptionen** und trage genau das ein:

```text
WINEDLLOVERRIDES="winhttp=n,b" %command%
```

<p align="center">
  <img src="docs/img/divider-small.png" width="340" alt="">
</p>

## 4. Spiel starten

Starte Gloomhaven. Beim ersten Start kann das Spiel geschlossen werden, während die Mod ihre
Rendering-Einstellungen übernimmt. Der automatische Neustart klappt auf der Frame möglicherweise
nicht; wenn sich das Spiel schließt, **starte es selbst ein zweites Mal**. Wenn die Mod geladen ist,
solltest du das VR-Menü sehen und am Tisch stehen.

Deine Spielstände, Kampagne und Einstellungen bleiben unangetastet. Die Mod sichert
`GH_Data/boot.config` als `GH_Data/boot.config.gloomhavenvr-backup`, bevor sie die
Rendering-Einstellungen ändert.

<p align="center">
  <img src="docs/img/divider-small.png" width="340" alt="">
</p>

## Updates

Wenn ein neueres Release verfügbar ist, bietet das VR-Hauptmenü ein Update an. Du kannst auch das
neue Release-ZIP herunterladen und wieder in denselben Gloomhaven-Ordner entpacken; führe
`BepInEx` erneut zusammen. Im Mehrspieler brauchen alle VR-Spieler denselben Mod-Build.

<p align="center">
  <img src="docs/img/divider-small.png" width="340" alt="">
</p>

## Fehlersuche

| Was du siehst | Was du machst |
|---|---|
| Die Mod wird nicht geladen | Prüf die Startoption und die beiden `BepInEx`-Ordner oben. Such nach einem Start nach `BepInEx/LogOutput.log`. |
| Der erste Start schließt sich | Starte das Spiel selbst ein zweites Mal. |
| Das Spiel startet nicht mehr | Kopiere `GH_Data/boot.config.gloomhavenvr-backup` über `GH_Data/boot.config`. |

Für eine Fehlermeldung bewahre `BepInEx/LogOutput.log` vom betroffenen Durchlauf auf und
beschreibe, was du gerade gemacht hast. Bei einem reproduzierbaren Problem setz
`[General] LogLevel = Debug` in `BepInEx/config/dev.gloomhavenvr.cfg` und wiederhole es für
ein genaueres Log.

<p align="center">
  <img src="docs/img/divider-small.png" width="340" alt="">
</p>

## Deinstallieren

Lösch `BepInEx/plugins/GloomhavenVR/` und `BepInEx/patchers/GloomhavenVR/`. Stell
`GH_Data/boot.config` aus `boot.config.gloomhavenvr-backup` wieder her, falls das Backup
vorhanden ist. Entferne die Steam-Startoption oben, wenn du auch BepInEx entfernst.
