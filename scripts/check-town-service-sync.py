#!/usr/bin/env python3
"""Run the production town-service codec and cumulative-delta loss tests."""
from pathlib import Path
import os
import shutil
import subprocess

root = Path(__file__).resolve().parents[1]
dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
subprocess.run([dotnet, "run", "--project", str(root / "tests/GloomhavenVR.TownServiceTests"), "--configuration", "Release"], cwd=root, env=os.environ.copy(), check=True)
