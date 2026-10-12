"""Local APK updates: retain owned game data and compile only changed native code.

The selected PC installation is required on every invocation. Its presence and
matching owned files are a small local qualification hurdle, not proof of purchase.
IL2CPP updates always replace a matched code/metadata pair; DLL hot-swapping is
not supported. APK signing secrets never enter update manifests or support logs.
"""
from __future__ import annotations
import copy
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import stat
import zipfile


def _core(builder):
    # Captured while the installer's isolated local dependency aliases exist.
    return builder.apk_updates


def preflight(builder, game_root, base_apk, mode=None, *, output=None, progress=None, cancel=None):
    """Always read the selected owned installation before downloads/build reuse."""
    if not base_apk:
        raise builder.BuildError("Select --base-apk and the matching locally owned PC installation.")
    data = builder.game_data(Path(game_root))
    apk = Path(base_apk).expanduser().resolve()
    source_package = str(apk) if apk.suffix.lower() == ".zip" else None
    if source_package:
        if output is None:
            raise builder.BuildError("Opening a portable Quest ZIP requires the Builder workspace.")
        apk = builder.portable_packages.import_apk(apk, output, progress=progress, cancel=cancel)
    if not apk.is_file() or apk.suffix.lower() != ".apk":
        raise builder.BuildError("Select an existing Quest APK or exported Quest ZIP.")
    core = _core(builder)
    base = core.inspect_base(apk)
    base["resolvedApk"] = str(apk)
    base["sourcePackage"] = source_package
    if mode == "update-profile" and not base["dynamicProfile"]:
        raise builder.BuildError("This older APK needs one mod/code update before a profile can be edited without Unity. Select 'Update mod' first.")
    proof = core.preflight_owned_game(apk, data)
    return base, data, proof


def inspect(builder, args, repo, output, data=None):
    mode = args.update_kind if args.command == "inspect-update" else args.command
    operation = "game-inputs" if args.command == "inspect-update" else "update-owned-game"
    builder.build_progress.operation(operation, detail="Checking the selected locally owned PC installation.")
    def import_progress(value):
        builder.build_progress.event("portable-import", value.get("completed"), value.get("total"),
                                     "bytes", value.get("detail"), operation=operation)
    base, data, proof = preflight(builder, args.game_root, args.base_apk, mode, output=output, progress=import_progress)
    base_apk = Path(base["resolvedApk"])
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
        inputs["mod"] = {"files": files, "key": builder.value_hash({"files": files}),
                         "commit": commit, "dirty": dirty, "modBuild": builder.mod_build(repo)}
        requirements = update_requirements(builder, repo, base, files, inputs["mod"]["modBuild"])
        reasons, code_changed, art_changed = (requirements[name] for name in ("rebuildReasons", "codeChanged", "artChanged"))
    else:
        reasons = []
        code_changed = art_changed = False
    # Owned game and DLC qualification above must precede any local key creation.
    source_stamp = _core(builder)._base_stamp(base_apk)
    source_change_stamp = _selection_stamp(builder, [base_apk])
    identity = select_signing(builder, args, output, base_apk)
    imported_digest = getattr(getattr(builder, "portable_packages", None), "imported_apk_digest", None)
    base_sha256 = imported_digest(base_apk, output) if callable(imported_digest) else None
    if base_sha256 is None:
        base_sha256 = builder.apk_input_hashes.digest_apk(base_apk, output)
    if (_core(builder)._base_stamp(base_apk) != source_stamp
            or source_change_stamp != _selection_stamp(builder, [base_apk])):
        raise builder.BuildError("The selected source APK changed while qualifying ownership/signature; keep it closed and retry.")
    plan = {"schema": 1, "mode": mode, "baseApk": str(base_apk),
            "baseApkSha256": base_sha256, "gameInputKey": base["inputKey"],
            "signing": identity, "sourcePackage": base.get("sourcePackage"),
            "ownedGame": proof, "inputs": inputs,
            "updateStrategy": "cached-full-build" if reasons else "compact-code" if mode == "update-mod" else "profile-only",
            "rebuildReasons": reasons, "requiresUnity": bool(reasons or code_changed or art_changed),
            "codeChanged": code_changed, "artChanged": art_changed,
            "updateRecipe": {name: builder.digest(Path(repo) / "tools/quest-builder" / name) for name in
                ("update_driver.py", "apk_update.py", "apk_manifest.py", "builder.py", "storage.py", "profile.py", "dlcs.py")}}
    plan["updateKey"] = builder.value_hash({name: value for name, value in plan.items() if name not in ("baseApk", "sourcePackage")})
    root = output / "updates" / plan["updateKey"]
    builder.write_json(root / "plan.json", plan)
    builder.write_json(output / "latest-update-input.json", {"schema": 1, "plan": (root / "plan.json").relative_to(output).as_posix()})
    identities = output / "identities" / inputs["profileKey"]
    builder.write_json(identities / "quest-profile.json", profile)
    (identities / "quest-steam-logo.png").write_bytes(logo)
    builder.build_progress.operation(operation, complete=True, detail="Selected owned PC installation matches the original APK game.")
    print("update inspect: " + mode + "; retained game " + base["inputKey"][:12] + "; B" + str(inputs["mod"]["modBuild"]), flush=True)
    return plan


def normalized_signing_tools(builder, tools):
    """Qualify one selected SDK/JDK without altering its recorded provenance."""
    selected = dict(tools)
    # Capture065619 reaches a completed Android Player, then the full-build
    # capability writer receives a legacy toolchain without zipalign. Resolve
    # that one omitted command beside the selected aapt, never from PATH or a
    # different SDK. Preserve the caller's map/key and freeze callback inputs.
    if "zipalign" not in selected:
        aapt = selected.get("aapt")
        if not isinstance(aapt, (str, os.PathLike)) or not str(aapt):
            raise builder.BuildError("APK signing tool selection lacks aapt; cannot locate matching zipalign.")
        suffix = ".exe" if Path(aapt).suffix.lower() == ".exe" else ""
        selected["zipalign"] = str(Path(aapt).with_name("zipalign" + suffix))
    for name in ("java", "keytool", "apksigner", "zipalign"):
        path = selected.get(name)
        if not isinstance(path, (str, os.PathLike)) or not str(path) or not Path(path).is_file():
            raise builder.BuildError("APK signing tool is missing: " + name
                                     + ". Restore the selected Android Build Support/SDK and JDK; the completed Player is retained.")
    return selected


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
        return normalized_signing_tools(builder, selected)
    return normalized_signing_tools(builder, builder.toolchain(args, output))


