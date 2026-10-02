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
import subprocess
import sys
import zipfile

from profile import discover_steam_root, dummy_identity, load_profile, read_logo, ProfileError
from storage import (BuildError, Stages, canonical, digest, ensure_output, inventory,
                     output_lock, snapshot, value_hash, write_json)


RECIPE = 1
PACKAGE = "dev.gloomhavenvr.quest"
REPO = Path(__file__).resolve().parents[2]


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
    inputs = {"schema": 1, "recipe": RECIPE, "target": args.target,
              "game": {"key": game_key, "unityVersion": original_version(data), "files": game_files},
              "mod": {"key": source_key, "commit": commit, "dirty": dirty,
                      "modBuild": mod_build(repo), "files": source_files},
              "profile": profile, "profileKey": profile_key, "probeAssets": probe}
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
                    probe_assets: Path | None = None) -> tuple[Path, Path]:
    source = output / "inputs/mod" / inputs["mod"]["key"]
    game = output / "inputs/game" / inputs["game"]["key"]
    print("snapshot: verifying/copying immutable game and selected source", flush=True)
    snapshot(repo, inputs["mod"]["files"], source)
    snapshot(data, inputs["game"]["files"], game)
    # Reject edits across files during the snapshot window, not only a torn
    # individual read. The frozen set must also be a state of the selected input.
    if inventory(repo, [r["path"] for r in inputs["mod"]["files"]]) != inputs["mod"]["files"]:
        raise BuildError("The mod changed during snapshotting; rerun against the completed edit.")
    if inventory(data) != inputs["game"]["files"]:
        raise BuildError("The original installation changed during snapshotting; finish its update and retry.")
    if mod_build(source) != inputs["mod"]["modBuild"]:
        raise BuildError("The input ModBuild disagrees with the captured source.")
    if inputs.get("probeAssets"):
        probe = inputs["probeAssets"]
        if not probe_assets:
            raise BuildError("The manifest has probe assets but no selected source directory.")
        snapshot(probe_assets.resolve(), probe["files"], output / "inputs/probe" / probe["key"])
        if inventory(probe_assets.resolve()) != probe["files"]:
            raise BuildError("The recovered probe assets changed during snapshotting; finish export and retry.")
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
            shutil.copytree(recovered, project, ignore=shutil.ignore_patterns("Library", "Temp", "Logs", ".git"))
        else:
            project.mkdir(parents=True)
        for directory in ("Assets", "Packages", "ProjectSettings"):
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
        manifest = project / "Assets/StreamingAssets/Quest/input-manifest.json"
        write_json(manifest, inputs)
        settings = project / "QuestBuilderSettings.json"
        write_json(settings, {"schema": 1, "target": args.target, "inputKey": inputs["inputKey"],
                   "profileSha256": digest(resources / "quest-profile.json"),
                   "package": PACKAGE, "modBuild": inputs["mod"]["modBuild"]})
        # The generated project is mutable under Unity. Cache this content-independent
        # contract and Resources instead of receipts over import-generated .meta files.
        contracts = [settings, manifest, resources / "quest-profile.json", resources / "quest-steam-logo.png"]
        contracts.extend(p for p in (project / "Assets/Quest").rglob("*")
                         if p.is_file() and p.suffix in (".cs", ".shader", ".asmdef", ".cginc"))
        if inputs.get("probeAssets"):
            contracts.extend(project / "Assets/Quest/Recovered" / item["path"]
                             for item in inputs["probeAssets"]["files"])
        return sorted(set(contracts)), {
            "project": project.relative_to(output).as_posix(), "target": args.target,
            "isDiagnostic": args.target == "probe", "isDummy": bool(inputs["profile"].get("isDummy"))}

    stages.run("prepare", key, generate)
    return project


