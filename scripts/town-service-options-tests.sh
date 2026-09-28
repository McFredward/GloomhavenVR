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
start = curated.index('    internal readonly struct CuratedEntry')
end = curated.index('\n    };', curated.index('internal static readonly CuratedCategory[] Curated')) + len('\n    };')
s = 'using System;\nusing System.Collections.Generic;\nusing UnityEngine;\nusing UnityEngine.UI;\nusing TMPro;\nusing GloomhavenVR.Core;\nusing GloomhavenVR.Cards;\nusing GloomhavenVR.Hands;\nnamespace GloomhavenVR.WorldUI {\ninternal static partial class VROptionsTab {\n'
s += curated[start:end] + '\n'
for signature in ['private static void EnsureLookup()', 'private static ConfigCatalog.ConfigItem? Lookup(string section, string key)']:
    s += method(curated, signature) + '\n'
start = curated.index('    private static bool HasSpecialRow(')
s += curated[start:curated.index(';', start)+1] + '\n'
s += method(content, 'private static int BuildCurated()') + '\n'
s += method(content, 'private static int BuildItem(ConfigCatalog.ConfigItem item, string? caption = null, string? hintKey = null)') + '\n'
# Compile the production bool dispatch and native-toggle binder. Other row kinds remain
# explicit fixture boundaries; this suite checks the town setting's real construction path.
start = rows.index('    private static void BuildRow(')
end = rows.index('        // …and a couple', start)
s += rows[start:end]
bool_start = rows.index('        if (item.Kind == ConfigCatalog.ConfigKind.Bool', end)
bool_end = rows.index('\n        if (item.Kind == ConfigCatalog.ConfigKind.Choice', bool_start)
s += rows[bool_start:bool_end] + '\n'
s += '        GenericRows++;\n    }\n'
s += method(rows, 'private static bool BuildBoolRow(Transform parent, ConfigCatalog.ConfigItem item, string? caption, string? hintKey)') + '\n'
start = curated.index('    private static bool TryBuildSpecialRow(')
end = curated.index('        // Item 12:', start)
s += curated[start:end] + '        return false;\n    }\n'
start = variants.index('    private sealed class VariantFamily')
end = variants.index('\n    }', variants.index('private static bool IsShownForCurrentVariant')) + len('\n    }')
s += variants[start:end] + '\n}\n}\n'
s += read('WorldUI/Options/VROptionsTab.8.Dependencies.cs').replace('using System;', '').replace('using System.Collections.Generic;', '').replace('namespace GloomhavenVR.WorldUI;', 'namespace GloomhavenVR.WorldUI {') + '\n}\n'
config = read('WorldUI/WorldUIConfig.cs')
start = config.index('        ImmersiveTownServices = _file.Bind(')
last = config.index('        ImmersiveTownSoundEffects = _file.Bind(', start)
bind = config[start:config.index(';', last)+1]
defaults_source = read('Defaults/Defaults.WorldUI.cs')
defaults = '\n'.join(re.search(r'    internal const bool '+key+r' = .*?;', defaults_source).group(0)
                     for key in ('ImmersiveTownServices','ImmersiveTownSpeech','ImmersiveTownSoundEffects'))
s += 'namespace GloomhavenVR.WorldUI { internal static class Defaults {\n' + defaults + '\n} internal static class WorldUIConfig { internal static Entry<bool> ImmersiveTownServices = null!, ImmersiveTownSpeech = null!, ImmersiveTownSoundEffects = null!; internal static ConfigFile _file = new(); internal static void Bind() {\n' + bind + '\n} } }\n'
loc = read('Core/Loc/Loc.cs')
keys = ['cat_environment','sec_map3d','vr_o_immersivetown','vr_o_townspeech','vr_o_townsfx',
        'h_vr_o_immersivetown','h_vr_o_townspeech','h_vr_o_townsfx']
s += 'namespace GloomhavenVR.Core { internal static partial class Loc { internal static readonly Dictionary<string,(string En,string De)> Texts = new() {\n'
for key in keys:
    match = re.search(r'\["'+key+r'"\]\s*=\s*Pair\([\s\S]*?\),', loc)
    assert match, key
    s += match.group(0) + '\n'
s += '}; } }\n'
(out / 'Options.fixture').write_text(s)
print('Town options: production curated tree, dependency gates, bool dispatch/toggle binding and localization compiled; Unity/BepInEx are fixture boundaries.')
PY
cp "$repo_root/tests/GloomhavenVR.TownOptionsTests/"*.cs "$test_dir/"
cp "$repo_root/tests/GloomhavenVR.TownOptionsTests/"*.csproj "$test_dir/"
project="$test_dir/GloomhavenVR.TownOptionsTests.csproj"
dotnet run --project "$project" --configuration Release --property:OptionsSource="$test_dir/Options.fixture"
for mutation in missing-entry bool-dispatch value-inversion callback-disabled off-hidden default-disabled donor-callback map-gate; do
    python3 - "$test_dir" "$mutation" <<'PY'
from pathlib import Path
import sys
out = Path(sys.argv[1]); s = (out / 'Options.fixture').read_text()
a,b = {
    'missing-entry': ('new("WorldUI", "ImmersiveTownServices", "vr_o_immersivetown"),', ''),
    'bool-dispatch': ('item.Kind == ConfigCatalog.ConfigKind.Bool && BuildBoolRow', 'item.Kind == ConfigCatalog.ConfigKind.Number && BuildBoolRow'),
    'value-inversion': ('toggle.isOn = item.Entry.BoxedValue is bool b && b;', 'toggle.isOn = item.Entry.BoxedValue is bool b && !b;'),
    'callback-disabled': ('ConfigCatalog.ToggleBool(item)', 'Noop()'),
    'off-hidden': ('!HasItsOwnPage(item) && IsShownForCurrentVariant(item) && DependencyMet(item);', '!HasItsOwnPage(item) && IsShownForCurrentVariant(item) && DependencyMet(item) && (!(item.Entry.BoxedValue is bool b) || b);'),
    'default-disabled': ('internal const bool ImmersiveTownServices = true;', 'internal const bool ImmersiveTownServices = false;'),
    'donor-callback': ('toggle.onValueChanged.RemoveAllListeners();', ''),
    'map-gate': ('["WorldUI/ImmersiveTownServices"] = new("Rig", "Vanilla2DMap", Off)', '["WorldUI/ImmersiveTownServices"] = new("Rig", "Vanilla2DMap", On)'),
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
