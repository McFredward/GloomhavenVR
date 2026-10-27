#!/usr/bin/env python3
"""Execute city departure repair through the existing exact native readiness kernel."""
from pathlib import Path
import hashlib,importlib.util,json,os,shutil,subprocess,sys,tempfile

root=Path(sys.argv[1]).resolve();suite='city-departure';out=root/'.planning/debug'/suite;out.mkdir(parents=True,exist_ok=True)
run=Path(tempfile.mkdtemp(prefix='run-',dir=out));project=run/'project';project.mkdir();source=run/'sources';source.mkdir()
fixture=root/'tests/GloomhavenVR.CityDepartureTests';predecessor=root/'tests/map-quest-ready-runtime'
for path in fixture.iterdir():
    if path.suffix in ('.cs','.csproj'):shutil.copy2(path,project/path.name)
native=(predecessor/'NativeDepartureFixture.cs').read_text();boundary=(predecessor/'DepartureBoundary.cs').read_text()
bindings={str(path):hashlib.sha256(path.read_bytes()).hexdigest() for path in (predecessor/'NativeDepartureFixture.cs',predecessor/'DepartureBoundary.cs')}
common=Path(subprocess.check_output(['git','-C',str(root),'rev-parse','--path-format=absolute','--git-common-dir'],text=True).strip())
native_path=common.parent/'decompiled/GH.Runtime/UIReadyToggle.cs'
if native_path.is_file():
    actual=native_path.read_text()
    for signature in ('private void OnPlayerLeft(', 'private IEnumerator<float> WaitForStateSyncBeforeProceeding(',
                      'private void Proceed(', 'public void ResetPlayerACKs(', 'public void Initialize(',
                      'public void ReadyUp(bool toggledOn, bool autoValidateUnreadying)'):
        start=actual.index(signature);opening=actual.index('{',start);end=opening+1;depth=1
        while depth:depth+=(actual[end]=='{')-(actual[end]=='}');end+=1
        assert actual[start:end] in native,'Native departure body mismatch: '+signature
    bindings[str(native_path)]=hashlib.sha256(native_path.read_bytes()).hexdigest()
    print('City departure kernel: six exact native initialization/departure/ACK/continuation bodies verified.',flush=True)
# Publicized mod access only; every native algorithm above is retained verbatim apart
# from its access modifier. Platform supplements never replace the departure/ACK path.
replacements={
    'internal sealed class UIMapMultiplayerController':'internal sealed partial class UIMapMultiplayerController',
    'internal sealed class NetworkPlayer':'internal sealed partial class NetworkPlayer',
    'internal sealed class GameAction':'internal sealed partial class GameAction',
    'internal sealed class HarmonyPatch':'internal sealed partial class HarmonyPatch',
    'internal static class VRLog':'internal static partial class VRLog',
    'private bool _requestVisible,_interactable,_allPlayersReady,_isOn;':'public bool _requestVisible,_interactable,_allPlayersReady,_isOn;',
    'private readonly HashSet<object> _visibilityRequests=new();':'public readonly HashSet<object> _visibilityRequests=new();',
    'ReadyUpPlayer,UnreadyPlayer,AllPlayersReady,ReadyProceed':'ReadyUpPlayer,UnreadyPlayer,AllPlayersReady,ReadyProceed,SelectCityEvent',
    'NotSet,Quests,CityEvents,Reward }':'NotSet,Quests,CityEvents,Reward,Retirement,TownRecords }',
}
for before,after in replacements.items():
    assert boundary.count(before)==1,'Native boundary seam changed: '+before
    boundary=boundary.replace(before,after)
assert native.count('private void UpdateVisiblity()')==1
native=native.replace('private void UpdateVisiblity()','public void UpdateVisiblity()')
(source/'Boundary.cs').write_text(boundary);(source/'Native.cs').write_text(native)
for name in ('MapCityEventReadyUp.cs','MapQuestDepartureValidation.cs'):
    original=root/'src/GloomhavenVR/WorldUI/MapRoom'/name;shutil.copy2(original,source/name)
    bindings[str(original)]=hashlib.sha256(original.read_bytes()).hexdigest()
