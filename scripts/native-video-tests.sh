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
needle = 'NativeVideoWindow.OwnsGrab(holder)'
assert sweep.count(needle) == 1
(out / 'ChromeSweep.mutant').write_text(sweep.replace(needle, 'false'))
retirement = 'MapRoom.MapRetirementPrompt.OwnsGrab(holder)'
assert sweep.count(retirement) == 1
(out / 'ChromeSweep.retirement-mutant').write_text(sweep.replace(retirement, 'false'))
# Teardown must close the video as well as the ordinary conversions it owns.
start = source.index('    private static void ReleaseAllWindows(')
assert 'NativeVideoWindow.Shutdown();' in source[start:source.index('WindowMaterialise.CancelAll(reason);', start)]
PY
dotnet run --project "$project" --configuration Release \
    --property:ChromeSweepSource="$mutation_dir/ChromeSweep.fixture" \
    --property:PointerGuardSource="$mutation_dir/PointerGuard.fixture"
python3 "$repo_root/tests/GloomhavenVR.NativeVideoTests/MoviePackagingTests.py" "$repo_root"
cp "$repo_root/tests/GloomhavenVR.NativeVideoTests/"*.cs "$mutation_dir/"
cp "$project" "$mutation_dir/"
for mutation in premature-frame orphan-sweep retirement-sweep scene-lifetime content-binding pointer-guard hero-stop stereo-aspect stereo-attach stereo-dispose stereo-left-uv stereo-dimensions stereo-skip native-skip opening-routing; do
    python3 - "$source_file" "$mutation_dir/NativeVideoWindow.fixture" "$mutation" <<'PY'
import pathlib, sys
source = pathlib.Path(sys.argv[1]).read_text()
mutations = {
    'premature-frame': ('|| source.frame < 0', ''),
    'content-binding': ('ContentGraphic = _image,', ''),
    'scene-lifetime': ('UnityEngine.Object.DontDestroyOnLoad(grabFrame.root.gameObject);', '{}'),
    'hero-stop': ('NativeEndReached.Invoke(VideoCamera.s_This, new object[] { native });', 'VideoCamera.s_This.Stop();'),
    'stereo-aspect': ('StereoVideoSurface.PerEyeAspect(source.texture.width, source.texture.height)', 'source.texture.width / (float)source.texture.height'),
    'stereo-attach': ('!StereoVideoSurface.TryAttach(_image, out _stereo, out string reason)', '!StereoVideoSurface.IsAvailable(out string reason)'),
    'stereo-dispose': ('_stereo?.Dispose();', '/* owned material release omitted */'),
    'stereo-left-uv': ('.5f - 1f / source.texture.width', '1f - 1f / source.texture.width'),
    'stereo-dimensions': ('source.texture.width != 3840 || source.texture.height != 1080', 'source.texture.width < 0 || source.texture.height < 0'),
    'stereo-skip': ('string key = NativeVideoMovies.CanonicalKey(PlaybackKey);', 'string key = PlaybackKey == NativeVideoMovies.NativeIntroKey ? NativeVideoMovies.IntroKey : PlaybackKey;'),
    'native-skip': ('string key = NativeVideoMovies.CanonicalKey(PlaybackKey);', 'string key = PlaybackKey == NativeVideoMovies.StereoIntroKey ? NativeVideoMovies.IntroKey : PlaybackKey;'),
    'opening-routing': ('(_stereo != null ? "stereo" : "left-only")', '(_stereo != null ? "stereo" : "stereo")'),
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
        retirement-sweep) expected='orphan sweep preserves the separately owned retirement prompt beside the movie'; sweep="$mutation_dir/ChromeSweep.retirement-mutant" ;;
        scene-lifetime) expected='grab holder survives scene unload with the movie' ;;
        content-binding) expected='movie declares its actual pixels as full-frame content' ;;
        pointer-guard) expected='laser and poke delivery guard admits the actual movie handler'; pointer_guard="$mutation_dir/PointerGuard.mutant" ;;
        hero-stop) expected='hero click runs native completion and releases reward input lock' ;;
        stereo-aspect|stereo-attach) expected='explicit SBS native window attaches stereo material with per-eye 16:9 geometry' ;;
        stereo-dispose) expected='external movie root destruction releases the owned stereo surface' ;;
        stereo-left-uv) expected='unsupported renderer shows only texel-centred left half with original per-eye geometry' ;;
        stereo-dimensions) expected='malformed selected SBS dimensions refuse picture without blocking native continuation' ;;
        stereo-skip) expected='versioned stereo native intro still dispatches its exact canonical native skip callback' ;;
        native-skip) expected='movie laser click calls the original active intro escape' ;;
        opening-routing) expected='window opening reports selected stereo key and actual left-only fallback routing' ;;
    esac
    if dotnet run --project "$mutation_dir/GloomhavenVR.NativeVideoTests.csproj" --configuration Release \
        --property:VideoSource="$mutation_dir/NativeVideoWindow.fixture" \
        --property:MovieSource="$repo_root/src/GloomhavenVR/WorldUI/FlatScreen/NativeVideoMovies.cs" \
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

