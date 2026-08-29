# GloomhavenVR — playing guide

Every control, what each part of the table does, how a session with other people works, and the
honest list of what is still rough. If you have not installed it yet, that is the
[install guide](../INSTALL.md).

- [Moving around](#moving-around)
- [Your cards](#your-cards)
- [The board](#the-board)
- [Windows](#windows)
- [The campaign map](#the-campaign-map)
- [The room you play in](#the-room-you-play-in)
- [Multiplayer](#multiplayer)
- [Known limitations](#known-limitations)

Everything below is adjustable. The settings live in the game's own Options window under the
**VR Options** tab — see [settings in the install guide](../INSTALL.md#settings).

---

## The tutorial teaches all of this

You do not have to read the tables below. Start the tutorial and, a couple of seconds in, **your
hands become the controller you are actually holding** — a Quest 3, a Pico 4 or a Valve Index as
its own model, anything else as a generic controller with the same keys in the same places. The
key you need lights up on it, and each step ends when you *do* the thing, not when you have read
about it.

Thirteen controls, in the order they become useful: point and click, reach and grab, drag the
table, zoom, rotate, take a card, hold one properly in your hand, fly, turn round, pick with a
fingertip, reel a window in, ping a hex, re-seat yourself.

**NEXT** passes any single step — useful when the room is not offering what a step asks for, such
as an open window to reel in — and **SKIP** ends the lesson. Switch it off for good under
**Komfort ▸ Hände & Zielen** (`[Compat] ControlsLesson`).

*Valve's Steam Frame is shown the generic controller: no openly-licensed model of its controllers
exists, and the licence on the ones we do ship forbids inventing one.*

---

## Moving around

| Action | Control |
|---|---|
| Drag the table / world | Hold **one thumbstick clicked in** and move your hand |
| Rotate and zoom the world | Hold **both thumbstick clicks** — turn your hands around each other to rotate, spread or close them to zoom |
| Snap turn | Flick a thumbstick left or right |
| Fly through the room | Push the movement hand's thumbstick |
| Rise and sink | Push the *turn* stick up or down — off by default, switch it on under **Komfort** |
| Recentre yourself | Hold **B + Y** (the upper face button on *both* controllers) for one second |

---

## Your cards

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

---

## The board

| Action | Control |
|---|---|
| Pick a hex, an enemy, a door, a chest | Point the **laser** at it and pull the trigger — or hold the **grip** button and touch it with a fingertip |
| See an enemy's coming turn | Point the laser at its portrait on the initiative track, or simply **pick the figure up** |
| Pick up a miniature | Reach out and squeeze the **trigger** |
| Resize a held miniature | While one hand holds it, pull the **other hand's trigger** and move the hands apart or together |

The fingertip route deliberately requires the grip button to be held, so a hand that merely
sweeps across the board never selects anything.

---

## Windows

The game's own windows (character sheets, the merchant, dialogs, the story) become **panels in
the room**. Grab the bar at the top to move one, resize it, close it with its X, or reel it
closer and further with the stick while the laser holds it. New windows are placed in your
field of view, and where you put one is where it stays.

---

## The campaign map

Between scenarios, the campaign map is a **room with a table in it**, not a flat screen. The
guildmaster buttons are physical caps on the table rim: press one to open its window, press it
again to close it. Point at a location on the map to see its quest placard, and your party
token actually walks the route it travels.

If you would rather have the original flat map, set `[Rig] Vanilla2DMap = true`.

---

## The room you play in

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

---

## Multiplayer

Multiplayer works, and it is designed so that it cannot break anyone's game:

- **Everyone in a VR session must run the same version.** If two players are on different
  versions, the mod shows a blocking version-mismatch dialog instead of letting a session go
  wrong quietly. Update together — see [updating](../INSTALL.md#updating).
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
  video memory, that is the first setting to turn down.
- **Mixed-reality / see-through mode turns the environments and the sky off** — they cannot be
  drawn over passthrough and still look right.
- **In multiplayer, arrival at a new location can differ by a couple of seconds** between
  players, because the travel animation runs on each machine.
- The mod is **pre-1.0**. It is developed round by round against real headset sessions and
  things move.
