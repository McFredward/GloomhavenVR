#!/usr/bin/env python3
"""Render original typed native flat-menu cards as complete capture-scoped raised planes.

Actual GH.Runtime card identities/serialized field shape, Unity/TMP/layout/masks and production
Driver/card registry/plane leases execute. Original card gameplay/bootstrap/asset-load lifecycle
bodies are suppressed; explicit card artwork/text/layout trees, converted panel membership and
XR eyes are named inputs. Glyph metrics come from TMP essentials, not the game's native atlas.
"""
from pathlib import Path
import argparse, hashlib, json, os, shutil, signal, subprocess, sys, tempfile
ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT/'scripts'))
from town_run_retention import cleanup_completed_run
sys.path.insert(0, str(ROOT/'scripts/menu-depth-character'))
from project_compilers import cleanup_project_compilers, cleanup_project_editors

def execute(command, log, timeout=300):
    with log.open('w') as stream:
        child=subprocess.Popen(command,cwd=ROOT,stdout=stream,stderr=subprocess.STDOUT,start_new_session=True)
        try:return child.wait(timeout=timeout)
        finally:
            if child.poll() is None:os.killpg(child.pid,signal.SIGKILL)
            child.wait()

def main():
    parser=argparse.ArgumentParser(description=__doc__); parser.add_argument('--case',action='append'); args=parser.parse_args()
    out=ROOT/'.planning/debug/menu-depth-cards';out.mkdir(parents=True,exist_ok=True)
    run=Path(tempfile.mkdtemp(prefix='run-',dir=out));completion=run/'completion.log';completion.write_text('Evidence: '+str(run)+'\n')
    print('Evidence: '+str(run),flush=True)
    source=ROOT/'src/GloomhavenVR/WorldUI/MenuDepth'; paths=[*source.glob('*.cs'),ROOT/'src/GloomhavenVR/Cards/Art/CardFaceMipBake.cs']
    sources={p.name:p.read_text()for p in paths}; fixture=ROOT/'tests/GloomhavenVR.MenuDepthCardTests'
    variants=[('production','','','',''),
        ('native-ui-freeze','','','',''),
        ('lost-native-freeze-parent','MenuDepthDriver.cs',
         'if (hierarchyChanged) { MovedGraphics.Add(graphic); PendingGraphics.Add(graphic); }',
         'if (hierarchyChanged && !KnownGraphics.Contains(graphic)) { MovedGraphics.Add(graphic); PendingGraphics.Add(graphic); }',
         'genuine native Graphic parent changes remain discovered'),
        ('lost-native-freeze-birth','MenuDepthDriver.cs',
         'if (!_running || graphic == null || (graphic is not Image && graphic is not RawImage)) return;',
         'if (!_running || graphic == null || (graphic is not Image && graphic is not RawImage) || !KnownGraphics.Contains(graphic)) return;',
         'real native Graphic birth during actual capture is still discovered'),
        ('lost-early-prime','MenuDepthCardPlane.cs','IMenuDepthOcclusionSource, IMenuDepthDirectSource','IMenuDepthOcclusionSource','the complete original card text follows'),
        ('lost-card-lift','MenuDepthCardPlane.cs','private const float DepthWidthRatio = .035f;','private const float DepthWidthRatio = 0f;','the complete original card text follows'),
        ('background-only-lift','MenuDepthCardPlane.cs','_owner = owner; _card = card;','_owner = owner; _card = backgrounds[0].rectTransform;','the complete original card text follows'),
        ('lost-pose-restore','MenuDepthCardPlane.cs','_card.localPosition = _position; _card.localScale = _scale;','/* injected: native root retains a private eye pose */','card capture restores the original root pose'),
        ('nested-double-lift','MenuDepthCardPlane.cs',' || (_parentPlane != null && _parentPlane.IsReady)','', 'nested native card and modifier content inherits exactly one raised parent plane'),
        ('lost-printed-visibility','MenuDepthCardPlane.cs','&& Printed() != null && Eligible();','&& Eligible();','active native card controllers with no printed graphics'),
        ('lost-late-background','MenuDepthCards.cs','plane.Rebind(windowRoot, graphics);','plane.Rebind(windowRoot, plane.DepthGraphics);','late native background assignment refreshes'),
        ('lost-fit-depth','MenuDepthCardPlane.cs',' * xScale / zScale;',';','native XY-only fitting retains a raised depth proportional'),
        ('lost-item-identity','MenuDepthCards.cs','new("ItemCardUI", false,','new("NoSuchNativeItemCard", false,','exact native full mini and item card roots receive'),
    ]
    if args.case:
        selection=set(args.case);assert selection<={v[0]for v in variants};variants=[v for v in variants if v[0]in selection]
    for name,filename,before,after,expected in variants:
        if filename:assert sources[filename].count(before)==1,(name,before)
    bundle=ROOT/'src/GloomhavenVR/Assets/MenuDepth/Rendering/menu-depth.bundle';unity=Path('/home/claw/unity-2021.3.5/Editor/Unity');game=ROOT/'ressources/GH_Data/Managed'
    pins={str(p):hashlib.sha256(p.read_bytes()).hexdigest()for p in [*paths,*fixture.glob('*'),Path(__file__),Path(__file__).parent/'Editor/Runner.cs',Path(__file__).parent/'audit-native.py',bundle,game/'GH.Runtime.dll',game/'UnityEngine.UI.dll',game/'Unity.TextMeshPro.dll',ROOT/'scripts/menu-depth-character/project_compilers.py']if p.is_file()}
    status=1;project=run/'unity'
    try:
        retained=run/'Sources';retained.mkdir(); shutil.copytree(fixture,retained/'fixture');shutil.copy2(Path(__file__),retained/'run.py');shutil.copy2(Path(__file__).parent/'Editor/Runner.cs',retained/'Runner.cs')
        for name,code in sources.items():(retained/name).write_text(code)
        (run/'source-pins.json').write_text(json.dumps({'sha256':pins,'scope':__doc__,'subset':args.case},indent=2)+'\n')
        (run/'editor-environment.json').write_text(json.dumps({k:os.environ.get(k)for k in ['ADB_SERVER_SOCKET','ADB_MDNS_AUTO_CONNECT']},indent=2)+'\n')
        audit_command=['/home/claw/unitypy-venv/bin/python',str(Path(__file__).parent/'audit-native.py'),'--output',str(run/'native-authoring.json')]
        if execute(audit_command,run/'native-authoring.log'):raise RuntimeError('Original native field audit failed')
        audit=json.loads((run/'native-authoring.json').read_text())
        required={'FullAbilityCard','MiniAbilityCard','ItemCardUI','AbilityCardUI','MonsterRoundCardUI','MonsterBaseUI','UIPerkAttackModifier','UIScenarioAttackModifier'}
        assert required <= {row['owner']['class']for row in audit['owners']},'Missing native card family'
        shutil.copy2(Path(__file__).parent/'audit-native.py',retained/'audit-native.py')
        manifest={'result':str(run/'results.txt'),'evidence':str(run),'gameManaged':str(game.resolve()),'harmonyRuntime':'/home/claw/.nuget/packages/lib.harmony/2.4.2/lib/net472','cases':[]}
        for name,filename,before,after,expected in variants:
            case=run/name; production=case/'production';production.mkdir(parents=True)
            for source_name,code in sources.items():
                if source_name==filename:code=code.replace(before,after,1)
                (production/source_name).write_text(code)
            csproj=case/'MenuDepthCard.csproj';shutil.copy2(fixture/csproj.name,csproj);assembly='MenuDepthCard_'+name.replace('-','_')
            command=['/home/claw/.dotnet/dotnet','build',str(csproj),'-c','Release','-v','quiet','--nologo','--disable-build-servers','-nr:false','-p:UseSharedCompilation=false','-p:CaseName='+assembly,'-p:FixtureDir='+str(retained/'fixture'),'-p:ProductionDir='+str(production),'-p:UnityManaged='+str(unity.parent/'Data/Managed'),'-p:GameManaged='+str(game),'-p:DepthBundle='+str(bundle)]
            if execute(command,case/'build.log'):raise RuntimeError('Compilation failure is not a causal-control pass: '+name)
            binary=case/'bin/Release/net472'/(assembly+'.dll');target=run/'assemblies';target.mkdir(exist_ok=True);shutil.copy2(binary,target/binary.name)
            manifest['cases'].append({'name':name,'dll':str(target/binary.name),'expected':expected,'method':'RunNativeUiFreeze' if name in {'native-ui-freeze','lost-native-freeze-birth','lost-native-freeze-parent'} else 'Run'})
        for folder in ['Assets/Editor','Packages','ProjectSettings']:(project/folder).mkdir(parents=True)
        (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
        (project/'Packages/manifest.json').write_text(json.dumps({'dependencies':{'com.unity.ugui':'1.0.0','com.unity.textmeshpro':'3.0.6','com.unity.modules.assetbundle':'1.0.0','com.unity.modules.imageconversion':'1.0.0'}})+'\n')
        shutil.copy2(Path(__file__).parent/'Editor/Runner.cs',project/'Assets/Editor/Runner.cs');(run/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
        command=['xvfb-run','-a',str(unity),'-batchmode','-force-glcore','-projectPath',str(project),'-executeMethod','MenuDepthCardRunner.Start','-menuDepthCardManifest',str(run/'manifest.json'),'-logFile',str(run/'unity.log')]
        (run/'command.json').write_text(json.dumps(command)+'\n');status=execute(command,run/'launch.log',420)
        stable=all(Path(p).is_file()and hashlib.sha256(Path(p).read_bytes()).hexdigest()==h for p,h in pins.items());(run/'source-stability.json').write_text(json.dumps({'unchanged':stable,'inputs':len(pins)})+'\n')
        if not stable:status=1
        if(run/'results.txt').is_file():print((run/'results.txt').read_text(),flush=True)
        if status:raise RuntimeError('Native card GPU proof failed: '+str(run))
    finally:
        editors=cleanup_project_editors(project,unity,launcher_reaped=True);(run/'editor-cleanup.json').write_text(json.dumps(editors,indent=2)+'\n');assert not editors['remaining']
        compilers=cleanup_project_compilers(project,editor_reaped=True);(run/'compiler-cleanup.json').write_text(json.dumps(compilers,indent=2)+'\n');assert not compilers['remaining']
        destination=run/'assemblies/editor';destination.mkdir(parents=True,exist_ok=True)
        for p in (project/'Library/ScriptAssemblies').glob('*'):
            if p.is_file()and p.suffix in ['.dll','.pdb','.mdb']:shutil.copy2(p,destination/p.name)
        receipt=cleanup_completed_run('menu-depth-cards',status,completion,ROOT,process_reaped=True);(run/'cache-cleanup.json').write_text(json.dumps(receipt,indent=2)+'\n')
    return status
if __name__=='__main__':raise SystemExit(main())
