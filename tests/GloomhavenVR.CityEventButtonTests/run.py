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

# Bind the actual per-frame replacement expression, including the city cap's absent
# mode button. The native service predicate is the fixture boundary; evaluating
# that predicate for a non-mode cap was the build-547 repeating map tick exception.
rail = (ROOT / "src/GloomhavenVR/WorldUI/MapRoom/MapButtonRail.cs").read_text()
import re
expression = re.search(r"bool replaced = ([^;]+);", rail)
assert expression is not None and len(re.findall(r"bool replaced = ", rail)) == 1
with tempfile.TemporaryDirectory(prefix="ghvr-city-cap-") as folder:
    stage = Path(folder)
    (stage / "Cap.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><OutputType>Exe</OutputType><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup></Project>')
    fixture = r"""
using System;
sealed class Button { public int GuildmasterMode; }
sealed class Cap { public Button? Button; }
static class TownServiceVisitTarget {
    public static int Calls;
    public static bool Enabled;
    public static bool Replaces(int mode) { Calls++; return Enabled && mode == 1; }
}
static class Program {
    static bool Sample(Cap c) => EXPRESSION;
    static void Main() {
        var city = new Cap();
        foreach (bool enabled in new[] {false, true}) {
            TownServiceVisitTarget.Enabled = enabled;
            for (int i=0; i<1000; i++) {
                if (Sample(city)) throw new Exception("City event must never be replaced by a resident");
                if (Sample(new Cap {Button=new Button {GuildmasterMode=1}}) != enabled) throw new Exception("Service substitution must follow setting");
                if (Sample(new Cap {Button=new Button {GuildmasterMode=2}})) throw new Exception("Unrelated cap remains native");
            }
        }
        if (TownServiceVisitTarget.Calls != 4000) throw new Exception("Only mode caps query service replacement");
        Console.WriteLine("City cap per-frame sampling: 6001 assertions passed.");
    }
}
"""
    original_expression = expression.group(1)
    for name, code in (("production", original_expression), ("missing-mode", original_expression.replace("c.Button != null && ", "").replace("c.Button.GuildmasterMode", "c.Button!.GuildmasterMode"))):
        (stage / "Program.cs").write_text(fixture.replace("EXPRESSION", code))
        result = subprocess.run([dotnet, "run", "--project", str(stage / "Cap.csproj"), "-c", "Release"], capture_output=True, text=True)
        if name == "production":
            assert result.returncode == 0, (result.stdout,result.stderr)
            print(result.stdout, end="")
        else:
            assert result.returncode != 0 and "NullReferenceException" in result.stderr, (result.stdout,result.stderr)
            print("City cap negative control: absent native mode rejected.")
