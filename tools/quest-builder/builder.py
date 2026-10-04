"""Player-operated local Quest conversion, using only Python's standard library.

Every external step executes real tools and verifies outputs. A diagnostic target
is explicitly distinct from a recovered, woven game. Proprietary inputs, identity,
keys and APKs remain in the marked local output directory.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import secrets
import shutil
import struct
import subprocess
import sys
import zipfile

import startup
import shaders as post_effects

from profile import discover_steam_root, dummy_identity, load_profile, read_logo, ProfileError
from storage import (BuildError, Stages, canonical, digest, ensure_output, inventory,
                     output_lock, record_file, snapshot, value_hash, verify_files, write_json)


RECIPE = 1
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
    log.parent.mkdir(parents=True, exist_ok=True)
    with log.open("w", encoding="utf-8") as stream:
        try:
            result = subprocess.run(argv, cwd=cwd, env=env, stdout=stream,
                                    stderr=subprocess.STDOUT, check=False)
        except OSError as exc:
            raise BuildError("Cannot execute " + Path(argv[0]).name + "; verify the selected tool path.") from exc
    output = log.read_text(encoding="utf-8", errors="replace")
    if result.returncode:
        raise BuildError(Path(argv[0]).name + " exited with " + str(result.returncode) +
                         "; inspect " + str(log))
    return output


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
    for raw in git_output(repo, "ls-files", "--cached", "--others", "--exclude-standard", "-z").split(b"\0"):
        if not raw:
            continue
        relative = raw.decode("utf-8")
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
    return inventory(repo, selected), commit, dirty


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
        raise BuildError("A local Steam profile is required; use --steam-root or --profile-json. "
                         "Maintainer-only hardware tests may explicitly select --dummy-profile.")
    return None, None


def inspect_inputs(args, repo: Path, output: Path, data: Path) -> dict:
    print("inspect: hashing owned game and current mod inputs", flush=True)
    game_files = inventory(data)
    source_files, commit, dirty = source_inventory(repo)
    profile, logo = selected_profile(args)
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
    if args.target == "startup" and original_startup is None:
        raise BuildError("--target startup requires --startup-project from the original startup recovery tool.")
    inputs = {"schema": 1, "recipe": RECIPE, "target": args.target,
              "game": {"key": game_key, "unityVersion": original_version(data), "files": game_files},
              "mod": {"key": source_key, "commit": commit, "dirty": dirty,
                      "modBuild": mod_build(repo), "files": source_files},
              "profile": profile, "profileKey": profile_key, "probeAssets": probe,
              "startupProject": original_startup}
    inputs["inputKey"] = value_hash(inputs)
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
    return inputs


def snapshot_inputs(inputs: dict, output: Path, repo: Path, data: Path,
                    probe_assets: Path | None = None, startup_project: Path | None = None) -> tuple[Path, Path]:
    source = output / "inputs/mod" / inputs["mod"]["key"]
    game = output / "inputs/game" / inputs["game"]["key"]
    print("snapshot: verifying/copying immutable game and selected source", flush=True)
    snapshot(repo, inputs["mod"]["files"], source)
    # Reject edits across files during the snapshot window, not only a torn
    # individual read or added source file. Once frozen, later mod development
    # proceeds independently of the potentially lengthy original-game copy.
    if source_inventory(repo)[0] != inputs["mod"]["files"]:
        raise BuildError("The mod changed during snapshotting; rerun against the completed edit.")
    if mod_build(source) != inputs["mod"]["modBuild"]:
        raise BuildError("The input ModBuild disagrees with the captured source.")
    snapshot(data, inputs["game"]["files"], game)
    if inventory(data) != inputs["game"]["files"]:
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
    apk_signer = build_tools / ("apksigner.bat" if os.name == "nt" else "apksigner")
    keytool = jdk / "bin" / ("keytool" + suffix)
    if not apk_signer.is_file() or not keytool.is_file() or not (ndk / "source.properties").is_file():
        raise BuildError("The selected Android SDK/NDK/JDK is incomplete (apksigner, keytool or NDK revision missing).")
    version = command([str(editor), "-version"], output / "logs/unity-version.log").strip().splitlines()
    found = next((v.strip() for v in version if re.fullmatch(r"20\d{2}\.\d+\.\d+[abfp]\d+", v.strip())), None)
    if not found:
        raise BuildError("The Unity executable did not report a recognizable editor version.")
    selected = {"editor": str(editor), "unityVersion": found, "androidSdk": str(sdk),
                "androidNdk": str(ndk), "jdk": str(jdk), "aapt": str(build_tools / ("aapt" + suffix)),
                "apksigner": str(apk_signer), "keytool": str(keytool),
                "editorSha256": digest(editor), "buildToolsVersion": build_tools.name,
                "ndkPropertiesSha256": digest(ndk / "source.properties")}
    selected["key"] = value_hash({k: v for k, v in selected.items() if k not in (
        "editor", "androidSdk", "androidNdk", "jdk", "aapt", "apksigner", "keytool")})
    write_json(output / "toolchain.json", selected)
    return selected


def prepare(args, inputs: dict, output: Path, source: Path, game: Path) -> Path:
    stages = Stages(output)
    recovered = None
    if args.target == "startup":
        if not inputs.get("startupProject"):
            raise BuildError("No validated original startup closure was selected.")
        recovered = output / "inputs/startup" / inputs["startupProject"]["key"]
        startup.inspect_project(recovered, inputs["game"]["key"], inputs["game"]["files"], snapshot_receipt=True)
    if args.target == "game":
        recipe_files = [item for item in inputs["mod"]["files"]
                        if item["path"].startswith("tools/quest-recovery/") or item["path"] == "scripts/recover-quest.py"]
        key = value_hash({"game": inputs["game"]["key"], "recoveryRecipe": recipe_files, "recipe": RECIPE})
        recovered = output / "cache/recovery" / key / "project"

        def recover():
            launcher = source / "scripts/recover-quest.py"
            if not launcher.is_file():
                raise BuildError("The selected source does not contain the Quest recovery tool.")
            command([sys.executable, str(launcher), "--game-data", str(game),
                     "--output-project", str(recovered), "--tool-root", str(output / "tool-cache")],
                    output / "logs" / ("recovery-" + key[:12] + ".log"))
            report = recovered / "quest-recovery-report.json"
            if not report.is_file():
                raise BuildError("Recovery did not produce quest-recovery-report.json; no readiness can be assumed.")
            metadata = json.loads(report.read_text(encoding="utf-8"))
            if not (recovered / "Assets").is_dir():
                raise BuildError("Recovery did not produce an actual Unity Assets directory.")
            paths = [p for p in recovered.rglob("*") if p.is_file() and "Library" not in p.parts]
            return paths, {"report": metadata, "project": recovered.relative_to(output).as_posix()}

        recovered_receipt = stages.run("recovery", key, recover)
        audit = recovered_receipt["details"]["report"].get("audit", {})
        readiness = audit.get("readiness", {})
        if readiness.get("fullGameReady") is not True:
            shaders = audit.get("shaders", {}).get("placeholderCount", "unknown")
            bundles = audit.get("addressables", {}).get("deferredBundleCount", "unknown")
            raise BuildError("Full game recovery is not ready: placeholder shaders=" + str(shaders) +
                             ", deferred bundles=" + str(bundles) +
                             "; inspect " + str(recovered / "quest-recovery-report.json") +
                             ". The explicitly diagnostic probe target is separate.")
        if audit.get("managedScriptBindings", {}).get("unexpectedUnresolvedCount", 0) != 0:
            raise BuildError("Recovery has unresolved script bindings; inspect " + str(recovered / "quest-recovery-report.json"))
    key = value_hash({"input": inputs["inputKey"], "recipe": RECIPE})
    project = output / "projects" / key

    def generate():
        template = source / "unity/GloomhavenVR.Quest"
        if not (template / "Assets/Quest").is_dir():
            raise BuildError("The selected source is missing unity/GloomhavenVR.Quest/Assets/Quest.")
        if project.exists():
            # This path is exclusively generated by this named stage under the marked output.
            shutil.rmtree(project)
        if recovered:
            shutil.copytree(recovered, project, ignore=shutil.ignore_patterns("Library", "Temp", "Logs", ".git", ".snapshot.json"))
        else:
            project.mkdir(parents=True)
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
                shutil.copytree(origin, project / directory, dirs_exist_ok=True)
        if inputs.get("probeAssets"):
            probe_root = output / "inputs/probe" / inputs["probeAssets"]["key"]
            shutil.copytree(probe_root, project / "Assets/Quest/Recovered", dirs_exist_ok=True,
                            ignore=shutil.ignore_patterns(".snapshot.json"))
        localized = source / "src/GloomhavenVR/Core/Loc/QuestText.cs"
        if not localized.is_file():
            raise BuildError("The selected mod is missing its shared Quest platform localization source.")
        (project / "Assets/Quest/Runtime").mkdir(parents=True, exist_ok=True)
        shutil.copyfile(localized, project / "Assets/Quest/Runtime/QuestText.cs")
        resources = project / "Assets/Quest/Resources"
        resources.mkdir(parents=True, exist_ok=True)
        identity = output / "identities" / inputs["profileKey"]
        for name in ("quest-profile.json", "quest-steam-logo.png"):
            shutil.copyfile(identity / name, resources / name)
        if args.target == "startup":
            # AssetRipper's Windows Bloom exports are one-pass placeholders.
            # Restore their original interfaces from pinned official portable
            # sources in the private build cache before Unity imports any asset.
            post_effects.restore_post_effects(project, output / "tool-cache/legacy-post-effects")
            loading_logo = source / "src/GloomhavenVR/Assets/GloomhavenVR_logo.png"
            if not loading_logo.is_file():
                raise BuildError("The selected mod is missing its original GloomhavenVR loading logo.")
            shutil.copyfile(loading_logo, resources / "quest-loading-logo.png")
            shutil.copyfile(recovered / startup.REPORT, resources / startup.REPORT)
            startup.stage_startup_movies(project, game)
            package_startup_content(project, inputs["inputKey"])
            package_data = json.loads((project / "Packages/manifest.json").read_text(encoding="utf-8"))
            package_data.setdefault("dependencies", {})["com.unity.addressables"] = "1.19.19"
            editor = tool_path(args.unity_editor, "Unity")
            package_data["dependencies"].update(original_builtin_modules(game, editor))
            write_json(project / "Packages/manifest.json", package_data)
            restore_ugui_layout_gate(project, game, editor)
            package_mod_content(project, inputs, output, source, editor)
            (project / "Assets/csc.rsp").write_text("-define:GHVR_QUEST_STARTUP\n", encoding="utf-8")
            # Before the first Unity domain load, exclude duplicate package DLLs.
            # Raw recovery and immutable original inputs retain their GUIDs/bytes.
            exclude_recovered_package_plugins(project)
            isolate_original_compiler_namespace(project)
            # Unity also treats asset paths as case-insensitive on Linux. Distinct
            # bundled/Resources variants need unique physical paths, with every
            # GUID, callback and original Addressables key retained.
            command([sys.executable, str(source / "tools/quest-recovery/case_paths.py"),
                     "--project", str(project)],
                    output / "logs" / ("startup-case-paths-" + key[:12] + ".log"))
            # Dumped Windows compute variants contain neither portable source nor
            # a valid Android compilation context. Restore only audited original
            # kernels from pinned official sources before any Unity import.
            command([sys.executable, str(source / "tools/quest-recovery/compute_sources.py"),
                     "--project", str(project), "--cache", str(output / "tool-cache/legacy-compute")],
                    output / "logs" / ("startup-compute-source-" + key[:12] + ".log"))
            startup.stage_startup_script_orders(project, game, source, output / "tool-cache",
                                               tool_path(args.dotnet, "dotnet"))
        manifest = project / "Assets/StreamingAssets/Quest/input-manifest.json"
        write_json(manifest, inputs)
        settings = project / "QuestBuilderSettings.json"
        write_json(settings, {"schema": 1, "target": args.target, "inputKey": inputs["inputKey"],
                   "profileSha256": digest(resources / "quest-profile.json"),
                   "package": PACKAGE, "modBuild": inputs["mod"]["modBuild"]})
        # The generated project is mutable under Unity. Cache this content-independent
        # contract and Resources instead of receipts over import-generated .meta files.
        contracts = [settings, manifest, resources / "quest-profile.json", resources / "quest-steam-logo.png"]
        if args.target == "startup":
            contracts.extend([resources / "quest-loading-logo.png", resources / startup.REPORT, resources / startup.MOVIES_REPORT, resources / "quest-startup-content.json",
                              project / "Assets/StreamingAssets/quest-startup-content.zip",
                              project / "QuestStartupEvidence/compute-source-restoration.json",
                              project / "Assets/QuestOriginalStartup/script-orders.json",
                              project / "QuestStartupEvidence/script-orders-source.json"])
            contracts.extend(startup_shader_contracts(project))
            contracts.extend([project / "QuestStartupEvidence/ugui-layout-gate.json",
                              project / "Packages/com.unity.ugui/Runtime/UI/Core/Layout/LayoutRebuilder.cs"])
            contracts.extend([resources / "quest-mod-content.json", resources / "quest-mod-bundles.json",
                              project / "Assets/StreamingAssets/quest-mod-content.zip"])
        contracts.extend(p for p in (project / "Assets/Quest").rglob("*")
                         if p.is_file() and p.suffix in (".cs", ".shader", ".asmdef", ".cginc"))
        if inputs.get("probeAssets"):
            contracts.extend(project / "Assets/Quest/Recovered" / item["path"]
                             for item in inputs["probeAssets"]["files"])
        return sorted(set(contracts)), {
            "project": project.relative_to(output).as_posix(), "target": args.target,
            "isDiagnostic": args.target != "game", "isDummy": bool(inputs["profile"].get("isDummy"))}

    stages.run("prepare", key, generate)
    return project


def startup_shader_contracts(project: Path) -> list[Path]:
    """Validate restored shader bytes outside the Quest template on cache reuse."""
    assets = project / "Assets"
    return sorted(path for path in assets.rglob("*") if path.is_file() and
                  (path.suffix in (".compute", ".cginc") or path.name.endswith(".compute.meta")))


def package_startup_content(project: Path, input_key: str) -> dict:
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
    for path in sources:
        path.unlink()
        Path(str(path) + ".meta").unlink(missing_ok=True)
    return manifest


def validate_mod_bundle(folder: Path, authored: Path, source_files: list[dict]) -> dict:
    """Verify native Android art and all declared compiler inputs on cache reuse."""
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


def package_mod_content(project: Path, inputs: dict, output: Path, source: Path, editor: Path) -> dict:
    """Build current authored mod art independently of changing gameplay code."""
    prefix = "unity/GloomhavenVR.Assets/"
    files = [{**row, "path": row["path"][len(prefix):]} for row in inputs["mod"]["files"]
             if row["path"].startswith(prefix) and row["path"][len(prefix):].split("/")[0]
             in ("Assets", "Packages", "ProjectSettings")]
    if not any(row["path"] == "Assets/Editor/QuestModBundles.cs" for row in files):
        raise BuildError("The selected source is missing the authored Android mod bundle recipe.")
    key = value_hash({"files": files, "editorSha256": digest(editor), "recipe": RECIPE})
    root = output / "cache/mod-bundle" / key
    authored, bundles = root / "project", root / "bundles"

    def compile_art():
        # These paths are exclusively owned generated cache, never the source tree.
        for path in (authored, bundles):
            if path.exists():
                shutil.rmtree(path)
        snapshot(source / prefix, files, authored)
        env = dict(os.environ)
        env["GHVR_QUEST_MOD_BUNDLE_OUTPUT"] = str(bundles)
        command([str(editor), "-batchmode", "-nographics", "-projectPath", str(authored),
                 "-buildTarget", "Android", "-executeMethod", "GloomhavenVR.QuestModBundles.BuildAll",
                 "-logFile", str(output / "logs" / ("mod-bundle-" + key[:12] + ".log"))],
                output / "logs" / ("mod-bundle-launch-" + key[:12] + ".log"), env=env)
        receipt = validate_mod_bundle(bundles, authored, files)
        return [bundles / "gloomhavenvr.bundle", bundles / "quest-mod-bundles.json"], receipt

    Stages(output).run("mod-bundle", key, compile_art)
    receipt = validate_mod_bundle(bundles, authored, files)
    streaming = project / "Assets/StreamingAssets"
    streaming.mkdir(parents=True, exist_ok=True)
    archive_path = streaming / "quest-mod-content.zip"
    name = "StreamingAssets/gloomhavenvr.bundle"
    record = {**receipt["bundle"], "path": name}
    with zipfile.ZipFile(archive_path, "w", compression=zipfile.ZIP_STORED) as archive:
        info = zipfile.ZipInfo(name, date_time=(2020, 1, 1, 0, 0, 0))
        with (bundles / "gloomhavenvr.bundle").open("rb") as original, archive.open(info, "w") as destination:
            shutil.copyfileobj(original, destination, 1024 * 1024)
    manifest = {"schema": 1, "inputKey": inputs["inputKey"], "archive": archive_path.name,
                "archiveSha256": digest(archive_path), "files": [record]}
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
    if args.target == "startup":
        compatibility = root / "Standalone"
        compatibility_report = root / "standalone-report.json"

        def adapt_startup():
            assets = source / "src/GloomhavenVR/obj/project.assets.json"
            bepinex = resolved_bepinex_runtime(assets)
            command([str(dotnet), "run", "--project", str(source / "tools/QuestWeaver/QuestWeaver.csproj"),
                     "--configuration", "Release", "--", "standalone", "--standalone-target", "startup",
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


def validate_apk(apk: Path, report: Path, inputs: dict, tools: dict, output: Path) -> dict:
    if not apk.is_file() or not report.is_file():
        raise BuildError("Unity did not produce both the APK and its .build.json evidence.")
    metadata = json.loads(report.read_text(encoding="utf-8"))
    expected = {"schema": 1, "target": inputs["target"], "inputKey": inputs["inputKey"],
                "package": PACKAGE, "profileSha256": digest(
                    output / "identities" / inputs["profileKey"] / "quest-profile.json"),
                "unityVersion": tools["unityVersion"], "buildResult": "Succeeded"}
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
    verify_env = dict(os.environ)
    if tools.get("jdk"):
        verify_env["JAVA_HOME"] = tools["jdk"]
    signing_text = command([tools["apksigner"], "verify", "--verbose", "--print-certs", str(apk)],
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
    return {"apkSha256": digest(apk), "certificateSha256": cert_hash,
            "package": PACKAGE, "isDiagnostic": inputs["target"] != "game",
            "isDummy": bool(inputs["profile"].get("isDummy")), "buildReport": metadata}


def build(args, inputs: dict, output: Path, source: Path, game: Path, project: Path) -> Path:
    tools = toolchain(args, output)
    weave(args, inputs, output, source, game, project)
    key_file, private = signing(output, tools)
    key = value_hash({"input": inputs["inputKey"], "toolchain": tools["key"], "recipe": RECIPE})
    apk = output / "builds" / key / "GloomhavenVR-Quest.apk"
    report = Path(str(apk) + ".build.json")

    def compile_player():
        apk.parent.mkdir(parents=True, exist_ok=True)
        apk.unlink(missing_ok=True)
        report.unlink(missing_ok=True)
        native = source / "scripts/build-quest-native.py"
        if not native.is_file():
            raise BuildError("The selected source does not contain the Quest passthrough native build tool.")
        command([sys.executable, str(native), "--ndk", tools["androidNdk"], "--output",
                 str(project / "Assets/Quest/Plugins/Android/arm64/libghvr_quest_passthrough.so"),
                 "--cache", str(output / "tool-cache/openxr-headers")],
                output / "logs" / ("native-" + key[:12] + ".log"), cwd=source)
        if args.target == "startup":
            bind_startup_package_apis(args, output, source, project, tools, key)
        env = dict(os.environ)
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
        if args.target == "startup":
            # Unity 2021.3's Android toolchain otherwise selects old NDK r21 BFD.
            # Keep the supported linker selection local to this diagnostic process.
            env["UNITY_IL2CPP_ANDROID_USE_LLD_LINKER"] = "1"
        command([tools["editor"], "-batchmode", "-nographics", "-quit", "-projectPath", str(project),
                 "-buildTarget", "Android", "-executeMethod", "GloomhavenVR.Quest.Editor.QuestBuild.Build",
                 "-logFile", str(output / "logs" / ("unity-build-" + key[:12] + ".log"))],
                output / "logs" / ("unity-launch-" + key[:12] + ".log"), env=env)
        details = validate_apk(apk, report, inputs, tools, output)
        return [apk, report], details

    receipt = Stages(output).run("build", key, compile_player)
    write_json(output / "latest-build.json", {"schema": 1, "receipt": Stages(output).path("build", key).relative_to(output).as_posix(),
               "apk": apk.relative_to(output).as_posix(), "details": receipt["details"]})
    print("build: verified " + str(apk) + (" (DIAGNOSTIC: " + args.target + ")" if args.target != "game" else "") +
          (" (DUMMY IDENTITY)" if inputs["profile"].get("isDummy") else ""), flush=True)
    return apk


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
    env.update({"GHVR_QUEST_PACKAGE": PACKAGE, "GHVR_QUEST_ANDROID_SDK": tools["androidSdk"],
                "GHVR_QUEST_ANDROID_NDK": tools["androidNdk"], "GHVR_QUEST_JDK": tools["jdk"]})
    command([tools["editor"], "-batchmode", "-nographics", "-quit", "-projectPath", str(project),
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
    result.add_argument("command", choices=("inspect", "prepare", "build", "install", "report"))
    result.add_argument("--repo-root", type=Path, default=REPO)
    result.add_argument("--game-root", type=Path)
    result.add_argument("--output-root", type=Path)
    result.add_argument("--target", choices=("probe", "startup", "game"), default="game")
    result.add_argument("--profile-json", type=Path)
    result.add_argument("--steam-root", type=Path)
    result.add_argument("--steam-id")
    result.add_argument("--steam-logo", type=Path)
    result.add_argument("--dummy-profile", action="store_true", help="Explicit maintainer-authorized development identity (ID 0, DUMMY).")
    result.add_argument("--probe-assets", type=Path, help="Pure native recovered asset slice, only for the diagnostic probe.")
    result.add_argument("--startup-project", type=Path, help="Validated original-scene closure, only for the startup diagnostic.")
    result.add_argument("--unity-editor")
    result.add_argument("--android-sdk")
    result.add_argument("--android-ndk")
    result.add_argument("--jdk")
    result.add_argument("--dotnet")
    result.add_argument("--adb")
    result.add_argument("--serial")
    return result


def main(argv: list[str] | None = None) -> int:
    args = parser().parse_args(argv)
    try:
        repo = args.repo_root.resolve()
        if args.command in ("inspect", "prepare", "build") and not args.game_root:
            raise BuildError("Supply --game-root pointing to the legally acquired PC installation.")
        if args.probe_assets and args.target != "probe":
            raise BuildError("--probe-assets belongs only to the explicitly diagnostic --target probe.")
        if args.startup_project and args.target != "startup":
            raise BuildError("--startup-project belongs only to the explicitly diagnostic --target startup.")
        data = game_data(args.game_root) if args.game_root else None
        output = ensure_output(args.output_root or repo / ".planning/quest3-local", repo, data)
        with output_lock(output):
            failure = output / "last-failure.json"
            if args.command != "report":
                failure.unlink(missing_ok=True)
            try:
                if args.command == "report":
                    report(output)
                elif args.command == "install":
                    install(args, output)
                else:
                    inputs = inspect_inputs(args, repo, output, data)
                    if args.command != "inspect":
                        source, game = snapshot_inputs(inputs, output, repo, data, args.probe_assets, args.startup_project)
                        project = prepare(args, inputs, output, source, game)
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
