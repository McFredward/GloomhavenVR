# GloomhavenVR — installation guide

End-user install for the release zip (`GloomhavenVR-<version>.zip`, built by
`scripts/package-release.sh`). Developers: see the README "Developer setup".

## Requirements

- **Gloomhaven (digital)** for PC, v1.1.x (the mod is pinned against v1.1.8307.0 —
  the game has not been patched since Jan 2024).
- **Windows + D3D11** (desktop OpenXR requires D3D11; it is Unity 2021.3's Windows
  default).
- A **PC-VR headset with an OpenXR runtime**. Verified targets (Quest 3, but any
  OpenXR-capable PCVR headset should work):
  - **Quest Link / Air Link** — Meta Quest Link PC app, set as *active OpenXR runtime*
    in its settings (Settings → General → OpenXR Runtime → "Set Meta Quest Link as
    active").
  - **Virtual Desktop** — uses its own **VDXR** runtime; select "VDXR" as the OpenXR
    runtime in the VD streamer settings.
  - **Steam Link / SteamVR** — SteamVR set as active OpenXR runtime
    (SteamVR Settings → OpenXR → "Set SteamVR as OpenXR runtime").

  You can also force a specific runtime per-game via the mod config
  (`[General] RuntimeOverride` = path to a runtime JSON, e.g. SteamVR's
  `steamxr_win64.json`); when unset the mod auto-detects the active runtime and
  fails over through all installed ones.

## 1. Install BepInEx 5.4.23.5

1. Download `BepInEx_win_x64_5.4.23.5.zip` from
   <https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5>.
2. Extract it into the Gloomhaven install folder — the folder containing `GH.exe`
   (Steam: right-click the game → Manage → Browse local files).
3. Run the game once flat, quit. Verify `BepInEx/LogOutput.log` now exists —
   that proves the loader chainloads.

## 2. Install the mod

Extract the release zip into the same Gloomhaven folder (merge `BepInEx/`).
Resulting layout:

```
Gloomhaven/
├── GH.exe
└── BepInEx/
    ├── plugins/GloomhavenVR/
    │   ├── GloomhavenVR.dll
    │   ├── RuntimeDeps/            Unity XR assemblies (loaded at runtime)
    │   └── gloomhavenvr.bundle     optional asset bundle (procedural fallback without it)
    └── patchers/GloomhavenVR/
        ├── GloomhavenVR.Preload.dll
        └── Natives/                UnityOpenXR.dll + openxr_loader.dll
```

## 3. First launch

Start your OpenXR runtime (headset connected/streaming), then launch the game
normally. What happens, in order:

1. **Preloader** (before the engine boots) copies the OpenXR natives into
   `Gloomhaven_Data/Plugins/x86_64/` and writes
   `Gloomhaven_Data/UnitySubsystems/UnityOpenXR/UnitySubsystemsManifest.json`.
   These are the **only** files placed outside `BepInEx/` (idempotent,
   hash-compared on every boot).
2. **Plugin** loads the RuntimeDeps, runs the OpenXR pre-flight and starts the
   session. The main menu appears head-tracked on a floating screen; scenarios
   render as a table-scale diorama.

Expected `BepInEx/LogOutput.log` lines on a good boot:

```
[Info   :GloomhavenVR.Preload] OpenXR runtime assets ready (package 1.10.0).
[Info   :GloomhavenVR] [Core] Loaded 3 runtime dependencies: Unity.XR.CoreUtils, Unity.XR.Management, Unity.XR.OpenXR
[Info   :GloomhavenVR] ... VR RUNNING on '<your runtime name>' ...
[Info   :GloomhavenVR] [Rig] Menu rig built around camera ...        (main menu)
[Info   :GloomhavenVR] [Rig] VR rig built at focus ...               (scenario start)
[Info   :GloomhavenVR] [Hands] Hands built under ...
```

## 4. Configuration

Created on first launch under `BepInEx/config/`:

| File | Contents |
|---|---|
| `dev.gloomhavenvr.cfg` | master switch (`[General] Enabled`), runtime override, world scale, menu rig, compat kill-switches, dominant hand, dev mode |
| `dev.gloomhavenvr.comfort.cfg` | turning, seated mode, world grab, vignette, recenter chord |
| `dev.gloomhavenvr.board.cfg` | touch range, hex snap, AoE stick tuning |
| `dev.gloomhavenvr.cards.cfg` | fan/tray/card sizes and placement |
| `dev.gloomhavenvr.worldui.cfg` | every physicalized UI surface + flat screen + settings panel |

Most settings are also adjustable **in VR**: poke the small **SET** gear at the
table edge (next to Ready/Undo/Skip) or hold the **A/X button on your
non-dominant hand** for 0.6 s. Changes apply live and persist.

**Recenter:** hold **B + Y (both controllers)** for 1 s.
**Back to vanilla:** set `[General] Enabled = false` — the game runs 100% unmodified.

## 5. Troubleshooting

- **Headset shows nothing / game stays flat:** add the Steam launch option
  `-force-d3d11`. Desktop OpenXR does not work under other graphics APIs.
- **"VR unavailable this session (RuntimeDeps missing)"** in the log: the
  `RuntimeDeps/` folder next to `GloomhavenVR.dll` is missing — re-extract the zip.
- **"OpenXR runtime asset install incomplete"**: the `Natives/` folder next to the
  preloader is missing — re-extract the zip.
- **Wrong/no runtime picked:** set `[General] RuntimeOverride` to your runtime's
  JSON, e.g. `C:\Program Files (x86)\Steam\steamapps\common\SteamVR\steamxr_win64.json`.
- Anything else: the stage-by-stage failure triage table in
  [`docs/TESTING-P1.md`](docs/TESTING-P1.md) §4 maps every log line to a cause and
  fix; `docs/TESTING-FULL-LOOP.md` is the full validation script.

## Uninstall

Delete `BepInEx/plugins/GloomhavenVR/` and `BepInEx/patchers/GloomhavenVR/`
(optionally the natives the preloader placed: `Gloomhaven_Data/Plugins/x86_64/
UnityOpenXR.dll`, `openxr_loader.dll` and `Gloomhaven_Data/UnitySubsystems/
UnityOpenXR/` — they are inert without the mod). Removing all of `BepInEx/`
removes the loader too.
