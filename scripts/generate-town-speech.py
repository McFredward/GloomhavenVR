#!/usr/bin/env python3
"""Generate exactly six reviewed resident greetings; credentials from the environment only.

prepare prints no secrets and makes no network request. submit reserves each of six
fixed requests before POST; an uncertain POST is never automatically retried. collect
only retrieves an existing request. Launch with uv --env-file if the key is not exported.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import urllib.request
import urllib.error

ENDPOINT = 'fal-ai/elevenlabs/tts/eleven-v3'
VOICES = {
    'merchant': ('JBFqnCBsd6RMkjVDRZzb', 'Welcome. Take a look at my wares.', 'Willkommen. Seht Euch meine Waren an.'),
    'priestess': ('Xb7hH8MSUJpSbSDYk0k2', 'Welcome. May the Great Oak watch over you.', 'Willkommen. Möge die Große Eiche über Euch wachen.'),
    'enchantress': ('pFZP5JQG7iQjIQuC4Bku', 'Welcome. Let us see what your abilities can become.', 'Willkommen. Sehen wir, was sich aus Euren Fähigkeiten machen lässt.'),
}

def save(path, data):
    path.write_text(json.dumps(data, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')

def plans():
    return {f'{npc}-{lang}': {'text': row[1+i], 'voice': row[0], 'language_code': lang,
            'stability': 0.5, 'timestamps': True, 'apply_text_normalization': 'auto'}
            for npc, row in VOICES.items() for i, lang in enumerate(('en', 'de'))}

def request(url, payload=None):
    if not url.startswith('https://queue.fal.run/'):
        raise ValueError('Credential origin rejected')
    key = os.environ.get('FAL_AI_API_KEY')
    if not key:
        raise RuntimeError('FAL_AI_API_KEY missing from process environment')
    req = urllib.request.Request(url, data=None if payload is None else json.dumps(payload).encode(),
        headers={'Authorization': 'Key ' + key, 'Content-Type': 'application/json'})
    try:
        with urllib.request.urlopen(req, timeout=60) as r:
            return json.load(r)
    except urllib.error.HTTPError as ex:
        raise RuntimeError(f'Provider HTTP {ex.code}; no retry. Reconcile receipt before proceeding.') from None

def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('action', choices=('prepare', 'submit', 'collect'))
    p.add_argument('--output', required=True, type=Path)
    p.add_argument('--cue', choices=tuple(plans()))
    a = p.parse_args()
    a.output.mkdir(parents=True, exist_ok=True)
    batch = plans()
    if a.action == 'prepare':
        for cue, args in batch.items():
            folder = a.output / cue; folder.mkdir(exist_ok=True)
            plan = {'endpoint': ENDPOINT, 'input': args, 'estimated_usd': len(args['text'])*.0001}
            path = folder / 'plan.json'
            if path.exists() and json.loads(path.read_text()) != plan:
                raise RuntimeError('Refusing to overwrite differing reviewed plan')
            save(path, plan)
        print('Six requests;', sum(len(x['text']) for x in batch.values()), 'characters; estimated USD',
              round(sum(len(x['text']) for x in batch.values())*.0001, 5))
        return
    if not a.cue:
        p.error('--cue required')
    folder = a.output / a.cue
    plan = json.loads((folder/'plan.json').read_text())
    if plan['input'] != batch[a.cue] or plan['endpoint'] != ENDPOINT:
        raise RuntimeError('Plan differs from fixed approved batch')
    receipt = folder/'receipt.json'
    if a.action == 'submit':
        if receipt.exists():
            print(a.cue, 'already submitted; collect instead'); return
        if not os.environ.get('FAL_AI_API_KEY'):
            raise RuntimeError('FAL_AI_API_KEY missing; not submitting')
        # Exclusive durable intent makes uncertain network failures manual-only.
        with (folder/'intent.json').open('x') as f:
            json.dump({'plan_sha256': hashlib.sha256((folder/'plan.json').read_bytes()).hexdigest()}, f)
            f.flush(); os.fsync(f.fileno())
        result = request('https://queue.fal.run/'+ENDPOINT, plan['input'])
        save(receipt, result)
        print(a.cue, 'submitted', result['request_id']); return
    result_url = json.loads(receipt.read_text())['response_url']
    status_url = json.loads(receipt.read_text())['status_url']
    status = request(status_url)
    if status.get('status') != 'COMPLETED':
        print(a.cue, status.get('status')); return
    result = request(result_url); save(folder/'result.json', result)
    url = result['audio']['url']
    if not url.startswith('https://'):
        raise ValueError('Audio result is not HTTPS')
    with urllib.request.urlopen(url, timeout=60) as r:
        raw = r.read(10*1024*1024+1)
    if len(raw)>10*1024*1024:
        raise ValueError('Unexpected audio size')
    (folder/'speech.mp3').write_bytes(raw)
    print(a.cue, 'downloaded', len(raw), hashlib.sha256(raw).hexdigest())

if __name__ == '__main__':
    main()
