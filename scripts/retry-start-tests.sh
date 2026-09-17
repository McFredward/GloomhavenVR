#!/usr/bin/env bash
# Run production retry lifetime and rig pose restoration with negative controls.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$root/tests/GloomhavenVR.RetryStartTests/GloomhavenVR.RetryStartTests.csproj"
python3 "$root/tests/GloomhavenVR.RetryStartTests/source_contract.py" "$root"
dotnet run --project "$project" --configuration Release
fixture="$(mktemp -d)"
trap 'rm -rf "$fixture"' EXIT
cp "$root/tests/GloomhavenVR.RetryStartTests/"*.cs "$project" "$fixture/"
python3 - "$root" "$fixture" <<'PY'
from pathlib import Path
import sys
root, dest = map(Path, sys.argv[1:])
base = root/'src/GloomhavenVR/Rig'
state = (base/'ScenarioRetrySeat.cs').read_text()
pose = (base/'VRRigDriver.RetryStart.cs').read_text()
board = (root/'src/GloomhavenVR/Cards/Tray/PlayTray.RetryStart.cs').read_text()
for name, original, old, new in [
    ('old-scene', state, 'if (ReferenceEquals(_scenario, scenario))', 'if (scenario == null)'),
    ('no-arm', state, '_retryRequested = true;', '_retryRequested = false;'),
    ('capture-drift', state, 'if (!CanCapture)', 'if (_scenario == null)'),
    ('no-retire', state, '_scenario = null;', 'return;'),
    ('scale', pose, 'root.localScale = Vector3.one * pose.Scale;', ''),
    ('head-offset', pose, 'pose.Head - root.rotation * (head.localPosition * pose.Scale)', 'pose.Head'),
    ('head-yaw', pose, 'pose.HeadYaw * Quaternion.Inverse(YawOnly(head.localRotation))', 'pose.HeadYaw'),
    ('board', pose, 'Cards.PlayTray.RequestRetryReset();', ''),
    ('board-position', board, '_root.position = pose.Position;', ''),
    ('board-parent-scale', board, 'pose.WorldScale / Mathf.Max(1e-4f, parentScale)', 'pose.WorldScale'),
    ('board-owner', board, '&& ReferenceEquals(_retryScenarioOwner, Choreographer.s_Choreographer)', ''),
    ('board-loading', board, '&& !CardsDriver.NativeSceneLoadInProgress', ''),
    ('board-pin', board, '_anchor.RecacheRigLocal(_root);', ''),
    ('board-correction', pose, 'if (_scenarioStart.HasPose && previous.HasBoard)', 'if (previous.Scale < 0)'),
    ('round-restart', pose, 'yield return AccessTools.Method(typeof(SceneController), "RegenerateAndRestartScenario");', ''),
]:
    assert original.count(old) == 1, name
    if name == 'no-retire':
        # Replace method body rather than introducing compiler unreachable-code warnings.
        start = original.index('    internal void LeaveScenario()')
        changed = original[:start] + '    internal void LeaveScenario() { }\n}\n'
    else:
        changed = original.replace(old, new)
    (dest/(name+'.fixture')).write_text(changed)
PY
for mutation in old-scene no-arm capture-drift no-retire scale head-offset head-yaw board board-position board-parent-scale board-owner board-loading board-pin board-correction round-restart; do
    state="$root/src/GloomhavenVR/Rig/ScenarioRetrySeat.cs"
    pose="$root/src/GloomhavenVR/Rig/VRRigDriver.RetryStart.cs"
    board="$root/src/GloomhavenVR/Cards/Tray/PlayTray.RetryStart.cs"
    case "$mutation" in
        old-scene) state="$fixture/$mutation.fixture"; expected='Camera rebuild must retain scenario identity' ;;
        no-arm) state="$fixture/$mutation.fixture"; expected='Teardown callbacks must not replace the original arrival' ;;
        capture-drift) state="$fixture/$mutation.fixture"; expected='Teardown callbacks must not replace the original arrival' ;;
        no-retire) state="$fixture/$mutation.fixture"; expected='Map/menu exit must cancel interrupted retry' ;;
        scale) pose="$fixture/$mutation.fixture"; expected='Retry must restore the original head world position' ;;
        head-offset) pose="$fixture/$mutation.fixture"; expected='Retry must restore the original head world position' ;;
        head-yaw) pose="$fixture/$mutation.fixture"; expected='Retry must absorb changed physical headset yaw' ;;
        board) pose="$fixture/$mutation.fixture"; expected='Each board restore must be consumed once' ;;
        board-position) board="$fixture/$mutation.fixture"; expected='Retry must restore actual original board world position' ;;
        board-parent-scale) board="$fixture/$mutation.fixture"; expected='Retry must preserve original board world size across follow and pinned parents' ;;
        board-owner) board="$fixture/$mutation.fixture"; expected='Outgoing tray must not consume a new scenario restore' ;;
        board-loading) board="$fixture/$mutation.fixture"; expected='Native loading must defer board restore' ;;
        board-pin) board="$fixture/$mutation.fixture"; expected='Pinned board must recache original world pose' ;;
        board-correction) pose="$fixture/$mutation.fixture"; expected='Late ring correction must carry original board without adopting the dragged board' ;;
        round-restart) pose="$fixture/$mutation.fixture"; expected='Both defeat callbacks and preserve-only round restart must be covered' ;;
    esac
    if dotnet run --project "$fixture/GloomhavenVR.RetryStartTests.csproj" --configuration Release \
        --property:RetryStateSource="$state" --property:RetryPoseSource="$pose" --property:RetryBoardSource="$board" > "$fixture/$mutation.log" 2>&1; then
        echo "FAIL: retry $mutation regression escaped coverage." >&2; exit 1
    fi
    if ! rg -Fq "Unhandled exception. System.Exception: $expected" "$fixture/$mutation.log"; then
        cat "$fixture/$mutation.log"; exit 1
    fi
    echo "Retry start negative control: $mutation rejected."
done