def _signing_command(builder, argv, log, *, env=None, password=None):
    """Retain the tool failure and a bounded, secret-free native explanation."""
    try:
        return builder.command(argv, log, env=env) if env is not None else builder.command(argv, log)
    except builder.BuildError as exc:
        try:
            with Path(log).open("rb") as stream:
                stream.seek(max(0, Path(log).stat().st_size - 8192))
                tail = stream.read(8192).decode("utf-8", errors="replace")
            if password: tail = tail.replace(password, "[redacted]")
            detail = "\n".join(line[:1024] for line in tail.strip().splitlines()[-6:])
        except OSError:
            detail = ""
        if detail:
            raise builder.BuildError(str(exc) + "\nNative tool output:\n" + detail) from exc
        raise


def _certificate_output(builder, output, certificate):
    # Called only after actual native signature/certificate verification. This
    # JSON is derived evidence: malformed metadata can be rebuilt from that
    # verified certificate; a valid different identity still cannot be adopted.
    path = builder._ordinary_owned(output / "signing/certificate.json")
    previous = None
    if path.exists():
        value = path.lstat()
        if (not stat.S_ISREG(value.st_mode) or value.st_nlink != 1
                or getattr(value, "st_file_attributes", 0) & 0x400):
            raise builder.BuildError("Local signing certificate evidence is not a regular owned file.")
        if value.st_size <= 4096:
            try:
                with path.open("rb") as stream: raw = stream.read(4097)
                metadata = json.loads(raw) if len(raw) <= 4096 else None
                digest = metadata.get("sha256") if isinstance(metadata, dict) else None
                if isinstance(digest, str) and re.fullmatch(r"[0-9a-fA-F]{64}", digest):
                    previous = digest.lower()
            except (UnicodeError, ValueError):
                pass
    if previous is not None and previous != certificate:
        raise builder.BuildError("Local signing identity changed; existing saves require the original signing key.")
    builder.write_json(path, {"sha256": certificate})


def _copy_signing_root(builder, output, signing_root):
    if not signing_root: return
    original = builder._ordinary_owned(Path(signing_root).resolve())
    destination = output / "signing"; destination.mkdir(parents=True, exist_ok=True)
    for name in ("quest.keystore", "local-key.json"):
        source = builder._ordinary_owned(original / name)
        target = builder._ordinary_owned(destination / name)
        if not source.is_file(): raise builder.BuildError("The selected signing folder lacks " + name)
        if target.is_file() and builder.digest(target) != builder.digest(source):
            raise builder.BuildError("This workspace already has a different signing key. Use a separate update workspace; no key was overwritten.")
        if not target.exists(): shutil.copyfile(source, target)


def _source_certificate(builder, tools, output, base_apk):
    before = _core(builder)._base_stamp(base_apk)
    change_before = _selection_stamp(builder, [base_apk])
    result = _signing_command(builder, [tools["java"], "-jar", tools["apksigner"], "verify", "--print-certs", str(base_apk)],
                              output / "logs/update-base-signature.log")
    matches = re.findall(r"certificate SHA-256 digest:\s*([0-9a-fA-F]{64})", result)
    if (_core(builder)._base_stamp(base_apk) != before
            or change_before != _selection_stamp(builder, [base_apk]) or len(matches) != 1):
        raise builder.BuildError("The selected source APK has no single verified signing certificate; no local signing key was created.")
    return matches[0].lower()


def _key_certificate(builder, tools, output, key, private, environment):
    result = _signing_command(builder, [tools["keytool"], "-J-Duser.language=en", "-J-Duser.country=US",
        "-list", "-v", "-keystore", str(key), "-alias", private["alias"],
        "-storepass:env", "GHVR_QUEST_SIGNING_PASSWORD"], output / "logs/update-key-certificate.log",
        env=environment, password=private["password"])
    match = re.search(r"SHA256:\s*([0-9a-fA-F:]{64,95})", result)
    certificate = match[1].replace(":", "").lower() if match else ""
    if not re.fullmatch(r"[0-9a-f]{64}", certificate):
        raise builder.BuildError("The selected local signing key has no verified certificate.")
    return certificate


def _selection_stamp(builder, paths):
    """Reliable native file-change identities; unknown hosts simply reverify."""
    metadata = getattr(getattr(builder, "apk_input_hashes", None), "storage", None)
    if metadata is None: return None
    selected = []
    for raw in paths:
        path = Path(raw).absolute()
        try:
            value = path.lstat()
            if not stat.S_ISREG(value.st_mode) or value.st_nlink != 1 or getattr(value, "st_file_attributes", 0) & 0x400:
                return None
            stamp = metadata._file_witness_stamp(path, value)
            if stamp is None: return None
            selected.append((str(path), stamp))
        except OSError:
            return None
    return tuple(selected)


def _selection_paths(output, base_apk, tools):
    return [base_apk, output / "signing/quest.keystore", output / "signing/local-key.json",
            *(tools[name] for name in ("java", "keytool", "apksigner", "zipalign"))]


def select_signing(builder, args, output, base_apk):
    """Qualify recipient identity before deriving any reusable update receipt."""
    identity = {}
    tools = signing_tools(builder, args, output)
    callbacks = _signers(builder, tools, output, getattr(args, "signing_root", None), base_apk=base_apk,
                         signing_mode=getattr(args, "signing_mode", "auto"), identity_out=identity)
    # Keep native proof only in this invocation's args. Nothing here is persisted
    # or accepted from plan JSON; a new process always verifies natively again.
    # Genuine NTFS/ReFS ChangeTime closes the Windows restored-mtime gap.
    args._quest_qualified_signers = {"identity": copy.deepcopy(identity), "tools": copy.deepcopy(tools),
        "baseApk": str(base_apk), "output": str(output), "callbacks": callbacks,
        "signingRoot": getattr(args, "signing_root", None),
        "stamp": _selection_stamp(builder, _selection_paths(output, base_apk, tools))}
    return identity


