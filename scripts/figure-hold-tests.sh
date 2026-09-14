#!/usr/bin/env bash
# Exercise native figure handover and real receive/local-release policies without Unity.
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if ! command -v dotnet >/dev/null 2>&1 && [[ -x "$HOME/.dotnet/dotnet" ]]; then
    export DOTNET_ROOT="$HOME/.dotnet"
fi
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$repo_root/tests/GloomhavenVR.FigureHoldTests/GloomhavenVR.FigureHoldTests.csproj"
dotnet run --project "$project" --configuration Release
mutation_dir="$(mktemp -d)"
trap 'rm -rf "$mutation_dir"' EXIT
for mutation in native-origin late-held-sample local-forced-glide idle-run-motion attack-participant animation-failure-guard; do
    python3 - "$repo_root" "$mutation_dir" "$mutation" <<'PY'
import pathlib
import sys
root, temporary = map(pathlib.Path, sys.argv[1:3])
mutation = sys.argv[3]
changes = {
    'native-origin': ('Board/FigureGrab/ActorBehaviour_HeldTransform_Patch.cs',
        'private static void SetLocoTarget_Prefix(ActorBehaviour __instance) => ReleaseForNativeAction(__instance);',
        'private static void SetLocoTarget_Prefix(ActorBehaviour __instance) => _ = __instance;'),
    'late-held-sample': ('Net/NetFigures.cs',
        'if (rec.ActionReleased)\n            return true; // retain quarantine even if the scene actor is destroyed or replaced',
        'if (rec.ActionReleased)\n            rec.ActionReleased = false; // injected stale pose revival'),
    'local-forced-glide': ('Board/FigureGrab/FigureGrabbable.cs',
        'HandSide? side = _holder != null ? _holder.Side : (HandSide?)null;\n        Restore();',
        'HandSide? side = _holder != null ? _holder.Side : (HandSide?)null;\n        if (TryBeginGlide()) return;\n        Restore();'),
    'idle-run-motion': ('Board/FigureGrab/FigureBusy.cs',
        'if (actor.IsMoving || actor.m_NewLocoTarget || actor.m_Jump || actor.m_IsPushPullInProgress\n            || actor.m_Teleport)',
        'if (bool.Parse("false"))'),
    'animation-failure-guard': ('Board/FigureGrab/MF_HeldFigureAnimation_Patch.cs',
        'catch (Exception error)',
        'catch (Exception error) when (error is ArgumentException)'),
    'attack-participant': ('Board/FigureGrab/Choreographer_HeldFigureAction_Patch.cs',
        'Release(choreographer, attacking.m_AttackingActor);',
        '// Release(choreographer, attacking.m_AttackingActor);'),
}
relative, needle, replacement = changes[mutation]
source = (root / 'src/GloomhavenVR' / relative).read_text()
assert source.count(needle) == 1, 'Figure mutation seam changed: ' + mutation
(temporary / 'mutant.cs').write_text(source.replace(needle, replacement))
PY
    case "$mutation" in
        native-origin) property=PatchSource; expected='Native movement must sample board origin, not the hand pose' ;;
        late-held-sample) property=FigureSource; expected='Stale hold must not resume after native action returns idle' ;;
        local-forced-glide) property=LocalSource; expected='Local forced release must be immediate, never a release glide' ;;
        idle-run-motion) property=BusySource; expected='Native locomotion flag must release even within Idle-Run' ;;
        animation-failure-guard) property=AnimationSource; expected='injected animator probe failure' ;;
        attack-participant) property=MessageSource; expected='Attack must restore attacker and actual target before native facing reads' ;;
    esac
    if dotnet run --project "$project" --configuration Release \
        --property:"$property=$mutation_dir/mutant.cs" > "$mutation_dir/output.log" 2>&1; then
        cat "$mutation_dir/output.log"
        echo "FAIL: $mutation escaped the figure hold regression test." >&2
        exit 1
    fi
    failure_signature="Unhandled exception. System.InvalidOperationException: $expected"
    if [[ "$mutation" == animation-failure-guard ]]; then
        failure_signature='Unhandled exception. System.Reflection.TargetInvocationException:'
    fi
    if ! grep -Fq "$failure_signature" "$mutation_dir/output.log"         || ! grep -Fq "$expected" "$mutation_dir/output.log"; then
        cat "$mutation_dir/output.log"
        echo "FAIL: $mutation did not reach the injected runtime defect." >&2
        exit 1
    fi
    echo "Figure hold negative control: $mutation failed as expected."
done
