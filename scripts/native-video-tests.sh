#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$repo_root/tests/GloomhavenVR.NativeVideoTests/GloomhavenVR.NativeVideoTests.csproj"
source_file="$repo_root/src/GloomhavenVR/WorldUI/FlatScreen/NativeVideoWindow.cs"
dotnet run --project "$project" --configuration Release
mutation_dir="$(mktemp -d)"
trap 'rm -rf "$mutation_dir"' EXIT
cp "$repo_root/tests/GloomhavenVR.NativeVideoTests/"*.cs "$mutation_dir/"
cp "$project" "$mutation_dir/"
python3 - "$source_file" "$mutation_dir/NativeVideoWindow.fixture" <<'PY'
import pathlib, sys
source = pathlib.Path(sys.argv[1]).read_text()
needle = '|| source.frame < 0'
assert source.count(needle) == 1
pathlib.Path(sys.argv[2]).write_text(source.replace(needle, ''))
PY
if dotnet run --project "$mutation_dir/GloomhavenVR.NativeVideoTests.csproj" --configuration Release \
    --property:VideoSource="$mutation_dir/NativeVideoWindow.fixture" > "$mutation_dir/mutant.log" 2>&1; then
    cat "$mutation_dir/mutant.log"
    echo "FAIL: missing decoded-frame reveal gate escaped native video test." >&2
    exit 1
fi
if ! rg -q 'first decoded frame gates the grab bar' "$mutation_dir/mutant.log"; then
    cat "$mutation_dir/mutant.log"
    exit 1
fi
echo "Native video negative control: premature empty window rejected."
