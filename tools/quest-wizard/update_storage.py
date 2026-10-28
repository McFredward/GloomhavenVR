"""Keep one published update APK without discarding its shared compiler cache.

Updates are atomic replacements: the selected APK must remain intact until its
successor is signed and published. Older Wizard-produced final APKs can then be
removed. Native Players, compiler projects, converted banks and source snapshots
are deliberately independent of that output history. No payload hashing, Unity
import, recursive cache sweep or external input deletion is needed here.
"""
from __future__ import annotations

from pathlib import Path
import hashlib
import json
import stat

from state import Cancelled, WizardError, atomic_json

FAST_AREAS = ("updates/code-projects", "updates/source-inputs", "updates/game-inputs",
              "cache/quest-figure-meshes", "cache/quest-native-audio", "cache/quest-movies",
              "cache/environment-meshes", "cache/environment-native", "cache/environment-geometry",
              "cache/update-art-delivery", "cache/update-seeds", "cache/device-update-seeds",
              "cache/menu-depth-resources", "cache/menu-depth-content", "cache/retained-mod-content")
MENU_CACHE_AREAS = ("menu-depth-resources", "menu-depth-content", "retained-mod-content")


def _overlaps(path, other):
    return path == other or path in other.parents or other in path.parents


def _fast_references(helper, root, build, value, result, *, cancel=None):
    """Observe explicit producer dependencies; absence keeps all new families.

    These paths only protect data. They never prove cache ownership or authorize
    deletion. A pending run without a closed dependency roster remains resumable.
    """
    found = False
    malformed = False
    def visit(item):
        nonlocal found, malformed
        helper._check_cancel(cancel)
        if isinstance(item, dict):
            if "fastUpdateInputs" in item:
                values = item["fastUpdateInputs"]
                if not isinstance(values, list) or len(values) > helper.MAX_IDENTITIES or not all(isinstance(path, str) for path in values):
                    malformed = True
                else:
                    found = True
                    references = helper._references(root, build, values, cancel=cancel)
                    for path in references:
                        matched = False
                        for area in MENU_CACHE_AREAS:
                            parent = build / "cache" / area
                            if path == parent:
                                result.add(parent); matched = True
                            elif parent in path.parents:
                                relative = path.relative_to(parent)
                                if helper._key(relative.parts[0]):
                                    result.add(parent / relative.parts[0]); matched = True
                        if not matched: malformed = True
                    if len(references) != len(set(values)):
                        malformed = True
            for member in item.values(): visit(member)
        elif isinstance(item, list):
            for member in item: visit(member)
    visit(value)
    if not found or malformed:
        result.update(build / "cache" / area for area in MENU_CACHE_AREAS)


def protected_paths(helper, root, build, proofs, *, cancel=None):
    """Read only published pointers and stopped/in-progress run dependencies."""
    result = set()
    helper._completed_build(build, proofs)
    latest = helper._read(build / "latest-build.json", proofs)
    result.add(build / latest["apk"])
    _fast_references(helper, root, build, latest, result, cancel=cancel)
    # Keep the original external content bank. A baseApk is an obsolete signed
    # predecessor, not a dependency once the successor has been published.
    def visit(value):
        if isinstance(value, dict):
            for key, member in value.items():
                if key in ("bank", "sourcePackage") and isinstance(member, str):
                    result.update(helper._references(root, build, [member], cancel=cancel))
                visit(member)
        elif isinstance(value, list):
            for member in value: visit(member)
    visit(latest)
    parent = root / "sessions"
    if parent.is_dir():
        helper._stamp(parent)
        # Bind the session set so a new pending selection invalidates a preview.
        proofs[str(parent)] = {"stamp": helper._stamp(parent), "sha256": None}
        count = 0
        for folder in sorted(parent.iterdir(), key=lambda value: value.name):
            helper._check_cancel(cancel)
            if not helper.SESSION.fullmatch(folder.name): continue
            count += 1
            if count > helper.MAX_IDENTITIES: raise helper._error("cleanup_receipt", "Too many retained runs.")
            value = helper._session(folder, proofs)
            if value.get("status") == "complete": continue
            _fast_references(helper, root, build, value, result, cancel=cancel)
            choices = value["choices"]
            selected = choices.get("baseApk")
            if isinstance(selected, str):
                result.update(helper._references(root, build, [selected], cancel=cancel))
            # A stopped installation can still require its already-built APK.
            for row in value["stages"]:
                details = row.get("details", {})
                if isinstance(details, dict):
                    result.update(helper._references(root, build,
                        [details[name] for name in ("apk", "bank", "sourcePackage", "baseApk")
                         if isinstance(details.get(name), str)], cancel=cancel))
    return result


