#!/usr/bin/env bash
# GloomhavenVR — release packaging (Phase 5, MISSION C.1).
#
# Builds Release and assembles dist/GloomhavenVR-<version>.zip with the EXACT
# install layout the runtime expects (paths verified against the code):
#
#   INSTALL.txt
#   BepInEx/plugins/GloomhavenVR/GloomhavenVR.dll
#   BepInEx/plugins/GloomhavenVR/RuntimeDeps/*.dll     <- RuntimeDepsLoader.RuntimeDepsDir
#                                                         (= <plugin dir>/RuntimeDeps)
#   BepInEx/plugins/GloomhavenVR/gloomhavenvr.bundle   <- HandVisuals/WorldUIAssets probe
#                                                         (<plugin dir>/gloomhavenvr.bundle);
#                                                         placeholder README shipped when the
#                                                         bundle has not been built
#   BepInEx/patchers/GloomhavenVR/GloomhavenVR.Preload.dll
#   BepInEx/patchers/GloomhavenVR/Natives/*.dll        <- Patcher.InstallNatives reads
#                                                         <patcher dir>/Natives
#
# Usage: scripts/package-release.sh
# Refuses to package when libs/Natives or libs/RuntimeDeps are not populated.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFIG=Release

# ---- version from the plugin csproj ------------------------------------------------------
VERSION="$(sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' "$ROOT/src/GloomhavenVR/GloomhavenVR.csproj" | head -1)"
if [[ -z "$VERSION" ]]; then
    echo "error: could not read <Version> from src/GloomhavenVR/GloomhavenVR.csproj" >&2
    exit 1
fi

# ---- preconditions ------------------------------------------------------------------------
NATIVES=("$ROOT/libs/Natives/UnityOpenXR.dll" "$ROOT/libs/Natives/openxr_loader.dll")
for f in "${NATIVES[@]}"; do
    if [[ ! -f "$f" ]]; then
        echo "error: missing native '$f' — run scripts/fetch-natives.sh first." >&2
        exit 1
    fi