def _run_signers(builder, args, output, base_apk, tools, identity):
    proof = getattr(args, "_quest_qualified_signers", None)
    if (isinstance(proof, dict) and proof.get("stamp") is not None
            and proof.get("identity") == identity and proof.get("tools") == tools
            and identity.get("requestedMode") == getattr(args, "signing_mode", "auto")
            and proof.get("signingRoot") == getattr(args, "signing_root", None)
            and proof.get("baseApk") == str(base_apk) and proof.get("output") == str(output)
            and proof["stamp"] == _selection_stamp(builder, _selection_paths(output, base_apk, tools))):
        return proof["callbacks"]
    return _signers(builder, tools, output, getattr(args, "signing_root", None), base_apk=base_apk,
                    signing_mode=getattr(args, "signing_mode", "auto"), expected_identity=identity)


def _signers(builder, tools, output, signing_root=None, *, base_apk=None,
             signing_mode="original", identity_out=None, expected_identity=None):
    if signing_mode not in ("auto", "original", "new-owner"):
        raise builder.BuildError("Select an automatic, original-key or new-owner signing mode.")
    # The default low-level API remains strict for ordinary update/capability
    # callers. Only an explicit portable/personalization caller enables auto or
    # new-owner. Verify the source before creating/copying recipient secrets.
    if signing_mode == "original" and base_apk is not None and not signing_root and not all(
            (output / "signing" / name).is_file() for name in ("quest.keystore", "local-key.json")):
        raise builder.BuildError("Updating an installed APK requires its original signing folder (quest.keystore and local-key.json). Select that folder or keep the original Builder workspace. An APK alone does not contain its private signing key.")
    tools = normalized_signing_tools(builder, tools)
    source_certificate = _source_certificate(builder, tools, output, base_apk) if base_apk is not None else None
    _copy_signing_root(builder, output, signing_root)
    key, private = builder.signing(output, tools)
    environment = dict(os.environ)
    environment["GHVR_QUEST_SIGNING_PASSWORD"] = private["password"]
    selected_certificate = None
    if base_apk is not None:
        selected_certificate = _key_certificate(builder, tools, output, key, private, environment)
        if signing_mode == "original" and source_certificate != selected_certificate:
            raise builder.BuildError("The selected signing key does not belong to this APK. Restore its original signing folder; no compile, APK update or uninstall was performed.")
        identity = {"schema": 1, "requestedMode": signing_mode,
                    "mode": "original" if source_certificate == selected_certificate else "new-owner",
                    "sourceCertificateSha256": source_certificate,
                    "certificateSha256": selected_certificate}
        if expected_identity is not None and identity != expected_identity:
            raise builder.BuildError("The source APK or selected local signing identity changed after update qualification. No cached output or uninstall was used.")
        _certificate_output(builder, output, selected_certificate)
        if identity_out is not None: identity_out.update(identity)

    def sign(apk):
        apk = Path(apk)
        signed = apk.with_suffix(".signed.apk")
        try:
            # Capture 085642 still fails in Windows 30.0.2 zipalign after the
            # ZIP32 repair. Its fseek/ftell reader cannot represent >2 GiB on
            # Windows. Repack now aligns local stored data in its existing raw
            # write pass. Check those actual offsets before Java signing;
            # neither another APK copy nor another Unity run is necessary.
            alignment = _core(builder).check_alignment(apk)
            builder.write_json(output / "logs/update-align.log", alignment)
            _signing_command(builder, [tools["java"], "-jar", tools["apksigner"], "sign", "--ks", str(key),
                             "--ks-key-alias", private["alias"], "--ks-pass", "env:GHVR_QUEST_SIGNING_PASSWORD",
                             "--key-pass", "env:GHVR_QUEST_SIGNING_PASSWORD", "--out", str(signed), str(apk)],
                            output / "logs/update-sign.log", env=environment, password=private["password"])
            os.replace(signed, apk)
        finally:
            signed.unlink(missing_ok=True)

    def verify(apk):
        result = _signing_command(builder, [tools["java"], "-jar", tools["apksigner"], "verify", "--verbose", "--print-certs", str(apk)],
                                  output / "logs/update-signature.log", password=private["password"])
        match = re.search(r"certificate SHA-256 digest:\s*([0-9a-fA-F]{64})", result)
        if not match:
            raise builder.BuildError("Updated APK has no verified signing certificate.")
        # Earlier legitimate outputs used the SDK's 4-KiB native-library rule.
        # Accept that rule on verification; freshly repacked files use 16-KiB.
        _core(builder).check_alignment(apk, shared_library_alignment=4096)
        certificate = match[1].lower()
        if selected_certificate is not None and certificate != selected_certificate:
            raise builder.BuildError("Updated APK was signed by a different key than the qualified recipient identity.")
        _certificate_output(builder, output, certificate)
        return {"certificateSha256": certificate}

    return sign, verify


def _game_subset(builder, inputs, data, game, *, art_changed):
    rows = [row for row in inputs["game"]["files"] if row["path"].startswith("Managed/")
            or (art_changed and row["path"].startswith("StreamingAssets/aa/StandaloneWindows64/pcg_databases_assets_assets/pcg/"))]
    # This partial snapshot has its own owner; never impersonate a complete game
    # snapshot. The compiler/environment producers use exactly these files.
    builder.snapshot(data, rows, game, phase="update-game-snapshot")


def _art_rows(files):
    producers = {"scripts/generate-environment-meshes.py", "tools/quest-builder/environment_bank.py", "tools/quest-builder/mod_assets.py"}
    return sorted((row for row in files if row["path"].startswith(("unity/GloomhavenVR.Assets/", "tools/environment-mesh/"))
                   and not row["path"].endswith(".md") or row["path"] in producers), key=lambda row: row["path"])