def weave(args, inputs: dict, output: Path, source: Path, game: Path, project: Path) -> None:
    if args.target != "game":
        return
    dotnet = tool_path(args.dotnet, "dotnet")
    key = value_hash({"game": inputs["game"]["key"], "mod": inputs["mod"]["key"], "recipe": RECIPE})
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
        if result.get("success") is False or result.get("blockers") or result.get("unsupported"):
            raise BuildError("The static integration report contains unsupported behavior; inspect " + str(report))
        return [report, *sorted(staged.glob("*.dll"))], {"report": result}

    Stages(output).run("weave", key, run_weaver)
    destination = project / "Assets/Plugins/QuestGame"
    destination.mkdir(parents=True, exist_ok=True)
    for dll in staged.glob("*.dll"):
        shutil.copyfile(dll, destination / dll.name)


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
        for required in ("AndroidManifest.xml", "lib/arm64-v8a/libil2cpp.so", "lib/arm64-v8a/libunity.so"):
            if required not in names:
                raise BuildError("The APK is not a genuine ARM64 IL2CPP Unity player: missing " + required)
        if any(name.startswith("lib/") and not name.startswith("lib/arm64-v8a/") for name in names if name.endswith(".so")):
            raise BuildError("The initial Quest APK must contain only ARM64 native binaries.")
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
    return {"apkSha256": digest(apk), "certificateSha256": cert_hash,
            "package": PACKAGE, "isDiagnostic": inputs["target"] == "probe",
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
        command([tools["editor"], "-batchmode", "-nographics", "-quit", "-projectPath", str(project),
                 "-buildTarget", "Android", "-executeMethod", "GloomhavenVR.Quest.Editor.QuestBuild.Build",
                 "-logFile", str(output / "logs" / ("unity-build-" + key[:12] + ".log"))],
                output / "logs" / ("unity-launch-" + key[:12] + ".log"), env=env)
        details = validate_apk(apk, report, inputs, tools, output)
        return [apk, report], details

    receipt = Stages(output).run("build", key, compile_player)
    write_json(output / "latest-build.json", {"schema": 1, "receipt": Stages(output).path("build", key).relative_to(output).as_posix(),
               "apk": apk.relative_to(output).as_posix(), "details": receipt["details"]})
    print("build: verified " + str(apk) + (" (DIAGNOSTIC)" if args.target == "probe" else "") +
          (" (DUMMY IDENTITY)" if inputs["profile"].get("isDummy") else ""), flush=True)
    return apk


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
    result.add_argument("--target", choices=("probe", "game"), default="game")
    result.add_argument("--profile-json", type=Path)
    result.add_argument("--steam-root", type=Path)
    result.add_argument("--steam-id")
    result.add_argument("--steam-logo", type=Path)
    result.add_argument("--dummy-profile", action="store_true", help="Explicit maintainer-authorized development identity (ID 0, DUMMY).")
    result.add_argument("--probe-assets", type=Path, help="Pure native recovered asset slice, only for the diagnostic probe.")
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
        data = game_data(args.game_root) if args.game_root else None
        output = ensure_output(args.output_root or repo / ".planning/quest3-local", repo, data)
        with output_lock(output):
            if args.command == "report":
                report(output)
            elif args.command == "install":
                install(args, output)
            else:
                inputs = inspect_inputs(args, repo, output, data)
                if args.command != "inspect":
                    source, game = snapshot_inputs(inputs, output, repo, data, args.probe_assets)
                    project = prepare(args, inputs, output, source, game)
                    print("prepare: " + str(project), flush=True)
                    if args.command == "build":
                        build(args, inputs, output, source, game, project)
        return 0
    except (BuildError, ProfileError, ValueError, OSError) as exc:
        print("Quest builder: " + str(exc), file=sys.stderr)
        return 1
    except KeyboardInterrupt:
        print("Quest builder: interrupted; completed hash-verified stages can be resumed.", file=sys.stderr)
        return 130


if __name__ == "__main__":
    raise SystemExit(main())