def cache_candidates(helper, root, build, proofs, *, cancel=None):
    """Prune finite superseded payloads while preserving all ownership evidence.

    Current/pending keys come from their explicit publisher rosters. Legacy or
    unfinished dependency metadata protects the whole family. No age heuristic,
    payload hash or recursive cache-directory deletion establishes ownership.
    """
    protected = {build / "cache" / area for area in MENU_CACHE_AREAS}
    if not any(path.is_dir() for path in protected): return [], protected, []
    try:
        protected = protected_paths(helper, root, build, proofs, cancel=cancel)
    except Cancelled: raise
    except (WizardError, OSError, ValueError, KeyError, TypeError):
        return [], protected, [{"path": "build/cache", "reason": "unproven_menu_dependencies"}]
    targets, skipped = [], []
    for area in MENU_CACHE_AREAS:
        for folder in helper._children(build / "cache" / area, cancel=cancel):
            if any(_overlaps(folder, path) for path in protected): continue
            try:
                if not helper._key(folder.name): continue
                if area == "menu-depth-resources":
                    owner = helper._read(folder / "menu-depth-android.provenance.json", proofs, cancel=cancel)
                    recipe = owner.get("recipe")
                    row = owner.get("bundle")
                    if (owner.get("schema") != 1 or owner.get("producerKey") != folder.name
                            or not isinstance(recipe, dict) or helper.value_hash(recipe) != folder.name
                            or recipe.get("scope") != "quest-menu-depth-native-card-v1"
                            or recipe.get("target") != "Android" or recipe.get("graphicsApi") != "Vulkan"
                            or not isinstance(row, dict) or row.get("path") != "menu-depth-android.bundle"):
                        raise ValueError("unproven native menu resource owner")
                else:
                    stage = "menu-depth-content" if area == "menu-depth-content" else "retained-mod-content"
                    receipt = helper._read(build / "receipts" / stage / (folder.name + ".json"), proofs, cancel=cancel)
                    if receipt.get("schema") != 1 or receipt.get("stage") != stage or receipt.get("key") != folder.name:
                        raise ValueError("unproven mod archive stage")
                    outputs = helper._rows(receipt.get("outputs"))
                    archive_name = (folder / "quest-mod-content.zip").relative_to(build).as_posix()
                    row = outputs.get(archive_name)
                    if row is None: raise ValueError("missing exact mod archive output")
                    if area == "retained-mod-content":
                        owner_path = folder / "owner.json"
                        owner = helper._read(owner_path, proofs, cancel=cancel)
                        expected = {"path": "quest-mod-content.zip", "size": row["size"], "sha256": folder.name}
                        if (owner != {"schema": 1, "scope": "quest-retained-mod-content-v1",
                                "archiveSha256": folder.name, "files": [expected]}
                                or row["sha256"] != folder.name):
                            raise ValueError("unproven retained mod archive identity")
                        metadata = [owner_path]
                    else:
                        manifest_path = folder / "quest-mod-content.json"
                        manifest = helper._read(manifest_path, proofs, cancel=cancel)
                        bundles_path = folder / "quest-mod-bundles.json"
                        bundles = helper._read(bundles_path, proofs, cancel=cancel)
                        details = receipt.get("details", {})
                        if (details.get("scope") != "quest-loose-menu-story-v1"
                                or manifest.get("schema") != 1 or manifest.get("archive") != "quest-mod-content.zip"
                                or manifest.get("archiveSha256") != row["sha256"]
                                or not helper._key(manifest.get("inputKey"))
                                or details.get("inputKey") != manifest["inputKey"]
                                or not isinstance(bundles.get("menuDepthStory"), dict)
                                or bundles["menuDepthStory"].get("contentKey") != details.get("storyKey")):
                            raise ValueError("unproven loose Story output identity")
                        metadata = [manifest_path, bundles_path]
                    expected_names = {archive_name, *(path.relative_to(build).as_posix() for path in metadata)}
                    if set(outputs) != expected_names:
                        raise ValueError("surplus mod archive stage outputs")
                    for path in metadata:
                        record = outputs[path.relative_to(build).as_posix()]
                        if record["sha256"] != proofs[str(path)]["sha256"] or record["size"] != helper._stamp(path)[3]:
                            raise ValueError("changed mod archive ownership metadata")
                    row = {**row, "path": "quest-mod-content.zip"}
                records = helper._rows([row])
                payload = folder / row["path"]
                stamp = helper._stamp(payload)
                if (not stat.S_ISREG(stamp[2]) or stamp[3] <= 0
                        or stamp[3] != records[row["path"]]["size"]):
                    raise ValueError("changed owned menu payload metadata")
                targets.append(payload)
            except Cancelled: raise
            except (WizardError, OSError, ValueError, KeyError, TypeError, AttributeError):
                skipped.append({"path": folder.relative_to(root).as_posix(), "reason": "unproven_menu_cache"})
    return targets, protected, skipped


