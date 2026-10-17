"""Move the exact approved VR wordmarks slightly in front of the cinema plane.

Only source pixels are reprojected. The original logo artwork supplies a matte,
never replacement RGB. Its fixed fire-title registration is the reviewed source
anchor used by the approved trailer; the trailer master remains untouched.
"""
from __future__ import annotations

import json
from pathlib import Path

import cv2
import numpy as np
from PIL import Image

from intro_depth import INTRO_SHA256, digest


class IntroLogoDepth:
    def __init__(self, specification: Path, logo: Path):
        self.specification = specification
        self.spec = json.loads(specification.read_text())
        if (self.spec["source_sha256"] != INTRO_SHA256
                or digest(logo) != self.spec["logo_sha256"]
                or self.spec["disparity_px"] != 12):
            raise ValueError("Logo-depth inputs differ from the reviewed approved wordmark")
        artwork = Image.open(logo).convert("RGBA")
        scale = 1730 / artwork.width
        opening = artwork.resize((1730, round(artwork.height * scale)), Image.Resampling.LANCZOS)
        canvas = Image.new("L", (1920, 1080))
        canvas.paste(opening.getchannel("A"), (95, round(451 - 64.5 * scale)))
        fire = cv2.warpAffine(np.asarray(artwork)[:, :, 3],
            np.asarray(self.spec["fire_registration"], np.float64), (1920, 1080),
            flags=cv2.INTER_LINEAR)
        self.fields = {}
        for name, alpha in (("opening-title", np.asarray(canvas)), ("fire-title", fire)):
            # Give every printed edge the same disparity, including its shadow.
            # Feather outside the artwork instead of shearing antialiased glyphs.
            solid = cv2.dilate((alpha > 1).astype(np.uint8), np.ones((19, 19), np.uint8))
            matte = cv2.GaussianBlur(solid.astype(np.float32), (11, 11), 2)
            self.fields[name] = np.ascontiguousarray(matte * 12, dtype=np.float32)

    def field(self, shot: str, index: int) -> np.ndarray | None:
        if shot not in self.fields:
            return None
        field = self.fields[shot]
        if shot == "fire-title":
            # Follow the original fire reveal; depth must not move unrelated fire
            # before the original printed wordmark has appeared.
            reveal = float(np.clip((index / 60 - 7.30) / .22, 0, 1))
            field = field * (reveal * reveal * (3 - 2 * reveal))
        return field

    def provenance(self) -> dict:
        return {"specification_sha256": digest(self.specification),
                "generator_sha256": digest(Path(__file__)),
                "disparity_px": 12, "affected_shots": list(self.fields),
                "rgb": "Only actual approved source pixels; original artwork is a matte only",
                "method": "Constant foreground plane inside padded original glyph matte; original fire reveal"}