done
shopt -s nullglob
RUNTIMEDEPS=("$ROOT"/libs/RuntimeDeps/*.dll)
if [[ ${#RUNTIMEDEPS[@]} -eq 0 ]]; then
    echo "error: libs/RuntimeDeps is empty — run scripts/build-runtimedeps.sh first." >&2
    exit 1
fi

# ---- build --------------------------------------------------------------------------------
if ! command -v dotnet >/dev/null 2>&1 && [[ -x "$HOME/.dotnet/dotnet" ]]; then
    export DOTNET_ROOT="$HOME/.dotnet"
    export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"
fi
dotnet build "$ROOT/GloomhavenVR.sln" -c "$CONFIG" -v minimal

PLUGIN="$ROOT/src/GloomhavenVR/bin/$CONFIG/net472/GloomhavenVR.dll"
PRELOADER="$ROOT/src/GloomhavenVR.Preload/bin/$CONFIG/net472/GloomhavenVR.Preload.dll"
for f in "$PLUGIN" "$PRELOADER"; do
    [[ -f "$f" ]] || { echo "error: build artifact missing: $f" >&2; exit 1; }
done

# ---- stage --------------------------------------------------------------------------------
DIST="$ROOT/dist"
STAGE="$DIST/stage"
ZIP="$DIST/GloomhavenVR-$VERSION.zip"
rm -rf "$STAGE" "$ZIP"
PLUGDIR="$STAGE/BepInEx/plugins/GloomhavenVR"
PATCHDIR="$STAGE/BepInEx/patchers/GloomhavenVR"
mkdir -p "$PLUGDIR/RuntimeDeps" "$PATCHDIR/Natives"

cp "$PLUGIN" "$PLUGDIR/"
cp "${RUNTIMEDEPS[@]}" "$PLUGDIR/RuntimeDeps/"
[[ -f "$ROOT/libs/RuntimeDeps/versions.json" ]] && cp "$ROOT/libs/RuntimeDeps/versions.json" "$PLUGDIR/RuntimeDeps/"
cp "$PRELOADER" "$PATCHDIR/"
cp "${NATIVES[@]}" "$PATCHDIR/Natives/"

# Asset bundle: prefer a freshly built one, else the committed prebuilt copy, else a
# placeholder README at the probe location.
BUNDLE="$ROOT/unity/GloomhavenVR.Assets/Build/Bundles/gloomhavenvr.bundle"
[[ -f "$BUNDLE" ]] || BUNDLE="$ROOT/prebuilt/gloomhavenvr.bundle"
if [[ -f "$BUNDLE" ]]; then
    cp "$BUNDLE" "$PLUGDIR/gloomhavenvr.bundle"
else
    cat > "$PLUGDIR/gloomhavenvr.bundle.README.txt" <<'EOF'
gloomhavenvr.bundle — OPTIONAL asset bundle (hand gloves, card backing, table props).

This release was packaged without it: the mod falls back to procedural hand and
table visuals automatically and is fully functional.

If a bundle is published later, drop the file 'gloomhavenvr.bundle' into THIS folder
(BepInEx/plugins/GloomhavenVR/) — no other change needed; it is probed at runtime.
Developers: build it with scripts/build-bundles.sh (needs Unity 2021.3.x).
EOF
fi

# ---- INSTALL.txt ----------------------------------------------------------------------------
cat > "$STAGE/INSTALL.txt" <<EOF
GloomhavenVR $VERSION — install
================================

Requirements: Gloomhaven (digital, Steam/GOG, v1.1.x), Windows, a PC-VR OpenXR
runtime (Quest Link / Virtual Desktop / Steam Link / SteamVR), BepInEx 5.4.23.5.

1. Install BepInEx 5.4.23.5 (x64):
   https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5
   Extract BepInEx_win_x64_5.4.23.5.zip into the Gloomhaven install folder
   (the folder containing GH.exe). Run the game once flat, quit, and check that
   BepInEx/LogOutput.log now exists.

2. Extract THIS zip into the same Gloomhaven install folder, merging the
   BepInEx/ directory. You should end up with:
     BepInEx/plugins/GloomhavenVR/GloomhavenVR.dll
     BepInEx/plugins/GloomhavenVR/RuntimeDeps/  (Unity XR assemblies)
     BepInEx/patchers/GloomhavenVR/GloomhavenVR.Preload.dll
     BepInEx/patchers/GloomhavenVR/Natives/     (UnityOpenXR + openxr_loader)

3. Make sure your OpenXR runtime is active (Meta: Quest Link app as active
   OpenXR runtime; Virtual Desktop: VDXR; Steam Link: SteamVR), put the headset
   on standby-awake, then start the game normally.

4. First launch writes config files to BepInEx/config/ (dev.gloomhavenvr*.cfg).
   Set [General] Enabled = false in dev.gloomhavenvr.cfg to run 100% vanilla.

Headset black / no VR? Add the launch option -force-d3d11 (desktop OpenXR
needs D3D11). Full troubleshooting: INSTALL.md and docs/TESTING-P1.md in the
project repository.
EOF

# ---- zip + verify ----------------------------------------------------------------------------
mkdir -p "$DIST"
(cd "$STAGE" && zip -q -r "$ZIP" .)
rm -rf "$STAGE"

echo
echo "Packaged: $ZIP"
echo "Contents:"
unzip -l "$ZIP"

# Sanity: the four load-bearing paths must exist in the archive.
for path in \
    "BepInEx/plugins/GloomhavenVR/GloomhavenVR.dll" \
    "BepInEx/plugins/GloomhavenVR/RuntimeDeps/Unity.XR.OpenXR.dll" \
    "BepInEx/patchers/GloomhavenVR/GloomhavenVR.Preload.dll" \
    "BepInEx/patchers/GloomhavenVR/Natives/openxr_loader.dll" \
    "INSTALL.txt"; do
    if ! unzip -l "$ZIP" | grep -q "$path"; then
        echo "error: packaged zip is missing '$path'" >&2
        exit 1
    fi
done
echo
echo "Layout verified (plugin, RuntimeDeps, preloader, natives, INSTALL.txt)."