def candidates(helper, root, build, proofs, *, keep_paths=(), cancel=None):
    """Finite successful update receipts prove precisely which APK is obsolete.

    Unknown, incomplete or changed outputs stay intact. The small successful
    report/plan/receipts remain as diagnostics and native reuse provenance.
    """
    protected = protected_paths(helper, root, build, proofs, cancel=cancel)
    protected.update(helper._references(root, build, keep_paths, cancel=cancel))
    targets = []; skipped = []
    for receipt_path in helper._children_files(build / "receipts/build", cancel=cancel):
        helper._check_cancel(cancel)
        try:
            receipt = helper._read(receipt_path, proofs, cancel=cancel)
            key = receipt_path.stem
            if (receipt.get("schema") != 1 or receipt.get("stage") != "build"
                    or receipt.get("key") != key): continue
            details = receipt.get("details")
            if (not isinstance(details, dict) or details.get("updateKey") != key
                    or details.get("isDiagnostic") is not False
                    or details.get("retainedGameContent") is not True): continue
            folder = build / "updates" / key
            apk = folder / "GloomhavenVR-Quest.apk"
            if not apk.exists(): continue
            if any(_overlaps(apk, path) for path in protected): continue
            plan = helper._read(folder / "plan.json", proofs, cancel=cancel)
            # updateKey hashes every captured input except the selected source
            # path, matching the actual update producer's ownership contract.
            if (plan.get("schema") != 1 or plan.get("updateKey") != key
                    or plan.get("mode") not in ("update-mod", "update-profile")
                    or helper.value_hash({name: member for name, member in plan.items()
                        if name not in ("baseApk", "sourcePackage", "compilerGameRoot", "updateKey")}) != key):
                continue
            outputs = helper._rows(receipt.get("outputs"))
            name = apk.relative_to(build).as_posix()
            report_name = name + ".build.json"
            if set(outputs) != {name, report_name}: continue
            if outputs[name]["sha256"] != details.get("apkSha256"): continue
            report = helper._read(build / report_name, proofs, cancel=cancel)
            if (report.get("schema") != 1 or report.get("buildResult") != "Succeeded"
                    or report.get("scope") != "APK update"
                    or report.get("updateKey") != key or report.get("apkSha256") != details["apkSha256"]
                    or report.get("retainedGameContent") is not True): continue
            stamp = helper._stamp(apk)
            if not stat.S_ISREG(stamp[2]) or stamp[3] != outputs[name]["size"] or stamp[3] <= 0: continue
            targets.append(apk)
        except Cancelled: raise
        except (WizardError, OSError, ValueError, KeyError, TypeError):
            skipped.append({"path": receipt_path.relative_to(root).as_posix(), "reason": "unproven_update_output"})
    # Each device generation needs only one base APK, never its external game
    # bank. Keep the latest closed import per headset (also before a UI session
    # is created), plus every explicitly pending source. Prune only older exact
    # owned imports; never walk unknown directories or delete partial transfers.
    imports = {}
    for folder in helper._children(build / "device-inputs", cancel=cancel):
        try:
            record = helper._read(folder / "source.json", proofs, cancel=cancel)
            witness = record.get("witness")
            apk = folder / "base.apk"
            if (record.get("source") != "quest" or not isinstance(witness, dict)
                    or witness.get("package") != "dev.gloomhavenvr.quest"
                    or not isinstance(witness.get("deviceSerial"), str)
                    or hashlib.sha256(json.dumps(witness, sort_keys=True, separators=(",", ":")).encode()).hexdigest() != folder.name
                    or not apk.is_file()): continue
            stamp = helper._stamp(apk)
            if not stat.S_ISREG(stamp[2]) or stamp[3] != witness.get("remoteStat", {}).get("size"): continue
            imports.setdefault(witness["deviceSerial"], []).append((folder, apk, helper._stamp(folder / "source.json")[4]))
        except Cancelled: raise
        except (WizardError, OSError, ValueError, KeyError, TypeError):
            skipped.append({"path": folder.relative_to(root).as_posix(), "reason": "unproven_device_input"})
    for rows in imports.values():
        rows.sort(key=lambda row: (row[2], row[0].name), reverse=True)
        protected.add(rows[0][1])
        for _, apk, _ in rows[1:]:
            if not any(_overlaps(apk, path) for path in protected): targets.append(apk)
    return targets, protected, skipped