def _code_rows(files):
    """Conservatively retain every consumed runtime/compiler source identity.

    The source snapshot also contains browser, installer and recovery tools.
    Those do not become IL2CPP code. Native/assets and orchestration are checked
    independently before this comparison; an unsupported change never reaches
    the no-compiler path. Compiler-driver edits remain inputs even when they
    only improve reporting, rather than guessing whether new code is equivalent.
    """
    prefixes = ("src/", "tools/QuestWeaver/", "libs/RefAsm/")
    producers = {"tools/quest-builder/apk_update.py", "tools/quest-builder/update_driver.py",
                 "unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestCodeUpdateBuild.cs"}
    def selected(name):
        # Every release records its commit and complete host-file inventory.
        # This metadata is qualified by source_inventory but consumed by no
        # runtime compiler; a new Wizard archive must not rebuild native code.
        if name == "quest-builder-release.json": return False
        if name.startswith(prefixes) or name in producers: return True
        if name.startswith("unity/GloomhavenVR.Quest/Assets/Quest/Runtime/"): return True
        return "/" not in name and name.endswith((".props", ".targets", ".config", ".json"))
    return sorted((row for row in files if selected(row["path"])), key=lambda row: row["path"])


def _update_source_rows(files, *, art_changed):
    """Snapshot only consumed compiler sources on an independently owned path.

    Retain the full captured mod identity in the signed update. A code compiler
    does not consume the large prebuilt Proton/converter programs, original
    recovery tools or authored banks. Art updates retain the full source set
    because their producer also builds original-derived environment geometry.
    A partial snapshot must never occupy inputs/mod, whose owner is a complete
    source conversion and may be reused by a genuine later full build.
    """
    if art_changed: return files
    prefixes = ("src/", "tools/QuestWeaver/", "unity/GloomhavenVR.Quest/", "libs/RefAsm/", "libs/RuntimeDeps/")
    # The release inventory describes complete inputs; this private partial
    # compiler snapshot must not advertise itself as another complete release.
    return [row for row in files if row["path"] != "quest-builder-release.json" and
            (row["path"].startswith(prefixes)
             or "/" not in row["path"] and row["path"].endswith((".props", ".targets", ".config", ".json")))]


# Exact complete sources reviewed against the existing orchestration aliases in
# preparation_identity. These are historical INPUT/UPDATE identities, not loose
# version ranges. A capsule without payloadProducer still signed those rows.
# No code is recovered from Git at build time and an unknown/mutated row is not
# accepted. All listed revisions normalize to the same complete producer AST.
_LEGACY_BUILDER_PRODUCERS = {
    (147152, '114fc82dbac09e1c563888bac005c7d2c4ff8e3f0ff3138505a6ed2fd0cb20b2'),  # last delivered B671

    (132328, "419aaa4eec29164fbf8298c60b5bea7ec9392d9eb74d35084f84000d92ab7b67"),  # 8063c8fc8
    (133597, "545fa081d3ef9e8839935e4b13464b461473a3f6e6c176ff71d91d271e482c92"),  # 19b973084
    (134727, "0d8231f08b47cf5816145a08d5dc20e1b5f03945be0817fd747710bfc2855d24"),  # cb6f84fb2
    (135037, "95d75fd770c3f729c00d836f71c18da2daff4a3535acd63c2ec3c8099207bbb9"),  # f23bea592
    (140271, "d65dd915ec9d2ded7614238690e64c0f746f1f8caf7afe4244878e0108129bbd"),  # 5cfcfa1c0
    (141280, "23915dd40b9a3b8b8089ad545d4731c8ba40d1d3c88ad373113f6af91874deff"),  # 4b4b40b1d
    (145682, "3e6a2ea23cce800fb041c5ffa4782feb63a205422a81fb6e97f946bee03c35ac"),  # 0acf20ef5
    (147152, "114fc82dbac09e1c563888bac005c7d2c4ff8e3f0ff3138505a6ed2fd0cb20b2"),  # 3fa908fc0
}
_LEGACY_BUILDER_AST = "6905242806bb695c86e84be373112f21804c425afa710398cb40b665f4d608d1"

# The B665 -> B671 hardware report migrated the complete original project for
# changes to final read-only Shader/Compute audits. These exact revisions leave
# campaign_compute.stage/recovery_process and every generated asset unchanged.
# Native Shader code only reads delivered native bytes and writes audit JSON.
# Unknown source edits (including new converter behavior) still need migration.
_PAYLOAD_AUDIT_ONLY_ROWS = {
    "tools/quest-builder/campaign_compute.py": {
        (22840, "a9f9201ba8a5c2c205f2c334efe1d57c715244846e3899dba8e83384d4158a97"),
        (23104, "b7215b2c8e61918816f4050e24c0a78469d4f12ea572aabda1e59d54497b4c86"),
        (24752, "4c12d3cc7023b2a576e4049e8b62ba7bc9ce8e3b0f46b1d250d247793ba1a4bc"),
    },
    "tools/quest-builder/campaign_native_shaders.py": {
        (35994, "64e7213dcedda078a38aae8b5dd9b5e37253644eb16a607cd0d193b86704b2f3"),
        (41271, "81805d07e6fe37a2697a31b2b1767f8b233c8641c0c5e2228f4f62b782863306"),
        (41671, "85d41dfd671901d6ba830f6e23f080f484828558b489843623e29fcb7727a7d6"),
    },
}

# Reviewed minimum-work Editor consumers retain all actual serialized assets.
_PAYLOAD_AUDIT_ONLY_ROWS.update({'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestBuild.cs': {(49322, '19d6e2358ba946b00f038c38674b1a0c37766d1a76ecfa9b0e5e29ec5ca84366'), (54277, 'dbea1fde653acfafd281186f966ea7951eda6b6fc5f325d2c05b83c979b546f4')}, 'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestStartupAddressablesBuild.cs': {(38495, '9a9d7ddadcee369ac3551102b608b13b5c92c57f993fe170e05ab314005e86ac'), (35449, 'f1e997c4ed9de67bf7f9c920df4d2d464c27b11e6cf34a5edbcbd6aedf358dc6')}, 'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestCampaignShaderValidation.cs': {(47628, '8adfde472a64405672550a0890274569603a56b21aba7f5fcdabcd3dbeac6740'), (48221, '73883a7f76937561365b3276daeec221ee4c522999429d9ab43762423ba3bbcb')}})

