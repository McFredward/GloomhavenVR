"""Persistent local conversion workflow; CLI and loopback UI use the same engine."""
from __future__ import annotations
import argparse
import copy
import json
import os
from pathlib import Path, PurePosixPath, PureWindowsPath
import re
import secrets
import sys
import time
import zipfile

if __package__ in (None, ""):
    sys.path.insert(0, str(Path(__file__).resolve().parent))

import discovery
import provision
from processes import Supervisor
from state import (Cancelled, STAGES, Store, WizardError, atomic_json, digest,
                   file_lock, ordinary, read_json, value_hash)

REPO = Path(__file__).resolve().parents[2]


def choices(value, *, store=None):
    allowed = {"gameRoot", "provider", "sourceRoot", "sourceCommit", "sourceRef", "unityEditor", "unityHub", "steamRoot", "steamId",
               "profile", "profileName", "ownedDlc", "steamLogo", "install", "acceptUnityTerms", "language", "mode", "baseApk", "signingRoot", "signingMode", "installedGame", "deviceSource"}
    if not isinstance(value, dict) or set(value) - allowed:
        raise WizardError("invalid_choices", "Wizard choices contain unsupported fields.", "Die Auswahl enthält nicht unterstützte Felder.")
    result = dict(value)
    result.setdefault("provider", "steam"); result.setdefault("language", "de")
    result.setdefault("install", True); result.setdefault("acceptUnityTerms", False)
    result.setdefault("mode", "build")
    installed = result.get("installedGame", False)
    if type(installed) is not bool or installed and result["mode"] == "build":
        raise WizardError("invalid_choices", "Installed-game sources are available only for APK updates.",
                          "Installierte Spieldaten können nur für APK-Updates verwendet werden.")
    if not installed and (not isinstance(result.get("gameRoot"), str) or not result["gameRoot"].strip()):
        raise WizardError("game_required", "Select your owned Gloomhaven installation.", "Bitte die eigene Gloomhaven-Installation wählen.")
    if installed:
        # The APK owns the embedded player/DLC identity. Do not silently import
        # stale PC-form overrides when the source selector changes.
        for field in ("gameRoot", "steamRoot", "steamId", "steamLogo", "ownedDlc"):
            result.pop(field, None)
        if result["mode"] != "update-profile": result.pop("profile", None)
    if "deviceSource" in result:
        item = result["deviceSource"]
        if (not installed or not isinstance(item, dict) or set(item) != {"deviceSerial", "versionName"}
                or not isinstance(item["deviceSerial"], str)
                or not re.fullmatch(r"[A-Za-z0-9_.:-]{1,256}", item["deviceSerial"])
                or not isinstance(item["versionName"], str) or len(item["versionName"]) > 256):
            raise WizardError("invalid_choices", "Invalid selected installed-game source.")
    if "signingMode" in result and result["signingMode"] not in ("auto", "original", "new-owner"):
        raise WizardError("invalid_choices", "Unsupported signing mode.")
    if result["mode"] not in ("build", "update-mod", "update-profile"):
        raise WizardError("invalid_choices", "Unsupported update mode.")
    if result["mode"] != "build":
        base = result.get("baseApk")
        if "baseApk" in result and not isinstance(base, str):
            raise WizardError("invalid_path", "Invalid selected APK or package path.")
        if base is None or not base.strip():
            selected = discovery.default_apk(store) if store is not None else None
            if selected is None:
                raise WizardError("base_apk_required", "No completed Quest APK is available in this workspace. Select an existing APK or ZIP package.",
                                  "Im Arbeitsordner ist keine fertige Quest-APK vorhanden. Bitte eine vorhandene APK oder ein ZIP-Paket auswählen.")
            # Freeze this exact completed source in the session. Restarting a
            # run must not silently switch it to a later workspace output.
            result["baseApk"] = selected["path"]
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
    for name in ("gameRoot", "sourceRoot", "unityEditor", "unityHub", "steamRoot", "steamLogo", "baseApk", "signingRoot"):
        if name in result:
            if not isinstance(result[name], str) or not result[name] or "\0" in result[name]: raise WizardError("invalid_path", "Invalid selected path.")
            result[name] = str(Path(result[name]).expanduser().absolute())
    if "profile" in result:
        profile = discovery.builder(REPO).load_profile.__globals__["validate_identity"](result["profile"])
        if profile["provider"] != result["provider"]: raise WizardError("profile_provider", "Profile provider differs from the selected installation.")
        result["profile"] = profile
    if "profileName" in result:
        name = result["profileName"]
        if (result["mode"] != "update-profile" or "profile" in result or not isinstance(name, str)
                or not name.strip() or len(name.encode("utf-8")) > 256
                or any(ord(char) < 32 or ord(char) == 127 for char in name)):
            raise WizardError("profile_name", "Enter a valid player name for the existing APK.",
                              "Einen gültigen Spielernamen für die vorhandene APK eingeben.")
        result["profileName"] = name.strip()
    return result


