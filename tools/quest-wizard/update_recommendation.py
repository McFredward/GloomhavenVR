"""Cheap homepage hints; update preflight still owns all byte qualifications.

Never hash an APK, open a content bank or walk a source inventory here.
Closed receipts and bounded APK contracts can suggest a local update. The
explicit preflight still owns byte validation, signatures and compiler inputs.
"""
from __future__ import annotations

import json
from pathlib import Path
import re
import stat

from state import WizardError, ordinary

SHA = re.compile(r"[0-9a-f]{64}\Z")
APK = re.compile(r"(?:builds|updates)/([0-9a-f]{64})/[A-Za-z0-9._-]+\.apk\Z")
BANK = re.compile(r"(?:builds|updates)/[0-9a-f]{64}/GloomhavenVR-Quest-content\.zip\Z")
SOURCE_FILES = ("scripts/build-quest.py", "tools/quest-builder/update_driver.py",
                "tools/quest-builder/apk_update.py", "src/GloomhavenVR/GloomhavenVR.csproj",
                "src/GloomhavenVR/Net/NetProtocol.cs")
MAX_CONTENT_OWNERS = 256
MAX_METADATA_BYTES = 4 * 1048576


def _file(path, size=None):
    path = ordinary(path)
    info = path.lstat()
    if (not stat.S_ISREG(info.st_mode) or getattr(info, "st_file_attributes", 0) & 0x400
            or info.st_size <= 0 or (size is not None and info.st_size != size)):
        raise ValueError("Update hint input is missing or changed.")
    return info


def _json(path, limit=2 * 1048576, *, budget=None):
    if budget is not None: limit = min(limit, budget["remaining"])
    if _file(path).st_size > limit: raise ValueError("Update hint metadata is too large.")
    with Path(path).open("rb") as stream: raw = stream.read(limit + 1)
    if len(raw) > limit: raise ValueError("Update hint metadata grew beyond its bound.")
    if budget is not None: budget["remaining"] -= len(raw)
    value = json.loads(raw)
    if not isinstance(value, dict) or value.get("schema") != 1:
        raise ValueError("Unsupported update hint metadata.")
    return value


def _identity(details):
    owners = [details]
    report = details.get("buildReport")
    if isinstance(report, dict): owners.append(report)
    for item in tuple(owners):
        provenance = item.get("buildProvenance")
        if isinstance(provenance, dict) and isinstance(provenance.get("runtime"), dict):
            owners.append(provenance["runtime"])
    if isinstance(details.get("installationContract"), dict): owners.append(details["installationContract"])
    keys = [item["inputKey"] for item in owners if "inputKey" in item]
    builds = [item["modBuild"] for item in owners if "modBuild" in item]
    if (not keys or any(not isinstance(key, str) or not SHA.fullmatch(key) for key in keys)
            or len(set(keys)) != 1 or not builds
            or any(type(build) is not int or not 1 <= build <= 65535 for build in builds)
            or len(set(builds)) != 1):
        raise ValueError("Inconsistent closed update identity.")
    return keys[0], builds[0]


def _receipt(apk, build, *, require_apk=True, budget=None):
    apk = ordinary(apk)
    relative = apk.relative_to(build).as_posix()
    matched = APK.fullmatch(relative)
    if not matched: raise ValueError("Not an owned closed APK.")
    receipt = _json(build / "receipts/build" / (matched[1] + ".json"), budget=budget)
    details = receipt.get("details")
    if (receipt.get("stage") != "build" or receipt.get("key") != matched[1]
            or not isinstance(details, dict) or details.get("isDiagnostic") is not False
            or details.get("package") != "dev.gloomhavenvr.quest"
            or not SHA.fullmatch(str(details.get("apkSha256", "")))):
        raise ValueError("No closed complete-game receipt.")
    rows = receipt.get("outputs")
    if not isinstance(rows, list) or not 1 <= len(rows) <= 64: raise ValueError("Invalid output roster.")
    matches = [row for row in rows if isinstance(row, dict) and row.get("path") == relative]
    if (len(matches) != 1 or type(matches[0].get("size")) is not int or matches[0]["size"] <= 0
            or matches[0].get("sha256") != details["apkSha256"]):
        raise ValueError("Closed APK output differs from its receipt.")
    # Only the selected APK is needed for this update. Closed ancestor receipts
    # locate its retained bank even after old APK payloads have been cleaned up.
    if require_apk: _file(apk, matches[0]["size"])
    return details, _identity(details)


def _portable(apk, build, *, budget=None):
    """An unchanged imported package can retain the bank without a second copy."""
    relative = apk.relative_to(build).as_posix()
    if not re.fullmatch(r"imports/portable/[0-9a-f]{64}/GloomhavenVR-Quest\.apk", relative): return False
    value = _json(apk.parent / "portable-source.json", 65536, budget=budget)
    entry, content = value.get("apk"), value.get("content")
    if (not isinstance(entry, dict) or not isinstance(content, dict)
            or not SHA.fullmatch(str(entry.get("sha256", "")))
            or not SHA.fullmatch(str(content.get("sha256", "")))
            or type(entry.get("size")) is not int or type(content.get("size")) is not int
            or content["size"] <= 0 or content.get("archive") != "quest-startup-content.zip"):
        return False
    _file(apk, entry["size"])
    source = value.get("sourcePackage")
    if not isinstance(source, str) or not source or Path(source).suffix.lower() != ".zip": return False
    info = _file(source)
    stamp = value.get("sourceStamp")
    return (isinstance(stamp, dict) and stamp.get("identity") == [info.st_dev, info.st_ino, info.st_size,
            info.st_mtime_ns, info.st_ctime_ns])


