#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$repo_root/tests/GloomhavenVR.PanelMaterialTests/GloomhavenVR.PanelMaterialTests.csproj"
material_source="$repo_root/src/GloomhavenVR/WorldUI/Sharpness/PanelGraphicMaterial.cs"
capture_source="$repo_root/src/GloomhavenVR/WorldUI/Sharpness/PanelSupersample.2.Capture.cs"
content_source="$repo_root/src/GloomhavenVR/WorldUI/Sharpness/PanelSupersample.4.Content.cs"
mutation_dir="$(mktemp -d)"
trap 'rm -rf "$mutation_dir"' EXIT
python3 - "$capture_source" "$content_source" "$mutation_dir" <<'PY'
import pathlib, sys
capture, content, out = map(pathlib.Path, sys.argv[1:])
source = capture.read_text()
start = source.index('    private static void NeutraliseGrabPassBlur(Entry e, Transform t)')
end = source.index('\n    private static bool BuildDisplay(', start)
# Execute the actual configured capture call site, including its null/off guards and
# counter, rather than a manually copied statement that could drift from production.
(out / 'Capture.fixture').write_text('using UnityEngine;\nusing UnityEngine.UI;\nnamespace GloomhavenVR.WorldUI;\ninternal static partial class PanelSupersample\n{\n' + source[start:end] + '\n}\n')
source = content.read_text()
start = source.index('    private static string PlateMaterialNote(Graphic? g)')
end = source.index('\n    /// <summary>', start)
assert 'mat = PanelGraphicMaterial.Read(g);' in source[start:end]
assert 'mat = g.material;' not in source[start:end]
PY
dotnet run --project "$project" --configuration Release \
    --property:CaptureSource="$mutation_dir/Capture.fixture"
cp "$repo_root/tests/GloomhavenVR.PanelMaterialTests/"*.cs "$mutation_dir/"
cp "$project" "$mutation_dir/"
for mutation in submesh-getter font-getter broad-neutralization skip-blur; do
    python3 - "$material_source" "$mutation_dir" "$mutation" <<'PY'
import pathlib, sys
source = pathlib.Path(sys.argv[1]).read_text()
mutations = {
    'submesh-getter': ('return submesh.sharedMaterial;', 'return submesh.material;'),
    'font-getter': ('return text.fontSharedMaterial;', 'return text.material;'),
    'broad-neutralization': ('material.shader.name.IndexOf("GrabPass", System.StringComparison.OrdinalIgnoreCase) < 0', 'false'),
    'skip-blur': ('material.shader.name.IndexOf("GrabPass", System.StringComparison.OrdinalIgnoreCase) < 0', 'true'),
}
before, after = mutations[sys.argv[3]]
assert source.count(before) == 1, before
(pathlib.Path(sys.argv[2]) / 'Material.mutant').write_text(source.replace(before, after))
PY
    case "$mutation" in
        submesh-getter) expected='TMP_SubMeshUI material getter instantiated a null source' ;;
        font-getter) expected='TMP_Text material getter must not be evaluated' ;;
        broad-neutralization) expected='native font and sprite assignments are preserved' ;;
        skip-blur) expected='GrabPass backdrop is neutralized and counted' ;;
    esac
    if dotnet run --project "$mutation_dir/GloomhavenVR.PanelMaterialTests.csproj" --configuration Release \
        --property:CaptureSource="$mutation_dir/Capture.fixture" \
        --property:MaterialSource="$mutation_dir/Material.mutant" > "$mutation_dir/mutant.log" 2>&1; then
        cat "$mutation_dir/mutant.log"
        echo "FAIL: $mutation escaped panel material test." >&2
        exit 1
    fi
    if ! rg -qF "$expected" "$mutation_dir/mutant.log"; then
        cat "$mutation_dir/mutant.log"
        exit 1
    fi
    echo "Panel material negative control: $mutation rejected."
done
