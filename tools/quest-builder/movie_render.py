"""Finite local reconstruction of approved SBS pictures from owned color/audio and lossless numeric depth."""
from __future__ import annotations
import argparse, hashlib, json, math, os, signal, struct, subprocess, sys
from fractions import Fraction
from pathlib import Path
import cv2
import numpy as np
import zstandard
from numba import njit
WIDTH, HEIGHT = 1920, 1080

@njit(cache=True)
def forward_warp(image: np.ndarray, disparity: np.ndarray) -> tuple[np.ndarray, int]:
    """x_right=x_left-disparity; nearer samples win; vertical address never changes."""
    height, width = disparity.shape
    output = np.empty_like(image)
    holes = 0
    for y in range(height):
        front = np.full(width, -np.inf, dtype=np.float32)
        weights = np.zeros(width, dtype=np.float32)
        color = np.zeros((width, 3), dtype=np.float32)
        for x in range(width):
            target = x - disparity[y, x]
            first = math.floor(target)
            fraction = target - first
            for k in range(2):
                dest = first + k
                weight = 1.0 - fraction if k == 0 else fraction
                if 0 <= dest < width and weight > 0.00001:
                    front[dest] = max(front[dest], disparity[y, x])
        for x in range(width):
            target = x - disparity[y, x]
            first = math.floor(target)
            fraction = target - first
            for k in range(2):
                dest = first + k
                weight = 1.0 - fraction if k == 0 else fraction
                if (0 <= dest < width and weight > 0.00001
                        and disparity[y, x] >= front[dest] - 0.15):
                    weights[dest] += weight
                    for channel in range(3):
                        color[dest, channel] += image[y, x, channel] * weight
        for x in range(width):
            if weights[x] > 0:
                for channel in range(3):
                    output[y, x, channel] = min(255, max(0, round(color[x, channel] / weights[x])))
        x = 0
        while x < width:
            if weights[x] > 0:
                x += 1
                continue
            start = x
            while x < width and weights[x] == 0:
                x += 1
            end = x
            holes += end - start
            left, right = start - 1, end
            # Only disoccluded samples are filled. A closer foreground endpoint
            # must never be stretched across a newly visible background strip.
            if left < 0:
                selected = min(right, width - 1)
            elif right >= width:
                selected = left
            else:
                selected = left if front[left] < front[right] else right
            for missing in range(start, end):
                if left >= 0 and right < width and abs(front[left] - front[right]) < 0.25:
                    fraction = (missing - left) / (right - left)
                    for channel in range(3):
                        output[y, missing, channel] = round(output[y, left, channel] * (1 - fraction)
                                                            + output[y, right, channel] * fraction)
                else:
                    output[y, missing] = output[y, selected]
    return output, holes

def smooth_strength(frame: int, interval: list[int]) -> float:
    value = min(1.0, max(0.0, (frame - interval[0]) / (interval[1] - interval[0])))
    return value * value * (3.0 - 2.0 * value)

def protected_regions(shot: dict, frame: int | None) -> list[tuple[list[int], float]]:
    # Legacy rectangles remain readable for retained before/after causal checks.
    regions = [(bounds, 1.0) for bounds in shot.get('screen_regions', [])]
    if shot.get('screen_panels') and (type(frame) is not int or not shot['start'] <= frame < shot['end']):
        raise ValueError('A native frame address is required for timed printed panels')
    for panel in shot.get('screen_panels', []):
        strength = smooth_strength(frame, panel['prepare'])
        if not strength:
            continue
        regions.append((panel['bounds'], strength))
        # Native titles and emblems briefly zoom/blur in beyond their settled
        # bounds. Protect that observed envelope, then release only the outside
        # scenery smoothly after the original print has settled.
        reveal = strength * (1.0 - smooth_strength(frame, panel['onset_release']))
        if reveal:
            regions.append((panel['onset_bounds'], reveal))
    return regions