def impact(root, paths, *, mode, protected=()):
    """Explain loss of reusable data before a destructive preview is accepted."""
    root = Path(root)
    removed = []
    for value in paths:
        path = root / value["path"] if isinstance(value, dict) else Path(value)
        relative = path.relative_to(root).as_posix()
        # Snapshot/source retirement does not remove the compact compiler's
        # independently retained inputs. Explicit cache deletion does.
        if mode == "build-cache" or any(_overlaps(path, root / "build" / area) for area in FAST_AREAS):
            removed.append(relative)
    slower = bool(removed)
    messages = ({"en": "Reusable build data will be removed. A later mod update may need to prepare or compile affected components again. The current APK and original content bank remain available.",
                 "de": "Wiederverwendbare Build-Daten werden gelöscht. Eine spätere Mod-Aktualisierung muss betroffene Komponenten möglicherweise erneut vorbereiten oder kompilieren. Die aktuelle APK und das originale Inhaltspaket bleiben erhalten."}
                if slower else
                {"en": "Reusable update data, the current APK and original content bank are retained. Removing run history does not remove them.",
                 "de": "Daten für schnelle Updates, die aktuelle APK und das originale Inhaltspaket bleiben erhalten. Das Entfernen alter Läufe löscht diese Daten nicht."})
    kept = sorted({(path.relative_to(root).as_posix() if isinstance(path, Path) else str(path)) for path in protected})
    kept.extend("build/" + area for area in FAST_AREAS if (root / "build" / area).exists())
    result = {"schema": 1, "status": "slower" if slower else "preserved", "messages": messages,
              "removedPaths": sorted(removed), "protectedPaths": sorted(set(kept))}
    return {"fastUpdateImpact": result, "protectedPaths": result["protectedPaths"],
            "warnings": ([{"code": "fast_update_cache_removed", "messages": messages}] if slower else [])}


def maintain(state_root, *, progress=None, cancel=None, keep_paths=(), run_lock_held=False):
    """Prune superseded generated final APKs only after successful publication.

    The Engine already owns run.lock during stage publication. Its caller may
    pass run_lock_held=True; the build kernel lock is still acquired. Errors are
    bounded warnings so an optional disk reclamation never fails the update.
    """
    import cleanup as helper
    proofs = {}; root = None
    result = {"schema": 1, "prunedBytes": 0, "prunedFiles": 0, "skipped": [], "warnings": [],
              "retainedFastUpdatePaths": []}
    try:
        root, build = helper._root(state_root, proofs)
        result["retainedFastUpdatePaths"] = ["build/" + area for area in FAST_AREAS if (build / area).is_dir()]
        guard = helper._helper().output_lock(build) if run_lock_held else helper._guard(root, build)
        with guard:
            targets, _, skipped = candidates(helper, root, build, proofs, keep_paths=keep_paths, cancel=cancel)
            caches, _, cache_skipped = cache_candidates(helper, root, build, proofs, cancel=cancel)
            caches = [path for path in caches if not any(_overlaps(path, kept) for kept in
                      helper._references(root, build, keep_paths, cancel=cancel))]
            targets.extend(caches); skipped.extend(cache_skipped)
            result["skipped"] = skipped
            # One metadata pass under the kernel locks, not one pass per old
            # result. Actual leaf deletion retains its own native witness.
            for name, proof in proofs.items():
                current = helper._stamp(Path(name))
                if (current[:3] != proof["identity"] if "identity" in proof else current != proof["stamp"]):
                    raise helper._error("cleanup_preview_changed")
            from cleanup_io import delete_owned
            for target in targets:
                helper._check_cancel(cancel)
                try:
                    tracker = helper._ScanProgress(progress, "cleanup-update-history", cancel, root=root)
                    scan = helper._scan(target, progress, "cleanup-update-history", cancel=cancel, tracker=tracker)
                    delete_owned(target, scan["nodes"][0][1], helper._stamp, owned_root=root,
                                 cancel=cancel, progress=progress)
                    result["prunedBytes"] += scan["bytes"]; result["prunedFiles"] += 1
                except Cancelled: raise
                except (WizardError, OSError, ValueError) as error:
                    result["skipped"].append({"path": target.relative_to(root).as_posix(),
                        "reason": getattr(error, "code", "cleanup_io_error")})
    except Cancelled: raise
    except (RuntimeError, OSError, ValueError, KeyError, TypeError) as error:
        result["warnings"].append({"code": getattr(error, "code", "update_history_retained"),
            "messages": {"en": "The update is ready; older update files were retained because their ownership could not be confirmed.",
                         "de": "Die Aktualisierung ist fertig; ältere Update-Dateien bleiben erhalten, weil ihre Zuordnung nicht bestätigt werden konnte."}})
    if root is None: return result
    try: atomic_json(root / "logs/update-storage.json", result)
    except (WizardError, OSError, ValueError):
        result["warnings"].append({"code": "update_storage_report_not_saved", "messages": {
            "en": "The update is ready; its storage cleanup report could not be saved.",
            "de": "Die Aktualisierung ist fertig; das Protokoll ihrer Speicherbereinigung konnte nicht gespeichert werden."}})
    return result
