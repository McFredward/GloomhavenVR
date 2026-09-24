#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
test_dir="$(mktemp -d)"
trap 'rm -rf "$test_dir"' EXIT
cp "$repo_root/tests/GloomhavenVR.MessageContinuationTests/"*.cs "$test_dir/"
cp "$repo_root/tests/GloomhavenVR.MessageContinuationTests/"*.csproj "$test_dir/"
source="$repo_root/src/GloomhavenVR/WorldUI/Modal/MessageWindowContinuation.cs"
project="$test_dir/GloomhavenVR.MessageContinuationTests.csproj"
dotnet run --project "$project" --configuration Release --property:ContinuationSource="$source"
for mutation in hide-only callback-only no-eligibility no-reentrancy dialog-bypass dialog-disabled dialog-mandatory; do
    python3 - "$source" "$test_dir/mutant.cs.txt" "$mutation" <<'PY'
from pathlib import Path
import sys
s=Path(sys.argv[1]).read_text()
pairs={
 'hide-only': ('close.onClick.Invoke();','window.Hide();'),
 'callback-only': ('close.onClick.Invoke();','message.Hide();'),
 'no-eligibility': ('close.IsActive() && close.IsInteractable() && ',''),
 'no-reentrancy': (' && Dispatching.Add(message)',''),
 'dialog-bypass': ('popup.Cancel();','window.Hide();'),
 'dialog-disabled': ('cancel.IsActive() && cancel.IsInteractable()', 'cancel.IsActive()'),
 'dialog-mandatory': (' || !popup.allowHide',''),
}
a,b=pairs[sys.argv[3]]; assert a in s
Path(sys.argv[2]).write_text(s.replace(a,b))
PY
    if dotnet run --project "$project" --configuration Release --property:ContinuationSource="$test_dir/mutant.cs.txt" > "$test_dir/mutant.log" 2>&1; then
        cat "$test_dir/mutant.log"
        echo "FAIL: $mutation escaped message continuation test." >&2
        exit 1
    fi
    case "$mutation" in
        hide-only|callback-only) expected='native close callback AND queue continuation both run exactly once' ;;
        no-eligibility) expected='native availability blocks dispatch' ;;
        no-reentrancy) expected='callback reentrancy cannot consume the next message' ;;
        dialog-bypass) expected='native dialog cancellation restores content' ;;
        dialog-disabled) expected='disabled native cancel cannot become a forced hide' ;;
        dialog-mandatory) expected='mandatory dialog stays with native choice' ;;
    esac
    if ! rg -qF "$expected" "$test_dir/mutant.log"; then cat "$test_dir/mutant.log"; exit 1; fi
    echo "Message continuation negative control: $mutation rejected."
done
