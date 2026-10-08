"""Bounded local discovery using the builder's existing provider/profile rules."""
from __future__ import annotations
import importlib.util
import json
import math
import os
from pathlib import Path
import platform
import re
import shutil
import sys
import subprocess
import threading

from state import WizardError, read_json, value_hash
import host_paths


_LOADER_LOCK = threading.RLock()


def builder(repo):
    with _LOADER_LOCK:
        return _builder(repo)


def _builder(repo):
    path = Path(repo) / "tools/quest-installer/installer.py"
    spec = importlib.util.spec_from_file_location("_quest_wizard_platform", path)
    module = importlib.util.module_from_spec(spec)
    missing = object(); prior = sys.modules.get(spec.name, missing)
    try:
        sys.modules[spec.name] = module; spec.loader.exec_module(module)
        return module.builder_module()
    finally:
        if prior is missing: sys.modules.pop(spec.name, None)
        else: sys.modules[spec.name] = prior


def local_support_module(repo, name):
    """Load release/support tools without altering cached application modules."""
    if name not in ("release", "support"): raise WizardError("local_module", "Unsupported local helper.")
    with _LOADER_LOCK:
        missing = object(); names = ["storage", "release"] + (["support"] if name == "support" else [])
        previous = {item: sys.modules.get(item, missing) for item in names}
        try:
            for item in names:
                spec = importlib.util.spec_from_file_location(item, Path(repo) / "tools/quest-builder" / (item + ".py"))
                module = importlib.util.module_from_spec(spec); sys.modules[item] = module; spec.loader.exec_module(module)
            return module
        finally:
            for item, value in previous.items():
                if value is missing: sys.modules.pop(item, None)
                else: sys.modules[item] = value


def mod_source(repo, commit=None):
    """Describe selected source without opening the installed game's mod DLL.

    This is a small presentation read, not a second full input inventory. The
    source stage still owns hash qualification and the immutable build snapshot.
    """
    root = Path(repo)
    manifest = root / "quest-builder-release.json"
    result = {"kind": "bundled-release" if manifest.is_file() else "checkout",
              "modVersion": None, "modBuild": None, "sourceCommit": None}
    for path, pattern, field in (
            (root / "src/GloomhavenVR/GloomhavenVR.csproj", r"<Version>\s*([0-9]+\.[0-9]+\.[0-9]+(?:[-+][A-Za-z0-9.-]+)?)\s*</Version>", "modVersion"),
            (root / "src/GloomhavenVR/Net/NetProtocol.cs", r"public\s+const\s+ushort\s+ModBuild\s*=\s*([0-9]+)\s*;", "modBuild")):
        try:
            if path.is_file():
                # NetProtocol includes a large historical changelog and wire
                # implementation; its version declaration is in the prefix.
                with path.open(encoding="utf-8") as stream: raw = stream.read(1048576)
                match = re.search(pattern, raw)
                if match: result[field] = int(match[1]) if field == "modBuild" else match[1]
        except (OSError, UnicodeError): pass
    if commit is None:
        if manifest.is_file():
            try: commit = read_json(manifest).get("sourceCommit")
            except (WizardError, OSError, ValueError): pass
        elif (root / ".git").exists():
            try:
                completed = subprocess.run(["git", "-C", str(root), "rev-parse", "HEAD"],
                                           capture_output=True, text=True, timeout=3)
                if completed.returncode == 0: commit = completed.stdout.strip()
            except (OSError, subprocess.TimeoutExpired): pass
    if re.fullmatch(r"[0-9a-f]{40}", str(commit)): result["sourceCommit"] = commit
    return result


def unity_paths(workspace=None):
    editors, hubs = [], []
    if os.name == "nt":
        program = Path(os.environ.get("PROGRAMFILES", r"C:\Program Files"))
        hub_candidates = [program / "Unity Hub/Unity Hub.exe"]
        candidates = list((program / "Unity/Hub/Editor").glob("*/Editor/Unity.exe"))
        registry = Path(os.environ.get("APPDATA", "")) / "UnityHub/editors-v2.json"
    else:
        home = Path.home()
        hub_candidates = [Path(value) for value in [shutil.which("unityhub")] if value]
        local = Path(workspace) if workspace is not None else home / ".ghvrq"
        hub_candidates += sorted((local / "tools").glob("unity-hub-*/UnityHub.AppImage"))
        candidates = list((home / "Unity/Hub/Editor").glob("*/Editor/Unity"))
        candidates += list((local / "tools/unity-editors").glob("*/Editor/Unity"))
        candidates += list((home / "Unity/Hub/Editor").glob("*/Unity"))
        # A manually configured Editor remains usable even when it was not
        # installed at Hub's default location. No directory tree is scanned.
        if os.environ.get("UNITY_EDITOR_PATH"):
            candidates.append(Path(os.environ["UNITY_EDITOR_PATH"]))
        registry = Path(os.environ.get("XDG_CONFIG_HOME") or home / ".config") / "UnityHub/editors-v2.json"
    for hub in hub_candidates:
        if hub.is_file() and (os.name == "nt" or os.access(hub, os.X_OK)):
            resolved = str(hub.resolve())
            if resolved not in hubs: hubs.append(resolved)
    if registry.is_file() and registry.stat().st_size <= 262144:
        try:
            value = json.loads(registry.read_text(encoding="utf-8"))
            entries = value if isinstance(value, list) else value.get("editors", [])
            for row in entries:
                if isinstance(row, dict) and isinstance(row.get("location"), str): candidates.append(Path(row["location"]))
        except (OSError, ValueError, AttributeError): pass
    seen = set()
    for path in sorted(set(candidates), key=lambda path: path.as_posix()):
        path = Path(host_paths.editor_path(path, windows=os.name == "nt"))
        if path.is_file() and str(path) not in seen:
            seen.add(str(path))
            editors.append({"path": str(path), "version": path.parent.parent.name,
                            "androidSupport": (path.parent / "Data/PlaybackEngines/AndroidPlayer/NDK/source.properties").is_file()})
    return editors, hubs


