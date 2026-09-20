#!/usr/bin/env bash
# GloomhavenVR — Unity batch-mode driver for the companion asset project.
#
# Usage:
#   UNITY_PATH=/path/to/2021.3.5f1/Editor/Unity(.exe) ./scripts/build-bundles.sh [bundles|town|harvest]
#
#   bundles  (default) build Assets/Bundle/** -> unity/GloomhavenVR.Assets/Build/Bundles/gloomhavenvr.bundle
#   harvest  dummy Windows Mono player build + collect XR RuntimeDeps/natives -> libs/
#   town     pack authored town assets -> Build/TownServices/ghvr-town.bundle
#
# Typical UNITY_PATH values:
#   Windows (Git Bash): "/c/Program Files/Unity/Hub/Editor/2021.3.5f1/Editor/Unity.exe"
#   Linux:              "$HOME/Unity/Hub/Editor/2021.3.5f1/Editor/Unity"
#     (harvest additionally needs the "Windows Build Support (Mono)" editor module)
#
# Notes:
#  - The invoked methods call EditorApplication.Exit themselves — do not add -quit.
#  - -nographics is fine for both jobs; if bundle shader compilation ever
#    misbehaves, retry without it.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT_PATH="$REPO_ROOT/unity/GloomhavenVR.Assets"
CMD="${1:-bundles}"

if [[ -z "${UNITY_PATH:-}" ]]; then
    echo "ERROR: UNITY_PATH is not set." >&2
    echo "Point it at a Unity 2021.3.5f1 editor binary (see unity/HARVESTING.md §2)." >&2
    exit 2
fi
if [[ ! -e "$UNITY_PATH" ]]; then
    echo "ERROR: UNITY_PATH does not exist: $UNITY_PATH" >&2
    exit 2
fi

case "$CMD" in
    bundles) METHOD="GloomhavenVR.AssetsBuilder.BuildAll"           LOG="$REPO_ROOT/build-bundles.log" ;;
    harvest) METHOD="GloomhavenVR.RuntimeDepsHarvester.BuildAndHarvest" LOG="$REPO_ROOT/harvest.log" ;;
    town) METHOD="GloomhavenVR.TownServicesBuilder.BuildBundle" LOG="$REPO_ROOT/build-town-bundle.log" ;;
    *) echo "ERROR: unknown command '$CMD' (use: bundles | town | harvest)" >&2; exit 2 ;;
esac

echo "[build-bundles] project: $PROJECT_PATH"
echo "[build-bundles] method:  $METHOD"
echo "[build-bundles] log:     $LOG"

EXIT_ARGS=()
if [[ "$CMD" == "town" ]]; then EXIT_ARGS=(-quit); fi
"$UNITY_PATH" "${EXIT_ARGS[@]}" \
    -batchmode \
    -nographics \
    -projectPath "$PROJECT_PATH" \
    -buildTarget Win64 \
    -executeMethod "$METHOD" \
    -logFile "$LOG"

STATUS=$?
echo "[build-bundles] Unity exited with $STATUS"
if [[ "$CMD" == "bundles" ]]; then
    echo "[build-bundles] output: $PROJECT_PATH/Build/Bundles/gloomhavenvr.bundle"
elif [[ "$CMD" == "town" ]]; then
    echo "[build-bundles] output: $PROJECT_PATH/Build/TownServices/ghvr-town.bundle"
else
    echo "[build-bundles] output: $REPO_ROOT/libs/RuntimeDeps + $REPO_ROOT/libs/Natives"
fi
exit "$STATUS"
