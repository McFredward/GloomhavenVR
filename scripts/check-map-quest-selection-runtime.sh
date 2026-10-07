#!/usr/bin/env bash
# Real production observer + exact native proposal/preview/cancel/selection bodies.
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$repo_root/tests/map-quest-selection-runtime/Selection.csproj"
helper="$repo_root/src/GloomhavenVR/WorldUI/MapRoom/NativeMapQuestSelection.cs"
interactor="$repo_root/src/GloomhavenVR/WorldUI/MapRoom/MapLocationInteractor.cs"
temporary="$(mktemp -d)"
trap 'rm -rf "$temporary"' EXIT
python3 - "$repo_root" <<'PY'
from pathlib import Path
import subprocess, sys
sys.path.insert(0, str(Path(sys.argv[1])/'tests/map-quest-selection-runtime'))
root = Path(sys.argv[1])
common = Path(subprocess.check_output(['git', '-C', str(root), 'rev-parse', '--path-format=absolute', '--git-common-dir'], text=True).strip())
native = common.parent/'decompiled/GH.Runtime'
fixture = (root/'tests/map-quest-selection-runtime/NativeFixture.cs').read_text()
entries = {
 'UIQuestPopupManager.cs': ('public bool IsQuestShown => selectedQuest != null;', 'public void ShowQuest(CQuestState', 'public void ShowQuest(IQuest', 'public void Hide(IQuest', 'public void HideAll(', 'public void HideMultiplayerPreview(', 'public void ShowMultiplayerPreview('),
 'UIMapMultiplayerController.cs': ('public CQuestState HostSelectedQuest => hostSelectedLocation?.LocationQuest;', 'public void ConfirmSelectedLocation()', 'public void ProxyHostSelectedLocation(', 'private void ProxyHostCancelledSelectedLocation()', 'public void ClearHostSelectedQuest(', 'private void CancelPreviewedQuest(', 'private void PreviewQuest(', 'public bool IsShowingHostQuestToClient()'),
 'MapLocation.cs': ('public void Select(bool', 'public void Deselect(bool'),
}
verified = 0
for file, signatures in entries.items():
    original = native/file
    if not original.exists():
        continue # hosted CI uses the committed verbatim fixture
    source = original.read_text()
    for signature in signatures:
        start = source.index('\t' + signature)
        if signature.endswith(';'):
            actual = '\t' + signature
        else:
            end = source.index('\n\t}', start) + len('\n\t}')
            actual = source[start:end]
        assert actual in fixture, 'Native method fixture differs: ' + signature
        verified += 1
if (native/'Assets.Script.GUI.Quest/Quest.cs').exists():
    quest = (native/'Assets.Script.GUI.Quest/Quest.cs').read_text()
    assert 'private CQuestState questState;' in quest, 'Native quest wrapper field changed'
    verified += 1
print(f'Map quest selection native source bindings: {verified} exact methods/properties/identity fields.')
PY
dotnet run --project "$project" --configuration Release
# Both the original game and publicized build/reference metadata must resolve the same fields.
python3 - "$repo_root/tests/map-quest-selection-runtime/Boundary.cs" "$temporary/Publicized.cs" <<'PY'
from pathlib import Path
import sys
source = Path(sys.argv[1]).read_text()
for needle in ('private CQuestState questState;', 'private UIQuestPopup selectedQuestPopup',
               'private IQuest selectedQuest;', 'private CQuestState clientSelectedQuest;',
               'private MapLocation hostSelectedLocation;'):
    assert source.count(needle) == 1, 'Publicized metadata seam changed: ' + needle
    source = source.replace(needle, needle.replace('private ', 'public ', 1))
Path(sys.argv[2]).write_text(source)
PY
dotnet run --project "$project" --configuration Release --property:BoundarySource="$temporary/Publicized.cs"
for mutation in native-proposal-blind observer-staging-only wrong-popup-instance late-browse-overwrite premature-auto-deselect first-sight-null wrong-token-identity enum-id-substitute shared-popup-subject; do
    python3 - "$helper" "$interactor" "$temporary" "$mutation" <<'PY'
