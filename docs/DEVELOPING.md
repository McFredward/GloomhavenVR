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

### The restart on the first start

The player docs carry three sentences about this and nothing more: the game closes and reopens
itself once, that is meant to happen, and here is how to put the original `boot.config` back. The
mechanism lives here, one link away from [`INSTALL.md`](../INSTALL.md). A player installing a mod
needs to know what will happen, whether it is normal, and what to do if it goes wrong; nothing else
on that page may compete with those three.

Unity reads the graphics-jobs flags out of `GH_Data/boot.config` **while it is starting up**, so the
run that switches them on can never be the run that benefits. The preloader therefore writes the two
lines, keeps the original as `boot.config.gloomhavenvr-backup`, and restarts the game once. On the
test machine that is a locked 45 Hz becoming a clean 90. It cannot loop, and it happens before any
save is touched.

Two ways to skip the restart, both in `BepInEx/config/dev.gloomhavenvr.cfg`:

| Setting | Effect |
|---|---|
| `[Core] AutoRestartForGraphicsJobs = false` | the file is still written; the log asks you to restart the game yourself |
| `[Core] EnableGraphicsJobs = false` **plus** `-force-gfx-jobs native` in the game's launch options | works from the very first start and writes no game file at all |

### Controller models in the tutorial

The in-headset tutorial draws the controller the player is actually holding. **A Steam Frame is
shown a neutral controller** — Valve does not distribute a model of theirs. This used to be a
parenthesis in [`PLAYING.md`](PLAYING.md); it is a fact about one headset's asset availability, not
something a player has to know to play, so it lives here.

## Requirements

