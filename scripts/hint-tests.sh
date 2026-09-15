#!/usr/bin/env bash
# Run production introduction provenance/transfer code and prove the regression checks fail
# against isolated broken implementations. TMP metric stubs exercise layout decisions, not pixels.
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$repo_root/tests/GloomhavenVR.HintTests/GloomhavenVR.HintTests.csproj"
origins_source="$repo_root/src/GloomhavenVR/WorldUI/Composites/HintMessageOrigins.cs"
transfer_source="$repo_root/src/GloomhavenVR/WorldUI/Modal/ModalFallback.CompositeTransfer.cs"
reflow_source="$repo_root/src/GloomhavenVR/WorldUI/Composites/HintTextReflow.cs"
dotnet run --project "$project" --configuration Release
mutation_dir="$(mktemp -d)"
trap 'rm -rf "$mutation_dir"' EXIT
cp "$repo_root/tests/GloomhavenVR.HintTests/"*.cs "$mutation_dir/"
cp "$project" "$mutation_dir/"
for mutation in lost-queued-origin missing-standalone-release pending-dissolve no-word-wrap no-layout-restore; do
    python3 - "$origins_source" "$transfer_source" "$mutation_dir" "$mutation" "$reflow_source" <<'PY'
import pathlib
import sys

origins = pathlib.Path(sys.argv[1]).read_text()
transfer = pathlib.Path(sys.argv[2]).read_text()
output = pathlib.Path(sys.argv[3])
mutation = sys.argv[4]
reflow = pathlib.Path(sys.argv[5]).read_text()
if mutation == 'lost-queued-origin':
    needle = 'message != null && _messages.TryGetValue(message, out Origin? origin) ? origin : null;'
    assert origins.count(needle) == 1, 'production queued-origin seam changed'
    origins = origins.replace(needle, 'CurrentScope;')
elif mutation == 'missing-standalone-release':
    needle = 'CanvasConversion.Release(wp.Panel);'
    assert transfer.count(needle) == 1, 'production transfer seam changed'
    transfer = transfer.replace(needle, '; // regression: standalone conversion survives')
elif mutation == 'pending-dissolve':
    needle = 'for (int i = CanvasConversion.ActivePanels.Count - 1; i >= 0; i--)'
    assert transfer.count(needle) == 1
    transfer = transfer.replace(needle, 'for (int i = -1; i >= 0; i--)')
elif mutation == 'no-word-wrap':
    needle = 'text.enableWordWrapping = true;'
    assert reflow.count(needle) == 1
    reflow = reflow.replace(needle, 'text.enableWordWrapping = false;')
else:
    needle = 'if (_box != null) _box.sizeDelta = _boxSize;'
    assert reflow.count(needle) == 1
    reflow = reflow.replace(needle, '; // regression: leave narrow layout on native home')
(output / 'Reflow.fixture').write_text(reflow)
(output / 'Origins.fixture').write_text(origins)
(output / 'Transfer.fixture').write_text(transfer)
PY
    if dotnet run --project "$mutation_dir/GloomhavenVR.HintTests.csproj" --configuration Release \
        --property:HintOriginsSource="$mutation_dir/Origins.fixture" \
        --property:HintTransferSource="$mutation_dir/Transfer.fixture" \
        --property:HintReflowSource="$mutation_dir/Reflow.fixture" > "$mutation_dir/mutant.log" 2>&1; then
        cat "$mutation_dir/mutant.log"
        echo "FAIL: runtime negative control survived: $mutation" >&2
        exit 1
    fi
    case "$mutation" in
        lost-queued-origin) expected='Enqueuing another producer must not retarget an earlier message' ;;
        missing-standalone-release) expected='Retire capture owner and grab before composite records its home' ;;
        pending-dissolve) expected='Pending dissolve must release before the composite records native home' ;;
        no-word-wrap) expected='Narrow hints must wrap at authored font size instead of shrinking glyphs' ;;
        no-layout-restore) expected='Unpark must restore both native rects exactly' ;;
    esac
    if ! rg -Fq "Unhandled exception. System.Exception: $expected" "$mutation_dir/mutant.log"; then
        cat "$mutation_dir/mutant.log"
        echo "FAIL: negative control did not reach its runtime assertion: $mutation" >&2
        exit 1
    fi
    echo "Hint runtime negative control failed as expected: $mutation"
done