# Intercept the actual patched caller: a direct helper-only assertion would stay green
# if Harmony stopped replacing the setter, broadened the requested name or left peers native.
for mutation in movie-interception movie-exact-name movie-peer-resolution stereo-before-play stereo-version stereo-companion stereo-no-native-substitution stereo-no-peer-upgrade native-owner-identity native-no-peer-upgrade mono-no-native-substitution; do
    # An intentionally unhandled negative-control assertion may terminate .NET before
    # finally cleanup. This output belongs exclusively to this serial mktemp fixture.
    rm -rf "$mutation_dir/bin/Release/net8.0/Movies"
    python3 - "$repo_root/src/GloomhavenVR/WorldUI/FlatScreen/NativeVideoMovies.cs" "$mutation_dir/NativeVideoMovies.fixture" "$mutation" <<'PY'
import pathlib, sys
source = pathlib.Path(sys.argv[1]).read_text()
mutations = {
    'movie-interception': ('if (count != 1)', 'if (count != 0)'),
    'movie-exact-name': ('requestedName == IntroName && Application.platform', 'Application.platform'),
    'movie-peer-resolution': ('if (TryModIntro(key, out url)) return true;', ''),
    'stereo-before-play': ('StereoVideoSurface.IsAvailable(out reason)', 'player.isPaused'),
    'stereo-version': ('extension == ".mov" && key == StereoIntroKey', 'extension == ".mov" && key.StartsWith(IntroName + ".sbs-", StringComparison.Ordinal)'),
    'stereo-companion': ('if (key == StereoIntroKey) return TryModIntro(IntroKey, out url);', 'if (key == StereoIntroKey) return false;'),
    'stereo-no-native-substitution': ('if (key == StereoIntroKey) return TryModIntro(IntroKey, out url);', 'if (key == StereoIntroKey) { if (TryModIntro(IntroKey, out url)) return true; key = NativeIntroKey; }'),
    'stereo-no-peer-upgrade': ('if (TryModIntro(key, out url)) return true;', 'if (key == IntroKey && TryModIntro(StereoIntroKey, out url)) return true; if (TryModIntro(key, out url)) return true;'),
    'native-owner-identity': ('if (key == IntroKey) key = NativeIntroKey;', 'if (key == IntroKey) key = IntroKey;'),
    'native-no-peer-upgrade': ('if (key == NativeIntroKey) key = IntroKey;', 'if (key == NativeIntroKey) { if (TryModIntro(IntroKey, out url)) return true; key = IntroKey; }'),
    'mono-no-native-substitution': ('if (key == IntroKey) return false;', ''),
}
before, after = mutations[sys.argv[3]]
assert source.count(before) == 1
pathlib.Path(sys.argv[2]).write_text(source.replace(before, after))
PY
    case "$mutation" in
        movie-interception) expected='actual Harmony interception assigns mod intro before the single native Play and setup' ;;
        movie-exact-name) expected='actual Harmony interception leaves Hero playback and canonical identity unchanged' ;;
        movie-peer-resolution) expected='remote canonical intro key resolves the same optional mod movie' ;;
        stereo-before-play) expected='stereo intro is selected before the single native Play and original setup' ;;
        stereo-version) expected='unknown stereo revision or filename is refused:' ;;
        stereo-companion) expected='missing selected SBS file uses only its approved original-left companion' ;;
        stereo-no-native-substitution) expected='missing SBS and approved companion never substitutes different native film content' ;;
        stereo-no-peer-upgrade) expected='owner mono key is never silently upgraded to local stereo media' ;;
        native-owner-identity) expected='native original owner publishes explicit original provenance before any mod assets exist' ;;
        native-no-peer-upgrade) expected="native original owner key never upgrades to a peer's present approved movie" ;;
        mono-no-native-substitution) expected='selected approved mono missing locally never substitutes the unrelated native movie' ;;
    esac
    if dotnet run --project "$mutation_dir/GloomhavenVR.NativeVideoTests.csproj" --configuration Release \
        --property:VideoSource="$source_file" \
        --property:MovieSource="$mutation_dir/NativeVideoMovies.fixture" \
        --property:ChromeSweepSource="$mutation_dir/ChromeSweep.fixture" \
        --property:PointerGuardSource="$mutation_dir/PointerGuard.fixture" > "$mutation_dir/movie-mutant.log" 2>&1; then
        cat "$mutation_dir/movie-mutant.log"
        echo "FAIL: $mutation escaped native movie interception test." >&2
        exit 1
    fi
    if ! rg -q "$expected" "$mutation_dir/movie-mutant.log"; then
        cat "$mutation_dir/movie-mutant.log"
        exit 1
    fi
    echo "Native movie negative control: $mutation rejected."
done
