#!/usr/bin/env bash
# Prove the send-reuse allocation regression is observable in the production scheduler.
# The normal vectors (including byte-exact four-sender replay) run in wire-tests.sh.
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if ! command -v dotnet >/dev/null 2>&1 && [[ -x "$HOME/.dotnet/dotnet" ]]; then
    export DOTNET_ROOT="$HOME/.dotnet"
fi
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
# A child of tests inherits the same build properties, without touching tracked input files.
mutation_dir="$(mktemp -d "$repo_root/tests/.send-reuse-negative.XXXXXX")"
trap 'rm -rf "$mutation_dir"' EXIT
cp "$repo_root/tests/GloomhavenVR.WireTests/"*.cs "$mutation_dir/"
cp "$repo_root/tests/GloomhavenVR.WireTests/GloomhavenVR.WireTests.csproj" "$mutation_dir/"
python3 - "$repo_root" "$mutation_dir" <<'PY'
import pathlib, sys
root, target = map(pathlib.Path, sys.argv[1:])
source = (root / 'src/GloomhavenVR/Net/ExtrasSendQueue.cs').read_text()
old = 'if (identity is not CardAppearanceSnapshot && CardAppearanceCodec.TryRead'
assert source.count(old) == 1
(target / 'ExtrasSendQueue.cs').write_text(source.replace(old, 'if (CardAppearanceCodec.TryRead'))
project = target / 'GloomhavenVR.WireTests.csproj'
text = project.read_text()
old = '$(RepoRoot)src\\GloomhavenVR\\Net\\ExtrasSendQueue.cs'
assert text.count(old) == 1
project.write_text(text.replace(old, 'ExtrasSendQueue.cs'))
PY
project="$mutation_dir/GloomhavenVR.WireTests.csproj"
if ! dotnet build "$project" -c Release --nologo > "$mutation_dir/build.log" 2>&1; then
    cat "$mutation_dir/build.log" >&2
    echo "send reuse negative control did not compile" >&2
    exit 1
fi
if "$mutation_dir/bin/Release/net8.0/GloomhavenVR.WireTests" "$repo_root" > "$mutation_dir/run.log" 2>&1; then
    echo "send reuse negative control unexpectedly passed" >&2
    exit 1
fi
if ! rg -q 'reuse removes decoded object graphs' "$mutation_dir/run.log"; then
    cat "$mutation_dir/run.log" >&2
    echo "send reuse negative control failed for an unrelated reason" >&2
    exit 1
fi
echo "Native send negative control: redundant production decode failed the allocation assertion as expected."
