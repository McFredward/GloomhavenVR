"""Compile the actual selection observer and deselection methods, never a policy copy."""
from pathlib import Path
import re
import sys


def method(source, signature):
    start = source.index(signature)
    # Mask comments and strings before counting braces (source retains exact method text).
    mask = re.sub(r'//[^\n]*|/\*.*?\*/|@?"(?:""|\\.|[^"\\])*"',
                  lambda m: ' ' * len(m.group()), source, flags=re.S)
    at = mask.index('{', start)
    depth = 1
    end = at + 1
    while depth:
        depth += (mask[end] == '{') - (mask[end] == '}')
        end += 1
    return source[start:end]


source = Path(sys.argv[1]).read_text()
methods = ('private void TickQuestDecision()', 'private void PublishDecision(',
           'internal static bool QuestSelectionCleared(', 'internal static bool TryGetPublishedDecision(',
           'private static string? DecisionIdOf(', 'private void TickAdoptGameSelection()',
           'private void TickDeselect()', 'private void Deselect(', 'private bool QuestPopupOpen()',
           'internal bool AdoptSelection(', 'internal bool PrepareSharedSelectionChange(',
           'internal static bool HandleSharedQuestClose(')
fields = ('_selected', '_selectedAt', '_selectedDecisionId', '_decisionId', '_decisionEver',
          '_decisionNullSince', '_publishedDecisionId', '_publishedDecisionEver', '_publishedBySettle',
          '_publishedAt', '_lastDispatchFrame', '_adoptedFromGame', '_dispatchedHere',
          'DecisionSettleSeconds', 'SelectionGraceSeconds', '_manualSelectionRevision', '_acceptedManualSelectionRevision', 'Scope')
body = '\n'.join(method(source, signature) for signature in methods)
staged = '    internal MapLocation? Staged => _selected;'
assert source.count(staged) == 1, 'Production staged selection read seam changed'
body += '\n' + staged
revision = re.findall(r'^    internal int SelectionRevision => [^\n]+;', source, re.M)
assert len(revision) == 1, 'Production selection lifetime seam changed'
body += '\n' + revision[0]
input_revision = re.findall(r'^    internal int LocalSelectionInputRevision => [^\n]+;', source, re.M)
assert len(input_revision) == 1, 'Production explicit local input seam changed'
body += '\n' + input_revision[0]
publication_revision = re.findall(r'^    internal int SelectionPublicationRevision => [^\n]+;', source, re.M)
assert len(publication_revision) == 1, 'Production accepted publication seam changed'
body += '\n' + publication_revision[0]
dispatch = method(source, 'internal void Dispatch(')
assert 'if (MapInputGate.IsBlocked || NativeMapQuestSelection.BlocksSharedBrowsing)' in dispatch, 'Original VR click dispatcher must reject incidental native-proposal browsing'
close = Path(__file__).resolve().parents[2]/'src/GloomhavenVR/WorldUI/Modal/ModalFallback.7.Close.cs'
actual_close = method(close.read_text(), 'internal static void CloseFloatedWindow(')
assert actual_close.index('MapRoom.MapLocationInteractor.HandleSharedQuestClose(window)') < actual_close.index('wp.UserClosing = true;'), 'Shared quest X must resolve native admission before any local release flag'
for field in fields:
    matches = re.findall(r'^    private (?:static |const |readonly )*[^\n;]+\b' + field + r'\b[^\n]*;', source, re.M)
    assert len(matches) == 1, 'Production field seam changed: ' + field
    body += '\n' + matches[0]
Path(sys.argv[2]).parent.mkdir(parents=True, exist_ok=True)
Path(sys.argv[2]).write_text('''#nullable enable
using System;
using UnityEngine;
using UnityEngine.UI;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.WorldUI;
namespace GloomhavenVR.WorldUI.MapRoom
{
internal sealed partial class MapLocationInteractor
{
''' + body + '\n}\n}\n')

remote = Path(sys.argv[3]).read_text()
methods = ('private static void ResolveSelection()', 'private static void ClearPendingSelection()',
           'private static void ResolvePendingSelection(', 'private static void ApplySelection(',
           'private static uint KeyOf(', 'private static void Forget(', 'private static void RequestSelection(',
           'private static bool EnsureVersionVisit()', 'private static void RetireVersionVisit()',
           'private static void ResolveVersionedSelection(', 'private static void SuspendOrRetireVersionVisit()',
           'private static object? VersionMapIdentity()',
           'internal static bool TryReadQuestSelectionOpening(', 'internal static void Sample(',
           'internal static void Observe(', 'internal static bool SendDue', 'private readonly struct PeerRoom')
fields = ('SeenSelectKey', '_pendingSelectInteractor', '_pendingSelectGeneration', '_pendingSelectRevision',
          '_pendingSelectSurface', '_pendingSelectKey', '_pendingSelectPriorLocation', '_pendingSelectPeer',
          'SeenSelectStamp', 'ConsumedSelectStamp', '_adoptedSelectKey', '_hasAdoptedSelect',
          '_adoptedSelectAt', 'Scope', '_pendingSelectVersioned', '_pendingSelectToken', 'SelectionVersions',
          '_versionVisitLive', '_versionMapIdentity', '_versionLocalRevision', '_displayedSelectKey',
          '_versionInputInteractor', '_versionLocalInputRevision', '_versionLocalPublicationRevision',
          '_displayedSelectOpening',
          '_publishedSurface', '_localSurfaceStamp', '_adoptedSurface', '_publishedSelectKey',
          '_publishedSelectValid', '_localSelectStamp', '_sentValid', '_sentSelectKey', '_sentFanCharacterKey',
          'AdoptedSuppressSeconds')
body = '\n'.join(method(remote, signature) for signature in methods)
for field in fields:
    matches = re.findall(r'^    private (?:static |const |readonly )*[^\n;]+\b' + field + r'\b[^\n]*;', remote, re.M)
    assert len(matches) == 1, 'Production remote delivery field seam changed: ' + field
    body += '\n' + matches[0]
for signature in ('internal static void Reset()', 'internal static void Resolve()'):
    actual = method(remote, signature)
    assert 'ClearPendingSelection();' in actual, 'Production lifecycle does not retire pending map intent: ' + signature
with Path(sys.argv[2]).open('a') as output:
    output.write('''
namespace GloomhavenVR.Net
{
using System.Collections.Generic;
using GloomhavenVR.WorldUI.MapRoom;
internal static partial class RemoteMapRoom
{
''' + body + '\n}\n}\n')
