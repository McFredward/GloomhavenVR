#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$repo_root/tests/GloomhavenVR.PanelInkTests/GloomhavenVR.PanelInkTests.csproj"
source_file="$repo_root/src/GloomhavenVR/WorldUI/Conversion/PanelInkBounds.cs"
heading_source="$repo_root/src/GloomhavenVR/WorldUI/Conversion/RewardHeadingBounds.cs"
mutation_dir="$(mktemp -d)"
trap 'rm -rf "$mutation_dir"' EXIT
cp "$repo_root/tests/GloomhavenVR.PanelInkTests/"*.cs "$mutation_dir/"
cp "$project" "$mutation_dir/"
python3 - "$repo_root" "$mutation_dir" <<'PY'
import pathlib, re, sys
repo, root = map(pathlib.Path, sys.argv[1:])
source = (repo / 'src/GloomhavenVR/WorldUI/Conversion/PanelInkBounds.cs').read_text()
visibility = (repo / 'src/GloomhavenVR/WorldUI/MrBackingVisibility.cs').read_text()
trace = (repo / 'src/GloomhavenVR/WorldUI/Conversion/MrBackingBoundsTrace.cs').read_text()
for name, needle, replacement in [
    ('trace-cap', 'Reports >= 8', 'Reports >= 800'),
    ('trace-steady', 'if (Seen && Valid == valid && (!valid || !moved))', 'if (Seen && Valid == valid && (!valid || !moved) && Reports < 0)'),
    ('trace-throw', 'catch (Exception)', 'catch (Exception) when (panel == null)'),
]:
    assert trace.count(needle) == 1, name
    (root / (name+'.fixture')).write_text(trace.replace(needle,replacement))
needle = 'if (backingGeometry) MrBackingBoundsTrace.Observe(panel, ink, measured);'
assert source.count(needle) == 1
(root / 'trace-binding.fixture').write_text(source.replace(needle,''))
for name, needle, replacement in [
    ('live-hidden', ' || !graphic.gameObject.activeInHierarchy', ''),
    ('live-alpha', 'graphic.color.a * renderer.GetInheritedAlpha()', 'graphic.color.a'),
    ('live-canvas', ' || !canvas.isActiveAndEnabled', ''),
]:
    assert visibility.count(needle) == 1, name
    (root / (name+'.fixture')).write_text(visibility.replace(needle,replacement))
painted = (repo / 'src/GloomhavenVR/WorldUI/Conversion/MrBackingPaintedBounds.cs').read_text()
for name, needle, replacement in [
    ('paint-mask', 'mask != null && mask.enabled && !mask.showMaskGraphic', 'mask != null && !graphic.enabled'),
    ('paint-alpha', 'colorsPresent && Colors[i].a == 0', 'colorsPresent && Vertices.Count < 0'),
    ('paint-transform', 'toHost.MultiplyPoint3x4(Vertices[i])', 'Vertices[i]'),
]:
    assert painted.count(needle) == 1, name
    (root / (name+'.fixture')).write_text(painted.replace(needle,replacement))
needle = '!backingGeometry && contentRoot == null'
assert source.count(needle) == 1
(root / 'paint-backdrop.fixture').write_text(source.replace(needle,'contentRoot == null'))
needle = 'MrBackingPaintedBounds.TryMeasure(host, graphic, out drawBounds)'
assert source.count(needle) == 1
(root / 'paint-layout.fixture').write_text(source.replace(needle,'TryHostLocalBounds(host, rt, out drawBounds)'))
needle = 'visibleWitnesses?.Clear();'
assert source.count(needle) == 2
(root / 'live-clear.fixture').write_text(source.replace(needle, '', 1))
needle = '!ReferenceEquals(graphic, panel.ContentGraphic)'
assert source.count(needle) == 1
(root / 'missing.fixture').write_text(source.replace(needle, 'true'))
(root / 'broad.fixture').write_text(source.replace(needle, 'panel.ContentGraphic == null'))
needle = 'RewardHeadingBounds.Expand(host, graphic, bounds)'
assert source.count(needle) == 1
(root / 'reward-heading.fixture').write_text(source.replace(needle, 'bounds'))
needle = 'frameOverride ?? host.rect'
assert source.count(needle) == 1
(root / 'mr-frame.fixture').write_text(source.replace(needle, 'host.rect'))
needle = '(excludedRoots != null && excludedRoots.Contains(t))'
assert source.count(needle) == 1
(root / 'mr-hover.fixture').write_text(source.replace(needle, 'false'))
for name, needle, replacement in [
    ('mr-scope', 'MrBackingScope.Paint(t, contentRoot)', 'true'),
    ('mr-scope-plate', 'contentRoot == null && !ReferenceEquals(graphic, panel.ContentGraphic)', '!ReferenceEquals(graphic, panel.ContentGraphic)'),
    ('mr-scope-clip', 'if (ClipsChildren(t) && !Intersect(clip, bounds, out clip))', 'if (contentRoot == null && ClipsChildren(t) && !Intersect(clip, bounds, out clip))'),
]:
    assert source.count(needle) == 1, name
    (root / (name+'.fixture')).write_text(source.replace(needle, replacement))
