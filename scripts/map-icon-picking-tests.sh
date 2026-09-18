#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$repo_root/tests/GloomhavenVR.MapIconPickingTests/GloomhavenVR.MapIconPickingTests.csproj"
source_file="$repo_root/src/GloomhavenVR/WorldUI/MapRoom/MapIconPickGeometry.cs"
dotnet run --project "$project" --configuration Release
python3 - "$repo_root" <<'PY'
from pathlib import Path
import re, sys
root=Path(sys.argv[1])/'src/GloomhavenVR'
def code(path):
    return re.sub(r'/\*[\s\S]*?\*/|//[^\r\n]*', '', (root/path).read_text())
pads=code('WorldUI/MapRoom/MapIconHoverPads.cs')
route=code('WorldUI/MapRoom/MapLocationInteractor.cs')
poke=code('Hands/Interact/PokeInteractor.cs')
bindings=[
 ('uncapped live registry', 'i < _padColliders.Count' in pads),
 ('local rotated footprint', 'pad.transform.InverseTransformPoint(world) - pad.center' in pads),
 ('active enabled targets', '!pad.enabled || !pad.gameObject.activeInHierarchy || loc == null' in pads),
 ('stable metric production binding', 'MapIconPickGeometry.Prefer(score, key, bestScore, bestKey)' in pads),
 ('ray foreground gate', 'out padDist) && LaserPointerPolicy.TargetBeforeBlocker(padDist, limit)' in route),
 ('ray resolver binding', 'padHit = _pads.PickAt(pick.Origin + pick.Direction * padDist);' in route),
 ('finger resolver binding', '_pokePickOwner = _pads.PickAt(point);' in route),
 ('physical ownership rejection', '_pokePickOwner == null || ReferenceEquals(_pokePickOwner, location)' in route),
 ('opt-in filter before distance arbitration', poke.index('filter.AcceptsPokePoint(tip)') < poke.index('float dist = Vector3.Distance(tip, collider.ClosestPoint(tip));')),
 ('native click retained', '_owner.Dispatch(_location,' in route),
 ('grip contact retained', 'private bool PressAllowed => _hand.GripPressed && _hand.Grabber.Held == null;' in poke),
 ('trigger re-picks before commit', 'MapLocation? clicked = PickFrom(clicking, out _);' in route),
]
for label, ok in bindings:
    assert ok,label
print(f'Map icon picking: {len(bindings)} integration bindings passed')
# Reject missing call-site wiring, including an old invocation left behind in a comment.
for source, needle in ((route, 'LaserPointerPolicy.TargetBeforeBlocker(padDist, limit)'),
                       (poke, 'filter.AcceptsPokePoint(tip)'),
                       (pads, 'i < _padColliders.Count')):
    assert needle in source
    broken=source.replace(needle, '/* '+needle+' */ false')
    broken=re.sub(r'/\*[\s\S]*?\*/|//[^\r\n]*', '', broken)
    assert needle not in broken, 'missing integration survived behind a comment'
print('Map icon picking: 3 disconnected integration negatives rejected')
PY
mutation_dir="$(mktemp -d)"
trap 'rm -rf "$mutation_dir"' EXIT
for mutation in centre footprint tie plane; do
    python3 - "$source_file" "$mutation_dir/Mutated.cs" "$mutation" <<'PY'
from pathlib import Path
import sys
s=Path(sys.argv[1]).read_text()
old,new={
 'centre':('deltaX * deltaX + deltaZ * deltaZ', '0f'),
 'footprint':('Math.Abs(localX) <= width * 0.5f && Math.Abs(localZ) <= depth * 0.5f', 'Math.Abs(localX) <= width * 50f && Math.Abs(localZ) <= depth * 50f'),
 'tie':('key < bestKey', 'key > bestKey'),
 'plane':('distance >= 0f', 'distance <= 0f'),
}[sys.argv[3]]
assert s.count(old)==1
Path(sys.argv[2]).write_text(s.replace(old,new))
PY
    if dotnet run --project "$project" --configuration Release --property:PickGeometrySource="$mutation_dir/Mutated.cs" > "$mutation_dir/output" 2>&1; then
        echo "FAIL: map icon picking mutation survived: $mutation" >&2; exit 1
    fi
    if ! rg -qF 'Unhandled exception. System.Exception:' "$mutation_dir/output"; then
        cat "$mutation_dir/output"; exit 1
    fi
    echo "Map icon picking negative rejected: $mutation"
done
