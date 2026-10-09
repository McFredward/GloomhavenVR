"""Player-operated local Quest conversion with privately provisioned build tools.

Every external step executes real tools and verifies outputs. A diagnostic target
is explicitly distinct from a recovered, woven game. Proprietary inputs, identity,
keys and APKs remain in the marked local output directory.
"""

from __future__ import annotations

import argparse
from contextlib import contextmanager, nullcontext
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import secrets
import shutil
import struct
import subprocess
import sys
from types import SimpleNamespace
import zipfile

# The CLI handoff uses -I: retain only this captured tool directory, never the
# launcher's current directory or an inherited PYTHONPATH, for local helpers.
if __name__ == "__main__":
    sys.path.insert(0, str(Path(__file__).resolve().parent))

import startup
import shaders as post_effects
import dlcs
from sprites import restore_loading_sprite_geometry
from audio import stage_startup_audio, REPORT as AUDIO_REPORT, RESOURCE as AUDIO_RESOURCE
import ui_assets
import full_assets
import campaign
import mod_assets
import build_provenance
import import_workspace
import native_plugins

from profile import discover_steam_root, dummy_identity, load_profile, read_logo, ProfileError
from storage import (BuildError, Stages, canonical, digest, ensure_output, inventory, persistent_inventory,
                     output_lock, record_file, snapshot, value_hash, verify_files, write_json,
                     restore_project_library, regenerate_project, recover_project_content, project_content_transaction,
                     project_content_valid, publish_project_content, CONTENT_PATHS, _ordinary_owned)


