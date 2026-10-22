#!/usr/bin/env python3
"""Execute reward placement/election/collector and Continue kernels with explicit native boundaries."""
import argparse
import hashlib
import json
import os
import re
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import tempfile
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'scripts'))
from town_run_retention import cleanup_completed_run

def block(text,signature):
    start=text.index(signature);at=text.index('{',start);end=at+1;depth=1
    while depth:depth+=(text[end]=='{')-(text[end]=='}');end+=1
    return text[start:end]

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--source-root',type=Path,default=ROOT);args=parser.parse_args();source=args.source_root
    dotnet=os.environ.get('DOTNET') or shutil.which('dotnet') or str(Path.home()/'.dotnet/dotnet')
    environment=os.environ.copy()
    environment.update(DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER='1',MSBUILDDISABLENODEREUSE='1',DOTNET_CLI_TELEMETRY_OPTOUT='1')
    parent=ROOT/'.planning/debug/shared-reward-placement-runtime';parent.mkdir(parents=True,exist_ok=True)
    run=Path(tempfile.mkdtemp(prefix='run-',dir=parent));completion=run/'completion.log';completion.write_text('evidence: '+str(run)+'\n')
    files={'placement':'WorldUI/Modal/RewardShowcasePlacement.cs','spawn':'WorldUI/Modal/ModalFallback.SharedWindowSpawn.cs','reward':'Net/Remote/RemoteMapStory.Reward.cs','sender':'Net/Avatar/NetAvatarDriver.cs','version':'Net/VersionGuard.cs','handshake':'Net/RewardPoseHandshake.cs','policy':'Net/RewardWindowState.cs','continue':'WorldUI/Modal/RewardShowcase.cs','hmd':'WorldUI/Modal/ModalFallback.9.Spawn.cs','arc':'WorldUI/Modal/ArcSeats.cs','grab':'WorldUI/Grab/GrabbableModal.cs'}
    texts={key:(source/'src/GloomhavenVR'/path).read_text() for key,path in files.items()}
    texts['core']=(source/'src/GloomhavenVR/WorldUI/Modal/ModalFallback.1.Core.cs').read_text();files['core']='WorldUI/Modal/ModalFallback.1.Core.cs'
    texts['law']=(source/'src/GloomhavenVR/WorldUI/Modal/SharedWindowSizeLaw.cs').read_text();files['law']='WorldUI/Modal/SharedWindowSizeLaw.cs'
    texts['protocol']=(source/'src/GloomhavenVR/Net/NetProtocol.cs').read_text();files['protocol']='Net/NetProtocol.cs'
    hashes={files[k]:hashlib.sha256(v.encode()).hexdigest() for k,v in texts.items()}
    (run/'source-hashes.json').write_text(json.dumps(hashes,indent=2)+'\n')
    assert 'self!.SharedDesignKind == SharedWindowKind.RewardShowcase' in block(texts['hmd'],'private static bool ComputeHmdPose(')
    assert 'kind == SharedWindowKind.RewardShowcase) continue;' in block(texts['arc'],'internal static void ReseatSharedWindowsOnce(')
    assert '!_panel.RevealPending' in block(texts['grab'],'internal void PlaceFrameAt(')
    assert 'RewardShowcasePlacement' not in block(texts['continue'],'private static ContinueBlock ConfirmBlock(')
    assert 'RewardShowcasePlacement' not in block(texts['continue'],'internal static bool TryConfirm(')
    lifecycle={
      'driver-disable':block(texts['sender'],'private void OnDisable('),
      'flat-net-transition':block(texts['sender'],'private void TickVersionGuard('),
      'per-frame-resolution':block(texts['sender'],'private void ApplyPending('),
      'offline-version-transition':block(texts['version'],'internal static void Tick('),
      'census-reset':block(texts['version'],'internal static void Reset('),
      'reward-reset':block(texts['reward'],'private static void ResetRewardPose(')}
    assert 'RemoteMapStory.Reset();' in lifecycle['driver-disable']
    assert 'RemoteMapStory.Reset();' in lifecycle['flat-net-transition']
    assert 'RemoteMapStory.ResolveReward(_transport != null ? _transport.LocalPlayerId : 0);' in lifecycle['per-frame-resolution']
    assert 'if (_wasOnline)\n                Reset();' in lifecycle['offline-version-transition']
    assert 'CensusRoster.Clear();' in lifecycle['census-reset']
    assert 'RewardShowcasePlacement.ClearInitialAuthority();' in lifecycle['reward-reset']
    (run/'lifecycle-source-pins.json').write_text(json.dumps(lifecycle,indent=2)+'\n')
    generated={'Placement.cs':texts['placement'],'Handshake.cs':texts['handshake'],
      'Spawn.cs':'using System;using UnityEngine;using GloomhavenVR.Core;namespace GloomhavenVR.WorldUI{internal static partial class ModalFallback{'+re.search(r'private const float WindowDistanceMeters = [^;]+;',texts['core'])[0]+block(texts['spawn'],'private static bool ComputeSharedHostSpawn(')+block(texts['spawn'],'private static float SharedHostSpawnWorldScale(')+block(texts['spawn'],'internal static bool EnsureRewardHostSpawn(')+'}}',
      'Collector.cs':'using System.Collections.Generic;namespace GloomhavenVR.Net{internal static partial class NetAvatarDriver{'+block(texts['sender'],'internal static void CollectRewardPosePeers(')+'}internal static partial class VersionGuard{'+block(texts['version'],'internal static void CollectContinuationPeers(')+'}}',
      'Election.cs':'using System;using GloomhavenVR.WorldUI;namespace GloomhavenVR.Net{internal static partial class RemoteMapStory{'+block(texts['reward'],'private static int RewardInitialOwner(')+block(texts['reward'],'internal static void ResolveReward(')+'}'+block(texts['policy'],'internal static class RewardPosePolicy')+'}',
      'Continue.cs':'using System;using GloomhavenVR.Core;using UnityEngine;using UnityEngine.UI;namespace GloomhavenVR.WorldUI{internal static partial class RewardShowcase{'+block(texts['continue'],'private enum ContinueBlock')+block(texts['continue'],'private static ContinueBlock ConfirmBlock(')+block(texts['continue'],'internal static bool TryConfirm(')+'}}'}
    factor=re.search(r'internal static float SharedGrabFactor\([\s\S]*?;',texts['law'])[0]
    codec='\n'.join(re.search(r'public const (?:byte|ushort) '+name+r' = [^;]+;',texts['protocol'])[0] for name in ['StorySizeMinCode','StorySizeMaxCode','StorySizeDefaultCode','ModBuild','ExtIdRewardPoseHandshake'])
    codec+='\n'+block(texts['protocol'],'public static byte EncodeStorySize(')+'\n'+re.search(r'public static float DecodeStorySize\([\s\S]*?;',texts['protocol'])[0]
    generated['Factor.cs']='namespace GloomhavenVR.WorldUI{internal static partial class SharedWindowSizeLaw{'+factor+'}}namespace GloomhavenVR.Net{internal static partial class NetProtocol{'+codec+'}}'
    for p in (ROOT/'scripts/shared-reward-placement-runtime').glob('*.cs'):
      generated[p.name]=p.read_text();hashes[str(p.relative_to(ROOT))]=hashlib.sha256(p.read_bytes()).hexdigest()
    hashes[str(Path(__file__).relative_to(ROOT))]=hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
    (run/'source-hashes.json').write_text(json.dumps(hashes,indent=2)+'\n')
    controls=[('production','','','',''),('lost-reward-author','Spawn.cs','? RewardShowcasePlacement.IsInitialAuthor : SharedWindowSpawnBridge.IsInitialAuthor','? SharedWindowSpawnBridge.IsInitialAuthor : SharedWindowSpawnBridge.IsInitialAuthor','Surrogate spawn follows its actual head'),('cold-follower-reveals','Placement.cs','return _mayReveal && ModalFallback.EnsureRewardHostSpawn(panel);','return !_mayReveal || ModalFallback.EnsureRewardHostSpawn(panel);','Cold follower stays hidden'),('wrong-chest-pose','Placement.cs','RewardShowcaseIdentity.ContentKey(window) != _key','false','A pose for another chest'),('refit-overwrites-manual','Spawn.cs','if (panel.SharedSpawnPoseReady) return true;','panel.SharedSpawnPoseReady=false;','An established reward pose must preserve'),('avatar-instead-of-census','Collector.cs','VersionGuard.CollectContinuationPeers(into, NetPlayerActors.LocalPlayerId());','into.Clear();','Stable compatible session census'),('lost-offline-recovery','Election.cs','localPlayerId <= 0 || initialOwner == 0 || initialOwner == localPlayerId','initialOwner == localPlayerId','Degraded transport with no identity must release stale reward follower authority')]
    result=1;results=[]
    try:
      for name,filename,old,new,expected in controls:
        case=run/name;case.mkdir();sources=case/'Sources';sources.mkdir()
        for filename2,text in generated.items():
          if filename2==filename:
            assert text.count(old)==1,(name,'binding drift');text=text.replace(old,new)
          (sources/filename2).write_text(text)
        project=case/'Reward.csproj';project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>latest</LangVersion><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors><NoWarn>CS0649;CS0169</NoWarn></PropertyGroup></Project>')
        code=1
        try:
          for label,command in [('build',[dotnet,'build','--disable-build-servers',str(project),'-c','Release','--nologo','-v','quiet','-p:UseSharedCompilation=false']),('runtime',[dotnet,str(case/'bin/Release/net8.0/Reward.dll')])]:
            log=case/(label+'.log')
            with log.open('w') as output:
              child=subprocess.Popen(command,stdout=output,stderr=subprocess.STDOUT,start_new_session=True,env=environment)
              try:code=child.wait(timeout=90)
              finally:
                if child.poll() is None:os.killpg(child.pid,signal.SIGKILL)
                child.wait()
            if label=='build' and code:raise RuntimeError(log.read_text())
          output=(case/'runtime.log').read_text();passed=(code!=0 and expected in output) if expected else code==0
          results.append({'case':name,'passed':passed,'exit_code':code,'output':output});print(('PASS ' if passed else 'FAIL ')+name+': '+output.strip(),flush=True)
          if not passed:raise RuntimeError(str(case))
        finally:print(cleanup_completed_run('shared-reward-placement-runtime',code,completion,ROOT,process_reaped=True),flush=True)
      result=0
      return 0
    finally:
      (run/'results.json').write_text(json.dumps({'passed':result==0,'results':results,'scope':'Production kernels; simulated native windows/transforms/camera/continuation, no pixels or native network execution.'},indent=2)+'\n');print('evidence:',run)
if __name__=='__main__':sys.exit(main())
