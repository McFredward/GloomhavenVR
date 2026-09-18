#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$repo_root/tests/GloomhavenVR.GuildmasterTableTests/GloomhavenVR.GuildmasterTableTests.csproj"
source_file="$repo_root/src/GloomhavenVR/WorldUI/MapRoom/GuildmasterTableFit.cs"
dotnet run --project "$project" --configuration Release
mutation_dir="$(mktemp -d)"
trap 'rm -rf "$mutation_dir"' EXIT
for mutation in clearance collision; do
    python3 - "$source_file" "$mutation_dir/Mutated.cs" "$mutation" <<'PY'
from pathlib import Path
import sys
s=Path(sys.argv[1]).read_text()
if sys.argv[3]=='clearance':
    s=s.replace('b.Left - clearance, b.Right + clearance, b.Near - clearance, b.Far + clearance', 'b.Left, b.Right, b.Near, b.Far')
else:
    s=s.replace('if (!fit.Overlaps(block)) continue;', 'if (fit.Area > 0f) continue;')
Path(sys.argv[2]).write_text(s)
PY
    if dotnet run --project "$project" --configuration Release --property:TableFitSource="$mutation_dir/Mutated.cs" > "$mutation_dir/output" 2>&1; then
        echo "FAIL: guildmaster table mutation survived: $mutation" >&2; exit 1
    fi
    if ! rg -qF 'Unhandled exception. System.Exception: prop clearance retained' "$mutation_dir/output"; then
        cat "$mutation_dir/output"; exit 1
    fi
    echo "Guildmaster table negative rejected: $mutation"
done
asset_source="$repo_root/src/GloomhavenVR/WorldUI/MapRoom/GuildmasterTableAssets.cs"
for mutation in placeholder slot-order; do
    python3 - "$asset_source" "$mutation_dir/MutatedAssets.cs" "$mutation" <<'PY'
from pathlib import Path
import sys
s=Path(sys.argv[1]).read_text()
if sys.argv[3]=='placeholder':
    old='data.MaterialReferences.Count > 0'
    new='data.MaterialReferences.Count < 0'
else:
    old='materials[i] = _materialLoads[i].Result;'
    new='materials[i] = _materialLoads[materials.Length - 1 - i].Result;'
assert s.count(old)==1
Path(sys.argv[2]).write_text(s.replace(old,new))
PY
    if dotnet run --project "$project" --configuration Release --property:TableAssetsSource="$mutation_dir/MutatedAssets.cs" > "$mutation_dir/output" 2>&1; then
        echo "FAIL: guildmaster material mutation survived: $mutation" >&2; exit 1
    fi
    expected='placeholder materials are not ready'
    if [[ "$mutation" == slot-order ]]; then expected='completion order never changes slot order'; fi
    if ! rg -qF "Unhandled exception. System.Exception: $expected" "$mutation_dir/output"; then
        cat "$mutation_dir/output"; exit 1
    fi
    echo "Guildmaster material negative rejected: $mutation"
done
