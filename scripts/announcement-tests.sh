#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
source_file="$repo_root/src/GloomhavenVR/WorldUI/Modal/AnnouncementContinue.cs"
project="$repo_root/tests/GloomhavenVR.AnnouncementTests/GloomhavenVR.AnnouncementTests.csproj"
python3 - "$repo_root" <<'PY'
from pathlib import Path
import sys
root=Path(sys.argv[1]); native=root/'decompiled/GH.Runtime/UILevelUpWindow.cs'
integration=(root/'src/GloomhavenVR/WorldUI/Modal/ModalFallback.10.CatchAll.cs').read_text()
assert 'AnnouncementContinue.Tick(inScenario && WorldUIConfig.ConversionActive);' in integration
assert 'AnnouncementContinue.Tick(false);' in integration
if native.exists():
    source=native.read_text(); fixture=(root/'tests/GloomhavenVR.AnnouncementTests/NativeLevelUpProcess.cs').read_text()
    for name in ['\tprotected void ShowCard()', '\tprivate void OnCardShown()', '\tprivate void OnFinishedShowCards()']:
        start=source.index(name); end=source.index('{',start)+1; depth=1
        while depth:
            depth += (source[end]=='{')-(source[end]=='}'); end+=1
        assert source[start:end] in fixture, 'Native level-up fixture diverged: '+name
    tracker=(root/'decompiled/GH.Runtime/ClickTrackerExtended.cs').read_text()
    start=tracker.index('\tprivate void ProcessClick()'); end=tracker.index('\n\tpublic void AddArea',start)
    assert tracker[start:end].replace('private void ProcessClick()', 'public void ProcessClick()') in fixture, 'Native click process diverged'
PY
dotnet run --project "$project" --configuration Release
mutation_dir="$(mktemp -d)"
trap 'rm -rf "$mutation_dir"' EXIT
cp "$repo_root/tests/GloomhavenVR.AnnouncementTests/"*.cs "$mutation_dir/"
cp "$project" "$mutation_dir/"
for mutation in readiness direct-callback duplicate-frame ownership selection hidden; do
    python3 - "$source_file" "$mutation_dir/Announcement.mutant" "$mutation" <<'PY'
from pathlib import Path
import sys
s=Path(sys.argv[1]).read_text()
mutations={
'readiness': ('&& _owner.enableTracker &&', '&& true &&'),
'direct-callback': ('_owner!.nextCardTracker.ProcessClick();', '_owner!.nextCardTracker.onClick.Invoke();'),
'duplicate-frame': ('_lastConfirmFrame == Time.frameCount', 'false'),
'ownership': ('(!FFSNetwork.IsOnline || _owner.character.IsUnderMyControl)', 'true'),
'selection': ('&& IsLive(_owner.myWindow) && _owner.IsShowing', '&& IsLive(_owner.myWindow)'),
'hidden': ('&& window.IsOpen;', ';'),
}
a,b=mutations[sys.argv[3]]
assert s.count(a)==1
Path(sys.argv[2]).write_text(s.replace(a,b))
PY
    case "$mutation" in
        readiness) expected='reveal animation gates Continue' ;;
        direct-callback) expected='native skip-next-click is consumed without bypass' ;;
        duplicate-frame) expected='same frame cannot advance twice' ;;
        ownership) expected="observer cannot advance another player's level-up" ;;
        selection) expected='card choice is not an announcement' ;;
        hidden) expected='hidden pooled window rejects stale click' ;;
    esac
    if dotnet run --project "$mutation_dir/GloomhavenVR.AnnouncementTests.csproj" --configuration Release \
        --property:AnnouncementSource="$mutation_dir/Announcement.mutant" > "$mutation_dir/mutant.log" 2>&1; then
        cat "$mutation_dir/mutant.log"
        echo "FAIL: $mutation escaped announcement test." >&2
        exit 1
    fi
    if ! rg -qF "$expected" "$mutation_dir/mutant.log"; then
        cat "$mutation_dir/mutant.log"
        exit 1
    fi
    echo "Announcement negative control: $mutation rejected."
done
