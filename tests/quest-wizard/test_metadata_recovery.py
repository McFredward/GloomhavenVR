"""Actual atomic writes/retained runs with injected Windows denial boundaries.

The host performs real file publication and workflow transitions. Injected
WinError codes/attributes exercise Windows recovery policy, not Windows API proof.
"""
import errno
import json
import os
from pathlib import Path
import stat
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
import discovery
import metadata_io
import server
import state
import wizard


def denied(code=5):
    error = PermissionError(errno.EACCES, "Access is denied", "latest-session.json")
    error.winerror = code
    return error


class MetadataRecoveryTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.store = state.Store(Path(temporary.name) / "owned")
        self.path = self.store.root / "latest-session.json"
        self.old = {"schema": 1, "session": "a" * 32}
        state.atomic_json(self.path, self.old)
        self.new = {"schema": 1, "session": "b" * 32}
        self.original_replace = os.replace

    def refusal(self, failures=None, code=5):
        calls = []
        def replace(source, destination):
            if Path(destination) == self.path:
                calls.append((source, destination))
                self.assertEqual(json.loads(self.path.read_bytes()), self.old)
                if failures is None or len(calls) <= failures: raise denied(code)
            return self.original_replace(source, destination)
        return calls, patch.object(metadata_io.os, "replace", side_effect=replace)

    def readonly(self, path, *args, **kwargs):
        info = os.lstat(path)
        fields = {name: getattr(info, name) for name in
                  ("st_dev", "st_ino", "st_mode", "st_nlink", "st_size", "st_mtime_ns", "st_ctime_ns")}
        return SimpleNamespace(**fields, st_file_attributes=metadata_io.READONLY if Path(path) == self.path else 0)

    def test_normal_publication_has_no_retry_or_permission_changes(self):
        with patch.object(metadata_io.time, "sleep") as sleep, patch.object(metadata_io.os, "chmod") as chmod:
            result = state.atomic_json(self.path, self.new)
        self.assertEqual(result, {"attempts": 1, "readOnlyRepaired": False})
        self.assertEqual(json.loads(self.path.read_bytes()), self.new)
        sleep.assert_not_called(); chmod.assert_not_called()
        self.assertFalse(list(self.path.parent.glob("*.tmp")))

    def test_transient_access_and_sharing_refusals_retry_same_atomic_payload(self):
        for code in (5, 32, 33):
            with self.subTest(code=code):
                state.atomic_json(self.path, self.old)
                calls, refusal = self.refusal(failures=2, code=code)
                with refusal, patch.object(metadata_io, "_windows_host", return_value=True), \
                        patch.object(metadata_io.time, "sleep") as sleep, patch.object(metadata_io.os, "chmod") as chmod:
                    result = state.atomic_json(self.path, self.new)
                self.assertEqual(result["attempts"], 3)
                self.assertEqual(len({str(row[0]) for row in calls}), 1)
                self.assertEqual(sleep.call_count, 2); chmod.assert_not_called()
                self.assertEqual(json.loads(self.path.read_bytes()), self.new)
                self.assertFalse(list(self.path.parent.glob("*.tmp")))

    def test_exact_windows_readonly_leaf_is_repaired_without_acl_or_folder_changes(self):
        self.path.chmod(0o444)
        original_chmod = os.chmod
        calls, refusal = self.refusal(failures=1)
        with refusal, patch.object(metadata_io, "_windows_host", return_value=True), \
                patch.object(Path, "lstat", lambda path, *args, **kwargs: self.readonly(path, *args, **kwargs)), \
                patch.object(metadata_io.os, "chmod", wraps=original_chmod) as chmod, \
                patch.object(metadata_io.time, "sleep") as sleep:
            result = state.atomic_json(self.path, self.new)
        self.assertEqual(result, {"attempts": 2, "readOnlyRepaired": True})
        chmod.assert_called_once_with(self.path, 0o644, follow_symlinks=False)
        sleep.assert_not_called()
        self.assertEqual(len(calls), 2)
        self.assertEqual(json.loads(self.path.read_bytes()), self.new)
        log = (self.store.root / "logs/metadata-repair.jsonl").read_text()
        self.assertIn("metadata-readonly-cleared", log)
        self.assertNotIn(self.new["session"], log)

    def test_permanent_mandatory_metadata_failure_keeps_old_document_and_offers_retry(self):
        calls, refusal = self.refusal()
        with refusal, patch.object(metadata_io, "_windows_host", return_value=True), \
                patch.object(metadata_io.time, "sleep"), patch.object(metadata_io.os, "chmod") as chmod:
            with self.assertRaises(state.WizardError) as caught: state.atomic_json(self.path, self.new)
        self.assertEqual(caught.exception.code, "workspace_metadata_busy")
        self.assertEqual(caught.exception.parameters["retryAttempts"], 6)
        self.assertEqual(caught.exception.parameters["path"], str(self.path))
        self.assertTrue(caught.exception.parameters["completedWorkRetained"])
        self.assertTrue(caught.exception.parameters["retryable"])
        self.assertEqual(len(calls), 6); chmod.assert_not_called()
        self.assertEqual(json.loads(self.path.read_bytes()), self.old)
        self.assertFalse(list(self.path.parent.glob("*.tmp")))

    def test_optional_restart_pointer_does_not_block_a_real_selected_workflow(self):
        choices = wizard.choices({"gameRoot": str(self.store.root / "owned-game"), "install": False})
        older = self.store.create(choices)
        self.old = {"schema": 1, "session": older["session"]}
        calls, refusal = self.refusal()
        actions = []
        def perform(row, supervisor):
            name = next(stage["id"] for stage in row["stages"] if stage["status"] == "running")
            actions.append(name)
            output = self.store.session_dir(row["session"]) / (name + ".result")
            output.write_bytes(b"completed owned step")
            return [output], {"retained": True}
        with refusal, patch.object(metadata_io, "_windows_host", return_value=True), patch.object(metadata_io.time, "sleep"):
            saved = self.store.create(choices)
            result = wizard.Engine(self.store, actions={name: perform for name in state.STAGES}).run(saved["session"])
        self.assertEqual(result["status"], "complete")
        self.assertEqual(actions, list(state.STAGES))
        self.assertEqual(len(calls), 12)  # Creation and startup each try once with five bounded retries.
        self.assertEqual(len(result["completed"]), 7)
        self.assertEqual(result["needsActions"], [])
        self.assertTrue(any(row["code"] == "workspace_metadata_deferred" for row in result["events"]))
        self.assertEqual(json.loads(self.path.read_bytes()), self.old)
        self.assertEqual(discovery.recent_sessions(self.store)[1], saved["session"])
        self.assertTrue(self.store.remember_session(result))
        self.assertNotIn("restartHintDeferred", result)
        self.assertEqual(json.loads(self.path.read_bytes())["session"], saved["session"])

    def test_no_permission_repair_for_unowned_or_hardlinked_metadata(self):
        for unowned in (False, True):
            with self.subTest(unowned=unowned):
                alias = self.store.root / "alias"
                marker = self.store.root / "wizard-owner.json"
                if unowned: marker.unlink()
                else: os.link(self.path, alias)
                calls, refusal = self.refusal()
                with refusal, patch.object(metadata_io, "_windows_host", return_value=True), \
                        patch.object(Path, "lstat", lambda path, *args, **kwargs: self.readonly(path, *args, **kwargs)), patch.object(metadata_io.time, "sleep"), \
                        patch.object(metadata_io.os, "chmod") as chmod:
                    with self.assertRaises(state.WizardError): state.atomic_json(self.path, self.new)
                chmod.assert_not_called()
                self.assertEqual(json.loads(self.path.read_bytes()), self.old)
                if alias.exists(): alias.unlink()

    def test_linux_denial_is_actionable_and_disk_full_keeps_its_original_error(self):
        with patch.object(metadata_io, "_windows_host", return_value=False), \
                patch.object(metadata_io.os, "replace", side_effect=PermissionError(errno.EACCES, "permission denied")), \
                patch.object(metadata_io.time, "sleep") as sleep:
            with self.assertRaises(state.WizardError): state.atomic_json(self.path, self.new)
        sleep.assert_not_called()
        with patch.object(metadata_io.os, "replace", side_effect=OSError(errno.ENOSPC, "disk full")):
            with self.assertRaises(OSError) as caught: state.atomic_json(self.path, self.new)
        self.assertEqual(caught.exception.errno, errno.ENOSPC)
        self.assertEqual(json.loads(self.path.read_bytes()), self.old)

    def test_linked_destination_is_rejected_before_publication(self):
        outside = self.store.root.parent / "external.json"
        outside.write_bytes(self.path.read_bytes()); self.path.unlink()
        try: self.path.symlink_to(outside)
        except OSError: self.skipTest("Host disallows symlinks")
        with patch.object(metadata_io.os, "replace") as replace:
            with self.assertRaises(state.WizardError): state.atomic_json(self.path, self.new)
        replace.assert_not_called()
        self.assertEqual(json.loads(outside.read_bytes()), self.old)

    def test_failed_mandatory_state_publication_does_not_leave_a_live_runner(self):
        saved = self.store.create(wizard.choices({"gameRoot": str(self.store.root / "game")}))
        with patch.object(self.store, "save", side_effect=state.WizardError("workspace_metadata_busy", "still locked")):
            with self.assertRaises(state.WizardError):
                with self.store.active(saved):
                    self.assertIn(saved["session"], self.store._live)
        self.assertNotIn(saved["session"], self.store._live)


class StartupFailureEvidenceTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(); self.addCleanup(temporary.cleanup)
        self.store = state.Store(Path(temporary.name) / "owned")
        self.saved = self.store.create(wizard.choices({"gameRoot": "C:\\PrivateGame", "install": False}))
        self.session = self.saved["session"]

    def fail_before_stage(self, error):
        def fail(_): raise error
        http = server.LocalServer(self.store, ROOT / "tools/quest-wizard-ui",
                                  engine_factory=lambda store: SimpleNamespace(run=fail))
        self.addCleanup(http.close_owned)
        http.run_session(self.session)
        http.jobs[self.session].join(timeout=5)
        self.assertFalse(http.jobs[self.session].is_alive())
        return http

    def test_pre_stage_error_retains_actual_cause_in_state_log_and_home_support(self):
        http = self.fail_before_stage(denied())
        saved = self.store.load(self.session)
        self.assertEqual(saved["status"], "blocked")
        self.assertIn("Access is denied", saved["needsActions"][0]["parameters"]["cause"])
        progress = (self.store.session_dir(self.session) / "logs/progress.log").read_text()
        self.assertIn("Access is denied", progress)
        self.assertIn("PermissionError", progress)
        self.assertIn("traceback", progress)
        self.assertEqual(http.job_errors[self.session]["code"], "stage_failed")
        support = discovery.local_support_module(ROOT, "support")
        metadata, entries = self.export(support)
        issue = metadata["currentRunIssue"]
        self.assertEqual(issue["session"], self.session)
        self.assertIn("Access is denied", issue["needsActions"][0]["parameters"]["cause"])
        self.assertIn("wizard/wizard-requests.log", entries)
        self.assertNotIn("PrivateGame", json.dumps(metadata))

    def export(self, support):
        result = support.export_support(self.store.root)
        with zipfile.ZipFile(result["path"]) as archive:
            return json.loads(archive.read("diagnostic.json")), archive.namelist()

    def test_typed_metadata_cause_and_retry_parameters_survive_server_boundary(self):
        error = metadata_io.busy_error(self.store.root / "sessions/state.json", denied(), state.WizardError, attempts=6)
        self.fail_before_stage(error)
        action = self.store.load(self.session)["needsActions"][0]
        self.assertEqual(action["code"], "workspace_metadata_busy")
        self.assertEqual(action["parameters"]["errorType"], "PermissionError")
        self.assertIn("Access is denied", action["parameters"]["cause"])
        self.assertEqual(action["parameters"]["retryAttempts"], 6)

    def test_live_metadata_action_remains_visible_if_saving_the_error_is_also_refused(self):
        error = metadata_io.busy_error(self.store.root / "sessions/state.json", denied(), state.WizardError, attempts=6)
        with patch.object(self.store, "event", side_effect=error):
            http = self.fail_before_stage(error)
        durable = self.store.load(self.session)
        self.assertEqual(durable["status"], "ready")
        visible = http.visible_state(self.session, durable)
        self.assertEqual(visible["status"], "blocked")
        self.assertEqual(visible["needsActions"][0]["code"], "workspace_metadata_busy")
        self.assertEqual(visible["needsActions"][0]["parameters"]["path"], error.parameters["path"])
        self.assertEqual(durable["choices"], visible["choices"])
        self.assertNotIn("needsActions", visible["requestError"].get("parameters", {}))

    def test_unexpected_programming_exception_is_exposed_instead_of_leaving_an_active_spinner(self):
        self.fail_before_stage(TypeError("controlled startup failure"))
        action = self.store.load(self.session)["needsActions"][0]
        self.assertEqual(action["parameters"]["errorType"], "TypeError")
        self.assertIn("controlled startup failure", action["parameters"]["cause"])

    def test_failed_run_action_and_metadata_repair_log_are_exported_but_completed_runs_are_not_current_issues(self):
        self.saved.update(status="failed", needsActions=[{"code": "stage_failed", "message": {"en": "current failure"},
            "parameters": {"cause": "actual current cause", "path": str(self.store.root / "state.json"), "secret": "NEVER"}}])
        self.store.save(self.saved)
        metadata_io.record(self.store.root / "latest-session.json", state.ordinary, "metadata-replaced", attempts=2)
        support = discovery.local_support_module(ROOT, "support")
        metadata, entries = self.export(support)
        self.assertEqual(metadata["currentRunIssue"]["needsActions"][0]["parameters"]["cause"], "actual current cause")
        self.assertNotIn("NEVER", json.dumps(metadata))
        self.assertIn("wizard/metadata-repair.jsonl", entries)
        self.saved["status"] = "complete"; self.store.save(self.saved)
        self.assertNotIn("currentRunIssue", self.export(support)[0])


if __name__ == "__main__": unittest.main()
