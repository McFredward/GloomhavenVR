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
import sys
sys.dont_write_bytecode = True
sys.path.insert(0,str(Path(__file__).resolve().parent))
from town_run_retention import cleanup_completed_run

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--source-root', type=Path, default=ROOT)
parser.add_argument('--output-dir', type=Path, default=ROOT/'.planning/debug/render-quality')
parser.add_argument('--production-only', action='store_true', help='Run current production lifecycle/typecheck without repeating causal controls; this is not a new full-suite pass.')
args = parser.parse_args()
source = args.source_root/'src/GloomhavenVR'
original = (source/'Rig/RenderQuality.cs').read_text()
quest_eye = (source/'Rig/QuestEyeResolution.cs').read_text()
quest_allocation = (source/'Core/QuestEyeAllocation.cs').read_text()
quest_text = (source/'Core/Loc/QuestText.cs').read_text()
bootstrap = (source/'Core/Startup/OpenXRBootstrap.cs').read_text()
rig = (source/'Rig/VRRigDriver.cs').read_text()
def validate_startup_boundary(text):
    prepare = text.index('            RenderQuality.PrepareSession();')
    assert prepare > text.index('            _generalSettings!.InitXRSDK();'), 'quality before loader initialization'
    failed = text[text.index('            if (active == null)'):prepare]
    assert 'return false;' in failed, 'failed loader reaches quality selection'
    assert text.count('RenderQuality.PrepareSession();') == 1, 'duplicate startup quality selection'
    assert prepare < text.index('            RenderQuality.PrepareDisplays();'), 'display setup before selection'

validate_startup_boundary(bootstrap)
startup_controls = []
for name, anchor, expected in [
    ('quality-before-initialize', '            _generalSettings!.InitXRSDK();', 'quality before loader initialization'),
    ('quality-on-loader-failure', '                OpenXRDiagnostics.AppendReportToFile($"FAILED (loader Initialize)', 'failed loader reaches quality selection'),
]:
    changed = bootstrap.replace('            RenderQuality.PrepareSession();\n', '', 1)
    changed = changed.replace(anchor, '            RenderQuality.PrepareSession();\n'+anchor, 1)
    try:
        validate_startup_boundary(changed)
    except AssertionError as error:
        if str(error) != expected: raise
        startup_controls.append({'name':name, 'expected':expected, 'rejected':True})
        print('RenderQuality startup boundary control rejected: '+name, flush=True)
    else:
        raise SystemExit('Startup boundary control survived: '+name)
