# TOOLCHAIN — GloomhavenVR (BepInEx VR mod for Gloomhaven digital)

> Research date: 2026-07-14/15. Local inspection of `ressources/Managed/` + web research
> (all load-bearing versions/URLs verified against primary sources: GitHub releases/APIs,
> needle-mirror package.json files, nuget feeds, PCGamingWiki API, Steam announcements).
>
> Contents: §1 engine facts · §2 BepInEx · §3 scaffolding · §4 assets · §5 XR enablement ·
> §6 Quest 3/OpenXR · §7 legal/multiplayer · §8 risks

## 1. Game engine facts (verified locally)

| Fact | Value | Evidence |
|---|---|---|
| Engine | **Unity 2021.3 LTS line** (2021.2 possible, 2021.3 by far most likely) | Module fingerprinting, see below |
| Scripting backend | **Mono** (not IL2CPP) | `Managed/` full of decompilable .NET DLLs; `mscorlib.dll` ProductName "Mono Common Language Infrastructure", FileVersion 4.6.57.0 (Unity Mono fork) |
| API compatibility level | **.NET Framework** ("4.x" profile) | Full `System.*` set incl. `System.EnterpriseServices`, `System.Configuration`, `System.ServiceModel.Internals`, `System.Drawing`; `mscorlib` AssemblyVersion **4.0.0.0**; `netstandard.dll` 2.1.0.0 facade also present |
| Main game assemblies | `GH.Runtime.dll`, `GH.Runtime.FirstPass.dll`, `GH.Shared.dll`, `ScenarioRuleLibrary.dll`, `MapRuleLibrary.dll`, `SharedLibrary.dll` (no `Assembly-CSharp.dll`) | dir listing; ILSpy-decompiled to `decompiled/` |
| Deterministic builds | PE timestamps are reproducible-build hashes | pefile `FILE_HEADER.TimeDateStamp` out of epoch range |

### Unity version fingerprint (local evidence)

The build ships these engine modules (`UnityEngine.*Module.dll`):

- `NVIDIAModule` (DLSS) — **added in Unity 2021.2** → engine ≥ 2021.2
- `TextCoreFontEngineModule` + `TextCoreTextEngineModule` (split TextCore) — 2021.x naming
- `UIElementsNativeModule` — exists only up to 2021.x (merged into UIElementsModule in 2022.1) → engine < 2022.1
- `UnityEngine.Pool.*` (12 classes) in CoreModule — added 2021.1
- `Texture2D.Reinitialize` (renamed from `Resize`) — 2021.2+
- No `UnityEngine.Awaitable` → not 2023.x

Package DLL versions (PE version resources / embedded constants):

- `Unity.InputSystem.dll` = **1.3.0** (`InputSystem.kAssemblyVersion = "1.3.0"`) — the verified/default version for **2021.3 LTS**
- `Newtonsoft.Json.dll` = 13.0.1 (13.0.102 file string)
- Cinemachine, PostProcessing v2 (built-in pipeline), Addressables + ResourceManager, TextMeshPro, Timeline, Burst, Mathematics, Odin Serializer, SRDebugger (StompyRobot) present
- Networking: `PhotonBolt.dll`, `Photon3Unity3D.dll`, `PhotonRealtime.dll`, `PhotonVoice`, `udpkit.*`
- Input: **InControl** (21 GH.Runtime source files reference it) **and** Unity InputSystem (16 files); legacy `UnityEngine.Input` unused (0 files)

### 1b. Exact version (web cross-check)

