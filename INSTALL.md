# GloomhavenVR — install guide

<p align="center">
  <img src="docs/img/flag-en.png" width="24" alt="English">&nbsp;<b>English</b>
  &nbsp;&nbsp;|&nbsp;&nbsp;
  <a href="INSTALL.de.md"><img src="docs/img/flag-de.png" width="24" alt="Deutsch">&nbsp;Deutsch</a>
</p>

Everything you need to get from a normal copy of Gloomhaven to standing at the table, and
everything you might need afterwards. If you have not read what this is yet, start at the
[README](README.md).

- [What you need](#what-you-need)
- [Pick your headset runtime](#pick-your-headset-runtime)
- [1. Install BepInEx](#1-install-bepinex)
- [2. Install the mod](#2-install-the-mod)
- [3. Check you unpacked in the right place](#3-check-you-unpacked-in-the-right-place)
- [First launch, and why the game restarts itself once](#first-launch-and-why-the-game-restarts-itself-once)
- [Updating](#updating)
- [Settings](#settings)
- [Troubleshooting](#troubleshooting)
- [Reporting a problem](#reporting-a-problem)
- [Uninstall](#uninstall)

---

## What you need

| | |
|---|---|
| **Game** | Gloomhaven (digital) for PC, v1.1.x — Steam or GOG. The mod is built against v1.1.8307.0, the last patch the game received. |
| **OS** | Windows, running the game under **D3D11** (the Windows default for this engine). |
| **Headset** | Any PC-VR headset with an **OpenXR runtime** — the piece of software your headset's own app installs to let PC games talk to it. Developed and played on a **Quest 3 over Virtual Desktop (VDXR)** at 90 Hz. Quest Link / Air Link, Steam Link and SteamVR use the same interface and should work, but have not been tried. |
| **Controllers** | Two tracked controllers with thumbsticks (Touch-style layout: A/B and X/Y face buttons, trigger, grip). |
| **Loader** | **BepInEx 5.4.23.5 (x64)** — the standard mod loader for Unity games. You install it yourself, once, in step 1. |
| **Play space** | Room-scale or standing. A small space is fine — you can fly, drag the world over to you, and recentre at any time. |

> **This is a PC-VR mod.** It does not run on a standalone headset by itself — the game runs on
> your PC and you stream or tether the headset to it, exactly as you would for any PC-VR title.

---

## Pick your headset runtime

Only one OpenXR runtime can be the active one at a time, and it has to be yours **before** you
start the game. Set it in whichever app you stream with:

- **Virtual Desktop** — select **VDXR** as the OpenXR runtime in the Virtual Desktop streamer
  settings on your PC.
- **Quest Link / Air Link** — Meta Quest Link app → Settings → General → OpenXR Runtime →
  *"Set Meta Quest Link as active"*.
- **Steam Link / SteamVR** — SteamVR Settings → OpenXR → *"Set SteamVR as OpenXR runtime"*.

If something else keeps taking it back, you can pin one for this game only — see
[Troubleshooting](#troubleshooting).

---

## 1. Install BepInEx

1. Download **`BepInEx_win_x64_5.4.23.5.zip`** from the
   [BepInEx 5.4.23.5 release page](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5).
2. Extract it into the **Gloomhaven install folder** — the folder that contains `GH.exe`.
   (Steam: right-click the game → Manage → Browse local files.)
3. Start the game once normally, then quit. Check that `BepInEx/LogOutput.log` now exists.
   That file appearing is proof it is working.
4. Recommended: open `BepInEx/config/BepInEx.cfg` and set
   `[Chainloader] HideManagerGameObject = true`.

---

## 2. Install the mod

Get **`GloomhavenVR-<version>.zip`** from the [releases page](https://github.com/McFredward/GloomhavenVR/releases) and extract it into
the same Gloomhaven folder, merging the `BepInEx/` directory with the one that is already there.

---

## 3. Check you unpacked in the right place

There is one thing to check, and it is just that two folders exist:

```
Gloomhaven/                      ← the folder with GH.exe in it
├── GH.exe
├── INSTALL.txt                  ← these two came out of the mod's zip
├── INSTALL-DEUTSCH.txt          ←   (the same short guide in German)
└── BepInEx/
    ├── plugins/GloomhavenVR/    ← both of these must exist
    └── patchers/GloomhavenVR/
```

If either `GloomhavenVR` folder is missing, you unpacked into the wrong place or the merge did not
happen. Unpack the zip again rather than moving files around by hand.

---

## First launch, and why the game restarts itself once

1. Start your headset's streaming app and put the headset on, or leave it awake on your head.
2. Launch the game the way you normally do.

**On the very first start after installing or updating, the game will close and reopen by
itself — once.** That is expected and it is not a crash.

Why: the mod switches on one Windows rendering setting that moves drawing work off the single
thread that is the bottleneck in a scenario. On the test machine that took the frame from 17.5 ms
to 11.14 ms and the headset from a locked 45 Hz to a clean 90 Hz, and the smearing during head
movement disappeared. The setting lives in a file the engine reads *before* any mod code exists,
so it can only ever apply to the *next* start. Rather than telling you to quit and start again,
the mod does it for you.

It cannot loop: the restarted game is marked as such, and a counter stops it after two attempts.
It happens before any save or campaign is loaded, so nothing of yours is in the air.

Prefer to keep control of it? Both of these work:

- Set `[Core] AutoRestartForGraphicsJobs = false` in `BepInEx/config/dev.gloomhavenvr.cfg`
  and restart the game yourself when the log asks you to.
- Set `[Core] EnableGraphicsJobs = false` and add `-force-gfx-jobs native` to the game's launch
  options instead. That works from the very first start, and no game file is written at all.

**This is the only game file the mod ever writes**: two `key=value` lines added to
`GH_Data/boot.config`, every other line preserved, and the original copied once to
`boot.config.gloomhavenvr-backup`. If the game ever refuses to start, copy that backup back
over `boot.config` by hand — the mod cannot help you there, because at that point it never runs.

To undo it later, either set `[Core] EnableGraphicsJobs = false` (the mod writes the two lines
back on the next start and touches nothing else) or copy the backup over `boot.config` yourself.
The second one works whether or not the mod is still installed.

---

## Updating

**The mod updates itself, and it will ask you first.** This is worth reading before it happens,
because a window you were not expecting looks like something going wrong.

Once per session, while you are standing in the main menu in VR, the mod checks its own releases
page. If there is a newer version it floats a panel in front of you titled **"Update available"**
(*"Update verfügbar"*), showing the version you have, the version there is, and how large the
download is. Two buttons: **Ignore** and **Update**. Ignore closes it and nothing else happens
that session. Starting a scenario also just closes it.

If you press Update, the same panel turns into a progress bar and tells you:

> Installing GloomhavenVR *x.y.z*. The game closes and comes back by itself when the files have
> been replaced. Do not close it by hand while the bar is running.

Which is exactly what happens — it downloads, checks the archive, unpacks it, closes the game,
swaps the files while nothing is holding them open, and starts the game again. The download is
around 70 MB, so how long it takes is your line speed. **Your saves, your campaign and your
settings are not touched.**

Things worth knowing:

- **If it fails, nothing changed.** The panel tells you what went wrong in plain words and the
  version you were running is still installed. You can always install the new version by hand
  instead, the same way you installed the first one.
- **The old version is kept until the new one has proven itself** by starting successfully, in
  `BepInEx/GloomhavenVR-update/`. That folder is deleted for you on the next successful start.
  If an update ever leaves you worse off, `BepInEx/GloomhavenVR-update/backup/` holds the two
  folders from step 3 exactly as they were — copy them back over the installed ones.
- **If the game does not close by itself within 20 seconds**, the panel asks you to close it
  yourself. It will come back with the new version installed.
- **The check only happens in VR**, only at the main menu, and only if the internet is reachable.
  A flat-screen session never checks.
- **Everyone in a multiplayer session needs the same version.** If two players are on different
  ones, the mod blocks the session with a version-mismatch dialog rather than letting it go wrong
  quietly. Update together.

---

## Settings

Everything is adjustable in the headset. Open the game's own **Options** window — from the main
menu, or from the pause menu during a scenario — and pick the **VR Options** tab ("VR Optionen"
in German). It sits next to the game's own tabs and looks like them.

The tabs, in the order you meet them:

| Tab | What is in it |
|---|---|
| **Komfort** (Comfort) | Turning, locomotion, grabbing the world (including the zoom limits), visibility, hands and aiming |
| **Bild** (Picture) | Presentation, windows & panels, mixed reality, the desktop monitor |
| **Brett & Karten** (Board & cards) | Control board, figures, cards, piles & hints |
| **Tafeln** (Panels) | Panels & readouts, health bars, the 2D screen, pointing & clicking, text entry |
| **Avatar & Mehrspieler** (Avatar & multiplayer) | Your appearance — hand style and head mask, three of each — and playing together |
| **Umgebung & Ton** (World & sound) | Environment, "Grusel" (the apparitions), sound, visibility, the campaign map |
| **Erweitert** (Advanced) | The deep twin of everything above, plus a browser over every single setting |

Changes apply live and are saved for you.

**Language:** the mod's own text follows the game's language and ships **English and German**.
Any other game language falls back to English. The German tab names above are what a German
player sees; an English player sees the English ones.

Everything also exists as plain text files under `BepInEx/config/`, named `dev.gloomhavenvr*.cfg`
(one per area). You do not need to touch them — the in-headset menu is the intended route — but
they are there, and each setting carries its own explanation in the file.

**To play the game completely unmodified**, set `[General] Enabled = false` in
`BepInEx/config/dev.gloomhavenvr.cfg`. That is better than uninstalling if you only want vanilla
for a while.

---

## Troubleshooting

**Headset black, or the game just runs flat on the monitor.**
Add `-force-d3d11` to the game's launch options. Desktop OpenXR needs D3D11.

**The game says VR could not start because something is missing.**
Part of the zip did not make it into the Gloomhaven folder. Unpack
`GloomhavenVR-<version>.zip` into it again and let Windows merge the folders.

**The wrong headset runtime is picked, or none is.**
Set `[General] RuntimeOverride` in `BepInEx/config/dev.gloomhavenvr.cfg` to your runtime's JSON
file, for example
`C:\Program Files (x86)\Steam\steamapps\common\SteamVR\steamxr_win64.json`.
Left empty, the mod detects the active runtime and fails over through the installed ones.

**The game will not start at all after installing.**
Copy `GH_Data/boot.config.gloomhavenvr-backup` over `GH_Data/boot.config`. That is the file
exactly as it was before the mod ever ran.

**An update left the mod broken.**
Copy the two folders out of `BepInEx/GloomhavenVR-update/backup/` back over the installed ones —
see [Updating](#updating).

---

## Reporting a problem

The log is here:

```
<Gloomhaven folder>/BepInEx/LogOutput.log
```

Send **that whole file**, plus:

- what you were doing when it happened (which scenario, which screen, solo or multiplayer),
- your headset and which app you streamed with (Virtual Desktop, Quest Link, SteamVR),
- whether the other players in the session had the mod.

**Before you reproduce it, set `[General] LogLevel = Debug`** and send *that* log. At the default
(`Info`) the mod writes only a few dozen lines a session — what it is, that VR came up, and
anything you could act on — which is right for playing and far too little for a report. At `Debug`
every subsystem writes its own running commentary, thousands of lines, and that is what makes a
report answerable. Either way the first lines name the exact version you are running, which is the
single most useful thing in it.

Things that are already known and are not worth reporting are listed under
[known limitations](docs/PLAYING.md#known-limitations).

---

## Uninstall

Delete these two folders:

```
BepInEx/plugins/GloomhavenVR/
BepInEx/patchers/GloomhavenVR/
```

If an update has run at some point, also delete `BepInEx/GloomhavenVR-update/` if it is still
there.

Optionally also remove what the mod placed for OpenXR — it does nothing without the mod:

```
GH_Data/Plugins/x86_64/UnityOpenXR.dll
GH_Data/Plugins/x86_64/openxr_loader.dll
GH_Data/UnitySubsystems/UnityOpenXR/
```

And restore `GH_Data/boot.config` from `boot.config.gloomhavenvr-backup` if you want the
rendering change gone. Deleting all of `BepInEx/` removes the loader as well.

---

The short version of this page ships inside the release zip as `INSTALL.txt`, generated from
[`packaging/INSTALL.txt.in`](packaging/INSTALL.txt.in).
Building the mod from source: [`docs/DEVELOPING.md`](docs/DEVELOPING.md).
