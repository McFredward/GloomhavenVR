"""New strategy observations through real parser/planner, without running Unity."""
import copy
import json
from pathlib import Path
import sys

sys.path.insert(0, str(Path(sys.argv[1]).resolve() / "tools/quest-wizard"))
from processes import ProgressParser
import stage_plan
from state import stage_progress

row = {"id": "build", "status": "running"}
parser = ProgressParser()


def event(value):
    fields = parser.parse("GHVRQ_PROGRESS " + json.dumps(dict(schema=1, **value)), "build.log")
    memory, operation, status = (fields.pop(name, None) for name in ("nativeMemory", "operation", "status"))
    progress = stage_progress(**fields)
    if memory: progress["nativeMemory"] = memory
    row["progress"] = stage_plan.advance(row, progress, operation, status)


event({"phase": "operation:weave", "operation": "weave", "status": "complete"})
event({"phase": "operation:player", "operation": "player", "status": "start"})
fields = parser.parse("[6/10 0s] Clang retained.cpp", "unity-build.log")
operation, status = (fields.pop(name, None) for name in ("operation", "status"))
row["progress"] = stage_plan.advance(row, stage_progress(**fields), operation, status)
row["timing"] = {"schema": 1, "elapsedSeconds": 300, "elapsedBasis": "run",
                 "estimate": {"status": "estimated", "scope": "stage", "lowerSeconds": 120, "upperSeconds": 180}}
states = {}
# Capacity values are the supplied 165405 capture, not a Windows compiler run.
memory = {"compilerProfile": "release-line-tables", "jobs": 1, "attempt": 2, "reason": "pressure",
          "resources": {"availableMemoryBytes": 38776823808, "commitHeadroomBytes": 29501243392,
                        "processWorkingSetBytes": 63471616, "processPrivateCommitBytes": 53080064}}
for name, phase, status in (("retry", "native-memory-retry", "start"),
                            ("wait", "native-memory-wait", "progress"),
                            ("resumed", "native-compiler-profile", "progress")):
    event({"phase": phase, "status": status, "nativeMemory": memory})
    states[name] = copy.deepcopy(row)
fields = parser.parse("[8/10 0s] Clang actual-next.cpp", "unity-build.log")
operation, status = (fields.pop(name, None) for name in ("operation", "status"))
row["progress"] = stage_plan.advance(row, stage_progress(**fields), operation, status)
states["counter"] = copy.deepcopy(row)
event({"phase": "operation:player", "operation": "player", "status": "complete"})
event({"phase": "operation:output-verify", "operation": "output-verify", "status": "complete"})
row["status"] = "complete"
stage_plan.advance(row, row["progress"])
states["complete"] = copy.deepcopy(row)
print(json.dumps(states))
