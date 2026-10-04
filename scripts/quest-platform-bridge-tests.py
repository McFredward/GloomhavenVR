#!/usr/bin/env python3
"""Test the actual standalone Platforms DLL and null-only IL guard.

Requires the player's original managed assemblies. Only selected pure managed
platform methods run in a separate CLR fixture; no Unity process/device is used.
All adapted DLLs and mutation controls remain in this worktree's ignored debug.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile


def main():
    root = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--managed", type=Path, default=root / "ressources/GH_Data/Managed")
    args = parser.parse_args()
    managed = args.managed.resolve(strict=True)
    names = ("GH.Runtime.dll", "SM.Consoles.dll", "Apparance.Unity.dll", "ScenarioRuleLibrary.dll", "Unity.InputSystem.dll")
    for name in names:
        if not (managed / name).is_file():
            raise SystemExit("Original managed input missing: " + name)
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    output = root / ".planning/debug/quest-platform-bridge"
    output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=output))
    standalone = (root / "tools/QuestWeaver/Standalone.cs").read_text()
    mutations = (
        ("missing-null-guard", "        GuardMissingPlatformUser(removeUser);", "        // mutation: absent null guard", "adapted-null-safe-without-list-or-event-change"),
        ("unconditional-return", "il.InsertBefore(first, il.Create(OpCodes.Brtrue, first));", "il.InsertBefore(first, il.Create(OpCodes.Pop));", "non-null-original-failure-preserved"),
        ("inverted-null-branch", "il.InsertBefore(first, il.Create(OpCodes.Brtrue, first));", "il.InsertBefore(first, il.Create(OpCodes.Brfalse, first));", "adapted-null-safe-without-list-or-event-change"),
        ("static-abi-bypass", "method.IsStatic || !method.HasThis || method.ExplicitThis || method.HasGenericParameters", "method.ExplicitThis || method.HasGenericParameters", "reject-static-abi"),
        ("user-list-seam-bypass", "if (userLists.Length != 1 || !method.Body.Instructions.Any(instruction =>", "if (!method.Body.Instructions.Any(instruction =>", "reject-user-list-seam"),
        ("input-user-seam-bypass", "if (userLists.Length != 1 || !method.Body.Instructions.Any(instruction =>", "if (userLists.Length != 1 || method.Body.Instructions.Count < 0 && !method.Body.Instructions.Any(instruction =>", "reject-input-user-call-seam"),
    )
    env = dict(os.environ)
    env["DOTNET_ROOT"] = str(Path(dotnet).resolve().parent)
    results = []
    for name, before, after, expected in [("production", "", "", "")] + list(mutations):
        case = run / name
        case.mkdir()
        if expected:
            if standalone.count(before) != 1:
                raise RuntimeError("Mutation no longer addresses exactly one production seam: " + name)
            shutil.copytree(root / "tools/QuestWeaver", case / "tools/QuestWeaver", ignore=shutil.ignore_patterns("bin", "obj"))
            shutil.copytree(root / "tests/QuestPlatformBridge.Tests", case / "tests/QuestPlatformBridge.Tests", ignore=shutil.ignore_patterns("bin", "obj"))
            (case / "tools/QuestWeaver/Standalone.cs").write_text(standalone.replace(before, after, 1))
            project = case / "tests/QuestPlatformBridge.Tests/QuestPlatformBridge.Tests.csproj"
        else:
            project = root / "tests/QuestPlatformBridge.Tests/QuestPlatformBridge.Tests.csproj"
        command = [dotnet, "run", "--project", str(project), "--configuration", "Release", "--", str(managed), str(case / "proof")]
        completed = subprocess.run(command, cwd=root, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
        (case / "output.log").write_text(completed.stdout)
        if not expected:
            if completed.returncode != 0:
                raise RuntimeError("Production fixture failed: " + completed.stdout)
            proof = json.loads((case / "proof/proof.json").read_text())
            results.append({"case": name, "assertions": proof["assertions"], "protectedTypes": proof["ProtectedTypesVerified"], "unrelatedTypes": proof["UnchangedTypesVerified"]})
            print(completed.stdout.strip(), flush=True)
        else:
            if completed.returncode == 0 or expected not in completed.stdout or "error CS" in completed.stdout:
                raise RuntimeError("Mutation was not detected by the expected runtime/ABI assertion: " + name + "\n" + completed.stdout)
            results.append({"case": name, "detectedBy": expected})
            print("Detected mutation: " + name, flush=True)
    summary = {"sourceSha256": hashlib.sha256(standalone.encode()).hexdigest(), "results": results,
               "boundary": "Original CIL and targeted CLR behavior; no hardware outcome is established."}
    (run / "results.json").write_text(json.dumps(summary, indent=2) + "\n")
    print("Quest platform bridge: production plus " + str(len(mutations)) + " mutation controls passed.")
    print("Proof: " + str(run / "results.json"))


if __name__ == "__main__":
    main()
