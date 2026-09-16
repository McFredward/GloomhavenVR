#!/usr/bin/env python3
"""Exercise original curtain/travel decisions and check each native-input admission site."""
from pathlib import Path
import re
import sys

story, interactor, travel = (Path(p).read_text() for p in sys.argv[1:4])


def method(source, signature):
    assert source.count(signature) == 1, f'Production signature changed: {signature}'
    start = source.index(signature)
    end = source.index('\n    }', start) + len('\n    }')
    return source[start:end]


def code(source):
    # Keep quoted strings intact while excluding comments from admission checks.
    return re.sub(r'"(?:\\.|[^"\\])*"|//[^\n]*|/\*[\s\S]*?\*/',
                  lambda m: m[0] if m[0].startswith('"') else '', source)


# The bool helper alone cannot establish that icon dispatch uses it. Each existing
# route must admit input before its first externally observable effect.
checks = [
    ('internal bool AdoptSelection(', 'if (MapInputGate.IsBlocked) return false;', 'if (ReferenceEquals(loc, _selected))'),
    ('private MapLocation? PickFrom(', 'if (MapInputGate.IsBlocked) { how = "the native map interaction mask is locked"; return null; }', 'if (hand == null'),
    ('internal void SetHover(', 'if (want != null && MapInputGate.IsBlocked) want = null;', 'if (ReferenceEquals(want, _hover))'),
    ('private void TickDeselect()', 'if (MapInputGate.IsBlocked) return;', 'if (Time.unscaledTime - _selectedAt'),
    ('private void Deselect(', 'if (MapInputGate.IsBlocked) return;', 'MapLocation? sel = _selected;'),
    ('internal void Dispatch(', 'if (MapInputGate.IsBlocked) return;', 'if (loc == null)'),
]
for signature, guard, effect in checks:
    body = ' '.join(code(method(interactor, signature)).split())
    assert guard in body and effect in body and body.index(guard) < body.index(effect), \
        f'Native map lock admission missing or late: {signature}'

curtain = method(story, '    internal static bool CurtainRefuses(UIWindow? window)')
withheld = method(story, '    private static int CurtainWithheldNow()')
reset = method(travel, '    internal static void Reset()')
shortcut_start = travel.index('        private static bool Prefix(AdventureMapUIManager __instance, MapLocation mapLocation,')
shortcut_end = travel.index('\n        }', shortcut_start) + len('\n        }')
shortcut = travel[shortcut_start:shortcut_end]
active = re.findall(r'        bool active = options\.activeInHierarchy[^;]*;', travel)
assert len(active) == 1, 'Production travel visibility expression changed'
output = '''#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using GloomhavenVR.WorldUI.MapRoom;
namespace GloomhavenVR.WorldUI
{
    internal static partial class StoryComposite
    {
'''+curtain+'\n'+withheld+'''
    }
    internal sealed partial class MapTravelConfirm
    {
        internal bool Visible(UnityEngine.GameObject options, AdventureMapUIManager? mgr)
        {
'''+active[0]+'''
            return active;
        }
    }
}
'''
output += '\nnamespace GloomhavenVR.WorldUI { internal sealed partial class MapTravelConfirm {\n' + reset + '\ninternal static class TravelShortcutGate {\nprivate static bool _logged;\n' + shortcut + '\ninternal static bool Run(AdventureMapUIManager manager, MapLocation location, Action<MapLocation> callback) => Prefix(manager, location, callback);\n} } }\n'
ancestor = method(Path(sys.argv[5]).read_text(), '    private static UIWindow? RefusedForTheMomentAbove(UIWindow window)')
output += '\nnamespace GloomhavenVR.WorldUI { internal static partial class ModalFallback {\n' + ancestor + '\n} }\n'
Path(sys.argv[4]).parent.mkdir(parents=True, exist_ok=True)
Path(sys.argv[4]).write_text(output)
print('Map flow source integration: 6 native input admission sites verified.')
