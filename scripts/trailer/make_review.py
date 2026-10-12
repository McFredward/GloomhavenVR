#!/usr/bin/env python3
"""Assemble versioned trailer reviews; preserve sources and burn in fan-mod branding.

Input JSON specifies shots, licensed source metadata and timed audio excerpts.
Pillow creates typography/logo layers; FFmpeg edits the video without inference.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import re
import shutil
import subprocess

from PIL import Image, ImageDraw, ImageFont


REPO = Path(__file__).resolve().parents[2]
WIDTH, HEIGHT, FPS = 1920, 1080, 24
LABEL = "UNOFFICIAL · FAN-MADE MOD"
FONT = Path("/usr/share/fonts/truetype/lato/Lato-Semibold.ttf")
SERIF = Path("/usr/share/fonts/truetype/dejavu/DejaVuSerif.ttf")


def run(command, log):
    with log.open("a") as stream:
        subprocess.run(command, stdout=stream, stderr=stream, check=True)


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
    logo.thumbnail((1450, 400), Image.Resampling.LANCZOS)
    layer.paste(logo, ((WIDTH-logo.width)//2, 370), logo)
    draw = ImageDraw.Draw(layer)
    for text, size, y, color, face in (
        (subtitle, 46, 640, (214, 187, 138), SERIF),
        ("AN UNOFFICIAL COMMUNITY VR MOD", 25, 731, (165, 170, 177), FONT),
        ("Requires Gloomhaven Digital", 23, 778, (146, 151, 158), FONT),
    ):
        font = ImageFont.truetype(str(face), size)
        box = draw.textbbox((0, 0), text, font=font)
        draw.text(((WIDTH-(box[2]-box[0]))/2, y), text, font=font, fill=color)
    draw.line((680, 704, 1240, 704), fill=(95, 76, 48), width=1)
    layer.save(path)


def assemble(spec_path, output, name):
    spec = json.loads(spec_path.read_text())
    if not re.fullmatch(r"[a-zA-Z0-9][a-zA-Z0-9_-]{0,79}", name):
        raise ValueError("Review name must be a safe basename")
    output = output.resolve()
    protected = [(REPO/p).resolve() for p in ("ressources", "libs", "decompiled", "src", "unity")]
    if any(output == p or output.is_relative_to(p) for p in protected):
        raise ValueError("Review outputs must be outside original source assets")
    output.mkdir(parents=True, exist_ok=True)
    private = output.parent / "edit-work" / name
    private.mkdir(parents=True, exist_ok=True)
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
    listing = private/"concat.txt"
    listing.write_text("".join("file '"+str(p).replace("'", "'\\''")+"'\n" for p in segments))
    joined = private/"picture.mp4"
    run(["ffmpeg", "-v", "warning", "-y", "-f", "concat", "-safe", "0", "-i", str(listing),
         "-c", "copy", "-an", str(joined)], log)
    command = ["ffmpeg", "-v", "warning", "-y", "-i", str(joined),
               "-loop", "1", "-framerate", str(FPS), "-i", str(water)]
    graph = [f"[0:v]fade=t=in:st=0:d=0.18,fade=t=out:st={total-.28}:d=0.28[picture]",
             "[picture][1:v]overlay=0:0:format=auto,format=yuv420p[video]"]
    audio = []
    for index, event in enumerate(spec.get("audio", []), start=2):
        path = Path(event["source"]).resolve(strict=True)
        sources.setdefault(str(path), digest(path))
        command += ["-i", str(path)]
        start, duration = float(event.get("start", 0)), float(event["duration"])
        delay, volume = round(float(event.get("at", 0))*1000), float(event.get("volume", 1))
        label = f"audio{index}"
        graph.append(f"[{index}:a]atrim=start={start}:duration={duration},asetpts=PTS-STARTPTS,"
                     f"afade=t=in:d=0.02,afade=t=out:st={max(0,duration-.05)}:d=0.05,"
                     f"volume={volume},adelay={delay}:all=1,apad=whole_dur={total}[{label}]")
        audio.append(f"[{label}]")
    if audio:
        graph.append("".join(audio)+f"amix=inputs={len(audio)}:normalize=0,atrim=duration={total},"
                     "loudnorm=I=-14:TP=-1:LRA=8[audio]")
    command += ["-filter_complex", ";".join(graph), "-map", "[video]"]
    if audio:
        command += ["-map", "[audio]", "-c:a", "aac", "-b:a", "256k", "-ar", "48000"]
    command += ["-t", str(total), "-c:v", "libx264", "-preset", "slow", "-crf", "17",
                "-threads", "4", "-movflags", "+faststart", str(output/(name+".mp4"))]
    run(command, log)
    movie = output/(name+".mp4")
    run(["ffmpeg", "-v", "warning", "-y", "-ss", str(spec.get("poster_time", min(8,total/2))),
         "-i", str(movie), "-frames:v", "1", str(output/(name+".jpg"))], log)
    receipt = {"name": name, "duration_seconds": total, "frame_rate": FPS,
               "branding": "Continuous lower-right "+LABEL+"; explicit end-card mod attribution",
               "tool_sha256": digest(__file__), "spec": spec, "shots": receipt,
               "sources_sha256": sources, "movie_sha256": digest(movie)}
    (evidence/"edit-manifest.json").write_text(json.dumps(receipt, indent=2)+"\n")
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


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("spec", type=Path)
    parser.add_argument("--output", type=Path, default=REPO/".planning/debug/trailer/review")
    parser.add_argument("--name", required=True)
    args = parser.parse_args()
    assemble(args.spec.resolve(strict=True), args.output, args.name)
