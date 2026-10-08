#!/usr/bin/env python3
"""Compile the real profile action; test native-call, persistence and independent-edit boundaries."""
import argparse
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
parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/graphics-profiles')
args = parser.parse_args()
args.output_dir.mkdir(parents=True, exist_ok=True)
run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
source = args.source_root / 'src/GloomhavenVR'
profile = (source / 'Rig/GraphicsProfiles.cs').read_text()
constants = ''.join(p.read_text() for p in (source / 'Defaults').glob('*.cs'))
required = set(re.findall(r'(?<!Frame)\bDefaults\.([A-Za-z]+)', profile))
defs = []
for name in sorted(required):
    m = re.search(r'internal const (\w+) '+name+r'\s*=\s*([^;]+);', constants)
    if not m: raise SystemExit('Missing original Defaults constant: '+name)
    defs.append('internal const '+m.group(1)+' '+name+' = '+m.group(2)+';')
extra = 'namespace GloomhavenVR { internal static class Defaults { '+'\n'.join(defs)+' } }\n'
# Bind only the platform/config boundary. Types/defaults are taken from original source,
# while the complete production profile callback, validation and finally paths stay unchanged.
owners = {'RenderQuality': ('Rig/RenderQuality.cs', 'GloomhavenVR.Rig', 'rig'),
          'PerfConfig': ('Core/Perf/PerfConfig.cs', 'GloomhavenVR.Core', 'perf'),
          'WorldUIConfig': ('WorldUI/WorldUIConfig.cs', 'GloomhavenVR.WorldUI', 'worldui'),
          'WallFadeTuning': ('Core/WallFade/WallSegmentFade.cs', 'GloomhavenVR.Core', 'wallfade')}
for owner, (rel, namespace, module) in owners.items():
    original = (source / rel).read_text()
    if owner == 'PerfConfig':
        original += (source / 'Core/Perf/PerfConfig.FrameRendering.cs').read_text()
    names = sorted(set(re.findall(r'\b'+owner+r'\.([A-Za-z]+)', profile)) - {'Bind'})
    fields = []
    for name in names:
        m = re.search(r'(?:internal|public) static ConfigEntry<(\w+)>\?? '+name+r'\b', original)
        if not m: raise SystemExit('Missing original field: '+owner+'.'+name)
        fields.append((m.group(1),name))
    extra += f'namespace {namespace} {{ internal static class {owner} {{\n'
    for kind,name in fields: extra += f'internal static BepInEx.Configuration.ConfigEntry<{kind}> {name} = null!;\n'
    extra += 'internal static void Bind() { var file = GloomhavenVR.Core.ModuleConfig.Get("'+module+'");\n'
    for kind,name in fields:
        section = 'RenderQuality' if owner=='RenderQuality' else 'Optimize' if owner=='PerfConfig' else 'WallFade' if owner=='WallFadeTuning' else 'WorldUI'
        value = 'false' if kind=='bool' else '0f' if kind=='float' else '0'
        extra += f'if ({name} == null) {name} = file.Add("{section}", "{name}", {value});\n'
    if owner == 'PerfConfig':
        # Compile the exact always-on getters; they deliberately create no binding.
        extra += '\n'.join(re.findall(r'internal static bool \w+\s*=> true;', original))+'\n'
    extra += '} } }\n'
extra += '''namespace GloomhavenVR.WorldUI { internal static class WindowMaterialise {
    internal static BepInEx.Configuration.ConfigEntry<bool> Entry = null!;
    internal static bool Enabled { get { if (Entry == null) Entry = GloomhavenVR.Core.ModuleConfig.Get("worldui").Add("WorldUI", "WindowMaterialise", true); return Entry.Value; } }
} }\n'''
(run/'Profiles.cs').write_text(profile)
(run/'Defaults.cs').write_text(extra)
shutil.copyfile(source/'Core/Startup/FrameDefaults.cs',run/'FrameDefaults.cs')
fixture = ROOT/'scripts/graphics-profiles-runtime'
cmd = [os.environ.get('DOTNET', str(Path.home()/'.dotnet/dotnet')), 'build', str(fixture/'Profiles.csproj'), '-c', 'Release', '-p:ProductionDir='+str(run), '-p:FixtureDir='+str(fixture), '-p:BaseIntermediateOutputPath='+str(run/'obj')+'/', '-p:OutputPath='+str(run/'bin')+'/']
result = subprocess.run(cmd, capture_output=True, text=True, timeout=90)
(run/'build.txt').write_text(result.stdout+result.stderr)
if result.returncode: raise SystemExit(result.stdout+result.stderr)
result = subprocess.run([cmd[0], str(run/'bin/Profiles.dll')], capture_output=True, text=True, timeout=30)
(run/'result.txt').write_text(result.stdout+result.stderr)
print(result.stdout+result.stderr, end='')
if result.returncode: raise SystemExit(result.returncode)
# The UI is independently bound to four distinct actions, never to the retired config index.
curated=(source/'WorldUI/Options/VROptionsTab.4.Curated.cs').read_text()
for index in range(4):
    if curated.count('() => GraphicsProfileActions.Apply('+str(index)+')') != 1: raise SystemExit('Missing unique profile button')
retired = ['CacheTickDelegates','MapIconCache','FigureScanCache','LeanLogStrings',
           'TooltipScanGate','SharedWallReadCache','LightStabiliserWorkCache',
           'SuspendUnusedCameras','AutomaticLodIdleSkip','SharedEnvironmentMaterialReads']
perf = (source/'Core/Perf/PerfConfig.cs').read_text() + (source/'Core/Perf/PerfConfig.FrameRendering.cs').read_text()
for key in retired:
    if re.search(r'\.Bind\("Optimize", "'+key+r'"', perf):
        raise SystemExit('Pure work-removal option must be retired: '+key)
print('PASS: four unique UI actions; pure work-removal getters are always on and retired keys are unbound')
# Default selection is independent of the explicit profile action. Keep the real Bind's
# platform conditional and PC constant source-bound; this is not a persisted-file fixture.
quality=(source/'Rig/RenderQuality.cs').read_text()
if not re.search(r'EyeResolutionScale = _file.Bind\("RenderQuality", "EyeResolutionScale", frame \? FrameDefaults.EyeResolutionScale : Defaults.EyeResolutionScale,',quality):
    raise SystemExit('Resolution Bind lost fresh Frame versus PC default selection')
if not re.search(r'internal const float EyeResolutionScale\s*=\s*1(?:\.0)?f;',constants):
    raise SystemExit('Ordinary PC resolution default changed')
print('PASS: fresh Frame/PC resolution selection remains source-bound; saved-file preservation uses existing BepInEx Bind contract')
print('Evidence: '+str(run))