class Engine:
    def __init__(self, store, repo=REPO, *, actions=None, supervisor_factory=Supervisor, emit=lambda value: None):
        self.store, self.repo, self.actions, self.supervisor_factory, self.emit = store, Path(repo), actions or {}, supervisor_factory, emit

    def plan(self, value, *, session=None):
        """Resolve omitted update inputs before any supervised work starts."""
        selected = choices(value, store=self.store)
        return self.store.amend(session, selected) if session else self.store.create(selected)

    def key(self, state, stage):
        before = STAGES[:STAGES.index(stage)]
        names = {"tools": ("mode",), "source": ("sourceRoot", "sourceCommit", "sourceRef", "gameRoot", "mode"),
                 "unity": ("unityEditor", "unityHub", "acceptUnityTerms", "mode"),
                 "profile": ("provider", "steamRoot", "steamId", "profile", "steamLogo"),
                 "inspect": ("gameRoot", "ownedDlc", "mode", "baseApk"), "build": ("mode", "baseApk", "signingRoot"), "install": ("install",)}[stage]
        # New name-only inputs must not add null fields to ordinary retained
        # profile keys. Capture150551 has six closed owners; an installer/UI
        # repair must keep that exact completion frontier and skip Unity work.
        if state["choices"].get("installedGame"):
            names += ("installedGame", "baseApk", "deviceSource")
        if stage == "profile" and "profileName" in state["choices"]:
            names += ("profileName", "baseApk")
        if stage in ("inspect", "build") and "signingMode" in state["choices"]:
            names += ("signingMode",)
        if stage == "inspect" and "signingRoot" in state["choices"]:
            names += ("signingRoot",)
        release_identity = None
        if stage == "source":
            selected = Path(state["choices"].get("sourceRoot") or self.repo)
            manifest = ordinary(selected / "quest-builder-release.json")
            if manifest.is_file() and not (selected / ".git").exists(): release_identity = digest(manifest)
        base_identity = None
        if ((stage == "inspect" and state["choices"].get("mode", "build") != "build")
                or (stage == "unity" and state["choices"].get("mode") == "update-mod")):
            module = discovery.builder(self.repo)
            apk = ordinary(state["choices"]["baseApk"])
            # A user may replace an APK at the same selected path. File path
            # equality must never authorize reuse of the preceding update.
            base_identity = (module.portable_packages.inspect(apk)["apk"]["sha256"] if apk.suffix.lower() == ".zip"
                             else module.apk_input_hashes.digest_apk(apk, self.store.root / "build"))
        # Adding a Linux host must not invalidate the pinned Windows recipes in
        # already completed sessions. Host-specific tools cannot be reused when
        # a workspace is moved to a different operating system.
        pins = {name: spec for name, spec in provision.LOCK.items() if name != "linux"}
        if os.name != "nt":
            pins.update(provision.LOCK.get("linux", {}))
            pins["host"] = "linux-x64"
        return value_hash({**({"releaseIdentity": release_identity} if release_identity else {}), "choices": {name: state["choices"].get(name) for name in names}, "stage": stage, "pins": value_hash(pins),
                           **({"baseApkSha256": base_identity} if base_identity else {}),
                           **({"prerequisitePolicy": 2} if stage == "unity" else {}),
                           **({"updatePrerequisitePolicy": 1} if stage == "unity" and state["choices"].get("mode") == "update-mod" else {}),
                           "dependencies": {name: state.get("completed", {}).get(name, {}).get("key") for name in before}})

    def selected_apk(self, state, module):
        """Retain the chosen package path while resolving only its cached APK."""
        path = ordinary(state["choices"]["baseApk"])
        if path.suffix.lower() != ".zip": return path
        stage = next((row["id"] for row in state["stages"] if row.get("status") == "running"), "profile")
        return module.portable_packages.import_apk(path, self.store.root / "build",
                    progress=lambda value: self.store.progress(state["session"], stage, "package-import",
                                    value.get("completed"), value.get("total"), "bytes", value.get("detail", "Importing package APK.")),
                    cancel=lambda: self.store.check_cancel(state["session"]))

    def run(self, session):
        with file_lock(self.store.root / "run.lock"):
            state = self.store.load(session)
            with self.store.active(state):
                result = self._run(state, session)
        if not self.actions and result.get("status") == "complete":
            # Publication and installation are already closed. A failed optional
            # housekeeping pass must never change their successful receipts.
            # Running after completion releases this run's old APK input while
            # keeping the latest APK and other pending runs' actual dependencies.
            try:
                from update_storage import maintain
                maintain(self.store.root)
            except (OSError, ValueError, RuntimeError, ImportError):
                pass  # Explicit storage management can retry; outputs stay valid.
        return result

    def build_work_key(self, state):
        """Progress scope, independent of release observer bytes; never a receipt."""
        prior = state.get("completed", {})
        inspected = prior.get("inspect", {}).get("details", {})
        profile = prior.get("profile", {}).get("details", {})
        game_key = inspected.get("gameKey")
        if not game_key or not profile.get("profilePath"):
            return None
        try:
            captured = read_json(ordinary(Path(profile["profilePath"])))
            identity = value_hash({name: captured.get(name) for name in
                                   ("provider", "steamId", "providerId", "accountId", "displayName", "isDummy")})
        except (OSError, ValueError, WizardError):
            return None
        return value_hash({"gameRoot": state["choices"]["gameRoot"], "gameKey": game_key,
                           "profile": identity, "provider": state["choices"]["provider"],
                           "ownedDlc": state["choices"].get("ownedDlc"), "target": "game",
                           "mode": state["choices"].get("mode", "build"),
                           "baseApk": inspected.get("baseApkSha256")})

    def _run(self, state, session):
        if value_hash(state["choices"]) != state["choicesKey"]:
            raise WizardError("choices_changed", "Saved choices changed; create a new session.")
        # Remember the explicitly continued run, including an older selection.
        # Read-only discovery/status never changes this restart preference.
        self.store.remember_session(state)
        installation_candidate = self.installation_resume_candidate(state)
        previous_work_key = None if installation_candidate else self.build_work_key(state)
        delivery_candidate = not installation_candidate and self.delivery_resume_candidate(state)
        retained_build = copy.deepcopy(state["stages"][STAGES.index("build")].get("progressPlan", {})) if delivery_candidate else None
        self.store.clear_cancel(session)
        self.store.begin_run(session)
        supervisor = self.supervisor_factory(self.store, session)
        self.store.event(state, "run_started")
        delivery_resume = None; delivery_error = None
        if delivery_candidate:
            try:
                module = discovery.builder(self.repo)
                selected = module._local_helper("player_delivery").select(
                    module, self.store.root / "build", state["choices"]["gameRoot"],
                    profile_path=self.details(state, "profile")["profilePath"],
                    input_key=self.details(state, "inspect")["inputKey"])
                if selected:
                    delivery_resume = (module, selected)
            except (OSError, ValueError, RuntimeError) as error:
                # A completed Player with inconsistent publication metadata
                # must not silently fall back to another expensive build.
                delivery_error = error
        finished = state.get("completed", {}).get("build", {}).get("details", {})
        install_only = installation_candidate
        if install_only:
            self.emit(self.store.event(state, "installation_resume_started", "install",
                                       inputKey=finished["inputKey"], modBuild=finished.get("modBuild", self.details(state, "inspect").get("modBuild")), earlierStagesChecked=False))
        if install_only: rows = state["stages"][STAGES.index("install"):]
        elif delivery_resume or delivery_error: rows = state["stages"][STAGES.index("build"):]
        else: rows = state["stages"]
        for row in rows:
            stage = row["id"]; key = self.key(state, stage); started = time.monotonic()
            try:
                self.store.check_cancel(session)
                if stage == "build":
                    self.store.begin_stage(session, stage, key, work_key=self.build_work_key(state),
                                           previous_work_key=previous_work_key)
                else:
                    self.store.begin_stage(session, stage, key)
                if stage == "tools" and state["choices"].get("mode", "build") != "build":
                    helper = discovery.builder(self.repo)
                    if not self.actions:
                        self.store.operation(session, "tools", "qualify", detail="Checking update disk reserve before importing the package APK.")
                        self.qualify(state, stage=stage)
                    helper._local_helper("update_driver").preflight(helper, state["choices"].get("gameRoot"), state["choices"]["baseApk"], state["choices"]["mode"],
                        installed_game=state["choices"].get("installedGame", False), output=self.store.root / "build", cancel=lambda: self.store.check_cancel(session),
                        progress=lambda value: self.store.progress(session, "tools", "package-import", value.get("completed"),
                                       value.get("total"), "bytes", value.get("detail", "Importing package APK.")))
                if stage == "tools" and not self.actions:
                    # Capacity takes precedence over network setup, including
                    # reused tools. Do this once before receipts/downloads.
                    self.store.operation(session, "tools", "qualify", detail="Checking available memory, disk space and supported host.")
                    if state["choices"].get("mode", "build") == "build": self.qualify(state, stage=stage)
                    self.store.operation(session, "tools", "qualify", complete=True, detail="Host capacity checked.")
                    self.check_downloads(state)
                if stage == "build" and delivery_error:
                    raise WizardError("delivery_resume_invalid", str(delivery_error),
                                      "Der gebaute Android-Player bleibt erhalten. Seine Abschlussdaten konnten nicht zugeordnet werden; Diagnosepaket speichern.",
                                      completedPlayerRetained=True, builderError=str(delivery_error)) from delivery_error
                prior = None if stage == "build" and delivery_resume else self.store.valid(session, stage, key, report_progress=True)
                if prior:
                    row.update(status="complete", details=prior["details"], durationSeconds=round(time.monotonic() - started, 3))
                    self.store.clear_waiting(session, stage)
                    self.store.progress(session, stage, "complete", 1, 1, "stages", "Verified stage outputs reused.")
                    state.setdefault("completed", {})[stage] = {"key": prior["key"], "details": prior["details"]}
                    if stage == 'tools': self.record_tool_owner(state, key)
                    self.emit(self.store.event(state, "stage_reused", stage, durationSeconds=row["durationSeconds"], outputCount=len(prior["outputs"]))); continue
                row.update(status="running", attempts=row["attempts"] + 1, startedAt=time.time())
                if hasattr(supervisor, "set_stage"): supervisor.set_stage(stage)
                descriptions = {"tools": "Checking host capacity and preparing Git and .NET tools.",
                                "source": "Verifying selected mod source and preparing its compile-time dependencies.",
                                "unity": "Checking Unity Editor, Android modules and licence prerequisites.",
                                "profile": "Preparing the local player identity and Steam logo.",
                                "inspect": "Reading owned game files and the selected mod source manifest.",
                                "build": "Preparing complete game assets, compiling the current mod and Android player.",
                                "install": "Connecting to the Quest and installing the verified APK and content."}
                if state["choices"].get("mode") == "update-mod":
                    descriptions["unity"] = "Checking whether this update needs compilation or only signing tools."
                    descriptions["build"] = "Updating the selected APK; retaining compatible game content and compiled code."
                elif state["choices"].get("mode") == "update-profile":
                    descriptions["build"] = "Replacing the selected player profile and signing the existing APK."
                self.store.progress(session, stage, "starting", detail=("Finishing the retained Android Player; earlier preparation and Unity work are not repeated."
                                                                         if stage == "build" and delivery_resume else descriptions[stage]))
                self.emit(self.store.event(state, "stage_started", stage))
                # A cached tool receipt or time spent in Unity Hub does not
                # reserve the drive. Recheck before inspect can copy the owned
                # inputs and immediately before the asset/player build begins.
                if stage in ("inspect", "build") and not self.actions and not delivery_resume:
                    self.qualify(state, stage=stage)
                if stage == "build" and delivery_resume:
                    self.retain_delivery_progress(state, retained_build)
                    module, selected = delivery_resume
                    self.emit(self.store.event(state, "delivery_resume_started", stage,
                                               inputKey=selected["inputKey"], modBuild=selected["modBuild"],
                                               resumeKind=selected.get("resumeKind"), earlierStagesChecked=False))
                    paths, details = self.stage_finish_build(state, supervisor, module, selected)
                else:
                    action = self.actions.get(stage) or getattr(self, "stage_" + stage)
                    paths, details = action(state, supervisor)
                self.store.check_cancel(session)
                receipt = self.store.publish(session, stage, key, paths, details)
                state.setdefault("completed", {})[stage] = {"key": receipt["key"], "details": details}
                if stage == 'tools': self.record_tool_owner(state, key)
                row.update(status="complete", details=details, durationSeconds=round(time.monotonic() - started, 3))
                self.store.clear_waiting(session, stage)
                self.store.progress(session, stage, "complete", 1, 1, "stages", "Stage outputs verified.")
                self.emit(self.store.event(state, "stage_complete", stage, durationSeconds=row["durationSeconds"], outputCount=len(receipt["outputs"])))
            except (Cancelled, WizardError) as error:
                row["durationSeconds"] = round(time.monotonic() - started, 3)
                row["status"] = "cancelled" if isinstance(error, Cancelled) else "failed" if error.code in ("build_tool_failed", "child_failed", "installation_tool_failed") else "blocked"
                state["status"] = row["status"]
                # Preserve an existing explicit wait and its retry action. A
                # process update/error must never hide a required user action.
                if isinstance(error, Cancelled):
                    self.store.clear_waiting(session, stage); state["needsActions"] = []
                elif not row.get("waiting"):
                    state["needsActions"] = [{"code": error.code, "stage": stage, "message": error.message, "parameters": error.parameters}]
                self.emit(self.store.event(state, error.code, stage, **{**error.parameters, "message": error.message, "durationSeconds": row["durationSeconds"]})); return state
            except (OSError, ValueError, RuntimeError) as error:
                from update_failure_guidance import installed_source_requirement
                requirement = installed_source_requirement(error, failureStage=stage, workflowMode=state["choices"].get("mode")) if state["choices"].get("installedGame") else None
                if requirement is not None:
                    row["status"] = state["status"] = "blocked"
                    row["durationSeconds"] = round(time.monotonic() - started, 3)
                    self.store.clear_waiting(session, stage)
                    state["needsActions"] = [{"code": requirement.code, "stage": stage, "message": requirement.message,
                                              "parameters": requirement.parameters}]
                    self.emit(self.store.event(state, requirement.code, stage, **{**requirement.parameters, "message": requirement.message}))
                    return state
                row["durationSeconds"] = round(time.monotonic() - started, 3)
                from failures import disk_full, disk_full_error
                if disk_full(error):
                    capacity = disk_full_error(self.store.root, stage, error=error)
                    row["status"] = state["status"] = "blocked"
                    self.store.clear_waiting(session, stage)
                    state["needsActions"] = [{"code": capacity.code, "stage": stage,
                                              "message": capacity.message, "parameters": capacity.parameters}]
                    self.emit(self.store.event(state, capacity.code, stage, **{**capacity.parameters,
                                              "message": capacity.message, "durationSeconds": row["durationSeconds"]}))
                    return state
                row["status"] = state["status"] = "failed"
                if not row.get("waiting"):
                    state["needsActions"] = [{"code": "stage_failed", "stage": stage, "message": {"en": str(error), "de": str(error)}}]
                import traceback
                self.emit(self.store.event(state, "stage_failed", stage, error=type(error).__name__, message=str(error), traceback=traceback.format_exc()[-16384:], durationSeconds=row["durationSeconds"])); return state
        state["status"] = "complete"; self.emit(self.store.event(state, "run_complete")); return state

    def installation_resume_candidate(self, state):
        """A completed build needs only installation, including its first pass.

        Capture095538 reaches all final gates and publishes the build receipt.
        A missing authorized Quest cannot invalidate that completed ownership.
        Qualify unchanged session choices and completed dependency keys without
        reopening source/SDK/preparation receipts or multi-gigabyte APK/banks.
        The actual installer still owns its mandatory device/artifact checks.
        """
        if self.actions or state["choices"].get("mode", "build") != "build": return False
        if not self.store.installation_pending(state): return False
        completed = state.get("completed", {})
        if any(completed[stage]["key"] != self.key(state, stage) for stage in ("profile", "inspect", "build")): return False
        details = completed["build"].get("details", {})
        return (isinstance(details.get("apk"), str) and isinstance(details.get("outputRoot"), str)
                and all(isinstance(details.get(name), str) and re.fullmatch(r"[0-9a-f]{64}", details[name])
                        for name in ("inputKey", "apkSha256"))
                and completed["inspect"].get("details", {}).get("inputKey") == details["inputKey"])

    def skip_installation(self, session):
        """Explicitly finish a closed build without resetting any earlier row."""
        with file_lock(self.store.root / "run.lock"):
            state = self.store.load(session)
            if (value_hash(state["choices"]) != state["choicesKey"]
                    or not self.store.installation_pending(state, skip=True)):
                raise WizardError("installation_skip_unavailable",
                    "Only an inactive installation failure after a completed build can be skipped.",
                    "Nur eine angehaltene Installation nach einem fertigen Build kann übersprungen werden.")
            with self.store.active(state):
                # Store.amend intentionally resets every stage. Changing only
                # this final switch keeps completed source/Unity/build owners.
                state["choices"]["install"] = False
                state["choicesKey"] = value_hash(state["choices"])
                self.store.clear_cancel(session)
                key = self.key(state, "install")
                result = self.store.session_dir(session) / "installation.json"
                atomic_json(result, {"schema": 1, "requested": False})
                receipt = self.store.publish(session, "install", key, [result], {"requested": False})
                row = state["stages"][STAGES.index("install")]
                row.update(status="complete", details=receipt["details"], progressKey=key)
                self.store.clear_waiting(session, "install")
                self.store.progress(session, "install", "complete", 1, 1, "stages", "Finished without installation at the user's request.")
                state.setdefault("completed", {})["install"] = {"key": receipt["key"], "details": receipt["details"]}
                state.update(status="complete", needsActions=[])
                state.pop("installationAction", None)
                self.emit(self.store.event(state, "installation_skipped", "install", requested=False))
                self.emit(self.store.event(state, "run_complete"))
                return state

    def installation_policy(self, state):
        """Keep an installation decision outside every build-stage identity.

        The maintainer explicitly prioritizes current and archived saves. The
        Wizard therefore requests saves protection; the standalone installer's
        default remains all data. An explicit stricter choice belongs only to
        this session's completed APK, never a global preference or new build.
        """
        build = state.get("completed", {}).get("build", {})
        details = build.get("details", {})
        binding = {"schema": 1, "session": state["session"], "buildKey": build.get("key"),
                   "inputKey": details.get("inputKey"), "apkSha256": details.get("apkSha256")}
        saved = state.get("installationRecoveryPolicy")
        if saved is not None and (not isinstance(saved, dict) or type(saved.get("schema")) is not int
                or set(saved) != {*binding, "policy"}
                or any(saved.get(name) != value for name, value in binding.items())
                or saved.get("policy") not in ("all", "saves")):
            raise WizardError("installation_policy_invalid",
                "The installation data choice no longer matches this completed APK. Keep the existing app and saves; save diagnostics before retrying.",
                "Die Datenauswahl für die Installation passt nicht mehr zu dieser fertigen APK. Vorhandene App und Spielstände behalten; vor dem erneuten Versuch das Diagnosepaket speichern.")
        return saved or dict(binding, policy="saves")

    def approve_installation_policy(self, session, nonce, policy):
        """Approve one declared compromise for this inactive install attempt."""
        if (not isinstance(nonce, str) or not re.fullmatch(r"[0-9a-f]{32}", nonce)
                or policy != "saves"):
            raise WizardError("installation_action_invalid", "Invalid installation action.", "Ungültige Installationsaktion.")
        with file_lock(self.store.root / "run.lock"):
            state = self.store.load(session)
            action = state.get("installationAction", {})
            current = self.installation_policy(state)
            needed = next((item for item in state.get("needsActions", [])
                           if isinstance(item, dict) and item.get("code") == "quest_signing_data_required"
                           and item.get("stage") == "install"), {})
            if (value_hash(state["choices"]) != state["choicesKey"]
                    or not self.store.installation_pending(state, skip=True)
                    or not isinstance(action, dict)
                    or set(action) != {*current, "nonce", "reason", "attempt"}
                    or any(action.get(name) != value for name, value in current.items())
                    or action.get("nonce") != nonce
                    or action.get("reason") != "non-save-inaccessible" or current["policy"] != "all"
                    or type(action.get("attempt")) is not int or action.get("attempt") != state["stages"][-1].get("attempts")
                    or needed.get("parameters", {}).get("installationActionNonce") != nonce
                    or needed.get("parameters", {}).get("recoveryDataReason") != "non-save-inaccessible"
                    or needed.get("parameters", {}).get("recoveryDataPolicy") != "all"
                    or needed.get("parameters", {}).get("saveDataProtected") is not True):
                raise WizardError("installation_action_stale",
                    "This installation choice is no longer current. Refresh the current run before continuing.",
                    "Diese Installationsauswahl ist nicht mehr aktuell. Vor dem Fortsetzen den aktuellen Lauf neu laden.")
            with self.store.active(state):
                state["installationRecoveryPolicy"] = dict(current, policy="saves")
                state.pop("installationAction", None)
                # Keep the current blocked row until run starts. Removing only
                # its consumed action cannot reset any successful predecessor.
                state["needsActions"] = []
                self.emit(self.store.event(state, "installation_policy_approved", "install",
                                           policy="saves", saveDataProtected=True,
                                           completedBuildRetained=True))
                return state

    def installation_content_approval(self, state):
        """A large content redeployment is consented to one APK/headset transaction."""
        saved = state.get("installationContentReinstallApproval")
        if saved is None: return None
        binding = self.installation_policy(state)
        if (not isinstance(saved, dict) or set(saved) != {*binding, "transaction", "deviceSerial"}
                or any(saved.get(name) != value for name, value in binding.items())
                or not isinstance(saved.get("transaction"), str) or not re.fullmatch(r"[0-9a-f]{32}", saved["transaction"])
                or not isinstance(saved.get("deviceSerial"), str) or not re.fullmatch(r"[A-Za-z0-9_.:-]{1,256}", saved["deviceSerial"])):
            raise WizardError("installation_action_stale", "The content transfer approval no longer matches this APK.",
                              "Die Freigabe der Inhaltsübertragung passt nicht mehr zu dieser APK.")
        return saved

    def approve_content_reinstallation(self, session, nonce):
        if not isinstance(nonce, str) or not re.fullmatch(r"[0-9a-f]{32}", nonce):
            raise WizardError("installation_action_invalid", "Invalid installation action.", "Ungültige Installationsaktion.")
        with file_lock(self.store.root / "run.lock"):
            state = self.store.load(session)
            binding = self.installation_policy(state)
            action = state.get("installationAction", {})
            needed = next((item for item in state.get("needsActions", []) if isinstance(item, dict)
                           and item.get("code") == "quest_signing_content_reinstall_required" and item.get("stage") == "install"), {})
            parameters = needed.get("parameters", {})
            if (value_hash(state["choices"]) != state["choicesKey"]
                    or not self.store.installation_pending(state, skip=True)
                    or not isinstance(action, dict) or set(action) != {*binding, "nonce", "reason", "attempt", "kind", "transaction", "deviceSerial"}
                    or any(action.get(name) != value for name, value in binding.items())
                    or action.get("nonce") != nonce or action.get("kind") != "content-reinstall"
                    or action.get("reason") != "native-rename-unavailable"
                    or type(action.get("attempt")) is not int or action["attempt"] != state["stages"][-1].get("attempts")
                    or not isinstance(action.get("transaction"), str) or not re.fullmatch(r"[0-9a-f]{32}", action["transaction"])
                    or not isinstance(action.get("deviceSerial"), str) or not re.fullmatch(r"[A-Za-z0-9_.:-]{1,256}", action["deviceSerial"])
                    or parameters.get("installationActionNonce") != nonce or parameters.get("requiresContentReinstall") is not True
                    or parameters.get("saveDataProtected") is not True or parameters.get("contentReinstallReason") != action["reason"]
                    or parameters.get("contentReinstallTransaction") != action["transaction"]
                    or parameters.get("contentReinstallDeviceSerial") != action["deviceSerial"]):
                raise WizardError("installation_action_stale", "This content transfer choice is no longer current. Refresh the run.",
                                  "Diese Auswahl zur Inhaltsübertragung ist nicht mehr aktuell. Den Lauf neu laden.")
            with self.store.active(state):
                state["installationContentReinstallApproval"] = dict(binding, transaction=action["transaction"], deviceSerial=action["deviceSerial"])
                state.pop("installationAction", None)
                state["needsActions"] = []
                self.emit(self.store.event(state, "installation_content_reinstall_approved", "install", completedBuildRetained=True))
                return state

    def installation_restore_approval(self, state):
        """Keep recovery consent bound to one completed APK and headset."""
        saved = state.get("installationRestoreApproval")
        if saved is None: return None
        binding = self.installation_policy(state)
        if (not isinstance(saved, dict) or set(saved) != {*binding, "transaction", "deviceSerial", "choice"}
                or any(saved.get(name) != value for name, value in binding.items())
                or saved.get("choice") not in ("manual-launch", "reset-app-data")
                or not isinstance(saved.get("transaction"), str) or not re.fullmatch(r"[0-9a-f]{32}", saved["transaction"])
                or not isinstance(saved.get("deviceSerial"), str) or not re.fullmatch(r"[A-Za-z0-9_.:-]{1,256}", saved["deviceSerial"])):
            raise WizardError("installation_action_stale", "The recovery choice no longer matches this APK. Refresh the run.",
                              "Die Wiederherstellungsauswahl passt nicht mehr zu dieser APK. Den Lauf neu laden.")
        return saved

    def approve_restoration_choice(self, session, nonce, choice):
        """Consume explicit manual-start/reset consent, never an inferred retry.

        The action is issued only after an installed replacement fails its
        storage restoration. None of the source, conversion, Unity or build
        owners change. A reset is never implied by a timeout or a retry.
        """
        if (not isinstance(nonce, str) or not re.fullmatch(r"[0-9a-f]{32}", nonce)
                or choice not in ("manual-launch", "reset-app-data")):
            raise WizardError("installation_action_invalid", "Invalid restoration choice.", "Ungültige Wiederherstellungsauswahl.")
        with file_lock(self.store.root / "run.lock"):
            state = self.store.load(session)
            binding = self.installation_policy(state)
            action = state.get("installationAction", {})
            needed = next((item for item in state.get("needsActions", []) if isinstance(item, dict)
                           and item.get("code") == "quest_signing_repair_wait" and item.get("stage") == "install"), {})
            parameters = needed.get("parameters", {})
            available = "manualLaunchAvailable" if choice == "manual-launch" else "resetAppDataAvailable"
            if (value_hash(state["choices"]) != state["choicesKey"]
                    or not self.store.installation_pending(state, skip=True)
                    or not isinstance(action, dict)
                    or set(action) != {*binding, "nonce", "reason", "attempt", "kind", "transaction", "deviceSerial"}
                    or any(action.get(name) != value for name, value in binding.items())
                    or action.get("nonce") != nonce or action.get("kind") != "restore-choice"
                    or action.get("reason") not in ("restore-app-access", "restore-app-launch-unavailable", "restore-app-storage-unavailable")
                    or type(action.get("attempt")) is not int or action["attempt"] != state["stages"][-1].get("attempts")
                    or not isinstance(action.get("transaction"), str) or not re.fullmatch(r"[0-9a-f]{32}", action["transaction"])
                    or not isinstance(action.get("deviceSerial"), str) or not re.fullmatch(r"[A-Za-z0-9_.:-]{1,256}", action["deviceSerial"])
                    or parameters.get("installationActionNonce") != nonce
                    or parameters.get("requiresRestoreChoice") is not True or parameters.get(available) is not True
                    or parameters.get("saveDataProtected") is not True or parameters.get("repairReason") != action["reason"]
                    or parameters.get("restoreChoiceTransaction") != action["transaction"]
                    or parameters.get("restoreChoiceDeviceSerial") != action["deviceSerial"]):
                raise WizardError("installation_action_stale", "This restoration choice is no longer current. Refresh the run.",
                                  "Diese Wiederherstellungsauswahl ist nicht mehr aktuell. Den Lauf neu laden.")
            with self.store.active(state):
                state["installationRestoreApproval"] = dict(binding, transaction=action["transaction"],
                                                           deviceSerial=action["deviceSerial"], choice=choice)
                state.pop("installationAction", None)
                state["needsActions"] = []
                self.emit(self.store.event(state, "installation_restoration_choice_approved", "install",
                                           choice=choice, completedBuildRetained=True,
                                           hostBackupsRetained=True, resetsDeviceSaves=choice == "reset-app-data"))
                return state

    def delivery_resume_candidate(self, state):
        """Qualify unchanged session choices without checking earlier outputs.

        Capture065619 reaches a completed Android Player before the signing
        helper raises KeyError('zipalign'). Replaying source/SDK/asset receipts
        at that boundary is unnecessary. The Player selector separately owns
        its bounded artifact identity and the continued presence of the game.
        Archive source updates intentionally do not replace this built Player.
        """
        if self.actions or state["choices"].get("mode", "build") != "build": return False
        build = state["stages"][STAGES.index("build")]
        completed = state.get("completed", {})
        retry_states = ("failed", "blocked", "cancelled", "interrupted")
        install_retry = (build.get("status") == "complete" and completed.get("build", {}).get("details", {}).get("resumedCompletedPlayer")
                         and state["stages"][STAGES.index("install")].get("status") in retry_states)
        if (build.get("status") not in retry_states and not install_retry) or not build.get("attempts"): return False
        if any(row.get("status") != "complete" or not completed.get(row["id"], {}).get("key")
               for row in state["stages"][:STAGES.index("build")]): return False
        # These keys contain the original predecessor keys, provider/profile,
        # game path and explicit DLC selection. Neither opens an owned file in
        # build mode, and neither recomputes the changing archive source key.
        if any(completed[stage]["key"] != self.key(state, stage) for stage in ("profile", "inspect")): return False
        return all(completed[stage].get("details", {}).get(field)
                   for stage, field in (("profile", "profilePath"), ("inspect", "inputKey")))

    def retain_delivery_progress(self, state, retained):
        """Close preparation from actual Player success, keeping observed detail."""
        import stage_plan
        row = state["stages"][STAGES.index("build")]
        plan = row.get("progressPlan", {})
        for name in ("unityWork", "unityImports", "unityCompiler", "unityTasks", "contentPack", "packageApi", "unityValidation"):
            if retained and name in retained: plan[name] = copy.deepcopy(retained[name])
        operations = stage_plan.planned_operations(row)
        before = operations[:operations.index("delivery")]
        # The final Player artifact is an aggregate proof of its prerequisite
        # work, not a request to reopen each closed substep for verification.
        for name in before:
            self.store.progress(state["session"], "build", "operation:" + name, 1, 1, "operations",
                                "Retained completed Player proves this earlier operation.", operation=name, status="reuse")
        for work in plan.get("unityWork", {}).values():
            for activities in work.get("activities", {}).values():
                for activity in activities.values(): activity.update(closed=True, status="complete")
        self.store.operation(state["session"], "build", "delivery",
                             detail="Finishing and signing the existing Android Player.")

    def details(self, state, stage): return state["completed"][stage]["details"]
    def log(self, state, stage): return self.store.session_dir(state["session"]) / "logs" / (stage + ".log")

    def record_tool_owner(self, state, key):
        if self.actions or state['choices'].get('mode', 'build') == 'update-profile': return
        import tool_reuse
        tool_reuse.record(self.store, state['session'], key, tool_reuse.recipe('compiler'))

    def check_downloads(self, state):
        import network_preflight
        pending = {stage for stage in STAGES if state.get("completed", {}).get(stage, {}).get("key") != self.key(state, stage)}
        source = Path(state["choices"].get("sourceRoot") or self.repo)
        if "source" not in pending:
            source = Path(self.details(state, "source")["sourceRoot"])
        editor = state["choices"].get("unityEditor") or state.get("completed", {}).get("unity", {}).get("details", {}).get("unityEditor")
        hub = state["choices"].get("unityHub")
        if state["choices"].get("mode", "build") != "update-profile":
            from unity_setup import _editor
            discovered_editor, discovered_hub = _editor(state, self.store.root if os.name != 'nt' else None)
            editor = editor or discovered_editor
            hub = hub or discovered_hub
        try:
            rows = network_preflight.inventory(self.store.root, source, state, pending=pending, editor=editor, hub=hub,
                                               tool_source=self.repo)
        except (KeyError, TypeError, SyntaxError) as error:
            raise WizardError("network_preflight_recipe", "The selected build dependency recipe cannot be read. Save diagnostics; completed conversions are retained.",
                              "Die ausgewählten Build-Abhängigkeiten konnten nicht gelesen werden. Diagnosepaket speichern; fertige Konvertierungen bleiben erhalten.",
                              recipeError=str(error), completedWorkRetained=True) from error
        session = state["session"]; done = [0]; observed = []
        self.emit(self.store.event(state, "network_preflight_started", "tools", dependencies=len(rows)))
        self.store.progress(session, "tools", "network-preflight", 0, len(rows), "dependencies", "Checking required download servers before conversion.")
        def report(row):
            done[0] += 1
            observed.append(row)
            self.emit(self.store.event(state, "network_dependency_checked", "tools", **row))
            status = row['status']
            if status == 'probe-timeout':
                status = ("Kurzprüfung hat das Zeitlimit erreicht; der Download versucht die verfügbaren Server."
                          if state['choices'].get('language', 'de') == 'de' else
                          "Quick check timed out; the downloader will try the available servers.")
            self.store.progress(session, "tools", "network-preflight", done[0], len(rows), "dependencies", row["label"] + ': ' + status)
        try:
            result = network_preflight.check(rows, check_cancel=lambda: self.store.check_cancel(session), report=report)
        except WizardError as error:
            atomic_json(self.store.session_dir(session) / "network-preflight.json",
                        {"schema": 1, "status": error.code, "offlineClosureVerified": False, "results": observed})
            raise
        atomic_json(self.store.session_dir(session) / "network-preflight.json", result)
        self.emit(self.store.event(state, "network_preflight_complete", "tools", checkedDependencies=result["checkedDependencies"], localDependencies=result["localDependencies"], offlineClosureVerified=False))

    @staticmethod
    def remember_storage_game(state, qualification):
        """Keep actual input ownership beyond the bounded progress history.

        This is diagnostic/cleanup provenance, never a build recipe or cache
        reuse authorization. Older runs retained the observed game key only in
        progress events; once those rotated, cleanup missed the new project's
        large intermediate trees. The next existing event saves this small row.
        """
        estimate = qualification.get("spaceEstimate") if isinstance(qualification, dict) else None
        key = estimate.get("cacheGameKey") if isinstance(estimate, dict) else None
        if not isinstance(key, str) or re.fullmatch(r"[0-9a-f]{64}", key) is None:
            return
        prior = state.get("storageProvenance", {})
        rows = prior.get("games", []) if isinstance(prior, dict) and prior.get("schema") == 1 else []
        rows = rows[-256:] if isinstance(rows, list) else []
        keys = [row["gameKey"] for row in rows if isinstance(row, dict)
                and isinstance(row.get("gameKey"), str) and re.fullmatch(r"[0-9a-f]{64}", row["gameKey"])]
        state["storageProvenance"] = {"schema": 1, "games": [{"gameKey": member}
            for member in dict.fromkeys([*keys, key])][-256:]}

    def qualify(self, state, *, stage="tools"):
        from qualification import qualify
        mode = state["choices"].get("mode", "build")
        # An update incompatibility is a decision point, not consent for the
        # full-build route or its disk reserve. Only a newly selected build
        # workflow may reserve and launch original-content preparation.
        try:
            qualification = qualify(self.store.root, game_root=state["choices"].get("gameRoot"), repo=self.repo,
                                    installed_game=state["choices"].get("installedGame", False), mode=mode, base_apk=state["choices"].get("baseApk"))
        except WizardError as error:
            if error.code in ("workspace_build_space_low", "workspace_space_estimate_incomplete"):
                self.remember_storage_game(state, error.parameters)
                # Keep the existing diagnostic token; this warning is now
                # accompanied by the mandatory stop and its actionable cause.
                self.emit(self.store.event(state, "space_estimate_warning", stage,
                                          freeBytes=error.parameters.get("freeBytes"),
                                          estimatedBytes=error.parameters.get("estimatedBytes"),
                                          spaceEstimate=error.parameters.get("spaceEstimate")))
            raise
        self.remember_storage_game(state, qualification)
        self.emit(self.store.event(state, "workspace_space_checked", stage,
                                  workspaceRoot=qualification["workspaceRoot"], freeBytes=qualification["freeBytes"],
                                  requiredFreeBytes=qualification["requiredFreeBytes"],
                                  spaceEstimate=qualification.get("spaceEstimate")))
        return qualification

    def stage_tools(self, state, supervisor):
        # The runner qualifies capacity before receipt reuse and preflight.
        if (not state['choices'].get('installedGame') and os.name != 'nt' and state['choices'].get('provider') == 'steam'
                and state['choices'].get('mode', 'build') != 'update-profile'
                and 'ownedDlc' not in state['choices']):
            # The original Windows Steam DLL cannot be loaded by a Linux host.
            # Ask before provisioning/import, rather than failing hours later.
            raise WizardError('linux_dlc_required',
                              'Select your purchased DLCs in the setup form before building on Linux. An empty selection declares base game only.',
                              'Vor dem Linux-Build die gekauften DLCs in der Auswahl angeben. Eine leere Auswahl bedeutet nur das Basisspiel.')
        if state["choices"].get("mode") == "update-profile":
            return provision.profile_tools(self.store, state["session"], supervisor)
        import tool_reuse
        retained = tool_reuse.lookup(self.store, tool_reuse.recipe('compiler'))
        if retained:
            for name in ('git', 'dotnet8', 'dotnet10'):
                self.store.operation(state['session'], 'tools', 'verify-' + name, complete=True,
                                     detail='Closed pinned ' + name + ' files retained; no download or extraction.')
            self.emit(self.store.event(state, 'compiler_tools_reused', 'tools', outputCount=len(retained[0])))
            return retained
        return provision.tools(self.store, state["session"], supervisor)
    def stage_source(self, state, supervisor):
        if state["choices"].get("mode") == "update-profile":
            self.store.operation(state["session"], "source", "source-verify", detail="Profile edits use the current APK and the local profile writer.", complete=True)
            marker = self.store.session_dir(state["session"]) / "profile-source.json"
            atomic_json(marker, {"schema": 1, "sourceRoot": str(self.repo), "purpose": "Profile-only APK update; no mod compilation."})
            return [marker], {"sourceRoot": str(self.repo)}
        selected = state["choices"]
        if selected.get("installedGame"):
            module = discovery.builder(self.repo)
            _, prepared, _ = module._local_helper("update_driver").preflight(module, None, selected["baseApk"], selected["mode"],
                installed_game=True, output=self.store.root / "build", cancel=lambda: self.store.check_cancel(state["session"]))
            selected = {**selected, "gameRoot": str(prepared)}
        return provision.source_checkout(self.store, state["session"], selected, self.details(state, "tools"), supervisor, self.repo)

    def stage_unity(self, state, supervisor):
        if state["choices"].get("mode") == "update-profile":
            self.store.operation(state["session"], "unity", "prerequisites", detail="This profile edit requires no Unity compiler or licence.", complete=True)
            marker = self.store.session_dir(state["session"]) / "profile-no-unity.json"
            atomic_json(marker, {"schema": 1, "required": False, "reason": "Signed offline profile is updated without Unity."})
            return [marker], {"required": False}
        if state["choices"].get("mode") == "update-mod":
            root = Path(self.details(state, "source")["sourceRoot"])
            module = discovery.builder(root)
            updates = module._local_helper("update_driver")
            self.store.operation(state["session"], "unity", "prerequisites",
                                 detail="Comparing changed mod inputs before preparing the compiler.")
            base = module.apk_updates.inspect_base(self.selected_apk(state, module))
            files, _, _ = module.source_inventory(root)
            requirements = updates.update_requirements(module, root, base, files, module.mod_build(root),
                                                      output=self.store.root / 'build')
            # This comparison admits only provisioning, never APK editing. The
            # normal inspect step independently qualifies ownership/signatures
            # and the actual current source before any output is published.
            if not requirements["requiresUnity"]:
                paths, _ = provision.profile_tools(self.store, state["session"], supervisor, stage="unity")
                marker = self.store.session_dir(state["session"]) / "update-no-unity.json"
                atomic_json(marker, {"schema": 1, "required": False, "sourceKey": value_hash({"files": files}),
                                     "reason": "Runtime code, mod artwork and serialized game producers are unchanged."})
                self.store.operation(state["session"], "unity", "prerequisites", complete=True,
                                     detail="Existing compiled code and game contents retained; signing tools prepared.")
                return [marker, *paths], {"required": False, "apkToolsPath": str(self.store.session_dir(state["session"]) / "apk-tools.json")}
        from unity_setup import prepare
        return prepare(self.store, state, supervisor)

    def profile_launcher_required(self, state, cause):
        from store_launcher import launch, required
        provider = state["choices"]["provider"]
        try:
            launch(self.repo, provider)
            self.emit(self.store.event(state, "store_launcher_opened", "profile", provider=provider))
        except WizardError as error:
            cause = str(cause) + " " + error.message["en"]
        # Opening a client cannot manufacture its account ID. Keep the profile
        # boundary blocked until the original local identity can be read or the
        # user supplies it. GOG offline manifests do not require Galaxy login.
        raise required(provider, cause=cause, failureStage="profile")

    def stage_profile(self, state, supervisor):
        selected = state["choices"]; module = discovery.builder(self.details(state, "source")["sourceRoot"])
        if selected.get("installedGame"):
            # No launcher or profile/logo download: the updater retains the
            # bounded embedded identity, logo and entitlement entries itself.
            marker = self.store.session_dir(state["session"]) / "installed-profile.json"
            details = {"retainedFromApk": True}
            if selected.get("profileName"):
                core = module.apk_updates
                base_apk = self.selected_apk(state, module)
                with zipfile.ZipFile(base_apk) as apk:
                    profile = core._json(apk, core.PROFILE)
                profile = {**profile, "displayName": selected["profileName"]}
                core.profile_update(base_apk, profile)
                atomic_json(marker, profile)
                details["profilePath"] = str(marker)
            else:
                atomic_json(marker, {"schema": 1, "retainedFromApk": selected["baseApk"]})
            self.store.operation(state["session"], "profile", "identity", complete=True,
                                 detail="Keeping the existing APK profile, logo and DLC ownership.")
            return [marker], details
        logo = self.store.session_dir(state["session"]) / "steam-logo.png"
        if selected.get("steamLogo"):
            import shutil
            shutil.copyfile(selected["steamLogo"], logo)
        else: provision.download(provision.LOCK["steamLogo"], logo, lambda: self.store.check_cancel(state["session"]),
                                 progress=lambda done, total: self.store.progress(state["session"], "profile", "steam-logo-download", done, total, "bytes", "Steam logo"))
        _, logo_hash = module.read_logo(logo)
        self.store.operation(state["session"], "profile", "identity", detail="Reading the selected local PC account identity.")
        if selected.get("profile"): profile = selected["profile"]
        elif selected.get("profileName"):
            # Name-only edits preserve the full existing identity. Reading the
            # bounded APK profile does not authorize an update: the normal
            # owned-PC installation qualification/preflight remains mandatory.
            core = module.apk_updates
            try:
                base_apk = self.selected_apk(state, module)
                with zipfile.ZipFile(base_apk) as apk:
                    profile = core._json(apk, core.PROFILE)
                if profile.get("provider") != selected["provider"]:
                    raise ValueError("Selected provider differs from the existing APK profile.")
                profile = {**profile, "displayName": selected["profileName"]}
                # Reuse the updater's complete identity/ownership validation;
                # no new identity or DLC entitlement is inferred from a name.
                core.profile_update(base_apk, profile)
            except (ValueError, OSError, RuntimeError, zipfile.BadZipFile) as error:
                raise WizardError("profile_name", "The existing APK profile could not be edited: " + str(error),
                                  "Das Profil der vorhandenen APK konnte nicht geändert werden: " + str(error)) from error
        elif selected["provider"] == "steam":
            root = Path(selected["steamRoot"]) if selected.get("steamRoot") else module.discover_steam_root()
            if not root: self.profile_launcher_required(state, "The local Steam account identity is unavailable.")
            try:
                profile = module.load_profile.__globals__["capture_steam_profile"](root, selected.get("steamId"))
            except (ValueError, OSError) as error:
                self.profile_launcher_required(state, str(error))
        else:
            self.profile_launcher_required(state, "GOG/Epic installation markers do not contain an account identity; enter its name and account ID.")
        path = self.store.session_dir(state["session"]) / "profile.json"
        atomic_json(path, profile)
        self.store.operation(state["session"], "profile", "identity", complete=True, detail="Local identity recorded.")
        return [path, logo], {"profilePath": str(path), "steamLogo": str(logo), "logoSha256": logo_hash}

    def arguments(self, state, command):
        root = Path(self.details(state, "source")["sourceRoot"])
        tool = self.details(state, "tools"); profile = self.details(state, "profile")
        mode = state["choices"].get("mode", "build")
        if mode != "build": command = "inspect-update" if command == "inspect" else mode
        argv = [sys.executable, "-B", "-X", "utf8", str(root / "scripts/build-quest.py"), command,
                "--repo-root", str(root), "--target", "game", "--output-root", str(self.store.root / "build")]
        if state["choices"].get("installedGame"):
            argv += ["--installed-game"]
            if profile.get("profilePath"): argv += ["--profile-json", profile["profilePath"]]
        else:
            argv += ["--game-root", state["choices"]["gameRoot"], "--profile-json", profile["profilePath"],
                     "--steam-logo", profile["steamLogo"]]
        if mode == "update-profile" or self.details(state, "unity").get("required") is False:
            argv += ["--apk-tools-json", str(self.store.session_dir(state["session"]) / "apk-tools.json")]
        else:
            argv += ["--dotnet", tool["dotnet8"], "--recovery-dotnet", tool["dotnet10"],
                     "--unity-editor", self.details(state, "unity")["unityEditor"]]
        if mode != "build": argv += ["--base-apk", state["choices"]["baseApk"], "--update-kind", mode,
                                    "--signing-mode", state["choices"].get("signingMode", "auto")]
        if state["choices"].get("signingRoot"): argv += ["--signing-root", state["choices"]["signingRoot"]]
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
        if state["choices"].get("mode", "build") != "build":
            root = self.store.root / "build"
            pointer = root / "latest-update-input.json"
            selected = read_json(pointer)
            if not re.fullmatch(r"updates/[0-9a-f]{64}/plan\.json", str(selected.get("plan", ""))):
                raise WizardError("update_input", "Update input pointer is invalid.")
            path = root / selected["plan"]; plan = read_json(path, limit=64 * 1048576)
            if plan.get("schema") != 1 or plan["updateKey"] != path.parent.name:
                raise WizardError("update_input", "Update plan identity is invalid.")
            mode = state["choices"].get("mode")
            strategy = plan.get("updateStrategy", "profile-only" if mode == "update-profile" else "compact-code")
            requires_unity = plan.get("requiresUnity", mode == "update-mod")
            reasons = plan.get("rebuildReasons", [])
            if (plan.get("mode", mode) != mode or strategy not in ("profile-only", "compact-code", "cached-full-build")
                    or type(requires_unity) is not bool
                    or (mode == "update-profile" and requires_unity)
                    or (strategy == "cached-full-build" and not requires_unity)
                    or (mode == "update-profile") != (strategy == "profile-only")
                    or not isinstance(reasons, list) or len(reasons) > 16
                    or any(not isinstance(item, str) or len(item) > 4096 for item in reasons)):
                raise WizardError("update_input", "Update compatibility strategy is invalid.")
            if strategy == "cached-full-build":
                from update_failure_guidance import rebuild_decision
                raise rebuild_decision("; ".join(reasons) or "The selected inputs do not qualify for a compact update.", stage="inspect-update")
            if requires_unity and not self.details(state, "unity").get("unityEditor"):
                raise WizardError("update_unity_required", "The current inputs now require Unity. Retry to prepare the compiler; retained game contents are preserved.",
                                  "Die aktuellen Eingaben benötigen jetzt Unity. Erneut versuchen, um den Compiler vorzubereiten; vorhandene Spielinhalte bleiben erhalten.")
            return [pointer, path], {"inputKey": plan["gameInputKey"], "gameKey": plan["inputs"]["game"]["key"],
                                    "baseApkSha256": plan["baseApkSha256"], "modBuild": plan["inputs"]["mod"]["modBuild"],
                                    "updateStrategy": strategy, "requiresUnity": requires_unity, "rebuildReasons": reasons}
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
        inspected = self.details(state, "inspect")
        if state["choices"].get("mode") == "update-mod":
            self.store.progress(state["session"], "build", "update-compact-qualified",
                                detail="Update remains selected; original-content rebuilding requires an explicit separate build choice.", status="progress")
        if state["choices"].get("mode") == "update-mod" and inspected.get("updateStrategy") == "cached-full-build":
            # Older archives persisted a fallback decision as inspect success.
            # Reusing that receipt must not silently authorize a game rebuild.
            from update_failure_guidance import rebuild_decision
            raise rebuild_decision("; ".join(inspected.get("rebuildReasons", [])) or "A retained update plan requests a full build.")
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

    def stage_finish_build(self, state, supervisor, module, selected):
        root = self.store.root / "build"
        argv = [sys.executable, "-B", "-X", "utf8", str(self.repo / "scripts/build-quest.py"), "finish-build",
                "--game-root", state["choices"]["gameRoot"], "--repo-root", str(self.repo),
                "--output-root", str(root), "--profile-json", self.details(state, "profile")["profilePath"],
                "--delivery-input-key", selected["inputKey"]]
        if state["choices"].get("signingRoot"): argv += ["--signing-root", state["choices"]["signingRoot"]]
        supervisor.run(argv, self.log(state, "build"))
        apk, details = module.verified_latest_build(root)
        if details["inputKey"] != selected["inputKey"]:
            raise WizardError("delivery_identity_changed", "The finished APK differs from the retained Player input.")
        latest = read_json(root / "latest-build.json")
        # The Builder receipt owns the verified multi-gigabyte content banks.
        # Do not hash them a second time merely to duplicate that ownership in
        # the Wizard's receipt. Installation still uses the Builder validator.
        paths = [apk, Path(str(apk) + ".build.json"), root / "latest-build.json", root / latest["receipt"]]
        return paths, {"outputRoot": str(root), "apk": str(apk), "apkSha256": details["apkSha256"],
                       "inputKey": details["inputKey"], "modBuild": selected["modBuild"],
                       "resumedCompletedPlayer": True}

    def stage_install(self, state, supervisor):
        result = self.store.session_dir(state["session"]) / "installation.json"
        if not state["choices"]["install"]:
            atomic_json(result, {"schema": 1, "requested": False})
            return [result], {"requested": False}
        # Installation is a host action owned by the running Builder, including
        # a first-pass Player retained across a newer archive's repair.
        source = self.repo
        config = self.store.root / "device/wireless-install.json"
        policy = self.installation_policy(state)
        content_approval = self.installation_content_approval(state)
        restore_approval = self.installation_restore_approval(state)
        state["installationRecoveryPolicy"] = policy
        self.store.save(state)
        self.emit(self.store.event(state, "installation_guidance", "install", automaticReconnect=True, message={
            "en": "The APK is ready. Unlock the Quest, enable developer mode, connect it with a USB data cable and accept USB debugging in the headset. An authorized USB Quest is used directly. Without USB, an already authorized Wi-Fi connection is reused. Keep the Quest awake during installation.",
            "de": "Die APK ist fertig. Quest entsperren, Entwicklermodus aktivieren, per USB-Datenkabel anschließen und USB-Debugging in der Brille bestätigen. Eine freigegebene USB-Quest wird direkt verwendet. Ohne USB wird eine bereits freigegebene WLAN-Verbindung wiederverwendet. Quest während der Installation eingeschaltet lassen."}))
        self.store.operation(state["session"], "install", "connect", detail="APK ready. Checking an authorized USB Quest before remembered Wi-Fi; keep the Quest unlocked and accept USB debugging.")
        try:
            arguments = [sys.executable, "-B", "-X", "utf8", str(source / "scripts/install-quest-wireless.py"),
                            "--output-root", self.details(state, "build")["outputRoot"], "--config", str(config),
                            "--recovery-data-policy", policy["policy"], "--wait-for-connection"]
            import network_preflight
            editor = (state["choices"].get("unityEditor")
                      or state.get("completed", {}).get("unity", {}).get("details", {}).get("unityEditor"))
            adb = network_preflight.local_adb(self.store.root, source, editor=editor)
            if adb is not None: arguments += ["--adb", str(adb)]
            if content_approval: arguments += ["--allow-content-reinstall", content_approval["transaction"]]
            if restore_approval:
                switch = "--manual-storage-launch" if restore_approval["choice"] == "manual-launch" else "--reset-app-data"
                arguments += [switch, restore_approval["transaction"], "--recovery-device", restore_approval["deviceSerial"]]
            supervisor.run(arguments, self.log(state, "install"))
        except WizardError as error:
            if error.code == "quest_signing_repair_wait" and error.parameters.get("requiresRestoreChoice") is True:
                action = dict(policy, nonce=secrets.token_hex(16), kind="restore-choice",
                              reason=error.parameters["repairReason"],
                              transaction=error.parameters["restoreChoiceTransaction"],
                              deviceSerial=error.parameters["restoreChoiceDeviceSerial"],
                              attempt=state["stages"][-1]["attempts"])
                state["installationAction"] = action
                error.parameters["installationActionNonce"] = action["nonce"]
            if error.code == "quest_signing_content_reinstall_required":
                action = dict(policy, nonce=secrets.token_hex(16), kind="content-reinstall",
                              reason=error.parameters["contentReinstallReason"],
                              transaction=error.parameters["contentReinstallTransaction"],
                              deviceSerial=error.parameters["contentReinstallDeviceSerial"],
                              attempt=state["stages"][-1]["attempts"])
                state["installationAction"] = action
                error.parameters["installationActionNonce"] = action["nonce"]
            if error.code == "quest_signing_data_required":
                if error.parameters.get("recoveryDataPolicy") != policy["policy"]:
                    raise WizardError("installation_action_invalid",
                        "The installer action differs from this run's data choice. Existing saves and the completed APK stay retained.",
                        "Die Installeraktion passt nicht zur Datenauswahl dieses Laufs. Vorhandene Spielstände und die fertige APK bleiben erhalten.") from error
                action = dict(policy, nonce=secrets.token_hex(16),
                              reason=error.parameters["recoveryDataReason"], attempt=state["stages"][-1]["attempts"])
                state["installationAction"] = action
                error.parameters["installationActionNonce"] = action["nonce"]
            raise
        receipt = config.parent / "wireless-last-install.json"
        value = read_json(receipt)
        if (value["apkSha256"] != self.details(state, "build")["apkSha256"]
                or value["inputKey"] != self.details(state, "build")["inputKey"]):
            raise WizardError("installed_identity", "Installed receipt differs from the completed build.")
        self.store.operation(state["session"], "install", "launch", complete=True, detail="Installed build identity and launch receipt confirmed.")
        recovery = value.get("signingRecovery", {})
        reset = isinstance(recovery, dict) and recovery.get("saveDataReset") is True
        atomic_json(result, {"schema": 1, "requested": True, "inputKey": value["inputKey"], "apkSha256": value["apkSha256"],
                             "launched": value["launched"], **({"saveDataReset": True, "hostBackupsRetained": True} if reset else {})})
        return [result, receipt], {"requested": True, "hardwareVerified": False,
                                 **({"saveDataReset": True, "hostBackupsRetained": True} if reset else {})}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    for command in ("plan", "status", "run", "cancel", "discover", "serve", "support"):
        part = sub.add_parser(command); part.add_argument("--state-root", type=Path, required=True)
        if command in ("status", "run", "cancel", "support"): part.add_argument("--session", required=True)
        if command == "support": part.add_argument("--output", type=Path)
        if command == "plan":
            part.add_argument("--choices-file", type=Path, required=True); part.add_argument("--session")
        if command == "serve":
            part.add_argument("--ui-root", type=Path, required=True); part.add_argument("--port", type=int, default=0)
            part.add_argument("--open-browser", action="store_true")
    args = parser.parse_args(argv)
    try:
        store = Store(args.state_root)
        if args.command == "support":
            support = discovery.local_support_module(REPO, "support")
            result = support.export_support(store.root, args.session, args.output)
        elif args.command == "serve":
            from server import serve
            serve(store, args.ui_root, port=args.port, open_browser=args.open_browser); return 0
        elif args.command == "plan":
            state = Engine(store).plan(json.loads(args.choices_file.read_text(encoding="utf-8-sig")), session=args.session)
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
    except (WizardError, OSError, ValueError, RuntimeError) as error:
        if not isinstance(error, WizardError): error = WizardError("wizard_error", str(error))
        print(json.dumps({"schema": 1, "event": "error", "code": error.code, "message": error.message, "parameters": error.parameters}), flush=True)
        return 1


if __name__ == "__main__": raise SystemExit(main())
