#!/usr/bin/env python3
"""Execute production standalone archive validation without proprietary inputs."""
import os
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
env = dict(os.environ)
env["DOTNET_NOLOGO"] = "1"
result = subprocess.run([dotnet, "run", "--project", str(ROOT / "tests/QuestModContent.Tests"),
                         "--configuration", "Release"], cwd=ROOT, env=env)
sys.exit(result.returncode)
