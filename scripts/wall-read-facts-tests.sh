#!/usr/bin/env bash
# Compile actual WallCache material admission and root lookup; count avoided native probes.
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if ! command -v dotnet >/dev/null 2>&1 && [[ -x "$HOME/.dotnet/dotnet" ]]; then
    export DOTNET_ROOT="$HOME/.dotnet"
fi
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$repo_root/tests/GloomhavenVR.WallReadFactsTests/GloomhavenVR.WallReadFactsTests.csproj"
dotnet run --project "$project" --configuration Release
mutation_dir="$(mktemp -d)"
trap 'rm -rf "$mutation_dir"' EXIT
for mutation in material-no-cache material-retained figure-no-cache figure-retained gate-inverted phase-unbounded material-bypass-ignored figure-bypass-ignored; do
    python3 - "$repo_root" "$mutation_dir" "$mutation" <<'PY'
import pathlib
import sys
root, temporary = map(pathlib.Path, sys.argv[1:3])
mutation = sys.argv[3]
source = root / 'src/GloomhavenVR/Core/WallFade/WallSegmentFade.ReadFacts.cs'
changes = {
    'material-no-cache': ('if (_wallCacheMaterialFactsActive\n                && _wallCacheMaterialFacts.TryGetValue',
                          'if (bool.Parse("false")\n                && _wallCacheMaterialFacts.TryGetValue'),
    'material-retained': ('_wallCacheMaterialFactsActive = false;\n            _wallCacheMaterialFacts.Clear();',
                          '_wallCacheMaterialFactsActive = false;'),
    'figure-no-cache': ('if (_figureRootMemoActive)\n                return FigurePropRootMemoized(t);',
                        'if (bool.Parse("false"))\n                return FigurePropRootMemoized(t);'),
    'figure-retained': ('FigureRootMemo.Clear();\n            GameLogicAncestryMemo.Clear();',
                        '// injected retained figure roots\n            GameLogicAncestryMemo.Clear();'),
    'gate-inverted': ('m.GetFloat(WallFadeOnMatId) != 0f', 'm.GetFloat(WallFadeOnMatId) == 0f'),
    'phase-unbounded': ('finally { EndWallCacheMaterialFacts(); }', '// injected missing finally close'),
    'material-bypass-ignored': ('_wallCacheMaterialFactsActive = PerfConfig.SharedWallReadCacheOn;',
                               '_wallCacheMaterialFactsActive = true;'),
    'figure-bypass-ignored': ('_figureRootMemoActive = PerfConfig.SharedWallReadCacheOn;',
                             '_figureRootMemoActive = true;'),
}
if mutation in ('figure-retained', 'gate-inverted', 'phase-unbounded', 'figure-bypass-ignored'):
    source = root / 'src/GloomhavenVR/Core/WallFade/WallSegmentFade.cs'
needle, replacement = changes[mutation]
text = source.read_text()
# The figure clear appears at both begin/end; remove the end only.
if mutation == 'figure-retained':
    start = text.index('private static void EndFigureMemo()')
    prefix, suffix = text[:start], text[start:]
    assert suffix.count(needle) == 1, 'Figure lifetime mutation seam changed'
    text = prefix + suffix.replace(needle, replacement)
else:
    assert text.count(needle) == 1, 'Wall mutation seam changed: ' + mutation
    text = text.replace(needle, replacement)
(temporary / 'mutant.cs').write_text(text)
PY
    property=ReadFactsSource
    case "$mutation" in
        material-no-cache) expected='Repeated shared material inspection must not repeat native property probes' ;;
        material-retained) expected='The material memo must close and release references' ;;
        figure-no-cache) expected='Shared ancestor component lookups must run once per node per synchronous window' ;;
        figure-retained) property=DriverSource; expected='Figure root memo must release all transform references' ;;
        gate-inverted) property=DriverSource; expected='Native gate and shader admission must match authored state' ;;
        phase-unbounded) property=DriverSource; expected='Wall material memo must bracket the real synchronous WallCache phase with finally' ;;
        material-bypass-ignored) expected='Material cache setting must be sampled once per phase' ;;
        figure-bypass-ignored) property=DriverSource; expected='Disabled figure root cache must retain the existing figure ancestry window' ;;
    esac
    if [[ "$mutation" == phase-unbounded ]]; then
        if python3 "$repo_root/tests/GloomhavenVR.WallReadFactsTests/extract-driver.py" \
            "$mutation_dir/mutant.cs" "$mutation_dir/extracted.cs" > "$mutation_dir/output.log" 2>&1; then
            echo "FAIL: $mutation escaped the production lifetime integration guard." >&2
            exit 1
        fi
    elif dotnet run --project "$project" --configuration Release \
        --property:"$property=$mutation_dir/mutant.cs" > "$mutation_dir/output.log" 2>&1; then
        cat "$mutation_dir/output.log"
        echo "FAIL: $mutation escaped the WallCache read regression test." >&2
        exit 1
    fi
    if ! grep -Fq "$expected" "$mutation_dir/output.log"; then
        cat "$mutation_dir/output.log"
        echo "FAIL: $mutation did not reach its intended read-fact defect." >&2
        exit 1
    fi
    echo "Wall read facts negative control: $mutation failed as expected."
done
