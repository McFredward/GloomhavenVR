#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if ! command -v dotnet >/dev/null 2>&1 && [[ -x "$HOME/.dotnet/dotnet" ]]; then
    export DOTNET_ROOT="$HOME/.dotnet"
fi
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$repo_root/tests/GloomhavenVR.SelfUpdateDialogTests/GloomhavenVR.SelfUpdateDialogTests.csproj"
dialog_source="${1:-$repo_root/src/GloomhavenVR/WorldUI/SelfUpdateDialog.cs}"
dotnet run --project "$project" --configuration Release --property:DialogSource="$dialog_source"

mutation_dir="$(mktemp -d)"
trap 'rm -rf "$mutation_dir"' EXIT
cp "$repo_root/tests/GloomhavenVR.SelfUpdateDialogTests/"*.cs "$mutation_dir/"
cp "$project" "$mutation_dir/"
python3 - "$dialog_source" "$mutation_dir/SelfUpdateDialog.fixture" <<'MUTATION'
import pathlib, sys
source = pathlib.Path(sys.argv[1]).read_text()
needle = 'new GameObject("Progress", typeof(RectTransform))'
assert source.count(needle) == 1, 'production progress-row construction seam changed'
pathlib.Path(sys.argv[2]).write_text(source.replace(needle, 'new GameObject("Progress")'))
MUTATION
if dotnet run --project "$mutation_dir/GloomhavenVR.SelfUpdateDialogTests.csproj" --configuration Release \
    --property:DialogSource="$mutation_dir/SelfUpdateDialog.fixture" > "$mutation_dir/mutant.log" 2>&1; then
    cat "$mutation_dir/mutant.log"
    echo "FAIL: bare Progress Transform escaped the self-update dialog regression test." >&2
    exit 1
fi
if ! grep -Fq 'System.InvalidCastException' "$mutation_dir/mutant.log"; then
    cat "$mutation_dir/mutant.log"
    echo "FAIL: self-update dialog negative control did not reach the injected transform defect." >&2
    exit 1
fi
echo "Self-update dialog runtime negative control: bare Progress Transform failed as expected."