# Exact scheduling/observation and decoded-content-equivalent ZIP wrappers.
_PAYLOAD_AUDIT_ONLY_ROWS.update({'tools/quest-builder/full_assets.py': {(32663,
                                         '871186d12af12ab23dd59746acd9a0ade6d87feee03f6b654d4b6451e874206e'),
                                        (33240,
                                         '6b0bd5ab1b5f0596c2484869b992cdaa9d35794c7b5139753aca10e662f27db1')},
 'tools/quest-builder/native_content_pack.py': {(16256,
                                                 '18bb1c74ec6da61c1c4500b0c94ba3b505762da645de10c338694bbb1c163ddd'),
                                                (22386,
                                                 '29c8e696072116265634379acbb89c84051c46701ad4e2b4ee577699f4d3a714')},
 'tools/quest-recovery/bundle_recovery.py': {(32407,
                                              '7a3993a642486797d43529e8e5e443c7f747cadb3da103c015c7e33b3c3e8a93'),
                                             (41183,
                                              '9d08921fe17ca32a679c6fca1a87d4dc29ccd8062b8ef86a0083ae8d1f690eaf')},
 'tools/quest-recovery/full_recovery.py': {(14972,
                                            '7c50355e648109232182f84ea271017603c12c87f70380cce76dbdca5ed23858'),
                                           (16521,
                                            'ca5019594872459fac3aa2eaf62eadaec08904904dfeea981d34e67a9d98202d')},
 'tools/quest-recovery/native_evidence.py': {(14123,
                                              'f272fd2db64f6b59a2d7e4c2fcc9eb130bc0996b9fb21fa20f7c82cb12ec8d62'),
                                             (14904,
                                              '4538cbbf9be0c6d4ed396076763b3809ff635131cf09de14b50affd94855c03f')},
 'tools/quest-recovery/recover.py': {(47667,
                                      'c0e738efa56b4c6c29cea965eb94a6e274e0ed19bd3802a42206cf343339365f'),
                                     (54417,
                                      'abb9abd9a35e27a618a5d3a08d1bd364a140d117c107f56557c87e026eb8b368')},
 'tools/quest-recovery/recovery_workers.py': {(4825,
                                               '462fd29f8096787f32f62ccae6fa682003eedd2a2b0c8afc8ed7573ef784fa97')}})
_PAYLOAD_INTRODUCED_OBSERVERS = {'tools/quest-recovery/recovery_workers.py': {'path': 'tools/quest-recovery/recovery_workers.py', 'size': 4825, 'sha256': '462fd29f8096787f32f62ccae6fa682003eedd2a2b0c8afc8ed7573ef784fa97'}}


def _legacy_payload_producer(files):
    rows = [row for row in files if row["path"] == "tools/quest-builder/builder.py"]
    if len(rows) == 1 and (rows[0].get("size"), rows[0].get("sha256")) in _LEGACY_BUILDER_PRODUCERS:
        return {"sha256": _LEGACY_BUILDER_AST}
    return None


def _audit_only_change(path, current, previous):
    added = _PAYLOAD_INTRODUCED_OBSERVERS.get(path)
    if added is not None and previous is None and current == added: return True
    known = _PAYLOAD_AUDIT_ONLY_ROWS.get(path)
    return bool(known and isinstance(current, dict) and isinstance(previous, dict)
                and (current.get("size"), current.get("sha256")) in known
                and (previous.get("size"), previous.get("sha256")) in known)


def _native_rows(files):
    """A code-only update cannot silently retain changed non-IL2CPP programs."""
    interop = {"QuestGameNetwork.cs", "QuestContentHash.cs", "QuestPassthroughFeature.cs", "QuestGameProcedural.cs"}
    producers = {"tools/quest-builder/native_plugins.py", "tools/quest-builder/campaign_native.py",
                 "scripts/build-quest-native.py",
                 "unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestNativePluginContract.cs"}
    prefixes = ("tools/quest-native/", "tools/quest-network/", "tools/quest-procedural-runtime/")
    def selected(name):
        if name in producers: return True
        if name.startswith("unity/GloomhavenVR.Quest/Assets/Quest/Runtime/") and Path(name).name in interop: return True
        return name.startswith(prefixes) and not (name.endswith((".md", ".meta")) or "/tests/" in name)
    return sorted((row for row in files if selected(row["path"])), key=lambda row: row["path"])


def _payload_rows(files):
    """Sources whose output is not replaced by the compact code/art writer.

    All Runtime C# files are compiled by stage_code_project; the static weaver
    and authored VR bank producer also run again when required. Original
    serialized scenes, shaders, recovered media and native programs do not.
    Their producer/template changes therefore need the ordinary preparation
    path, which retains independently compatible conversion checkpoints.
    Host-only progress, installer and diagnostics edits do not invalidate game
    payloads. A missing producer in an older manifest is a real mismatch too.
    """
    producers = {"builder.py", "startup.py", "campaign.py", "audio.py", "media.py", "shaders.py", "sprites.py",
                 "ui_assets.py", "ui_blur.py", "full_assets.py", "full_audio.py", "full_shaders.py", "full_sprites.py",
                 "full_texture2d.py", "full_textures.py", "campaign_compute.py", "campaign_shaders.py",
                 "campaign_native_shaders.py", "stereo_eye_repair.py", "script_order.py", "installation_manifest.py",
                 "native_content_pack.py", "preparation_repair.py", "import_workspace.py"}
    def selected(name):
        if name.startswith("unity/GloomhavenVR.Quest/"):
            # Runtime .meta and asmdef changes can change retained script GUIDs
            # or assembly identities, even when the C# bodies are unchanged.
            if name.startswith("unity/GloomhavenVR.Quest/Assets/Quest/Runtime/") and name.endswith(".cs"):
                return False
            if name == "unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestCodeUpdateBuild.cs":
                return False
            return not name.endswith(".md")
        if name.startswith("tools/quest-builder/") and Path(name).name in producers:
            return True
        if name == "src/GloomhavenVR/Assets/GloomhavenVR_logo.png": return True
        return name.startswith(("tools/quest-recovery/", "tools/quest-shaders/", "tools/quest-compute/",
                                "libs/RuntimeDeps/", "libs/Natives/", "prebuilt/")) and not name.endswith(".md") and "/tests/" not in name
    return sorted((row for row in files if selected(row["path"])), key=lambda row: row["path"])


