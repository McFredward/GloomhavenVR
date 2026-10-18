"""Keep one published update APK without discarding its shared compiler cache.

Updates are atomic replacements: the selected APK must remain intact until its
successor is signed and published. Older Wizard-produced final APKs can then be
removed. Native Players, compiler projects, converted banks and source snapshots
are deliberately independent of that output history. No payload hashing, Unity
import, recursive cache sweep or external input deletion is needed here.
"""
from __future__ import annotations

from pathlib import Path
import stat

from state import Cancelled, WizardError, atomic_json

FAST_AREAS = ("updates/code-projects", "updates/source-inputs", "updates/game-inputs",
              "cache/quest-figure-meshes", "cache/quest-native-audio", "cache/quest-movies",
              "cache/environment-meshes", "cache/environment-native", "cache/environment-geometry",
              "cache/update-art-delivery")


def _overlaps(path, other):
    return path == other or path in other.parents or other in path.parents


def protected_paths(helper, root, build, proofs, *, cancel=None):
    """Read only published pointers and stopped/in-progress run dependencies."""
    result = set()
    helper._completed_build(build, proofs)
    latest = helper._read(build / "latest-build.json", proofs)
    result.add(build / latest["apk"])
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
                        if name not in ("baseApk", "sourcePackage", "updateKey")}) != key):
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
