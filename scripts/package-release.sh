#!/usr/bin/env bash
# GloomhavenVR — release packaging (Phase 5, MISSION C.1).
#
# Builds Release and assembles dist/GloomhavenVR-<version>.zip with the EXACT
# install layout the runtime expects (paths verified against the code):
#
#   INSTALL.txt                                        <- packaging/INSTALL.txt.in
#   INSTALL-DEUTSCH.txt                                <- packaging/INSTALL.de.txt.in
#   BepInEx/plugins/GloomhavenVR/GloomhavenVR.dll
#   BepInEx/plugins/GloomhavenVR/RuntimeDeps/*.dll     <- RuntimeDepsLoader.RuntimeDepsDir
#                                                         (= <plugin dir>/RuntimeDeps)
#   BepInEx/plugins/GloomhavenVR/gloomhavenvr.bundle   <- HandVisuals/WorldUIAssets probe
#                                                         (<plugin dir>/gloomhavenvr.bundle).
#                                                         REQUIRED: every 3D asset and every
#                                                         bundled shader lives in it. When no
#                                                         bundle can be found the script warns
#                                                         loudly and ships a README in its place
#                                                         (packaging/gloomhavenvr.bundle.README.txt)
#                                                         that says so in both languages.
#   BepInEx/plugins/GloomhavenVR/THIRD-PARTY.txt       <- packaging/THIRD-PARTY.txt
#   BepInEx/patchers/GloomhavenVR/GloomhavenVR.Preload.dll
#   BepInEx/patchers/GloomhavenVR/Natives/*.dll        <- Patcher.InstallNatives reads
#                                                         <patcher dir>/Natives
#
# EVERY .txt IN THE ZIP IS WRITTEN AS UTF-8 WITH BOM AND CRLF LINE ENDINGS (stage_text below),
# and scripts/check-package-text.py fails the run if one is not. The reader is a Windows user
# who double-clicks a .txt: without the BOM, legacy Notepad, WordPad, the 7-Zip and WinRAR
# viewers and the Explorer preview pane decode the file as the ANSI code page and the German
# guide's "raumgroßes" renders as "raumgroÃŸes" (reported 2026-09-03). The templates in
# packaging/ stay plain UTF-8 + LF in git; the conversion happens here, at staging.
#
# Usage: scripts/package-release.sh
# Developer local bundle opt-in: GHVR_USE_LOCAL_BUNDLE=1 scripts/package-release.sh
# Refuses to package when libs/Natives or libs/RuntimeDeps are not populated.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFIG=Release

# An ignored Unity output can be older than the tracked asset set. Use the committed
# bundle unless the developer explicitly requests their local build. Validate that
# request before building or replacing anything in dist/.
BUNDLE="$ROOT/prebuilt/gloomhavenvr.bundle"
if [[ "${GHVR_USE_LOCAL_BUNDLE:-0}" == "1" ]]; then
    BUNDLE="$ROOT/unity/GloomhavenVR.Assets/Build/Bundles/gloomhavenvr.bundle"
    if [[ ! -f "$BUNDLE" ]]; then
        echo "error: GHVR_USE_LOCAL_BUNDLE=1 requested a missing local bundle: $BUNDLE" >&2
        exit 1
    fi
fi
echo "Asset bundle source: $BUNDLE"

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

# ---- text files a Windows user double-clicks -------------------------------------------------
# stage_text SRC DEST: render @VERSION@ and write DEST as UTF-8 WITH BOM and CRLF line endings,
# whatever SRC had. See the header for why. LC_ALL=C keeps sed byte-transparent: under a UTF-8
# locale a multibyte-aware sed can reject or mangle bytes it considers invalid, and under any
# locale the escapes below are bytes, not characters. Any BOM or CR already in SRC is stripped
# first so the result is the same whichever line-ending convention the template was saved with.
stage_text() {
    local src="$1" dest="$2"
    printf '\xEF\xBB\xBF' > "$dest"
    LC_ALL=C sed -e '1s/^\xEF\xBB\xBF//' -e 's/\r$//' -e "s/@VERSION@/$VERSION/g" -e 's/$/\r/' "$src" >> "$dest"
}

