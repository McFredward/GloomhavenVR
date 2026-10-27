"""Execute native-ready ownership coordinators, not a restated phase predicate."""
from pathlib import Path
import sys

def method(source, signature):
    assert source.count(signature) == 1, signature
    start = source.index(signature)
    opening = source.index('{', start)
    depth, end = 1, opening + 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]

root, out = map(Path, sys.argv[1:])
source = (root / 'LoadoutConfirmPark.cs').read_text()
text = '''using System;using UnityEngine;using UnityEngine.UI;using Object=UnityEngine.Object;
using GloomhavenVR.Core;using GloomhavenVR.WorldUI.MapRoom;
namespace GloomhavenVR.WorldUI { internal static partial class LoadoutConfirmPark {
'''
for signature in ('private static void TickCore()',
                  'private static GameObject? ResolveControl(UILoadoutManager lm, out bool readyToggle)',
                  'private static bool ReadyToggleVisible()',
                  'private static bool Park(UIWindow host, GameObject control, bool readyToggle)',
                  'private static void Unpark(string why)'):
    text += method(source, signature) + '\n'
text += '}}\nnamespace GloomhavenVR.WorldUI { internal static partial class ModalFallback {\n'
source = (root / 'ModalFallback.7.Close.cs').read_text()
text += method(source, 'private readonly struct WindowGroupRule') + '\n'
start = source.index('private static readonly WindowGroupRule[] WindowGroups =')
end = source.index('\n    };', start) + len('\n    };')
text += source[start:end] + '\n'
text += method(source, 'private static bool TryFindFloatedGroupLeader(UIWindow window, out UIWindow? leader, out string why)') + '\n'
source = (root / 'ModalFallback.CompositeTransfer.cs').read_text()
text += method(source, 'internal static bool ReleaseForComposite(UIWindow window)') + '\n}}\n'
out.write_text(text)
