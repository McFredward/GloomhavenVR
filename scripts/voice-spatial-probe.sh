#!/usr/bin/env bash
# GloomhavenVR — VoiceSpatialProbe driver.
#
# Measures Unity's ACTUAL spatialiser output by building a small headless Linux player from a
# generated scene and reading the FINAL MIX back through UnityEngine.AudioRenderer. Nothing here
# reports a setting; every number in the CSV comes from mixed samples.
#
# Usage:
#   scripts/voice-spatial-probe.sh <config.json> [--tag NAME] [--skip-build] [--no-plot]
#
#   <config.json>   run description — see .planning/voice/placeholder.json for the shape.
#   --tag NAME      output basename (default: the config's basename).
#   --skip-build    reuse the player from a previous run instead of rebuilding it.
#   --no-plot       write the CSV only.
#   --capture-mode  audiorenderer (default) | filter. See CAPTURE below.
#
# CAPTURE — WHICH BACKEND ACTUALLY RAN
#   AudioRenderer is the preferred path and it DOES NOT WORK on this box. Measured on Unity
#   2021.3.5f1, Linux standalone, -batchmode -nographics under xvfb-run: Start() returns true,
#   GetSampleCountForCaptureFrame() then returns 0 forever, and AudioSettings.dspTime FREEZES at
#   the value it held when Start() was called — starting the recorder stops the mixer. The probe
#   detects this after 3 s, says so in the log, and falls back to OnAudioFilterRead on the
#   AudioListener's GameObject (post-mix, post-pan). Grep the player log for "PROBE FALLBACK" to
#   see which backend produced a given CSV; do not assume.
#
#   Consequence: the mixer is REAL TIME. A sweep costs (settleSeconds + measureSeconds) x points
#   seconds of wall clock. captureFramerate does not change that (it speeds the game clock only).
#
# Env:
#   UNITY_PATH      editor binary (default /home/claw/unity-2021.3.5/Editor/Unity)
#   PROBE_PYTHON    python with matplotlib (default: auto-detect, else a venv under the debug dir)
#
# Outputs (per tag):
#   .planning/debug/voice/<tag>.csv          the measurement            (gitignored scratch)
#   .planning/debug/voice/<tag>.png          the plot, controls included (gitignored scratch)
#   .planning/debug/voice/<tag>-player.log   the player's own transcript — READ IT, it carries the
#                                            actual sample rate, channel count and peak sample
#   .planning/debug/voice/<tag>-build.log    the Unity build transcript
#   .planning/voice/<tag>.csv / .png         tracked copies of the two deliverables
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT_PATH="$REPO_ROOT/unity/GloomhavenVR.Assets"
UNITY_PATH="${UNITY_PATH:-/home/claw/unity-2021.3.5/Editor/Unity}"

DEBUG_DIR="$REPO_ROOT/.planning/debug/voice"
KEEP_DIR="$REPO_ROOT/.planning/voice"
PLAYER_DIR="$DEBUG_DIR/player"
PLAYER_BIN="$PLAYER_DIR/VoiceSpatialProbe"

CONFIG=""
TAG=""
SKIP_BUILD=0
DO_PLOT=1
CAPTURE_MODE="audiorenderer"

while [[ $# -gt 0 ]]; do
    case "$1" in
        --tag)          TAG="$2"; shift 2 ;;
        --capture-mode) CAPTURE_MODE="$2"; shift 2 ;;
        --skip-build) SKIP_BUILD=1; shift ;;
        --no-plot)    DO_PLOT=0; shift ;;
        -h|--help)    sed -n '2,30p' "${BASH_SOURCE[0]}"; exit 0 ;;
        *)            CONFIG="$1"; shift ;;
    esac
done

if [[ -z "$CONFIG" ]]; then
    echo "ERROR: no config given. Usage: $0 <config.json> [--tag NAME] [--skip-build] [--no-plot]" >&2
    exit 2
fi
[[ -f "$CONFIG" ]] || { echo "ERROR: config not found: $CONFIG" >&2; exit 2; }
CONFIG="$(cd "$(dirname "$CONFIG")" && pwd)/$(basename "$CONFIG")"
[[ -n "$TAG" ]] || TAG="$(basename "${CONFIG%.json}")"
[[ -x "$UNITY_PATH" ]] || { echo "ERROR: UNITY_PATH is not executable: $UNITY_PATH" >&2; exit 2; }

mkdir -p "$DEBUG_DIR" "$KEEP_DIR" "$PLAYER_DIR"
CSV="$DEBUG_DIR/$TAG.csv"
PNG="$DEBUG_DIR/$TAG.png"
PLAYER_LOG="$DEBUG_DIR/$TAG-player.log"
BUILD_LOG="$DEBUG_DIR/$TAG-build.log"

echo "[voice-probe] repo:    $REPO_ROOT"
echo "[voice-probe] unity:   $UNITY_PATH"
echo "[voice-probe] config:  $CONFIG"
echo "[voice-probe] csv:     $CSV"

# ---- 1. build the player -----------------------------------------------------------------------
if [[ $SKIP_BUILD -eq 1 && -x "$PLAYER_BIN" ]]; then
    echo "[voice-probe] build:   skipped, reusing $PLAYER_BIN"
