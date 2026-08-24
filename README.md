<p align="center">
  <img src="src/GloomhavenVR/Assets/GloomhavenVR_logo.png" alt="GloomhavenVR" width="620">
</p>

<p align="center">
  <b>Play Gloomhaven (digital) as a room-scale VR board game.</b><br>
  You stand at the table. You pick your cards up with your hands.
</p>

---

GloomhavenVR is a mod for the PC version of **Gloomhaven (digital)**. It does not hang the flat
game on a big screen inside a headset. It rebuilds it as a tabletop you stand at.

The scenario is a lit diorama on the table in front of you. You lean over it to see round a
corner, you walk to the other end of it, you take the whole board in both hands and pull it
closer. Turn your palm up and your hand of ability cards fans out of it — pick one out with your
fingers, hold it up to your face to read it, drop it into a slot on the board beside you. Reach
into the scenario and the miniatures come up off their hexes. Between scenarios the campaign map
is not a screen either; it is a room with a table in it, and your party token walks the route it
travels.

Everything else is exactly the game you already have. Same rules, same saves, same campaign, and
the same people to play it with — including the ones who are not in a headset.

<!-- VIDEO: docs/img/overview.mp4 -->
> **[ VIDEO PLACEHOLDER — `docs/img/overview.mp4` ]**
> *The hero shot: standing at the table in a lit scenario, then dragging, rotating and zooming
> the board with the two-handed world grab.*

---

## What that looks like

**Your hand of cards lives in your palm.** Roll your wrist up, the fan opens, and you drop the
two cards you want into the slots on your control board — slot order is your initiative, exactly
like the physical game.

<div align="center">
  <video src="https://github.com/McFredward/GloomhavenVR/raw/main/docs/img/card-fan.mp4" width="800" controls muted loop playsinline>
    <a href="./docs/img/card-fan.mp4">card-fan.mp4</a>
  </video>
</div>

> *Palm rolls up, the card fan opens, a card is grabbed and dropped into a board slot; then the
> top or bottom half is poked to choose it.*

**You pick the miniatures up.** Squeeze one off the board, hold it up to see what it is, resize it
in mid-air with your other hand, and let go — it glides back down to its hex.

<div align="center">
  <video src="https://github.com/McFredward/GloomhavenVR/raw/main/docs/img/figure-grab.mp4" width="800" controls muted loop playsinline>
    <a href="./docs/img/figure-grab.mp4">figure-grab.mp4</a>
  </video>
</div>

> *A miniature is picked off the board, held up, scaled with the second hand, and glides back
> to its hex when released.*

**The game's windows become panels in the room.** Character sheets, the merchant, the story — grab
one by its bar, move it, resize it, park it where you want it. Where you put one is where it stays.

<!-- VIDEO: docs/img/windows.mp4 -->
> **[ VIDEO PLACEHOLDER — `docs/img/windows.mp4` ]**
> *A window opens in front of the player, is grabbed by its bar, moved and resized, then reeled
> closer with the thumbstick.*

**The campaign map is a room, not a menu.** The guildmaster buttons are physical caps on the table
rim. Press one and its window opens; press it again and it closes.

<!-- VIDEO: docs/img/map-room.mp4 -->
> **[ VIDEO PLACEHOLDER — `docs/img/map-room.mp4` ]**
> *The 3D campaign map room: pressing a table-rim cap to open a window, pointing at a location,
> the party token walking its route.*

**And you choose the room you play in** — a candle-lit cellar or a moonlit night forest, both built
by hand for this, with a real star catalogue overhead, firelight, drips, cobwebs and quiet ambience.
Both of them hide rare apparitions you will hopefully not be looking at when they happen. They have
their own off switch.

<!-- VIDEO: docs/img/environments.mp4 -->
> **[ VIDEO PLACEHOLDER — `docs/img/environments.mp4` ]**
> *The cellar and the night forest: firelight, the night sky, foliage moving, the switch between
> environments in the settings.*

---

## What you need

- **Gloomhaven (digital) for PC**, v1.1.x — Steam or GOG.
- **A Windows PC** that already runs it, and a **PC-VR headset with two tracked controllers**.
  The game runs on the PC and you stream or tether the headset to it, as you would for any PC-VR
  title. It does not run on a standalone headset by itself.
- **Room-scale or standing.** A small space is fine — you can fly, pull the world over to you, and
  recentre yourself at any time.

Built and played on a **Quest 3 over Virtual Desktop**. Quest Link, Steam Link and SteamVR are the
other obvious ways in and *have not been tried yet* — if you get there first, say so.

Setup is two zip files extracted into the game folder, once.
**→ [Install guide](INSTALL.md)** — it also covers settings, updates, problems and uninstalling.

It installs alongside the normal game, writes nothing you cannot undo, and can be switched off
again with a single line in a text file to get the original game back.

---

## Playing with other people

Multiplayer works, and it is built so it cannot spoil anyone's session. Everyone playing in VR
needs the same version of the mod. Players who do not have the mod at all can sit in the same game
with you quite happily — they just see the flat version, and nothing you do in VR reaches them as
anything but a normal move.

---

## Rough edges

It is **pre-1.0** and it moves. The honest short list:

- Large, fully-revealed scenarios seen from above can hitch for a moment as the walls open up.
- With five windows open in the map room they can start to overlap, and the nearer one takes the
  clicks.
- A couple of the game's own messages still appear only on the flat monitor — you see the effect
  but not the sentence.
- Mixed-reality see-through mode turns the built rooms and the sky off; they cannot be drawn over
  passthrough and still look right.

[The full list is in the playing guide](docs/PLAYING.md#known-limitations) — it is longer, and none
of it is a surprise.

---

## Where to go next

- **[Install guide](INSTALL.md)** — what you need, how to install it, settings, updates,
  troubleshooting, uninstall.
- **[Playing guide](docs/PLAYING.md)** — every control, what the board, the panels and the map
  room do, multiplayer in full, and the complete list of rough edges.
- **[Working on the mod](docs/DEVELOPING.md)** — building from source, the asset pipeline, the
  hardware test scripts. Nothing in this README is needed to build it.

---

## Credits and licence

Built on the work of others:

- **[LCVR](https://github.com/DaXcess/LCVR)** and **[RepoXR](https://github.com/DaXcess/RepoXR)**
  (GPL-3.0) — the VR startup pattern, headset failover and finger curling this mod adapts.
- **[UUVR](https://github.com/Raicuparta/uuvr)** (GPL-3.0) — the flat-screen-in-VR pattern used
  where a world panel is not the right answer.
- **[SteamVR Unity Plugin](https://github.com/ValveSoftware/steamvr_unity_plugin)** by Valve
  (BSD-3-Clause) — the base hand models.
- **Demeo** (Resolution Games) — the interaction model this mod chases. No assets or code from
  it are used.
- The hands, the gloves and the other built 3D assets were made for this project by its artist.

**Licence: GPL-3.0** — see [LICENSE](LICENSE). The mod ships **only its own code and its own
licensed artwork**; never game files, game assets or decompiled sources.

Not affiliated with Flaming Fowl Studios, Twin Sails Interactive, Asmodee or Cephalofair Games.
Gloomhaven and its artwork belong to their owners.
