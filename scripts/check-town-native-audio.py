#!/usr/bin/env python3
"""Compile and exercise the exact immersive-town native audio suppression seams."""
from pathlib import Path
import shutil
import subprocess
import tempfile

root = Path(__file__).resolve().parents[1]
source = (root / "src/GloomhavenVR/WorldUI/Modal/ModalFallback.TownServices.cs").read_text()
marker = "internal static class TownServiceNativeAudioSilence"
section = source[source.index(marker):]
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
