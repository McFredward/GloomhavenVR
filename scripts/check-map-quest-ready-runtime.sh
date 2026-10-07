#!/usr/bin/env bash
# Production dispatcher + unchanged native ready visibility/init/press and desktop wrapper.
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
fixture_dir="$(mktemp -d)"
trap 'rm -rf "$fixture_dir"' EXIT
python3 - "$repo_root" "$fixture_dir" <<'PY'
from pathlib import Path
import hashlib,json,os,subprocess,sys
root,out=map(Path,sys.argv[1:]); out.mkdir(exist_ok=True)
common=Path(subprocess.check_output(['git','-C',str(root),'rev-parse','--path-format=absolute','--git-common-dir'],text=True).strip())
native=Path(os.environ.get('GHVR_NATIVE_SOURCE_ROOT',str(common.parent/'decompiled/GH.Runtime')))
fixture=root/'tests/map-quest-ready-runtime/NativeFixture.cs'
fixture_text=fixture.read_text()
sources={}
def method(file,signature):
    source=(native/file).read_text(); sources[str(native/file)]=hashlib.sha256((native/file).read_bytes()).hexdigest()
    start=source.index(signature); opening=source.index('{',start); depth=1; end=opening+1
    while depth:
        depth+=(source[end]=='{')-(source[end]=='}'); end+=1
    return source[start:end]
if native.exists():
    signatures=[('UIReadyToggle.cs',name) for name in (
        'public bool ShouldBeVisible','public void Initialize(', 'public void ToggleVisibility(',
        'public void SetInteractable(', 'private void UpdateVisiblity(',
        'private bool IsReadyUpForbidden(', 'public void ReadyUp(bool toggledOn, bool autoValidateUnreadying)')]
    signatures.extend((('UIGuildmasterConfirmActionButtonPresenter.cs','public override void ShowQuestSelectedAction('),
        ('UIGuildmasterConfirmActionButton.cs','private void OnClicked('),
        ('UIGuildmasterConfirmActionPopup.cs','private void Confirm(')))
    for file,signature in signatures:
        assert method(file,signature) in fixture_text, 'Native ready fixture differs from read-only game source: '+file+' '+signature
    print('Native readiness fixture: ten verbatim method bodies match the read-only game source.')
else:
    print('Native readiness fixture: read-only source unavailable; executing the committed native bodies.')
sources[str(fixture)]=hashlib.sha256(fixture.read_bytes()).hexdigest()
(out/'Native.cs').write_text(fixture_text)
production=root/'src/GloomhavenVR/WorldUI/MapRoom'
for name in ('MapQuestReadyUp.cs','ReadyToggleParkClaim.cs'):
    path=production/name; (out/name).write_bytes(path.read_bytes()); sources[str(path)]=hashlib.sha256(path.read_bytes()).hexdigest()
(out/'source-hashes.json').write_text(json.dumps(sources,indent=2)+'\n')
PY
project="$repo_root/tests/map-quest-ready-runtime/Ready.csproj"
dotnet run --project "$project" --configuration Release --property:FixtureDir="$repo_root/tests/map-quest-ready-runtime" \
    --property:ProductionDir="$fixture_dir" --property:NativeSource="$fixture_dir/Native.cs"
cp "$fixture_dir/MapQuestReadyUp.cs" "$fixture_dir/Original.cs"
for mutation in visible-shortcut reset-discard wrong-controller desktop-cleanup stale-claim flat-replay destroyed-claim; do
    python3 - "$fixture_dir" "$mutation" <<'PY'
from pathlib import Path
import sys
folder=Path(sys.argv[1]); source=(folder/'Original.cs').read_text()
if sys.argv[2]=='destroyed-claim':
    (folder/'MapQuestReadyUp.cs').write_text(source)
    path=folder/'ReadyToggleParkClaim.cs'; original=path.read_text()
    needle='_claimedObject != null && Time.unscaledTime < _claimedUntil'
    assert original.count(needle)==1
    path.write_text(original.replace(needle,'Time.unscaledTime < _claimedUntil'))
    raise SystemExit(0)
changes={
 'visible-shortcut': ('        if (!Singleton<UIReadyToggle>.IsInitialized)\n            return;\n        UIReadyToggle? toggle',
                       '        if (Singleton<UIReadyToggle>.IsInitialized && Singleton<UIReadyToggle>.Instance.IsVisible) { Drop("old visibility shortcut"); return; }\n        if (!Singleton<UIReadyToggle>.IsInitialized)\n            return;\n        UIReadyToggle? toggle'),
 'reset-discard': ('        // The native map/controller may survive a VR room rebuild',
                    '        _pendingConfirm = null;\n        _pendingQuest = null;\n        // The native map/controller may survive a VR room rebuild'),
 'wrong-controller': ('        if (!ReferenceEquals(controller, _pendingController)\n            || live == null',
                      '        if (live == null'),
 'desktop-cleanup': ('            CapturePrompt("UIGuildmasterConfirmActionButtonPresenter", quest, clicked);',
                      '            CapturePrompt("UIGuildmasterConfirmActionButtonPresenter", quest, onConfirmCallback);'),
 'stale-claim': ('                      && ReferenceEquals(parkedToggle, questConfirmToggle);', ';'),
 'flat-replay': ('            ConsumeNativeAnswer(DesktopConfirm?.GetValue(__instance) as Action);',
                 '            ConsumeNativeAnswer(null);'),
}
needle,replacement=changes[sys.argv[2]]; assert source.count(needle)==1, 'production mutation seam changed'
(folder/'MapQuestReadyUp.cs').write_text(source.replace(needle,replacement))
PY
    if dotnet run --project "$project" --configuration Release --property:FixtureDir="$repo_root/tests/map-quest-ready-runtime" \
        --property:ProductionDir="$fixture_dir" --property:NativeSource="$fixture_dir/Native.cs" > "$fixture_dir/$mutation.log" 2>&1; then
        cat "$fixture_dir/$mutation.log"
        echo "FAIL: $mutation escaped the production readiness test." >&2; exit 1
    fi
    case "$mutation" in
        visible-shortcut) expected='visible sticky toggle must not swallow native quest preview' ;;
        reset-discard) expected='room reset preserves the pending native proposal' ;;
        wrong-controller) expected='replacement native controller invalidates its predecessor callback' ;;
        desktop-cleanup) expected='desktop native click wrapper closes hover preview' ;;
        stale-claim) expected='stale parked singleton cannot claim the current confirm' ;;
        flat-replay) expected='native 2D prompt already answered must not replay on room entry' ;;
        destroyed-claim) expected='destroyed native target immediately relinquishes its claim' ;;
    esac
    if ! rg -Fq "System.InvalidOperationException: $expected" "$fixture_dir/$mutation.log"; then
        cat "$fixture_dir/$mutation.log"; echo "FAIL: negative control failed outside its causal assertion." >&2; exit 1
    fi
    echo "Map quest readiness negative control: $mutation rejected."
done
