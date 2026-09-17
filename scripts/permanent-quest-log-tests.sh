#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$repo_root/tests/GloomhavenVR.PermanentQuestLogTests/GloomhavenVR.PermanentQuestLogTests.csproj"
source_file="$repo_root/src/GloomhavenVR/WorldUI/Modal/ModalFallback.PermanentQuestLog.cs"
dotnet run --project "$project" --configuration Release
python3 - "$repo_root" <<'PY'
from pathlib import Path
import sys
root=Path(sys.argv[1])/'src/GloomhavenVR/WorldUI/Modal'
tick=(root/'ModalFallback.4.Tick.cs').read_text()
convert=(root/'ModalFallback.8.Convert.cs').read_text()
spawn=(root/'ModalFallback.9.Spawn.cs').read_text()
assert 'if (refused && alive && !wp.UserClosing && !wp.EmptyReleasePending)\n                RememberWithdrawnQuestLog(wp.Window!);\n            Converted.RemoveAt(i);' in tick
assert 'TickCatchAll(inScenario);\n        TickPermanentQuestLog();' in tick
assert 'Converted.Add(wp);\n            CompletePermanentQuestLogReturn(window);' in convert
assert 'ReleaseAllWindows("module shutdown");\n        ResetPermanentQuestLog();' in tick
assert 'ReleaseMapRoomFloats(string reason)\n    {\n        ResetPermanentQuestLog();' in spawn
print('Permanent quest log: 5 production binding checks passed.')
PY
mutation_dir="$(mktemp -d)"
trap 'rm -rf "$mutation_dir"' EXIT
for mutation in native-closed story-return consume-return room-exit; do
    python3 - "$source_file" "$mutation_dir/Mutated.cs" "$mutation" <<'PY'
from pathlib import Path
import sys
s=Path(sys.argv[1]).read_text()
changes={
 'native-closed': ('if (WorldUIConfig.ConversionActive)', 'if (window.IsOpen && WorldUIConfig.ConversionActive)'),
 'story-return': ('StoryComposite.PointOfNoReturn || FloatRefusalTable.Refuses(window)', 'FloatRefusalTable.Refuses(window)'),
 'consume-return': ('if (ReferenceEquals(window, _permanentQuestLog))', 'if (!ReferenceEquals(window, _permanentQuestLog))'),
 'room-exit': ('if (!MapRoom.MapRoomDriver.Active || window == null)', 'if (window == null)'),
}
old,new=changes[sys.argv[3]]
assert s.count(old)==1
Path(sys.argv[2]).write_text(s.replace(old,new))
PY
    if dotnet run --project "$project" --configuration Release --property:QuestLogSource="$mutation_dir/Mutated.cs" > "$mutation_dir/output" 2>&1; then
        echo "FAIL: permanent quest log mutation survived: $mutation" >&2; exit 1
    fi
    case "$mutation" in
        native-closed|consume-return) expected='native closed quest log must return after curtain' ;;
        story-return) expected='story and loadout exclusion must remain' ;;
        room-exit) expected='map exit must clear pending return' ;;
    esac
    if ! rg -qF "Unhandled exception. System.Exception: $expected" "$mutation_dir/output"; then
        cat "$mutation_dir/output"; exit 1
    fi
    echo "Permanent quest log negative rejected: $mutation"
done
