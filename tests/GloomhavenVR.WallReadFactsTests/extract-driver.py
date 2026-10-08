#!/usr/bin/env python3
"""Compile the actual consumer and lifetime seams with counted Unity test doubles."""
import pathlib
import re
import sys
source = pathlib.Path(sys.argv[1]).read_text()
prepare_source = pathlib.Path(sys.argv[4]).read_text() if len(sys.argv) > 4 else pathlib.Path(__file__).parents[2].joinpath('src/GloomhavenVR/Core/WallFade/WallSegmentFade.Prepare.cs').read_text()
mounted_source = pathlib.Path(sys.argv[3]).read_text() if len(sys.argv) > 3 else pathlib.Path(__file__).parents[2].joinpath('src/GloomhavenVR/Core/WallFade/WallSegmentFade.Mounted.cs').read_text()
assert 'f.Mod = f.ModPresentation || IsNativeNonWallPresentation(r);' in source, 'Exact presentation ownership must reach the production classifier'
assert 'private static bool IsModObject(Renderer r) => IsModPresentation(r, r.name) || IsNativeNonWallPresentation(r);' in source, 'Live adoption must share the exact cold ownership verdict'
consumer_root = pathlib.Path(__file__).parents[2] / 'src/GloomhavenVR/Core/WallFade'
free_source = consumer_root.joinpath('WallSegmentFade.FreeStanding.cs').read_text()
assert 'if (f.R == null || f.Mod || f.Figure || !f.Mountable)' in free_source, \
    'FreeStanding riders must reject the same native presentation owner fact'
assert 'if (f.R == null || f.Mod)' in mounted_source, \
    'Mounted candidates must reject the same native presentation owner fact'
assert re.search(r'if \(IsModObject\(p.Renderer\)\)\s*\{\s*RestoreProp\(p\);\s*_mountedOwned.Remove\(p.Renderer\);\s*continue;', mounted_source), \
    'Mounted sticky carry must restitute newly acquired native presentation ownership'
assert 'if (f.WaterSurface && !f.Mod)' in source and 'if (f.Mesh != null && f.WallFadeShader)' in source, \
    'Water protection and mesh-only native wall adoption must retain their original input gates'



def member(signature, source=source):
    assert source.count(signature) == 1, 'Wall read extraction seam changed: ' + signature
    start = source.index(signature)
    opening = source.index('{', start)
    depth = 1
    end = opening + 1
    while depth:
        if source[end] == '{':
            depth += 1
        elif source[end] == '}':
            depth -= 1
        end += 1
    return source[start:end]

assert 'ReadWallCacheChildren(seg.Anchor, _wallCacheRendererScratch);' in member('        private void RefreshSegment(Segment seg)'), 'Prepared child reads must reach the real WallCache hierarchy consumer'
commit = re.sub(r'//[^\n]*|/\*.*?\*/', '', member('        private void RescanCore('), flags=re.S)
assert re.search(r'using\s*\(Phase\(CommitPhase.WallCache\)\)\s*\{\s*'
                 r'BeginWallCacheMaterialFacts\(\);\s*try\s*\{\s*CommitWallCache\(\);\s*\}'
                 r'\s*finally\s*\{\s*EndWallCacheMaterialFacts\(\);\s*\}', commit), \
    'Wall material memo must bracket the real synchronous WallCache phase with finally'
def expression(signature, source=source):
    assert source.count(signature) == 1, 'Wall expression extraction seam changed: ' + signature
    start = source.index(signature)
    return source[start:source.index(';', start) + 1]


def ownership_source():
    root = pathlib.Path(sys.argv[1]).parent if '--ownership-only' in sys.argv else pathlib.Path(__file__).parents[2] / 'src/GloomhavenVR/Core/WallFade'
    rows = root.joinpath('WallSegmentFade.CommitPhases.cs').read_text()
    water = root.joinpath('WallSegmentFade.Water.cs').read_text()
    result = '#nullable enable\nusing System;\nusing System.Collections.Generic;\nusing UnityEngine;\n'
    result += 'namespace GloomhavenVR.Core;\ninternal static partial class WallSegmentFade\n{\n'
    for signature in ('    internal static bool IsWallFadeShaderName(',
                      '    internal static bool IsFoliageShaderName('):
        result += expression(signature) + '\n'
    result += 'private sealed partial class FadeDriver\n{\n'
    for signature in ('        private struct RendererFact',
                      '        private void ClassifyMaterialsAndName(',
                      '        private void ClassifySlice('):
        result += member(signature) + '\n'
    result += expression('        private static bool IsModObject(') + '\n'
    for signature in ('        private bool IsWaterShader(', '        private static bool IsWaterNameFamily('):
        result += member(signature, water) + '\n'
    result += expression('        private static readonly string[] WaterNameTokens', water) + '\n'
    for signature in ('        private const ulong FnvOffset', '        private const ulong FnvPrime',
                      '        private const ulong DeadRendererSigTerm'):
        result += expression(signature, prepare_source) + '\n'
    for signature in ('        private static ulong FoldSig(',
                      '        private void FoldSceneFact(', '        private void FoldNarrowSceneFact(',
                      '        private void FoldFigureSetFact('):
        result += member(signature, prepare_source) + '\n'
    result += expression('        private bool SceneRowWasExemptWhenAlive(', rows) + '\n'
    result += member('        private void RecordSceneFactRow(', rows) + '\n'
    for signature in ('        private const byte SigRowExempt', '        private const byte SigRowFigure'):
        result += expression(signature, rows) + '\n'
    result += '}\n}\n'
    return result


if '--ownership-only' in sys.argv:
    text = ownership_source()
    seam = '\n}\n}\n'
    pos = text.rindex(seam)
    read_facts = pathlib.Path(sys.argv[1]).parent.joinpath('WallSegmentFade.ReadFacts.cs').read_text()
    helpers = expression('        private static Material FadeSourceMaterial(', read_facts) + '\n'
    helpers += member('        private static void ReadFadeMaterials(', read_facts) + '\n'
    text = text[:pos] + helpers + member('        private static void BeginFigureMemo()') + '\n' + member('        private static void EndFigureMemo()') + text[pos:]
    pathlib.Path(sys.argv[2]).write_text(text)
    sys.exit(0)

text = '''#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
namespace GloomhavenVR.Core;
internal static partial class WallSegmentFade
{
    private sealed partial class FadeDriver
    {
'''
for signature in ('        private readonly struct ShaderFadeName',
                  '        private ShaderFadeName FadeNameOf(',
                  '        private static bool HasLiveWallFadeToggle(',
                  '        private static void BeginFigureMemo()',
                  '        private static void EndFigureMemo()',
                  '        private bool CollectWallFadeInfo(MeshRenderer r, Segment seg, bool figureArmOnly = false)',
                  '        private bool CollectWallFadeInfo(MeshRenderer r, Segment seg, bool figureArmOnly,'):
    text += member(signature) + '\n'
text += member('        private static bool IsActuallyDrawing(Renderer r)', mounted_source) + '\n'
text += member('        private void VerifyPrepareStillValid(TilesOcclusionGenerator gen)', prepare_source) + '\n'
text += '    }\n}\n'
pathlib.Path(sys.argv[2]).with_name('OwnershipReads.g.cs').write_text(ownership_source())
pathlib.Path(sys.argv[2]).write_text(text)
