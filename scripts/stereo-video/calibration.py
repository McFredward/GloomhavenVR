"""Offline, code-drawn stereo calibration; no game inputs or inferred depth."""
from __future__ import annotations

from fractions import Fraction
import hashlib
import json
import os
from pathlib import Path
import signal
import subprocess
import sys

sys.dont_write_bytecode = True
from PIL import Image, ImageDraw, ImageFont

WIDTH, HEIGHT, FPS, FRAMES = 1920, 1080, 60, 540
CUTS = (180, 360)
HEADER_Y, HEADER_X, CELL = 224, 96, 48
DEPTH_Y = 540
EYE_CODES = {"left": 1, "right": 2, "mono": 3}
LABELS = {"left": "LEFT EYE / LINKES AUGE", "right": "RIGHT EYE / RECHTES AUGE",
          "mono": "MONO / 2D"}
ROOT = Path(__file__).resolve().parents[2]


def save(path: Path, value: object) -> None:
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n")


def digest(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def run(command: list[str], log: Path, timeout: int = 180) -> bytes:
    """Reap even cancelled tools before any caller's completion cleanup."""
    with log.open("wb") as error:
        process = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=error,
                                   start_new_session=True)
        try:
            output, _ = process.communicate(timeout=timeout)
        except BaseException:
            try:
                os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
            process.wait()
            raise
    if process.returncode:
        raise RuntimeError(f"Tool failed ({process.returncode}); see {log}")
    return output


