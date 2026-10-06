#!/usr/bin/env python3
"""Run complete production RenderQuality against explicit provider boundaries and causal controls.

The fixture models XR resource setters/readbacks; it cannot establish headset pixels,
FOV correctness or native driver stability. Lifecycle call ordering is source-bound to
OpenXRBootstrap, rather than a fabricated fixture-only startup policy.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--source-root', type=Path, default=ROOT)
parser.add_argument('--output-dir', type=Path, default=ROOT/'.planning/debug/render-quality')
args = parser.parse_args()
source = args.source_root/'src/GloomhavenVR'
original = (source/'Rig/RenderQuality.cs').read_text()
bootstrap = (source/'Core/Startup/OpenXRBootstrap.cs').read_text()
assert bootstrap.index('RenderQuality.PrepareSession();') < bootstrap.index('        CreateSettings();')
assert bootstrap.index('RenderQuality.PrepareDisplays();') < bootstrap.index('            _generalSettings.Start();')
reuse = bootstrap[bootstrap.index('        if (DisplayExists())'):bootstrap.index('        RenderQuality.PrepareSession();')]
assert 'RenderQuality.AdoptRunningSession();' in reuse
stop = bootstrap[bootstrap.index('    internal static void Stop()'):]
assert stop.index('StopAndDeinitQuiet();') < stop.index('RenderQuality.EndSession();')
assert 'if (!_sessionPrepared) AdoptRunningSession();\n        ApplyMsaa();' in original
assert 'Camera.rect =' not in original and '.projectionMatrix =' not in original
args.output_dir.mkdir(parents=True,exist_ok=True)
run = Path(tempfile.mkdtemp(prefix='run-',dir=args.output_dir.resolve()))
fixture = ROOT/'tests/GloomhavenVR.RenderQualityTests'
constants = ''.join(p.read_text() for p in (source/'Defaults').glob('*.cs'))
frame = (source/'Core/Startup/FrameDefaults.cs').read_text()
extra = ''
for owner, namespace, text in [('Defaults','GloomhavenVR',constants),('FrameDefaults','GloomhavenVR.Core',frame)]:
    fields = []
    for name in sorted(set(re.findall(r'(?<![A-Za-z])'+owner+r'\.([A-Za-z]+)',original))-{'Active'}):
        match = re.search(r'internal const (\w+) '+name+r'\s*=\s*([^;]+);',text)
        if not match: raise SystemExit('Missing source-bound default '+owner+'.'+name)
        fields.append('internal const '+match.group(1)+' '+name+' = '+match.group(2)+';')
    if owner=='FrameDefaults': fields.append('internal static bool Active => false;')
    extra += 'namespace '+namespace+' { internal static class '+owner+' { '+'\n'.join(fields)+' } }\n'
(run/'Defaults.cs').write_text(extra)
(run/'RenderQuality.cs').write_text(original)
dotnet = os.environ.get('DOTNET',str(Path.home()/'.dotnet/dotnet'))
project = fixture/'GloomhavenVR.RenderQualityTests.csproj'
command = [dotnet,'build',str(project),'--configuration','Release',
           '-p:ProductionDir='+str(run),'-p:FixtureDir='+str(fixture),
           '-p:BaseIntermediateOutputPath='+str(run/'obj')+'/', '-p:OutputPath='+str(run/'bin')+'/']
def execute(name):
    result = subprocess.run(command,capture_output=True,text=True,timeout=90)
    if result.returncode == 0:
        execution = subprocess.run([dotnet,str(run/'bin/GloomhavenVR.RenderQualityTests.dll')],capture_output=True,text=True,timeout=90)
        execution.stdout = result.stdout + execution.stdout
        execution.stderr = result.stderr + execution.stderr
        result = execution
    (run/(name+'.log')).write_text(result.stdout+result.stderr)
    return result
production = execute('production')
if production.returncode: raise SystemExit(production.stdout+production.stderr)
print(production.stdout.strip())
mutations = [
 ('live-allocation','float viewport = Mathf.Clamp(wanted / _sessionAllocationScale, 0.01f, 1f);','XRSettings.eyeTextureResolutionScale = wanted;\n        float viewport = Mathf.Clamp(wanted / _sessionAllocationScale, 0.01f, 1f);','live resolution never recreates XR allocation'),
 ('resolution-no-quiet','Time.unscaledTime - _pendingEyeScaleSince < SliderQuietSeconds','false','intermediate resolution requests wait for slider quiet'),
 ('capacity-wrong-ratio','float viewport = Mathf.Clamp(wanted / _sessionAllocationScale, 0.01f, 1f);','float viewport = Mathf.Clamp(wanted, 0.01f, 1f);','viewport uses requested scale divided by startup capacity'),
 ('erase-larger-request','float wanted = WantedEyeScale();\n        if (_baseEyeWidth','EyeResolutionScale!.Value = Mathf.Min(EyeResolutionScale.Value, _sessionAllocationScale);\n        float wanted = WantedEyeScale();\n        if (_baseEyeWidth','above-capacity request persists for next VR start'),
 ('msaa-no-quiet','Time.unscaledTime - _pendingMsaaSince >= SliderQuietSeconds','true','MSAA intermediate cycles do not recreate surfaces'),
 ('msaa-no-spacing','&& Time.unscaledTime >= _nextMsaaApplyTime && Camera.current == null','&& Camera.current == null','consecutive MSAA resource changes have minimum spacing'),
 ('native-msaa-no-restore','if (current != wanted && Camera.current == null)','if (current != wanted && Camera.current == null && QualitySettings.antiAliasing < 0)','native quality swap restores committed MSAA while request waits'),
 ('camera-boundary-ignored','if (Time.unscaledTime - _pendingEyeScaleSince < SliderQuietSeconds || Camera.current != null) return;','if (Time.unscaledTime - _pendingEyeScaleSince < SliderQuietSeconds) return;','rendering camera blocks resolution writes'),
 ('deferred-ignored','if (deferred)\n','if (deferred && head == null)\n','deferred camera cannot take unsupported viewport path'),
 ('provider-realloc-fallback','bool stuck = Mathf.Abs(XRSettings.renderViewportScale - wanted) < 0.005f;','bool stuck = Mathf.Abs(XRSettings.renderViewportScale - wanted) < 0.005f;\n        if (!stuck) XRSettings.eyeTextureResolutionScale = wanted;','provider refusal never uses hazardous allocation fallback'),
 ('hot-reload-allocation','float allocation = XRSettings.eyeTextureResolutionScale;','float allocation = XRSettings.eyeTextureResolutionScale;\n        XRSettings.eyeTextureResolutionScale = allocation;','hot reload adopts live allocation without setter'),
 ('startup-refusal-aborts', 'catch (System.Exception e) when (e is System.ArgumentException\n            || e is System.InvalidOperationException)', 'catch (System.Exception e) when (e is System.ArgumentException)', 'pre-loader allocation rejected'),
 ('startup-capacity-readback-ignored', 'if (ValidScale(acceptedAllocation)) _sessionAllocationScale = acceptedAllocation;', 'if (ValidScale(acceptedAllocation) && false) _sessionAllocationScale = acceptedAllocation;', 'refused startup allocation uses actual accepted capacity for later viewport'),
]
results = []
for name, before, after, expected in mutations:
    if original.count(before)!=1: raise SystemExit('Ambiguous causal control '+name+': '+str(original.count(before)))
    (run/'RenderQuality.cs').write_text(original.replace(before,after,1))
    result=execute(name)
    if result.returncode==0 or expected not in result.stdout+result.stderr:
        raise SystemExit('Causal control failed '+name+'\n'+result.stdout+result.stderr)
    results.append({'name':name,'expected':expected,'rejected':True})
    print('RenderQuality causal control rejected: '+name)
(run/'RenderQuality.cs').write_text(original)
final = execute('production-final')
if final.returncode or 'assertions passed' not in final.stdout:
    raise SystemExit('Final original-source restoration failed\n'+final.stdout+final.stderr)
report={'result':'PASS','sourceBindings':6,'productionAssertions':int(re.search(r'(\d+) assertions passed',production.stdout).group(1)),
        'causalControls':results,'sourceSha256':hashlib.sha256(original.encode()).hexdigest(),
        'bootstrapSha256':hashlib.sha256(bootstrap.encode()).hexdigest(),
        'fixtureProgramSha256':hashlib.sha256((fixture/'Program.cs').read_bytes()).hexdigest(),
        'fixtureBoundarySha256':hashlib.sha256((fixture/'Boundary.cs').read_bytes()).hexdigest(),
        'productionExecutableSha256':hashlib.sha256((run/'bin/GloomhavenVR.RenderQualityTests.dll').read_bytes()).hexdigest(),
        'limits':'Explicit XR boundary model; no native OpenXR/driver/headset pixel stability is claimed.'}
(run/'results.json').write_text(json.dumps(report,indent=2)+'\n')
print('RenderQuality proof: '+str(run/'results.json'))
