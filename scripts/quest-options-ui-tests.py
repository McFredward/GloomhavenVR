#!/usr/bin/env python3
"""Actual native options font/mask qualification; original game references remain read-only."""
from pathlib import Path
import argparse,ctypes,hashlib,importlib.util,json,os,re,shutil,subprocess,sys,tempfile
root=Path(__file__).resolve().parents[1];sys.path.insert(0,str(root/'scripts'))
from town_run_retention import cleanup_completed_run
parser=argparse.ArgumentParser(description='Qualify the native Quest options viewport and slider using retained original font/material/shader pixels.');parser.add_argument('--prepared-project',type=Path,required=True);args=parser.parse_args();owned=args.prepared_project.resolve()
base=root/'.planning/debug/quest-options-ui';base.mkdir(parents=True,exist_ok=True)
run=Path(tempfile.mkdtemp(prefix='run-',dir=base));project=run/'unity';log=run/'runner.log'
log.write_text('Quest options UI evidence: '+str(run)+'\n');print(log.read_text(),end='',flush=True)
code=1;process=None
ctypes.CDLL(None,use_errno=True).prctl(36,1,0,0,0)
try:
 for f in ['Assets/Editor','Assets/Plugins','Assets/Resources','Packages','ProjectSettings']:(project/f).mkdir(parents=True,exist_ok=True)
 paths=['Assets/Resources/TMP Settings.asset','Assets/Plugins/Unity.TextMeshPro.dll','Assets/Resources/fonts & materials/Sarala-Regular SDF.asset','Assets/Resources/fonts & materials/Sarala-Regular SDF Atlas.png','Assets/Resources/fonts & materials/Sarala-Regular SDF Material.mat','Assets/Resources/fonts & materials/Sarala-Regular-Shadow SDF.mat','Assets/Resources/shaders/tmp_sdf.shader']
 shader=(owned/paths[-1]).read_text();paths+=list(set(re.findall(r'#include "(Assets/QuestOriginalCampaign/ShaderPrograms/[^"\n]+)"',shader)))
 inputs=[]
 for p in paths:
  src=owned/p;dst=project/p;dst.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(src,dst)
  if src.with_name(src.name+'.meta').is_file():shutil.copyfile(src.with_name(src.name+'.meta'),dst.with_name(dst.name+'.meta'))
  inputs.append({'path':str(src),'sha256':hashlib.sha256(src.read_bytes()).hexdigest()})
 paths_to_source=['src/GloomhavenVR/WorldUI/Patches/QuestTutorialViewportMask.cs','src/GloomhavenVR/WorldUI/Options/QuestOptionsMask.cs']
 helpers=[]
 for relative in paths_to_source:
  production=(root/relative).read_text()
  helper=production.split('[HarmonyPatch',1)[0].replace('using GloomhavenVR.Core;','').replace('using HarmonyLib;','').replace('namespace GloomhavenVR.WorldUI;','namespace GloomhavenVR.WorldUI {')+'\n}\n'
  (project/'Assets/Editor'/Path(relative).name).write_text(helper);helpers.append({'path':relative,'sha256':hashlib.sha256(production.encode()).hexdigest()})
 inject=(root/'src/GloomhavenVR/WorldUI/Options/VROptionsTab.1.Inject.cs').read_text()
 rows=(root/'src/GloomhavenVR/WorldUI/Options/VROptionsTab.2.Rows.cs').read_text()
 assert 'QuestOptionsMask.ApplyViewport(scroll, QuestStandalonePlatform.Enabled);' in inject
 assert 'QuestOptionsMask.ApplySlider(slider, QuestStandalonePlatform.Enabled);' in rows
 # Exact original TMP variant with only its lost stencil comparator restored is
 # the pixel oracle, not a hand-painted approximation of native text.
 reference=shader.replace('Shader "TextMeshPro/Distance Field"','Shader "QuestFixture/Native TMP Stencil Reference"').replace('CompFront Always','CompFront [_StencilComp]').replace('CompBack Always','CompBack [_StencilComp]')
 (project/'Assets/Resources/NativeStencilReference.shader').write_text(reference)
 shutil.copyfile(root/'tests/QuestOptionsUi.Tests/Probe.cs',project/'Assets/Editor/Probe.cs')
 (run/'source-hashes.json').write_text(json.dumps({'sources':helpers,'fixture':hashlib.sha256((root/'tests/QuestOptionsUi.Tests/Probe.cs').read_bytes()).hexdigest(),'hardwareVerified':False},indent=2)+'\n')
 (run/'owned-inputs.json').write_text(json.dumps(inputs,indent=2)+'\n')
 (project/'Packages/manifest.json').write_text('{"dependencies":{"com.unity.ugui":"1.0.0"}}\n')
 (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
 process=subprocess.Popen(['xvfb-run','-a','/home/claw/unity-2021.3.5/Editor/Unity','-batchmode','-force-vulkan','-projectPath',str(project),'-executeMethod','QuestOptionsUiProbe.Run','-logFile',str(run/'unity.log')],stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT,start_new_session=True)
 code=process.wait(timeout=240)
 result=project/'ProbeOutput/results.json'
 if result.is_file():shutil.copyfile(result,run/'results.json');print(result.read_text(),flush=True)
 if code or not result.is_file():raise RuntimeError('Native mask proof failed: '+str(run/'unity.log'))
 for row in inputs:assert hashlib.sha256(Path(row['path']).read_bytes()).hexdigest()==row['sha256'],'Read-only native input changed'
finally:
 if process is not None and process.poll() is None:process.kill();process.wait()
 spec=importlib.util.spec_from_file_location('ui_mask_cleanup',root/'scripts/quest-artwork-tests.py');m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
 (run/'process-cleanup.json').write_text(json.dumps(m.reap_fixture_group(process.pid if process else None),indent=2)+'\n')
 (run/'cleanup.json').write_text(json.dumps(cleanup_completed_run('quest-options-ui',code,log,root,process_reaped=True),indent=2)+'\n')
