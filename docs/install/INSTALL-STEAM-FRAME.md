# GloomhavenVR — Steam Frame install guide

<p align="center">
  <img src="../img/flag-en.png" width="24" alt="English">&nbsp;<b>English</b>
  &nbsp;&nbsp;|&nbsp;&nbsp;
  <a href="INSTALL-STEAM-FRAME.de.md"><img src="../img/flag-de.png" width="24" alt="Deutsch">&nbsp;Deutsch</a>
</p>

<p align="center">
  <img src="../img/divider.png" width="600" alt="">
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
  <img src="../img/divider-small.png" width="340" alt="">
</p>

## 1. Install the game and download the files

1. Install **Gloomhaven** from your Steam library on the Frame, then switch to **Desktop Mode**.
2. In Chrome or another installed browser, download **`BepInEx_win_x64_5.4.23.5.zip`** from the
   [BepInEx release](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5).
3. Download **`GloomhavenVR-<version>.zip`** from the
   [latest mod release](https://github.com/McFredward/GloomhavenVR/releases/latest).

Use the **Windows x64** BepInEx archive: Gloomhaven runs as a Windows game through Proton.

<p align="center">
  <img src="../img/divider-small.png" width="340" alt="">
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
  <img src="../img/divider-small.png" width="340" alt="">
</p>

## 3. Set up Steam

In Dolphin, open `BepInEx/plugins/GloomhavenVR/FrameSetup/` inside the Gloomhaven folder and
double-click **`GloomhavenVR-Setup.desktop`**.
Choose **Execute** if Dolphin asks. The setup configures the original game's BepInEx launch option,
adds a separate **GloomhavenVR** entry to the VR library, installs its icon and library artwork, and prepares
the game's rendering settings before the first VR launch. You do not need to change file permissions
or edit Steam settings. Steam may restart to load the new entry. The original **Gloomhaven** entry
stays flat; both entries use the same installed game, Steam account and Proton game profile.

If your desktop does not offer Execute or setup cannot find `GH.exe` in a different Steam library,
open a terminal in the Gloomhaven folder and run:

```bash
bash ./BepInEx/plugins/GloomhavenVR/FrameSetup/install-steam-frame.sh
```

Using `bash` also works if your ZIP extractor did not preserve executable permissions. If setup
cannot update Steam, it reports which step failed and leaves the original Steam entry intact.

The new entry is a local shortcut; it does not change Gloomhaven's Steamworks classification.
Steam may still show the original game's per-app VR resolution settings only while GloomhavenVR
is running. Set those settings in a running session if they are not listed before launch.

<p align="center">
  <img src="../img/divider-small.png" width="340" alt="">
</p>

## 4. Start the game

Launch **GloomhavenVR** for VR, or the original **Gloomhaven** entry for flat play. The setup
prepares the rendering settings, so the first VR launch should open normally. When the mod loads,
you should see the VR menu and stand at the table.

For the first check, start the original entry once and confirm it stays flat. Then start the
**GloomhavenVR** entry and confirm the VR menu appears. If the second entry starts flat, Steam
did not forward `--gloomhavenvr`; keep `BepInEx/LogOutput.log` and report it. The shortcut is
designed to fail closed: a lost flag must never make the original entry start VR.

Your saves, campaign and settings are not touched. The mod backs up `GH_Data/boot.config` as
`GH_Data/boot.config.gloomhavenvr-backup` before changing its rendering settings.

<p align="center">
  <img src="../img/divider-small.png" width="340" alt="">
</p>

## Updating

When a newer release is available, the VR main menu offers an update. You can also download the
new release ZIP and extract it into the same Gloomhaven folder, merging `BepInEx` again.
All VR players in a multiplayer session need the same mod build.

<p align="center">
  <img src="../img/divider-small.png" width="340" alt="">
</p>

## Troubleshooting

| What you see | What to do |
|---|---|
| GloomhavenVR does not appear in Steam | Run `BepInEx/plugins/GloomhavenVR/FrameSetup/GloomhavenVR-Setup.desktop` again. Keep the setup output if it reports an error. |
| The mod does not load from GloomhavenVR | Rerun setup. Check the two `BepInEx` folders and `BepInEx/patchers/GloomhavenVR/frame-launch-opt-in.marker`. Look for `BepInEx/LogOutput.log` after a launch. |
| The original Gloomhaven entry opens VR | Remove `--gloomhavenvr` from that entry's launch options, then run `GloomhavenVR-Setup.desktop` again. |
| GloomhavenVR opens flat | Steam may not have forwarded the VR flag. Check that the shortcut targets `launch-steam-frame.sh`, then keep `BepInEx/LogOutput.log` for a report. |
| The first VR launch closes | Start GloomhavenVR a second time. Keep `BepInEx/LogOutput.log` for a report if it closes again. |
| The game will not start | Copy `GH_Data/boot.config.gloomhavenvr-backup` over `GH_Data/boot.config`. |
| The helper reports `bash\r: No such file or directory` | Extract the latest mod ZIP again. Its SteamOS launchers have Unix line endings. |

To report a setup problem, keep `BepInEx/plugins/GloomhavenVR/FrameSetup/steam-frame-setup.log`.
For an in-game problem, keep `BepInEx/LogOutput.log` from the affected run and describe what you
were doing. For a reproducible issue, set `[General] LogLevel = Debug` in
`BepInEx/config/dev.gloomhavenvr.cfg`, then reproduce it for a more detailed log.

<p align="center">
  <img src="../img/divider-small.png" width="340" alt="">
</p>

## Uninstall

Remove the **GloomhavenVR** non-Steam shortcut in Steam. Delete
`/home/steamos/.local/share/GloomhavenVR/`, `BepInEx/plugins/GloomhavenVR/` and
`BepInEx/patchers/GloomhavenVR/`. Restore `GH_Data/boot.config` from
`boot.config.gloomhavenvr-backup` if that backup exists. Remove the original game's
`WINEDLLOVERRIDES` launch option from the original game's Steam properties if you also remove
BepInEx.
