"""Requalify a completed compact Player before touching its compiler project.

The production receipt and current APK gates are independent: host parser repairs
must not repeat successful weaving, SDK binding and IL2CPP compilation. No folder
scan or edited receipt stands in for the original compiler's matched native pair.
"""
from __future__ import annotations
import ast
import hashlib
import json
from pathlib import Path
import re
import stat
import zipfile

# Support205558: these exact delivered host sources completed the Player and
# wrote its native-pair receipt, then failed solely during roster collection.
# A finite reviewed bridge supports that legacy plan without an invented recipe.
_LEGACY_HOST = {
    "update_driver.py": "e0756f5738d1f3791197769d15b059c4f166e4576aac57f1f44d71828e65f894",
    "apk_update.py": "0febdc91294da2b64843491fd50dcba4aa6fc8f1882a02310799beaba5e01af3",
}
_PREVIOUS_RECIPE = "f9326c1a8bdcecad3cce0f6b6e2954389479a260d094d31b5477502d51de93e3"
# This reviewed recipe removes only the post-compilation replacement allowlist
# and signed APK publisher from the native producer census. Their callers are
# outside stage_code_project/code_build_env; neither produces compiler input.
_LEGACY_RECIPE = "ce4a2de2b4a86cc7eec667875c128ad0116d5afcc095ead4d39e88f19630ed02"
# Exact B691 data-only selector correction. The complete native producer
# rows and matched code/metadata qualification remain mandatory below.
_PIXEL_SCOPE_PREVIOUS = 'd1171ab145d33bcdcbeba7cbc6d4117cf1aa2c519033934ae8f5a4450e319667'
_PIXEL_SCOPE_CURRENT = '1507d267a9957ccc9b813a60e2f490badac20ac926f24b9c78cbae9c3c385163'
# The complete reviewed media update recipe changes only classification of
# separately delivered movie Content, bounded signed media metadata and the APK
# publisher. Stage/build/weave/package compiler calls retain their old meaning.
# Bind the entire current compiler recipe, including the new selector body;
# unknown production edits cannot cross this finite compatibility bridge.
_MEDIA_SCOPE_CURRENT = '627631b89b964c7a7a00767d5fb3ef42b19566a37f1116879dbcb963776cc92f'
_MEDIA_SCOPE_PREVIOUS = 'e59c521e2299a9f7a03691bdf1b7d577c1bffeaf2e01f9a734bcb44094784f34'
# Exact source-only acquisition and seed publication addition. Its compiler
# calls are unchanged; globals and the reviewed source-row classifier are still
# included above, so admit only this complete AST recipe, never arbitrary edits.
_INSTALLED_SOURCE_RECIPE = '11c6d285414bbd809b05169a41e42d0a87112310aa94f7670def7207a66d5eed'
_HEX = re.compile(r"[0-9a-f]{64}")


def _read(builder, path, *, limit=16 * 1048576):
    path = builder._ordinary_owned(Path(path))
    info = path.lstat()
    if (not stat.S_ISREG(info.st_mode) or info.st_nlink != 1 or info.st_size > limit
            or getattr(info, "st_file_attributes", 0) & 0x400):
        raise builder.BuildError("Retained compact update metadata is not a bounded regular owned file: " + path.name)
    with path.open("rb") as stream: raw = stream.read(limit + 1)
    after = path.lstat()
    stamp = lambda row: (row.st_dev, row.st_ino, row.st_size, row.st_mtime_ns, row.st_ctime_ns)
    if stamp(info) != stamp(after) or len(raw) > limit:
        raise builder.BuildError("Retained compact update metadata changed while reading: " + path.name)
    try:
        result = json.loads(raw)
        if not isinstance(result, dict): raise ValueError("Expected one metadata object")
        return result
    except (ValueError, UnicodeError) as exc:
        raise builder.BuildError("Retained compact update metadata is invalid: " + path.name) from exc


