"""Preview finite owned intermediates; explicitly retire stopped Wizard runs.

Only metadata and small ownership receipts are read. APK/content bytes are never
rehashed to offer cleanup. Eligible intermediate/source paths stay inside the
Wizard-owned workspace. Run retirement is a separate confirmed choice retaining
small diagnostics and optionally deleting only its exclusive intermediates. A
preview binds exact tree identities in memory; restarting requires a new preview
rather than accepting paths from a client.
"""
from __future__ import annotations

from contextlib import contextmanager, ExitStack
import copy
import errno
import hashlib
import importlib.util
import json
import os
from pathlib import Path, PurePosixPath, PureWindowsPath
import re
import secrets
import stat
import threading
import time

from state import Cancelled, STAGES, WizardError, atomic_json, canonical, file_lock, ordinary, stage_progress, value_hash

HEX = re.compile(r"[0-9a-f]{64}\Z")
SESSION = re.compile(r"[0-9a-f]{32}\Z")
MAX_JSON_BYTES = 16 * 1048576
MAX_IDENTITIES = 256
PREVIEW_SECONDS = 1800
_plans = {}
_plans_lock = threading.RLock()
_storage = None
_access = None
_profile = None
INVENTORY_CATEGORIES = ("inputs", "converted-assets", "unity-projects", "unity-caches", "tools",
                        "apk-content", "signing-backups", "logs-captures", "other")


def _inventory_category(relative):
    parts = relative.parts
    if "signing-recovery" in parts: return "signing-backups"
    if parts[:2] in (("build", "builds"), ("build", "updates")) or parts[:1] == ("exports",): return "apk-content"
    if parts[:2] == ("build", "inputs"): return "inputs"
    if parts[:2] == ("build", "projects"):
        if len(parts) > 3 and parts[3] in ("Library", "Temp", "Obj", "obj", "bin"): return "unity-caches"
        return "unity-projects"
    if parts[:2] == ("build", "cache"): return "converted-assets"
    if (parts[:2] == ("build", "tool-cache") or parts[:1] == ("tools",)
            or parts[:2] == ("build", "signing")): return "tools"
    if ("logs" in parts or "captures" in parts or relative.suffix.lower() in
            (".log", ".dmp", ".dump", ".crash")): return "logs-captures"
    return "other"


def inventory(state_root, progress=None, cancel=None):
    """Measure owned workspace files once, without hashes or following links.

    This read-only snapshot does not authorize deletion. Cleanup still needs its
    separate retained confirmation preview and producer receipts. Logical sizes
    match file-manager folder sizes; the allocated count, where available, avoids
    counting the same hard-linked inode twice. Builds may continue during a scan,
    so disappearing/inaccessible files are reported rather than silently claimed.
    The server caches this result and only refreshes it on explicit request.
    """
    proofs = {}; root, _ = _root(state_root, proofs)
    rows = {name: {"id": name, "bytes": 0, "files": 0} for name in INVENTORY_CATEGORIES}
    skipped_links = inaccessible = count = 0
    signing = {"originalApkBytes": 0, "appDataBytes": 0, "otherBytes": 0}
    allocated = 0; allocated_known = True; allocated_seen = set()
    pending = [root]; last_report = 0.0
    while pending:
        if cancel is not None and cancel.is_set():
            raise Cancelled()
        folder = pending.pop()
        try:
            ordinary(folder)
            with os.scandir(folder) as entries:
                for entry in entries:
                    if cancel is not None and cancel.is_set():
                        raise Cancelled()
                    try:
                        info = entry.stat(follow_symlinks=False)
                        if stat.S_ISLNK(info.st_mode) or getattr(info, "st_file_attributes", 0) & 0x400:
                            skipped_links += 1; continue
                        path = Path(entry.path)
                        if stat.S_ISDIR(info.st_mode): pending.append(path); continue
                        if not stat.S_ISREG(info.st_mode): skipped_links += 1; continue
                        row = rows[_inventory_category(path.relative_to(root))]
                        row["bytes"] += info.st_size; row["files"] += 1; count += 1
                        if row["id"] == "signing-backups":
                            kind = "originalApkBytes" if path.name == "original.apk" else (
                                "appDataBytes" if path.suffix == ".tar" else "otherBytes")
                            signing[kind] += info.st_size
                        if hasattr(info, "st_blocks"):
                            identity = (info.st_dev, info.st_ino)
                            if info.st_nlink <= 1 or identity not in allocated_seen:
                                if info.st_nlink > 1: allocated_seen.add(identity)
                                allocated += info.st_blocks * 512
                        else: allocated_known = False
                    except Cancelled: raise
                    except (OSError, WizardError): inaccessible += 1
        except Cancelled: raise
        except (OSError, WizardError): inaccessible += 1
        now = time.monotonic()
        if count == 0 or now - last_report >= .15:
            _notify(progress, "storage-inventory", count, None, folder.relative_to(root).as_posix())
            last_report = now
    if cancel is not None and cancel.is_set(): raise Cancelled()
    result = {"status": "complete", "totalBytes": sum(row["bytes"] for row in rows.values()),
              "files": count, "categories": list(rows.values()), "skippedLinks": skipped_links,
              "inaccessible": inaccessible, "complete": inaccessible == 0,
              "allocatedBytes": allocated if allocated_known else None, "completedAt": time.time(),
              "signingBackups": signing, "canDeleteBuildCache": has_completed_build(root)}
    _notify(progress, "storage-inventory-complete", count, count, str(result["totalBytes"]))
    return result


def has_completed_build(state_root):
    """Enable cache cleanup from retained build receipts, without APK rehashing."""
    try:
        proofs = {}; _, build = _root(state_root, proofs)
        _completed_build(build, proofs)
        return True
    except (WizardError, OSError, ValueError, KeyError, TypeError):
        return False


