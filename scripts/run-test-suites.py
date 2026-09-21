#!/usr/bin/env python3
"""Bounded parallel execution of unchanged harnesses, with complete, ordered evidence.

A suite owns one project and runs its negative controls serially. Different suites use
separate project bin/obj directories or mktemp fixtures. Checkout/suite locks also
serialize overlapping invocations. NuGet's package cache supports concurrent restore;
compiler/MSBuild daemons are disabled so the scheduler owns the process lifetime.
"""
from __future__ import annotations

import argparse
import fcntl
import hashlib
import json
import math
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import tempfile
import time
import uuid

ROOT = Path(__file__).resolve().parent.parent
MANIFEST = ROOT / 'scripts/test-suites.json'


def read_limit(path):
    try:
        value = Path(path).read_text().strip()
        return None if value == 'max' else int(value)
    except (OSError, ValueError):
        return None


def resource_budget():
    """Respect CPU affinity, cgroup v2 limits and currently available memory."""
    cpus = len(os.sched_getaffinity(0)) if hasattr(os, 'sched_getaffinity') else os.cpu_count() or 1
    try:
        quota, period = Path('/sys/fs/cgroup/cpu.max').read_text().split()
        if quota != 'max':
            cpus = min(cpus, max(1, math.ceil(int(quota) / int(period))))
    except (OSError, ValueError):
        pass
    available = None
    try:
        match = re.search(r'^MemAvailable:\s+(\d+) kB', Path('/proc/meminfo').read_text(), re.M)
        if match:
            available = int(match[1]) * 1024
    except OSError:
        pass
    limit = read_limit('/sys/fs/cgroup/memory.max')
    used = read_limit('/sys/fs/cgroup/memory.current')
    if limit is not None and used is not None:
        available = min(available or limit, max(0, limit - used))
    return cpus, available


