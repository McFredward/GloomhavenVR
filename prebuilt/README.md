# Prebuilt asset bundle

`gloomhavenvr.bundle` — the mod's AssetBundle, committed so a clone can ship the
real 3D visuals without a Unity install. Built for **StandaloneWindows64**,
Unity **2021.3.45f1**, TypeTrees ON (loads in the game's 2021.3.5f1 runtime — the
2021.3.x serialization layout is stable and forward-compatible).

Deployed to `BepInEx/plugins/GloomhavenVR/gloomhavenvr.bundle` automatically by
`scripts/deploy.ps1` and `scripts/package-release.sh` (a freshly built
`unity/GloomhavenVR.Assets/Build/Bundles/gloomhavenvr.bundle` is preferred when
present; this committed copy is the fallback).

## Contents
- `PlayTray.prefab` — the control-board 3D asset (aged-oak + brass board, ~0.64 × 0.32 m):
  textured mesh + the six named anchor transforms the mod looks up
  (`Slot1 Slot2 ShortRestToken LongRestToken ConfirmButton UndoButton`), material on
  the bundled `GloomhavenVR/BoardLit` shader (albedo + normal, baked studio light —
  no built-in Standard, so no pink-material trap).
- `VRTestCube.prefab` — pre-existing smoke-test asset.

## Rebuilding
Source lives in `unity/GloomhavenVR.Assets/Assets/Bundle/`. On a licensed Unity
2021.3.45f1 editor: `GloomhavenVR → Build AssetBundles` (or
`scripts/build-bundles.sh`), then copy `Build/Bundles/gloomhavenvr.bundle` here.
The board prefab/material are assembled from the FBX by
`Assets/Editor/BuildBoard.cs` (`-executeMethod GloomhavenVR.BoardBuilder.Build`).
