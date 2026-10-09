#!/usr/bin/env python3
"""Run the actual imported UGUI/InputSystem remap fixture in a private project."""
import argparse
import json
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[2]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--unity", required=True, type=Path)
    parser.add_argument("--startup-project", required=True, type=Path)
    parser.add_argument("--work-root", required=True, type=Path)
    args = parser.parse_args()
    args.work_root.mkdir(parents=True, exist_ok=True)
    project = Path(tempfile.mkdtemp(prefix="sdk-bindings-fixture-", dir=args.work_root))
    for name in ("Assets/Editor", "Packages", "ProjectSettings"):
        (project / name).mkdir(parents=True)
    editor = ROOT / "unity/GloomhavenVR.Quest/Assets/Quest/Editor"
    for name in ("QuestOriginalScriptBindings.cs", "QuestWizardProgress.cs", "QuestOriginalScriptBindingsFixture.cs"):
        shutil.copyfile(editor / name, project / "Assets/Editor" / name)
    shutil.copyfile(args.startup_project / "Assets/QuestOriginalStartup/script-bindings.json", project / "quest-original-fixture.json")
    (project / "Packages/manifest.json").write_text(json.dumps({"dependencies": {
        "com.unity.inputsystem": "1.7.0", "com.unity.ugui": "1.0.0"}}, indent=2))
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    command = [str(args.unity.resolve(strict=True)), "-batchmode", "-nographics", "-projectPath", str(project),
               "-executeMethod", "QuestOriginalScriptBindingsFixture.Run", "-logFile", str(project / "unity.log")]
    print("[Quest SDK bindings fixture] Private project:", project, flush=True)
    result = subprocess.run(command)
    report = project / "fixture-result.json"
    if result.returncode != 0 or not report.is_file():
        print("[Quest SDK bindings fixture] Failed; inspect", project / "unity.log")
        return 1
    evidence = json.loads(report.read_text())
    print(json.dumps(evidence, sort_keys=True))
    return 0 if evidence.get("passed") and evidence.get("failureControls") == 4 else 1


if __name__ == "__main__":
    raise SystemExit(main())
