#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$repo_root/tests/GloomhavenVR.GuildmasterRoomTests/GloomhavenVR.GuildmasterRoomTests.csproj"
source_file="$repo_root/src/GloomhavenVR/WorldUI/MapRoom/GuildmasterRoomLayout.cs"
dotnet run --project "$project" --configuration Release
python3 - "$repo_root" <<'PY'
from pathlib import Path
import sys
r=Path(sys.argv[1]); p=r/'src/GloomhavenVR'
g=(p/'WorldUI/MapRoom/GuildmasterRoomGeometry.cs').read_text()
b=(p/'WorldUI/MapRoom/MapButtonRail.cs').read_text()
s=(p/'Core/Environment/SkyAlternative.cs').read_text()
checks=[
 '!MapRuleLibrary.Adventure.AdventureState.MapState.IsCampaign' in g,
 'if (!Active || parchment == null' in g,
 'bool measured = Active && MapTableLegs.TryFindTable' in g,
 'knifeFar = Mathf.Max(knifeFar, prop.max.z + .02f * seat.Scale)' in g,
 'prop.min.y > map.max.y + .2f * seat.Scale' in g,
 'renderer.transform.TransformPoint(new Vector3(' in g,
 'table = measured ? table : FindTableSupport(parchment, seat.Scale)' in g,
 'layout = GuildmasterRoomLayout.FallbackRail' in g,
 'renderer.isPartOfStaticBatch || filter == null' in g,
 'if (!supportedRail && !_fitWarned)' in b,
 b.index('if (_scratch.Count == 0)', b.index('_scratch.Sort(CompareByDeclaredRank)')) < b.index('if (SameSet())'),
 'float size = c.IconWorldSize / IconFraction * GlowFraction * ratio;' in b,
 'keeping the native actions on the right-side fallback rail.' in b,
 'return; // The slab may still be loading' not in b,
 'renderer.gameObject.scene != parchment.gameObject.scene' in g,
 'sideRail.Position(built, out float x, out float z)' in b,
 'float cap = guildmaster ? sideRail.Cap : CapSizeMeters * _scale;' in b,
 'if (WorldUI.MapRoom.GuildmasterRoomGeometry.TryFloor(out float furnitureFloor))' in s,
 s.index('GuildmasterRoomGeometry.TryFloor') < s.index('room.transform.SetPositionAndRotation(new Vector3(center.x, floorY, center.z)')
]
assert all(checks),checks
print(f'Guildmaster room bindings: {len(checks)} passed.')
PY
mutation_dir="$(mktemp -d)"
trap 'rm -rf "$mutation_dir"' EXIT
for mutation in footprint order floor fallback-side fallback-knife; do
    python3 - "$source_file" "$mutation_dir/Mutated.cs" "$mutation" <<'PY'
from pathlib import Path
import sys
s=Path(sys.argv[1]).read_text()
old,new={
 'footprint':('far - outerRatio * cap * .5f','far'),
 'order':('Z - index / Columns * Pitch','Z + index / Columns * Pitch'),
 'floor':('furnitureBottom + .025f * scale','furnitureBottom - .025f * scale'),
 'fallback-side':('mapRight + radius','mapRight - radius'),
 'fallback-knife':('Math.Max(mapFar - radius, knifeFar + radius + (rows - 1) * pitch)','mapFar - radius')
}[sys.argv[3]]
assert old in s
Path(sys.argv[2]).write_text(s.replace(old,new))
PY
    if dotnet run --project "$project" --configuration Release --property:RoomLayoutSource="$mutation_dir/Mutated.cs" > "$mutation_dir/output" 2>&1; then
        echo "FAIL: Guildmaster room mutation survived: $mutation" >&2; exit 1
    fi
    expected='whole cap stays behind knife and within far edge'
    if [[ "$mutation" == floor ]]; then expected='furniture floor contact allowance'; fi
    if [[ "$mutation" == fallback-side ]]; then expected='fallback stays right of parchment'; fi
    if [[ "$mutation" == fallback-knife ]]; then expected='fallback clears measured knife'; fi
    if ! rg -qF "Unhandled exception. System.Exception: $expected" "$mutation_dir/output"; then
        cat "$mutation_dir/output"; exit 1
    fi
    echo "Guildmaster room negative rejected: $mutation"
done
