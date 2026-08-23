<p align="center">
  <img src="src/GloomhavenVR/Assets/GloomhavenVR_logo.png" alt="GloomhavenVR" width="620">
</p>

<p align="center">
  <b>Play Gloomhaven (digital) as a room-scale VR board game.</b><br>
  You stand at the table. You pick your cards up with your hands.
</p>

---

GloomhavenVR is an add-on for the PC version of **Gloomhaven (digital)**. It does not just show
the flat game on a big screen in your headset — it rebuilds the game as a tabletop you stand at:
the scenario is a lit diorama in front of you, your ability cards fan out of your palm, you reach
out and move the miniatures, and the campaign map is a room with a real table in it.

It installs alongside your normal game, changes nothing you cannot undo, and can be switched off
again with a single line in a text file to get the original game back.

<!-- GIF: docs/img/gifs/overview.gif -->
> **[ GIF PLACEHOLDER — `docs/img/gifs/overview.gif` ]**
> *The hero shot: standing at the table in a lit scenario, then dragging, rotating and zooming
> the board with the two-handed world grab.*

---

## Table of contents

- [What you need](#what-you-need)
- [Install](#install)
- [Starting the game the first time](#starting-the-game-the-first-time)
- [Updating](#updating)
- [Playing in VR](#playing-in-vr)
- [Settings](#settings)
- [Playing with other people](#playing-with-other-people)
- [Known rough edges](#known-rough-edges)
- [If something goes wrong](#if-something-goes-wrong)
- [Uninstall](#uninstall)
- [Credits and licence](#credits-and-licence)

---

## What you need

| | |
|---|---|
| **The game** | Gloomhaven (digital) for PC, version 1.1.x — Steam or GOG. |
| **A PC** | Windows. This is a PC-VR mod: the game runs on your computer and your headset shows it. It does not run on a headset on its own. |
| **A headset** | Developed and tested on a **Quest 3 over Virtual Desktop** at 90 Hz. Other PC-VR headsets and streaming apps *should* work — Quest Link, Steam Link, SteamVR — but nobody has tested them yet, so treat those as untried. |
| **Controllers** | Two tracked controllers with thumbsticks, like the Quest ones (two face buttons per hand, a trigger and a grip button). |
| **Room** | Standing is enough. A small space is fine — you can fly, pull the table towards you, and recentre yourself at any time. |
| **One free download** | **BepInEx** — a small, free program that lets mods run at all. You install it once, and the next section walks you through it. |

### Tell your headset software to hand PC games over

Every headset has one setting that decides which app gets to show PC VR games. It is usually
called the **OpenXR runtime**. Set it before you start Gloomhaven:

- **Virtual Desktop** — in the Virtual Desktop streamer settings on your PC, choose **VDXR**.
- **Quest Link / Air Link** — Meta Quest Link app → Settings → General → OpenXR Runtime →
  *"Set Meta Quest Link as active"*.
- **Steam Link / SteamVR** — SteamVR Settings → OpenXR → *"Set SteamVR as OpenXR runtime"*.

If your PC keeps picking the wrong one, you can force a choice for this game only — see
[If something goes wrong](#if-something-goes-wrong).

---

## Install

### 1. Install BepInEx, once

BepInEx is the small free program that lets mods run. It is not made by us and you only ever do
this once.

1. Download **`BepInEx_win_x64_5.4.23.5.zip`** from the
   [BepInEx 5.4.23.5 download page](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5).
   Take that exact version.
2. Unpack it into your **Gloomhaven folder** — the folder that has `GH.exe` in it.
   (On Steam: right-click the game → Manage → Browse local files.)
3. Start the game once the normal way, then quit again. A new folder called `BepInEx` should now
   have a file called `LogOutput.log` inside it. That file appearing is your proof it worked.

### 2. Install GloomhavenVR

Unpack **`GloomhavenVR-<version>.zip`** into that same Gloomhaven folder and let Windows merge the
`BepInEx` folder when it asks.

To check you unpacked it in the right place, look for these two folders:

```
Gloomhaven/                      ← the folder with GH.exe in it
├── GH.exe
├── INSTALL.txt                  ← this came out of the mod's zip
└── BepInEx/
    ├── plugins/GloomhavenVR/    ← both of these must exist
    └── patchers/GloomhavenVR/
```

If either `GloomhavenVR` folder is missing, you unpacked into the wrong place or the merge did not
happen. Unpack the zip again rather than moving files around by hand.

Everything the mod needs is inside that one zip. There is nothing else to download.

---

## Starting the game the first time

1. Put your headset on (or leave it awake), with the setting from
   [above](#tell-your-headset-software-to-hand-pc-games-over) already made.
2. Start Gloomhaven the way you normally do.

**The first time you start the game after installing or updating, it will close and reopen by
itself — once. That is meant to happen. It is not a crash.**

Why: the mod switches on a speed setting that the game only reads while it is starting up, so it
can never take effect on the run that switches it on. Rather than telling you to quit and start
again, the mod does it for you. On the test machine that setting was the difference between a
stuttery 45 frames per second and a smooth 90, and the smearing when you turn your head went away.
It happens once, before any save game is touched, and it cannot get stuck in a loop.

If you would rather do it yourself, open `BepInEx/config/dev.gloomhavenvr.cfg` and, under the
`[Core]` heading, set `AutoRestartForGraphicsJobs = false`. The game will then ask you to restart
it instead.

**This is the only game file the mod ever changes**: two lines added to `GH_Data/boot.config`, with
everything else in it left alone and the original saved next to it as
`boot.config.gloomhavenvr-backup`. If the game ever refuses to start, copy that backup back over
`boot.config` yourself — see [If something goes wrong](#if-something-goes-wrong).

---

## Updating

When you are in the main menu, the mod quietly asks GitHub whether a newer version exists. If there
is one, a window appears offering **Update** or **Ignore**. Update downloads the new version,
replaces the mod's files and restarts the game for you.

Nothing happens without you pressing the button, and if you are offline or GitHub is unreachable,
the window simply never appears.

---

## Playing in VR

### Moving around

| What you want | How |
|---|---|
| Drag the table towards you | Hold **one thumbstick pressed in** and move your hand |
| Rotate and zoom the table | Hold **both thumbsticks pressed in** — turn your hands around each other to rotate, spread or close them to zoom |
| Turn on the spot | Flick a thumbstick left or right |
| Fly through the room | Push the movement hand's thumbstick |
| Rise and sink | Push the *turning* stick up or down — off by default, switch it on under **Comfort** |
| Recentre yourself | Hold the **upper face button on both controllers** (**B** and **Y** on Quest controllers) for one second |

### Your cards

Turn your palm up and your hand of ability cards **fans out in front of it**. Grab a card with the
**trigger**, look at it, and drop it into a slot on your control board to play it — the slot order
is your initiative, exactly like the physical game. When both cards are in, press the **CONFIRM**
key on the board.

On your turn, **poke the top or bottom half** of a played card to choose which half you use (or
point the laser at it and pull the trigger).

The control board floating beside you also carries confirm and undo, the short and long rest discs,
skip, the decision drawer, a slot for using items, and your discard, burnt and item piles — which
you can open and read as fans, either above the board or held in your palm.

<!-- GIF: docs/img/gifs/card-fan.gif -->
> **[ GIF PLACEHOLDER — `docs/img/gifs/card-fan.gif` ]**
> *Palm rolls up, the card fan opens, a card is grabbed with the trigger and dropped into a
> board slot; then the top/bottom half is poked.*

### The board

| What you want | How |
|---|---|
| Pick a hex, an enemy, a door, a chest | Point the **laser** at it and pull the trigger — or hold the **grip** button and touch it with a fingertip |
| See what an enemy is about to do | Point the laser at its portrait on the initiative track, or simply **pick the figure up** |
| Pick up a miniature | Reach out and squeeze the **trigger** |
| Make a held miniature bigger or smaller | While one hand holds it, pull the **other hand's trigger** and move your hands apart or together |

Touching with a fingertip deliberately needs the grip button held down, so a hand that just sweeps
across the board never selects anything by accident.

<!-- GIF: docs/img/gifs/figure-grab.gif -->
> **[ GIF PLACEHOLDER — `docs/img/gifs/figure-grab.gif` ]**
> *A miniature is picked off the board, held up, scaled with the second hand, and glides back
> to its hex when released.*

### Windows

The game's own windows — character sheets, the merchant, dialogs, the story — become **panels
hanging in the room**. Grab the bar at the top to move one, resize it, close it with its X, or push
the thumbstick to reel it closer and further while the laser is holding it. New windows appear in
front of you, and where you put one is where it stays.

<!-- GIF: docs/img/gifs/windows.gif -->
> **[ GIF PLACEHOLDER — `docs/img/gifs/windows.gif` ]**
> *A window opens in front of the player, is grabbed by its bar, moved and resized, then reeled
> closer with the thumbstick.*

### The campaign map

Between scenarios, the campaign map is a **room with a table in it**, not a flat screen. The
guildmaster buttons are real caps on the table rim: press one to open its window, press it again to
close it. Point at a place on the map to read its quest card, and your party token actually walks
the route it travels.

If you would rather have the old flat map, there is a setting for that.

<!-- GIF: docs/img/gifs/map-room.gif -->
> **[ GIF PLACEHOLDER — `docs/img/gifs/map-room.gif` ]**
> *The 3D campaign map room: pressing a table-rim cap to open a window, pointing at a location,
> the party token walking its route.*

### The room you play in

Scenarios can be played in one of two hand-built rooms — a **candle-lit cellar** or a **moonlit
night forest** — with a night sky built from a real star catalogue, firelight, drips, cobwebs,
moonbeams through the trees and quiet ambient sound. Or pick **Standard** for the game's own look,
or **Off** for plain black.

Both rooms also hide rare, quiet apparitions: a face at the barred window as the moonlight dims,
someone standing in the dark of the stair shaft, eyes that blink once in the undergrowth. They are
lit by the room and by nothing else, they are **never over the board and never two at once**, and
everyone playing together sees the same one in the same place at the same moment. They have their
own on/off switch and a "how often" dial in the settings — and turning them off is your own private
decision: if you switch them off you see none, whatever anybody else has chosen.

<!-- GIF: docs/img/gifs/environments.gif -->
> **[ GIF PLACEHOLDER — `docs/img/gifs/environments.gif` ]**
> *The cellar and the night forest: firelight, the night sky, foliage moving, the switch between
> environments in the settings.*

---

## Settings

Everything is adjustable from inside the headset. Open the game's own **Options** window — from the
main menu, or from the pause menu during a scenario — and pick the **VR Options** tab. It sits next
to the game's own tabs and looks like them.

| Tab | What is in it |
|---|---|
| **Comfort** | Turning, moving, grabbing the world (including how far you can zoom), visibility, hands and aiming |
| **Picture** | How things are presented, windows and panels, see-through mode, what shows on your monitor |
| **Board & cards** | The control board, figures, cards, piles and hints |
| **Panels** | Panels and readouts, health bars, the flat screen, pointing and clicking, typing |
| **Avatar & multiplayer** | How you look to others — three hand styles and three head masks — and playing together |
| **World & sound** | The room you play in, the apparitions, sound, visibility, the campaign map |
| **Advanced** | A deeper version of everything above, plus a list of every single setting there is |

Changes take effect straight away and are saved for you.

**Language:** the mod speaks **English and German** and follows whatever language the game is set
to. Any other game language falls back to English.

If you prefer text files, every setting also lives in `BepInEx/config/`, in files starting with
`dev.gloomhavenvr`. You never need to open them — the in-headset menu is the intended route — but
they are there, and each setting explains itself in the file.

**To play the game completely unmodified for a while**, open
`BepInEx/config/dev.gloomhavenvr.cfg` and, under the `[General]` heading, set `Enabled = false`.
You do not have to uninstall anything.

---

## Playing with other people

Multiplayer works, and it is built so that it cannot spoil anybody's game:

- **Everyone playing together needs the same version of the mod.** If two people are on different
  versions, the game says so with a clear message rather than letting a session go quietly wrong.
  Update together — see [Updating](#updating).
- **People without the mod can still play with you, normally.** They just see the ordinary flat
  game. Nothing the mod sends can change anything in their game.
- **What other VR players see of you:** your head and both hands with real finger movement, your
  chosen hand style and head mask, a figure or a card while you are holding it (cards always face
  down — a card in your hand is never revealed), how many cards and items you are holding, and a
  live copy of your control board.
- **What you share:** map windows that everyone opens together (they have a blue grab bar instead
  of a brass one), the story window, with everyone on the same page, and a shared clock for the
  room, so the same ambient sounds and the same apparitions happen at the same moment for everyone.

---

## Known rough edges

An honest list. None of these needs reporting — they are known.

- **Big, fully explored scenarios can hitch when you look at the whole map.** A short stutter every
  couple of seconds.
- **Windows in the campaign map room can overlap.** With five open there is barely room left, and
  since a window you placed is never moved for you, a nearer one can end up in front of a further
  one — and it will swallow the clicks meant for the one behind. Move or close one.
- **A couple of messages still only appear on your monitor**, notably "waiting for other players"
  and the message that another player is currently acting. In the headset you see the effect (a
  button goes dead) but not the sentence.
- **Other players' cards in the map room show their backs**, not their fronts.
- **Distant models get a little blockier slightly sooner than they should.** Small, and left alone
  on purpose.
- **Some lights can flicker** if you turn the extra-lighting setting all the way down (which is its
  default, because it costs frame rate).
- **Turning textures up to full costs graphics memory** — not frame rate. If your graphics card is
  short on memory, that is the first setting to turn back down.
- **See-through (mixed reality) mode hides the room and the sky.** They cannot be drawn over your
  real living room and still look right.
- **When travelling in multiplayer, people can arrive a couple of seconds apart**, because the
  travel animation runs on each computer separately.
- The mod is **not finished**. It is developed round by round against real headset sessions, and
  things move.

---

## If something goes wrong

**The game starts on your monitor instead of in your headset, or the headset stays black.**
First check that your headset software is the one set to handle PC VR games — see
[What you need](#what-you-need). If that is right and it still happens, add this to the game's
launch options in Steam or GOG:

```
-force-d3d11
```

**Your PC keeps handing the game to the wrong headset software, or to none.**
Open `BepInEx/config/dev.gloomhavenvr.cfg` and, under the `[General]` heading, point
`RuntimeOverride` at your headset software's `.json` file, for example:

```
RuntimeOverride = C:\Program Files (x86)\Steam\steamapps\common\SteamVR\steamxr_win64.json
```

Left empty, the mod works it out by itself and tries the ones you have installed in turn.

**The game says VR could not start because something is missing.**
Part of the zip did not make it into the Gloomhaven folder. Unpack
`GloomhavenVR-<version>.zip` into it again and let Windows merge the folders.

**The game will not start at all after installing.**
In your Gloomhaven folder, copy `GH_Data/boot.config.gloomhavenvr-backup` over
`GH_Data/boot.config`. That is that file exactly as it was before the mod ever ran. (The mod cannot
fix this for you, because at that point it never gets to run.)

### Reporting a problem

There is one file to send. In your Gloomhaven folder:

```
BepInEx/LogOutput.log
```

Send **that whole file**, plus:

- what you were doing when it happened — which scenario, which screen, alone or with others;
- your headset, and how you connect it to the PC (Virtual Desktop, Quest Link, Steam Link…);
- whether the other people in the session had the mod.

That file is chatty on purpose — several hundred lines in a session is normal — and its first lines
say exactly which version you are running, which is the single most useful thing in a report.

---

## Uninstall

Delete these two folders from your Gloomhaven folder:

```
BepInEx/plugins/GloomhavenVR/
BepInEx/patchers/GloomhavenVR/
```

The mod also puts three things in the game's own folders to talk to your headset. They do nothing
once the mod is gone, but you can delete them too:

```
GH_Data/Plugins/x86_64/UnityOpenXR.dll
GH_Data/Plugins/x86_64/openxr_loader.dll
GH_Data/UnitySubsystems/UnityOpenXR/
```

And to undo the speed setting from the first launch, copy
`GH_Data/boot.config.gloomhavenvr-backup` over `GH_Data/boot.config`. Deleting the whole `BepInEx`
folder removes BepInEx as well, along with any other mods you had.

If you only want the plain game for a while, do not uninstall anything — set `Enabled = false`
under `[General]` in `BepInEx/config/dev.gloomhavenvr.cfg` and the mod does nothing at all.

---

## Credits and licence

Built on the work of others:

- **[LCVR](https://github.com/DaXcess/LCVR)** and **[RepoXR](https://github.com/DaXcess/RepoXR)**
  (GPL-3.0) — the headset start-up approach and the finger curling this mod adapts.
- **[UUVR](https://github.com/Raicuparta/uuvr)** (GPL-3.0) — how to show a flat screen in VR, used
  where a floating panel is not the right answer.
- **[SteamVR Unity Plugin](https://github.com/ValveSoftware/steamvr_unity_plugin)** by Valve
  (BSD-3-Clause) — the base hand models.
- **Demeo** (Resolution Games) — the feel this mod is chasing. Nothing from it is used.
- The hands, gloves and the other 3D models were made for this project by its artist.

**Licence: GPL-3.0** — see [LICENSE](LICENSE). The mod ships **only its own code and its own
licensed artwork** — never game files or game artwork.

Not affiliated with Flaming Fowl Studios, Twin Sails Interactive, Asmodee or Cephalofair Games.
Gloomhaven and its artwork belong to their owners.

---

### Working on the mod?

Building it from source, the asset pipeline and the test scripts are in
**[docs/DEVELOPING.md](docs/DEVELOPING.md)**. Nothing in this README is needed to build it.
