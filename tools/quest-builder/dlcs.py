"""Capture local DLC availability before building; no store API runs on Quest.

The original game ships DLC data with the base installation. File presence is
therefore not ownership. Prefer provider-local installed-entitlement metadata
where it identifies ownership (GOG mini-manifests); preserve the existing local
Steam fallback. Epic's base install records cannot prove bundled-DLC entitlement.
Every provider can use a small explicit ownership declaration. This is deliberately
a convenience hurdle, not DRM or tamper-proof license verification.
"""
from __future__ import annotations

import argparse
import ctypes
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time

if __name__ == "__main__":
    sys.path.insert(0, str(Path(__file__).resolve().parent))
from storage import BuildError, build_progress, digest, write_json

APP_ID = 780290
CATALOG = (
    ("jotl", "DLC1", 1, 1809490, "DLC_JoTL"),
    ("solo", "DLC2", 2, 1958560, "DLC_Solo"),
    ("jotl-skins", "DLC3", 4, 2584170, "DLC_4Skins"),
)
IDS = {row[3] for row in CATALOG}
CLIENT_UNAVAILABLE = "[steam-client-unavailable]"
CLIENT_UNAVAILABLE_EXIT = 75
CLIENT_READY_SECONDS = 90.
CLIENT_RETRY_SECONDS = 2.


class SteamClientUnavailable(BuildError):
    """Only native initialization failure permits one automatic launcher action."""


class SteamProbeTimeout(BuildError):
    """An isolated native call exceeded its bounded host deadline."""


