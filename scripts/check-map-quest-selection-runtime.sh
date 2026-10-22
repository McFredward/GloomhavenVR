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
               'private MapLocation hostSelectedLocation;', 'private bool m_Initialised = true;'):
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
for mutation in drop-locked-edge omit-unready stale-subject stale-generation stale-surface wrong-clear-subject backwards-stamp native-refusal lost-adopted-subject lost-supersession incidental-table-close incidental-x-close native-click-refusal same-quest-opening native-retirement-loss later-local-close; do
    python3 - "$repo_root" "$temporary" "$mutation" <<'PY'
from pathlib import Path
import sys
root, out = map(Path, sys.argv[1:3]); mutation = sys.argv[3]
remote = (root/'src/GloomhavenVR/Net/Remote/RemoteMapRoom.cs').read_text()
interactor = (root/'src/GloomhavenVR/WorldUI/MapRoom/MapLocationInteractor.cs').read_text()
changes = {
 'drop-locked-edge': ('remote', 'if (MapInputGate.IsBlocked)\n        {\n            _pendingSelectInteractor', 'if (bool.Parse("false") && MapInputGate.IsBlocked)\n        {\n            _pendingSelectInteractor'),
 'omit-unready': ('interactor', 'MapQuestReadyUp.OnQuestDecisionChanged(from, to);', 'System.GC.KeepAlive(from);'),
 'stale-subject': ('remote', '|| !sameSubject', '|| false'),
 'stale-generation': ('remote', '|| interactor.ScanGeneration != _pendingSelectGeneration', '|| false'),
 'stale-surface': ('remote', '|| MapIconLayer.CurrentSurface != _pendingSelectSurface', '|| false'),
 'wrong-clear-subject': ('remote', 'if (wantKey == 0u && priorKey != currentKey)', 'if (bool.Parse("false") && wantKey == 0u && priorKey != currentKey)'),
 'backwards-stamp': ('remote', 'if (forward >= 128)', 'if (bool.Parse("false") && forward >= 128)'),
 'native-refusal': ('interactor', 'if (sel.IsSelected)\n                return;', 'if (bool.Parse("false") && sel.IsSelected)\n                return;'),
 'lost-adopted-subject': ('remote', 'SeenSelectKey[kv.Key] = s.SelectKey;\n                continue;\n            }\n            if (forward >= 128)', 'System.GC.KeepAlive(s.SelectKey);\n                continue;\n            }\n            if (forward >= 128)'),
 'lost-supersession': ('remote', 'ClearPendingSelection();\n        uint currentKey = KeyOf(interactor.Staged);', 'System.GC.KeepAlive(interactor);\n        uint currentKey = KeyOf(interactor.Staged);'),
 'incidental-table-close': ('interactor', 'if (NativeMapQuestSelection.BlocksSharedBrowsing)\n            return;', 'if (bool.Parse("false") && NativeMapQuestSelection.BlocksSharedBrowsing)\n            return;'),
 'incidental-x-close': ('interactor', 'if (interactor == null || MapInputGate.IsBlocked || NativeMapQuestSelection.BlocksSharedBrowsing\n            || StoryComposite.PointOfNoReturn)', 'if (interactor == null || MapInputGate.IsBlocked\n            || StoryComposite.PointOfNoReturn)'),
 'native-click-refusal': ('interactor', 'return ReferenceEquals(_selected, loc);', 'return true;'),
 'same-quest-opening': ('remote', '|| interactor.SelectionRevision != _pendingSelectRevision', '|| false'),
 'native-retirement-loss': ('remote', '|| (currentKey == 0u && _pendingSelectPriorLocation != null', '|| (bool.Parse("false") && currentKey == 0u && _pendingSelectPriorLocation != null'),
 'later-local-close': ('interactor', 'unchecked(_dispatchedHere + _adoptedFromGame + _manualSelectionRevision)', 'unchecked(_dispatchedHere + _adoptedFromGame + 0 * _manualSelectionRevision)'),
}
name, needle, replacement = changes[mutation]
source = remote if name == 'remote' else interactor
assert source.count(needle) == 1, 'Delivery mutation seam changed: ' + mutation
source = source.replace(needle, replacement)
if name == 'remote': remote = source
else: interactor = source
(out/'RemoteRoom.cs').write_text(remote); (out/'Interactor.cs').write_text(interactor)
PY
    if dotnet run --project "$project" --configuration Release \
        --property:InteractorSource="$temporary/Interactor.cs" --property:RemoteRoomSource="$temporary/RemoteRoom.cs" \
        > "$temporary/mutant.log" 2>&1; then
        echo "FAIL: shared quest delivery mutation survived: $mutation" >&2; exit 1
    fi
    case "$mutation" in
        drop-locked-edge) expected='Locked shared clear must retain its original visible native subject until authorized' ;;
        omit-unready) expected='Locked shared clear must request own native quest-unready before adoption' ;;
        stale-subject) expected='Pending clear must retire on subject' ;;
        stale-generation) expected='Pending clear must retire on generation' ;;
        stale-surface) expected='Pending clear must retire on surface' ;;
        wrong-clear-subject) expected='Delayed clear of old subject must not dismiss a new quest or withdraw its readiness' ;;
        backwards-stamp) expected='Backwards selection packet must not reopen a closed subject' ;;
        native-refusal) expected='Native Deselect refusal must preserve staging and prevent replacement dispatch' ;;
        lost-adopted-subject) expected='A peer may clear an adopted subject without first publishing its own selection stamp' ;;
        lost-supersession) expected='A newer unavailable selection must retire its obsolete pending clear' ;;
        incidental-table-close) expected='Incidental table click must leave the shared native proposal visible' ;;
        incidental-x-close) expected='Incidental X must leave the shared native proposal visible' ;;
        native-click-refusal) expected='Native click refusal must not report a requested selection as adopted' ;;
        same-quest-opening) expected='Pending clear must retire after a new opening of the same quest' ;;
        native-retirement-loss) expected='Native unready retirement must preserve a pending replacement selection' ;;
        later-local-close) expected='Later explicit local close must supersede a pending replacement selection' ;;
    esac
    if ! rg -qF "Unhandled exception. System.InvalidOperationException: $expected" "$temporary/mutant.log"; then
        cat "$temporary/mutant.log"; echo "FAIL: delivery control did not reach injected defect: $mutation" >&2; exit 1
    fi
    echo "Shared quest delivery negative control rejected: $mutation"
