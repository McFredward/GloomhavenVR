#!/usr/bin/env python3
"""Generate and measure the deterministic calibration delivery in an owned run."""
import contextlib
import importlib.util
import json
from pathlib import Path
import sys
import tempfile

from town_run_retention import cleanup_completed_run

ROOT = Path(__file__).resolve().parents[1]
sys.dont_write_bytecode = True
sys.path.insert(0, str(ROOT / 'scripts/stereo-video'))


def execute(path, arguments, log):
    spec = importlib.util.spec_from_file_location('stereo_calibration_' + path.stem, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    previous = sys.argv
    try:
        sys.argv = [str(path), *arguments]
        with log.open('w') as stream, contextlib.redirect_stdout(stream), contextlib.redirect_stderr(stream):
            # Both real entry points own, kill and reap their ffmpeg processes. Running
            # in-process preserves their interruption cleanup rather than orphaning a
            # nested process group when the umbrella suite is cancelled.
            return module.main()
    finally:
        sys.argv = previous


def main():
    output = ROOT / '.planning/debug/stereo-video-tools'
    output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=output))
    completion = run / 'completion.log'
    completion.write_text('evidence: ' + str(run) + '\n')
    print('evidence: ' + str(run), flush=True)
    status = 1
    try:
        for name, path, arguments in [
            ('generate', ROOT / 'scripts/stereo-video/build.py', ['--out-dir', str(run / 'media')]),
            ('measure', ROOT / 'tests/stereo-video-tools/check.py', ['--media-dir', str(run / 'media'), '--out-dir', str(run / 'checks')]),
        ]:
            code = execute(path, arguments, run / (name + '.log'))
            print((run / (name + '.log')).read_text(), end='', flush=True)
            if code:
                return code
        status = 0
        return 0
    finally:
        receipt = cleanup_completed_run('stereo-video-tools', status, completion, ROOT, process_reaped=True)
        (run / 'cache-cleanup.json').write_text(json.dumps(receipt, indent=2) + '\n')


if __name__ == '__main__':
    raise SystemExit(main())
