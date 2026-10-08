"""Local APK updates: retain owned game data and compile only changed native code.

The selected PC installation is required on every invocation. Its presence and
matching owned files are a small local qualification hurdle, not proof of purchase.
IL2CPP updates always replace a matched code/metadata pair; DLL hot-swapping is
not supported. APK signing secrets never enter update manifests or support logs.
"""
from __future__ import annotations
import copy
import json
import os
from pathlib import Path
import re
import shutil
import zipfile


def _core(builder):
    # Captured while the installer's isolated local dependency aliases exist.
    return builder.apk_updates


def preflight(builder, game_root, base_apk, mode=None):
    """Always read the selected owned installation before downloads/build reuse."""
    if not base_apk:
        raise builder.BuildError("Select --base-apk and the matching locally owned PC installation.")
    apk = Path(base_apk).expanduser().resolve()
    if not apk.is_file() or apk.suffix.lower() != ".apk":
        raise builder.BuildError("The selected base APK is missing or has the wrong extension.")
    data = builder.game_data(Path(game_root))
    core = _core(builder)
    base = core.inspect_base(apk)
    if mode == "update-profile" and not base["dynamicProfile"]:
        raise builder.BuildError("This older APK needs one mod/code update before a profile can be edited without Unity. Select 'Update mod' first.")
    proof = core.preflight_owned_game(apk, data)
    return base, data, proof


def inspect(builder, args, repo, output, data=None):
    mode = args.update_kind if args.command == "inspect-update" else args.command
    operation = "game-inputs" if args.command == "inspect-update" else "update-owned-game"
    builder.build_progress.operation(operation, detail="Checking the selected locally owned PC installation.")
    base, data, proof = preflight(builder, args.game_root, args.base_apk, mode)
    profile, logo = builder.selected_profile(args)
    if profile is None:
        raise builder.BuildError("An update requires a selected local account profile.")
    profile["dlcOwnership"] = builder.dlcs.capture(args, data, profile)
    original = base["manifest"]
    # A profile change cannot add content missing from the original conversion.
    old_owned = original.get("profile", {}).get("dlcOwnership", {}).get("installedAppIds", [])
    new_owned = profile["dlcOwnership"].get("installedAppIds", [])
    if isinstance(old_owned, list) and isinstance(new_owned, list) and not set(new_owned).issubset(old_owned):
        raise builder.BuildError("The selected PC account owns DLC absent from this APK. Build the additional game content first.")
    inputs = copy.deepcopy(original)
    # INPUT describes the immutable game conversion. UPDATE describes the mod
    # currently installed on top of it; a later profile edit must retain that
    # code/art version instead of restoring the original conversion's mod.
    current = base.get("update") or {}
    if isinstance(current.get("mod"), dict):
        inputs["mod"] = copy.deepcopy(current["mod"])
    if type(base.get("modBuild")) is int:
        inputs["mod"]["modBuild"] = base["modBuild"]
    inputs["profile"] = profile
    inputs["profileKey"] = builder.value_hash(profile)
    if mode == "update-mod":
        files, commit, dirty = builder.source_inventory(repo)
        current_mod = current.get("mod", original["mod"])
        if _native_rows(files) != _native_rows(current_mod["files"]):
            raise builder.BuildError("Native Quest programs or their interop sources changed. This APK update retains those programs; select a full Quest build to compile the changed native runtime.")
        inputs["mod"] = {"files": files, "key": builder.value_hash({"files": files}),
                         "commit": commit, "dirty": dirty, "modBuild": builder.mod_build(repo)}
    plan = {"schema": 1, "mode": mode, "baseApk": str(Path(args.base_apk).resolve()),
            "baseApkSha256": builder.digest(Path(args.base_apk)), "gameInputKey": base["inputKey"],
            "ownedGame": proof, "inputs": inputs,
            "updateRecipe": {name: builder.digest(Path(repo) / "tools/quest-builder" / name) for name in
                ("update_driver.py", "apk_update.py", "apk_manifest.py", "builder.py", "storage.py", "profile.py", "dlcs.py")}}
    plan["updateKey"] = builder.value_hash({name: value for name, value in plan.items() if name != "baseApk"})
    root = output / "updates" / plan["updateKey"]
    builder.write_json(root / "plan.json", plan)
    builder.write_json(output / "latest-update-input.json", {"schema": 1, "plan": (root / "plan.json").relative_to(output).as_posix()})
    identities = output / "identities" / inputs["profileKey"]
    builder.write_json(identities / "quest-profile.json", profile)
    (identities / "quest-steam-logo.png").write_bytes(logo)
    builder.build_progress.operation(operation, complete=True, detail="Selected owned PC installation matches the original APK game.")
    print("update inspect: " + mode + "; retained game " + base["inputKey"][:12] + "; B" + str(inputs["mod"]["modBuild"]), flush=True)
    return plan


