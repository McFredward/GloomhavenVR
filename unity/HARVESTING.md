# Harvesting the XR runtime set (Unity 2021.3 → `libs/`)

> Alternative editor-harvest workflow retained for investigation. Current CI/release builds
> use the pinned source packages in `scripts/build-runtimedeps.sh` and native downloads in
> `scripts/fetch-natives.sh`; see [DEVELOPING](../docs/DEVELOPING.md). The output below describes
> the original harvest design, not the current shipped DLL inventory.

> Goal: produce the **one coherent set** of XR binaries the mod ships
> (TOOLCHAIN.md §5.2, risk R5): managed `Unity.XR.*` DLLs, native
> `UnityOpenXR.dll` + `openxr_loader.dll`, and a version-matched
> `UnitySubsystemsManifest.json`. Total hands-on time on a Windows machine:
> **≈ 25–30 min** (most of it is the editor download).

## Output (what "done" looks like)

```
<repo>/libs/
├── RuntimeDeps/
│   ├── Unity.XR.Management.dll            # 4.5.0
│   ├── Unity.XR.OpenXR.dll                # 1.10.0
│   ├── Unity.XR.CoreUtils.dll             # 2.2.3
│   ├── Unity.XR.Interaction.Toolkit.dll   # 2.6.5
│   ├── Unity.InputSystem.dll              # 1.7.0 (see "Package versions" below)
│   ├── UnityEngine.SpatialTracking.dll    # optional (game ships its own)
│   ├── Unity.XR.OpenXR.Features.*.dll     # optional (MockRuntime etc., dev aids)
│   └── harvest-manifest.json              # versions + editor + build date
└── Natives/
    ├── Plugins/x86_64/
    │   ├── UnityOpenXR.dll
    │   └── openxr_loader.dll
    └── UnitySubsystems/UnityOpenXR/
        └── UnitySubsystemsManifest.json   # "version" == OpenXR package version
```

Binaries are **never committed** (root `.gitignore` has `*.dll`);
`harvest-manifest.json` may be committed as the version record.

## Editor version rule (read before installing)

Use **Unity 2021.3.5f1**, the exact game editor. Later 2021.3.x editors have produced
incompatible UnityFS wrappers and shaders in hardware tests. Being in the same LTS family
does not make them interchangeable. Stripping or rewriting the version header cannot repair
those shaders. See [prebuilt/README.md](../prebuilt/README.md).

## Option A — dummy build + harvest (alternative path)

### 1. Install Unity Hub (~3 min)

https://unity.com/download → install Unity Hub, sign in with a (free) Unity ID,
accept the Personal license.

### 2. Install the editor (~10–15 min, mostly download)

Paste a deep link into a browser (Hub picks it up), or use Hub → Installs →
Install Editor → Archive:

| Version | Hub deep link |
|---|---|
| 2021.3.5f1 (game-exact) | `unityhub://2021.3.5f1/40eb3a945986` |

(Download archive page: https://unity.com/releases/editor/archive)

**In the module selection, tick "Windows Build Support (Mono)"** — on a
Windows machine this is usually implicit for the default install, but verify:
the harvest does a real Windows **Mono** player build. No other modules needed.

### 3. Open the companion project (~5 min first import)

Hub → Projects → Add → select `<repo>/unity/GloomhavenVR.Assets` → open with
the 2021.3.x editor. First open resolves packages and imports; let it finish.

Expected after open:
- Console shows no compile errors; menu **GloomhavenVR** exists in the menu bar.
- Package Manager shows: XR Plugin Management 4.5.0, OpenXR Plugin 1.10.0,
  XR Interaction Toolkit 2.6.5, XR Core Utilities 2.2.3, **Input System 1.7.0**.
- Unity may rewrite `ProjectSettings/*.asset` (we committed minimal stubs) and
  generate `.meta` files — **commit those regenerated files once**.
- Check Edit → Project Settings → Player → Other Settings → Color Space =
  **Linear** (stub sets it; verify it survived).

#### Package versions — why Input System shows 1.7.0, not 1.3.0

`Packages/manifest.json` pins `com.unity.inputsystem: 1.3.0` (the game's
version), but UPM resolves to the highest requested version:
XRIT 2.6.5 requires `com.unity.inputsystem >= 1.7.0` (and OpenXR 1.10.0
requires >= 1.6.3), so the project — and therefore the harvested
`Unity.InputSystem.dll` — is **1.7.0**. This is intentional
(harvest "Unity.InputSystem if newer"): the OpenXR plugin and XRIT are
compiled against 1.6+/1.7 APIs, so the mod will likely have to upgrade the
game's `Unity.InputSystem.dll` (1.3.0 → 1.7.0, a superset; the game's own
input code keeps working) via the preloader. Final decision lands in Phase 1/2.

*Fallback if we ever need to stay on the game's exact 1.3.0:* pin
`com.unity.xr.interaction.toolkit: 2.2.0` — the last XRIT that depends on
exactly `com.unity.inputsystem 1.3.0` (verified on needle-mirror). Costs the
2.3+ fixes/poke-interactor; our poke primitive is hand-rolled anyway
(ARCHITECTURE.md §4). Note OpenXR 1.10.0 still pulls 1.6.3 — so this fallback
also requires dropping to an older OpenXR; treat it as last resort.

### 4. Run the harvest (~3 min)

Editor menu: **GloomhavenVR → Harvest RuntimeDeps (dummy build + collect)**.

Or headless (PowerShell, from the repo root):