def compiler_recipe(builder, driver_path):
    """Hash actual compiler functions independently of post-build collection.

    Every other captured compiler producer is compared by its complete source
    row. These two host modules also own the repaired roster parser/adoption
    flow, so select their production functions explicitly rather than treating
    a parser repair as a new native-code input.
    """
    producers = ((Path(driver_path), ("_compile_code", "_build_code_player", "_bind_code_packages", "package_fingerprint", "_compact_player_command", "_native_producer_rows", "_code_rows", "_payload_rows", "_native_rows", "_art_rows", "_separate_movie")),
                 (Path(builder.apk_updates.__file__), None))
    rows = []
    for path, names in producers:
        tree = ast.parse(path.read_text(encoding="utf-8"))
        excluded = {"_script_conflict", "_admit_script_rows", "script_roster", "collect_code_update", "_replaceable", "repack"}
        selected = ([node for node in tree.body if not isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)) or node.name not in excluded]
                    if names is None else [node for node in tree.body if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)) and node.name in names])
        if names is not None and {node.name for node in selected} != set(names):
            raise builder.BuildError("The compact compiler recipe is incomplete: " + path.name)
        rows.append({"path": path.name, "functions": [ast.dump(node, include_attributes=False) for node in selected]})
    recipe = builder.value_hash({"scope": "quest-native-production-recipe-v1", "producers": rows})
    return _MEDIA_SCOPE_CURRENT if recipe == _INSTALLED_SOURCE_RECIPE else recipe


def _same_recipe(previous, recipe):
    if previous.get("nativeCompilerRecipe") == recipe:
        return True
    if recipe == _PIXEL_SCOPE_CURRENT and previous.get("nativeCompilerRecipe") == _PIXEL_SCOPE_PREVIOUS:
        return True
    if recipe == _MEDIA_SCOPE_CURRENT and previous.get("nativeCompilerRecipe") in (
            _MEDIA_SCOPE_PREVIOUS, _PIXEL_SCOPE_PREVIOUS, _PIXEL_SCOPE_CURRENT):
        return True
    # Support213445: only this explicitly reviewed host-only publication repair
    # may cross the old compiler recipe. Any real producer change has a different
    # current digest and must take the ordinary incremental compiler route.
    if recipe != _LEGACY_RECIPE:
        return False
    prior = previous.get("nativeCompilerRecipe")
    return (prior == _PREVIOUS_RECIPE or prior is None
            and all(previous.get("updateRecipe", {}).get(name) == digest for name, digest in _LEGACY_HOST.items()))


def _published_descendant(builder, base, output, previous, current, receipt, *, producer_rows, recipe, package_abi):
    """Qualify one published descendant from its signed UPDATE and native pair.

    The driver has already verified the selected APK's signature and ownership.
    Follow only its exact signed updateKey to its owned plan, never a directory
    search. The selected APK must contain the completed compiler's exact native
    pair, not merely claim the same mod version or original conversion identity.
    """
    core = builder.apk_updates
    before = core._base_stamp(base)
    info = core.inspect_base(base)
    update = info.get("update")
    if (not isinstance(update, dict) or update.get("mode") != "update-mod"
            or update.get("gameInputKey") != current.get("gameInputKey")
            or update.get("baseApkSha256") != previous.get("baseApkSha256")
            or update.get("modBuild") != current["inputs"]["mod"]["modBuild"]
            or update.get("profile") != current["inputs"]["profile"]):
        return False
    key = update.get("updateKey")
    if not isinstance(key, str) or not _HEX.fullmatch(key):
        return False
    published_root = builder._ordinary_owned(Path(output) / "updates" / key)
    if not (published_root / "plan.json").is_file():
        return False
    published = _plan(builder, published_root)
    if (published.get("baseApkSha256") != previous.get("baseApkSha256")
            or published.get("gameInputKey") != previous.get("gameInputKey")
            or published["inputs"].get("game") != current["inputs"].get("game")
            or published["inputs"].get("profile") != current["inputs"].get("profile")
            or published["inputs"].get("mod") != update.get("mod")
            or published["inputs"]["mod"].get("modBuild") != current["inputs"]["mod"].get("modBuild")
            or producer_rows(published["inputs"]["mod"]["files"]) != producer_rows(current["inputs"]["mod"]["files"])
            or published.get("artChanged") != previous.get("artChanged")
            or published.get("nativeHelperChanged") != previous.get("nativeHelperChanged")
            or not _same_recipe(published, recipe)):
        return False
    capsule = info.get("capsule") or {}
    if (info.get("inputKey") != current.get("gameInputKey")
            or info["manifest"].get("game") != previous["inputs"].get("game")
            or capsule.get("packageAbi") != package_abi):
        return False
    native = _read(builder, receipt, limit=65536)
    if (native.get("schema") != 1 or native.get("scope") != "quest-code-update-v1"
            or native.get("buildResult") != "Succeeded" or native.get("originalAssetsImported") is not False
            or native.get("abi") != "arm64-v8a" or native.get("packageAbi") != package_abi
            or native.get("unityVersion") != capsule.get("unityVersion")
            or not isinstance(native.get("files"), list)):
        raise builder.BuildError("Published compact Player has an incompatible retained native receipt.")
    with zipfile.ZipFile(base) as apk:
        entries = core._entries(apk)
        for name in (core.LIBRARY, core.METADATA):
            rows = [row for row in native.get("files", []) if isinstance(row, dict) and row.get("path") == name]
            if (len(rows) != 1 or type(rows[0].get("size")) is not int or rows[0]["size"] <= 0
                    or not isinstance(rows[0].get("sha256"), str) or not _HEX.fullmatch(rows[0]["sha256"])
                    or name not in entries or entries[name].file_size != rows[0]["size"]):
                return False
            digest = hashlib.sha256()
            with apk.open(name) as stream:
                while chunk := stream.read(1048576):
                    digest.update(chunk)
            if digest.hexdigest() != rows[0]["sha256"]:
                return False
    if core._base_stamp(base) != before:
        raise builder.BuildError("Published compact Player changed while qualifying its retained native pair.")
    return True


