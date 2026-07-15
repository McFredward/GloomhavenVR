# tools/RuntimeDepsBuild — provisional XR RuntimeDeps compile

Builds `libs/RuntimeDeps/{Unity.XR.Management,Unity.XR.CoreUtils,Unity.XR.OpenXR}.dll`
from needle-mirror package source **outside Unity**, so the mod can compile and
iterate before the human Unity-editor harvest (`unity/HARVESTING.md`) produces
the runtime-blessed set (drop-in replacement, same file names).

Run: `scripts/build-runtimedeps.sh` (fetches pinned tags into `sources/`
— gitignored — then `dotnet build`s the three projects and collects outputs +
`versions.json` into `libs/RuntimeDeps/`).

## What is compiled

Only each package's `Runtime/**/*.cs` — exactly the file set of the package's
runtime asmdef:

| Project | Source | asmdef mirrored |
|---|---|---|
| `Unity.XR.Management/` | `com.unity.xr.management@4.5.0` `Runtime/` | `Unity.XR.Management` |
| `Unity.XR.CoreUtils/` | `com.unity.xr.core-utils@2.2.3` `Runtime/` | `Unity.XR.CoreUtils` |
| `Unity.XR.OpenXR/` | `com.unity.xr.openxr@1.10.0` `Runtime/` | `Unity.XR.OpenXR` |

`Editor/`, `Tests/`, `Samples~/` and the OpenXR feature-group asmdefs
(`MockRuntime/`, `MetaQuest/`, `OculusQuest/`, `RuntimeDebugger/`,
`ConformanceAutomation/`) are not built. The interaction profiles the mod
needs (`OculusTouchControllerProfile`, `ValveIndexControllerProfile`,
`KHRSimpleControllerProfile`) live in the main `Unity.XR.OpenXR` assembly
(`Runtime/Features/Interactions/`), verified against tag 1.10.0.

**File exclusions: none.** All three Runtime trees compile unmodified with the
define set below (editor-only code in Runtime files is `#if UNITY_EDITOR`-gated
and compiles away).

## Define constants (see `Common.props` for the full rationale)

Replicates a Unity **2021.3.5f1 / Windows Standalone / Mono player** build with
the game's module+package set: version-ladder defines up to `UNITY_2021_3`,
`UNITY_STANDALONE(_WIN)`, `NET_4_6`, `ENABLE_VR`, `ENABLE_XR_MODULE`,
`ENABLE_INPUT_SYSTEM`, `UNITY_ANALYTICS`/`ENABLE_CLOUD_SERVICES_ANALYTICS`, and
per-asmdef versionDefines evaluated against the game's versions
(`INCLUDE_PHYSICS_MODULE`, `INCLUDE_UGUI`, `INCLUDE_INPUT_SYSTEM`,
`INCLUDE_LEGACY_INPUT_HELPERS`).

Deliberately **off** (and why):

- `INPUT_SYSTEM_POSE_VALID` / `INPUT_SYSTEM_BINDING_VALIDATOR` /
  `USE_INPUT_SYSTEM_POSE_CONTROL` — require InputSystem ≥ 1.4.2/1.6.3; the mod
  compiles against the **game's** InputSystem 1.3.0 so OpenXR uses its own
  `UnityEngine.XR.OpenXR.Input.PoseControl`. (An editor harvest per
  HARVESTING.md resolves InputSystem 1.7.0 and flips these on — that set also
  ships the newer InputSystem; decision deferred to Phase 2.)
- `HAS_SET_LOCAL_POSITION_AND_ROTATION` / `HAS_GET_POSITION_AND_ROTATION` —
  core-utils fast paths added in 2021.3.11/2021.3.17; game is 2021.3.5f1.
- `USE_STICK_CONTROL_THUMBSTICKS` — opt-in scripting define normally added by
  OpenXR project validation; not a player default.
- `UNITY_EDITOR*`, test/docs defines.

## References

- `UnityEngine.Modules 2021.3.5` (nuget, engine reference assemblies)
- Game DLLs from `$(GameManaged)`: `Unity.InputSystem.dll` (1.3.0),
  `UnityEngine.SpatialTracking.dll`, `UnityEngine.UI.dll` — so
  `Directory.Build.props.user` must be configured (see repo README).

## Fidelity notes

- AssemblyVersion is `0.0.0.0` — exactly what Unity stamps on asmdef
  assemblies without an explicit `[AssemblyVersion]` (verified against the
  game's own `Unity.TextMeshPro.dll` etc.). Package version is recorded in
  FileVersion/InformationalVersion instead.
- Needle-mirror gotcha handled in the fetch script: these repos have
  **branches with version-like names** pointing at different snapshots than
  the same-named **tags** (e.g. core-utils branch `2.2.3` = 2.3.0 content).
  Tags are fetched explicitly via `refs/tags/<tag>` and cross-checked against
  `package.json`.
