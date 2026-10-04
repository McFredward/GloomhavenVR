"""Capture local DLC availability before building; no store API runs on Quest.

The original game ships DLC data with the base installation. File presence is
therefore not ownership. Windows queries the active Steam client through the
owned game's original library in a short-lived process. Portable build hosts can
consume a small explicit local ownership declaration instead; this is deliberately
a convenience hurdle, not DRM or a claim of tamper-proof license verification.
"""
from __future__ import annotations

import argparse
import ctypes
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile

if __name__ == "__main__":
    sys.path.insert(0, str(Path(__file__).resolve().parent))
from storage import BuildError, digest, write_json

APP_ID = 780290
CATALOG = (
    ("jotl", "DLC1", 1, 1809490, "DLC_JoTL"),
    ("solo", "DLC2", 2, 1958560, "DLC_Solo"),
    ("jotl-skins", "DLC3", 4, 2584170, "DLC_4Skins"),
)
IDS = {row[3] for row in CATALOG}


def manifest(steam_id: str, installed_ids: list[int], source: str) -> dict:
    if (not isinstance(installed_ids, list) or len(installed_ids) != len(set(installed_ids))
            or any(type(i) is not int or i not in IDS for i in installed_ids)):
        raise BuildError("DLC ownership contains duplicate or unsupported app IDs.")
    mask = sum(row[2] for row in CATALOG if row[3] in installed_ids)
    return {"schema": 1, "provider": "steam", "appId": APP_ID,
            "steamId": steam_id, "source": source, "ownedMask": mask,
            "installedAppIds": sorted(installed_ids),
            "dlcs": [{"key": row[1], "appId": row[3], "owned": row[3] in installed_ids}
                     for row in CATALOG]}


def declaration(path: Path, steam_id: str) -> dict:
    if path.stat().st_size > 4096:
        raise BuildError("DLC ownership JSON must contain only the small local declaration, not an account export.")
    try:
        data = json.loads(path.read_text(encoding="utf-8-sig"))
    except (ValueError, OSError) as error:
        raise BuildError("DLC ownership JSON is invalid.") from error
    if (not isinstance(data, dict) or set(data) != {"schema", "provider", "appId", "steamId", "installedAppIds"}
            or data.get("schema") != 1 or data.get("provider") != "steam"
            or data.get("appId") != APP_ID or data.get("steamId") != steam_id):
        raise BuildError("DLC ownership declaration must match the selected Steam account and Gloomhaven app.")
    value = manifest(steam_id, data["installedAppIds"], "explicit-local-declaration")
    value["declarationSha256"] = digest(path)
    return value


def native_query(library: Path, sdk=None) -> dict:
    """Read-only flat SDK calls. Never log in, purchase, install or restart Steam."""
    if sdk is None:
        if sys.platform != "win32" or ctypes.sizeof(ctypes.c_void_p) != 8:
            raise BuildError("Automatic DLC capture requires 64-bit Windows and the running Steam client.")
        sdk = ctypes.CDLL(str(library.resolve()))
    def api(name, result, *args):
        try:
            call = getattr(sdk, name)
        except AttributeError as error:
            raise BuildError("The owned Steam library lacks the required local DLC interface: " + name) from error
        call.restype, call.argtypes = result, list(args)
        return call
    init = api("SteamAPI_Init", ctypes.c_bool)
    shutdown = api("SteamAPI_Shutdown", None)
    apps = api("SteamAPI_SteamApps_v008", ctypes.c_void_p)
    users = api("SteamAPI_SteamUser_v021", ctypes.c_void_p)
    utils = api("SteamAPI_SteamUtils_v010", ctypes.c_void_p)
    user_id = api("SteamAPI_ISteamUser_GetSteamID", ctypes.c_uint64, ctypes.c_void_p)
    app_id = api("SteamAPI_ISteamUtils_GetAppID", ctypes.c_uint32, ctypes.c_void_p)
    subscribed = api("SteamAPI_ISteamApps_BIsSubscribedApp", ctypes.c_bool, ctypes.c_void_p, ctypes.c_uint32)
    installed = api("SteamAPI_ISteamApps_BIsDlcInstalled", ctypes.c_bool, ctypes.c_void_p, ctypes.c_uint32)
    if not init():
        raise BuildError("Start Steam with the account owning Gloomhaven before building its Quest copy.")
    try:
        a, u, v = apps(), users(), utils()
        if not a or not u or not v or app_id(v) != APP_ID or not subscribed(a, APP_ID):
            raise BuildError("The active Steam client has no matching Gloomhaven license/context.")
        owned = [row[3] for row in CATALOG if subscribed(a, row[3])]
        active = [row[3] for row in CATALOG if installed(a, row[3])]
        if set(owned) - set(active):
            raise BuildError("Install/enable your purchased Gloomhaven DLCs in Steam on the PC, then rebuild the Quest APK.")
        if set(active) - set(owned):
            raise BuildError("Steam DLC ownership and installation disagree; refresh the PC installation before building.")
        return manifest(str(user_id(u)), active, "steam-local-client")
    finally:
        shutdown()


