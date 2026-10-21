#!/usr/bin/env python3
"""Execute the production Quest menu-anchor resolver and target-identity predicates.

Unity camera enumeration is a managed seam, not a headset/rendering simulation.
The native compile/picture remains a separate SDK and hardware gate.
"""
import argparse
import hashlib
import os
import signal
import sys
import json
from pathlib import Path
import re
import shutil
import subprocess
import tempfile

sys.dont_write_bytecode = True


def method(text, signature):
    start = text.index(signature)
    opening = text.index('{', start)
    end, depth = opening + 1, 1
    while depth:
        depth += (text[end] == '{') - (text[end] == '}')
        end += 1
    return text[start:end]


def main():
    root = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=root)
    parser.add_argument('--output', type=Path, default=root / '.planning/debug/quest-menu-presentation')
    args = parser.parse_args()
    source = args.source_root.resolve(strict=True)
    sys.path.insert(0, str(root / 'scripts'))
    from town_run_retention import cleanup_completed_run
    flat = source / "src/GloomhavenVR/WorldUI/FlatScreen"
    paths = {
        "VRRigDriver.cs": source / "src/GloomhavenVR/Rig/VRRigDriver.cs",
        "FlatScreen.NativeTargets.cs": flat / "FlatScreen.NativeTargets.cs",
        "desktop": flat / "FlatScreen.3.Desktop.cs",
        "stack": flat / "FlatScreen.2.CameraStack.cs",
        "state": flat / "FlatScreen.1.Core.cs",
    }
    original = {name: path.read_text() for name, path in paths.items()}
    target = re.search(r"    private RenderTexture\? TargetFor\(CapturedCamera c\) =>\s*\n        .*?;", original["stack"])
    assert target, "production captured target method drift"
    assert original["stack"].count("                Owner = this,") == 1
    assert "internal FlatScreen Owner = null!;" in original["state"]
    publication = "            DesktopScrubTarget = _scrubRt;"
    release = "            if (DesktopScrubTarget == _scrubRt)\n                DesktopScrubTarget = null;"
    desktop = original["desktop"]
    assert desktop.count(publication) == desktop.count(release) == 1
    assert desktop.index("_scrubRt.Create();") < desktop.index(publication)
    assert desktop.index(release) < desktop.index("_scrubRt.Release();")
    rig = original["VRRigDriver.cs"]
    live_start = rig.index('    private static bool IsLiveMenuAnchor(')
    live = rig[live_start:rig.index(';', live_start) + 1]
    assert rig.count('private static Camera? ResolveMenuCamera(') == 1
    sources = {"FlatScreen.NativeTargets.cs": original["FlatScreen.NativeTargets.cs"]}
    sources["VRRigDriver.MenuCamera.cs"] = "using GloomhavenVR.Core;\nusing UnityEngine;\nnamespace GloomhavenVR.Rig;\ninternal sealed partial class VRRigDriver {\n" + live + "\n" + method(rig, '    private static Camera? ResolveMenuCamera(') + "\n}\n"
    sources["CapturedTarget.cs"] = "using UnityEngine;\nnamespace GloomhavenVR.WorldUI;\ninternal sealed partial class FlatScreen\n{\n" + target.group() + "\n}\n"
    sources["ScrubTarget.cs"] = "namespace GloomhavenVR.WorldUI;\ninternal sealed partial class FlatScreen\n{\nprivate void PublishScrub() {\n" + publication + "\n}\nprivate void DropScrub() {\n" + release + "\n}\n}\n"
    controls = [
        ("sink-not-published", "ScrubTarget.cs", publication, "", "scrub target publication"),
        ("sink-not-released", "ScrubTarget.cs", release, "", "scrub target release"),
        ("capture-membership-only", "FlatScreen.NativeTargets.cs", "target != null && camera.targetTexture == target", "target != null", "capture membership cannot grant a foreign target"),
        ("target-name-spoof", "FlatScreen.NativeTargets.cs", "camera.targetTexture == DesktopScrubTarget", "camera.targetTexture != null && camera.targetTexture.name == DesktopScrubTarget.name", "texture names cannot grant ownership"),
        ("quest-owned-target-rejected", "VRRigDriver.MenuCamera.cs", "(!quest || !WorldUI.FlatScreen.OwnsPresentationTarget(candidate))", "true", "scrubbed original UICamera recovers menu anchor"),
        ("desktop-owned-target-admitted", "VRRigDriver.MenuCamera.cs", "(!quest || !WorldUI.FlatScreen.OwnsPresentationTarget(candidate))", "!WorldUI.FlatScreen.OwnsPresentationTarget(candidate)", "desktop owned RT eligibility unchanged"),
        ("ui-over-world-anchor", "VRRigDriver.MenuCamera.cs", "return best != null ? best : ui;", "return ui != null ? ui : best;", "world anchor priority over UI"),
        ("main-self-anchor", "VRRigDriver.MenuCamera.cs", "if (IsLiveMenuAnchor(cam))", "if (cam != null)", "Quest main camera cannot self-anchor"),
    ]
    output = args.output
    output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=output))
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    proof = {"source_root": str(source), "sources": {name: hashlib.sha256(path.read_bytes()).hexdigest() for name, path in paths.items()},
             "limit": "Managed camera seams, no Unity process or headset rendering claim.", "cases": []}
    print("Quest original menu camera proof: " + str(run), flush=True)
    fixture_source = root / "tests/QuestMenuPresentation.Tests"
    if not (fixture_source / "Program.cs").is_file():
        fixture_source = source / "tests/QuestMenuPresentation.Tests"
    for name, filename, before, after, expected in [("production", "", "", "", "")] + controls:
        case = run / name
        case.mkdir()
        for filename_source, value in sources.items():
            if filename_source == filename:
                assert value.count(before) == 1, "mutation binding drift: " + name
                value = value.replace(before, after, 1)
            (case / filename_source).write_text(value)
        fixture = case / "fixture"
        shutil.copytree(fixture_source, fixture,
                        ignore=shutil.ignore_patterns("bin", "obj"))
        # A dev-based worker may borrow the unchanged Quest fixture program/project;
        # compile the owned updated host boundary from this worktree in either case.
        shutil.copyfile(root / "tests/QuestMenuPresentation.Tests/PresentationStubs.cs", fixture / "PresentationStubs.cs")
        project = fixture / "QuestMenuPresentation.Tests.csproj"
        completion = case / "completion.log"
        completion.write_text("Evidence: " + str(run) + "\n")
        command = [dotnet, "run", "--project", str(project), "-c", "Release",
                   "-p:RuntimeSource=" + str(case), "-p:UseSharedCompilation=false"]
        process = subprocess.Popen(command, text=True, stdout=subprocess.PIPE,
                                   stderr=subprocess.STDOUT, start_new_session=True)
        case_code = 1
        try:
            try:
                console, _ = process.communicate(timeout=120)
            except BaseException:
                if process.poll() is None: os.killpg(process.pid, signal.SIGKILL)
                console, _ = process.communicate()
                (case / "console.log").write_text(console)
                raise
            result = subprocess.CompletedProcess(command, process.returncode, console)
            (case / "console.log").write_text(result.stdout)
            case_code = result.returncode
        finally:
            receipt = cleanup_completed_run('quest-menu-presentation', case_code, completion, root, process_reaped=True)
            (case / "cleanup.json").write_text(json.dumps(receipt, indent=2) + "\n")
        compiled = "error CS" not in result.stdout and "error MSB" not in result.stdout
        passed = compiled and ((result.returncode == 0 and "PASS Quest original menu camera ownership:" in result.stdout)
                               if not expected else (result.returncode != 0 and "FAIL " + expected in result.stdout))
        proof["cases"].append({"name": name, "exitCode": result.returncode, "expected": expected, "passed": passed})
        (run / "proof.json").write_text(json.dumps(proof, indent=2) + "\n")
        if not passed:
            raise SystemExit("FAIL " + name + "; see " + str(case / "console.log"))
        print(next(line for line in result.stdout.splitlines() if line.startswith("PASS ")) if not expected
              else "PASS rejected " + name + " at " + expected, flush=True)
    print("PASS production Quest menu resolver + 8 defect controls", flush=True)


if __name__ == "__main__":
    main()
