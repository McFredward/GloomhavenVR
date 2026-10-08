"""Prepare an isolated tiny Unity Android compute compilation proof project."""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import shutil


def prepare(overlay: Path, project: Path, editor_source: Path) -> None:
    if project.exists() and any(project.iterdir()):
        raise ValueError("Compute proof project must be an empty private directory.")
    receipt = json.loads((overlay / "QuestCampaignEvidence/compute-recovery.json").read_text())
    for relative in receipt["files"]:
        source, destination = overlay / relative, project / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, destination)
    evidence = project / "QuestCampaignEvidence/compute-recovery.json"
    evidence.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(overlay / "QuestCampaignEvidence/compute-recovery.json", evidence)
    editor = project / "Assets/Editor/QuestCampaignComputeValidation.cs"
    editor.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(editor_source, editor)
    packages = project / "Packages/manifest.json"
    packages.parent.mkdir(parents=True, exist_ok=True)
    packages.write_text('{"dependencies":{}}\n')
    version = project / "ProjectSettings/ProjectVersion.txt"
    version.parent.mkdir(parents=True, exist_ok=True)
    version.write_text("m_EditorVersion: 2021.3.5f1\n")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--overlay", required=True, type=Path)
    parser.add_argument("--project", required=True, type=Path)
    parser.add_argument("--editor-source", required=True, type=Path)
    args = parser.parse_args()
    prepare(args.overlay.resolve(), args.project.resolve(), args.editor_source.resolve())