def _error(code, detail=""):
    return WizardError(code, "Cleanup stopped. Preview again; files already removed remain removed. " + detail,
                       "Die Bereinigung wurde gestoppt. Vorschau erneut erstellen; bereits entfernte Dateien bleiben entfernt. " + detail)


def _stamp(path):
    path = ordinary(path)
    value = path.lstat()
    if getattr(value, "st_file_attributes", 0) & 0x400 or not (stat.S_ISREG(value.st_mode) or stat.S_ISDIR(value.st_mode)):
        raise _error("cleanup_linked_path", path.name)
    if stat.S_ISREG(value.st_mode) and value.st_nlink != 1:
        raise _error("cleanup_linked_path", path.name)
    native = _helper()._file_witness_stamp(path, value) if os.name == "nt" and stat.S_ISREG(value.st_mode) else None
    return (value.st_dev, value.st_ino, value.st_mode, value.st_size, value.st_mtime_ns, value.st_ctime_ns, native)


def _read(path, proofs):
    stamp = _stamp(path)
    if not stat.S_ISREG(stamp[2]) or stamp[3] > MAX_JSON_BYTES:
        raise _error("cleanup_receipt", path.name)
    raw = path.read_bytes()
    if _stamp(path) != stamp or len(raw) > MAX_JSON_BYTES:
        raise _error("cleanup_changed", path.name)
    try: value = json.loads(raw)
    except (ValueError, TypeError) as error: raise _error("cleanup_receipt", path.name) from error
    if not isinstance(value, dict): raise _error("cleanup_receipt", path.name)
    proofs[str(path)] = {"stamp": stamp, "sha256": hashlib.sha256(raw).hexdigest()}
    return value


def _root(state_root, proofs):
    root = ordinary(state_root)
    if _read(root / "wizard-owner.json", proofs) != {"schema": 1, "owner": "GloomhavenVR.QuestWizard"}:
        raise _error("cleanup_unowned")
    proofs[str(root)] = {"identity": _stamp(root)[:3], "sha256": None}
    build = ordinary(root / "build")
    if build.exists() and not build.is_dir(): raise _error("cleanup_unowned")
    return root, build


def _helper():
    global _storage
    if _storage is None:
        path = Path(__file__).resolve().parent.parent / "quest-builder/storage.py"
        spec = importlib.util.spec_from_file_location("quest_cleanup_storage", path)
        module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
        _storage = module
    return _storage


@contextmanager
def _guard(root, build):
    with file_lock(root / "run.lock"):
        if build.is_dir():
            helper = _helper()
            with ExitStack() as stack:
                try: stack.enter_context(helper.output_lock(build))
                except OSError as error:
                    if error.errno != errno.ENOSPC: raise
                    # Handle only acquisition failures, never an ENOSPC thrown
                    # by cleanup after entry: a second yield would retry a body
                    # that may already have removed files.
                    guard = build / ".builder.guard"
                    if not guard.is_file() or _stamp(guard)[3] < 1: raise
                    stack.enter_context(file_lock(guard))
                    lock = build / ".builder.lock"
                    if lock.exists():
                        _stamp(lock); raw = lock.read_text(encoding="utf-8").strip()
                        if raw.isdecimal():
                            try: os.kill(int(raw), 0)
                            except ProcessLookupError: pass
                            except (OSError, PermissionError) as exc: raise _error("cleanup_busy") from exc
                            else: raise _error("cleanup_busy")
                        else:
                            try: value = json.loads(raw)
                            except ValueError as exc: raise _error("cleanup_busy") from exc
                            if not isinstance(value, dict) or value.get("schema") != 1 or value.get("lock") != "kernel-guard":
                                raise _error("cleanup_busy")
                except helper.BuildError as error:
                    raise WizardError("cleanup_busy", "Stop the current builder before cleanup.",
                                      "Den laufenden Builder vor der Bereinigung beenden.") from error
                yield
        else: yield


def _relative(name):
    if not isinstance(name, str) or not name or "\\" in name or "\0" in name:
        raise _error("cleanup_path")
    path = PurePosixPath(name)
    if path.is_absolute() or PureWindowsPath(name).drive or ".." in path.parts or path.as_posix() != name:
        raise _error("cleanup_path")
    return path


def _rows(rows, size_name="size"):
    if not isinstance(rows, list) or not rows: raise _error("cleanup_receipt")
    result = {}
    for row in rows:
        if not isinstance(row, dict): raise _error("cleanup_receipt")
        name = row.get("path"); _relative(name)
        size, hashed = row.get(size_name), row.get("sha256")
        if name in result or type(size) is not int or size < 0 or not isinstance(hashed, str) or not HEX.fullmatch(hashed):
            raise _error("cleanup_receipt")
        result[name] = {"path": name, "size": size, "sha256": hashed}
    return result


def _children(parent):
    if not parent.is_dir(): return []
    _stamp(parent)
    selected = []
    for item in parent.iterdir():
        if len(selected) >= MAX_IDENTITIES: raise _error("cleanup_receipt", "Too many retained identities.")
        if HEX.fullmatch(item.name) and item.is_dir(): selected.append(item)
    return selected


def _snapshot(folder, proofs):
    proofs[str(folder)] = {"identity": _stamp(folder)[:3], "sha256": None}
    value = _read(folder / ".snapshot.json", proofs)
    if value.get("schema") != 1 or set(value) != {"schema", "files"}: raise _error("cleanup_receipt")
    rows = _rows(value["files"])
    if value_hash({"files": value["files"]}) != folder.name: raise _error("cleanup_receipt")
    for name, row in rows.items():
        stamp = _stamp(folder / name)
        if not stat.S_ISREG(stamp[2]) or stamp[3] != row["size"]: raise _error("cleanup_receipt", name)
    return value, rows