def _plan(builder, root):
    value = _read(builder, root / "plan.json")
    if (value.get("schema") != 1 or value.get("mode") != "update-mod" or value.get("updateKey") != root.name
            or value.get("updateStrategy") != "compact-code" or value.get("codeChanged") is not True):
        raise builder.BuildError("Retained compact Player has an incompatible update plan; no compiler output was overwritten.")
    # Match inspect()'s immutable update identity. Installed-Quest updates carry
    # an extracted original Mono seed at compilerGameRoot; its local location
    # is not a compiler input. Hashing that transport path here rejects the
    # real freshly written plan before current_plan() can qualify native work.
    # Seed bytes and original inputs retain their independent exact admission.
    original = {key: item for key, item in value.items()
                if key not in ("updateKey", "baseApk", "sourcePackage", "compilerGameRoot")}
    # The prior post-build parser may have attached a proposed rebuild verdict.
    # Reconstruct only this one declared overlay over an original compact plan.
    if original.pop("rebuildDecisionRequired", False):
        original["rebuildReasons"] = []
    if builder.value_hash(original) != root.name:
        raise builder.BuildError("Retained compact Player plan changed; keep its evidence for a targeted repair.")
    inputs = value.get("inputs")
    mod = inputs.get("mod") if isinstance(inputs, dict) else None
    files = mod.get("files") if isinstance(mod, dict) else None
    if (not isinstance(inputs, dict) or not isinstance(inputs.get("game"), dict)
            or not isinstance(inputs.get("profile"), dict) or not isinstance(files, list)
            or any(not isinstance(row, dict) or not isinstance(row.get("path"), str)
                   or type(row.get("size")) is not int or row["size"] < 0
                   or not isinstance(row.get("sha256"), str) or not _HEX.fullmatch(row["sha256"]) for row in files)
            or len({row["path"] for row in files}) != len(files)):
        raise builder.BuildError("Retained compact Player has invalid captured source inputs.")
    return value


def current_plan(builder, driver_path, inputs, root):
    plan = _plan(builder, root)
    if plan["inputs"] != inputs or plan.get("nativeCompilerRecipe") != compiler_recipe(builder, driver_path):
        raise builder.BuildError("Current compact compiler plan differs from its actual production inputs or recipe.")
    return plan


def closed_paths(builder, output, root, receipt, *, require_current=False):
    """A successful stage may reference the earlier Player without copying it."""
    records = receipt.get("outputs", [])
    names = [row.get("path") for row in records if isinstance(row, dict)]
    apks = [name for name in names if isinstance(name, str) and
            re.fullmatch(r"updates/[0-9a-f]{64}/code-player[.]apk", name)]
    if (len(names) != 2 or len(apks) != 1 or apks[0] + ".code-build.json" not in names
            or require_current and apks[0].split("/")[1] != root.name):
        raise builder.BuildError("Retained native production receipt has an unexpected output scope.")
    return tuple(builder._ordinary_owned(Path(output) / name) for name in (apks[0], apks[0] + ".code-build.json"))


