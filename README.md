<p align="center">
  <picture>
    <source media="(prefers-color-scheme: light)" srcset="docs/img/logo-onlight.png">
    <img src="docs/img/logo.png" alt="GloomhavenVR" width="600">
  </picture>
</p>

<p align="center">
  A room-scale VR mod for <b>Gloomhaven (digital)</b> on PC.<br>
  The scenario becomes a diorama on a table you stand at, played with tracked hands.<br>
  Rules, saves and campaign progress stay the game's own.
</p>

<p align="center">
  <a href="INSTALL.md"><b>Install →</b></a> &nbsp;·&nbsp;
  <a href="docs/PLAYING.md">Playing guide</a> &nbsp;·&nbsp;
  <a href="docs/PLAYING.md#known-limitations">Known limitations</a>
</p>

---

**Multiplayer** is supported for the whole session. Every VR player has a mask, hands and their own
control board; lifted miniatures, moved windows and pointing fingers are visible to everyone.
Players without a headset join the same game on a flat screen.

<table>
<tr>
<td width="50%"><video src="https://github.com/user-attachments/assets/265e8b02-5b8b-4732-aad8-2ba134bf4225" controls muted loop></video></td>
<td width="50%"><video src="https://github.com/user-attachments/assets/5bc45e99-89d1-4b3b-aa7d-47a74c184805" controls muted loop></video></td>
</tr>
</table>

Turn your palm up and the hand of cards fans out; take one, read it, drop it into a slot on your
board. Squeeze a miniature off the board, hold it, let go. The game's windows become panels you can
grab, move and park. Between scenarios the campaign map is a room with a table in it.

## Assets

Three pairs of hands, three masks, three control boards. One dropdown each, changeable mid-session,
and the other players see the choice. All modelled for this project.

<p align="center">
  <img src="docs/img/styles.png" width="680" alt="Three hands, three masks, three control boards">
</p>

## Environments

A candle-lit cellar or a night forest with a real star catalogue overhead — both built for this
project, both with firelight, ambient sound and rare events, each with its own setting.

<p align="center">
  <img src="docs/img/env-cellar.jpg" width="345" alt="The cellar">
  <img src="docs/img/env-forest.jpg" width="345" alt="The night forest">
</p>

The rooms react to the scenario's element infusions, at the edges rather than over the play area.
Fire lights the room and sets props burning, Ice grows frost, Air moves flames and foliage, Earth
brings up growth, Light lifts the ambient level and Dark lowers it and eclipses the moon.

<p align="center">
  <img src="docs/img/env-elements.jpg" width="700" alt="The same camera with the element off and on"><br>
  <i>Same camera, element off left and on right.</i>
</p>

**Mixed reality** replaces the sky with a chroma-key colour so the board can be composited over your
real room; the built rooms switch off while it is on.

## Requirements

**Gloomhaven (digital)** for PC (Steam or GOG) · a Windows PC that runs it · a PC-VR headset with
two tracked controllers · room-scale or standing. Developed on a **Quest 3 over Virtual Desktop**.

Installation is two archives into the game folder. **[→ Install guide](INSTALL.md)**

---

<details>
<summary><b>Credits and licence</b></summary>

- **[LCVR](https://github.com/DaXcess/LCVR)** and **[RepoXR](https://github.com/DaXcess/RepoXR)**
  (GPL-3.0) — the VR startup pattern, headset failover and finger curling this mod adapts.
- **[UUVR](https://github.com/Raicuparta/uuvr)** (GPL-3.0) — the flat-screen-in-VR pattern used
  where a world panel is not the right answer.
- **[SteamVR Unity Plugin](https://github.com/ValveSoftware/steamvr_unity_plugin)** by Valve
  (BSD-3-Clause) — the base hand models.
- **Demeo** (Resolution Games) — the interaction model this mod follows. No assets or code from it
  are used.
- The hands, the masks, the boards and the other built 3D assets were made for this project by its
  artist.

**Licence: GPL-3.0** — see [LICENSE](LICENSE). The mod ships **only its own code and its own
licensed artwork**; never game files, game assets or decompiled sources.

Not affiliated with Flaming Fowl Studios, Twin Sails Interactive, Asmodee or Cephalofair Games.
Gloomhaven and its artwork belong to their owners.

**Working on the mod?** [docs/DEVELOPING.md](docs/DEVELOPING.md)

</details>
