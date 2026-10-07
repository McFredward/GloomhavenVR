#!/usr/bin/env python3
"""Run actual OpenXR input and navigation in Unity, including injected defect controls."""
import hashlib
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
unity = Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity'))
output = ROOT / '.planning/debug/quest-probe-input'
output.mkdir(parents=True, exist_ok=True)
run = Path(tempfile.mkdtemp(prefix='run-', dir=output))
project = run / 'project'
assets = project / 'Assets'
(assets / 'Editor').mkdir(parents=True)
(project / 'Packages').mkdir()
(project / 'ProjectSettings').mkdir()
shutil.copy2(ROOT / 'unity/GloomhavenVR.Quest/Packages/manifest.json', project / 'Packages/manifest.json')
(project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
source = (ROOT / 'unity/GloomhavenVR.Quest/Assets/Quest/Runtime/QuestProbeInput.cs').read_text()
(assets / 'QuestProbeInput.cs').write_text(source)
(run / 'source.sha256').write_text(hashlib.sha256(source.encode()).hexdigest() + '\n')
for name, before, after in (
    ('AimDefect', 'aim ? "pointer" : "devicePose"', 'aim ? "devicePose" : "devicePose"'),
    ('TrackingDefect', '(state.ReadValue<int>() & 3) != 3', '(state.ReadValue<int>() & 1) != 1'),
    ('PivotDefect', 'origin.RotateAround(head.position, Vector3.up,', 'origin.RotateAround(origin.position, Vector3.up,'),
    ('ResumeDefect', 'ready = false; turnLatched = false;', 'ready = true; turnLatched = false;'),
    ('HeightDefect', 'origin.position += Vector3.up * (Mathf.Clamp(height, -1, 1)',
     'origin.position += Vector3.forward * (Mathf.Clamp(height, -1, 1)'),
):
    if source.count(before) != 1:
        raise RuntimeError('Defect seam changed: ' + name)
    (assets / (name + '.cs')).write_text(source.replace('namespace GloomhavenVR.Quest', 'namespace GloomhavenVR.' + name).replace(before, after))
shutil.copy2(ROOT / 'tests/quest-probe-input/ProbeInputFixture.cs', assets / 'Editor/ProbeInputFixture.cs')
environment = dict(os.environ, GHVR_INPUT_FIXTURE_OUTPUT=str(run / 'result.txt'))
result = subprocess.run([str(unity), '-batchmode', '-nographics', '-projectPath', str(project),
                         '-executeMethod', 'ProbeInputFixture.Start', '-logFile', str(run / 'unity.log')],
                        env=environment, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=300)
report = run / 'result.txt'
if report.is_file():
    print(report.read_text(), end='')
if result.returncode or not report.is_file() or not report.read_text().startswith("PASS: "):
    raise SystemExit('Real Unity input fixture failed; see ' + str(run / 'unity.log'))
print('Evidence: ' + str(run))