**Unity 2021.3.5f1** — [PCGamingWiki Gloomhaven page](https://www.pcgamingwiki.com/wiki/Gloomhaven) lists engine build `2021.3.5f1` (wikitext via [PCGW API](https://www.pcgamingwiki.com/w/api.php?action=parse&page=Gloomhaven&prop=wikitext&format=json)); fully consistent with every local fingerprint above (InputSystem 1.3.0 is the 2021.3-bundled version). SteamDB app 780290 would confirm `unityversion` but 403s anonymous fetches. Note: the engine was upgraded at least once during the game's life (early builds were older Unity), so old forum posts mentioning other versions refer to pre-final patches. Executable is `GH.exe`, data folder `Gloomhaven_Data` (confirmed by [Leopard2ARC/GloomhavenMod](https://github.com/Leopard2ARC/GloomhavenMod) install instructions).

**Scripting runtime:** Unity Mono (Unity's Mono fork, mscorlib FileVersion 4.6.57.0), API compatibility level **.NET Framework** → plugin TFM `net472` (§3).

### XR state of the shipped build (critical)

Present in `Managed/`:

- `UnityEngine.XRModule.dll` — **XR SDK infra is compiled in**: `XRDisplaySubsystem`, `XRInputSubsystem`, `XRMeshSubsystem`, `InputDevices`, `CommonUsages`, `InputTracking`
- `UnityEngine.SubsystemsModule.dll` — subsystem manager (needed by XR plugins)
- `UnityEngine.VRModule.dll` — legacy surface: `XRSettings` (incl. `LoadDeviceByName`), `XRDevice`
- `UnityEngine.SpatialTracking.dll` — **`TrackedPoseDriver` ships with the game** (com.unity.xr.legacyinputhelpers runtime)
- `UnityEngine.XR.LegacyInputHelpers.dll`

**Absent** (must be shipped by the mod):

- `Unity.XR.Management.dll` (`UnityEngine.XR.Management` — `XRGeneralSettings`, `XRManagerSettings`, `XRLoader`)
- Any XR provider plugin (no `UnityEngine.XR.OpenXR*.dll`, no `Unity.XR.OpenVR.dll`, no `UnityOpenXR.dll`/`openxr_loader.dll` natives, no `UnitySubsystems/` manifest folder)

Conclusion: legacy `XRSettings.LoadDeviceByName("OpenVR")` is a dead end (API exists, but built-in VR device backends were removed from the engine in Unity 2020.1). The mod must bootstrap **XR Plugin Management + a provider (OpenXR)** at runtime — the LCVR/UUVR approach. Details in §5.

## 2. BepInEx choice + config

### 2.1 Version: BepInEx 5.4.23.5 (LTS)

- **Use BepInEx 5 LTS, latest stable 5.4.23.5 (2026-02-08)**, asset `BepInEx_win_x64_5.4.23.5.zip` — [releases](https://github.com/BepInEx/BepInEx/releases), [v5.4.23.5](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5) (ships Doorstop 4.5.0). BepInEx 5 is in long-term-support mode on the `v5-lts` branch and is the standard for **all Unity Mono** games.
- **BepInEx 6 is still pre-release** (latest 6.0.0-pre.2, 2024-08-27; bleeding-edge be.785 builds continue) and its own release notes say: *"If you want to mod new Unity Mono games: Consider using BepInEx 5!"* — v6 is only recommended for IL2CPP. BepInEx 5 plugins don't load on v6. ([v6.0.0-pre.2 notes](https://github.com/BepInEx/BepInEx/releases/tag/v6.0.0-pre.2))
- Gloomhaven precedent: BepInEx 5 + Harmony is **proven on this exact game** — [Leopard2ARC/GloomhavenMod](https://github.com/Leopard2ARC/GloomhavenMod) (active, Nov 2025), plus ~8 BepInEx-based mods on [Nexus](https://www.nexusmods.com/games/gloomhaven/mods) ("Camera Tweaks", "UI Tweaks", "Bug Fixes"…).

### 2.2 Recommended `BepInEx.cfg`

```ini
[Chainloader]
HideManagerGameObject = true   ; sets HideAndDontSave on the BepInEx_Manager GO so game code
                               ; iterating root/DontDestroyOnLoad objects can't see/kill it;
                               ; also the standard fix for UnityExplorer/RUE hotkeys not working

[Logging.Console]
Enabled = true                 ; live load progress + errors (docs' first troubleshooting step)
```

Sources: [Chainloader.cs](https://raw.githubusercontent.com/BepInEx/BepInEx/v5-lts/BepInEx/Bootstrap/Chainloader.cs), [troubleshooting docs](https://docs.bepinex.dev/articles/user_guide/troubleshooting.html), [RuntimeUnityEditor README](https://github.com/ManlyMarco/RuntimeUnityEditor). Disk log: `BepInEx/LogOutput.log`.

### 2.3 Injection / Proton

- Doorstop `winhttp.dll` proxy next to `GH.exe` + `doorstop_config.ini` ([install docs](https://docs.bepinex.dev/articles/user_guide/installation/index.html)).
- Under Proton/Wine the system winhttp wins unless overridden: Steam launch option **`WINEDLLOVERRIDES="winhttp=n,b" %command%`** (or protontricks winecfg override) — [official Proton/Wine page](https://docs.bepinex.dev/articles/advanced/proton_wine.html), [discussion](https://github.com/BepInEx/BepInEx/discussions/1168). (For VR dev the game will realistically run on Windows anyway — SteamVR/OpenXR under Proton for a Windows-process game is unsupported territory.)

## 3. Dev project scaffolding

### 3.1 Template + packages

```bash
dotnet new install BepInEx.Templates::2.0.0-be.4 --nuget-source https://nuget.bepinex.dev/v3/index.json
dotnet new bepinex5plugin -n GloomhavenVR -T net472
```

([official tutorial](https://docs.bepinex.dev/articles/dev_guide/plugin_tutorial/1_setup.html), [BepInEx.Templates](https://github.com/BepInEx/BepInEx.Templates))

**TFM: `net472`.** BepInEx docs rule: netstandard2.1 for Unity 2021.2+ *or* `net472` when the game ships the full framework profile / you hit reference errors ([docs](https://docs.bepinex.dev/master/articles/dev_guide/plugin_tutorial/2_plugin_start.html)). Gloomhaven ships the full `System.*` set (.NET Framework API level, mscorlib 4.0.0.0) → `net472`. Cross-platform/CI builds need `Microsoft.NETFramework.ReferenceAssemblies` (the template adds it conditionally).

Package versions (verified on feeds, 2026-07):

| Package | Version | Feed |
|---|---|---|
| `BepInEx.Core` | `5.*` (latest 5.4.21 — API frozen; binary-compatible with 5.4.23.x runtime) | nuget.bepinex.dev |
| `BepInEx.PluginInfoProps` | `2.*` | nuget.bepinex.dev |
| `BepInEx.Analyzers` | `1.*` (latest 1.0.8, `PrivateAssets="all"`) | nuget.bepinex.dev |
| `UnityEngine.Modules` | **`2021.3.5`** (exact game version; `IncludeAssets="compile"`) | nuget.bepinex.dev |
| `BepInEx.AssemblyPublicizer.MSBuild` | 0.4.3 (`PrivateAssets="all"`) | nuget.org |
| HarmonyX | don't reference directly — use the `0Harmony.dll` pulled in by `BepInEx.Core` (HarmonyX lineage; standalone nuget latest 2.16.1) | — |

### 3.2 Concrete csproj

```xml
<!-- Directory.Build.props -->
<Project>
  <PropertyGroup>
    <!-- override per-machine via env var or Directory.Build.props.user -->
    <GamePath Condition="'$(GamePath)' == ''">C:\Program Files (x86)\Steam\steamapps\common\Gloomhaven</GamePath>
    <GameManaged>$(GamePath)\Gloomhaven_Data\Managed</GameManaged>
  </PropertyGroup>
</Project>
```

```xml
<!-- GloomhavenVR.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
    <AssemblyName>GloomhavenVR</AssemblyName>
    <Version>0.1.0</Version>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <DebugType>embedded</DebugType>              <!-- readable stack traces in LogOutput.log -->
    <RestoreAdditionalProjectSources>
      https://api.nuget.org/v3/index.json;
      https://nuget.bepinex.dev/v3/index.json
    </RestoreAdditionalProjectSources>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="BepInEx.Analyzers" Version="1.*" PrivateAssets="all" />
    <PackageReference Include="BepInEx.Core" Version="5.*" />
    <PackageReference Include="BepInEx.PluginInfoProps" Version="2.*" />
    <PackageReference Include="UnityEngine.Modules" Version="2021.3.5" IncludeAssets="compile" />
    <PackageReference Include="BepInEx.AssemblyPublicizer.MSBuild" Version="0.4.3" PrivateAssets="all" />
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.2"
                      PrivateAssets="all" Condition="'$(TargetFrameworkIdentifier)' == '.NETFramework'" />
  </ItemGroup>

  <ItemGroup>
    <!-- game assemblies: reference locally, publicized, never copied to output -->
    <Reference Include="GH.Runtime" HintPath="$(GameManaged)\GH.Runtime.dll" Publicize="true" Private="false" />
    <Reference Include="GH.Runtime.FirstPass" HintPath="$(GameManaged)\GH.Runtime.FirstPass.dll" Publicize="true" Private="false" />
    <Reference Include="GH.Shared" HintPath="$(GameManaged)\GH.Shared.dll" Publicize="true" Private="false" />
    <Reference Include="ScenarioRuleLibrary" HintPath="$(GameManaged)\ScenarioRuleLibrary.dll" Publicize="true" Private="false" />
    <Reference Include="MapRuleLibrary" HintPath="$(GameManaged)\MapRuleLibrary.dll" Publicize="true" Private="false" />
    <Reference Include="Cinemachine" HintPath="$(GameManaged)\Cinemachine.dll" Publicize="true" Private="false" />
    <Reference Include="InControl" HintPath="$(GameManaged)\InControl.dll" Publicize="true" Private="false" />
    <Reference Include="Unity.InputSystem" HintPath="$(GameManaged)\Unity.InputSystem.dll" Private="false" />
    <Reference Include="Unity.Addressables" HintPath="$(GameManaged)\Unity.Addressables.dll" Private="false" />
    <Reference Include="Unity.ResourceManager" HintPath="$(GameManaged)\Unity.ResourceManager.dll" Private="false" />
    <Reference Include="Unity.TextMeshPro" HintPath="$(GameManaged)\Unity.TextMeshPro.dll" Private="false" />
  </ItemGroup>
</Project>
```

Notes:
- `Publicize="true"` (BepInEx.AssemblyPublicizer.MSBuild) rewrites the *reference* assembly at build time so private/internal members are directly usable — no reflection; publicized refs are compile-only, you still run against the game's real DLLs ([README](https://github.com/BepInEx/BepInEx.AssemblyPublicizer)). Works on Unity Mono because runtime member-access checks don't bite already-JITed calls.
- `Private="false"` keeps game DLLs out of `BepInEx/plugins`.
- Pattern matches community game templates, e.g. [ArchipelagoBepInExPluginTemplate](https://github.com/alwaysintreble/ArchipelagoBepInExPluginTemplate).

### 3.3 Dev-loop tooling

| Tool | Version / asset | Notes |
|---|---|---|
| **ScriptEngine** ([BepInEx.Debug](https://github.com/BepInEx/BepInEx.Debug) r11.1, 2026-01-01) | plugin DLLs dropped in `BepInEx\scripts`, hot-reload on **F6** | Caveats (README): Harmony patches & spawned GameObjects survive reload — clean up in `OnDestroy()` (`harmony.UnpatchSelf()`, destroy GOs, unload bundles); statics linger in the old `data-xxxxxxxx` assembly |
| **DemystifyExceptions**, StartupProfiler, MirrorInternalLogs (same repo) | r11.1 | readable stack traces / startup profiling |
| **UnityExplorer** — maintained fork [yukieiji/UnityExplorer](https://github.com/yukieiji/UnityExplorer) (sinai-dev original archived 2023) | **v4.13.6** (2026-04-30), asset `UnityExplorer.BepInEx5.Mono.zip` | supports Unity 2017–2023 Mono; needs `HideManagerGameObject = true` in some games |
| **RuntimeUnityEditor** ([ManlyMarco](https://github.com/ManlyMarco/RuntimeUnityEditor)) | v6.3 (2025-11-02), `RuntimeUnityEditor.Bepin5_v6.3.zip` | inspector + REPL alternative |
| [CinematicUnityExplorer](https://github.com/originalnicodr/CinematicUnityExplorer) | fork with free-cam/posing | handy for camera-rig archaeology |

### 3.4 Gloomhaven modding quirks (from existing mods)

- Save-name parsing bug corrupts/loses "modded" saves — patched by [GloomhavenMod](https://github.com/Leopard2ARC/GloomhavenMod); be aware when testing.
- The game has an official **custom-ruleset system** (YAML parsed by `ScenarioRuleLibrary.YML.*`, Steam-only "Modding" menu; documented by [GHEM](https://github.com/Acenm5/GHEM)) whose compiler can touch/delete files in its mod folders — keep VR files out of the game's ruleset folders.
- A "Camera Tweaks" Nexus mod exists → camera control is centralized and patchable (good sign for the VR camera takeover).
- Planned official modding support was **cancelled** when Cephalofair ended the Flaming Fowl relationship in early 2024 ([kaytomas.com report](https://kaytomas.com/news/frosthaven-digital-is-closer-than-you-think)).

## 4. Asset pipeline (custom 3D assets into the shipped game)

### 4.1 AssetBundles — the primary path

**Editor version rule:** AssetBundles are not forward-compatible — a bundle built with a *newer* Unity than the runtime may be rejected; older-into-newer works. Build bundles with **Unity 2021.3 LTS** (2021.3.45f2, released 2025-10-03, is still downloadable via the [Unity download archive](https://unity.com/releases/editor/archive) with Hub deep-links even though 2021 LTS is out of support — [endoflife.date/unity](https://endoflife.date/unity)). Keep TypeTrees **enabled** (default; never pass `BuildAssetBundleOptions.DisableWriteTypeTree` — without TypeTrees any serialization-layout drift crashes the load). If the game turns out to be 2021.2.x, either build with 2021.2 or use `BuildAssetBundleOptions.AssetBundleStripUnityVersion` and test.
Sources: [Unity Manual — AssetBundles](https://docs.unity3d.com/Manual/AssetBundlesIntro.html), [version-mismatch failure thread](https://discussions.unity.com/t/cannot-load-assetbundle-because-it-is-not-compatible-with-the-newer-version-of-the-unity-runtime/695983).

**Builder project:** empty 2021.3 project (built-in RP is the default — matches the game), one editor script:

```csharp
// Assets/Editor/BuildBundles.cs
[MenuItem("Mod/Build AssetBundles")]
static void Build() => BuildPipeline.BuildAssetBundles(
    "Build/Bundles", BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64);
```

**Shader safety (built-in pipeline):**
- Custom shaders referenced by bundled materials are compiled **into** the bundle — always safe.
- **Built-in shaders (Standard, UI/Default, …) are never bundled** — a bundled material referencing Standard resolves against the *game's* copy, with only the variants the game didn't strip (classic pink-material problem). ([discussion](https://discussions.unity.com/t/adding-shaders-from-mods/668291), [NewAtlantis wiki](https://github.com/jonlab/NewAtlantis/wiki/Shader-included-in-an-AssetBundle))
- `Shader.Find` only finds shaders included in the game build. Robustness order:
  1. **Reuse the game's own materials** at runtime (`Resources.FindObjectsOfTypeAll<Material>()` or grab from loaded objects) — guaranteed variants + visual consistency.
  2. Ship a **self-contained custom lit shader** in the bundle (not built-in Standard).
  3. If bundling Standard-based materials: re-hook `mat.shader = Shader.Find(mat.shader.name)` on load and accept possible missing variants.

**Loading:** `AssetBundle.LoadFromFile(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "gloomhavenvr.bundle"))` — load once per process and cache (double-load errors). Patterns: [Nautilus guide](https://subnauticamodding.github.io/Nautilus/guides/assetbundles.html), [Jötunn asset loading](https://valheim-modding.github.io/Jotunn/tutorials/asset-loading.html).

**Addressables coexistence:** no conflict — Addressables is a layer over AssetBundles; a raw `LoadFromFile` bundle coexists fine (CAB names from a fresh project won't collide). Bonus: since Gloomhaven uses Addressables, the mod can spawn the **game's own assets** by key via `Addressables.LoadAssetAsync<T>(key)` once the game initialized Addressables — cleanest way to get game-consistent props (e.g. real card faces/materials) without redistributing anything.

### 4.2 Alternatives

- **Procedural meshes / `GameObject.CreatePrimitive`** — zero compatibility risk; fine for play surfaces, pointers, colliders.
- **glTFast** (`com.unity.cloud.gltfast`): runtime glTF loading, requires Unity ≥ 2020.1 → OK for 2021.3; its shaders must be shipped in a bundle. ([docs](https://docs.unity3d.com/Packages/com.unity.cloud.gltfast@5.2/manual/index.html), [repo](https://github.com/Unity-Technologies/com.unity.cloud.gltfast))
- **UnityGLTF** (Khronos): current releases support 2021.3+, runtime import, built-in RP via `UnityGLTF/PBRGraph`. ([repo](https://github.com/KhronosGroup/UnityGLTF))

### 4.3 World-space uGUI in VR

- Reference approach = **LCVR**: bundles Unity's **XR Interaction Toolkit** and drives menus with **ray interactors**; XRIT's `TrackedDeviceGraphicRaycaster` replaces `GraphicRaycaster` on world-space canvases ([XRIT UI setup](https://docs.unity3d.com/Packages/com.unity.xr.interaction.toolkit@3.0/manual/ui-setup.html)). XRIT 2.x and 3.x-before-the-6000.0-bump support Unity 2021.3 ([changelog](https://github.com/needle-mirror/com.unity.xr.interaction.toolkit/blob/master/CHANGELOG.md)).
- LCVR injects the XR packages via a **BepInEx preloader patcher** (`LCVR.Preload.dll` in `BepInEx/patchers`) — [LCVR repo](https://github.com/DaXcess/LCVR), [preloader docs](https://docs.bepinex.dev/articles/dev_guide/preloader_patchers.html).
- Lighter alternative: hand-rolled laser pointer feeding a custom `BaseInputModule` over stock `GraphicRaycaster` — proven in older VR mods, but XRIT bundling is less work and battle-tested. For Gloomhaven's screen-space-overlay canvases: they are **invisible in VR** and must be re-parented to world-space canvases (see §5 pitfalls).

## 5. XR enablement, step by step (Unity 2021.3, built-in RP)

### 5.1 Why legacy VR is a dead end

Built-in XR device backends (OpenVR/Oculus) were deprecated in 2019.3 and **removed in Unity 2020.1** ([Microsoft Learn](https://learn.microsoft.com/en-us/windows/mixed-reality/develop/unity/legacy-xr-support), [UploadVR](https://www.uploadvr.com/unity-deprecates-openvr-support/)). `XRSettings.LoadDeviceByName` still compiles in 2021.3 but is a no-op — there is nothing left to load ([Unity discussion](https://discussions.unity.com/t/xrsettings-loaddevicebyname-not-working/879614)). UUVR only uses the `enabledVRDevices` globalgamemanagers trick behind a `#if LEGACY` define for pre-2020 games ([UuvrPatcher.cs](https://github.com/Raicuparta/uuvr/blob/main/Uuvr.Patcher/UuvrPatcher.cs)). → **XR Plugin Management + provider plugin, bootstrapped by the mod.**

### 5.2 Files to ship (OpenXR path — recommended)

Harvest from a **dummy Unity 2021.3.x Mono project** with XR Plugin Management + OpenXR plugin installed (LCVR README documents this harvesting workflow):

| File | From dummy build | Install to |
|---|---|---|
| `Unity.XR.Management.dll`, `Unity.XR.OpenXR.dll`, `Unity.XR.CoreUtils.dll` (+ optionally `Unity.XR.Interaction.Toolkit.dll`) | `<Proj>_Data/Managed` | `BepInEx/plugins/GloomhavenVR/RuntimeDeps/` — loaded via `Assembly.LoadFile` in the plugin ([LCVR Plugin.cs `PreloadRuntimeDependencies`](https://github.com/DaXcess/LCVR/blob/main/Source/Plugin.cs)) |
| `UnityOpenXR.dll`, `openxr_loader.dll` (natives) | `<Proj>_Data/Plugins/x86_64` | `Gloomhaven_Data/Plugins/` (root Plugins works; [RepoXR Preload.cs](https://github.com/DaXcess/RepoXR/blob/main/Preload/Preload.cs), [UUVR patcher comment](https://github.com/Raicuparta/uuvr/blob/main/Uuvr.Patcher/UuvrPatcher.cs)) |
| `UnitySubsystemsManifest.json` | authored by mod | `Gloomhaven_Data/UnitySubsystems/UnityOpenXR/UnitySubsystemsManifest.json` |

Manifest content (RepoXR ships exactly this, version matched to the OpenXR package):

```json
{
  "name": "OpenXR XR Plugin",
  "version": "1.10.0",
  "libraryName": "UnityOpenXR",
  "displays": [ { "id": "OpenXR Display" } ],
  "inputs":   [ { "id": "OpenXR Input" } ]
}
```

**Timing:** Unity scans `UnitySubsystems/` + native plugins at engine startup, *before* chainloaded plugins run. Do the copy in a **BepInEx preloader patcher** (`BepInEx/patchers/GloomhavenVR.Preload.dll`, `Initialize()`) — RepoXR/LCVR-1.3.2 pattern; copying from `Plugin.Awake()` forces a one-time game restart (old LCVR 1.2.5 `mustRestart`). Sources: [LCVR CHANGELOG](https://github.com/DaXcess/LCVR/blob/main/CHANGELOG.md) ("restarting is no longer required"), [v1.3.2 Preload.cs](https://github.com/DaXcess/LCVR/blob/v1.3.2/Preloader/Preload.cs). (Current LCVR 1.5.0 removed the copy step only because vanilla Lethal Company now ships OpenXR itself — follow **RepoXR**, its game is built-in RP + PPv2 like Gloomhaven.)

### 5.3 Package versions for Unity 2021.3

| Package | Version for 2021.3 | Evidence |
|---|---|---|
| `com.unity.xr.openxr` | **1.10.0 recommended** (battle-tested: RepoXR ships exactly 1.10.0); acceptable range 1.8.2–**1.15.1**; 1.16.0+ requires 2022.3 | [1.10.0 package.json](https://github.com/needle-mirror/com.unity.xr.openxr/blob/1.10.0/package.json) (`"unity": "2021.3"`), [changelog](https://github.com/needle-mirror/com.unity.xr.openxr/blob/master/CHANGELOG.md), [RepoXR.csproj](https://github.com/DaXcess/RepoXR/blob/main/RepoXR.csproj) |
| `com.unity.xr.management` | **4.5.0** (RepoXR's choice; 4.4.0 also fine) | [4.5.0 package.json](https://github.com/needle-mirror/com.unity.xr.management/blob/4.5.0/package.json) |
| XR Interaction Toolkit (optional, for UI rays) | 2.x / 3.x pre-6000-bump | [changelog](https://github.com/needle-mirror/com.unity.xr.interaction.toolkit/blob/master/CHANGELOG.md) |

### 5.4 Init sequence (C# sketch — DaXcess pattern, publicized assemblies)

```csharp
// after Assembly.LoadFile of RuntimeDeps — internal members via AssemblyPublicizer
var generalSettings = ScriptableObject.CreateInstance<XRGeneralSettings>();
var managerSettings = ScriptableObject.CreateInstance<XRManagerSettings>();
var loader          = ScriptableObject.CreateInstance<OpenXRLoader>();

generalSettings.Manager = managerSettings;                    // internal setter
((List<XRLoader>)managerSettings.activeLoaders).Clear();      // internal list
((List<XRLoader>)managerSettings.activeLoaders).Add(loader);

// interaction profiles MUST be set before session start or no controller input:
var touch = ScriptableObject.CreateInstance<OculusTouchControllerProfile>();
touch.enabled = true;                                         // + Index/Vive/WMR/KHRSimple as desired
OpenXRSettings.Instance.features = new OpenXRFeature[] { touch /*, ...*/ };

OpenXRSettings.Instance.renderMode = OpenXRSettings.RenderMode.MultiPass; // safe default for built-in RP + PPv2; SPI as opt-in
OpenXRSettings.Instance.depthSubmissionMode = OpenXRSettings.DepthSubmissionMode.None;

Environment.SetEnvironmentVariable("XR_RUNTIME_JSON", runtimeJsonPath);   // optional runtime override/failover
generalSettings.InitXRSDK();                                  // internal
generalSettings.Start();                                      // internal

var displays = new List<XRDisplaySubsystem>();
SubsystemManager.GetInstances(displays);                      // success = displays.Count > 0
```

Real code: [LCVR Source/OpenXR.cs](https://github.com/DaXcess/LCVR/blob/main/Source/OpenXR.cs), [RepoXR Source/OpenXR.cs](https://github.com/DaXcess/RepoXR/blob/main/Source/OpenXR.cs). Extras worth copying:
- **Pre-flight check**: `SubsystemManager.GetAllSubsystemDescriptors()` must contain `"OpenXR Display"`/`"OpenXR Input"` — else the manifest/natives weren't picked up at boot.
- **Runtime enumeration + failover**: read `HKLM\SOFTWARE\Khronos\OpenXR\1` (`ActiveRuntime`, `AvailableRuntimes`) + well-known runtime JSONs (SteamVR `steamxr_win64.json`, Virtual Desktop `virtualdesktop-openxr.json`, Oculus `oculus_openxr_64.json`), try each via `XR_RUNTIME_JSON` until a display subsystem appears.
- **Diagnostics**: P/Invoke `UnityOpenXR.dll` → `DiagnosticReport_GenerateReport()`, `NativeConfig_GetRuntimeName/Version()`.
- **Public-API-only variant** (no publicizer, UUVR): `managerSettings.loaders.Add(loader); managerSettings.InitializeLoaderSync(); managerSettings.StartSubsystems();` ([XrPluginToggler.cs](https://github.com/Raicuparta/uuvr/blob/main/Uuvr/VrTogglers/XrPluginToggler.cs); documented order in [XR Management docs](https://docs.unity3d.com/Packages/com.unity.xr.management@4.5/manual/EndUser.html)).

### 5.5 After init: rendering & tracking

- With a running `XRDisplaySubsystem`, existing cameras render **stereo automatically** and get implicit HMD pose driving; opt a camera out with `XRDevice.DisableAutoXRCameraTracking(cam, true)` ([docs](https://docs.unity3d.com/ScriptReference/XR.XRDevice.DisableAutoXRCameraTracking.html)). For the VR rig, use Input System `TrackedPoseDriver` with `<XRHMD>/centerEyePosition|centerEyeRotation` and `<XRController>{LeftHand|RightHand}/pointerPosition|pointerRotation` actions, manually `Enable()`d ([RepoXR TrackingInput.cs](https://github.com/DaXcess/RepoXR/blob/main/Source/Input/TrackingInput.cs)); the game's own `UnityEngine.SpatialTracking.TrackedPoseDriver` is the fallback.
- **D3D11 required** for desktop OpenXR (no OpenGL) — [OpenXR manual](https://docs.unity3d.com/Packages/com.unity.xr.openxr@1.10/manual/index.html).
- **PPv2 + Single-Pass Instanced is trouble** (Texture2DArray/`STEREO_INSTANCING_ON` shader requirements) — default to MultiPass; RepoXR patches its game's PPv2 pipeline for SPI ([PostProcessPatches.cs](https://github.com/DaXcess/RepoXR/blob/main/Source/Patches/Rendering/PostProcessPatches.cs), [SPI shader manual](https://docs.unity3d.com/Manual/SinglePassInstancing.html)).
- **Screen-Space-Overlay canvases don't render in VR** — convert to `RenderMode.WorldSpace` (interactable UI) or `ScreenSpaceCamera` (fades/overlays), the RepoXR approach ([Entrypoint.cs](https://github.com/DaXcess/RepoXR/blob/main/Source/Entrypoint.cs)).
- **Input reading**: OpenXR requires the Input System package ([input docs](https://docs.unity3d.com/Packages/com.unity.xr.openxr@1.10/manual/input.html)) — Gloomhaven ships InputSystem 1.3.0. Read controllers via Input System XR layouts (DaXcess approach) or `UnityEngine.XR.InputDevices`+`CommonUsages`. If bundling XRIT, its `RuntimeInitializeOnLoad` composites must be initialized manually after `Assembly.LoadFile` (`ButtonFallbackComposite.Initialize()` etc., [LCVR Plugin.cs](https://github.com/DaXcess/LCVR/blob/main/Source/Plugin.cs)). InControl (game's flat-input lib) keeps working untouched.

### 5.6 OpenVR XR Plugin alternative (not recommended)

Valve's [unity-xr-plugin](https://github.com/ValveSoftware/unity-xr-plugin) (`com.valvesoftware.unity.openvr`): natives `XRSDKOpenVR.dll` + `openvr_api.dll`, manifest folder `UnitySubsystems/XRSDKOpenVR/` ([manifest](https://github.com/ValveSoftware/unity-xr-plugin/blob/master/com.valve.openvr/Runtime/UnitySubsystemsManifest.json), ids "OpenVR Display"/"OpenVR Input"). Last stable v1.1.4 (Jan 2021); a v1.2.4 pre-release surfaced Oct 2025 noting a Unity bug where "single pass doesn't work with the built-in render pipeline" ([releases](https://github.com/ValveSoftware/unity-xr-plugin/releases)). Input needs SteamVR action manifests in `StreamingAssets/SteamVR/` (UUVR ships them: [tree](https://github.com/Raicuparta/uuvr/tree/main/Uuvr.Patcher/CopyToGame/Data/StreamingAssets/SteamVR)). SteamVR-only, effectively dormant → OpenXR is the maintained path and covers SteamVR anyway.

### 5.7 Testing without a headset

- OpenXR **Mock Runtime** feature (in the package, aimed at editor/tests): [MockRuntime API](https://docs.unity3d.com/Packages/com.unity.xr.openxr@1.10/api/UnityEngine.XR.OpenXR.Features.Mock.MockRuntime.html).
- For the built game: point `XR_RUNTIME_JSON` at a simulator runtime, e.g. [elliotttate/OpenXR-Simulator](https://github.com/elliotttate/OpenXR-Simulator) — slots directly into the failover mechanism above.


## 6. Quest 3 / OpenXR specifics

### 6.1 Connection paths → OpenXR runtimes

| Connection | Active OpenXR runtime |
|---|---|
| Quest Link / Air Link | Meta/Oculus PC runtime (native OpenXR runtime) |
| Virtual Desktop | **VDXR** (bypasses SteamVR, ~10% faster) |
| Steam Link | **SteamVR** (Steam Link requires SteamVR as OpenXR runtime) |

An OpenXR-plugin-based mod works on **all three** — LCVR explicitly supports "Oculus, Virtual Desktop, SteamVR and many more" and even lets users switch the active OpenXR runtime from its settings (registry-independent runtime discovery — worth copying). Sources: [UploadVR on VDXR](https://www.uploadvr.com/virtual-desktops-vdxr-runtime/), [Steam Link ↔ SteamVR](https://resolutiongames.zendesk.com/hc/en-us/articles/21366070277019-I-can-t-start-the-game-through-Steam-Link), [LCVR](https://github.com/DaXcess/LCVR).

### 6.2 Controller interaction profiles

- Enable the **generic Oculus Touch profile** (`/interaction_profiles/oculus/touch_controller`) — every runtime aliases Quest 3 Touch Plus to it.
- Quest 3's dedicated profile `/interaction_profiles/meta/touch_controller_plus` (`XR_META_touch_controller_plus`: trigger curl/slide, proximity) exists in Unity OpenXR plugin **≥ 1.12.0** as `MetaQuestTouchPlusControllerProfile` ([Unity docs](https://docs.unity3d.com/Packages/com.unity.xr.openxr@1.16/manual/features/metaquesttouchpluscontrollerprofile.html), [Meta docs](https://developers.meta.com/horizon/documentation/native/pc/native-touch-plus-controllers/)) — but there is a reported Unity bug where it misidentifies Quest 3 controllers as HMDs ([forum report](https://discussions.unity.com/t/metaquesttouchplus-profile-reads-controllers-as-hmds-not-controllers-breaking-all-controller-inputs-for-quest3/1690098)). Treat Touch Plus as optional icing; ship generic Touch.
- Version constraint: **OpenXR plugin 1.15.1 (2025-07-23) is the last release supporting Unity 2021.3** (1.16.0-pre.1 raised the minimum to 2022.3) — [changelog](https://github.com/needle-mirror/com.unity.xr.openxr/blob/master/CHANGELOG.md).

### 6.3 Bindings exposure

- OpenXR mandates no user-facing rebinding. SteamVR ~1.26 added rebinding of OpenXR apps in its bindings UI but it is reported buggy and exists only on SteamVR (not Meta runtime / VDXR) — [SteamVR notes](https://store.steampowered.com/news/posts/?feed=steam_community_announcements&enddate=1688600566), [UploadVR](https://www.uploadvr.com/steamvr-automatic-controller-rebinding-update/).
- Mod pattern (per LCVR): auto-detect controller type → apply named binding profile; expose `ControllerBindingsOverrideProfile` in the BepInEx config (LCVR even pulls community profiles from [a GitHub repo](https://github.com/DaXcess/LCVR-Controller-Profiles)) + in-game world-space settings menu. Emulate: BepInEx `.cfg` for remaps + simple in-game VR settings panel.

## 7. Legal / multiplayer notes

- **Frozen target:** last patch **v1.1.8307.0 (2024-01-15/18)** — Polish localization, crossplay network-version sync, desync/softlock fixes; nothing since → **stable patch targets, no update-breakage risk** ([announcement](https://steamcommunity.com/games/780290/announcements/detail/3985189205478215123?l=english), [SteamDB patchnotes](https://steamdb.info/app/780290/patchnotes/)). Game is **not delisted** — still on sale ([store page](https://store.steampowered.com/app/780290/Gloomhaven/)). Publisher: Asmodee Digital → rebranded **Twin Sails Interactive** (2022), independent via management buyout April 2025 ([announcement](https://twin-sails.com/en/twin-sails-interactive-moves-forward-as-an-independent-entity-from-asmodee/)).
- **Anticheat: none** — absent from PCGamingWiki's anticheat list and SteamDB's EAC tech list ([list](https://www.pcgamingwiki.com/wiki/List_of_games_with_anti-cheat_technology)). No VAC, no ban infrastructure.
- **Multiplayer (Photon Bolt):** state replication + event system (listen-server "the server is just another player"), **not deterministic lockstep** ([Bolt overview](https://doc.photonengine.com/bolt/current/getting-started/overview)). The game's own patch history shows desync was a rules-layer concern. **A camera/rendering/UI/input-only mod that issues the same game commands as the mouse UI cannot cause desync**; never touch rule state or Bolt entities. v1 is single-player-focused anyway (PROJECT.md).
- **EULA:** Saber's ([saber.games/eula](https://saber.games/eula/)) and Asmodee Digital's ([Steam EULA](https://store.steampowered.com/eula/622440_eula_0)) contain standard no-reverse-engineering/no-derivative-works clauses, no mod carve-out. Standard community practice (identical to LCVR's situation): **distribute only your own plugin DLLs + self-authored bundles; never game assets, Addressables catalogs, or decompiled code**. Non-competitive co-op game, no anticheat, publisher has never acted against mods → real-world risk minimal.


## 8. Risks

| # | Risk | Severity | Mitigation |
|---|---|---|---|
| R1 | **Graphics API**: desktop OpenXR needs D3D11; if the game launches under D3D12/Vulkan or `-force-glcore`, XR init fails | High | Verify `GH.exe` default API (2021.3 default is D3D11); force via `-force-d3d11` launch arg if needed |
| R2 | **PPv2 vs Single-Pass Instanced**: broken post effects/one-eye rendering in SPI on built-in RP | High | Default `RenderMode.MultiPass` (RepoXR exposes SPI as opt-in); patch PPv2 only if SPI perf is required |
| R3 | **Screen-space-overlay UI invisible in VR**: Gloomhaven is UI-heavy (cards, HUD, menus) | High (known, budgeted) | Systematic canvas conversion to WorldSpace/ScreenSpaceCamera (RepoXR `Entrypoint.cs` pattern); this is the mod's core work anyway (PROJECT.md R3/R4) |
| R4 | **UnitySubsystems manifest timing**: XR descriptors only registered at engine boot | Medium | BepInEx preloader patcher installs natives + manifest before engine init (RepoXR pattern); pre-flight check via `SubsystemManager.GetAllSubsystemDescriptors` |
| R5 | **Version mismatch of harvested XR DLLs**: managed/native/manifest versions must be one coherent package set built with 2021.3 | Medium | Harvest all files from one dummy 2021.3.5f1 (or any 2021.3.x) Mono build with OpenXR 1.10.0; keep manifest `version` field matched |
| R6 | **Camera rig fight**: Cinemachine will fight the XR pose driving | Medium | `XRDevice.DisableAutoXRCameraTracking` on game cameras + own rig camera; disable/patch `CinemachineBrain` in VR mode ("Camera Tweaks" mod proves camera code is patchable) |
| R7 | **Quest 3 TouchPlus profile bug** (controllers detected as HMDs in Unity OpenXR) | Low | Ship generic `OculusTouchControllerProfile`; TouchPlus optional |
| R8 | **AssetBundle/editor drift**: bundles built with wrong Unity version rejected | Low | Build bundles with 2021.3 LTS (archive still serves 2021.3.45f2); keep TypeTrees on |
| R9 | **Multiplayer desync** | Low (v1 single-player) | Patch only UI/input/camera seams; issue the same commands as mouse UI; never touch `ScenarioRuleLibrary`/Bolt state |
| R10 | **EULA** (no mod carve-out) | Low practical | Distribute only own code/assets; no anticheat, dormant publisher, industry-standard practice |
| R11 | **ScriptEngine hot-reload leaks** (Harmony patches, GOs, bundles survive reload) | Low (dev-only) | `OnDestroy`: `harmony.UnpatchSelf()`, destroy spawned GOs, `AssetBundle.Unload` |
| R12 | **Game updates breaking patch targets** | Very low | Last patch Jan 2024; official support ended; frozen target |