done

# Exercise defects in the real causal register and its actual sampler/receiver integration.
for mutation in event-order-instead-of-opening resurrect-closed-opening relay-carrier-as-author missing-retirement-fence stripped-selected-relay truncated-native-author forged-native-clear lost-first-dispatch restored-selection-is-human refused-manual-is-human; do
    python3 - "$repo_root" "$temporary" "$mutation" <<'PYVERSION'
from pathlib import Path
import sys
root, out = map(Path, sys.argv[1:3]); mutation = sys.argv[3]
version = (root/'src/GloomhavenVR/Net/SharedQuestSelectionVersion.cs').read_text()
remote = (root/'src/GloomhavenVR/Net/Remote/RemoteMapRoom.cs').read_text()
changes = {
 'event-order-instead-of-opening': ('version', 'internal ulong Opening => Key != 0u ? Token : TokenOf(ParentCounter, ParentAuthor);', 'internal ulong Opening => Token;'),
 'resurrect-closed-opening': ('version', 'if (_current.Key == 0u && candidate.Key != 0u)', 'if (bool.Parse("false") && _current.Key == 0u && candidate.Key != 0u)'),
 'relay-carrier-as-author': ('version', 'relay ? new Event(counter, parentAuthor, 0u, 0, key)', 'relay ? new Event(counter, sender, 0u, 0, key)'),
 'missing-retirement-fence': ('version', 'if (_haveRetirement && candidate.Opening <= _retiredThrough)', 'if (bool.Parse("false") && _haveRetirement && candidate.Opening <= _retiredThrough)'),
 'stripped-selected-relay': ('remote', 'if (current.Key != 0u && current.Author != localPlayerId)', 'if (bool.Parse("false") && current.Key != 0u && current.Author != localPlayerId)'),
 'truncated-native-author': ('remote', 'SelectionVersions.TryPublish(localPlayerId, selectKey, _displayedSelectOpening, out var own)', 'SelectionVersions.TryPublish(unchecked((byte)localPlayerId), selectKey, _displayedSelectOpening, out var own)'),
 'lost-first-dispatch': ('remote', '|| localInputRevision != priorInputRevision);', '|| false);'),
 'refused-manual-is-human': ('remote', 'int localPublicationRevision = locations?.SelectionPublicationRevision ?? 0;', 'int localPublicationRevision = locations?.SelectionRevision ?? 0;'),
 'restored-selection-is-human': ('remote', 'int localInputRevision = locations?.LocalSelectionInputRevision ?? 0;', 'int localInputRevision = locations?.SelectionRevision ?? 0;'),
 'forged-native-clear': ('remote', 'bool localIntent = !adoptedSelect && ((_versionLocalObserved && localPublicationRevision != _versionLocalPublicationRevision)\n            || localInputRevision != priorInputRevision);', 'bool localIntent = !adoptedSelect && ((_versionLocalObserved && (localPublicationRevision != _versionLocalPublicationRevision || selectKey != _publishedSelectKey))\n            || localInputRevision != priorInputRevision);'),
}
name, needle, replacement = changes[mutation]
source = version if name == 'version' else remote
assert source.count(needle) == 1, 'Causal version mutation seam changed: ' + mutation
source = source.replace(needle, replacement)
if name == 'version': version = source
else: remote = source
(out/'Version.cs').write_text(version); (out/'RemoteRoom.cs').write_text(remote)
PYVERSION
    if dotnet run --project "$project" --configuration Release \
        --property:VersionSource="$temporary/Version.cs" --property:RemoteRoomSource="$temporary/RemoteRoom.cs" \
        > "$temporary/mutant.log" 2>&1; then
        echo "FAIL: causal quest version mutation survived: $mutation" >&2; exit 1
    fi
    case "$mutation" in
        event-order-instead-of-opening|resurrect-closed-opening) expected='Opening register must converge across reordered and duplicate causal events' ;;
        relay-carrier-as-author) expected='Selected relay must retain original full author and counter without minting a human event' ;;
        missing-retirement-fence) expected='A retired native opening must not replay after map stand-down' ;;
        stripped-selected-relay) expected='Remote dispatch before first presence sample must relay its original opening without a human mint' ;;
        truncated-native-author) expected='A genuine first dispatch before the first presence sample must publish its human opening' ;;
        lost-first-dispatch) expected='A genuine first dispatch before the first presence sample must publish its human opening' ;;
        restored-selection-is-human) expected='Native initial selection adoption must remain a baseline rather than a forged first human click' ;;
        refused-manual-is-human) expected='Refused manual X or table close must consume intent without minting a same-key reopening' ;;
        forged-native-clear) expected='Native unready cleanup must not mint a human clear over the pending replacement' ;;
    esac
    if ! rg -qF "Unhandled exception. System.InvalidOperationException: $expected" "$temporary/mutant.log"; then
        cat "$temporary/mutant.log"; echo "FAIL: causal version control did not reach injected defect: $mutation" >&2; exit 1
    fi
    echo "Causal quest version negative control rejected: $mutation"
