#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$repo_root/tests/GloomhavenVR.TownWindowMapSwitchTests/GloomhavenVR.TownWindowMapSwitchTests.csproj"
mutation_dir="$(mktemp -d)"
trap 'rm -rf "$mutation_dir"' EXIT
python3 - "$repo_root" "$mutation_dir/CloseDispatch.cs" <<'PYTHON'
from pathlib import Path
import sys
s=(Path(sys.argv[1])/'src/GloomhavenVR/WorldUI/MapRoom/GuildmasterDestinations.cs').read_text()
start=s.index('    private static bool ReturnHome(')
end=s.index('    private static EGuildmasterMode HomeMode()', start)
Path(sys.argv[2]).write_text('using GloomhavenVR.Core;\nnamespace GloomhavenVR.WorldUI.MapRoom;\ninternal static partial class GuildmasterDestinations {\n'+s[start:end]+'\n}')
PYTHON
dotnet run --project "$project" --configuration Release --property:ProductionCloseDispatch="$mutation_dir/CloseDispatch.cs"
for mutation in exit scoped-mode restore live close-intent close-dispatch; do
    python3 - "$repo_root" "$mutation_dir/Production.cs" "$mutation" <<'PYTHON'
from pathlib import Path
import sys
s=(Path(sys.argv[1])/'src/GloomhavenVR/WorldUI/MapRoom/TownWindowMapSwitch.cs').read_text()
close=Path(sys.argv[2]).with_name('CloseDispatch.cs').read_text()
changes={
'exit':('return false;\n    }','return true;\n    }'),
'scoped-mode':('currentMode = newMode;','// mutation: native map visibility sees service mode'),
'restore':('currentMode = service;','// mutation: map steals the active service'),
'live':('!ModalFallback.FloatIsLive(window)','false'),
'close-intent':('if (TownWindowCloseScope.Active) return true;', '// mutation: explicit service close intercepted as map browsing'),
}
if sys.argv[3]=='close-dispatch':
    a='using var closeScope = TownWindowCloseScope.Enter();'
    assert close.count(a)==1
    Path(sys.argv[2]).with_name('CloseDispatchMutated.cs').write_text(close.replace(a,'// mutation: actual native close omits intent scope'))
    Path(sys.argv[2]).write_text(s)
else:
    a,b=changes[sys.argv[3]]
    assert s.count(a)==1
    Path(sys.argv[2]).write_text(s.replace(a,b))
PYTHON
    close_source="$mutation_dir/CloseDispatch.cs"
    if [[ "$mutation" == close-dispatch ]]; then close_source="$mutation_dir/CloseDispatchMutated.cs"; fi
    if dotnet run --project "$project" --configuration Release --property:ProductionSource="$mutation_dir/Production.cs" --property:ProductionCloseDispatch="$close_source" > "$mutation_dir/output" 2>&1; then
        echo "FAIL: map/service lifecycle mutation escaped: $mutation" >&2; exit 1
    fi
    if ! grep -q 'Unhandled exception' "$mutation_dir/output"; then
        cat "$mutation_dir/output" >&2; exit 1
    fi
done
echo "Town window map switch: 6 causal controls rejected."
