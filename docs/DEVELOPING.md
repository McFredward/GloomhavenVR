# Developing GloomhavenVR

Everything a contributor needs that a player does not. The [README](../README.md) is written for
players; nothing in it is required to build the mod, and nothing here belongs in it.

> **Read [`.planning/STATE.md`](../.planning/STATE.md) first.** It is the handover document: the
> hard rules, the verification gates every change must pass, the multiplayer 1:1 ruling, what the
> recent rounds fixed and why, the open queue, and the operational hazards that have each cost a
> round. The phase documents below describe how the project was *built*; `STATE.md` describes where
> it *is*.

## What it is, technically

A BepInEx 5 plugin (net472, Unity Mono) plus a BepInEx **preloader patcher**, Harmony-patched with
HarmonyX against Gloomhaven (digital) v1.1.8307.0 on Unity 2021.3.5f1. No game file is modified
except two `key=value` lines in `GH_Data/boot.config` (graphics jobs — the preloader writes them and
keeps a backup). Everything else lives under `BepInEx/`.

## Requirements

- **.NET SDK 8+**
- A Gloomhaven install — only its `Managed/` folder is needed at build time. Game DLLs are never
  committed.
- Unity **2021.3.5f1** — only if you are rebuilding the asset bundle. Using any other 2021.3.x
  editor produces a bundle that silently loads nowhere.

## Build

1. Point the build at your game DLLs. Create `Directory.Build.props.user` (gitignored) in the repo
   root:

   ```xml
   <Project>
     <PropertyGroup>
       <!-- folder containing GH.Runtime.dll etc. -->
       <GameManaged>C:\Program Files (x86)\Steam\steamapps\common\Gloomhaven\GH_Data\Managed</GameManaged>
     </PropertyGroup>
   </Project>
   ```

   Without it the default Steam path is assumed. The `GamePath` / `GameManaged` environment
   variables work too.

