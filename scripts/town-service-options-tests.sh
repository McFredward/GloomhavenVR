#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
test_dir="$(mktemp -d)"
trap 'rm -rf "$test_dir"' EXIT
python3 - "$repo_root" "$test_dir" <<'PY'
from pathlib import Path
import re, sys
root, out = map(Path, sys.argv[1:])
base = root / 'src/GloomhavenVR'
def read(path): return (base / path).read_text()
def method(source, signature):
    start = source.index('    ' + signature)
    return source[start:source.index('\n    }', start) + len('\n    }')]
curated = read('WorldUI/Options/VROptionsTab.4.Curated.cs')
content = read('WorldUI/Options/VROptionsTab.3.Content.cs')
rows = read('WorldUI/Options/VROptionsTab.2.Rows.cs')
variants = read('WorldUI/Options/VROptionsTab.5.Variants.cs')
# Compile the entire real curated table, not a hand-written town-only surrogate.
start = curated.index('    internal readonly struct CuratedEntry')
end = curated.index('\n    };', curated.index('internal static readonly CuratedCategory[] Curated')) + len('\n    };')
s = 'using System;\nusing System.Collections.Generic;\nusing UnityEngine;\nusing TMPro;\nusing GloomhavenVR.Core;\nusing GloomhavenVR.Cards;\nusing GloomhavenVR.Hands;\nnamespace GloomhavenVR.WorldUI {\ninternal static partial class VROptionsTab {\n'
s += curated[start:end] + '\n'
for signature in ['private static void EnsureLookup()', 'private static ConfigCatalog.ConfigItem? Lookup(string section, string key)', 'private static bool TryBuildTownServiceModeRow(Transform parent, ConfigCatalog.ConfigItem item,']:
    s += method(curated, signature) + '\n'
start = curated.index('    private static bool HasSpecialRow(')
s += curated[start:curated.index(';', start)+1] + '\n'
s += method(content, 'private static int BuildCurated()') + '\n'
s += method(content, 'private static int BuildItem(ConfigCatalog.ConfigItem item, string? caption = null, string? hintKey = null)') + '\n'
s += method(rows, 'private static void BuildPresetRow(Transform parent, ConfigCatalog.ConfigItem item, string? caption,') + '\n'
# Preserve the actual dispatch through special rows; generic row bodies are outside this test.
start = rows.index('    private static void BuildRow(')
end = rows.index('        // …and a couple', start)
s += rows[start:end] + '        GenericRows++;\n    }\n'
start = curated.index('    private static bool TryBuildSpecialRow(')
end = curated.index('        // Item 12:', start)
s += curated[start:end] + '        return false;\n    }\n'
# Production filters, including every live dependency and both real variant families.
start = variants.index('    private sealed class VariantFamily')
end = variants.index('\n    }', variants.index('private static bool IsShownForCurrentVariant')) + len('\n    }')
s += variants[start:end] + '\n}\n}\n'
s += read('WorldUI/Options/VROptionsTab.8.Dependencies.cs').replace('using System;', '').replace('using System.Collections.Generic;', '').replace('namespace GloomhavenVR.WorldUI;', 'namespace GloomhavenVR.WorldUI {') + '\n}\n'
# Production default, bound description and localization; config persistence itself is a fixture.
config = read('WorldUI/WorldUIConfig.cs')
start = config.index('        ImmersiveTownServices = _file.Bind(')
last = config.index('        ImmersiveTownSoundEffects = _file.Bind(', start)
bind = config[start:config.index(';', last)+1]
defaults_source = read('Defaults/Defaults.WorldUI.cs')
defaults = '\n'.join(re.search(r'    internal const bool '+key+r' = .*?;', defaults_source).group(0)
                     for key in ('ImmersiveTownServices','ImmersiveTownSpeech','ImmersiveTownSoundEffects'))
s += 'namespace GloomhavenVR.WorldUI { internal static class Defaults {\n' + defaults + '\n} internal static class WorldUIConfig { internal static Entry<bool> ImmersiveTownServices = null!, ImmersiveTownSpeech = null!, ImmersiveTownSoundEffects = null!; internal static ConfigFile _file = new(); internal static void Bind() {\n' + bind + '\n} } }\n'
loc = read('Core/Loc/Loc.cs')
keys = ['cat_panels','vr_sec_townservices','vr_o_immersivetown','vr_o_townspeech','vr_o_townsfx',
        'town_mode_npcs','town_mode_windows','h_vr_o_immersivetown','h_vr_o_townspeech','h_vr_o_townsfx']
s += 'namespace GloomhavenVR.Core { internal static partial class Loc { internal static readonly Dictionary<string,(string En,string De)> Texts = new() {\n'
for key in keys:
    match = re.search(r'\["'+key+r'"\]\s*=\s*Pair\([\s\S]*?\),', loc)
    assert match, key
    s += match.group(0) + '\n'
s += '}; } }\n'
(out / 'Options.fixture').write_text(s)
print('Town options: real curated table, row construction/dispatch, dropdown callbacks, visibility filters, bound default and localized strings compiled; native UI/config are fixtures.')
PY
cp "$repo_root/tests/GloomhavenVR.TownOptionsTests/"*.cs "$test_dir/"
cp "$repo_root/tests/GloomhavenVR.TownOptionsTests/"*.csproj "$test_dir/"
project="$test_dir/GloomhavenVR.TownOptionsTests.csproj"
dotnet run --project "$project" --configuration Release --property:OptionsSource="$test_dir/Options.fixture"
for mutation in missing-entry special-dispatch value-inversion callback-inversion off-hidden default-disabled donor-callback; do
    python3 - "$test_dir" "$mutation" <<'PY'
from pathlib import Path
import sys
out = Path(sys.argv[1]); s = (out / 'Options.fixture').read_text()
a,b = {
    'missing-entry': ('new("WorldUI", "ImmersiveTownServices", "vr_o_immersivetown"),', ''),
    'special-dispatch': ('if (component == 0 && TryBuildSpecialRow', 'if (component == 99 && TryBuildSpecialRow'),
    'value-inversion': ('enabled ? 0 : 1', 'enabled ? 1 : 0'),
    'callback-inversion': ('item.Entry.BoxedValue = index == 0;', 'item.Entry.BoxedValue = index == 1;'),
    'off-hidden': ('!HasItsOwnPage(item) && IsShownForCurrentVariant(item) && DependencyMet(item);', '!HasItsOwnPage(item) && IsShownForCurrentVariant(item) && DependencyMet(item) && (!(item.Entry.BoxedValue is bool b) || b);'),
    'default-disabled': ('internal const bool ImmersiveTownServices = true;', 'internal const bool ImmersiveTownServices = false;'),
    'donor-callback': ('dropdown.onValueChanged.RemoveAllListeners();', ''),
}[sys.argv[2]]
assert a in s, sys.argv[2]
(out / 'Options.mutant').write_text(s.replace(a,b,1))
PY
    if dotnet run --project "$project" --configuration Release --property:OptionsSource="$test_dir/Options.mutant" > "$test_dir/mutant.log" 2>&1; then
        cat "$test_dir/mutant.log"
        echo "FAIL: town options negative control $mutation escaped." >&2
        exit 1
    fi
    if ! rg -q 'TOWN OPTIONS ASSERTION:' "$test_dir/mutant.log"; then
        cat "$test_dir/mutant.log"
        exit 1
    fi
    echo "Town options negative control: $mutation rejected."
done
