"""Fake-clock proofs for observed runtime, honest ETA scope and cheap status polls."""
from pathlib import Path
import copy
import json
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
import state
import timing


class Clock:
    def __init__(self): self.now = 100.0
    def __call__(self): return self.now
    def advance(self, seconds): self.now += seconds


class TimingTests(unittest.TestCase):
    def setUp(self):
        self.clock = Clock()
        self.tracker = timing.Timeline(self.clock)
        self.row = {"id": "build", "status": "running", "progressKey": "known-input", "attempts": 0}
        self.saved = {"session": "owner", "status": "running", "stages": [self.row]}
        self.tracker.activate(self.saved); self.tracker.sync(self.saved)

    def progress(self, phase="recovery-original-collection-merge", done=0, total=100, **extras):
        value = {"phase": phase, "done": done, "total": total, "unit": "files", **extras}
        self.tracker.observe(self.saved, self.row, value)
        return self.row["timing"]

    def sample_phase(self):
        self.progress(done=0)
        self.clock.advance(10); self.progress(done=10)
        self.clock.advance(10); return self.progress(done=20)

    def batch(self, index, seconds=100, total=6, retained=False):
        self.progress("recovery-batch", index, total)
        self.progress("recovery-batch-export-verify" if retained else "recovery-asset-load", None, None)
        self.clock.advance(seconds)
        return self.progress("recovery-batches", index + 1, total)

    def test_active_monotonic_elapsed_is_independent_of_work(self):
        self.progress(done=1)
        self.clock.advance(120)
        before = copy.deepcopy(self.saved)
        visible = self.tracker.snapshot(self.saved)
        self.assertEqual(visible["timing"]["elapsedSeconds"], 120)
        self.assertEqual(visible["stages"][0]["timing"]["estimate"]["status"], "learning")
        self.assertEqual(self.saved, before)

    def test_repeated_poll_projects_clock_without_state_mutation(self):
        before = copy.deepcopy(self.saved)
        for amount in (1, 2, 3):
            self.clock.advance(amount)
            self.tracker.snapshot(self.saved)
        self.assertEqual(self.saved, before)
        self.assertEqual(self.tracker.snapshot(self.saved)["timing"]["elapsedSeconds"], 6)

    def test_wait_freezes_elapsed_and_rates_until_action_clears(self):
        self.sample_phase()
        self.row["waiting"] = {"code": "unity_license"}; self.tracker.sync(self.saved)
        self.clock.advance(500)
        visible = self.tracker.snapshot(self.saved)["stages"][0]["timing"]
        self.assertEqual(visible["elapsedSeconds"], 20)
        self.assertEqual(visible["estimate"]["status"], "paused")
        self.row.pop("waiting"); self.tracker.sync(self.saved)
        self.clock.advance(5); resumed = self.progress(done=30)
        self.assertEqual(resumed["elapsedSeconds"], 25)
        self.assertEqual(resumed["estimate"]["status"], "learning")

    def test_cancel_immediately_freezes_elapsed_and_scoped_eta(self):
        self.sample_phase(); self.tracker.pause(self.saved)
        self.clock.advance(900)
        visible = self.tracker.snapshot(self.saved)["stages"][0]["timing"]
        self.assertEqual(visible["elapsedSeconds"], 20)
        self.assertEqual(visible["estimate"]["status"], "paused")

    def test_failed_stage_retains_elapsed_but_not_live_rate(self):
        self.sample_phase()
        self.row["status"] = self.saved["status"] = "failed"
        self.tracker.sync(self.saved)
        self.clock.advance(100)
        visible = self.tracker.snapshot(self.saved)["stages"][0]["timing"]
        self.assertEqual(visible["elapsedSeconds"], 20)
        self.assertEqual(visible["estimate"]["status"], "paused")

    def test_restart_never_charges_offline_gap_or_reuses_clock_rate(self):
        self.sample_phase(); self.tracker.close(self.saved)
        self.clock.advance(10000)
        fresh = timing.Timeline(self.clock)
        fresh.activate(self.saved); fresh.sync(self.saved)
        self.clock.advance(10)
        view = fresh.snapshot(self.saved)["stages"][0]["timing"]
        self.assertEqual(view["elapsedSeconds"], 30)
        self.assertEqual(view["estimate"]["status"], "unknown")

    def test_phase_eta_uses_measured_count_rate_and_exposes_scope_range(self):
        view = self.sample_phase()["estimate"]
        self.assertEqual(view["status"], "estimated")
        self.assertEqual(view["scope"], "phase")
        self.assertEqual(view["remainingSeconds"], 80)
        self.assertLess(view["lowerSeconds"], 80)
        self.assertGreater(view["upperSeconds"], 80)
        self.assertEqual(view["samples"], 3)

    def test_elapsed_or_stage_percent_alone_produces_no_eta(self):
        self.row["progress"] = {"stagePercent": 50, "percent": 50}
        self.clock.advance(500)
        view = self.progress("unity-shaders", None, None)["estimate"]
        self.assertEqual(view["status"], "unknown")
        self.assertIsNone(view["remainingSeconds"])

    def test_unobserved_import_phase_does_not_inherit_previous_rate(self):
        self.sample_phase()
        eta = self.progress("unity-asset-import", None, None)["estimate"]
        self.assertEqual(eta["status"], "unknown")
        self.assertIsNone(eta["remainingSeconds"])

    def test_changed_total_or_decreasing_count_learns_new_scope(self):
        self.sample_phase()
        self.assertEqual(self.progress(done=30, total=500)["estimate"]["status"], "learning")
        self.clock.advance(10); self.progress(done=40, total=500)
        self.clock.advance(10); self.progress(done=50, total=500)
        self.assertEqual(self.progress(done=1, total=500)["estimate"]["status"], "learning")

    def test_individual_file_events_do_not_replace_aggregate_rate(self):
        self.sample_phase()
        self.clock.advance(1)
        eta = self.progress("recovery-checkpoint-file-hash", 123, 999, unit="bytes")["estimate"]
        self.assertEqual(eta["basisPhase"], "recovery-original-collection-merge")
        self.assertEqual(eta["remainingSeconds"], 80)

    def test_stalled_counter_discards_old_eta_without_inventing_progress(self):
        self.sample_phase(); self.clock.advance(31)
        eta = self.tracker.snapshot(self.saved)["stages"][0]["timing"]["estimate"]
        self.assertEqual(eta["status"], "unknown")
        self.assertEqual(eta["reason"], "counter-stale")
        self.assertIsNone(eta["remainingSeconds"])

    def test_completed_counter_does_not_promise_whole_stage_completion(self):
        self.sample_phase()
        eta = self.progress(done=100)["estimate"]
        self.assertEqual(eta["status"], "unknown")
        self.assertIsNone(eta["remainingSeconds"])

    def test_fresh_batch_history_excludes_retained_exports(self):
        self.batch(0, 800, retained=True)
        self.batch(1, 100); self.batch(2, 120)
        self.progress("recovery-batch", 3, 6)
        eta = self.progress("recovery-asset-load", None, None)["estimate"]
        self.assertEqual([entry["index"] for entry in self.row["timingState"]["batches"]], [1, 2])
        self.assertEqual(eta["scope"], "conversion-batches")
        self.assertEqual(eta["samples"], 2)
        self.assertEqual(eta["remainingSeconds"], 330)
        self.assertEqual((eta["batchIndex"], eta["batchTotal"]), (4, 6))

    def test_one_fresh_batch_cannot_estimate_remaining_conversion(self):
        self.batch(0, 100)
        self.progress("recovery-batch", 1, 6)
        eta = self.progress("recovery-asset-load", None, None)["estimate"]
        self.assertEqual(eta["status"], "unknown")
        self.assertEqual(eta["scope"], "phase")

    def test_reused_native_index_does_not_reclassify_fresh_export_as_retained(self):
        self.progress("recovery-batch", 0, 6); self.progress("recovery-asset-load", None, None)
        self.clock.advance(100)
        self.tracker.observe(self.saved, self.row,
            {"phase": "recovery-native-index-set", "done": 1, "total": 2}, status="reuse")
        self.progress("recovery-batches", 1, 6)
        self.assertEqual(len(self.row["timingState"]["batches"]), 1)

    def test_new_conversion_section_cannot_retain_old_packet_eta(self):
        self.batch(0, 100); self.batch(1, 100)
        self.progress("recovery-batch", 2, 6); self.progress("recovery-asset-load", None, None)
        eta = self.progress("recovery-section:references", None, None)["estimate"]
        self.assertEqual(eta["status"], "unknown")
        self.assertEqual(eta["scope"], "phase")

    def test_different_packet_schedule_cannot_inherit_old_conversion_eta(self):
        self.batch(0, 100); self.batch(1, 100)
        self.progress("recovery-batch", 2, 18)
        eta = self.progress("recovery-asset-load", None, None)["estimate"]
        self.assertEqual(eta["scope"], "phase")
        self.assertEqual(eta["status"], "unknown")

    def test_batch_estimate_updates_elapsed_not_work_percent(self):
        self.batch(0, 100); self.batch(1, 100)
        self.progress("recovery-batch", 2, 6); self.progress("recovery-asset-load", None, None)
        first = self.tracker.snapshot(self.saved)["stages"][0]["timing"]["estimate"]
        self.clock.advance(20)
        later = self.tracker.snapshot(self.saved)["stages"][0]["timing"]["estimate"]
        self.assertEqual((first["remainingSeconds"], later["remainingSeconds"]), (400, 380))

    def test_repeated_batch_start_keeps_original_duration(self):
        self.progress("recovery-batch", 0, 6); self.progress("recovery-asset-load", None, None)
        self.clock.advance(40); self.progress("recovery-batch", 0, 6)
        self.clock.advance(60); self.progress("recovery-batches", 1, 6)
        self.assertEqual(self.row["timingState"]["batches"][0]["seconds"], 100)

    def test_paused_partial_batch_cannot_pollute_completed_batch_history(self):
        self.progress("recovery-batch", 0, 6); self.progress("recovery-asset-load", None, None)
        self.clock.advance(40); self.row["waiting"] = {}; self.tracker.sync(self.saved)
        # A real wait object is truthy; empty dictionaries do not gate work.
        self.row["waiting"] = {"code": "action"}; self.tracker.sync(self.saved)
        self.clock.advance(500); self.row.pop("waiting"); self.tracker.sync(self.saved)
        self.clock.advance(40); self.progress("recovery-batches", 1, 6)
        self.assertEqual(self.row["timingState"]["batches"], [])

    def test_batch_history_is_bounded_and_duplicate_index_replaces_sample(self):
        for index in range(20): self.batch(index, 100, total=21)
        self.assertEqual(len(self.row["timingState"]["batches"]), 16)
        self.batch(19, 120, total=21)
        self.assertEqual(len(self.row["timingState"]["batches"]), 16)
        self.assertEqual(self.row["timingState"]["batches"][-1]["seconds"], 120)

    def test_batch_history_survives_resume_but_changed_input_discards_it(self):
        self.batch(0, 100); self.batch(1, 120); self.tracker.close(self.saved)
        self.clock.advance(10000)
        fresh = timing.Timeline(self.clock); fresh.activate(self.saved); fresh.sync(self.saved)
        value = {"phase": "recovery-batch", "done": 2, "total": 6, "unit": "batches"}
        fresh.observe(self.saved, self.row, value)
        fresh.observe(self.saved, self.row, {"phase": "recovery-asset-load", "done": None, "total": None})
        self.assertEqual(self.row["timing"]["estimate"]["scope"], "conversion-batches")
        self.row["progressKey"] = "different-input"; fresh.sync(self.saved)
        self.assertEqual(self.row["timingState"]["batches"], [])
        self.assertEqual(self.row["timing"]["elapsedSeconds"], 0)

    def test_legacy_wall_timestamp_is_not_claimed_as_active_elapsed(self):
        old = {"session": "legacy", "status": "failed", "stages": [
            {"id": "build", "status": "failed", "attempts": 3, "startedAt": 1, "durationSeconds": 10000}]}
        view = timing.Timeline(self.clock).snapshot(old)
        self.assertEqual(view["timing"]["elapsedSeconds"], 0)
        self.assertEqual(view["timing"]["elapsedBasis"], "since-update")
        self.assertNotIn("timingState", old["stages"][0])

    def test_changed_inspected_game_contract_discards_old_batch_timing(self):
        self.saved["completed"] = {"inspect": {"details": {"inputKey": "original-content"}}}
        self.tracker.sync(self.saved)
        self.batch(0, 100); self.batch(1, 120)
        self.saved["completed"]["inspect"]["details"]["inputKey"] = "different-content"
        self.tracker.sync(self.saved)
        self.assertEqual(self.row["timingState"]["batches"], [])
        self.assertEqual(self.row["timing"]["elapsedSeconds"], 0)

    def test_complete_scope_has_no_remaining_eta_and_duration_is_frozen(self):
        self.clock.advance(100)
        self.row["status"] = self.saved["status"] = "complete"; self.tracker.sync(self.saved)
        self.clock.advance(100)
        visible = self.tracker.snapshot(self.saved)["stages"][0]["timing"]
        self.assertEqual(visible["elapsedSeconds"], 100)
        self.assertEqual(visible["estimate"]["status"], "complete")


class StoreTimingTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.clock = Clock()
        self.store = state.Store(Path(self.temp.name) / "owned", clock=self.clock)
        self.saved = self.store.create({"gameRoot": "fixture"})
        self.session = self.saved["session"]; self.row = self.saved["stages"][5]
    def tearDown(self): self.temp.cleanup()

    def test_status_poll_updates_elapsed_without_writing_or_progress_event(self):
        with state.file_lock(self.store.root / "run.lock"), self.store.active(self.saved):
            self.saved["status"] = self.row["status"] = "running"
            self.store.begin_stage(self.session, "build", "input-key")
            self.row["status"] = "running"
            self.store.progress(self.session, "build", "recovery-asset-export", 0, 100, "assets")
            path = self.store.session_dir(self.session) / "state.json"
            before = path.read_bytes(); log = (path.parent / "logs/progress.log").read_bytes()
            self.clock.advance(20)
            visible = self.store.load(self.session)
            self.assertEqual(visible["timing"]["elapsedSeconds"], 20)
            self.assertEqual(path.read_bytes(), before)
            self.assertEqual((path.parent / "logs/progress.log").read_bytes(), log)
            self.assertEqual(visible["stages"][5]["progress"]["done"], 0)

    def test_same_owner_cancel_freezes_live_elapsed_immediately(self):
        with state.file_lock(self.store.root / "run.lock"), self.store.active(self.saved):
            self.saved["status"] = self.row["status"] = "running"
            self.store.progress(self.session, "build", "starting")
            self.clock.advance(20); self.store.cancel(self.session)
            self.clock.advance(100)
            visible = self.store.load(self.session)
            self.assertEqual(visible["timing"]["elapsedSeconds"], 20)
            self.assertEqual(visible["stages"][5]["timing"]["estimate"]["status"], "paused")

    def test_another_store_cancel_never_overwrites_running_owner_state(self):
        with state.file_lock(self.store.root / "run.lock"), self.store.active(self.saved):
            self.saved["status"] = self.row["status"] = "running"
            self.store.progress(self.session, "build", "starting")
            path = self.store.session_dir(self.session) / "state.json"; before = path.read_bytes()
            other = state.Store(self.store.root, clock=self.clock); other.cancel(self.session)
            self.assertEqual(path.read_bytes(), before)
            self.assertTrue((path.parent / "cancel.json").is_file())

    def test_save_persists_active_time_and_fresh_store_resume_excludes_offline_gap(self):
        with state.file_lock(self.store.root / "run.lock"), self.store.active(self.saved):
            self.saved["status"] = self.row["status"] = "running"
            self.store.progress(self.session, "build", "starting")
            self.clock.advance(20)
            self.row["status"] = self.saved["status"] = "failed"
            self.store.event(self.saved, "fixture_failure", "build")
        self.clock.advance(10000)
        fresh = state.Store(self.store.root, clock=self.clock)
        reopened = fresh.load(self.session)
        self.assertEqual(reopened["timing"]["elapsedSeconds"], 20)
        with state.file_lock(fresh.root / "run.lock"), fresh.active(reopened):
            reopened["status"] = reopened["stages"][5]["status"] = "running"
            fresh.progress(self.session, "build", "starting")
            self.clock.advance(10)
            self.assertEqual(fresh.load(self.session)["timing"]["elapsedSeconds"], 30)

    def test_existing_bounded_progress_log_includes_timing_without_poll_noise(self):
        with self.store.active(self.saved):
            self.saved["status"] = self.row["status"] = "running"
            self.store.progress(self.session, "build", "starting")
            self.clock.advance(11)
            self.store.progress(self.session, "build", "recovery-asset-export", 100, 1000, "assets")
        log = self.store.session_dir(self.session) / "logs/progress.log"
        records = [json.loads(line) for line in log.read_text().splitlines()]
        self.assertEqual(records[-1]["parameters"]["timing"]["elapsedSeconds"], 11)


if __name__ == "__main__": unittest.main()
