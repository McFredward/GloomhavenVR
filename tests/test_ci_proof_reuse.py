"""Fail-closed CI evidence decisions, using real Git merge trees and API fixtures."""
import copy
from contextlib import redirect_stdout
from datetime import datetime, timedelta, timezone
import importlib.util
import io
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location('ci_proof_reuse', ROOT / 'scripts/ci-proof-reuse.py')
M = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(M)
NOW = datetime(2026, 9, 17, 12, tzinfo=timezone.utc)
REPO = 'owner/project'


class API:
    def __init__(self, runs, jobs):
        self.records = {r['id']: copy.deepcopy(r) for r in runs}
        self.listed = copy.deepcopy(runs)
        self.job_list = jobs
        self.workflow = {'id': 10, 'path': M.WORKFLOW, 'state': 'active'}
        self.reread = None
        self.reads = 0

    def get(self, path):
        if path == '/actions/workflows/ci.yml':
            return self.workflow
        self.reads += 1
        if self.reread and self.reads > 1:
            return self.reread
        return self.records[int(path.rsplit('/', 1)[1])]

    def runs(self, workflow_id):
        return self.listed

    def jobs(self, run_id, attempt):
        return self.job_list


class ProofTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.g('init', '-q', '-b', 'main')
        self.g('config', 'user.name', 'Test')
        self.g('config', 'user.email', 'test@example.invalid')
        self.write(M.WORKFLOW, 'trusted workflow\n')
        self.write(M.HELPER, 'trusted verifier\n')
        self.write('source', 'base\n')
        self.base = self.save('base')
        self.g('checkout', '-q', '-b', 'dev')
        self.write('source', 'candidate\n')
        self.head = self.save('dev source')
        self.g('update-ref', 'refs/remotes/origin/dev', self.head)
        self.tree = M.tree(self.root, 'HEAD')
        self.run = {'id': 101, 'run_number': 20, 'run_attempt': 1, 'workflow_id': 10,
                    'repository': {'full_name': REPO}, 'head_repository': {'full_name': REPO},
                    'path': M.WORKFLOW, 'event': 'push', 'head_branch': 'dev', 'head_sha': self.head,
                    'status': 'completed', 'conclusion': 'success', 'updated_at': NOW.isoformat()}
        self.job = {'run_id': 101, 'run_attempt': 1, 'head_sha': self.head,
                    'name': f'Full checks [{self.tree}]', 'status': 'completed', 'conclusion': 'success',
                    'steps': [{'name': M.PROOF_STEP, 'status': 'completed', 'conclusion': 'success'}]}
        self.api = API([self.run], [self.job])

    def g(self, *args):
        return M.git(self.root, *args)

    def write(self, path, text):
        file = self.root / path
        file.parent.mkdir(parents=True, exist_ok=True)
        file.write_text(text)

    def save(self, message):
        self.g('add', '.')
        self.g('commit', '-qm', message)
        return self.g('rev-parse', 'HEAD')

    def proof(self):
        return M.find_proof(self.root, REPO, M.tree(self.root, 'HEAD'), self.api, NOW)

    def reject(self):
        with self.assertRaises(M.NoProof):
            self.proof()

    def merge(self):
        self.g('checkout', '-q', 'main')
        self.g('merge', '--no-ff', '-qm', 'reviewed merge', 'dev')
        return self.g('rev-parse', 'HEAD')

    def pr_event(self):
        return {'pull_request': {'head': {'sha': self.head, 'repo': {'full_name': REPO}},
                                 'base': {'sha': self.base, 'repo': {'full_name': REPO}}}}

    def test_successful_dev_push(self):
        self.assertTrue(self.proof().endswith('/101/attempts/1'))

    def test_main_merge_reuses_identical_tree(self):
        merged = self.merge()
        self.assertNotEqual(self.head, merged)
        M.candidate(self.root, {'ref': 'refs/heads/main', 'after': merged}, 'release', REPO, merged)
        self.assertTrue(self.proof())

    def resume_fixture(self):
        self.write('src/GloomhavenVR/GloomhavenVR.csproj', '<Project><PropertyGroup><Version>1.0.4</Version></PropertyGroup></Project>')
        tagged = self.save('release source')
        self.g('tag', 'v1.0.4')
        self.g('checkout', '-q', 'main')
        self.g('merge', '--no-ff', '-qm', 'release merge', 'dev')
        self.write(M.HELPER, 'new trusted recovery verifier')
        workflow = self.save('recovery workflow')
        self.g('update-ref', 'refs/remotes/origin/dev', workflow)
        self.g('update-ref', 'refs/remotes/origin/main', workflow)
        return tagged, workflow

    def resume_candidate(self, event, workflow, **kwargs):
        return M.candidate(self.root, event, 'release-resume', REPO, workflow,
                           kwargs.get('event_name', 'workflow_dispatch'),
                           kwargs.get('event_ref', 'refs/heads/main'))

    def test_resume_binds_current_workflow_but_returns_original_tagged_tree(self):
        tagged, workflow = self.resume_fixture()
        for ref in ('main', 'refs/heads/main'):
            event = {'ref': ref, 'inputs': {'resume_tag': 'v1.0.4'}}
            self.assertEqual(self.resume_candidate(event, workflow), (tagged, M.tree(self.root, tagged)))
        # Original exact-tree evidence remains usable even though the recovery verifier
        # is newer: trust its CURRENT main/dev version, not code from the old checkout.
        wanted = M.tree(self.root, tagged)
        self.api = API([dict(self.run, head_sha=tagged)], [dict(self.job, head_sha=tagged, name=f'Full checks [{wanted}]')])
        self.assertTrue(M.find_proof(self.root, REPO, wanted, self.api, NOW))

    def test_resume_rejects_wrong_event_branch_head_missing_tag_and_version(self):
        tagged, workflow = self.resume_fixture()
        event = {'ref': 'main', 'inputs': {'resume_tag': 'v1.0.4'}}
        for kwargs in ({'event_name': 'push'}, {'event_ref': 'refs/heads/dev'}):
            with self.subTest(kwargs=kwargs), self.assertRaises(M.NoProof):
                self.resume_candidate(event, workflow, **kwargs)
        with self.assertRaises(M.NoProof):
            self.resume_candidate(event, tagged)
        for bad in ('', '../main', 'main', 'v1.0.4\n'):
            with self.subTest(tag=bad), self.assertRaises(M.NoProof):
                self.resume_candidate(dict(event, inputs={'resume_tag': bad}), workflow)
        self.g('tag', 'v1.0.5', tagged)
        with self.assertRaises(M.NoProof):
            self.resume_candidate(dict(event, inputs={'resume_tag': 'v1.0.5'}), workflow)

    def test_resume_rejects_source_outside_main_and_rewritten_workflow_history(self):
        tagged, workflow = self.resume_fixture()
        event = {'ref': 'main', 'inputs': {'resume_tag': 'v1.0.4'}}
        orphan = self.g('commit-tree', M.tree(self.root, tagged), '-m', 'not main')
        self.g('tag', '-f', 'v1.0.4', orphan)
        with self.assertRaises(M.NoProof):
            self.resume_candidate(event, workflow)
        self.g('tag', '-f', 'v1.0.4', tagged)
        self.g('update-ref', 'refs/remotes/origin/main', tagged)
        with self.assertRaises(M.NoProof):
            self.resume_candidate(event, workflow)

    def test_pr_validates_both_merge_parents(self):
        merged = self.merge()
        self.assertEqual(M.candidate(self.root, self.pr_event(), 'pr', REPO, merged)[1], self.tree)
        for side in ('head', 'base'):
            event = self.pr_event()
            event['pull_request'][side]['sha'] = 'a' * 40
            with self.assertRaises(M.NoProof):
                M.candidate(self.root, event, 'pr', REPO, merged)

    def test_head_checkout_is_not_synthetic_merge(self):
        with self.assertRaises(M.NoProof):
            M.candidate(self.root, self.pr_event(), 'pr', REPO, self.head)

    def test_fork_always_full(self):
        merged = self.merge()
        event = self.pr_event()
        event['pull_request']['head']['repo']['full_name'] = 'fork/project'
        with self.assertRaises(M.NoProof):
            M.candidate(self.root, event, 'pr', REPO, merged)

    def test_wrong_event_sha_or_nonmain_release(self):
        for sha, ref in [('a' * 40, 'refs/heads/main'), (self.head, 'refs/heads/dev')]:
            with self.assertRaises(M.NoProof):
                M.candidate(self.root, {'ref': ref, 'after': self.head}, 'release', REPO, sha)

    def test_docs_source_and_workflow_changes_invalidate_tree(self):
        for path in ('README.md', 'source', M.WORKFLOW, M.HELPER):
            with self.subTest(path=path):
                self.g('reset', '--hard', self.head)
                self.write(path, 'changed\n')
                self.save('changed input')
                self.reject()

    def test_release_highlights_and_plain_markdown_reuse_tested_parent(self):
        for path in ('packaging/release-highlights/1.0.8.md', 'README.md',
                     'README.de.md', 'docs/PLAYING.md', '.planning/STATE.md'):
            with self.subTest(path=path):
                self.g('reset', '--hard', self.head)
                self.write(path, 'updated text\n')
                docs = self.save('documentation only')
                self.g('update-ref', 'refs/remotes/origin/dev', docs)
                self.assertNotEqual(M.tree(self.root, docs), self.tree)
                self.assertEqual(M.proof_tree(self.root, M.tree(self.root, docs)), self.tree)
                self.assertTrue(self.proof())

    def test_multiple_docs_commits_reuse_only_nearest_tested_source(self):
        self.write('packaging/release-highlights/1.0.8.md', 'release text\n')
        self.save('release message')
        self.write('README.md', 'player documentation\n')
        docs = self.save('readme')
        self.g('update-ref', 'refs/remotes/origin/dev', docs)
        self.assertTrue(self.proof())
        self.g('checkout', '-q', 'main')
        self.g('merge', '--no-ff', '-qm', 'release merge', 'dev')
        merged = self.g('rev-parse', 'HEAD')
        self.assertEqual(M.candidate(self.root, {'ref': 'refs/heads/main', 'after': merged},
                                     'release', REPO, merged)[1], M.tree(self.root, docs))
        self.assertTrue(self.proof())

    def test_internal_pr_merge_of_docs_only_dev_head_reuses_source(self):
        self.write('packaging/release-highlights/1.0.8.md', 'release text\n')
        docs = self.save('release message')
        self.g('update-ref', 'refs/remotes/origin/dev', docs)
        self.head = docs
        merged = self.merge()
        self.assertEqual(M.candidate(self.root, self.pr_event(), 'pr', REPO, merged)[1],
                         M.tree(self.root, docs))
        self.assertTrue(self.proof())

    def test_docs_after_untested_code_or_policy_change_cannot_borrow_old_green(self):
        for path in ('source', M.WORKFLOW, M.HELPER):
            with self.subTest(path=path):
                self.g('reset', '--hard', self.head)
                self.write(path, 'untested change\n')
                changed = self.save('untested source or policy')
                self.write('packaging/release-highlights/1.0.8.md', 'release text\n')
                docs = self.save('release message')
                self.g('update-ref', 'refs/remotes/origin/dev', docs)
                self.assertEqual(M.proof_tree(self.root, M.tree(self.root, docs)), M.tree(self.root, changed))
                self.reject()

    def test_other_packaging_files_and_nonmarkdown_docs_require_full_checks(self):
        for path in ('packaging/gloomhavenvr.bundle.README.txt', 'docs/CI-CD.py',
                     'docs/PATCH-INVENTORY.md', 'docs/NET-ACTION-SURFACE.md',
                     '.planning/refactor/INVARIANTS-Net-Rig.md',
                     'packaging/release-highlights/README.md', 'scripts/ci-build.sh'):
            with self.subTest(path=path):
                self.g('reset', '--hard', self.head)
                self.write(path, 'changed\n')
                changed = self.save('not allowable docs')
                self.g('update-ref', 'refs/remotes/origin/dev', changed)
                self.assertEqual(M.proof_tree(self.root, M.tree(self.root, changed)), M.tree(self.root, changed))
                self.reject()

    def test_markdown_symlink_requires_full_checks(self):
        target = self.root / 'packaging/release-highlights/1.0.8.md'
        target.parent.mkdir(parents=True)
        target.symlink_to('../../source')
        changed = self.save('symlink')
        self.g('update-ref', 'refs/remotes/origin/dev', changed)
        self.assertEqual(M.proof_tree(self.root, M.tree(self.root, changed)), M.tree(self.root, changed))
        self.reject()

    def test_failed_or_pending_source_run_is_not_bypassed_by_docs(self):
        self.write('packaging/release-highlights/1.0.8.md', 'release text\n')
        docs = self.save('release message')
        self.g('update-ref', 'refs/remotes/origin/dev', docs)
        for status, conclusion in [('completed', 'failure'), ('in_progress', None)]:
            with self.subTest(status=status):
                newer = dict(self.run, id=102, run_number=21, status=status, conclusion=conclusion)
                self.api = API([self.run, newer], [self.job])
                self.reject()

    def test_docs_push_binds_exact_dev_event(self):
        event = {'ref': 'refs/heads/dev', 'after': self.head}
        self.assertEqual(M.candidate(self.root, event, 'dev-docs', REPO, self.head,
                                     'push', 'refs/heads/dev'), (self.head, self.tree))
        for name, ref, after in [('workflow_dispatch', 'refs/heads/dev', self.head),
                                 ('push', 'refs/heads/main', self.head),
                                 ('push', 'refs/heads/dev', 'a' * 40)]:
            with self.subTest(name=name, ref=ref, after=after), self.assertRaises(M.NoProof):
                M.candidate(self.root, {'ref': ref, 'after': after}, 'dev-docs', REPO,
                            self.head, name, ref)

    def test_conflict_resolution_changes_require_full_checks(self):
        self.g('checkout', '-q', 'main')
        self.write('source', 'main edits\n')
        self.save('main divergence')
        result = subprocess.run(['git', '-C', str(self.root), 'merge', '--no-ff', 'dev'], capture_output=True)
        self.assertNotEqual(result.returncode, 0)
        self.write('source', 'resolved differently\n')
        self.save('conflict resolution')
        self.assertNotEqual(M.tree(self.root, 'HEAD'), self.tree)
        self.reject()

    def test_orphan_same_tree_is_not_dev_evidence(self):
        orphan = self.g('commit-tree', self.tree, '-m', 'unrelated root')
        self.api.records[101]['head_sha'] = orphan
        self.api.listed[0]['head_sha'] = orphan
        self.reject()

    def test_fork_pr_and_other_workflow_runs_are_not_proof(self):
        for key, value in [('event', 'pull_request'), ('event', 'workflow_run'), ('head_branch', 'main'),
                           ('workflow_id', 11), ('path', '.github/workflows/fake.yml'),
                           ('repository', {'full_name': 'other/project'}),
                           ('head_repository', {'full_name': 'fork/project'})]:
            with self.subTest(key=key, value=value):
                self.api = API([dict(self.run, **{key: value})], [self.job])
                self.reject()

    def test_newer_failure_cancel_or_pending_supersedes_green(self):
        for status, conclusion in [('completed', 'failure'), ('completed', 'cancelled'),
                                   ('completed', 'skipped'), ('queued', None), ('in_progress', None)]:
            with self.subTest(conclusion=conclusion):
                newer = dict(self.run, id=102, run_number=21, status=status, conclusion=conclusion)
                self.api = API([newer, self.run], [self.job])
                self.reject()

    def test_latest_attempt_required(self):
        self.api.records[101].update(run_attempt=2, conclusion='failure')
        self.reject()
        self.api.records[101]['conclusion'] = 'success'
        self.reject()  # attempt-one jobs cannot attest attempt two
        self.job['run_attempt'] = 2
        self.assertTrue(self.proof().endswith('/attempts/2'))

    def test_rerun_during_read_refuses(self):
        self.api.reread = dict(self.run, run_attempt=2, status='queued', conclusion=None)
        self.reject()

    def test_reuse_success_never_mints_new_full_proof(self):
        self.job['name'] = 'Build and gates'
        self.reject()

    def test_missing_duplicate_failed_or_skipped_marker_refuses(self):
        for steps in ([], [self.job['steps'][0], self.job['steps'][0]],
                      [{'name': M.PROOF_STEP, 'status': 'completed', 'conclusion': 'skipped'}],
                      [{'name': M.PROOF_STEP, 'status': 'completed', 'conclusion': 'failure'}]):
            self.job['steps'] = steps
            self.reject()

    def test_job_identity_tree_and_status_are_checked(self):
        for field, value in [('head_sha', 'a' * 40), ('name', 'Full checks [wrong]'),
                             ('run_id', 999), ('run_attempt', 2), ('status', 'queued'),
                             ('conclusion', 'cancelled')]:
            with self.subTest(field=field):
                self.api.job_list = [dict(self.job, **{field: value})]
                self.reject()
        self.api.job_list = [self.job, self.job]
        self.reject()

    def parallel_fixture(self):
        self.write(M.WORKFLOW, f'trusted workflow\n      - name: {M.PARALLEL_GATE}\n')
        self.head = self.save('parallel workflow')
        self.g('update-ref', 'refs/remotes/origin/dev', self.head)
        self.tree = M.tree(self.root, 'HEAD')
        self.run['head_sha'] = self.head
        self.job.update(head_sha=self.head, name=f'Full checks [{self.tree}]')
        self.job['steps'].append({'name': M.PARALLEL_GATE, 'status': 'completed', 'conclusion': 'success'})
        dependencies = [dict(self.job, name=name, steps=[]) for name in
                        ['Source and build checks'] + [f'Runtime suites [{i}/4]' for i in range(4)]]
        self.api = API([self.run], [self.job] + dependencies)
        return dependencies

    def test_parallel_proof_requires_all_successful_jobs_and_gate(self):
        self.parallel_fixture()
        self.assertTrue(self.proof())
        self.job['steps'] = [self.job['steps'][0]]
        self.reject()

    def test_parallel_proof_covers_documentation_only_descendant(self):
        self.parallel_fixture()
        tested_tree = self.tree
        self.write('packaging/release-highlights/1.0.8.md', 'release message\n')
        docs_commit = self.save('release message')
        self.g('update-ref', 'refs/remotes/origin/dev', docs_commit)
        self.assertNotEqual(M.tree(self.root, docs_commit), tested_tree)
        self.assertEqual(M.proof_tree(self.root, M.tree(self.root, docs_commit)), tested_tree)
        self.assertTrue(self.proof())

    def test_parallel_missing_or_duplicate_dependency_cannot_mint_proof(self):
        self.parallel_fixture()
        complete = self.api.job_list[:]
        for index in range(1, len(complete)):
            with self.subTest(missing=complete[index]['name']):
                self.api.job_list = complete[:index] + complete[index + 1:]
                self.reject()
            with self.subTest(duplicate=complete[index]['name']):
                self.api.job_list = complete + [complete[index]]
                self.reject()
        self.api.job_list = complete
        self.assertTrue(self.proof())

    def test_parallel_falsely_green_aggregator_cannot_hide_failed_or_stale_jobs(self):
        dependencies = self.parallel_fixture()
        for job in dependencies:
            original = dict(job)
            for field, value in [('conclusion', 'failure'), ('conclusion', 'skipped'),
                                 ('conclusion', 'cancelled'), ('status', 'in_progress'),
                                 ('run_attempt', 2), ('head_sha', 'a' * 40), ('run_id', 102)]:
                with self.subTest(job=job['name'], field=field, value=value):
                    job[field] = value
                    self.reject()
                    job.clear()
                    job.update(original)
        self.assertTrue(self.proof())

    def test_historical_serial_proof_does_not_require_new_matrix(self):
        wanted = self.tree
        self.write(M.WORKFLOW, f'new workflow\n      - name: {M.PARALLEL_GATE}\n')
        newer = self.save('adopt parallel execution after historical release')
        self.g('update-ref', 'refs/remotes/origin/dev', newer)
        self.assertTrue(M.find_proof(self.root, REPO, wanted, self.api, NOW))

    def test_stale_future_and_inactive_workflow_refuse(self):
        for date in (NOW - timedelta(days=31), NOW + timedelta(hours=1)):
            self.api.records[101]['updated_at'] = date.isoformat()
            self.reject()
        self.api.records[101]['updated_at'] = NOW.isoformat()
        self.api.workflow['state'] = 'disabled_manually'
        self.reject()

    def test_manual_dev_full_validation_is_proof_independent_of_upload(self):
        self.api = API([dict(self.run, event='workflow_dispatch')], [self.job])
        self.job['steps'].append({'name': 'Upload requested dev build', 'status': 'completed', 'conclusion': 'failure'})
        self.assertTrue(self.proof())  # optional step uses continue-on-error; full job is green
        self.api.job_list.append({'name': 'Maintain optional dev artifact storage', 'conclusion': 'failure'})
        self.assertTrue(self.proof())

    def test_advanced_dev_preserves_old_exact_tree_when_policy_unchanged(self):
        self.write('unrelated', 'new work')
        newer = self.save('new work')
        self.g('update-ref', 'refs/remotes/origin/dev', newer)
        self.g('checkout', '-q', self.head)
        self.assertTrue(self.proof())

    def test_advanced_dev_policy_change_invalidates_old_proof(self):
        self.write(M.WORKFLOW, 'new policy')
        newer = self.save('policy changed')
        self.g('update-ref', 'refs/remotes/origin/dev', newer)
        self.g('checkout', '-q', self.head)
        self.reject()

    def test_missing_proof_and_api_failure_release_fail_closed(self):
        merged = self.merge()
        event_path = self.root / 'event.json'
        event_path.write_text(json.dumps({'ref': 'refs/heads/main', 'after': merged}))
        output = self.root / 'outputs'
        env = {'GITHUB_REPOSITORY': REPO, 'GITHUB_SHA': merged, 'GITHUB_EVENT_PATH': str(event_path),
               'GITHUB_OUTPUT': str(output), 'GH_TOKEN': 'test-only'}
        output_text = io.StringIO()
        # The production helper emits a real Actions error. Keep this intentionally
        # rejected fixture from adding a misleading error annotation to successful CI.
        with redirect_stdout(output_text), patch.dict(os.environ, env), patch('sys.argv', ['helper', '--mode', 'release']), \
                patch.object(Path, 'cwd', return_value=self.root), patch.object(M.GitHub, 'get', side_effect=OSError('API unavailable')):
            self.assertEqual(M.main(), 1)
        self.assertIn('::error::Release blocked:', output_text.getvalue())
        self.assertIn('reuse=false', output.read_text())
        self.api.listed = []
        self.reject()


