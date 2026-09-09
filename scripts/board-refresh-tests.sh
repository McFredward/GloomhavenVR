#!/usr/bin/env bash
# Compile the production per-board refresh gate and prove broad owner invalidation fails.
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if ! command -v dotnet >/dev/null 2>&1 && [[ -x "$HOME/.dotnet/dotnet" ]]; then
    export DOTNET_ROOT="$HOME/.dotnet"
fi
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$repo_root/tests/GloomhavenVR.BoardRefreshTests/GloomhavenVR.BoardRefreshTests.csproj"
dotnet run --project "$project" --configuration Release
mutation_dir="$(mktemp -d)"
trap 'rm -rf "$mutation_dir"' EXIT
cp "$repo_root/tests/GloomhavenVR.BoardRefreshTests/"*.cs "$mutation_dir/"
cp "$project" "$mutation_dir/"
for mutation in broad-invalidation actor-identity readiness-edge; do
python3 - "$repo_root/src/GloomhavenVR/Net/Remote/RemoteBoardRefreshGate.cs" "$mutation_dir/Gate.fixture" "$mutation" <<'MUTATION'
import pathlib, sys
source = pathlib.Path(sys.argv[1]).read_text()
changes = {
    'broad-invalidation': (
        '_objectives.Poll(objectives, (recovery & RemoteBoardRefreshSections.Objectives) != 0, now, recoverySeconds)',
        '_objectives.Poll(objectives, (recovery & RemoteBoardRefreshSections.Objectives) != 0 || _presence != presence, now, recoverySeconds)'),
    'actor-identity': ('        MixReference(ref revision, actor);', ''),
    'readiness-edge': ('        return recovered;', '        return false;'),
}
needle, replacement = changes[sys.argv[3]]
assert source.count(needle) == 1, 'production mutation seam changed'
pathlib.Path(sys.argv[2]).write_text(source.replace(needle, replacement))
MUTATION
if dotnet run --project "$mutation_dir/GloomhavenVR.BoardRefreshTests.csproj" --configuration Release \
    --property:GateSource="$mutation_dir/Gate.fixture" > "$mutation_dir/mutant.log" 2>&1; then
    cat "$mutation_dir/mutant.log"
    echo "FAIL: $mutation escaped the production regression test." >&2
    exit 1
fi
case "$mutation" in
    broad-invalidation) expected='Owner extras must not refit unrelated native panels' ;;
    actor-identity) expected='Pooled native row actor retarget invalidates every observer' ;;
    readiness-edge) expected='Locally recovered native output retries validated fit/show without a new packet' ;;
esac
if ! grep -Fq "Unhandled exception. System.InvalidOperationException: $expected" "$mutation_dir/mutant.log"; then
    cat "$mutation_dir/mutant.log"
    echo "FAIL: $mutation negative control did not reach the injected runtime defect." >&2
    exit 1
fi
echo "Board refresh runtime negative control: $mutation failed as expected."
done
