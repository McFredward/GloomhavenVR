#!/usr/bin/env python3
"""Prepare recorded greetings and sound-derived Rhubarb mouth cues without network access."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import uuid
import wave


def run(*args):
    subprocess.run(args, check=True, stdout=subprocess.DEVNULL)


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--input', required=True, type=Path)
    p.add_argument('--output', required=True, type=Path)
    p.add_argument('--rhubarb', required=True, type=Path)
    a=p.parse_args();a.output.mkdir(parents=True,exist_ok=True)
    manifest=[]
    for service,npc in enumerate(('merchant','priestess','enchantress'),1):
        for language,lang in enumerate(('en','de')):
            name=f'{npc}-{lang}'; source=a.input/name/'speech.mp3'; wav=a.output/(name+'.wav')
            # Mono decompressed clips are tiny, allow sample-accurate seeks, and avoid runtime decoder work.
            run('ffmpeg','-hide_banner','-loglevel','error','-y','-i',str(source),'-ac','1','-ar','24000',
                '-af','loudnorm=I=-20:TP=-2:LRA=7','-c:a','pcm_s16le',str(wav))
            analysis=a.input/name/'rhubarb.json'
            run(str(a.rhubarb),'-r','phonetic','-f','json','--extendedShapes','GHX','--quiet',
                '-o',str(analysis),str(wav))
            parsed=json.loads(analysis.read_text())
            with wave.open(str(wav)) as w:
                duration=w.getnframes()/w.getframerate()
                assert w.getnchannels()==1 and w.getsampwidth()==2
            cues=parsed['mouthCues']
            assert cues and all(x['value'] in 'ABCDEFGHX' and 0<=x['start']<=x['end']<=duration+.011 for x in cues)
            output={'schema':1,'cue':service*2-1+language,'service':service,'language':lang,
                    'duration':duration,'mouthCues':cues}
            path=a.output/(name+'.json'); path.write_text(json.dumps(output,indent=2)+'\n')
            plan=json.loads((a.input/name/'plan.json').read_text())
            manifest.append({'name':name,'cue':output['cue'],'text':plan['input']['text'],
                'voice':plan['input']['voice'],'endpoint':plan['endpoint'],'duration':duration,
                'source_mp3_sha256':hashlib.sha256(source.read_bytes()).hexdigest(),
                'wav_sha256':hashlib.sha256(wav.read_bytes()).hexdigest(),
                'curve_sha256':hashlib.sha256(path.read_bytes()).hexdigest()})
            audio_meta='''fileFormatVersion: 2
guid: {guid}
AudioImporter:
  externalObjects: {{}}
  serializedVersion: 7
  defaultSettings:
    serializedVersion: 2
    loadType: 0
    sampleRateSetting: 0
    sampleRateOverride: 24000
    compressionFormat: 0
    quality: 1
    conversionMode: 0
  platformSettingOverrides: {{}}
  forceToMono: 1
  normalize: 0
  preloadAudioData: 1
  loadInBackground: 0
  ambisonic: 0
  3D: 1
  userData:
  assetBundleName:
  assetBundleVariant:
'''
            text_meta='''fileFormatVersion: 2
guid: {guid}
TextScriptImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
'''
            for file,template in ((wav,audio_meta),(path,text_meta)):
                guid=uuid.uuid5(uuid.NAMESPACE_URL,'gloomhavenvr/town543/audio/'+file.name).hex
                file.with_suffix(file.suffix+'.meta').write_text(template.format(guid=guid))
            print(name,round(duration,3),'seconds',len(cues),'sound-derived mouth intervals')
    # Generation metadata is developer provenance, not included in the runtime bundle.
    (a.input/'manifest.json').write_text(json.dumps(manifest,indent=2,ensure_ascii=False)+'\n')

if __name__=='__main__':main()