def signing_tools(builder, args, output):
    if getattr(args, "apk_tools_json", None):
        path = Path(args.apk_tools_json)
        if path.stat().st_size > 65536:
            raise builder.BuildError("APK signing tool selection is excessive.")
        selected = json.loads(path.read_text(encoding="utf-8"))
        if selected.pop("schema", None) != 1 or set(selected) != {"java", "keytool", "jdk", "aapt", "apksigner", "zipalign"}:
            raise builder.BuildError("APK signing tool selection has an unsupported schema.")
        for name in ("java", "keytool", "aapt", "apksigner", "zipalign"):
            if not Path(selected[name]).is_file():
                raise builder.BuildError("APK signing tool is missing: " + name)
        return selected
    tools = builder.toolchain(args, output)
    tools["zipalign"] = str(Path(tools["aapt"]).with_name("zipalign.exe" if os.name == "nt" else "zipalign"))
    return tools


def _signers(builder, tools, output, signing_root=None, *, base_apk=None):
    if signing_root:
        original = builder._ordinary_owned(Path(signing_root).resolve())
        destination = output / "signing"; destination.mkdir(parents=True, exist_ok=True)
        for name in ("quest.keystore", "local-key.json"):
            source = builder._ordinary_owned(original / name)
            target = builder._ordinary_owned(destination / name)
            if not source.is_file(): raise builder.BuildError("The selected signing folder lacks " + name)
            if target.is_file() and builder.digest(target) != builder.digest(source):
                raise builder.BuildError("This workspace already has a different signing key. Use a separate update workspace; no key was overwritten.")
            if not target.exists(): shutil.copyfile(source, target)
    if base_apk is not None and not all((output / "signing" / name).is_file() for name in ("quest.keystore", "local-key.json")):
        raise builder.BuildError("Updating an installed APK requires its original signing folder (quest.keystore and local-key.json). Select that folder or keep the original Builder workspace. An APK alone does not contain its private signing key.")
    key, private = builder.signing(output, tools)
    environment = dict(os.environ)
    environment["GHVR_QUEST_SIGNING_PASSWORD"] = private["password"]
    if base_apk is not None:
        original_certificate = builder.command([tools["java"], "-jar", tools["apksigner"], "verify", "--print-certs", str(base_apk)],
                                               output / "logs/update-base-signature.log")
        original = re.search(r"certificate SHA-256 digest:\s*([0-9a-fA-F]{64})", original_certificate)
        selected_certificate = builder.command([tools["keytool"], "-J-Duser.language=en", "-J-Duser.country=US",
            "-list", "-v", "-keystore", str(key), "-alias", private["alias"],
            "-storepass:env", "GHVR_QUEST_SIGNING_PASSWORD"], output / "logs/update-key-certificate.log", env=environment)
        selected = re.search(r"SHA256:\s*([0-9a-fA-F:]{64,95})", selected_certificate)
        if not original or not selected or original[1].lower() != selected[1].replace(":", "").lower():
            raise builder.BuildError("The selected signing key does not belong to this APK. Restore its original signing folder; no compile, APK update or uninstall was performed.")

    def sign(apk):
        apk = Path(apk)
        aligned, signed = apk.with_suffix(".aligned.apk"), apk.with_suffix(".signed.apk")
        try:
            builder.command([tools["zipalign"], "-f", "-p", "4", str(apk), str(aligned)], output / "logs/update-align.log")
            builder.command([tools["java"], "-jar", tools["apksigner"], "sign", "--ks", str(key),
                             "--ks-key-alias", private["alias"], "--ks-pass", "env:GHVR_QUEST_SIGNING_PASSWORD",
                             "--key-pass", "env:GHVR_QUEST_SIGNING_PASSWORD", "--out", str(signed), str(aligned)],
                            output / "logs/update-sign.log", env=environment)
            os.replace(signed, apk)
        finally:
            aligned.unlink(missing_ok=True); signed.unlink(missing_ok=True)

    def verify(apk):
        result = builder.command([tools["java"], "-jar", tools["apksigner"], "verify", "--verbose", "--print-certs", str(apk)],
                                 output / "logs/update-signature.log")
        match = re.search(r"certificate SHA-256 digest:\s*([0-9a-fA-F]{64})", result)
        if not match:
            raise builder.BuildError("Updated APK has no verified signing certificate.")
        certificate = match[1].lower()
        path = output / "signing/certificate.json"
        if path.is_file() and json.loads(path.read_text())["sha256"] != certificate:
            raise builder.BuildError("Local signing identity changed; existing saves require the original signing key.")
        builder.write_json(path, {"sha256": certificate})
        return {"certificateSha256": certificate}

    return sign, verify


