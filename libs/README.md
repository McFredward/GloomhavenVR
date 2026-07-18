# libs/ — shipped XR binaries (NOT committed)

Two independently populated directories (only READMEs/.gitkeep are tracked;
the repo-root `.gitignore` excludes `*.dll` and the generated `versions.json`):

| Dir | Contents | Populated by |
|---|---|---|
| `Natives/` | `UnityOpenXR.dll`, `openxr_loader.dll` (Windows x64, prebuilt in the OpenXR package) | `scripts/fetch-natives.sh` (SHA256-pinned download) |
| `RuntimeDeps/` | `Unity.XR.Management.dll` 4.5.0, `Unity.XR.CoreUtils.dll` 2.2.3, `Unity.XR.OpenXR.dll` 1.10.0 | `scripts/build-runtimedeps.sh` (provisional, from needle-mirror source) — or the Unity editor harvest (`unity/HARVESTING.md`), which replaces the provisional set 1:1 |

See the README inside each directory for details (hashes, defines, provenance,
replacement rules). Deploy targets (via `scripts/install.ps1`):

```
libs/RuntimeDeps/*.dll -> <Game>/BepInEx/plugins/GloomhavenVR/RuntimeDeps/
libs/Natives/*.dll     -> <Game>/BepInEx/patchers/GloomhavenVR/Natives/
```

All shipped XR files must form ONE coherent OpenXR package set (managed 1.10.0 +
natives 1.10.0 + manifest version 1.10.0) — TOOLCHAIN.md risk R5. Never mix
versions; bump everything together (fetch pin, RuntimeDeps pin, preloader
`OpenXRPackageVersion` const).
