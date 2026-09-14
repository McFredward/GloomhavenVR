#!/usr/bin/env bash
# Execute production loadout ownership and curtain/travel policies, including their callers.
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if ! command -v dotnet >/dev/null 2>&1 && [[ -x "$HOME/.dotnet/dotnet" ]]; then
    export DOTNET_ROOT="$HOME/.dotnet"
fi
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$repo_root/tests/GloomhavenVR.MapFlowTests/GloomhavenVR.MapFlowTests.csproj"
# Isolated lanes may read the integrator's production sources without copying or editing them.
loadout_source="${1:-$repo_root/src/GloomhavenVR/WorldUI/Composites/LoadoutWindowOwnership.cs}"
story_source="${2:-$repo_root/src/GloomhavenVR/WorldUI/Composites/StoryComposite.cs}"
gate_source="${3:-$repo_root/src/GloomhavenVR/WorldUI/MapRoom/MapInputGate.cs}"
interactor_source="${4:-$repo_root/src/GloomhavenVR/WorldUI/MapRoom/MapLocationInteractor.cs}"
travel_source="${5:-$repo_root/src/GloomhavenVR/WorldUI/MapRoom/MapTravelConfirm.cs}"
modal_source="$repo_root/src/GloomhavenVR/WorldUI/Modal/ModalFallback.10.CatchAll.cs"
dotnet run --project "$project" --configuration Release \
    --property:LoadoutSource="$loadout_source" --property:StorySource="$story_source" \
    --property:MapGateSource="$gate_source" --property:InteractorSource="$interactor_source" \
    --property:TravelSource="$travel_source" --property:ModalSource="$modal_source"
mutation_dir="$(mktemp -d)"
trap 'rm -rf "$mutation_dir"' EXIT
cp "$repo_root/tests/GloomhavenVR.MapFlowTests/"*.cs "$mutation_dir/"
cp "$repo_root/tests/GloomhavenVR.MapFlowTests/extract-map-policies.py" "$mutation_dir/"
cp "$project" "$mutation_dir/"
for mutation in missing-curtain-integration unrelated-curtain-escape missing-party-container native-lock-bypass missing-dispatch-admission offline-travel-bypass; do
python3 - "$loadout_source" "$story_source" "$gate_source" "$interactor_source" "$travel_source" "$mutation_dir" "$mutation" <<'MUTATION'
import pathlib, sys
sources = [pathlib.Path(p).read_text() for p in sys.argv[1:6]]
changes = {
    'missing-curtain-integration': (1, 'if (LoadoutWindowOwnership.IsCurrentContent(window))', 'if (bool.Parse("false"))'),
    'unrelated-curtain-escape': (0, 'return ReferenceEquals(candidate, container);', 'return true;'),
    'missing-party-container': (0, 'if (ReferenceEquals(candidate, owner) || candidate.transform.IsChildOf(owner.transform))', 'return ReferenceEquals(candidate, owner) || candidate.transform.IsChildOf(owner.transform);\n#pragma warning disable CS0162\n        if (ReferenceEquals(candidate, owner) || candidate.transform.IsChildOf(owner.transform))'),
    'native-lock-bypass': (2, 'manager != null && manager.IsLocked;', 'manager != null && false;'),
    'missing-dispatch-admission': (3, 'if (MapInputGate.IsBlocked)\n            return;\n        if (loc == null)', '// if (MapInputGate.IsBlocked) return;\n        if (loc == null)'),
    'offline-travel-bypass': (4, 'mgr != null && !MapInputGate.IsBlockedBy(mgr)', 'mgr != null'),
}
index, needle, replacement = changes[sys.argv[7]]
assert sources[index].count(needle) == 1, 'Production map-flow mutation seam changed'
sources[index] = sources[index].replace(needle, replacement)
for name, source in zip(('Loadout', 'Story', 'Gate', 'Interactor', 'Travel'), sources):
    pathlib.Path(sys.argv[6], name + '.fixture').write_text(source)
MUTATION
if [[ "$mutation" == missing-dispatch-admission ]]; then
    if python3 "$mutation_dir/extract-map-policies.py" "$mutation_dir/Story.fixture" \
        "$mutation_dir/Interactor.fixture" "$mutation_dir/Travel.fixture" \
        "$mutation_dir/MapPolicies.fixture" "$modal_source" > "$mutation_dir/mutant.log" 2>&1; then
        echo "FAIL: $mutation escaped the map input admission check." >&2
        exit 1
    fi
elif dotnet run --project "$mutation_dir/GloomhavenVR.MapFlowTests.csproj" --configuration Release \
    --property:LoadoutSource="$mutation_dir/Loadout.fixture" --property:StorySource="$mutation_dir/Story.fixture" \
    --property:MapGateSource="$mutation_dir/Gate.fixture" --property:InteractorSource="$mutation_dir/Interactor.fixture" \
    --property:TravelSource="$mutation_dir/Travel.fixture" --property:ModalSource="$modal_source" > "$mutation_dir/mutant.log" 2>&1; then
    cat "$mutation_dir/mutant.log"
    echo "FAIL: $mutation escaped the map-flow regression test." >&2
    exit 1
fi
case "$mutation" in
    missing-curtain-integration) expected='Unhandled exception. System.InvalidOperationException: Reopened native loadout owner must escape the story curtain' ;;
    unrelated-curtain-escape) expected='Unhandled exception. System.InvalidOperationException: Unrelated same-ID party container must remain curtained' ;;
    missing-party-container) expected='Unhandled exception. System.InvalidOperationException: Native outer PartyPanel must escape the curtain when its different inner owner reopens' ;;
    native-lock-bypass) expected='Unhandled exception. System.InvalidOperationException: Native map lock must block direct VR input' ;;
    missing-dispatch-admission) expected='AssertionError: Native map lock admission missing or late: internal void Dispatch(' ;;
    offline-travel-bypass) expected='Unhandled exception. System.InvalidOperationException: Offline travel must disappear under the native map lock' ;;
esac
if ! grep -Fq "$expected" "$mutation_dir/mutant.log"; then
    cat "$mutation_dir/mutant.log"
    echo "FAIL: $mutation negative control did not reach the injected defect." >&2
    exit 1
fi
echo "Map flow negative control: $mutation failed as expected."
done
