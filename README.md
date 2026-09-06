<p align="center">
  <img src="docs/img/promo.gif" alt="GloomhavenVR" width="720">
</p>

<p align="center">
  <a href="LICENSE">
    <img src="https://img.shields.io/badge/License-GPL_v3-blue.svg" alt="License: GPL v3">
  </a>
  <a href="https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5">
    <img src="https://img.shields.io/badge/ModLoader-BepInEx 5.4.23.5-orange.svg" alt="Mod loader: BepInEx 5.4.23.5">
  </a>
  <a href="https://steamdb.info/app/780290/patchnotes/">
    <img src="https://img.shields.io/badge/Supported Game version-1.1.8307.0-yellow.svg" alt="Supported game version">
  </a>
  <a>
    <img src="https://img.shields.io/badge/version-0.1.1-7c3aed.svg" alt="Mod version">
  </a>
</p>

<p align="center">
  <img src="docs/img/flag-en.png" width="24" alt="English">&nbsp;<b>English</b>
  &nbsp;&nbsp;|&nbsp;&nbsp;
  <a href="README.de.md"><img src="docs/img/flag-de.png" width="24" alt="Deutsch">&nbsp;Deutsch</a>
</p>

<p align="center">
  A community-made VR mod for <a href="https://store.steampowered.com/app/780290/Gloomhaven/"><b>Gloomhaven (Digital)</b></a>.<br>
  Demeo-like motion controls, fully integrated VR Multiplayer, Custom Environments, Mixed Reality …
</p>

<p align="center">
  <a href="INSTALL.md"><b>Install →</b></a> &nbsp;·&nbsp;
  <a href="docs/PLAYING.md#the-controls">Controls</a> &nbsp;·&nbsp;
  <a href="docs/PLAYING.md">Playing guide</a> &nbsp;·&nbsp;
  <a href="docs/PLAYING.md#known-limitations">Known limitations</a>
</p>

---

<!-- VIDEO PLACEHOLDER: overview.mp4
     WHAT IT SHOWS: the hero shot. Standing at the table in a lit scenario, then dragging, rotating
     and zooming the whole board with the two-handed world grab.
     Full brief: docs/VIDEO-SHOTLIST.md. Record it, upload the mp4 through a GitHub
     comment box (docs/img/README.md explains how), paste the returned
     user_attachments URL below, then delete this line and the closing marker. The same
     URL goes into the other language page at the same spot.
<p align="center">
  <video src="https://github.com/user-attachments/assets/PASTE_UUID_HERE" controls muted loop></video>
</p>
-->

### Your cards and your board

<table>
<tr>
<td width="50%"><video src="https://github.com/user-attachments/assets/265e8b02-5b8b-4732-aad8-2ba134bf4225" controls muted loop></video></td>
<td width="50%"><video src="https://github.com/user-attachments/assets/c3611c21-964e-4983-ad4d-ef13518285ab" controls muted loop></video></td>
</tr>
</table>

Your control board is a real desk in front of you: two recesses for the round's cards, keys you push
in with a fingertip, and a rod under it that carries the whole thing wherever you want it.
Every part of it is named on one labelled picture in the
[playing guide](docs/PLAYING.md#your-cards-and-your-board).

### The board in front of you

<table>
<tr>
<td width="50%"><video src="https://github.com/user-attachments/assets/5bc45e99-89d1-4b3b-aa7d-47a74c184805" controls muted loop></video></td>
<td width="50%"><video src="https://github.com/user-attachments/assets/cf113c2e-d995-4ca3-87f5-d0059e909a2c" controls muted loop></video></td>
</tr>
</table>

### Full VR multiplayer

Every VR player has a mask, hands and their own control board. Lifted miniatures, moved windows and
pointing fingers are visible to everyone, and players without a headset join the same game on a flat
screen.

<!-- VIDEO PLACEHOLDER: multiplayer.mp4
     WHAT IT SHOWS: the table from the other side. A second player's mask and hands, their control
     board mirrored beside them, a miniature they lift, a shared window one of them drags across
     the room.
     Full brief: docs/VIDEO-SHOTLIST.md. Record it, upload the mp4 through a GitHub
     comment box (docs/img/README.md explains how), paste the returned
     user_attachments URL below, then delete this line and the closing marker. The same
     URL goes into the other language page at the same spot.
<p align="center">
  <video src="https://github.com/user-attachments/assets/PASTE_UUID_HERE" controls muted loop></video>
</p>
-->

## Mod assets

Three pairs of hands, three masks, three control boards — one dropdown each, changeable mid-session,
and the other players see your choice. All modelled for this project.

<p align="center">
  <img src="docs/img/styles.png" width="680" alt="Three hands, three masks, and three control boards with the grab rod each one comes with">
</p>

Every window and every board hangs from a turned rod and is moved by taking hold of it — with your
hand, or with the pointer from across the table. A board you look at across the table wears **its
owner's** rod, not yours.

## Environments

A candle-lit cellar or a night forest under a real star catalogue — both built for this project, both
with firelight, ambient sound and rare events.

<p align="center">
  <img src="docs/img/env-cellar.jpg" width="345" alt="The cellar">
  <img src="docs/img/env-forest.jpg" width="345" alt="The night forest">
</p>

**Mixed reality** drops the sky, so your streaming app can put the table in your real room.

### Elemental changes

**The rooms react to the scenario's element infusions**, at the edges rather than over the play area.
Fire sets props burning, Ice grows frost, Air moves flames and foliage, Earth brings up growth, Light
lifts the ambient level, Dark eclipses the moon.

<p align="center">
  <img src="docs/img/env-elements.jpg" width="700" alt="The same camera with the element off and on"><br>
  <i>Same camera, element off left and on right.</i>
</p>

## Requirements

**Gloomhaven (Digital)** for PC (Steam or GOG) · Windows · a PC-VR headset with two tracked
controllers · room-scale or standing. Developed on a **Quest 3 over Virtual Desktop**.

**[→ Install guide](INSTALL.md)** — two archives into the game folder.

---

<details>
<summary><b>Credits and licence</b></summary>

- **[LCVR](https://github.com/DaXcess/LCVR)** and **[RepoXR](https://github.com/DaXcess/RepoXR)**
  (GPL-3.0) — the VR startup pattern, headset failover and finger curling this mod adapts.
- **[UUVR](https://github.com/Raicuparta/uuvr)** (GPL-3.0) — the flat-screen-in-VR pattern used
  where a world panel is not the right answer.
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