def recent_sessions(store):
    """Offer only readable owner session IDs; a stale latest pointer is optional.

    Discovery never resumes work or reconciles a running process. Loading the
    chosen session through the status endpoint retains those existing lock rules.
    Browser origins change with the local port, so the owned store supplies the
    restart selection instead of browser storage.
    """
    recent = []
    for path in (store.root / "sessions").glob("*/state.json"):
        try:
            value = read_json(path)
            session = value.get("session")
            selected = value.get("choices")
            updated = value.get("updated", value.get("created", 0))
            if (not isinstance(session, str) or re.fullmatch(r"[0-9a-f]{32}", session) is None
                    or path.parent != store.session_dir(session) or not isinstance(selected, dict)
                    or not isinstance(selected.get("gameRoot"), str)
                    or value.get("status") not in ("ready", "running", "complete", "failed", "blocked", "cancelled", "interrupted")
                    or type(updated) not in (int, float) or not math.isfinite(updated)):
                continue
            recent.append({"session": session, "status": value["status"],
                           "gameRoot": selected["gameRoot"], "updated": updated})
        except (OSError, ValueError, KeyError, WizardError):
            continue
    recent.sort(key=lambda row: (row["updated"], row["session"]), reverse=True)
    latest = None
    try:
        pointer = read_json(store.root / "latest-session.json").get("session")
        if any(row["session"] == pointer for row in recent): latest = pointer
    except (OSError, ValueError, WizardError):
        pass
    if latest is None and recent: latest = recent[0]["session"]
    visible = recent[:8]
    if latest is not None and not any(row["session"] == latest for row in visible):
        visible[-1] = next(row for row in recent if row["session"] == latest)
    return visible, latest


def discover(repo, store):
    helper = builder(repo); games = []; candidates = []
    steam = helper.discover_steam_root()
    if steam:
        libraries = {steam}
        library = steam / "steamapps/libraryfolders.vdf"
        if library.is_file() and library.stat().st_size < 262144:
            try:
                values = helper.load_profile.__globals__["capture_steam_profile"].__globals__["parse_vdf"](library.read_text(encoding="utf-8-sig"))
                for row in values.get("libraryfolders", {}).values():
                    if isinstance(row, dict) and isinstance(row.get("path"), str): libraries.add(Path(row["path"]))
            except (OSError, ValueError): pass
        for root in libraries:
            candidates.append(("steam", root / "steamapps/common/Gloomhaven"))
    epic = Path(os.environ.get("PROGRAMDATA", r"C:\ProgramData")) / "Epic/EpicGamesLauncher/Data/Manifests"
    if epic.is_dir():
        for path in sorted(epic.glob("*.item"))[:2048]:
            if path.is_symlink() or path.stat().st_size > 131072: continue
            try:
                row = json.loads(path.read_text(encoding="utf-8-sig"))
                if row.get("DisplayName", "").casefold() == "gloomhaven" and isinstance(row.get("InstallLocation"), str):
                    candidates.append(("epic", Path(row["InstallLocation"])))
            except (OSError, ValueError, AttributeError): pass
    if os.name == "nt":
        import winreg
        try:
            with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\WOW6432Node\GOG.com\Games") as root:
                for index in range(min(winreg.QueryInfoKey(root)[0], 2048)):
                    with winreg.OpenKey(root, winreg.EnumKey(root, index)) as key:
                        if str(winreg.QueryValueEx(key, "gameName")[0]).casefold() == "gloomhaven":
                            candidates.append(("gog", Path(winreg.QueryValueEx(key, "path")[0])))
        except OSError: pass
    if os.name == "nt":
        candidates.append(("gog", Path(r"C:\GOG Games\Gloomhaven")))
    else:
        # Non-Steam installations can always be selected explicitly. These
        # common user folders are candidates, never evidence of DLC ownership.
        candidates.extend(("gog", path) for path in (Path.home() / "GOG Games/Gloomhaven",
                                                     Path.home() / "Games/Gloomhaven"))
    seen = set()
    for provider, root in candidates:
        try: helper.game_data(root)
        except (OSError, ValueError, RuntimeError): continue
        identity = (provider, str(root.resolve()))
        if identity in seen: continue
        seen.add(identity)
        games.append({"id": value_hash(identity)[:32], "provider": provider, "gameRoot": identity[1],
                      "displayName": "Gloomhaven", "profileAvailable": provider == "steam",
                      "ownershipComplete": False})
    editors, hubs = unity_paths(store.root)
    recent, latest = recent_sessions(store)
    return {"schema": 1, "event": "discovery", "modSource": mod_source(repo), "games": games, "unityEditors": editors, "unityHubs": hubs,
            "recentSessions": recent, "latestSession": latest,
            "host": {"system": platform.system(), "machine": platform.machine()},
            "capabilities": {"browse": host_paths.browse_available(), "artwork": False, "logs": True, "support": True, "spaceEstimate": True, "capture": False, "cleanCache": False}}
