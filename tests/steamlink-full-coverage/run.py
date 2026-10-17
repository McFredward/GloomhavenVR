#!/usr/bin/env python3
"""Actual old/full-res production atlas raster and structural full-halo proof."""
import argparse
import hashlib
import importlib
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import tempfile
sys.dont_write_bytecode=True
HERE=Path(__file__).resolve().parent


def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()


def overlap(a,b):return max(0,min(a[0]+a[2],b[0]+b[2])-max(a[0],b[0]))*max(0,min(a[1]+a[3],b[1]+b[3])-max(a[1],b[1]))


def profile_contract(profile):
    assert profile['schema']=='ghvr-steamlink-scene-atlas-v2'
    assert profile['canvas']==[3168,3168] and profile['scene']==[560,560,2048,2048]
    assert profile['coverage']['size']==[2048,2048] and profile['coverage']['rotation_quarter_turns']==[0,1,0,1]
    assert profile['coverage']['tile_rows']==512 and profile['coverage']['halo']==16
    rects=[o+s for o,s in zip(profile['coverage']['outer_origins'],profile['coverage']['outer_sizes'])]
    assert len(rects)==4
    for i,rect in enumerate(rects):
        assert 0<=rect[0]<rect[0]+rect[2]<=3168 and 0<=rect[1]<rect[1]+rect[3]<=3168
        assert not overlap(rect,profile['scene']),'Coverage outer rectangle overlaps contribution RGB'
        for other in rects[:i]:assert not overlap(rect,other),'Contradictory coverage halo overlap'
    assert profile['guard']['count']==12 and len(profile['guard']['codes'])==12
    guard=profile['guard'];guard_rects=[[guard['origin'][0]+i*guard['stride'][0],guard['origin'][1]+i*guard['stride'][1]]+guard['size'] for i in range(12)]
    for i,r in enumerate(guard_rects):
        assert not overlap(r,profile['scene']) and all(not overlap(r,o) for o in rects)
        assert all(not overlap(r,o) for o in guard_rects[:i])
    return {'rectangles':rects,'outer_pair_overlap_area':0,'outer_scene_overlap_area':0,'guard_cells':guard_rects,'quarter_turn_1_inverse':'atlas=origin+(localY,2048-localX)','scope':'STRUCTURAL_LAYOUT_NOT_NATIVE_SHADER_EXECUTION'}


def profile_source(p):
    def vec(v):return 'new Vector2('+','.join(str(x)+'f' for x in v)+')'
    return 'using UnityEngine;\ninternal static class FullProfile {internal static readonly Vector2[] Origins={'+','.join(vec(v) for v in p['coverage']['data_origins'])+'};internal static readonly int[] Codes={'+','.join(map(str,p['guard']['codes']))+'};internal static readonly Vector2 GuardOrigin='+vec(p['guard']['origin'])+';internal static readonly Vector2 GuardStride='+vec(p['guard']['stride'])+';}\n'



