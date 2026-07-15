# GloomhavenVR

**A Demeo-style room-scale VR mod for [Gloomhaven (digital)](https://store.steampowered.com/app/780290/Gloomhaven/)** — Unity Mono, loaded via BepInEx 5, patched with Harmony. No game files are modified.

> **Status: pre-alpha scaffolding.** Nothing VR-related works yet — this is the project
> skeleton (build system, plugin/patcher stubs, module layout). Watch the releases.

## What / why

Gloomhaven digital is a faithful adaptation of the board game — and board games are the
genre that VR does best (see **Demeo**). This mod turns the game into a virtual tabletop
you stand at, instead of a flat screen:

- **Diorama on a table** — the scenario map becomes a miniature world you grab, rotate
  and zoom with your hands.
- **Physical card hand** — turn your palm toward you and your ability cards fan out as
  real 3D cards; grab one, inspect it, play it by placing it on the play surface.
  First card placed = initiative, just like the rules.
- **Poke the UI** — top/bottom card actions, Ready/Undo buttons, initiative track and
  element board become physical objects you press with your virtual finger.
- **Touch the board** — move/attack/AoE targeting by touching or pointing at hexes,
  with the game's own highlight system responding.
- **Full head-tracked stereo rendering** — a real VR camera rig, not a screen-in-VR.

Primary target: **Meta Quest 3 over PC** (Quest Link, Virtual Desktop, Steam Link — all
OpenXR runtimes). The game keeps working flat-screen in parallel; multiplayer is out of
scope for v1.

## Install (users)

Nothing useful to install yet (pre-alpha). Once there are releases:

1. Install **[BepInEx 5.4.23.5](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5)**
   (`BepInEx_win_x64_5.4.23.5.zip`) — extract into the Gloomhaven install folder
   (the one containing `GH.exe`), run the game once, verify `BepInEx/LogOutput.log` exists.
2. From the mod release zip:
   - `GloomhavenVR.dll` → `<Gloomhaven>/BepInEx/plugins/GloomhavenVR/`
   - `GloomhavenVR.Preload.dll` → `<Gloomhaven>/BepInEx/patchers/`
3. Start the game. Config appears at `BepInEx/config/dev.gloomhavenvr.cfg`
   (`[General] Enabled = false` returns the game to 100% vanilla).

## Developer setup

Requirements: **.NET SDK 8+** and a Gloomhaven install (only its `Managed/` folder is
needed at build time; game DLLs are never committed).

1. Point the build at your game DLLs — create `Directory.Build.props.user`
   (gitignored) in the repo root:

   ```xml
   <Project>
     <PropertyGroup>
       <!-- folder containing GH.Runtime.dll etc. -->
       <GameManaged>C:\Program Files (x86)\Steam\steamapps\common\Gloomhaven\Gloomhaven_Data\Managed</GameManaged>
     </PropertyGroup>
   </Project>
   ```

   Without this file the default Steam path above is assumed (you can also set the
   `GamePath`/`GameManaged` environment variables instead).

2. Build:

   ```
   dotnet build GloomhavenVR.sln -c Release      # or: scripts/build.sh
   ```

   Works on Windows and Linux/macOS (net472 via Microsoft.NETFramework.ReferenceAssemblies).
   NuGet packages come from nuget.org + [nuget.bepinex.dev](https://nuget.bepinex.dev)
   (committed `nuget.config`). Game references are **publicized** at build time
   (BepInEx.AssemblyPublicizer.MSBuild) so internals are used without reflection.

3. Deploy into your game install:

   ```powershell
   .\scripts\deploy.ps1 -GamePath "C:\...\Gloomhaven"
   ```

### Dev loop

- **Hot reload:** drop [ScriptEngine](https://github.com/BepInEx/BepInEx.Debug)
  (BepInEx.Debug r11.1) into `BepInEx/patchers/`, put the plugin DLL into
  `BepInEx/scripts/` instead of `plugins/`, press **F6** in-game to reload.
  The plugin cleans up after itself in `OnDestroy` (Harmony unpatch, GO destruction)
  so reloads stay sane — statics from old loads linger, restart when in doubt.
- **Scene archaeology:** [UnityExplorer](https://github.com/yukieiji/UnityExplorer)
  v4.13.6 (`UnityExplorer.BepInEx5.Mono.zip`); set `HideManagerGameObject = true`
  in `BepInEx/config/BepInEx.cfg`.
- Logs: `BepInEx/LogOutput.log` (embedded PDBs → readable stack traces).

## Repo layout

```
GloomhavenVR.sln
├── src/GloomhavenVR.Preload/   BepInEx preloader patcher — installs OpenXR natives +
│                               UnitySubsystems manifest before engine boot (Phase 1)
├── src/GloomhavenVR/           main BepInEx 5 plugin (net472)
│   ├── Core/                   XR bootstrap, config, diagnostics, module registry contract
│   ├── Rig/                    VR camera rig, world grab/scale, comfort
│   ├── Hands/                  hand models, finger curling, interaction primitives
│   ├── Cards/                  palm-fan card hand, play surface, half selection
│   ├── Board/                  hex touch/ray picking, AoE rotation
│   ├── WorldUI/                canvas conversion, physical buttons, HUD, 2D fallback
│   └── Compat/                 stereo/PPv2 fixes, scene variants, performance
├── scripts/                    build.sh (POSIX), deploy.ps1 (copy into game install)
└── .planning/                  roadmap, architecture, research notes
```

## License

**GPL-3.0** (see [LICENSE](LICENSE)) — this project adapts patterns and code from the
GPL-3.0 VR mods [LCVR](https://github.com/DaXcess/LCVR),
[RepoXR](https://github.com/DaXcess/RepoXR) and
[UUVR](https://github.com/Raicuparta/uuvr).
The mod distributes **only its own code and self-authored assets** — never game files,
game assets, or decompiled sources. Not affiliated with Flaming Fowl Studios,
Twin Sails Interactive, or Cephalofair Games.
