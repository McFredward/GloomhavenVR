"""Execute the original ready-row coordinator and its real painted-union kernel."""
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
source = (root / 'MapQuestReadyRoster.cs').read_text()
text = 'using System;using System.Collections.Generic;using TMPro;using UnityEngine;using UnityEngine.UI;using GloomhavenVR.Core;\n'
text += 'namespace GloomhavenVR.WorldUI.MapRoom {\n' + method(source, 'internal enum ReadyRosterSite')
start = source.index('internal static class MapQuestReadyRoster')
end = source.index('private static void ClearReport()', start)
text += '\n' + source[start:end].replace('internal static class MapQuestReadyRoster', 'internal static partial class MapQuestReadyRoster')
for signature in ('private static void ClearReport()',
                  'internal static void Tick(ReadyRosterSite site, UIWindow? host, GameObject? parkedConfirm,',
                  'private static void Resolve()',
                  'private static bool IsCurrentSite(ReadyRosterSite site, GameObject? confirm)',
                  'internal static void Reset()',
                  'private static void HoldRow(UIWindow questWindow, GameObject confirm, bool confirmPosed)',
                  'private static void MeasureInk(RectTransform win, RectTransform rowRect, RectTransform confirmRect)',
                  'private static float ResolveGap(RectTransform win, float windowHeight, out string why)',
                  'private static void ReleaseRow(string why)',
                  'private static void EnsureRowLayoutIgnore(GameObject bar)',
                  'private static void ReleaseRowLayoutIgnore()',
                  'private static void WriteRowLayers(int layer, Transform root)',
                  'private static void WriteRowLayerWalk(Transform t, int layer)',
                  'private static void RestoreRowLayers()',
                  'private static GameObject? TrackerBar()'):
    text += method(source, signature) + '\n'
start = source.index('internal static float ReservedHeight() =>')
text += source[start:source.index(';', start)+1] + '\n'
text += '}}\nnamespace GloomhavenVR.WorldUI.MapRoom { internal static partial class MapTravelConfirm {\n'
source = (root / 'MapTravelConfirm.cs').read_text()
for signature in ('internal static bool TryInkBounds(RectTransform frame, Transform sweepRoot,\n                                      out Rect ink, out int counted)',
                  'internal static bool TryInkBounds(RectTransform frame, Transform sweepRoot, Transform? excludeRoot,',
                  'private static bool TryContentBounds(RectTransform frame, Transform sweepRoot,\n                                         Transform? excludeRoot, out Rect content,',
                  'private static bool TryContentBounds(RectTransform frame, Transform sweepRoot,\n                                         Transform? excludeRoot, Transform? excludeParked,',
                  'private static bool ClipToMasks(RectTransform frame, RectTransform rt,',
                  'private static void IntersectWithRect(RectTransform frame, RectTransform clipper,'):
    # The thin overloads are expressions rather than brace-bodied methods.
    start = source.index(signature)
    if source.find('=>', start) < source.find('{', start):
        text += source[start:source.index(';', start)+1] + '\n'
    else:
        text += method(source, signature) + '\n'
text += '}}\n'
out.write_text(text)