(run/'source-bindings.json').write_text(json.dumps(bindings,indent=2)+'\n')
env=os.environ.copy();env['PATH']=str(Path(env.get('DOTNET_ROOT',str(Path.home()/'.dotnet'))))+os.pathsep+env['PATH']
base=['dotnet','build',str(project/'CityDeparture.csproj'),'-c','Release','--no-incremental','-v','quiet',
      '-p:FixtureDir='+str(project),'-p:ProductionDir='+str(source),'-p:BaseIntermediateOutputPath='+str(project/'obj')+'/','-p:OutputPath='+str(project/'bin')+'/']
log=run/'console.log';log.write_text('Evidence: '+str(run)+'\n');print('Evidence: '+str(run),flush=True);status=1
def execute(command):
    result=subprocess.run(command,env=env,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
    with log.open('a') as stream:stream.write(result.stdout)
    print(result.stdout,flush=True)
    if result.returncode:raise RuntimeError('Command failed: '+str(command))
    return result.stdout
try:
    execute(base);text=execute(['dotnet',str(project/'bin/CityDeparture.dll')])
    assertions=int(__import__('re').search(r'City native departure: (\d+) assertions',text).group(1))
    executed=run/'executed-assembly';executed.mkdir()
    for path in (project/'bin').iterdir():
        if path.is_file():shutil.copy2(path,executed/path.name)
    original=(source/'MapCityEventReadyUp.cs').read_text()
    controls=[
        ('departure-disabled','validateReadyUpOnPlayerLeft = true;','validateReadyUpOnPlayerLeft = false;',
         'native city departure validation option is enabled'),
        ('wrong-phase','if (phase == ActionPhaseType.MapHQ || phase == ActionPhaseType.MapAtLinkedScenario)','if (true)',
         'city departure scope excludes unrelated native modes and phases'),
    ]
    for name,needle,replacement,expected in controls:
        assert original.count(needle)==1,'Exact city departure mutation target: '+name
        (source/'MapCityEventReadyUp.cs').write_text(original.replace(needle,replacement))
        execute(base);result=subprocess.run(['dotnet',str(project/'bin/CityDeparture.dll')],env=env,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
        (run/(name+'.log')).write_text(result.stdout)
        if result.returncode==0 or expected not in result.stdout:raise RuntimeError('Causal departure control failed at wrong boundary: '+name+'\n'+result.stdout)
        print('City departure negative control: '+name+' rejected.',flush=True)
    (source/'MapCityEventReadyUp.cs').write_text(original)
    # Exercise real HarmonyX parameter binding and all exact target registrations, not
    # only attributes or hand-delivered prefix calls. Existing Unity Mono is reused.
    mono=Path(os.environ.get('GHVR_UNITY_MONO',str(Path.home()/'unity-2021.3.5/Editor/Data/MonoBleedingEdge/bin/mono')))
    actual=0
    if mono.is_file():
        execute(base+['-p:RealHarmony=true']);result=execute([str(mono),str(project/'bin/CityDeparture.exe')])
        actual=int(__import__('re').search(r'City actual HarmonyX: (\d+) assertions',result).group(1))
        actual_assembly=run/'actual-harmony-assembly';actual_assembly.mkdir()
        for path in (project/'bin').iterdir():
            if path.is_file():shutil.copy2(path,actual_assembly/path.name)
    else:
        print('SKIP: actual HarmonyX registration requires existing Unity Mono; no runtime pass claimed.',flush=True)
    (run/'result.json').write_text(json.dumps({'native_assertions':assertions,'negative_controls':[x[0] for x in controls],
        'actual_harmony_assertions':actual,'actual_harmony_targets':11 if actual else 0,'hardware_verified':False},indent=2)+'\n')
    status=0
finally:
    spec=importlib.util.spec_from_file_location('retention',root/'scripts/town_run_retention.py');retention=importlib.util.module_from_spec(spec);spec.loader.exec_module(retention)
    receipt=retention.cleanup_completed_run(suite,status,log,root,process_reaped=True);(run/'cleanup.json').write_text(json.dumps(receipt,indent=2)+'\n')
