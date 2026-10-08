"""Preview and explicitly reclaim owned duplicate exports or completed build caches.

Only metadata and small ownership receipts are read. APK/content bytes are never
rehashed to offer cleanup, and nothing outside the wizard-owned build root is
eligible. A preview is bound in memory to its exact tree identities; restarting
the wizard requires another preview rather than accepting paths from a client.
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

from state import WizardError, atomic_json, canonical, file_lock, ordinary, stage_progress, value_hash

HEX = re.compile(r"[0-9a-f]{64}\Z")
SESSION = re.compile(r"[0-9a-f]{32}\Z")
MAX_JSON_BYTES = 16 * 1048576
MAX_IDENTITIES = 256
PREVIEW_SECONDS = 1800
_plans = {}
_plans_lock = threading.RLock()
_storage = None


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
    def visit(path):
        nonlocal total, count
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
        with _plans_lock:
            now = time.monotonic()
            for identity in list(_plans):
                if now - _plans[identity]["created"] > PREVIEW_SECONDS or _plans[identity]["root"] == root:
                    _plans.pop(identity)
            _plans[value["id"]] = {"created": now, "root": root, "proofs": copy.deepcopy(proofs), "trees": private, "public": copy.deepcopy(value)}
        _notify(progress, "cleanup-ready", value["files"], value["files"], mode)
        return value


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
        _notify(progress, "cleanup-complete", deleted, public["files"], public["mode"])
    return {"freedBytes": freed, "deletedFiles": deleted,
            "invalidatedStages": ["inspect", "build"] if public["mode"] == "build-cache" and deleted else [], "sessionsReset": reset}
