#!/usr/bin/env python3
"""Losslessly finalize native hero AAC and its original MOV presentation window."""
from __future__ import annotations
import json
from fractions import Fraction
import os
from pathlib import Path
import signal
import struct
import subprocess
from intro_depth import digest, save
from intro_stereo import audio_packets


def packets(path: Path, selector: str) -> list[dict]:
    command = ['ffprobe', '-v', 'error', '-select_streams', selector, '-show_packets',
        '-show_data_hash', 'sha256', '-show_entries', 'packet=pts,dts,duration,size,data_hash',
        '-of', 'json', str(path)]
    return json.loads(subprocess.check_output(command))['packets']


def probe(path: Path) -> dict:
    return json.loads(subprocess.check_output(['ffprobe', '-v', 'error', '-show_streams',
        '-show_format', '-of', 'json', str(path)]))


def require_packets(expected: list[dict], actual: list[dict], label: str) -> None:
    if not expected or expected != actual:
        raise ValueError(label + ' packet payloads, PTS/DTS, durations or count changed')


def copy_mux(command: list[str], evidence: Path) -> None:
    with (evidence / 'copy-mux.log').open('wb') as stream:
        process = subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=stream,
                                   start_new_session=True)
        save(evidence / 'copy-mux-process.json', {'pid': process.pid, 'status': 'running'})
        try:
            if process.wait():
                raise RuntimeError('Lossless native AAC copy mux failed')
        finally:
            if process.poll() is None:
                try:
                    os.killpg(process.pid, signal.SIGKILL)
                except ProcessLookupError:
                    pass
                finally:
                    process.wait()
            save(evidence / 'copy-mux-process.json', {'pid': process.pid,
                'status': 'stopped-and-reaped', 'returncode': process.returncode})


def presentation(stream: dict) -> Fraction:
    return Fraction(int(stream['duration_ts'])) * Fraction(stream['time_base'])


def validate_mux(encoded: Path, source: Path, final: Path) -> dict:
    old, native, finished = probe(encoded), probe(source), probe(final)
    native_audio = next(s for s in native['streams'] if s['codec_type'] == 'audio')
    final_audio = next(s for s in finished['streams'] if s['codec_type'] == 'audio')
    old_video = next(s for s in old['streams'] if s['codec_type'] == 'video')
    final_video = next(s for s in finished['streams'] if s['codec_type'] == 'video')
    if (native_audio['time_base'] != final_audio['time_base']
            or old_video['time_base'] != final_video['time_base']):
        raise ValueError('Copy mux changed a native audio or encoded video clock')
    original_audio, final_audio_packets = audio_packets(source), audio_packets(final)
    original_video, final_video_packets = packets(encoded, 'v:0'), packets(final, 'v:0')
    require_packets(original_audio, final_audio_packets, 'Native AAC')
    require_packets(original_video, final_video_packets, 'Encoded video')
    if (presentation(native_audio) != presentation(final_audio)
            or presentation(old_video) != presentation(final_video)):
        raise ValueError('Copy mux changed the original presented audio or video window')
    return {'status': 'passed', 'identical_aac_packets': len(original_audio),
        'identical_encoded_video_packets': len(original_video),
        'audio_presentation_seconds': str(presentation(final_audio)),
        'video_presentation_seconds': str(presentation(final_video)),
        'audio_time_base': final_audio['time_base'], 'video_time_base': final_video['time_base'],
        'audio_payload_pts_dts_durations_identical': True,
        'video_payload_pts_dts_durations_identical': True}


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


def finalize_audio(encoded: Path, source: Path, target: Path, movie: dict, evidence: Path) -> dict:
    if target.exists():
        raise ValueError('Refusing to replace an existing finalized movie')
    evidence.mkdir(parents=True, exist_ok=False)
    source_layout = movie_layout(source.read_bytes())
    raw = evidence / 'copy-mux-before-native-window.mov'
    command = ['ffmpeg', '-hide_banner', '-v', 'error', '-nostdin', '-n', '-threads', '1',
        '-i', str(encoded), '-i', str(source), '-map', '0:v:0', '-map', '1:a:0',
        '-c', 'copy', '-map_metadata', '0', '-movie_timescale', str(source_layout['timescale']),
        '-movflags', '+faststart', str(raw)]
    save(evidence / 'command.json', command)
    copy_mux(command, evidence)
    window = restore_native_window(raw, source, target, movie)
    proof = validate_mux(encoded, source, target)
    proof.update({'subject': movie['key'], 'encoded_candidate_sha256': digest(encoded),
        'native_source_sha256': digest(source), 'finalized_sha256': digest(target),
        'finalized_bytes': target.stat().st_size, 'native_presentation': window,
        'copy_mux_source_sha256': digest(Path(__file__)), 'picture_reencoded': False})
    save(evidence / 'receipt.json', proof)
    return proof