2. Fetch the XR dependencies once, then build:

   ```sh
   scripts/fetch-natives.sh          # OpenXR natives -> libs/Natives (SHA256-pinned)
   scripts/build-runtimedeps.sh      # Unity.XR.* assemblies -> libs/RuntimeDeps (provisional)
   dotnet build GloomhavenVR.sln -c Release       # or: scripts/build.sh
   ```

   The plugin compiles against `libs/RuntimeDeps` (publicized). The provisional RuntimeDeps are
   compiled from needle-mirror package source outside Unity
   (`tools/RuntimeDepsBuild/README.md`); an editor-harvested set ([`unity/HARVESTING.md`](../unity/HARVESTING.md))
   replaces them 1:1 when available.

   Builds on Windows and Linux/macOS (net472 via
   `Microsoft.NETFramework.ReferenceAssemblies`). NuGet packages come from nuget.org and
   [nuget.bepinex.dev](https://nuget.bepinex.dev) (committed `nuget.config`). Game references are
   **publicized** at build time (`BepInEx.AssemblyPublicizer.MSBuild`) so internals are used
   without reflection.

3. Deploy into a game install:

   ```powershell
   .\scripts\install.ps1 -GamePath "C:\...\Gloomhaven"
   ```

   `scripts\uninstall.ps1` reverses it, including the `boot.config` restore.

## Packaging a release

```sh
scripts/package-release.sh
```

Builds Release and assembles `dist/GloomhavenVR-<version>.zip` (version read from
`src/GloomhavenVR/GloomhavenVR.csproj`) with the exact layout the runtime probes for:

```
INSTALL.txt
BepInEx/plugins/GloomhavenVR/GloomhavenVR.dll
BepInEx/plugins/GloomhavenVR/RuntimeDeps/*.dll
BepInEx/plugins/GloomhavenVR/gloomhavenvr.bundle
BepInEx/patchers/GloomhavenVR/GloomhavenVR.Preload.dll
BepInEx/patchers/GloomhavenVR/Natives/*.dll
```

It refuses to package when `libs/Natives` or `libs/RuntimeDeps` are unpopulated, and verifies the
load-bearing paths inside the finished zip. The bundle comes from a fresh Unity build if there is
one, else from the committed `prebuilt/gloomhavenvr.bundle`.

`INSTALL.txt` is rendered from **[`packaging/INSTALL.txt.in`](../packaging/INSTALL.txt.in)** — the
single source for the text a drag-and-drop user reads. `install.ps1` renders the same template, so
the two install paths cannot describe the install differently. **If you change install steps, change
that template and the README's Install section together.**

## Asset bundle

```sh
scripts/build-bundles.sh      # needs Unity 2021.3.5f1
```

The Unity project lives in `unity/GloomhavenVR.Assets/`. See
[`unity/HANDS.md`](../unity/HANDS.md) and [`unity/CARD-ASSETS.md`](../unity/CARD-ASSETS.md).

Contributor-facing asset docs:

- [`ASSET-GUIDE-MITWIRKENDE.md`](ASSET-GUIDE-MITWIRKENDE.md) — editing the mod's 3D assets in
  Blender (German). File names, paths and bone/anchor names are contracts.
- [`ANLEITUNG-HAENDE.md`](ANLEITUNG-HAENDE.md) — the one-off Unity editor step for real hand models
  (German).

Environment work is verifiable **without a headset**: `EnvironmentsPreview.RenderAll` under
`xvfb-run` produces preview renders. The bundle step only packs; `BuildAll` does the bakes.

## Dev loop

- **Hot reload** — drop [ScriptEngine](https://github.com/BepInEx/BepInEx.Debug) (BepInEx.Debug
  r11.1) into `BepInEx/patchers/`, put the plugin DLL in `BepInEx/scripts/` instead of `plugins/`,
  press **F6** in-game. The plugin cleans up after itself in `OnDestroy` (Harmony unpatch, GameObject
  destruction) so reloads stay sane — statics from old loads linger, restart when in doubt.
- **Scene archaeology** — [UnityExplorer](https://github.com/yukieiji/UnityExplorer) v4.13.6
  (`UnityExplorer.BepInEx5.Mono.zip`); set `HideManagerGameObject = true` in `BepInEx/config/BepInEx.cfg`.
- **Logs** — `BepInEx/LogOutput.log`. Embedded PDBs and a restored stack-trace path mean exceptions
  are attributable; read the stack before theorising.
- **Multiplayer** — bump `NetProtocol.ModBuild` by 1 for every build handed to another player. It
  drives the version-mismatch dialog. Peers on different `ModBuild` values are blocked on purpose.

## Gates

Run before proposing a change:

```sh
scripts/refactor-guard.sh check    # includes the generated patch-inventory drift check
scripts/wire-tests.sh              # multiplayer wire suite
scripts/patch-inventory.sh check
scripts/check-bundle-format.sh
```

## Repo layout

```
GloomhavenVR.sln
├── src/GloomhavenVR.Preload/   BepInEx preloader patcher — installs the OpenXR natives and the
│                               UnitySubsystems manifest before the engine boots
├── src/GloomhavenVR/           the main BepInEx 5 plugin (net472)
│   ├── Core/                   XR bootstrap, config, localization, diagnostics, module registry,
│   │                           and the environment surface (sky, ambience, haunt, wall fade, water)
│   ├── Rig/                    VR camera rig, world grab/scale, comfort, locomotion, render quality
│   ├── Hands/                  hand models, finger curling, interaction primitives
│   ├── Cards/                  palm-fan card hand, play surface, half selection
│   ├── Board/                  hex/actor picking, figure grab, focus
│   ├── WorldUI/                canvas conversion, panels, control board, settings tab, map room
│   ├── Net/                    multiplayer wire protocol, avatars, shared windows
│   ├── Compat/                 stereo/PPv2 fixes, scene variants, tutorial, performance
│   ├── Defaults/               every shipped default value, in one place
│   └── Assets/                 the mod's own embedded art (the wordmark)
├── libs/                       Natives/ + RuntimeDeps/ — populated by scripts, never committed
├── prebuilt/                   the committed asset bundle shipped when Unity is unavailable
├── tools/RuntimeDepsBuild/     provisional RuntimeDeps compile from needle-mirror source
├── packaging/INSTALL.txt.in    the template for the zip's INSTALL.txt
├── scripts/                    build, install, packaging, bundle and verification scripts
├── unity/                      the Unity 2021.3.5f1 asset project and its guides
├── docs/                       this file, interface contracts, patch inventory, test scripts
└── .planning/                  STATE.md, roadmap, architecture, verified game-API research
```

## Document index

| Doc | Contents |
|---|---|
| [`CAMERA-POLICY.md`](CAMERA-POLICY.md) | camera ownership and layer policy — the rule set that came out of hardware tests #3/#4 |
| [`INTERFACES-P2.md`](INTERFACES-P2.md) | shared module API: hands, interactors, event bus, mode matrix, module-config pattern |
| [`INTERFACES-P4.md`](INTERFACES-P4.md) | comfort / rig surface (`ComfortSettings`, world grab, menu rig) |
| [`PATCH-INVENTORY.md`](PATCH-INVENTORY.md) | **generated** — every Harmony patch (method → module → type) |
| [`PATCH-NOTES.md`](PATCH-NOTES.md) | hand-maintained companion: why each patch exists, contention rules |
| [`TESTING-FULL-LOOP.md`](TESTING-FULL-LOOP.md) | the end-to-end hardware session script |
| [`TESTING-P1.md`](TESTING-P1.md) … [`TESTING-P4.md`](TESTING-P4.md) | per-phase hardware checklists; P1 §4 is the stage-by-stage failure-triage table |
| [`ASSET-GUIDE-MITWIRKENDE.md`](ASSET-GUIDE-MITWIRKENDE.md), [`ANLEITUNG-HAENDE.md`](ANLEITUNG-HAENDE.md) | asset contributor guides (German) |
| [`../.planning/`](../.planning/) | STATE.md, architecture, roadmap, game-API research |

## Licence

GPL-3.0 — required and embraced, since the project adapts patterns and code from the GPL-3.0 mods
credited in the README. The bundled SteamVR-derived hand assets remain BSD-3-Clause. The mod
distributes only its own code and self-authored or licensed assets: never game files, game assets
or decompiled sources.