def codec_proof(run,profile,children,execute):
    # A reproducible neutral 8-bit420 roundtrip, not the SteamLink encoder/profile.
    from PIL import Image
    binary=shutil.which('ffmpeg');assert binary,'Optional codec proof requires ffmpeg'
    source=run/'full-atlas-3408.png';raw=run/'owned-codec-input.nv12';movie=run/'owned-lossless420.mkv';decoded=run/'codec-decoded.png'
    before=Image.open(source).convert('RGB');side=before.width;assert before.size==(3408,3408)
    commands=[
        [binary,'-v','error','-y','-i',str(source),'-vf','scale=in_range=pc:out_range=tv:out_color_matrix=bt709,format=nv12','-frames:v','1','-f','rawvideo',str(raw)],
        [binary,'-v','error','-y','-f','rawvideo','-pixel_format','nv12','-video_size',str(side)+'x'+str(side),'-framerate','1','-color_range','tv','-colorspace','bt709','-i',str(raw),'-frames:v','1','-c:v','libx264','-preset','ultrafast','-qp','0','-threads','1','-colorspace','bt709','-color_primaries','bt709','-color_trc','bt709',str(movie)],
        [binary,'-v','error','-y','-i',str(movie),'-vf','scale=in_range=tv:out_range=pc:in_color_matrix=bt709,format=rgb24','-frames:v','1',str(decoded)]
    ]
    record={'scope':'OWNED_BT709_LIMITED_RANGE_NV12_AND_LOSSLESS_H264420_ONLY_NOT_STEAMLINK_CODEC','ffmpeg':str(Path(binary).resolve()),'ffmpeg_sha256':sha(Path(binary)),'source_sha256':sha(source),'commands':commands}
    try:
        for i,command in enumerate(commands):assert execute(command,run/('codec-'+str(i)+'.log'),60)==0,'Optional codec command failed'
        after=Image.open(decoded).convert('RGB');mask_max=0;checks=0
        for origin,size in zip(profile['coverage']['outer_origins'],profile['coverage']['outer_sizes']):
            for yy in range(4,size[1]-4,31):
                for xx in range(4,size[0]-4,31):
                    x=int((origin[0]+xx)/3168*side);y=int((origin[1]+yy)/3168*side)
                    mask_max=max(mask_max,abs(before.getpixel((x,y))[1]-after.getpixel((x,y))[1]));checks+=1
        guard_errors=[]
        for i,code in enumerate(profile['guard']['codes']):
            point=[profile['guard']['origin'][a]+profile['guard']['stride'][a]*i+16 for a in [0,1]]
            pixel=after.getpixel(tuple(int(v/3168*side) for v in point));guard_errors.append(abs(pixel[1]-code))
        record.update({'mask_points':checks,'mask_max_code_error':mask_max,'guard_errors':guard_errors,'decoded_sha256':sha(decoded),'passed':mask_max<=2 and max(guard_errors)<=2})
        assert record['passed'],'Neutral420 numericG or format guard drift exceeded2codes'
    finally:
        if raw.is_file() and not raw.is_symlink():raw.unlink()
        record['raw_nv12_removed']=not raw.exists();(run/'codec-proof.json').write_text(json.dumps(record,indent=2)+'\n')


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--continuation',action='store_true',help='Only affected resampled halo + previously unreached motion/thin cases')
    parser.add_argument('--codec',action='store_true',help='Optional owned NV12 + lossless420 CPU roundtrip; requires ffmpeg/Pillow')
    parser.add_argument('--production-root',type=Path,default=HERE.parents[1])
    parser.add_argument('--old-bank',type=Path,help='Optional historical legacy bank; default uses retained legacy shader in current bank')
    parser.add_argument('--loader-only',action='store_true',help='One real raster case proving default same-bank loading')
    parser.add_argument('--unity',type=Path,default=Path('/home/claw/unity-2021.3.5/Editor/Unity'))
    args=parser.parse_args();root=HERE.parents[1];production=args.production_root.resolve()
    if args.codec and args.loader_only:parser.error('--codec requires the gradient/atlas cases, not --loader-only')
    if args.old_bank is None:args.old_bank=production/'src/GloomhavenVR/Assets/SteamLink/scene.bundle'
    sys.path.insert(0,str(root/'scripts'));cleanup=importlib.import_module('town_run_retention').cleanup_completed_run
    args.output.mkdir(parents=True,exist_ok=True);run=Path(tempfile.mkdtemp(prefix='run-',dir=args.output.resolve()));(run/'.keep').touch();log=run/'completion.log';log.write_text(str(run)+'\n');copies=run/'sources';copies.mkdir()
    profile_path=production/'tests/steamlink-scene-payload/profile-v2.json';profile=json.loads(profile_path.read_text());layout=profile_contract(profile)
    mutant=json.loads(json.dumps(profile));mutant['coverage']['outer_origins'][1][1]-=16
    try:profile_contract(mutant)
    except AssertionError as error:
        assert str(error)=='Contradictory coverage halo overlap'
        layout['overlapping_halo_mutant_rejected']=True
    else:raise AssertionError('Structural halo overlap control did not reject')
    (run/'layout-proof.json').write_text(json.dumps(layout,indent=2)+'\n')
    live=[HERE/name for name in ['Fixture.cs','Surface.shader','Probe.shader','run.py']]+[production/'src/GloomhavenVR/Core/SteamLink'/name for name in ['SceneEyeCoverage.cs','SceneEyeOutput.cs','SceneAtlasProfile.cs']]+[production/'unity/Assets/Shaders/SteamLinkSceneFullAtlas.shader',production/'src/GloomhavenVR/Assets/SteamLink/scene.bundle',profile_path,args.old_bank]
    pins={str(p):sha(p) for p in live};(run/'source-pins.json').write_text(json.dumps(pins,indent=2)+'\n')
    for source in live:shutil.copyfile(source,copies/(source.name+'-'+pins[str(source)][:12]))
    project=run/'owned-unity-project';assets=project/'Assets';(assets/'Editor').mkdir(parents=True)
    for source in live:
        if source.suffix=='.cs':shutil.copyfile(source,assets/'Editor'/source.name)
        elif source.suffix=='.shader' and source.parent==HERE:shutil.copyfile(source,assets/source.name)
    constants=profile_source(profile);(assets/'Editor/FullProfile.cs').write_text(constants);(copies/'FullProfile.cs').write_text(constants)
    full=run/'full-scene.bundle';old=run/'old-scene.bundle';shutil.copyfile(production/'src/GloomhavenVR/Assets/SteamLink/scene.bundle',full);shutil.copyfile(args.old_bank,old)
    packages=project/'Packages';packages.mkdir();(packages/'manifest.json').write_text('{"dependencies":{"com.unity.modules.assetbundle":"1.0.0","com.unity.modules.jsonserialize":"1.0.0"}}\n')
    settings=project/'ProjectSettings';settings.mkdir();(settings/'ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n');(settings/'ProjectSettings.asset').write_text('%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!129 &1\nPlayerSettings:\n  m_ObjectHideFlags: 0\n  serializedVersion: 26\n  m_ActiveColorSpace: 0\n')
    command=['xvfb-run','-a',str(args.unity),'-batchmode','-force-vulkan','-projectPath',str(project),'-executeMethod','FullCoverageFixture.Start','-fullResult',str(run/'gpu.json'),'-oldBank',str(old),'-fullBank',str(full),'-logFile',str(run/'unity.log')];
    if args.continuation:command.append('-continuation')
    if args.loader_only:command.append('-loaderOnly')
    if sha(full)==sha(old):command.append('-sameBank')
    (run/'command.json').write_text(json.dumps(command,indent=2)+'\n');env=os.environ.copy()
    if Path('/usr/share/vulkan/icd.d/intel_icd.json').is_file():env['VK_DRIVER_FILES']='/usr/share/vulkan/icd.d/intel_icd.json'
    code=1;children=[]
    def execute(command,logfile,timeout):
        with logfile.open('w') as stream:
            child=subprocess.Popen(command,stdout=stream,stderr=subprocess.STDOUT,start_new_session=True);children.append(child)
            try:return child.wait(timeout=timeout)
            except BaseException:
                if child.poll() is None:os.killpg(child.pid,signal.SIGKILL)
                child.wait();raise
    try:
        with (run/'launcher.log').open('w') as stream:
            child=subprocess.Popen(command,stdout=stream,stderr=subprocess.STDOUT,env=env,start_new_session=True);children.append(child)
            try:status=child.wait(timeout=300)
            except BaseException:
                if child.poll() is None:os.killpg(child.pid,signal.SIGKILL)
                child.wait();raise
        assert all(sha(Path(p))==pin for p,pin in pins.items()),'Frozen input changed through child reap'
        assert (run/'gpu.json').is_file(),'GPU result missing: '+str(run)
        result=json.loads((run/'gpu.json').read_text());assert status==0 and result['passed'],'Full-res raster failed: '+str(run)
        if args.codec:codec_proof(run,profile,children,execute)
        assert all(sha(Path(p))==pin for p,pin in pins.items()),'Frozen input changed through final child reap'
        code=0
    finally:
        reaped=all(child.poll() is not None for child in children);receipt=cleanup('steamlink-full-coverage',code,log,root,process_reaped=reaped)
        if reaped and project.is_dir() and not project.is_symlink():shutil.rmtree(project)
        (run/'cleanup.json').write_text(json.dumps({'retention_api':receipt,'all_children_reaped':reaped,'owned_project_removed':not project.exists()},indent=2)+'\n')
        (run/'results.json').write_text(json.dumps({'exit_code':code,'source_hashes_unchanged':{p:sha(Path(p))==pin for p,pin in pins.items()},'scope':'ACTUAL_UNITY_SHIPPING_PRODUCER_FULL2048_COVERAGE','native_decoder_or_codec_executed':False,'actual_headset_result_proven':False},indent=2)+'\n');print('Evidence: '+str(run))
    return code


if __name__=='__main__':raise SystemExit(main())