def _manifests(build, proofs):
    result = []
    for path in _children_files(build / "manifests"):
        try:
            value = _read(path, proofs)
            if (value.get("schema") != 1 or value.get("inputKey") != path.stem
                    or value_hash({key: item for key, item in value.items() if key != "inputKey"}) != path.stem):
                continue
            for name in ("game", "mod"):
                member = value[name]; _rows(member["files"])
                if member["key"] != value_hash({"files": member["files"]}): raise _error("cleanup_receipt")
            result.append(value)
        except (WizardError, OSError, KeyError, TypeError): continue
    return result


def _children_files(parent):
    if not parent.is_dir(): return []
    _stamp(parent); result = []
    for path in parent.iterdir():
        if len(result) >= MAX_IDENTITIES: raise _error("cleanup_receipt", "Too many retained identities.")
        if HEX.fullmatch(path.stem) and path.suffix == ".json": result.append(path)
    return result


def _duplicates(build, proofs):
    targets = []; duplicates = {}; manifests = _manifests(build, proofs)
    for folder in _children(build / "inputs/game"):
        try:
            value, rows = _snapshot(folder, proofs)
            ordered = sorted(value["files"], key=lambda row: row["path"])
            correct_key = value_hash({"files": ordered})
            if correct_key == folder.name: continue
            correct = folder.with_name(correct_key)
            correct_value, correct_rows = _snapshot(correct, proofs)
            if correct_value["files"] != ordered or correct_rows != rows: continue
            if not any(item["game"]["key"] == folder.name and item["game"]["files"] == value["files"] for item in manifests):
                continue
            targets.append(folder); duplicates[folder.name] = (folder, rows)
        except (WizardError, OSError, KeyError, TypeError): continue
    for folder in _children(build / "cache/recovery"):
        try:
            owner = _read(folder / "stage-owner.json", proofs)
            if owner == {"schema": 1, "owner": "Quest recovered Campaign stage", "key": folder.name, "gameKey": owner.get("gameKey")} and owner.get("gameKey") in duplicates:
                targets.append(folder)
        except (WizardError, OSError): continue
    for folder in _children(build / "cache/full-original-recovery"):
        try:
            original = _read(folder / "original-source.json", proofs)
            if original.get("schema") != 1 or original.get("sourceFingerprint") != hashlib.sha256(
                    json.dumps(original["sourceInventory"], sort_keys=True, separators=(",", ":")).encode()).hexdigest(): continue
            rows = _rows(original["sourceInventory"], "bytes")
            for snapshot, expected in duplicates.values():
                receipt = snapshot / ".snapshot.json"
                expected = {**expected, ".snapshot.json": {"path": ".snapshot.json", "size": receipt.stat().st_size,
                            "sha256": proofs[str(receipt)]["sha256"]}}
                if rows == expected: targets.append(folder); break
        except (WizardError, OSError, KeyError, TypeError): continue
    return targets


def _completed_build(build, proofs):
    latest = _read(build / "latest-build.json", proofs)
    receipt_path, apk_name = latest.get("receipt"), latest.get("apk")
    if (not isinstance(receipt_path, str) or not re.fullmatch(r"receipts/build/[0-9a-f]{64}\.json", receipt_path)
            or not isinstance(apk_name, str) or not re.fullmatch(r"(?:builds|updates)/[0-9a-f]{64}/[^/]+\.apk", apk_name)):
        raise _error("cleanup_no_completed_build")
    receipt = _read(build / receipt_path, proofs)
    if (receipt.get("schema") != 1 or receipt.get("stage") != "build" or receipt.get("key") != Path(receipt_path).stem
            or latest.get("details") != receipt.get("details") or receipt["details"].get("isDiagnostic") is not False):
        raise _error("cleanup_no_completed_build")
    outputs = _rows(receipt.get("outputs"))
    if apk_name not in outputs or latest["details"].get("apkSha256") != outputs[apk_name]["sha256"]:
        raise _error("cleanup_no_completed_build")
    if latest["details"].get("contentFiles") == [] and latest["details"].get("retainedGameContent") is True:
        # An APK-only update closes its own signed artifact and retains the
        # base game's bank. All delivered builds/banks stay outside cleanup;
        # this small successful report proves this is the real update producer,
        # rather than treating any missing content list as a successful port.
        report_name = apk_name + ".build.json"
        if report_name not in outputs: raise _error("cleanup_no_completed_build")
        report = _read(build / report_name, proofs)
        if (report.get("schema") != 1 or report.get("buildResult") != "Succeeded"
                or report.get("scope") != "APK update" or report.get("retainedGameContent") is not True
                or report.get("inputKey") != latest["details"].get("inputKey")
                or not isinstance(report.get("inputKey"), str) or not HEX.fullmatch(report["inputKey"])):
            raise _error("cleanup_no_completed_build")
        content = {}
    else: content = _rows(latest["details"].get("contentFiles"))
    if any(outputs.get(name) != row for name, row in content.items()): raise _error("cleanup_no_completed_build")
    for name, row in outputs.items():
        if _relative(name).parts[0] not in ("builds", "updates"): raise _error("cleanup_no_completed_build")
        stamp = _stamp(build / name)
        if not stat.S_ISREG(stamp[2]) or stamp[3] != row["size"] or row["size"] <= 0:
            raise _error("cleanup_no_completed_build")
        proofs[str(build / name)] = {"stamp": stamp, "sha256": None}


def _notify(progress, phase, done, total, detail):
    if progress: progress({"phase": phase, "done": done, "total": total, "detail": detail})


