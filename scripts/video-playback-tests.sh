#!/usr/bin/env bash
# Execute the real shared-video state machine against controlled decoder/audio components.
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$repo_root/tests/GloomhavenVR.VideoPlaybackTests/GloomhavenVR.VideoPlaybackTests.csproj"
dotnet run --project "$project" --configuration Release
mutation_dir="$(mktemp -d)"
trap 'rm -rf "$mutation_dir"' EXIT
for mutation in cosmetic-authority cosmetic-end premature-audio-latch stale-audio-binding destroyed-native-audio stereo-completion stereo-fallback-report original-completion elected-window-key original-presence-key; do
    python3 - "$repo_root/src/GloomhavenVR/Net/Remote/RemoteVideoPlayback.cs" "$mutation_dir/mutant.cs" "$mutation" <<'PY'
import pathlib, sys
source = pathlib.Path(sys.argv[1]).read_text()
if sys.argv[3] == 'premature-audio-latch':
    source = source.replace('|| AudioBindingChanged()', '')
mutations = {
    'cosmetic-authority': ('private static void FailMirror(string reason)\n    {',
        'private static void FailMirror(string reason)\n    {\n        FinishActivePlayback();'),
    'cosmetic-end': ('private static void HoldMirrorEnd(VideoPlayer player)\n    {',
        'private static void HoldMirrorEnd(VideoPlayer player)\n    {\n        FinishActivePlayback();'),
    'premature-audio-latch': ('|| !_native.isPrepared || _native.audioTrackCount == 0', ''),
    'stale-audio-binding': ('|| AudioBindingChanged()', ''),
    'destroyed-native-audio': ('if (_savedMute != null)', 'if (_mutedNative != null && _savedMute != null)'),
    'stereo-completion': ('NativeVideoMovies.SamePresentation(_nativeClip, _active.Clip)', '_nativeClip == _active.Clip'),
    'stereo-fallback-report': ('&& !NativeVideoMovies.IsStereoUrl(url)', '&& NativeVideoMovies.IsStereoUrl(url)'),
    'original-completion': ('NativeVideoMovies.SamePresentation(_nativeClip, _active.Clip)', '(_active.Clip != NativeVideoMovies.NativeIntroKey && NativeVideoMovies.SamePresentation(_nativeClip, _active.Clip))'),
    'elected-window-key': ('SetRemoteSource(_mirror, _active.Clip ?? string.Empty)', 'SetRemoteSource(_mirror)'),
    'original-presence-key': ('Revision = ++_revision, Clip = _nativeClip,', 'Revision = ++_revision, Clip = _nativeClip == NativeVideoMovies.NativeIntroKey ? "CP_Intro/GH_CP_Intro.mov" : _nativeClip,'),
}
needle, replacement = mutations[sys.argv[3]]
assert source.count(needle) == 1, 'video mutation seam changed: ' + sys.argv[3]
pathlib.Path(sys.argv[2]).write_text(source.replace(needle, replacement))
PY
    if dotnet run --project "$project" --configuration Release \
        --property:PlaybackSource="$mutation_dir/mutant.cs" > "$mutation_dir/output.log" 2>&1; then
        cat "$mutation_dir/output.log"
        echo "FAIL: video playback mutation survived: $mutation" >&2
        exit 1
    fi
    case "$mutation" in
        cosmetic-authority) expected='Local cosmetic decoder error must not suppress or stop healthy native playback' ;;
        cosmetic-end) expected='Cosmetic decoder end holds its last frame without replay or source retirement' ;;
        premature-audio-latch) expected='Native audio tracks discovered after decoder preparation must still be muted' ;;
        stale-audio-binding) expected='New initially audible track is muted when native track count grows' ;;
        destroyed-native-audio) expected='Destroyed native decoder still restores surviving target AudioSources' ;;
        stereo-completion) expected='owner stereo completion retires matching concurrent mono presentation without native callbacks' ;;
        stereo-fallback-report) expected='shared stereo companion fallback explicitly reports once per elected opening' ;;
        original-completion) expected='original owner completion retires concurrent intro variants without native callbacks' ;;
        elected-window-key) expected='cosmetic window receives elected key separately from its resolved media URL' ;;
        original-presence-key) expected='presence publishes the exact original alias without canonicalizing content selection' ;;
    esac
    if ! rg -Fq "Unhandled exception. System.InvalidOperationException: $expected" "$mutation_dir/output.log"; then
        cat "$mutation_dir/output.log"
        echo "FAIL: video mutation did not reach expected runtime assertion: $mutation" >&2
        exit 1
    fi
    echo "Video playback negative control: $mutation failed as expected."
done