def _launch_steam():
    # The isolated Wizard loader deliberately restores temporary import aliases.
    # Load this stdlib-only sibling explicitly, as storage loads progress.py.
    spec = importlib.util.spec_from_file_location("quest_dlc_store_launchers", Path(__file__).with_name("store_launchers.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    try:
        return module.launch("steam")
    except module.LauncherError as error:
        raise BuildError("[launcher-action:steam] " + error.messages["en"] +
                         (" Details: " + error.detail if error.detail else "")) from error


def _client_progress(elapsed, *, status="progress", ready=False):
    if build_progress:
        detail = ("Steam local account and base-game/DLC query completed."
                  if ready else "Waiting for the local Steam account and ownership interface: "
                  + str(round(elapsed, 1)) + " s elapsed / " + str(int(CLIENT_READY_SECONDS))
                  + " s timeout. Sign in with the Gloomhaven-owning account if prompted.")
        # Elapsed wait is not measured license-check completion. Do not present a
        # fabricated ownership percentage while the interactive client starts.
        build_progress.event("store-steam-ready", detail=detail, status=status)


def _probe_detail(run):
    def text(value):
        if isinstance(value, bytes):
            return value.decode("utf-8", errors="replace")
        return str(value or "")
    return "exit=" + str(run.returncode) + "; stdout=" + text(getattr(run, "stdout", "")).strip()[-2000:] + "; stderr=" + text(getattr(run, "stderr", "")).strip()[-2000:]


def _probe(library, directory, output, environment, timeout=30., *, base_only=False):
    output.unlink(missing_ok=True)
    try:
        run = subprocess.run([sys.executable, "-I", str(Path(__file__).resolve()),
                              "--probe", str(library.resolve()), *(["--base-only"] if base_only else []), "--output", str(output)],
                             cwd=directory, env=environment, capture_output=True,
                             text=True, encoding="utf-8", errors="replace", timeout=timeout, shell=False)
    except subprocess.TimeoutExpired as error:
        raise SteamProbeTimeout("Local Steam DLC capture timed out after " + str(round(timeout, 1)) + " s. "
                         "stdout=" + str(error.stdout or "")[-2000:] + "; stderr=" + str(error.stderr or "")[-2000:]) from error
    except OSError as error:
        raise BuildError("Local Steam DLC capture could not start: " + str(error)) from error
    if run.returncode == CLIENT_UNAVAILABLE_EXIT and CLIENT_UNAVAILABLE in getattr(run, "stderr", ""):
        raise SteamClientUnavailable(_probe_detail(run))
    if run.returncode or not output.is_file():
        raise BuildError("Local Steam DLC capture failed; verify the active account and installed DLCs or supply --dlc-ownership-json. "
                         + _probe_detail(run))
    try:
        value = json.loads(output.read_text(encoding="utf-8"))
    except (OSError, ValueError) as error:
        raise BuildError("Local Steam DLC capture returned invalid output. " + _probe_detail(run)) from error
    if not isinstance(value, dict):
        raise BuildError("Local Steam DLC capture returned inconsistent data. " + _probe_detail(run))
    return value


def _local_capture(library, directory, output, environment, *, base_only=False):
    try:
        return _probe(library, directory, output, environment, base_only=base_only)
    except SteamClientUnavailable as initial:
        initial_detail = str(initial)
    _client_progress(0., status="start")
    try:
        _launch_steam()  # Exactly once; this does not grant a license or account.
    except BuildError as error:
        raise BuildError(str(error) + " Initial native query: " + initial_detail) from error
    started = time.monotonic()
    latest_detail = initial_detail
    while True:
        elapsed = time.monotonic() - started
        remaining = CLIENT_READY_SECONDS - elapsed
        if remaining <= 0:
            raise BuildError("[launcher-action:steam] Steam's local ownership interface is not ready. "
                             "Sign in with the account owning Gloomhaven, leave Steam open, then retry this step. "
                             "Existing build results are retained. Last native query: " + latest_detail)
        _client_progress(elapsed)
        try:
            value = _probe(library, directory, output, environment, min(30., remaining), base_only=base_only)
        except SteamClientUnavailable as unavailable:
            latest_detail = str(unavailable)
            time.sleep(min(CLIENT_RETRY_SECONDS, max(0., CLIENT_READY_SECONDS - (time.monotonic() - started))))
            continue
        except SteamProbeTimeout as error:
            raise BuildError("[launcher-action:steam] Steam did not complete its local ownership query in time. "
                             "Leave the client open, sign in if prompted, then retry this step. " + str(error)) from error
        _client_progress(time.monotonic() - started, status="complete", ready=True)
        return value


def manifest(steam_id: str, installed_ids: list[int], source: str,
             provider: str = "steam", provider_id: str | None = None) -> dict:
    if (not isinstance(installed_ids, list) or any(type(i) is not int or i not in IDS for i in installed_ids)
            or len(installed_ids) != len(set(installed_ids))):
        raise BuildError("DLC ownership contains duplicate or unsupported app IDs.")
    mask = sum(row[2] for row in CATALOG if row[3] in installed_ids)
    if provider not in ("steam", "epic", "gog") or provider != "steam" and not provider_id:
        raise BuildError("DLC ownership needs a supported provider and the selected local provider ID.")
    result = {"schema": 1, "provider": provider, "appId": APP_ID,
            "steamId": steam_id, "source": source, "ownedMask": mask,
            "installedAppIds": sorted(installed_ids),
            "dlcs": [{"key": row[1], "appId": row[3], "owned": row[3] in installed_ids}
                     for row in CATALOG]}
    if provider != "steam":
        result["providerId"] = provider_id
    return result


def declaration(path: Path, steam_id: str, provider: str = "steam", provider_id: str | None = None) -> dict:
    if path.stat().st_size > 4096:
        raise BuildError("DLC ownership JSON must contain only the small local declaration, not an account export.")
    try:
        data = json.loads(path.read_text(encoding="utf-8-sig"))
    except (ValueError, OSError) as error:
        raise BuildError("DLC ownership JSON is invalid.") from error
    expected_fields = {"schema", "provider", "appId", "installedAppIds", "steamId" if provider == "steam" else "providerId"}
    if (not isinstance(data, dict) or set(data) != expected_fields
            or type(data.get("schema")) is not int or data.get("schema") != 1 or data.get("provider") != provider
            or type(data.get("appId")) is not int or data.get("appId") != APP_ID
            or data.get("steamId" if provider == "steam" else "providerId") != (steam_id if provider == "steam" else provider_id)):
        raise BuildError("DLC ownership declaration must match the selected local provider account and Gloomhaven app.")
    value = manifest(steam_id, data["installedAppIds"], "explicit-local-declaration", provider, provider_id)
    value["declarationSha256"] = digest(path)
    return value


def native_query(library: Path, sdk=None, *, base_only=False) -> dict:
    """Read-only flat SDK calls. The parent handles client startup, never login."""
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
    installed = None if base_only else api("SteamAPI_ISteamApps_BIsDlcInstalled", ctypes.c_bool, ctypes.c_void_p, ctypes.c_uint32)
    if not init():
        raise SteamClientUnavailable("Start Steam with the account owning Gloomhaven before building its Quest copy.")
    try:
        a, u, v = apps(), users(), utils()
        if not a or not u or not v or app_id(v) != APP_ID or not subscribed(a, APP_ID):
            raise BuildError("[launcher-action:steam] The active Steam client has no matching Gloomhaven license/context.")
        if base_only:
            return manifest(str(user_id(u)), [], "steam-local-client")
        owned = [row[3] for row in CATALOG if subscribed(a, row[3])]
        active = [row[3] for row in CATALOG if installed(a, row[3])]
        if set(owned) - set(active):
            raise BuildError("[launcher-action:steam] Install/enable your purchased Gloomhaven DLCs in Steam on the PC, then rebuild the Quest APK.")
        if set(active) - set(owned):
            raise BuildError("[launcher-action:steam] Steam DLC ownership and installation disagree; refresh the PC installation before building.")
        return manifest(str(user_id(u)), active, "steam-local-client")
    finally:
        shutdown()


def capture(args, game: Path, profile: dict) -> dict:
    provider = profile.get("provider", "steam")
    provider_id = profile.get("providerId", profile["steamId"])
    explicit = getattr(args, "dlc_ownership_json", None)
    selected = getattr(args, "owned_dlc", None)
    if explicit and selected:
        raise BuildError("Choose one DLC ownership source.")
    if explicit:
        return _explicit_ownership(game, profile, declaration(Path(explicit), profile["steamId"], provider, provider_id))
    if selected is not None:
        try:
            ids = [next(row[3] for row in CATALOG if row[0] == value) for value in selected]
        except (StopIteration, TypeError) as error:
            raise BuildError("Explicit DLC selection contains an unsupported product.") from error
        source = "maintainer-declared-test-ownership" if profile.get("isDummy") else "explicit-local-ownership"
        return _explicit_ownership(game, profile, manifest(profile["steamId"], ids, source, provider, provider_id))
    if profile.get("isDummy"):
        return manifest(profile["steamId"], [], "dummy-base-game-only")
    if provider != "steam":
        import provider_metadata
        metadata_root = getattr(args, "provider_metadata_dir", None)
        evidence = provider_metadata.installation(game, provider, Path(metadata_root) if metadata_root else None)
        if evidence is None or not evidence["ownershipComplete"]:
            raise BuildError("The selected " + provider + " copy has no complete local DLC entitlement markers. "
                             "Bundled DLC files are not ownership; supply --dlc-ownership-json or explicit --owned-dlc selections "
                             "(an empty installedAppIds list declares base game only).")
        ids = [row[3] for row in CATALOG if row[0] in evidence["ownedDlcKeys"]]
        value = manifest(profile["steamId"], ids, evidence["source"], provider, provider_id)
        value["installationEvidence"] = evidence
        return value
    if sys.platform != "win32":
        raise BuildError("Automatic DLC capture uses the local Windows Steam client; supply --dlc-ownership-json on another build host.")
    return _steam_capture(game, profile)


def _explicit_ownership(game, profile, ownership):
    # A real Windows Steam declaration chooses DLCs, not the base-game license
    # or account. Keep offline/Linux and other provider metadata contracts;
    # never try to load a Windows DLL on those hosts.
    if not profile.get("isDummy") and profile.get("provider", "steam") == "steam" and sys.platform == "win32":
        _steam_capture(game, profile, base_only=True)
    return ownership


def _steam_capture(game, profile, *, base_only=False):
    library = game / "Plugins/x86_64/steam_api64.dll"
    if not library.is_file():
        raise BuildError("The PC game has no original Steam library for local DLC capture; repair the selected Steam game installation before retrying.")
    # Isolate native initialization and any SDK abort from the builder itself.
    # steam_appid.txt belongs only to this disposable directory, never the game.
    with tempfile.TemporaryDirectory(prefix="ghvr-dlc-") as temporary:
        directory = Path(temporary)
        output = directory / "dlcs.json"
        (directory / "steam_appid.txt").write_text(str(APP_ID), encoding="ascii")
        environment = os.environ.copy()
        environment.update(SteamAppId=str(APP_ID), SteamGameId=str(APP_ID))
        value = _local_capture(library, directory, output, environment, base_only=base_only)
    if value.get("steamId") != profile["steamId"]:
        raise BuildError("[launcher-action:steam] DLC ownership comes from a different active Steam account; switch Steam before building.")
    expected = manifest(profile["steamId"], value.get("installedAppIds"), "steam-local-client")
    if value != expected or base_only and expected["installedAppIds"]:
        raise BuildError("Local Steam DLC capture returned inconsistent data.")
    if not base_only:
        expected["librarySha256"] = digest(library)
    return expected


def stage(project: Path, ownership: dict) -> dict:
    """Exclude unavailable DLC rules while retaining native promotional assets."""
    project = project.absolute()
    if any(parent.is_symlink() for parent in (project, *project.parents)):
        raise BuildError("Generated DLC filtering must not follow links.")
    generated = project / "Assets/StreamingAssets/Rulebase/DLC"
    if any(parent.is_symlink() for parent in (generated, *generated.parents)):
        raise BuildError("Generated DLC filtering must not follow links.")
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
               "scope": "owned DLC rule selection and original local availability; complete campaign/Android outcomes require runtime evidence"}
    write_json(project / "Assets/Quest/Resources/quest-dlc-ownership.json", ownership)
    write_json(project / "QuestStartupEvidence/dlc-content-selection.json", receipt)
    return receipt


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--probe", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--base-only", action="store_true")
    args = parser.parse_args()
    try:
        write_json(args.output, native_query(args.probe, base_only=args.base_only))
    except SteamClientUnavailable as error:
        print(CLIENT_UNAVAILABLE + " " + str(error), file=sys.stderr)
        raise SystemExit(CLIENT_UNAVAILABLE_EXIT)
    except (BuildError, OSError, AttributeError) as error:
        print(str(error), file=sys.stderr)
        raise SystemExit(1)
