#!/usr/bin/env python3
"""Compile the actual consumer and lifetime seams with counted Unity test doubles."""
import pathlib
import re
import sys
source = pathlib.Path(sys.argv[1]).read_text()
prepare_source = pathlib.Path(sys.argv[4]).read_text() if len(sys.argv) > 4 else pathlib.Path(__file__).parents[2].joinpath('src/GloomhavenVR/Core/WallFade/WallSegmentFade.Prepare.cs').read_text()
mounted_source = pathlib.Path(sys.argv[3]).read_text() if len(sys.argv) > 3 else pathlib.Path(__file__).parents[2].joinpath('src/GloomhavenVR/Core/WallFade/WallSegmentFade.Mounted.cs').read_text()
assert '|| IsNativeHexSelectionVisual(r);' in source, 'Native selection ownership must reach the production classifier'
assert 'bool modExempt = f.Mod && !(f.Mesh != null && f.WallFadeShader);' in source, 'Actual wall shader membership must retain its conservative signature even under UI ownership'


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
text = '''#nullable enable
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
pathlib.Path(sys.argv[2]).write_text(text)
