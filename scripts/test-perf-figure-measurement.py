#!/usr/bin/env python3
"""Production figure-window policy/adapter and deliberate runtime defect controls."""
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
FIXTURE = ROOT / 'tests/GloomhavenVR.PerfFigureMeasurementTests'
PROJECT = FIXTURE / 'GloomhavenVR.PerfFigureMeasurementTests.csproj'
POLICY = ROOT / 'src/GloomhavenVR/Core/Perf/PerfFigureMeasurement.cs'
ADAPTER = ROOT / 'src/GloomhavenVR/Core/Perf/PerfMonitor.Figures.cs'
COUNTER = ROOT / 'src/GloomhavenVR/Core/Perf/PerfMonitor.Counters.cs'
monitor = (ROOT / 'src/GloomhavenVR/Core/Perf/PerfMonitor.cs').read_text()
sample = monitor[monitor.index('private static void Sample()'):monitor.index('private static void SampleGpuTime()')]
assert sample.index('ObserveFigureMeasurement(now, splitOn)') < sample.index('record: true'), 'Production Sample must gate before rolling a mixed frame'
assert 'AppendFigureMeasurement(sb);\n        VRLog.Info(Scope0, sb.ToString());\n        LogSteps' in monitor, 'Complete FRAME must carry its latched state before summary emission'
assert 'FigureMeasurement.Clear();' in monitor, 'Shutdown must clear the lifetime'
assert '!_figureBoundarySummary || PerfFrameSplit.NativeProbeComplete' in monitor, 'Boundary SPLIT must gate incomplete native capture'
assert 'if (includeNativeCapture)\n            PerfNativeLoopProbe.LogSummary' in monitor, 'Native callback summary must use the same completed-capture gate'
split = (ROOT / 'src/GloomhavenVR/Core/Perf/PerfFrameSplit.cs').read_text()
assert 'if (includeNativeCapture) AppendDebugLogic(sb);' in split, 'Unity profiler capture must obey the boundary gate too'
env = dict(os.environ)
env['PATH'] = str(Path.home()/'.dotnet') + os.pathsep + env['PATH']
subprocess.run(['dotnet', 'run', '--project', str(PROJECT), '-c', 'Release'], check=True, env=env)
variants = [
    ('premature-steady', POLICY, '&& now >= _steadyAfter', '', 'Two-second preparation guard must not be bypassed'),
    ('ignore-preparation', POLICY, 'if (!applied) _steadyAfter = now + SettleSeconds;', '', 'Delayed application must restart the quiet guard'),
    ('ignore-cloth', POLICY, '&& Effects == other.Effects && Cloth == other.Cloth;', '&& Effects == other.Effects;', 'Cloth is a confound'),
    ('mislabel-old-state', ADAPTER, 'if (_frameCount >= MinMarkFrames) LogSummary(elapsed);', '_figureWindowSettings = FigureMeasurement.Current;\n        if (_frameCount >= MinMarkFrames) LogSummary(elapsed);', 'Old window must retain OLD settings'),
    ('keep-mixed-steps', ADAPTER, 'step.FrameSeconds = 0d; step.FrameCalls = 0;', 'step.FrameSeconds += 0d; step.FrameCalls += 0;', 'Mixed frame\'s nested steps'),
    ('early-tally', COUNTER, '_frame += amount;', 'WindowTotal += amount; _frame += amount;\n            if (_frame > WindowWorstFrame) WindowWorstFrame = _frame;', 'Mixed frame\'s direct tally additions'),
    ('unfinished-native', ADAPTER, '_figureBoundarySummary = true;', '_figureBoundarySummary = false;', 'Boundary summary must exclude unfinished native captures'),
]
with tempfile.TemporaryDirectory(prefix='ghvr-perf-measure-') as directory:
    dest = Path(directory)
    for path in FIXTURE.glob('*'):
        if path.is_file(): shutil.copy2(path, dest/path.name)
    for name, source, old, new, reason in variants:
        text = source.read_text()
        assert text.count(old) == 1, name + ' production mutation anchor changed'
        broken = dest/(name+'.txt')
        broken.write_text(text.replace(old, new))
        result = subprocess.run(['dotnet', 'run', '--project', str(dest/PROJECT.name), '-c', 'Release',
            '-p:PolicySource='+str(broken if source == POLICY else POLICY),
            '-p:AdapterSource='+str(broken if source == ADAPTER else ADAPTER),
            '-p:CounterSource='+str(broken if source == COUNTER else COUNTER)],
            env=env, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        assert result.returncode != 0 and reason in result.stdout and 'error CS' not in result.stdout, name + ': runtime defect was not caught\n' + result.stdout
        print('PASS runtime negative control: '+name)
print('Perf figure measurement: production binding checks and 7 runtime defect controls passed.')
