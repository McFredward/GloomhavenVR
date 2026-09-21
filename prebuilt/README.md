# Prebuilt asset bundles

`gloomhavenvr.bundle` — the mod's AssetBundle, committed so a clone can ship the
real 3D visuals without a Unity install. Built for **StandaloneWindows64**,
Unity **2021.3.5f1**, TypeTrees ON.

`ghvr-town.bundle` separately contains the three town NPCs, body and facial rigs,
separate articulated eyes, animation, station furniture, work tray and their shaders.
Both archives ship beside the plugin DLL.
Keeping town art separate avoids the GitHub file-size ceiling and leaves the reviewed
existing board/hand/environment bank unchanged. Its name deliberately does not contain
`gloomhavenvr`: historical main-bank discovery uses that substring.

> **Build it with `/home/claw/unity-2021.3.5`, NOT `/home/claw/unity-2021.3`.**
> The second one is 2021.3.45f1 and its bundles do not load in the game at all.
> `scripts/check-bundle-format.sh` (run by `refactor-guard.sh check`) enforces this.

**Built NATIVELY with Unity 2021.3.5f1** (the game's exact version) → UnityFS archive
**format 7** and shaders compiled for the game runtime. Build with a licensed installation
of that exact editor; machine-specific activation files and credentials stay private.

Two failure modes this fixes, both seen in-game before:
- **Format 8 → "Unable to read header from archive file"**: the 2021.3.45f1 editor emits a
  format-8 wrapper (`BlockInfoNeedPaddingAtStart` flag) the older 2021.3.5f1 runtime cannot
  read → procedural fallback. A native 2021.3.5 build writes format 7 directly.
- **Pink board**: a custom shader compiled by 2021.3.45 fails in the 2021.3.5 runtime
  (`Shader GloomhavenVR/BoardLit is not supported on this GPU`) → magenta. Compiling the
  shader with 2021.3.5 fixes it.

**Historical investigation:** `unity/repack-bundle/repack_fmt7.py` can downgrade the wrapper
while preserving its contents, but cannot repair shaders compiled with the wrong editor.
It is not a replacement for the exact-editor release build. Verify the wrapper with: `head -c12 gloomhavenvr.bundle | xxd`
→ offset 8 must read `00000007`.

Deployed to `BepInEx/plugins/GloomhavenVR/gloomhavenvr.bundle` automatically by
`scripts/install.ps1` and `scripts/package-release.sh` **by default**, even if an older ignored
Unity output exists. For deliberate local asset testing, select
`unity/GloomhavenVR.Assets/Build/Bundles/gloomhavenvr.bundle` using `-UseLocalBundle` in
PowerShell or `GHVR_USE_LOCAL_BUNDLE=1` for the shell packager. Missing explicitly selected
output is an error. Promote tested assets into this committed copy before releasing.

## Contents
- `PlayTray.prefab` — the control-board 3D asset (aged-oak + brass board, ~0.64 × 0.32 m):
  textured mesh + the six named anchor transforms the mod looks up
  (`Slot1 Slot2 ShortRestToken LongRestToken ConfirmButton UndoButton`), material on
  the bundled `GloomhavenVR/BoardLit` shader (albedo + normal, baked studio light —
  no built-in Standard, so no pink-material trap).
- `VRTestCube.prefab` — pre-existing smoke-test asset.

## Rebuilding
Source lives in `unity/GloomhavenVR.Assets/Assets/Bundle/`. On the licensed Unity
2021.3.5f1 editor: `GloomhavenVR → Build AssetBundles` (or
`UNITY_PATH=/home/claw/unity-2021.3.5/Editor/Unity scripts/build-bundles.sh`), then copy
`Build/Bundles/gloomhavenvr.bundle` here and run `scripts/check-bundle-format.sh`.
When a hand FBX changed, use `-executeMethod GloomhavenVR.HandsBuilder.Build` instead —
it regenerates the hand prefabs (whose baked `m_AABB` is otherwise stale) and then builds
the bundle itself.
The board prefab/material are assembled from the FBX by
`Assets/Editor/BuildBoard.cs` (`-executeMethod GloomhavenVR.BoardBuilder.Build`).

Pack the already-authored town sources with `scripts/build-bundles.sh town`, then promote
`Build/TownServices/ghvr-town.bundle` here. This does not regenerate meshes or alter the main
bank. Use the same exact Unity editor; on a headless Linux host wrap the command with
`xvfb-run -a` so shader/import workers have a display. Full art reproduction and validation:
[town runtime assets](../.planning/research/TOWN-SERVICES-RUNTIME-ASSETS.md).
The articulated face revision and its source-bound runtime checks are recorded in
[build 543](../.planning/research/TOWN-SERVICES-543.md). The matching town bundle is
required; updating only the DLL cannot add the eye geometry or facial blend shapes.
