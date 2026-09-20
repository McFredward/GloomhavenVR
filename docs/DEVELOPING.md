# Developing GloomhavenVR

Build, test and contribution reference. For player instructions, see the [README](../README.md).
The [documentation index](README.md) separates current guides from historical phase notes.

> Read [AGENTS.md](../AGENTS.md), [CLAUDE.md](../CLAUDE.md) and
> [STATE.md](../.planning/STATE.md) before implementation. They define the workflow, technical
> contracts, current integration state and outstanding hardware validation. Per-build changes
> are recorded beside `NetProtocol.ModBuild`; phase documents preserve historical design evidence.

## What it is, technically

A BepInEx 5 plugin (net472, Unity Mono) plus a BepInEx **preloader patcher**, Harmony-patched with
HarmonyX against Gloomhaven (digital) v1.1.8307.0 on Unity 2021.3.5f1. No game file is modified
except two `key=value` lines in `GH_Data/boot.config` (graphics jobs — the preloader keeps a
backup). The preloader also installs its OpenXR libraries under `GH_Data/Plugins/x86_64/`
and its subsystem manifest under `GH_Data/UnitySubsystems/UnityOpenXR/`. Plugin files and
settings live under `BepInEx/`.

### The restart on the first start

Unity reads the graphics-jobs flags out of `GH_Data/boot.config` **while it is starting up**, so the
run that switches them on can never be the run that benefits. When enabling those flags, the
preloader writes the two lines, keeps the original as `boot.config.gloomhavenvr-backup`, and requests a bounded restart.
An install whose flags are already enabled does not need another graphics-jobs restart.
This happens before a save is loaded; hardware results depend on the scene and machine.

Two ways to skip the restart, both in `BepInEx/config/dev.gloomhavenvr.cfg`:

| Setting | Effect |
|---|---|
| `[Core] AutoRestartForGraphicsJobs = false` | the file is still written; the log asks you to restart the game yourself |
| `[Core] EnableGraphicsJobs = false` **plus** `-force-gfx-jobs native` in the game's launch options | works from the very first start and writes no game file at all |

### Controller models in the tutorial

The in-headset tutorial selects its controller model and button names through
`Compat/Tutorial/Controls/ControllerVisual.cs`. Its device table uses a generic model when no
matching bundled model exists, including the Steam Frame mapping. Check that table before
adding a device-specific illustration.

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

### Test the updater against a published release

Build and deploy the current checkout while stamping only its runtime semantic version as an older
one:

```powershell
.\scripts\install.ps1 -GamePath "C:\...\Gloomhaven" -FakeVersion 0.9.0
```

