#!/usr/bin/env bash
# Execute production admission and message-hold lifetime, including regression mutations.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$root/tests/GloomhavenVR.TutorialScopeTests/GloomhavenVR.TutorialScopeTests.csproj"
python3 "$root/tests/GloomhavenVR.TutorialScopeTests/source_contract.py" "$root"
dotnet run --project "$project" --configuration Release
fixture="$(mktemp -d)"
trap 'rm -rf "$fixture"' EXIT
cp "$root/tests/GloomhavenVR.TutorialScopeTests/"*.cs "$project" "$fixture/"
python3 - "$root" "$fixture" <<'PY'
from pathlib import Path
import sys
root, dest = map(Path, sys.argv[1:])
base = root/'src/GloomhavenVR/Compat/Tutorial'
scope = (base/'TutorialLessonScope.cs').read_text()
hold = (base/'TutorialChainHold.cs').read_text()
for name, original, old, new in [
    ('identity', scope, 'string.Equals(currentId, firstId, StringComparison.Ordinal)', 'true'),
    ('filename', scope, 'string.Equals(currentFilename, firstFilename, StringComparison.Ordinal)', 'true'),
    ('mode', scope, 'mode == EGameMode.FrontEndTutorial', 'true'),
    ('online', scope, '!online && ', ''),
    ('hold-admission', hold, '!TutorialLessonScope.IsActive || LevelEventsController.s_Instance == null', 'LevelEventsController.s_Instance == null'),
    ('capture-owner', hold, '!TutorialLessonScope.IsActive || !ReferenceEquals(controller, _controller)', '!TutorialLessonScope.IsActive'),
    ('replay-owner', hold, '&& ReferenceEquals(controller, LevelEventsController.s_Instance)', '')]:
    assert original.count(old) == 1, name
    (dest/(name+'.fixture')).write_text(original.replace(old, new))
# A commented-out entry gate cannot pass the source binding.
mutant = dest/'src/GloomhavenVR/Compat/Tutorial'
import shutil
shutil.copytree(base, mutant)
p = mutant/'Controls/ControlsTutorial.cs'
s = p.read_text()
old = 'if (!Enabled || !TutorialLessonScope.IsActive || _phase != Phase.Idle)'
assert s.count(old) == 1
p.write_text(s.replace(old, '// '+old+'\n        if (!Enabled || _phase != Phase.Idle)'))
PY
for mutation in identity filename mode online hold-admission capture-owner replay-owner; do
    scope="$root/src/GloomhavenVR/Compat/Tutorial/TutorialLessonScope.cs"
    hold="$root/src/GloomhavenVR/Compat/Tutorial/TutorialChainHold.cs"
    case "$mutation" in
        identity) scope="$fixture/$mutation.fixture"; expected="Different tutorial ID must not reuse the first file's admission" ;;
        filename) scope="$fixture/$mutation.fixture"; expected='Stale first ID with another file must not admit a lesson' ;;
        mode) scope="$fixture/$mutation.fixture"; expected='Other game modes must not inherit stale tutorial identity' ;;
        online) scope="$fixture/$mutation.fixture"; expected='Online sessions must not inject local tutorial steps' ;;
        hold-admission) hold="$fixture/$mutation.fixture"; expected='Later tutorial must never engage a lesson hold' ;;
        capture-owner) hold="$fixture/$mutation.fixture"; expected='New controller messages must bypass an old controller hold' ;;
        replay-owner) hold="$fixture/$mutation.fixture"; expected='Stale held messages must not replay into either controller after scenario replacement' ;;
    esac
    if dotnet run --project "$fixture/GloomhavenVR.TutorialScopeTests.csproj" --configuration Release \
        --property:TutorialScopeSource="$scope" --property:TutorialHoldSource="$hold" > "$fixture/$mutation.log" 2>&1; then
        echo "FAIL: tutorial $mutation regression escaped coverage." >&2; exit 1
    fi
    if ! rg -Fq "Unhandled exception. System.Exception: $expected" "$fixture/$mutation.log"; then
        cat "$fixture/$mutation.log"; exit 1
    fi
    echo "Tutorial scope negative control: $mutation rejected."
done
if python3 "$root/tests/GloomhavenVR.TutorialScopeTests/source_contract.py" "$fixture" > "$fixture/binding.log" 2>&1; then
    echo 'FAIL: removed admission gate escaped production binding.' >&2; exit 1
fi
rg -Fq 'RequestForTutorial must reject non-first tutorials before changing lesson state' "$fixture/binding.log"
echo 'Tutorial scope negative control: commented admission gate rejected.'