def default_jobs(cpus=None, memory=None):
    if cpus is None:
        cpus, memory = resource_budget()
    # Leave room for the OS and source checks; each managed harness gets two CPUs.
    memory_jobs = max(1, (memory - 1024**3) // (2 * 1024**3)) if memory is not None else 2
    return max(1, min(8, max(1, cpus // 2), memory_jobs))


def load_manifest(path):
    raw = path.read_bytes()
    data = json.loads(raw)
    if data.get('schema_version') != 1 or not isinstance(data.get('suites'), list):
        raise ValueError('Unsupported or incomplete test manifest')
    seen = set()
    for suite in data['suites']:
        name = suite.get('id', '')
        command = suite.get('command')
        groups = suite.get('groups')
        if not re.fullmatch(r'[a-z0-9][a-z0-9-]*', name) or name in seen:
            raise ValueError(f'Invalid or duplicate suite ID: {name}')
        if not isinstance(command, list) or not command or not all(isinstance(x, str) and x for x in command):
            raise ValueError(f'Invalid command: {name}')
        if not isinstance(groups, list) or not groups or len(set(groups)) != len(groups) or not set(groups) <= {'local', 'ci', 'source'}:
            raise ValueError(f'Invalid groups: {name}')
        if not isinstance(suite.get('weight', 1), int) or suite.get('weight', 1) < 1:
            raise ValueError(f'Invalid scheduling weight: {name}')
        seen.add(name)
    if not seen:
        raise ValueError('Empty test manifest')
    return data['suites'], hashlib.sha256(raw).hexdigest()


def parse_shard(value):
    try:
        index, count = map(int, value.split('/'))
        if count < 1 or not 0 <= index < count:
            raise ValueError()
        return index, count
    except ValueError:
        raise argparse.ArgumentTypeError('Shard must be zero-based INDEX/COUNT, with 0 <= INDEX < COUNT')


def selected_suites(suites, group, shard):
    members = [s for s in suites if group in s['groups']]
    if not members or shard[1] > len(members):
        raise ValueError('Missing group or more shards than suites')
    return [s for i, s in enumerate(members) if i % shard[1] == shard[0]]


def atomic_json(path, value):
    temp = path.with_suffix('.tmp')
    temp.write_text(json.dumps(value, indent=2) + '\n')
    temp.replace(path)


def terminate_group(proc, sig):
    try:
        os.killpg(proc.pid, sig)
    except ProcessLookupError:
        pass


def execute(suites, jobs, output, root, manifest_hash, group, shard):
    output.mkdir(parents=True, exist_ok=True)
    if any(output.iterdir()):
        raise ValueError(f'Output directory must be empty: {output}')
    # Even simultaneous callers choosing the same explicit directory cannot overwrite logs.
    with (output / '.runner-owner').open('x') as marker:
        marker.write(str(os.getpid()) + '\n')
    locks = root / '.planning/debug/test-locks'
    locks.mkdir(parents=True, exist_ok=True)
    # Script length estimates mutation work until measured timings justify new weights.
    # Long suites start first; reports and complete log replay retain manifest order.
    pending = sorted(suites, key=lambda s: s.get('weight', 1), reverse=True)
    running = {}
    results = {}
    cancelled = [0]
    previous = {}
    started = time.monotonic()
    report = {'schema_version': 1, 'manifest_sha256': manifest_hash, 'group': group,
              'shard': list(shard), 'jobs': jobs, 'expected_suite_ids': [s['id'] for s in suites]}
    for sig in (signal.SIGINT, signal.SIGTERM):
        previous[sig] = signal.signal(sig, lambda number, _frame: cancelled.__setitem__(0, number))
    print(f'Test suites: {len(suites)}, jobs={jobs}, shard={shard[0]}/{shard[1]}, logs={output}', flush=True)
    try:
        while pending or running:
            if cancelled[0]:
                for name, entry in running.items():
                    terminate_group(entry['process'], signal.SIGTERM)
                deadline = time.monotonic() + 2
                while time.monotonic() < deadline and any(e['process'].poll() is None for e in running.values()):
                    time.sleep(.05)
                # Kill groups even if their shell exited: a descendant can ignore TERM.
                for entry in running.values():
                    terminate_group(entry['process'], signal.SIGKILL)
                for suite in pending:
                    results[suite['id']] = {'id': suite['id'], 'status': 'cancelled', 'exit_code': None, 'duration_seconds': 0}
                pending.clear()
            for suite in list(pending):
                if len(running) >= jobs or cancelled[0]:
                    break
                name = suite['id']
                lock = (locks / f'{name}.lock').open('w')
                try:
                    fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
                except BlockingIOError:
                    lock.close()
                    continue
                log_path = output / f'{name}.log'
                log = log_path.open('wb')
                scratch = Path(tempfile.mkdtemp(prefix=f'{name}-', dir=output))
                env = os.environ.copy()
                env.update(TMPDIR=str(scratch), DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER='1',
                           MSBUILDDISABLENODEREUSE='1', UseSharedCompilation='false',
                           DOTNET_PROCESSOR_COUNT='2')
                # No shell interpretation: commands are explicit argument vectors.
                try:
                    proc = subprocess.Popen(suite['command'], cwd=root, env=env, stdout=log,
                                            stderr=subprocess.STDOUT, start_new_session=True)
                except OSError as exc:
                    log.write(f'Could not launch suite: {exc}\n'.encode())
                    log.close()
                    lock.close()
                    shutil.rmtree(scratch)
                    results[name] = {'id': name, 'status': 'failed', 'exit_code': 127,
                                     'duration_seconds': 0, 'log': log_path.name}
                else:
                    running[name] = {'process': proc, 'log': log, 'lock': lock, 'scratch': scratch,
                                     'started': time.monotonic(), 'log_path': log_path}
                pending.remove(suite)
            for name, entry in list(running.items()):
                code = entry['process'].poll()
                if code is None:
                    continue
                # Prevent a completed shell from leaving fixture descendants behind.
                terminate_group(entry['process'], signal.SIGKILL)
                entry['log'].close()
                entry['lock'].close()
                shutil.rmtree(entry['scratch'], ignore_errors=True)
                duration = time.monotonic() - entry['started']
                status = 'cancelled' if cancelled[0] else ('passed' if code == 0 else 'failed')
                results[name] = {'id': name, 'status': status, 'exit_code': code,
                                 'duration_seconds': round(duration, 3), 'log': entry['log_path'].name}
                print(f'[{status}] {name}: {duration:.1f}s', flush=True)
                del running[name]
            if pending or running:
                time.sleep(.05)
    finally:
        for entry in running.values():
            terminate_group(entry['process'], signal.SIGKILL)
            entry['process'].wait()
            entry['log'].close()
            entry['lock'].close()
            shutil.rmtree(entry['scratch'], ignore_errors=True)
        for sig, handler in previous.items():
            signal.signal(sig, handler)
        for suite in suites:
            results.setdefault(suite['id'], {'id': suite['id'], 'status': 'missing', 'exit_code': None, 'duration_seconds': 0})
        ordered = [results[s['id']] for s in suites]
        for result in ordered:
            if 'log' in result:
                result['log_sha256'] = hashlib.sha256((output / result['log']).read_bytes()).hexdigest()
        report.update(results=ordered, duration_seconds=round(time.monotonic()-started, 3),
                      passed=all(r['status'] == 'passed' and r['exit_code'] == 0 for r in ordered))
        atomic_json(output / 'results.json', report)
    # Preserve every original assertion/count line; concurrency never interleaves suite output.
    for result in report['results']:
        print(f"\n--- {result['id']} ({result['status']}) ---", flush=True)
        if 'log' in result:
            with (output / result['log']).open('r', errors='replace') as log:
                shutil.copyfileobj(log, sys.stdout)
    print(f"\nTest suite result: {'PASS' if report['passed'] else 'FAIL'}; "
          f"{len(results)}/{len(suites)} recorded; {report['duration_seconds']:.1f}s; {output / 'results.json'}", flush=True)
    return 128 + cancelled[0] if cancelled[0] else (0 if report['passed'] else 1)


def verify_results(paths, suites, manifest_hash, group, count):
    files = []
    for name in paths:
        path = Path(name)
        files.extend(sorted(path.rglob('results.json')) if path.is_dir() else [path])
    if len(files) != count:
        raise ValueError(f'Expected {count} shard reports, found {len(files)}')
    seen = set()
    for path in files:
        report = json.loads(path.read_text())
        shard = report.get('shard', [])
        if len(shard) != 2 or shard[1] != count or not isinstance(shard[0], int) or not 0 <= shard[0] < count or shard[0] in seen:
            raise ValueError(f'Duplicate or invalid shard: {path}')
        seen.add(shard[0])
        expected = [s['id'] for s in selected_suites(suites, group, tuple(shard))]
        if report.get('schema_version') != 1 or report.get('manifest_sha256') != manifest_hash or report.get('group') != group or report.get('expected_suite_ids') != expected or report.get('passed') is not True:
            raise ValueError(f'Stale, incomplete or unsuccessful report: {path}')
        results = report.get('results', [])
        if [r.get('id') for r in results] != expected:
            raise ValueError(f'Missing or duplicate suite results: {path}')
        for result in results:
            if result.get('status') != 'passed' or result.get('exit_code') != 0:
                raise ValueError(f'Failed suite: {result.get("id")}')
            log = result.get('log', '')
            if log != result['id'] + '.log' or hashlib.sha256((path.parent / log).read_bytes()).hexdigest() != result.get('log_sha256'):
                raise ValueError(f'Missing or altered suite log: {result["id"]}')
    print(f'PASS: complete {group} coverage across {count} shards; {sum(group in s["groups"] for s in suites)} suites')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--group', required=True, choices=('local', 'ci', 'source'))
    parser.add_argument('--jobs', type=int, default=None)
    parser.add_argument('--shard', type=parse_shard, default=(0, 1))
    parser.add_argument('--output-dir', type=Path)
    parser.add_argument('--list', action='store_true', help='Print selected suite IDs as JSON without running tests')
    parser.add_argument('--verify-results', nargs='+', help='Verify reports and logs from all shards')
    parser.add_argument('--shard-count', type=int, default=1)
    args = parser.parse_args()
    try:
        suites, digest = load_manifest(MANIFEST)
        selected = selected_suites(suites, args.group, args.shard)
        if args.list:
            print(json.dumps([s['id'] for s in selected]))
            return 0
        if args.verify_results:
            verify_results(args.verify_results, suites, digest, args.group, args.shard_count)
            return 0
        jobs = args.jobs if args.jobs is not None else int(os.environ.get('GHVR_TEST_JOBS', default_jobs()))
        if not 1 <= jobs <= 64:
            raise ValueError('Jobs must be between 1 and 64')
        output = args.output_dir or ROOT / '.planning/debug/test-runs' / (time.strftime('%Y%m%d-%H%M%S') + '-' + uuid.uuid4().hex[:8])
        return execute(selected, jobs, output.resolve(), ROOT, digest, args.group, args.shard)
    except (OSError, ValueError, KeyError, TypeError) as exc:
        print(f'Test runner error: {exc}', file=sys.stderr)
        return 1


if __name__ == '__main__':
    sys.exit(main())
