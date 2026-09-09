#!/usr/bin/env bash
# Execute production native playback helpers, including actual-target repair and field ownership.
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$repo_root/tests/GloomhavenVR.NativePlaybackTests/GloomhavenVR.NativePlaybackTests.csproj"
dotnet run --project "$project" --configuration Release
mutation_dir="$(mktemp -d)"
trap 'rm -rf "$mutation_dir"' EXIT
cp "$repo_root/tests/GloomhavenVR.NativePlaybackTests/"*.cs "$mutation_dir/"
cp "$project" "$mutation_dir/"
for mutation in unconditional-write material-ping-pong stale-shader; do
    python3 - "$repo_root/src/GloomhavenVR/Net/Remote/NativePlaybackWrites.cs" "$mutation_dir/Playback.fixture" "$mutation" <<'PY'
import pathlib, sys
source = pathlib.Path(sys.argv[1]).read_text()
mutations = {
    'unconditional-write': ('if (!target.GetFloat(property).Equals(value)) target.SetFloat(property, value);', 'target.SetFloat(property, value);'),
    'material-ping-pong': ('if (_owned == null) NativePlaybackWrites.Material(target, source.material);', 'NativePlaybackWrites.Material(target, source.material);'),
    'stale-shader': ('if (shader != null && ReferenceEquals(_shader, shader)) return _support;', 'if (_shader != null) return _support;'),
}
needle, replacement = mutations[sys.argv[3]]
assert source.count(needle) == 1, 'production mutation seam changed'
pathlib.Path(sys.argv[2]).write_text(source.replace(needle, replacement))
PY
    if dotnet run --project "$mutation_dir/GloomhavenVR.NativePlaybackTests.csproj" --configuration Release \
        --property:PlaybackSource="$mutation_dir/Playback.fixture" > "$mutation_dir/mutant.log" 2>&1; then
        cat "$mutation_dir/mutant.log"
        echo "FAIL: runtime negative control survived: $mutation" >&2
        exit 1
    fi
    case "$mutation" in
        unconditional-write) expected='Unchanged native output must perform no writes' ;;
        material-ping-pong) expected='Claimed material avoids mirror/owner ping-pong' ;;
        stale-shader) expected='Shader replacement on the same material invalidates support immediately' ;;
    esac
    if ! grep -Fq "Unhandled exception. System.InvalidOperationException: $expected" "$mutation_dir/mutant.log"; then
        cat "$mutation_dir/mutant.log"
        echo "FAIL: negative control did not reach its runtime assertion: $mutation" >&2
        exit 1
    fi
    echo "Native playback runtime negative control failed as expected: $mutation"
done
