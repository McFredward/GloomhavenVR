# Prebuilt asset bundle

`gloomhavenvr.bundle` — the mod's AssetBundle, committed so a clone can ship the
real 3D visuals without a Unity install. Built for **StandaloneWindows64**,
Unity **2021.3.5f1**, TypeTrees ON.

> **Build it with `/home/claw/unity-2021.3.5`, NOT `/home/claw/unity-2021.3`.**
> The second one is 2021.3.45f1 and its bundles do not load in the game at all.
> `scripts/check-bundle-format.sh` (run by `refactor-guard.sh check`) enforces this.

**Built NATIVELY with Unity 2021.3.5f1** (the game's exact version) → UnityFS archive
**format 7** and shaders compiled for the game runtime. This is the primary path since the
headless license for 2021.3.5 was solved (mint a Personal ULF from Hub's token — see
`.planning/debug/mint-ulf.sh` and `unity-license-headless.md`).

Two failure modes this fixes, both seen in-game before:
- **Format 8 → "Unable to read header from archive file"**: the 2021.3.45f1 editor emits a
  format-8 wrapper (`BlockInfoNeedPaddingAtStart` flag) the older 2021.3.5f1 runtime cannot
  read → procedural fallback. A native 2021.3.5 build writes format 7 directly.
- **Pink board**: a custom shader compiled by 2021.3.45 fails in the 2021.3.5 runtime
  (`Shader GloomhavenVR/BoardLit is not supported on this GPU`) → magenta. Compiling the
  shader with 2021.3.5 fixes it.

**Fallback** if only the 2021.3.45 editor is available: build with it, then downgrade the
wrapper to format 7 with `unity/repack-bundle/repack_fmt7.py` (UnityPy, copies inner bytes
verbatim). NOTE the repack cannot fix the pink shader — only a native 2021.3.5 build does —
so the native path is strongly preferred. Verify either way: `head -c12 gloomhavenvr.bundle | xxd`
→ offset 8 must read `00000007`.

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
Source lives in `unity/GloomhavenVR.Assets/Assets/Bundle/`. On the licensed Unity
2021.3.5f1 editor: `GloomhavenVR → Build AssetBundles` (or
`UNITY_PATH=/home/claw/unity-2021.3.5/Editor/Unity scripts/build-bundles.sh`), then copy
`Build/Bundles/gloomhavenvr.bundle` here and run `scripts/check-bundle-format.sh`.
When a hand FBX changed, use `-executeMethod GloomhavenVR.HandsBuilder.Build` instead —
it regenerates the hand prefabs (whose baked `m_AABB` is otherwise stale) and then builds
the bundle itself.
The board prefab/material are assembled from the FBX by
`Assets/Editor/BuildBoard.cs` (`-executeMethod GloomhavenVR.BoardBuilder.Build`).
