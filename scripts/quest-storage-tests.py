#!/usr/bin/env python3
"""Run focused actual local storage, provider evidence and snapshot controls."""
from pathlib import Path
import shutil
import subprocess
import sys
import unittest

ROOT=Path(__file__).resolve().parents[1]
suite=unittest.defaultTestLoader.discover(str(ROOT/"tests/quest-builder"),pattern="test_storage.py")
if not unittest.TextTestRunner(verbosity=1).run(suite).wasSuccessful():raise SystemExit(1)
dotnet=shutil.which("dotnet") or str(Path.home()/".dotnet/dotnet")
raise SystemExit(subprocess.run([dotnet,"run","--project",str(ROOT/"tests/QuestStorage.Tests/QuestStorage.Tests.csproj"),"--configuration","Release"],cwd=ROOT).returncode)
