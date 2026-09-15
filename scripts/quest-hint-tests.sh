#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$root/tests/GloomhavenVR.QuestHintTests/GloomhavenVR.QuestHintTests.csproj"
fixture="$(mktemp -d)"
trap 'rm -rf "$fixture"' EXIT
dotnet run --project "$project" --configuration Release
cp "$root/tests/GloomhavenVR.QuestHintTests/"*.cs "$project" "$fixture/"
python3 - "$root" "$fixture" <<'PY'
from pathlib import Path
import sys
root, dest = map(Path, sys.argv[1:])
s = (root/'src/GloomhavenVR/WorldUI/Composites/QuestPreparationHint.cs').read_text()
for name, old, new in [
    ('callback', '__1?.Invoke();', ';'),
    ('identity', '!ReferenceEquals(manager.questIntroduction, __instance)', 'false'),
    ('flat', '!VRSession.IsRunning || ', '')]:
    assert s.count(old) == 1
    (dest/(name+'.fixture')).write_text(s.replace(old, new))
PY
for mutation in callback identity flat; do
    if dotnet run --project "$fixture/GloomhavenVR.QuestHintTests.csproj" --configuration Release \
        --property:QuestHintSource="$fixture/$mutation.fixture" > "$fixture/$mutation.log" 2>&1; then
        echo "FAIL: quest hint $mutation regression escaped coverage." >&2; exit 1
    fi
    case "$mutation" in
        callback) expected='Suppressed producer must deliver its callback exactly once' ;;
        identity) expected='Battle-goal hint must stay native' ;;
        flat) expected='Flat presentation must stay native' ;;
    esac
    if ! rg -Fq "$expected" "$fixture/$mutation.log"; then cat "$fixture/$mutation.log"; exit 1; fi
    echo "Quest hint negative control: $mutation rejected."
done
