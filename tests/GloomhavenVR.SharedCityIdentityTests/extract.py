"""Compile the actual shared-window and encounter-address resolution methods."""
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
source = (root / 'SharedWindows.cs').read_text()
text = 'using System;using UnityEngine;using UnityEngine.UI;using GloomhavenVR.WorldUI;using GloomhavenVR.WorldUI.MapRoom;\n'
text += 'namespace GloomhavenVR.WorldUI {\n' + method(source, 'internal enum SharedWindowKind : byte')
text += '\ninternal static partial class SharedWindows {\n'
for signature in ('internal static SharedWindowKind KindOf(UIWindow? window)',
                  'internal static UIWindow? WindowOf(SharedWindowKind kind)',
                  'internal static bool ParticipatesHere(SharedWindowKind kind)',
                  'internal static bool IsShared(UIWindow? window)'):
    text += method(source, signature) + '\n'
text += '}}\nnamespace GloomhavenVR.Net { internal static partial class RemoteMapStory {\n'
source = (root / 'RemoteMapStory.cs').read_text()
for signature in ('private static UIWindow? EventPanel()', 'private static uint EncounterKey(UIWindow? window)'):
    text += method(source, signature) + '\n'
text += '}}\nnamespace GloomhavenVR.Net { internal static class NetProtocol {\n'
text += method((root / 'NetProtocol.cs').read_text(), 'public static uint HashMapKey(string? id)')
text += '\n}}\n'
out.write_text(text)
