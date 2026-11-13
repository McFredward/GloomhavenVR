"""Reclaim only obsolete producer-owned files without sacrificing fast updates.

The maintainer requested one safe button, not a hidden full cache reset. Use the
existing immutable duplicate/update publishers and the portable export's exact
abandoned-attempt record. Never guess from a filename or sweep Unity, compiler,
asset or input trees looking for age-based garbage. Incomplete ownership keeps
data; explicit cache/run removal remains a separate, confirmed operation.
"""
from __future__ import annotations

import json
from pathlib import Path
import re
import sqlite3
from contextlib import closing

from state import Cancelled, WizardError
from update_storage import FAST_AREAS, candidates as update_candidates


def _overlaps(path, other):
    return path == other or path in other.parents or other in path.parents


def _retained_roots(root, build):
    # A compact update also owns native Players/package API inputs under its
    # individual updateKey. Only the exact obsolete *final APK* is eligible in
    # that tree; do not make the whole updates root a sweep candidate.
    return {root / name for name in ("sessions", "tools", "device", "logs", "captures")} | {
        build / name for name in (*FAST_AREAS, "tool-cache", "signing", "identities",
                                  "imports", "receipts", "manifests")}


def _dependencies(helper, root, build, proofs, *, cancel=None):
    protected = _retained_roots(root, build)
    uncertain = False
    try: manifests = helper._manifests(build, proofs, cancel=cancel)
    except Cancelled: raise
    except (WizardError, OSError, ValueError, TypeError, KeyError):
        manifests = []; uncertain = True
    indexed = {value["inputKey"]: value for value in manifests}
    current_inputs = set(); current_updates = set(); completed = []

    def observe(value):
        """Keep native publisher paths and the exact manifested game owner."""
        nonlocal uncertain
        helper._check_cancel(cancel)
        if isinstance(value, dict):
            for name, member in value.items():
                if name in ("path", "apk", "bank", "sourcePackage", "baseApk", "sourceRoot", "sourceRepo") and isinstance(member, str):
                    protected.update(helper._references(root, build, [member], cancel=cancel))
                if name in ("inputKey", "gameInputKey") and helper._key(member):
                    current_inputs.add(member)
                    manifest = indexed.get(member)
                    if manifest is not None:
                        protected.add(build / "inputs/game" / manifest["game"]["key"])
                    elif name == "inputKey":
                        # The original manifest can be absent after a compact
                        # package import. Its current source owner is unknown.
                        uncertain = True
                if name in ("gameKey", "cacheGameKey") and helper._key(member):
                    protected.add(build / "inputs/game" / member)
                if name == "updateKey" and helper._key(member): current_updates.add(member)
                observe(member)
        elif isinstance(value, list):
            for member in value: observe(member)

    for name in ("latest-build.json", "latest-update.json", "latest-input.json"):
        path = build / name
        if not path.exists(): continue
        try:
            value = helper._read(path, proofs, cancel=cancel)
            observe(value)
            if name == "latest-input.json":
                manifest_name = value.get("manifest")
                if (not isinstance(manifest_name, str)
                        or re.fullmatch(r"manifests/[0-9a-f]{64}\.json", manifest_name) is None
                        or Path(manifest_name).stem not in indexed):
                    uncertain = True
                else:
                    current_inputs.add(Path(manifest_name).stem)
                    protected.add(build / "inputs/game" / indexed[Path(manifest_name).stem]["game"]["key"])
        except Cancelled: raise
        except (WizardError, OSError, ValueError, TypeError, KeyError): uncertain = True

    latest_inputs = set(current_inputs); latest_updates = set(current_updates)
    for parent in (root / "sessions", root / "logs/removed-runs"):
        if not parent.is_dir(): continue
        try:
            proofs[str(parent)] = {"stamp": helper._stamp(parent), "sha256": None}
            sessions = [folder for folder in parent.iterdir() if helper.SESSION.fullmatch(folder.name)]
        except Cancelled: raise
        except (WizardError, OSError, ValueError, TypeError, KeyError):
            uncertain = True; continue
        if len(sessions) > helper.MAX_IDENTITIES: uncertain = True
        else:
            for folder in sessions:
                helper._check_cancel(cancel)
                try:
                    value = helper._session(folder, proofs)
                    scope = helper._session_scope(root, build, folder, value, manifests, proofs, cancel=cancel)
                    retired = parent.name == "removed-runs"
                    is_current = bool(scope["updates"] & latest_updates or (
                        not latest_updates and scope["inputs"] & latest_inputs))
                    if retired or value.get("status") == "complete":
                        completed.append((folder, value, scope))
                        if not is_current: continue
                    observe(value.get("choices", {}))
                    protected.update(helper._references(root, build, scope["references"] | scope["sources"], cancel=cancel))
                    current_inputs.update(scope["inputs"])
                    for key in scope["games"]: protected.add(build / "inputs/game" / key)
                    if not scope["games"]: uncertain = True
                except Cancelled: raise
                except (WizardError, OSError, ValueError, TypeError, KeyError): uncertain = True

    if not latest_inputs and not latest_updates and completed: uncertain = True
    for game_key in {path.name for path in protected if path.parent == build / "inputs/game"}:
        key = helper._project_key({"target": "game", "game": {"key": game_key, "unityVersion": "2021.3.5f1"}})
        protected.add(build / "projects" / key)
        for area in ("project-lifecycle", "prepare-resume", "project-content-transactions", "preparation-repair"):
            protected.add(build / "cache" / area / key)
    for key in current_inputs:
        manifest = indexed.get(key)
        if manifest is None: continue
        for kind, area in (("game", "game"), ("mod", "mod"), ("probeAssets", "probe"), ("startupProject", "startup")):
            member = manifest.get(kind)
            if isinstance(member, dict) and helper._key(member.get("key")):
                protected.add(build / "inputs" / area / member["key"])
        key = helper._project_key(manifest)
        if key:
            protected.add(build / "projects" / key)
            for area in ("project-lifecycle", "prepare-resume", "project-content-transactions", "preparation-repair"):
                protected.add(build / "cache" / area / key)
    try: raw_owners = helper._raw_owners(build, manifests, proofs, verify_files=False, cancel=cancel)
    except Cancelled: raise
    except (WizardError, OSError, ValueError, TypeError, KeyError):
        raw_owners = {}; uncertain = True
    if uncertain:
        # A newly started/stopped run may not have published inspect yet. Its
        # old import/recovery work must remain resumable even in that interval.
        protected.add(root / "source")
        protected.update(build / name for name in ("inputs", "projects", "cache/recovery", "cache/full-original-recovery",
            "cache/project-lifecycle", "cache/prepare-resume", "cache/project-content-transactions", "cache/preparation-repair"))
    else:
        games = {path.name for path in protected if path.parent == build / "inputs/game"}
        try: recovery = helper._children(build / "cache/recovery", cancel=cancel)
        except Cancelled: raise
        except (WizardError, OSError, ValueError, TypeError, KeyError): recovery = []
        for folder in recovery:
            try:
                owner = helper._read(folder / "stage-owner.json", proofs, cancel=cancel)
                if owner.get("gameKey") in games: protected.add(folder)
            except Cancelled: raise
            except (WizardError, OSError, ValueError, TypeError, KeyError): continue
        for folder, key in raw_owners.items():
            if key in games: protected.add(folder)
    return protected, completed, raw_owners, uncertain