def _scan(folder, progress=None, phase="cleanup-scan"):
    nodes = []; total = count = 0
    for ancestor in (folder, *folder.parents):
        pin = ancestor / ".keep"
        if pin.exists() or pin.is_symlink(): raise _error("cleanup_pinned", ".keep")
    def visit(path):
        nonlocal total, count
        if path.name == ".keep": raise _error("cleanup_pinned", path.name)
        stamp = _stamp(path); nodes.append((path, stamp))
        if stat.S_ISDIR(stamp[2]):
            for child in sorted(path.iterdir(), key=lambda item: item.name): visit(child)
            if _stamp(path) != stamp: raise _error("cleanup_changed", path.name)
        else:
            if path.suffix.lower() in (".keystore", ".jks", ".p12", ".pem", ".key"):
                raise _error("cleanup_signing_path", path.name)
            total += stamp[3]; count += 1
            if count == 1 or count % 256 == 0: _notify(progress, phase, count, None, path.name)
    visit(folder)
    fingerprint = value_hash([{"path": path.relative_to(folder).as_posix(), "stamp": stamp} for path, stamp in nodes])
    return {"bytes": total, "files": count, "fingerprint": fingerprint, "nodes": nodes}


def _retain(value, root, proofs, private, **extra):
    with _plans_lock:
        now = time.monotonic()
        for identity in list(_plans):
            if now - _plans[identity]["created"] > PREVIEW_SECONDS or _plans[identity]["root"] == root:
                _plans.pop(identity)
        _plans[value["id"]] = {"created": now, "root": root, "proofs": copy.deepcopy(proofs),
                                "trees": private, "public": copy.deepcopy(value), **extra}
    return value


def _session(folder, proofs):
    value = _read(folder / "state.json", proofs)
    if (value.get("schema") != 1 or value.get("session") != folder.name
            or not isinstance(value.get("stages"), list) or not isinstance(value.get("choices"), dict)
            or [row.get("id") if isinstance(row, dict) else None for row in value["stages"]] != list(STAGES)):
        raise _error("cleanup_session")
    return value


def _key(value):
    return isinstance(value, str) and HEX.fullmatch(value) is not None


def _same_identity(captured, manifest_profile):
    global _profile
    if not isinstance(captured, dict) or not isinstance(manifest_profile, dict): return False
    if _profile is None:
        path = Path(__file__).resolve().parent.parent / "quest-builder/profile.py"
        spec = importlib.util.spec_from_file_location("quest_cleanup_profile", path)
        _profile = importlib.util.module_from_spec(spec); spec.loader.exec_module(_profile)
    fields = ("schema", "provider", "providerId", "steamId", "accountId", "displayName")
    # load_profile revalidates the Wizard's identity: Steam capture's source
    # annotation becomes explicit-local-profile, and logo/DLC are added later.
    # Use that actual identity normalizer, never compare provenance annotations
    # or silently drop differences in the full provider/account identity.
    if captured.get("isDummy") is True or manifest_profile.get("isDummy") is True:
        return (captured.get("isDummy") is True and manifest_profile.get("isDummy") is True
                and all(captured.get(name) == manifest_profile.get(name) for name in fields))
    try:
        return _profile.validate_identity({name: captured[name] for name in fields if name in captured}) == _profile.validate_identity(
            {name: manifest_profile[name] for name in fields if name in manifest_profile})
    except (ValueError, TypeError, KeyError): return False


def _session_scope(root, build, folder, value, manifests, proofs):
    """Read actual stage owners, including the full-update fallback's inputs.

    Full migration publishes a new manifest but inspect retains the old APK's
    inputKey. Bind that fallback only to the captured exact source inventory,
    profile fields and the game inventory observed by capacity qualification.
    Paths in progress text and the mutable global latest-input pointer are not
    per-run owners and must never authorize cleanup.
    """
    inputs = set(); games = set(); sources = set(); references = set()
    def observe(item):
        if isinstance(item, dict):
            for name, member in item.items():
                if name in ("inputKey", "gameInputKey") and _key(member): inputs.add(member)
                if name in ("gameKey", "cacheGameKey") and _key(member): games.add(member)
                if name == "sourceRoot" and isinstance(member, str): sources.add(member)
                if name in ("path", "sourcePackage", "apk", "bank", "profilePath") and isinstance(member, str):
                    references.add(member)
                observe(member)
        elif isinstance(item, list):
            for member in item: observe(member)
    observe(value.get("completed", {})); observe(value.get("qualification", {}))
    for row in value["stages"]:
        observe(row.get("details", {}))
        receipt = folder / "receipts" / (str(row.get("id")) + ".json")
        if receipt.is_file():
            captured = _read(receipt, proofs)
            if (captured.get("schema") != 1 or captured.get("stage") != row.get("id")
                    or not _key(captured.get("key"))): raise _error("cleanup_receipt")
            observe(captured)
            for output in captured.get("outputs", []):
                name = output.get("path") if isinstance(output, dict) else None
                if isinstance(name, str) and re.fullmatch(r"build/manifests/[0-9a-f]{64}\.json", name):
                    inputs.add(Path(name).stem)
                if isinstance(name, str) and re.fullmatch(r"build/updates/[0-9a-f]{64}/plan\.json", name):
                    plan = _read(root / name, proofs)
                    if plan.get("schema") != 1 or plan.get("updateKey") != Path(name).parent.name:
                        raise _error("cleanup_receipt")
                    observe(plan)
    source = next((row.get("details", {}) for row in value["stages"] if row.get("id") == "source"), {})
    source_hash = source.get("sourceHash")
    profile_path = folder / "profile.json"
    profile = _read(profile_path, proofs) if profile_path.is_file() else None
    selected = []
    for manifest in manifests:
        fallback = (value["choices"].get("mode") == "update-mod" and _key(source_hash)
                    and value_hash(manifest["mod"]["files"]) == source_hash
                    and _same_identity(profile, manifest.get("profile"))
                    and manifest["game"]["key"] in games)
        if manifest["inputKey"] in inputs or fallback:
            selected.append(manifest); inputs.add(manifest["inputKey"]); games.add(manifest["game"]["key"])
    return {"inputs": inputs, "games": games, "sources": sources, "references": references, "manifests": selected}


