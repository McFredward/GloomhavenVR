#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$repo_root/tests/GloomhavenVR.MrScenarioTests/GloomhavenVR.MrScenarioTests.csproj"
python3 "$repo_root/tests/GloomhavenVR.MrScenarioTests/extract.py"
dotnet run --project "$project" --configuration Release
python3 - "$repo_root" <<'PY'
from pathlib import Path
import sys
r=Path(sys.argv[1]); wall=(r/'src/GloomhavenVR/Core/WallFade/WallSegmentFade.cs').read_text()
mr=(r/'src/GloomhavenVR/Core/MixedReality/MixedReality.cs').read_text()
layers=(r/'src/GloomhavenVR/Core/VRLayers.cs').read_text()
assert '|| ModVisualOwnership.IsName(n);' in wall
assert '|| ModVisualOwnership.IsName(r.name);' in wall
assert 'internal const string ModOwnedNamePrefix = "VR";' in layers
assert 'internal const string ModOwnedQualifiedPrefix = "GloomhavenVR.";' in layers
region=mr[mr.index('private static void RegionMembershipPass('):mr.index('private enum RegionVerdict')]
assert region.index('CanBackRegion(r, mats)') < region.index('BuildUnseenUnderlay(')
assert 'GetTag("RenderType", false, string.Empty) == "TransparentCutout"' in mr
assert 'IsKeywordEnabled("_ALPHATEST_ON")' in mr
assert 'bool hasRegion = SeedRegionBounds();' in region
assert 'UnseenSources.Remove(entry.SourceId);' in region
assert '|| !e.Source.gameObject.activeInHierarchy)' in mr
assert 'MrUnseenRegionEligibility.Retain(entry.Source.enabled,' in region
assert 'entry.Source.gameObject.activeInHierarchy,' in region
assert 'MrRimCurtain.ReleaseMeshes();' in mr
rim=(r/'src/GloomhavenVR/Core/MixedReality/MrRimCurtain.cs').read_text()
assert 'MeshCache.TryGetValue(key, out Mesh mesh)' in rim
assert mr.count('sharedMesh = filter.sharedMesh;') == 2
# Build 532: the previous scenery implementation is dormant for compatibility. The
# live MR path must retire it, while UI readability and chroma-key remain independent.
tick=mr[mr.index('internal static void Tick('):mr.index('// ---- sky/background geometry')]
assert 'RetireSceneryBackings();' in tick
assert 'ForceUnseenOpaque();' not in tick
assert mr.count('ForceUnseenOpaque(') == 1 # declaration, no creation caller
assert 'HideSkyGeometry();' in tick and 'ForceSolid(head, key);' in tick
assert '_file != null && Enabled.Value && VRSession.IsRunning;' in mr
assert mr.count('"INERT since build 532. MR backing geometry is restricted to UI; scenery, water and "') == 7
restore=mr[mr.index('private static void RestoreUnseenUnderlays('):mr.index('private static void RestoreSky(')]
for name in ('plate', 'fill', 'rim'):
    assert restore.index(name+'.enabled = false;') < restore.index('Destroy('+name+'.gameObject)')
assert 'MrRimCurtain.ReleaseMeshes();' in restore
assert 'WorldUI.' not in restore
print('MR scenario ownership: 26 source bindings passed.')
PY
mutation_dir="$(mktemp -d)"
trap 'rm -rf "$mutation_dir"' EXIT
for mutation in prefix overlap cutout inactive retirement; do
    python3 - "$repo_root" "$mutation_dir/Mutated.cs" "$mutation" <<'PY'
from pathlib import Path
import sys
r=Path(sys.argv[1]); mutation=sys.argv[3]
path='Core/ModVisualOwnership.cs' if mutation=='prefix' else 'Core/MixedReality/MrUnseenRegionEligibility.cs'
s=(r/'tests/GloomhavenVR.MrScenarioTests/obj/Retirement.g.cs').read_text() if mutation=='retirement' else (r/'src/GloomhavenVR'/path).read_text()
if mutation=='retirement':
    old='RestoreUnseenUnderlays();'
    new='return;'
elif mutation=='prefix':
    old='|| name.StartsWith(VRLayers.ModOwnedQualifiedPrefix, StringComparison.Ordinal)'
    new='|| name.StartsWith(VRLayers.ModOwnedNamePrefix, StringComparison.Ordinal)'
elif mutation=='overlap':
    old='string.Equals(objectName, "Simple Tile", StringComparison.Ordinal)'
    new='objectName.Length > 0'
elif mutation=='inactive':
    old='sourceEnabled && sourceActive && overlapsLiveHost'
    new='overlapsLiveHost'
else:
    old='!hasCutoutMaterial &&'
    new=''
assert s.count(old)==1
Path(sys.argv[2]).write_text(s.replace(old,new))
PY
    property=EligibilitySource
    expected='overlap does not imply fog ownership'
    if [[ "$mutation" == prefix ]]; then property=OwnershipSource; expected='mod visual excluded'; fi
    if [[ "$mutation" == cutout ]]; then expected='cutout silhouettes never filled'; fi
    if [[ "$mutation" == retirement ]]; then property=RetirementSource; expected="every tracked scenery resource is retired"; fi
    if [[ "$mutation" == inactive ]]; then expected='only active source with a live host'; fi
    if dotnet run --project "$project" --configuration Release --property:"$property=$mutation_dir/Mutated.cs" > "$mutation_dir/output" 2>&1; then
        echo "FAIL: MR scenario mutation survived: $mutation" >&2; exit 1
    fi
    if ! rg -qF "Unhandled exception. System.Exception: $expected" "$mutation_dir/output"; then
        cat "$mutation_dir/output"; exit 1
    fi
    echo "MR scenario negative rejected: $mutation"
done
