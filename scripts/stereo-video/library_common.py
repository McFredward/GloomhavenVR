#!/usr/bin/env python3
"""Shared media geometry and strict provenance for native-clock library conversion."""
from __future__ import annotations
import json
import math
import time
from pathlib import Path
import cv2
import numpy as np
from intro_depth import CODE_REVISION, MODEL_REVISION, WEIGHTS_SHA256, digest, save
from intro_stereo import forward_warp, audio_packets
from library_depth import receipt_matches, validate_movie, source_for

WIDTH, HEIGHT = 1920, 1080


def read_depth(directory: Path, movie: dict, shot: dict, wait: bool = False):
    receipt, target = directory / (shot['id'] + '.json'), directory / (shot['id'] + '.npz')
    deadline = time.monotonic() + (7200 if wait else 0)
    while not receipt.exists():
        if time.monotonic() >= deadline:
            raise FileNotFoundError(receipt)
        time.sleep(0.5)
    info = json.loads(receipt.read_text())
    if not receipt_matches(info, movie, shot) or digest(target) != info['depth_sha256']:
        raise ValueError('Depth frame address, profile or source provenance mismatch')
    low, high = info['normalization_p02_p98']
    if not math.isfinite(low) or not math.isfinite(high) or high <= low:
        raise ValueError('Invalid fixed shot-wide normalization')
    with np.load(target) as archive:
        data = archive['depths']
    if data.dtype != np.float16 or list(data.shape) != info['depth_shape'] or not np.isfinite(data).all():
        raise ValueError('Consumed depth array differs from its complete receipt')
    return data, info


def field_for(depth, image: np.ndarray, movie: dict, shot: dict, info=None) -> np.ndarray:
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
    for x0, y0, x1, y1 in shot.get('screen_regions', []):
        # The source has already composited these original labels. Keep the
        # observed printed panel and glyphs in one undistorted screen plane.
        field[y0:y1, x0:x1] = 0
        # Blend only outside the protected rectangle; no glyph is interpolated.
        for band in range(1, 33):
            factor = band / 33.0
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


def load_movie(manifest_path: Path, key: str) -> tuple[dict, dict]:
    manifest = json.loads(manifest_path.read_text())
    movies = [movie for movie in manifest['movies'] if movie['key'] == key]
    if len(movies) != 1 or movies[0].get('root_owned'):
        raise ValueError('Unknown, excluded or separately root-owned movie')
    validate_movie(movies[0])
    return manifest, movies[0]
