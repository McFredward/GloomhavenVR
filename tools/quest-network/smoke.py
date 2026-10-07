"""Opt-in real original Photon lobby connection on a local Unity editor host."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

from audit import AuditError, digest, write_report

LIBRARIES = ("bolt.dll", "udpkit.dll", "udpkit.common.dll", "udpkit.platform.photon.dll",
             "udpkit.platform.dotnet.dll", "PhotonRealtime.dll", "Photon3Unity3D.dll")


def main(argv=None, runner=subprocess.run):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-data", required=True, type=Path)
    parser.add_argument("--recovered-project", required=True, type=Path)
    parser.add_argument("--unity-editor", required=True, type=Path)
    parser.add_argument("--output-root", required=True, type=Path)
    parser.add_argument("--timeout", type=int, default=45)
    args = parser.parse_args(argv)
    try:
        if not 10 <= args.timeout <= 90:
            raise AuditError("Connection timeout must be 10 to 90 seconds")
        output = args.output_root.resolve()
        inputs = [args.game_data, args.recovered_project, args.unity_editor]
        # Reuse the audit's input/repository output containment checks first.
        write_report(output / "smoke-source.json", {"schema": 1, "status": "preparing"}, inputs)
        work = Path(tempfile.mkdtemp(prefix="unity-photon-", dir=output))
        project = work / "project"
        plugins = project / "Assets/Plugins"
        plugins.mkdir(parents=True)
        sources = []
        def copy_input(original, staged, reference):
            before = digest(original)
            shutil.copy2(original, staged)
            if digest(staged) != before or digest(original) != before:
                raise AuditError("An original input changed during the isolated copy; retry")
            sources.append({"reference": reference, "sha256": before})
        for name in LIBRARIES:
            original = args.game_data / "Managed" / name
            metadata = args.recovered_project / "Assets/Plugins" / (name + ".meta")
            if not original.is_file() or not metadata.is_file():
                raise AuditError("Original Photon managed input/importer is missing: " + name)
            copy_input(original, plugins / name, "Managed/" + name)
            copy_input(metadata, plugins / (name + ".meta"), "importers/" + name + ".meta")
        resources = project / "Assets/Resources"
        resources.mkdir()
        for name in ("BoltRuntimeSettings.asset", "BoltRuntimeSettings.asset.meta"):
            original = args.recovered_project / "Assets/Resources" / name
            copy_input(original, resources / name, "Resources/" + name)
        editor = project / "Assets/Editor"
        editor.mkdir()
        shutil.copy2(Path(__file__).with_name("QuestNetworkSmoke.cs"), editor / "QuestNetworkSmoke.cs")
        (project / "Packages").mkdir()
        (project / "Packages/manifest.json").write_text('{"dependencies":{}}\n')
        (project / "ProjectSettings").mkdir()
        (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
        env = os.environ.copy()
        report_path = work / "photon-result.json"
        env["GHVR_NETWORK_REPORT"] = str(report_path)
        env["GHVR_NETWORK_TIMEOUT_SECONDS"] = str(args.timeout)
        command = [str(args.unity_editor), "-batchmode", "-nographics", "-projectPath", str(project),
                   "-executeMethod", "QuestNetworkSmoke.Run", "-logFile", str(work / "unity-private.log")]
        print("Starting original Photon connection; no game room will be joined.", flush=True)
        try:
            result = runner(command, env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                            timeout=args.timeout + 180)
        except subprocess.TimeoutExpired:
            raise AuditError("Unity exceeded its bounded startup/connection time; inspect private logs")
        # Raw editor output/config is private and must never reach the console.
        proof = {"schema": 1, "stage": "unity-startup", "failure": "No valid runtime receipt; inspect private Unity log"}
        if report_path.is_file():
            raw = json.loads(report_path.read_text())
            if not isinstance(raw, dict) or raw.get("schema") != 1:
                raise AuditError("Unity runtime receipt has an unsupported format")
            allowed = ("schema", "platform", "stage", "failure", "disconnectReason", "originalDefaultConfig",
                "customAuthenticationSupplied", "eosInitialized", "connectedToMaster", "joinedOriginalDefaultLobby",
                "gameRoomJoined", "androidConnected", "elapsedSeconds")
            proof = {key: raw[key] for key in allowed if key in raw}
        proof.update(originalInputs=sources, unityEditorSha256=digest(args.unity_editor),
                     fixtureSha256=digest(Path(__file__).with_name("QuestNetworkSmoke.cs")),
                     smokeToolSha256=digest(Path(__file__)),
                     unityExitCode=result.returncode, evidenceScope="desktop-editor-only; no Android or game-room join")
        write_report(work / "smoke-report.json", proof, inputs)
        success = result.returncode == 0 and proof.get("joinedOriginalDefaultLobby") is True and not proof.get("failure") \
            and proof.get("connectedToMaster") is True and proof.get("originalDefaultConfig") is True \
            and all(proof.get(key) is False for key in ("customAuthenticationSupplied", "eosInitialized", "gameRoomJoined", "androidConnected"))
        print("Original Photon default lobby: " + ("connected" if success else "not proven"))
        print("Private evidence: " + str(work / "smoke-report.json"))
        return 0 if success else 1
    except (AuditError, OSError, ValueError) as failure:
        reason = str(failure) if isinstance(failure, AuditError) else "Cannot validate a local input or private receipt"
        print("Quest network smoke failed: " + reason, file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
