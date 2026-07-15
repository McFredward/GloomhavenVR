#!/usr/bin/env bash
# Build the PROVISIONAL managed XR RuntimeDeps (libs/RuntimeDeps/) from
# needle-mirror package source at pinned tags.
#
#   com.unity.xr.management  4.5.0  -> Unity.XR.Management.dll
#   com.unity.xr.core-utils  2.2.3  -> Unity.XR.CoreUtils.dll
#   com.unity.xr.openxr      1.10.0 -> Unity.XR.OpenXR.dll
#
# These exist so the plugin can COMPILE against the XR APIs before the human
# editor-build harvest (unity/HARVESTING.md) produces the runtime-blessed set,
# which replaces these files 1:1. See tools/RuntimeDepsBuild/README.md.
#
# Requirements: .NET SDK 8+, git, GameManaged configured (Directory.Build.props.user)
# Usage: scripts/build-runtimedeps.sh [--refetch]
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TOOLS="$ROOT/tools/RuntimeDepsBuild"
SOURCES="$TOOLS/sources"
DEST="$ROOT/libs/RuntimeDeps"

# package -> pinned needle-mirror tag
PACKAGES=(
    "com.unity.xr.management 4.5.0"
    "com.unity.xr.core-utils 2.2.3"
    "com.unity.xr.openxr 1.10.0"
)
PROJECTS=(
    "Unity.XR.Management"
    "Unity.XR.CoreUtils"
    "Unity.XR.OpenXR"
)

if ! command -v dotnet >/dev/null 2>&1 && [[ -x "$HOME/.dotnet/dotnet" ]]; then
    export DOTNET_ROOT="$HOME/.dotnet"
    export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"
fi
command -v dotnet >/dev/null 2>&1 || { echo "error: dotnet SDK not found" >&2; exit 1; }
command -v git >/dev/null 2>&1 || { echo "error: git not found" >&2; exit 1; }

# --- 1. fetch sources (pinned tags) ---------------------------------------
# NOTE: fetch refs/tags/<tag> EXPLICITLY. needle-mirror repos also carry
# BRANCHES with version-like names that point at *different* snapshots
# (e.g. com.unity.xr.core-utils has branch "2.2.3" = the 2.3.0 snapshot,
# while tag "2.2.3" is the real 2.2.3) — `git clone --branch` would pick
# the branch and silently give the wrong package version.
mkdir -p "$SOURCES"
for entry in "${PACKAGES[@]}"; do
    read -r pkg tag <<<"$entry"
    dir="$SOURCES/$pkg"
    if [[ -d "$dir" && "${1:-}" == "--refetch" ]]; then
        rm -rf "$dir"
    fi
    if [[ -d "$dir" ]]; then
        have="$(sed -n 's/.*"version": "\([^"]*\)".*/\1/p' "$dir/package.json" 2>/dev/null | head -1)"
        if [[ "$have" == "$tag" ]]; then
            echo "sources  $pkg@$tag (cached)"
            continue
        fi
        echo "sources  $pkg: cached checkout is '${have:-unknown}', want '$tag' — refetching"
        rm -rf "$dir"
    fi
    echo "sources  fetching $pkg tag $tag"
    mkdir -p "$dir"
    git -C "$dir" init -q
    git -C "$dir" remote add origin "https://github.com/needle-mirror/$pkg.git"
    git -C "$dir" fetch -q --depth 1 origin "refs/tags/$tag"
    git -C "$dir" checkout -q FETCH_HEAD
    have="$(sed -n 's/.*"version": "\([^"]*\)".*/\1/p' "$dir/package.json" | head -1)"
    if [[ "$have" != "$tag" ]]; then
        echo "error: $pkg tag $tag checked out but package.json says '$have'" >&2
        exit 1
    fi
done

# --- 2. build --------------------------------------------------------------
for proj in "${PROJECTS[@]}"; do
    echo "build    $proj"
    dotnet build "$TOOLS/$proj/$proj.csproj" -c Release --nologo -v quiet
done

# --- 3. collect into libs/RuntimeDeps --------------------------------------
mkdir -p "$DEST"
for proj in "${PROJECTS[@]}"; do
    cp "$TOOLS/$proj/bin/Release/net472/$proj.dll" "$DEST/$proj.dll"
done

# --- 4. record provenance ---------------------------------------------------
cat > "$DEST/versions.json" <<EOF
{
  "kind": "provisional (compiled from needle-mirror source outside Unity)",
  "builtUtc": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
  "targetUnity": "2021.3.5f1 (UnityEngine.Modules 2021.3.5 reference assemblies)",
  "targetInputSystem": "1.3.0 (game-shipped Unity.InputSystem.dll)",
  "packages": {
    "com.unity.xr.management": "4.5.0",
    "com.unity.xr.core-utils": "2.2.3",
    "com.unity.xr.openxr": "1.10.0"
  },
  "assemblies": {
$(first=1; for proj in "${PROJECTS[@]}"; do
    [[ $first -eq 0 ]] && printf ',\n'
    first=0
    printf '    "%s.dll": { "sha256": "%s" }' "$proj" "$(sha256sum "$DEST/$proj.dll" | cut -d' ' -f1)"
done)
  },
  "replacedBy": "editor-harvested set from unity/HARVESTING.md (drop-in, same file names)"
}
EOF

echo
echo "RuntimeDeps ready in libs/RuntimeDeps/:"
ls -l "$DEST"/*.dll
echo
echo "Provenance: $DEST/versions.json"