done

# Readiness is supplied by the native InitMap completion and an actual surface census.
for mutation in native-initialisation-ignored stale-native-cache immediate-key-retirement omit-cross-surface-unready repeat-latejoin-cap omit-unresolved-local-fence omit-unresolved-surface-fence omit-local-publish-retirement unknown-controller-merge unknown-controller-retirement; do
    python3 - "$repo_root" "$temporary" "$mutation" <<'PYREADINESS'
from pathlib import Path
import sys
root, out = map(Path, sys.argv[1:3]); mutation = sys.argv[3]
remote = (root/'src/GloomhavenVR/Net/Remote/RemoteMapRoom.cs').read_text()
interactor = (root/'src/GloomhavenVR/WorldUI/MapRoom/MapLocationInteractor.cs').read_text()
changes = {
 'unknown-controller-merge': ('remote', 'if (!NativeMapQuestSelection.TryGetHostProposal(out _, out _))', 'if (bool.Parse("false") && !NativeMapQuestSelection.TryGetHostProposal(out _, out _))'),
 'unknown-controller-retirement': ('remote', 'if (!NativeMapQuestSelection.TryGetHostProposal(out var proposal, out _))\n            return false; // initialization is unknown, never proof of a committed/retired opening', 'if (!NativeMapQuestSelection.TryGetHostProposal(out var proposal, out _))\n        { RetireVersionVisit(); return false; }'),
 'native-initialisation-ignored': ('interactor', 'NativeMapInitialised?.GetValue(native) is bool ready && ready;', 'bool.Parse("true");'),
 'stale-native-cache': ('remote', 'if (_cacheValid && generation == _cacheGeneration)', 'if (_cacheValid)'),
 'immediate-key-retirement': ('remote', 'if (!_unresolvedSelectMiss)\n            {', 'if (bool.Parse("false") && !_unresolvedSelectMiss)\n            {'),
 'omit-cross-surface-unready': ('remote', 'interactor.PrepareSharedSelectionChange(null);', 'System.GC.KeepAlive(interactor);'),
 'repeat-latejoin-cap': ('remote', 'if (!_unresolvedSelectSurfaceAttempted)', 'if (bool.Parse("true"))'),
 'omit-unresolved-local-fence': ('remote', '|| _unresolvedSelectRevision != interactor.SelectionRevision', '|| false'),
 'omit-unresolved-surface-fence': ('remote', 'if (_unresolvedSelectInteractor != null && advertised != MapIconLayer.MapSurface.Unknown', 'if (bool.Parse("false") && _unresolvedSelectInteractor != null && advertised != MapIconLayer.MapSurface.Unknown'),
 'omit-local-publish-retirement': ('remote', 'if (won)\n                    {\n                        ClearPendingSelection();\n                        ClearUnresolvedSelection();\n                    }', 'if (won)\n                    {\n                        ClearPendingSelection();\n                    }'),
}
name, needle, replacement = changes[mutation]
source = remote if name == 'remote' else interactor
assert source.count(needle) == 1, 'Native readiness mutation seam changed: ' + mutation
source = source.replace(needle, replacement)
if name == 'remote': remote = source
else: interactor = source
(out/'RemoteRoom.cs').write_text(remote); (out/'Interactor.cs').write_text(interactor)
PYREADINESS
    if dotnet run --project "$project" --configuration Release \
        --property:InteractorSource="$temporary/Interactor.cs" --property:RemoteRoomSource="$temporary/RemoteRoom.cs" \
        > "$temporary/mutant.log" 2>&1; then
        echo "FAIL: native quest readiness mutation survived: $mutation" >&2; exit 1
    fi
    case "$mutation" in
        unknown-controller-merge) expected='Missing native proposal controller must hold the latest peer snapshot without merging or fencing it' ;;
        unknown-controller-retirement) expected='An accepted opening must survive temporary native-controller absence with its original relay token' ;;
        native-initialisation-ignored) expected='Partial native census must retain the same live opening while InitMap is still constructing rows' ;;
        stale-native-cache) expected='A completed initialized census must deliver the original opening even when the row set generation did not change' ;;
        immediate-key-retirement) expected='A ready cache miss must retain its opening until one fresh qualified census' ;;
        omit-cross-surface-unready) expected='Readied World A must request original unready before waiting for an unresolved City B or its locked cap' ;;
        repeat-latejoin-cap) expected='Unavailable native surface cap must never be pressed again for the same opening' ;;
        omit-unresolved-local-fence) expected='Obsolete unresolved opening must retire before native delivery: local-refusal' ;;
        omit-unresolved-surface-fence) expected='Obsolete unresolved opening must retire before native delivery: carrier-surface' ;;
        omit-local-publish-retirement) expected='Obsolete unresolved opening must retire before native delivery: local-selection' ;;
    esac
    if ! rg -qF "Unhandled exception. System.InvalidOperationException: $expected" "$temporary/mutant.log"; then
        cat "$temporary/mutant.log"; echo "FAIL: readiness control did not reach injected defect: $mutation" >&2; exit 1
    fi
    echo "Native quest readiness negative control rejected: $mutation"
done