else
    echo "[voice-probe] build:   $PLAYER_BIN (log $BUILD_LOG)"
    # Two attempts, because the XR packages bootstrap themselves on a project that has never built
    # a player: com.unity.xr.management CREATES Assets/XR/XRGeneralSettingsPerBuildTarget.asset from
    # inside OnPreprocessBuild, and that asset creation fires a project-changed callback that trips
    # com.unity.inputsystem's static constructor ("Invalid value of 'activeInputHandler' setting:
    # -1", because this project's ProjectSettings.asset has no activeInputHandler key at all). The
    # exception escapes the build preprocessor and fails the build. Once those assets exist the
    # second attempt goes straight through. The OpenXR half of the same problem is handled inside
    # VoiceSpatialProbeBuilder.PreloadOpenXrSettings.
    BUILD_STATUS=1
    for attempt in 1 2; do
        VOICE_PROBE_PLAYER="$PLAYER_BIN" \
        "$UNITY_PATH" \
            -batchmode \
            -nographics \
            -projectPath "$PROJECT_PATH" \
            -buildTarget Linux64 \
            -executeMethod GloomhavenVR.VoiceProbe.VoiceSpatialProbeBuilder.BuildPlayer \
            -logFile "$BUILD_LOG"
        BUILD_STATUS=$?
        [[ $BUILD_STATUS -eq 0 && -x "$PLAYER_BIN" ]] && break
        echo "[voice-probe] build attempt $attempt failed (exit $BUILD_STATUS); XR bootstrap assets may now exist — retrying" >&2
    done
    if [[ $BUILD_STATUS -ne 0 || ! -x "$PLAYER_BIN" ]]; then
        echo "[voice-probe] BUILD FAILED (exit $BUILD_STATUS). Last lines of $BUILD_LOG:" >&2
        tail -n 40 "$BUILD_LOG" >&2 || true
        exit 3
    fi
fi

# ---- 2. run it ---------------------------------------------------------------------------------
# -nographics is fine: the probe never renders. xvfb-run is belt and braces for the Unity player's
# startup path on a box with no X display.
rm -f "$CSV"
echo "[voice-probe] run:     $PLAYER_BIN (log $PLAYER_LOG)"
xvfb-run -a "$PLAYER_BIN" \
    -batchmode \
    -nographics \
    -logFile "$PLAYER_LOG" \
    --probe-config "$CONFIG" \
    --probe-out "$CSV" \
    --capture-mode "$CAPTURE_MODE"
RUN_STATUS=$?
echo "[voice-probe] player exited with $RUN_STATUS"

# The player's own health lines. These are the difference between a measurement and a decoration.
# PROBE DONE carries the peak absolute sample over the whole run: if it is 0, the CSV is a table of
# silence no matter how plausible the columns look.
grep -E "PROBE (mode|audio|config|clip|capture|AudioRenderer|FALLBACK|dsp clock|DONE|WARNING|FAIL|CSV)" \
     "$PLAYER_LOG" 2>/dev/null || true

if [[ ! -s "$CSV" ]]; then
    echo "[voice-probe] NO CSV WAS WRITTEN. Tail of $PLAYER_LOG:" >&2
    tail -n 60 "$PLAYER_LOG" >&2 || true
    exit 4
fi

ROWS=$(( $(wc -l < "$CSV") - 1 ))
echo "[voice-probe] rows:    $ROWS"
cp -f "$CSV" "$KEEP_DIR/$TAG.csv"

# ---- 3. plot -----------------------------------------------------------------------------------
if [[ $DO_PLOT -eq 1 ]]; then
    PY="${PROBE_PYTHON:-}"
    if [[ -z "$PY" ]]; then
        for c in python3 /usr/bin/python3; do
            if command -v "$c" >/dev/null 2>&1 && "$c" -c "import matplotlib" >/dev/null 2>&1; then PY="$c"; break; fi
        done
    fi
    if [[ -z "$PY" ]]; then
        VENV="$DEBUG_DIR/.venv"
        if [[ ! -x "$VENV/bin/python" ]]; then
            echo "[voice-probe] no python with matplotlib; creating $VENV"
            python3 -m venv "$VENV" && "$VENV/bin/pip" install --quiet matplotlib
        fi
        [[ -x "$VENV/bin/python" ]] && PY="$VENV/bin/python"
    fi
    if [[ -n "$PY" ]]; then
        # The provenance note is READ OUT OF THE PLAYER LOG, not asserted: which backend actually
        # produced these samples is a fact about the run, and the picture should carry the fact
        # rather than the intention.
        if grep -q "PROBE FALLBACK" "$PLAYER_LOG" 2>/dev/null; then
            BACKEND="captured via OnAudioFilterRead on the AudioListener (AudioRenderer failed on this platform — see PROBE FALLBACK in the player log)"
        elif grep -q "PROBE capture: ListenerTap" "$PLAYER_LOG" 2>/dev/null; then
            BACKEND="captured via OnAudioFilterRead on the AudioListener (--capture-mode filter)"
        elif grep -q "PROBE AudioRenderer.Start() ok" "$PLAYER_LOG" 2>/dev/null; then
            BACKEND="captured via UnityEngine.AudioRenderer"
        else
            BACKEND="capture backend not identified in the player log"
        fi
        PEAK=$(grep -o "peak |sample| over the whole run [0-9.]*" "$PLAYER_LOG" 2>/dev/null | tail -1)
        echo "[voice-probe] plot:    $PNG (via $PY)"
        "$PY" "$REPO_ROOT/scripts/voice-spatial-probe-plot.py" "$CSV" "$PNG" "$BACKEND${PEAK:+   ·   $PEAK}" \
            && cp -f "$PNG" "$KEEP_DIR/$TAG.png"
    else
        echo "[voice-probe] WARNING: no matplotlib available; CSV written, no plot." >&2
    fi
fi

echo "[voice-probe] done."
echo "[voice-probe]   CSV  $CSV"
echo "[voice-probe]   CSV  $KEEP_DIR/$TAG.csv"
[[ -f "$PNG" ]] && echo "[voice-probe]   PNG  $PNG" && echo "[voice-probe]   PNG  $KEEP_DIR/$TAG.png"
exit "$RUN_STATUS"
