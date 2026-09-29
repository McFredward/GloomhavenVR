#!/usr/bin/env python3
"""Run the actual merchant transaction code against native-window lifecycle failures in Unity."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile


def main():
    root = Path(__file__).resolve().parent.parent
    fixture = root / "scripts/town-merchant-transaction-runtime"
    source = (root / "src/GloomhavenVR/WorldUI/TownServices/TownServiceMerchantTransaction.cs").read_text()
    output = root / ".planning/debug/town-merchant-transaction"
    output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=output.resolve()))
    unity = Path(os.environ.get("UNITY_PATH", "/home/claw/unity-2021.3.5/Editor/Unity"))
    ui = root / "ressources/GH_Data/Managed/UnityEngine.UI.dll"
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    if not unity.is_file() or not ui.is_file():
        raise SystemExit("Unity 2021.3.5f1 and the real game UI assembly are required")

    stale_guard = ('if (!nativeWindow.IsOpen && !nativeWindow.IsVisible && confirmation.IsActive)\n'
                   '        {\n'
                   '            if (Core.VRLog.WantsDebug) Core.VRLog.Debug("TownServices", "Merchant native confirmation had a stale active wrapper after its window closed; reconciling before the next offer.");\n'
                   '            confirmation.Hide();\n'
                   '        }')
    fade_guard = "if (confirmation.IsActive || nativeWindow.IsOpen || nativeWindow.IsVisible) return false;"
    refused_guard = ('if (!created && confirmation.IsActive && !nativeWindow.IsOpen && !nativeWindow.IsVisible)\n'
                     '        {\n'
                     '            if (Core.VRLog.WantsDebug) Core.VRLog.Debug("TownServices", "Merchant native confirmation Show was refused after installing a callback; clearing its invisible active wrapper.");\n'
                     '            confirmation.Hide();\n'
                     '        }')
    for guard in (stale_guard, fade_guard, refused_guard):
        if source.count(guard) != 1:
            raise SystemExit("Merchant native lifecycle guard binding drifted: " + guard)
    cases = [
        ("production", source, ""),
        ("stale-wrapper", source.replace(stale_guard, "// Deliberately keep the stale active wrapper."),
         "a stale invisible native wrapper is reconciled and the next sell opens"),
        ("interrupted-fade", source.replace(fade_guard, "if (confirmation.IsActive || nativeWindow.IsOpen) return false;"),
         "an outgoing native fade is not interrupted by the next offer"),
        ("refused-show", source.replace(refused_guard, "// Deliberately keep the failed Show wrapper."),
         "a native Show refusal cannot leave IsActive latched with no buttons"),
    ]
    manifest = {"result": str(run / "results.txt"), "cases": []}
    for name, text, expected in cases:
        build = run / name
        build.mkdir()
        assembly = "TownMerchantTransaction_" + name.replace("-", "_")
        for file in ("Boundaries.cs", "Program.cs"):
            shutil.copyfile(fixture / file, build / file)
        (build / "Transaction.cs").write_text(text)
        project = (fixture / "Transaction.csproj").read_text().replace(
            "../../src/GloomhavenVR/WorldUI/TownServices/TownServiceMerchantTransaction.cs", "Transaction.cs")
        (build / "Transaction.csproj").write_text(project)
        command = [dotnet, "build", str(build / "Transaction.csproj"), "--configuration", "Release",
                   "--nologo", "--verbosity", "quiet",
                   f"-p:CaseName={assembly}",
                   f"-p:UnityManaged={unity.parent / 'Data/Managed'}", f"-p:UnityUi={ui}"]
        compiled = subprocess.run(command, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        (build / "build.log").write_text(compiled.stdout)
        if compiled.returncode:
            raise SystemExit(f"FAIL {name} compile:\n{compiled.stdout}")
        manifest["cases"].append({"name": name,
                                  "dll": str(build / "bin/Release/netstandard2.1" / (assembly + ".dll")),
                                  "expected": expected})
    manifest_path = run / "manifest.json"
    manifest_path.write_text(json.dumps(manifest) + "\n")
    project = run / "unity"
    (project / "Assets/Editor").mkdir(parents=True)
    (project / "Packages").mkdir()
    (project / "ProjectSettings").mkdir()
    shutil.copyfile(root / "scripts/town-merchant-handoff-runtime/Editor/InteractionRunner.cs",
                    project / "Assets/Editor/InteractionRunner.cs")
    (project / "Packages/manifest.json").write_text('{"dependencies":{"com.unity.ugui":"1.0.0"}}\n')
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    log = run / "unity.log"
    command = [str(unity), "-batchmode", "-nographics", "-projectPath", str(project),
               "-executeMethod", "InteractionRunner.Start", "-interactionManifest", str(manifest_path),
               "-logFile", str(log)]
    result = subprocess.run(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=240)
    evidence = Path(manifest["result"])
    if evidence.exists():
        print(evidence.read_text(), end="")
    if result.returncode or not evidence.exists():
        raise SystemExit(f"FAIL Unity {result.returncode}; log: {log}")
    print(f"PASS: {len(cases)} production/negative variants; evidence: {run}")


if __name__ == "__main__":
    main()