def _project_key(manifest):
    # Exact stable ownership contract in import_workspace.workspace_key. No
    # editor, recovery recipe normalization or game payload is needed here.
    if manifest.get("target") == "game" and manifest["game"].get("unityVersion") == "2021.3.5f1":
        return value_hash({"schema": 1, "purpose": "Quest owned original-asset workspace",
                           "gameKey": manifest["game"]["key"], "unityVersion": "2021.3.5f1",
                           "scope": "game", "import": {"schema": 1, "target": "Android",
                           "graphicsApi": "Vulkan", "colorSpace": "Linear"}})
    if type(manifest.get("recipe")) is int:
        return value_hash({"input": manifest["inputKey"], "recipe": manifest["recipe"]})
    return None


def _editor_busy(project):
    global _access
    if _access is None:
        # Load the standard-library guard with its exact storage dependency;
        # do not pollute global loader aliases used by another Wizard thread.
        path = Path(__file__).resolve().parent.parent / "quest-builder/project_access.py"
        import builtins
        module = type(_helper())("quest_cleanup_project_access")
        def local_import(name, globals=None, locals=None, fromlist=(), level=0):
            if name == "storage" and level == 0: return _helper()
            return builtins.__import__(name, globals, locals, fromlist, level)
        module.__dict__["__builtins__"] = {**vars(builtins), "__import__": local_import}
        exec(compile(path.read_text(encoding="utf-8"), str(path), "exec"), module.__dict__)
        _access = module
    return _access.editor_busy(project)


def _references(root, build, values):
    result = set()
    for value in values:
        try:
            path = Path(value)
            if not path.is_absolute():
                relative = _relative(value)
                path = root / relative if relative.parts[0] in ("build", "source", "sessions", "exports") else build / relative
            path = ordinary(path)
            if path == root or root in path.parents: result.add(path)
        except (WizardError, OSError, ValueError): continue
    return result


def _delivered_references(root, build, proofs):
    values = []
    def visit(item):
        if isinstance(item, dict):
            for name, value in item.items():
                if name in ("path", "apk", "sourcePackage", "bank") and isinstance(value, str): values.append(value)
                visit(value)
        elif isinstance(item, list):
            for value in item: visit(value)
    for name in ("latest-build.json", "latest-update.json"):
        if (build / name).is_file(): visit(_read(build / name, proofs))
    for path in _children_files(build / "receipts/build"):
        value = _read(path, proofs)
        if value.get("schema") != 1 or value.get("stage") != "build" or value.get("key") != path.stem:
            raise _error("cleanup_receipt")
        visit(value)
    return _references(root, build, values)


def _source_targets(root, folder, session, proofs, skipped):
    targets = []
    parent = root / "source"
    if not parent.is_dir(): return targets
    _stamp(parent)
    for candidate in parent.iterdir():
        if not candidate.is_dir(): continue
        owned = False; matched = candidate.name == session or re.fullmatch(re.escape(session) + r"-[0-9a-f]{16}", candidate.name)
        if not matched: continue
        try:
            if candidate.name == session:
                owner = _read(folder / "source-attempt.json", proofs)
                if owner.get("schema") == 1 and owner.get("session") == session and isinstance(owner.get("origin"), str):
                    targets.append(candidate); owned = True
            else:
                owner_path = candidate.with_name(candidate.name + ".owner.json")
                owner = _read(owner_path, proofs)
                if (owner.get("schema") == 1 and owner.get("session") == session and _key(owner.get("releaseHash"))
                        and candidate.name.endswith(owner["releaseHash"][:16])
                        and re.fullmatch(r"[0-9a-f]{40}", str(owner.get("commit", "")))):
                    targets.extend((candidate, owner_path)); owned = True
        except (WizardError, OSError, KeyError, TypeError): pass
        if not owned: skipped.append({"path": candidate.relative_to(root).as_posix(), "reason": "unproven_owner"})
    return targets


def _raw_owners(build, manifests, proofs):
    inventories = {}; result = {}
    for manifest in manifests:
        key = manifest["game"]["key"]
        if key in inventories: continue
        snapshot = build / "inputs/game" / key
        if not snapshot.is_dir(): continue
        try:
            _, rows = _snapshot(snapshot, proofs); receipt = snapshot / ".snapshot.json"
            inventories[key] = {**rows, ".snapshot.json": {"path": ".snapshot.json", "size": receipt.stat().st_size,
                                "sha256": proofs[str(receipt)]["sha256"]}}
        except (WizardError, OSError, KeyError, TypeError): continue
    for candidate in _children(build / "cache/full-original-recovery"):
        path = candidate / "original-source.json"
        if not path.is_file(): continue
        try:
            original = _read(path, proofs)
            if original.get("schema") != 1 or original.get("sourceFingerprint") != hashlib.sha256(
                    json.dumps(original.get("sourceInventory"), sort_keys=True, separators=(",", ":")).encode()).hexdigest(): continue
            rows = _rows(original.get("sourceInventory"), "bytes")
            for key, expected in inventories.items():
                if rows == expected: result[candidate] = key; break
        except (WizardError, OSError, KeyError, TypeError): continue
    return result


