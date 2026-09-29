# GloomhavenVR — Steam Frame install guide

<p align="center">
  <img src="docs/img/flag-en.png" width="24" alt="English">&nbsp;<b>English</b>
  &nbsp;&nbsp;|&nbsp;&nbsp;
  <a href="INSTALL-STEAM-FRAME.de.md"><img src="docs/img/flag-de.png" width="24" alt="Deutsch">&nbsp;Deutsch</a>
</p>

<p align="center">
  <img src="docs/img/divider.png" width="600" alt="">
</p>

This guide is for running Gloomhaven **on the Steam Frame itself**. If you run the game on a
Windows PC and stream it to the headset, use the [PC VR install guide](INSTALL.md).

## What you need

| | |
|---|---|
| **Game** | Gloomhaven installed from Steam on the Frame |
| **Controls** | Two tracked controllers with thumbsticks |
| **Downloads** | BepInEx 5.4.23.5 for Windows x64 and the latest GloomhavenVR release ZIP |

<p align="center">
  <img src="docs/img/divider-small.png" width="340" alt="">
</p>

## 1. Install the game and download the files

1. Install **Gloomhaven** from your Steam library on the Frame, then switch to **Desktop Mode**.
2. In Chrome or another installed browser, download **`BepInEx_win_x64_5.4.23.5.zip`** from the
   [BepInEx release](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5).
3. Download **`GloomhavenVR-<version>.zip`** from the
   [latest mod release](https://github.com/McFredward/GloomhavenVR/releases/latest).

Use the **Windows x64** BepInEx archive: Gloomhaven runs as a Windows game through Proton.

<p align="center">
  <img src="docs/img/divider-small.png" width="340" alt="">
</p>

## 2. Extract both archives

Open both ZIP files in **Dolphin** and extract their **contents** into:

```text
/home/steamos/.local/share/Steam/steamapps/common/Gloomhaven
```

This is the folder containing `GH.exe`. Merge the `BepInEx` folders if Dolphin asks. If your
Steam library is elsewhere, use **Gloomhaven → Manage → Browse local files** to find the correct
folder. Dolphin hides `.local` by default; press **Ctrl+H** to show hidden folders.

Check that these two folders now exist inside the Gloomhaven folder:

```text
BepInEx/plugins/GloomhavenVR/
BepInEx/patchers/GloomhavenVR/
```

<p align="center">
  <img src="docs/img/divider-small.png" width="340" alt="">
</p>

## 3. Set Steam launch options

Open **Gloomhaven → Properties → General → Launch Options** in Steam and enter exactly:

```text
WINEDLLOVERRIDES="winhttp=n,b" %command%
```

<p align="center">
  <img src="docs/img/divider-small.png" width="340" alt="">
</p>

## 4. Start the game

Launch Gloomhaven. The first launch may close while the mod applies its rendering settings.
Automatic relaunch may not work on the Frame; if the game closes, **start it again manually**.
When the mod loads, you should see the VR menu and stand at the table.

Your saves, campaign and settings are not touched. The mod backs up `GH_Data/boot.config` as
`GH_Data/boot.config.gloomhavenvr-backup` before changing its rendering settings.

<p align="center">
  <img src="docs/img/divider-small.png" width="340" alt="">
</p>

## Updating

When a newer release is available, the VR main menu offers an update. You can also download the
new release ZIP and extract it into the same Gloomhaven folder, merging `BepInEx` again.
All VR players in a multiplayer session need the same mod build.

<p align="center">
  <img src="docs/img/divider-small.png" width="340" alt="">
</p>

## Troubleshooting

| What you see | What to do |
|---|---|
| The mod does not load | Check the launch option and the two `BepInEx` folders above. Look for `BepInEx/LogOutput.log` after a launch. |
| The first launch closes | Start the game a second time manually. |
| The game will not start | Copy `GH_Data/boot.config.gloomhavenvr-backup` over `GH_Data/boot.config`. |

To report a problem, keep `BepInEx/LogOutput.log` from the affected run and describe what you
were doing. For a reproducible issue, set `[General] LogLevel = Debug` in
`BepInEx/config/dev.gloomhavenvr.cfg`, then reproduce it for a more detailed log.

<p align="center">
  <img src="docs/img/divider-small.png" width="340" alt="">
</p>

## Uninstall

Delete `BepInEx/plugins/GloomhavenVR/` and `BepInEx/patchers/GloomhavenVR/`. Restore
`GH_Data/boot.config` from `boot.config.gloomhavenvr-backup` if that backup exists. Remove the
Steam launch option above if you also remove BepInEx.
