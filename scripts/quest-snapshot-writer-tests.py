#!/usr/bin/env python3
"""Exercise real snapshot threads/files and causal coalescing/error controls."""
import json
from pathlib import Path
import shutil
import subprocess
import tempfile

root = Path(__file__).resolve().parents[1]
runtime = root / "unity/GloomhavenVR.Quest/Assets/Quest/Runtime/QuestSnapshotWriter.cs"
source = runtime.read_text()
parent = root / ".planning/debug/quest-snapshot-writer"
parent.mkdir(parents=True, exist_ok=True)
run = Path(tempfile.mkdtemp(prefix="run-", dir=parent))
print("Snapshot writer evidence: " + str(run), flush=True)
cases = (
    ("production", "", "", ""),
    ("stale-pending", "pending = json;", 'pending = running ? "stale" : json;', "latest pending"),
    ("lost-io-error", "if (error != null && failure == null)", "if (error != null && failure != null)", "error returned"),
)
results = []
for name, before, after, expected in cases:
    folder = run / name
    shutil.copytree(root / "tests/QuestSnapshotWriter.Tests", folder, ignore=shutil.ignore_patterns("bin", "obj"))
    if before:
        assert source.count(before) == 1
    (folder / "QuestSnapshotWriter.cs").write_text(source.replace(before, after) if before else source)
    result = subprocess.run([str(Path.home() / ".dotnet/dotnet"), "run", "--project", str(folder), "-c", "Release",
        "-p:UseSharedCompilation=false", "--", str(folder / "files")], capture_output=True, text=True, timeout=90)
    text = result.stdout + result.stderr
    (folder / "console.log").write_text(text)
    passed = result.returncode == 0 if not expected else result.returncode != 0 and expected in text
    results.append({"name": name, "exitCode": result.returncode, "expected": expected, "passed": passed})
    (run / "results.json").write_text(json.dumps(results, indent=2) + "\n")
    if not passed:
        raise SystemExit("FAIL " + name + ": " + text[-2000:])
    print(text.strip() if not expected else "PASS rejected " + name + " at " + expected, flush=True)