def _session_targets(root, build, folder, session, scope, protected, proofs, skipped, raw_owners):
    """Prove each subtree independently; no entire-cache or guessed-path sweep."""
    targets = []
    for manifest in scope["manifests"]:
        for kind in ("game", "mod", "probeAssets", "startupProject"):
            member = manifest.get(kind)
            if not isinstance(member, dict) or not _key(member.get("key")): continue
            area = {"game": "game", "mod": "mod", "probeAssets": "probe", "startupProject": "startup"}[kind]
            candidate = build / "inputs" / area / member["key"]
            if not candidate.is_dir(): continue
            captured, rows = _snapshot(candidate, proofs)
            if captured["files"] != member.get("files"): raise _error("cleanup_receipt")
            targets.append(candidate)
        key = _project_key(manifest)
        if key:
            project = build / "projects" / key
            marker = build / "cache/project-lifecycle" / key / "owner.json"
            if marker.is_file():
                owner = _read(marker, proofs)
                expected = {"schema": 2, "owner": "Quest generated project", "project": "projects/" + key, "workspaceKey": key}
                legacy = {"schema": 1, "owner": "Quest generated project", "project": "projects/" + key, "inputKey": manifest["inputKey"]}
                if owner not in (expected, legacy):
                    skipped.append({"path": project.relative_to(root).as_posix(), "reason": "unproven_owner"}); continue
                if project.is_dir():
                    if _editor_busy(project): raise _error("cleanup_editor_busy", key[:12])
                    targets.append(project)
                # These exact producer roots are all owned by this same project
                # lifecycle. Their large journals are stat-scanned, not decoded.
                for area in ("project-lifecycle", "prepare-resume", "project-content-transactions", "preparation-repair"):
                    candidate = build / "cache" / area / key
                    if candidate.is_dir(): targets.append(candidate)
            elif project.is_dir(): skipped.append({"path": project.relative_to(root).as_posix(), "reason": "unproven_owner"})
    for candidate in _children(build / "cache/recovery"):
        owner = _read(candidate / "stage-owner.json", proofs) if (candidate / "stage-owner.json").is_file() else {}
        if (owner == {"schema": 1, "owner": "Quest recovered Campaign stage", "key": candidate.name, "gameKey": owner.get("gameKey")}
                and owner.get("gameKey") in scope["games"]): targets.append(candidate)
    for candidate, game_key in raw_owners.items():
        if game_key in scope["games"]: targets.append(candidate)
    targets.extend(_source_targets(root, folder, session, proofs, skipped))
    unique = []
    for target in sorted(set(targets), key=lambda path: (len(path.parts), str(path))):
        if any(parent == target or parent in target.parents for parent in unique): continue
        owner_source = target.with_name(target.name[:-len(".owner.json")]) if target.name.endswith(".owner.json") and target.parent == root / "source" else target
        if any(target == path or target in path.parents or path in target.parents
               or owner_source == path or owner_source in path.parents for path in protected):
            skipped.append({"path": target.relative_to(root).as_posix(), "reason": "shared_or_delivered"}); continue
        unique.append(target)
    return unique


def _tree_projects(tree, build):
    projects = {tree["root"]} if tree["root"].parent == build / "projects" else set()
    for path, stamp in tree["nodes"]:
        if stat.S_ISREG(stamp[2]) and path.name == "ProjectVersion.txt" and path.parent.name == "ProjectSettings":
            projects.add(path.parent.parent)
    return projects