def field_for(depth, image: np.ndarray, movie: dict, shot: dict, info=None, frame: int | None = None) -> np.ndarray:
    """The accepted687 [-4,+12] horizontal geometry with source-specific masks."""
    if shot['mode'] == 'screen':
        return np.zeros(image.shape[:2], dtype=np.float32)
    if shot['mode'] == 'logo':
        # This is the original studio mark on its original black background:
        # translate its recorded plane uniformly rather than bend the wordmark.
        field = np.full(image.shape[:2], 8.0, dtype=np.float32)
    else:
        low, high = info['normalization_p02_p98']
        normalized = np.clip((depth.astype(np.float32) - low) / (high - low), 0, 1)
        field = cv2.resize(normalized, (WIDTH, HEIGHT), interpolation=cv2.INTER_LINEAR)
        guide = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY).astype(np.float32) / 255.0
        mean = lambda value: cv2.boxFilter(value, -1, (9, 9), borderType=cv2.BORDER_REFLECT)
        mean_i, mean_p = mean(guide), mean(field)
        covariance = mean(guide * field) - mean_i * mean_p
        variance = mean(guide * guide) - mean_i * mean_i
        a = covariance / (variance + 0.0025)
        b = mean_p - a * mean_i
        field = np.clip(mean(a) * guide + mean(b), 0, 1) * 16.0 - 4.0
    top, bottom = movie['body_rows']
    field[:top] = 0
    field[bottom:] = 0
    border = np.minimum(np.arange(WIDTH), np.arange(WIDTH)[::-1]).astype(np.float32)
    field *= np.minimum(border / 64.0, 1.0)[None, :]
    for (x0, y0, x1, y1), strength in protected_regions(shot, frame):
        # The source has already composited these original labels. Keep the
        # observed printed panel and glyphs in one undistorted screen plane.
        field[y0:y1, x0:x1] *= 1.0 - strength
        # Blend only outside the protected rectangle; no glyph is interpolated.
        for band in range(1, 33):
            factor = 1.0 - strength * (1.0 - band / 33.0)
            if x0 - band >= 0:
                field[max(0, y0-band):min(HEIGHT, y1+band), x0-band] *= factor
            if x1 + band - 1 < WIDTH:
                field[max(0, y0-band):min(HEIGHT, y1+band), x1+band-1] *= factor
            if y0 - band >= 0:
                field[y0-band, max(0,x0-band):min(WIDTH,x1+band)] *= factor
            if y1 + band - 1 < HEIGHT:
                field[y1+band-1, max(0,x0-band):min(WIDTH,x1+band)] *= factor
    # Original near-black fades contain no reliable positional cue. The same
    # luminance-based taper as687 also closes native ambient loops at black.
    field *= min(1.0, float(image[top:bottom].mean()) / 8.0)
    return np.ascontiguousarray(field, dtype=np.float32)

def boxes(data: bytes | bytearray, begin: int, end: int):
    cursor = begin
    while cursor < end:
        if cursor + 8 > end:
            raise ValueError('Truncated MOV atom header')
        size, kind = struct.unpack_from('>I4s', data, cursor)
        header = 8
        if size == 1:
            if cursor + 16 > end:
                raise ValueError('Truncated extended MOV atom header')
            size = struct.unpack_from('>Q', data, cursor + 8)[0]
            header = 16
        if size == 0:
            size = end - cursor
        if size < header or cursor + size > end:
            raise ValueError('MOV atom exceeds its parent')
        yield kind, cursor + header, cursor + size
        cursor += size

def one(data, begin, end, kind):
    found = [(body, finish) for name, body, finish in boxes(data, begin, end) if name == kind]
    if len(found) != 1:
        raise ValueError('Expected one MOV atom ' + kind.decode())
    return found[0]

def duration_field(data, body: int, kind: bytes) -> tuple[int, str]:
    version = data[body]
    if version not in (0, 1):
        raise ValueError('Unsupported MOV full-box version')
    if kind == b'tkhd':
        return body + (28 if version else 20), '>Q' if version else '>I'
    return body + (24 if version else 16), '>Q' if version else '>I'

