#!/usr/bin/env python3
"""Execute the production catalog adapter, keeping asset loading with the game."""
from pathlib import Path
import shutil
import subprocess
import tempfile

root = Path(__file__).resolve().parents[1]
source = root / "unity/GloomhavenVR.Quest/Assets/Quest/Runtime/QuestGameAddressables.cs"
dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
text = source.read_text()
seam = "int count = locations.Result.Count;"
assert text.count(seam) == 1
with tempfile.TemporaryDirectory(prefix="quest-catalog-") as temporary:
    work = Path(temporary)
    fixture = work / "fixture"
    shutil.copytree(root / "tests/QuestAddressables.Tests", fixture, ignore=shutil.ignore_patterns("bin", "obj"))
    for name, code, expected in (("production", text, ""),
            ("duplicate-preload", text.replace(seam, seam + "\n                Addressables.LoadAssetsAsync<UnityEngine.Object>(label, null);"), "original-asset-load-owner")):
        runtime = work / name
        runtime.mkdir()
        (runtime / source.name).write_text(code)
        result = subprocess.run([dotnet, "run", "--project", str(fixture), "-c", "Release",
            "--property:RuntimeSource=" + str(runtime)], stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, timeout=90)
        if expected:
            assert result.returncode != 0 and expected in result.stdout, result.stdout
            print("PASS rejected duplicate native asset preload")
        else:
            assert result.returncode == 0 and "PASS Quest catalog:" in result.stdout, result.stdout
            print(result.stdout.strip())
