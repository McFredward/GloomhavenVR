<p align="center">
  <img src="src/GloomhavenVR/Assets/GloomhavenVR_logo.png" alt="GloomhavenVR" width="620">
</p>

<p align="center">
  <b>Play Gloomhaven (digital) as a room-scale VR board game.</b><br>
  You stand at the table. You pick your cards up with your hands.
</p>

---

GloomhavenVR is a mod for the PC version of **Gloomhaven (digital)**. It does not stream the
flat game into a headset — it rebuilds the game as a tabletop you stand at: the scenario is a
lit diorama in front of you, your ability cards fan out of your palm, you reach out and move
the miniatures, and the campaign map is a room with a real table in it.

It installs alongside the normal game, changes nothing you cannot undo, and can be switched
off with a single line in a config file to get the original game back.

<!-- GIF: docs/img/gifs/overview.gif -->
> **[ GIF PLACEHOLDER — `docs/img/gifs/overview.gif` ]**
> *The hero shot: standing at the table in a lit scenario, then dragging, rotating and zooming
> the board with the two-handed world grab.*

---

## Table of contents

- [Requirements](#requirements)
- [Install](#install)
- [First launch](#first-launch)
- [Playing in VR](#playing-in-vr)
- [Settings](#settings)
- [Multiplayer](#multiplayer)
- [Known limitations](#known-limitations)
- [Troubleshooting](#troubleshooting)
- [Uninstall](#uninstall)
- [Credits and licence](#credits-and-licence)

---

## Requirements

| | |
|---|---|
| **Game** | Gloomhaven (digital) for PC, v1.1.x — Steam or GOG. The mod is built against v1.1.8307.0, the last patch the game received. |
| **OS** | Windows, running the game under **D3D11** (the Windows default for this engine). |
| **Headset** | Any PC-VR headset with an **OpenXR** runtime. Developed and tested on a **Quest 3 over Virtual Desktop (VDXR)** at 90 Hz. Quest Link / Air Link, Steam Link and SteamVR are the other supported runtimes. |
| **Controllers** | Two tracked controllers with thumbsticks (Touch-style layout: A/B and X/Y face buttons, trigger, grip). |
| **Loader** | **BepInEx 5.4.23.5 (x64)** — you install this yourself, once, see below. |
| **Play space** | Room-scale or standing. A small space is fine — you can fly, drag the world to you, and recenter at any time. |

> **This is a PC-VR mod.** It does not run on a standalone headset by itself — the game runs on
> your PC and you stream or tether the headset to it, exactly as you would for any PC-VR title.

**Your OpenXR runtime must be the active one** before you start the game:

- **Quest Link / Air Link** — Meta Quest Link app → Settings → General → OpenXR Runtime →
  *"Set Meta Quest Link as active"*.
- **Virtual Desktop** — select **VDXR** as the OpenXR runtime in the Virtual Desktop streamer
  settings.
- **Steam Link / SteamVR** — SteamVR Settings → OpenXR → *"Set SteamVR as OpenXR runtime"*.

If the wrong runtime keeps winning, you can pin one for this game only — see
[Troubleshooting](#troubleshooting).

---

## Install

### 1. Install BepInEx 5.4.23.5

1. Download **`BepInEx_win_x64_5.4.23.5.zip`** from the
   [BepInEx 5.4.23.5 release page](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5).
2. Extract it into the **Gloomhaven install folder** — the folder that contains `GH.exe`.
   (Steam: right-click the game → Manage → Browse local files.)
3. Start the game once normally, then quit. Check that `BepInEx/LogOutput.log` now exists.
   That file appearing is proof the loader is working.
4. Recommended: open `BepInEx/config/BepInEx.cfg` and set
   `[Chainloader] HideManagerGameObject = true`.

### 2. Install the mod

Extract **`GloomhavenVR-<version>.zip`** into the same Gloomhaven folder, merging the
`BepInEx/` directory. You should end up with exactly this:

```
Gloomhaven/
├── GH.exe
├── INSTALL.txt
└── BepInEx/
    ├── plugins/GloomhavenVR/
    │   ├── GloomhavenVR.dll
    │   ├── RuntimeDeps/            (Unity XR assemblies — loaded at runtime)
    │   └── gloomhavenvr.bundle     (hands, props, environments)
    └── patchers/GloomhavenVR/
        ├── GloomhavenVR.Preload.dll
        └── Natives/                (UnityOpenXR.dll + openxr_loader.dll)
```

If any of those folders is missing, the mod will tell you so in the log and fall back or
refuse to start VR. Re-extract the zip rather than moving files around by hand.

---

## First launch

1. Start your OpenXR runtime and put the headset on (or leave it awake on your head).
2. Launch the game the way you normally do.

**On the very first start after installing or updating, the game will close and reopen by
itself — once.** That is expected and it is not a crash.

Why: the mod switches on a Unity setting called *graphics jobs*, which moves rendering work
off the single thread that is the bottleneck in a scenario. On the test machine that took the
frame from 17.5 ms to 11.14 ms and the headset from a locked 45 Hz to a clean 90 Hz, and the
smearing during head movement disappeared. The setting lives in a file the engine reads
*before* any mod code exists, so it can only ever apply to the *next* start. Rather than
telling you to quit and start again, the mod does it for you.

It cannot loop: the restarted game is marked as such, and a counter stops it after two
attempts. It happens before any save or campaign is loaded.

Prefer to keep control of it? Both of these work:

- Set `[Core] AutoRestartForGraphicsJobs = false` in `BepInEx/config/dev.gloomhavenvr.cfg`
  and restart the game yourself when the log asks you to.
- Set `[Core] EnableGraphicsJobs = false` and add `-force-gfx-jobs native` to the game's launch
  options instead. That works from the very first start, and no game file is written at all.

**This is the only game file the mod ever writes**: two `key=value` lines added to
`GH_Data/boot.config`, every other line preserved, and the original copied once to
`boot.config.gloomhavenvr-backup`. If the game ever refuses to start, copy that backup back
over `boot.config` by hand — the mod cannot help you there, because at that point it never runs.

---

## Playing in VR

### Moving around

| Action | Control |
|---|---|
| Drag the table / world | Hold **one thumbstick clicked in** and move your hand |
| Rotate and zoom the world | Hold **both thumbstick clicks** — turn your hands around each other to rotate, spread or close them to zoom |
| Snap turn | Flick a thumbstick left or right |
| Fly through the room | Push the movement hand's thumbstick |
| Rise and sink | Push the *turn* stick up or down — off by default, switch it on under **Komfort** |
| Recenter yourself | Hold **B + Y** (the upper face button on *both* controllers) for one second |

### Your cards

Turn your palm up and your hand of ability cards **fans out in front of it**. Grab a card with
the **trigger**, look at it, and drop it into a slot on your control board to play it — the
slot order is your initiative, exactly like the physical game. When both cards are in, press
the **CONFIRM** keycap on the board.

On your turn, **poke the top or bottom half** of a played card in its slot to choose which half
you use (or point the laser at it and pull the trigger).

The control board floating beside you also carries the confirm and undo keycaps, the short and
long rest discs, the skip cap, the decision drawer, a recess for using items, and the discard,
burnt and item piles — which you can open and read as fans, either above the board or held in
your palm.

<!-- GIF: docs/img/gifs/card-fan.gif -->
> **[ GIF PLACEHOLDER — `docs/img/gifs/card-fan.gif` ]**
> *Palm rolls up, the card fan opens, a card is grabbed with the trigger and dropped into a
> board slot; then the top/bottom half is poked.*

### The board

| Action | Control |
|---|---|
| Pick a hex, an enemy, a door, a chest | Point the **laser** at it and pull the trigger — or hold the **grip** button and touch it with a fingertip |
| See an enemy's coming turn | Point the laser at its portrait on the initiative track, or simply **pick the figure up** |
| Pick up a miniature | Reach out and squeeze the **trigger** |
| Resize a held miniature | While one hand holds it, pull the **other hand's trigger** and move the hands apart or together |

The fingertip route deliberately requires the grip button to be held, so a hand that merely
sweeps across the board never selects anything.

<!-- GIF: docs/img/gifs/figure-grab.gif -->
> **[ GIF PLACEHOLDER — `docs/img/gifs/figure-grab.gif` ]**
> *A miniature is picked off the board, held up, scaled with the second hand, and glides back
> to its hex when released.*

### Windows

The game's own windows (character sheets, the merchant, dialogs, the story) become **panels in
the room**. Grab the bar at the top to move one, resize it, close it with its X, or reel it
closer and further with the stick while the laser holds it. New windows are placed in your
field of view, and where you put one is where it stays.

<!-- GIF: docs/img/gifs/windows.gif -->
> **[ GIF PLACEHOLDER — `docs/img/gifs/windows.gif` ]**
> *A window opens in front of the player, is grabbed by its bar, moved and resized, then reeled
> closer with the thumbstick.*

### The campaign map

Between scenarios, the campaign map is a **room with a table in it**, not a flat screen. The
guildmaster buttons are physical caps on the table rim: press one to open its window, press it
again to close it. Point at a location on the map to see its quest placard, and your party
token actually walks the route it travels.

If you would rather have the original flat map, set `[Rig] Vanilla2DMap = true`.

<!-- GIF: docs/img/gifs/map-room.gif -->
> **[ GIF PLACEHOLDER — `docs/img/gifs/map-room.gif` ]**
> *The 3D campaign map room: pressing a table-rim cap to open a window, pointing at a location,
> the party token walking its route.*

### The room you play in

Scenarios can be played in one of two hand-built environments — a **candle-lit cellar** or a
**moonlit night forest** — with a procedural night sky built from a real star catalogue,
firelight, drips, cobwebs, moonbeams through the trees and quiet spatial ambience. Or pick
**Standard** for the game's own look, or **Off** for plain black.

Both rooms also hide rare, quiet apparitions: a face at the barred window as the moonlight dims,
someone standing in the dark of the stair shaft, eyes that blink once in the undergrowth. They are
lit by the room and by nothing else, they are **never over the board and never two at once**, and
every player in a session sees the same one in the same place at the same moment. They have their
own on/off switch and a frequency dial under **Umgebung & Ton ▸ Grusel** — and switching them off
is a purely local decision, so if you turn them off you see none, whatever anybody else has set.

<!-- GIF: docs/img/gifs/environments.gif -->
> **[ GIF PLACEHOLDER — `docs/img/gifs/environments.gif` ]**
> *The cellar and the night forest: firelight, the night sky, foliage moving, the switch between
> environments in the settings.*

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
(one per subsystem). You do not need to touch them — the in-VR menu is the intended route —
but they are there, and each setting carries its own explanation in the file.

**To play the game completely unmodified**, set `[General] Enabled = false` in
`BepInEx/config/dev.gloomhavenvr.cfg`.

---

## Multiplayer

Multiplayer works, and it is designed so that it cannot break anyone's game:

- **Everyone in a VR session must run the same mod build.** If two players are on different
  builds, the mod shows a blocking version-mismatch dialog instead of letting a session go
  wrong quietly. Update together.
- **Players who do not have the mod at all can still play with you normally.** The mod's data
  rides an event the game already has, and an unmodded client simply ignores it. Nothing the
  mod sends changes game state.
- **What other VR players see of you:** your head and both hands with real finger poses, your
  chosen hand style and head mask, a figure or a card while you hold it (cards always as backs
  — no card you hold is ever revealed), your card and item fans as counts, and a live mirror of
  your control board.
- **What you experience together:** shared map windows (marked by a blue grab bar instead of a
  brass one), a fully shared story window with the same page for everyone, and a shared clock
  for the environment so the same ambient sounds and the same apparitions happen at the same
  moment in the same place on every client.

---

## Known limitations

Honest list. None of these is a bug report worth filing — they are known.

- **Fully-revealed, large scenarios seen from above can hitch.** The wall-fade update is done in
  one piece on purpose; in the worst case that costs roughly 80–100 ms every couple of seconds.
- **Windows in the map room can overlap.** With five windows open, the placement arc is almost
  full. Placed windows are never moved for you, so a nearer window can end up in front of a
  further one — and it will take the clicks meant for the one behind it. Move or close one.
- **A couple of texts still live only on the flat screen** — notably the "waiting for other
  players" hint and the multiplayer lock overlay. In VR you see the effect (a button changes)
  but not the sentence.
- **Peer cards in the map room show as backs**, not fronts.
- **VR field of view makes distant models drop to a coarser version slightly earlier** than the
  game intends. Real, small, and deliberately left alone.
- **Per-pixel lights are expensive** and ship off by default; at zero, some lights can flicker.
- **Forcing full texture resolution costs video memory** (never frame time). If you are tight on
  VRAM, that is the first setting to turn down.
- **Mixed-reality / see-through mode turns the environments and the sky off** — they cannot be
  drawn over passthrough and still look right.
- **In multiplayer, arrival at a new location can differ by a couple of seconds** between
  players, because the travel animation runs on each machine.
- The mod is **pre-1.0**. It is developed round by round against real headset sessions and
  things move.

---

## Troubleshooting

**Headset black, or the game just runs flat on the monitor.**
Add `-force-d3d11` to the game's launch options. Desktop OpenXR needs D3D11.

**"VR unavailable this session (RuntimeDeps missing)"** in the log — the `RuntimeDeps/` folder
next to `GloomhavenVR.dll` is missing. Re-extract the zip.

**"OpenXR runtime asset install incomplete"** — the `Natives/` folder next to the preloader is
missing. Re-extract the zip.

**The wrong headset runtime is picked, or none is.**
Set `[General] RuntimeOverride` in `BepInEx/config/dev.gloomhavenvr.cfg` to your runtime's JSON
file, for example
`C:\Program Files (x86)\Steam\steamapps\common\SteamVR\steamxr_win64.json`.
Left empty, the mod detects the active runtime and fails over through the installed ones.

**The game will not start at all after installing.**
Copy `GH_Data/boot.config.gloomhavenvr-backup` over `GH_Data/boot.config`. That is the file
exactly as it was before the mod ever ran.

**Something looks wrong and you want to report it.**

The log is here:

```
<Gloomhaven folder>/BepInEx/LogOutput.log
```

Send **that whole file**, plus:

- what you were doing when it happened (which scenario, which screen, solo or multiplayer),
- your headset and which runtime you streamed with (VDXR, Quest Link, SteamVR),
- whether the other players in the session had the mod.

The log is deliberately verbose right now — several hundred lines a session is normal, and the
first lines name the exact build you are running, which is the single most useful thing in a
report. If you want it quieter, `[General] LogLevel` accepts `Warnings` or `Normal`.

---

## Uninstall

Delete these two folders:

```
BepInEx/plugins/GloomhavenVR/
BepInEx/patchers/GloomhavenVR/
```

Optionally also remove what the mod placed for OpenXR — it does nothing without the mod:

```
GH_Data/Plugins/x86_64/UnityOpenXR.dll
GH_Data/Plugins/x86_64/openxr_loader.dll
GH_Data/UnitySubsystems/UnityOpenXR/
```

And restore `GH_Data/boot.config` from `boot.config.gloomhavenvr-backup` if you want the
graphics-jobs change gone. Deleting all of `BepInEx/` removes the loader as well.

If you only want the game vanilla for a while, do not uninstall anything — set
`[General] Enabled = false` and the mod does nothing at all.

---

## Credits and licence

Built on the work of others:

- **[LCVR](https://github.com/DaXcess/LCVR)** and **[RepoXR](https://github.com/DaXcess/RepoXR)**
  (GPL-3.0) — the OpenXR bootstrap pattern, runtime failover and finger curling this mod adapts.
- **[UUVR](https://github.com/Raicuparta/uuvr)** (GPL-3.0) — the flat-screen-in-VR pattern used
  where a world panel is not the right answer.
- **[SteamVR Unity Plugin](https://github.com/ValveSoftware/steamvr_unity_plugin)** by Valve
  (BSD-3-Clause) — the base hand models.
- **Demeo** (Resolution Games) — the interaction model this mod chases. No assets or code from
  it are used.
- The hand and glove models and the other bundled 3D assets were made for this project by its
  artist.

**Licence: GPL-3.0** — see [LICENSE](LICENSE). The mod ships **only its own code and its own
licensed assets**; never game files, game assets or decompiled sources.

Not affiliated with Flaming Fowl Studios, Twin Sails Interactive, Asmodee or Cephalofair Games.
Gloomhaven and its artwork belong to their owners.

---

### Working on the mod?

Building from source, the asset pipeline, the patch inventory and the hardware test scripts are
in **[docs/DEVELOPING.md](docs/DEVELOPING.md)**. Nothing in this README is needed to build it.
