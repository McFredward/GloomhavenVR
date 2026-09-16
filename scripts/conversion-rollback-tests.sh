#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$repo_root/tests/GloomhavenVR.ConversionRollbackTests/GloomhavenVR.ConversionRollbackTests.csproj"
mutation_dir="$(mktemp -d)"
trap 'rm -rf "$mutation_dir"' EXIT
python3 "$repo_root/tests/GloomhavenVR.ConversionRollbackTests/extract-rollback.py" "$repo_root" "$mutation_dir/Rollback.fixture"
dotnet run --project "$project" --configuration Release --property:RollbackSource="$mutation_dir/Rollback.fixture"
for mutation in lost-transaction lost-modal-cleanup lost-enrollment lost-retry stale-native-retry silent-refusal unsafe-host-destroy lost-camera-snapshot; do
    python3 - "$mutation_dir/Rollback.fixture" "$mutation_dir/Rollback.mutant" "$mutation" <<'PY'
from pathlib import Path
import sys
source=Path(sys.argv[1]).read_text()
changes={
    'lost-transaction': ('if (!_complete) RollbackFailedConversion(_panel);', 'if (!_complete) { }'),
    'lost-modal-cleanup': ('CanvasConversion.RollbackFailedConversion(panel, grab);', '{}'),
    'lost-enrollment': ('if (wp != null) Converted.Remove(wp);', 'if (wp != null) { }'),
    'lost-retry': ('if (pending.Reported) return;', 'FailedConversions.Remove(pending);\n        if (pending.Reported) return;'),
    'stale-native-retry': ('if (!pending.NativeRestored)', 'if (bool.Parse("true"))'),
    'silent-refusal': ('if (!NativeConversionHomeRestored(panel)) return;', 'if (bool.Parse("false")) return;'),
    'unsafe-host-destroy': ('if (!HostHoldsGameContent(host, out string blocker))', 'if (!HostHoldsGameContent(host, out string blocker) || bool.Parse("true"))'),
    'lost-camera-snapshot': ('bool cameraWasWrong = RestoreAdoptedCameras("release");', 'panel.AdoptedCanvases.Clear();\n        bool cameraWasWrong = RestoreAdoptedCameras("release");'),
}
before,after=changes[sys.argv[3]]
assert source.count(before)==1, 'Rollback mutation seam changed: '+sys.argv[3]
Path(sys.argv[2]).write_text(source.replace(before,after))
PY
    case "$mutation" in
        lost-transaction) expected='Rollback restores original layout' ;;
        lost-modal-cleanup) expected='Failed attachment destroys its partial mod chrome' ;;
        lost-enrollment) expected='Failed modal enrollment must be removed' ;;
        lost-retry) expected='Failed restoration retains rollback ownership' ;;
        stale-native-retry) expected='Chrome retries must not repeat successful native restoration' ;;
        silent-refusal) expected='Silently refused detach must retain native restoration ownership' ;;
        unsafe-host-destroy) expected='Refused detach must defer host destruction without deleting native content' ;;
        lost-camera-snapshot) expected='Failed camera restoration retains original camera snapshots' ;;
    esac
    if dotnet run --project "$project" --configuration Release --property:RollbackSource="$mutation_dir/Rollback.mutant" > "$mutation_dir/mutant.log" 2>&1; then
        cat "$mutation_dir/mutant.log"
        echo "FAIL: $mutation escaped conversion rollback test." >&2
        exit 1
    fi
    if ! rg -Fq "$expected" "$mutation_dir/mutant.log"; then
        cat "$mutation_dir/mutant.log"
        exit 1
    fi
    echo "Conversion rollback runtime negative control: $mutation rejected."
done
# Exercise the placement check itself: moving ownership after the native reparent
# must fail even though the independently executable transaction remains correct.
python3 - "$repo_root" "$mutation_dir" <<'PY'
from pathlib import Path
import subprocess,sys
root,out=map(Path,sys.argv[1:])
for rel in ('Conversion/CanvasConversion.1.Core.cs','Conversion/CanvasConversion.2.Adopt.cs','Conversion/CanvasConversion.4.Lifecycle.cs','Conversion/CanvasConversion.6.Hide.cs','Modal/ModalFallback.8.Convert.cs'):
    destination=out/'src/GloomhavenVR/WorldUI'/rel
    destination.parent.mkdir(parents=True,exist_ok=True)
    destination.write_text((root/'src/GloomhavenVR/WorldUI'/rel).read_text())
core=out/'src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.1.Core.cs'
source=core.read_text()
source=source.replace('panel.HostGo = hostGo;', '')
source=source.replace('target.SetParent(hostRect, worldPositionStays: false);', 'target.SetParent(hostRect, worldPositionStays: false);\n        panel.HostGo = hostGo;')
core.write_text(source)
result=subprocess.run([sys.executable,str(root/'tests/GloomhavenVR.ConversionRollbackTests/extract-rollback.py'),str(out),str(out/'LateOwnership.fixture')],capture_output=True,text=True)
assert result.returncode and 'ownership must precede native mutation' in result.stderr, result.stdout+result.stderr
print('Conversion rollback binding negative control: late-host-ownership rejected.')
PY
