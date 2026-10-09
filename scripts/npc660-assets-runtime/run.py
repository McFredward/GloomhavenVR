#!/usr/bin/env python3
"""Reproduce native duplicate-texture blockers with unchanged serialized game assets."""
import argparse
import hashlib
import importlib.util
import json
import os
import shutil
import subprocess
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, default=ROOT)
    parser.add_argument("--output-dir", type=Path, default=ROOT / ".planning/debug/npc660-assets/proof")
    args = parser.parse_args()
    root = args.source_root.resolve()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=args.output_dir.resolve()))
    fixture = Path(__file__).resolve().parent
    py = os.environ.get("UNITYPY_PYTHON", str(Path.home() / "unitypy-venv/bin/python"))
    subprocess.run([py, str(fixture / "export-native.py"), str(root / "ressources/GH_Data"), str(run / "native")], check=True)
    source = root / "src/GloomhavenVR"
    paths = list((source / "Net/TownServices").glob("TownServiceAssets*.cs"))
    paths += [source / "Net/TownServices/TownServiceMaterial.cs", source / "WorldUI/TownServices/TownServiceTemplateAssets.cs"]
    bound = {p.name: p.read_text() for p in paths}
    frame = source / "Net/TownServices/TownServiceFrame.cs"
    text = frame.read_text()
    # Preserve exact production values/comparer; only unused whole-frame fields
    # and rack/gameplay model types lie outside this resolver/material proof.
    start, end = text.index("internal sealed class TownServiceValue\n"), text.index("internal static class TownServiceProperty\n")
    bound["NativeValues.cs"] = "using System; using System.Collections.Generic; namespace GloomhavenVR.Net.TownServices;\n" + text[start:end] + "internal static class TownServiceFrame { internal const int MaxProperties = 254; }\n"
    assert "MaxProperties = 254" in text
    template = source / "WorldUI/TownServices/NativeTemplates.cs"
    spec = importlib.util.spec_from_file_location("native_source", root / "scripts/check-town-service-mirror.py")
    loader = importlib.util.module_from_spec(spec); spec.loader.exec_module(loader)
    append = loader.method(template.read_text(), "internal static string Append(")
    bound["TemplateBoundary.cs"] = "using System; using UnityEngine; namespace GloomhavenVR.WorldUI; internal static class NativeTemplates { internal static bool IsBoundary(Transform node) => false;\n" + append + "\n}\n"
    hashes = {str(p.relative_to(root)): hashlib.sha256(p.read_bytes()).hexdigest() for p in paths + [frame, template]}
    cases = [("production", dict(bound), "")]
    for name in ("Default-Particle", "T_sphere_norm"):
        current = dict(bound)
        before = 'texture.name == "' + name + '"'
        assert current["TownServiceAssets.cs"].count(before) == 1
        current["TownServiceAssets.cs"] = current["TownServiceAssets.cs"].replace(before, 'texture.name == "removed660-' + name + '"', 1)
        cases.append(("old-" + name, current, "Ambiguous native town-service asset: texture|" + name + "|"))
    unity = Path(os.environ.get("UNITY_PATH", "/home/claw/unity-2021.3.5/Editor/Unity"))
    project = run / "unity"
    for directory in ("Assets/Editor", "Packages", "ProjectSettings"):
        (project / directory).mkdir(parents=True)
    (project / "Packages/manifest.json").write_text('{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6"}}\n')
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    for companion in (run / "native").glob("*.resS"):
        (project / companion.name).symlink_to(companion)
    shutil.copyfile(root / "scripts/npc639-assets-runtime/AssetsRunner.cs.txt", project / "Assets/Editor/AssetsRunner.cs")
    results = []
    for name, sources, expected in cases:
        case = run / name
        prod, ports = case / "production", case / "fixture"
        prod.mkdir(parents=True); ports.mkdir()
        for filename, data in sources.items():
            (prod / filename).write_text(data)
        shutil.copyfile(fixture / "AssetsProof.cs", ports / "AssetsProof.cs")
        shutil.copyfile(root / "scripts/npc639-assets-runtime/Boundaries.cs", ports / "Boundaries.cs")
        # No procedural body is constructed by this serialized-native asset proof.
        # Its separate full CardMesh proof exercises the production factory identity.
        with (ports / "Boundaries.cs").open("a") as boundary:
            boundary.write("\nnamespace GloomhavenVR.Cards { internal static class CardMesh { internal static bool IsOriginalBackTexture(UnityEngine.Texture texture) => false; } }\n")
        project_text = (root / "scripts/npc639-assets-runtime/Assets.csproj").read_text()
        project_text = project_text.replace('<Reference Include="Unity.TextMeshPro">', '<Reference Include="UnityEngine.UI"><HintPath>$(UnityUi)</HintPath><Private>false</Private></Reference><Reference Include="Unity.TextMeshPro">')
        (case / "Assets.csproj").write_text(project_text)
        assembly = "Assets660_" + name.replace("-", "_")
        command = [str(Path.home() / ".dotnet/dotnet"), "build", str(case / "Assets.csproj"), "-c", "Release", "--nologo", "--verbosity", "quiet", "-p:CaseName=" + assembly,
                   "-p:FixtureDir=" + str(ports), "-p:ProductionDir=" + str(prod), "-p:UnityManaged=" + str(unity.parent / "Data/Managed"),
                   "-p:UnityUi=" + str(root / "ressources/GH_Data/Managed/UnityEngine.UI.dll"), "-p:UnityTmp=" + str(root / "ressources/GH_Data/Managed/Unity.TextMeshPro.dll")]
        result = subprocess.run(command, capture_output=True, text=True)
        (case / "build.log").write_text(result.stdout + result.stderr)
        if result.returncode:
            raise SystemExit("Compilation failed " + name + ": " + str(case / "build.log"))
        command = ["xvfb-run", "-a", str(unity), "-batchmode", "-force-glcore", "-projectPath", str(project), "-executeMethod", "AssetsRunner.Start",
                   "-assetsDll", str(case / "bin/Release/netstandard2.1" / (assembly + ".dll")), "-assetsBank", str(run / "native"), "-assetsOutput", str(case / "assertions.txt"), "-logFile", str(case / "unity.log")]
        result = subprocess.run(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=180)
        log = (case / "unity.log").read_text()
        passed = result.returncode != 0 and expected in log if expected else result.returncode == 0 and (case / "assertions.txt").exists()
        receipt = {"case": name, "passed": passed, "exit": result.returncode, "expected": expected,
                   "assertions": (case / "assertions.txt").read_text() if (case / "assertions.txt").exists() else None}
        results.append(receipt)
        print(("PASS " if passed else "FAIL ") + name + ": " + str(receipt["assertions"] or expected), flush=True)
        if not passed:
            raise SystemExit("Runtime failed: " + str(case / "unity.log"))
    (run / "results.json").write_text(json.dumps({"passed": True, "source_sha256": hashes, "cases": results,
        "limits": "Actual serialized GPU-only native textures/material resolver; declared unlit readback and no-boundary single-root model ports. No gameplay shader/network/HMD proof."}, indent=2) + "\n")
    print("Evidence: " + str(run), flush=True)


if __name__ == "__main__":
    main()
