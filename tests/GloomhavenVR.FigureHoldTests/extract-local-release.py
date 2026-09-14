#!/usr/bin/env python3
"""Compile real local release decisions; verify their existing restore integration separately."""
import pathlib
import re
import sys
source = pathlib.Path(sys.argv[1]).read_text()

def member(signature):
    start = source.index(signature)
    opening = source.index('{', start)
    depth = 1
    index = opening + 1
    while depth:
        if source[index] == '{':
            depth += 1
        elif source[index] == '}':
            depth -= 1
        index += 1
    return source[start:index]

# The production Restore body is large native cleanup. Its pose/ownership writes must remain
# attached to the local decisions executed below; comments cannot satisfy these checks.
restore = member('    internal void Restore()')
restore = re.sub(r'//[^\n]*|/\*.*?\*/', '', restore, flags=re.S)
for seam in ('FinishGlide(', 't.SetParent(parent, worldPositionStays: false)',
             't.localPosition = _origLocalPos', 't.localRotation = _origLocalRot',
             't.localScale = _origLocalScale', 'HeldFigures.Remove(_actor)'):
    assert seam in restore, 'Local restore integration missing: ' + seam
text = '''#nullable enable
using UnityEngine;
using GloomhavenVR.Hands;
using GloomhavenVR.Core;
namespace GloomhavenVR.Board.FigureGrab;
public sealed partial class FigureGrabbable
{
'''
for signature in ('    public void OnRelease(', '    internal void AutoReleaseToBoard(',
                  '    internal static void ReleaseForNativeAction(', '    private void TickGlide()'):
    text += member(signature) + '\n'
text += '}\n'
source = pathlib.Path(sys.argv[3]).read_text()
text += 'public static partial class FigureBusy {\n'
start = source.index('    private static readonly string[] IdleStateNames')
text += source[start:source.index(';', start) + 1] + '\n'
for signature in ('    internal static bool IsIdleClip(', '    private static bool FigureItselfBusy(',
                  '    internal static bool HoldMustEnd(ActorBehaviour? actor, out string why)'):
    text += member(signature) + '\n'
text += '}\n'
source = pathlib.Path(sys.argv[4]).read_text()
text += 'public static partial class FigureGhosts {\n'
for signature in ('    internal static void ReleaseIfUnheld(', '    private static void Destroy('):
    text += member(signature) + '\n'
text += '}\n'
pathlib.Path(sys.argv[2]).write_text(text)