from pathlib import Path
import sys
helper = Path(sys.argv[1]).read_text()
interactor = Path(sys.argv[2]).read_text()
mutation = sys.argv[4]
changes = {
 'native-proposal-blind': ('helper', 'if (proposal != null)', 'if (bool.Parse("false") && proposal != null)', 0),
 'observer-staging-only': ('interactor', 'bool measured = NativeMapQuestSelection.TryGetDecision(out string? now);', 'bool measured = true; string? now = _selectedDecisionId;', 0),
 'wrong-popup-instance': ('interactor', 'UIWindow? window = NativeMapQuestSelection.ConfirmationWindow;', 'UIWindow? window = UnityEngine.Object.FindObjectOfType<UIQuestPopup>()?.GetComponent<UIWindow>();', 0),
 'late-browse-overwrite': ('interactor', 'if (NativeMapQuestSelection.TryGetHostProposal(out var proposal, out _) && proposal != null)', 'if (bool.Parse("false") && NativeMapQuestSelection.TryGetHostProposal(out var proposal, out _) && proposal != null)', 0),
 'premature-auto-deselect': ('interactor', 'if (NativeMapQuestSelection.TryGetHostProposal(out var proposal, out _) && proposal != null)', 'if (bool.Parse("false") && NativeMapQuestSelection.TryGetHostProposal(out var proposal, out _) && proposal != null)', 1),
 'first-sight-null': ('interactor', 'return; // late initialization is unknown, never first-sight absence', 'System.GC.KeepAlive(now); // mutated: publish unknown as null', 0),
 'wrong-token-identity': ('helper', 'locationId = LocationId(location);', 'locationId = quest.ID;', 0),
 'enum-id-substitute': ('helper', 'ReferenceEquals(window, ConfirmationWindow)', 'window.ID == ConfirmationWindow?.ID', 0),
 'shared-popup-subject': ('helper', 'hasSubject = ClientQuest.GetValue(manager) != null;', 'hasSubject = SelectedQuest.GetValue(manager) != null;', 0),
}
name, needle, replacement, occurrence = changes[mutation]
source = helper if name == 'helper' else interactor
positions = [i for i in range(len(source)) if source.startswith(needle, i)]
assert len(positions) == (2 if mutation in ('native-proposal-blind', 'late-browse-overwrite', 'premature-auto-deselect', 'wrong-token-identity') else 1), 'Production mutation seam changed: ' + mutation
at = positions[occurrence]
source = source[:at] + replacement + source[at+len(needle):]
if name == 'helper': helper = source
else: interactor = source
Path(sys.argv[3], 'Helper.cs').write_text(helper)
Path(sys.argv[3], 'Interactor.cs').write_text(interactor)
PY
    if dotnet run --project "$project" --configuration Release \
        --property:SelectionSource="$temporary/Helper.cs" --property:InteractorSource="$temporary/Interactor.cs" \
        > "$temporary/mutant.log" 2>&1; then
        echo "FAIL: map quest selection mutation survived: $mutation" >&2; exit 1
    fi
    case "$mutation" in
        native-proposal-blind) expected='A sampled/native decision must be available' ;;
        observer-staging-only) expected='Observer must sample native proposal rather than local staging' ;;
        wrong-popup-instance) expected='Inactive first type-wide popup must not hide current selected popup' ;;
        late-browse-overwrite) expected='Late VR browsing clear must not cancel native host proposal' ;;
        premature-auto-deselect) expected='Temporarily missing presentation must not auto-deselect live host proposal' ;;
        first-sight-null) expected='Uninitialized native manager must not publish first-sight cancellation' ;;
        wrong-token-identity) expected='Host proposal uses native location token identity' ;;
        enum-id-substitute) expected='Equal enum IDs must never substitute another popup instance' ;;
        shared-popup-subject) expected='Hover popup owns its own native subject' ;;
    esac
    if ! rg -qF "Unhandled exception. System.InvalidOperationException: $expected" "$temporary/mutant.log"; then
        cat "$temporary/mutant.log"; echo "FAIL: negative control did not reach injected defect: $mutation" >&2; exit 1
    fi
    echo "Map quest selection negative control rejected: $mutation"
done
