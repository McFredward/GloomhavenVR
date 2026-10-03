#!/usr/bin/env python3
"""Exercise production resident voice scheduling, curves and playback in Unity."""
import argparse,json,os,shutil,subprocess,tempfile
from pathlib import Path
repo=Path(__file__).resolve().parent.parent
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--no-negative-controls',action='store_true')
parser.add_argument('--negative-control',action='append',default=[],help='Run named controls and production; partial focused evidence')
args=parser.parse_args()
dotnet=shutil.which('dotnet') or str(Path.home()/'.dotnet/dotnet')
portable=subprocess.run([dotnet,'run','--project',str(repo/'scripts/town-voice-runtime/PortableSchedule.csproj'),
                         '-c','Release','--nologo'],text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
if portable.returncode: raise SystemExit(portable.stdout)
print(portable.stdout.strip(),flush=True)
output=repo/'.planning/debug/town-voice';output.mkdir(parents=True,exist_ok=True)
out=Path(tempfile.mkdtemp(prefix='run-',dir=output))
fixture=repo/'scripts/town-voice-runtime';base=repo/'src/GloomhavenVR/WorldUI/TownServices'
source={n+'.cs':(base/(n+'.cs')).read_text() for n in ['TownServiceVoice','TownServiceVoiceSchedule','TownServiceVoiceCurve','TownServiceFaceSpeech']}
variants=[('production',None,None,None,''),
 ('overlap','TownServiceVoiceSchedule.cs',
  '        e.Pending = false;\n        if (e.Generation == uint.MaxValue) return;',
  '        foreach (Entry other in _entries) if (other != e && other.Cue != 0) return;\n        e.Pending = false;\n        if (e.Generation == uint.MaxValue) return;',
  'a priestess greeting cannot block the merchant\'s simultaneous visitor greeting'),
 ('restart','TownServiceVoice.cs','if (different)','if (different || source != null)','same cue packet never restarts audio'),
 ('ignore-volume','TownServiceVoice.cs','_volume * speechGain','1f','master and story sliders scale greeting'),
 ('quiet-priestess','TownServiceVoice.cs','if (service == 2) speechGain *= 1.30f;','if (service == 2) speechGain *= 1f;','priestess source gain compensates measured integrated loudness'),
 ('quiet-incantation','TownServiceVoice.cs','IsWhisperedCastCue(cue) ? .22f','IsWhisperedCastCue(cue) ? .42f','mystical incantation stays below ordinary speech'),
 ('ignore-narration','TownServiceVoiceSchedule.cs','(narration || now - e.Started >= duration(e.Cue))','(now - e.Started >= duration(e.Cue))','native narration interrupts resident speech'),
 ('closure','TownServiceVoiceCurve.cs','default: return Vector3.zero;','default: return new Vector3(.5f, 0f, 0f);','closed/silent interval closes mouth'),
 ('forget-adoption','TownServiceVoiceSchedule.cs','e.Started = now - age;','e.Started = now;','shared invitation age survives handover'),
 ('restart-ended','TownServiceVoiceSchedule.cs','age + .001f < e.ObservedAge || e.Ended','age + .001f < e.ObservedAge','ended cue cannot reopen')]
variants += [
 ('story-reaction-relay','TownServiceVoice.cs','if (StoryComposite.PointOfNoReturn) return;','if (StoryComposite.PointOfNoReturn && service == byte.MaxValue) return;','point of no return cannot relay a new resident reaction'),
 ('story-cue-survives','TownServiceVoiceSchedule.cs','entry.Pending = false;\n            entry.Cue = 0;','entry.Pending = false;','story commitment retires active and queued resident speech'),
 ('missing-enchantress-visit','TownServiceVoiceSchedule.cs','beginning && age <= 2f','beginning && service != 3 && age <= 2f','enchantress native visit starts an invitation even if attention was already raised'),
 ('stale-enchantress-invitation','TownServiceVoiceSchedule.cs',
  'if (service == 3 && (firstCue == 61 || firstCue == 46))',
  'if (service == 3 && firstCue == ushort.MaxValue)',
  'completed enhancement retires an obsolete inspection line'),
 ('duplicate-hand-invite','TownServiceVoiceSchedule.cs',
  'e.WorkSeeded = true; e.FollowerAttentionKnown = false;\n        e.LastWorkClock = clock;',
  'if (service == 3 && e.WorkSeeded && e.LastAttention < .35f && attention >= .35f) QueueVariant(service, 51, 1, now + 4f, clock); e.WorkSeeded = true; e.FollowerAttentionKnown = false;\n        e.LastWorkClock = clock;',
  'hand extension never duplicates the native-visit greeting'),
 ('repeat-variant','TownServiceVoiceSchedule.cs',
  'if (cue == e.LastVariantCue)',
  'if (e.LastVariantCue == ushort.MaxValue)',
  'event selection cannot repeat the immediately preceding performance'),
 ('hard-mouth-boundary','TownServiceVoiceCurve.cs','joinedRight ? .5f : 1f','1f','shared phoneme boundary does not step the mouth'),
 ('stale-late-join','TownServiceVoice.cs',
  'if (playback.Cue != 0 && playback.Source != null) playback.Source.Stop();\n            playback.Cue = 0; playback.Generation = 0; HeadEar.Release(ear);\n            return;\n        }\n        if (!HeadEar.Claim(ear))',
  'if (playback.Cue == 255 && playback.Source != null) playback.Source.Stop();\n            playback.Cue = 0; playback.Generation = 0; HeadEar.Release(ear);\n            return;\n        }\n        if (!HeadEar.Claim(ear))',
  'late join skips expired shared cue and stops stale resident audio'),
 ('ambient-prayer-spam','TownServiceVoiceSchedule.cs',
  'e.NextAmbientAllowed = now + 180f;', 'e.NextAmbientAllowed = now;',
  'incidental prayer stays quiet on the next short occupation cycle'),
 ('ambient-cast-spam','TownServiceVoiceSchedule.cs',
  'e.NextAmbientAllowed = now + 150f;', 'e.NextAmbientAllowed = now;',
  'repeated visual spells do not repeat incidental speech every cast'),
 ('merchant-reply-service','TownServiceVoice.cs',
  '|| cue >= 66 && cue <= 75 ? (byte)1', '? (byte)1',
  'curve rejected cue=66'),
 ('stock-author-source','TownServiceVoice.cs',
  'if (!TownServicePopulation.IsFaceAuthor) return StockRelayRequest?.Invoke(reaction) == true;',
  'if (reaction == (TownVoiceReaction)byte.MaxValue) return StockRelayRequest?.Invoke(reaction) == true;',
  'non-author stock inspection uses its independent cosmetic source'),
 ('stock-replay-namespace','TownServiceVoice.cs',
  '(stock ? (byte)4 : service)', 'service',
  'stock replay lifetime is independent of a larger private merchant generation'),
 ('ambient-never-silent','TownServiceVoiceSchedule.cs',
  '% 100u >= 45u', '% 100u >= 0u',
  'optional idle speech sometimes remains silent without rerolling'),
 ('voice-ignores-physical-owner','TownServiceVoice.cs',
  'return owner == 0 || owner == visitor;', 'return true;',
  'physical peer ownership prevents the local spectator from starting merchant speech')]
if args.no_negative_controls: variants=variants[:1]
if args.negative_control:
 unknown=set(args.negative_control)-{v[0] for v in variants}
 if unknown:parser.error('Unknown controls: '+', '.join(sorted(unknown)))
 variants=[v for v in variants if v[0]=='production' or v[0] in args.negative_control]
manifest={'result':str(out/'results.txt'),'cases':[]}
unity=Path('/home/claw/unity-2021.3.5/Editor/Unity'); managed=repo/'ressources/GH_Data/Managed'
for name,target,before,after,expected in variants:
 run=out/name;prod=run/'production';prod.mkdir(parents=True)
 for filename,text in source.items():
  if filename==target:
   assert text.count(before)==1,(name,text.count(before));text=text.replace(before,after)
  (prod/filename).write_text(text)
 project=run/'Speech.csproj';shutil.copyfile(repo/'scripts/town-service-mirror-runtime/Mirror.csproj',project)
 cmd=[dotnet,'build',str(project),'-c','Release','-v','quiet','--nologo',
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
