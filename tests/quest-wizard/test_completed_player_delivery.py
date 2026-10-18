"""The real Wizard runner resumes final APK delivery without earlier checks."""
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
import state
import wizard
import stage_plan


class CompletedPlayerDeliveryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.store = state.Store(self.root / "owned")
        self.game = self.root / "Game"; self.game.mkdir()
        self.repo = self.root / "New Builder"; self.repo.mkdir()
        self.profile = self.root / "profile.json"
        state.atomic_json(self.profile, {"provider": "steam", "steamId": "76561198000000001", "displayName": "Owned player"})
        self.saved = self.store.create(wizard.choices({"gameRoot": str(self.game), "install": False, "ownedDlc": ["jotl", "solo"]}))
        self.session = self.saved["session"]
        self.input_key = "a" * 64
        self.selection = {"inputKey": self.input_key, "modBuild": 665, "resumeKind": "completed-player"}
        self.selector = mock.Mock(return_value=self.selection)
        self.helper = SimpleNamespace(select=self.selector)
        self.module = SimpleNamespace(_local_helper=self.load_helper, verified_latest_build=self.verified)
        self.commands = []
        self.observe = lambda: None
        self.failure = None
        self.engine = wizard.Engine(self.store, self.repo, supervisor_factory=self.supervisor)
        self.saved["completed"] = {}
        details = {"tools": {}, "source": {"sourceRoot": str(self.root / "Old source"), "commit": "b" * 40},
                   "unity": {"unityEditor": str(self.root / "Removed Unity.exe")},
                   "profile": {"profilePath": str(self.profile)},
                   "inspect": {"inputKey": self.input_key, "gameKey": "c" * 64, "modBuild": 665}}
        for row in self.saved["stages"][:5]:
            key = self.engine.key(self.saved, row["id"])
            row.update(status="complete", details=details[row["id"]], attempts=1)
            self.saved["completed"][row["id"]] = {"key": key, "details": details[row["id"]]}
        row = self.saved["stages"][5]
        row.update(status="failed", attempts=1)
        row["progressKey"] = self.engine.key(self.saved, "build")
        self.saved["status"] = "failed"
        self.store.save(self.saved)
        for name in stage_plan.planned_operations(row):
            if name == "delivery": break
            self.store.operation(self.session, "build", name, complete=True)
        self.store.operation(self.session, "build", "delivery")
        self.saved = self.store.load(self.session)
        row = self.saved["stages"][5]
        row["progressPlan"]["unityWork"] = {"player": {"parts": dict(stage_plan.UNITY_METHOD_PARTS["player"]),
            "completed": ["assets", "method", "result"], "fractions": {}, "counters": {},
            "partCompleted": list(stage_plan.UNITY_METHOD_PARTS["player"]),
            "activities": {"native": {"il2cpp": {"done": 2, "total": 2, "closed": True, "status": "complete"}}}}}
        self.store.save(self.saved)
        self.old_percent = self.store.load(self.session)["stages"][5]["progress"]["stagePercent"]
        self.builder_patch = mock.patch("wizard.discovery.builder", return_value=self.module)
        self.builder_patch.start(); self.addCleanup(self.builder_patch.stop)

    def load_helper(self, name):
        self.assertEqual(name, "player_delivery")
        return self.helper

    def verified(self, root):
        return root / "apks/completed.apk", {"inputKey": self.input_key, "apkSha256": "d" * 64,
            "contentFiles": [{"path": "banks/never-open-this-bank"}]}

    def supervisor(self, store, session):
        return SimpleNamespace(set_stage=lambda name: None, run=self.run_child)

    def run_child(self, argv, log, **kwargs):
        self.commands.append((argv, kwargs)); self.observe()
        if self.failure: raise self.failure
        root = self.store.root / "build"; apk = root / "apks/completed.apk"
        apk.parent.mkdir(parents=True, exist_ok=True); apk.write_bytes(b"signed artifact")
        state.atomic_json(Path(str(apk) + ".build.json"), {"schema": 1, "inputKey": self.input_key, "modBuild": 665})
        state.atomic_json(root / "latest-build.json", {"schema": 1, "receipt": "stages/build/receipt.json"})
        receipt = root / "stages/build/receipt.json"; receipt.parent.mkdir(parents=True, exist_ok=True)
        state.atomic_json(receipt, {"schema": 1, "stage": "build", "details": {"inputKey": self.input_key}})

    def no_earlier_work(self):
        stack = []
        for name in ("qualify", "check_downloads", "stage_tools", "stage_source", "stage_unity", "stage_profile", "stage_inspect", "stage_build"):
            patch = mock.patch.object(self.engine, name, side_effect=AssertionError("Earlier work called: " + name))
            patch.start(); stack.append(patch)
        original = self.store.valid
        def valid(session, stage, key, **kwargs):
            self.assertEqual(stage, "install", "Earlier receipt validation is forbidden after Player completion")
            return original(session, stage, key, **kwargs)
        patch = mock.patch.object(self.store, "valid", side_effect=valid); patch.start(); stack.append(patch)
        for patch in stack: self.addCleanup(patch.stop)

    def test_real_runner_enters_delivery_without_any_prerequisite_or_asset_checks(self):
        self.no_earlier_work()
        current = self.engine.run(self.session)
        self.assertEqual(current["status"], "complete")
        self.assertEqual(len(self.commands), 1)
        argv = self.commands[0][0]
        self.assertEqual(argv[4:6], [str(self.repo / "scripts/build-quest.py"), "finish-build"])
        self.assertIn("--delivery-input-key", argv)
        self.assertEqual(argv[argv.index("--delivery-input-key") + 1], self.input_key)
        self.assertNotIn("--unity-editor", argv)
        self.assertNotIn("--dotnet", argv)
        self.assertEqual(self.commands[0][1], {})
        self.selector.assert_called_once_with(self.module, self.store.root / "build", str(self.game),
                                             profile_path=str(self.profile), input_key=self.input_key)

    def test_progress_keeps_saved_frontier_and_closed_unity_parts_green(self):
        self.no_earlier_work()
        def inspect():
            saved = self.store.load(self.session); row = saved["stages"][5]
            self.assertGreaterEqual(row["progress"]["stagePercent"], self.old_percent)
            overview = row["progress"]["buildOverview"]
            self.assertEqual(overview["active"], "delivery")
            operations = [item for group in overview["groups"] for item in group["operations"]]
            before = operations[:next(i for i, item in enumerate(operations) if item["id"] == "delivery")]
            self.assertTrue(all(item["closed"] and item["percent"] == 100 for item in before))
            player = next(item for item in operations if item["id"] == "player")
            self.assertTrue(all(part["closed"] and part["percent"] == 100 for phase in player["unityWork"]["phases"] for part in phase.get("parts", [])))
            self.assertFalse(saved["needsActions"])
        self.observe = inspect
        self.assertEqual(self.engine.run(self.session)["status"], "complete")

    def test_new_archive_source_does_not_relabel_existing_player_as_new_mod(self):
        state.atomic_json(self.repo / "quest-builder-release.json", {"schema": 1, "sourceCommit": "e" * 40, "modBuild": 667})
        self.no_earlier_work()
        saved = self.engine.run(self.session)
        self.assertEqual(saved["completed"]["inspect"]["details"]["modBuild"], 665)
        self.assertEqual(saved["completed"]["build"]["details"]["modBuild"], 665)
        self.assertTrue(saved["completed"]["build"]["details"]["resumedCompletedPlayer"])
        event = next(item for item in saved["events"] if item["code"] == "delivery_resume_started")
        self.assertEqual(event["parameters"]["modBuild"], 665)
        self.assertFalse(event["parameters"]["earlierStagesChecked"])

    def test_wizard_receipt_does_not_hash_the_builder_owned_content_banks_again(self):
        self.no_earlier_work()
        self.assertEqual(self.engine.run(self.session)["status"], "complete")
        receipt = state.read_json(self.store.receipt(self.session, "build"))
        self.assertEqual(len(receipt["outputs"]), 4)
        self.assertTrue(all(not row["path"].startswith("build/banks/") for row in receipt["outputs"]))

    def test_selector_refusal_keeps_files_and_never_rebuilds_all_work_silently(self):
        self.no_earlier_work()
        self.selector.side_effect = RuntimeError("Completed Player identity mismatch")
        saved = self.engine.run(self.session)
        self.assertEqual(saved["status"], "blocked")
        self.assertEqual(saved["needsActions"][0]["code"], "delivery_resume_invalid")
        self.assertTrue(saved["needsActions"][0]["parameters"]["completedPlayerRetained"])
        self.assertEqual(self.commands, [])

    def test_another_delivery_failure_and_process_restart_keep_the_fast_lane(self):
        self.no_earlier_work()
        self.failure = state.WizardError("build_tool_failed", "Signing retry failed")
        failed = self.engine.run(self.session)
        self.assertEqual(failed["status"], "failed")
        self.assertEqual(failed["stages"][5]["attempts"], 2)
        self.failure = None
        self.store = state.Store(self.store.root)
        self.engine.store = self.store
        self.assertEqual(self.engine.run(self.session)["status"], "complete")
        self.assertEqual(len(self.commands), 2)
        self.assertTrue(all(argv[5] == "finish-build" for argv, _ in self.commands))

    def test_missing_player_uses_the_ordinary_resume_instead_of_fabricating_completion(self):
        self.selector.return_value = None
        with mock.patch.object(self.engine, "qualify", side_effect=state.WizardError("fixture_full_resume", "No completed Player")) as qualify:
            saved = self.engine.run(self.session)
        self.assertEqual(saved["status"], "blocked")
        self.assertEqual(saved["needsActions"][0]["code"], "fixture_full_resume")
        qualify.assert_called_once()
        self.assertEqual(self.commands, [])

    def test_changed_game_profile_provider_or_dlc_cannot_finish_an_unrelated_player(self):
        for changes in ({"gameRoot": str(self.root / "Other Game")}, {"steamId": "76561198000000002"},
                        {"provider": "gog"}, {"ownedDlc": ["jotl"]}):
            with self.subTest(changes=changes):
                saved = self.store.load(self.session)
                saved["choices"].update(changes)
                self.assertFalse(self.engine.delivery_resume_candidate(saved))

    def test_amended_session_must_not_reuse_old_completed_player(self):
        saved = self.store.amend(self.session, dict(self.saved["choices"], ownedDlc=["solo"]))
        self.assertFalse(self.engine.delivery_resume_candidate(saved))
        self.selector.assert_not_called()

    def test_earlier_incomplete_phase_prevents_fast_lane_without_artifact_selection(self):
        saved = self.store.load(self.session)
        saved["stages"][2]["status"] = "blocked"
        self.assertFalse(self.engine.delivery_resume_candidate(saved))
        self.selector.assert_not_called()

    def test_install_uses_fresh_installer_after_resuming_old_source_artifact(self):
        self.no_earlier_work()
        saved = self.store.load(self.session)
        saved["choices"]["install"] = True; saved["choicesKey"] = state.value_hash(saved["choices"])
        self.store.save(saved)
        original_run = self.run_child
        def run(argv, log, **kwargs):
            if "install-quest-wireless.py" not in argv[4]: return original_run(argv, log, **kwargs)
            self.commands.append((argv, kwargs))
            config = self.store.root / "device/wireless-last-install.json"; config.parent.mkdir(parents=True, exist_ok=True)
            state.atomic_json(config, {"schema": 1, "inputKey": self.input_key, "apkSha256": "d" * 64, "launched": True})
        self.run_child = run
        self.assertEqual(self.engine.run(self.session)["status"], "complete")
        self.assertEqual(len(self.commands), 2)
        self.assertEqual(self.commands[1][0][4], str(self.repo / "scripts/install-quest-wireless.py"))

    def test_install_failure_retry_does_not_reopen_finished_delivery_or_prerequisites(self):
        self.no_earlier_work()
        saved = self.store.load(self.session)
        saved["choices"]["install"] = True; saved["choicesKey"] = state.value_hash(saved["choices"])
        self.store.save(saved)
        original_run = self.run_child
        installs = []
        def run(argv, log, **kwargs):
            if "install-quest-wireless.py" not in argv[4]: return original_run(argv, log, **kwargs)
            self.commands.append((argv, kwargs)); installs.append(argv)
            if len(installs) == 1: raise state.WizardError("child_failed", "Quest is not connected")
            row = self.store.load(self.session)["stages"][5]
            self.assertEqual(row["status"], "complete")
            self.assertEqual(row["progress"]["stagePercent"], 100)
            self.assertEqual(row["attempts"], 2)
            config = self.store.root / "device/wireless-last-install.json"; config.parent.mkdir(parents=True, exist_ok=True)
            state.atomic_json(config, {"schema": 1, "inputKey": self.input_key, "apkSha256": "d" * 64, "launched": True})
        self.run_child = run
        self.assertEqual(self.engine.run(self.session)["status"], "failed")
        finished = self.engine.run(self.session)
        self.assertEqual(finished["status"], "complete")
        self.assertEqual(len(self.commands), 3)
        self.assertEqual(sum(argv[5] == "finish-build" for argv, _ in self.commands), 1)
        self.assertEqual(finished["completed"]["build"]["details"]["modBuild"], 665)


if __name__ == "__main__": unittest.main()
