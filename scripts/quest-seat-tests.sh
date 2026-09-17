#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$repo_root/tests/GloomhavenVR.QuestSeatTests/GloomhavenVR.QuestSeatTests.csproj"
source_file="$repo_root/src/GloomhavenVR/WorldUI/Modal/ModalFallback.13.QuestSelectionSeat.cs"
dotnet run --project "$project" --configuration Release
mutation_dir="$(mktemp -d)"
trap 'rm -rf "$mutation_dir"' EXIT
for mutation in centre-first occupied-centre private-corner; do
    python3 - "$source_file" "$mutation_dir/Mutated.cs" "$mutation" <<'PY'
from pathlib import Path
import sys
source=Path(sys.argv[1]).read_text()
changes={
 'centre-first': ('centreLimit >= -1e-3f && ArcSeatIsFree(gazeYawDeg, halfAngle)', 'centreLimit < float.MinValue'),
 'occupied-centre': ('centreLimit >= -1e-3f && ArcSeatIsFree(gazeYawDeg, halfAngle)', 'centreLimit >= -1e-3f && (ArcSeatIsFree(gazeYawDeg, halfAngle) || centreLimit >= 0f)'),
 'private-corner': ('bool corner = StandingQuestLogSlot() < 0;', 'bool corner = false;'),
}
old,new=changes[sys.argv[3]]
assert source.count(old)==1
Path(sys.argv[2]).write_text(source.replace(old,new))
PY
    if dotnet run --project "$project" --configuration Release --property:QuestSeatSource="$mutation_dir/Mutated.cs" > "$mutation_dir/output" 2>&1; then
        echo "FAIL: quest seat mutation survived: $mutation" >&2; exit 1
    fi
    case "$mutation" in
        centre-first) expected='free centre must not inherit adjacent-log offset' ;;
        occupied-centre) expected='fallback respects existing log clearance' ;;
        private-corner) expected='private selection retains right corner when log hidden' ;;
    esac
    if ! rg -qF "Unhandled exception. System.Exception: $expected" "$mutation_dir/output"; then
        cat "$mutation_dir/output"; exit 1
    fi
    echo "Quest seat negative rejected: $mutation"
done
