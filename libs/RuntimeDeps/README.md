# libs/RuntimeDeps — managed XR assemblies (NOT committed)

Managed `Unity.XR.*` assemblies the plugin compiles against and the mod ships
(loaded at runtime via `Assembly.LoadFile` from
`BepInEx/plugins/GloomhavenVR/RuntimeDeps/`). Only this README, `.gitkeep`
and nothing else are tracked (`*.dll` and `versions.json` are gitignored).

## Two ways to populate this directory

### 1. Provisional set — `scripts/build-runtimedeps.sh` (works today, no Unity needed)

Compiles the **Runtime/** portion of the pinned packages from needle-mirror
source (see `tools/RuntimeDepsBuild/README.md` for exact defines/exclusions):

| Assembly | Package | Tag |
|---|---|---|
| `Unity.XR.Management.dll` | `com.unity.xr.management` | 4.5.0 |
| `Unity.XR.CoreUtils.dll` | `com.unity.xr.core-utils` | 2.2.3 |
| `Unity.XR.OpenXR.dll` | `com.unity.xr.openxr` | 1.10.0 |

AssemblyName + AssemblyVersion (0.0.0.0) match what Unity produces for these
asmdefs; `versions.json` records tags, SHA256 and build date.

**Status: provisional.** These are compiled with the .NET SDK against
`UnityEngine.Modules 2021.3.5` reference assemblies and the game's
`Unity.InputSystem.dll` 1.3.0 — good enough to develop and compile the mod,
and expected to work at runtime (they are plain C# with no native parts),
but they are NOT editor-blessed.

### 2. Harvested set — dummy Unity 2021.3.x editor build (runtime-blessed, replaces #1)

`unity/HARVESTING.md` documents the human step: a throwaway Windows Mono
player build of `unity/GloomhavenVR.Assets` produces the same three DLLs
(plus XRIT/InputSystem for Phase 2+) exactly as Unity compiles them.
Drop them in here — same file names, straight replacement. If runtime
behavior ever differs between the two sets, the harvested set wins.

## Version coherence (TOOLCHAIN.md risk R5)

The OpenXR *managed* assembly version here (1.10.0), the *native*
`UnityOpenXR.dll`/`openxr_loader.dll` in `libs/Natives/` and the
`UnitySubsystemsManifest.json` version written by the preloader must always
come from the same package version. Bump all three together or none.