def header(eye: str, frame: int) -> list[int]:
    # Fixed magic + eye + absolute clock + shot. Each cell has a wide codec guard.
    fields = ((0xA7, 8), (EYE_CODES[eye], 2), (frame, 10), (frame // 180, 2))
    return [int(bit) for value, width in fields for bit in f"{value:0{width}b}"]


def motion(frame: int) -> int:
    """Integer triangle: continuous within each 180-frame shot, 48px travel."""
    phase = frame % 180
    return (phase if phase < 90 else 180 - phase) * 48 // 90 - 24


def marker_centres(eye: str, frame: int) -> tuple[int, int, int]:
    shift = {"left": 6, "right": -6, "mono": 0}[eye]
    travel = motion(frame)
    return 480 + travel + shift, 960 + travel, 1440 + travel - shift


def render(eye: str, frame: int) -> Image.Image:
    shot = frame // 180
    image = Image.new("RGB", (WIDTH, HEIGHT), ((17, 23, 31), (23, 17, 31), (17, 31, 23))[shot])
    draw = ImageDraw.Draw(image)
    small = ImageFont.load_default(size=30)
    medium = ImageFont.load_default(size=42)
    large = ImageFont.load_default(size=58)
    # The same grid and all explanatory text are at zero disparity in both eyes.
    for x in range(0, WIDTH, 120):
        draw.line((x, 300, x, 780), fill=(48, 54, 62), width=2)
    for y in range(300, 781, 120):
        draw.line((48, y, WIDTH - 48, y), fill=(48, 54, 62), width=2)
    draw.rectangle((24, 24, WIDTH - 25, HEIGHT - 25), outline=(190, 190, 190), width=4)
    draw.text((72, 46), LABELS[eye], font=large, fill=(235, 235, 235))
    draw.text((1390, 58), "UP / OBEN  ^", font=medium, fill=(235, 235, 235))
    draw.text((72, 123), "GloomhavenVR | STEREO CALIBRATION / STEREO-KALIBRIERUNG", font=small,
              fill=(200, 200, 200))
    # Asymmetric corner markers distinguish rotation, vertical flip and mirroring.
    draw.polygon(((40, 300), (40, 370), (110, 300)), fill=(220, 160, 60))
    draw.ellipse((WIDTH - 110, 300, WIDTH - 40, 370), fill=(70, 160, 220))
    draw.text((72, 730), "BOTTOM / UNTEN  v", font=medium, fill=(210, 210, 210))
    for i, bit in enumerate(header(eye, frame)):
        shade = 230 if bit else 20
        x = HEADER_X + i * CELL
        draw.rectangle((x, HEADER_Y - 18, x + CELL - 9, HEADER_Y + 18), fill=(shade,) * 3)
    draw.text((1240, 205), "CLOCK / TAKT", font=small, fill=(210, 210, 210))
    colours = ((60, 225, 225), (220, 220, 220), (245, 195, 90))
    names = ("NEAR / NAH", "SCREEN / LEINWAND", "FAR / FERN")
    for x, centre, colour, name in zip((480, 960, 1440), marker_centres(eye, frame), colours, names):
        draw.text((x - 180, 370), name, font=medium, fill=(220, 220, 220))
        draw.rectangle((centre - 80, 460, centre + 80, 620), fill=colour)
        draw.text((x - 160, 656), ("+12 px", "0 px", "-12 px")[(x // 480) - 1], font=small,
                  fill=(220, 220, 220))
    # Clock comes from one absolute frame address, for both eye pictures and Mono.
    draw.text((72, 842), f"FRAME / BILD  {frame:04d} / {FRAMES - 1:04d}   |   60 fps", font=large,
              fill=(240, 240, 240))
    draw.text((72, 928), f"SHOT / SZENE {shot + 1}/3 | CUTS / SCHNITTE 0180, 0360 | {frame / FPS:05.2f} s",
              font=medium, fill=(210, 210, 210))
    return image


def encode(mode: str, target: Path, ffmpeg: str, directory: Path) -> list[str]:
    width = WIDTH * 2 if mode == "sbs" else WIDTH
    command = [ffmpeg, "-hide_banner", "-v", "error", "-nostdin", "-n", "-threads", "1",
               "-filter_threads", "1", "-f", "rawvideo", "-pixel_format", "rgb24", "-video_size",
               f"{width}x{HEIGHT}", "-framerate", str(FPS), "-i", "pipe:0", "-an", "-frames:v",
               str(FRAMES), "-vf", "scale=out_color_matrix=bt709,format=yuv420p,setsar=1", "-c:v",
               "libx264", "-threads", "1", "-preset", "veryfast", "-crf", "18", "-bf", "0",
               "-g", "60", "-keyint_min", "60", "-sc_threshold", "0", "-r", str(FPS),
               "-video_track_timescale", "60000", "-color_primaries", "bt709", "-color_trc", "bt709",
               "-colorspace", "bt709", "-color_range", "tv", "-map_metadata", "-1", "-fflags",
               "+bitexact", "-movflags", "+faststart", str(target)]
    with (directory / f"encode-{mode}.log").open("wb") as error:
        process = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.DEVNULL,
                                   stderr=error, start_new_session=True)
        try:
            for frame in range(FRAMES):
                if mode == "sbs":
                    image = Image.new("RGB", (width, HEIGHT))
                    image.paste(render("left", frame), (0, 0))
                    image.paste(render("right", frame), (WIDTH, 0))
                else:
                    image = render("mono", frame)
                if frame in (0, 179, 180, 359, 360, 539):
                    image.save(directory / f"source-{mode}-{frame:04d}.png")
                process.stdin.write(image.tobytes())
            process.stdin.close()
            process.wait(timeout=180)
        except BaseException:
            try:
                os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
            process.wait()
            raise
    if process.returncode:
        raise RuntimeError(f"Encoder failed ({process.returncode}); see encode-{mode}.log")
    return command


def observe(path: Path, mode: str, directory: Path, ffmpeg: str, ffprobe: str) -> dict:
    probe = json.loads(run([ffprobe, "-v", "error", "-count_frames", "-show_streams", "-show_format",
                           "-show_frames", "-show_entries",
                           "stream=codec_name,codec_type,width,height,pix_fmt,r_frame_rate,avg_frame_rate,time_base,nb_read_frames,duration,sample_aspect_ratio:format=duration:frame=best_effort_timestamp_time,key_frame",
                           "-of", "json", str(path)], directory / f"probe-{mode}.log"))
    rows = {}
    for name, y in (("header", HEADER_Y), ("depth", DEPTH_Y)):
        rows[name] = run([ffmpeg, "-v", "error", "-nostdin", "-threads", "1", "-i", str(path),
                          "-vf", f"format=gray,crop=iw:1:0:{y}:exact=1", "-an", "-vsync", "0",
                          "-f", "rawvideo", "-pix_fmt", "gray", "pipe:1"],
                         directory / f"decode-{mode}-{name}.log")
    return {"probe": probe, **rows}


def validate(observation: dict, mode: str) -> dict:
    """Measure decoded pixels/PTS; metadata, labels and a round trip alone are insufficient."""
    streams = observation["probe"]["streams"]
    if len(streams) != 1 or streams[0].get("codec_type") != "video":
        raise ValueError("streams: exactly one silent video stream is required")
    stream = streams[0]
    width = WIDTH * (2 if mode == "sbs" else 1)
    if (stream.get("width"), stream.get("height")) != (width, HEIGHT):
        raise ValueError("size: eye dimensions or full-SBS packing differ")
    if stream.get("codec_name") != "h264" or stream.get("pix_fmt") != "yuv420p":
        raise ValueError("codec: require H.264 8-bit YUV420")
    if stream.get("sample_aspect_ratio") != "1:1":
        raise ValueError("aspect: square source pixels required")
    if any(Fraction(stream[key]) != FPS for key in ("r_frame_rate", "avg_frame_rate")):
        raise ValueError("clock: require exact 60fps")
    if int(stream.get("nb_read_frames", 0)) != FRAMES:
        raise ValueError("clock: decoded frame count differs")
    if abs(float(stream["duration"]) - FRAMES / FPS) > 0.000001:
        raise ValueError("clock: stream duration differs")
    frames = observation["probe"]["frames"]
    if len(frames) != FRAMES or any(abs(float(item["best_effort_timestamp_time"]) - i / FPS) > .000001
                                    for i, item in enumerate(frames)):
        raise ValueError("clock: decoded presentation timestamps differ")
    if any(frames[i]["key_frame"] != 1 for i in (0, *CUTS)):
        raise ValueError("seek: cuts must start at independently decodable keyframes")
    if any(len(observation[name]) != width * FRAMES for name in ("header", "depth")):
        raise ValueError("clock: decoded witness row count differs")
    eyes = ("left", "right") if mode == "sbs" else ("mono",)
    checked, largest_error = 0, 0
    for frame in range(FRAMES):
        for index, eye in enumerate(eyes):
            offset = frame * width + index * WIDTH
            row = observation["header"][offset:offset + WIDTH]
            bits = [int(row[HEADER_X + i * CELL + 16] > 128) for i in range(22)]
            if bits != header(eye, frame):
                raise ValueError(f"eye-clock: {eye} decoded frame {frame} has wrong eye/frame/shot bits")
            row = observation["depth"][offset:offset + WIDTH]
            for centre in marker_centres(eye, frame):
                bright = [x for x in range(centre - 110, centre + 111) if row[x] > 145]
                if not bright:
                    raise ValueError(f"depth: missing {eye} marker at frame {frame}")
                error = abs((bright[0] + bright[-1]) / 2 - centre)
                largest_error = max(largest_error, error)
                if error > 1:
                    raise ValueError(f"depth: {eye} disparity marker differs at frame {frame}")
            checked += 1
    return {"status": "passed", "frames": FRAMES, "decoded_eye_frames": checked,
            "decoded_header_cells": checked * 22, "depth_marker_centres": checked * 3,
            "maximum_marker_error_px": largest_error, "cut_frames": list(CUTS),
            "duration_seconds": FRAMES / FPS, "source_frame_rate": FPS}


def seek_checks(path: Path, mode: str, directory: Path, ffmpeg: str) -> list[dict]:
    """Exercise actual decoder seeks at and around both hard cuts and the final frame."""
    results = []
    width = WIDTH * (2 if mode == "sbs" else 1)
    for frame in (0, 179, 180, 181, 359, 360, 361, 539):
        output = run([ffmpeg, "-v", "error", "-nostdin", "-threads", "1", "-ss", f"{frame / FPS:.9f}",
                      "-i", str(path), "-frames:v", "1", "-vf",
                      f"format=gray,crop=iw:1:0:{HEADER_Y}:exact=1", "-an", "-f", "rawvideo",
                      "-pix_fmt", "gray", "pipe:1"], directory / f"seek-{mode}-{frame:04d}.log")
        if len(output) != width:
            raise ValueError(f"seek: missing frame {frame}")
        for index, eye in enumerate(("left", "right") if mode == "sbs" else ("mono",)):
            row = output[index * WIDTH:(index + 1) * WIDTH]
            if [int(row[HEADER_X + i * CELL + 16] > 128) for i in range(22)] != header(eye, frame):
                raise ValueError(f"seek: {eye} wrong decoded clock at frame {frame}")
        results.append({"frame": frame, "seconds": frame / FPS, "status": "passed"})
    return results


def completion(directory: Path, code: int, checkout: Path) -> None:
    log = directory / "completion.log"
    log.write_text(f"All owned media processes have exited and been reaped; exit={code}.\nevidence: {directory}\n")
    sys.path.insert(0, str(ROOT / "scripts"))
    from town_run_retention import cleanup_completed_run
    save(directory / "cleanup.json", cleanup_completed_run("stereo-video-tools", code, log, checkout,
                                                          process_reaped=True))
