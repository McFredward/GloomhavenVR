"""Production release uploader under lost responses, partial uploads and immutable history."""
import copy
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
from urllib.error import HTTPError
from urllib.parse import urlsplit

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

    def test_verification_never_cleans_up_published_assets(self):
        release = {'draft': False, 'assets': [{'id': 7, 'name': self.archive.name, 'state': 'starter'}]}
        with self.assertRaises(M.Refused):
            M.verified_asset(self.api, release, self.archive, 'unused')
        self.assertEqual(self.api.deletes, 0)

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



class Response(io.BytesIO):
    def __init__(self, body, status=200):
        super().__init__(json.dumps(body).encode())
        self.status = status


def missing(request, **kwargs):
    raise HTTPError(request.full_url, 404, 'Not found', {}, io.BytesIO())


class AdapterTests(unittest.TestCase):
    def setUp(self):
        self.api = M.GitHub('owner/repo', 'test-token')
        self.draft = {'id': 391041682, 'tag_name': TAG, 'draft': True, 'assets': []}

    def test_draft_discovery_paginates_then_reads_by_pinned_id(self):
        paths = []
        def transport(request, **kwargs):
            path = urlsplit(request.full_url).path + ('?' + urlsplit(request.full_url).query if urlsplit(request.full_url).query else '')
            paths.append(path)
            if '/tags/' in path:
                return missing(request)
            if path.endswith('page=1'):
                return Response([dict(self.draft, id=i, tag_name=f'v0.0.{i}') for i in range(100)])
            if path.endswith('page=2'):
                return Response([self.draft])
            if path.endswith('/391041682'):
                return Response(self.draft)
            self.fail('Unexpected request: ' + path)
        with patch.object(M, 'urlopen', side_effect=transport):
            self.assertEqual(self.api.release(TAG), self.draft)
            self.assertEqual(self.api.release(TAG), self.draft)
        self.assertEqual(len(paths), 4)
        self.assertTrue(paths[-1].endswith('/releases/391041682'))

    def test_duplicate_drafts_are_rejected_including_across_pages(self):
        def transport(request, **kwargs):
            if '/tags/' in request.full_url:
                return missing(request)
            if request.full_url.endswith('page=1'):
                return Response([self.draft] + [dict(self.draft, id=i, tag_name=f'v0.0.{i}') for i in range(99)])
            return Response([dict(self.draft, id=999)])
        with patch.object(M, 'urlopen', side_effect=transport), self.assertRaises(M.Refused):
            self.api.release(TAG)

    def test_published_lookup_remains_read_only(self):
        published = dict(self.draft, draft=False)
        with patch.object(M, 'urlopen', return_value=Response(published)) as transport:
            self.assertEqual(self.api.release(TAG), published)
        self.assertEqual(transport.call_count, 1)
        self.assertIn('/releases/tags/', transport.call_args.args[0].full_url)

    def test_unreadable_or_retargeted_pinned_release_cannot_create_another(self):
        self.api.release_ids[TAG] = self.draft['id']
        with patch.object(M, 'urlopen', side_effect=missing), self.assertRaises(OSError):
            self.api.release(TAG)
        with patch.object(M, 'urlopen', return_value=Response(dict(self.draft, tag_name='v9.9.9'))), self.assertRaises(M.Refused):
            self.api.release(TAG)

    def test_create_checks_existing_tag_and_pins_returned_id(self):
        requests = []
        def transport(request, **kwargs):
            requests.append(request)
            if request.get_method() == 'GET':
                return Response({'ref': 'refs/tags/' + TAG})
            return Response(self.draft, 201)
        with patch.object(M, 'urlopen', side_effect=transport):
            self.assertEqual(self.api.create(TAG, 'Title', 'Notes'), self.draft)
        self.assertEqual(self.api.release_ids[TAG], self.draft['id'])
        self.assertTrue(requests[0].full_url.endswith('/git/ref/tags/' + TAG))
        self.assertEqual(requests[1].get_header('Content-type'), 'application/json')
        payload = json.loads(requests[1].data)
        self.assertTrue(payload['draft'])
        self.assertNotIn('target_commitish', payload)
        with patch.object(M, 'urlopen', side_effect=missing) as transport, self.assertRaises(M.Refused):
            M.GitHub('owner/repo', 'test').create(TAG, 'Title', 'Notes')
        self.assertEqual(transport.call_count, 1)

    def test_real_adapter_recovers_lost_create_response_and_uploads_by_id(self):
        self.exercise_recovery(hidden_forever=False)

    def test_unreadable_lost_create_response_never_repeats_creation(self):
        self.exercise_recovery(hidden_forever=True)

    def exercise_recovery(self, hidden_forever):
        state = {'release': None, 'creates': 0, 'uploads': 0, 'published': 0}
        content = b'actual package bytes sent through production Request adapter'
        def transport(request, **kwargs):
            url = urlsplit(request.full_url)
            path = url.path
            method = request.get_method()
            self.assertEqual(request.get_header('Authorization'), 'Bearer test-token')
            if '/commits/' in path:
                return Response({'sha': SHA})
            if '/compare/' in path:
                return Response({'merge_base_commit': {'sha': SHA}})
            if '/git/ref/tags/' in path:
                return Response({'ref': 'refs/tags/' + TAG})
            if '/releases/tags/' in path:
                return missing(request)
            if method == 'POST' and path.endswith('/releases'):
                state['creates'] += 1
                state['release'] = copy.deepcopy(self.draft)
                self.assertTrue(json.loads(request.data)['draft'])
                raise HTTPError(request.full_url, 500, 'Lost creation response', {}, io.BytesIO())
            if path.endswith('/releases') and method == 'GET':
                return Response([] if hidden_forever or state['release'] is None else [state['release']])
            if path.endswith('/releases/391041682/assets') and method == 'POST':
                self.assertEqual(url.netloc, 'uploads.github.com')
                self.assertEqual(url.query, 'name=GloomhavenVR-1.0.4.zip')
                self.assertEqual(request.get_header('Content-length'), str(len(content)))
                self.assertEqual(request.get_header('Content-type'), 'application/zip')
                self.assertEqual(request.data.read(), content)
                state['uploads'] += 1
                asset = {'id': 7, 'name': 'GloomhavenVR-1.0.4.zip', 'state': 'uploaded',
                         'size': len(content), 'digest': 'sha256:' + hashlib.sha256(content).hexdigest()}
                state['release']['assets'] = [asset]
                return Response(asset, 201)
            if path.endswith('/releases/391041682'):
                if method == 'PATCH':
                    self.assertEqual(request.get_header('Content-type'), 'application/json')
                    self.assertEqual(state['uploads'], 1)
                    self.assertFalse(json.loads(request.data)['draft'])
                    state['published'] += 1
                    state['release']['draft'] = False
                return Response(state['release'])
            self.fail(f'Unexpected request {method} {request.full_url}')
        with tempfile.TemporaryDirectory() as tmp:
            archive = Path(tmp) / 'GloomhavenVR-1.0.4.zip'
            archive.write_bytes(content)
            with patch.object(M, 'urlopen', side_effect=transport):
                if hidden_forever:
                    with self.assertRaises(M.Refused):
                        M.publish(self.api, TAG, SHA, archive, 'Notes', lambda _: None)
                else:
                    M.publish(self.api, TAG, SHA, archive, 'Notes', lambda _: None)
        self.assertEqual(state['creates'], 1)
        self.assertEqual(state['uploads'], 0 if hidden_forever else 1)
        self.assertEqual(state['published'], 0 if hidden_forever else 1)


if __name__ == '__main__':
    unittest.main()