- **.NET SDK 8.0.400 or newer stable SDK** — `global.json` prefers 8.0.4xx when installed
  and permits later SDK families, including .NET 10, when it is absent. CI installs and
  verifies 8.0.4xx explicitly. `install.ps1` checks SDK resolution before installation.
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
INSTALL-DEUTSCH.txt
BepInEx/plugins/GloomhavenVR/GloomhavenVR.dll
BepInEx/plugins/GloomhavenVR/RuntimeDeps/*.dll
BepInEx/plugins/GloomhavenVR/RuntimeDeps/versions.json
BepInEx/plugins/GloomhavenVR/gloomhavenvr.bundle
BepInEx/plugins/GloomhavenVR/THIRD-PARTY.txt
BepInEx/patchers/GloomhavenVR/GloomhavenVR.Preload.dll
BepInEx/patchers/GloomhavenVR/Natives/*.dll
```

`THIRD-PARTY.txt` ships only when the bundle does — it is the licence notice the bundled art
requires, and it has to travel with the copies.

**The bundle is REQUIRED.** Every 3D asset (hands, control board, card backing, map table, head
avatars, environments, controller models) and every shader the mod ships lives in it; without the
file the mod starts, logs an Alert per subsystem and degrades to procedural placeholders everywhere.
The bundle comes from a fresh Unity build if there is one, else from the committed
`prebuilt/gloomhavenvr.bundle`. If neither exists the script warns on stderr and ships
[`packaging/gloomhavenvr.bundle.README.txt`](../packaging/gloomhavenvr.bundle.README.txt) (English
and German in one file) at the bundle's path, so the player who opens that zip is told what is
missing, where it goes and which log line proves it loaded — `[Hands] gloomhavenvr.bundle loaded
from …`. Such a zip is not a release; `release.yml` refuses to publish one without the bundle.

It refuses to package when `libs/Natives` or `libs/RuntimeDeps` are unpopulated, and verifies the
load-bearing paths inside the finished zip.

**Every `.txt` in the zip is written as UTF-8 with BOM and CRLF line endings**, whatever the
template in git has, and `scripts/check-package-text.py <zip>` fails the run if one is not (or
carries a double-encoded umlaut). The reader is a Windows user who double-clicks a `.txt`; without
the BOM, legacy Notepad, WordPad, the 7-Zip and WinRAR viewers and the Explorer preview pane decode
it as the ANSI code page and `raumgroßes` renders as `raumgroÃŸes`. `install.ps1` writes its zip
the same way and runs the same check.

`INSTALL.txt` and `INSTALL-DEUTSCH.txt` are rendered from
**[`packaging/INSTALL.txt.in`](../packaging/INSTALL.txt.in)** and
**[`packaging/INSTALL.de.txt.in`](../packaging/INSTALL.de.txt.in)** — the single source for the text
a drag-and-drop user reads, in the two languages the mod ships. `install.ps1` renders the same two
templates, so the two install paths cannot describe the install differently. `@VERSION@` is the
only substitution. **If you change install steps, change BOTH templates, both
[`INSTALL.md`](../INSTALL.md) and [`INSTALL.de.md`](../INSTALL.de.md), and the README's Install
section together** — then run `python3 scripts/check-docs-i18n.py`, which is what tells you a
language fell behind.

## Asset bundle

```sh
scripts/build-bundles.sh      # needs Unity 2021.3.5f1
```

The Unity project lives in `unity/GloomhavenVR.Assets/`. See
[`unity/HANDS.md`](../unity/HANDS.md) and [`unity/CARD-ASSETS.md`](../unity/CARD-ASSETS.md).

The brief handed to the external 3D artist who edits the meshes and textures is
[`ASSET-GUIDE-MITWIRKENDE.md`](ASSET-GUIDE-MITWIRKENDE.md) (German — he is its only reader). It
states the contracts a change must not break: file names, paths, the 19 named hand anchors, the
per-bone axis frame, the 100x armature scale and the shared UV atlas. Read it before touching an
asset, whoever you are.

Environment work is verifiable **without a headset**: `EnvironmentsPreview.RenderAll` under
`xvfb-run` produces preview renders.

**The bundle step only PACKS.** `AssetsBuilder.BuildAll` — what `build-bundles.sh` invokes — calls
`BuildAssetBundles` and nothing else. The bakes that produce the meshes and materials it packs are
separate editor methods (`GloomhavenVR.HandsBuilder.Build`, `GloomhavenVR.BoardBuilder.Build`; see
[`prebuilt/README.md`](../prebuilt/README.md)). Re-running `BuildAll` after editing a bake's INPUTS
re-packs the OLD output, and looks exactly like the edit did nothing.

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
scripts/ci-build.sh Release        # 0 errors AND 0 warnings — TreatWarningsAsErrors is on
scripts/refactor-guard.sh check    # the umbrella gate, see below
python3 scripts/rebase-defaults.py check     # needs a tester's cfg drop, see below
python3 scripts/check-docs-i18n.py # the four player-facing docs and their German twins
```

`refactor-guard.sh check` is an umbrella: it runs **seventeen** checkers before it diffs the
compiled form — `patch-inventory.sh check`,
`check-frame-order.sh`, `check-mirrors.sh`, `check-partial-order.py`, `check-instrument-writes.py`,
`check-remote-defaults.py`, `check-wire-coverage.py`, `check-tune-fields.py`,
`check-desync-surface.py`, `check-hw-verify.py`, `check-options-coverage.py`,
`check-card-identity-mask.py`, `check-mirror-dials.py`, `check-enum-arrays.py`, `wire-tests.sh`,
`check-bundle-format.sh` and the `check-surface.py` diff. Running one of those by hand as well is
duplicated work, not extra coverage. The three lines beside it are the ones it does **not** cover.

`ci.yml` runs sixteen of those seventeen (everything but `wire-tests.sh`, which is compile-only on a
runner — see [`CI-CD.md`](CI-CD.md) §5) plus `check-refasm.py`. So the desk guard is the *stricter*
of the two, not a convenience: the surface diff runs against a stored baseline here and only against
the PR base there, and push events skip it entirely.

`check-card-identity-mask.py` is a standalone **twin** of
`tests/GloomhavenVR.WireTests/CardIdentityMaskVectors.cs`. The C# original is a pure text lint that
needs no game DLL, but it shares an executable with the golden wire vectors, which do — so it is
compile-only in CI and had never run there (review R1 F2, 2026-09-07). The twin runs everywhere.
**Change one and change the other**, and if you add a new source lint, add it to `scripts/` rather
than to the wire-test project, where CI cannot execute it. See docs/CI-CD.md §5.

`rebase-defaults.py check` compares the shipped defaults against a tester's `.cfg` drop in
`.planning/debug/default`. That directory is gitignored, so it is absent on a fresh clone and on a
runner — CI prints a notice and skips. It is a **local** gate, and the one that catches a value the
user tuned on hardware being silently overwritten by a later edit.

`check-docs-i18n.py` runs in `ci.yml` ("User-facing docs ship in English and German"). It had no
automatic caller until the 2026-09 tooling review wired it, along with seven other checkers the desk
guard ran and the workflow did not. Run it locally anyway: a red PR after the fact is a slower way
to learn that a German twin fell behind.

## Repo layout

```
GloomhavenVR.sln
├── src/GloomhavenVR.Preload/   BepInEx preloader patcher — installs the OpenXR natives and the
│                               UnitySubsystems manifest before the engine boots
├── src/GloomhavenVR/           the main BepInEx 5 plugin (net472)
│   ├── Core/                   XR bootstrap, logging, layers, module registry, scene registry
│   │   ├── Startup/            getting an OpenXR runtime up before anything touches Unity.XR
│   │   ├── Events/             the game-event bridge and the VR mode state machine
│   │   ├── Perf/               the frame budget: measuring it, and spending less of it
│   │   ├── WallFade/           the wall-fade driver (32 files) and its prop classifiers
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
│   │   ├── Board/              the shared play surface as a NETWORK object
│   │   └── Desync/             the game's own desync verdict: watching it, and not tripping it
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
| [`TESTING-P1.md`](TESTING-P1.md), [`-P2`](TESTING-P2.md), [`-P3A`](TESTING-P3A.md), [`-P3B`](TESTING-P3B.md), [`-P3C`](TESTING-P3C.md), [`-P4`](TESTING-P4.md) | per-phase hardware checklists; P1 §4 is the stage-by-stage failure-triage table. **Do not retire these:** shipping code cites P1, P2, P3B and P3C by path — P1 §4 from a user-visible error message (`Core/Startup/OpenXRBootstrap.cs`, `Preload/Patcher.cs`), the rest from source comments |
| [`CI-CD.md`](CI-CD.md) | the two GitHub workflows, the release order of operations, and why each gate sits where it does |
| [`NET-ACTION-SURFACE.md`](NET-ACTION-SURFACE.md) | hand-maintained ledger — the game types that dispatch network actions, which of them the mod patches, and a recorded verdict per patch. **Not generated:** `check-desync-surface.py generate` only *prints* candidate rows with the note column blank; the verdict and the note are human judgements and the gate checks they exist, never what they say |
| [`ASSET-GUIDE-MITWIRKENDE.md`](ASSET-GUIDE-MITWIRKENDE.md) | the brief for the external 3D artist who edits the meshes and textures; file names, paths and bone/anchor names are contracts. German on purpose — it has exactly one reader and he works in German |
| [`PLAYING.md`](PLAYING.md) / [`PLAYING.de.md`](PLAYING.de.md) | player-facing; listed here so you know to change both and to run `check-docs-i18n.py` after |
| [`img/README.md`](img/README.md) | how every image and clip in the README was produced |
| [`VIDEO-SHOTLIST.md`](VIDEO-SHOTLIST.md) | the clips that still have to be recorded — one row each, with the target filename and the spot in the docs that is already prepared for it |
| [`../.planning/`](../.planning/) | STATE.md, architecture, roadmap, game-API research |

## Licence

GPL-3.0 — required and embraced, since the project adapts patterns and code from the GPL-3.0 mods
credited in the README. The mod distributes only its own code and self-authored or licensed assets:
never game files, game assets or decompiled sources.

The hand meshes are **this project's own work** — three authored pairs (`VRHand`, `VRHandPlate`,
`VRHandArcane`), built by the Blender pipeline in `unity/hand-prep/`. The early plan to ship Valve's
BSD-3-Clause SteamVR gloves was **not** taken, and no Valve asset is in the bundle. The third-party
art the bundle does carry is listed in
[`packaging/THIRD-PARTY.txt`](../packaging/THIRD-PARTY.txt), which the packager copies in beside the
bundle.
