#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$repo_root/tests/GloomhavenVR.PanelInkTests/GloomhavenVR.PanelInkTests.csproj"
source_file="$repo_root/src/GloomhavenVR/WorldUI/Conversion/PanelInkBounds.cs"
mutation_dir="$(mktemp -d)"
trap 'rm -rf "$mutation_dir"' EXIT
cp "$repo_root/tests/GloomhavenVR.PanelInkTests/"*.cs "$mutation_dir/"
cp "$project" "$mutation_dir/"
python3 - "$repo_root" "$mutation_dir" <<'PY'
import pathlib, re, sys
repo, root = map(pathlib.Path, sys.argv[1:])
source = (repo / 'src/GloomhavenVR/WorldUI/Conversion/PanelInkBounds.cs').read_text()
needle = '!ReferenceEquals(graphic, panel.ContentGraphic)'
assert source.count(needle) == 1
(root / 'missing.fixture').write_text(source.replace(needle, 'true'))
(root / 'broad.fixture').write_text(source.replace(needle, 'panel.ContentGraphic == null'))
needle = '!includeParkedHint && !HintOnOwnerComposite.IsParkedContent(target)'
assert source.count(needle) == 1
(root / 'hint-ink.fixture').write_text(source.replace(needle, 'false'))
fit = (repo / 'src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.3.Fit.cs').read_text()
start = fit.index('    private static bool TryMeasureDrawnUnion(')
opening = fit.index('{', start)
depth, end = 1, opening + 1
while depth:
    depth += (fit[end] == '{') - (fit[end] == '}')
    end += 1
union = ('using UnityEngine; using UnityEngine.UI; namespace GloomhavenVR.WorldUI; '
         'internal static partial class CanvasConversion {\n' + fit[start:end] + '\n}')
(root / 'union.fixture').write_text(union)
needle = '!includeParkedHint && !HintOnOwnerComposite.IsParkedContent(root)'
assert union.count(needle) == 1
(root / 'hint-union.fixture').write_text(union.replace(needle, 'false'))

def code(text):
    return re.sub(r'/\*.*?\*/|//[^\n]*', '', text, flags=re.S)

# Binding checks cover the production wrapper and both primary/fallback placement queries.
# Hit/chrome callers deliberately keep the default that includes the native Continue button.
arc = code((repo / 'src/GloomhavenVR/WorldUI/Modal/ArcSeats.cs').read_text())
def placement_binding(text):
    queries = re.findall(r'(?:CanvasConversion.TryMeasureDrawnContent|PanelInkBounds.TryMeasure)\([^;]+?\)', text)
    return len(queries) == 4 and all('includeParkedHint: false' in q for q in queries)
assert placement_binding(arc), 'placement must exclude annotations in every visible/fallback query'
assert not placement_binding(arc.replace('includeParkedHint: false', 'includeParkedHint: true', 1)), 'placement binding negative control escaped'
assert 'out content, out contributors, out _, out _, includeParkedHint)' in code(fit)
print('Placement binding negative control: inclusion of owner annotation rejected.')
PY
dotnet run --project "$project" --configuration Release --property:DrawnUnionSource="$mutation_dir/union.fixture"
for mutation in missing broad hint-ink hint-union; do
    ink_source="$mutation_dir/$mutation.fixture"
    union_source="$mutation_dir/union.fixture"
    if [[ "$mutation" == hint-union ]]; then
        ink_source="$source_file"
        union_source="$mutation_dir/hint-union.fixture"
    fi
    if dotnet run --project "$mutation_dir/GloomhavenVR.PanelInkTests.csproj" --configuration Release \
        --property:InkSource="$ink_source" --property:DrawnUnionSource="$union_source" > "$mutation_dir/$mutation.log" 2>&1; then
        cat "$mutation_dir/$mutation.log"
        echo "FAIL: $mutation mutation escaped the ink test." >&2
        exit 1
    fi
    expected='full-frame movie remains measurable content'
    if [[ "$mutation" == broad ]]; then expected='content exemption never admits a neighboring backdrop'; fi
    if [[ "$mutation" == hint-ink ]]; then expected='placement fallback excludes hint'; fi
    if [[ "$mutation" == hint-union ]]; then expected='visible placement excludes hint'; fi
    if ! rg -q "$expected" "$mutation_dir/$mutation.log"; then
        cat "$mutation_dir/$mutation.log"
        exit 1
    fi
done
echo "Panel ink negative controls: four runtime mutations and one placement binding mutation rejected."
