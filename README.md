# GloomhavenVR

**A Demeo-style room-scale VR mod for [Gloomhaven (digital)](https://store.steampowered.com/app/780290/Gloomhaven/)** — Unity Mono, loaded via BepInEx 5, patched with Harmony. No game files are modified.

> **Status: pre-alpha — Phase 3a (board touch targeting) code-complete, awaiting hardware validation.**
> On top of the Phase-1 OpenXR bootstrap and diorama camera rig, the mod now has tracked
> hands with articulated fingers (bundle gloves or procedural fallback), the poke / ray /
> proximity-grab / palm-gate interaction primitives, haptics, a typed VR event bus over the
> game's message pump, the VR mode state machine and the virtual-mouse bridge — plus a
> desktop dev harness (`[Dev] Enabled`) that exercises all of it without an HMD.
> **New in Phase 3a:** the board is playable by hand — hex/actor/door/chest picking by
> fingertip touch (near) or laser ray (far), clicks committed through the game's own
> click path (undo/MP-safe), AoE rotation on the thumbstick, hover haptics, enemy stat
> panel on point/touch. Frozen API for the Phase-3 feature workers: `docs/INTERFACES-P2.md`.
> Hardware checklists: `docs/TESTING-P1.md`, `docs/TESTING-P2.md`, `docs/TESTING-P3A.md`.
> No card hand / world-space UI yet (Phases 3b/3c).

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
2. From the mod release zip, into the Gloomhaven folder:

   ```
   BepInEx/plugins/GloomhavenVR/GloomhavenVR.dll
   BepInEx/plugins/GloomhavenVR/RuntimeDeps/*.dll        (Unity XR assemblies)
   BepInEx/patchers/GloomhavenVR/GloomhavenVR.Preload.dll
   BepInEx/patchers/GloomhavenVR/Natives/*.dll           (UnityOpenXR + openxr_loader)
   ```

3. Start the game. At boot the preloader copies the OpenXR natives into
   `Gloomhaven_Data/Plugins/x86_64/` and writes
   `Gloomhaven_Data/UnitySubsystems/UnityOpenXR/UnitySubsystemsManifest.json`
   (idempotent; these are the only files placed outside `BepInEx/`).
   Config appears at `BepInEx/config/dev.gloomhavenvr.cfg`
   (`[General] Enabled = false` returns the game to 100% vanilla).
4. If the headset shows nothing, launch with `-force-d3d11` (desktop OpenXR
   requires D3D11) and see the triage table in `docs/TESTING-P1.md`.

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

2. Fetch/build the XR dependencies once, then build:

   ```
   scripts/fetch-natives.sh          # OpenXR natives -> libs/Natives (SHA256-pinned)
   scripts/build-runtimedeps.sh      # Unity.XR.* assemblies -> libs/RuntimeDeps (provisional)
   dotnet build GloomhavenVR.sln -c Release      # or: scripts/build.sh
   ```

   The plugin compiles against `libs/RuntimeDeps` (publicized). The provisional
   RuntimeDeps are compiled from needle-mirror package source outside Unity
   (`tools/RuntimeDepsBuild/README.md`); an editor-harvested set
   (`unity/HARVESTING.md`) replaces them 1:1 when available.

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
├── libs/                       Natives/ (fetched OpenXR natives) + RuntimeDeps/ (Unity XR
│                               assemblies) — populated by scripts, never committed
├── tools/RuntimeDepsBuild/     provisional RuntimeDeps compile from needle-mirror source
├── scripts/                    build.sh, fetch-natives.sh, build-runtimedeps.sh, deploy.ps1
├── docs/                       TESTING-P1/P2.md (Windows validation checklists),
│                               INTERFACES-P2.md (frozen Phase-2 API for feature workers)
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
