"""Production release uploader under lost responses, partial uploads and immutable history."""
import copy
import hashlib
import importlib.util
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location('release_upload', ROOT / 'scripts/release-upload.py')
M = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(M)
SHA = 'a' * 40
TAG = 'v1.0.4'


class API:
    def __init__(self):
        self.state = {'id': 123, 'draft': True, 'assets': []}
        self.tag_sha = self.main_base = SHA
        self.uploads = self.publishes = self.creates = self.deletes = 0
        self.fail_uploads = 0
        self.lost_upload = self.lost_publish = self.lost_create = False
        self.mutate_tag_on_upload = False

    def request(self, path):
        return {'sha': self.tag_sha} if path.startswith('/commits/') else {'merge_base_commit': {'sha': self.main_base}}

    def release(self, tag):
        return copy.deepcopy(self.state)

    def create(self, tag, title, notes):
        self.creates += 1
        self.state = {'id': 123, 'draft': True, 'assets': []}
        if self.lost_create:
            raise OSError('HTTP 500 after draft creation')

    def upload(self, tag, archive):
        self.uploads += 1
        if self.uploads <= self.fail_uploads:
            raise OSError('HTTP 500')
        self.state['assets'] = [{'id': 7, 'name': archive.name, 'state': 'uploaded',
                                  'size': archive.stat().st_size,
                                  'digest': hashlib.sha256(archive.read_bytes()).hexdigest()}]
        if self.mutate_tag_on_upload:
            self.tag_sha = 'b' * 40
        if self.lost_upload:
            raise OSError('HTTP 500 after successful upload')

    def remove_starter(self, asset):
        self.deletes += 1
        self.state['assets'] = []

    def digest(self, asset):
        return asset['digest']

    def publish(self, release, title, notes):
        self.publishes += 1
        if not self.state['assets'] or self.state['assets'][0]['state'] != 'uploaded':
            raise AssertionError('Published before upload finished')
        self.state['draft'] = False
        if self.lost_publish:
            raise OSError('HTTP 500 after publication')


class UploadTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.archive = Path(self.tmp.name) / 'GloomhavenVR-1.0.4.zip'
        self.archive.write_bytes(b'fresh main-built archive fixture')
        self.api = API()
        self.sleeps = []

    def publish(self):
        M.publish(self.api, TAG, SHA, self.archive, 'Player-facing release notes.', self.sleeps.append)

    def test_new_release_is_draft_until_verified(self):
        self.api.state = None
        self.publish()
        self.assertEqual((self.api.creates, self.api.uploads, self.api.publishes), (1, 1, 1))
        self.assertFalse(self.api.state['draft'])

    def test_existing_empty_draft_recovers(self):
        self.publish()
        self.assertEqual((self.api.creates, self.api.uploads, self.api.publishes), (0, 1, 1))

    def test_bounded_retry_keeps_failed_release_unpublished(self):
        self.api.fail_uploads = 10
        with self.assertRaises(M.Refused):
            self.publish()
        self.assertEqual(self.api.uploads, 5)
        self.assertEqual(self.sleeps, [10, 20, 30, 40])
        self.assertTrue(self.api.state['draft'])
        self.assertEqual(self.api.publishes, 0)

    def test_transient_upload_failure_recovers(self):
        self.api.fail_uploads = 2
        self.publish()
        self.assertEqual((self.api.uploads, self.api.publishes), (3, 1))

    def test_lost_upload_response_is_reconciled_without_reupload(self):
        self.api.lost_upload = True
        self.publish()
        self.assertEqual((self.api.uploads, self.api.publishes), (1, 1))

    def test_lost_publish_response_is_reconciled_without_modification(self):
        self.api.lost_publish = True
        self.publish()
        self.assertEqual((self.api.uploads, self.api.publishes), (1, 1))

    def test_lost_create_response_reuses_created_draft(self):
        self.api.state = None
        self.api.lost_create = True
        self.publish()
        self.assertEqual((self.api.creates, self.api.uploads, self.api.publishes), (1, 1, 1))

    def test_published_release_is_never_modified_even_when_bytes_match(self):
        self.publish()
        before = copy.deepcopy(self.api.state)
        with self.assertRaises(M.Refused):
            self.publish()
        self.assertEqual(self.api.state, before)
        self.assertEqual(self.api.publishes, 1)

    def test_preuploaded_matching_archive_is_not_replaced(self):
        self.api.upload(TAG, self.archive)
        self.publish()
        self.assertEqual(self.api.uploads, 1)

    def test_digest_size_state_and_extra_assets_fail_closed(self):
        for key, bad in [('digest', 'bad'), ('size', 1), ('state', 'unknown')]:
            self.api = API()
            self.api.upload(TAG, self.archive)
            self.api.state['assets'][0][key] = bad
            with self.subTest(key=key), self.assertRaises(M.Refused):
                self.publish()
            self.assertEqual(self.api.publishes, 0)
            self.assertEqual(self.api.deletes, 0)
        self.api = API()
        self.api.upload(TAG, self.archive)
        self.api.state['assets'].append({'name': 'unrelated.zip'})
        with self.assertRaises(M.Refused):
            self.publish()

    def test_incomplete_github_starter_can_be_retried(self):
        self.api.state['assets'] = [{'id': 7, 'name': self.archive.name, 'state': 'starter', 'size': 0}]
        self.publish()
        self.assertEqual((self.api.deletes, self.api.uploads, self.api.publishes), (1, 1, 1))

    def test_wrong_tag_or_main_history_prevents_upload(self):
        for field in ('tag_sha', 'main_base'):
            self.api = API()
            setattr(self.api, field, 'b' * 40)
            with self.subTest(field=field), self.assertRaises(M.Refused):
                self.publish()
            self.assertEqual(self.api.uploads, 0)

    def test_tag_retargeted_while_uploading_never_publishes(self):
        self.api.mutate_tag_on_upload = True
        with self.assertRaises(M.Refused):
            self.publish()
        self.assertTrue(self.api.state['draft'])
        self.assertEqual(self.api.publishes, 0)

    def test_github_create_requires_existing_tag_and_is_draft(self):
        api = M.GitHub('owner/repo', 'test')
        with patch.object(M.subprocess, 'run') as run:
            api.create(TAG, 'Title', 'Notes')
        command = run.call_args.args[0]
        self.assertIn('--verify-tag', command)
        self.assertIn('--draft', command)
        self.assertNotIn('--clobber', command)


if __name__ == '__main__':
    unittest.main()