needle = 'RewardHeadingBounds.Expand(host, graphic, bounds)'
capture = (repo / 'src/GloomhavenVR/WorldUI/Sharpness/PanelSupersample.4.Content.cs').read_text()
measure = capture[capture.index('    private static void MeasureFrame('):]
measure = measure[:measure.index('        ContentStack.Clear();', measure.index('        ContentStack.Clear();') + 1)]
# The capture must union glyph draw bounds AFTER fixing native mask geometry and BEFORE
# clipping that draw; its scale census must keep the original RectTransform extent.
assert measure.count(needle) == 1
assert measure.index('ClipsChildren(t)') < measure.index(needle) < measure.index('Intersect(clip, drawBounds, out Rect visible)')
assert 'float sx = bounds.width / local.width;' in measure
assert 'float sy = bounds.height / local.height;' in measure
print('Reward heading capture binding: original masks and authored scale census retained.')
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
for mutation in missing broad hint-ink hint-union reward-heading mr-frame mr-hover mr-scope mr-scope-plate mr-scope-clip live-hidden live-alpha live-canvas live-clear paint-mask paint-alpha paint-transform paint-backdrop paint-layout trace-cap trace-steady trace-throw trace-binding; do
    ink_source="$mutation_dir/$mutation.fixture"
    union_source="$mutation_dir/union.fixture"
    visibility_source="$repo_root/src/GloomhavenVR/WorldUI/MrBackingVisibility.cs"
    trace_source="$repo_root/src/GloomhavenVR/WorldUI/Conversion/MrBackingBoundsTrace.cs"
    if [[ "$mutation" == trace-cap || "$mutation" == trace-steady || "$mutation" == trace-throw ]]; then
        ink_source="$source_file"
        trace_source="$mutation_dir/$mutation.fixture"
    fi
    painted_source="$repo_root/src/GloomhavenVR/WorldUI/Conversion/MrBackingPaintedBounds.cs"
    if [[ "$mutation" == paint-mask || "$mutation" == paint-alpha || "$mutation" == paint-transform ]]; then
        ink_source="$source_file"
        painted_source="$mutation_dir/$mutation.fixture"
    fi
    if [[ "$mutation" == live-hidden || "$mutation" == live-alpha || "$mutation" == live-canvas ]]; then
        ink_source="$source_file"
        visibility_source="$mutation_dir/$mutation.fixture"
    fi
    if [[ "$mutation" == hint-union ]]; then
        ink_source="$source_file"
        union_source="$mutation_dir/hint-union.fixture"
    fi
    if dotnet run --project "$mutation_dir/GloomhavenVR.PanelInkTests.csproj" --configuration Release \
        --property:TraceSource="$trace_source" --property:PaintedSource="$painted_source" --property:VisibilitySource="$visibility_source" --property:ScopeSource="$repo_root/src/GloomhavenVR/WorldUI/MrBackingScope.cs" --property:InkSource="$ink_source" --property:HeadingSource="$heading_source" --property:DrawnUnionSource="$union_source" > "$mutation_dir/$mutation.log" 2>&1; then
        cat "$mutation_dir/$mutation.log"
        echo "FAIL: $mutation mutation escaped the ink test." >&2
        exit 1
    fi
    expected='full-frame movie remains measurable content'
    if [[ "$mutation" == broad ]]; then expected='content exemption never admits a neighboring backdrop'; fi
    if [[ "$mutation" == hint-ink ]]; then expected='placement fallback excludes hint'; fi
    if [[ "$mutation" == reward-heading ]]; then expected='reward heading includes overflowing glyphs'; fi
    if [[ "$mutation" == hint-union ]]; then expected='visible placement excludes hint'; fi
    if [[ "$mutation" == mr-frame ]]; then expected='mirror backdrop classification uses the sampled owner frame'; fi
    if [[ "$mutation" == mr-hover ]]; then expected='neutralized remote hover branch must not inflate'; fi
    if [[ "$mutation" == mr-scope ]]; then expected='scoped initiative excludes ancestor screen artwork'; fi
    if [[ "$mutation" == mr-scope-plate ]]; then expected='scoped full-frame portrait is real content'; fi
    if [[ "$mutation" == mr-scope-clip ]]; then expected='scoped portrait retains ancestor clipping'; fi
    if [[ "$mutation" == live-hidden ]]; then expected='native hidden ancestor immediately hides backing'; fi
    if [[ "$mutation" == live-alpha ]]; then expected='backing alpha follows current native effective alpha'; fi
    if [[ "$mutation" == live-canvas ]]; then expected='native disabled canvas immediately hides backing'; fi
    if [[ "$mutation" == live-clear ]]; then expected='empty geometry sample clears stale witness references'; fi
    if [[ "$mutation" == paint-mask ]]; then expected='nonpainting stencil keeps descendant clipping'; fi
    if [[ "$mutation" == paint-alpha ]]; then expected='fully transparent mesh has no painted backing'; fi
    if [[ "$mutation" == paint-transform ]]; then expected='painted vertices retain the complete native transform chain'; fi
    if [[ "$mutation" == paint-backdrop || "$mutation" == paint-layout ]]; then expected='painted merchant bounds exclude a tall empty label layout rectangle'; fi
    if [[ "$mutation" == trace-cap ]]; then expected='each converted panel has an eight-record diagnostic ceiling'; fi
    if [[ "$mutation" == trace-steady ]]; then expected='unchanged diagnostics emit no duplicate records or allocations'; fi
    if [[ "$mutation" == trace-throw ]]; then expected='throwing diagnostic getter cannot invalidate'; fi
    if [[ "$mutation" == trace-binding ]]; then expected='first MR trace identifies original extrema'; fi
    if ! rg -q "$expected" "$mutation_dir/$mutation.log"; then
        cat "$mutation_dir/$mutation.log"
        exit 1
    fi
done
echo "Panel ink negative controls: twenty-three runtime mutations and one placement binding mutation rejected."
