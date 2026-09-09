#!/usr/bin/env bash
# Execute production binding discovery against a minimal tree API, then prove the old defect fails.
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if ! command -v dotnet >/dev/null 2>&1 && [[ -x "$HOME/.dotnet/dotnet" ]]; then
    export DOTNET_ROOT="$HOME/.dotnet"
fi
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$repo_root/tests/GloomhavenVR.CardBindingsTests/GloomhavenVR.CardBindingsTests.csproj"
dotnet run --project "$project" --configuration Release
mutation_dir="$(mktemp -d)"
trap 'rm -rf "$mutation_dir"' EXIT
cp "$repo_root/tests/GloomhavenVR.CardBindingsTests/"*.cs "$mutation_dir/"
cp "$project" "$mutation_dir/"
python3 - "$repo_root/src/GloomhavenVR/Net/CardAppearanceBindings.cs" "$mutation_dir/Bindings.cs" <<'PY'
import pathlib, sys
source = pathlib.Path(sys.argv[1]).read_text()
needle = '        RefreshGroups();\n    }\n    internal void RefreshGroups()'
assert source.count(needle) == 1, 'production constructor mutation seam changed'
source = source.replace(needle, '        RefreshGroups();\n        if (Groups.Count > 8) throw new InvalidOperationException("Legacy eight-group cap");\n    }\n    internal void RefreshGroups()')
pathlib.Path(sys.argv[2]).write_text(source)
PY
# Avoid compiling Bindings.cs twice: the explicit source link points outside the project tree.
mv "$mutation_dir/Bindings.cs" "$mutation_dir/Bindings.fixture"
if dotnet run --project "$mutation_dir/GloomhavenVR.CardBindingsTests.csproj" --configuration Release \
    --property:BindingSource="$mutation_dir/Bindings.fixture" \
    --property:StateSource="$repo_root/src/GloomhavenVR/Net/CardAppearanceState.cs" \
    --property:CaptureSource="$repo_root/src/GloomhavenVR/Net/CardAppearanceCapture.cs" > "$mutation_dir/mutant.log" 2>&1; then
    cat "$mutation_dir/mutant.log"
    echo 'FAIL: old eight-group constructor limit escaped the runtime regression test.' >&2
    exit 1
fi
if ! grep -Fq 'Unhandled exception. System.InvalidOperationException: Legacy eight-group cap' "$mutation_dir/mutant.log"; then
    cat "$mutation_dir/mutant.log"
    echo 'FAIL: negative control did not reach the injected runtime defect.' >&2
    exit 1
fi
echo 'Card binding runtime negative control: old eight-group cap failed as expected.'
# These independent mutations must execute the production capture path and fail for the
# intended reason, not merely fail to compile. They pin immutability, shader replacement
# invalidation and the allocation measurement itself.
for defect in retained_snapshot shader_invalidation warmed_allocation; do
    python3 - "$repo_root/src/GloomhavenVR/Net/CardAppearanceBindings.cs" "$mutation_dir/Bindings.fixture" "$defect" <<'PY'
import pathlib, sys
source = pathlib.Path(sys.argv[1]).read_text()
defect = sys.argv[3]
if defect == 'retained_snapshot':
    needle = '                published[i] = node;'
    replacement = '                published[i] = source;'
elif defect == 'shader_invalidation':
    needle = ' || !ReferenceEquals(_maskShaders[role], shader)'
    replacement = ''
else:
    needle = '        int count = 0;\n        for (byte role'
    replacement = '        GC.KeepAlive(new byte[64]);\n        int count = 0;\n        for (byte role'
assert source.count(needle) == 1, defect + ' mutation seam changed'
pathlib.Path(sys.argv[2]).write_text(source.replace(needle, replacement))
PY
    if dotnet run --project "$mutation_dir/GloomhavenVR.CardBindingsTests.csproj" --configuration Release \
        --property:BindingSource="$mutation_dir/Bindings.fixture" \
        --property:StateSource="$repo_root/src/GloomhavenVR/Net/CardAppearanceState.cs" \
        --property:CaptureSource="$repo_root/src/GloomhavenVR/Net/CardAppearanceCapture.cs" > "$mutation_dir/mutant.log" 2>&1; then
        cat "$mutation_dir/mutant.log"
        echo "FAIL: $defect escaped the capture regression test." >&2
        exit 1
    fi
    case "$defect" in
        retained_snapshot) expected='Changed graphic publishes a new node' ;;
        shader_invalidation) expected='same material changed shader clears absent property lanes role/flags/binding/mask' ;;
        warmed_allocation) expected='Warmed unchanged capture/publication allocates no managed objects' ;;
    esac
    if ! grep -Fq "Unhandled exception. System.InvalidOperationException: $expected" "$mutation_dir/mutant.log"; then
        cat "$mutation_dir/mutant.log"
        echo "FAIL: $defect negative control did not reach the intended runtime defect." >&2
        exit 1
    fi
    echo "Card capture runtime negative control: $defect failed as expected."
done