def movie_layout(data) -> dict:
    moov, end = one(data, 0, len(data), b'moov')
    mvhd, _ = one(data, moov, end, b'mvhd')
    version = data[mvhd]
    if version not in (0, 1):
        raise ValueError('Unsupported movie header')
    scale = struct.unpack_from('>I', data, mvhd + (20 if version else 12))[0]
    tracks = {}
    for kind, body, finish in boxes(data, moov, end):
        if kind != b'trak':
            continue
        tkhd, _ = one(data, body, finish, b'tkhd')
        mdia, mdia_end = one(data, body, finish, b'mdia')
        hdlr, _ = one(data, mdia, mdia_end, b'hdlr')
        role = bytes(data[hdlr + 8:hdlr + 12])
        if role not in (b'soun', b'vide') or role in tracks:
            raise ValueError('Unsupported or duplicate native MOV track')
        edts, edts_end = one(data, body, finish, b'edts')
        elst, _ = one(data, edts, edts_end, b'elst')
        elst_version = data[elst]
        count = struct.unpack_from('>I', data, elst + 4)[0]
        if elst_version not in (0, 1) or count != 1:
            raise ValueError('Native movie needs explicit review for multiple edit segments')
        fmt = '>Qqhh' if elst_version else '>Iihh'
        segment, media_time, rate, fraction = struct.unpack_from(fmt, data, elst + 8)
        if (rate, fraction) != (1, 0):
            raise ValueError('Native movie changes playback rate in its edit list')
        tracks[role] = {'tkhd': tkhd, 'elst': elst, 'segment': segment,
            'media_time': media_time, 'elst_format': '>Q' if elst_version else '>I'}
    if set(tracks) != {b'soun', b'vide'}:
        raise ValueError('Expected one native audio and video track')
    return {'timescale': scale, 'mvhd': mvhd, 'tracks': tracks}

def restore_native_window(raw: Path, source: Path, target: Path, movie: dict) -> dict:
    native = movie_layout(source.read_bytes())
    data = bytearray(raw.read_bytes())
    muxed = movie_layout(data)
    audio, video = native['tracks'][b'soun'], native['tracks'][b'vide']
    duration = Fraction(movie['frames'], movie['fps'])
    if (audio['media_time'] != 0 or Fraction(audio['segment'], native['timescale']) != duration
            or Fraction(video['segment'], native['timescale']) != duration
            or muxed['timescale'] != native['timescale']):
        raise ValueError('Native hero presentation differs from its recorded exact frame window')
    # Mainconcept retains complete AAC packets beyond its presentation boundary.
    # Keep every demuxed native packet and restore that same edit-list window;
    # never shorten the last AAC packet or round the native60fps clock to ms.
    desired = audio['segment']
    target_audio = muxed['tracks'][b'soun']
    if target_audio['media_time'] != 0:
        raise ValueError('Copy mux unexpectedly changed native audio edit origin')
    offset, fmt = duration_field(data, target_audio['tkhd'], b'tkhd')
    before_track_duration = struct.unpack_from(fmt, data, offset)[0]
    struct.pack_into(fmt, data, offset, desired)
    struct.pack_into(target_audio['elst_format'], data, target_audio['elst'] + 8, desired)
    offset, fmt = duration_field(data, muxed['mvhd'], b'mvhd')
    struct.pack_into(fmt, data, offset, desired)
    target.write_bytes(data)
    return {'native_movie_timescale': native['timescale'], 'native_edit_duration': desired,
        'native_audio_edit_media_time': audio['media_time'],
        'copy_mux_audio_duration_before_native_window': before_track_duration,
        'presentation_seconds': str(duration),
        'changed_atoms_only': ['moov/mvhd duration', 'audio trak/tkhd duration', 'audio trak/edts/elst segment duration']}