def payload_producer(builder, source, files):
    """Bind normalized complete orchestration to exact captured source bytes.

    Only the existing finite reviewed observer aliases may normalize executable
    AST changes. Every unknown producer, prepare() call, constant and argument
    remains qualified. A legacy source without these records gains no proof.
    """
    names = ("tools/quest-builder/builder.py", "tools/quest-builder/preparation_identity.py")
    selected = {name: [row for row in files if row.get("path") == name] for name in names}
    if not any(selected.values()): return None
    if any(len(rows) != 1 for rows in selected.values()):
        raise builder.BuildError("Current captured Builder payload producer inventory is incomplete.")
    raw = None
    for name in names:
        row = selected[name][0]
        path = builder._ordinary_owned(Path(source) / name)
        before = path.lstat()
        if (not stat.S_ISREG(before.st_mode) or before.st_nlink != 1 or before.st_size > 2 * 1048576
                or getattr(before, "st_file_attributes", 0) & 0x400):
            raise builder.BuildError("Captured Builder payload producer is not a bounded regular owned file.")
        with path.open("rb") as stream: body = stream.read(2 * 1048576 + 1)
        after = path.lstat()
        stamp = lambda item: (item.st_dev, item.st_ino, item.st_size, item.st_mtime_ns, item.st_ctime_ns)
        if (stamp(before) != stamp(after) or len(body) != row["size"]
                or hashlib.sha256(body).hexdigest() != row["sha256"]):
            raise builder.BuildError("Captured Builder payload producer bytes changed or lack source provenance.")
        if name == names[0]: raw = body
    normalizer = getattr(builder, "preparation_identity", None)
    if normalizer is None:
        raise builder.BuildError("The current Builder lacks its complete payload producer normalizer.")
    return {"schema": 1, "scope": "quest-builder-producer-ast-v1", "source": copy.deepcopy(selected[names[0]][0]),
            "normalizer": copy.deepcopy(selected[names[1]][0]), "sha256": normalizer.builder_producer_digest(raw)}


def update_requirements(builder, source, base, files, mod_build):
    """Classify actual captured sources before provisioning the Unity compiler.

    This is a requirements estimate, never APK/signature/ownership admission.
    The final inspect invocation repeats it after qualifying those boundaries.
    It reads only captured producer source and bounded package/capsule metadata;
    neither the original game banks nor the imported Unity project are opened.
    """
    original = base["manifest"]
    current_mod = (base.get("update") or {}).get("mod", original["mod"])
    capsule = base.get("capsule")
    # Earlier signed capsules predate payloadProducer, but INPUT/UPDATE
    # already contains each exact producer's size and SHA. Only the finite
    # reviewed legacy sources may supply that missing AST identity.
    producer = payload_producer(builder, source, files) if capsule else None
    reasons = rebuild_reasons(files, current_mod["files"], payload_producer=producer,
                              previous_producer=capsule.get("payloadProducer") if capsule else None)
    if capsule is None:
        reasons.append("The older APK has no signed compact compiler/package compatibility capsule.")
    elif capsule["packageAbi"] != package_fingerprint(builder, source, original):
        reasons.append("The selected Unity package/template ABI changed.")
    code_changed = (_code_rows(files) != _code_rows(current_mod["files"])
                    or mod_build != base.get("modBuild", current_mod.get("modBuild")))
    art_changed = _art_rows(files) != _art_rows(current_mod["files"])
    return {"rebuildReasons": reasons, "codeChanged": code_changed, "artChanged": art_changed,
            "requiresUnity": bool(reasons or code_changed or art_changed),
            "updateStrategy": "cached-full-build" if reasons else "compact-code"}


def rebuild_reasons(current_files, previous_files, *, payload_producer=None, previous_producer=None):
    """Classify real unsupported payload changes without reading game banks."""
    reasons = []
    for name, selected in (("Native Quest programs or interop sources", _native_rows),
                           ("Quest scenes, shaders, package settings or game-asset producers", _payload_rows)):
        current = {row["path"]: row for row in selected(current_files)}
        previous = {row["path"]: row for row in selected(previous_files)}
        prior = previous_producer if previous_producer is not None else _legacy_payload_producer(previous_files)
        same_builder = (payload_producer and prior and payload_producer["sha256"] == prior["sha256"])
        changed = sorted(path for path in current.keys() | previous.keys() if current.get(path) != previous.get(path)
                         and not (selected is _payload_rows and
                                  (path == "tools/quest-builder/builder.py" and same_builder
                                   or _audit_only_change(path, current.get(path), previous.get(path)))))
        if changed:
            shown = ", ".join(changed[:4])
            suffix = " (+" + str(len(changed) - 4) + " more)" if len(changed) > 4 else ""
            reasons.append(name + " changed: " + shown + suffix)
    return reasons