def plan_session(state_root, session, *, remove_data=False, progress=None):
    """Retire one stopped run, optionally removing only its exclusive data.

    Capture130723 contains different game inventories, not canonical duplicate
    snapshots. Ordinary duplicate cleanup correctly finds nothing in that case.
    Removing a run is a separate explicit choice: move its small diagnostics out
    of the resumable session directory; delete proven exclusive data only after
    the retained preview is accepted. Completed APKs/banks/keys are never targets.
    """
    if not isinstance(session, str) or SESSION.fullmatch(session) is None or type(remove_data) is not bool:
        raise _error("cleanup_session")
    proofs = {}; root, build = _root(state_root, proofs)
    with _guard(root, build):
        parent = ordinary(root / "sessions"); folder = ordinary(parent / session)
        value = _session(folder, proofs)
        if value.get("status") not in ("ready", "failed", "blocked", "cancelled", "interrupted"):
            raise WizardError("cleanup_session_active", "Only a stopped, unfinished run can be removed.",
                              "Nur ein angehaltener, nicht abgeschlossener Lauf kann entfernt werden.")
        proofs[str(parent)] = {"stamp": _stamp(parent), "sha256": None}
        retired = ordinary(root / "logs/removed-runs" / session)
        if retired.exists(): raise _error("cleanup_session_archive")
        session_scan = _scan(folder, progress, "cleanup-session-scan")
        latest = root / "latest-session.json"
        pointer = _read(latest, proofs) if latest.is_file() else None
        survivors = []; skipped = []; private = []; paths = []
        uncertainty = False
        try:
            manifests = _manifests(build, proofs) if remove_data else []
            scope = _session_scope(root, build, folder, value, manifests, proofs) if remove_data else None
            protected = _delivered_references(root, build, proofs) if remove_data else set()
            raw_owners = _raw_owners(build, manifests, proofs) if remove_data else {}
        except (WizardError, OSError, ValueError, TypeError, KeyError):
            manifests = []; scope = None; protected = set(); raw_owners = {}; uncertainty = True
            skipped.append({"path": "build", "reason": "unreadable_ownership"})
        for other in parent.iterdir():
            if other == folder or SESSION.fullmatch(other.name) is None: continue
            try:
                state = _session(other, proofs)
                updated = state.get("updated", state.get("created", 0))
                survivors.append((updated if type(updated) in (int, float) else 0, other.name))
                if remove_data and scope is not None:
                    used = _session_scope(root, build, other, state, manifests, proofs)
                    protected.update(_references(root, build, used["references"] | used["sources"]))
                    # Capacity qualification can bind a game before inspect
                    # publishes its input receipt. That run still owns access to
                    # the exact snapshot/import workspace it is about to reuse.
                    for game_key in used["games"]:
                        protected.add(build / "inputs/game" / game_key)
                        key = _project_key({"target": "game", "game": {"key": game_key, "unityVersion": "2021.3.5f1"}})
                        protected.add(build / "projects" / key)
                        for area in ("project-lifecycle", "prepare-resume", "project-content-transactions", "preparation-repair"):
                            protected.add(build / "cache" / area / key)
                    for manifest in used["manifests"]:
                        for kind, area in (("game", "game"), ("mod", "mod"), ("probeAssets", "probe"), ("startupProject", "startup")):
                            member = manifest.get(kind)
                            if isinstance(member, dict) and _key(member.get("key")):
                                protected.add(build / "inputs" / area / member["key"])
                        key = _project_key(manifest)
                        if key:
                            protected.add(build / "projects" / key)
                            for area in ("project-lifecycle", "prepare-resume", "project-content-transactions", "preparation-repair"):
                                protected.add(build / "cache" / area / key)
                    # Recovery owners bind to gameKey independently of changing
                    # mod manifests; every other run keeps that common raw stage.
                    for candidate in _children(build / "cache/recovery"):
                        path = candidate / "stage-owner.json"
                        if path.is_file() and _read(path, proofs).get("gameKey") in used["games"]: protected.add(candidate)
                    for candidate, game_key in raw_owners.items():
                        if game_key in used["games"]: protected.add(candidate)
            except (WizardError, OSError, ValueError, TypeError, KeyError):
                uncertainty = True
                skipped.append({"path": other.relative_to(root).as_posix(), "reason": "unreadable_dependency"})
        if remove_data and uncertainty:
            skipped.append({"path": "build", "reason": "dependency_proof_incomplete"})
        elif remove_data:
            try: targets = _session_targets(root, build, folder, session, scope, protected, proofs, skipped, raw_owners)
            except (WizardError, OSError, ValueError, TypeError, KeyError) as error:
                if getattr(error, "code", None) == "cleanup_editor_busy": raise
                targets = []; skipped.append({"path": "build", "reason": "unreadable_ownership"})
            retained_projects = set()
            retained_sources = set()
            for target in targets:
                if (target.parent.parent == build / "cache" and target.name in retained_projects
                        and target.parent.name in ("project-lifecycle", "prepare-resume", "project-content-transactions", "preparation-repair")):
                    skipped.append({"path": target.relative_to(root).as_posix(), "reason": "project_retained"}); continue
                if target.parent == root / "source" and target.name.endswith(".owner.json") and target.name[:-len(".owner.json")] in retained_sources:
                    skipped.append({"path": target.relative_to(root).as_posix(), "reason": "source_retained"}); continue
                try:
                    scan = _scan(target, progress)
                    if target.parent.parent == build / "inputs":
                        expected = set(_rows(_read(target / ".snapshot.json", proofs)["files"])) | {".snapshot.json"}
                        observed = {path.relative_to(target).as_posix() for path, stamp in scan["nodes"] if stat.S_ISREG(stamp[2])}
                        if expected != observed: raise _error("cleanup_surplus_files")
                except (WizardError, OSError) as error:
                    if target.parent == build / "projects": retained_projects.add(target.name)
                    if target.parent == root / "source": retained_sources.add(target.name)
                    skipped.append({"path": target.relative_to(root).as_posix(), "reason": getattr(error, "code", "unreadable")}); continue
                for project in _tree_projects({"root": target, **scan}, build):
                    if _editor_busy(project): raise _error("cleanup_editor_busy", project.name[:12])
                paths.append({"path": target.relative_to(root).as_posix(), "bytes": scan["bytes"], "files": scan["files"]})
                private.append({"root": target, **scan})
        preview = {"id": secrets.token_urlsafe(24), "mode": "session", "session": session,
                   "removeData": remove_data, "paths": paths, "bytes": sum(row["bytes"] for row in paths),
                   "files": sum(row["files"] for row in paths), "exact": True, "eligible": True,
                   "skipped": skipped, "sessionBytes": session_scan["bytes"], "sessionFiles": session_scan["files"],
                   "consequences": ["run-removed", "resume-lost", "diagnostics-retained"] +
                                   (["exclusive-run-data-removed"] if remove_data else ["build-data-retained"])}
        saved = {"folder": folder, "archive": retired, "scan": session_scan,
                 "pointer": pointer, "survivor": max(survivors)[1] if survivors else None,
                 "projects": set().union(*(_tree_projects(tree, build) for tree in private))}
        _notify(progress, "cleanup-ready", preview["files"], preview["files"], "session")
        return _retain(preview, root, proofs, private, session=saved)


def plan(state_root, mode="duplicates", progress=None):
    """Return a confirmation preview; unknown/unproven duplicate data is retained."""
    if mode not in ("duplicates", "build-cache"): raise _error("cleanup_mode")
    proofs = {}; root, build = _root(state_root, proofs)
    targets = []; skipped = []
    with _guard(root, build):
        if build.is_dir():
            if mode == "build-cache":
                try: _completed_build(build, proofs)
                except (OSError, KeyError, TypeError, WizardError) as error:
                    raise WizardError("cleanup_no_completed_build", "Keep the caches until a complete APK and content bank have been built successfully.",
                                      "Die Zwischendaten behalten, bis APK und Inhaltspaket erfolgreich erstellt wurden.") from error
                targets = [build / name for name in ("cache", "projects", "inputs") if (build / name).is_dir()]
            else: targets = _duplicates(build, proofs)
        paths = []; private = []
        for target in targets:
            try: scan = _scan(target, progress)
            except (WizardError, OSError) as error:
                skipped.append({"path": target.relative_to(root).as_posix(), "reason": getattr(error, "code", "unreadable")}); continue
            # Reject surplus files in duplicate snapshots: they are not part of
            # the proven immutable source owner, even if a valid receipt exists.
            if target.parent == build / "inputs/game":
                expected = set(_rows(_read(target / ".snapshot.json", proofs)["files"])) | {".snapshot.json"}
                observed = {path.relative_to(target).as_posix() for path, stamp in scan["nodes"] if stat.S_ISREG(stamp[2])}
                if expected != observed: skipped.append({"path": target.relative_to(root).as_posix(), "reason": "surplus_files"}); continue
            row = {"path": target.relative_to(root).as_posix(), "bytes": scan["bytes"], "files": scan["files"]}
            paths.append(row); private.append({"root": target, **scan})
        value = {"id": secrets.token_urlsafe(24), "mode": mode, "paths": paths,
                 "bytes": sum(row["bytes"] for row in paths), "files": sum(row["files"] for row in paths),
                 "exact": True, "eligible": bool(paths), "skipped": skipped,
                 "consequences": ["full-rebuild-cache-removed"] if mode == "build-cache" else ["canonical-resume-preserved"]}
        _notify(progress, "cleanup-ready", value["files"], value["files"], mode)
        return _retain(value, root, proofs, private)


