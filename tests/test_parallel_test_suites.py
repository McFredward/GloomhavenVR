"""Exercise real processes, cancellation, evidence and inventory rather than scheduler mocks."""
import contextlib
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import time
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location('suite_runner', ROOT / 'scripts/run-test-suites.py')
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)

# Inventories frozen from dev 0e84862c before replacing the sequential gate lists.
LOCAL_INVENTORY = set("""
card-bindings
native-playback
native-video
reward-showcase
modal-close
vr-options
vr-options-close
town-service-options
reward-pose
conversion-rollback
panel-material
panel-ink
video-playback
hint
quest-hint
tutorial-scope
tutorial-controller
retry-start
window-reflow
shared-window-reflow
board-refresh
card-loss-modal
map-flow
map-button
round-card
burn-layout
burn-replay
pick-tray
mr-backing
mr-backing-materialise
mr-backing-animation
card-scene-lifetime
card-pool-lifetime
burn-material-ownership
permanent-quest-log
guildmaster-table
mr-scenario
guildmaster-room
map-icon-picking
remote-burn-sequencing
burn-completion
item-burn
item-appearance
flight-timing
figure-hold
presentation-send
""".split())
CI_INVENTORY = set("""
card-bindings
native-playback
board-refresh
card-loss-modal
map-flow
map-button
round-card
burn-layout
burn-replay
pick-tray
mr-backing
mr-backing-materialise
mr-backing-animation
card-scene-lifetime
card-pool-lifetime
burn-material-ownership
permanent-quest-log
guildmaster-table
mr-scenario
guildmaster-room
map-icon-picking
remote-burn-sequencing
burn-completion
item-burn
item-appearance
flight-timing
figure-hold
self-update-dialog
native-video
reward-showcase
modal-close
vr-options
vr-options-close
town-service-options
reward-pose
conversion-rollback
panel-material
panel-ink
banner-pose
video-playback
hint
quest-hint
tutorial-scope
tutorial-controller
retry-start
quest-seat
window-reflow
shared-window-reflow
""".split())
SOURCE_INVENTORY = set("""
patch-inventory
frame-order
mirrors
partial-order
instrument-writes
remote-defaults
wire-coverage
tune-fields
desync-surface
hw-verify
options-coverage
card-identity-mask
mirror-dials
enum-arrays
""".split())


class ParallelSuitesTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)

    def tearDown(self):
        self.temp.cleanup()

    def suite(self, name, code):
        return {'id': name, 'command': [sys.executable, '-c', code], 'groups': ['local']}

    def run_suites(self, suites, jobs=2, name='output', shard=(0, 1)):
        output = self.root / name
        with contextlib.redirect_stdout(io.StringIO()) as log:
            code = runner.execute(suites, jobs, output, self.root, 'hash', 'local', shard)
        return code, json.loads((output / 'results.json').read_text()), log.getvalue()

    def test_overlap_and_deterministic_complete_output(self):
        suites = [self.suite(name, f'import time; print("{name}-start", time.time(), flush=True); time.sleep(.35); print("{name}-end", time.time())') for name in ['first', 'second']]
        code, report, log = self.run_suites(suites)
        self.assertEqual(code, 0)
        intervals = []
        for result in report['results']:
            lines = (self.root / 'output' / result['log']).read_text().splitlines()
            intervals.append([float(line.split()[1]) for line in lines])
        self.assertLess(max(i[0] for i in intervals), min(i[1] for i in intervals))
        self.assertLess(log.index('first-start'), log.index('second-start'))
        self.assertEqual([r['id'] for r in report['results']], ['first', 'second'])

    def test_failure_does_not_skip_other_suites(self):
        code, report, log = self.run_suites([self.suite('bad', 'raise SystemExit(7)'), self.suite('good', 'print("314 assertions passed")')])
        self.assertEqual(code, 1)
        self.assertFalse(report['passed'])
        self.assertEqual([r['exit_code'] for r in report['results']], [7, 0])
        self.assertIn('314 assertions passed', log)

    def test_launch_error_is_failure(self):
        suite = self.suite('missing', '')
        suite['command'] = ['/no/such/program']
        code, report, _ = self.run_suites([suite])
        self.assertEqual(code, 1)
        self.assertEqual(report['results'][0]['exit_code'], 127)

    def test_jobs_one_is_serial(self):
        marker = self.root / 'marker'
        suites = [self.suite('first', f'import time,pathlib; time.sleep(.15); pathlib.Path({str(marker)!r}).write_text("done")'), self.suite('second', f'from pathlib import Path; assert Path({str(marker)!r}).exists()')]
        self.assertEqual(self.run_suites(suites, jobs=1)[0], 0)

    def test_resource_defaults_bounded(self):
        self.assertEqual(runner.default_jobs(16, 32*1024**3), 8)
        self.assertEqual(runner.default_jobs(2, 7*1024**3), 1)
        self.assertEqual(runner.default_jobs(128, 4*1024**3), 1)
        self.assertEqual(runner.default_jobs(128, 1024**4), 8)
        self.assertEqual(runner.default_jobs(1, 0), 1)

    def test_original_inventory_and_shards(self):
        suites, _ = runner.load_manifest(runner.MANIFEST)
        local = {s['id'] for s in runner.selected_suites(suites, 'local', (0, 1))}
        ci = {s['id'] for s in runner.selected_suites(suites, 'ci', (0, 1))}
        physical_town = {'town-service-catalog', 'town-service-mirror', 'town-service-interaction',
                         'town-service-decor', 'town-ritual-transactions', 'town-flame',
                         'town-service-workspace', 'town-ritual-layout', 'town-enhancement-handoff',
                         'town-facial-landmarks', 'town-service-clearance', 'town-merchant-handoff',
                         'town-public-catalog', 'town-item-transfer', 'town-card-slots'}
        map_hotfix = {'aoe-control', 'city-event-button', 'map-tooltip', 'map-tooltip-transport'}
        self.assertEqual(local, LOCAL_INVENTORY | map_hotfix | {"town-service-setting", "town-residents", "town-service-lighting", "town-face", "town-activity"} | physical_town)
        self.assertEqual(ci, CI_INVENTORY | map_hotfix | {"town-service-setting", "town-residents", "town-service-lighting", "town-activity-portable"})
        self.assertEqual({s['id'] for s in runner.selected_suites(suites, 'source', (0, 1))}, SOURCE_INVENTORY)
        self.assertEqual(len(local), 70)
        self.assertEqual(len(ci), 56)
        self.assertEqual(local-ci, {'presentation-send', 'town-face', 'town-activity'} | physical_town)
        self.assertEqual(ci-local, {'self-update-dialog', 'banner-pose', 'quest-seat', 'town-activity-portable'})
        self.assertEqual(len(runner.selected_suites(suites, 'source', (0, 1))), 14)
        partition = [s['id'] for i in range(4) for s in runner.selected_suites(suites, 'ci', (i, 4))]
        self.assertEqual(len(partition), len(set(partition)))
        self.assertEqual(set(partition), ci)
        for suite in suites:
            self.assertTrue((ROOT / suite['command'][1]).is_file())

    def test_evidence_rejects_missing_duplicate_failed_and_modified_logs(self):
        suites = [self.suite('first', 'print("12 assertions")'), self.suite('second', 'print("13 assertions")')]
        for index in range(2):
            self.run_suites([suites[index]], name=f'shard-{index}', shard=(index, 2))
        paths = [str(self.root / f'shard-{i}') for i in range(2)]
        with contextlib.redirect_stdout(io.StringIO()):
            runner.verify_results(paths, suites, 'hash', 'local', 2)
        for bad in [paths[:1], [paths[0], paths[0]]]:
            with self.assertRaises(ValueError):
                runner.verify_results(bad, suites, 'hash', 'local', 2)
        report_path = self.root / 'shard-0/results.json'
        original = report_path.read_text()
        data = json.loads(original)
        for mutation in ['missing', 'failed', 'stale']:
            altered = json.loads(original)
            if mutation == 'missing':
                altered['results'] = []
            elif mutation == 'failed':
                altered['results'][0]['exit_code'] = 3
            else:
                altered['manifest_sha256'] = 'other'
            report_path.write_text(json.dumps(altered))
            with self.assertRaises(ValueError):
                runner.verify_results(paths, suites, 'hash', 'local', 2)
        report_path.write_text(original)
        (self.root / 'shard-0/first.log').write_text('tampered')
        with self.assertRaises(ValueError):
            runner.verify_results(paths, suites, 'hash', 'local', 2)

    def start_worker(self, suites, output):
        # A real separate runner lets signals and cross-invocation locks be exercised.
        code = f'''import importlib.util,pathlib
spec=importlib.util.spec_from_file_location('runner',{str(ROOT / 'scripts/run-test-suites.py')!r})
r=importlib.util.module_from_spec(spec);spec.loader.exec_module(r)
raise SystemExit(r.execute({suites!r}, 1, pathlib.Path({str(output)!r}), pathlib.Path({str(self.root)!r}), 'hash', 'local', (0,1)))'''
        return subprocess.Popen([sys.executable, '-c', code], stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)

    def wait_file(self, path, timeout=5):
        deadline = time.monotonic() + timeout
        while not path.exists() and time.monotonic() < deadline:
            time.sleep(.02)
        self.assertTrue(path.exists())

    def test_cancellation_kills_children_and_records_pending(self):
        pidfile = self.root / 'child.pid'
        child = f'import signal,time,pathlib,os; signal.signal(signal.SIGTERM, signal.SIG_IGN); pathlib.Path({str(pidfile)!r}).write_text(str(os.getpid())); time.sleep(60)'
        code = f'import subprocess,sys,pathlib,time; p=subprocess.Popen([sys.executable,"-c",{child!r}]); time.sleep(60)'
        output = self.root / 'cancel'
        process = self.start_worker([self.suite('running', code), self.suite('pending', 'raise SystemExit(0)')], output)
        try:
            self.wait_file(pidfile)
            process.send_signal(signal.SIGTERM)
            _, error = process.communicate(timeout=8)
            self.assertEqual(process.returncode, 143, error.decode())
            report = json.loads((output / 'results.json').read_text())
            self.assertFalse(report['passed'])
            self.assertEqual([r['status'] for r in report['results']], ['cancelled', 'cancelled'])
            # A reparented zombie has stopped executing; init may reap it asynchronously.
            stat = Path('/proc') / pidfile.read_text() / 'stat'
            deadline = time.monotonic()+3
            while stat.exists() and stat.read_text().split()[2] != 'Z' and time.monotonic()<deadline:
                time.sleep(.02)
            self.assertTrue(not stat.exists() or stat.read_text().split()[2] == 'Z')
        finally:
            if process.poll() is None:
                process.kill()
                process.wait()

    def test_overlapping_invocations_lock_same_project(self):
        marker = self.root / 'exclusive'
        started = self.root / 'started'
        code = f'import pathlib,time; p=pathlib.Path({str(marker)!r}); f=p.open("x"); pathlib.Path({str(started)!r}).touch(); time.sleep(.3); f.close(); p.unlink()'
        suite = self.suite('same-project', code)
        first = self.start_worker([suite], self.root / 'one')
        self.wait_file(started)
        second = self.start_worker([suite], self.root / 'two')
        for process in (first, second):
            _, error = process.communicate(timeout=8)
            self.assertEqual(process.returncode, 0, error.decode())

    def test_full_wire_gate_rejects_partial_inventory_options(self):
        for option in ['--group=ci', '--shard=0/4', '--list', '--verify-results=x', '--help', '--unknown']:
            result = subprocess.run(['bash', str(ROOT / 'scripts/wire-tests.sh'), option],
                                    capture_output=True, text=True)
            self.assertEqual(result.returncode, 2)
            self.assertIn('partial/group/list options', result.stderr)

    def test_nested_cgroup_resource_limits_and_subtree_mount(self):
        proc = self.root / 'proc'
        (proc / 'self').mkdir(parents=True)
        mount = self.root / 'cgroup'
        current = mount / 'user/session'
        current.mkdir(parents=True)
        (proc / 'self/cgroup').write_text('0::/tenant/user/session\n')
        (proc / 'self/mountinfo').write_text(f'1 0 0:1 /tenant {mount} rw - cgroup2 cgroup2 rw\n')
        (proc / 'meminfo').write_text(f'MemAvailable: {32*1024*1024} kB\n')
        (current / 'cpu.max').write_text('max 100000')
        (current.parent / 'cpu.max').write_text('300000 100000')
        (mount / 'cpu.max').write_text('200000 100000')
        (current / 'memory.max').write_text(str(10*1024**3))
        (current / 'memory.current').write_text(str(1024**3))
        (mount / 'memory.max').write_text(str(8*1024**3))
        (mount / 'memory.current').write_text(str(2*1024**3))
        cpus, memory = runner.resource_budget(proc)
        self.assertLessEqual(cpus, 2)
        self.assertEqual(memory, 6*1024**3)
        self.assertEqual(runner.cgroup_v2_ancestors(proc), [current, current.parent, mount])

    def test_fixtures_and_compiler_ids_are_isolated(self):
        # TMPDIR must stay outside the checkout. Otherwise standalone mutation projects
        # silently inherit Directory.Build.props and different warning/build semantics.
        code = f'''import os,pathlib
p=pathlib.Path(os.environ['TMPDIR']).resolve()
assert not p.is_relative_to(pathlib.Path({str(self.root)!r}))
assert len(str(p / ('CoreFxPipe_' + os.environ['SharedCompilationId'])).encode()) < 104
assert os.environ['UseSharedCompilation'] == 'true'
print(os.environ['SharedCompilationId'])'''
        # Even an explicitly configured TMPDIR under the checkout must not change fixtures.
        with mock.patch.object(tempfile, 'tempdir', str(self.root)):
            _, report, _ = self.run_suites([self.suite('one', code), self.suite('two', code)])
        self.assertTrue(report['passed'])
        ids = [r['compiler_id'] for r in report['results']]
        self.assertEqual(len(set(ids)), 2)

    @unittest.skipUnless(os.environ.get('GHVR_TEST_COMPILER_SMOKE') == '1', 'opt-in real SDK/compiler lifecycle proof')
    def test_real_private_compiler_reuse_group_and_cleanup(self):
        # The poisoned parent makes the historical TMPDIR-under-checkout error observable.
        (self.root / 'Directory.Build.props').write_text('<Project><PropertyGroup><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup></Project>')
        suites = [self.suite(name, self.compiler_probe(name, False)) for name in ['compiler-one', 'compiler-two']]
        _, report, log = self.run_suites(suites)
        self.assertTrue(report['passed'], log)
        pids = []
        for name in ['compiler-one', 'compiler-two']:
            data = json.loads((self.root / (name + '.json')).read_text())
            self.assertEqual(data['first_pid'], data['second_pid'])
            self.assertEqual(data['server_pgid'], data['suite_pgid'])
            pids.append(data['second_pid'])
            self.assert_process_stopped(data['second_pid'])
        self.assertEqual(len(set(pids)), 2)

    @unittest.skipUnless(os.environ.get('GHVR_TEST_COMPILER_SMOKE') == '1', 'opt-in real SDK/compiler lifecycle proof')
    def test_real_private_compiler_cancellation_cleanup(self):
        output = self.root / 'compiler-cancel'
        name = 'compiler-cancel'
        process = self.start_worker([self.suite(name, self.compiler_probe(name, True))], output)
        try:
            marker = self.root / (name + '.json')
            self.wait_file(marker, timeout=40)
            data = json.loads(marker.read_text())
            process.send_signal(signal.SIGTERM)
            _, error = process.communicate(timeout=8)
            self.assertEqual(process.returncode, 143, error.decode())
            self.assert_process_stopped(data['second_pid'])
        finally:
            if process.poll() is None:
                process.send_signal(signal.SIGTERM)
                process.wait(timeout=8)

    def assert_process_stopped(self, pid):
        stat = Path('/proc') / str(pid) / 'stat'
        deadline = time.monotonic() + 3
        while stat.exists() and stat.read_text().split()[2] != 'Z' and time.monotonic() < deadline:
            time.sleep(.02)
        self.assertTrue(not stat.exists() or stat.read_text().split()[2] == 'Z', f'Leaked process {pid}')

    def compiler_probe(self, name, wait):
        # Two actual changed builds prove reuse rather than a no-op incremental build.
        return f'''import os,pathlib,subprocess,json,time
root=pathlib.Path(os.environ['TMPDIR'])/'probe';root.mkdir()
(root/'Probe.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><GenerateDocumentationFile>true</GenerateDocumentationFile><NoWarn>1591</NoWarn></PropertyGroup></Project>')
source=root/'Probe.cs'
pipe=('-pipename:'+os.environ['SharedCompilationId']).encode()
dotnet=pathlib.Path(os.environ.get('DOTNET_ROOT',str(pathlib.Path.home()/'.dotnet')))/'dotnet'
def build(n):
 source.write_text('/// <see cref="MissingMember"/>\\npublic class Probe {{ public int Value => '+str(n)+'; }}')
 result=subprocess.run([str(dotnet),'build',str(root/'Probe.csproj'),'-v:normal','--nologo'],text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
 print(result.stdout,flush=True)
 assert result.returncode==0,'Standalone fixture inherited error policy'
 assert 'warning CS1574' in result.stdout,'Probe must reach the real compiler and diagnostic'
 assert 'server processed compilation' in result.stdout,'Compiler unexpectedly fell back to standalone mode'
 for path in pathlib.Path('/proc').glob('[0-9]*/cmdline'):
  try:
   args=path.read_bytes().split(b'\\0')
   if pipe in args: return int(path.parent.name)
  except (OSError,ProcessLookupError): pass
 raise AssertionError('Private compiler server missing')
a=build(1);b=build(2)
data=dict(first_pid=a,second_pid=b,server_pgid=os.getpgid(b),suite_pgid=os.getpgrp())
pathlib.Path({str(self.root / (name + '.json'))!r}).write_text(json.dumps(data))
print(json.dumps(data),flush=True)
if {wait!r}: time.sleep(60)
'''

    def test_existing_output_is_rejected(self):
        output = self.root / 'output'
        output.mkdir()
        (output / 'results.json').write_text('{}')
        with self.assertRaises(ValueError):
            self.run_suites([self.suite('one', 'pass')])


if __name__ == '__main__':
    unittest.main()
