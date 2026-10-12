"""Support reports explain selected update and utility work without payload scans."""
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/quest-builder'))
import support
from storage import write_json


class UpdateSupportTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name); self.session = 'b' * 32
        self.session_root = self.root / 'sessions' / self.session
        write_json(self.root / 'wizard-owner.json', {'schema': 1, 'owner': 'GloomhavenVR.QuestWizard'})
        self.state = {'session': self.session, 'status': 'running', 'choices': {'mode': 'update-mod'},
            'completed': {'inspect': {'details': {'inputKey': 'a' * 64, 'baseApkSha256': 'd' * 64}}}}
        write_json(self.session_root / 'state.json', self.state)
        self.key = 'c' * 64; self.build = self.root / 'build'
        self.plan = {'schema': 1, 'mode': 'update-mod', 'updateKey': self.key, 'gameInputKey': 'a' * 64,
            'baseApkSha256': 'd' * 64, 'updateStrategy': 'cached-full-build', 'requiresUnity': True,
            'rebuildReasons': ['changed asset producer'], 'signing': {'password': 'SECRET MUST NOT SHIP'},
            'inputs': {'game': {'key': 'e' * 64, 'files': [{'path': 'Managed/game.dll', 'size': 1, 'sha256': 'f' * 64}]},
                       'mod': {'key': '1' * 64}, 'profileKey': '2' * 64,
                       'profile': {'displayName': 'PRIVATE PROFILE'}}}
        write_json(self.build / 'latest-update-input.json', {'schema': 1, 'plan': 'updates/' + self.key + '/plan.json'})
        self.save_plan()

    def save_plan(self): write_json(self.build / 'updates' / self.key / 'plan.json', self.plan)

    def export(self):
        result = support.export_support(self.root, self.session)
        with zipfile.ZipFile(result['path']) as archive:
            rows = {name: archive.read(name).decode() for name in archive.namelist()}
        return json.loads(rows['diagnostic.json']), rows

    def test_route_is_visible_without_publishing_owned_inventories_or_keys(self):
        meta, rows = self.export()
        self.assertEqual(meta['updateRoute']['updateStrategy'], 'cached-full-build')
        self.assertEqual(meta['updateRoute']['retainedGameKey'], 'e' * 64)
        text = '\n'.join(rows.values())
        for value in ('PRIVATE PROFILE', 'SECRET MUST NOT SHIP', 'Managed/game.dll'):
            self.assertNotIn(value, text)

    def test_related_fallback_inventory_delta_uses_only_already_written_metadata(self):
        write_json(self.build / 'latest-input.json', {'manifest': 'manifests/' + '3' * 64 + '.json'})
        write_json(self.build / 'manifests' / ('3' * 64 + '.json'), {'inputKey': '3' * 64, 'target': 'game',
            'mod': {'key': '1' * 64}, 'profileKey': '2' * 64,
            'game': {'key': '4' * 64, 'files': [{'path': 'Managed/game.dll', 'size': 2, 'sha256': '5' * 64}]}})
        with patch.object(support, 'digest', side_effect=AssertionError('No payload hashing in route diagnostics')):
            value = support.update_route_stats(self.build, self.state)
        self.assertEqual(value['observedGameDelta']['changedCount'], 1)
        self.assertFalse(value['observedGameDelta']['contentVerified'])

    def test_unrelated_or_escaped_plan_cannot_describe_this_run(self):
        self.plan['baseApkSha256'] = '0' * 64; self.save_plan()
        self.assertIsNone(support.update_route_stats(self.build, self.state))
        write_json(self.build / 'latest-update-input.json', {'plan': '../outside.json'})
        self.assertIsNone(support.update_route_stats(self.build, self.state))

    def test_current_device_log_and_real_progress_are_captured_with_redaction(self):
        folder = self.root / 'device'; folder.mkdir()
        name = 'device-tools-' + '6' * 32 + '.log'
        (folder / name).write_text('adb -s QUEST_PRIVATE shell sha256sum current.apk\n')
        (folder / ('device-tools-' + '7' * 32 + '.log')).write_text('STALE DEVICE LOG MUST NOT SHIP')
        write_json(folder / 'device-tools-state.json', {'status': 'capturing', 'operation': 'capture',
            'serial': 'QUEST_PRIVATE', 'log': str(folder / name), 'progress': {'done': 3, 'total': 29},
            'activity': {'command': 'adb -s QUEST_PRIVATE shell sha256sum', 'elapsedSeconds': 45}})
        (folder / 'device-tools-progress.jsonl').write_text(json.dumps({'activity': {'elapsedSeconds': 45}}) + '\n')
        meta, rows = self.export()
        self.assertEqual(meta['deviceTools']['activity']['elapsedSeconds'], 45)
        self.assertIn('device/' + name, rows)
        text = '\n'.join(rows.values())
        self.assertNotIn('QUEST_PRIVATE', text); self.assertNotIn('STALE DEVICE LOG MUST NOT SHIP', text)
        self.assertIn('device/device-tools-progress.jsonl', rows)

    def test_invalid_utility_state_and_external_log_are_optional_diagnostics(self):
        folder = self.root / 'device'; folder.mkdir()
        write_json(folder / 'device-tools-state.json', {'status': 'running', 'log': '/outside/device-tools-' + '6' * 32 + '.log'})
        _, logs = support.device_tool_evidence(self.root, [])
        self.assertEqual(logs, [])
        (folder / 'device-tools-state.json').write_text('broken JSON')
        meta, _ = self.export(); self.assertNotIn('deviceTools', meta)

    def test_current_cleanup_and_request_failures_survive_many_historical_unity_logs(self):
        logs = self.root / 'logs'; logs.mkdir()
        (logs / 'storage-cleanup.log').write_text('already_running: current cleanup admission failed\n')
        (logs / 'wizard-requests.log').write_text('current utility failure traceback\n')
        build_logs = self.build / 'logs'; build_logs.mkdir()
        for index in range(support.MAX_FILES + 3):
            (build_logs / ('unity-build-' + str(index) + '.log')).write_text('historic unrelated build\n')
        _, rows = self.export()
        self.assertIn('wizard/storage-cleanup.log', rows)
        self.assertIn('wizard/wizard-requests.log', rows)
        self.assertIn('current cleanup', rows['wizard/storage-cleanup.log'])


if __name__ == '__main__': unittest.main()
