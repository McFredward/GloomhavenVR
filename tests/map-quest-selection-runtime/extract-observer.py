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
           'internal bool AdoptSelection(')
fields = ('_selected', '_selectedAt', '_selectedDecisionId', '_decisionId', '_decisionEver',
          '_decisionNullSince', '_publishedDecisionId', '_publishedDecisionEver', '_publishedBySettle',
          '_publishedAt', '_lastDispatchFrame', '_adoptedFromGame', '_dispatchedHere',
          'DecisionSettleSeconds', 'SelectionGraceSeconds', 'Scope')
body = '\n'.join(method(source, signature) for signature in methods)
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
namespace GloomhavenVR.WorldUI.MapRoom;
internal sealed partial class MapLocationInteractor
{
''' + body + '\n}\n')