class WorkflowBindings(unittest.TestCase):
    def test_required_gate_and_fork_permissions(self):
        source = (ROOT / M.WORKFLOW).read_text()
        self.assertIn('name: Build and gates', source)
        self.assertIn('name: Full checks [${{ needs.plan.outputs.tree }}]', source)
        self.assertIn('pull_request.head.repo.full_name == github.repository', source)
        self.assertIn("needs.plan.outputs.reuse != 'true'", source)
        self.assertIn('persist-credentials: false', source)
        self.assertIn('fetch-depth: 0', source)
        self.assertIn('python3 scripts/check-docs-i18n.py', source)
        self.assertIn('scripts/ci-proof-reuse.py --mode dev-docs', source)
        self.assertNotIn('pull_request_target:', source)
        self.assertIn('"$FORK_PR" != true', source)
        self.assertIn('Record full-check completion', source)
        self.assertLess(source.index('Wire tests (compile only'), source.index('Record full-check completion'))
        self.assertIn('"$(git rev-parse HEAD^{tree})" == "$EXPECTED_TREE"', source)

    def test_release_fresh_build_proof_and_artifact_checks_remain(self):
        source = (ROOT / '.github/workflows/release.yml').read_text()
        self.assertIn('branches: [main]', source)
        self.assertIn("GhvrReleaseBuild: 'true'", source)
        self.assertIn('scripts/ci-proof-reuse.py --mode release', source)
        self.assertLess(source.index('--mode release'), source.index('scripts/ci-build.sh Release'))
        self.assertNotIn('Native presentation regression harnesses', source)
        self.assertIn('workflow_dispatch:', source)
        self.assertIn('scripts/ci-proof-reuse.py --mode release-resume', source)
        self.assertIn('cp scripts/release-upload.py "$RUNNER_TEMP/release-upload.py"', source)
        self.assertLess(source.index('cp scripts/release-upload.py'), source.index('git checkout --detach "$SOURCE_SHA"'))
        self.assertIn("steps.version.outputs.resuming != 'true'", source)
        self.assertIn('"$RUNNER_TEMP/release-upload.py" publish', source)
        self.assertNotIn('"$GITHUB_SHA"', source)
        for token in ('scripts/release-provenance.sh check', 'scripts/ci-build.sh Release',
                      'scripts/package-release.sh', 'scripts/check-bundle-format.sh', '--verify-tag',
                      'git merge-base --is-ancestor "$RELEASE_SOURCE_SHA"'):
            self.assertIn(token, source)


if __name__ == '__main__':
    unittest.main()
