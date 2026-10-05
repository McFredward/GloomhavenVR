"""Persistent local conversion workflow; CLI and loopback UI use the same engine."""
from __future__ import annotations
import argparse
import json
import os
from pathlib import Path, PurePosixPath, PureWindowsPath
import re
import sys

if __package__ in (None, ""):
    sys.path.insert(0, str(Path(__file__).resolve().parent))

import discovery
import provision
from processes import Supervisor
from state import (Cancelled, STAGES, Store, WizardError, atomic_json, digest,
                   file_lock, ordinary, read_json, value_hash)

REPO = Path(__file__).resolve().parents[2]


def choices(value):
    allowed = {"gameRoot", "provider", "sourceRoot", "sourceCommit", "sourceRef", "unityEditor", "unityHub", "steamRoot", "steamId",
               "profile", "ownedDlc", "steamLogo", "install", "acceptUnityTerms", "language"}
    if not isinstance(value, dict) or set(value) - allowed:
        raise WizardError("invalid_choices", "Wizard choices contain unsupported fields.", "Die Auswahl enthält nicht unterstützte Felder.")
    result = dict(value)
    if not isinstance(result.get("gameRoot"), str) or not result["gameRoot"].strip():
        raise WizardError("game_required", "Select your owned Gloomhaven installation.", "Bitte die eigene Gloomhaven-Installation wählen.")
    result.setdefault("provider", "steam"); result.setdefault("language", "de")
    result.setdefault("install", True); result.setdefault("acceptUnityTerms", False)
    if result["provider"] not in ("steam", "gog", "epic") or result["language"] not in ("en", "de"):
        raise WizardError("invalid_choices", "Unsupported provider or language.")
    if any(type(result[name]) is not bool for name in ("install", "acceptUnityTerms")):
        raise WizardError("invalid_choices", "Wizard switches must be booleans.")
    if "sourceCommit" in result and not re.fullmatch(r"[0-9a-f]{40}", str(result["sourceCommit"])):
        raise WizardError("source_commit", "Select a full immutable source commit.")
    if "sourceRef" in result and not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._/-]{0,159}", str(result["sourceRef"])):
        raise WizardError("source_ref", "Invalid source reference.")
    if "ownedDlc" in result and (not isinstance(result["ownedDlc"], list)
                                 or any(not isinstance(item, str) or item not in ("jotl", "solo", "jotl-skins") for item in result["ownedDlc"])
                                 or len(set(result["ownedDlc"])) != len(result["ownedDlc"])):
        raise WizardError("dlc_choices", "Select supported purchased DLCs.", "Bitte unterstützte, gekaufte DLCs wählen.")
    for name in ("gameRoot", "sourceRoot", "unityEditor", "unityHub", "steamRoot", "steamLogo"):
        if name in result:
            if not isinstance(result[name], str) or not result[name] or "\0" in result[name]: raise WizardError("invalid_path", "Invalid selected path.")
            result[name] = str(Path(result[name]).expanduser().absolute())
    if "profile" in result:
        profile = discovery.builder(REPO).load_profile.__globals__["validate_identity"](result["profile"])
        if profile["provider"] != result["provider"]: raise WizardError("profile_provider", "Profile provider differs from the selected installation.")
        result["profile"] = profile
    return result