def digest(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def depth_frames(root, shot):
    """Decode at most128 original half-depth frames; preserve every half-float bit."""
    spec = shot['depth']
    if spec['encoding'] != 'float16-xy-delta-zstd-v1' or spec['shape'] != [360, 640]:
        raise ValueError('Unknown lossless movie depth recipe')
    cursor = shot['start']
    for chunk in spec['chunks']:
        path = root / chunk['path']
        if (chunk['start'] != cursor or not 1 <= chunk['frames'] <= 128
                or chunk['size'] != path.stat().st_size or digest(path) != chunk['sha256']):
            raise ValueError('Movie depth chunk address or identity changed')
        expected = chunk['frames'] * 360 * 640 * 2
        raw = zstandard.ZstdDecompressor().decompress(path.read_bytes(), max_output_size=expected)
        if len(raw) != expected: raise ValueError('Movie depth chunk has an invalid decoded extent')
        value = np.frombuffer(raw, dtype='<u2').reshape(chunk['frames'], 360, 640)
        value = np.cumsum(np.cumsum(value, axis=1, dtype=np.uint16), axis=2, dtype=np.uint16)
        if hashlib.sha256(value.tobytes()).hexdigest() != chunk['rawSha256']:
            raise ValueError('Movie depth inverse predictor changed a half-float bit')
        value = value.view('<f2')
        if not np.isfinite(value).all(): raise ValueError('Nonfinite original movie depth')
        yield from value
        cursor += chunk['frames']
    if cursor != shot['end']: raise ValueError('Movie depth recipe lost original frames')


def stop(process):
    if process is None or process.poll() is not None: return
    if os.name == 'nt': process.kill()
    else:
        try: os.killpg(process.pid, signal.SIGKILL)
        except ProcessLookupError: pass
    process.wait()


def packet_hashes(ffmpeg, path, selector):
    command = [ffmpeg, '-hide_banner', '-v', 'error', '-nostdin', '-threads', '1', '-i', str(path),
        '-map', '0:' + selector + ':0', '-c', 'copy', '-f', 'framehash', '-hash', 'sha256', '-']
    result = subprocess.run(command, capture_output=True, check=True)
    return [line.strip() for line in result.stdout.decode().splitlines() if line and not line.startswith('#')]


def execute(command, log):
    process = None
    try:
        with Path(log).open('wb') as errors:
            process = subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=errors, start_new_session=os.name != 'nt')
            if process.wait(): raise RuntimeError('Movie tool failed; see ' + str(log))
    finally: stop(process)


def remux(ffmpeg, source, target, log):
    execute([ffmpeg, '-hide_banner', '-v', 'error', '-nostdin', '-y', '-threads', '1', '-i', str(source),
        '-map', '0:v:0', '-map', '0:a:0?', '-c', 'copy', '-map_metadata', '-1', '-movflags', '+faststart',
        '-f', 'mp4', str(target)], log)