def _invalidate(root):
    reset = []
    parent = root / "sessions"
    if not parent.is_dir(): return reset
    for folder in parent.iterdir():
        if not SESSION.fullmatch(folder.name) or not folder.is_dir(): continue
        try:
            proofs = {}; path = folder / "state.json"; value = _read(path, proofs)
            if value.get("schema") != 1 or value.get("session") != folder.name or not isinstance(value.get("stages"), list): continue
            for stage in ("inspect", "build"):
                receipt = ordinary(folder / "receipts" / (stage + ".json"))
                if receipt.exists(): _stamp(receipt); receipt.unlink()
            value["cacheCleaned"] = {"mode": "build-cache", "time": time.time()}
            if value.get("status") != "complete":
                affected = [item for item in value.get("events", []) if item.get("stage") in ("inspect", "build")]
                history = value.setdefault("cacheCleanupHistory", [])
                history.append({"time": time.time(), "mode": "build-cache", "events": affected})
                value["cacheCleanupHistory"] = history[-4:]
                # Historical events remain in their log/archive, but cannot
                # seed counters for work the user deliberately removed.
                value["events"] = [item for item in value.get("events", []) if item.get("stage") not in ("inspect", "build")]
                value["status"] = "ready"
                value["needsActions"] = [item for item in value.get("needsActions", []) if item.get("stage") not in (None, "inspect", "build")]
                for row in value["stages"]:
                    if row.get("id") not in ("inspect", "build"): continue
                    row.update(status="pending", details={}, progress=stage_progress())
                    for name in ("progressPlan", "progressKey", "progressWorkKey", "waiting", "timingState", "failure", "error", "key"):
                        row.pop(name, None)
                    value.get("completed", {}).pop(row["id"], None)
                value.pop("buildFailure", None)
                value.setdefault("progress", {}).update(completed=sum(row.get("status") == "complete" for row in value["stages"]), phase=None, percent=None)
            atomic_json(path, value); reset.append(folder.name)
        except (WizardError, OSError, ValueError, TypeError): continue
    return reset


def execute(state_root, preview, progress=None):
    """Execute only a retained, unchanged preview under both kernel locks."""
    identity = preview.get("id") if isinstance(preview, dict) else preview
    with _plans_lock: saved = _plans.pop(identity, None) if isinstance(identity, str) else None
    if saved is None or time.monotonic() - saved["created"] > PREVIEW_SECONDS: raise _error("cleanup_preview_expired")
    proofs = {}; root, build = _root(state_root, proofs)
    if root != saved["root"]: raise _error("cleanup_preview_changed")
    public = saved["public"]; deleted = freed = 0; reset = []
    with _guard(root, build):
        for name, proof in saved["proofs"].items():
            path = ordinary(name)
            current = _stamp(path)
            if (current[:3] != proof["identity"] if "identity" in proof else current != proof["stamp"]):
                raise _error("cleanup_preview_changed", path.name)
            if proof["sha256"] is not None and hashlib.sha256(path.read_bytes()).hexdigest() != proof["sha256"]:
                raise _error("cleanup_preview_changed", path.name)
        for tree in saved["trees"]:
            fresh = _scan(tree["root"], progress, "cleanup-check")
            if fresh["fingerprint"] != tree["fingerprint"]: raise _error("cleanup_preview_changed", tree["root"].name)
        session = saved.get("session")
        if session is not None:
            if session["archive"].exists(): raise _error("cleanup_session_archive")
            current = _scan(session["folder"], progress, "cleanup-session-check")
            if current["fingerprint"] != session["scan"]["fingerprint"]: raise _error("cleanup_preview_changed")
            for project in session["projects"]:
                if _editor_busy(project): raise _error("cleanup_editor_busy", project.name[:12])
        for tree in saved["trees"]:
            for path, stamp in reversed(tree["nodes"]):
                current = _stamp(path)
                if stat.S_ISDIR(stamp[2]):
                    if current[:3] != stamp[:3]: raise _error("cleanup_preview_changed", path.name)
                    path.rmdir()
                else:
                    if current != stamp: raise _error("cleanup_preview_changed", path.name)
                    path.unlink(); freed += stamp[3]; deleted += 1
                    if deleted == 1 or deleted % 256 == 0: _notify(progress, "cleanup-delete", deleted, public["files"], path.name)
        if public["mode"] == "build-cache": reset = _invalidate(root)
        if session is not None:
            session["archive"].parent.mkdir(parents=True, exist_ok=True)
            ordinary(session["archive"].parent)
            os.replace(session["folder"], session["archive"])
            if session["pointer"] and session["pointer"].get("session") == public["session"]:
                if session["survivor"]:
                    atomic_json(root / "latest-session.json", {"schema": 1, "session": session["survivor"]})
                else: (root / "latest-session.json").unlink()
        _notify(progress, "cleanup-complete", deleted, public["files"], public["mode"])
    return {"freedBytes": freed, "deletedFiles": deleted,
            "invalidatedStages": ["inspect", "build"] if public["mode"] == "build-cache" and deleted else [], "sessionsReset": reset,
            "sessionsRemoved": [public["session"]] if public["mode"] == "session" else []}
