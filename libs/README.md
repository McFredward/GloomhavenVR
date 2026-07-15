# libs/ — harvested XR binaries (NOT committed)

This directory is populated by the harvest step of the companion Unity project
(`unity/GloomhavenVR.Assets`, script `Assets/Editor/HarvestRuntimeDeps.cs`) —
see `unity/HARVESTING.md`. The repo-root `.gitignore` excludes `*.dll`, so only
the JSON manifests and this README are ever tracked.

Expected layout after a harvest:

```
libs/
├── RuntimeDeps/                  # managed DLLs, Assembly.LoadFile'd by the plugin
│   ├── Unity.XR.Management.dll         (4.5.0)
│   ├── Unity.XR.OpenXR.dll             (1.10.0)
│   ├── Unity.XR.CoreUtils.dll          (2.2.3)
│   ├── Unity.XR.Interaction.Toolkit.dll (2.6.5)
│   ├── Unity.InputSystem.dll           (1.7.0 — newer than the game's 1.3.0)
│   ├── UnityEngine.SpatialTracking.dll (optional)
│   └── harvest-manifest.json           # exact versions + build date (committed-able)
└── Natives/
    ├── Plugins/x86_64/
    │   ├── UnityOpenXR.dll
    │   └── openxr_loader.dll
    └── UnitySubsystems/UnityOpenXR/
        └── UnitySubsystemsManifest.json  # version-matched to the OpenXR package
```

All files in one harvest form ONE coherent set (TOOLCHAIN.md risk R5) — never
mix DLLs from different harvests or Unity versions.
