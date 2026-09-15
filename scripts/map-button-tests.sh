#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$repo_root/tests/GloomhavenVR.MapButtonTests/GloomhavenVR.MapButtonTests.csproj"
fixture_dir="$(mktemp -d)"
trap 'rm -rf "$fixture_dir"' EXIT
python3 - "$repo_root/src/GloomhavenVR/WorldUI/MapRoom/MapButtonRail.cs" "$fixture_dir" <<'PY'
from pathlib import Path
import sys
source=Path(sys.argv[1]).read_text(); out=Path(sys.argv[2])
start=source.index('    private void SelectThroughTheGamesOwnApi(')
end=source.index('\n    private static string CapName', start)
method=source[start:end]
fixture='using System;\nusing UnityEngine.UI;\nusing GloomhavenVR.Core;\nnamespace GloomhavenVR.WorldUI.MapRoom;\ninternal sealed partial class MapButtonRail\n{\n'+method+'\n}'
(out/'press.fixture').write_text(fixture)
needle='selectedToggle.isOn = true;'
assert fixture.count(needle)==1
(out/'silent.fixture').write_text(fixture.replace(needle,'button.Select();'))
needle='other.Toggle.isOn = false;'
assert fixture.count(needle)==1
(out/'sibling.fixture').write_text(fixture.replace(needle,'other.Button.Deselect();'))
# All off-bar callers must still reach this dispatcher; comments do not count.
import re
code=re.sub(r'//[^\n]*|/\*[\s\S]*?\*/','',source)
assert 'SelectThroughTheGamesOwnApi(button, source, physical);' in code
PY
dotnet run --project "$project" --configuration Release --property:MapPressSource="$fixture_dir/press.fixture"
cp "$repo_root/tests/GloomhavenVR.MapButtonTests/"*.cs "$project" "$fixture_dir/"
for mutation in silent sibling; do
    if dotnet run --project "$fixture_dir/GloomhavenVR.MapButtonTests.csproj" --configuration Release \
        --property:MapPressSource="$fixture_dir/$mutation.fixture" > "$fixture_dir/$mutation.log" 2>&1; then
        echo "FAIL: $mutation native toggle regression escaped tests." >&2; exit 1
    fi
    expected='native tutorial toggle listeners receive the world-map press'
    if [[ "$mutation" == sibling ]]; then expected='inactive group siblings receive native deselection'; fi
    if ! rg -q "$expected" "$fixture_dir/$mutation.log"; then cat "$fixture_dir/$mutation.log"; exit 1; fi
    echo "Map button negative control: $mutation rejected."
done
