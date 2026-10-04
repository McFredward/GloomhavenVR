#!/usr/bin/env bash
# Compile actual WallCache material admission and root lookup; count avoided native probes.
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if ! command -v dotnet >/dev/null 2>&1 && [[ -x "$HOME/.dotnet/dotnet" ]]; then
    export DOTNET_ROOT="$HOME/.dotnet"
fi
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$repo_root/tests/GloomhavenVR.WallReadFactsTests/GloomhavenVR.WallReadFactsTests.csproj"
mutations=(material-no-cache material-retained figure-no-cache figure-retained gate-inverted phase-unbounded material-bypass-ignored figure-bypass-ignored selection-owner-broadened drawing-force-ignored masked-restitution-lost prepared-cross-frame prepared-label-unshared prepare-room-gate-omitted mirror-owner-ignored mirror-cache-ignored mirror-cache-unbounded mirror-wall-signature-unconditional mirror-death-reclassified root-selection-owner-broadened root-selection-owner-ignored waypoint-parent-broadened waypoint-owner-ignored action-prefab-broadened action-parked-omitted native-water-veto-omitted native-wall-veto-omitted native-live-owner-omitted native-high-family-broadened native-high-diagnostic-suffix-ignored)
if (($#)); then
    mutations=()
    while (($#)); do
        [[ "$1" == --mutation && $# -ge 2 ]] || { echo "Usage: $0 [--mutation NAME ...]" >&2; exit 2; }
        mutations+=("$2"); shift 2
    done
else
    dotnet run --project "$project" --configuration Release
fi
mutation_dir="$(mktemp -d)"
trap 'rm -rf "$mutation_dir"' EXIT
for mutation in "${mutations[@]}"; do
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
    'selection-owner-broadened': ('ReferenceEquals(selector.HexProjector, renderer)', 'bool.Parse("true")'),
    'drawing-force-ignored': (' || r.forceRenderingOff', ''),
    'masked-restitution-lost': ('if (piece.Driven && previousState > highest)', 'if (bool.Parse("false"))'),
    'prepared-cross-frame': ('_preparedWallChildFrame != Time.frameCount\n                ||', 'bool.Parse("false")\n                ||'),
    'prepared-label-unshared': ('if (_standingLabels.TryGetValue(root, out StandingLabel cached)',
                              'if (bool.Parse("false") && _standingLabels.TryGetValue(root, out StandingLabel cached)'),
    'prepare-room-gate-omitted': ('if (gen.m_RoomRenderers.Count != _live.BuiltRoomCount)', 'if (bool.Parse("false"))'),
    'mirror-owner-ignored': ('renderer.GetComponentInParent<FigureVisualMirror>(true) != null', 'bool.Parse("false")'),
    'mirror-cache-ignored': ('if (_figureRootMemoActive\n                && VisualMirrorOwnershipMemo.TryGetValue',
                             'if (bool.Parse("false")\n                && VisualMirrorOwnershipMemo.TryGetValue'),
    'mirror-cache-unbounded': ('_figureRootMemoActive = false;\n            VisualMirrorOwnershipMemo.Clear();',
                               '_figureRootMemoActive = false;'),
    'mirror-wall-signature-unconditional': ('bool modExempt = f.Mod && !(f.Mesh != null && f.WallFadeShader);',
                                            'bool modExempt = f.Mod;'),
    'mirror-death-reclassified': ('bool holeExempt = SceneRowWasExemptWhenAlive(i);', 'bool holeExempt = false;'),
    'root-selection-owner-broadened': ('renderer.GetComponent<HexSelect_Control>() != null', 'renderer.GetComponentInParent<HexSelect_Control>(true) != null'),
    'root-selection-owner-ignored': ('renderer.GetComponent<HexSelect_Control>() != null', 'bool.Parse("false")'),
    'waypoint-parent-broadened': ('renderer.transform.IsChildOf(member.Prefab.transform)', 'bool.Parse("true")'),
    'waypoint-owner-ignored': ('owned = IsNativeWaypointParticle(renderer) || IsNativeActionParticle(renderer);', 'owned = IsNativeActionParticle(renderer);'),
    'action-prefab-broadened': ('IsPublishedActionPrefab(entry.Value, settings)', 'bool.Parse("true")'),
    'action-parked-omitted': ('if (parked != null)', 'if (parked != null && bool.Parse("false"))'),
    'native-water-veto-omitted': ('shader.IndexOf("Water_Sh", System.StringComparison.OrdinalIgnoreCase) >= 0', 'bool.Parse("false")'),
    'native-wall-veto-omitted': ('IsWallFadeShaderName(shader)', 'bool.Parse("false")'),
    'native-live-owner-omitted': ('if (!cold && f.Particles) f.Mod = f.ModPresentation || IsNativeNonWallPresentation(r);', '// injected: immutable native pooled ownership'),
    'native-high-family-broadened': ('name == "Amp_Basic_WallFade"', 'name.StartsWith("Amp_Basic")'),
    'native-high-diagnostic-suffix-ignored': ('|| name == "Amp_Basic_N_MRAO(toggle-native)";', ';'),
}
if mutation in ('figure-retained', 'gate-inverted', 'phase-unbounded', 'figure-bypass-ignored', 'mirror-cache-unbounded', 'mirror-wall-signature-unconditional', 'mirror-death-reclassified', 'native-live-owner-omitted'):
    source = root / 'src/GloomhavenVR/Core/WallFade/WallSegmentFade.cs'
extra_sources = {
    'selection-owner-broadened': 'WallSegmentFade.SelectionFacts.cs',
    'mirror-owner-ignored': 'WallSegmentFade.SelectionFacts.cs',
    'mirror-cache-ignored': 'WallSegmentFade.SelectionFacts.cs',
    'root-selection-owner-broadened': 'WallSegmentFade.SelectionFacts.cs',
    'root-selection-owner-ignored': 'WallSegmentFade.SelectionFacts.cs',
    'waypoint-parent-broadened': 'WallSegmentFade.SelectionFacts.cs',
    'waypoint-owner-ignored': 'WallSegmentFade.SelectionFacts.cs',
    'action-prefab-broadened': 'WallSegmentFade.SelectionFacts.cs',
    'action-parked-omitted': 'WallSegmentFade.SelectionFacts.cs',
    'native-water-veto-omitted': 'WallSegmentFade.SelectionFacts.cs',
    'native-wall-veto-omitted': 'WallSegmentFade.SelectionFacts.cs',
    'drawing-force-ignored': 'WallSegmentFade.Mounted.cs',
    'masked-restitution-lost': 'WallSegmentFade.BudgetMask.cs',
    'prepared-cross-frame': 'WallSegmentFade.PreparedReads.cs',
    'prepared-label-unshared': 'WallSegmentFade.PreparedReads.cs',
    'prepare-room-gate-omitted': 'WallSegmentFade.Prepare.cs',
    'native-high-family-broadened': 'WallSegmentFade.NativeTransition.cs',
    'native-high-diagnostic-suffix-ignored': 'WallSegmentFade.NativeTransition.cs',
}
if mutation in extra_sources:
    source = root / 'src/GloomhavenVR/Core/WallFade' / extra_sources[mutation]
needle, replacement = changes[mutation]
text = source.read_text()
# The figure clear appears at both begin/end; remove the end only.
if mutation in ('figure-retained', 'prepare-room-gate-omitted'):
    start = text.index('private static void EndFigureMemo()' if mutation == 'figure-retained' else 'private void VerifyPrepareStillValid(')
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
        selection-owner-broadened) property=SelectionFactsSource; expected='Being near a selector never exempts a real wall mesh' ;;
        drawing-force-ignored) property=MountedSource; expected='Force-hidden scenery never contributes false drawing diagnostics' ;;
        masked-restitution-lost) property=BudgetMaskSource; expected='A masked driven piece retains the outstanding wall restitution latch' ;;
        prepared-cross-frame) property=PreparedReadsSource; expected='A cross-frame native regeneration discards prepared hierarchy facts' ;;
        prepared-label-unshared) property=PreparedReadsSource; expected='Repeated unit child keeps one exact immutable diagnostic label' ;;
        prepare-room-gate-omitted) property=PrepareSource; expected='A room reveal during preparation drops all old measured floor derivations before publication' ;;
        mirror-owner-ignored) property=SelectionFactsSource; expected='An inactive native-named mirror child is excluded from live wall adoption' ;;
        mirror-cache-ignored) property=SelectionFactsSource; expected='Repeated live adoption lanes query each exact mirror ancestor once per synchronous scope' ;;
        mirror-cache-unbounded) property=DriverSource; expected='Mirror ownership memo releases every renderer at the scope boundary' ;;
        mirror-wall-signature-unconditional) property=DriverSource; expected='A mirror mesh with a real wall-fade shader retains its conservative signature' ;;
        mirror-death-reclassified) property=DriverSource; expected='A dead clone row retains its exact exemption without signature churn' ;;
        root-selection-owner-broadened) property=SelectionFactsSource; expected='Parent proximity never exempts a foreign selection-root emitter' ;;
        root-selection-owner-ignored) property=SelectionFactsSource; expected='Exact native root selection emitter is not wall scenery' ;;
        waypoint-parent-broadened) property=SelectionFactsSource; expected='Unpublished nearby same-name scenery cannot become a waypoint' ;;
        waypoint-owner-ignored) property=SelectionFactsSource; expected='Published waypoint particles share exact signature and live collector rejection' ;;
        action-prefab-broadened) property=SelectionFactsSource; expected='Pooled root reassigned to scenery immediately loses action ownership' ;;
        action-parked-omitted) property=SelectionFactsSource; expected='Native recycle and inactive action pool preserve exact prefab ownership' ;;
        native-water-veto-omitted) property=SelectionFactsSource; expected='Published waypoint water remains a protection input' ;;
        native-wall-veto-omitted) property=SelectionFactsSource; expected='Published waypoint with a real wall shader remains a table input' ;;
        native-live-owner-omitted) property=DriverSource; expected='Reparented unpublished waypoint fails conservative on warm ownership' ;;
        native-high-family-broadened) property=NativeTransitionSource; expected='An unverified themed toggle material permanently rejects the native HIGH branch for this segment' ;;
        native-high-diagnostic-suffix-ignored) property=NativeTransitionSource; expected='Both verified native HIGH and cached toggle diagnostic suffix retain the exact branch' ;;
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
