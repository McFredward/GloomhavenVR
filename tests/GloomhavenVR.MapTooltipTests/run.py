#!/usr/bin/env python3
"""Exercise production tooltip parsing and paint setters with observable Unity boundaries."""
import os
from pathlib import Path
import re
import subprocess
import tempfile

HERE = Path(__file__).resolve().parent
ROOT = Path(os.environ.get("REPO_ROOT", HERE.parents[1])).resolve()
SOURCE = ROOT / "src/GloomhavenVR/WorldUI/MapRoom/MapButtonTooltipPresentation.cs"


def member(source, marker):
    start = source.index(marker)
    opening = source.index("{", start)
    end = opening + 1
    depth = 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[start:end]


source = SOURCE.read_text()
code = re.sub(r'//[^\n]*|/\*[\s\S]*?\*/', '', source)
surface = member(code, "    private sealed class Surface")
assert surface.index("_host.SetActive(false)") < surface.index("Object.Instantiate(")
assert surface.index("RemoteWidgetMirror.Neutralize(clone,") < surface.index("Show(true);")
assert "node.gameObject.layer = VRLayers.ModLayer" in surface
assert surface.index("Apply(_nodes[i],") < surface.index("ApplyRootPose(Root,")
assert "_host.SetActive(false); Object.Destroy(_host);" in surface
assert "Samples.Remove(Root)" in surface
print("Map tooltip presentation: six source integration bindings passed.")
parts = [member(source, marker) for marker in (
    "    private sealed class Picture", "    private sealed class Node",
    "    private sealed class Peer", "    internal static void Receive(",
    "    internal static void Tick(", "    private static bool Compatible(",
    "    private static void ApplyRootPose(", "    private static void Apply(",
    "    private static float ParentAlpha(",
    "    private static bool TryRead(byte[] bytes, out Picture[] pictures)")]
tail = source[source.index("    private static bool TextStyleValid("):source.index("    private static void Report(")]
fixture = """using System; using System.IO; using System.Collections.Generic; using UnityEngine; using UnityEngine.UI; using TMPro;
using GloomhavenVR.Net;
namespace GloomhavenVR.WorldUI.MapRoom;
internal static partial class MapButtonTooltipPresentation {
private static readonly Dictionary<int, Peer> Peers = new();
""" + "\n".join(re.findall(r'(?:internal|private) const int (?:MaxPayload|MaxNodes) = [^;]+;', source)) + "\n" + "\n".join(parts) + "\n" + tail + "\n}"

# Retain the actual native rectangle validator, rather than accepting bad geometry in a stub.
native = (ROOT / "src/GloomhavenVR/Net/NativeDecisionPromptState.cs").read_text()
validator = "using System; using System.Text; namespace GloomhavenVR.Net; internal static class NativeDecisionPromptState {\n"
validator += "internal static readonly UTF8Encoding Utf8 = new(false, true);\n"
validator += member(native, "    internal static bool Values(") + "\n"
validator += member(native, "    internal static bool RectValid(") + "\n}"

env = dict(os.environ)
env["PATH"] = str(Path(env.get("DOTNET_ROOT", Path.home() / ".dotnet"))) + os.pathsep + env["PATH"]
with tempfile.TemporaryDirectory(prefix="ghvr-map-tooltip-") as temp:
    out = Path(temp)
    (out / "Test.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup></Project>')
    for name in ("Standins.cs", "Vectors.cs"):
        (out / name).write_text((HERE / name).read_text())
    (out / "Validator.cs").write_text(validator)
    (out / "Clock.cs").write_text((ROOT / "src/GloomhavenVR/Net/UseBarAnimationPlaybackClock.cs").read_text())
    (out / "Production.cs").write_text(fixture)
    command = ["dotnet", "run", "--project", str(out / "Test.csproj"), "-c", "Release"]
    subprocess.run(command, env=env, cwd=ROOT, check=True)
    for name, before, after, expected in (
        ("nonfinite", "float.IsNaN(f) || float.IsInfinity(f) || ", "", "invalid root scale rejected"),
        ("alpha", "group.alpha = Mathf.Lerp(previous.Alpha, n.Alpha, progress)", "group.alpha = n.Alpha", "owner inherited alpha interpolates"),
        ("scale", "root.localScale = Vector3.Lerp(previous.Scale, p.Scale, progress) * mapScale;", "root.localScale = Vector3.Lerp(previous.Scale, p.Scale, progress) * mapScale * mapScale;", "root scale replaces viewer scale with owner sampled scale"),
        ("text-style", "t.fontWeight = (FontWeight)s[7];", "t.fontWeight = FontWeight.Regular;", "native style and alignment applied"),
        ("topology-cursor", "Picture[] shown = peer.Clock.Cursor >= to.Time ? to.Pictures : from.Pictures;", "Picture[] shown = peer.Pictures;", "new detail must not appear before the playback cursor reaches it"),
        ("clear-history", "if (peer.History.Count == 0)", "if (peer.History.Count == 0 || peer.Pictures.Length == 0)", "close and reopen burst retains pending clear history"),
    ):
        assert fixture.count(before) == 1, f"production mutation seam moved: {name}"
        (out / "Production.cs").write_text(fixture.replace(before, after))
        run = subprocess.run(command, env=env, cwd=ROOT, capture_output=True, text=True)
        assert run.returncode != 0 and f"Unhandled exception. System.Exception: {expected}" in run.stderr, (name, run.stdout, run.stderr)
        print(f"Map tooltip negative control: {name} rejected.")
