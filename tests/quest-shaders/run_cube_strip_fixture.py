"""Prepare a bounded exact-source Player preprocessor witness in a private project."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[2]
EDITOR = ROOT / "unity/GloomhavenVR.Quest/Assets/Quest/Editor"


def prepare(original_project, project):
    if project.exists() and any(project.iterdir()):
        raise ValueError("Use a new private fixture; existing projects are never overwritten.")
    full = original_project / "Assets/QuestOriginalCampaign/campaign-shaders.json"
    manifest = json.loads(full.read_text())
    shader = next(row for row in manifest["shaders"] if row["guid"] == "ddfc15a0c4897be4aa2350135238cb95")
    copied = set()

    def copy(path):
        if path in copied:
            return
        copied.add(path)
        source, target = original_project / path, project / path
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, target)
        if source.with_name(source.name + ".meta").exists():
            shutil.copyfile(str(source) + ".meta", str(target) + ".meta")
        for include in re.findall(r'#include\s+"(Assets/[^"\n]+)"', source.read_text()):
            copy(include)

    copy(shader["assetPath"])
    out = project / "Assets/QuestOriginalCampaign/campaign-shaders.json"
    mini = dict(manifest, requiredShaderCount=1, requiredMaterialCount=0, shaders=[shader], materials=[], programs=[], renderCases=[])
    out.write_text(json.dumps(mini, indent=2) + "\n")
    shutil.copyfile(full, project / "FullOriginalManifest.json")
    editor = project / "Assets/Quest/Editor"
    editor.mkdir(parents=True)
    for name in ("QuestCampaignShaderStrip.cs", "QuestVulkanShaderValidation.cs", "QuestSmolvDecoder.cs"):
        shutil.copyfile(EDITOR / name, editor / name)
    shutil.copyfile(Path(__file__).parent / "UnityHost/QuestCubeShadowStripWitness.cs", editor / "QuestCubeShadowStripWitness.cs")
    controls = []
    for required in ({"DIRECTIONAL"}, {"POINT", "SHADOWS_CUBE"}, {"SPOT", "SHADOWS_DEPTH"}):
        choices = [bank for bank in shader["variants"] if bank["passType"] == "ForwardAdd" and required <= set(bank["keywords"])]
        controls.append(min(choices, key=lambda bank: (len(bank["keywords"]), bank["hardwareTier"])))
    inputs = {"shader": shader, "controls": controls, "copiedFileCount": len(copied),
              "originalManifestSha256": hashlib.sha256(full.read_bytes()).hexdigest(),
              "shaderSha256": hashlib.sha256((original_project / shader["assetPath"]).read_bytes()).hexdigest()}
    (project / "WitnessInput.json").write_text(json.dumps(inputs, indent=2) + "\n")
    settings = project / "ProjectSettings"
    settings.mkdir()
    (settings / "ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\nm_EditorVersionWithRevision: 2021.3.5f1 (40eb3a945986)\n")
    (settings / "ProjectSettings.asset").write_text("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!129 &1\nPlayerSettings:\n  m_ObjectHideFlags: 0\n  serializedVersion: 26\n  scriptingDefineSymbols:\n    Android: GHVR_QUEST_GAME\n")
    packages = project / "Packages"
    packages.mkdir()
    (packages / "manifest.json").write_text(json.dumps({"dependencies": {"com.unity.modules.jsonserialize": "1.0.0", "com.unity.modules.imgui": "1.0.0", "com.unity.modules.vr": "1.0.0"}}, indent=2) + "\n")
    return inputs


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--original-project", type=Path, required=True)
    parser.add_argument("--project", type=Path, required=True)
    parser.add_argument("--unity", type=Path, required=True)
    args = parser.parse_args()
    prepare(args.original_project.resolve(), args.project.resolve())
    command = [str(args.unity), "-batchmode", "-force-glcore", "-quit", "-projectPath", str(args.project.resolve()),
               "-buildTarget", "Android", "-executeMethod", "QuestCubeShadowStripWitness.Run", "-logFile", str(args.project.parent / "unity.log")]
    command[:0] = ["xvfb-run", "-a"]
    subprocess.run(command, check=True)
    receipt = json.loads((args.project / "CubeStripWitness/result.json").read_text())
    if not receipt["actualNativeBundleBuilt"] or receipt["headsetPictureVerified"] or receipt["removedCompilerProducts"] <= 0:
        raise ValueError("Targeted native bundle witness is incomplete.")
    print("PASS actual original shader light/shadow compiler-product exclusion; no native program changed.")


if __name__ == "__main__":
    main()
