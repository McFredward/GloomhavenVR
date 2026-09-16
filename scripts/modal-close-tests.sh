#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$repo_root/tests/GloomhavenVR.ModalCloseTests/GloomhavenVR.ModalCloseTests.csproj"
mutation_dir="$(mktemp -d)"
trap 'rm -rf "$mutation_dir"' EXIT
python3 - "$repo_root" "$mutation_dir" <<'PY'
import pathlib, sys
root, out = map(pathlib.Path, sys.argv[1:])
source = (root / 'src/GloomhavenVR/WorldUI/Modal/ModalFallback.7.Close.cs').read_text()
start = source.index('    internal static void CloseFloatedWindow(UIWindow? window)')
end = source.index('\n    /// <summary>', start)
# Execute the actual final admission + complete close method. MandatoryDecision and
# MandatoryDecisionTerm compile directly from production, including rescue ownership.
(out / 'Close.fixture').write_text('using System;\nusing GloomhavenVR.Core;\nusing UnityEngine.UI;\nnamespace GloomhavenVR.WorldUI;\ninternal static partial class ModalFallback {\n' + source[start:end] + '\n}\n')
PY
dotnet run --project "$project" --configuration Release --property:CloseSource="$mutation_dir/Close.fixture"
cp "$repo_root/tests/GloomhavenVR.ModalCloseTests/"*.cs "$mutation_dir/"
cp "$project" "$mutation_dir/"
for mutation in missing-guard late-guard no-rescue hide-before-rescue; do
    python3 - "$mutation_dir" "$mutation" <<'PY'
import pathlib, sys
out = pathlib.Path(sys.argv[1])
s = (out / 'Close.fixture').read_text()
start = s.index('        if (IsMandatoryDecision(window, out string mandatoryReason))')
end = s.index('\n\n', start)
guard = s[start:end]
mutations = {
    'missing-guard': (guard, ''),
    'late-guard': (guard, 'FindPanel(window)!.UserClosing = true;\n' + guard),
    'no-rescue': ('RescueForMandatoryDecision(window, mandatoryReason, 0f, "a mod close request");', '{}'),
    'hide-before-rescue': ('RescueForMandatoryDecision(window, mandatoryReason, 0f, "a mod close request");', 'window.Hide(); RescueForMandatoryDecision(window, mandatoryReason, 0f, "a mod close request");'),
}
a, b = mutations[sys.argv[2]]
assert s.count(a) == 1
(out / 'Close.mutant').write_text(s.replace(a, b))
PY
    case "$mutation" in
        missing-guard|hide-before-rescue) expected='stale close plate cannot hide a newly mandatory pooled dialog' ;;
        late-guard|no-rescue) expected='pooled policy change is rechecked before conversion release' ;;
    esac
    if dotnet run --project "$mutation_dir/GloomhavenVR.ModalCloseTests.csproj" --configuration Release \
        --property:CloseSource="$mutation_dir/Close.mutant" \
        --property:DecisionSource="$repo_root/src/GloomhavenVR/WorldUI/Modal/MandatoryDecision.cs" \
        --property:TermsSource="$repo_root/src/GloomhavenVR/WorldUI/Modal/MandatoryDecisionTerm.cs" > "$mutation_dir/mutant.log" 2>&1; then
        cat "$mutation_dir/mutant.log"
        echo "FAIL: $mutation escaped modal close regression test." >&2
        exit 1
    fi
    if ! rg -qF "$expected" "$mutation_dir/mutant.log"; then
        cat "$mutation_dir/mutant.log"
        exit 1
    fi
    echo "Modal close negative control: $mutation rejected."
done