assert bootstrap.index('RenderQuality.PrepareDisplays();') < bootstrap.index('            _generalSettings.Start();')
reuse = bootstrap[bootstrap.index('        if (DisplayExists())'):bootstrap.index('        CreateSettings();')]
assert 'RenderQuality.AdoptRunningSession();' in reuse
stop = bootstrap[bootstrap.index('    internal static void Stop()'):]
assert stop.index('StopAndDeinitQuiet();') < stop.index('RenderQuality.EndSession();')
assert 'if (!_sessionPrepared) AdoptRunningSession();' in original
assert '("Rig.RenderQuality", RenderQuality.TickFromUpdate),' in rig
assert rig.index('CanvasConversion.BeginFramePhase("Rig.Update");') < rig.index('                UpdateBody();')
assert 'internal static void Tick() => TickCore(fromFrameUpdate: false);' in original
assert 'internal static void TickFromUpdate() => TickCore(fromFrameUpdate: true);' in original
assert 'Camera.rect =' not in original and '.projectionMatrix =' not in original
args.output_dir.mkdir(parents=True,exist_ok=True)
run = Path(tempfile.mkdtemp(prefix='run-',dir=args.output_dir.resolve()))
print('RenderQuality evidence: '+str(run), flush=True)
completion = run/'completion.log'
completion.write_text('RenderQuality focused production lifecycle and causal controls\nevidence: '+str(run)+'\n')
status = 1
try:
    fixture = ROOT/'tests/GloomhavenVR.RenderQualityTests'
    inputs = [source/'Rig/RenderQuality.cs', source/'Core/Startup/OpenXRBootstrap.cs',
              source/'Rig/VRRigDriver.cs', source/'Rig/QuestEyeResolution.cs',
              source/'Core/QuestEyeAllocation.cs', source/'Core/Loc/QuestText.cs', Path(__file__), *fixture.glob('*.cs'),
              *fixture.glob('*.csproj'), *sorted((source/'Defaults').glob('*.cs')),
              source/'Core/Startup/FrameDefaults.cs']
    input_hashes = {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in inputs}
    constants = ''.join(p.read_text() for p in (source/'Defaults').glob('*.cs'))
    frame = (source/'Core/Startup/FrameDefaults.cs').read_text()
    extra = ''
    for owner, namespace, text in [('Defaults','GloomhavenVR',constants),('FrameDefaults','GloomhavenVR.Core',frame)]:
        fields = []
        for name in sorted(set(re.findall(r'(?<![A-Za-z])'+owner+r'\.([A-Za-z]+)',original))-{'Active'}):
            match = re.search(r'internal const (\w+) '+name+r'\s*=\s*([^;]+);',text)
            if not match: raise SystemExit('Missing source-bound default '+owner+'.'+name)
            fields.append('internal const '+match.group(1)+' '+name+' = '+match.group(2)+';')
        if owner=='FrameDefaults': fields.append('private static bool _active = true; internal static int Reads; internal static bool Active { get { Reads++; return _active; } set { _active=value; } }')
        extra += 'namespace '+namespace+' { internal static class '+owner+' { '+'\n'.join(fields)+' } }\n'
    (run/'Defaults.cs').write_text(extra)
    (run/'RenderQuality.cs').write_text(original)
    (run/'QuestEyeResolution.cs').write_text(quest_eye)
    (run/'QuestEyeAllocation.cs').write_text(quest_allocation)
    (run/'QuestText.cs').write_text(quest_text)
    # setup-dotnet installs on PATH; ~/.dotnet is only the local-development fallback.
    dotnet = os.environ.get('DOTNET') or shutil.which('dotnet') or str(Path.home()/'.dotnet/dotnet')
    project = fixture/'GloomhavenVR.RenderQualityTests.csproj'
    # Physical copied project markers let completion cleanup retain JSON reports
    # from both compiler caches before removing them. Never touch fixture sources.
    shutil.copyfile(project,run/'Fixture.csproj')
    typecheck_dir=run/'Typecheck'; typecheck_dir.mkdir()
    shutil.copyfile(fixture/'Typecheck.csproj',typecheck_dir/'Typecheck.csproj')
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
    framework=Path.home()/'.nuget/packages/microsoft.netframework.referenceassemblies.net472/1.0.3/build/.NETFramework/v4.7.2'
    # A runtime shard can have a cold optional NuGet cache. Use the local pack
    # only if its actual reference assembly exists; otherwise let the SDK restore
    # its ordinary framework-reference package. Never delete shared caches.
    framework_args=['-p:FrameworkReferenceDir='+str(framework)] if (framework/'mscorlib.dll').is_file() else []
    cold = subprocess.run([dotnet,'msbuild',str(fixture/'Typecheck.csproj'),
        '-getProperty:AutomaticallyUseReferenceAssemblyPackages,FrameworkPathOverride',
        '-p:FrameworkReferenceDir='+str(run/'missing-reference-pack')],
        capture_output=True,text=True,timeout=45)
    (run/'net472-cold-reference-policy.log').write_text(cold.stdout+cold.stderr)
    if cold.returncode: raise SystemExit(cold.stdout+cold.stderr)
    cold_policy=json.loads(cold.stdout)
    if cold_policy['Properties']['AutomaticallyUseReferenceAssemblyPackages'].lower() != 'true' \
        or cold_policy['Properties']['FrameworkPathOverride'] == str(run/'missing-reference-pack'):
        raise SystemExit('Cold net472 cache disables SDK reference-package fallback.')
    typecheck = subprocess.run([dotnet,'build',str(fixture/'Typecheck.csproj'),'--configuration','Release',
        '-p:ProductionDir='+str(run),'-p:FixtureDir='+str(fixture),*framework_args,
        '-p:BaseIntermediateOutputPath='+str(typecheck_dir/'obj')+'/',
        '-p:OutputPath='+str(typecheck_dir/'bin')+'/', '-p:UseSharedCompilation=false'],
        capture_output=True,text=True,timeout=90)
    (run/'net472-typecheck.log').write_text(typecheck.stdout+typecheck.stderr)
    if typecheck.returncode: raise SystemExit(typecheck.stdout+typecheck.stderr)
    production = execute('production')
    if production.returncode: raise SystemExit(production.stdout+production.stderr)
    print(production.stdout.strip())
    mutations = [
     ('reduced-startup-capacity-lost','_sessionAllocationScale = wanted;','_sessionAllocationScale = Mathf.Max(1f, wanted);','saved reduction allocates smaller stopped eye targets'),
     ('native-reset-repair-omitted','if (!_viewportScaleAccepted || Time.unscaledTime < _nextViewportRepairTime','if (true || Time.unscaledTime < _nextViewportRepairTime','unchanged accepted viewport is restored after native reset'),
     ('native-reset-no-spacing','Time.unscaledTime < _nextViewportRepairTime','false','repeated native viewport resets have bounded repair spacing'),
     ('native-reset-refusal-retried','_viewportScaleAccepted = ApplyViewportScale(viewport, "restoring a previously accepted viewport after native reset");','ApplyViewportScale(viewport, "restoring a previously accepted viewport after native reset");','refused native reset repair disarms automatic retries'),
     ('persistent-reset-unbounded','if (_viewportResetRepairs >= MaxViewportResetRepairs)','if (_viewportResetRepairs >= MaxViewportResetRepairs && false)','persistent delayed viewport reset stops after three spaced repairs'),
     ('stable-reset-budget-not-cleared','                _viewportResetRepairs = 0;','                /* injected: stable viewport does not replenish reset budget */','stable viewport replenishes the later native reset budget'),
     ('live-allocation','float viewport = Mathf.Clamp(wanted / _sessionAllocationScale, 0.01f, 1f);','XRSettings.eyeTextureResolutionScale = wanted;\n        float viewport = Mathf.Clamp(wanted / _sessionAllocationScale, 0.01f, 1f);','live resolution never recreates XR allocation'),
     ('resolution-no-quiet','Time.unscaledTime - _pendingEyeScaleSince < SliderQuietSeconds','false','intermediate resolution requests wait for slider quiet'),
     ('capacity-wrong-ratio','float viewport = Mathf.Clamp(wanted / _sessionAllocationScale, 0.01f, 1f);','float viewport = Mathf.Clamp(wanted, 0.01f, 1f);','viewport uses requested scale divided by startup capacity'),
     ('erase-larger-request','float wanted = WantedEyeScale();\n        if (_baseEyeWidth','EyeResolutionScale!.Value = Mathf.Min(EyeResolutionScale.Value, _sessionAllocationScale);\n        float wanted = WantedEyeScale();\n        if (_baseEyeWidth','above-capacity request persists for next VR start'),
     ('msaa-no-quiet','Time.unscaledTime - _pendingMsaaSince >= SliderQuietSeconds','true','MSAA intermediate cycles do not recreate surfaces'),
     ('msaa-no-spacing','&& Time.unscaledTime >= _nextMsaaApplyTime && canWriteRenderResources','&& canWriteRenderResources','consecutive MSAA resource changes have minimum spacing'),
     ('native-msaa-no-restore','if (current != wanted && canWriteRenderResources)','if (current != wanted && canWriteRenderResources && QualitySettings.antiAliasing < 0)','native quality swap restores committed MSAA while request waits'),
     ('camera-boundary-ignored','bool canWriteRenderResources = fromFrameUpdate || Camera.current == null;','bool canWriteRenderResources = true;','rendering camera blocks resolution writes'),
     ('stale-camera-blocks-update','bool canWriteRenderResources = fromFrameUpdate || Camera.current == null;','bool canWriteRenderResources = Camera.current == null;','known Update phase commits viewport despite stale Camera.current'),
     ('deferred-ignored','if (deferred)\n','if (deferred && head == null)\n','deferred camera cannot take unsupported viewport path'),
     ('provider-realloc-fallback','bool stuck = Mathf.Abs(XRSettings.renderViewportScale - wanted) < 0.005f;','bool stuck = Mathf.Abs(XRSettings.renderViewportScale - wanted) < 0.005f;\n        if (!stuck) XRSettings.eyeTextureResolutionScale = wanted;','provider refusal never uses hazardous allocation fallback'),
     ('hot-reload-allocation','float allocation = XRSettings.eyeTextureResolutionScale;','float allocation = XRSettings.eyeTextureResolutionScale;\n        XRSettings.eyeTextureResolutionScale = allocation;','hot reload adopts live allocation without setter'),
     ('startup-refusal-aborts', 'catch (System.Exception e) when (e is System.ArgumentException\n            || e is System.InvalidOperationException)', 'catch (System.Exception e) when (e is System.ArgumentException)', 'startup display allocation rejected'),
     ('early-legacy-allocation', '_sessionAllocationScale = wanted;', '_sessionAllocationScale = wanted;\n        TryStartupSetting(() => XRSettings.eyeTextureResolutionScale = _sessionAllocationScale, "legacy eye allocation");', 'quality selection never touches native resources before provider initialization'),
     ('early-legacy-viewport', '_viewportScaleApplied = Mathf.Clamp(wanted / _sessionAllocationScale, 0.01f, 1f);\n        _lastLoggedEyeScale = wanted;', '_viewportScaleApplied = Mathf.Clamp(wanted / _sessionAllocationScale, 0.01f, 1f);\n        TryStartupSetting(() => XRSettings.renderViewportScale = _viewportScaleApplied, "legacy eye viewport");\n        _lastLoggedEyeScale = wanted;', 'quality selection never touches native resources before provider initialization'),
     ('early-native-msaa', '_pendingMsaa = _committedMsaa;', '_pendingMsaa = _committedMsaa;\n        QualitySettings.antiAliasing = _committedMsaa;', 'native render resource touched before provider initialization'),
     ('startup-capacity-readback-ignored', 'if (ValidScale(acceptedAllocation)) _sessionAllocationScale = acceptedAllocation;', 'if (ValidScale(acceptedAllocation) && false) _sessionAllocationScale = acceptedAllocation;', 'startup viewport respects actual accepted provider capacity'),
     ('pc-live-path-lost', 'if (!_standaloneSession)\n        {\n            ApplyPcEyeScale(wanted);', 'if (false && !_standaloneSession)\n        {\n            ApplyPcEyeScale(wanted);', 'PC increase resizes the running actual XR allocation'),
     ('pc-allocation-omitted', 'display.scaleOfAllRenderTargets = wanted;', '_ = display.scaleOfAllRenderTargets;', 'PC increase resizes the running actual XR allocation'),
     ('pc-stable-replayed', 'if (Mathf.Abs(_lastPcEyeScaleAttempt - wanted) < 0.0005f) return;', 'if (false && Mathf.Abs(_lastPcEyeScaleAttempt - wanted) < 0.0005f) return;', 'PC stable resolution does not repeat lifecycle work or logging'),
     ('pc-legacy-viewport-retained', 'display.scaleOfAllViewports = 1f;', '_ = display.scaleOfAllViewports;', 'PC clears a legacy display viewport reduction'),
     ('profile-marker-read-in-update', 'if (!_standaloneSession)\n        {\n            ApplyPcEyeScale(wanted);', 'if (!FrameDefaults.Active)\n        {\n            ApplyPcEyeScale(wanted);', 'Standalone Update and diagnostics never reread the profile marker'),
     ('pc-display-wait-latched', 'if (Mathf.Abs(_lastPcEyeScaleAttempt - wanted) < 0.0005f) return;\n        SubsystemManager.GetInstances(Displays);', 'if (Mathf.Abs(_lastPcEyeScaleAttempt - wanted) < 0.0005f) return;\n        _lastPcEyeScaleAttempt = wanted;\n        SubsystemManager.GetInstances(Displays);', 'PC same settled request recovers when its display becomes running'),
    ('quest-live-msaa-restored', '\n            || QuestStandalonePlatform.FixedEyeTextureAllocation) return;', ') return;', 'Quest Vulkan never resizes the player-owned running swapchain or MSAA surfaces'),
    ('quest-startup-msaa-replaced', 'int request = QuestStandalonePlatform.FixedEyeTextureAllocation\n            ? QuestEyeResolution.StartupMsaa : Sanitize(MsaaLevel!.Value);', 'int request = Sanitize(MsaaLevel!.Value);', 'Quest Vulkan retains startup mobile MSAA without rewriting the saved desktop request'),
    ]
    results = []
    for name, before, after, expected in ([] if args.production_only else mutations):
        if original.count(before)!=1: raise SystemExit('Ambiguous causal control '+name+': '+str(original.count(before)))
        (run/'RenderQuality.cs').write_text(original.replace(before,after,1))
        result=execute(name)
        if result.returncode==0 or expected not in result.stdout+result.stderr:
            raise SystemExit('Causal control failed '+name+'\n'+result.stdout+result.stderr)
        results.append({'name':name,'expected':expected,'rejected':True})
        print('RenderQuality causal control rejected: '+name)
    (run/'RenderQuality.cs').write_text(original)
    final = production if args.production_only else execute('production-final')
    if final.returncode or 'assertions passed' not in final.stdout:
        raise SystemExit('Final original-source restoration failed\n'+final.stdout+final.stderr)
    changed = {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in inputs
               if hashlib.sha256(p.read_bytes()).hexdigest() != input_hashes[str(p)]}
    (run/'source-stability.json').write_text(json.dumps({'unchanged': not changed, 'changed': changed}, indent=2)+'\n')
    if changed:
        raise SystemExit('RenderQuality source inputs changed during validation: '+str(run/'source-stability.json'))
    report={'result':'PASS','scope':'production-only' if args.production_only else 'full-focused-suite','sourceBindings':14,'productionAssertions':int(re.search(r'(\d+) assertions passed',production.stdout).group(1)),
            'inputSha256': input_hashes,
            'causalControls':results,'startupBoundaryControls':startup_controls,
            'sourceSha256':hashlib.sha256(original.encode()).hexdigest(),
            'bootstrapSha256':hashlib.sha256(bootstrap.encode()).hexdigest(),
            'rigUpdateSha256':hashlib.sha256(rig.encode()).hexdigest(),
            'fixtureProgramSha256':hashlib.sha256((fixture/'Program.cs').read_bytes()).hexdigest(),
            'fixtureBoundarySha256':hashlib.sha256((fixture/'Boundary.cs').read_bytes()).hexdigest(),
            'productionExecutableSha256':hashlib.sha256((run/'bin/GloomhavenVR.RenderQualityTests.dll').read_bytes()).hexdigest(),
            'net472Typecheck':'PASS (complete production class against explicit provider boundaries)',
            'net472ColdReferencePolicy':'PASS (SDK automatic reference-package fallback remains enabled)',
            'limits':'Explicit XR boundary model; no native OpenXR/driver/headset pixel stability is claimed.'}
    (run/'results.json').write_text(json.dumps(report,indent=2)+'\n')
    print('RenderQuality proof: '+str(run/'results.json'))
    status = 0
finally:
    cleanup = cleanup_completed_run('render-quality-runtime', status, completion, ROOT, process_reaped=True)
    (run/'cleanup.json').write_text(json.dumps(cleanup,indent=2)+'\n')

