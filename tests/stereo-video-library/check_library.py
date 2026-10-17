#!/usr/bin/env python3
"""Physical stereo witnesses and native-library provenance refusal controls."""
from __future__ import annotations
import copy
import json
from pathlib import Path
import sys
import tempfile
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts/stereo-video'))
from library_common import field_for, forward_warp, load_movie
from library_depth import receipt_matches, validate_movie
from library_verify import validate_probe, right_half_yuv


def run() -> dict:
    passed, controls = [], []
    def require(value, name):
        if not value:
            raise AssertionError(name)
        passed.append(name)
    def refused(call, name):
        try:
            call()
        except (ValueError, KeyError, FileNotFoundError):
            controls.append(name)
        else:
            raise AssertionError('Negative control escaped: ' + name)
    manifest_path = ROOT / 'scripts/stereo-video/library-manifest.json'
    manifest = json.loads(manifest_path.read_text())
    keys = {m['key'] for m in manifest['movies']}
    require(len(keys) == len(manifest['movies']) == 20, '20 unique subjects including approved intro')
    require(sum(k.startswith('Heroes/') for k in keys) == 11, '11 exact native hero subjects')
    require(sum(k.startswith('Ambient/') for k in keys) == 7, '7 exact native ambient subjects')
    require('Embedded/gameplay_trailer.mov' not in keys, 'explicit user trailer exclusion')
    refused(lambda: load_movie(manifest_path, 'Embedded/gameplay_trailer.mov'), 'excluded trailer cannot enter converter')
    refused(lambda: load_movie(manifest_path, 'CP_Intro/GH_CP_Intro.mov'), 'root-owned approved intro cannot enter library converter')
    for movie in manifest['movies']:
        validate_movie(movie)
    movie = next(m for m in manifest['movies'] if m['key'].startswith('Ambient/'))
    for change, name in [('gap', 'shot gap'), ('ending', 'lost native ending'), ('fps', 'retimed frame rate')]:
        broken = copy.deepcopy(movie)
        if change == 'gap': broken['shots'][1]['start'] += 1
        elif change == 'ending': broken['shots'][-1]['end'] -= 1
        else: broken['fps'] = 24
        refused(lambda b=broken: validate_movie(b), name)
    # Bright horizontal landmarks have analytically known right-eye movement.
    image = np.zeros((12, 64, 3), np.uint8)
    image[:, 20:25] = [0, 0, 255]
    image[:, 40:45] = [0, 255, 0]
    # The black background is farther than both colored landmarks.
    field = np.full((12, 64), -4, np.float32)
    field[:, 20:25] = 4
    field[:, 40:45] = -3
    right, holes = forward_warp(image, field)
    require(np.all(right[:, 16:21] == [0, 0, 255]), 'near object shifts left4pixels in right eye')
    require(np.all(right[:, 43:48] == [0, 255, 0]), 'far object shifts right3pixels in right eye')
    require(np.all(right[0] == right[-1]), 'horizontal reprojection never changes vertical address')
    require(holes > 0, 'foreground shift exposes real disocclusion')
    require(not np.array_equal(right, image), 'stereo witness rejects duplicated left')
    wrong, _ = forward_warp(image, -field)
    require(not np.array_equal(wrong[:, 16:21], right[:, 16:21]), 'reversed disparity visibly fails known near landmark')
    # Distinct rows prove the correct plane crop of actual3840-wide YUV, not RGB.
    y = np.zeros((1080, 3840), np.uint8); y[:, 1920:] = 77
    u = np.zeros((540, 1920), np.uint8); u[:, 960:] = 111
    v = np.zeros((540, 1920), np.uint8); v[:, 960:] = 199
    actual = right_half_yuv(y.tobytes() + u.tobytes() + v.tobytes())
    require(np.all(actual[:1920*1080] == 77) and np.all(actual[1920*1080:1920*1080+960*540] == 111)
            and np.all(actual[-960*540:] == 199), 'native YUV420 right-half chroma sampling')
    hero = next(m for m in manifest['movies'] if m['key'] == 'Heroes/Elementalist.mov')
    shot = next(s for s in hero['shots'] if s.get('screen_regions'))
    original = np.full((1080, 1920, 3), 100, np.uint8)
    depth = np.tile(np.linspace(0, 1, 640, dtype=np.float32), (360, 1))
    measured = field_for(depth, original, hero, shot, {'normalization_p02_p98': [0, 1]})
    require(float(measured.min()) >= -4 and float(measured.max()) <= 12, 'accepted687 physical disparity range retained')
    for x0, y0, x1, y1 in shot['screen_regions']:
        require(not measured[y0:y1, x0:x1].any(), 'observed native name and class panel remains screen-plane')
    require(not measured[:128].any() and not measured[956:].any(), 'native cinematic letterbox plane retained')
    require(measured[600, 1500] > 5, 'actor side outside printed panel retains positive depth')
    studio = next(m for m in manifest['movies'] if m['kind'] == 'embedded')
    measured = field_for(None, original, studio, studio['shots'][0])
    require(measured[540, 960] == 8, 'original studio mark is a uniform positive plane')
    require(not field_for(None, np.zeros_like(original), studio, studio['shots'][0]).any(), 'black studio fade carries no stereo jump')
    # A metadata fixture has all frame times individually enumerated.
    tiny = {'fps':30, 'frames':4}
    probe = {'streams':[{'codec_type':'video','width':3840,'height':1080,'sample_aspect_ratio':'1:1',
        'avg_frame_rate':'30/1','r_frame_rate':'30/1','codec_name':'h264','pix_fmt':'yuv420p',
        'color_space':'bt709','color_range':'tv','duration':str(4/30)}],
        'frames':[{'media_type':'video','best_effort_timestamp_time':str(i/30)} for i in range(4)]}
    require(validate_probe(probe,tiny)['frames'] == 4, 'actual source30fps clock remains30fps')
    for kind in ('clock','drop','half-sbs','sar','duration','color'):
        broken = copy.deepcopy(probe)
        if kind=='clock': broken['frames'][2]['best_effort_timestamp_time']='0.04'
        elif kind=='drop': broken['frames'].pop()
        elif kind=='half-sbs': broken['streams'][0]['width']=1920
        elif kind=='sar': broken['streams'][0]['sample_aspect_ratio']='1:2'
        elif kind=='duration': broken['streams'][0]['duration']='0.5'
        else: broken['streams'][0]['color_space']='bt601'
        refused(lambda b=broken: validate_probe(b,tiny),kind)
    return {'status':'passed','physical_and_inventory_checks':len(passed),'checks':passed,
        'negative_controls':len(controls),'refusals':controls,'hardware_result':'unverified'}


def main() -> int:
    import argparse
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out',type=Path,required=True)
    args=parser.parse_args();args.out.mkdir(parents=True,exist_ok=False)
    code=1
    try:
        result=run();(args.out/'result.json').write_text(json.dumps(result,indent=2)+'\n')
        print(json.dumps(result));code=0;return code
    finally:
        sys.path.insert(0,str(ROOT/'scripts'))
        from town_run_retention import cleanup_completed_run
        log=args.out/'completion.log';log.write_text(f'Physical fixture complete; all in-process computations stopped; exit={code}.\nevidence: {args.out.resolve()}\n')
        (args.out/'cleanup.json').write_text(json.dumps(cleanup_completed_run('stereo-video-library',code,log.resolve(),ROOT,process_reaped=True),indent=2)+'\n')

if __name__=='__main__':raise SystemExit(main())
