#!/usr/bin/env bash
# Exercise actual native-lock ownership and the extracted production FlatScreen policy.
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if ! command -v dotnet >/dev/null 2>&1 && [[ -x "$HOME/.dotnet/dotnet" ]]; then
    export DOTNET_ROOT="$HOME/.dotnet"
fi
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$repo_root/tests/GloomhavenVR.CardLossModalTests/GloomhavenVR.CardLossModalTests.csproj"
# Optional source overrides allow an isolated test lane to validate the integrator's files.
guard_source="${1:-$repo_root/src/GloomhavenVR/WorldUI/CardLossModalGuard.cs}"
flat_source="${2:-$repo_root/src/GloomhavenVR/WorldUI/FlatScreen/FlatScreen.4.Lifecycle.cs}"
python3 - "$repo_root/src/GloomhavenVR/WorldUI/FlatScreen/FlatScreen.1.Core.cs" <<'BINDING'
import pathlib, sys
source = pathlib.Path(sys.argv[1]).read_text()
assert source.count('\n        UpdateScreenTakeover();') == 1, 'FlatScreen tick must apply desktop takeover'
BINDING
dotnet run --project "$project" --configuration Release \
    --property:CardLossGuardSource="$guard_source" --property:FlatScreenLifecycleSource="$flat_source"
mutation_dir="$(mktemp -d)"
trap 'rm -rf "$mutation_dir"' EXIT
cp "$repo_root/tests/GloomhavenVR.CardLossModalTests/"*.cs "$mutation_dir/"
cp "$repo_root/tests/GloomhavenVR.CardLossModalTests/extract-flat-screen.py" "$mutation_dir/"
cp "$project" "$mutation_dir/"
for mutation in mixed-lock empty-lock idle-hand missing-integration explicit-modal-priority map-visibility map-takeover; do
python3 - "$guard_source" "$flat_source" "$mutation_dir" "$mutation" <<'MUTATION'
import pathlib, sys
source = pathlib.Path(sys.argv[1]).read_text()
flat = pathlib.Path(sys.argv[2]).read_text()
changes = {
    'mixed-lock': ('guard',
        'if (hand == null || !hand.AnimatingLostCards)\n                return false;',
        'if (hand == null || !hand.AnimatingLostCards)\n                continue;'),
    'empty-lock': ('guard', 'owners.Count == 0', 'owners.Count < 0'),
    'idle-hand': ('guard', 'hand == null || !hand.AnimatingLostCards', 'hand == null'),
    'missing-integration': ('flat',
        'CardLossModalGuard.OwnsAllLocks(UIManager.Instance, Cards.Patches.HandSuppression.Active)',
        'bool.Parse("false")'),
    'explicit-modal-priority': ('flat', 'if (ModalFallback.ScreenWanted)',
        'if (ModalFallback.ScreenWanted && !CardLossModalGuard.OwnsAllLocks(UIManager.Instance, Cards.Patches.HandSuppression.Active))'),
    'map-visibility': ('flat', 'if (_rescueShow || MapFallbackActive)', 'if (_rescueShow)'),
    'map-takeover': ('flat', 'ManualScreenActive = _manualShow || _rescueShow || MapFallbackActive;',
        'ManualScreenActive = _manualShow || _rescueShow;'),
}
which, needle, replacement = changes[sys.argv[4]]
text = source if which == 'guard' else flat
assert text.count(needle) == 1, 'production mutation seam changed'
text = text.replace(needle, replacement)
if which == 'guard': source = text
else: flat = text
pathlib.Path(sys.argv[3], 'Guard.fixture').write_text(source)
pathlib.Path(sys.argv[3], 'FlatScreen.fixture').write_text(flat)
MUTATION
if dotnet run --project "$mutation_dir/GloomhavenVR.CardLossModalTests.csproj" --configuration Release \
    --property:CardLossGuardSource="$mutation_dir/Guard.fixture" \
    --property:FlatScreenLifecycleSource="$mutation_dir/FlatScreen.fixture" > "$mutation_dir/mutant.log" 2>&1; then
    cat "$mutation_dir/mutant.log"
    echo "FAIL: $mutation escaped the card-loss modal regression test." >&2
    exit 1
fi
case "$mutation" in
    mixed-lock) expected='Mixed unrelated lock must retain the real modal fallback' ;;
    empty-lock) expected='Cancelled unlocked hand must not suppress fallback despite stale animation flag' ;;
    idle-hand) expected='A non-animating hand lock is not owned by card loss' ;;
    missing-integration) expected='Production FlatScreen suppresses the empty native card-loss fallback' ;;
    explicit-modal-priority) expected='Explicit modal composite stays above card-loss suppression' ;;
    map-visibility) expected='Failed blocking map window reaches the native desktop fallback' ;;
    map-takeover) expected='Map fallback hands converted widgets back to the desktop' ;;
esac
if ! grep -Fq "Unhandled exception. System.InvalidOperationException: $expected" "$mutation_dir/mutant.log"; then
    cat "$mutation_dir/mutant.log"
    echo "FAIL: $mutation negative control did not reach the injected runtime defect." >&2
    exit 1
fi
echo "Card-loss modal runtime negative control: $mutation failed as expected."
done