# Software licences travel with release packages and local installations. Keep them
# below BepInEx so existing in-game updaters accept the package without a protocol change.
stage_text "$ROOT/LICENSE" "$PLUGDIR/LICENSE.txt"
mkdir -p "$PLUGDIR/Licenses"
for notice in "$ROOT"/packaging/licenses/*.txt; do
    stage_text "$notice" "$PLUGDIR/Licenses/$(basename "$notice")"
done

# Copy the explicitly selected asset bundle.
#
# THE BUNDLE IS REQUIRED. Every 3D asset the mod draws (hands, control board, card backing,
# map table, head avatars, environments, controller models) and every shader it ships live in
# it; without the file the mod starts, logs an Alert per subsystem and degrades to procedural
# placeholders everywhere at once. A zip without it is not a release. The script still
# completes, so a developer can package a DLL-only build on a checkout that has no bundle,
# but it says so on stderr and ships a bilingual README at the probe location that says the
# same to the player — the previous README called the bundle OPTIONAL, which it never was.
if [[ -f "$BUNDLE" ]]; then
    cp "$BUNDLE" "$PLUGDIR/gloomhavenvr.bundle"
    # The bundle carries third-party art (the WebXR Input Profiles controller models,
    # MIT). That licence requires its notice to travel with the copies, and the bundle
    # builder deliberately excludes .txt files from the archive itself, so the notice
    # ships beside it.
    stage_text "$ROOT/packaging/THIRD-PARTY.txt" "$PLUGDIR/THIRD-PARTY.txt"
else
    echo "WARNING: selected bundle is missing: $BUNDLE" >&2
    echo "         The bundle is REQUIRED — this zip is INCOMPLETE and must not" >&2
    echo "         be published. Shipping packaging/gloomhavenvr.bundle.README.txt in its place." >&2
    stage_text "$ROOT/packaging/gloomhavenvr.bundle.README.txt" "$PLUGDIR/gloomhavenvr.bundle.README.txt"
fi

# ---- INSTALL.txt / INSTALL-DEUTSCH.txt -------------------------------------------------------
# ONE source of truth per language for the text a drag-and-drop user reads:
# packaging/INSTALL.txt.in and packaging/INSTALL.de.txt.in. install.ps1 renders the same
# templates, so the zip it produces and the zip this produces cannot describe the install
# differently — which they silently did before the template existed (this file's copy never
# mentioned the graphics-jobs restart at all).
#
# BOTH ship. The mod's own UI is English and German, so a German player must not have to read
# the install through English; each file's first body line names the other one, so opening the
# wrong one costs a glance rather than a search.
TEMPLATE="$ROOT/packaging/INSTALL.txt.in"
TEMPLATE_DE="$ROOT/packaging/INSTALL.de.txt.in"
for f in "$TEMPLATE" "$TEMPLATE_DE"; do
    if [[ ! -f "$f" ]]; then
        echo "error: missing $f" >&2
        exit 1
    fi
done
# Through stage_text, never a bare `sed > file`: that wrote the template's raw UTF-8 bytes with
# no BOM and LF endings, which is what a Windows viewer without a BOM heuristic reads as ANSI.
stage_text "$TEMPLATE"    "$STAGE/INSTALL.txt"
stage_text "$TEMPLATE_DE" "$STAGE/INSTALL-DEUTSCH.txt"

# NO graphics-jobs enabler ships any more. The preloader writes boot.config itself
# and restarts the game once on the boot that needs it, so a script whose whole job
# was to write the same two keys beforehand only offered the user a way to do by
# hand what already happens. Undo is [Core] EnableGraphicsJobs = false, or the
# backup the preloader leaves beside boot.config (both in INSTALL.txt).

# ---- zip + verify ----------------------------------------------------------------------------
mkdir -p "$DIST"
(cd "$STAGE" && zip -q -r "$ZIP" .)
rm -rf "$STAGE"

echo
echo "Packaged: $ZIP"
echo "Contents:"
unzip -l "$ZIP"

# Sanity: the load-bearing paths must exist in the archive.
#
# THE LISTING IS TAKEN ONCE, INTO A FILE, AND NOT PIPED INTO grep -q. This script runs under
# `set -euo pipefail`, and `unzip -l "$ZIP" | grep -q PATH` makes grep exit at the FIRST match,
# which SIGPIPEs unzip (141) and — with pipefail — makes the whole pipeline report failure. The
# test then reads FALSE for a path that IS in the archive, i.e. it refuses to publish a correct
# release. It has not fired because this archive lists ~15 entries and they fit in the pipe
# buffer before grep exits; a zip with a few thousand entries would fail on the first path it
# checked. Exactly the defect check-mirrors.sh PART 3 documents under "HERESTRINGS, NOT PIPES",
# where it DID fire and made a companion test read false on every file that had the companion.
LISTING="$(unzip -l "$ZIP")"
for path in \
    "BepInEx/plugins/GloomhavenVR/GloomhavenVR.dll" \
    "BepInEx/plugins/GloomhavenVR/LICENSE.txt" \
    "BepInEx/plugins/GloomhavenVR/Licenses/SOURCES.txt" \
    "BepInEx/plugins/GloomhavenVR/RuntimeDeps/Unity.XR.OpenXR.dll" \
    "BepInEx/patchers/GloomhavenVR/GloomhavenVR.Preload.dll" \
    "BepInEx/patchers/GloomhavenVR/Natives/openxr_loader.dll" \
    "INSTALL.txt" \
    "INSTALL-DEUTSCH.txt"; do
    if ! grep -qF -- "$path" <<<"$LISTING"; then
        echo "error: packaged zip is missing '$path'" >&2
        exit 1
    fi
done

# Every text file in the archive must open cleanly on Windows: valid UTF-8, BOM, CRLF, and no
# double-encoded umlauts. Fails the run — a zip that renders "raumgroÃŸes" is not a release.
python3 "$ROOT/scripts/check-package-text.py" "$ZIP"

echo
echo "Layout verified (plugin, RuntimeDeps, preloader, natives, INSTALL.txt + INSTALL-DEUTSCH.txt)."
