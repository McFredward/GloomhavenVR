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
    <img src="https://img.shields.io/badge/version-1.0.0-7c3aed.svg" alt="Mod version">
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
  <a href="docs/PLAYING.md">Playing guide</a> &nbsp;·&nbsp;
  <a href="docs/PLAYING.md#known-limitations">Known limitations</a>
</p>

---

### Your cards and your board

<table>
<tr>
<td width="50%"><video src="https://github.com/user-attachments/assets/265e8b02-5b8b-4732-aad8-2ba134bf4225" controls muted loop></video></td>
<td width="50%"><video src="https://github.com/user-attachments/assets/c3611c21-964e-4983-ad4d-ef13518285ab" controls muted loop></video></td>
</tr>
</table>

### The board in front of you

<table>
<tr>
<td width="50%"><video src="https://github.com/user-attachments/assets/5bc45e99-89d1-4b3b-aa7d-47a74c184805" controls muted loop></video></td>
<td width="50%"><video src="https://github.com/user-attachments/assets/cf113c2e-d995-4ca3-87f5-d0059e909a2c" controls muted loop></video></td>
</tr>
</table>

### Grab it where it is

Every window and every control board hangs from a turned wooden or metal rod, and you move it by
taking hold of that rod — with your hand, or with the pointer from across the table. Each of the
three board styles wears its own: honey oak with a dark bronze knob, forged steel, or patinated
bronze with gilt caps. Windows get a quieter walnut one so it does not compete with the page next
to it.

The rods are real geometry, not a painted strip: they are round, so the pointer has to actually
hit one, and a window's rod keeps the same grain, the same worn grip and the same fine beading
whatever width the window happens to be.

### Full VR Multiplayer

Every VR player has a mask, hands and their own
control board; lifted miniatures, moved windows and pointing fingers are visible to everyone.
Players without a headset join the same game on a flat screen.

A board you are looking at across the table wears **its owner's** rod, not yours — if they play on
the bronze board and you play on oak, that is what you see. Windows shared with the group carry a
small badge in the corner rather than a coloured bar.

## Mod Assets

Three pairs of hands, three masks, three control boards. One dropdown each, changeable mid-session,
and the other players see the choice. All modelled for this project.

<p align="center">
  <img src="docs/img/styles.png" width="680" alt="Three hands, three masks, and three control boards with the grab rod each one comes with">
</p>

Each board is shown with the turned grab rod it comes with, at the spot along its bottom edge where
you actually take hold of it. The rods are modelled from a lathe profile and textured one by one,
down to the incised fillets, the knurled collars and the worn band where a hand has held it. Windows
get a fourth, quieter walnut rod.

## Environments

A candle-lit cellar or a night forest with a real star catalogue overhead — both built for this
project, both with firelight, ambient sound and rare events, each with its own setting.

<p align="center">
  <img src="docs/img/env-cellar.jpg" width="345" alt="The cellar">
  <img src="docs/img/env-forest.jpg" width="345" alt="The night forest">
</p>

**Mixed reality** replaces the sky with a chroma-key colour so the board can be composited over your
real room; the built rooms switch off while it is on.

### Elemental changes

<b>The rooms react to the scenario's element infusions</b>, at the edges rather than over the play area.
Fire lights the room and sets props burning, Ice grows frost, Air moves flames and foliage, Earth
brings up growth, Light lifts the ambient level and Dark lowers it and eclipses the moon.

<p align="center">
  <img src="docs/img/env-elements.jpg" width="700" alt="The same camera with the element off and on"><br>
  <i>Same camera, element off left and on right.</i>
</p>

## Requirements

**Gloomhaven (Digital)** for PC (Steam or GOG) · a Windows PC that runs it · a PC-VR headset with
two tracked controllers · room-scale or standing. Developed on a **Quest 3 over Virtual Desktop**.

Installation is two archives into the game folder. **[→ Install guide](INSTALL.md)**

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
