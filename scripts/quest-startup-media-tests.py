#!/usr/bin/env python3
"""Prove deterministic lossless media staging with an optional owned Intro file.

ffprobe is an independent proof tool, never a builder dependency. No source file
is changed. Native Android decoding still needs a hardware run.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import tempfile


def probe(executable, path):
    result = subprocess.run([executable, "-v", "error", "-show_format", "-show_streams", "-show_packets",
                             "-show_data_hash", "sha256", "-of", "json", str(path)],
                            capture_output=True, text=True, check=True, timeout=60)
    return json.loads(result.stdout)


def verify(original, derived):
    if [s["codec_type"] for s in derived["streams"]] != [s["codec_type"] for s in original["streams"] if s["codec_type"] != "data"]:
        raise RuntimeError("authored-track-set")
    result = []
    for kind in ("video", "audio"):
        old = next(s for s in original["streams"] if s["codec_type"] == kind)
        new = next(s for s in derived["streams"] if s["codec_type"] == kind)
        fields = ("codec_name", "codec_tag_string", "profile", "level", "width", "height", "pix_fmt", "sample_rate",
                  "channels", "channel_layout", "time_base", "start_pts", "duration_ts", "nb_frames", "extradata_hash")
        if any(old.get(key) != new.get(key) for key in fields):
            raise RuntimeError("authored-codec-or-timeline:" + kind)
        def packets(value, index):
            return [{key: p.get(key) for key in ("pts", "dts", "duration", "size", "data_hash", "flags")}
                    for p in value["packets"] if p["stream_index"] == index]
        before, after = packets(original, old["index"]), packets(derived, new["index"])
        if not before or before != after:
            raise RuntimeError("authored-payload-or-timestamps:" + kind)
        result.append({"kind": kind, "packets": len(before), "packetProofSha256": hashlib.sha256(json.dumps(before, sort_keys=True).encode()).hexdigest()})
    if original["format"]["duration"] != derived["format"]["duration"]:
        raise RuntimeError("authored-movie-duration")
    return result


def main():
    root = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--original-intro", type=Path, help="Read-only recovered original Intro MP4; optional independent packet proof.")
    parser.add_argument("--ffprobe", default=shutil.which("ffprobe"))
    args = parser.parse_args()
    subprocess.run([sys.executable, "-m", "unittest", "discover", "-s", str(root / "tests/quest-builder"),
                    "-p", "test_media.py", "-v"], check=True, cwd=root)
    if args.original_intro is None:
        print("PASS lossless media delivery fixtures; owned-file packet proof not requested")
        return
    if not args.ffprobe:
        raise SystemExit("Independent owned-file proof requires an installed ffprobe; the builder itself does not.")
    sys.path.insert(0, str(root / "tools/quest-builder"))
    import media
    import storage
    output = root / ".planning/debug/quest-startup-media"
    output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=output))
    source = args.original_intro.resolve()
    original_record = storage.record_file(source, source.name)
    target = run / "intro-android.mp4"
    record = media.stage_media(source, target, "StreamingAssets/QuestOriginalMovies/intro.mp4")
    original, derived = probe(args.ffprobe, source), probe(args.ffprobe, target)
    packets = verify(original, derived)
    if record["delivery"] != media.RECIPE:
        raise RuntimeError("Actual Intro proof expected ancillary tmcd adaptation.")
    evidence = {"schema": 1, "source": original_record, "delivery": record,
                "helperSha256": storage.digest(root / "tools/quest-builder/media.py"),
                "probeVersion": subprocess.run([args.ffprobe, "-version"], capture_output=True, text=True, check=True).stdout.splitlines()[0],
                "packets": packets, "cases": [], "boundary": "lossless container and authored AV packet/timing proof; no Android decoder or headset proof"}
    (run / "original-probe.json").write_text(json.dumps(original, indent=2))
    (run / "derived-probe.json").write_text(json.dumps(derived, indent=2))
    # Independent byte and timing controls exercise the probe comparator itself.
    data = target.read_bytes()
    for kind in ("video", "audio"):
        stream = next(s for s in derived["streams"] if s["codec_type"] == kind)
        packet = next(p for p in derived["packets"] if p["stream_index"] == stream["index"])
        changed = bytearray(data); changed[int(packet["pos"]) + int(packet["size"]) - 1] ^= 1
        path = run / (kind + "-payload-mutated.mp4"); path.write_bytes(changed)
        try:
            verify(original, probe(args.ffprobe, path))
        except RuntimeError as error:
            if str(error) != "authored-payload-or-timestamps:" + kind:
                raise
            evidence["cases"].append({"name": kind + "-payload-mutated", "rejected": str(error)})
        else:
            raise RuntimeError("Packet comparator accepted changed " + kind + " payload.")
    top, movie = media._top(target)
    moov = media._one(top, b"moov")
    track = next(a for a in media._children(movie, media._one(media._atoms(movie), b"moov"))
                 if a.kind == b"trak" and media._track(movie, a)[1] == b"vide")
    mdia = media._one(media._children(movie, track), b"mdia")
    minf = media._one(media._children(movie, mdia), b"minf")
    stbl = media._one(media._children(movie, minf), b"stbl")
    stts = media._one(media._children(movie, stbl), b"stts")
    changed = bytearray(data)
    at = moov.start + stts.start + stts.header + 12
    struct.pack_into(">I", changed, at, struct.unpack_from(">I", changed, at)[0] + 1)
    path = run / "video-timeline-mutated.mp4"; path.write_bytes(changed)
    try:
        verify(original, probe(args.ffprobe, path))
    except RuntimeError as error:
        if not str(error).startswith(("authored-codec-or-timeline:", "authored-payload-or-timestamps:")):
            raise
        evidence["cases"].append({"name": "video-timeline-mutated", "rejected": str(error)})
    else:
        raise RuntimeError("Timeline comparator accepted changed video duration.")
    try:
        verify(original, original)
    except RuntimeError as error:
        if str(error) != "authored-track-set":
            raise
        evidence["cases"].append({"name": "timecode-retained", "rejected": str(error)})
    else:
        raise RuntimeError("Track comparator accepted retained timecode.")
    if storage.record_file(source, source.name) != original_record:
        raise RuntimeError("Read-only original changed during proof.")
    (run / "results.json").write_text(json.dumps(evidence, indent=2) + "\n")
    print("PASS actual owned Intro: " + ", ".join(str(p["packets"]) + " " + p["kind"] + " packets" for p in packets)
          + "; codec/timing/audio identical; " + str(len(evidence["cases"])) + " defect controls")
    print("Media proof: " + str(run / "results.json"))


if __name__ == "__main__":
    main()