def _game_subset(builder, inputs, data, game, *, art_changed):
    rows = [row for row in inputs["game"]["files"] if row["path"].startswith("Managed/")
            or (art_changed and row["path"].startswith("StreamingAssets/aa/StandaloneWindows64/pcg_databases_assets_assets/pcg/"))]
    # This partial snapshot has its own owner; never impersonate a complete game
    # snapshot. The compiler/environment producers use exactly these files.
    builder.snapshot(data, rows, game, phase="update-game-snapshot")


def _art_rows(files):
    return [row for row in files if row["path"].startswith("unity/GloomhavenVR.Assets/")
            or row["path"] in ("tools/environment-mesh/export-native.py", "scripts/generate-environment-meshes.py")]


def _native_rows(files):
    """A code-only update cannot silently retain changed non-IL2CPP programs."""
    interop = {"QuestGameNetwork.cs", "QuestContentHash.cs", "QuestPassthroughFeature.cs", "QuestGameProcedural.cs"}
    producers = {"tools/quest-builder/native_plugins.py", "tools/quest-builder/campaign_native.py",
                 "unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestNativePluginContract.cs"}
    prefixes = ("tools/quest-native/", "tools/quest-network/", "tools/quest-procedural-runtime/")
    def selected(name):
        if name in producers: return True
        if name.startswith("unity/GloomhavenVR.Quest/Assets/Quest/Runtime/") and Path(name).name in interop: return True
        return name.startswith(prefixes) and not (name.endswith((".md", ".meta")) or "/tests/" in name)
    return sorted((row for row in files if selected(row["path"])), key=lambda row: row["path"])