def _content(apk, build, input_key, first, budget):
    """Follow only exact retained base owners, never scan other build results."""
    seen = set()
    # Eight predecessors is ordinary after repeated compact updates. Bound total
    # metadata instead of silently declaring the bank missing on the ninth one.
    for index in range(MAX_CONTENT_OWNERS):
        apk = ordinary(apk)
        if apk in seen: return False
        seen.add(apk)
        if _portable(apk, build, budget=budget): return True
        details, identity = first if index == 0 else _receipt(apk, build, require_apk=False, budget=budget)
        if identity[0] != input_key: return False
        rows = details.get("contentFiles")
        if isinstance(rows, list) and 0 < len(rows) <= 8:
            for row in rows:
                if (not isinstance(row, dict) or not isinstance(row.get("path"), str)
                        or not BANK.fullmatch(row["path"]) or type(row.get("size")) is not int
                        or not SHA.fullmatch(str(row.get("sha256", "")))):
                    return False
                _file(build / row["path"], row["size"])
                contract = details.get("installationContract", {})
                if isinstance(contract, dict) and contract.get("gameArchiveSha256", row["sha256"]) != row["sha256"]:
                    return False
            return True
        base = details.get("baseApk")
        if details.get("retainedGameContent") is not True or not isinstance(base, str) or not base: return False
        apk = Path(base)
    return False


def recommend(repo, store, source, selected, games, recent, helper):
    """Return a bounded metadata hint, not an update feasibility certificate."""
    installed = selected.get("modBuild") if isinstance(selected, dict) else None
    bundled = source.get("modBuild")
    result = {"status": "unavailable", "available": False, "inputsReady": False,
              "installedBuild": installed, "bundledBuild": bundled,
              "apkPath": selected.get("path") if isinstance(selected, dict) else None,
              "gameRoot": None, "reason": "no-completed-apk", "missing": [],
              "validation": "metadata-only"}
    if selected is None: return result
    if type(installed) is not int or type(bundled) is not int:
        result["reason"] = "unknown-build"; return result
    # Host-only Wizard repairs change the source commit but not the runtime.
    # Do not ask users to recompile an already current ModBuild for that reason.
    if bundled <= installed:
        result.update(status="current" if bundled == installed else "newer-installed",
                      reason="already-current" if bundled == installed else "installed-build-newer")
        return result
    result.update(status="available", available=True, reason="inputs-missing")
    try:
        for path in SOURCE_FILES: _file(Path(repo) / path)
    except (OSError, ValueError, WizardError): result["missing"].append("bundled-update-source")
    candidates = [row.get("gameRoot") for row in recent[:8]] + [row.get("gameRoot") for row in games[:8]]
    seen = set()
    for candidate in candidates:
        if not isinstance(candidate, str) or not candidate or candidate in seen: continue
        seen.add(candidate)
        try:
            path = Path(candidate).absolute()
            # Builder snapshots are not the user's selected PC installation.
            if store.root == path or store.root in path.parents: continue
            helper.game_data(path)
            result["gameRoot"] = candidate; break
        except (OSError, ValueError, RuntimeError): continue
    if result["gameRoot"] is None: result["missing"].append("owned-pc-game")
    try:
        build = ordinary(store.root / "build")
        budget = {"remaining": MAX_METADATA_BYTES}
        details, identity = _receipt(Path(selected["path"]), build, budget=budget)
        if identity[1] != installed: raise ValueError("The selected completed APK identity changed.")
        seed = details.get("updateSeed")
        if not isinstance(seed, dict) and hasattr(helper, "_local_helper"):
            # Read only ZIP headers and bounded signed JSON contracts. No
            # original DLL or seed payload is decompressed or hashed here.
            base = helper.apk_updates.inspect_base(Path(selected["path"]))
            if base["inputKey"] != identity[0]: raise ValueError("APK input identity differs from its receipt.")
            seed = helper._local_helper("update_seed").inspect_seed(Path(selected["path"]), base)
        if isinstance(seed, dict): result["seed"] = {name: seed[name] for name in ("available", "reason", "state") if name in seed}
        if isinstance(seed, dict) and seed.get("available") is True:
            result["installedGame"] = True
            result["missing"] = [name for name in result["missing"] if name != "owned-pc-game"]
        elif not _content(Path(selected["path"]), build, identity[0], (details, identity), budget):
            result["missing"].append("campaign-content")
    except (OSError, ValueError, RuntimeError, KeyError, TypeError, AttributeError, WizardError):
        result["missing"].append("campaign-content")
    result["inputsReady"] = not result["missing"]
    if result["inputsReady"]: result["reason"] = "local-update-ready"
    return result
