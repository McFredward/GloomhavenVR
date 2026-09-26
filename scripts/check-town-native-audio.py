#!/usr/bin/env python3
"""Compile and exercise the exact immersive-town native audio suppression seams.

The window-field fixture protects the first half of the path. The source contract below protects
the separate automatic mode-exit path which runs before UIWindow.Hide and otherwise dispatches an
ExtendedToggle pointer press (and therefore PlaySound_UIMapOpen). Both bindings are production
source, and the mutations prove that this checker rejects either regression.
"""
from pathlib import Path
import shutil
import subprocess
import tempfile

root = Path(__file__).resolve().parents[1]
source = (root / "src/GloomhavenVR/WorldUI/Modal/ModalFallback.TownServices.cs").read_text()
destinations = (root / "src/GloomhavenVR/WorldUI/MapRoom/GuildmasterDestinations.cs").read_text()
rail = (root / "src/GloomhavenVR/WorldUI/MapRoom/MapButtonRail.cs").read_text()
marker = "internal static class TownServiceNativeAudioSilence"
section = source[source.index(marker):]


def method(text: str, signature: str, next_signature: str) -> str:
    start = text.index(signature)
    end = text.index(next_signature, start)
    return text[start:end]


def validate_automatic_route(destination_source: str, rail_source: str) -> None:
    return_home = method(destination_source,
        "private static bool ReturnHome(string source, string what)",
        "private static EGuildmasterMode HomeMode")
    required = "PressGuildmasterMode(home, source, suppressNativeSound: true)"
    if required not in return_home:
        raise AssertionError("automatic resident exit does not request the no-pointer audio route")

    press = method(rail_source,
        "internal void Press(UIGuildmasterButton button, string source, bool physical = true,",
        "private void SelectThroughTheGamesOwnApi")
    silent = press.index("if (suppressNativeSound)")
    pointer = press.index("NativeUiPress.Press(target")
    if silent >= pointer:
        raise AssertionError("automatic audio gate occurs after the native pointer sound")
    silent_body = press[silent:pointer]
    if "SelectThroughTheGamesOwnApi(button, source, physical: false);" not in silent_body \
            or "return;" not in silent_body:
        raise AssertionError("automatic audio gate does not terminate through the native no-pointer selection")

    select = method(rail_source, "private void SelectThroughTheGamesOwnApi(",
        "private static string CapName")
    if "selectedToggle.SetIsOnWithoutNotify(false);" not in select \
            or "selectedToggle.isOn = true;" not in select:
        raise AssertionError("no-pointer selection no longer runs the real Toggle listeners")
    if "NativeUiPress.Press" in select:
        raise AssertionError("no-pointer selection unexpectedly dispatches pointer audio")


validate_automatic_route(destinations, rail)

# Effective negative controls: each is the exact defect this round is intended to prevent.
mutations = {
    "resident exit loses silent flag": destinations.replace(
        "PressGuildmasterMode(home, source, suppressNativeSound: true)",
        "PressGuildmasterMode(home, source, suppressNativeSound: false)", 1),
    "silent route falls through to pointer press": rail.replace(
        "SelectThroughTheGamesOwnApi(button, source, physical: false);\n            return;",
        "SelectThroughTheGamesOwnApi(button, source, physical: false);", 1),
}
for name, mutation in mutations.items():
    try:
        validate_automatic_route(mutation if "exit" in name else destinations,
            mutation if "route" in name else rail)
    except (AssertionError, ValueError):
        continue
    raise AssertionError("negative control survived: " + name)
print("PASS: automatic resident audio route bound; 2 negative controls rejected")

fixture = root / "scripts/town-native-audio-runtime"
out_root = root / ".planning/debug/town-native-audio"
out_root.mkdir(parents=True, exist_ok=True)
run = Path(tempfile.mkdtemp(prefix="run-", dir=out_root))
for name in ("TownNativeAudio.csproj", "Boundaries.cs", "Program.cs"):
    shutil.copyfile(fixture / name, run / name)
(run / "production.cs").write_text(
    "using System;\nusing System.Runtime.CompilerServices;\nusing GloomhavenVR.Core;\nusing HarmonyLib;\nusing UnityEngine;\n"
    "namespace GloomhavenVR.WorldUI {\n" + section + "\n}\n")
dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
result = subprocess.run([dotnet, "run", "--project", str(run / "TownNativeAudio.csproj"),
    "-c", "Release", "--nologo"], text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
print(result.stdout, end="")
print("Evidence:", run)
raise SystemExit(result.returncode)
