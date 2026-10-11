"""Actual supervised install decision/HTTP fixture; no APK or Unity build."""
import argparse
import copy
import hashlib
import json
from pathlib import Path
import sys

parser = argparse.ArgumentParser()
parser.add_argument("repo", type=Path)
parser.add_argument("root", type=Path)
parser.add_argument("--protected", action="store_true")
parser.add_argument("--child-policy", choices=("all", "saves"))
args = parser.parse_args()
sys.path.insert(0, str(args.repo / "tools/quest-wizard"))
from processes import Supervisor
from server import LocalServer
from state import STAGES, Store, WizardError, atomic_json
from wizard import Engine, choices

store = Store(args.root / "owned")
input_key = "a" * 64
apk = store.root / "build/builds/player/complete.apk"
apk_sha = hashlib.sha256(b"retained completed player").hexdigest()
backup = store.root / "device/retained-save-history.tar"

if args.child_policy:
    policy = args.child_policy
    if args.protected or policy == "all":
        reason = "save-data-inaccessible" if args.protected else "non-save-inaccessible"
        print("GHVRQ_ACTION " + json.dumps({"schema": 1, "kind": "signing-data-policy", "reason": reason,
                                           "policy": policy, "saveDataProtected": True}), flush=True)
        print("Quest installation stopped: Signing migration could not safely continue: "
              + ("protected save folder" if args.protected else "non-save diagnostic") + " is inaccessible. "
              "Original recovery data is retained at " + str(backup.parent)
              + ". Retry with the same APK and headset.", flush=True)
        print("ADB log: fixture-current.log", flush=True)
        raise SystemExit(1)
    atomic_json(store.root / "device/wireless-last-install.json",
                {"schema": 1, "inputKey": input_key, "apkSha256": apk_sha, "launched": True})
    raise SystemExit(0)

apk.parent.mkdir(parents=True); apk.write_bytes(b"retained completed player")
backup.parent.mkdir(parents=True); backup.write_bytes(b"current and archived save history")
saved = store.create(choices({"gameRoot": str(args.root / "Fixture game"), "provider": "gog",
                              "language": "de", "acceptUnityTerms": True, "install": True}))
session = saved["session"]
profile = store.session_dir(session) / "profile.json"
atomic_json(profile, {"schema": 1, "provider": "gog", "displayName": "Fixture player", "providerId": "fixture"})


class InstallationSupervisor(Supervisor):
    def run(self, argv, log, **options):
        if len(argv) < 5 or Path(argv[4]).name != "install-quest-wireless.py":
            raise WizardError("fixture_earlier_work", "Earlier build work must not execute in an installation-only fixture.")
        policy = argv[argv.index("--recovery-data-policy") + 1]
        command = [sys.executable, "-I", "-B", str(Path(__file__).resolve()), str(args.repo), str(args.root),
                   "--child-policy", policy, *(["--protected"] if args.protected else [])]
        return super().run(command, log, **options)


class InstallationEngine(Engine):
    def __init__(self, value): super().__init__(value, args.repo, supervisor_factory=InstallationSupervisor)

    def build_work_key(self, _): raise WizardError("fixture_earlier_work", "Closed build must not reopen profile work.")

    def run(self, value):
        if not self.installation_resume_candidate(self.store.load(value)):
            raise WizardError("fixture_earlier_work", "The installation fixture cannot start any earlier build work.")
        return super().run(value)


engine = InstallationEngine(store)
details = {"tools": {"fixture": True}, "source": {"sourceRoot": str(args.repo)},
           "unity": {"unityEditor": "not-used"}, "profile": {"profilePath": str(profile)},
           "inspect": {"inputKey": input_key, "gameKey": "b" * 64, "modBuild": 665},
           "build": {"inputKey": input_key, "apkSha256": apk_sha, "modBuild": 665,
                     "apk": str(apk), "outputRoot": str(store.root / "build")}}
for row in saved["stages"][:-1]:
    stage = row["id"]; key = engine.key(saved, stage)
    row.update(status="complete", attempts=1, details=details[stage], progressKey=key)
    saved.setdefault("completed", {})[stage] = {"key": key, "details": copy.deepcopy(details[stage])}
    output = store.session_dir(session) / (stage + ".output"); output.write_text(stage)
    store.publish(session, stage, key, [output], details[stage])
saved["stages"][-1].update(status="failed", attempts=1)
saved.update(status="failed", needsActions=[{"code": "child_failed", "stage": "install"}])
if not args.protected: saved["installationRecoveryPolicy"] = dict(engine.installation_policy(saved), policy="all")
store.save(saved)
initial = engine.run(session)
assert initial["status"] == "blocked"
initial_receipts = {stage: store.receipt(session, stage).read_bytes() for stage in STAGES[:-1]}
atomic_json(args.root / "fixture-witness.json", {"session": session, "apkSha256": apk_sha,
            "receiptSha256": {stage: hashlib.sha256(raw).hexdigest() for stage, raw in initial_receipts.items()},
            "backupSha256": hashlib.sha256(backup.read_bytes()).hexdigest()})


def discover(*_):
    return {"schema": 1, "event": "discovery", "latestSession": session, "games": [], "unityEditors": [],
            "capabilities": {"browse": False, "logs": True, "support": False}}


http = LocalServer(store, args.repo / "tools/quest-wizard-ui", discover=discover, engine_factory=InstallationEngine)
print(json.dumps({"url": http.url, "session": session}), flush=True)
try: http.serve_forever()
except KeyboardInterrupt: pass
finally: http.close_owned()