def _local_helper(name):
    # Installer/API loaders temporarily register builder dependencies without
    # placing this directory on sys.path. Retain each exact local helper object.
    path = Path(__file__).with_name(name + ".py")
    spec = importlib.util.spec_from_file_location("quest_builder_" + name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


host_resources = _local_helper("host_resources")
build_progress = _local_helper("progress")
recovery_resume = _local_helper("recovery_resume")
prepare_resume = _local_helper("prepare_resume")
preparation_identity = _local_helper("preparation_identity")
_release = _local_helper("release") if Path(__file__).with_name("release.py").is_file() else None
apk_updates = _local_helper("apk_update")


def _module_context():
    # The installer deliberately removes its temporary import aliases after
    # loading us. Keep callable context without depending on that registry.
    return SimpleNamespace(**globals())


RECIPE = 1
BUILDER_RESUME_CONTRACT = 1
PACKAGE = "dev.gloomhavenvr.quest"
REPO = Path(__file__).resolve().parents[2]
REPLACED_PACKAGES = ("UnityEngine.UI", "Unity.InputSystem", "Unity.Addressables",
                     "Unity.ResourceManager", "Unity.ScriptableBuildPipeline",
                     "Unity.XR.Management", "Unity.XR.OpenXR", "Unity.XR.CoreUtils")
UGUI_LAYOUT_SOURCE_SHA256 = "386d0e978df81c3bfde2c91acf01c8fc447a36e170684fdd83a06fdebaac944c"
ORIGINAL_UGUI_SHA256 = "267daefe946bbed18d13c7c572043bee15cd265ef1b443934b3dcbcd3a351f5f"


def command(argv: list[str], log: Path, *, cwd: Path | None = None,
            env: dict | None = None) -> str:
    """No shell interpolation, no environment dump and no passwords in argv."""
    argv = list(argv)
    build_progress.event("tool:" + log.stem[:140], detail=Path(argv[0]).name, status="start")
    unity = any(Path(arg).name.casefold() in ("unity", "unity.exe") for arg in argv)
    if unity and "-version" not in argv:
        policy = host_resources.phase_budget("unity", log.parent)
        if "-job-worker-count" in argv:
            index = argv.index("-job-worker-count")
            argv[index + 1] = str(min(int(argv[index + 1]), policy["jobs"]))
        else:
            argv += ["-job-worker-count", str(policy["jobs"])]
    log.parent.mkdir(parents=True, exist_ok=True)
    with host_resources.timed_phase("unity" if unity else "command", log=log), log.open("w", encoding="utf-8") as stream:
        try:
            result = subprocess.run(argv, cwd=cwd, env=env, stdout=stream,
                                    stderr=subprocess.STDOUT, check=False)
        except OSError as exc:
            raise BuildError("Cannot execute " + Path(argv[0]).name + "; verify the selected tool path.") from exc
        if result.returncode:
            raise BuildError(Path(argv[0]).name + " exited with " + str(result.returncode) +
                             "; inspect " + str(log))
    output = log.read_text(encoding="utf-8", errors="replace")
    build_progress.event("tool:" + log.stem[:140], 1, 1, "commands", Path(argv[0]).name, status="complete")
    return output


def unity_launcher(editor, *, graphics=False):
    """Expose native MRT/compute capability for complete Campaign compiler gates."""
    arguments = [str(editor), "-batchmode"]
    if not graphics: return arguments + ["-nographics"]
    if sys.platform == "linux":
        if os.environ.get("DISPLAY", "").strip():
            return arguments + ["-force-glcore"]
        display = shutil.which("xvfb-run")
        if not display or not shutil.which("xauth"):
            raise BuildError("The Linux full Campaign build needs a desktop X display or xvfb-run and xauth for its OpenGLCore compiler host.")
        return [display, "-a", *arguments, "-force-glcore"]
    if sys.platform == "win32":
        # Use the same witnessed Editor compute backend as the Linux builder.
        # Unity 2021.3 supports this switch on Windows; otherwise its default
        # D3D import bank is an unqualified extra in the Android content build.
        return arguments + ["-force-glcore"]
    return arguments


def git_output(repo: Path, *args: str) -> bytes:
    try:
        return subprocess.run(["git", "-C", str(repo), *args], check=True,
                              stdout=subprocess.PIPE, stderr=subprocess.PIPE).stdout
    except (OSError, subprocess.CalledProcessError) as exc:
        raise BuildError("The builder requires a Git checkout and Git on PATH.") from exc


def game_data(root: Path) -> Path:
    root = root.resolve()
    if (root / "Managed/GH.Runtime.dll").is_file():
        result = root
    else:
        candidates = sorted(p for p in root.glob("*_Data") if (p / "Managed/GH.Runtime.dll").is_file())
        if len(candidates) != 1:
            raise BuildError("Select the owned Gloomhaven installation or its unique *_Data directory.")
        result = candidates[0]
    required = ["globalgamemanagers", "resources.assets", "Managed/GH.Runtime.dll",
                "Managed/GH.Shared.dll", "StreamingAssets/Rulebase"]
    missing = [p for p in required if not (result / p).exists()]
    if missing:
        raise BuildError("The game installation is incomplete: " + ", ".join(missing))
    return result


def source_inventory(repo: Path) -> tuple[list[dict], str, bool]:
    selected = []
    if not (repo / ".git").exists():
        if _release is None or not (repo / "quest-builder-release.json").is_file():
            raise BuildError("The builder requires a Git checkout or a verified Quest builder release.")
        records, commit, dirty = _release.verified_source_inventory(repo)
        local = [p.relative_to(repo).as_posix() for name in ("libs/RuntimeDeps", "libs/Natives")
                 for p in (repo / name).glob("*") if p.is_file() and p.suffix.lower() in (".dll", ".json")]
        for relative in local:
            _ordinary_owned(repo / relative)
        combined = [*records, *inventory(repo, local, phase="source-local-dependency-hash")]
        if len({row["path"].casefold() for row in combined}) != len(combined):
            raise BuildError("Release/local dependency inventories overlap or collide.")
        return sorted(combined, key=lambda row: row["path"]), commit, dirty
    for raw in git_output(repo, "ls-files", "--cached", "--others", "--exclude-standard", "-z").split(b"\0"):
        if not raw:
            continue
        relative = raw.decode("utf-8")
        if _release is not None and _release.owned_derived_source(relative):
            continue
        if Path(relative).suffix.lower() in (".env", ".alf", ".ulf", ".keystore", ".jks", ".p12", ".pem", ".key"):
            continue
        if relative.startswith(("src/", "unity/", "tools/", "scripts/", "prebuilt/", "libs/RefAsm/")) or (
                "/" not in relative and relative.endswith((".props", ".targets", ".config", ".sln", ".json"))):
            if (repo / relative).is_file():
                selected.append(relative)
    # Include ordinary newly added source/assets automatically, while honoring the
    # repository's ignored output/secrets. Only the declared XR DLLs override ignore.
    for relative in ("libs/RuntimeDeps", "libs/Natives"):
        directory = repo / relative
        selected.extend(p.relative_to(repo).as_posix() for p in directory.glob("*")
                        if p.is_file() and p.suffix.lower() in (".dll", ".json"))
    commit = git_output(repo, "rev-parse", "HEAD").decode().strip()
    dirty = bool(git_output(repo, "status", "--porcelain", "--untracked-files=normal"))
    return inventory(repo, selected, phase="source-hash"), commit, dirty


def original_version(data: Path) -> str:
    raw = (data / "globalgamemanagers").read_bytes()[:1024 * 1024]
    match = re.search(rb"20[0-9]{2}\.[0-9]+\.[0-9]+[abfp][0-9]+", raw)
    return match.group().decode() if match else "unknown"


def mod_build(repo: Path) -> int:
    raw = (repo / "src/GloomhavenVR/Net/NetProtocol.cs").read_text(encoding="utf-8")
    match = re.search(r"public\s+const\s+ushort\s+ModBuild\s*=\s*([0-9]+)\s*;", raw)
    if not match:
        raise BuildError("Cannot determine the selected mod's ModBuild.")
    return int(match.group(1))


def selected_profile(args) -> tuple[dict | None, bytes | None]:
    if args.dummy_profile:
        if args.profile_json or args.steam_root or args.steam_id:
            raise BuildError("--dummy-profile cannot be combined with a real account selection.")
        if not args.steam_logo:
            raise BuildError("Supply --steam-logo even for the explicit DUMMY hardware-test identity.")
        logo, hashed = read_logo(Path(args.steam_logo))
        result = dummy_identity()
        result["logoSha256"] = hashed
        return result, logo
    if not args.profile_json and not args.steam_root and args.command != "inspect":
        args.steam_root = discover_steam_root()
    if args.profile_json or args.steam_root:
        if not args.steam_logo:
            raise BuildError("Supply --steam-logo: a local static Steam PNG, not a personal-avatar URL.")
        return load_profile(Path(args.profile_json) if args.profile_json else None,
                            Path(args.steam_root) if args.steam_root else None,
                            args.steam_id, Path(args.steam_logo))
    if args.command != "inspect":
        raise BuildError("A local provider profile is required; use --steam-root for Steam or --profile-json for Steam/Epic/GOG. "
                         "Maintainer-only hardware tests may explicitly select --dummy-profile.")
    return None, None


def inspect_inputs(args, repo: Path, output: Path, data: Path) -> dict:
    print("inspect: reading owned game changes and current mod inputs", flush=True)
    build_progress.operation("game-inputs", detail="Reading owned original game files")
    game_files = persistent_inventory(data, output / "cache/input-inventories/game.sqlite3", phase="game-hash")
    build_progress.operation("mod-inputs", detail="Checking selected source files and source ModBuild")
    source_files, commit, dirty = source_inventory(repo)
    build_progress.operation("profile-inputs", detail="Validating local identity and owned DLC records")
    profile, logo = selected_profile(args)
    if profile is not None:
        profile["dlcOwnership"] = dlcs.capture(args, data, profile)
    game_key = value_hash({"files": game_files})
    source_key = value_hash({"files": source_files})
    profile_key = value_hash(profile) if profile else None
    probe = None
    if args.probe_assets:
        probe_files = inventory(args.probe_assets.resolve())
        forbidden = [item["path"] for item in probe_files if Path(item["path"]).suffix.lower() in (
            ".cs", ".dll", ".so", ".exe", ".asmdef", ".asmref", ".rsp", ".boo", ".js", ".env")]
        if forbidden:
            raise BuildError("Probe slices accept native assets only; executable/config input found: " + forbidden[0])
        if not probe_files:
            raise BuildError("The selected probe-asset slice is empty.")
        probe = {"key": value_hash({"files": probe_files}), "files": probe_files}
    startup_project = getattr(args, "startup_project", None)
    original_startup = startup.inspect_project(startup_project, game_key, game_files) if startup_project else None
    campaign_project = getattr(args, "campaign_project", None)
    original_campaign = campaign.inspect_project(campaign_project, game_key, game_files) if campaign_project else None
    if campaign_project and args.target != "game":
        raise BuildError("--campaign-project is only valid for the complete game target.")
    if args.target == "startup" and original_startup is None:
        raise BuildError("--target startup requires --startup-project from the original startup recovery tool.")
    inputs = {"schema": 1, "recipe": RECIPE, "target": args.target,
              "game": {"key": game_key, "unityVersion": original_version(data), "files": game_files},
              "mod": {"key": source_key, "commit": commit, "dirty": dirty,
                      "modBuild": mod_build(repo), "files": source_files},
              "profile": profile, "profileKey": profile_key, "probeAssets": probe,
              "startupProject": original_startup, "campaignProject": original_campaign}
    if args.target == "game":
        backend = getattr(args, "procedural_backend", None) or os.environ.get("GHVRQ_PROCEDURAL_BACKEND", "proton-arm64ec-fex")
        if backend not in ("proton-arm64ec-fex", "box64-wine9"):
            raise BuildError("Unknown procedural runtime backend: " + backend)
        # Backend choice is an immutable preparation/build input. Switching an
        # explicit fallback must never reuse the other backend's prepared Player.
        inputs["proceduralBackend"] = backend
    inputs["inputKey"] = value_hash(inputs)
    if getattr(args, "command", "inspect") == "inspect": build_progress.operation("manifest", detail="Publishing the immutable input manifest")
    else: build_progress.operation("profile-inputs", complete=True, detail="Input identity and DLC ownership captured")
    manifest = output / "manifests" / (inputs["inputKey"] + ".json")
    write_json(manifest, inputs)
    if profile:
        folder = output / "identities" / profile_key
        write_json(folder / "quest-profile.json", profile)
        (folder / "quest-steam-logo.png").write_bytes(logo)
    write_json(output / "latest-input.json", {"manifest": manifest.relative_to(output).as_posix(),
               "sourceGameRoot": str(data), "sourceRepo": str(repo)})
    print("inspect: " + str(len(game_files)) + " game files, " + str(len(source_files)) +
          " mod/tool/asset files; ModBuild " + str(inputs["mod"]["modBuild"]) +
          ("; explicitly DUMMY test identity" if profile and profile.get("isDummy") else ""), flush=True)
    if getattr(args, "command", "inspect") == "inspect": build_progress.operation("manifest", complete=True, detail="Input manifest published")
    return inputs


def snapshot_inputs(inputs: dict, output: Path, repo: Path, data: Path,
                    probe_assets: Path | None = None, startup_project: Path | None = None) -> tuple[Path, Path]:
    source = output / "inputs/mod" / inputs["mod"]["key"]
    game = output / "inputs/game" / inputs["game"]["key"]
    print("snapshot: verifying/copying immutable game and selected source", flush=True)
    build_progress.operation("source-snapshot", detail="Freezing the selected current mod source")
    snapshot(repo, inputs["mod"]["files"], source, phase="source-snapshot")
    # Reject edits across files during the snapshot window, not only a torn
    # individual read or added source file. Once frozen, later mod development
    # proceeds independently of the potentially lengthy original-game copy.
    if source_inventory(repo)[0] != inputs["mod"]["files"]:
        raise BuildError("The mod changed during snapshotting; rerun against the completed edit.")
    if mod_build(source) != inputs["mod"]["modBuild"]:
        raise BuildError("The input ModBuild disagrees with the captured source.")
    build_progress.operation("game-snapshot", detail="Copying or verifying retained original game inputs")
    snapshot(data, inputs["game"]["files"], game, phase="game-snapshot")
    build_progress.operation("snapshot-check", detail="Confirming the installation did not change during the copy")
    if persistent_inventory(data, output / "cache/input-inventories/game.sqlite3", phase="game-snapshot-source-verify") != inputs["game"]["files"]:
        raise BuildError("The original installation changed during snapshotting; finish its update and retry.")
    if inputs.get("probeAssets"):
        probe = inputs["probeAssets"]
        if not probe_assets:
            raise BuildError("The manifest has probe assets but no selected source directory.")
        snapshot(probe_assets.resolve(), probe["files"], output / "inputs/probe" / probe["key"])
        if inventory(probe_assets.resolve()) != probe["files"]:
            raise BuildError("The recovered probe assets changed during snapshotting; finish export and retry.")
    if inputs.get("startupProject"):
        original_startup = inputs["startupProject"]
        if startup_project is None:
            raise BuildError("The manifest has original startup assets but no selected source directory.")
        snapshot(startup_project.resolve(), original_startup["files"],
                 output / "inputs/startup" / original_startup["key"])
        if inventory(startup_project.resolve()) != original_startup["files"]:
            raise BuildError("The original startup project changed during snapshotting; finish export and retry.")
    build_progress.operation("snapshot-check", complete=True, detail="Immutable mod and original game snapshots confirmed")
    return source, game


def tool_path(value: str | None, name: str) -> Path:
    candidate = value or shutil.which(name)
    if not candidate or not Path(candidate).is_file():
        raise BuildError(name + " was not found; install it or supply its explicit path.")
    return Path(candidate).resolve()


def toolchain(args, output: Path) -> dict:
    editor = tool_path(args.unity_editor, "Unity")
    data = editor.parent / "Data"
    android = data / "PlaybackEngines/AndroidPlayer"
    if not android.is_dir():
        raise BuildError("This Unity Editor has no Android Build Support. Install its matching Android module.")
    sdk = Path(args.android_sdk or android / "SDK").resolve()
    ndk = Path(args.android_ndk or android / "NDK").resolve()
    jdk = Path(args.jdk or android / "OpenJDK").resolve()
    suffix = ".exe" if os.name == "nt" else ""
    sdk_tools = sorted((sdk / "build-tools").glob("*"),
                       key=lambda p: tuple(int(x) for x in re.findall(r"[0-9]+", p.name)))
    sdk_tools = [p for p in sdk_tools if (p / ("aapt" + suffix)).is_file()]
    if not sdk_tools:
        raise BuildError("The selected Android SDK has no installed build-tools/aapt.")
    build_tools = sdk_tools[-1]
    apk_signer = build_tools / "lib/apksigner.jar"
    java = jdk / "bin" / ("java" + suffix)
    keytool = jdk / "bin" / ("keytool" + suffix)
    if not apk_signer.is_file() or not java.is_file() or not keytool.is_file() or not (ndk / "source.properties").is_file():
        raise BuildError("The selected Android SDK/NDK/JDK is incomplete (apksigner.jar, Java, keytool or NDK revision missing).")
    version = command([str(editor), "-version"], output / "logs/unity-version.log").strip().splitlines()
    found = next((v.strip() for v in version if re.fullmatch(r"20\d{2}\.\d+\.\d+[abfp]\d+", v.strip())), None)
    if not found:
        raise BuildError("The Unity executable did not report a recognizable editor version.")
    selected = {"editor": str(editor), "unityVersion": found, "androidSdk": str(sdk),
                "androidNdk": str(ndk), "jdk": str(jdk), "aapt": str(build_tools / ("aapt" + suffix)),
                "apksigner": str(apk_signer), "java": str(java), "keytool": str(keytool),
                "apksignerSha256": digest(apk_signer), "javaSha256": digest(java),
                "editorSha256": digest(editor), "buildToolsVersion": build_tools.name,
                "ndkPropertiesSha256": digest(ndk / "source.properties"),
                "buildDriverSha256": digest(Path(__file__).resolve())}
    selected["key"] = value_hash({k: v for k, v in selected.items() if k not in (
        "editor", "androidSdk", "androidNdk", "jdk", "aapt", "apksigner", "java", "keytool")})
    write_json(output / "toolchain.json", selected)
    return selected


def prepare(args, inputs: dict, output: Path, source: Path, game: Path, *, conversion_python: Path | None = None) -> Path:
    build_progress.operation("recovery", detail="Recovering or reusing the complete original scene and asset catalog")
    stages = Stages(output)
    recovered = None
    recovered_copy_count = None
    def conversion_environment():
        nonlocal conversion_python
        if conversion_python is None:
            import dependencies
            conversion_python = dependencies.python_environment(output / "tool-cache", source,
                                                                 procedural=args.target == "game")
        return conversion_python
    if args.target == "startup":
        if not inputs.get("startupProject"):
            raise BuildError("No validated original startup closure was selected.")
        recovered = output / "inputs/startup" / inputs["startupProject"]["key"]
        startup.inspect_project(recovered, inputs["game"]["key"], inputs["game"]["files"], snapshot_receipt=True)
    if args.target == "game" and inputs.get("campaignProject"):
        recovered = Path(args.campaign_project).resolve()
        campaign.inspect_project(recovered, inputs["game"]["key"], inputs["game"]["files"])
    elif args.target == "game":
        key = recovery_resume.recipe_key(inputs, RECIPE)
        recovered = output / "cache/recovery" / key / "project"

        def recover():
            import dependencies
            launcher = source / "tools/quest-recovery/full_recovery.py"
            if not launcher.is_file():
                raise BuildError("The selected source does not contain the Quest recovery tool.")
            # Import the post-export helpers before starting a potentially long
            # conversion. A fresh Windows CLI has no recovery modules on sys.path.
            recovery_helper_preflight()
            qualifications = {}
            workspace = recovery_resume.select_workspace(output, inputs, source, game, RECIPE, qualifications=qualifications)
            raw_project = workspace / "RecoveredProject"
            raw = recovery_resume.completed_raw(workspace, inputs, game, qualifications=qualifications)
            # Capture 215452 reused all16 raw packages, then failed immediately
            # in canonical_contracts because the launcher contains no UnityPy.
            # Raw reuse skips exporter work, never its staging dependencies.
            python = conversion_environment()
            if raw is None:
                recovery_dotnet = dependencies.dotnet10(output / "tool-cache", source, getattr(args, "recovery_dotnet", None))
                command([str(python), str(launcher), "--game-data", str(game), "--workspace", str(workspace),
                         "--output-project", str(raw_project), "--tool-cache", str(output / "tool-cache/full-recovery"),
                         "--dotnet", str(recovery_dotnet), "--managed-dotnet", str(tool_path(args.dotnet, "dotnet"))],
                        output / "logs" / ("recovery-" + key[:12] + ".log"))
                raw = json.loads((workspace / "full-recovery.json").read_text(encoding="utf-8"))
            else:
                recovery_resume.announce_completed_raw(raw)
            if raw.get("fullOriginalCatalogRecovered") is not True:
                raise BuildError("Full recovery did not finish the original catalog; inspect its bounded checkpoint.")
            build_progress.event("recovery-section:staging", detail="Preparing the recovered full game project", status="start")
            archive = owned_tmp_source_archive(output / "tool-cache/official-tmp")
            stage_owner = recovered.parent / "stage-owner.json"
            expected_owner = {"schema": 1, "owner": "Quest recovered Campaign stage", "key": key,
                              "gameKey": inputs["game"]["key"]}
            _ordinary_owned(stage_owner); _ordinary_owned(recovered)
            if stage_owner.exists():
                if json.loads(stage_owner.read_text()) != expected_owner: raise BuildError("Recovered stage ownership differs.")
            elif recovered.exists(): raise BuildError("Incomplete recovered stage has no matching owner; retained for inspection.")
            else: write_json(stage_owner, expected_owner)
            # All original exports/batch checkpoints above remain verified.
            # Retry only the unfinished derived stage, never the original game.
            metadata = full_assets.stage(raw_project, game, recovered, archive, canonical_project=None,
                canonical_startup=None, managed_types=raw["managedTypes"], cab_bundles=raw["cabBundles"], resume_owner=expected_owner)
            if not (recovered / "Assets").is_dir():
                raise BuildError("Recovery did not produce an actual Unity Assets directory.")
            paths = [p for p in recovered.rglob("*") if p.is_file() and "Library" not in p.parts]
            return paths, {"report": metadata, "project": recovered.relative_to(output).as_posix()}

        recovered_receipt = stages.run("recovery", key, recover)
        copy_root = recovered.relative_to(output).as_posix() + "/"
        recovered_copy_count = sum(row["path"].startswith(copy_root) and not any(part in ("Library", "Temp", "Logs", ".git", ".snapshot.json")
                                   for part in Path(row["path"][len(copy_root):]).parts) for row in recovered_receipt["outputs"])
        audit = recovered_receipt["details"]["report"]
        readiness = audit.get("readiness", {})
        if readiness.get("originalSceneClosureStaged") is not True or readiness.get("fullOriginalCatalogRecovered") is not True:
            raise BuildError("Campaign source recovery is incomplete; no menu-only output can substitute for the full game.")
        refs = audit.get("missingReferences", {})
        if audit.get("unresolvedAddressables") or refs.get("missingGuidCount", 0) or refs.get("duplicateGuidCount", 0):
            raise BuildError("Campaign source recovery contains unresolved native assets; inspect quest-campaign-report.json.")
        if audit.get("managedScriptBindings", {}).get("unexpectedUnresolvedCount", 0) != 0:
            raise BuildError("Recovery has unresolved script bindings; inspect " + str(recovered / "quest-recovery-report.json"))
        build_progress.event("recovery-section:staging", 1, 1, "sections", "Recovered stage and complete output receipt verified", status="complete")
    build_progress.operation("recovery", complete=True, detail="Selected original recovery and readiness contracts verified")
    key = value_hash({"input": inputs["inputKey"], "recipe": RECIPE})
    project = output / "projects" / (import_workspace.workspace_key(inputs, args.target) if args.target == "game" else key)

    restore_project_library(output, project, inputs["inputKey"])
    recover_project_content(output, project, inputs["inputKey"])
    stages = Stages(output)
    content_proofs = []
    content_valid = args.target in ("startup", "game") and project.exists() and project_content_valid(output, project, inputs["inputKey"])
    if content_valid:
        # project_content_valid just read/hash-qualified this exact owned pair
        # under the caller's output lock. Retain those current producer records
        # for the substage check, rather than rereading a multi-GB archive.
        ledger = _ordinary_owned(output / "cache/project-content-transactions" / project.name / "complete.json")
        content = json.loads(ledger.read_text())
        if content.get("schema") != 1 or content.get("inputKey") != inputs["inputKey"] or {row["path"] for row in content.get("files", [])} != set(CONTENT_PATHS):
            raise BuildError("Current mutable preparation content lost its owned input scope.")
        content_proofs = [{**row, "inputKey": inputs["inputKey"], "project": project.relative_to(output).as_posix(),
                           "stamp": list(prepare_resume._stamp(project / row["path"]))} for row in content["files"]]
    elif args.target in ("startup", "game") and project.exists():
        # Missing or damaged mutable outputs need repair, but never a cold
        # reimport of the retained native assets/Library.
        stages.path("prepare", key).unlink(missing_ok=True)

    def generate_files(resume):
        if args.target in ("startup", "game"):
            # A retained complete recovery receipt skips recover() above. This
            # first real preparation work still needs the same private packages.
            conversion_environment()
        resources = project / "Assets/Quest/Resources"
        cab_owners = None
        def owners():
            nonlocal cab_owners
            if cab_owners is None:
                import campaign_shaders
                cab_owners = campaign_shaders.original_cab_bundles(source, game)
            return cab_owners
        def base_project():
            template = source / "unity/GloomhavenVR.Quest"
            if not (template / "Assets/Quest").is_dir():
                raise BuildError("The selected source is missing unity/GloomhavenVR.Quest/Assets/Quest.")
            # Use already captured inventories; do not walk the original project
            # a second time merely to calculate a progress denominator.
            omitted = {"Library", "Temp", "Logs", ".git", ".snapshot.json"}
            source_inventory = inputs.get("campaignProject") or inputs.get("startupProject")
            recovered_files = source_inventory.get("files") if source_inventory else None
            if recovered_copy_count is not None: total = recovered_copy_count
            elif not recovered: total = 0
            elif recovered_files is not None:
                total = sum(not any(part in omitted for part in Path(row["path"]).parts) for row in recovered_files)
            else: total = None
            template_prefix = "unity/GloomhavenVR.Quest/"
            folders = ("Assets", "ProjectSettings") if recovered else ("Assets", "Packages", "ProjectSettings")
            if total is not None:
                total += sum(row["path"].startswith(template_prefix) and row["path"][len(template_prefix):].split("/")[0] in folders
                             for row in inputs["mod"]["files"])
                total += len(inputs["probeAssets"]["files"]) if inputs.get("probeAssets") else 0
                total += 3  # current localization source and two profile resources
            resume.begin_copy(total)
            if recovered:
                shutil.copytree(recovered, project, dirs_exist_ok=True, copy_function=resume.copy, ignore=shutil.ignore_patterns("Library", "Temp", "Logs", ".git", ".snapshot.json"))
                if inputs.get("campaignProject"):
                    campaign.verify_copy(project, inputs["campaignProject"])
                    full_assets.repair_reused_stage(project)
            else:
                project.mkdir(parents=True, exist_ok=True)
            # Retain original built-in modules (video, particles, cloth, etc.) while
            # adding the pinned XR packages. Replacing the recovered manifest would
            # silently remove modules referenced by the original game assemblies.
            if recovered:
                original_packages = project / "Packages/manifest.json"
                package_data = json.loads(original_packages.read_text(encoding="utf-8")) if original_packages.is_file() else {"dependencies": {}}
                additions = json.loads((template / "Packages/manifest.json").read_text(encoding="utf-8"))
                package_data.setdefault("dependencies", {}).update(additions["dependencies"])
                write_json(original_packages, package_data)
            for directory in ("Assets", "ProjectSettings") if recovered else ("Assets", "Packages", "ProjectSettings"):
                origin = template / directory
                if origin.is_dir():
                    shutil.copytree(origin, project / directory, dirs_exist_ok=True, copy_function=resume.copy)
            if inputs.get("probeAssets"):
                probe_root = output / "inputs/probe" / inputs["probeAssets"]["key"]
                shutil.copytree(probe_root, project / "Assets/Quest/Recovered", dirs_exist_ok=True, copy_function=resume.copy,
                                ignore=shutil.ignore_patterns(".snapshot.json"))
            localized = source / "src/GloomhavenVR/Core/Loc/QuestText.cs"
            if not localized.is_file():
                raise BuildError("The selected mod is missing its shared Quest platform localization source.")
            (project / "Assets/Quest/Runtime").mkdir(parents=True, exist_ok=True)
            resume.copy(localized, project / "Assets/Quest/Runtime/QuestText.cs")
            resources = project / "Assets/Quest/Resources"
            resources.mkdir(parents=True, exist_ok=True)
            identity = output / "identities" / inputs["profileKey"]
            for name in ("quest-profile.json", "quest-steam-logo.png"):
                resume.copy(identity / name, resources / name)
            resume.end_copy()
        def contracts(*manifests, extra=()):
            return lambda _: prepare_resume.manifest_contracts(project, manifests, extra=extra)
        prefix = "unity/GloomhavenVR.Quest/"
        base_contracts = [row["path"][len(prefix):] for row in inputs["mod"]["files"] if row["path"].startswith(prefix + "Assets/Quest/")
                          and Path(row["path"]).suffix in (".cs", ".shader", ".asmdef", ".cginc")]
        base_contracts += ["Assets/Quest/Runtime/QuestText.cs", "Assets/Quest/Resources/quest-profile.json",
                           "Assets/Quest/Resources/quest-steam-logo.png", "Packages/manifest.json"]
        if inputs.get("probeAssets"):
            base_contracts += ["Assets/Quest/Recovered/" + row["path"] for row in inputs["probeAssets"]["files"]]
        identities = None
        def original_objects(progress_scope=None):
            nonlocal identities
            if identities is None:
                path = project / "QuestRecovery/original-asset-identities.json"
                if path.is_file() and progress_scope:
                    counter = build_progress.Counter("prepare-items:" + progress_scope + "-identities", path.stat().st_size, "bytes", path.name)
                    raw = bytearray()
                    with path.open("rb") as stream:
                        for chunk in iter(lambda: stream.read(1048576), b""):
                            raw.extend(chunk); counter.add(len(chunk))
                    # The final count closes only after parsing has actually returned.
                    identities = json.loads(raw)["identities"]
                    counter.finish()
                else:
                    identities = json.loads(path.read_text())["identities"] if path.is_file() else []
            return identities
        def class_paths(classes, progress_scope=None):
            return list(dict.fromkeys(row["path"] for row in original_objects(progress_scope)
                         if any(obj.get("classId") in classes for obj in row["objects"])))
        def with_meta(paths):
            return list(dict.fromkeys(name for path in paths for name in (path, path + ".meta")))
        def remap_ledgers():
            result = ["QuestRecovery/original-asset-identities.json"]
            for folder in ("QuestOriginalStartup", "QuestOriginalCampaign"):
                result += ["Assets/" + folder + "/" + name for name in
                           ("startup-addressables.json", "campaign-addressables.json", "script-bindings.json")]
            return result
        def pointer_owners(targets, progress_scope):
            guids = {row["guid"].encode() for row in original_objects(progress_scope) if row["path"] in targets}
            if not guids: return []
            # The existing native identity rows supply the candidates. Preserve
            # actual matching owners, rather than copying every serialized asset.
            owners = []
            records = original_objects()
            counter = build_progress.Counter("prepare-items:" + progress_scope + "-pointer-owners", len(records), "items", "Find affected native reference owners")
            for row in records:
                path = project / row["path"]
                if path.suffix.lower() in (".unity", ".prefab", ".asset", ".mat", ".controller", ".anim", ".playable") and path.is_file():
                    with path.open("rb") as stream:
                        prefix = stream.read(128)
                        # Native texel payloads have no external texture PPtrs.
                        if re.search(rb"--- !u!(?:28|89) &", prefix):
                            counter.add(1, row["path"])
                            continue
                        data = prefix + stream.read()
                    if guids.intersection(re.findall(rb"\bguid:\s*([0-9a-f]{32})\b", data)): owners.append(row["path"])
                counter.add(1, row["path"])
            counter.finish()
            return owners
        def texture_mutations(targets, suffix, progress_scope):
            paths = with_meta(targets + [Path(path).with_suffix(suffix).as_posix() for path in targets])
            paths += pointer_owners(set(targets), progress_scope) + remap_ledgers()
            paths += ["Assets/QuestOriginalCampaign/" + name for name in
                      ("native-sprites.json", "native-cubemaps.json", "native-texture2d.json", "native-platform-images.json",
                       "bundled-audio.json", "native-texture-references.json", "ordinary-texture2d-audit.json")]
            paths += [path.relative_to(project).as_posix() for path in (project / "QuestRecovery").glob("packed-*.json")]
            return paths
        with resume.operation("project-files", 1):
            resume.run("base-project", "project-files", base_project, base_contracts)
        if args.target in ("startup", "game"):
            def loading_resources():
                logo = source / "src/GloomhavenVR/Assets/GloomhavenVR_logo.png"
                if not logo.is_file(): raise BuildError("The selected mod is missing its original GloomhavenVR loading logo.")
                resume.copy(logo, resources / "quest-loading-logo.png")
                if args.target == "game":
                    report = json.loads((recovered / startup.REPORT).read_text())
                    write_json(resources / startup.REPORT, {name: report[name] for name in
                               ("schema", "target", "selectedScenes", "readiness", "unresolvedAddressables", "missingReferences") if name in report})
                else: resume.copy(recovered / startup.REPORT, resources / startup.REPORT)
            def movie_mutations():
                report = json.loads((project / startup.REPORT).read_text())
                video = class_paths({329}) or [path.relative_to(project).as_posix() for path in (project / "Assets/VideoClip").glob("*")
                                              if path.is_file() and path.suffix.lower() in startup.VIDEO_EXTENSIONS]
                return with_meta(video) + report["selectedScenes"] + ["Assets/Quest/Resources/" + startup.MOVIES_REPORT]
            with resume.operation("startup-content", 11 if args.target == "game" else 8):
                resume.run("post-effects", "startup-content", lambda: post_effects.restore_post_effects(project, output / "tool-cache/legacy-post-effects"), contracts(post_effects.RECEIPT))
                resume.run("loading-resources", "startup-content", loading_resources,
                           ["Assets/Quest/Resources/quest-loading-logo.png", "Assets/Quest/Resources/" + startup.REPORT])
                resume.run("startup-movies", "startup-content", lambda: startup.stage_startup_movies(project, game),
                           contracts("Assets/Quest/Resources/" + startup.MOVIES_REPORT), mutations=movie_mutations)
                if args.target == "game":
                    import full_sprites, campaign_shaders
                    resume.run("native-sprites", "startup-content", lambda: full_sprites.stage(project, game, cab_bundles=owners()), contracts("Assets/QuestOriginalCampaign/native-sprites.json"))
                resume.run("loading-sprite", "startup-content", lambda: restore_loading_sprite_geometry(project, game), contracts("QuestStartupEvidence/loading-sprite-geometry.json"))
                resume.run("startup-audio", "startup-content", lambda: stage_startup_audio(project, game), contracts(AUDIO_REPORT, extra=[AUDIO_RESOURCE]))
                if args.target == "game":
                    resume.run("ui-recipes", "startup-content", lambda: ui_assets.stage_campaign_recipe_manifest(project), [ui_assets.RECIPES])
                resume.run("startup-ui", "startup-content", lambda: ui_assets.stage_startup_ui(project), contracts(ui_assets.RECEIPT))
                resume.run("startup-blur", "startup-content", lambda: ui_assets.stage_startup_blur(project), contracts("QuestStartupEvidence/original-ui-blur.json"))
                resume.run("dlc-selection", "startup-content", lambda: dlcs.stage(project, inputs["profile"]["dlcOwnership"]),
                           ["Assets/Quest/Resources/quest-dlc-ownership.json", "QuestStartupEvidence/dlc-content-selection.json"])
                if args.target == "game":
                    resume.run("file-extras", "startup-content", lambda: campaign.stage_file_backed_extras(project, game),
                               lambda result: ["Assets/StreamingAssets/" + path for path in result] or ["Assets/Quest/Resources/" + startup.REPORT])
            if args.target == "game":
                import dependencies, campaign_native, campaign_shaders, full_audio, full_textures, full_texture2d, campaign_compute
                def native_runtime():
                    selected = toolchain(args, output)
                    return campaign_native.stage(source, project, game, output / "tool-cache/campaign-native", Path(selected["androidNdk"]), backend=inputs["proceduralBackend"])
                def native_contracts(_):
                    runtime = json.loads((resources / "quest-procedural-runtime.json").read_text())
                    payload = "Assets/StreamingAssets/ProceduralRuntime/"
                    return prepare_resume.manifest_contracts(project, ["QuestCampaignEvidence/native-runtime.json"], extra=[
                        "Assets/Quest/Resources/quest-procedural-runtime.json", "Assets/Quest/Resources/quest-procedural-native.json",
                        "Assets/StreamingAssets/Quest/procedural-native.json", payload + "runtime-manifest.json"] +
                        [payload + row["path"] for row in runtime["files"]])
                with resume.operation("native-runtime", 1):
                    resume.run("native-runtime", "native-runtime", native_runtime, native_contracts)
                codec = output / "tool-cache/campaign-native-codecs"
                with resume.operation("audio", 1):
                    resume.run("bundled-audio", "audio", lambda: full_audio.stage(project, game, dotnet=tool_path(args.dotnet, "dotnet"), tool_cache=codec, cab_bundles=owners()), contracts("Assets/QuestOriginalCampaign/bundled-audio.json"))
                with resume.operation("textures", 3):
                    resume.run("native-cubemaps", "textures", lambda: full_textures.stage(project, game, dotnet=tool_path(args.dotnet, "dotnet"), tool_cache=codec, cab_bundles=owners()),
                               contracts("Assets/QuestOriginalCampaign/native-cubemaps.json", "Assets/QuestOriginalCampaign/native-platform-images.json", "Assets/QuestOriginalCampaign/native-texture-references.json"),
                               mutations=lambda: texture_mutations(class_paths({89}, "native-cubemaps"), ".asset", "native-cubemaps"))
                    identities = None
                    resume.run("ordinary-texture-audit", "textures", lambda: full_texture2d.audit(project, game, cab_bundles=owners(), output=project / "Assets/QuestOriginalCampaign/ordinary-texture2d-audit.json"),
                               contracts("Assets/QuestOriginalCampaign/ordinary-texture2d-audit.json"))
                    def texture_audit(): return json.loads((project / "Assets/QuestOriginalCampaign/ordinary-texture2d-audit.json").read_text())
                    resume.run("native-texture2d", "textures", lambda: full_texture2d.restore_float_textures(project, game, texture_audit(), dotnet=tool_path(args.dotnet, "dotnet"), tool_cache=codec, cab_bundles=owners()),
                               contracts("Assets/QuestOriginalCampaign/native-texture2d.json", "Assets/QuestOriginalCampaign/native-texture-references.json", "Assets/QuestOriginalCampaign/ordinary-texture2d-audit.json"),
                               mutations=lambda: texture_mutations([row["assetPath"] for row in texture_audit()["assets"] if row["nativeFloatOrHdr"]], ".texture2D", "native-texture2d"))
                identities = None
                def compute_mutations():
                    targets = class_paths({72}, "campaign-compute")
                    return with_meta(targets + [Path(path).with_suffix(".compute").as_posix() for path in targets]) + remap_ledgers() + pointer_owners(set(targets), "campaign-compute") + [
                           "Assets/QuestOriginalCampaign/campaign-computes.json", "QuestStartupEvidence/compute-source-restoration.json"]
                with resume.operation("graphics", 2):
                    resume.run("campaign-compute", "graphics", lambda: campaign_compute.stage(source, project, output / "tool-cache/campaign-compute" / inputs["inputKey"]),
                               contracts("Assets/QuestOriginalCampaign/campaign-computes.json", "QuestStartupEvidence/compute-source-restoration.json"), mutations=compute_mutations)
                    resume.run("campaign-shaders", "graphics", lambda: campaign_shaders.stage(source, project, game, campaign_shader_cache(output, inputs["game"]["key"]), cab_bundles=owners()),
                               contracts("Assets/QuestOriginalCampaign/campaign-shaders.json", "QuestCampaignEvidence/shader-reconstruction.json"),
                               mutations=lambda: with_meta(class_paths({48})) + ["Assets/QuestOriginalCampaign/ShaderPrograms", "Assets/QuestOriginalCampaign/campaign-shaders.json", "QuestCampaignEvidence/shader-reconstruction.json"])
            editor = tool_path(args.unity_editor, "Unity")
            def package_settings():
                package_data = json.loads((project / "Packages/manifest.json").read_text(encoding="utf-8"))
                package_data.setdefault("dependencies", {})["com.unity.addressables"] = "1.19.19"
                package_data["dependencies"].update(original_builtin_modules(game, editor))
                write_json(project / "Packages/manifest.json", package_data)
                restore_ugui_layout_gate(project, game, editor)
            def archive_cleanup():
                manifest = json.loads((project / CONTENT_PATHS[1]).read_text())
                for row in manifest["files"]:
                    path = project / "Assets" / row["path"]
                    _ordinary_owned(path).unlink(missing_ok=True)
                    _ordinary_owned(Path(str(path) + ".meta")).unlink(missing_ok=True)
            with resume.operation("mod-banks", 4):
                resume.run("startup-archive", "mod-banks", lambda: package_startup_content(project, inputs["inputKey"], retain_sources=True), list(CONTENT_PATHS))
                resume.run("archive-cleanup", "mod-banks", archive_cleanup,
                           lambda _: prepare_resume.Contracts(list(CONTENT_PATHS) + ["Assets/" + row["path"] for row in json.loads((project / CONTENT_PATHS[1]).read_text())["files"]],
                                absent=["Assets/" + row["path"] for row in json.loads((project / CONTENT_PATHS[1]).read_text())["files"]]))
                resume.run("package-settings", "mod-banks", package_settings,
                           ["Packages/manifest.json", "QuestStartupEvidence/ugui-layout-gate.json", "Packages/com.unity.ugui/Runtime/UI/Core/Layout/LayoutRebuilder.cs"],
                           mutations=["Packages/manifest.json", "Packages/com.unity.ugui", "QuestStartupEvidence/ugui-layout-gate.json"])
                resume.run("mod-resource-banks", "mod-banks", lambda: package_mod_content(project, inputs, output, source, editor),
                           ["Assets/Quest/Resources/quest-mod-content.json", "Assets/Quest/Resources/quest-mod-bundles.json", "Assets/StreamingAssets/quest-mod-content.zip"])
            def compiler_contracts():
                defines = "GHVR_QUEST_STARTUP;GHVR_QUEST_GAME" if args.target == "game" else "GHVR_QUEST_STARTUP"
                (project / "Assets/csc.rsp").write_text("-define:" + defines + "\n", encoding="utf-8")
                exclude_recovered_package_plugins(project)
                isolate_original_compiler_namespace(project)
            def compiler_paths():
                # Original plug-ins occupy this bounded subtree. Their metadata
                # is intentionally rewritten by these compiler-only adapters.
                return ["Assets/csc.rsp"] + [path.relative_to(project).as_posix()
                        for path in (project / "Assets/Plugins").rglob("*.dll.meta")]
            case_tool = None
            def case_module():
                nonlocal case_tool
                if case_tool is None:
                    spec = importlib.util.spec_from_file_location("quest_prepare_case_paths", source / "tools/quest-recovery/case_paths.py")
                    case_tool = importlib.util.module_from_spec(spec)
                    spec.loader.exec_module(case_tool)
                return case_tool
            def case_mutations():
                module = case_module()
                nodes = module.nodes(project)
                _, _, bundled = module.load_manifests(project)
                moves, _ = module.plan(nodes, bundled)
                paths = [module.RECEIPT, module.VARIANTS]
                paths += [name for name in (module.ADDRESSABLES, module.BINDINGS) if (project / name).is_file()]
                paths += list(module.campaign_manifest_updates(project, moves))
                for name, kind in nodes.items():
                    destination = module.mapped(name, moves)
                    if kind == "file" and name != destination: paths += [name, destination]
                return paths
            def case_contracts(_):
                module = case_module()
                document = json.loads((project / module.RECEIPT).read_text())
                return prepare_resume.manifest_contracts(project,
                    [module.RECEIPT] + list(document["manifestSha256"]))
            def migrate_case_paths():
                command([sys.executable, str(source / "tools/quest-recovery/case_paths.py"), "--project", str(project)],
                        output / "logs" / ("startup-case-paths-" + key[:12] + ".log"))
            def script_orders():
                return startup.stage_startup_script_orders(project, game, source, output / "tool-cache", tool_path(args.dotnet, "dotnet"))
        def final_settings():
            manifest = project / "Assets/StreamingAssets/Quest/input-manifest.json"
            write_json(manifest, inputs)
            write_json(project / "QuestBuilderSettings.json", {"schema": 1, "target": args.target, "inputKey": inputs["inputKey"],
                       "profileSha256": digest(resources / "quest-profile.json"), "package": PACKAGE, "modBuild": inputs["mod"]["modBuild"]})
            import_workspace.stage(project, args.target)
        with resume.operation("preparation-contracts", 5 if args.target == "startup" else 4 if args.target == "game" else 1):
            if args.target in ("startup", "game"):
                resume.run("compiler-contracts", "preparation-contracts", compiler_contracts, lambda _: compiler_paths(), mutations=compiler_paths)
                resume.run("case-paths", "preparation-contracts", migrate_case_paths, case_contracts, mutations=case_mutations)
                if args.target == "startup":
                    resume.run("startup-compute", "preparation-contracts", lambda: command([sys.executable,
                        str(source / "tools/quest-recovery/compute_sources.py"), "--project", str(project),
                        "--cache", str(output / "tool-cache/legacy-compute")], output / "logs" / ("startup-compute-source-" + key[:12] + ".log")),
                        contracts("QuestStartupEvidence/compute-source-restoration.json"))
                resume.run("script-orders", "preparation-contracts", script_orders,
                           contracts("QuestStartupEvidence/script-orders-source.json", "Assets/QuestOriginalStartup/script-orders.json"),
                           mutations=lambda: compiler_paths() + ["QuestStartupEvidence/script-orders-source.json", "Assets/QuestOriginalStartup/script-orders.json"])
            resume.run("final-settings", "preparation-contracts", final_settings,
                       lambda _: prepare_resume.manifest_contracts(project,
                           ([import_workspace.RECEIPT] if (project / import_workspace.RECEIPT).is_file() else []),
                           extra=["QuestBuilderSettings.json", "Assets/StreamingAssets/Quest/input-manifest.json"]),
                       mutations=["QuestBuilderSettings.json", "Assets/StreamingAssets/Quest/input-manifest.json", import_workspace.RECEIPT,
                                  "ProjectSettings/ProjectSettings.asset"])
        manifest = project / "Assets/StreamingAssets/Quest/input-manifest.json"
        settings = project / "QuestBuilderSettings.json"
        import_receipt = project / import_workspace.RECEIPT if (project / import_workspace.RECEIPT).is_file() else None
        # The generated project is mutable under Unity. Cache this content-independent
        # contract and Resources instead of receipts over import-generated .meta files.
        contracts = [settings, manifest, resources / "quest-profile.json", resources / "quest-steam-logo.png"]
        if import_receipt: contracts.append(import_receipt)
        if args.target in ("startup", "game"):
            contracts.extend([resources / "quest-loading-logo.png", resources / startup.REPORT, resources / startup.MOVIES_REPORT, resources / "quest-startup-content.json",
                              resources / "quest-dlc-ownership.json", project / "QuestStartupEvidence/dlc-content-selection.json",
                              project / "QuestStartupEvidence/loading-sprite-geometry.json",
                              project / AUDIO_RESOURCE, project / AUDIO_REPORT,
                              project / ui_assets.RECEIPT,
                              project / "Assets/StreamingAssets/quest-startup-content.zip",
                              project / "QuestStartupEvidence/compute-source-restoration.json",
                              project / "Assets/QuestOriginalStartup/script-orders.json",
                              project / "QuestStartupEvidence/script-orders-source.json"])
            contracts.extend(startup_shader_contracts(project))
            contracts.extend(project / row["assetPath"] for row in
                             json.loads((project / ui_assets.RECEIPT).read_text(encoding="utf-8"))["shaders"])
            contracts.extend(project / row["assetPath"] for row in
                             json.loads((project / AUDIO_REPORT).read_text(encoding="utf-8"))["clips"])
            contracts.extend([project / "QuestStartupEvidence/ugui-layout-gate.json",
                              project / "Packages/com.unity.ugui/Runtime/UI/Core/Layout/LayoutRebuilder.cs"])
            contracts.extend([resources / "quest-mod-content.json", resources / "quest-mod-bundles.json",
                              project / "Assets/StreamingAssets/quest-mod-content.zip"])
            if args.target == "game":
                contracts.extend([project / "QuestCampaignEvidence/native-runtime.json", resources / "quest-procedural-runtime.json",
                                  resources / "quest-procedural-native.json", project / "Assets/StreamingAssets/Quest/procedural-native.json",
                                  project / "Assets/QuestOriginalCampaign/bundled-audio.json"])
                contracts.append(project / "Assets/QuestOriginalCampaign/native-cubemaps.json")
                contracts.extend([project / "Assets/QuestOriginalCampaign/native-sprites.json",
                                  project / "Assets/QuestOriginalCampaign/campaign-computes.json",
                                  project / "Assets/QuestOriginalCampaign/native-platform-images.json",
                                  project / "Assets/QuestOriginalCampaign/native-texture2d.json",
                                  project / "Assets/QuestOriginalCampaign/native-texture-references.json",
                                  project / "Assets/QuestOriginalCampaign/ordinary-texture2d-audit.json"])
                native = json.loads((project / "QuestCampaignEvidence/native-runtime.json").read_text())
                contracts.extend(project / row["path"] for row in native["nativeFiles"])
        contracts.extend(p for p in (project / "Assets/Quest").rglob("*")
                         if p.is_file() and p.suffix in (".cs", ".shader", ".asmdef", ".cginc"))
        if inputs.get("probeAssets"):
            contracts.extend(project / "Assets/Quest/Recovered" / item["path"]
                             for item in inputs["probeAssets"]["files"])
        # Addressables repacks these build-owned outputs and Campaign temporarily
        # moves the archive outside StreamingAssets. Their transaction journal is
        # separate from this immutable preparation contract.
        contracts = [path for path in contracts if path.relative_to(project).as_posix() not in CONTENT_PATHS]
        return sorted(set(contracts)), {
            "project": project.relative_to(output).as_posix(), "target": args.target,
            "isDiagnostic": args.target != "game", "isDummy": bool(inputs["profile"].get("isDummy"))}

    prior_preparation_key = preparation_identity.rebind_key(
        output, project, inputs, source, target=args.target, recipe=RECIPE, recovery=recovery_resume)

    def generate():
        def reset():
            # Only a new input/recipe or older uncheckpointed project enters this
            # path. Same-input interruptions always retain project and Library.
            with regenerate_project(output, project, inputs["inputKey"]):
                project.mkdir(parents=True, exist_ok=True)
        sources = [(source / row["path"], row) for row in inputs["mod"]["files"]]
        sources += [(game / row["path"], row) for row in inputs["game"]["files"]]
        build_progress.operation("project-files", detail="Qualifying retained preparation checkpoints")
        resume = prepare_resume.Preparation(output, project, input_key=inputs["inputKey"], target=args.target,
                                           recipe=RECIPE, source_files=sources, progress=build_progress, reset=reset,
                                           content_proofs=content_proofs, compatible_input_key=prior_preparation_key,
                                           current_inputs=inputs)
        try:
            result = generate_files(resume)
            if args.target in ("startup", "game"): publish_project_content(output, project, inputs["inputKey"])
            resume.finish()
            return result
        finally:
            resume.close()

    stages.run("prepare", key, generate)
    build_progress.operation("preparation-contracts", complete=True, detail="Prepared Unity project and complete content contracts verified")
    return project


def campaign_shader_cache(output: Path, game_key: str) -> Path:
    if not isinstance(game_key, str) or not re.fullmatch(r"[0-9a-f]{64}", game_key):
        raise BuildError("Campaign shader cache requires the owned game hash.")
    # Keep the former campaign-shaders tree untouched. Native shader receipts
    # and identities are unchanged; a short host path avoids Win32 MAX_PATH.
    return output / "tool-cache/cs" / game_key


def startup_shader_contracts(project: Path) -> list[Path]:
    """Validate restored shader bytes outside the Quest template on cache reuse."""
    assets = project / "Assets"
    return sorted(path for path in assets.rglob("*") if path.is_file() and
                  (path.suffix in (".compute", ".cginc") or path.name.endswith(".compute.meta")))


def recovery_helper_preflight():
    """Resolve the same local helpers used for staging before running exports."""
    try:
        return full_assets._modules()
    except ImportError as error:
        raise BuildError("Quest staging helper import failed before game conversion: " + str(error)) from error


def owned_tmp_source_archive(cache: Path) -> Path:
    """Acquire only the pinned official shader source package, never game data."""
    import dependencies
    # The shared loader supplies recover/md4 and avoids depending on imports
    # accidentally performed by another target or the test runner.
    module = recovery_helper_preflight()[4]
    cache.mkdir(parents=True, exist_ok=True)
    archive = cache / "com.unity.textmeshpro-3.0.6.tgz"
    dependencies.download_sdk({"url": module.TMP_URL, "hash": module.TMP_SHA256}, archive, algorithm="sha256")
    if digest(archive) != module.TMP_SHA256:
        raise BuildError("Cached official TMP shader source changed; the private archive needs replacement.")
    return archive


def package_startup_content(project: Path, input_key: str, *, retain_sources=False) -> dict:
    """Keep original file-based reads usable on Android, with an exact byte contract."""
    streaming = project / "Assets/StreamingAssets"
    streaming.mkdir(parents=True, exist_ok=True)
    archive_path = streaming / "quest-startup-content.zip"
    sources = sorted(path for path in streaming.rglob("*") if path.is_file() and
                     path.suffix != ".meta" and path != archive_path and
                     "Quest" not in path.relative_to(streaming).parts)
    if not any(path.relative_to(streaming).parts[0] == "Rulebase" for path in sources):
        raise BuildError("Original startup requires real Rulebase files; no empty content success is allowed.")
    records = []
    with zipfile.ZipFile(archive_path, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for path in sources:
            name = "StreamingAssets/" + path.relative_to(streaming).as_posix()
            record = {"path": name, "sha256": digest(path), "size": path.stat().st_size}
            records.append(record)
            # Fixed ZIP timestamps prevent cache identity changing with the export time.
            info = zipfile.ZipInfo(name, date_time=(2020, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            with path.open("rb") as source, archive.open(info, "w") as destination:
                shutil.copyfileobj(source, destination, 1024 * 1024)
    manifest = {"schema": 1, "inputKey": input_key, "archive": archive_path.name,
                "archiveSha256": digest(archive_path), "files": records}
    write_json(project / "Assets/Quest/Resources/quest-startup-content.json", manifest)
    # Ship exactly one copy of file-backed data. Unity assets continue through
    # their native Android import/bundle path; the original game installation is untouched.
    if not retain_sources:
        for path in sources:
            path.unlink()
            Path(str(path) + ".meta").unlink(missing_ok=True)
    return manifest


def validate_mod_bundle(folder: Path, authored: Path, source_files: list[dict], *, full_game=False) -> dict:
    """Verify native Android art and all declared compiler inputs on cache reuse."""
    if full_game:
        try:
            generated = json.loads((authored / "quest-owned-environment.json").read_text())
        except (OSError, ValueError) as error:
            raise BuildError("Owned environment compiler-input owner is missing or unreadable.") from error
        return mod_assets.validate_bundle_set(folder, authored, source_files, full_game=True, generated=generated)
    receipt = json.loads((folder / "quest-mod-bundles.json").read_text(encoding="utf-8"))
    contract = {"schema": 1, "target": "Android", "unityVersion": "2021.3.5f1",
                "bundleName": "gloomhavenvr.bundle", "graphicsApi": "OpenGLES3",
                "colorSpace": "Linear", "stereoRenderingPath": "SinglePass",
                "typeTreesEnabled": True, "chunkBasedCompression": True, "townBanksIncluded": False}
    if any(receipt.get(name) != value for name, value in contract.items()):
        raise BuildError("Authored mod bundle has an unsupported Android rendering contract.")
    names = receipt.get("assetNames", [])
    required = receipt.get("requiredAssetNames", [])
    if (not names or len(names) != len(set(names)) or not required or len(required) != len(set(required))
            or not set(required).issubset(names)
            or any(not name.startswith("Assets/Bundle/") or "\\" in name or ":" in name
                   or any(part in ("", ".", "..", "TownServices") for part in name.split("/")) for name in names)):
        raise BuildError("Authored mod bundle is missing its required menu/rig assets.")
    known = {row["path"]: row for row in source_files}
    declared = receipt.get("sourceFiles", [])
    if not declared or len(declared) != len({row["path"] for row in declared}):
        raise BuildError("Authored mod bundle compiler-input receipt is empty or duplicated.")
    for row in declared:
        if row != known.get(row.get("path")) or not row["path"].startswith("Assets/"):
            raise BuildError("Authored mod bundle references unknown or changed compiler input.")
    if not set(names).issubset(row["path"] for row in declared):
        raise BuildError("Authored mod bundle names art absent from its compiler-input receipt.")
    if not verify_files(authored, declared):
        raise BuildError("Authored mod art changed during Android compilation.")
    bundle = receipt.get("bundle", {})
    path = folder / "gloomhavenvr.bundle"
    if bundle != record_file(path, "gloomhavenvr.bundle"):
        raise BuildError("Authored Android bundle bytes differ from their receipt.")
    with path.open("rb") as stream:
        if stream.read(8) != b"UnityFS\0":
            raise BuildError("Authored mod art is not a real Unity AssetBundle.")
    return receipt


@contextmanager
def _mod_bank_workspace(output: Path, root: Path, key: str, files: list[dict]):
    """Regenerate only this keyed mod project while retaining its imported Library.

    Publish ownership before the first Library rename. A killed copy or compiler
    leaves its backup beside the project, so the next retry restores the same
    imports without relaxing the ordinary Player-project lifecycle contract.
    """
    output, root = _ordinary_owned(output), _ordinary_owned(root)
    if root != output / "cache/mod-bundle" / key or not re.fullmatch(r"[0-9a-f]{64}", key):
        raise BuildError("Unexpected authored Android mod workspace identity.")
    authored, backup = _ordinary_owned(root / "project"), _ordinary_owned(root / "Library")
    library, marker = _ordinary_owned(authored / "Library"), _ordinary_owned(root / "project-owner.json")
    expected = {"schema": 1, "owner": "Quest authored Android mod project", "key": key, "files": files}
    if marker.exists():
        if json.loads(marker.read_text()) != expected:
            raise BuildError("Authored Android mod project owner differs; retained for inspection.")
    else:
        # The previous recipe used snapshot ownership. Only that exact completed
        # snapshot can establish ownership of a pre-existing project/Library.
        if root.exists() and any(root.iterdir()):
            snapshot_owner = authored / ".snapshot.json"
            if backup.exists() or not snapshot_owner.is_file() or json.loads(snapshot_owner.read_text()).get("files") != files:
                raise BuildError("Existing authored Android mod workspace has no matching owner.")
        write_json(marker, expected)
    if backup.exists() and library.exists():
        raise BuildError("Two retained authored Android Unity Libraries exist; neither was deleted.")
    if library.exists(): os.replace(library, backup)
    try:
        if authored.exists(): shutil.rmtree(authored)
        yield
    finally:
        if backup.exists():
            authored.mkdir(parents=True, exist_ok=True)
            if library.exists(): raise BuildError("Authored preparation created a second Unity Library; retained both.")
            os.replace(backup, library)


def package_mod_content(project: Path, inputs: dict, output: Path, source: Path, editor: Path, *, owned_game_source=None) -> dict:
    """Build current authored mod art independently of changing gameplay code."""
    prefix = "unity/GloomhavenVR.Assets/"
    files = [{**row, "path": row["path"][len(prefix):]} for row in inputs["mod"]["files"]
             if row["path"].startswith(prefix) and row["path"][len(prefix):].split("/")[0]
             in ("Assets", "Packages", "ProjectSettings")]
    if not any(row["path"] == "Assets/Editor/QuestModBundles.cs" for row in files):
        raise BuildError("The selected source is missing the authored Android mod bundle recipe.")
    full_game = inputs.get("target") == "game"
    environment = _local_helper("environment_bank") if full_game else None
    environment_plan = environment.plan(inputs["game"], inputs["mod"]["files"]) if environment else None
    key = value_hash({"files": files, "editorSha256": digest(editor), "fullGame": full_game, "recipe": RECIPE,
                      "ownedEnvironment": environment_plan})
    root = output / "cache/mod-bundle" / key
    authored, bundles = root / "project", root / "bundles"

    def compile_art():
        with _mod_bank_workspace(output, root, key, files):
            if bundles.exists(): shutil.rmtree(_ordinary_owned(bundles))
            snapshot(source / prefix, files, authored)
            if environment:
                environment.stage(source, owned_game_source or output / "inputs/game" / inputs["game"]["key"], authored, output,
                                  inputs["game"], inputs["mod"]["files"])
        env = dict(os.environ)
        env["GHVR_QUEST_MOD_BUNDLE_OUTPUT"] = str(bundles)
        env["GHVR_QUEST_MOD_FULL_GAME"] = "1" if full_game else "0"
        command(unity_launcher(editor, graphics=full_game) + ["-projectPath", str(authored),
                 "-buildTarget", "Android", "-executeMethod", "GloomhavenVR.QuestModBundles.BuildAll",
                 "-logFile", str(output / "logs" / ("mod-bundle-" + key[:12] + ".log"))],
                output / "logs" / ("mod-bundle-launch-" + key[:12] + ".log"), env=env)
        receipt = validate_mod_bundle(bundles, authored, files, full_game=full_game)
        return [bundles / row["path"] for row in mod_assets.bundle_records(receipt)] + [bundles / "quest-mod-bundles.json"], receipt

    Stages(output).run("mod-bundle", key, compile_art)
    receipt = validate_mod_bundle(bundles, authored, files, full_game=full_game)
    streaming = project / "Assets/StreamingAssets"
    streaming.mkdir(parents=True, exist_ok=True)
    archive_path = streaming / "quest-mod-content.zip"
    records = [{**row, "path": "StreamingAssets/" + row["path"]} for row in mod_assets.bundle_records(receipt)]
    with zipfile.ZipFile(archive_path, "w", compression=zipfile.ZIP_STORED) as archive:
        for record in records:
            info = zipfile.ZipInfo(record["path"], date_time=(2020, 1, 1, 0, 0, 0))
            with (bundles / Path(record["path"]).name).open("rb") as original, archive.open(info, "w") as destination:
                shutil.copyfileobj(original, destination, 1024 * 1024)
    manifest = {"schema": 1, "inputKey": inputs["inputKey"], "archive": archive_path.name,
                "archiveSha256": digest(archive_path), "files": records}
    resources = project / "Assets/Quest/Resources"
    write_json(resources / "quest-mod-content.json", manifest)
    write_json(resources / "quest-mod-bundles.json", receipt)
    return manifest


def exclude_recovered_package_plugins(project: Path) -> None:
    """Keep original script assets for exact remapping; compile one copy of each package."""
    for name in ("UnityEngine.UI", "Unity.InputSystem", "Unity.Addressables", "Unity.ResourceManager", "Unity.ScriptableBuildPipeline"):
        for path in (project / "Assets").rglob(name + ".dll.meta"):
            text = path.read_text(encoding="utf-8")
            if "PluginImporter:" not in text:
                raise BuildError("Recovered package plugin has no import contract: " + str(path.relative_to(project)))
            text = re.sub(r"(?m)^([ \t]+enabled:)[ \t]*1[ \t]*$", r"\1 0", text)
            path.write_text(text, encoding="utf-8")


def restore_ugui_layout_gate(project: Path, game: Path, editor: Path) -> None:
    """Restore the owned game's layout batching ABI in a private embedded package.

    ObjectPool temporarily disables MarkLayoutForRebuild while reparenting cards.
    Ignoring its setter changes behavior; shipping unmodified UGUI calls an absent
    method. The audited original getter/setter and default are a static bool, with
    an early return before all MarkLayoutForRebuild work. Editor and game stay read-only.
    """
    source = editor.parent / "Data/Resources/PackageManager/BuiltInPackages/com.unity.ugui"
    relative = Path("Runtime/UI/Core/Layout/LayoutRebuilder.cs")
    original = game / "Managed/UnityEngine.UI.dll"
    if not original.is_file() or digest(original) != ORIGINAL_UGUI_SHA256:
        raise BuildError("Original UGUI layout gate ABI changed; audit the owned assembly before rebuilding.")
    if not (source / relative).is_file() or digest(source / relative) != UGUI_LAYOUT_SOURCE_SHA256:
        raise BuildError("Selected Unity UGUI layout source changed; audit its batching gate before rebuilding.")
    text = (source / relative).read_text(encoding="utf-8")
    declaration = "    public class LayoutRebuilder : ICanvasElement\n    {\n"
    method = "        public static void MarkLayoutForRebuild(RectTransform rect)\n        {\n"
    if text.count(declaration) != 1 or text.count(method) != 1:
        raise BuildError("Audited UGUI layout source anchors changed.")
    text = text.replace(declaration, declaration +
                        "        // Owned Gloomhaven batching contract; default and guard match original IL.\n"
                        "        public static bool Enable { get; set; } = true;\n\n")
    text = text.replace(method, method + "            if (!Enable) return;\n")
    target = project / "Packages/com.unity.ugui"
    if target.exists():
        raise BuildError("Generated UGUI package already exists; regenerate the isolated project.")
    # Embedded packages also compile their loose Tests sources. Built-in UGUI
    # relies on Unity's own test environment, which the player project does not
    # have. Retain its runtime/editor/package files, excluding only that test tree.
    shutil.copytree(source, target, ignore=shutil.ignore_patterns("Tests", "Tests.meta"))
    (target / relative).write_text(text, encoding="utf-8")
    write_json(project / "QuestStartupEvidence/ugui-layout-gate.json", {
        "schema": 1, "originalAssemblySha256": digest(original),
        "sourceSha256": UGUI_LAYOUT_SOURCE_SHA256, "derivedSha256": digest(target / relative),
        "defaultEnabled": True, "guard": "MarkLayoutForRebuild early return when disabled",
        "builtinTestSourcesExcluded": True,
        "scope": "private generated UGUI package; original game and editor unchanged"})


def weave(args, inputs: dict, output: Path, source: Path, game: Path, project: Path) -> None:
    if args.target not in ("game", "startup"):
        return
    dotnet = tool_path(args.dotnet, "dotnet")
    key = value_hash({"game": inputs["game"]["key"], "mod": inputs["mod"]["key"],
                      "target": args.target, "profile": inputs["profileKey"], "recipe": RECIPE})
    root = output / "cache/weave" / key
    staged = root / "Managed"
    report = root / "weave-report.json"

    def run_weaver():
        mod_project = source / "src/GloomhavenVR/GloomhavenVR.csproj"
        dll_dir = root / "mod"
        command([str(dotnet), "build", str(mod_project), "--configuration", "Release",
                 "--output", str(dll_dir), "-p:GameManaged=" + str(game / "Managed")],
                output / "logs" / ("mod-" + key[:12] + ".log"), cwd=source)
        dll = dll_dir / "GloomhavenVR.dll"
        if not dll.is_file():
            raise BuildError("The selected mod build did not produce GloomhavenVR.dll.")
        weaver_project = source / "tools/QuestWeaver/QuestWeaver.csproj"
        if not weaver_project.is_file():
            raise BuildError("The selected source does not contain the static Quest weaver.")
        command([str(dotnet), "run", "--project", str(weaver_project), "--configuration", "Release", "--",
                 "weave", "--mod", str(dll), "--managed", str(game / "Managed"),
                 "--output", str(staged), "--report", str(report)],
                output / "logs" / ("weave-" + key[:12] + ".log"), cwd=source)
        if not report.is_file() or not staged.is_dir():
            raise BuildError("The weaver produced no report/managed output; missing hooks are not a successful build.")
        result = json.loads(report.read_text(encoding="utf-8"))
        # CLI failures are mandatory. Also fail closed if the reported audit says it
        # cannot emit complete semantics, regardless of a zero external exit status.
        if result.get("version") != 1 or result.get("complete") is not True or result.get("issues"):
            raise BuildError("The static integration report contains unsupported behavior; inspect " + str(report))
        if result.get("modSha256") != digest(dll):
            raise BuildError("The static integration report does not match the freshly built selected mod.")
        link = staged / "link.xml"
        if not link.is_file():
            raise BuildError("The weaver did not produce its required AOT preservation link.xml.")
        return [report, link, *sorted(staged.glob("*.dll"))], {"report": result}

    Stages(output).run("weave", key, run_weaver)
    deploy_woven_assemblies(staged, game, project)
    if args.target in ("startup", "game"):
        compatibility = root / "Standalone"
        compatibility_report = root / "standalone-report.json"

        def adapt_startup():
            assets = source / "src/GloomhavenVR/obj/project.assets.json"
            bepinex = resolved_bepinex_runtime(assets)
            command([str(dotnet), "run", "--project", str(source / "tools/QuestWeaver/QuestWeaver.csproj"),
                     "--configuration", "Release", "--", "standalone", "--standalone-target", args.target,
                     "--managed", str(game / "Managed"), "--overrides", str(staged),
                     "--profile", str(project / "Assets/Quest/Resources/quest-profile.json"),
                     "--bepinex", str(bepinex), "--mod", str(root / "mod/GloomhavenVR.dll"),
                     "--output", str(compatibility), "--report", str(compatibility_report)],
                    output / "logs" / ("standalone-" + key[:12] + ".log"), cwd=source)
            if not compatibility_report.is_file():
                raise BuildError("Standalone conversion produced no original-startup evidence.")
            report = json.loads(compatibility_report.read_text(encoding="utf-8"))
            if report.get("startupAdapterComplete") is not True or report.get("fullGameReady") is not False or report.get("issues"):
                raise BuildError("Original startup adaptation is incomplete; inspect " + str(compatibility_report))
            return [compatibility_report, *sorted(p for p in compatibility.rglob("*") if p.is_file())], {"report": report}

        Stages(output).run("standalone", key, adapt_startup)
        deploy_woven_assemblies(compatibility, game, project, link_name="Standalone/link.xml")
        shutil.copyfile(compatibility_report, project / "Assets/Quest/Resources/quest-standalone-report.json")
        if args.target == "game":
            stage_campaign_runtime_assembly(project)


def original_builtin_modules(game: Path, editor: Path) -> dict:
    """Restore the owned player's native modules using the selected editor's catalog.

    A recovered scene export need not contain its original Packages directory.
    Missing modules otherwise silently discard serialized ParticleSystems, video,
    cloth and other genuine original components during Android bundle building.
    """
    catalog = editor.parent / "Data/Resources/PackageManager/BuiltInPackages"
    if not catalog.is_dir():
        raise BuildError("The selected Unity editor has no built-in module package catalog.")
    required = {"com.unity.modules." + path.stem[len("UnityEngine."):-len("Module")].lower()
                for path in (game / "Managed").glob("UnityEngine.*Module.dll")}
    dependencies = {}
    for package in sorted(catalog.glob("com.unity.modules.*/package.json")):
        metadata = json.loads(package.read_text(encoding="utf-8"))
        name, version = metadata.get("name"), metadata.get("version")
        if metadata.get("type") == "module" and name in required:
            if not isinstance(version, str) or not version:
                raise BuildError("A selected Unity built-in module has no version: " + str(name))
            dependencies[name] = version
    return dependencies


def isolate_original_compiler_namespace(project: Path) -> None:
    """Keep the original global Debug wrapper out of unrelated package compilation.

    Auto Reference only controls C# compiler references. The recovered plugin's
    platform availability, GUID and original runtime dependencies are retained.
    """
    matches = list((project / "Assets").rglob("GH.Runtime.dll.meta"))
    if len(matches) != 1:
        raise BuildError("Original startup needs one recovered GH.Runtime plugin importer.")
    metadata = matches[0].read_text(encoding="utf-8")
    metadata, count = re.subn(r"(?m)^([ \t]*isExplicitlyReferenced:)[ \t]*[01][ \t]*$", r"\1 1", metadata)
    if count != 1:
        raise BuildError("The recovered GH.Runtime plugin has no unique compiler-reference setting.")
    matches[0].write_text(metadata, encoding="utf-8")


def stage_campaign_runtime_assembly(project: Path) -> Path:
    """Give only the Quest runtime explicit access to the original game API.

    The original global Debug wrapper must remain hidden from Unity package
    compilation. A generated assembly definition references its plugin locally,
    while package and desktop source/importer settings remain unchanged.
    """
    runtime = project / "Assets/Quest/Runtime"
    if not runtime.is_dir():
        raise BuildError("The Quest runtime source folder is missing.")
    plugins = []
    for path in (project / "Assets").rglob("*.dll"):
        if path.stem not in REPLACED_PACKAGES:
            plugins.append(path.name)
    if len(set(plugins)) != len(plugins) or not {"GH.Runtime.dll", "GloomhavenVR.dll", "QuestGame.Compatibility.dll"}.issubset(plugins):
        raise BuildError("Campaign runtime needs unique original game, current mod and compatibility plugin references.")
    target = runtime / "QuestGame.Campaign.asmdef"
    write_json(target, {"name": "QuestGame.Campaign", "rootNamespace": "GloomhavenVR.Quest",
        "references": ["Unity.InputSystem", "Unity.Addressables", "Unity.ResourceManager", "UnityEngine.UI",
                       "Unity.TextMeshPro", "Unity.XR.Management", "Unity.XR.OpenXR", "Unity.XR.CoreUtils"],
        "includePlatforms": [], "excludePlatforms": [], "allowUnsafeCode": False,
        "overrideReferences": True, "precompiledReferences": sorted(plugins), "autoReferenced": True,
        "defineConstraints": ["GHVR_QUEST_GAME"], "versionDefines": [], "noEngineReferences": False})
    return target


def resolved_bepinex_runtime(assets: Path) -> Path:
    """Select the real restored package variant, never the metadata-only CI reference."""
    if not assets.is_file():
        raise BuildError("The selected mod has no restored NuGet assets; build it before standalone adaptation.")
    document = json.loads(assets.read_text(encoding="utf-8"))
    candidates = []
    for name, package in document.get("libraries", {}).items():
        if name.lower().startswith("bepinex.baselib/") and package.get("type") == "package":
            relative = package.get("path")
            if not isinstance(relative, str) or Path(relative).is_absolute() or ".." in Path(relative).parts:
                raise BuildError("The restored BepInEx package path is invalid.")
            for folder in document.get("packageFolders", {}):
                candidate = Path(folder) / relative / "lib/netstandard2.0/BepInEx.dll"
                if candidate.is_file():
                    candidates.append(candidate.resolve())
    if len(set(candidates)) != 1:
        raise BuildError("The selected mod needs one restored BepInEx.BaseLib netstandard2.0 runtime for Android adaptation.")
    return candidates[0]


def deploy_woven_assemblies(staged: Path, game: Path, project: Path, *, link_name: str = "link.xml") -> None:
    """Retain recovered plugin identity; add only genuinely new assemblies."""
    destination = project / "Assets/Plugins/QuestGame"
    destination.mkdir(parents=True, exist_ok=True)
    for dll in staged.glob("*.dll"):
        if (game / "Managed" / dll.name).is_file():
            # AssetRipper retains the original MonoScript-to-plugin GUID mapping.
            # A second copy would introduce duplicate types and break that identity.
            matches = [p for p in (project / "Assets").rglob(dll.name) if p.is_file()]
            if len(matches) != 1 or not Path(str(matches[0]) + ".meta").is_file():
                raise BuildError("A rewritten original assembly needs one recovered DLL and its retained .meta: " + dll.name)
            shutil.copyfile(dll, matches[0])
        else:
            shutil.copyfile(dll, destination / dll.name)
    link = project / "Assets/Quest/Generated" / link_name
    link.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(staged / "link.xml", link)


def signing(output: Path, tools: dict) -> tuple[Path, dict]:
    folder = output / "signing"
    folder.mkdir(parents=True, exist_ok=True)
    if os.name != "nt":
        folder.chmod(0o700)
    key = folder / "quest.keystore"
    secret_file = folder / "local-key.json"
    if key.exists() != secret_file.exists():
        raise BuildError("The signing key/password pair is incomplete; restore it before building updates.")
    if secret_file.exists():
        private = json.loads(secret_file.read_text(encoding="utf-8"))
    else:
        private = {"password": secrets.token_urlsafe(32), "alias": "quest"}
        # A local secret is never copied into the source snapshot, manifest or APK.
        write_json(secret_file, private)
        if os.name != "nt":
            secret_file.chmod(0o600)
    env = dict(os.environ)
    env["GHVR_QUEST_SIGNING_PASSWORD"] = private["password"]
    if not key.exists():
        try:
            command([tools["keytool"], "-genkeypair", "-keystore", str(key), "-alias", private["alias"],
                     "-keyalg", "RSA", "-keysize", "2048", "-validity", "10000", "-dname",
                     "CN=GloomhavenVR Local Quest Build", "-storepass:env", "GHVR_QUEST_SIGNING_PASSWORD",
                     "-keypass:env", "GHVR_QUEST_SIGNING_PASSWORD", "-noprompt"],
                    output / "logs/keytool.log", env=env)
        except BaseException:
            if not key.exists():
                secret_file.unlink(missing_ok=True)
            raise
        if os.name != "nt":
            key.chmod(0o600)
    return key, private


def validate_apk(apk: Path, report: Path, inputs: dict, tools: dict, output: Path,
                 expected_provenance: dict | None = None) -> dict:
    if not apk.is_file() or not report.is_file():
        raise BuildError("Unity did not produce both the APK and its .build.json evidence.")
    metadata = json.loads(report.read_text(encoding="utf-8"))
    if expected_provenance is not None and metadata.get("buildProvenance") != expected_provenance:
        raise BuildError("Unity build evidence lost the actual staged build-tool provenance.")
    expected = {"schema": 1, "target": inputs["target"], "inputKey": inputs["inputKey"],
                "package": PACKAGE, "profileSha256": digest(
                    output / "identities" / inputs["profileKey"] / "quest-profile.json"),
                "unityVersion": tools["unityVersion"], "buildResult": "Succeeded"}
    if inputs["target"] == "game":
        expected["graphicsApi"] = "Vulkan"
    for field, value in expected.items():
        if metadata.get(field) != value:
            raise BuildError("Unity build evidence disagrees with selected inputs: " + field)
    with zipfile.ZipFile(apk) as archive:
        bad = archive.testzip()
        if bad:
            raise BuildError("APK ZIP integrity failed: " + bad)
        names = archive.namelist()
        for required in ("AndroidManifest.xml", "lib/arm64-v8a/libil2cpp.so", "lib/arm64-v8a/libunity.so",
                         "lib/arm64-v8a/libghvr_quest_passthrough.so", "lib/arm64-v8a/libUnityOpenXR.so",
                         "lib/arm64-v8a/libopenxr_loader.so"):
            if required not in names:
                raise BuildError("The APK is not a genuine ARM64 IL2CPP Unity player: missing " + required)
        if any(name.startswith("lib/") and not name.startswith("lib/arm64-v8a/") for name in names if name.endswith(".so")):
            raise BuildError("The initial Quest APK must contain only ARM64 native binaries.")
        for name in names:
            if not name.startswith("lib/") or not name.endswith(".so"):
                continue
            with archive.open(name) as native:
                header = native.read(64)
            if (len(header) < 64 or header[:7] != b"\x7fELF\x02\x01\x01" or
                    struct.unpack_from("<HHI", header, 16) != (3, 183, 1)):
                raise BuildError("APK native library is not an ELF64 little-endian AArch64 shared object: " + name)
        if not any(name.endswith("/global-metadata.dat") for name in names):
            raise BuildError("The APK lacks IL2CPP metadata.")
        content_files = []
        if inputs["target"] == "game":
            native_path = "assets/Quest/procedural-native.json"
            if native_path not in names or archive.getinfo(native_path).file_size > 65536:
                raise BuildError("Complete Campaign APK lacks its signed procedural native program inventory.")
            native_contract = json.loads(archive.read(native_path))
            selected_backend = inputs.get("proceduralBackend")
            native_rows = native_plugins.validate(native_contract, backend=selected_backend)
            if metadata.get("proceduralBackend") != native_contract["backend"] or metadata.get("stagedProceduralNativeFiles") != native_rows:
                raise BuildError("Player evidence differs from its signed native backend inventory.")
            required_names = {"lib/arm64-v8a/" + row["path"][len(native_plugins.PREFIX):] for row in native_rows}
            for required in required_names:
                if names.count(required) != 1:
                    raise BuildError("Complete Campaign APK lacks or repeats its native game ABI: " + required)
            other_backend_names = {"lib/arm64-v8a/" + name for name in
                (("libquest_box64.so", "libquest_wineserver.so") if native_contract["backend"] == "proton-arm64ec-fex"
                 else tuple(native_plugins.PROTON_REQUIRED))}
            if other_backend_names.intersection(names):
                raise BuildError("Campaign APK contains native programs from a different procedural backend.")
            path = "assets/Quest/content-delivery.json"
            if path not in names or archive.getinfo(path).file_size > 65536:
                raise BuildError("Complete Campaign APK lacks its signed content delivery contract.")
            delivery = json.loads(archive.read(path))
            rows = delivery.get("files")
            if delivery.get("schema") != 1 or delivery.get("inputKey") != inputs["inputKey"] or delivery.get("package") != PACKAGE or not isinstance(rows, list) or len(rows) != 1:
                raise BuildError("Campaign content delivery differs from this APK.")
            for row in rows:
                if (row.get("file") != "GloomhavenVR-Quest-content.zip" or row.get("archive") != "quest-startup-content.zip"
                        or not re.fullmatch(r"[0-9a-f]{64}", str(row.get("sha256", ""))) or type(row.get("size")) is not int):
                    raise BuildError("Campaign content bank identity is invalid.")
                bank = apk.parent / row["file"]
                if bank.is_symlink() or not bank.is_file() or bank.stat().st_size != row["size"]:
                    raise BuildError("Complete Campaign content bank changed or is missing.")
                bank_before = bank.stat()
                bank_record = record_file(bank, bank.relative_to(output).as_posix())
                if bank_record["sha256"] != row["sha256"]:
                    raise BuildError("Complete Campaign content bank changed or is missing.")
                content_files.append(bank_record)
                # The PC installer publishes completed file-backed content before
                # launch. Verify its signed inventory without a second multi-GB
                # Campaign entry sweep; the bank SHA was checked immediately above.
                spec = importlib.util.spec_from_file_location("_ghvr_installation_manifest", Path(__file__).with_name("installation_manifest.py"))
                installation = importlib.util.module_from_spec(spec)
                spec.loader.exec_module(installation)
                try:
                    installation_contract = installation.validate(archive, inputs["inputKey"], bank, row)
                except (ValueError, OSError, KeyError, TypeError, zipfile.BadZipFile) as error:
                    raise BuildError("Signed PC installation contract failed: " + str(error)) from error
                bank_after = bank.stat()
                if (bank_before.st_size, bank_before.st_mtime_ns) != (bank_after.st_size, bank_after.st_mtime_ns):
                    raise BuildError("Complete Campaign content bank changed during installation-contract verification.")
    verify_env = dict(os.environ)
    if tools.get("jdk"):
        verify_env["JAVA_HOME"] = tools["jdk"]
    signing_text = command([tools["java"], "-jar", tools["apksigner"], "verify", "--verbose", "--print-certs", str(apk)],
                           output / "logs/apk-signature.log", env=verify_env)
    matched = re.search(r"certificate SHA-256 digest:\s*([0-9a-fA-F]+)", signing_text)
    if not matched:
        raise BuildError("apksigner did not report a signing certificate digest.")
    cert_hash = matched.group(1).lower()
    prior = output / "signing/certificate.json"
    if prior.exists() and json.loads(prior.read_text())["sha256"] != cert_hash:
        raise BuildError("The APK signing identity changed; updates would lose their data continuity.")
    write_json(prior, {"sha256": cert_hash})
    badging = command([tools["aapt"], "dump", "badging", str(apk)], output / "logs/apk-badging.log")
    if not re.search(r"^package: name='" + re.escape(PACKAGE) + r"'", badging, re.MULTILINE):
        raise BuildError("The APK uses an unexpected package name.")
    if re.search(r"^uses-feature:\s.*\boculus\.software\.eye_tracking\b", badging, re.MULTILINE | re.IGNORECASE):
        raise BuildError("Quest 3 APK declares mandatory eye tracking, which its hardware does not support.")
    if re.search(r"^uses-permission(?:-sdk-\d+)?:\s.*\beye_tracking\b", badging, re.MULTILINE | re.IGNORECASE):
        raise BuildError("Quest 3 APK requests an unused eye-tracking permission.")
    result = {"apkSha256": digest(apk), "certificateSha256": cert_hash,
            "package": PACKAGE, "isDiagnostic": inputs["target"] != "game",
            "isDummy": bool(inputs["profile"].get("isDummy")), "buildReport": metadata, "contentFiles": content_files}
    if "buildProvenance" in metadata:
        result["buildProvenance"] = metadata["buildProvenance"]
    if inputs["target"] == "game":
        result["installationContract"] = installation_contract
    return result


def campaign_shader_environment(args, env):
    # Ordinary users keep the native SVC/imported/pass/material gate. The costly
    # original-program compile/reflection audit is an explicit developer mode.
    env.pop("GHVR_QUEST_VALIDATE_CAMPAIGN_SHADERS", None)
    if getattr(args, "validate_campaign_shaders", False): env["GHVR_QUEST_VALIDATE_CAMPAIGN_SHADERS"] = "1"


def validate_player_shader_log(path):
    """Reject broken native shader banks even when Unity reports Player success.

    The complete dc3 Player succeeded with 47 Vulkan compiler errors. The native
    log gate complements BuildReport and imported shader checks without repeating
    the original-program matrix. Compiler success does not certify headset pixels.
    """
    path = _ordinary_owned(path)
    if not path.is_file():
        raise BuildError("Completed full Player shader compiler log is missing.")
    errors, samples = 0, []
    pattern = re.compile(r"^\s*(?:Shader error in|Compute shader error in)\b", re.IGNORECASE)
    with path.open("r", encoding="utf-8", errors="replace") as stream:
        for line in stream:
            if pattern.match(line):
                errors += 1
                if len(samples) < 5:
                    samples.append(line.strip()[:512])
    if errors:
        raise BuildError("Full Player contains " + str(errors) + " native shader compiler errors: " + " | ".join(samples))
    return {"schema": 1, "scope": "actual-completed-player-shader-compiler-log", "nativeCompilerErrorCount": 0,
            "originalShaderMatrixRerun": False, "hardwareVerified": False}


def build(args, inputs: dict, output: Path, source: Path, game: Path, project: Path) -> Path:
    tools = toolchain(args, output)
    build_progress.operation("weave", detail="Compiling current VR mod and adapting original managed game assemblies")
    weave(args, inputs, output, source, game, project)
    build_progress.operation("weave", complete=True, detail="Current mod and original managed game bindings compiled")
    key_file, private = signing(output, tools)
    provenance = build_provenance.capture(inputs, project, source, Path(__file__).resolve().parent, tools)
    key = value_hash({"input": inputs["inputKey"], "toolchain": tools["key"], "recipe": RECIPE,
                      "buildProvenance": value_hash(provenance),
                      "validateCampaignShaders": bool(getattr(args, "validate_campaign_shaders", False))})
    apk = output / "builds" / key / "GloomhavenVR-Quest.apk"
    report = Path(str(apk) + ".build.json")
    provenance_path = apk.parent / "build-provenance.json"

    def compile_player_files():
        recover_delivery_pending(output, key, provenance)
        policy = host_resources.phase_budget("il2cpp" if args.target in ("startup", "game") else "unity", output)
        if not policy["nativeLaunchAllowed"]:
            raise BuildError("Available RAM/commit is insufficient or unknown for the large native game compiler; "
                             "close other programs or provide sufficient Windows pagefile commit headroom and retry. "
                             "Completed stages are retained; --jobs cannot bypass the memory reserve.")
        apk.parent.mkdir(parents=True, exist_ok=True)
        apk.unlink(missing_ok=True)
        report.unlink(missing_ok=True)
        write_json(provenance_path, provenance)
        native = source / "scripts/build-quest-native.py"
        if not native.is_file():
            raise BuildError("The selected source does not contain the Quest passthrough native build tool.")
        command([sys.executable, str(native), "--ndk", tools["androidNdk"], "--output",
                 str(project / "Assets/Quest/Plugins/Android/arm64/libghvr_quest_passthrough.so"),
                 "--cache", str(output / "tool-cache/openxr-headers")],
                output / "logs" / ("native-" + key[:12] + ".log"), cwd=source)
        if args.target in ("startup", "game"):
            build_progress.operation("package-api", detail="Binding recovered original scripts to Android Unity package APIs")
            bind_startup_package_apis(args, output, source, project, tools, key)
        if args.target == "game":
            import campaign_native
            campaign_native.build_contract(project, inputs)
        env = dict(os.environ)
        campaign_shader_environment(args, env)
        env.update({"GHVR_QUEST_OUTPUT_APK": str(apk), "GHVR_QUEST_TARGET": args.target,
                    "GHVR_QUEST_PACKAGE": PACKAGE,
                    "GHVR_QUEST_PROFILE_PATH": str(project / "Assets/Quest/Resources/quest-profile.json"),
                    "GHVR_QUEST_CONTENT_ROOT": str(game),
                    "GHVR_QUEST_MANIFEST_PATH": str(project / "Assets/StreamingAssets/Quest/input-manifest.json"),
                    "GHVR_QUEST_KEYSTORE_PATH": str(key_file), "GHVR_QUEST_KEYSTORE_ALIAS": private["alias"],
                    "GHVR_QUEST_KEYSTORE_PASSWORD": private["password"],
                    "GHVR_QUEST_KEYALIAS_PASSWORD": private["password"],
                    "GHVR_QUEST_ANDROID_SDK": tools["androidSdk"],
                    "GHVR_QUEST_ANDROID_NDK": tools["androidNdk"], "GHVR_QUEST_JDK": tools["jdk"]})
        env = content_pack_environment(env)
        if args.target == "game":
            env = campaign_native_shader_environment(env, source)
        if args.target in ("startup", "game"):
            # Unity 2021.3's Android toolchain otherwise selects old NDK r21 BFD.
            # Keep the supported linker selection local to this Android build process.
            env["UNITY_IL2CPP_ANDROID_USE_LLD_LINKER"] = "1"
        dotnet = tool_path(args.dotnet, "dotnet")
        launcher = host_resources.prepare_bee_launcher(dotnet, output)
        env = host_resources.unity_native_environment(tools["editor"], launcher, policy["jobs"], dotnet, env)
        build_progress.operation("unity-import", detail="Unity imports the prepared project and compiles Editor scripts")
        command(unity_launcher(tools["editor"], graphics=args.target == "game") + ["-quit", "-projectPath", str(project),
                 "-buildTarget", "Android", "-executeMethod", "GloomhavenVR.Quest.Editor.QuestBuildConcurrency.Build",
                 "-logFile", str(output / "logs" / ("unity-build-" + key[:12] + ".log"))],
                output / "logs" / ("unity-launch-" + key[:12] + ".log"), env=env)
        build_progress.operation("delivery", detail="Validating the actual signed APK and complete delivered content banks")
        if build_provenance.capture(inputs, project, source, Path(__file__).resolve().parent, tools) != provenance:
            raise BuildError("Staged build tools changed during the Player build; retry after editing stops.")
        metadata = json.loads(report.read_text(encoding="utf-8"))
        if args.target == "game":
            metadata["nativePlayerShaderValidation"] = validate_player_shader_log(
                output / "logs" / ("unity-build-" + key[:12] + ".log"))
            metadata["updateCapability"] = _local_helper("update_driver").add_update_capability(
                _module_context(), apk, inputs, output, source, tools)
        metadata["buildProvenance"] = provenance
        write_json(report, metadata)
        details = validate_apk(apk, report, inputs, tools, output, provenance)
        details["buildEvidenceFiles"] = [record_file(provenance_path, provenance_path.relative_to(output).as_posix())]
        evidence = [provenance_path]
        if args.target == "game":
            import campaign_compute
            import campaign_native_shaders
            early_receipt = apk.parent / "shaders-addressables-validation.json"
            shutil.copyfile(_ordinary_owned(project / "QuestCampaignShaderEvidence/native-addressables-validation.json"), early_receipt)
            evidence.append(early_receipt)
            content_files = [output / row["path"] for row in details["contentFiles"]]
            if len(content_files) != 1:
                raise BuildError("Full Campaign graphics require one exact delivered native content bank.")
            shader_receipt = apk.parent / "shaders-delivered-validation.json"
            print("graphics: validating original shader aliases in the actual signed player and native bank", flush=True)
            graphics = campaign_native_shaders.validate_delivered(source, project, apk, content_files[0], shader_receipt)
            details["nativeShaderValidation"] = {field: graphics[field] for field in (
                "shaderCount", "originalNativeAliasCount", "allOriginalAliasesRetained",
                "actualNativeAliasMetadataVerified", "materialSampleCount", "compilerQueries", "hardwarePictureVerified")}
            evidence.append(shader_receipt)
            compute_receipt = apk.parent / "compute-delivered-validation.json"
            print("compute: validating the actual signed player and complete delivered banks", flush=True)
            checked = campaign_compute.validate_delivered(source, project, apk,
                content_files, compute_receipt)
            details["computeValidation"] = {field: checked[field] for field in (
                "shaderCount", "kernelCount", "graphicsApi", "actualExecutableBytesVerified", "hardwareVerified")}
            for field in ("actualVulkanSpirvBytesVerified", "actualGles31BytesVerified"):
                if field in checked:
                    details["computeValidation"][field] = checked[field]
            evidence.append(compute_receipt)
        return [apk, report, *evidence, *(output / row["path"] for row in details["contentFiles"])], details

    def compile_player():
        transaction = project_content_transaction(output, project, inputs["inputKey"]) if args.target in ("startup", "game") else nullcontext()
        with transaction:
            return compile_player_files()

    receipt = Stages(output).run("build", key, compile_player)
    write_json(output / "latest-build.json", {"schema": 1, "receipt": Stages(output).path("build", key).relative_to(output).as_posix(),
               "apk": apk.relative_to(output).as_posix(), "details": receipt["details"]})
    print("build: verified " + str(apk) + (" (DIAGNOSTIC: " + args.target + ")" if args.target != "game" else "") +
          (" (DUMMY IDENTITY)" if inputs["profile"].get("isDummy") else ""), flush=True)
    build_progress.operation("delivery", complete=True, detail="Signed APK, native content bank and build receipt verified")
    return apk


def recover_delivery_pending(output: Path, key: str, provenance: dict) -> bool:
    """Recover only this build's interrupted atomic delivery, before child launch.

    The caller owns output_lock and has no running child. The Wizard stops its
    supervised process tree before allowing a retry; a provenance file alone
    never authorizes killing or adopting a process from a previous invocation.
    """
    if not isinstance(key, str) or not re.fullmatch(r"[0-9a-f]{64}", key):
        raise BuildError("Interrupted delivery has an invalid build identity.")
    output = _ordinary_owned(output)
    folder = _ordinary_owned(output / "builds" / key)
    pending = _ordinary_owned(folder / "GloomhavenVR-Quest-content.zip.quest-content-pending")
    if not pending.exists():
        return False
    message = "Interrupted Campaign delivery could not be verified; retain the pending file and inspect this build's provenance before retrying."
    try:
        prior = _ordinary_owned(folder / "build-provenance.json")
        lock = _ordinary_owned(output / ".builder.lock")
        delivered = _ordinary_owned(folder / "GloomhavenVR-Quest-content.zip")
        if (not pending.is_file() or pending.stat().st_nlink != 1 or not prior.is_file()
                or prior.stat().st_size > 4 * 1024 * 1024 or not lock.is_file() or lock.stat().st_size > 4096
                or (delivered.exists() and not delivered.is_file())):
            raise BuildError(message)
        before = pending.stat(), prior.stat(), lock.stat()
        owner = json.loads(lock.read_text(encoding="utf-8"))
        if (not isinstance(owner, dict) or owner.get("schema") != 1 or owner.get("lock") != "kernel-guard"
                or type(owner.get("pid")) is not int or owner["pid"] != os.getpid()
                or not isinstance(owner.get("nonce"), str) or not re.fullmatch(r"[0-9a-f]{32}", owner["nonce"])
                or not isinstance(provenance, dict) or type(provenance.get("schema")) is not int or provenance["schema"] != 1
                or canonical(json.loads(prior.read_text(encoding="utf-8"))) != canonical(provenance)):
            raise BuildError(message)
        for path, original in zip((pending, prior, lock), before):
            current = _ordinary_owned(path).stat()
            identity = lambda row: (row.st_dev, row.st_ino, row.st_size, row.st_mtime_ns, row.st_ctime_ns)
            if not path.is_file() or identity(current) != identity(original):
                raise BuildError(message)
        pending.unlink()
    except (OSError, ValueError, TypeError) as exc:
        raise BuildError(message) from exc
    return True


def campaign_native_shader_environment(base, source):
    """Use captured host-owned validation and the exact frozen source decoder."""
    helper = Path(__file__).resolve().with_name("campaign_native_shaders.py")
    if not helper.is_file() or helper.is_symlink():
        raise BuildError("Verified native Shader gate is missing from the selected builder.")
    result = dict(base)
    # Resolving a venv's Unix executable symlink starts the base interpreter
    # without its installed UnityPy/PyYAML. Keep the actual venv command path.
    result.update(GHVR_QUEST_NATIVE_SHADER_PYTHON=str(Path(sys.executable).absolute()),
                  GHVR_QUEST_NATIVE_SHADER_HELPER=str(helper),
                  GHVR_QUEST_NATIVE_SHADER_SOURCE=str(Path(source).resolve()))
    return result


def content_pack_environment(base):
    """Only the captured builder selects the literal standard-library packer."""
    helper = Path(__file__).with_name("native_content_pack.py").resolve()
    if not helper.is_file():
        raise BuildError("The captured native content packer is missing.")
    result = dict(base)
    result.update(GHVR_QUEST_CONTENT_PACK_PYTHON=str(Path(sys.executable).resolve()),
                  GHVR_QUEST_CONTENT_PACK_HELPER=str(helper))
    return result


def bind_startup_package_apis(args, output: Path, source: Path, project: Path,
                            tools: dict, build_key: str) -> dict:
    """Check staged plugins against real imported packages before IL2CPP compilation.

    MonoScript remapping preserves serialized identities, but says nothing about
    binary calls into newer packages. B612 called an absent Vector2Control getter
    after InputSystem changed it to return DeltaControl. Unsupported member drift
    must stop here; only audited equivalent bindings may reach the player.
    """
    sdk = project / "QuestStartupEvidence/PlayerSdk"
    env = dict(os.environ)
    env.update({"GHVR_QUEST_PACKAGE": PACKAGE, "GHVR_QUEST_TARGET": args.target, "GHVR_QUEST_ANDROID_SDK": tools["androidSdk"],
                "GHVR_QUEST_ANDROID_NDK": tools["androidNdk"], "GHVR_QUEST_JDK": tools["jdk"]})
    # The complete recovered project includes the original custom cursor. Once
    # imported, Unity 2021.3's Null graphics host crashes in native cursor setup
    # before this SDK method runs. Preserve the cursor and use the same real
    # editor graphics host as the complete Campaign player build.
    command(unity_launcher(tools["editor"], graphics=args.target == "game") + ["-quit", "-projectPath", str(project),
             "-buildTarget", "Android", "-executeMethod", "GloomhavenVR.Quest.Editor.QuestBuild.CompileStartupSdk", "-logFile",
             str(output / "logs" / ("package-import-" + build_key[:12] + ".log"))],
            output / "logs" / ("package-import-launch-" + build_key[:12] + ".log"), env=env)
    evidence = json.loads((sdk / "compilation.json").read_text(encoding="utf-8"))
    if evidence != {"schema": 1, "target": "Android", "backend": "IL2CPP", "compilation": "Player",
                    "options": "DevelopmentBuild|Assertions", "unityVersion": "2021.3.5f1"}:
        raise BuildError("Package compatibility requires actual Android IL2CPP player SDK compilation.")
    sdk_files = inventory(sdk, [name + ".dll" for name in REPLACED_PACKAGES])
    plugins = {}
    for path in sorted((project / "Assets").rglob("*.dll")):
        # Unity copies StreamingAssets as runtime data; Wine/engine payload DLLs
        # there are not managed plugins and must never be rewritten by this audit.
        if path.relative_to(project / "Assets").parts[0].casefold() == "streamingassets":
            continue
        if path.stem in REPLACED_PACKAGES:
            continue
        if path.name in plugins:
            raise BuildError("Package API audit requires one staged plugin per assembly: " + path.name)
        plugins[path.name] = path
    if not plugins:
        raise BuildError("Package API audit has no actual staged game/mod plugins.")
    selected = [record_file(path, name) for name, path in sorted(plugins.items())]
    key = value_hash({"build": build_key, "plugins": selected, "sdk": sdk_files})
    root = output / "cache/package-api" / key
    original, rewritten = root / "input", root / "output"
    report_path = root / "report.json"

    def validate_report():
        report = json.loads(report_path.read_text(encoding="utf-8"))
        expected = {row["path"]: row["sha256"] for row in selected}
        sdk_expected = {row["path"]: row["sha256"] for row in sdk_files}
        if (report.get("schema") != 1 or report.get("sdkTarget") != "Android" or report.get("complete") is not True or report.get("issues")
                or report.get("inputAssemblies") != expected or report.get("sdkAssemblies") != sdk_expected
                or set(report.get("outputAssemblies", {})) != set(expected)):
            raise BuildError("Package API compatibility report is incomplete or belongs to different inputs.")
        if not verify_files(original, selected) or inventory(sdk, [row["path"] for row in sdk_files]) != sdk_files:
            raise BuildError("Package API audit inputs changed during validation.")
        actual = inventory(rewritten)
        if {row["path"]: row["sha256"] for row in actual} != report["outputAssemblies"]:
            raise BuildError("Package API rewritten plugins differ from the audited outputs.")
        return report

    def audit():
        root.mkdir(parents=True, exist_ok=True)
        for folder in (original, rewritten):
            if folder.exists():
                shutil.rmtree(folder)
        original.mkdir()
        for row in selected:
            shutil.copyfile(plugins[row["path"]], original / row["path"])
        reference = output / "inputs/game"
        # The immutable original snapshot supplies framework/base type resolution;
        # it never receives rewritten outputs.
        manifest = json.loads((project / "Assets/StreamingAssets/Quest/input-manifest.json").read_text())
        reference = reference / manifest["game"]["key"] / "Managed"
        command([str(tool_path(args.dotnet, "dotnet")), "run", "--project",
                 str(source / "tools/QuestWeaver/QuestWeaver.csproj"), "--configuration", "Release", "--",
                 "package-api", "--managed", str(original), "--sdk", str(sdk), "--sdk-target", "Android",
                 "--reference-managed", str(reference), "--output", str(rewritten), "--report", str(report_path)],
                output / "logs" / ("package-api-" + key[:12] + ".log"), cwd=source)
        report = validate_report()
        return [report_path, *sorted(rewritten.glob("*.dll"))], {"report": report}

    Stages(output).run("package-api", key, audit)
    report = validate_report()
    for name, path in plugins.items():
        shutil.copyfile(rewritten / name, path)
    shutil.copyfile(report_path, project / "Assets/Quest/Resources/quest-package-api-report.json")
    write_json(project / "Assets/Quest/Resources/quest-package-api-contract.json", {
        "schema": 1, "complete": True, "reportSha256": digest(report_path),
        "sdkRoot": "QuestStartupEvidence/PlayerSdk",
        "plugins": [record_file(path, path.relative_to(project).as_posix())
                    for name, path in sorted(plugins.items())],
        "sdk": sdk_files})
    return report


def verified_latest_build(output: Path) -> tuple[Path, dict]:
    latest = output / "latest-build.json"
    if not latest.is_file():
        raise BuildError("There is no completed APK in this output. Run build first.")
    value = json.loads(latest.read_text(encoding="utf-8"))
    receipt_path = output / value["receipt"]
    if output not in receipt_path.resolve().parents:
        raise BuildError("The latest-build receipt points outside the local output.")
    receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
    valid = Stages(output).valid("build", receipt["key"])
    apk = output / value["apk"]
    if not valid or output not in apk.resolve().parents or digest(apk) != valid["details"]["apkSha256"]:
        raise BuildError("The latest APK or build evidence changed; rebuild before installing.")
    return apk, valid["details"]


def install(args, output: Path) -> None:
    apk, details = verified_latest_build(output)
    if details.get("isDiagnostic") is False:
        installer = Path(__file__).resolve().parents[2] / "scripts/install-quest-wireless.py"
        arguments = [sys.executable, str(installer), "--output-root", str(output)]
        if args.adb: arguments += ["--adb", str(args.adb)]
        if args.serial: arguments += ["--serial", args.serial]
        command(arguments, output / "logs/adb-campaign-install.log")
        print("install: complete Campaign APK and content installed; existing native saves retained", flush=True)
        return
    adb = tool_path(args.adb, "adb")
    raw = command([str(adb), "devices"], output / "logs/adb-devices.log")
    ready = [line.split()[0] for line in raw.splitlines() if len(line.split()) == 2 and line.split()[1] == "device"]
    serial = args.serial
    if serial is None:
        if len(ready) != 1:
            raise BuildError("Connect/authorize one Quest, or choose an available device with --serial.")
        serial = ready[0]
    elif serial not in ready:
        raise BuildError("The selected ADB device is not connected and authorized.")
    command([str(adb), "-s", serial, "install", "-r", str(apk)], output / "logs/adb-install.log")
    # Never uninstall on signature mismatch: uninstalling would delete local campaigns.
    command([str(adb), "-s", serial, "shell", "am", "start", "-n", PACKAGE + "/com.unity3d.player.UnityPlayerActivity"],
            output / "logs/adb-launch.log")
    write_json(output / "last-install.json", {"schema": 1, "apkSha256": details["apkSha256"],
               "package": PACKAGE, "isDiagnostic": details["isDiagnostic"], "isDummy": details["isDummy"]})
    print("install: existing app data retained; collect adb logcat and headset observations", flush=True)


def report(output: Path) -> None:
    value = {"schema": 1, "output": str(output), "stages": []}
    for path in sorted((output / "receipts").glob("*/*.json")):
        try:
            receipt = json.loads(path.read_text(encoding="utf-8"))
            value["stages"].append({"stage": receipt["stage"], "key": receipt["key"],
                "verified": bool(Stages(output).valid(receipt["stage"], receipt["key"]))})
        except (ValueError, OSError, KeyError):
            value["stages"].append({"receipt": path.relative_to(output).as_posix(), "verified": False})
    latest = output / "latest-build.json"
    if latest.is_file():
        _, details = verified_latest_build(output)
        value["latestApk"] = {k: details[k] for k in ("apkSha256", "package", "isDiagnostic", "isDummy")}
    failure = output / "last-failure.json"
    if failure.is_file():
        value["lastFailure"] = json.loads(failure.read_text(encoding="utf-8"))
    print(json.dumps(value, indent=2))


def parser() -> argparse.ArgumentParser:
    result = argparse.ArgumentParser(description="Build a locally owned Gloomhaven copy for Quest; no store/cloud services.")
    result.add_argument("command", choices=("inspect", "prepare", "build", "install", "report", "package", "inspect-update", "update-mod", "update-profile"))
    result.add_argument("--base-apk", type=Path, help="Existing locally built Quest APK; updates always require the matching owned PC game.")
    result.add_argument("--update-kind", choices=("update-mod", "update-profile"), default="update-mod")
    result.add_argument("--apk-tools-json", type=Path, help="Wizard-owned Android signing tool paths for profile-only updates.")
    result.add_argument("--signing-root", type=Path, help="Original local signing folder, required for save-preserving updates signed with that key.")
    result.add_argument("--repo-root", type=Path, default=REPO)
    result.add_argument("--game-root", type=Path)
    result.add_argument("--output-root", type=Path)
    result.add_argument("--target", choices=("probe", "startup", "game"), default="game")
    result.add_argument("--procedural-backend", choices=("proton-arm64ec-fex", "box64-wine9"),
                        help="Developer override: the default full-game backend is Android ARM64EC Proton/FEX; Box64/Wine9 is an explicit comparison only.")
    result.add_argument("--validate-campaign-shaders", action="store_true",
                        help="Developer audit: compile/reflect every original Campaign shader program; normal builds keep required native SVC/import checks.")
    result.add_argument("--profile-json", type=Path)
    result.add_argument("--steam-root", type=Path)
    result.add_argument("--steam-id")
    result.add_argument("--steam-logo", type=Path)
    result.add_argument("--dummy-profile", action="store_true", help="Explicit maintainer-authorized development identity (ID 0, DUMMY).")
    result.add_argument("--dlc-ownership-json", type=Path, help="Small explicit local DLC declaration for portable build hosts; matches the selected account.")
    result.add_argument("--provider-metadata-dir", type=Path, help="Local GOG/Epic installation metadata directory; bundled DLC bytes alone do not establish ownership.")
    result.add_argument("--owned-dlc", action="append", choices=[row[0] for row in dlcs.CATALOG],
                        help="Explicit local purchased-DLC selection when complete provider metadata is unavailable; repeat per DLC.")
    result.add_argument("--probe-assets", type=Path, help="Pure native recovered asset slice, only for the diagnostic probe.")
    result.add_argument("--startup-project", type=Path, help="Validated original-scene closure, only for the startup diagnostic.")
    result.add_argument("--campaign-project", type=Path, help="Optional completed local Campaign recovery; its original input and every file are verified before reuse.")
    result.add_argument("--jobs", type=host_resources.parse_jobs, help="Maximum concurrent build jobs; automatic CPU/RAM limits still apply. Also GHVRQ_BUILD_JOBS.")
    result.add_argument("--unity-editor")
    result.add_argument("--android-sdk")
    result.add_argument("--android-ndk")
    result.add_argument("--jdk")
    result.add_argument("--dotnet")
    result.add_argument("--recovery-dotnet", help="Optional pinned .NET 10.0.401 recovery SDK; otherwise provisioned in the private build cache.")
    result.add_argument("--adb")
    result.add_argument("--serial")
    result.add_argument("--hardware-dir", type=Path, help="Destination for the verified Windows hardware-test archive.")
    return result


def main(argv: list[str] | None = None) -> int:
    arguments = list(sys.argv[1:] if argv is None else argv)
    args = parser().parse_args(arguments)
    try:
        repo = args.repo_root.resolve()
        if args.command in ("inspect", "prepare", "build", "inspect-update", "update-mod", "update-profile") and not args.game_root:
            raise BuildError("Supply --game-root pointing to the legally acquired PC installation.")
        if args.probe_assets and args.target != "probe":
            raise BuildError("--probe-assets belongs only to the explicitly diagnostic --target probe.")
        if args.startup_project and args.target != "startup":
            raise BuildError("--startup-project belongs only to the explicitly diagnostic --target startup.")
        data = game_data(args.game_root) if args.game_root else None
        output = ensure_output(args.output_root or repo / ".planning/quest3-local", repo, data)
        conversion_python = None
        if args.command in ("prepare", "build", "update-mod", "update-profile") and args.target in ("startup", "game"):
            # The Windows launcher intentionally installs no conversion wheels.
            # Do not merely add private site-packages to that process: its later
            # compute, codec and Unity shader-gate children use sys.executable.
            # Provision under the output lock, release it, and hand off before
            # acquiring the actual run lock. Both fresh and retained exports run
            # inside the exact private ABI. Read-only discovery/inspect stays
            # stdlib-only, and an already handed-off CLI never launches itself.
            import dependencies
            with output_lock(output):
                failure = output / "last-failure.json"
                failure.unlink(missing_ok=True)
                try:
                    python = dependencies.python_environment(output / "tool-cache", repo,
                                                             procedural=args.target == "game" and args.command in ("prepare", "build"), activate=False)
                except BaseException as exc:
                    write_json(failure, {"schema": 1, "stage": "builder-python", "error": type(exc).__name__, "message": str(exc)})
                    raise
            if os.path.normcase(os.path.abspath(sys.executable)) != os.path.normcase(os.path.abspath(python)):
                build_progress.event("builder-python-handoff", detail="Running conversion in isolated build Python", status="start")
                child = subprocess.Popen([str(python), "-I", "-B", "-X", "utf8", str(Path(__file__).resolve()), *arguments])
                try:
                    result = child.wait()
                except BaseException:
                    # Keep the supervised launcher alive until its build child
                    # exits; interruption must not leave an unobserved build.
                    child.terminate()
                    child.wait()
                    raise
                build_progress.event("builder-python-handoff", 1, 1, "commands", "Isolated build Python exited",
                                     status="complete" if result == 0 else "failed")
                return result
            conversion_python = python
        with output_lock(output), host_resources.resource_context(output, args.jobs):
            failure = output / "last-failure.json"
            if args.command != "report":
                failure.unlink(missing_ok=True)
            try:
                if args.command == "report":
                    report(output)
                elif args.command == "install":
                    install(args, output)
                elif args.command == "package":
                    import handoff
                    apk, details = verified_latest_build(output)
                    archive = handoff.package(repo, output, apk, details, args.hardware_dir or repo / ".planning/debug/quest3")
                    print("package: " + str(archive), flush=True)
                elif args.command in ("inspect-update", "update-mod", "update-profile"):
                    updates = _local_helper("update_driver")
                    if args.command == "inspect-update":
                        updates.inspect(_module_context(), args, repo, output, data)
                    else:
                        updates.run(_module_context(), args, repo, output)
                else:
                    inputs = inspect_inputs(args, repo, output, data)
                    os.environ[host_resources.INPUT_ENV] = inputs["inputKey"]
                    if args.command != "inspect":
                        source, game = snapshot_inputs(inputs, output, repo, data, args.probe_assets, args.startup_project)
                        project = prepare(args, inputs, output, source, game, conversion_python=conversion_python)
                        print("prepare: " + str(project), flush=True)
                        if args.command == "build":
                            build(args, inputs, output, source, game, project)
            except BaseException as exc:
                if not failure.exists():
                    write_json(failure, {"schema": 1, "stage": args.command, "error": type(exc).__name__, "message": str(exc)})
                raise
        return 0
    except (BuildError, ProfileError, ValueError, OSError) as exc:
        print("Quest builder: " + str(exc), file=sys.stderr)
        return 1
    except KeyboardInterrupt:
        print("Quest builder: interrupted; completed hash-verified stages can be resumed.", file=sys.stderr)
        return 130


if __name__ == "__main__":
    raise SystemExit(main())
