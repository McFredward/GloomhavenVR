#!/usr/bin/env python3
"""Retain executable burn-layout and fan-audio evidence, then clear project caches."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import tempfile

ROOT=Path(sys.argv[1]).resolve()
sys.path.insert(0,str(ROOT/'scripts'))
from town_run_retention import cleanup_completed_run

# Keep the original sixteen controls and their assertion targets. Audio controls
# alter the extracted driver branch, so a missing edge guard must fail at runtime.
MUTATIONS=[
    ('early-layout','if (DeferLayoutForBurn()) return;','if (bool.Parse("false")) return;','Model-first loss must retain the outgoing layout'),
    ('missed-model','if (card.IsHeld || !IsFreshBurn(_boundHand, card)','if (bool.Parse("true") || card.IsHeld || !IsFreshBurn(_boundHand, card)','Model-first loss must retain the outgoing layout'),
    ('missed-native','_burnLayoutNativeActive |= artworkPlaying;','_burnLayoutNativeActive |= false;','Live burn must never admit sibling movement or replacement'),
    ('no-retry','_dirty = true;','_dirty = false;','Retained card input must wait and rebuild must remain scheduled'),
    ('last-card-only','_burnLayoutNativeActive |=','_burnLayoutNativeActive =','Live burn must never admit sibling movement or replacement'),
    ('missed-incoming','bool incomingActive = IncomingHandBurnActive();','bool incomingActive = false;','Incoming native burn must wait before replacing the outgoing character'),
    ('historical-incoming','active |= BurnArtwork.Playing(BurnArtwork.EffectsOf(widget));','active |= widget.AbilityCard != null;','Completed incoming burns must admit focus without replaying a historical loss'),
    ('missed-foreign','Net.CardAppearanceMirror.OwnerBurnInProgress(incoming.PlayerActor)','false','Foreign incoming owner progress must retain the admitted local view when its native proxy is inactive'),
    ('missed-progress','Net.CardAppearanceSampler.ObserveBurnProgress(widget);','','Every incoming original widget must publish its owner progress before adoption'),
    ('forgotten-model','widget.AbilityCard != null && _knownBurntCards.Contains(widget.AbilityCard)','false','Already-burnt browse cards must not restart a hold'),
    ('lost-is-recovered','!character.LostAbilityCards.Contains(card)','true','Replacing a completed lost widget must not replay its original burn'),
    ('missed-recovery','_knownBurntCards.Remove(card);','// omitted recovery','Authoritative recovery must re-arm a real later burn'),
    ('duplicate-original','&& ReferenceEquals(held.AbilityCard, widget.AbilityCard)','&& ReferenceEquals(held, widget)','Two widgets for one original must share one pending burn hold'),
    ('no-native-baseline','foreach (CAbilityCard card in character.LostAbilityCards) _knownBurntCards.Add(card);','// omitted baseline','Historical native loss with no initial widget must not become a fresh burn on later UI creation'),
    ('completed-is-historical','_completedBurnClaims.Contains(widget.AbilityCard)','_knownBurntCards.Contains(widget.AbilityCard)','Initial historical seeding must not suppress a first round or active burn handover'),
    ('missed-completed-recovery','_completedBurnClaims.Remove(card);','// omitted completed recovery','Real recovery must also clear completed round or active flight claims'),
    ('unconditional-close-audio','if (!_fan.IsOpen)\n                PlayFanEdgeSound(open: false);','if (bool.Parse("true"))\n                PlayFanEdgeSound(open: false);','Blocked close frames must not emit a hide cue'),
    ('unconditional-open-audio','if (_fan.IsOpen)\n                PlayFanEdgeSound(open: true);','if (bool.Parse("true"))\n                PlayFanEdgeSound(open: true);','Blocked open frames must not emit a reveal cue'),
]


def execute(command,log,timeout=120):
    with log.open('w') as stream:
        process=subprocess.Popen(command,stdout=stream,stderr=subprocess.STDOUT,start_new_session=True)
        try:
            return process.wait(timeout=timeout)
        finally:
            try: os.killpg(process.pid,signal.SIGKILL)
            except ProcessLookupError: pass
            process.wait()


def main():
    destination=ROOT/'.planning/debug/burn-layout-runtime'
    destination.mkdir(parents=True,exist_ok=True)
    run=Path(tempfile.mkdtemp(prefix='run-',dir=destination))
    completion=run/'completion.log';completion.write_text('evidence: '+str(run)+'\n')
    print('evidence: '+str(run),flush=True)
    outcome=1
    result={'scope':'Existing burn sequencing kernel and exact driver audio-edge branch with complete production CardFan.Open/Close; explicit hierarchy/animation-helper/audio sinks and caller intent/burn-wait inputs',
            'native_gameplay':False,'audio_device':False,'full_palm_measurement':False,'headset':False,'cases':[]}
    try:
        fixture=run/'Sources/fixture'
        shutil.copytree(ROOT/'tests/GloomhavenVR.BurnLayoutTests',fixture)
        inputs=[ROOT/'src/GloomhavenVR/Cards'/name for name in [
            'CardFan.cs','BurnArtwork.cs','Driver/CardsDriver.2.Update.cs',
            'Driver/CardsDriver.4.Rebuild.cs','Driver/CardsDriver.5.Interactions.cs',
            'Driver/CardsDriver.8.BurnSequencing.cs','Driver/CardsDriver.9.BurnClaims.cs']]
        hashes={str(path.relative_to(ROOT)):hashlib.sha256(path.read_bytes()).hexdigest()
                for path in [*inputs,*sorted((ROOT/'tests/GloomhavenVR.BurnLayoutTests').glob('*')),
                             ROOT/'scripts/burn-layout-tests.sh'] if path.is_file()}
        (run/'source-hashes.json').write_text(json.dumps(hashes,indent=2)+'\n')
        for source in inputs:
            target=run/'Sources/production-inputs'/source.relative_to(ROOT)
            target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(source,target)
        extracted=run/'Sources/Production.cs'
        code=execute([sys.executable,'-B',str(fixture/'extract.py'),str(ROOT),str(extracted)],run/'extract.log')
        if code: raise RuntimeError('Production extraction failed; see '+str(run/'extract.log'))
        source=extracted.read_text()
        dotnet=shutil.which('dotnet') or str(Path.home()/'.dotnet/dotnet')
        for name,before,after,expected in [('production','','',''),*MUTATIONS]:
            case=run/name;case.mkdir()
            for file in fixture.glob('*'):
                if file.suffix in ['.cs','.csproj']: shutil.copyfile(file,case/file.name)
            if before:
                assert before in source,'Mutation binding drift: '+name
            (case/'Production.cs').write_text(source.replace(before,after) if before else source)
            project=case/'GloomhavenVR.BurnLayoutTests.csproj'
            code=execute([dotnet,'build',str(project),'-c','Release','--nologo','-v','quiet'],case/'build.log')
            if code: raise RuntimeError('Compilation is not a valid negative control: '+name+'; see '+str(case/'build.log'))
            code=execute([dotnet,str(case/'bin/Release/net8.0/GloomhavenVR.BurnLayoutTests.dll')],case/'run.log',30)
            output=(case/'run.log').read_text()
            passed=code==0 if not expected else code!=0 and expected in output
            result['cases'].append({'name':name,'exit_code':code,'passed':passed,'expected':expected})
            (run/'results.json').write_text(json.dumps(result,indent=2)+'\n')
            if not passed: raise RuntimeError('FAIL '+name+':\n'+output)
            print(output.strip() if not expected else 'Burn layout negative control: '+name+' failed as expected.',flush=True)
        result['passed']=True
        print('PASS burn layout and fan audio with '+str(len(MUTATIONS))+' runtime negative controls.',flush=True)
        outcome=0
        return 0
    finally:
        result['exit_code']=outcome
        (run/'results.json').write_text(json.dumps(result,indent=2)+'\n')
        receipt=cleanup_completed_run('burn-layout',outcome,completion,ROOT,process_reaped=True)
        (run/'cache-cleanup.json').write_text(json.dumps(receipt,indent=2)+'\n')


if __name__=='__main__':
    def interrupted(signum,frame): raise KeyboardInterrupt
    signal.signal(signal.SIGTERM,interrupted)
    raise SystemExit(main())
