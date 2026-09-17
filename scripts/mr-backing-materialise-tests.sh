#!/usr/bin/env bash
# Execute the actual MR backing grid and shared materialise field, including defect controls.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$root/tests/GloomhavenVR.MrBackingMaterialiseTests/GloomhavenVR.MrBackingMaterialiseTests.csproj"
dotnet run --project "$project" --configuration Release
fixture="$(mktemp -d)"
trap 'rm -rf "$fixture"' EXIT
cp "$root/tests/GloomhavenVR.MrBackingMaterialiseTests/"*.cs "$project" "$fixture/"
python3 - "$root" "$fixture" <<'PY'
from pathlib import Path
import sys
root, dest = map(Path, sys.argv[1:])
s=(root/'src/GloomhavenVR/WorldUI/MrBackingMaterialise.cs').read_text()
for name, old, new in [
    ('plate-uv', 'Mathf.Clamp01((shown.xMin + u * shown.width - hostFrame.xMin) / hostFrame.width)', 'u'),
    ('aspect', 'hostFrame.width / Mathf.Max(hostFrame.height, 0.01f)', '1f'),
    ('uniform-alpha', 'WindowMaterialiseField.Presence(_thresholds![i], progress)', '1f - progress'),
    ('fit-cache', '!Same(_shown, shown)', 'false'),
    ('host-cache', '!Same(_hostFrame, hostFrame)', 'false'),
    ('restore', '_filter.sharedMesh = _originalMesh;', '_filter.sharedMesh = _mesh;'),
    ('owned-dispose', 'UnityEngine.Object.Destroy(_mesh);', '_mesh.name = "leaked";'),
    ('visibility', '_renderer.sharedMaterial = fadeMaterial;', '_renderer.enabled = true; _renderer.sharedMaterial = fadeMaterial;'),
]:
    assert s.count(old)==1, name
    (dest/(name+'.fixture')).write_text(s.replace(old,new))
PY
for mutation in plate-uv aspect uniform-alpha fit-cache host-cache restore owned-dispose visibility; do
    case "$mutation" in
        plate-uv|aspect|uniform-alpha|fit-cache|host-cache) expected='Backing vertices must match the original host field' ;;
        restore) expected='Progress zero must restore original opaque quad and material' ;;
        owned-dispose) expected='Dispose must destroy only the owned mesh once' ;;
        visibility) expected='Restore must not reveal a natively hidden plate' ;;
    esac
    if dotnet run --project "$fixture/GloomhavenVR.MrBackingMaterialiseTests.csproj" --configuration Release \
        --property:BackingSource="$fixture/$mutation.fixture" \
        --property:FieldSource="$root/src/GloomhavenVR/WorldUI/Materialise/WindowMaterialiseField.cs" \
        > "$fixture/$mutation.log" 2>&1; then
        echo "FAIL: MR materialise $mutation regression escaped coverage" >&2; exit 1
    fi
    if ! rg -Fq "Unhandled exception. System.Exception: $expected" "$fixture/$mutation.log"; then
        cat "$fixture/$mutation.log"; exit 1
    fi
    echo "MR backing materialise negative control: $mutation rejected."
done