def _abandoned_exports(helper, root, build, proofs, *, cancel=None):
    """Read <=256 exact portable producer attempts; never glob partial files.

    Export has no partial-resume consumer: its next attempt retires precisely
    this recorded temporary before copying again. Workspace/build locks already
    exclude a live producer. A WAL or malformed/foreign record is left intact.
    """
    database = build / "cache/portable-witnesses.sqlite3"
    if not database.exists(): return []
    try:
        before = helper._stamp(database)
        if before[3] > helper.MAX_JSON_BYTES: return []
        if any(Path(str(database) + suffix).exists() for suffix in ("-wal", "-shm", "-journal")): return []
        with closing(sqlite3.connect(database.as_uri() + "?mode=ro&immutable=1", uri=True)) as connection:
            rows = connection.execute("SELECT destination,value FROM portable_exports LIMIT ?",
                                      (helper.MAX_IDENTITIES + 1,)).fetchall()
        if len(rows) > helper.MAX_IDENTITIES or helper._stamp(database) != before: return []
        result = []
        for destination, raw in rows:
            helper._check_cancel(cancel)
            if not isinstance(raw, str) or len(raw) > 65536: continue
            try:
                value = json.loads(raw)
                if (not isinstance(value, dict) or set(value) != {"schema", "destination", "temporary"}
                        or value["schema"] != 1 or value["destination"] != destination
                        or not isinstance(destination, str) or not isinstance(value["temporary"], str)): continue
                target = helper.ordinary(value["temporary"]); final = helper.ordinary(destination)
                prefix = final.name + ".partial-"
                if (target.parent != final.parent or root / "exports" not in target.parents
                        or not target.name.startswith(prefix)
                        or re.fullmatch(r"[0-9a-f]{32}", target.name[len(prefix):]) is None
                        or not target.is_file()): continue
                helper._stamp(target)
                result.append(target)
            except (WizardError, OSError, ValueError, TypeError, KeyError): continue
        proofs[str(database)] = {"stamp": before, "sha256": None}
        return result
    except (WizardError, OSError, ValueError, TypeError, sqlite3.DatabaseError): return []