def render(recipe_root, movie, source, target, ffmpeg, threads=3):
    if digest(source) != movie['source_sha256']: raise ValueError('Owned original movie changed before conversion')
    root = target.parent; root.mkdir(parents=True, exist_ok=True)
    encoded = target.with_name(target.name + '.picture.mp4')
    muxed = target.with_name(target.name + '.mux.mp4')
    fps, frames = movie['fps'], movie['frames']
    maximum_rate = 34 if movie['key'].startswith('Heroes/') else 17
    command = [ffmpeg, '-hide_banner', '-v', 'error', '-nostdin', '-y',
        '-f', 'rawvideo', '-pixel_format', 'bgr24', '-video_size', '1920x1080', '-framerate', str(fps), '-i', 'pipe:0',
        '-threads', '1', '-i', str(source), '-filter_complex_threads', '1', '-filter_complex',
        '[0:v]scale=flags=bicubic+accurate_rnd+full_chroma_int+full_chroma_inp:out_color_matrix=bt709,format=yuv420p,setsar=1[right];'
        '[1:v][right]hstack=inputs=2,setsar=1[packed]', '-map', '[packed]', '-frames:v', str(frames),
        '-c:v', 'libx264', '-threads', str(threads), '-preset', 'fast', '-crf', '17',
        '-maxrate', f'{maximum_rate}M', '-bufsize', f'{maximum_rate*2}M', '-profile:v', 'high', '-level:v', '5.1',
        '-g', str(fps), '-keyint_min', '1', '-video_track_timescale', str(fps*256),
        '-color_primaries', 'bt709', '-color_trc', 'bt709', '-colorspace', 'bt709', '-color_range', 'tv',
        '-map_metadata', '-1', '-movflags', '+faststart', '-f', 'mp4', str(encoded)]
    decode = [ffmpeg, '-hide_banner', '-v', 'error', '-nostdin', '-threads', '1', '-i', str(source), '-map', '0:v:0', '-an',
        '-vf', 'scale=in_color_matrix=bt709:in_range=tv', '-pix_fmt', 'bgr24', '-fps_mode', 'passthrough', '-f', 'rawvideo', '-']
    decoder = encoder = None
    cv2.setNumThreads(1)
    try:
        with (root/'decode.log').open('wb') as de, (root/'encode.log').open('wb') as en:
            decoder = subprocess.Popen(decode, stdout=subprocess.PIPE, stderr=de, start_new_session=os.name != 'nt')
            encoder = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.DEVNULL, stderr=en, start_new_session=os.name != 'nt')
            for shot in movie['shots']:
                depths = iter(depth_frames(recipe_root, shot)) if shot['mode'] == 'depth' else None
                info = {'normalization_p02_p98': shot['depth']['normalization']} if depths is not None else None
                for index in range(shot['start'], shot['end']):
                    raw = bytearray()
                    while len(raw) < WIDTH*HEIGHT*3:
                        block = decoder.stdout.read(WIDTH*HEIGHT*3-len(raw))
                        if not block: raise ValueError('Original decoder lost frame ' + str(index))
                        raw.extend(block)
                    original = np.frombuffer(raw, np.uint8).reshape(HEIGHT, WIDTH, 3)
                    field = field_for(next(depths) if depths is not None else None, original, movie, shot, info, frame=index)
                    right, _ = (original, 0) if shot['mode'] == 'screen' else forward_warp(original, field)
                    encoder.stdin.write(right.tobytes())
                    if index % 8 == 0 or index == frames-1:
                        print(json.dumps({'done': index+1, 'total': frames, 'detail': movie['key']}), flush=True)
            if decoder.stdout.read(1) or decoder.wait(timeout=60): raise ValueError('Original decoder produced extra frames or failed')
            encoder.stdin.close()
            if encoder.wait(timeout=600): raise RuntimeError('SBS picture encoder failed')
        # Copy every original AAC packet after finishing picture encoding. The
        # video frame count may never truncate native audio's final coded packet.
        mux = [ffmpeg, '-hide_banner', '-v', 'error', '-nostdin', '-y', '-threads', '1',
            '-i', str(encoded), '-i', str(source), '-map', '0:v:0', '-map', '1:a:0?', '-c', 'copy', '-map_metadata', '-1']
        if movie['key'].startswith('Heroes/'):
            layout = movie_layout(source.read_bytes())
            mux += ['-movie_timescale', str(layout['timescale'])]
        mux += ['-movflags', '+faststart', '-f', 'mp4', str(muxed)]
        execute(mux, root/'mux.log')
        if movie['key'].startswith('Heroes/'): restore_native_window(muxed, source, target, movie)
        else: muxed.replace(target)
        if movie['key'].startswith(('Heroes/', 'Embedded/')):
            if packet_hashes(ffmpeg, source, 'a') != packet_hashes(ffmpeg, target, 'a'):
                raise ValueError('Original AAC packets or clocks changed')
        if target.stat().st_size > 100*1024**2: raise ValueError('Movie exceeds the unchanged100MiB per-film gate')
    finally:
        stop(decoder); stop(encoder)
        encoded.unlink(missing_ok=True); muxed.unlink(missing_ok=True)


def main():
    p = argparse.ArgumentParser(); p.add_argument('--job', type=Path, required=True)
    args = p.parse_args(); job = json.loads(args.job.read_text())
    import imageio_ffmpeg
    ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
    source, target = Path(job['source']), Path(job['target'])
    if job['kind'] == 'remux': remux(ffmpeg, source, target, target.parent/'remux.log')
    else: render(Path(job['recipeRoot']), job['movie'], source, target, ffmpeg, job.get('threads',3))
    print(json.dumps({'done':job.get('frames',1),'total':job.get('frames',1),'detail':'complete'}),flush=True)
    return 0


if __name__ == '__main__': raise SystemExit(main())
