#!/usr/bin/env python3
"""Exercise the five production Android Vulkan gates in an isolated Unity project.

Only six audited legacy sources, their receipts, and the two owned video adapters
are copied. The prepared full-game project remains read-only. Actual SPIR-V
compiler evidence and targeted failure controls are retained; headset pixels are
not inferred from this compiler fixture.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile


def main():
    root = Path(__file__).resolve().parents[1]
    editor = root / "unity/GloomhavenVR.Quest/Assets/Quest/Editor"
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--unity", type=Path, required=True)
    parser.add_argument("--prepared-project", type=Path, required=True)
    parser.add_argument("--vulkan-helper", type=Path,
                        default=editor / "QuestVulkanShaderValidation.cs")
    args = parser.parse_args()
    source = args.prepared_project.resolve()
    names = ("legacy-post-effects", "original-ui-assets", "original-ui-blur")
    if not all((source / "QuestStartupEvidence" / (name + ".json")).is_file() for name in names):
        raise SystemExit("Prepared audited legacy shader source receipts are required.")
    if not args.vulkan_helper.is_file():
        raise SystemExit("The shared production Vulkan validation helper is required.")
    output = root / ".planning/debug/quest-vulkan-legacy-shaders"
    output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=output))
    project = run / "unity"
    before = {}

    def copy_owned(path, relative):
        if path.is_symlink():
            raise SystemExit("Linked shader fixture input: " + str(path))
        content = path.read_bytes()
        before[str(path)] = content
        target = project / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(content)

    for name in names:
        relative = "QuestStartupEvidence/" + name + ".json"
        receipt = json.loads((source / relative).read_text())
        copy_owned(source / relative, relative)
        for row in receipt.get("shaders", [receipt]):
            asset = row["assetPath"]
            if not asset.startswith("Assets/Shader/") or ".." in Path(asset).parts:
                raise SystemExit("Unsafe audited legacy shader input.")
            copy_owned(source / asset, asset)
            copy_owned(source / (asset + ".meta"), asset + ".meta")
    for name in ("QuestPostEffectValidation", "QuestUiAssetValidation", "QuestBlurValidation",
                 "QuestVideoValidation", "QuestWorldScreenValidation"):
        copy_owned(editor / (name + ".cs"), "Assets/Quest/Editor/" + name + ".cs")
    copy_owned(args.vulkan_helper, "Assets/Quest/Editor/QuestVulkanShaderValidation.cs")
    copy_owned(args.vulkan_helper.with_name("QuestSmolvDecoder.cs"), "Assets/Quest/Editor/QuestSmolvDecoder.cs")
    for name in ("QuestCameraVideo", "QuestWorldScreen"):
        path = root / "unity/GloomhavenVR.Quest/Assets/Quest/Resources" / (name + ".shader")
        copy_owned(path, "Assets/Quest/Resources/" + path.name)
    copy_owned(root / "tests/QuestLegacyVulkan.Tests/Probe.cs", "Assets/Quest/Editor/Probe.cs")
    (project / "Assets/csc.rsp").write_text("-define:GHVR_QUEST_GAME\n")
    (project / "Packages").mkdir()
    (project / "ProjectSettings").mkdir()
    (project / "Packages/manifest.json").write_text('{"dependencies":{}}\n')
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    print("Quest legacy Vulkan evidence: " + str(run), flush=True)
    result = subprocess.run([str(args.unity.resolve()), "-batchmode", "-nographics", "-quit",
                             "-buildTarget", "Android", "-projectPath", str(project),
                             "-executeMethod", "QuestLegacyVulkanProbe.Run", "-logFile", str(run / "unity.log")],
                            timeout=360, capture_output=True, text=True)
    (run / "console.log").write_text(result.stdout + result.stderr)
    if result.returncode:
        raise SystemExit("Actual legacy Vulkan gate fixture failed: " + str(run / "unity.log"))
    evidence = json.loads((project / "ProbeOutput/results.json").read_text())
    if evidence["actualBanks"] != 27 or evidence["actualStages"] != 54 or evidence["defectControls"] != 7:
        raise SystemExit("Actual legacy Vulkan gate fixture evidence is incomplete.")
    if evidence["hardwareVerified"] or not all(Path(path).read_bytes() == data for path, data in before.items()):
        raise SystemExit("Shader fixture source immutability or evidence scope differs.")
    (run / "source-hashes.json").write_text(json.dumps(
        {path: hashlib.sha256(data).hexdigest() for path, data in before.items()}, indent=2) + "\n")
    for cache in ("Library", "Temp"):
        shutil.rmtree(project / cache, ignore_errors=True)
    print("PASS Quest legacy Vulkan: 27 actual banks, 54 stages, 7 targeted defects")


if __name__ == "__main__":
    main()