```powershell
& "C:\Program Files\Unity\Hub\Editor\2021.3.45f2\Editor\Unity.exe" `
  -batchmode -nographics `
  -projectPath "$PWD\unity\GloomhavenVR.Assets" `
  -buildTarget Win64 `
  -executeMethod GloomhavenVR.RuntimeDepsHarvester.BuildAndHarvest `
  -logFile "$PWD\harvest.log"
```

(or `UNITY_PATH=... ./scripts/build-bundles.sh harvest` from Git Bash.)

What it does: forces managed stripping OFF, builds a throwaway empty-scene
Windows Mono player to `unity/GloomhavenVR.Assets/Build/DummyPlayer/`, copies
the managed XR DLLs from the player's `Managed/`, copies the natives from the
resolved `com.unity.xr.openxr` package cache (`Runtime/windows/x64/` and
`RuntimeLoaders/windows/x64/` — the build only ever copies these same files),
writes `UnitySubsystemsManifest.json` with `version` set to the resolved
OpenXR package version, and records everything in `harvest-manifest.json`.

### 5. Verify

- `libs/RuntimeDeps/` and `libs/Natives/` match the tree at the top.
- `libs/RuntimeDeps/harvest-manifest.json` says `unityVersion` = your editor,
  `packages` = 4.5.0 / 1.10.0 / 2.2.3 / 2.6.5 / 1.7.0.
- `libs/Natives/UnitySubsystems/UnityOpenXR/UnitySubsystemsManifest.json` has
  `"version": "1.10.0"`.

### 6. Build the asset bundle (~1 min)

Editor menu: **GloomhavenVR → Build AssetBundles**, or
`UNITY_PATH=... ./scripts/build-bundles.sh` (default job).
Output: `unity/GloomhavenVR.Assets/Build/Bundles/gloomhavenvr.bundle`
(plus Unity's `Build/Bundles/Bundles` manifest bundle, not shipped).

### Troubleshooting

| Symptom | Fix |
|---|---|
| `BuildPlayer` fails "No valid Unity Editor license / target not supported" | Install the **Windows Build Support (Mono)** module for that editor in Hub |
| `Required managed assembly missing: Unity.XR.*.dll` | Packages didn't resolve — open the project interactively once, check Package Manager, then re-run |
| Natives "not found in package" | `Library/PackageCache` was pruned mid-run; reopen the project (forces re-resolve) and use "collect only" menu item |
| Bundle loads but materials pink in-game | Expected for built-in-shader materials (TOOLCHAIN §4.1) — reassign game materials at runtime; ship custom shaders inside the bundle for own materials |
| Editor hangs in batch mode | Check the `-logFile`; commonly a license prompt — run the editor once interactively |

## Option B — prebuilt donor set (researched 2026-07: **no viable donor**)

We looked for an existing open-source VR mod that ships a GPL-compatible,
**Unity 2021.3-built** OpenXR runtime set we could reuse. Result:

| Candidate | Game engine version | Verdict |
|---|---|---|
| [LCVR](https://github.com/DaXcess/LCVR) (GPL-3.0) | Unity **2022.3.9f1** | RuntimeDeps built on 2022.3 — wrong editor line, unusable |
| [CWVR](https://github.com/DaXcess/CWVR) (GPL-3.0) | Unity **2022.3.10f1** | same |
| [RepoXR](https://github.com/DaXcess/RepoXR) (GPL-3.0) | Unity **2022.3.21f1** | same (but its OpenXR **1.10.0** choice is what we mirror) |
| [DredgeVR](https://github.com/xen-42/DredgeVR) (MIT) | 2021-era Unity | ships the **OpenVR/SteamVR** path (`XRSDKOpenVR.dll` + `openvr_api.dll` + SteamVR plugin), no OpenXR set to borrow |
| [UUVR](https://github.com/Raicuparta/uuvr) (GPL-3.0) | universal | vendors **renamed** recompiled assemblies (`Uuvr.XR.Management` etc.) — not drop-in `Unity.XR.*` names |
| [UnityVRMod](https://github.com/NewUnityModder/UnityVRMod) (GPL-3.0) | universal | bypasses Unity XR packages entirely (talks to runtimes directly) — nothing to harvest |

**Conclusion: Option A (own dummy build) is required for the managed DLLs.**

### Partial shortcut that IS valid: natives straight from the package

`UnityOpenXR.dll` and `openxr_loader.dll` are **prebuilt inside the
com.unity.xr.openxr package** — no Unity editor compiles them. They can be
fetched version-exact without any Unity install (this is also exactly where
the harvest script takes them from):

- https://github.com/needle-mirror/com.unity.xr.openxr/raw/1.10.0/Runtime/windows/x64/UnityOpenXR.dll
- https://github.com/needle-mirror/com.unity.xr.openxr/raw/1.10.0/RuntimeLoaders/windows/x64/openxr_loader.dll

(or the registry tarball
`https://download.packages.unity.com/com.unity.xr.openxr/-/com.unity.xr.openxr-1.10.0.tgz`,
same files under `package/`.)

The **managed** package DLLs cannot be shortcut this way — packages ship them
as C# source that the editor compiles per Unity version.

**License note:** Unity packages (incl. these binaries) are under the
[Unity Companion License](https://unity.com/legal/licenses/unity-companion-license),
which permits distributing them as part of projects made with Unity; they
remain separate, non-GPL files aggregated alongside our GPL-3.0 mod. This is
established practice — every DaXcess mod redistributes the same files in its
release zips/Thunderstore packages. (The OpenXR loader inside Unity's package
is Unity's build of Khronos' Apache-2.0 loader.)
