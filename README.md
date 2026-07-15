# GloomhavenVR

**A Demeo-style room-scale VR mod for [Gloomhaven (digital)](https://store.steampowered.com/app/780290/Gloomhaven/)** — Unity Mono, loaded via BepInEx 5, patched with Harmony. No game files are modified.

> **Status: v0.1 pre-alpha — feature-complete and integrated (phases 0–5), awaiting
> the full-loop hardware pass** (`docs/TESTING-FULL-LOOP.md`). Release zips are
> produced by `scripts/package-release.sh`; install guide: [INSTALL.md](INSTALL.md).

## Features (v0.1)

- **VR bootstrap** — OpenXR (Quest Link / Virtual Desktop / Steam Link / SteamVR)
  installed at boot by a BepInEx preloader, with pre-flight checks, runtime failover
  and MultiPass stereo; the game stays 100% vanilla when disabled or when VR is
  unavailable.
- **Diorama rig** — scenarios render as a head-tracked table-scale miniature world;
  the main menu / guildmaster map show on a floating screen with a head-tracked
  menu camera.
- **Hands** — tracked hands with articulated fingers (bundle gloves or procedural
  fallback), poke / laser-ray / proximity-grab / palm-gate interaction primitives,
  haptics, and a per-mode/per-hand interactor policy (dominant hand points, the
  other holds the cards).
- **The Demeo card hand** — palm-up fans your live ability cards out as 3D cards
  (real game card faces); grab to inspect, drop on the play tray to play
  (slot order = initiative, physical swap), short/long-rest tokens, in-turn
  top/bottom half selection by poking the played cards.
- **Touch the board** — hex/actor/door/chest picking by fingertip touch (near) or
  laser (far, reticle snaps to hex centers), clicks committed through the game's
  own click path (undo/MP-safe), AoE rotation on the thumbstick, hover haptics,
  poke a miniature for its stat panel.
- **Physical interface** — poke-able Ready/Undo/Skip buttons at the table edge,
  initiative track / element board / combat log / objectives as world panels,
  world-modal confirmation dialogs, phase-banner toasts, true world-space actor HP
  bars, a wrist status HUD, world tooltips, and a floating 2D screen (+ ray pointer
  via the game's own virtual mouse) for menus/merchant/level-up. Every surface is
  toggleable; every conversion is fully reversible.
- **Comfort** — Demeo-style world grab (one grip drags, two grips rotate/pinch-scale),
  snap/smooth turn, recenter chord (B+Y both hands) with seated/standing presets,
  optional vignette, head-under-table guard.
- **In-VR settings panel** — poke the SET gear at the table edge (or hold the
  non-dominant A/X): world scale, turning, seated mode, vignette, grab toggles,
  dominant hand, module switches — live and persistent.
- **Dev harness** — `[Dev] Enabled` runs the event bus, mode machine, simulated
  hands, cards and panels on a flat desktop without an HMD (F6 hot reload via
  ScriptEngine supported; everything cleans up after itself).

## Documentation index

| Doc | Contents |
|---|---|
| [INSTALL.md](INSTALL.md) | end-user install, OpenXR runtime selection, config files, troubleshooting |
| [docs/TESTING-FULL-LOOP.md](docs/TESTING-FULL-LOOP.md) | the M4/v0.1 end-to-end hardware session script |
| [docs/TESTING-P1.md](docs/TESTING-P1.md) … [TESTING-P4.md](docs/TESTING-P4.md) | per-phase hardware checklists + the P1 failure-triage table |
| [docs/INTERFACES-P2.md](docs/INTERFACES-P2.md) | shared module API: hands, interactors, event bus, mode matrix, module-config pattern |
| [docs/INTERFACES-P4.md](docs/INTERFACES-P4.md) | comfort/rig surface (`ComfortSettings`, world grab, menu rig) |
| [docs/PATCH-INVENTORY.md](docs/PATCH-INVENTORY.md) | every Harmony patch (method → module → type) + contention rules |
| [.planning/](/.planning/) | architecture, roadmap, verified game-API research |

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

See **[INSTALL.md](INSTALL.md)**. Short version: install
[BepInEx 5.4.23.5 (x64)](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5)
into the Gloomhaven folder, extract the release zip over it, pick your OpenXR
runtime, start the game. `[General] Enabled = false` in
`BepInEx/config/dev.gloomhavenvr.cfg` returns the game to 100% vanilla; if the
headset shows nothing, launch with `-force-d3d11`.

Release zips are built with `scripts/package-release.sh` (plugin + RuntimeDeps +
preloader + natives + INSTALL.txt; refuses to package incomplete artifact sets).

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
├── scripts/                    build.sh, fetch-natives.sh, build-runtimedeps.sh,
│                               deploy.ps1, package-release.sh, build-bundles.sh
├── docs/                       testing checklists, interface contracts, patch inventory
└── .planning/                  roadmap, architecture, research notes
```

## Credits

- **[LCVR](https://github.com/DaXcess/LCVR)** and **[RepoXR](https://github.com/DaXcess/RepoXR)**
  by DaXcess (GPL-3.0) — the OpenXR preloader/bootstrap pattern, runtime failover and
  finger-curling approach this mod adapts.
- **[UUVR](https://github.com/Raicuparta/uuvr)** by Raicuparta (GPL-3.0) — the
  screen-mirror ("flat screen in VR") pattern for non-physicalized UI.
- **[SteamVR Unity Plugin](https://github.com/ValveSoftware/steamvr_unity_plugin)**
  by Valve (BSD-3-Clause) — hand model assets used by the optional asset bundle.
- **Demeo** (Resolution Games) — the interaction model this mod chases; no assets
  or code from it are used.

## License

**GPL-3.0** (see [LICENSE](LICENSE)) — required and embraced: this project adapts
patterns and code from the GPL-3.0 mods credited above. SteamVR hand assets remain
under BSD-3-Clause (compatible; license text ships with the asset bundle sources).
The mod distributes **only its own code and self-authored/licensed assets** — never
game files, game assets, or decompiled sources. Not affiliated with Flaming Fowl
Studios, Twin Sails Interactive, or Cephalofair Games.
