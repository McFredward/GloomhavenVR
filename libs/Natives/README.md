# libs/Natives — OpenXR native plugins (NOT committed)

Populated by `scripts/fetch-natives.sh`. The repo-root `.gitignore` excludes
`*.dll`, so only this README and `.gitkeep` are tracked.

These two Windows natives are **prebuilt inside the `com.unity.xr.openxr`
package** — no Unity editor compiles them (see `unity/HARVESTING.md`,
"Partial shortcut"). They are fetched version-exact from the
[needle-mirror repo](https://github.com/needle-mirror/com.unity.xr.openxr)
at tag **1.10.0** and hash-verified.

| File | Source path in package | SHA256 (tag 1.10.0, recorded 2026-07-15) |
|---|---|---|
| `UnityOpenXR.dll` | `Runtime/windows/x64/UnityOpenXR.dll` | `2275da2750ebc9c815386604f73f0450b03fed6f44dafdeb15e978633e4866f5` |
| `openxr_loader.dll` | `RuntimeLoaders/windows/x64/openxr_loader.dll` | `c008f1f429eb89ad1ebb959a74425c1b8df78abe6b1a08287ed44900d49881d3` |

Deployment (see `docs/TESTING-P1.md`): both DLLs ship next to the preloader as
`BepInEx/patchers/GloomhavenVR/Natives/*.dll`; at boot the preloader copies
them into `Gloomhaven_Data/Plugins/x86_64/` and writes the version-matched
`UnitySubsystemsManifest.json`.

**License:** Unity Companion License (the loader is Unity's build of Khronos'
Apache-2.0 OpenXR loader) — redistributable alongside the GPL-3.0 mod as
separate aggregated files; same practice as LCVR/RepoXR release packages.

If the OpenXR package pin ever changes, update the version + hashes here AND
in `scripts/fetch-natives.sh`, and keep the manifest version written by the
preloader in sync (single coherent set — TOOLCHAIN.md risk R5).
