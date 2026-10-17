#!/usr/bin/env python3
"""Owned scene contribution pack/decode experiment, never a live source admission."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import tempfile

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
from town_run_retention import cleanup_completed_run


def digest(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()


def execute(args, log, **kwargs):
    with log.open('w') as out:
        child = subprocess.Popen(args, stdout=out, stderr=subprocess.STDOUT,
                                 start_new_session=True, **kwargs)
        try:
            return child.wait(timeout=240)
        except BaseException:
            if child.poll() is None: os.killpg(child.pid, signal.SIGKILL)
            child.wait()
            raise


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    mode=parser.add_mutually_exclusive_group()
    mode.add_argument('--gpu', action='store_true', help='Actual owned Unity GPU experiment; no XR/device access')
    mode.add_argument('--cpu-only', action='store_true', help='Only the new source-bound CPU geometry controls')
    parser.add_argument('--color-space',choices=['Gamma','Linear','both'],default='both')
    parser.add_argument('--unity', type=Path, default=Path('/home/claw/unity-2021.3.5/Editor/Unity'))
    parser.add_argument('--output-root', type=Path, default=ROOT/'.planning/debug/steamlink-scene-payload-worker-20261017')
    args = parser.parse_args()
    args.output_root.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_root))
    completion=run/'completion.log'; completion.write_text('Owned scene pack/decode only\nevidence: '+str(run)+'\n')
    fixture=Path(__file__).resolve().parent
    source=ROOT/'src/GloomhavenVR/Core/SteamLink/ScenePayloadDraft.cs'
    files=[source,*[p for p in fixture.iterdir() if p.is_file()]]
    pins={str(p.relative_to(ROOT)):digest(p) for p in files}
    (run/'source-hashes.json').write_text(json.dumps(pins,indent=2)+'\n')
    sources=run/'sources'; sources.mkdir()
    for p in files: shutil.copy2(p,sources/p.name)
    result={'scope':'OWNED_SAME_EYE_DRAFT_RT_PACK_DECODE_ONLY','gpu_executed':False,
            'game_coverage_authorized':False,'native_carriage_proven':False,
            'passthrough_authorized':False,'stages':[]}
    dotnet=shutil.which('dotnet') or str(Path.home()/'.dotnet/dotnet')
    code=1
    def checked(name,cmd,**kwargs):
        nonlocal code
        code=execute(cmd,run/(name+'.log'),**kwargs)
        result['stages'].append({'name':name,'exit_code':code})
        if code: raise RuntimeError(name+' failed; evidence '+str(run))
    try:
        managed=args.unity.parent/'Data/Managed'
        framework=Path.home()/'.nuget/packages/microsoft.netframework.referenceassemblies.net472/1.0.3/build/.NETFramework/v4.7.2'
        checked('cpu-layout',[sys.executable,'-B',str(fixture/'layout_controls.py')])
        result['cpu_controls']=json.loads((run/'cpu-layout.log').read_text())
        for target in ([] if args.cpu_only else ['Fixture'] if args.gpu else ['Typecheck','Fixture']):
            d=run/target;d.mkdir(); project=d/(target+'.csproj');shutil.copy2(fixture/project.name,project)
            checked(target+'-build',[dotnet,'build','--disable-build-servers',str(project),'-c','Release',
                '--configfile',str(fixture/'NuGet.Config'),'-p:SourceRoot='+str(ROOT),'-p:FixtureDir='+str(fixture),
                '-p:UnityManaged='+str(managed),'-p:FrameworkReferenceDir='+str(framework),'-p:UseSharedCompilation=false'])
        if args.gpu:
            assembly=run/'SteamLinkScenePayloadGpu.dll'
            shutil.copy2(run/'Fixture/bin/Release/netstandard2.1/SteamLinkScenePayloadGpu.dll',assembly)
            for color_space in (['Gamma','Linear'] if args.color_space=='both' else [args.color_space]):
                d=run/color_space;d.mkdir(); project=d/'unity'
                (project/'Assets/Editor').mkdir(parents=True)
                shaders=project/'Assets/Shaders';shaders.mkdir()
                shutil.copy2(fixture/'EditorRunner.cs',project/'Assets/Editor/EditorRunner.cs')
                shutil.copy2(fixture/'GpuFixture.cs',project/'Assets/Editor/GpuFixture.cs')
                shutil.copy2(source,project/'Assets/Editor/ScenePayloadDraft.cs')
                for shader in fixture.glob('*.shader'):shutil.copy2(shader,shaders/shader.name)
                decode=(fixture/'ScenePayloadDecode.shader').read_text()
                assert decode.count('return float4(e,a);')==1
                (shaders/'DoublePremultiply.shader').write_text(decode.replace('ScenePayloadDraft/Decode','ScenePayloadDraft/DecodeDoublePremultiply').replace('return float4(e,a);','return float4(e*a,a);'))
                pack=(fixture/'ScenePayloadPack.shader').read_text()
                anchor='float2 q = clamp(global/1024,0.5/1024,1-0.5/1024);'
                assert pack.count(anchor)==1
                (shaders/'WrongHalo.shader').write_text(pack.replace('ScenePayloadDraft/Pack','ScenePayloadDraft/PackWrongHalo').replace(anchor,'global.y = clamp(global.y,t*256+0.5,(t+1)*256-0.5);\n                    '+anchor))
                (project/'Packages').mkdir();(project/'Packages/manifest.json').write_text('{"dependencies":{}}\n')
                settings=project/'ProjectSettings';settings.mkdir()
                (settings/'ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
                (settings/'ProjectSettings.asset').write_text('%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!129 &1\nPlayerSettings:\n  m_ObjectHideFlags: 0\n  serializedVersion: 26\n  m_ActiveColorSpace: '+('1' if color_space=='Linear' else '0')+'\n')
                env=dict(os.environ);env['VK_DRIVER_FILES']='/usr/share/vulkan/icd.d/intel_icd.json'
                result['gpu_executed']=True
                checked('gpu-'+color_space,['xvfb-run','-a','-s','-screen 0 800x600x24',str(args.unity),'-batchmode','-force-vulkan',
                    '-projectPath',str(project),'-executeMethod','ScenePayloadEditorRunner.Start','-sceneEvidence',str(d),'-logFile',str(d/'unity.log')],env=env)
                gpu=json.loads((d/'gpu-result.json').read_text());assert gpu['passed'] is True
                assert gpu['color_space']==color_space and gpu['game_coverage_authorized'] is False
                result.setdefault('gpu_results',[]).append(gpu)
        assert pins=={str(p.relative_to(ROOT)):digest(p) for p in files},'Source changed during owned run'
        code=0
    finally:
        result['exit_code']=code
        (run/'result.json').write_text(json.dumps(result,indent=2)+'\n')
        receipt=cleanup_completed_run('steamlink-scene-payload',code,completion,ROOT,process_reaped=True)
        # Unity package documentation/Bee graph data are regenerable fixture
        # caches too. Never retain or sweep another project's input references.
        fallback=[]
        for color in ['Gamma','Linear']:
            for relative in ['Library','Temp','Obj','obj','Logs']:
                p=run/color/'unity'/relative
                if p.exists():
                    assert p.is_dir() and not p.is_symlink() and run in p.parents
                    size=sum(q.stat().st_size for q in p.rglob('*') if q.is_file())
                    shutil.rmtree(p);fallback.append({'path':str(p.relative_to(run)),'bytes':size})
        receipt['exact_owned_cache_fallback']=fallback
        (run/'cleanup.json').write_text(json.dumps(receipt,indent=2)+'\n')
        print(json.dumps({'run':str(run),'exit_code':code,'gpu_executed':result['gpu_executed']}))
    return code


if __name__=='__main__':raise SystemExit(main())
