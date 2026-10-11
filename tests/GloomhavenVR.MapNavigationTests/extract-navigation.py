#!/usr/bin/env python3
"""Execute complete production hover/teardown bodies against a navigation boundary."""
from pathlib import Path
import re
import sys


def method(source, signature):
    assert source.count(signature) == 1, f"Production signature changed: {signature}"
    start = source.index(signature)
    # Ignore prose/string braces while preserving every byte of the executed body.
    masked = re.sub(r'//[^\n]*|/\*.*?\*/|@?"(?:""|\\.|[^"\\])*"',
                    lambda match: " " * len(match.group()), source, flags=re.S)
    begin = masked.index("{", start)
    depth, end = 1, begin + 1
    while depth:
        depth += (masked[end] == "{") - (masked[end] == "}")
        end += 1
    return source[start:end]


source = Path(sys.argv[1]).read_text()
methods = [method(source, signature) for signature in (
    "    internal void SetHover(",
    "    internal void Release(",
    "    private static void StateMachineEnterHover(",
    "    private static void StateMachineEnterWorldMap(",
)]
# Preserve the complete production expression, including its unknown-state result.
signature = "    private static bool IsHoverNavigationState("
assert source.count(signature) == 1, "Production navigation eligibility seam changed"
start = source.index(signature)
methods.append(source[start:source.index(";", start) + 1])
output = """#nullable enable
using Code.State;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using Script.GUI.SMNavigation;
using Script.GUI.SMNavigation.States.CampaignMapStates;
using UnityEngine;
namespace GloomhavenVR.WorldUI.MapRoom;
internal sealed partial class MapLocationInteractor
{
""" + "\n".join(methods) + "\n}\n"
destination = Path(sys.argv[2])
destination.parent.mkdir(parents=True, exist_ok=True)
destination.write_text(output)