def capture(args, game: Path, profile: dict) -> dict:
    explicit = getattr(args, "dlc_ownership_json", None)
    selected = getattr(args, "owned_dlc", None)
    if explicit and selected:
        raise BuildError("Choose one DLC ownership source.")
    if explicit:
        return declaration(Path(explicit), profile["steamId"])
    if selected is not None:
        if not profile.get("isDummy"):
            raise BuildError("--owned-dlc is reserved for labelled maintainer diagnostics; use automatic Steam capture or --dlc-ownership-json.")
        ids = [next(row[3] for row in CATALOG if row[0] == value) for value in selected]
        return manifest(profile["steamId"], ids, "maintainer-declared-test-ownership")
    if profile.get("isDummy"):
        return manifest(profile["steamId"], [], "dummy-base-game-only")
    if sys.platform != "win32":
        raise BuildError("Automatic DLC capture uses the local Windows Steam client; supply --dlc-ownership-json on another build host.")
    library = game / "Plugins/x86_64/steam_api64.dll"
    if not library.is_file():
        raise BuildError("The PC game has no original Steam library for local DLC capture; supply --dlc-ownership-json.")
    # Isolate native initialization and any SDK abort from the builder itself.
    # steam_appid.txt belongs only to this disposable directory, never the game.
    with tempfile.TemporaryDirectory(prefix="ghvr-dlc-") as temporary:
        directory = Path(temporary)
        output = directory / "dlcs.json"
        (directory / "steam_appid.txt").write_text(str(APP_ID), encoding="ascii")
        environment = os.environ.copy()
        environment.update(SteamAppId=str(APP_ID), SteamGameId=str(APP_ID))
        try:
            run = subprocess.run([sys.executable, "-I", str(Path(__file__).resolve()),
                                  "--probe", str(library.resolve()), "--output", str(output)],
                                 cwd=directory, env=environment, capture_output=True,
                                 text=True, encoding="utf-8", errors="replace", timeout=30, shell=False)
        except (OSError, subprocess.TimeoutExpired) as error:
            raise BuildError("Local Steam DLC capture failed or timed out; keep Steam running or supply --dlc-ownership-json.") from error
        if run.returncode or not output.is_file():
            raise BuildError("Local Steam DLC capture failed; verify the active account and installed DLCs or supply --dlc-ownership-json. "
                             + run.stderr.strip()[-800:])
        value = json.loads(output.read_text())
    if value.get("steamId") != profile["steamId"]:
        raise BuildError("DLC ownership comes from a different active Steam account; switch Steam before building.")
    expected = manifest(profile["steamId"], value.get("installedAppIds"), "steam-local-client")
    if value != expected:
        raise BuildError("Local Steam DLC capture returned inconsistent data.")
    expected["librarySha256"] = digest(library)
    return expected


def stage(project: Path, ownership: dict) -> dict:
    """Exclude unavailable DLC rules while retaining native promotional assets."""
    generated = project / "Assets/StreamingAssets/Rulebase/DLC"
    removed = []
    for _, key, _, app, folder in CATALOG:
        path = generated / folder
        if app in ownership["installedAppIds"]:
            if folder != "DLC_4Skins" and not (path / (folder + "_Global.ruleset")).is_file():
                raise BuildError("Purchased " + key + " is missing from the selected PC copy; update it before building.")
            continue
        if not path.exists():
            continue
        if path.is_symlink():
            raise BuildError("Generated DLC filtering must not follow links.")
        for item in sorted(path.rglob("*")):
            if item.is_symlink():
                raise BuildError("Generated DLC filtering must not follow links.")
            if item.is_file():
                removed.append({"path": item.relative_to(project).as_posix(), "sha256": digest(item), "bytes": item.stat().st_size})
        import shutil
        shutil.rmtree(path)
        Path(str(path) + ".meta").unlink(missing_ok=True)
    receipt = {"schema": 1, "ownership": ownership, "removedUnavailableRuleFiles": removed,
               "promotionalAssetsRetained": True,
               "scope": "startup rules and original local DLC availability; playable campaign export remains gated"}
    write_json(project / "Assets/Quest/Resources/quest-dlc-ownership.json", ownership)
    write_json(project / "QuestStartupEvidence/dlc-content-selection.json", receipt)
    return receipt


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--probe", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        write_json(args.output, native_query(args.probe))
    except (BuildError, OSError, AttributeError) as error:
        print(str(error), file=sys.stderr)
        raise SystemExit(1)
