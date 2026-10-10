#!/usr/bin/env bash
# Execute the production hover/teardown/navigation path and its original native predicate.
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$repo_root/tests/GloomhavenVR.MapNavigationTests/GloomhavenVR.MapNavigationTests.csproj"
source_file="${MAP_NAVIGATION_SOURCE:-$repo_root/src/GloomhavenVR/WorldUI/MapRoom/MapLocationInteractor.cs}"
temporary="$(mktemp -d)"
trap 'rm -rf "$temporary"' EXIT
python3 - "$repo_root" <<'PY'
from pathlib import Path
import subprocess, sys
root = Path(sys.argv[1])
common = Path(subprocess.check_output(['git', '-C', str(root), 'rev-parse', '--path-format=absolute', '--git-common-dir'], text=True).strip())
original = common.parent / 'decompiled/GH.Runtime/Assets.Script.AdventureMap/MapLocationSelector.cs'
if original.exists():
    source = original.read_text()
    start = source.index('\tprivate void Update()')
    end = source.index('\n\t}', start) + len('\n\t}')
    fixture = (root / 'tests/GloomhavenVR.MapNavigationTests/NativeSelectorFixture.cs').read_text()
    assert source[start:end] in fixture, 'Native selector fixture differs from the read-only original game Update'
    print('Map navigation: original native selector Update verified against the read-only game source.')
else:
    print('Map navigation: read-only game source unavailable; using the tracked original Update fixture.')
PY
dotnet run --project "$project" --configuration Release --property:InteractorSource="$source_file"
for mutation in missing-hover-gate missing-exit-gate; do
    python3 - "$source_file" "$temporary/Interactor.cs" "$mutation" <<'PY'
from pathlib import Path
import sys
source = Path(sys.argv[1]).read_text()
needle = ' || !IsHoverNavigationState(nav.StateMachine.CurrentState)'
assert source.count(needle) == 2, 'Production navigation mutation seam changed'
start = source.index('    private static void StateMachineEnterHover(' if sys.argv[3] == 'missing-hover-gate'
                     else '    private static void StateMachineEnterWorldMap(')
at = source.index(needle, start)
Path(sys.argv[2]).write_text(source[:at] + source[at + len(needle):])
PY
    if dotnet run --project "$project" --configuration Release \
        --property:InteractorSource="$temporary/Interactor.cs" > "$temporary/mutant.log" 2>&1; then
        echo "FAIL: map navigation negative control survived: $mutation" >&2
        exit 1
    fi
    case "$mutation" in
        missing-hover-gate) expected='Protected native state must survive VR hover entry' ;;
        missing-exit-gate) expected='Protected native state must survive VR hover exit without releasing its ownership' ;;
    esac
    if ! rg -qF "Unhandled exception. System.InvalidOperationException: $expected" "$temporary/mutant.log"; then
        cat "$temporary/mutant.log"
        echo "FAIL: map navigation negative control did not reach the injected defect: $mutation" >&2
        exit 1
    fi
    echo "Map navigation negative control rejected: $mutation"
done
