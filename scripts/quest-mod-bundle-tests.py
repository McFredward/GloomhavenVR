#!/usr/bin/env python3
"""Exercise the real authored Android bundle recipe with controlled Unity host APIs."""
from pathlib import Path
import shutil
import subprocess
import sys

root = Path(__file__).resolve().parents[1]
dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
raise SystemExit(subprocess.run([dotnet, "run", "--project", str(root / "tests/QuestModBundles.Tests/QuestModBundles.Tests.csproj"),
                                "--configuration", "Release"], cwd=root).returncode)
