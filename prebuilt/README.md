# Prebuilt asset bundle

`gloomhavenvr.bundle` — the mod's AssetBundle, committed so a clone can ship the
real 3D visuals without a Unity install. Built for **StandaloneWindows64**,
Unity **2021.3.45f1**, TypeTrees ON.

**Archive format: UnityFS version 7** (post-processed). The 2021.3.45f1 editor emits
a format-**8** archive wrapper (with the `BlockInfoNeedPaddingAtStart` flag), which the
game's older **2021.3.5f1** runtime CANNOT read — it fails at load with
`Unable to read header from archive file` and the mod falls back to procedural visuals.
So after building we re-wrap the archive down to format 7 with
`unity/repack-bundle/repack_fmt7.py` (UnityPy). The inner SerializedFile + `.resS` bytes
are copied **verbatim** — only the outer container changes — so the assets are exactly
what 2021.3.45 produced, now in a container 2021.3.5 accepts. The inner 2021.3.x
serialization layout itself is stable and reads fine.

(Why not just build with 2021.3.5f1? On the headless clawmachine builder the fresh
2021.3.5 editor demands an online license re-activation that never completes — the box
has no OS keyring, so Unity Hub's access token never reaches the licensing daemon
(`Error: Access token is unavailable`). Only the already-activated 2021.3.45 editor
builds headlessly. Hence: build with 2021.3.45 → repack to format 7.)

Deployed to `BepInEx/plugins/GloomhavenVR/gloomhavenvr.bundle` automatically by
`scripts/install.ps1` and `scripts/package-release.sh` (a freshly built
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