def _compile_code(builder, args, inputs, output, source, game, project, base, tools, root):
    core = _core(builder)
    builder.build_progress.operation("update-code", detail="Compiling the current mod and Quest-only original-game bindings.")
    package_abi = package_fingerprint(builder, source, inputs)
    compiled = root / "code-player.apk"
    compiled_receipt = Path(str(compiled) + ".code-build.json")
    if builder.Stages(output).valid("update-code", root.name):
        result = core.collect_code_update(base, compiled, compiled_receipt, package_abi=package_abi)
        builder.build_progress.operation("update-code", complete=True, detail="Completed native code and metadata reused; no compiler rerun.")
        return result
    core.stage_code_project(source, project, base, game / "Managed", profile=inputs["profile"], package_abi=package_abi,
                            version_code=inputs["mod"]["modBuild"],
                            version_name="0.1.0.B" + str(inputs["mod"]["modBuild"]) + "." + root.name[:12], code_key=root.name)
    builder.weave(args, inputs, output, source, game, project)
    ugui = project / "Packages/com.unity.ugui/Runtime/UI/Core/Layout/LayoutRebuilder.cs"
    if ugui.is_file():
        proof = json.loads((project / "QuestStartupEvidence/ugui-layout-gate.json").read_text())
        if proof.get("derivedSha256") != builder.digest(ugui) or proof.get("sourceSha256") != builder.UGUI_LAYOUT_SOURCE_SHA256:
            raise builder.BuildError("The retained private UGUI package changed; no native update was produced.")
    else:
        builder.restore_ugui_layout_gate(project, game, Path(tools["editor"]))
    packages = json.loads((project / "Packages/manifest.json").read_text())
    packages["dependencies"].update(builder.original_builtin_modules(game, Path(tools["editor"])))
    # Keep the same package dependencies as the normal standalone compiler.
    packages["dependencies"].update({"com.unity.addressables": "1.19.19"})
    builder.write_json(project / "Packages/manifest.json", packages)
    env = dict(os.environ); env.update(core.code_build_env(project, compiled))
    env.update({"GHVR_QUEST_ANDROID_SDK": tools["androidSdk"], "GHVR_QUEST_ANDROID_NDK": tools["androidNdk"],
                "GHVR_QUEST_JDK": tools["jdk"], "UNITY_IL2CPP_ANDROID_USE_LLD_LINKER": "1"})
    policy = builder.host_resources.phase_budget("il2cpp", output)
    if not policy["nativeLaunchAllowed"]:
        raise builder.BuildError("Available RAM/commit cannot admit the native compiler. Completed update work is retained.")
    dotnet = builder.tool_path(args.dotnet, "dotnet")
    launcher = builder.host_resources.prepare_bee_launcher(dotnet, output)
    env = builder.host_resources.unity_native_environment(tools["editor"], launcher, policy["jobs"], dotnet, env)
    _bind_code_packages(builder, args, output, source, game, project, tools, env, root.name)
    def compile_player():
        builder.command(builder.unity_launcher(tools["editor"]) + ["-quit", "-projectPath", str(project),
                        "-buildTarget", "Android", "-executeMethod", "GloomhavenVR.Quest.Editor.QuestCodeUpdateBuild.Build",
                        "-logFile", str(output / "logs/update-code-unity.log")], output / "logs/update-code-launch.log", env=env)
        core.collect_code_update(base, compiled, compiled_receipt, package_abi=package_abi)
        return [compiled, compiled_receipt], {"nativePairQualified": True}
    builder.Stages(output).run("update-code", root.name, compile_player)
    result = core.collect_code_update(base, compiled, compiled_receipt, package_abi=package_abi)
    builder.build_progress.operation("update-code", complete=True, detail="Matched ARM64 code, metadata and original script types compiled.")
    return result


def _bind_code_packages(builder, args, output, source, game, project, tools, env, update_key):
    sdk = project / "QuestStartupEvidence/PlayerSdk"
    def compile_sdk():
        builder.command(builder.unity_launcher(tools["editor"]) + ["-quit", "-projectPath", str(project),
                        "-buildTarget", "Android", "-executeMethod", "GloomhavenVR.Quest.Editor.QuestCodeUpdateBuild.CompileSdk",
                        "-logFile", str(output / "logs/update-sdk-unity.log")], output / "logs/update-sdk-launch.log", env=env)
        return [sdk / "compilation.json", *(sdk / (name + ".dll") for name in builder.REPLACED_PACKAGES)], {"actualAndroidSdk": True}
    builder.Stages(output).run("update-sdk", update_key, compile_sdk)
    evidence = json.loads((sdk / "compilation.json").read_text())
    if evidence != {"schema": 1, "target": "Android", "backend": "IL2CPP", "compilation": "Player",
                    "options": "DevelopmentBuild|Assertions", "unityVersion": "2021.3.5f1"}:
        raise builder.BuildError("The code update did not compile the actual Android package APIs.")
    plugins = sorted((project / "Assets/Plugins/QuestGame").glob("*.dll"))
    root = output / "updates" / update_key / "package-api"
    original, rewritten = root / "input", root / "output"
    original.mkdir(parents=True, exist_ok=True)
    for path in plugins: shutil.copyfile(path, original / path.name)
    report = root / "report.json"
    sdk_rows = builder.inventory(sdk, [name + ".dll" for name in builder.REPLACED_PACKAGES])
    selected = [builder.record_file(path, path.name) for path in plugins]
    key = builder.value_hash({"plugins": selected, "sdk": sdk_rows})
    def bind():
        builder.command([str(builder.tool_path(args.dotnet, "dotnet")), "run", "--project",
                         str(source / "tools/QuestWeaver/QuestWeaver.csproj"), "--configuration", "Release", "--",
                         "package-api", "--managed", str(original), "--sdk", str(sdk), "--sdk-target", "Android",
                         "--reference-managed", str(game / "Managed"), "--output", str(rewritten), "--report", str(report)],
                        output / "logs/update-package-api.log", cwd=source)
        value = json.loads(report.read_text())
        if (value.get("complete") is not True or value.get("issues")
                or value.get("inputAssemblies") != {row["path"]: row["sha256"] for row in selected}
                or value.get("sdkAssemblies") != {row["path"]: row["sha256"] for row in sdk_rows}
                or value.get("outputAssemblies") != {row["path"]: row["sha256"] for row in builder.inventory(rewritten)}):
            raise builder.BuildError("The code-update package API binding is incomplete or differs from its actual inputs.")
        return [report, *sorted(rewritten.glob("*.dll"))], {"complete": True}
    builder.Stages(output).run("update-package-api", key, bind)
    for path in plugins: shutil.copyfile(rewritten / path.name, path)


