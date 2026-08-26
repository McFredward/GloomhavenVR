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
│   ├── Core/                   XR bootstrap, logging, layers, module registry, scene registry
│   │   ├── Startup/            getting an OpenXR runtime up before anything touches Unity.XR
│   │   ├── Perf/               the frame budget: measuring it, and spending less of it
│   │   ├── WallFade/           the 15-part wall-fade driver and its prop classifiers
│   │   ├── Haunt/              the apparitions and their schedule
│   │   ├── Sound/              ambience: what the room sounds like
│   │   ├── Environment/        what the room looks like beyond the board (sky, mood, lights)
│   │   ├── Water/              the water feature
│   │   ├── MixedReality/       passthrough, the rim curtain
│   │   ├── SelfUpdate/         the mod's own update channel: check, verify, stage, relaunch
│   │   ├── Loc/                every user-visible string, EN + DE
│   │   └── Diagnostics/        instruments about the MOD, not about the game
│   ├── Rig/                    VR camera rig, world grab/scale, comfort, locomotion, quality
│   ├── Hands/                  hand models, finger curling · Interact/ interaction primitives
│   ├── Cards/                  the card hand: config, API, fan, the card type itself
│   │   ├── Driver/             the 6-part interaction driver (laser, rebuild, flows)
│   │   ├── Tray/               the 7-part play tray
│   │   ├── Art/                what a card LOOKS like: face, mesh, contour, glow, dust
│   │   ├── Caps/               the wooden board's physical controls and their engraving
│   │   ├── Piles/              draw / discard / burnt / item piles and their browsers
│   │   └── Patches/            Harmony patches owned by this module
│   ├── Board/                  hex/actor picking, focus · FigureGrab/ · Patches/
│   ├── WorldUI/                everything the player reads: bars, mirror, wrist HUD, assets
│   │   ├── Modal/              the float pipeline: which game windows leave the 2D screen
│   │   ├── Conversion/         uGUI canvas -> world-space panel, and keeping it fitted
│   │   ├── Sharpness/          supersampling, mip bakes, and the probes that measure them
│   │   ├── FlatScreen/         the flat game screen in the world + per-eye stereo compositor
│   │   ├── Options/            the mod's settings surface inside the game's options screen
│   │   ├── Grab/               picking a panel up and operating it
│   │   ├── Composites/         windows assembled from more than one game window
│   │   ├── Materialise/        the window appear/vanish particle effect
│   │   ├── Buttons/            the control board's caps and their skin
│   │   ├── Tooltips/           hover text, in the world and on windows
│   │   ├── Surfaces/           one file per GAME widget the mod adopts
│   │   ├── MapRoom/            the 3D campaign map room
│   │   └── Patches/            Harmony patches owned by this module
│   ├── Net/                    wire protocol, packets, session, transport, version guard
│   │   ├── Remote/             everything a PEER draws on our side — one prefix, one job
│   │   ├── Avatar/             the peer's embodiment: head, hands, mask, badge
│   │   └── Board/              the shared play surface as a NETWORK object
│   ├── Voice/                  spatial voice chat and the speaker indicator
│   ├── Compat/                 stereo/PPv2 fixes, scene variants · Tutorial/
│   ├── Defaults/               every shipped default value, in one place
│   └── Assets/                 the mod's own embedded art (the wordmark)
├── libs/                       Natives/ + RuntimeDeps/ — populated by scripts, never committed
├── prebuilt/                   the committed asset bundle shipped when Unity is unavailable
├── tools/RuntimeDepsBuild/     provisional RuntimeDeps compile from needle-mirror source
├── packaging/INSTALL.txt.in    the template for the zip's INSTALL.txt
├── scripts/                    build, install, packaging, bundle and verification scripts
├── tests/GloomhavenVR.WireTests/  the only executable tests: byte-exact wire vectors and the
│                               pure-arithmetic lints that ride along with them
├── unity/                      the Unity 2021.3.5f1 asset project and its guides
├── docs/                       this file, interface contracts, patch inventory, test scripts
└── .planning/                  STATE.md, roadmap, architecture, verified game-API research
```

### Folder does not equal namespace, deliberately

A file under `WorldUI/Modal/` still declares `namespace GloomhavenVR.WorldUI`. C# does not
require the two to agree, and the subfolders exist for a reader scanning a directory, not for the
type system — so `using` statements, and every existing reference, are untouched by the layout.

The alternative was renaming the namespace with the folder. That renames every type in it, which
means the compiled-form guard (`scripts/refactor-guard.sh`) would report hundreds of NEW/GONE
entries in exactly the commit where "prove nothing else moved" matters most. As a pure move, the
whole restructure — 251 files across four modules — came back **0 changed**.

An IDE may offer to "fix" the namespace to match the folder. Do not accept it.

### Moving a file is safe, and five things will tell you if it is not

Static field initialisers run in declaration order, and across the parts of a partial type that
order is MSBuild's **compile** order — which follows the file path. So a move reshuffles them.
`scripts/check-partial-order.py` proves no initialiser in the mod depends on another part, which
is what makes moves free; it fails if a new one appears.

Five places pin a source path, and every one of them fails loudly rather than silently:

| what | how it tells you |
|---|---|
| `.planning/refactor/FRAME-ORDER.lock` | names the marker and both paths |
| `tests/…/GloomhavenVR.WireTests.csproj` | `CS2001` naming each missing source |
| `scripts/check-mirrors.sh` | "did it move or get renamed?" |
| `scripts/check-remote-defaults.py`, `check-wire-coverage.py` | `FileNotFoundError` with the path |
| `docs/PATCH-INVENTORY.md` | generated — regenerate with `scripts/patch-inventory.sh generate` |

Two of the wire tests go further and check that they can still *find* what they lint —
`WaterOwnSurfaceVectors` counts the water sources it sees ("a sudden drop means this lint stopped
looking at the driver") and `Shims.cs` pins `HeadMaskLibrary.cs` because the shimmed `MaskCount`
is otherwise unverified. Both fired during the restructure and both were right.

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