def _compact_player_command(builder, argv, launch_log, unity_log, env):
    """Recognize only the current compiler's explicit serialized migration.

    Unity can reject a removed old script before writing a native-pair receipt.
    That one producer exception requests preparation; a stale log, ordinary
    shader/compiler failure or unrelated exception never authorizes a rebuild.
    """
    unity_log = Path(unity_log)
    def stamp():
        if not unity_log.exists(): return None
        value = unity_log.lstat()
        if not stat.S_ISREG(value.st_mode) or value.st_nlink != 1 or getattr(value, "st_file_attributes", 0) & 0x400:
            raise builder.BuildError("Compact compiler log is not a regular owned file.")
        return value.st_dev, value.st_ino, value.st_size, value.st_mtime_ns, value.st_ctime_ns
    before = stamp()
    try:
        return builder.command(argv, launch_log, env=env)
    except builder.BuildError as exc:
        after = stamp()
        if after is not None and after != before:
            with unity_log.open("rb") as stream:
                stream.seek(max(0, after[2] - 65536))
                text = stream.read(65536).decode("utf-8", errors="replace")
            migration = re.search(r"(?:^|\n)(?:InvalidDataException: )?(Retained original serialized script is missing: [^\r\n]{1,512}; a full build is required\.)\r?(?:\n|$)", text)
            if migration and stamp() == after:
                raise _core(builder).RebuildRequired(migration[1]) from exc
        raise


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
                            version_name="0.1.0.B" + str(inputs["mod"]["modBuild"]) + "." + root.name[:12], code_key=root.name,
                            retain_compatible_library=True)
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
        _compact_player_command(builder, builder.unity_launcher(tools["editor"]) + ["-quit", "-projectPath", str(project),
                        "-buildTarget", "Android", "-executeMethod", "GloomhavenVR.Quest.Editor.QuestCodeUpdateBuild.Build",
                        "-logFile", str(output / "logs/update-code-unity.log")], output / "logs/update-code-launch.log",
                        output / "logs/update-code-unity.log", env)
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
    apk, output = Path(apk), Path(output)
    package_abi = package_fingerprint(builder, source, inputs)
    producer = payload_producer(builder, source, inputs["mod"].get("files", []))
    receipt_key = builder.value_hash({"scope": "full-build-update-capability-receipt-v1", "inputKey": inputs["inputKey"],
        "profile": inputs["profile"], "modBuild": inputs["mod"]["modBuild"], "packageAbi": package_abi,
        "payloadProducer": producer})
    stages = builder.Stages(output)

    def close_capability():
        # An atomic APK replacement can precede its stage receipt if the host
        # dies during later delivery. Adopt exact signed metadata once, without
        # rereading the original MonoScript roster or recopying the Player.
        adopted = False
        try:
            base = core.inspect_base(apk); capsule = base["capsule"]; update = base["update"]
            expected_key = builder.value_hash({"inputKey": inputs["inputKey"], "scope": "full-build-update-capability", "capsule": capsule})
            expected_update = {"schema": 1, "updateKey": expected_key, "gameInputKey": inputs["inputKey"],
                "mode": "build-capability", "modBuild": inputs["mod"]["modBuild"], "profile": inputs["profile"]}
            with zipfile.ZipFile(apk) as archive:
                profile = core._json(archive, core.PROFILE, optional=True)
            adopted = bool(capsule and capsule["packageAbi"] == package_abi and capsule["dynamicProfile"]
                           and capsule.get("payloadProducer") == producer
                           and base["inputKey"] == inputs["inputKey"] and profile == inputs["profile"]
                           and update == expected_update)
        except (builder.BuildError, OSError, ValueError, zipfile.BadZipFile):
            adopted = False
        if adopted:
            _signers(builder, tools, output, base_apk=apk)
        else:
            capsule = core.make_capsule(apk, package_abi=package_abi, dynamic_profile=True,
                                        repair_update_contracts=True, payload_producer=producer)
            key = builder.value_hash({"inputKey": inputs["inputKey"], "scope": "full-build-update-capability", "capsule": capsule})
            update = {"schema": 1, "updateKey": key, "gameInputKey": inputs["inputKey"],
                "mode": "build-capability", "modBuild": inputs["mod"]["modBuild"], "profile": inputs["profile"]}
            sign, verify = _signers(builder, tools, output)
            capable = apk.with_name(apk.stem + ".capable.apk")
            result = core.repack(apk, capable, {core.CAPSULE: builder.canonical(capsule), core.PROFILE: builder.canonical(inputs["profile"])},
                update_manifest=update, signer=sign, verifier=verify, repair_update_contracts=True)
            stamp = core._base_stamp(capable)
            os.replace(capable, apk)
            # Repack has hashed the exact verified output. Rename retains its
            # inode/size/mtime; qualification still guards all current stamps.
            current = core._base_stamp(apk)
            if current[1:5] != stamp[1:5]:
                raise builder.BuildError("Signed Player changed during capability publication; the completed Player is retained.")
            with stages._witnesses("player-update-capability", receipt_key) as witnesses:
                witnesses.observe(apk, hasher=lambda path: result["sha256"])
                if core._base_stamp(apk) != current:
                    raise builder.BuildError("Signed Player changed while recording its capability proof.")
        details = {"schema": 1, "dynamicProfile": True, "scriptTypes": len(capsule["scriptTypes"]), "packageAbi": package_abi,
                   "payloadProducer": capsule.get("payloadProducer")}
        return [apk, *(output / "signing" / name for name in ("certificate.json", "quest.keystore", "local-key.json"))], details

    return stages.run("player-update-capability", receipt_key, close_capability)["details"]


def retained_source_package(builder, apk, output, fallback=None):
    """Carry a direct nested-bank locator across updates, never a bank proof.

    Repeated updates must not grow a chain of previous APK dependencies: users
    can remove old import/update caches after a successful export. The package
    helper qualifies its bounded locator against this APK's embedded bank
    identity. Missing helpers/legacy manifests keep their existing behavior;
    a physical bank is not a portable outer ZIP and must not be labelled one.
    """
    helper = getattr(builder, "portable_packages", None)
    resolve = getattr(helper, "content_source", None)
    if not callable(resolve): return fallback
    try:
        with zipfile.ZipFile(apk) as archive:
            archive.getinfo("assets/Quest/content-delivery.json")
        source = resolve(apk, output)
        if (isinstance(source, dict) and source.get("member") == getattr(helper, "CONTENT", "GloomhavenVR-Quest-content.zip")
                and isinstance(source.get("path"), (str, os.PathLike))):
            return str(Path(source["path"]).resolve())
    except (OSError, KeyError, TypeError, ValueError, zipfile.BadZipFile, builder.BuildError):
        # Optional locator preservation never turns an otherwise valid APK/code
        # update into a failure. Export/installation qualify required bank bytes.
        pass
    return fallback