`-FakeVersion` accepts only `MAJOR.MINOR.PATCH`. It passes that value and the release-build marker
to MSBuild without changing the checkout, so the updater uses its normal published-release path.
It leaves the source `ModBuild` intact and automatically skips release-ZIP packaging. Use a version
below the public latest release, start the game in single-player and wait for the main menu. This is
for updater testing only; do not join VR multiplayer with that installation.

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
BepInEx/plugins/GloomhavenVR/ghvr-town.bundle
BepInEx/plugins/GloomhavenVR/THIRD-PARTY.txt
BepInEx/plugins/GloomhavenVR/LICENSE.txt
BepInEx/plugins/GloomhavenVR/Licenses/*.txt
BepInEx/patchers/GloomhavenVR/GloomhavenVR.Preload.dll
BepInEx/patchers/GloomhavenVR/Natives/*.dll
```

`THIRD-PARTY.txt` ships only when the bundle does — it is the licence notice the bundled art
requires, and it has to travel with the copies. `LICENSE.txt` carries the mod's GPL text;
`Licenses/` carries the pinned XR dependency notices and their source/provenance list.

**The asset bundles are required for a full installation.** The original bank contains hands,
control boards, card backing, map tables, head avatars, environments and controller models; without the
file the mod starts, logs an Alert per subsystem and degrades to procedural placeholders everywhere.
Both installation and packaging use the committed `prebuilt/gloomhavenvr.bundle` by default.
The separate `prebuilt/ghvr-town.bundle` contains town NPCs, their rigs, stations and work trays.
It is required by the installer and packager. An incomplete runtime installation retains the
original service windows and reports the missing town assets once per attempted opening.
An old ignored Unity output must not silently override the reviewed assets. To test a newly
built local bundle, use `GHVR_USE_LOCAL_BUNDLE=1 bash scripts/package-release.sh` or
`.\scripts\install.ps1 -UseLocalBundle`; explicit local selection fails if that file is missing.
After asset validation, copy the new bundle into `prebuilt/` and commit it for the release.
If the default committed bundle is missing, the script warns on stderr and ships
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
scripts/build-bundles.sh town # independently packs authored town assets, same editor
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
scripts/refactor-guard.sh check --summary # umbrella gate; create a baseline before editing
python3 scripts/check-docs-i18n.py # the four player-facing docs and their German twins
```

`refactor-guard.sh check` is an umbrella: it runs **seventeen** checkers before it diffs the
compiled form — `patch-inventory.sh check`,
`check-frame-order.sh`, `check-mirrors.sh`, `check-partial-order.py`, `check-instrument-writes.py`,
`check-remote-defaults.py`, `check-wire-coverage.py`, `check-tune-fields.py`,
`check-desync-surface.py`, `check-hw-verify.py`, `check-options-coverage.py`,
`check-card-identity-mask.py`, `check-mirror-dials.py`, `check-enum-arrays.py`, `wire-tests.sh`,
`check-bundle-format.sh` and the `check-surface.py` diff. Running one of those by hand as well is
duplicated work, not extra coverage. The strict build and bilingual-docs checks must also be run
explicitly. The guard needs a local baseline (`scripts/refactor-guard.sh baseline`, taken before
editing); exit 1 means compiled output differs, so inspect its verdict and explain the changes.

Full hosted CI runs the source checks and standalone native presentation harnesses on every
dev push and manual run. Internal PRs may reuse successful full-dev evidence for an identical
merged Git tree; fork PRs always run full checks. Release requires exact-tree evidence and then
builds and packages fresh from main, without repeating the test suite. The full wire executable
is compile-only on hosted runners because it needs the game's real Unity assembly; the surface
comparison runs on every PR, including reuse. The local guard covers both.
See [CI-CD.md](CI-CD.md#3-verification-coverage) for the coverage and limits.

`bash scripts/tutorial-scope-tests.sh` exercises first-tutorial admission and native message-hold
lifetime, with mutation controls and bindings to lesson entry/cleanup paths. It runs through
the local wire-test umbrella and both hosted presentation suites.

`check-card-identity-mask.py` is a standalone **twin** of
`tests/GloomhavenVR.WireTests/CardIdentityMaskVectors.cs`. The C# original is a pure text lint that
needs no game DLL, but it shares an executable with the golden wire vectors, which do — so it is
compile-only in CI and had never run there (review R1 F2, 2026-09-07). The twin runs everywhere.
**Change one and change the other**, and if you add a new source lint, add it to `scripts/` rather
than to the wire-test project, where CI cannot execute it. See docs/CI-CD.md §5.

`rebase-defaults.py check` compares the shipped defaults against a tester's `.cfg` drop in
`.planning/debug/default`. That directory is gitignored, so it is absent on a fresh clone and on a
runner — CI prints a notice and skips. Run it locally when a tester drop is available. It catches
drift from hardware tuning; explicit pins preserve intentional shipped defaults. Investigate unresolved entries without
blindly replacing user-approved defaults.

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
│   │   ├── WallFade/           the wall-fade driver and its prop classifiers
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
│   │   ├── Driver/             the interaction driver (laser, rebuild, flows)
│   │   ├── Tray/               the physical play tray
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
├── libs/                       RefAsm/ committed metadata; Natives/ + RuntimeDeps/ built locally
├── prebuilt/                   the reviewed asset bundle used by default for installs and packages
├── tools/RuntimeDepsBuild/     provisional RuntimeDeps compile from needle-mirror source
├── packaging/INSTALL.txt.in    the template for the zip's INSTALL.txt
├── scripts/                    build, install, packaging, bundle and verification scripts
├── tests/GloomhavenVR.WireTests/  byte-exact wire vectors and accompanying lints (real Unity DLL)
├── tests/GloomhavenVR.CardBindingsTests/  production card capture with controlled Unity substitutes
├── tests/GloomhavenVR.NativePlaybackTests/  production native presentation playback
├── tests/GloomhavenVR.BoardRefreshTests/  production section refresh and invalidation
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

See [docs/README.md](README.md) for current guides, source-checked ledgers and historical
phase references. Historical test files retain their paths because shipping diagnostics and
source comments refer to them; they are not a complete current release checklist.

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