def candidates(helper, root, build, proofs, *, cancel=None):
    protected, completed, raw_owners, uncertain = _dependencies(helper, root, build, proofs, cancel=cancel)
    skipped = ([{"path": "build", "reason": "current_dependency_unknown"}] if uncertain else [])
    targets = []
    for folder, value, scope in completed:
        try:
            targets.extend(helper._session_targets(root, build, folder, value["session"], scope,
                protected, proofs, skipped, raw_owners, cancel=cancel, skip_protected_metadata=True))
        except Cancelled: raise
        except (WizardError, OSError, ValueError, TypeError, KeyError):
            skipped.append({"path": folder.relative_to(root).as_posix(), "reason": "unreadable_ownership"})
    try:
        # Bind the remaining canonical payload metadata too. Its receipt alone
        # must never authorize deleting the only remaining snapshot if a file
        # disappears or changes between discovery and execution.
        targets.extend(helper._duplicates(build, proofs, bind_files=True,
                                          keep_paths=protected, cancel=cancel))
    except Cancelled: raise
    except (WizardError, OSError, ValueError, TypeError, KeyError):
        skipped.append({"path": "build/inputs", "reason": "unreadable_ownership"})
    try:
        old, keep, omitted = update_candidates(helper, root, build, proofs, cancel=cancel)
        targets.extend(old); protected.update(keep); skipped.extend(omitted)
    except Cancelled: raise
    except (WizardError, OSError, ValueError, TypeError, KeyError):
        skipped.append({"path": "build/updates", "reason": "unproven_update_output"})
    targets.extend(_abandoned_exports(helper, root, build, proofs, cancel=cancel))
    selected = []
    for target in sorted(set(targets), key=lambda path: (len(path.parts), str(path))):
        if any(_overlaps(target, kept) for kept in protected):
            skipped.append({"path": target.relative_to(root).as_posix(), "reason": "shared_or_delivered"})
        elif target.parent == root / "source":
            # A local Git copy can contain the maintainer's uncommitted source.
            # Keep it and any recorded dirty snapshot even after its run ends.
            source = target.with_name(target.name[:-len(".owner.json")]) if target.name.endswith(".owner.json") else target
            session = source.name[:32]
            folders = (root / "sessions" / session, root / "logs/removed-runs" / session)
            dirty = (source / ".git").exists()
            for folder in folders:
                receipt = folder / "source.json"
                if receipt.is_file():
                    try: dirty = dirty or helper._read(receipt, proofs, cancel=cancel).get("dirty") is not False
                    except Cancelled: raise
                    except (WizardError, OSError, ValueError, TypeError, KeyError): dirty = True
            if dirty: skipped.append({"path": target.relative_to(root).as_posix(), "reason": "source_edits_retained"})
            elif not any(parent == target or parent in target.parents for parent in selected): selected.append(target)
        elif not any(parent == target or parent in target.parents for parent in selected): selected.append(target)
    return selected, protected, skipped