class Engine:
    def __init__(self, store, repo=REPO, *, actions=None, supervisor_factory=Supervisor, emit=lambda value: None):
        self.store, self.repo, self.actions, self.supervisor_factory, self.emit = store, Path(repo), actions or {}, supervisor_factory, emit

    def key(self, state, stage):
        before = STAGES[:STAGES.index(stage)]
        names = {"tools": (), "source": ("sourceRoot", "sourceCommit", "sourceRef", "gameRoot"),
                 "unity": ("unityEditor", "unityHub", "acceptUnityTerms"),
                 "profile": ("provider", "steamRoot", "steamId", "profile", "steamLogo"),
                 "inspect": ("gameRoot", "ownedDlc"), "build": (), "install": ("install",)}[stage]
        return value_hash({"choices": {name: state["choices"].get(name) for name in names}, "stage": stage, "pins": value_hash(provision.LOCK),
                           "dependencies": {name: state.get("completed", {}).get(name, {}).get("key") for name in before}})

    def run(self, session):
        with file_lock(self.store.root / "run.lock"):
            state = self.store.load(session)
            if value_hash(state["choices"]) != state["choicesKey"]: raise WizardError("choices_changed", "Saved choices changed; create a new session.")
            self.store.clear_cancel(session); state["status"] = "running"; state["needsActions"] = []
            supervisor = self.supervisor_factory(self.store, session)
            self.store.event(state, "run_started")
            for row in state["stages"]:
                stage = row["id"]; key = self.key(state, stage)
                try:
                    self.store.check_cancel(session)
                    prior = self.store.valid(session, stage, key)
                    if prior:
                        row.update(status="complete", details=prior["details"])
                        state.setdefault("completed", {})[stage] = {"key": prior["key"], "details": prior["details"]}
                        self.emit(self.store.event(state, "stage_reused", stage)); continue
                    row.update(status="running", attempts=row["attempts"] + 1)
                    self.emit(self.store.event(state, "stage_started", stage))
                    action = self.actions.get(stage) or getattr(self, "stage_" + stage)
                    paths, details = action(state, supervisor)
                    self.store.check_cancel(session)
                    receipt = self.store.publish(session, stage, key, paths, details)
                    state.setdefault("completed", {})[stage] = {"key": receipt["key"], "details": details}
                    row.update(status="complete", details=details)
                    self.emit(self.store.event(state, "stage_complete", stage))
                except (Cancelled, WizardError) as error:
                    row["status"] = "cancelled" if isinstance(error, Cancelled) else "blocked"
                    state["status"] = row["status"]
                    state["needsActions"] = [] if isinstance(error, Cancelled) else [{"code": error.code, "message": error.message, "parameters": error.parameters}]
                    self.emit(self.store.event(state, error.code, stage, **error.parameters)); return state
                except (OSError, ValueError, RuntimeError) as error:
                    row["status"] = state["status"] = "failed"
                    state["needsActions"] = [{"code": "stage_failed", "message": {"en": str(error), "de": str(error)}}]
                    self.emit(self.store.event(state, "stage_failed", stage)); return state
            state["status"] = "complete"; self.emit(self.store.event(state, "run_complete")); return state

    def details(self, state, stage): return state["completed"][stage]["details"]
    def log(self, state, stage): return self.store.session_dir(state["session"]) / "logs" / (stage + ".log")

    def stage_tools(self, state, supervisor): return provision.tools(self.store, state["session"], supervisor)
    def stage_source(self, state, supervisor):
        return provision.source_checkout(self.store, state["session"], state["choices"], self.details(state, "tools"), supervisor, self.repo)

    def stage_unity(self, state, supervisor):
        selected = state["choices"].get("unityEditor")
        editors, hubs = discovery.unity_paths()
        if not selected:
            selected = next((row["path"] for row in editors if row["version"] == provision.LOCK["unity"]["version"] and row["androidSupport"]), None)
        existing_editor = selected or next((row["path"] for row in editors if row["version"] == provision.LOCK["unity"]["version"]), None)
        if not selected or not (Path(selected).parent / "Data/PlaybackEngines/AndroidPlayer/NDK/source.properties").is_file():
            hub = state["choices"].get("unityHub") or next(iter(hubs), None)
            if not state["choices"]["acceptUnityTerms"]:
                raise WizardError("unity_terms_required", "Review Unity and Android module terms before installation.", "Vor der Installation die Bedingungen von Unity und den Android-Modulen bestätigen.")
            if not hub:
                if os.name != "nt": raise WizardError("windows_required", "Automatic Unity provisioning currently supports Windows.")
                setup = provision.download(provision.LOCK["unityHub"], self.store.root / "tools/downloads/UnityHubSetup-3.22.2-x64.exe",
                                           lambda: self.store.check_cancel(state["session"]))
                try:
                    # Unity documents the installer window, not an unattended
                    # installation/license promise. Its verified setup is started
                    # automatically; the user completes the displayed setup.
                    supervisor.run([setup], self.log(state, "unity"))
                except OSError as error:
                    if getattr(error, "winerror", None) != 740: raise
                    os.startfile(str(setup), "runas")
                    raise WizardError("unity_hub_setup", "Complete the verified Unity Hub setup window, then continue.",
                                      "Das Fenster der geprüften Unity-Hub-Installation abschließen, dann fortsetzen.",
                                      installer=str(setup), requiresUserInteraction=True)
                editors, hubs = discovery.unity_paths(); hub = next(iter(hubs), None)
                if not hub: raise WizardError("unity_hub_setup", "Complete the Unity Hub setup window, then select its installed location.",
                                              "Die Unity-Hub-Installation abschließen und ihren Installationsort wählen.", installer=str(setup))
                raise WizardError("unity_login_required", "Sign in to Unity Hub and activate an eligible license, then continue. The Editor and Android modules will be installed automatically.",
                                  "In Unity Hub anmelden und eine passende Lizenz aktivieren, dann fortsetzen. Editor und Android-Module werden automatisch installiert.",
                                  url=provision.LOCK["unity"]["licenseUrl"])
            help_log = self.store.session_dir(state["session"]) / "logs/unity-hub-help.log"
            supervisor.run([hub, "--", "--headless", "help", "--errors"], help_log)
            supported = help_log.read_text(encoding="utf-8", errors="replace")
            if not re.search(r"\binstall\b", supported) or not re.search(r"\beditors\b", supported):
                raise WizardError("unity_cli_unavailable", "This Hub has no supported archived-Editor CLI. Install Unity2021.3.5f1 and Android modules in its Installs screen, then continue.",
                                  "Dieser Hub bietet keine unterstützte CLI für den archivierten Editor. Unity2021.3.5f1 mit Android-Modulen unter Installationen hinzufügen, dann fortsetzen.",
                                  url=provision.LOCK["unity"]["installDocumentation"])
            install = ["install-modules", "--version", "2021.3.5f1"] if existing_editor else ["install", "--version", "2021.3.5f1", "--changeset", "40eb3a945986"]
            supervisor.run([hub, "--", "--headless", *install, "--module", "android", "--childModules", "--errors"], self.log(state, "unity"))
            editors, _ = discovery.unity_paths()
            selected = next((row["path"] for row in editors if row["version"] == "2021.3.5f1" and row["androidSupport"]), None)
            if not selected: raise WizardError("unity_install_incomplete", "Unity installation has not produced the required Editor and Android modules.", "Die Unity-Installation enthält den benötigten Editor und die Android-Module noch nicht.")
        supervisor.run([selected, "-version"], self.log(state, "unity"))
        if "2021.3.5f1" not in self.log(state, "unity").read_text(encoding="utf-8"):
            raise WizardError("unity_version", "Select Unity2021.3.5f1 with Android support.", "Bitte Unity2021.3.5f1 mit Android-Unterstützung wählen.")
        android = Path(selected).parent / "Data/PlaybackEngines/AndroidPlayer"
        if not (android / "NDK/source.properties").is_file(): raise WizardError("unity_android_required", "Install this Editor's Android Build Support, SDK/NDK and OpenJDK.")
        receipt = self.store.session_dir(state["session"]) / "unity.json"
        atomic_json(receipt, {"schema": 1, "unityEditor": selected, "version": "2021.3.5f1", "licenseVerified": False})
        return [receipt, self.log(state, "unity")], {"unityEditor": selected, "licenseVerified": False}

    def stage_profile(self, state, supervisor):
        selected = state["choices"]; module = discovery.builder(self.details(state, "source")["sourceRoot"])
        logo = self.store.session_dir(state["session"]) / "steam-logo.png"
        if selected.get("steamLogo"):
            import shutil
            shutil.copyfile(selected["steamLogo"], logo)
        else: provision.download(provision.LOCK["steamLogo"], logo, lambda: self.store.check_cancel(state["session"]))
        _, logo_hash = module.read_logo(logo)
        if selected.get("profile"): profile = selected["profile"]
        elif selected["provider"] == "steam":
            root = Path(selected["steamRoot"]) if selected.get("steamRoot") else module.discover_steam_root()
            if not root: raise WizardError("profile_required", "Select the PC account's local display name and identity.", "Bitte Anzeigenamen und lokale Identität des PC-Kontos wählen.")
            profile = module.load_profile.__globals__["capture_steam_profile"](root, selected.get("steamId"))
        else: raise WizardError("profile_required", "GOG/Epic install records do not contain an account identity; enter its name and account ID.", "GOG-/Epic-Installationsdaten enthalten keine Kontoidentität; bitte Namen und Konto-ID eingeben.")
        path = self.store.session_dir(state["session"]) / "profile.json"
        atomic_json(path, profile)
        return [path, logo], {"profilePath": str(path), "steamLogo": str(logo), "logoSha256": logo_hash}

    def arguments(self, state, command):
        root = Path(self.details(state, "source")["sourceRoot"])
        tool = self.details(state, "tools"); profile = self.details(state, "profile")
        argv = [sys.executable, "-B", "-X", "utf8", str(root / "scripts/build-quest.py"), command,
                "--game-root", state["choices"]["gameRoot"], "--repo-root", str(root), "--target", "game",
                "--output-root", str(self.store.root / "build"), "--dotnet", tool["dotnet8"], "--recovery-dotnet", tool["dotnet10"],
                "--profile-json", profile["profilePath"], "--steam-logo", profile["steamLogo"],
                "--unity-editor", self.details(state, "unity")["unityEditor"]]
        if "ownedDlc" in state["choices"]:
            ids = {row[0]: row[3] for row in discovery.builder(root).dlcs.CATALOG}
            ownership = self.store.session_dir(state["session"]) / "dlc.json"
            profile_value = read_json(Path(profile["profilePath"]))
            provider = profile_value["provider"]
            atomic_json(ownership, {"schema": 1, "provider": provider, "appId": 780290,
                                    "steamId" if provider == "steam" else "providerId": profile_value["steamId" if provider == "steam" else "providerId"],
                                    "installedAppIds": sorted(ids[name] for name in state["choices"]["ownedDlc"])})
            argv += ["--dlc-ownership-json", str(ownership)]
        return argv

    def stage_inspect(self, state, supervisor):
        supervisor.run(self.arguments(state, "inspect"), self.log(state, "inspect"), env=provision.environment(self.details(state, "tools")))
        path = self.store.root / "build/latest-input.json"
        # The existing inspect pointer deliberately predates schema-versioned
        # stage receipts. Accept only its exact shape, not arbitrary schema-less
        # JSON; the actual selected manifest is independently schema/hash checked.
        if ordinary(path).stat().st_size > 65536: raise WizardError("manifest_path", "Builder input pointer exceeds supported bounds.")
        value = json.loads(path.read_text(encoding="utf-8"))
        if (not isinstance(value, dict) or set(value) != {"manifest", "sourceGameRoot", "sourceRepo"}
                or any(not isinstance(item, str) for item in value.values())):
            raise WizardError("manifest_path", "Builder input pointer has unsupported fields.")
        raw = value["manifest"]; relative = PurePosixPath(raw)
        if (relative.is_absolute() or PureWindowsPath(raw).drive or "\\" in raw or ":" in raw
                or ".." in relative.parts or not re.fullmatch(r"manifests/[0-9a-f]{64}\.json", raw)):
            raise WizardError("manifest_path", "Builder input pointer escaped its manifest directory.")
        manifest = ordinary(self.store.root / "build" / raw)
        module = discovery.builder(self.details(state, "source")["sourceRoot"])
        if (Path(value["sourceRepo"]).resolve() != Path(self.details(state, "source")["sourceRoot"]).resolve()
                or Path(value["sourceGameRoot"]).resolve() != module.game_data(Path(state["choices"]["gameRoot"])).resolve()):
            raise WizardError("manifest_input", "Builder input pointer belongs to another source or game.")
        actual = read_json(manifest, limit=64 * 1048576)
        if (actual.get("target") != "game" or actual.get("inputKey") != manifest.stem
                or value_hash({key: item for key, item in actual.items() if key != "inputKey"}) != manifest.stem
                or actual["game"]["key"] != value_hash({"files": actual["game"]["files"]})
                or actual["mod"]["key"] != value_hash({"files": actual["mod"]["files"]})):
            raise WizardError("manifest_input", "Builder input manifest identity is inconsistent.")
        return [path, manifest], {"inputKey": actual["inputKey"], "gameKey": actual["game"]["key"], "modBuild": actual["mod"]["modBuild"]}

    def stage_build(self, state, supervisor):
        root = self.store.root / "build"
        if (state["stages"][STAGES.index("build")]["attempts"] > 1 and any(root.glob("projects/*/Library"))
                and getattr(discovery.builder(self.details(state, "source")["sourceRoot"]), "BUILDER_RESUME_CONTRACT", 0) != 1):
            raise WizardError("build_resume_requires_workspace_guard", "The interrupted imported workspace is retained. Its transactional builder-resume integration is required before continuing.",
                              "Der importierte Arbeitsstand bleibt erhalten. Zum Fortsetzen ist die transaktionale Builder-Integration erforderlich.")
        supervisor.run(self.arguments(state, "build"), self.log(state, "build"), env=provision.environment(self.details(state, "tools")))
        module = discovery.builder(self.details(state, "source")["sourceRoot"])
        apk, details = module.verified_latest_build(root)
        latest = read_json(root / "latest-build.json")
        paths = [apk, Path(str(apk) + ".build.json"), root / "latest-build.json", root / latest["receipt"]]
        paths.extend(root / row["path"] for row in details.get("contentFiles", []))
        return paths, {"outputRoot": str(root), "apk": str(apk), "apkSha256": details["apkSha256"], "inputKey": details["inputKey"]}

    def stage_install(self, state, supervisor):
        result = self.store.session_dir(state["session"]) / "installation.json"
        if not state["choices"]["install"]:
            atomic_json(result, {"schema": 1, "requested": False})
            return [result], {"requested": False}
        source = Path(self.details(state, "source")["sourceRoot"])
        config = self.store.root / "device/wireless-install.json"
        supervisor.run([sys.executable, "-B", "-X", "utf8", str(source / "scripts/install-quest-wireless.py"),
                        "--output-root", self.details(state, "build")["outputRoot"], "--config", str(config)], self.log(state, "install"))
        receipt = config.parent / "wireless-last-install.json"
        value = read_json(receipt)
        if (value["apkSha256"] != self.details(state, "build")["apkSha256"]
                or value["inputKey"] != self.details(state, "build")["inputKey"]):
            raise WizardError("installed_identity", "Installed receipt differs from the completed build.")
        atomic_json(result, {"schema": 1, "requested": True, "inputKey": value["inputKey"], "apkSha256": value["apkSha256"], "launched": value["launched"]})
        return [result, receipt], {"requested": True, "hardwareVerified": False}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    for command in ("plan", "status", "run", "cancel", "discover", "serve"):
        part = sub.add_parser(command); part.add_argument("--state-root", type=Path, required=True)
        if command in ("status", "run", "cancel"): part.add_argument("--session", required=True)
        if command == "plan":
            part.add_argument("--choices-file", type=Path, required=True); part.add_argument("--session")
        if command == "serve":
            part.add_argument("--ui-root", type=Path, required=True); part.add_argument("--port", type=int, default=0)
            part.add_argument("--open-browser", action="store_true")
    args = parser.parse_args(argv)
    try:
        store = Store(args.state_root)
        if args.command == "serve":
            from server import serve
            serve(store, args.ui_root, port=args.port, open_browser=args.open_browser); return 0
        if args.command == "plan":
            selected = choices(json.loads(args.choices_file.read_text(encoding="utf-8-sig")))
            state = store.amend(args.session, selected) if args.session else store.create(selected)
            result = {"schema": 1, "event": "planned", "session": state["session"], "state": state}
        elif args.command == "discover": result = discovery.discover(REPO, store)
        elif args.command == "cancel":
            store.cancel(args.session); result = {"schema": 1, "event": "cancel_requested", "session": args.session}
        elif args.command == "run":
            state = Engine(store, emit=lambda event: print(json.dumps({"schema": 1, "event": "progress", **event}), flush=True)).run(args.session)
            result = {"schema": 1, "event": "result", "session": args.session, "state": state}
        else: result = {"schema": 1, "event": "status", "session": args.session, "state": store.load(args.session)}
        print(json.dumps(result, ensure_ascii=False), flush=True)
        return 0 if result.get("state", {}).get("status") not in ("failed", "blocked", "cancelled") else 1
    except (WizardError, OSError, ValueError) as error:
        if not isinstance(error, WizardError): error = WizardError("wizard_error", str(error))
        print(json.dumps({"schema": 1, "event": "error", "code": error.code, "message": error.message, "parameters": error.parameters}), flush=True)
        return 1


if __name__ == "__main__": raise SystemExit(main())
