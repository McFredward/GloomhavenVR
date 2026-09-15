#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$repo_root/tests/GloomhavenVR.NativeVideoTests/GloomhavenVR.NativeVideoTests.csproj"
source_file="$repo_root/src/GloomhavenVR/WorldUI/FlatScreen/NativeVideoWindow.cs"
mutation_dir="$(mktemp -d)"
trap 'rm -rf "$mutation_dir"' EXIT
# Execute the actual orphan sweep beside the actual movie owner. The containing modal
# manager also controls native gameplay UI, so isolate this method with API substitutes.
python3 - "$repo_root" "$mutation_dir" <<'PY'
import pathlib, sys
root, out = map(pathlib.Path, sys.argv[1:])
source = (root / 'src/GloomhavenVR/WorldUI/Modal/ModalFallback.9.Spawn.cs').read_text()
start = source.index('    private static void SweepOrphanChrome()')
end = source.index('    private static void LogPollTransition', start)
sweep = 'using System;\nusing GloomhavenVR.Core;\nnamespace GloomhavenVR.WorldUI;\ninternal static partial class ModalFallback\n{\n' + source[start:end] + '\n}'
pointer = (root / 'src/GloomhavenVR/Hands/Interact/UguiPointer.cs').read_text()
start_guard = pointer.index('    internal static bool ShouldWithholdUnstartedWindowClick(')
end_guard = pointer.index('    /// <summary>Abort any in-flight', start_guard)
guard = 'using System;\nusing UnityEngine;\nusing UnityEngine.UI;\nnamespace GloomhavenVR.Hands.Interact;\ninternal sealed class UguiPointer\n{\nprivate static int s_unstartedWithheld;\nprivate static string? s_lastUnstartedKey;\n' + pointer[start_guard:end_guard] + '\n}'
(out / 'PointerGuard.fixture').write_text(guard)
needle_guard = '&& WorldUI.NativeVideoWindow.OwnsClick(clickHandler)'
assert guard.count(needle_guard) == 1
(out / 'PointerGuard.mutant').write_text(guard.replace(needle_guard, '&& false'))
# Pin the actual Release dispatch seam as well: this must remain the guard shared by
# the laser and fingertip before ExecuteEvents sends the attached movie handler its click.
release = pointer[pointer.index('    internal void Release('):pointer.index('    internal static bool ShouldWithholdUnstartedWindowClick(')]
assert release.index('ShouldWithholdUnstartedWindowClick(_pressedClickHandler, _sourceTag)') < release.index('ExecuteEvents.Execute(_pressedClickHandler, data, ExecuteEvents.pointerClickHandler)')
(out / 'ChromeSweep.fixture').write_text(sweep)
needle = 'bool owned = NativeVideoWindow.OwnsGrab(holder);'
assert sweep.count(needle) == 1
(out / 'ChromeSweep.mutant').write_text(sweep.replace(needle, 'bool owned = false;'))
# Teardown must close the video as well as the ordinary conversions it owns.
start = source.index('    private static void ReleaseAllWindows(')
assert 'NativeVideoWindow.Shutdown();' in source[start:source.index('WindowMaterialise.CancelAll(reason);', start)]
PY
dotnet run --project "$project" --configuration Release \
    --property:ChromeSweepSource="$mutation_dir/ChromeSweep.fixture" \
    --property:PointerGuardSource="$mutation_dir/PointerGuard.fixture"
cp "$repo_root/tests/GloomhavenVR.NativeVideoTests/"*.cs "$mutation_dir/"
cp "$project" "$mutation_dir/"
for mutation in premature-frame orphan-sweep scene-lifetime content-binding pointer-guard hero-stop; do
    python3 - "$source_file" "$mutation_dir/NativeVideoWindow.fixture" "$mutation" <<'PY'
import pathlib, sys
source = pathlib.Path(sys.argv[1]).read_text()
mutations = {
    'premature-frame': ('|| source.frame < 0', ''),
    'content-binding': ('ContentGraphic = _image,', ''),
    'scene-lifetime': ('UnityEngine.Object.DontDestroyOnLoad(grabFrame.root.gameObject);', '{}'),
    'hero-stop': ('NativeEndReached.Invoke(VideoCamera.s_This, new object[] { native });', 'VideoCamera.s_This.Stop();'),
}
if sys.argv[3] in mutations:
    before, after = mutations[sys.argv[3]]
    assert source.count(before) == 1
    source = source.replace(before, after)
pathlib.Path(sys.argv[2]).write_text(source)
PY
    sweep="$mutation_dir/ChromeSweep.fixture"
    pointer_guard="$mutation_dir/PointerGuard.fixture"
    case "$mutation" in
        premature-frame) expected='first decoded frame gates the grab bar' ;;
        orphan-sweep) expected='orphan sweep preserves the live movie holder'; sweep="$mutation_dir/ChromeSweep.mutant" ;;
        scene-lifetime) expected='grab holder survives scene unload with the movie' ;;
        content-binding) expected='movie declares its actual pixels as full-frame content' ;;
        pointer-guard) expected='laser and poke delivery guard admits the actual movie handler'; pointer_guard="$mutation_dir/PointerGuard.mutant" ;;
        hero-stop) expected='hero click runs native completion and releases reward input lock' ;;
    esac
    if dotnet run --project "$mutation_dir/GloomhavenVR.NativeVideoTests.csproj" --configuration Release \
        --property:VideoSource="$mutation_dir/NativeVideoWindow.fixture" \
        --property:ChromeSweepSource="$sweep" \
        --property:PointerGuardSource="$pointer_guard" > "$mutation_dir/mutant.log" 2>&1; then
        cat "$mutation_dir/mutant.log"
        echo "FAIL: $mutation escaped native video test." >&2
        exit 1
    fi
    if ! rg -q "$expected" "$mutation_dir/mutant.log"; then
        cat "$mutation_dir/mutant.log"
        exit 1
    fi
    echo "Native video negative control: $mutation rejected."
done
