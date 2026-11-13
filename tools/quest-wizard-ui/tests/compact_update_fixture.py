"""Compact compiler observations projected through the real Wizard planner."""
import copy
import json
from pathlib import Path
import sys
import tempfile

sys.path.insert(0, str(Path(sys.argv[1]) / "tools/quest-wizard"))
import state
from processes import ProgressParser, Supervisor

state.PROGRESS_INTERVAL = 0
with tempfile.TemporaryDirectory() as temporary:
    store = state.Store(Path(temporary) / "owned")
    session = store.create({"mode": "update-mod", "gameRoot": "owned"})["session"]
    store.begin_stage(session, "build", "retained-b672")
    supervisor = Supervisor(store, session); supervisor.set_stage("build")
    parser = ProgressParser()
    snapshots = {}

    def event(phase, done=None, total=None, operation=None, status="progress", detail="Actual counted task"):
        fields = dict(schema=1, phase=phase, done=done, total=total, unit="tasks", status=status, detail=detail)
        if operation: fields["operation"] = operation
        supervisor._publish_progress(parser.parse("GHVRQ_PROGRESS " + json.dumps(fields), "build.log"))

    def snapshot(name): snapshots[name] = copy.deepcopy(store.load(session)["stages"][5])

    for name in ("update-owned-game", "update-mod-source"):
        event("operation:" + name, 1, 1, name, "complete")
    event("operation:update-code", operation="update-code", status="start")
    for name in ("weave", "standalone", "update-sdk", "update-package-api"):
        event("stage:" + name, status="start")
        event("stage:" + name, 1, 1, status="complete")
    event("stage:update-code", status="start")
    event("update-code-catalog", 0, 128, "update-code")
    event("update-code-catalog", 128, 128, "update-code", "complete")
    event("update-code-roster", 64, 128, "update-code", detail="Matched original serialized types: 64 / 128")
    snapshot("roster")
    event("unity-refresh", operation="update-code", detail="Asset Pipeline Refresh: Total: 14.672 seconds")
    snapshot("refresh")
    for name in ("roster", "orders", "preload", "handover"):
        event("update-code-" + name, 128, 128, "update-code", "complete")
    snapshot("handover")
    event("operation:update-code", 1, 1, "update-code", "complete")
    event("operation:update-art", operation="update-art", status="start")
    snapshot("code-complete")
    parser.close_coverage()
    print(json.dumps(snapshots))
