"""Run original Campaign scenes in an isolated, build-time Windows generator.

The executable is a same-version Mono host; its data is the player's immutable
original installation. Managed adapters are copied into a private data directory.
The host retains owned original bytes and a separate writable state directory.
"""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import uuid
import zipfile

from host import sha


def immutable_copy(source: Path, destination: Path) -> None:
    """Create a private name for original read-only bytes, without rewriting them."""
    source, destination = Path(source), Path(destination)
    destination.parent.mkdir(parents=True, exist_ok=True)
    if destination.exists():
        if os.path.samefile(source, destination):
            return
        if destination.stat().st_size != source.stat().st_size or sha(destination) != sha(source):
            raise ValueError("An isolated original input changed: " + str(destination))
        return
    try:
        os.link(source, destination)
    except OSError:
        shutil.copyfile(source, destination)


def windows_path(path: Path) -> str:
    return str(path) if os.name == "nt" else "Z:" + str(path).replace("/", "\\")


def prepare(args: argparse.Namespace) -> tuple[Path, Path]:
    game, root, host = args.game_data.resolve(), args.output_root.resolve(), args.host_player.resolve()
    if root == game or root.is_relative_to(game) or game.is_relative_to(root):
        raise ValueError("Campaign conversion needs a separate private output root.")
    root.mkdir(parents=True, exist_ok=True)
    original_inputs = {"schema": 1, "gameManagedSha256": sha(game / "Managed/GH.Runtime.dll"),
                       "globalManagersSha256": sha(game / "globalgamemanagers"),
                       "hostUnityPlayerSha256": sha(host / "UnityPlayer.dll")}
    contract = root / "original-host-inputs.json"
    if contract.exists() and json.loads(contract.read_text()) != original_inputs:
        raise ValueError("Native conversion inputs changed; select a new private root.")
    contract.write_text(json.dumps(original_inputs, indent=2) + "\n")
    player = root / "Player"
    player.mkdir(exist_ok=True)
    for name in ("UnityPlayer.dll", "UnityCrashHandler64.exe"):
        if (host / name).is_file():
            immutable_copy(host / name, player / name)
    shutil.copytree(host / "MonoBleedingEdge", player / "MonoBleedingEdge", copy_function=immutable_copy, dirs_exist_ok=True)
    immutable_copy(host / "QuestProceduralHost.exe", player / "GH.exe")
    data = player / "GH_Data"
    for source in game.rglob("*"):
        if not source.is_file():
            continue
        relative = source.relative_to(game)
        # The process may replace these files only within its private namespace.
        if relative.parts[0] == "Managed" or relative.name == "ScriptingAssemblies.json":
            destination = data / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            if not destination.exists():
                shutil.copyfile(source, destination)
        else:
            immutable_copy(source, data / relative)
    output = root / "output"
    state = output / "isolated-save-state/quest-owned-game/StreamingAssets"
    for source in (game / "StreamingAssets").rglob("*"):
        if source.is_file():
            immutable_copy(source, state / source.relative_to(game / "StreamingAssets"))
    managed = data / "Managed"
    for source in args.adapters.resolve().glob("*.dll"):
        destination = managed / source.name
        temporary = destination.with_suffix(".conversion-new")
        shutil.copyfile(source, temporary)
        os.replace(temporary, destination)
    manifest = json.loads((game / "ScriptingAssemblies.json").read_text())
    for name in ("QuestGame.Compatibility.dll", "QuestProceduralExport.dll"):
        if name not in manifest["names"]:
            manifest["names"].append(name)
            manifest["types"].append(16)
    (data / "ScriptingAssemblies.json").write_text(json.dumps(manifest))
    levels = root / "inputs/levels"
    levels.mkdir(parents=True, exist_ok=True)
    archive = game / "StreamingAssets/Rulebase/Campaign.ruleset"
    with zipfile.ZipFile(archive) as source:
        entry = "CustomLevels/Scenario_Campaign_001.lvldat"
        level = levels / "Scenario_Campaign_001.lvldat"
        level.write_bytes(source.read(entry))
    configuration = root / "campaign-export-config.json"
    configuration.write_text(json.dumps({"schema": 1, "runId": uuid.uuid4().hex, "outputRoot": windows_path(output),
                                       "levels": [{"path": windows_path(level), "archive": "Campaign.ruleset", "entry": entry}]}, indent=2))
    return player / "GH.exe", configuration


def run(args: argparse.Namespace) -> None:
    exe, configuration = prepare(args)
    root = args.output_root.resolve()
    log = root / "original-player.log"
    command = [str(exe), "-batchmode", "-logFile", windows_path(log), "--quest-export-config", windows_path(configuration)]
    if not args.graphics:
        command.insert(2, "-nographics")
    environment = dict(os.environ)
    if os.name != "nt":
        if not args.wine:
            raise ValueError("Select the actual Windows process runner with --wine.")
        command.insert(0, str(args.wine.resolve()))
        environment.update(WINEPREFIX=str(root / "wine-prefix"), WINEDEBUG="-all")
        if sys.platform.startswith("linux") and not environment.get("DISPLAY"):
            virtual_display = shutil.which("xvfb-run")
            if not virtual_display:
                raise ValueError("The Windows Mono host requires xvfb-run on a headless build machine.")
            command = [virtual_display, "-a", *command]
    with (root / "original-process.log").open("w") as output:
        process = subprocess.run(command, env=environment, cwd=exe.parent, stdout=output,
                                 stderr=subprocess.STDOUT, timeout=540, check=False)
    receipt = root / "output/original-campaign-stage.json"
    if not receipt.is_file():
        raise RuntimeError("Original Campaign process produced no receipt; inspect " + str(log))
    proof = json.loads(receipt.read_text())
    expected_run = json.loads(configuration.read_text())["runId"]
    if process.returncode != 0 or proof.get("runId") != expected_run or proof.get("error") or proof.get("state") != "original-geometry-ready":
        raise RuntimeError("Original native Campaign stage failed; inspect " + str(receipt))
    print("Original Campaign geometry generation passed: " + str(receipt))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("game-data", "output-root", "host-player", "adapters"):
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--wine", type=Path)
    parser.add_argument("--graphics", action="store_true")
    try:
        run(parser.parse_args())
        return 0
    except (OSError, ValueError, RuntimeError, subprocess.TimeoutExpired, zipfile.BadZipFile) as error:
        print("Quest original Campaign exporter: " + str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