def retained_player(builder, driver_path, inputs, output, source, project, base, root, package_abi, *, producer_rows, current):
    """Return current-gate-qualified native output, or no compatible candidate.

    The stable compiler owner supplies at most one prior codeKey. Never search
    arbitrary historical folders, delete suspect outputs or rewrite native pair
    evidence to make an incompatible player appear compatible.
    """
    output, source, project, root = map(Path, (output, source, project, root))
    marker = builder._ordinary_owned(project / "QuestCodeUpdate/code-project-owner.json")
    production = builder.Stages(output).valid("update-code-production", root.name)
    if production:
        compiled, receipt = closed_paths(builder, output, root, production, require_current=True)
        result = builder.apk_updates.collect_code_update(base, compiled, receipt, package_abi=package_abi)
        return compiled, receipt, result
    if not marker.is_file(): return None
    state = _read(builder, marker, limit=65536)
    owner = state.get("owner")
    if state.get("state") != "ready" or not isinstance(owner, dict): return None
    key = owner.get("codeKey")
    if not isinstance(key, str) or not _HEX.fullmatch(key):
        raise builder.BuildError("The retained compact compiler owner has no valid native output identity.")
    candidate = builder._ordinary_owned(output / "updates" / key)
    compiled, receipt = candidate / "code-player.apk", candidate / "code-player.apk.code-build.json"
    # No complete produced Player exists yet: keep the ordinary incremental
    # compiler route. A partial/corrupt existing pair is preserved and reported.
    if not compiled.exists() and not receipt.exists(): return None
    previous = _plan(builder, candidate)
    same = (previous.get("gameInputKey") == current.get("gameInputKey")
        and previous["inputs"].get("game") == inputs.get("game")
        and previous["inputs"].get("profile") == inputs.get("profile")
        and previous["inputs"].get("mod", {}).get("modBuild") == inputs["mod"]["modBuild"]
        and previous.get("artChanged") == current.get("artChanged")
        and producer_rows(previous["inputs"]["mod"]["files"]) == producer_rows(inputs["mod"]["files"]))
    if not same: return None
    recipe = compiler_recipe(builder, driver_path)
    if current.get("nativeCompilerRecipe") != recipe:
        raise builder.BuildError("Current compact compiler plan differs from its actual production recipe.")
    if not _same_recipe(previous, recipe):
        return None
    same_base = previous.get("baseApkSha256") == current.get("baseApkSha256")
    same_helpers = previous.get("nativeHelperChanged") == current.get("nativeHelperChanged")
    if not same_base:
        if (current.get("nativeHelperChanged") is not False
                or not _published_descendant(builder, base, output, previous, current, receipt,
                    producer_rows=producer_rows, recipe=recipe, package_abi=package_abi)):
            return None
    elif not same_helpers:
        return None
    if not compiled.is_file() or not receipt.is_file():
        raise builder.BuildError("Retained compact Player is incomplete; its existing files are preserved for targeted repair.")
    producer_files = [*sorted((source / "unity/GloomhavenVR.Quest/Assets/Quest/Runtime").glob("*.cs")),
                      source / "unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestCodeUpdateBuild.cs",
                      source / "src/GloomhavenVR/Core/Loc/QuestText.cs"]
    source_key = builder.value_hash([{"path": path.relative_to(source).as_posix(), "sha256": builder.digest(path)} for path in producer_files])
    expected = {"schema": 1, "gameInputKey": current["gameInputKey"], "codeKey": key,
                "sourceKey": source_key, "profileKey": builder.value_hash(inputs["profile"]), "packageAbi": package_abi,
                "versionCode": inputs["mod"]["modBuild"],
                "versionName": "0.1.0.B" + str(inputs["mod"]["modBuild"]) + "." + key[:12],
                "packagesKey": builder.value_hash(None)}
    if owner != expected:
        raise builder.BuildError("Retained compact Player owner differs from its captured compiler inputs; no output was adopted or overwritten.")
    for path in (compiled, receipt):
        info = builder._ordinary_owned(path).lstat()
        if not stat.S_ISREG(info.st_mode) or info.st_nlink != 1:
            raise builder.BuildError("Retained compact Player output is not a regular owned file.")
    # Native pair hashes, ABI, serialized layout, assembly ownership, callbacks
    # and sidecars are all checked by the current collector before any new
    # successful receipt. A parser fix does not relax these APK gates.
    result = builder.apk_updates.collect_code_update(base, compiled, receipt, package_abi=package_abi)
    return compiled, receipt, result