def run(builder, args, repo, output):
    # Do not authorize a cached update from yesterday's absent PC installation.
    plan = inspect(builder, args, repo, output)
    core = _core(builder); base_apk = Path(plan["baseApk"]); base = core.inspect_base(base_apk)
    inputs, update_key = plan["inputs"], plan["updateKey"]
    root = output / "updates" / update_key
    apk = root / "GloomhavenVR-Quest.apk"
    tools = signing_tools(builder, args, output)
    sign, verify = _run_signers(builder, args, output, base_apk, tools, plan["signing"])
    if plan["updateStrategy"] == "cached-full-build":
        return _rebuild(builder, args, plan, repo, output)

    def produce():
        replacements = {}
        code = None
        if args.command == "update-profile":
            builder.build_progress.operation("update-profile", detail="Preparing the signed offline profile.")
            replacements.update(core.profile_update(base_apk, inputs["profile"]))
            builder.build_progress.operation("update-profile", complete=True, detail="Signed offline profile prepared; no Unity build.")
        else:
            changed_art = plan["artChanged"]
            if plan["codeChanged"] or changed_art:
                builder.build_progress.operation("update-mod-source", detail="Freezing the current mod source.")
                source_rows = _update_source_rows(inputs["mod"]["files"], art_changed=changed_art)
                source_key = builder.value_hash({"mod": inputs["mod"]["key"], "files": source_rows,
                                                "scope": "compact-update-source-v1"})
                source = output / "updates/source-inputs" / source_key
                builder.snapshot(repo, source_rows, source, phase="update-source-snapshot")
                builder.build_progress.operation("update-mod-source", complete=True, detail="Current mod source snapshot retained.")
                game = output / "updates/game-inputs" / inputs["game"]["key"] / ("with-art" if changed_art else "code")
                _game_subset(builder, inputs, builder.game_data(args.game_root), game, art_changed=changed_art)
            else:
                builder.build_progress.operation("update-mod-source", complete=True,
                    detail="Runtime/compiler and authored art sources are unchanged; no new source or game snapshot.")
            # A stable private compiler directory retains Unity/Bee's native
            # object cache across mod versions. Its owner requires the same
            # converted game and package ABI; each update keeps separate receipts.
            if plan["codeChanged"]:
                compiler_scope = builder.value_hash({"gameInputKey": base["inputKey"],
                    "packageAbi": package_fingerprint(builder, source, inputs), "scope": "quest-code-project-v1"})
                project = output / "updates/code-projects" / compiler_scope
                code = _compile_code(builder, args, inputs, output, source, game, project, base_apk, tools, root)
                replacements.update(code["replacements"])
            else:
                builder.build_progress.operation("update-code", complete=True,
                    detail="Signed runtime code and metadata retained; no Unity or native compilation.")
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
                  "mod": inputs["mod"], "ownedGame": plan["ownedGame"], "signing": plan["signing"],
                  "sourceCertificateSha256": plan["signing"]["sourceCertificateSha256"]}
        replacements.update(core.patch_resource_json(base_apk, {
            "quest-build": {"schema": 1, "inputKey": base["inputKey"], "modBuild": inputs["mod"]["modBuild"]},
            "quest-profile": inputs["profile"]}, replacements=replacements))
        replacements["AndroidManifest.xml"] = core.update_android_versions(base_apk,
            inputs["mod"]["modBuild"], "0.1.0.B" + str(inputs["mod"]["modBuild"]) + "." + base["inputKey"][:12])
        builder.build_progress.operation("update-repack", detail="Reusing unchanged APK payloads and signing the update.")
        result = core.repack(base_apk, apk, replacements, update_manifest=update, signer=sign, verifier=verify, code_evidence=code,
                             progress=lambda done, total, name: builder.build_progress.event(
                                 "update-apk-copy", done, total, "bytes", name, operation="update-repack"))
        output_hash = observe_repacked_output(builder, output, apk, update_key, result)
        builder.build_progress.operation("update-repack", complete=True, detail="Signed update APK assembled.")
        builder.build_progress.operation("update-verify", detail="Publishing verified update evidence.")
        report = Path(str(apk) + ".build.json")
        details = {"inputKey": base["inputKey"], "updateKey": update_key, "package": builder.PACKAGE,
                   "modBuild": inputs["mod"]["modBuild"], "isDiagnostic": False,
                   "isDummy": bool(inputs["profile"].get("isDummy")), "apkSha256": output_hash,
                   "contentFiles": [], "retainedGameContent": True, "updateResult": result,
                   "signing": plan["signing"], "sourceCertificateSha256": plan["signing"]["sourceCertificateSha256"],
                   "sourcePackage": retained_source_package(builder, base_apk, output, plan.get("sourcePackage")),
                   "baseApk": plan["baseApk"],
                   **({"certificateSha256": result["certificateSha256"]} if "certificateSha256" in result else {})}
        builder.write_json(report, {"schema": 1, "buildResult": "Succeeded", "scope": "APK update", **details})
        builder.build_progress.operation("update-verify", complete=True, detail="Signed update published; original game content is retained.")
        return [apk, report], details

    try:
        receipt = builder.Stages(output).run("build", update_key, produce)
    except core.RebuildRequired as exc:
        # A valid compiled registry can prove a serialized migration is
        # necessary. This is not a completed update stage; keep its independent
        # compiler receipts and route only typed compatibility to preparation.
        return _rebuild(builder, args, plan, repo, output, reason=str(exc))
    builder.write_json(output / "latest-build.json", {"schema": 1, "apk": apk.relative_to(output).as_posix(),
                       "receipt": "receipts/build/" + update_key + ".json", "details": receipt["details"]})
    print("update: " + str(apk), flush=True)
    return apk


def observe_repacked_output(builder, output, apk, update_key, result):
    """Close the normal stage witness from repack's exact verified byte proof."""
    core = _core(builder)
    current = core._base_stamp(apk)
    if (result.get("schema") != 1 or result.get("output") != str(apk)
            or not isinstance(result.get("sha256"), str) or not re.fullmatch(r"[0-9a-f]{64}", result["sha256"])
            or result.get("outputStamp") != list(current)):
        raise builder.BuildError("Signed update APK changed before recording its verified output proof.")
    with builder.Stages(output)._witnesses("build", update_key) as witnesses:
        witnesses.remember(apk, result["sha256"], stamp=witnesses.current(apk))
        if core._base_stamp(apk) != current:
            raise builder.BuildError("Signed update APK changed while recording its verified output proof.")
    return result["sha256"]


def _rebuild(builder, args, plan, repo, output, *, reason=None):
    selected = copy.deepcopy(plan)
    selected["updateStrategy"] = "cached-full-build"
    selected["requiresUnity"] = True
    if reason and reason not in selected["rebuildReasons"]:
        selected["rebuildReasons"].append(reason)
    root = output / "updates" / selected["updateKey"]
    builder.write_json(root / "plan.json", selected)
    builder.build_progress.event("update-rebuild-required", detail="; ".join(selected["rebuildReasons"]),
                                 operation="update-code", status="progress")
    callback = getattr(builder, "rebuild_update", None)
    if not callable(callback):
        raise builder.BuildError("The selected APK requires current Quest asset/native patches; this Builder lacks the cache-aware migration path. Keep the workspace and use the current Builder.")
    return callback(args, selected, repo, output)
