# GloomhavenVR — Installation auf Steam Frame

<p align="center">
  <a href="INSTALL-STEAM-FRAME.md"><img src="../img/flag-en.png" width="24" alt="English">&nbsp;English</a>
  &nbsp;&nbsp;|&nbsp;&nbsp;
  <img src="../img/flag-de.png" width="24" alt="Deutsch">&nbsp;<b>Deutsch</b>
</p>

<p align="center">
  <img src="../img/divider.png" width="600" alt="">
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
  <img src="../img/divider-small.png" width="340" alt="">
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
  <img src="../img/divider-small.png" width="340" alt="">
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
  <img src="../img/divider-small.png" width="340" alt="">
</p>

## 3. Steam einrichten

Öffne `BepInEx/plugins/GloomhavenVR/FrameSetup/` im Gloomhaven-Ordner in Dolphin und doppelklicke auf
**`GloomhavenVR-Setup.desktop`**. Falls Dolphin nachfragt, wähle **Ausführen**. Das Setup richtet
die BepInEx-Startoption des Originalspiels ein, erstellt einen separaten Eintrag **GloomhavenVR**
in der VR-Bibliothek, fügt Symbol und Bibliotheksbilder hinzu und bereitet die Rendering-Einstellungen vor dem
ersten VR-Start vor. Du musst weder Dateiberechtigungen noch Steam-Einstellungen selbst ändern.
Steam wird möglicherweise neu gestartet, um den neuen Eintrag zu laden. Der ursprüngliche Eintrag
**Gloomhaven** bleibt flat; beide Einträge verwenden dieselbe Spielinstallation, denselben
Steam-Account und dasselbe Proton-Spielprofil.

Bietet dein Desktop kein Ausführen an oder findet die Einrichtung `GH.exe` in einer anderen
Steam-Bibliothek nicht, öffne im Gloomhaven-Ordner ein Terminal und gib ein:

```bash
bash ./BepInEx/plugins/GloomhavenVR/FrameSetup/install-steam-frame.sh
```

Mit `bash` funktioniert es auch, falls das Entpackprogramm die Ausführungsberechtigung nicht
erhalten hat. Falls das Setup Steam nicht aktualisieren kann, nennt es den fehlgeschlagenen Schritt
und lässt den ursprünglichen Steam-Eintrag intakt.

Der neue Eintrag ist eine lokale Verknüpfung und ändert die Steamworks-Einstufung des
Originalspiels nicht. Steam zeigt dessen VR-Auflösungseinstellungen möglicherweise weiterhin
erst an, während GloomhavenVR läuft. Falls sie vor dem Start fehlen, stell sie während eines
laufenden Spiels für Gloomhaven ein.

<p align="center">
  <img src="../img/divider-small.png" width="340" alt="">
</p>

## 4. Spiel starten

Starte **GloomhavenVR** für VR oder den ursprünglichen Eintrag **Gloomhaven** für Flat. Das Setup
bereitet die Rendering-Einstellungen vor, sodass der erste VR-Start normalerweise direkt
funktioniert. Wenn die Mod geladen ist, solltest du das VR-Menü sehen und am Tisch stehen.

Prüfe zuerst mit einem Start des ursprünglichen Eintrags, dass er flat bleibt. Starte danach
**GloomhavenVR** und prüfe, ob das VR-Menü erscheint. Wenn der zweite Eintrag flat startet, hat
Steam `--gloomhavenvr` nicht weitergereicht. Bewahre dann `BepInEx/LogOutput.log` auf und melde
das Problem. Die Verknüpfung bleibt bei verlorenem Startargument absichtlich flat, damit der
ursprüngliche Eintrag niemals versehentlich VR startet.

Deine Spielstände, Kampagne und Einstellungen bleiben unangetastet. Die Mod sichert
`GH_Data/boot.config` als `GH_Data/boot.config.gloomhavenvr-backup`, bevor sie die
Rendering-Einstellungen ändert.

<p align="center">
  <img src="../img/divider-small.png" width="340" alt="">
</p>

## Updates

Wenn ein neueres Release verfügbar ist, bietet das VR-Hauptmenü ein Update an. Du kannst auch das
neue Release-ZIP herunterladen und wieder in denselben Gloomhaven-Ordner entpacken; führe
`BepInEx` erneut zusammen. Im Mehrspieler brauchen alle VR-Spieler denselben Mod-Build.

<p align="center">
  <img src="../img/divider-small.png" width="340" alt="">
</p>

## Fehlersuche

| Was du siehst | Was du machst |
|---|---|
| GloomhavenVR erscheint nicht in Steam | Starte `BepInEx/plugins/GloomhavenVR/FrameSetup/GloomhavenVR-Setup.desktop` erneut. Bewahre die Ausgabe auf, falls ein Fehler gemeldet wird. |
| Die Mod wird über GloomhavenVR nicht geladen | Führe das Setup erneut aus. Prüf die beiden `BepInEx`-Ordner und `BepInEx/patchers/GloomhavenVR/frame-launch-opt-in.marker`. Such nach einem Start nach `BepInEx/LogOutput.log`. |
| Der ursprüngliche Gloomhaven-Eintrag startet VR | Entferne `--gloomhavenvr` aus dessen Startoptionen und starte `GloomhavenVR-Setup.desktop` erneut. |
| GloomhavenVR startet flat | Steam hat das VR-Argument möglicherweise nicht weitergereicht. Prüfe, ob die Verknüpfung auf `launch-steam-frame.sh` zeigt, und bewahre `BepInEx/LogOutput.log` für eine Fehlermeldung auf. |
| Der erste VR-Start schließt sich | Starte GloomhavenVR ein zweites Mal. Falls es erneut schließt, bewahre `BepInEx/LogOutput.log` für eine Fehlermeldung auf. |
| Das Spiel startet nicht mehr | Kopiere `GH_Data/boot.config.gloomhavenvr-backup` über `GH_Data/boot.config`. |
| Das Hilfsskript meldet `bash\r: No such file or directory` | Entpacke das aktuelle Mod-ZIP erneut. Dessen SteamOS-Starter haben Unix-Zeilenenden. |

Für ein Setup-Problem bewahre `BepInEx/plugins/GloomhavenVR/FrameSetup/steam-frame-setup.log` auf.
Für ein Problem im Spiel bewahre `BepInEx/LogOutput.log` vom betroffenen Durchlauf auf und
beschreibe, was du gerade gemacht hast. Bei einem reproduzierbaren Problem setz
`[General] LogLevel = Debug` in `BepInEx/config/dev.gloomhavenvr.cfg` und wiederhole es für
ein genaueres Log.

<p align="center">
  <img src="../img/divider-small.png" width="340" alt="">
</p>

## Deinstallieren

Entferne die Steam-fremde Verknüpfung **GloomhavenVR**. Lösch
`/home/steamos/.local/share/GloomhavenVR/`, `BepInEx/plugins/GloomhavenVR/` und
`BepInEx/patchers/GloomhavenVR/`. Stell `GH_Data/boot.config` aus
`boot.config.gloomhavenvr-backup` wieder her, falls das Backup vorhanden ist. Entferne die
`WINEDLLOVERRIDES`-Startoption in den Steam-Eigenschaften des Originalspiels, wenn du auch
BepInEx entfernst.
