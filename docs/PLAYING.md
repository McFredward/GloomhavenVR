# GloomhavenVR — playing guide

<p align="center">
  <img src="img/flag-en.png" width="24" alt="English">&nbsp;<b>English</b>
  &nbsp;&nbsp;|&nbsp;&nbsp;
  <a href="PLAYING.de.md"><img src="img/flag-de.png" width="24" alt="Deutsch">&nbsp;Deutsch</a>
</p>

Not installed yet? → [install guide](../INSTALL.md)

---

## The controls

<p align="center">
  <img src="img/controls-en.png" width="860" alt="A Quest 3 controller pair with every binding labelled: thumbstick, trigger, grip, X, A and the Y+B chord">
</p>

**You do not have to learn this.** The tutorial turns your hands into the controller you are actually
holding and lights up the key for each of the fourteen controls, in the order they become useful.
Switch it off under **Comfort ▸ Hands & aiming**. *(A Steam Frame is shown a neutral controller —
Valve does not distribute a model of theirs.)*

Three more that the picture cannot show:

- **Resize a held figure** — hold it in one hand, pull the **other hand's trigger**, move your hands apart or together.
- **45° snap turning** instead of a smooth one — **Comfort ▸ Turning**.
- **Rise and sink** on the right stick — off by default, switch it on under **Comfort**.

---

## Your cards and your board

<p align="center">
  <a href="img/card-fan.mp4"><img src="img/card-fan-poster.jpg" width="420" alt="The card fan"></a>
  <a href="img/control-board.mp4"><img src="img/control-board-poster.jpg" width="420" alt="The control board"></a>
</p>

Turn your palm up and your hand **fans out in front of it**. Take a card with the trigger and drop
it into a slot on your control board — slot order is your initiative, exactly like the physical
game. Both cards in, press **CONFIRM**. On your turn, **poke the top or bottom half** of a played
card to choose which half you use.

The board beside you also carries undo, the short and long rest discs, skip, the decision drawer, a
recess for using items, and the discard, burnt and item piles — each opens as a fan you can read
above the board or in your palm.

<p align="center">
  <img src="img/board-en.png" width="860" alt="The control board with every part labelled: the two card recesses, the three keys on the right, the two rest keys on the left, the grab rod, the initiative track, the objectives and elements, the discard, burnt and item stacks, and your hand of cards">
</p>

---

## The board in front of you

<p align="center">
  <a href="img/figure-grab.mp4"><img src="img/figure-grab-poster.jpg" width="420" alt="Lifting a miniature"></a>
  <a href="img/physical-interaction.mp4"><img src="img/physical-interaction-poster.jpg" width="420" alt="Reaching into the scenario"></a>
</p>

Point the laser and pull the trigger to pick a hex, an enemy, a door or a chest — or hold the
**grip** and touch it with a fingertip. The grip is required on purpose, so a hand that merely
sweeps across the board never selects anything.

**Pick a figure up** with the trigger to see its coming turn.

---

## Windows

The game's windows — character sheets, the merchant, dialogs, the story — become **panels in the
room**. Grab the bar at the top to move one, resize it, or close it with its X, and reel it closer
and further with the stick while the laser holds it. Where you put one is where it stays.

---

## The campaign map

<p align="center">
  <a href="img/map-interaction.mp4"><img src="img/map-interaction-poster.jpg" width="560" alt="The campaign map room"></a>
</p>

Between scenarios the map is a **room with a table in it**. The guildmaster buttons are physical
caps on the table rim, point at a location to see its quest placard, and your party token walks the
route it travels. Prefer the original flat map? `[Rig] Vanilla2DMap = true`.

---

## The room you play in

<p align="center">
  <img src="img/env-cellar.jpg" width="420" alt="The cellar">
  <img src="img/env-forest.jpg" width="420" alt="The night forest">
</p>

A candle-lit **cellar**, a **moonlit forest** under a real star catalogue, the game's own **Default**
look, or **Off (black)**. Both built rooms have firelight, drips, moonbeams and quiet spatial
ambience.

They also hide rare apparitions — a face at the barred window, someone in the dark of the stair
shaft, eyes that blink in the undergrowth. Never over the board, never two at once, and everyone in
a session sees the same one at the same moment. Dial or switch them off under **World ▸ Creepy**;
that choice is yours alone.

---

## Multiplayer

- **Everyone in a VR session must run the same version.** A mismatch is blocked with a dialog rather
  than allowed to go quietly wrong — [update together](../INSTALL.md#updating).
- **Players without the mod play with you normally.** Nothing the mod sends changes game state.
- **What others see of you:** your head and both hands with real finger poses, your mask and hand
  style, whatever you are holding (cards always as backs), your fan counts, and a live mirror of
  your control board.
- **What you share:** map windows (marked by a small badge in the corner), the story window on the
  same page for everyone, and one clock — so the same ambient sounds and the same apparitions
  happen at the same moment in the same place for everybody.

---

## Known limitations

Known, and not worth reporting.

- **Big, fully-revealed scenarios seen from above can hitch** — roughly 80–100 ms every couple of seconds, worst case.
- **Windows in the map room can overlap.** With five open the arc is full; a nearer window takes the clicks meant for the one behind it. Move or close one.
- **A couple of texts live only on the flat screen** — the "waiting for other players" hint and the multiplayer lock overlay. In VR you see the effect but not the sentence.
- **Distant models drop to a coarser version slightly earlier** than the game intends. Real, small, left alone.
- **Per-pixel lights ship off** and can flicker at zero; **forced full texture resolution costs video memory** — the first thing to turn down if you are tight.
- **Mixed reality turns the environments and the sky off.** They cannot be drawn over passthrough and still look right.
- **In multiplayer, arrival at a new location can differ by a second or two** — the travel animation runs on each machine.
- The mod is **pre-1.0** and moves round by round against real headset sessions.
