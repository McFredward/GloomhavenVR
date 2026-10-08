"""Real Store/Engine/HTTP retry witness with owned pause files; no build tools."""
import json
from pathlib import Path
import sys
import time

repo, root = map(Path, sys.argv[1:3])
sys.path.insert(0, str(repo / "tools/quest-wizard"))
import stage_plan
from state import Store, STAGES, WizardError
from wizard import Engine, choices
from server import LocalServer


def hold(session, attempt, phase):
    (root / (phase + "-" + str(attempt) + ".waiting")).write_text("waiting")
    release = root / (phase + "-" + str(attempt) + ".continue")
    while not release.exists():
        store.check_cancel(session)
        time.sleep(.02)


class ControlledStore(Store):
    def valid(self, session, stage, key, **options):
        saved = self._state(session)
        previous = saved["stages"][5]["attempts"]
        if stage == "tools" and options.get("report_progress") and previous:
            hold(session, previous + 1, "prerequisites")
        return super().valid(session, stage, key, **options)


store = ControlledStore(root / "owned")
saved = store.create(choices({"gameRoot": str(root / "Fixture game"), "language": "de", "acceptUnityTerms": True, "install": False}))


def action(name):
    def run(saved, supervisor):
        session = saved["session"]
        if name == "build":
            attempt = saved["stages"][5]["attempts"]
            store.operation(session, name, "recovery")
            if attempt > 1: hold(session, attempt, "recovery")
            store.progress(session, name, "recovery-plan", 16, 16, "batches", status="reuse")
            for section in stage_plan.RECOVERY_SECTIONS[:-1]:
                store.progress(session, name, "recovery-section:" + section, 1, 1, "sections", status="reuse")
                if section == "batches": store.progress(session, name, "recovery-batches", 16, 16, "batches", status="reuse")
            store.progress(session, name, "recovery-section:staging", status="start")
            store.progress(session, name, "staging-section:catalog", 1, 1, "steps", status="complete" if attempt == 1 else "reuse")
            store.progress(session, name, "staging-section:canonical", 0, 1, "steps", status="start")
            if attempt == 1:
                raise WizardError("build_tool_failed", "Initial canonical fixture failure", "Erster Canonical-Testfehler.", failureStage="recovery", cause="Fixture original canonical failure")
            store.progress(session, name, "staging-section:canonical", 1, 1, "steps", status="reuse")
            store.progress(session, name, "staging-section:copy", 0, 1, "steps", status="start")
            store.progress(session, name, "staging-copy", 10, 100, "files", "current attempt copy item")
            hold(session, attempt, "copy")
            if attempt == 2:
                raise WizardError("build_tool_failed", "New copy fixture failure", "Neuer Kopier-Testfehler.", failureStage="recovery", cause="Fixture current copy failure")
        output = store.session_dir(session) / (name + ".txt")
        output.write_text(name)
        return [output], {}
    return run


actions = {name: action(name) for name in STAGES}
Engine(store, actions=actions).run(saved["session"])


def discover(*_):
    return {"schema": 1, "event": "discovery", "latestSession": saved["session"], "games": [], "unityEditors": [],
            "capabilities": {"browse": False, "logs": True, "support": False}}


server = LocalServer(store, repo / "tools/quest-wizard-ui", discover=discover,
                     engine_factory=lambda value: Engine(value, actions=actions))
print(json.dumps({"url": server.url, "session": saved["session"]}), flush=True)
try: server.serve_forever()
except KeyboardInterrupt: pass
finally: server.close_owned()