def package_fingerprint(builder, source, inputs):
    manifest = json.loads((Path(source) / "unity/GloomhavenVR.Quest/Packages/manifest.json").read_text())
    # Compiler ABI is separate from mod logic and art. The exact package recipe,
    # original Unity version and private UGUI API repair remain compatibility inputs.
    return builder.value_hash({"unityVersion": inputs["game"]["unityVersion"], "packages": manifest,
                               "playerUnityVersion": "2021.3.5f1", "addressables": "1.19.19", "tmpShaderSource": "3.0.6",
                               "uguiOriginal": builder.ORIGINAL_UGUI_SHA256,
                               "uguiSource": builder.UGUI_LAYOUT_SOURCE_SHA256, "format": 1})


def add_update_capability(builder, apk, inputs, output, source, tools):
    """Embed compatibility captured from the actual completed native player."""
    core = _core(builder)
    capsule = core.make_capsule(apk, package_abi=package_fingerprint(builder, source, inputs), dynamic_profile=True)
    key = builder.value_hash({"inputKey": inputs["inputKey"], "scope": "full-build-update-capability", "capsule": capsule})
    update = {"schema": 1, "updateKey": key, "gameInputKey": inputs["inputKey"],
              "mode": "build-capability", "modBuild": inputs["mod"]["modBuild"], "profile": inputs["profile"]}
    sign, verify = _signers(builder, tools, output)
    capable = apk.with_name(apk.stem + ".capable.apk")
    core.repack(apk, capable, {core.CAPSULE: builder.canonical(capsule), core.PROFILE: builder.canonical(inputs["profile"])},
                update_manifest=update, signer=sign, verifier=verify)
    os.replace(capable, apk)
    return {"schema": 1, "dynamicProfile": True, "scriptTypes": len(capsule["scriptTypes"]), "packageAbi": capsule["packageAbi"]}


