"""Bind the unchanged production tooltip placement/lifetime kernels for native browse pixels.

Diagnostic strings and unrelated character-annotation detection are explicit fixture ports.
Every placement, flatten, clamp, raise, scale receipt and restore body is copied verbatim.
"""


def block(source, signature):
    assert source.count(signature) == 1, signature
    start = source.index(signature)
    end = source.index('{', start) + 1
    depth = 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]


def build(source, patches, adoption, lifecycle, panel):
    bodies = [block(source, signature) for signature in (
        'private struct FlatRecord', 'private struct RaiseRecord',
        'internal static bool PlaceFullCardPreview(', 'internal static void Settle(',
        'private static string RaiseToWindowTop(', 'private static void RecordRaise(',
        'private static void MarkScaleWritten(', 'private static int IndexOfRaise(',
        'private static void KeepLastSibling(', 'private static void KeepRaisedOverlayOnTop(',
        'internal static void ReleaseRaiseOf(', 'private static void RestoreRaise(',
        'private static void TickRaises(', 'private static void Flatten(',
        'internal static void CompleteCanvasTeardown(')]
    safe = source.index('private static float SafeRatio(')
    bodies.append(source[safe:source.index(';', safe) + 1])
    return '''// Source-bound kernels; see tooltip-kernel-boundary.json and the retained full source.
#pragma warning disable 0414, 0649
using System; using System.Collections.Generic; using GloomhavenVR.Core; using HarmonyLib; using UnityEngine; using UnityEngine.UI;
namespace GloomhavenVR.WorldUI {
internal static class TooltipOnWindow {
private const string Scope = "WorldUI";
private const float FlattenAngleEpsilon=.05f, FlattenZEpsilon=.01f, ClampEpsilon=1e-4f, RaiseScaleEpsilon=.001f;
private static readonly List<FlatRecord> Flattened = new();
private static readonly HashSet<Transform> FlattenedIndex = new();
private static readonly List<RaiseRecord> Raised = new();
private static readonly HashSet<string> ReplacedLogged = new(), ReleasedLogged = new();
private static readonly HashSet<int> PlacedLogged = new(), StretchLogged = new(), OverlayNoCanvas = new();
private static readonly List<Transform> SubtreeScratch = new();
private static readonly Vector3[] BoxCorners = new Vector3[4], WinCorners = new Vector3[4];
private static ConvertedPanel? _lastSettleWindow;
private static int _lastSettleFrame, _raisesTaken, _raisesReleased, _raisesDeclinedForeign,
    _raisesDroppedHomeDead, _raisesReleasedBySweep, _overlayFlagWrites, _leakedCanvasesCleared;
private static string DrawOrderEvidence(RectTransform rect, ConvertedPanel owner, string note) => note;
internal static void NoteSlotHover(Component source, string kind, bool hovered) { }
internal static void NoteShowEdgeWithoutWidget(string kind, string what) { }
internal static void FixtureTick() => TickRaises();
internal static int FixtureOutstanding => Raised.Count;
''' + '\n'.join(bodies) + '\n}\n[HarmonyPatch] internal static class NativeTooltipHide747 {\n' + '''
[HarmonyPostfix]
[HarmonyPatch(typeof(AbilityCardUI), nameof(AbilityCardUI.ToggleFullCardPreview))]
''' + block(patches, 'private static void ReleaseAbilityCardPreview(') + '''
}
internal static partial class CanvasConversion {
private const string DropdownListName="Dropdown List", DropdownBlockerName="Blocker";
private const int DropdownListSortingOrder=4000, DropdownBlockerSortingOrder=3999;
private static bool IsDropdownOverlay(Canvas nested) => nested.name==DropdownListName||nested.name==DropdownBlockerName;
private static Camera? SafeOriginalWorldCamera(ConvertedPanel owner, Canvas nested) => nested.worldCamera;
private static void WatchAdoptedCamera(Canvas canvas, Camera? original) { }
internal static bool FixtureAdopt(ConvertedPanel owner, Canvas nested) => AdoptCanvas(owner,nested,0);
''' + block(adoption, 'private static bool AdoptCanvas(') + '\n' + block(lifecycle, 'internal static bool KeepRaisedOverlayOnTop(') + '\n}\n' + block(panel, 'internal struct NestedCanvasRecord') + '''
internal static class UguiPokeSurfaces {
internal static void RegisterNested(Canvas owner, Canvas nested) { }
}
}
'''
