"""Build-time host for the actual owned Windows Apparance engine.

This stage never copies that Windows library into an Android player. It provides
the executable foundation for exporting native presentation from the original
generator without requiring the player's original game executable.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys


def sha(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def prepare(game: Path, root: Path, source: Path) -> Path:
    root.mkdir(parents=True, exist_ok=True)
    if game == root or root.is_relative_to(game):
        raise ValueError("The export host must be outside the selected original installation.")
    project = root / "host-project"
    if project.exists():
        contract = json.loads((project / "quest-host-inputs.json").read_text(encoding="utf-8"))
        current = inputs(game, source)
        if contract != current:
            raise ValueError("Export host inputs changed; select a new isolated output root.")
        return project
    (project / "Packages").mkdir(parents=True)
    (project / "ProjectSettings").mkdir()
    (project / "Assets/Plugins/x86_64").mkdir(parents=True)
    (project / "Packages/manifest.json").write_text('{"dependencies":{"com.unity.modules.jsonserialize":"1.0.0"}}\n', encoding="utf-8")
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n", encoding="utf-8")
    shutil.copytree(source / "UnityHost", project / "Assets/Host")
    shutil.copyfile(game / "Managed/Apparance.Net.dll", project / "Assets/Plugins/Apparance.Net.dll")
    shutil.copyfile(game / "Plugins/x86_64/ApparanceEngine.dll", project / "Assets/Plugins/x86_64/ApparanceEngine.dll")
    shutil.copytree(game / "StreamingAssets/Procedures", project / "Assets/StreamingAssets/Procedures")
    (project / "quest-host-inputs.json").write_text(json.dumps(inputs(game, source), indent=2) + "\n", encoding="utf-8")
    return project


def inputs(game: Path, source: Path) -> dict:
    paths = [game / "Managed/Apparance.Net.dll", game / "Plugins/x86_64/ApparanceEngine.dll"]
    paths.extend(sorted((game / "StreamingAssets/Procedures").rglob("*")))
    original = [{"path": path.relative_to(game).as_posix(), "sha256": sha(path)} for path in paths if path.is_file()]
    recipe = [{"path": path.relative_to(source).as_posix(), "sha256": sha(path)}
              for path in sorted((source / "UnityHost").rglob("*")) if path.is_file()]
    return {"schema": 1, "unityVersion": "2021.3.5f1", "original": original, "recipe": recipe}


def run(args: argparse.Namespace) -> None:
    source = Path(__file__).resolve().parent
    game, root = args.game_data.resolve(), args.output_root.resolve()
    project = prepare(game, root, source)
    exe = root / "Windows/QuestProceduralHost.exe"
    log = root / "host-unity-build.log"
    env = dict(os.environ, GHVR_PROCEDURAL_HOST_EXE=str(exe))
    command = [str(args.unity_editor.resolve()), "-batchmode", "-nographics", "-quit", "-projectPath", str(project),
               "-buildTarget", "Win64", "-executeMethod", "QuestProceduralHostBuild.Build", "-logFile", str(log)]
    result = subprocess.run(command, env=env, check=False)
    if result.returncode != 0 or not exe.is_file():
        raise RuntimeError("Original export host build failed; inspect " + str(log))
    receipt = root / "host-native-probe.json"
    player_log = root / "host-native-player.log"
    command = [str(exe), "-batchmode", "-nographics", "-logFile", str(player_log), "--quest-host-receipt", str(receipt)]
    if os.name != "nt":
        if not args.wine:
            raise ValueError("On this host select --wine for the actual Windows export process.")
        # Wine's default Z: mapping exposes these private local paths. This never
        # rewrites canonical inputs or the user's existing Wine prefix.
        command = [str(args.wine), str(exe), "-batchmode", "-nographics", "-logFile", "Z:" + str(player_log).replace("/", "\\"),
                   "--quest-host-receipt", "Z:" + str(receipt).replace("/", "\\")]
        env.update({"WINEPREFIX": str(root / "wine-prefix"), "WINEDEBUG": "-all"})
        if sys.platform.startswith("linux") and not env.get("DISPLAY"):
            virtual_display = shutil.which("xvfb-run")
            if not virtual_display:
                raise ValueError("The Windows host needs xvfb-run for its batch-mode window when DISPLAY is absent.")
            command = [virtual_display, "-a", *command]
    with (root / "host-process.log").open("w", encoding="utf-8") as output:
        process = subprocess.run(command, env=env, stdout=output, stderr=subprocess.STDOUT, timeout=180, check=False)
    if not receipt.is_file():
        raise RuntimeError("Original Windows export process produced no receipt; inspect " + str(player_log))
    evidence = json.loads(receipt.read_text(encoding="utf-8"))
    required = ("nativeEngineStarted", "nativeEntityCreated", "nativeUpdateExecuted", "cleanupCompleted")
    if process.returncode != 0 or evidence.get("error") or any(evidence.get(key) is not True for key in required):
        raise RuntimeError("Original native export probe failed; inspect " + str(receipt))
    print("Original procedural native host passed: " + str(receipt))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-data", type=Path, required=True)
    parser.add_argument("--output-root", type=Path, required=True)
    parser.add_argument("--unity-editor", type=Path, required=True)
    parser.add_argument("--wine", type=Path)
    try:
        run(parser.parse_args())
        return 0
    except (OSError, ValueError, RuntimeError, subprocess.TimeoutExpired) as error:
        print("Quest procedural host: " + str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
