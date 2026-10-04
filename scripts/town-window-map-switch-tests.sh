#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$repo_root/tests/GloomhavenVR.TownWindowMapSwitchTests/GloomhavenVR.TownWindowMapSwitchTests.csproj"
dotnet run --project "$project" --configuration Release
mutation_dir="$(mktemp -d)"
trap 'rm -rf "$mutation_dir"' EXIT
for mutation in exit scoped-mode restore live; do
    python3 - "$repo_root" "$mutation_dir/Production.cs" "$mutation" <<'PYTHON'
from pathlib import Path
import sys
s=(Path(sys.argv[1])/'src/GloomhavenVR/WorldUI/MapRoom/TownWindowMapSwitch.cs').read_text()
changes={
'exit':('return false;\n    }','return true;\n    }'),
'scoped-mode':('___currentMode = newMode;','// mutation: native map visibility sees service mode'),
'restore':('___currentMode = service;','// mutation: map steals the active service'),
'live':('!ModalFallback.FloatIsLive(window)','false'),
}
a,b=changes[sys.argv[3]]
assert s.count(a)==1
Path(sys.argv[2]).write_text(s.replace(a,b))
PYTHON
    if dotnet run --project "$project" --configuration Release --property:ProductionSource="$mutation_dir/Production.cs" > "$mutation_dir/output" 2>&1; then
        echo "FAIL: map/service lifecycle mutation escaped: $mutation" >&2; exit 1
    fi
    if ! grep -q 'Unhandled exception' "$mutation_dir/output"; then
        cat "$mutation_dir/output" >&2; exit 1
    fi
done
echo "Town window map switch: 4 causal controls rejected."
