"""Exact character fixed-fit kernels with explicit lifecycle/diagnostic ports."""
import re


def bind(root, member):
    source = (root / 'src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.3.Fit.cs').read_text()
    signatures = (
        'private sealed class SubViewFit', 'private sealed class FixedFitState',
        'private static bool ApplyFixedFitCore(', 'private static void CollectActiveSubViews(',
        'private static void AddSubViewCandidate(', 'private static void MeasureFixedFitParts(',
        'private static SubViewFit? OwningSubView(', 'private static bool SolveSubViewPlacement(',
        'private static void EnsureColumnSeam(', 'private static void MaybeReDeriveSeamOnNarrowing(',
        'private static float HostPerParentUnits(', 'private static int FixedFitLevelsUp(',
        'private static bool TryGetVisibleHostRect(ConvertedPanel panel, Graphic g,\n        out Vector2 gMin, out Vector2 gMax, out Vector2 aMin',
        'internal static bool CountsAsFitContent(', 'internal static void BeginContentQuery(',
        'private static Vector2 AuthoredOffset(', 'private static void AuthoredHostRect(',
        'private static void ClampFittedHeightToCanvas(', 'private static RectTransform? FindEnclosingClipper(',
        'private static bool TryMeasureContent(', 'private static void RecordTopContributor(',
        'private static bool ReassertConversionFrame(', 'private enum MeasureReject',
    )
    bodies = [member(source, signature) for signature in signatures]
    for signature in (
        'private static bool TryGetVisibleHostRect(ConvertedPanel panel, Graphic g,\n        out Vector2 gMin, out Vector2 gMax, float',
        'private static Transform? ParkedGuestControl()', 'private static Transform? ParkedGuestReadyRow()',
        'private static bool IsParkedGuest(', 'private static int TransientFamilyOf(',
    ):
        start = source.index(signature)
        bodies.append(source[start:source.index(';', start) + 1])
    burst = (root / 'src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.9c.SubViewBurst.cs').read_text()
    bodies += [member(burst, signature) for signature in (
        'private static int SubViewOpenSetSignature(', 'private static void MixOpenSubView(', 'private static bool SubViewIsShown(',
    )]
    width = re.search(r'private const float FixedFitWidthPx\s*=\s*[^;]+;', source)[0]
    used = '\n'.join(bodies) + width
    declarations = []
    for declaration in re.findall(r'^    (?:private|internal) (?:static(?: readonly)?|const) [^\n{;]+;', source, re.M):
        if '=>' in declaration:
            continue
        names = re.findall(r'\b([A-Za-z_]\w*)\s*(?:=|;)', declaration)
        if any(re.search(r'\b' + name + r'\b', used) for name in names):
            declarations.append(declaration)
    declarations.append(width)
    code = ('using System;using System.Collections.Generic;using UnityEngine;using UnityEngine.UI;using GloomhavenVR.Core;'
            'namespace GloomhavenVR.WorldUI {internal static partial class CanvasConversion {'
            + '\n'.join(declarations + bodies))
    code += '''
    internal static object NewCreationFit764()=>new FixedFitState();
    internal static void FitCreation764(ConvertedPanel panel,object state)
    {var fx=(FixedFitState)state;fx.Comparisons++;ApplyFixedFitCore(panel,panel.Target,fx,panel.HostRect.rect.size,Vector2.zero);panel.FitMeasuredOnce=true;}
    internal static Vector4 CreationSeat764(object state){var f=(FixedFitState)state;return new Vector4(f.ViewGapPx,f.ViewOverlapPx,f.ViewSpillPx,f.ColumnSeamX);}
    private static readonly Dictionary<int,UIWindow?> SubViewWindows=new();
    private static void NoticeBurstCensus(FixedFitState state,Vector2 size){}
    private static void NoticeFixedFitSettled(ConvertedPanel panel,FixedFitState state){}
    private static void LogFixedFit(ConvertedPanel p,FixedFitState f,float s,string verdict,string note,bool throttled){}
    private static float ResolveStableHeightCap(RectTransform t,string name,out string source){source="outside creation fixture";return 1080;}
    }}
'''
    return code
