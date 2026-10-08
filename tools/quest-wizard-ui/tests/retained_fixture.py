"""Real Store/Engine-key retry snapshots, without game or Unity processes."""
import copy
import json
from pathlib import Path
import sys
import tempfile

sys.path.insert(0, str(Path(sys.argv[1]).resolve() / "tools/quest-wizard"))
import stage_plan
import state
import wizard

with tempfile.TemporaryDirectory(prefix="quest-progress-scope-") as temporary:
    root = Path(temporary)
    release = root / "Builder"
    release.mkdir()
    manifest = release / "quest-builder-release.json"
    state.atomic_json(manifest, {"schema": 1, "sourceCommit": "a" * 40})
    store = state.Store(root / "owned")
    saved = store.create({"gameRoot": str(root / "OwnedGame"), "provider": "gog", "mode": "build"})
    session = saved["session"]
    engine = wizard.Engine(store, release)
    saved["completed"] = {}
    for row in saved["stages"][:5]:
        key = engine.key(saved, row["id"])
        row.update(status="complete", progressKey=key, progress=state.stage_progress("complete", 1, 1))
        saved["completed"][row["id"]] = {"key": key, "details": {}}
    store.save(saved)
    old_key = engine.key(saved, "build")
    scope = state.value_hash({"gameKey": "original-game-bytes", "profile": "fixture-owner", "mode": "build", "target": "arm64-quest"})
    store.begin_stage(session, "build", old_key)
    store.operation(session, "build", "recovery")
    store.progress(session, "build", "recovery-plan", 16, 16, "batches", status="reuse")
    for section in stage_plan.RECOVERY_SECTIONS:
        if section == "staging":
            store.progress(session, "build", "recovery-section:staging", status="start")
            for name in stage_plan.STAGING_STEPS:
                store.progress(session, "build", "staging-section:" + name, 1, 1, "steps", status="reuse")
        store.progress(session, "build", "recovery-section:" + section, 1, 1, "sections", status="reuse")
        if section == "batches": store.progress(session, "build", "recovery-batches", 16, 16, "batches", status="reuse")
    store.operation(session, "build", "project-files", complete=True)
    store.operation(session, "build", "startup-content")
    for number, name in enumerate(("post-effects", "loading-resources", "startup-movies"), 1):
        store.progress(session, "build", "prepare-substage:" + name, number, 11, "checkpoints", operation="startup-content", status="complete")
    store.progress(session, "build", "prepare-substage:native-sprites", 3, 11, "checkpoints", operation="startup-content", status="start")
    store.progress(session, "build", "prepare-items:native-sprites", 3368, 3369, "items")
    saved = store.load(session)
    saved.update(status="failed", needsActions=[{"code": "build_tool_failed", "stage": "build"}])
    saved["stages"][5]["status"] = "failed"
    store.save(saved)
    snapshots = [copy.deepcopy(saved)]
    with state.file_lock(store.root / "run.lock"), store.active(saved):
        store.begin_run(session)
        state.atomic_json(manifest, {"schema": 1, "sourceCommit": "b" * 40})
        new_source = engine.key(saved, "source")
        assert new_source != saved["completed"]["source"]["key"]
        saved["completed"]["source"]["key"] = new_source
        new_key = engine.key(saved, "build")
        assert new_key != old_key
        store.begin_stage(session, "build", new_key, work_key=scope, previous_work_key=scope)
        saved["stages"][0]["status"] = "running"
        store.progress(session, "tools", "receipt-verify", 0, 100, "bytes", "Qualifying retained tools")
        snapshots.append(copy.deepcopy(saved))
        saved["stages"][0]["status"] = "complete"
        saved["stages"][1]["status"] = "running"
        store.begin_stage(session, "source", new_source)
        saved["stages"][1]["status"] = "running"
        store.progress(session, "source", "source-copy", 3, 100, "files", "Qualifying the updated Builder source")
        snapshots.append(copy.deepcopy(saved))
        saved["stages"][1]["status"] = "complete"
        saved["stages"][5]["status"] = "running"
        store.progress(session, "build", "starting")
        store.operation(session, "build", "game-inputs")
        snapshots.append(copy.deepcopy(saved))
        store.operation(session, "build", "startup-content")
        store.progress(session, "build", "prepare-substage:native-sprites", 3, 11, "checkpoints", operation="startup-content", status="start")
        store.progress(session, "build", "prepare-items:native-sprites", 100, 3369, "items", "Current attempt Sprite")
        snapshots.append(copy.deepcopy(saved))
    print(json.dumps(snapshots))
