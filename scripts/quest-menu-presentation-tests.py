#!/usr/bin/env python3
"""Execute the production Quest menu-anchor resolver and target-identity predicates.

Unity camera enumeration is a managed seam, not a headset/rendering simulation.
The native compile/picture remains a separate SDK and hardware gate.
"""
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import tempfile


def main():
    root = Path(__file__).resolve().parents[1]
    flat = root / "src/GloomhavenVR/WorldUI/FlatScreen"
    paths = {
        "VRRigDriver.MenuCamera.cs": root / "src/GloomhavenVR/Rig/VRRigDriver.MenuCamera.cs",
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
    sources = {name: original[name] for name in ("VRRigDriver.MenuCamera.cs", "FlatScreen.NativeTargets.cs")}
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
        ("main-self-anchor", "VRRigDriver.MenuCamera.cs", "cam != null && (!quest || cam != HeadCamera)", "cam != null", "Quest main camera cannot self-anchor"),
    ]
    output = root / ".planning/debug/quest-menu-presentation"
    output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=output))
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    proof = {"sources": {name: hashlib.sha256(path.read_bytes()).hexdigest() for name, path in paths.items()},
             "limit": "Managed camera seams, no Unity process or headset rendering claim.", "cases": []}
    print("Quest original menu camera proof: " + str(run), flush=True)
    for name, filename, before, after, expected in [("production", "", "", "", "")] + controls:
        case = run / name
        case.mkdir()
        for filename_source, value in sources.items():
            if filename_source == filename:
                assert value.count(before) == 1, "mutation binding drift: " + name
                value = value.replace(before, after, 1)
            (case / filename_source).write_text(value)
        fixture = case / "fixture"
        shutil.copytree(root / "tests/QuestMenuPresentation.Tests", fixture,
                        ignore=shutil.ignore_patterns("bin", "obj"))
        project = fixture / "QuestMenuPresentation.Tests.csproj"
        result = subprocess.run([dotnet, "run", "--project", str(project), "-c", "Release",
                                 "-p:RuntimeSource=" + str(case)], text=True,
                                stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=120)
        (case / "console.log").write_text(result.stdout)
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
