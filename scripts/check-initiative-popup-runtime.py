#!/usr/bin/env python3
"""Render the production enemy-popup depth and portrait seat in actual Unity/uGUI/TMP.
Native board/popup rectangles are declared witnesses; this does not replay a paired
hardware session or the complete native initiative controller. Both real call sites
are input-bound so a passing helper cannot hide a missing production consumer.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import tempfile
from town_run_retention import cleanup_completed_run

ROOT=Path(__file__).resolve().parents[1]

def execute(command,log,timeout=240,env=None):
    with log.open('w') as stream:
        child=subprocess.Popen(command,stdout=stream,stderr=subprocess.STDOUT,start_new_session=True,env=env)
        try:return child.wait(timeout=timeout)
        finally:
            if child.poll() is None:os.killpg(child.pid,signal.SIGKILL)
            child.wait()

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root',type=Path,default=ROOT)
    parser.add_argument('--output-dir',type=Path,default=ROOT/'.planning/debug/initiative-popup-runtime')
    parser.add_argument('--case',action='append',help='Run only selected production/control cases (partial proof).')
    args=parser.parse_args();root=args.source_root.resolve();out=args.output_dir.resolve();out.mkdir(parents=True,exist_ok=True)
    run=Path(tempfile.mkdtemp(prefix='run-',dir=out));completion=run/'completion.log';completion.write_text('Evidence: '+str(run)+'\n')
    status=1
    own_adb=not os.environ.get('ADB_SERVER_SOCKET')
    env=dict(os.environ)
    if own_adb:
        env['ADB_SERVER_SOCKET']='localfilesystem:/tmp/gvr-popup-'+run.name+'.sock'
        env['ADB_MDNS_AUTO_CONNECT']=''
    (run/'editor-environment.json').write_text(json.dumps({name:env.get(name) for name in ['ADB_SERVER_SOCKET','ADB_MDNS_AUTO_CONNECT']},indent=2)+'\n')
    try:
        conversion=root/'src/GloomhavenVR/WorldUI/Conversion'
        paths=[conversion/'OnTopUiGraphics.cs',conversion/'InitiativePopupPlacement.cs',root/'src/GloomhavenVR/WorldUI/Surfaces/TablePanelSurfaces.cs',root/'src/GloomhavenVR/Net/Remote/RemoteInitiativeTrack.cs']
        material,seat,local,remote=(p.read_text() for p in paths)
        assert 'OnTopUiGraphics _enemyInfoOnTop = new(UnityEngine.Rendering.CompareFunction.LessEqual)' in local,'local popup must use real world depth'
        assert 'OnTopUiGraphics _popupOnTop = new(UnityEngine.Rendering.CompareFunction.LessEqual)' in remote,'remote popup must use real world depth'
        assert '_enemyPopupSeat.Place(popupRect, portrait, board)' in local,'local popup must consume shared portrait seat'
        assert '_popupSeat.Place(popupRect, node.Portrait, _boardRoot)' in remote,'remote popup must consume shared portrait seat'
        assert remote.index('SeatPopupOverlay(node.ActorId, node.Popup)')<remote.index('if (wantPopup && !node.PopupLaidOut'),'popup order must follow the live owner band on every visible tick'
        assert remote.index('LayoutMirroredPopup(node.ActorId, node.Popup, node.PopupSource)')<remote.index('_popupSeat.Place(popupRect, node.Portrait, _boardRoot)'),'remote seat must follow actual clone layout'
        fixture=ROOT/'scripts/initiative-popup-runtime';unity=Path('/home/claw/unity-2021.3.5/Editor/Unity');managed=root/'ressources/GH_Data/Managed'
        sources={'OnTopUiGraphics.cs':material,'InitiativePopupPlacement.cs':seat,**{p.name:p.read_text() for p in fixture.glob('*.cs')}}
        cases=[('production',None,None,None,''),
            ('always-depth','OnTopUiGraphics.cs','clone.SetInt(GuiZTestMode, (int)depthTest);','clone.SetInt(GuiZTestMode, (int)CompareFunction.Always);','nearer other board occludes popup in final pixels'),
            ('stale-draw-band','InitiativePopupPlacement.cs','if (overlay.sortingOrder != want) overlay.sortingOrder = want;','if (overlay.sortingOrder == 0) overlay.sortingOrder = want;','nearer depthless board still occludes popup after its owner\'s draw band changes'),
            ('font-always-depth','OnTopUiGraphics.cs','text.fontSharedMaterial = clone;','text.fontSharedMaterial = clone; clone.SetInt(GuiZTestMode, (int)CompareFunction.Always);','nearer board occludes original TMP glyph region as well as the card background'),
            ('shared-holder-origin','InitiativePopupPlacement.cs','(left + right) * .5f','0f','popup sits directly below its displayed portrait after reorder and board motion'),
            ('missing-rim','InitiativePopupPlacement.cs','_rim - RimClearance','0f','popup clears its own opaque rim'),
            ('missing-restore','InitiativePopupPlacement.cs','popup.localPosition = pose;','// missing native pose restoration','popup closure restores its native source pose')]
        if args.case:
            names={c[0] for c in cases};assert set(args.case)<=names,'Unknown selected case';cases=[c for c in cases if c[0] in args.case]
        inputs=run/'native-inputs'
        code=execute([str(Path.home()/'unitypy-venv/bin/python'),str(ROOT/'scripts/levelup-native-runtime/export_inputs.py'),str(root),str(inputs)],run/'export.log')
        assert code==0,'Native font export failed: '+str(run/'export.log')
        manifest={'result':str(run/'results.txt'),'inputs':str(inputs),'cases':[]}
        hashes={str(p.relative_to(root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in paths}
        for p in [Path(__file__),*fixture.rglob('*')]:
            if p.is_file():hashes[str(p.relative_to(ROOT))]=hashlib.sha256(p.read_bytes()).hexdigest()
        project=run/'unity-project'
        for rel in ['Assets/Editor','Assets/Plugins','Assets/Resources/OriginalShaders','ProjectSettings','Packages']:(project/rel).mkdir(parents=True)
        for name,file,old,new,expected in cases:
            case=run/name;production=case/'production';production.mkdir(parents=True)
            for path,source in sources.items():
                if path==file:
                    assert old in source,'Missing mutation seam '+name;source=source.replace(old,new)
                (production/path).write_text(source)
            csproj=case/'Popup.csproj';shutil.copyfile(fixture/'Popup.csproj',csproj)
            assembly='InitiativePopup_'+name.replace('-','_')
            code=execute([str(Path.home()/'.dotnet/dotnet'),'build',str(csproj),'-c','Release','--nologo','--verbosity','quiet','-p:UseSharedCompilation=false','-nodeReuse:false','-p:CaseName='+assembly,'-p:ProductionDir='+str(production),'-p:FixtureDir='+str(case/'none'),'-p:UnityManaged='+str(unity.parent/'Data/Managed'),'-p:GameManaged='+str(managed)],case/'build.log',120)
            assert code==0,'Fixture compilation failed: '+str(case/'build.log')
            dll=case/'bin/Release/netstandard2.1'/(assembly+'.dll');dest=project/'Assets/Plugins'/dll.name;shutil.copyfile(dll,dest)
            manifest['cases'].append({'name':name,'dll':str(dest),'expected':expected})
        for name in ['UnityEngine.UI.dll','Unity.TextMeshPro.dll']:shutil.copyfile(managed/name,project/'Assets/Plugins'/name)
        overlay=root/'unity/GloomhavenVR.Assets/Assets/Bundle/Table/Overlay.shader';shutil.copyfile(overlay,project/'Assets/Resources/Overlay756.shader');hashes[str(overlay)]=hashlib.sha256(overlay.read_bytes()).hexdigest()
        shaders=root/'unity/GloomhavenVR.Assets/Assets/Bundle/HealthBars'
        source=(shaders/'ActorBarSDF.shader').read_text().replace('Shader "GloomhavenVR/ActorBarSDF"','Shader "TextMeshPro/Distance Field"',1)
        source=source.replace('\n\t\t\t#include "ActorBarForeground.cginc"','',1)
        source=re.sub(r'\n\t\t\tGHVRActorBarForegroundClip\([^;]*\);','',source)
        source=re.sub(r'\n\t\t\tfloat4 ghvrScreenPosition : TEXCOORD7;','',source)
        source=re.sub(r'\n\t\t\tOUT.ghvrScreenPosition = ComputeScreenPos\(OUT.vertex\);','',source)
        source=source.replace('\n\t\t\t\tComputeScreenPos(vPosition),','').replace('Fallback "GloomhavenVR/ActorBarMobileSDF"','Fallback "TextMeshPro/Mobile/Distance Field"')
        provenance=json.loads((shaders/'source-provenance.json').read_text());record=next(r for r in provenance['sources'] if r['file']=='ActorBarSDF.shader');original=source
        for pragma in record['retained_feature_pragmas']:
            before=pragma.strip().replace('shader_feature','multi_compile');assert original.count(before)==1;original=original.replace(before,pragma.strip())
        assert hashlib.sha256(original.encode()).hexdigest()==record['normalized_original_sha256'],'Original TMP shader binding drift'
        (project/'Assets/Resources/OriginalShaders/NativeSDF.shader').write_text(source)
        for name in ['TMPro.cginc','TMPro_Properties.cginc']:shutil.copyfile(shaders/name,project/'Assets/Resources/OriginalShaders'/name)
        for p in [shaders/'ActorBarSDF.shader',shaders/'source-provenance.json',shaders/'TMPro.cginc',shaders/'TMPro_Properties.cginc',*inputs.glob('*'),managed/'UnityEngine.UI.dll',managed/'Unity.TextMeshPro.dll']:
            if p.is_file():hashes[str(p)]=hashlib.sha256(p.read_bytes()).hexdigest()
        (run/'source-pins.json').write_text(json.dumps(hashes,indent=2)+'\n')
        shutil.copyfile(fixture/'Editor/PopupRunner.cs',project/'Assets/Editor/PopupRunner.cs')
        (project/'Packages/manifest.json').write_text(json.dumps({'dependencies':{name:'1.0.0' for name in ['com.unity.modules.ui','com.unity.modules.imgui','com.unity.modules.jsonserialize','com.unity.modules.physics','com.unity.modules.imageconversion']}}))
        (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
        path=run/'manifest.json';path.write_text(json.dumps(manifest,indent=2)+'\n')
        command=[str(unity),'-batchmode','-force-glcore','-projectPath',str(project),'-executeMethod','PopupRunner.Run','-popupManifest',str(path),'-logFile',str(run/'Unity.log')]
        if not os.environ.get('DISPLAY'):command=['xvfb-run','-a',*command]
        code=execute(command,run/'launcher.log',env=env);text=(run/'results.txt').read_text() if (run/'results.txt').is_file() else ''
        print(text,end='');assert code==0,'Actual Unity proof failed: '+str(run)
        assert 'PASS production:' in text if not args.case or 'production' in args.case else True
        assert text.count('PASS negative control ')==sum(1 for case in cases if case[0]!='production'),'Missing compiling causal control evidence'
        (run/'limits.txt').write_text(__doc__+'\nNative font/art properties and original canonical TMP shader are source-bound. UI and board rectangle witnesses, driver type identities and logging are explicit ports. No headset/network-renderer claim.\n')
        status=0;print('Evidence: '+str(run))
    finally:
        if own_adb:
            adb=shutil.which('adb')
            if adb:
                stopped=subprocess.run([adb,'kill-server'],env=env,capture_output=True,text=True,timeout=20)
                (run/'adb-cleanup.log').write_text(stopped.stdout+stopped.stderr)
        receipt=cleanup_completed_run('initiative-popup-runtime',status,completion,ROOT,process_reaped=True)
        (run/'cleanup.json').write_text(json.dumps(receipt,indent=2)+'\n')

if __name__=='__main__':main()
