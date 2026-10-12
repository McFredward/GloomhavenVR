#!/usr/bin/env python3
"""Assemble versioned trailer reviews; preserve sources and burn in fan-mod branding.

Input JSON specifies shots, licensed source metadata and timed audio excerpts.
Pillow creates typography/logo layers; FFmpeg edits the video without inference.
"""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import re
import shutil
import subprocess

from PIL import Image, ImageDraw, ImageFont
import numpy as np


REPO = Path(__file__).resolve().parents[2]
WIDTH, HEIGHT, FPS = 1920, 1080, 24
LABEL = "UNOFFICIAL · FAN-MADE MOD"
FONT = Path("/usr/share/fonts/truetype/lato/Lato-Semibold.ttf")
SERIF = Path("/usr/share/fonts/truetype/dejavu/DejaVuSerif.ttf")


def run(command, log, input_bytes=None):
    try:
        with log.open("a") as stream:
            subprocess.run(command, input=input_bytes, stdout=stream, stderr=stream, check=True)
    except (subprocess.CalledProcessError, KeyboardInterrupt):
        # subprocess.run has reaped the encoder. Delete only this invocation's
        # owned intermediate encodes, retaining the error log and source evidence.
        cache = log.parent.parent.parent / "edit-work" / log.parent.name
        owner = cache / "owner.json"
        if owner.is_file() and json.loads(owner.read_text()).get("pid") == os.getpid():
            shutil.rmtree(cache)
        raise


def digest(path):
    with Path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def branding(path):
    layer = Image.new("RGBA", (WIDTH, HEIGHT), (0, 0, 0, 0))
    draw = ImageDraw.Draw(layer)
    font = ImageFont.truetype(str(FONT), 24)
    box = draw.textbbox((0, 0), LABEL, font=font)
    x, y = WIDTH - (box[2] - box[0]) - 54, HEIGHT - 137
    # Keep the reference uploader's lower-right watermark visible below this
    # label. The final own-capture edit keeps the same consistent safe position.
    draw.rounded_rectangle((x-15, y-10, WIDTH-38, y+35), radius=4,
                           fill=(7, 9, 12, 205))
    draw.rectangle((x-15, y-10, x-13, y+35), fill=(190, 149, 86, 255))
    draw.text((x, y), LABEL, font=font, fill=(241, 236, 225, 255))
    layer.save(path)