def run(builder, args, repo, output):
    # Do not authorize a cached update from yesterday's absent PC installation.
    plan = inspect(builder, args, repo, output)
    core = _core(builder); base_apk = Path(plan["baseApk"]); base = core.inspect_base(base_apk)
    inputs, update_key = plan["inputs"], plan["updateKey"]
    root = output / "updates" / update_key
    apk = root / "GloomhavenVR-Quest.apk"
    tools = signing_tools(builder, args, output)
    sign, verify = _signers(builder, tools, output, getattr(args, "signing_root", None), base_apk=base_apk)

    def produce():
        replacements = {}
        code = None
        if args.command == "update-profile":
            builder.build_progress.operation("update-profile", detail="Preparing the signed offline profile.")
            replacements.update(core.profile_update(base_apk, inputs["profile"]))
            builder.build_progress.operation("update-profile", complete=True, detail="Signed offline profile prepared; no Unity build.")
        else:
            builder.build_progress.operation("update-mod-source", detail="Freezing the current mod source.")
            source = output / "inputs/mod" / inputs["mod"]["key"]
            builder.snapshot(repo, inputs["mod"]["files"], source, phase="update-source-snapshot")
            builder.build_progress.operation("update-mod-source", complete=True, detail="Current mod source snapshot retained.")
            current_mod = (base.get("update") or {}).get("mod", base["manifest"]["mod"])
            changed_art = _art_rows(inputs["mod"]["files"]) != _art_rows(current_mod["files"])
            game = output / "updates/game-inputs" / inputs["game"]["key"] / ("with-art" if changed_art else "code")
            _game_subset(builder, inputs, builder.game_data(args.game_root), game, art_changed=changed_art)
            project = root / "code-project"
            code = _compile_code(builder, args, inputs, output, source, game, project, base_apk, tools, root)
            replacements.update(code["replacements"])
            if changed_art:
                builder.build_progress.operation("update-art", detail="Building changed authored mod banks; original game assets are retained.")
                # Only authored VR banks and their original-derived environment
                # inputs are rebuilt; the original Campaign bank is never opened.
                art_inputs = copy.deepcopy(inputs)
                bank_project = root / "mod-bank-delivery"
                # package_mod_content is extended with an explicit partial-game
                # root; its standard full-build caller continues using its snapshot.
                manifest = builder.package_mod_content(bank_project, art_inputs, output, source, Path(tools["editor"]), owned_game_source=game)
                replacements["assets/quest-mod-content.zip"] = (bank_project / "Assets/StreamingAssets/quest-mod-content.zip").read_bytes()
                bank_receipt = json.loads((bank_project / "Assets/Quest/Resources/quest-mod-bundles.json").read_text())
                replacements.update(core.patch_resource_json(base_apk, {
                    "quest-mod-content": manifest, "quest-mod-bundles": bank_receipt}, replacements=replacements))
                with zipfile.ZipFile(plan["baseApk"]) as original:
                    installation = json.loads(original.read("assets/Quest/installation-manifest.json"))
                installation["mod"] = manifest
                replacements["assets/Quest/installation-manifest.json"] = json.dumps(installation, sort_keys=True).encode()
            builder.build_progress.operation("update-art", complete=True, detail="Changed mod art built or unchanged embedded banks reused.")
            replacements["assets/Quest/offline-profile.json"] = builder.canonical(inputs["profile"])
        update = {"schema": 1, "updateKey": update_key, "mode": args.command,
                  "gameInputKey": base["inputKey"], "baseApkSha256": plan["baseApkSha256"],
                  "modBuild": inputs["mod"]["modBuild"], "profile": inputs["profile"],
                  "mod": inputs["mod"], "ownedGame": plan["ownedGame"]}
        replacements.update(core.patch_resource_json(base_apk, {
            "quest-build": {"schema": 1, "inputKey": base["inputKey"], "modBuild": inputs["mod"]["modBuild"]},
            "quest-profile": inputs["profile"]}, replacements=replacements))
        replacements["AndroidManifest.xml"] = core.update_android_versions(base_apk,
            inputs["mod"]["modBuild"], "0.1.0.B" + str(inputs["mod"]["modBuild"]) + "." + base["inputKey"][:12])
        builder.build_progress.operation("update-repack", detail="Reusing unchanged APK payloads and signing the update.")
        result = core.repack(base_apk, apk, replacements, update_manifest=update, signer=sign, verifier=verify, code_evidence=code,
                             progress=lambda done, total, name: builder.build_progress.event(
                                 "update-apk-copy", done, total, "bytes", name, operation="update-repack"))
        builder.build_progress.operation("update-repack", complete=True, detail="Signed update APK assembled.")
        builder.build_progress.operation("update-verify", detail="Publishing verified update evidence.")
        report = Path(str(apk) + ".build.json")
        details = {"inputKey": base["inputKey"], "updateKey": update_key, "package": builder.PACKAGE,
                   "modBuild": inputs["mod"]["modBuild"], "isDiagnostic": False,
                   "isDummy": bool(inputs["profile"].get("isDummy")), "apkSha256": builder.digest(apk),
                   "contentFiles": [], "retainedGameContent": True, "updateResult": result}
        builder.write_json(report, {"schema": 1, "buildResult": "Succeeded", "scope": "APK update", **details})
        builder.build_progress.operation("update-verify", complete=True, detail="Signed update published; original game content is retained.")
        return [apk, report], details

    receipt = builder.Stages(output).run("build", update_key, produce)
    builder.write_json(output / "latest-build.json", {"schema": 1, "apk": apk.relative_to(output).as_posix(),
                       "receipt": "receipts/build/" + update_key + ".json", "details": receipt["details"]})
    print("update: " + str(apk), flush=True)
    return apk
