"""Bind unchanged native first-fit measurement/apply kernels; no copied policy."""
from pathlib import Path
import re


def bind(root: Path, method):
    source = (root / 'src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.3.Fit.cs').read_text()
    signatures = (
        'private static bool TryGetVisibleHostRect(ConvertedPanel panel, Graphic g,\n        out Vector2 gMin, out Vector2 gMax, out Vector2 aMin',
        'internal static bool CountsAsFitContent(', 'internal static void BeginContentQuery(',
        'private static Vector2 AuthoredOffset(', 'private static void AuthoredHostRect(',
        'private static void ClampFittedHeightToCanvas(', 'private static RectTransform? FindEnclosingClipper(',
        'private static bool TryMeasureContent(', 'private static void RecordTopContributor(',
        'private static void ApplyFitConverging(', 'private static bool ReassertConversionFrame(',
        'private enum MeasureReject',
    )
    bodies = [method(source, signature) for signature in signatures]
    short = 'private static bool TryGetVisibleHostRect(ConvertedPanel panel, Graphic g,\n        out Vector2 gMin, out Vector2 gMax, float'
    start = source.index(short)
    bodies.append(source[start:source.index(';', start) + 1])
    used = '\n'.join(bodies)
    names = set(re.findall(r'\b(?:s_last\w+|\w+Memo|ClipperAncestorScratch|GraphicScratch|CornerScratch|MeasureTop\w+|FitMinAlpha|AuthoredCollapsedRatio|ShowAnimationScaleTol|FitContentPaddingPx|FitChangeFraction|FitApplyIterations)\b', used))
    declarations = []
    for declaration in re.findall(r'^    (?:private|internal) (?:static(?: readonly)?|const) [^\n{;]+;', source, re.M):
        if any(re.search(r'\b' + name + r'\b', declaration) for name in names):
            declarations.append(declaration)
    declared = '\n'.join(declarations)
    missing = [name for name in names if not re.search(r'\b' + name + r'\b', declared)]
    if missing:
        raise RuntimeError('Native first-fit field binding drift: ' + str(sorted(missing)))
    diagnostics = sorted(set(re.findall(r'\bs_last\w+\b', declared)))
    return ('using System;using System.Collections.Generic;using UnityEngine;using UnityEngine.UI;using GloomhavenVR.Core;'
            'namespace GloomhavenVR.WorldUI {internal static partial class CanvasConversion {'
            + '\n'.join(declarations + bodies)
            + 'internal static bool MeasureFirst748(ConvertedPanel panel,out Vector2 size,out Vector2 center)'
              '{return TryMeasureContent(panel,panel.Target,out size,out center);}'
              'internal static void ApplyFirst748(ConvertedPanel panel,ref Vector2 size,ref Vector2 center)'
              '{ApplyFitConverging(panel,panel.Target,ref size,ref center,out _,out _);'
              'GC.KeepAlive(new object?[]{' + ','.join(diagnostics) + '});}'
              'private static float ResolveStableHeightCap(RectTransform target,string name,out string source)'
              '{source="outside native loadout fixture";throw new NotSupportedException(source);}'
              'private static void FlushPendingLayout(ConvertedPanel panel)'
              '{LayoutRebuilder.ForceRebuildLayoutImmediate(panel.Target);Canvas.ForceUpdateCanvases();}'
              '}}')
