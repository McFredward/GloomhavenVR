#!/usr/bin/env python3
"""Offline shot-wise CPU depth for the approved GloomhavenVR intro only.

Video Depth Anything code and Small weights remain external, pinned inputs.
This tool never installs packages or calls a paid inference service.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import resource
import subprocess
import sys
import time
from pathlib import Path

import cv2
import numpy as np

INTRO_SHA256 = "22fc9f48d4da6783fed379122e8c5675c4149afd9c0bc90550a9e4e22696fec5"
CODE_REVISION = "4f5ae23172ba60fd7bc11ef671cca678842c7072"
MODEL_REVISION = "256875362cff76724b920335dfb4b29dd611f66e"
WEIGHTS_SHA256 = "13379300b739e659f076a59d52e9801bd8d38c541a7e71f73bbca4dcfb013609"


def digest(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def save(path: Path, value: object) -> None:
    path.write_text(json.dumps(value, indent=2) + "\n")


def frames(path: Path, start: int, end: int, width: int) -> np.ndarray:
    capture = cv2.VideoCapture(str(path))
    try:
        if not capture.isOpened() or int(capture.get(cv2.CAP_PROP_FRAME_COUNT)) != 2931:
            raise ValueError("Expected the complete 2931-frame approved intro")
        capture.set(cv2.CAP_PROP_POS_FRAMES, start)
        images = []
        for index in range(start, end):
            okay, image = capture.read()
            if not okay:
                raise ValueError(f"Missing original frame {index}")
            image = cv2.resize(image, (width, width * 9 // 16), interpolation=cv2.INTER_AREA)
            images.append(cv2.cvtColor(image, cv2.COLOR_BGR2RGB))
        return np.stack(images)
    finally:
        capture.release()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--model-root", type=Path, required=True)
    parser.add_argument("--shots", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--input-size", type=int, default=392)
    parser.add_argument("--width", type=int, default=640)
    parser.add_argument("--threads", type=int, default=6)
    args = parser.parse_args()
    args.source, args.model_root = args.source.resolve(), args.model_root.resolve()
    if digest(args.source) != INTRO_SHA256:
        raise ValueError("Only the already replaced, approved GloomhavenVR intro is accepted")
    revision = subprocess.check_output(["git", "-C", str(args.model_root), "rev-parse", "HEAD"], text=True).strip()
    weights = args.model_root / "checkpoints/video_depth_anything_vits.pth"
    if revision != CODE_REVISION or digest(weights) != WEIGHTS_SHA256:
        raise ValueError("Depth code/checkpoint does not match the reviewed pins")
    if args.width < 320 or args.width % 32:
        raise ValueError("Depth width must be a multiple of 32, at least 320")
    args.out.mkdir(parents=True, exist_ok=True)
    sys.path.insert(0, str(args.model_root))
    import torch
    from video_depth_anything.video_depth import VideoDepthAnything

    # Importable xformers would choose a CUDA-only attention operation on CPU.
    try:
        import xformers  # noqa: F401
    except ImportError:
        pass
    else:
        raise ValueError("Use an isolated CPU environment without xformers")
    torch.set_num_threads(args.threads)
    cv2.setNumThreads(1)
    model = VideoDepthAnything(encoder="vits", features=64, out_channels=[48, 96, 192, 384])
    model.load_state_dict(torch.load(weights, map_location="cpu"), strict=True)
    model.eval()
    shots = json.loads(args.shots.read_text())["shots"]
    receipts = []
    for shot in shots:
        if shot["mode"] == "screen":
            continue
        name = shot["id"]
        target = args.out / f"{name}.npz"
        receipt = args.out / f"{name}.json"
        if target.exists() or receipt.exists():
            raise ValueError(f"Refusing to overwrite depth evidence: {target}")
        start, end = shot["start"], shot["end"]
        if not 0 <= start < end <= 2931:
            raise ValueError("Invalid original frame interval")
        began = time.monotonic()
        images = frames(args.source, start, end, args.width)
        depths, fps = model.infer_video_depth(images, 60, input_size=args.input_size,
                                             device="cpu", fp32=True)
        if depths.shape != (end - start, args.width * 9 // 16, args.width) or fps != 60:
            raise ValueError("Depth output lost original frame addresses")
        if not np.isfinite(depths).all():
            raise ValueError("Non-finite inferred depth")
        # A single normalization for the entire shot prevents per-frame pumping.
        # Exclude original letterbox rows from the normalization population.
        body = depths[:, args.width * 72 // 1024:args.width * 504 // 1024, :]
        low, high = np.percentile(body[:, ::4, ::4], (2, 98)).tolist()
        if high <= low:
            raise ValueError(f"Degenerate inferred depth in {name}")
        np.savez_compressed(target, depths=depths.astype(np.float16))
        info = {"id": name, "start": start, "end": end, "frames": end - start,
                "source_sha256": INTRO_SHA256, "code_revision": CODE_REVISION,
                "model_revision": MODEL_REVISION, "checkpoint_sha256": WEIGHTS_SHA256,
                "device": "cpu", "dtype": "float32", "input_size": args.input_size,
                "depth_shape": list(depths.shape), "normalization_p02_p98": [low, high],
                "elapsed_seconds": round(time.monotonic() - began, 3),
                "process_peak_rss_kib": resource.getrusage(resource.RUSAGE_SELF).ru_maxrss,
                "depth_sha256": digest(target), "torch": torch.__version__}
        save(receipt, info)
        receipts.append(info)
        print(json.dumps(info), flush=True)
        del images, depths, body
    save(args.out / "depth-manifest.json", {"shots": receipts, "source_sha256": INTRO_SHA256,
                                            "shot_manifest_sha256": digest(args.shots)})
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
