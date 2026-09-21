#!/usr/bin/env python3
"""Run actual production floor geometry, with mutations proving the tracking-floor regression."""
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "src/GloomhavenVR/WorldUI/TownServices/TownServicePlacement.cs"

def main():
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    env = dict(os.environ, DOTNET_ROOT=str(Path(dotnet).resolve().parent))
    source = SOURCE.read_text()
    with tempfile.TemporaryDirectory(prefix="ghvr-setting-") as scratch:
        folder = Path(scratch)
        (folder / "Test.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup></Project>')
        shutil.copyfile(ROOT / "scripts/town-service-setting-runtime/Program.cs", folder / "Program.cs")
        variants = [source,
            source.replace('position.y = room != null ? room.position.y : seat.FloorPosition.y;', 'position.y = seat.FloorPosition.y;').replace('if (room != null) position.y = GroundHeight(room, position);', ''),
            source.replace('if (room != null) position.y = GroundHeight(room, position);', '')]
        for index, variant in enumerate(variants):
            (folder / "Placement.cs").write_text(variant)
            run = subprocess.run([dotnet, "run", "--project", str(folder / "Test.csproj"), "-c", "Release"], env=env, capture_output=True, text=True)
            if index == 0:
                print(run.stdout, end="")
                if run.returncode: raise SystemExit(run.stdout + run.stderr)
            elif run.returncode == 0 or "error CS" in run.stdout:
                raise SystemExit("Negative control did not fail at runtime: " + str(index) + run.stdout + run.stderr)
        print("Town setting: 2 compiled negative controls passed")

if __name__ == "__main__": main()