def title(path, subtitle="TAKE YOUR PLACE"):
    layer = Image.new("RGB", (WIDTH, HEIGHT), (5, 7, 9))
    logo = Image.open(REPO / "src/GloomhavenVR/Assets/GloomhavenVR_logo.png").convert("RGBA")
    logo = logo.resize((1450, round(logo.height*1450/logo.width)), Image.Resampling.LANCZOS)
    layer.paste(logo, ((WIDTH-logo.width)//2, 370), logo)
    draw = ImageDraw.Draw(layer)
    for text, size, y, color, face in (
        (subtitle, 46, 640, (214, 187, 138), SERIF),
        ("AN UNOFFICIAL COMMUNITY VR MOD", 25, 731, (165, 170, 177), FONT),
        ("Requires Gloomhaven Digital", 23, 778, (146, 151, 158), FONT),
        ("A community project by McFredward", 22, 823, (125, 131, 138), FONT),
    ):
        font = ImageFont.truetype(str(face), size)
        box = draw.textbbox((0, 0), text, font=font)
        draw.text(((WIDTH-(box[2]-box[0]))/2, y), text, font=font, fill=color)
    draw.line((680, 704, 1240, 704), fill=(95, 76, 48), width=1)
    layer.save(path)


def mix_audio(events, seconds, destination, log, sources, ducking=None):
    rate = 48000
    timeline = np.zeros((round(seconds*rate), 2), dtype=np.float32)
    music = None
    if ducking is not None:
        gain = float(ducking.get("gain", .30))
        attack = float(ducking.get("attack", .12))
        release = float(ducking.get("release", .35))
        if (not all(math.isfinite(v) for v in (gain, attack, release))
                or not 0 <= gain <= 1 or min(attack, release) < 0):
            raise ValueError("Invalid music ducking gain or timing")
        music = np.zeros_like(timeline)
        duck = np.ones(len(timeline), dtype=np.float32)
    for event in events:
        path = Path(event["source"]).resolve(strict=True)
        sources.setdefault(str(path), digest(path))
        start, duration = float(event.get("start", 0)), float(event["duration"])
        at, volume = float(event.get("at", 0)), float(event.get("volume", 1))
        fade_in = float(event.get("fade_in", .02))
        fade_out = float(event.get("fade_out", .05))
        if (not all(math.isfinite(v) for v in (start, duration, at, volume, fade_in, fade_out))
                or min(start, at, volume, fade_in, fade_out) < 0 or duration <= 0 or at >= seconds):
            raise ValueError("Invalid audio event timing or volume")
        # Decode each excerpt independently and mix on an explicit sample
        # timeline. Delayed amix/atrim branches can finish early in some FFmpeg
        # builds; sample placement makes the requested duration unambiguous.
        raw = subprocess.check_output([
            "ffmpeg", "-v", "error", "-ss", str(start), "-i", str(path),
            "-t", str(duration), "-vn", "-ar", str(rate), "-ac", "2",
            "-f", "f32le", "-"])
        chunk = np.frombuffer(raw, dtype="<f4").reshape(-1, 2).copy()
        if not len(chunk) or not np.isfinite(chunk).all():
            raise ValueError("Audio excerpt is empty or non-finite")
        offset = round(at*rate)
        count = min(len(chunk), round(duration*rate), len(timeline)-offset)
        chunk = chunk[:count]
        head, tail = min(count, round(fade_in*rate)), min(count, round(fade_out*rate))
        if head:
            chunk[:head] *= np.linspace(0, 1, head, dtype=np.float32)[:, None]
        if tail:
            chunk[-tail:] *= np.linspace(1, 0, tail, dtype=np.float32)[:, None]
        if music is not None and event.get("role") == "music":
            music[offset:offset+count] += chunk*volume
        else:
            timeline[offset:offset+count] += chunk*volume
        if music is not None and event.get("role") == "voice":
            before = max(0, offset-round(attack*rate))
            after = min(len(timeline), offset+count+round(release*rate))
            if before < offset:
                duck[before:offset] = np.minimum(duck[before:offset],
                                                np.linspace(1, gain, offset-before, dtype=np.float32))
            duck[offset:offset+count] = np.minimum(duck[offset:offset+count], gain)
            if offset+count < after:
                duck[offset+count:after] = np.minimum(duck[offset+count:after],
                                                     np.linspace(gain, 1, after-offset-count, dtype=np.float32))
    if music is not None:
        timeline += music*duck[:, None]
    run(["ffmpeg", "-v", "warning", "-y", "-f", "f32le", "-ar", str(rate),
         "-ac", "2", "-i", "-", "-af", "loudnorm=I=-14:TP=-1:LRA=8",
         "-t", str(seconds), "-c:a", "pcm_f32le", "-ar", str(rate), str(destination)],
        log, input_bytes=timeline.astype("<f4").tobytes())
    probe = json.loads(subprocess.check_output([
        "ffprobe", "-v", "error", "-show_entries", "format=duration",
        "-of", "json", str(destination)]))
    if abs(float(probe["format"]["duration"])-seconds) > .01:
        raise ValueError("Mixed audio duration differs from the edit")


def _assemble(spec_path, output, name):
    spec = json.loads(spec_path.read_text())
    if not re.fullmatch(r"[a-zA-Z0-9][a-zA-Z0-9_-]{0,79}", name):
        raise ValueError("Review name must be a safe basename")
    output = output.resolve()
    protected = [(REPO/p).resolve() for p in ("ressources", "libs", "decompiled", "src", "unity")]
    if any(output == p or output.is_relative_to(p) for p in protected):
        raise ValueError("Review outputs must be outside original source assets")
    output.mkdir(parents=True, exist_ok=True)
    if (output/(name+".mp4")).exists():
        raise ValueError("Review movie already exists; choose a new versioned name")
    if (output/"latest.mp4").exists() and not (output/"latest.mp4").is_symlink():
        raise ValueError("latest.mp4 must be a managed symlink; preserve the existing file")
    private = output.parent / "edit-work" / name
    private.mkdir(parents=True, exist_ok=False)
    (private/"owner.json").write_text(json.dumps({"pid": os.getpid(), "name": name}))
    evidence = output.parent / "edit-evidence" / name
    evidence.mkdir(parents=True, exist_ok=True)
    log = evidence / "ffmpeg.log"
    water, end_card = evidence/"branding.png", evidence/"title.png"
    branding(water)
    title(end_card, spec.get("tagline", "TAKE YOUR PLACE"))
    segments, receipt, total = [], [], 0.0
    sources = {}
    for index, shot in enumerate(spec["shots"]):
        duration = float(shot["duration"])
        if not math.isfinite(duration) or duration <= 0:
            raise ValueError("Shot duration must be finite and positive")
        source = end_card if shot["kind"] == "title" else Path(shot["source"]).resolve(strict=True)
        start = float(shot.get("start", 0))
        segment = private/f"shot-{index:02d}.mp4"
        command = ["ffmpeg", "-v", "warning", "-y"]
        still = shot["kind"] in {"title", "image"}
        if still:
            command += ["-loop", "1", "-framerate", str(FPS)]
        else:
            command += ["-ss", str(start)]
        command += ["-i", str(source)]
        crop = "scale=1920:1080:force_original_aspect_ratio=increase,crop=1920:1080"
        if still and shot.get("push"):
            frames = round(duration * FPS)
            gain = float(shot["push"])
            crop = ("scale=3840:2160," +
                    f"zoompan=z='1+{gain}*on/{frames}':x='iw/2-iw/zoom/2':" +
                    f"y='ih/2-ih/zoom/2':d=1:s=1920x1080:fps={FPS}")
        if "grade" in shot:
            grade = shot["grade"]
            crop += f",eq=gamma={float(grade.get('gamma',1))}:saturation={float(grade.get('saturation',1))}"
        command += ["-an", "-t", str(duration), "-vf", crop+f",fps={FPS},setsar=1,format=yuv420p",
                    "-c:v", "libx264", "-preset", "medium", "-crf", "16", "-threads", "4", str(segment)]
        run(command, log)
        segments.append(segment)
        sources.setdefault(str(source), digest(source))
        receipt.append({**shot, "source": str(source), "timeline_start": total,
                        "encoded_segment_sha256": digest(segment)})
        total += duration
    joined = private/"picture.mp4"
    join_command = ["ffmpeg", "-v", "warning", "-y", "-filter_complex_threads", "2"]
    for segment in segments:
        join_command += ["-i", str(segment)]
    # Decode before joining: stream-copy concatenation can propagate mismatched
    # H.264 parameter sets and timestamps across generated/captured inputs.
    join_graph = [f"[{i}:v]setpts=PTS-STARTPTS[shot{i}]" for i in range(len(segments))]
    join_graph.append("".join(f"[shot{i}]" for i in range(len(segments))) +
                      f"concat=n={len(segments)}:v=1:a=0,fps={FPS},setpts=N/({FPS}*TB)[picture]")
    join_command += ["-filter_complex", ";".join(join_graph), "-map", "[picture]", "-an",
                     "-c:v", "libx264", "-preset", "medium", "-crf", "16", "-threads", "4", str(joined)]
    run(join_command, log)
    # Render a finite stereo mix separately. Many short, delayed inputs in the
    # same graph as a looping PNG can stall older FFmpeg framesync schedulers.
    audio = spec.get("audio", [])
    if audio:
        mixed = private/"mix.wav"
        mix_audio(audio, total, mixed, log, sources, spec.get("audio_ducking"))
    command = ["ffmpeg", "-v", "warning", "-y", "-filter_complex_threads", "2",
               "-i", str(joined), "-loop", "1", "-framerate", str(FPS),
               "-t", str(total), "-i", str(water)]
    graph = [f"[0:v]setpts=N/({FPS}*TB),fade=t=in:st=0:d=0.18,"
             f"fade=t=out:st={total-.28}:d=0.28[picture]",
             f"[1:v]setpts=N/({FPS}*TB)[water]",
             "[picture][water]overlay=0:0:format=auto:shortest=1,format=yuv420p[video]"]
    if audio:
        command += ["-i", str(mixed)]
    command += ["-filter_complex", ";".join(graph), "-map", "[video]"]
    if audio:
        command += ["-map", "2:a", "-c:a", "aac", "-b:a", "256k", "-ar", "48000", "-ac", "2"]
    movie = private/"master.mp4"
    command += ["-t", str(total), "-c:v", "libx264", "-preset", "slow", "-crf", "17",
                "-threads", "4", "-movflags", "+faststart", str(movie)]
    run(command, log)
    probe = json.loads(subprocess.check_output([
        "ffprobe", "-v", "error", "-show_entries", "stream=codec_type,nb_frames,width,height,r_frame_rate,duration:format=duration",
        "-of", "json", str(movie)]))
    video = next(s for s in probe["streams"] if s["codec_type"] == "video")
    if int(video["nb_frames"]) != round(total*FPS) or video["r_frame_rate"] != f"{FPS}/1":
        raise ValueError("Rendered movie frame count/rate differs from the edit")
    if audio:
        sound = next(s for s in probe["streams"] if s["codec_type"] == "audio")
        if abs(float(sound["duration"])-total) > .05:
            raise ValueError("Rendered audio duration differs from the edit")
    run(["ffmpeg", "-v", "warning", "-y", "-ss", str(spec.get("poster_time", min(8,total/2))),
         "-i", str(movie), "-frames:v", "1", str(output/(name+".jpg"))], log)
    receipt = {"name": name, "duration_seconds": total, "frame_rate": FPS,
               "branding": "Continuous lower-right "+LABEL+"; explicit end-card mod attribution",
               "tool_sha256": digest(__file__), "spec": spec, "shots": receipt,
               "sources_sha256": sources, "movie_sha256": digest(movie), "ffprobe": probe}
    (evidence/"edit-manifest.json").write_text(json.dumps(receipt, indent=2)+"\n")
    # A review appears only after its complete encode and validation, so opening
    # the shared directory never exposes an unfinished MP4 under its final name.
    completed = output/(name+".mp4")
    os.replace(movie, completed)
    movie = completed
    latest = output/"latest.mp4"
    if latest.is_symlink():
        latest.unlink()
    elif latest.exists():
        raise ValueError("latest.mp4 must be a managed symlink; preserve the existing file")
    latest.symlink_to(movie.name)
    shutil.copy2(output/(name+".jpg"), output/"latest.jpg")
    # Completed intermediate encodes are reproducible caches; all raw inputs,
    # manifests, still captures, logs and the actual review remain retained.
    shutil.rmtree(private)
    print(json.dumps({"movie": str(movie), "seconds": total, "poster": str(output/(name+".jpg"))}))


def assemble(spec_path, output, name):
    try:
        _assemble(spec_path, output, name)
    finally:
        if re.fullmatch(r"[a-zA-Z0-9][a-zA-Z0-9_-]{0,79}", name):
            cache = output.resolve().parent/"edit-work"/name
            owner = cache/"owner.json"
            if owner.is_file() and json.loads(owner.read_text()).get("pid") == os.getpid():
                # All subprocesses have been reaped before returning here.
                shutil.rmtree(cache)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("spec", type=Path)
    parser.add_argument("--output", type=Path, default=REPO/".planning/debug/trailer/review")
    parser.add_argument("--name", required=True)
    args = parser.parse_args()
    try:
        assemble(args.spec.resolve(strict=True), args.output, args.name)
    except ValueError as error:
        parser.error(str(error))
