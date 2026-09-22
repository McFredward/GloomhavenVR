#!/usr/bin/env python3
"""Run the unchanged production city-control source and two executable negative controls."""
from pathlib import Path
import shutil
import os
import subprocess
import tempfile

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
source = ROOT / "src/GloomhavenVR/WorldUI/MapRoom/MapCityEventSource.cs"
project = HERE / "GloomhavenVR.CityEventButtonTests.csproj"
dotnet = shutil.which("dotnet") or str(Path(os.environ.get("DOTNET_ROOT", str(Path.home() / ".dotnet"))) / "dotnet")
subprocess.run([dotnet, "run", "--project", str(project), "-c", "Release"], check=True)
original = source.read_text()
with tempfile.TemporaryDirectory(prefix="ghvr-city-event-") as folder:
    stage = Path(folder)
    for file in HERE.glob("*.cs"):
        shutil.copy2(file, stage / file.name)
    shutil.copy2(project, stage / project.name)
    for name, before, after, expected in (
        ("native-lock", "return requests == null || requests.Count != 0;", "return requests == null;", "native options requests block city event"),
        ("party-size", "&& AdventureState.MapState.MapParty.SelectedCharacters.Count() > 1", "&& true", "native party-size condition 0"),
    ):
        assert original.count(before) == 1
        mutation = stage / "Production.fixture"
        mutation.write_text(original.replace(before, after))
        result = subprocess.run([dotnet, "run", "--project", str(stage / project.name), "-c", "Release",
                                 "--property:MapCitySource=" + str(mutation)], capture_output=True, text=True)
        assert result.returncode != 0 and "ASSERT: " + expected in result.stderr, (name, result.stdout, result.stderr)
        print(f"City event negative control: {name} rejected.")
