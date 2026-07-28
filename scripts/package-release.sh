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
# ONE source of truth for the text a drag-and-drop user reads: packaging/INSTALL.txt.in.
# install.ps1 renders the same template, so the zip it produces and the zip this produces
# cannot describe the install differently — which they silently did before the template
# existed (this file's copy never mentioned the graphics-jobs restart at all).
TEMPLATE="$ROOT/packaging/INSTALL.txt.in"
if [[ ! -f "$TEMPLATE" ]]; then
    echo "error: missing $TEMPLATE" >&2
    exit 1
fi
sed "s/@VERSION@/$VERSION/g" "$TEMPLATE" > "$STAGE/INSTALL.txt"

# The one-click graphics-jobs enabler, at the zip ROOT so it lands next to GH.exe
# when the archive is extracted into the game folder. It is what removes the
# "start the game twice" step for a drag-and-drop install (INSTALL.txt step 4).
cp "$ROOT/packaging/EnableGraphicsJobs.bat" "$ROOT/packaging/EnableGraphicsJobs.ps1" "$STAGE/"

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
    "INSTALL.txt" \
    "EnableGraphicsJobs.bat" \
    "EnableGraphicsJobs.ps1"; do
    if ! unzip -l "$ZIP" | grep -q "$path"; then
        echo "error: packaged zip is missing '$path'" >&2
        exit 1
    fi
done
echo
echo "Layout verified (plugin, RuntimeDeps, preloader, natives, INSTALL.txt)."
