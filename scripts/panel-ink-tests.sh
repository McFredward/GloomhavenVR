#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$repo_root/tests/GloomhavenVR.PanelInkTests/GloomhavenVR.PanelInkTests.csproj"
source_file="$repo_root/src/GloomhavenVR/WorldUI/Conversion/PanelInkBounds.cs"
dotnet run --project "$project" --configuration Release
mutation_dir="$(mktemp -d)"
trap 'rm -rf "$mutation_dir"' EXIT
cp "$repo_root/tests/GloomhavenVR.PanelInkTests/"*.cs "$mutation_dir/"
cp "$project" "$mutation_dir/"
python3 - "$source_file" "$mutation_dir" <<'PY'
import pathlib, sys
source = pathlib.Path(sys.argv[1]).read_text()
needle = '!ReferenceEquals(graphic, panel.ContentGraphic)'
assert source.count(needle) == 1
root = pathlib.Path(sys.argv[2])
(root / 'missing.fixture').write_text(source.replace(needle, 'true'))
(root / 'broad.fixture').write_text(source.replace(needle, 'panel.ContentGraphic == null'))
PY
for mutation in missing broad; do
    if dotnet run --project "$mutation_dir/GloomhavenVR.PanelInkTests.csproj" --configuration Release \
        --property:InkSource="$mutation_dir/$mutation.fixture" > "$mutation_dir/$mutation.log" 2>&1; then
        cat "$mutation_dir/$mutation.log"
        echo "FAIL: $mutation full-frame content mutation escaped the ink test." >&2
        exit 1
    fi
    expected='full-frame movie remains measurable content'
    if [[ "$mutation" == broad ]]; then expected='content exemption never admits a neighboring backdrop'; fi
    if ! rg -q "$expected" "$mutation_dir/$mutation.log"; then
        cat "$mutation_dir/$mutation.log"
        exit 1
    fi
done
echo "Panel ink negative controls: lost content and broad backdrop exemption rejected."
