#!/usr/bin/env python3
"""Exercise production English resident voice scheduling/curves/playback in Unity."""
import argparse,json,os,shutil,subprocess,tempfile
from pathlib import Path
repo=Path(__file__).resolve().parent.parent
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--no-negative-controls',action='store_true')
args=parser.parse_args()
output=repo/'.planning/debug/town-voice';output.mkdir(parents=True,exist_ok=True)
out=Path(tempfile.mkdtemp(prefix='run-',dir=output))
fixture=repo/'scripts/town-voice-runtime';base=repo/'src/GloomhavenVR/WorldUI/TownServices'
source={n+'.cs':(base/(n+'.cs')).read_text() for n in ['TownServiceVoice','TownServiceVoiceSchedule','TownServiceVoiceCurve','TownServiceFaceSpeech']}
variants=[('production',None,None,None,''),
 ('overlap','TownServiceVoiceSchedule.cs','foreach (Entry other in _entries) if (other.Cue != 0) return;','', 'only one resident speaks at a time'),
 ('restart','TownServiceVoice.cs','if (different)','if (different || _source != null)','same cue packet never restarts audio'),
 ('ignore-volume','TownServiceVoice.cs','_volume * (cue == 7 ? .16f : .52f)','1f','master and story sliders scale greeting'),
 ('ignore-narration','TownServiceVoiceSchedule.cs','(narration || now - e.Started >= duration(e.Cue))','(now - e.Started >= duration(e.Cue))','native narration interrupts resident speech'),
 ('closure','TownServiceVoiceCurve.cs','default: return Vector3.zero;','default: return new Vector3(.5f, 0f, 0f);','closed/silent interval closes mouth'),
 ('forget-adoption','TownServiceVoiceSchedule.cs','e.Started = now - age;','e.Started = now;','shared invitation age survives handover'),
 ('restart-ended','TownServiceVoiceSchedule.cs','age + .001f < e.ObservedAge || e.Ended','age + .001f < e.ObservedAge','ended cue cannot reopen')]
variants += [
 ('premature-enchantress','TownServiceVoiceSchedule.cs','beginning && service != 3 && age <= 2f','beginning && age <= 2f','enchantress does not greet before the shared hand gesture'),
 ('no-hand-invite','TownServiceVoiceSchedule.cs','e.LastAttention < .35f && attention >= .35f','false','hand extension selects one author-owned invitation'),
 ('hard-mouth-boundary','TownServiceVoiceCurve.cs','joinedRight ? .5f : 1f','1f','shared phoneme boundary does not step the mouth'),
 ('stale-late-join','TownServiceVoice.cs',
  'if (_playingService == service)\n            {\n                if (_source != null) _source.Stop();',
  'if (_playingService == 255)\n            {\n                if (_source != null) _source.Stop();',
  'late join skips expired shared cue and stops stale resident audio')]
if args.no_negative_controls: variants=variants[:1]
manifest={'result':str(out/'results.txt'),'cases':[]}
unity=Path('/home/claw/unity-2021.3.5/Editor/Unity'); managed=repo/'ressources/GH_Data/Managed'
for name,target,before,after,expected in variants:
 run=out/name;prod=run/'production';prod.mkdir(parents=True)
 for filename,text in source.items():
  if filename==target:
   assert text.count(before)==1,(name,text.count(before));text=text.replace(before,after)
  (prod/filename).write_text(text)
 project=run/'Speech.csproj';shutil.copyfile(repo/'scripts/town-service-mirror-runtime/Mirror.csproj',project)
 cmd=[str(Path.home()/'.dotnet/dotnet'),'build',str(project),'-c','Release','-v','quiet','--nologo',
      '-p:CaseName=Speech_'+name.replace('-','_'),f'-p:FixtureDir={fixture}',f'-p:ProductionDir={prod}',
      f'-p:UnityManaged={unity.parent/"Data/Managed"}',f'-p:UnityUi={managed/"UnityEngine.UI.dll"}',f'-p:UnityTmp={managed/"Unity.TextMeshPro.dll"}']
 done=subprocess.run(cmd,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT);(run/'build.log').write_text(done.stdout)
 if done.returncode:raise SystemExit(done.stdout)
 manifest['cases'].append({'name':name,'dll':str(run/'bin/Release/netstandard2.1'/('Speech_'+name.replace('-','_')+'.dll')),'expected':expected})
 print('Compiled',name,flush=True)
project=out/'unity';(project/'Assets/Editor').mkdir(parents=True);(project/'Packages').mkdir();(project/'ProjectSettings').mkdir()
shutil.copyfile(repo/'scripts/town-service-interaction-runtime/Editor/InteractionRunner.cs',project/'Assets/Editor/InteractionRunner.cs')
(project/'Packages/manifest.json').write_text('{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6"}}')
(project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
path=out/'manifest.json';path.write_text(json.dumps(manifest))
env=dict(os.environ,TOWN_SPEECH_ASSETS=str(repo/'unity/GloomhavenVR.Assets/Assets/Bundle/TownServices/Audio'))
result=subprocess.run([str(unity),'-batchmode','-nographics','-projectPath',str(project),'-executeMethod','InteractionRunner.Start',
 '-interactionManifest',str(path),'-logFile',str(out/'unity.log')],timeout=240,stdout=subprocess.DEVNULL,env=env)
print((out/'results.txt').read_text() if (out/'results.txt').exists() else 'No results');print('Evidence:',out)
raise SystemExit(result.returncode)
