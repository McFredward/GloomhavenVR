#!/usr/bin/env python3
"""Locally isolate A04's reference-driven presence without replacing the film.

This is a shot-specific VFX utility, not a general segmentation model. It uses
offline OpenCV geometry/color mattes, original-frame camera registration and
explicit foreground holdouts. All images outside its soft matte are copied
from decoded source frames exactly. It never calls a model or reads secrets.

The last sharp presence frame is reprojected during the whip rather than using
the generated mask's ghost-like fade. Original Tinkerer, crossbow and entering
Earth Demon remain in front. The matte/plate are straight (not premultiplied).
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import time

for _name in ("OMP_NUM_THREADS", "OPENBLAS_NUM_THREADS", "MKL_NUM_THREADS"):
    os.environ[_name] = "4"

import cv2
import numpy as np

cv2.setNumThreads(4)
WIDTH, HEIGHT, FPS, COUNT = 1920, 1080, 60, 395
SMALL = (640, 360)
LAST_SHARP_MODEL = 24
LAST_SHARP_SOURCE = 57
TRACK_END = 79


# Clockwise outlines traced on native-resolution generated images. Polygon
# interpolation supplies topology; the source foliage holdout restores blades
# crossing those outlines. No region outside these two hands is selected.
HAND_LEFT = {
    0: [(796,447),(820,433),(861,438),(885,460),(904,487),(919,533),
        (913,549),(905,549),(894,510),(885,501),(894,550),(886,575),
        (876,575),(870,523),(860,506),(870,556),(860,577),(850,572),
        (847,524),(840,511),(842,542),(832,557),(822,551),(824,520),
        (811,496),(791,484),(788,467)],
    6: [(762,472),(789,453),(831,450),(866,474),(890,506),(905,553),
        (907,584),(896,594),(882,546),(873,531),(883,591),(880,616),
        (865,623),(857,556),(846,538),(854,597),(842,628),(829,620),
        (828,559),(816,540),(818,579),(800,594),(789,583),(792,546),
        (779,521),(755,511),(754,491)],
    12: [(552,516),(584,493),(634,493),(681,520),(716,558),(741,615),
        (753,655),(740,675),(716,609),(701,584),(714,665),(702,701),
        (685,707),(678,632),(661,592),(666,683),(643,716),(629,698),
        (633,622),(610,589),(607,652),(585,678),(569,658),(577,610),
        (553,575),(535,564),(531,542)],
    18: [(478,508),(514,478),(568,478),(622,507),(659,550),(687,614),
        (704,659),(690,681),(660,614),(647,593),(660,677),(647,716),
        (628,720),(616,636),(599,600),(602,690),(578,726),(563,708),
        (563,629),(541,598),(533,664),(510,687),(493,667),(502,617),
        (480,580),(462,565),(459,542)],
    24: [(578,510),(612,480),(664,481),(713,508),(748,554),(774,612),
        (790,660),(775,682),(746,615),(733,594),(745,679),(732,718),
        (713,721),(701,638),(684,602),(687,693),(663,729),(648,710),
        (648,632),(626,601),(618,667),(595,690),(578,670),(587,620),
        (565,583),(548,568),(547,545)],
}
HAND_RIGHT = {
    0: [(1056,481),(1084,467),(1117,470),(1150,487),(1173,514),(1188,556),
        (1195,590),(1186,603),(1175,560),(1167,548),(1180,607),(1170,635),
        (1158,637),(1148,577),(1134,553),(1149,615),(1135,640),(1122,632),
        (1117,579),(1103,555),(1108,602),(1095,620),(1081,606),(1082,565),
        (1064,538),(1047,551),(1038,540),(1044,511)],
    6: [(1050,509),(1080,486),(1124,490),(1160,510),(1187,543),(1204,590),
        (1207,625),(1196,641),(1181,592),(1171,576),(1188,643),(1175,675),
        (1161,681),(1149,611),(1132,583),(1148,656),(1133,684),(1116,673),
        (1113,617),(1095,585),(1102,641),(1086,662),(1070,646),(1072,603),
        (1051,571),(1031,586),(1021,573),(1028,537)],
    12: [(977,574),(1009,550),(1055,552),(1092,575),(1119,608),(1139,658),
        (1143,700),(1130,721),(1113,668),(1100,645),(1115,721),(1100,753),
        (1083,757),(1073,686),(1052,652),(1069,734),(1051,767),(1033,752),
        (1028,690),(1008,655),(1013,718),(994,741),(977,720),(981,675),
        (960,641),(942,655),(930,641),(939,602)],
}
HEAD_BOUNDS = {
    0:[833,176,1050,495], 6:[803,158,1052,518],
    12:[616,134,965,599], 14:[566,134,980,620],
    18:[573,134,1014,646], 22:[638,134,1077,658],
    24:[673,134,1057,628],
}
WEAPON_BOUNDS = {
    0:[1290,465,1580,622], 27:[995,495,1570,704],
    42:[575,535,1405,714], 57:[500,576,1420,770],
    64:[340,613,1210,819], 70:[470,552,1030,760],
    79:[1110,586,1460,794],
}


# Source Tinkerer silhouette. The low-left arm is handled separately by the
# original color/geometry crossbow holdout. Broad silhouette holdouts are
# deliberately source-only; they never borrow generated actor pixels.
ACTOR = {
    0: [(1415,347),(1514,324),(1590,371),(1587,464),(1650,484),(1670,540),
        (1695,576),(1650,614),(1601,594),(1645,696),(1572,785),(1480,945),
        (1310,945),(1385,769),(1361,669),(1395,602),(1379,527),(1400,475)],
    27: [(1270,284),(1395,248),(1520,282),(1560,365),(1540,475),(1600,501),
         (1701,550),(1780,623),(1790,707),(1680,689),(1650,787),(1620,945),
         (1190,945),(1220,791),(1260,717),(1250,626),(1250,542),(1230,483)],
    42: [(1120,254),(1260,225),(1400,264),(1455,358),(1415,465),(1480,509),
         (1610,545),(1720,661),(1700,805),(1560,763),(1530,880),(1510,946),
         (1040,946),(1070,783),(1090,712),(1090,635),(1070,549),(1090,458)],
    57: [(1150,255),(1290,238),(1420,276),(1470,371),(1440,452),(1540,416),
         (1640,396),(1670,451),(1600,558),(1600,750),(1540,871),(1520,946),
         (1050,946),(1080,784),(1110,710),(1110,631),(1100,527),(1120,463)],
    64: [(900,330),(1020,287),(1165,302),(1255,331),(1280,466),(1240,576),
         (1300,674),(1240,788),(1220,900),(1210,946),(730,946),(810,792),
         (820,716),(800,633),(815,565),(830,470),(865,407)],
    70: [(886,359),(971,334),(1052,363),(1110,320),(1160,343),(1160,494),
         (1115,598),(1140,690),(1100,778),(1080,892),(1050,946),(790,946),
         (816,812),(854,741),(832,663),(835,596),(856,537),(856,436)],
    79: [(1290,420),(1370,421),(1430,470),(1450,551),(1440,630),(1500,662),
         (1470,735),(1470,822),(1500,900),(1500,946),(1230,946),(1270,845),
         (1290,788),(1280,727),(1290,681),(1310,625),(1300,578),(1280,516)],
}
DEMON_EDGE = {
    64: [0,0,80,300],
    66: [80,190,470,620],
    68: [330,520,840,1040],
    70: [510,730,1010,1270],
    72: [850,980,1320,1450],
    74: [1020,1190,1420,1540],
    79: [1260,1440,1510,1600],
}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def interpolate(table: dict, index: float) -> np.ndarray:
    keys = sorted(table)
    lower = max((k for k in keys if k <= index), default=keys[0])
    upper = min((k for k in keys if k >= index), default=keys[-1])
    a, b = np.asarray(table[lower], np.float32), np.asarray(table[upper], np.float32)
    if a.shape != b.shape:
        def resample(points):
            closed = np.vstack([points, points[:1]])
            distances = np.r_[0., np.cumsum(np.linalg.norm(np.diff(closed,axis=0),axis=1))]
            positions = np.linspace(0,distances[-1],64,endpoint=False)
            return np.column_stack([np.interp(positions,distances,closed[:,axis]) for axis in (0,1)])
        a, b = resample(a), resample(b)
    return a if lower == upper else a + (b - a) * ((index - lower) / (upper - lower))


def polygon_mask(points: np.ndarray) -> np.ndarray:
    result = np.zeros((HEIGHT, WIDTH), np.uint8)
    cv2.fillPoly(result, [np.rint(points).astype(np.int32)], 255)
    return result


def soften(mask: np.ndarray, sigma: float = 1.25) -> np.ndarray:
    # Keep support finite, so the preservation proof has an exact boundary.
    radius = max(1, int(np.ceil(sigma * 3)))
    return cv2.GaussianBlur(mask, (radius * 2 + 1, radius * 2 + 1), sigma)


def presence_matte(image: np.ndarray, index: int) -> tuple[np.ndarray, np.ndarray, dict]:
    b, g, r = cv2.split(image.astype(np.float32))
    neutral = ((r > .85 * g) & (b > .66 * g) & (np.maximum.reduce([b, g, r]) > 70)).astype(np.uint8) * 255
    search = np.zeros_like(neutral)
    x0,y0,x1,y1 = np.rint(interpolate(HEAD_BOUNDS,index)).astype(int)
    search[y0:y1,x0:x1] = neutral[y0:y1,x0:x1]
    if index>=13:
        # Remove a thin original crossbow/string connection before taking the
        # outer head hull; it must never broaden the head into a triangle.
        search = cv2.morphologyEx(search,cv2.MORPH_OPEN,np.ones((9,9),np.uint8))
    search = cv2.morphologyEx(search, cv2.MORPH_CLOSE, np.ones((5, 5), np.uint8))
    contours, _ = cv2.findContours(search, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    candidates = [c for c in contours if cv2.contourArea(c) > 12000]
    if not candidates:
        raise ValueError(f"No reliable head component in model frame {index}")
    head_contour = max(candidates, key=cv2.contourArea)
    head = np.zeros_like(neutral)
    hull = cv2.convexHull(head_contour)
    cv2.fillConvexPoly(head, hull, 255)
    # The original black letterbox never becomes an added element.
    head[:134] = 0
    head[946:] = 0
    head = soften(head, .9)
    left = polygon_mask(interpolate(HAND_LEFT, index))
    right = polygon_mask(interpolate(HAND_RIGHT, min(index, 12))) if index <= 12 else np.zeros_like(head)
    hands = soften(cv2.max(left, right), 1.25)
    return head, hands, {
        "model_frame": index,
        "head_bbox": list(cv2.boundingRect(head_contour)),
        "head_area": int(np.count_nonzero(head)),
        "hands_area": int(np.count_nonzero(hands)),
    }


def actor_silhouette(source: np.ndarray, polygon: np.ndarray) -> np.ndarray:
    """Refine a coarse source-only silhouette with local foreground colors."""
    small = cv2.resize(source,(960,540),interpolation=cv2.INTER_AREA)
    poly = cv2.resize(polygon,(960,540),interpolation=cv2.INTER_NEAREST)
    inner = cv2.erode(poly,np.ones((17,17),np.uint8))
    outer = cv2.dilate(poly,np.ones((35,35),np.uint8))
    mask = np.full(poly.shape,cv2.GC_BGD,np.uint8)
    mask[outer>0] = cv2.GC_PR_BGD
    mask[poly>0] = cv2.GC_PR_FGD
    b,g,r = cv2.split(small.astype(np.float32))
    bright = (np.maximum.reduce([b,g,r])>72)&((r>.79*g)|(b>1.10*r))
    mask[(inner>0)&bright] = cv2.GC_FGD
    background, foreground = np.zeros((1,65),np.float64),np.zeros((1,65),np.float64)
    cv2.grabCut(small,mask,None,background,foreground,2,cv2.GC_INIT_WITH_MASK)
    result = ((mask==cv2.GC_FGD)|(mask==cv2.GC_PR_FGD)).astype(np.uint8)*255
    result = cv2.morphologyEx(result,cv2.MORPH_CLOSE,np.ones((3,3),np.uint8))
    return cv2.resize(result,(WIDTH,HEIGHT),interpolation=cv2.INTER_LINEAR)


def foreground_holdout(source: np.ndarray, index: int, hands: np.ndarray) -> tuple[np.ndarray, dict]:
    b, g, r = cv2.split(source.astype(np.float32))
    actor = actor_silhouette(source,polygon_mask(interpolate(ACTOR,index)))
    # The exact source pixels select warm wood, strings, original armor and
    # pale fingers. The dark forest is not selected by this crossbow key.
    warm = ((r > .94 * g) & (g > 1.10 * b) & (r > 42)).astype(np.uint8) * 255
    white = ((r > .95 * g) & (g >= b) & (r > 120)).astype(np.uint8) * 255
    weapon = cv2.max(warm, white)
    weapon = cv2.dilate(weapon, np.ones((3, 3), np.uint8))
    x0,y0,x1,y1 = np.rint(interpolate(WEAPON_BOUNDS,index)).astype(int)
    weapon_roi = np.zeros_like(weapon)
    weapon_roi[y0:y1,x0:x1] = 255
    weapon[weapon_roi==0] = 0
    # Green blades in front of hands retain the original foreground shape.
    grass = ((g > 1.14 * r) & (g > 1.15 * b) & (g > 24)).astype(np.uint8) * 255
    grass[hands == 0] = 0
    demon = np.zeros_like(actor)
    if index >= 64:
        edge = interpolate(DEMON_EDGE, index)
        points = np.array([(0,134),(edge[0],134),(edge[1],350),
                           (edge[2],650),(edge[3],946),(0,946)], np.float32)
        region = polygon_mask(points)
        # Blurred near foliage and the entering demon are selected from the
        # actual source image, not by applying the straight polygon as a wipe.
        near = ((g>1.09*r)&(g>1.08*b)&(g>55)).astype(np.uint8)*255
        near[region==0] = 0
        near = cv2.morphologyEx(near,cv2.MORPH_CLOSE,np.ones((13,13),np.uint8))
        labels,names,stats,_ = cv2.connectedComponentsWithStats(near,8)
        demon = np.zeros_like(near)
        for component in range(1,labels):
            if stats[component,cv2.CC_STAT_AREA]>1500:
                demon[names==component] = 255
        demon = soften(demon,9+max(0,index-64)*1.2)
        # Once the Earth Demon genuinely fills the foreground, an original
        # source color segmentation retains its dark interior as well.
        if index>=72:
            refined = actor_silhouette(source,region)
            demon = cv2.max(demon,soften(refined,12.))
    holdout = cv2.max(cv2.max(actor, weapon), cv2.max(grass, demon))
    holdout[:134] = 255
    holdout[946:] = 255
    holdout = soften(holdout,1.1 if index<58 else min(13.,1.1+(index-58)*.65))
    if index>=68:
        # The fast source whip has a broad swept foreground silhouette. Expand
        # that segmented silhouette (never a polygon wipe) along its blurred
        # leading edge so a mathematically faint trailing halo cannot survive
        # the complete physical occlusion into the next shot.
        swept_radius = min(50,(index-68)*5)
        if swept_radius:
            holdout = cv2.dilate(holdout,np.ones((swept_radius*2+1,swept_radius*2+1),np.uint8))
    holdout[:134] = 255
    holdout[946:] = 255
    return holdout, {"actor_pixels": int(np.count_nonzero(actor)),
                     "weapon_pixels": int(np.count_nonzero(weapon)),
                     "demon_pixels": int(np.count_nonzero(demon))}


class Registration:
    def __init__(self):
        self.sift = cv2.SIFT_create(nfeatures=2200, contrastThreshold=.02)
        self.matcher = cv2.BFMatcher()
        self.model_features = {}
        self.source_features = {}

    def features(self, image: np.ndarray, exclude: np.ndarray | None = None):
        small = cv2.resize(image, SMALL, interpolation=cv2.INTER_AREA)
        grey = cv2.cvtColor(small, cv2.COLOR_BGR2GRAY)
        mask = np.full((360, 640), 255, np.uint8)
        mask[:45] = 0
        mask[315:] = 0
        if exclude is not None:
            mask[cv2.resize(exclude, SMALL, interpolation=cv2.INTER_NEAREST) > 0] = 0
        return self.sift.detectAndCompute(grey, mask)

    def solve(self, source_features, target_features, fallback: np.ndarray, affine: bool = False):
        key_a, desc_a = source_features
        key_b, desc_b = target_features
        if desc_a is None or desc_b is None:
            return fallback.copy(), {"fallback": True, "inliers": 0}
        pairs = self.matcher.knnMatch(desc_a, desc_b, k=2)
        good = [a for pair in pairs if len(pair) == 2 for a, b in [pair] if a.distance < .72 * b.distance]
        if len(good) < 8:
            return fallback.copy(), {"fallback": True, "inliers": len(good)}
        a = np.float32([key_a[m.queryIdx].pt for m in good])
        b = np.float32([key_b[m.trainIdx].pt for m in good])
        if affine:
            partial,accepted = cv2.estimateAffinePartial2D(a,b,method=cv2.RANSAC,ransacReprojThreshold=2.,maxIters=3000)
            matrix = np.vstack([partial,[0,0,1]]) if partial is not None else None
        else:
            matrix, accepted = cv2.findHomography(a, b, cv2.RANSAC, 2.0)
        if matrix is None or accepted is None or int(accepted.sum()) < 20:
            return fallback.copy(), {"fallback": True, "inliers": 0}
        transformed = cv2.perspectiveTransform(a[:,None,:], matrix)[:,0,:]
        residual = np.linalg.norm(transformed - b, axis=1)[accepted[:,0] > 0]
        # Reject catastrophic projective warps despite a few accidental matches.
        determinant = float(np.linalg.det(matrix[:2,:2]))
        if not .015 < abs(determinant) < 15:
            return fallback.copy(), {"fallback": True, "inliers": int(accepted.sum())}
        scale = np.diag([3., 3., 1.])
        full = scale @ matrix @ np.linalg.inv(scale)
        full /= full[2,2]
        return full, {"fallback": False, "inliers": int(accepted.sum()),
                       "median_error_640px": float(np.median(residual)),
                       "matrix": full.tolist()}


def decode_first(path: Path, count: int) -> list[np.ndarray]:
    capture = cv2.VideoCapture(str(path))
    images = []
    for _ in range(count):
        ok, frame = capture.read()
        if not ok:
            break
        if frame.shape != (HEIGHT, WIDTH, 3):
            raise ValueError(f"Unexpected input dimensions {frame.shape}")
        images.append(frame)
    capture.release()
    if len(images) != count:
        raise ValueError(f"Expected {count} frames, got {len(images)}")
    return images


def complete_cropped_anchor(images: list[np.ndarray], heads: list[np.ndarray], notes: list[dict]):
    """Supply only the cropped upper mask using its earlier complete image."""
    earlier = 8
    xa,ya,wa,ha = notes[earlier]["head_bbox"]
    xb,yb,wb,hb = notes[LAST_SHARP_MODEL]["head_bbox"]
    ratio = wb/wa
    matrix = np.array([[ratio,0,xb-ratio*xa],
                       [0,ratio,yb+hb-ratio*(ya+ha)], [0,0,1]],np.float64)
    expanded = cv2.warpPerspective(images[earlier],matrix,(WIDTH,HEIGHT))
    mask = cv2.warpPerspective(heads[earlier],matrix,(WIDTH,HEIGHT))
    native = images[LAST_SHARP_MODEL].copy()
    head = heads[LAST_SHARP_MODEL].copy()
    # The completion is invisible at the anchor frame because its top is
    # behind the source letterbox. It enters view only with the camera pullback.
    native[:134] = expanded[:134]
    head[:134] = mask[:134]
    for row in range(134,154):
        weight = (154-row)/20.
        foreground = mask[row]>0
        native[row,foreground] = np.rint(expanded[row,foreground]*weight + native[row,foreground]*(1-weight)).astype(np.uint8)
        head[row] = np.maximum(head[row],np.rint(mask[row]*weight).astype(np.uint8))
    return native,head,{"earlier_model_frame":earlier,"region":"cropped upper mask only",
                        "matrix":matrix.tolist()}


def save_contact(images: list[np.ndarray], labels: list[str], path: Path, columns: int = 4):
    width, height = 480, 294
    canvas = np.zeros((((len(images)+columns-1)//columns)*height, columns*width, 3), np.uint8)
    for n, (frame, label) in enumerate(zip(images, labels)):
        x, y = n % columns * width, n // columns * height
        canvas[y:y+270,x:x+width] = cv2.resize(frame, (width,270), interpolation=cv2.INTER_AREA)
        cv2.putText(canvas, label, (x+8,y+287), cv2.FONT_HERSHEY_SIMPLEX, .44, (235,235,235), 1, cv2.LINE_AA)
    cv2.imwrite(str(path), canvas, [cv2.IMWRITE_JPEG_QUALITY,92])


class Encoder:
    def __init__(self, path: Path, matte: bool = False, review: bool = False):
        self.log = path.with_suffix(path.suffix + ".ffmpeg.log").open("wb")
        command = ["ffmpeg","-hide_banner","-loglevel","warning","-y",
                   "-threads","4","-f","rawvideo","-pixel_format","gray" if matte else "bgr24",
                   "-video_size",f"{WIDTH}x{HEIGHT}","-framerate",str(FPS),"-i","pipe:0","-an"]
        command += (["-c:v","libx264","-preset","fast","-crf","17","-pix_fmt","yuv420p",
                     "-movflags","+faststart"] if review else
                    ["-c:v","ffv1","-level","3","-coder","1","-context","1",
                     "-pix_fmt","gray" if matte else "bgr0"])
        command += ["-threads","4",str(path)]
        self.process = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.DEVNULL, stderr=self.log)

    def write(self, frame: np.ndarray):
        self.process.stdin.write(np.ascontiguousarray(frame).tobytes())

    def finish(self):
        if self.process.stdin and not self.process.stdin.closed:
            self.process.stdin.close()
        code = self.process.wait()
        self.log.close()
        if code:
            raise RuntimeError(f"ffmpeg failed with exit {code}")

    def abort(self):
        if self.process.poll() is None:
            self.process.terminate()
            try:
                self.process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                self.process.kill()
                self.process.wait()
        self.log.close()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--generated", type=Path, required=True)
    parser.add_argument("--alignment", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--preview-only", action="store_true")
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    delivered = [args.output/name for name in ("aligned-presence.mkv","moving-matte.mkv","composite-review.mp4")]
    if any(path.exists() for path in delivered):
        parser.error("Output contains a previous export; use a fresh versioned output directory to preserve it")
    started = time.monotonic()
    reference = json.loads(args.alignment.read_text())
    bindings = {r["model_frame"]:r for r in reference}
    source_phase = np.array([r["matched_source_frame"] for r in reference], np.float32)
    model_phase = np.array([r["model_frame"] for r in reference], np.float32)
    generated = decode_first(args.generated, LAST_SHARP_MODEL+1)
    original = decode_first(args.source, TRACK_END+1)
    heads, hands, matte_notes = [], [], []
    for n, frame in enumerate(generated):
        head, hand, note = presence_matte(frame, n)
        heads.append(head)
        hands.append(hand)
        matte_notes.append(note)
    world_plate,world_head,completion_note = complete_cropped_anchor(generated,heads,matte_notes)
    registrar = Registration()
    for n, frame in enumerate(generated):
        exclude = cv2.max(heads[n], hands[n])
        exclude = cv2.max(exclude, polygon_mask(interpolate(ACTOR, int(source_phase[n]))))
        registrar.model_features[n] = registrar.features(frame, exclude)
    for n, frame in enumerate(original):
        registrar.source_features[n] = registrar.features(frame, polygon_mask(interpolate(ACTOR,n)))
    scale = np.diag([3.,3.,1.])
    base_matrix = scale @ np.array(bindings[LAST_SHARP_MODEL]["model_to_source_homography_640px"]) @ np.linalg.inv(scale)
    registration_notes, preservation, previews, preview_mattes = [], [], [], []
    encoders = []
    if not args.preview_only:
        encoders = [Encoder(args.output/"aligned-presence.mkv"),
                    Encoder(args.output/"moving-matte.mkv", matte=True),
                    Encoder(args.output/"composite-review.mp4", review=True)]
    capture = cv2.VideoCapture(str(args.source))
    last_world_matrix = np.eye(3)
    try:
        total = TRACK_END+1 if args.preview_only else COUNT
        for index in range(total):
            ok, source = capture.read()
            if not ok:
                raise ValueError(f"Source ended before frame {index}")
            plate = np.zeros_like(source)
            head = np.zeros((HEIGHT,WIDTH), np.uint8)
            hand = np.zeros_like(head)
            note = {"source_frame":index}
            if index <= LAST_SHARP_SOURCE:
                phase = float(np.interp(index, source_phase, model_phase))
                lower = min(int(np.floor(phase)), LAST_SHARP_MODEL)
                upper = min(lower+1, LAST_SHARP_MODEL)
                blend = phase - lower if lower != upper else 0.
                warped_images, warped_heads, warped_hands = [], [], []
                registrations = []
                for n in sorted(set([lower,upper])):
                    fallback = scale @ np.array(bindings[n]["model_to_source_homography_640px"]) @ np.linalg.inv(scale)
                    matrix, stats = registrar.solve(registrar.model_features[n], registrar.source_features[index], fallback)
                    warped_images.append(cv2.warpPerspective(generated[n], matrix, (WIDTH,HEIGHT)))
                    warped_heads.append(cv2.warpPerspective(heads[n], matrix, (WIDTH,HEIGHT)))
                    warped_hands.append(cv2.warpPerspective(hands[n], matrix, (WIDTH,HEIGHT)))
                    registrations.append({"model_frame":n, **stats})
                plate = warped_images[0] if lower == upper else cv2.addWeighted(warped_images[0],1-blend,warped_images[1],blend,0)
                head = warped_heads[0] if lower == upper else cv2.addWeighted(warped_heads[0],1-blend,warped_heads[1],blend,0)
                hand = warped_hands[0] if lower == upper else cv2.addWeighted(warped_hands[0],1-blend,warped_hands[1],blend,0)
                note.update({"model_phase":phase,"registrations":registrations,"world_anchor":False})
            elif index <= TRACK_END:
                step, stats = registrar.solve(registrar.source_features[index-1],
                                               registrar.source_features[index], np.eye(3),affine=True)
                last_world_matrix = step @ last_world_matrix
                matrix = last_world_matrix @ base_matrix
                plate = cv2.warpPerspective(world_plate,matrix,(WIDTH,HEIGHT))
                head = cv2.warpPerspective(world_head,matrix,(WIDTH,HEIGHT))
                hand = cv2.warpPerspective(hands[LAST_SHARP_MODEL],matrix,(WIDTH,HEIGHT))
                # Motion blur derives from ORIGINAL camera displacement between
                # frames; a short finite Gaussian matches the blurred source.
                sigma = min(17., max(0., (index-LAST_SHARP_SOURCE)*1.1))
                if sigma > .5:
                    plate = cv2.GaussianBlur(plate,(0,0),sigma)
                    head, hand = soften(head,sigma), soften(hand,sigma)
                note.update({"world_anchor":True,"registration":stats,"matrix":matrix.tolist(),"blur_sigma":sigma})
            if index <= TRACK_END:
                holdout, holdout_note = foreground_holdout(source,index,hand)
                alpha = cv2.max(head,hand)
                alpha = np.rint(alpha.astype(np.float32)*(1.-holdout.astype(np.float32)/255.)).astype(np.uint8)
                alpha[alpha<=1] = 0
                note["holdout"] = holdout_note
            else:
                alpha = np.zeros((HEIGHT,WIDTH), np.uint8)
            # One exact quantized alpha is used for both the plate and proof.
            support = alpha > 0
            plate[~support] = 0
            composite = source.copy()
            weights = alpha[support].astype(np.float32)[:,None]/255.
            composite[support] = np.rint(plate[support]*weights + source[support]*(1-weights)).astype(np.uint8)
            outside_changes = int(np.count_nonzero(composite[~support] != source[~support]))
            protected = (holdout==255) if index<=TRACK_END else ~support
            protected_changes = int(np.count_nonzero(composite[protected] != source[protected]))
            if outside_changes or protected_changes:
                raise AssertionError("Original pixels changed outside matte or inside foreground holdout")
            preservation.append({"frame":index,"matte_pixels":int(support.sum()),
                                 "outside_matte_changed_channels":outside_changes,
                                 "protected_foreground_changed_channels":protected_changes})
            if index <= TRACK_END:
                registration_notes.append(note)
                if index % 3 == 0 or index in (LAST_SHARP_SOURCE,64,67,69,72,74,79):
                    previews.append(composite)
                    preview_mattes.append(cv2.cvtColor(alpha,cv2.COLOR_GRAY2BGR))
                if index in (0,12,27,42,57,60,64,67,69,72,74,79):
                    cv2.imwrite(str(args.output/f"qc-composite-{index:03d}.png"),composite)
                    cv2.imwrite(str(args.output/f"qc-matte-{index:03d}.png"),alpha)
                    outline = source.copy()
                    contours,_ = cv2.findContours(alpha,cv2.RETR_EXTERNAL,cv2.CHAIN_APPROX_SIMPLE)
                    cv2.drawContours(outline,contours,-1,(50,50,255),2)
                    cv2.imwrite(str(args.output/f"qc-outline-{index:03d}.jpg"),outline,[cv2.IMWRITE_JPEG_QUALITY,93])
            if encoders:
                encoders[0].write(plate)
                encoders[1].write(alpha)
                encoders[2].write(composite)
            if index % 30 == 0:
                print(f"frame {index}/{total}: matte {support.sum()} pixels, original preservation PASS",flush=True)
        capture.release()
        for encoder in encoders:
            encoder.finish()
    except BaseException:
        capture.release()
        for encoder in encoders:
            encoder.abort()
        raise
    labels = [f"source {n['frame']:03d}, {n['frame']/FPS:.3f}s" for n in preservation
              if n['frame']<=TRACK_END and (n['frame']%3==0 or n['frame'] in (57,64,67,69,72,74,79))]
    save_contact(previews,labels,args.output/"qc-dense-composite.jpg")
    save_contact(preview_mattes,labels,args.output/"qc-dense-matte.jpg")
    manifest = {
        "source":str(args.source),"source_sha256":sha256(args.source),
        "generated":str(args.generated),"generated_sha256":sha256(args.generated),
        "alignment":str(args.alignment),"alignment_sha256":sha256(args.alignment),
        "script_sha256":sha256(Path(__file__)),
        "dimensions":[WIDTH,HEIGHT],"fps":FPS,"frames":total,"duration":total/FPS,
        "preview_only":args.preview_only,"paid_calls":0,"threads":4,
        "matte_format":"8-bit gray; 0 source, 255 straight plate; soft edge",
        "plate_format":"lossless FFV1 BGR, straight colors; black outside matte",
        "audio":"none; original shot audio is added by final original-film assembler",
        "max_outside_matte_changed_channels":max(n['outside_matte_changed_channels'] for n in preservation),
        "max_protected_foreground_changed_channels":max(n['protected_foreground_changed_channels'] for n in preservation),
        "last_nonzero_matte_frame":max((n['frame'] for n in preservation if n['matte_pixels']),default=None),
        "after_presence":"all decoded original source pixels unchanged before lossy review encoding",
        "late_occlusion":"source-segmented moving foreground includes a 0-50px swept blur margin; one-level quantization tails are zero",
        "identity":"Runeveil mask and exactly two hands come from approved reference-driven pilot; original actor pixels remain source",
        "limitations":["shot-specific manual hand/foreground outlines, not a general segmentation system",
                       "planar background registration approximates added object's forest depth",
                       "last sharp plate holds rigid expression during the short original whip",
                       "review H264 is lossy; pixel preservation is proved before review encoding"],
        "matte_notes":matte_notes,"frame_preservation":preservation,
        "upper_head_completion":completion_note,
        "registration_notes":registration_notes,"elapsed_seconds":time.monotonic()-started,
        "outputs":{},
    }
    for name in ("aligned-presence.mkv","moving-matte.mkv","composite-review.mp4"):
        path=args.output/name
        if path.exists() and not args.preview_only:
            manifest["outputs"][name]={"path":str(path),"bytes":path.stat().st_size,"sha256":sha256(path)}
    (args.output/"manifest.json").write_text(json.dumps(manifest,indent=2)+"\n")
    print(json.dumps({k:manifest[k] for k in ("frames","last_nonzero_matte_frame",
                      "max_outside_matte_changed_channels","elapsed_seconds")}),flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
