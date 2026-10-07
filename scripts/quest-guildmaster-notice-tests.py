#!/usr/bin/env python3
"""Verify restored Guildmaster native UI ownership in the complete Quest target.

The former rejection-notice fixture is retired by the2026-10-06 scope change.
Original mode/admission IL preservation is checked separately by QuestWeaver.
"""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile


def main():
    root = Path(__file__).resolve().parents[1]
    runtime = root / "unity/GloomhavenVR.Quest/Assets/Quest/Runtime"
    sources = {name: (runtime / name).read_text() for name in
               ("QuestGameScope.cs", "QuestSceneObjects.cs", "QuestScopeObjects.cs")}
    sources["QuestText.cs"] = (root / "src/GloomhavenVR/Core/Loc/QuestText.cs").read_text()
    output = root / ".planning/debug/quest-guildmaster-scope"
    output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=output))
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    proof = {"sources": {name: hashlib.sha256(value.encode()).hexdigest() for name, value in sources.items()},
             "limit": "Actual scope with Unity component seams; no native engine/headset claim.", "cases": []}
    for name, defect in (("production", False), ("guildmaster-exclusion-restored", True)):
        case = run / name
        case.mkdir()
        for filename, source in sources.items():
            if defect and filename == "QuestGameScope.cs":
                assert source.count("#if !GHVR_QUEST_GAME") == 1
                source = source.replace("#if !GHVR_QUEST_GAME", "#if GHVR_QUEST_GAME")
            (case / filename).write_text(source)
        fixture = case / "fixture"
        shutil.copytree(root / "tests/QuestGameScope.Tests", fixture, ignore=shutil.ignore_patterns("bin", "obj"))
        result = subprocess.run([dotnet, "run", "--project", str(fixture / "QuestGameScope.Tests.csproj"),
                                 "-c", "Release", "-p:QuestFullGame=true", "-p:RuntimeSource=" + str(case)],
                                text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=120)
        (case / "console.log").write_text(result.stdout)
        compiled = "error CS" not in result.stdout and "error MSB" not in result.stdout
        passed = compiled and (result.returncode != 0 and "FAIL full game Guildmaster native ownership retained" in result.stdout
                               if defect else result.returncode == 0 and "PASS Quest native purchase scope:" in result.stdout)
        proof["cases"].append({"name": name, "exitCode": result.returncode, "passed": passed})
        (run / "proof.json").write_text(json.dumps(proof, indent=2) + "\n")
        if not passed:
            raise SystemExit("FAIL " + name + "; see " + str(case / "console.log"))
        print("PASS " + name, flush=True)
    print("PASS restored Guildmaster scope and executable exclusion defect; evidence " + str(run))


if __name__ == "__main__":
    main()
