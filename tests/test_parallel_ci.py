"""Exercise the shipped Actions gates; protect parallel coverage and release topology."""
import importlib.util
import itertools
import os
from pathlib import Path
import re
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
WORKFLOW = (ROOT / '.github/workflows/ci.yml').read_text()
SPEC = importlib.util.spec_from_file_location('proof', ROOT / 'scripts/ci-proof-reuse.py')
PROOF = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PROOF)


def job(name):
    match = re.search(r'^  ' + re.escape(name) + r':\n(.*?)(?=^  \w+:\n|\Z)',
                      WORKFLOW, flags=re.M | re.S)
    if not match:
        raise AssertionError(f'Missing job: {name}')
    return match.group(1)


def step_script(block, name):
    # Deliberately use the actual workflow shell, not a Python reimplementation.
    step = block.split('      - name: ' + name + '\n', 1)[1].split('      - name:', 1)[0]
    shell = step.split('        run: |\n', 1)[1]
    return '\n'.join(line[10:] for line in shell.splitlines() if line.startswith('          '))


class ParallelWorkflowTests(unittest.TestCase):
    def test_matrix_and_proof_agree_on_every_shard(self):
        runtime = job('runtime_checks')
        shards = re.search(r'^        shard: \[([^\]]+)\]', runtime, re.M).group(1)
        self.assertEqual([int(value.strip()) for value in shards.split(',')],
                         list(range(PROOF.RUNTIME_SHARDS)))
        self.assertIn('name: Runtime suites [${{ matrix.shard }}/4]', runtime)
        self.assertIn('--group ci --jobs 2', runtime)
        self.assertIn('--shard ${{ matrix.shard }}/4', runtime)
        self.assertIn('fail-fast: false', runtime)
        self.assertIn('max-parallel: 4', runtime)
        self.assertNotIn('continue-on-error:', runtime)
        self.assertNotIn('exclude:', runtime)
        self.assertNotIn('include:', runtime)
        self.assertNotIn('needs: [source_checks', runtime)
        self.assertNotIn('build-runtimedeps.sh', runtime)

    def test_aggregator_waits_for_source_and_whole_matrix(self):
        full = job('full_checks')
        self.assertIn('needs: [plan, source_checks, runtime_checks]', full)
        self.assertIn('SOURCE_RESULT: ${{ needs.source_checks.result }}', full)
        self.assertIn('RUNTIME_RESULT: ${{ needs.runtime_checks.result }}', full)
        self.assertLess(full.index('name: ' + PROOF.PARALLEL_GATE), full.index('name: ' + PROOF.PROOF_STEP))
        self.assertNotIn('continue-on-error:', full)
        gate = step_script(full, PROOF.PARALLEL_GATE)
        statuses = ('success', 'failure', 'cancelled', 'skipped', '', 'timed_out')
        for source, runtime in itertools.product(statuses, repeat=2):
            with self.subTest(source=source, runtime=runtime):
                result = subprocess.run(['bash', '-euo', 'pipefail', '-c', gate],
                                        env=dict(os.environ, SOURCE_RESULT=source, RUNTIME_RESULT=runtime),
                                        stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
                self.assertEqual(result.returncode == 0, source == runtime == 'success')

    def test_reuse_cannot_turn_incomplete_checks_into_success(self):
        stable = job('build')
        self.assertIn('name: Build and gates', stable)
        self.assertIn('needs: [plan, full_checks]', stable)
        gate = step_script(stable, 'Require full checks or verified existing evidence')
        cases = [
            ('success', 'false', 'success', 'false', '', True),
            ('success', 'true', 'skipped', 'false', 'https://proof.invalid', True),
            ('success', 'true', 'skipped', 'true', 'https://proof.invalid', False),
            ('success', 'true', 'failure', 'false', 'https://proof.invalid', False),
            ('success', 'true', 'cancelled', 'false', 'https://proof.invalid', False),
            ('success', 'true', 'skipped', 'false', '', False),
            ('success', 'false', 'skipped', 'false', '', False),
            ('failure', 'false', 'success', 'false', '', False),
        ]
        with tempfile.TemporaryDirectory() as temporary:
            for plan, reuse, full, fork, proof, expected in cases:
                with self.subTest(plan=plan, reuse=reuse, full=full, fork=fork, proof=proof):
                    result = subprocess.run(['bash', '-euo', 'pipefail', '-c', gate],
                        env=dict(os.environ, PLAN_RESULT=plan, REUSED=reuse, FULL_RESULT=full,
                                 FORK_PR=fork, PROOF_URL=proof, GITHUB_STEP_SUMMARY=temporary + '/summary'),
                        stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
                    self.assertEqual(result.returncode == 0, expected)

    def test_independent_validation_keeps_optional_storage_and_secret_boundaries(self):
        runtime = job('runtime_checks')
        source = job('source_checks')
        full = job('full_checks')
        for block in (runtime, source, full):
            self.assertIn("needs.plan.outputs.reuse != 'true'", block)
            self.assertIn('persist-credentials: false', block)
        self.assertNotRegex(WORKFLOW, r'\$\{\{[^}]*secrets\.')
        self.assertNotIn('pull_request_target:', WORKFLOW)
        self.assertNotIn('permissions:', runtime)
        self.assertIn('actions/cache/restore@', runtime)
        self.assertNotIn('uses: actions/cache@', runtime)
        self.assertEqual(WORKFLOW.count('uses: actions/upload-artifact@'), 1)
        self.assertNotIn('upload-artifact', runtime + full)
        self.assertIn("inputs.upload_dev_build && github.ref == 'refs/heads/dev'", source)
        self.assertIn("needs.prune_dev_artifacts.outputs.can_upload == 'true'", source)
        self.assertIn('retention-days: 2', source)
        self.assertIn('bash scripts/ci-build.sh Release', source)
        self.assertIn('Wire tests (compile only', source)
        self.assertNotIn('Native presentation regression harnesses', source)
        self.assertIn('--group source --jobs 2', source)
        self.assertIn('command -v rg', runtime)


if __name__ == '__main__':
    unittest.main()
