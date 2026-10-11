"""Bind the exact quiet request/open lifecycle into existing native handoff fixtures.

Native transaction preparation itself uses real-game reflection and is reviewed
against the decompiled native entry contracts. This boundary exercises the actual
Request/RequestedService/InteractionMode/IsOpen/EndRequest code, not a second mode
selector; Requests counts successful calls without changing any native destination.
"""
import hashlib


def method(source, signature):
    start = source.index("    " + signature)
    opening = source.index("{", start)
    depth, end = 1, opening + 1
    while depth:
        if source[end] == "{": depth += 1
        elif source[end] == "}": depth -= 1
        end += 1
    return source[start:end]


def sources(root):
    path = root / "src/GloomhavenVR/WorldUI/TownServices/TownServiceQuietController.cs"
    source = path.read_text()
    start = source.index("    internal static byte RequestedService")
    end = source.index("    internal static bool IsSourceBoundary", start)
    lifecycle = source[start:end]
    request = method(source, "internal static bool Request(").replace(
        "internal static bool Request(", "private static bool OriginalRequest(", 1)
    bound = """using GloomhavenVR.WorldUI.MapRoom; using GloomhavenVR.Net.TownServices;
namespace GloomhavenVR.WorldUI {
internal static class TownServiceQuietController {
    private static byte _requested;
    internal static int Requests;
    internal static bool Request(byte service) {
        bool accepted = OriginalRequest(service);
        if (accepted) Requests++;
        return accepted;
    }
""" + lifecycle + request + "\n} }\n"
    return {"QuietControllerFixture.cs": bound}, {
        "TownServiceQuietController.cs": hashlib.sha256(source.encode()).hexdigest(),
        "QuietControllerFixture.cs": hashlib.sha256(bound.encode()).hexdigest(),
    }
