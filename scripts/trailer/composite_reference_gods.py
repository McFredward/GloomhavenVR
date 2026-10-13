#!/usr/bin/env python3
"""Isolate two overhead players in A04 while retaining the original film pixels.

This bounded, offline VFX helper uses the first generated head nod, traced
native-resolution silhouettes and original-camera registration. It never
submits inference or reads credentials. Its companion A04 helper supplies
source-only actor, weapon and moving-foreground holdouts and lossless encoders.
The added faces are raised in world space; original actors never move to fit.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import sys
import time

from composite_reference_presence import (
    ACTOR, COUNT, FPS, HEIGHT, WIDTH, Encoder, Registration, decode_first,
    foreground_holdout, interpolate, polygon_mask, save_contact, sha256, soften,
)
import cv2
import numpy as np

MODEL_START, MODEL_END = 3, 7
POSE_END, TRACK_END = 48, 57
GRIMHORN_VERTICAL_SCALE = .83
GRIMHORN_CHIN_ANCHOR = 550.

# Outlines describe only generated native mod props. No generated character,
# weapon, scenery or text can enter the straight plate.
OUTLINES = {
    "grimhorn": {
        0: [(530,134),(836,134),(833,193),(815,223),(829,248),(815,275),
            (825,292),(814,321),(804,334),(804,351),(788,365),(779,388),
            (776,435),(752,494),(728,540),(674,549),(637,539),(607,500),
            (590,462),(573,416),(563,376),(559,346),(550,315),(554,300),
            (538,292),(548,264),(539,245),(548,222),(538,200)],
        5: [(526,134),(831,134),(823,190),(811,215),(821,240),(807,263),
            (813,280),(799,311),(789,327),(784,346),(771,362),(764,389),
            (753,433),(729,478),(710,502),(663,506),(629,493),(603,463),
            (582,429),(569,391),(558,359),(548,332),(539,302),(542,286),
            (532,275),(540,248),(532,226),(540,204),(529,181)],
        9: [(500,134),(804,134),(788,157),(778,173),(785,185),(771,205),
            (775,218),(763,240),(752,255),(748,271),(736,285),(730,312),
            (718,348),(697,387),(680,411),(638,418),(608,407),(584,380),
            (565,354),(552,329),(540,307),(530,286),(520,262),(522,248),
            (513,240),(520,219),(512,202),(520,185),(510,164)],
    },
    "runeveil": {
        0: [(1019,134),(1233,134),(1267,165),(1286,192),(1291,240),
            (1289,297),(1279,357),(1264,408),(1238,449),(1204,482),
            (1166,502),(1130,508),(1096,496),(1056,468),(1028,433),
            (1006,392),(991,343),(985,293),(985,248),(992,201),(1005,162)],
        5: [(1012,134),(1287,134),(1300,164),(1301,190),(1294,230),
            (1285,278),(1272,323),(1256,366),(1227,406),(1197,433),
            (1162,454),(1128,461),(1096,451),(1068,428),(1042,397),
            (1020,363),(1005,325),(996,286),(994,247),(998,207),(1004,169)],
        9: [(1007,134),(1328,134),(1316,155),(1307,176),(1295,200),
            (1282,230),(1267,256),(1249,282),(1225,309),(1197,332),
            (1169,349),(1140,356),(1110,349),(1087,333),(1069,312),
            (1051,289),(1037,263),(1025,235),(1018,210),(1012,181),(1007,154)],
    },
    "leather": {
        0: [(179,134),(311,134),(323,157),(322,184),(336,207),(344,236),
            (351,278),(346,303),(331,312),(317,289),(309,267),(303,253),
            (303,280),(304,324),(296,351),(283,357),(271,345),(269,316),
            (267,289),(258,275),(249,284),(244,317),(238,340),(227,349),
            (212,344),(210,320),(211,291),(215,261),(202,244),(195,249),
            (181,281),(164,305),(151,305),(141,293),(136,268),(128,248),
            (129,213),(148,185),(157,159)],
        5: [(163,134),(300,134),(307,155),(306,177),(313,197),(319,227),
            (319,267),(311,283),(296,285),(283,263),(278,244),(268,229),
            (267,254),(269,287),(257,308),(244,313),(232,304),(231,279),
            (231,254),(223,244),(214,250),(208,278),(199,303),(187,316),
            (173,314),(168,293),(166,268),(167,246),(157,236),(147,245),
            (134,264),(121,278),(108,275),(101,261),(98,245),(91,224),
            (92,196),(108,172),(128,152)],
        9: [(89,134),(280,134),(282,151),(279,171),(280,184),(279,212),
            (276,238),(267,251),(253,251),(246,229),(245,215),(236,207),
            (235,225),(235,252),(224,266),(211,269),(198,260),(197,243),
            (197,221),(191,215),(182,222),(175,248),(170,269),(158,275),
            (143,271),(137,251),(133,228),(130,214),(120,209),(109,218),
            (96,235),(82,247),(70,242),(64,231),(60,217),(56,199),
            (58,180),(69,163),(82,147)],
    },
    "arcane": {
        0: [(1557,134),(1705,134),(1719,164),(1725,196),(1720,224),
            (1732,251),(1741,284),(1742,318),(1735,338),(1720,342),
            (1706,327),(1699,300),(1692,289),(1694,328),(1691,355),
            (1682,370),(1667,369),(1658,352),(1654,324),(1647,309),
            (1645,335),(1634,355),(1619,355),(1609,339),(1609,312),
            (1606,292),(1597,280),(1586,296),(1577,311),(1563,312),
            (1550,302),(1549,280),(1555,252),(1554,231),(1544,220),
            (1535,242),(1523,278),(1509,293),(1497,286),(1497,264),
            (1505,233),(1507,211),(1506,183),(1519,157)],
        5: [(1590,134),(1742,134),(1746,154),(1747,178),(1741,200),
            (1756,223),(1766,252),(1767,280),(1758,303),(1745,303),
            (1736,290),(1726,269),(1720,252),(1723,289),(1718,320),
            (1707,335),(1693,335),(1687,316),(1684,290),(1677,276),
            (1673,305),(1662,330),(1647,331),(1638,315),(1639,288),
            (1636,270),(1628,258),(1617,277),(1606,293),(1592,293),
            (1585,281),(1585,264),(1592,239),(1592,221),(1580,211),
            (1570,232),(1558,262),(1546,280),(1533,276),(1532,256),
            (1540,229),(1540,209),(1540,182),(1554,156)],
        9: [(1616,134),(1786,134),(1794,147),(1802,158),(1794,175),
            (1810,193),(1816,217),(1815,238),(1807,257),(1795,258),
            (1784,247),(1775,230),(1768,221),(1768,248),(1762,274),
            (1751,291),(1737,292),(1730,278),(1729,255),(1720,242),
            (1719,266),(1708,289),(1694,291),(1686,279),(1688,255),
            (1686,240),(1678,229),(1668,247),(1658,263),(1646,262),
            (1638,253),(1639,239),(1644,217),(1641,203),(1632,194),
            (1622,212),(1611,237),(1598,253),(1587,249),(1589,230),
            (1596,208),(1594,192),(1593,173),(1602,153)],
    },
}
RAISE = {"grimhorn": -200., "runeveil": -180., "leather": -80., "arcane": -80.}
GENERATED_HEAD_BOUNDS = {3:[1460,328,1612,530],7:[1500,297,1660,530]}
SKELETON_HEAD = {
    0: [(600,334),(611,313),(640,302),(672,312),(685,333),(684,354),
        (672,369),(644,372),(619,356)],
    12: [(542,337),(551,316),(581,305),(616,314),(639,336),(641,357),
         (629,373),(595,376),(566,360)],
    24: [(374,339),(382,315),(419,303),(461,316),(486,338),(490,361),
         (479,380),(436,389),(400,369)],
    36: [(249,284),(259,258),(301,242),(350,255),(384,282),(390,309),
         (375,337),(327,348),(282,325)],
    48: [(357,277),(372,250),(412,236),(465,250),(504,278),(514,306),
         (501,334),(450,343),(398,322)],
    57: [(442,299),(457,271),(499,253),(550,267),(578,301),(579,330),
         (562,359),(515,369),(470,349)],
}


def prop_matte(frame: np.ndarray, name: str, index: int) -> np.ndarray:
    coarse = polygon_mask(interpolate(OUTLINES[name], index))
    if name in OUTLINES:
        labels = np.full(coarse.shape, cv2.GC_BGD, np.uint8)
        outer = cv2.dilate(coarse, np.ones((15,15), np.uint8))
        inner = cv2.erode(coarse, np.ones((13,13), np.uint8))
        labels[outer>0] = cv2.GC_PR_BGD
        labels[coarse>0] = cv2.GC_PR_FGD
        if name in ("grimhorn", "runeveil"):
            labels[inner>0] = cv2.GC_FGD
        else:
            # Skin/leather and cyan print are definite foreground. An eroded
            # outline alone can accidentally force a forest gap between the
            # fingers to foreground, creating black triangular patches.
            b,g,r=cv2.split(frame.astype(np.float32))
            warm=(r>1.04*g)&(g>1.04*b)&(r>27)
            cyan=(b>1.12*r)&(g>1.12*r)&(g>55)
            # Let actual hand colors define complete rounded fingertips. The
            # old polygon is a search prior, not a hard finger boundary.
            points=interpolate(OUTLINES[name],index)
            x0,y0=np.floor(points.min(axis=0)-22).astype(int)
            x1,y1=np.ceil(points.max(axis=0)+22).astype(int)
            search=np.zeros_like(coarse)
            search[max(134,y0):min(HEIGHT,y1),max(0,x0):min(WIDTH,x1)]=255
            labels[search>0]=cv2.GC_PR_BGD
            labels[coarse>0]=cv2.GC_PR_FGD
            labels[(search>0)&(warm|cyan)] = cv2.GC_FGD
        labels[:134] = cv2.GC_BGD
        if name=="arcane":
            # The character crown must not train the hand's foreground color
            # bank in the first place; deleting it only after segmentation
            # leaves a learned halo/corner below the actual glove.
            x0,y0,x1,y1=np.rint(interpolate(GENERATED_HEAD_BOUNDS,index)).astype(int)
            labels[y0:y1,x0:x1]=cv2.GC_BGD
        cv2.grabCut(frame, labels, None, np.zeros((1,65),np.float64),
                    np.zeros((1,65),np.float64), 2, cv2.GC_INIT_WITH_MASK)
        matte = ((labels==cv2.GC_FGD)|(labels==cv2.GC_PR_FGD)).astype(np.uint8)*255
    else:
        matte = coarse
    if name in ("leather","arcane"):
        # A warm character crown below the arcane thumb can share the hand's
        # palette. Keep only the connected physical hand; never that separate
        # generated actor fragment. Preserve open spaces between fingers.
        contours,_=cv2.findContours(matte,cv2.RETR_EXTERNAL,cv2.CHAIN_APPROX_SIMPLE)
        if not contours: raise ValueError(f"No hand component for {name} frame{index}")
        selected=max(contours,key=cv2.contourArea)
        matte=np.zeros_like(matte)
        cv2.drawContours(matte,[selected],-1,255,cv2.FILLED)
        if name=="arcane":
            # Palette similarity can join the native generated character's
            # bright crown to the thumb's search region. That actor is never
            # part of a player hand; original character pixels supply it.
            x0,y0,x1,y1=np.rint(interpolate(GENERATED_HEAD_BOUNDS,index)).astype(int)
            matte[y0:y1,x0:x1]=0
            # Dark finger gaps can be merged into the palm by color fitting.
            # The traced physical silhouette is the outer limit for this
            # glove, so neither a forest corner nor a character can travel
            # with the raised hand.
            matte=cv2.min(matte,coarse)
    matte[:134] = 0
    return soften(matte, .8 if name in ("grimhorn","runeveil") else 1.1)


def translated(dx: float = 0., dy: float = 0.) -> np.ndarray:
    return np.array([[1.,0.,dx],[0.,1.,dy],[0.,0.,1.]])


def prepare_motion(generated, mattes, model_world):
    """Normalize props before measuring their independent bidirectional motion."""
    props, flows, notes = {}, {}, []
    for name in OUTLINES:
        pitch=(np.array([[1.,0.,0.],[0.,GRIMHORN_VERTICAL_SCALE,
                        GRIMHORN_CHIN_ANCHOR*(1.-GRIMHORN_VERTICAL_SCALE)],
                        [0.,0.,1.]]) if name=="grimhorn" else np.eye(3))
        union=np.zeros((HEIGHT,WIDTH),np.uint8)
        for n in range(MODEL_START,MODEL_END+1):
            matrix=translated(dy=RAISE[name]) @ pitch @ model_world[n]
            rgb=cv2.warpPerspective(generated[n],matrix,(WIDTH,HEIGHT))
            alpha=cv2.warpPerspective(mattes[n][name],matrix,(WIDTH,HEIGHT))
            rgb[alpha==0]=0
            props[name,n]=(rgb,alpha)
            union=cv2.max(union,alpha)
        x,y,w,h=cv2.boundingRect(union)
        x0,y0=max(0,x-36),max(0,y-36)
        x1,y1=min(WIDTH,x+w+36),min(HEIGHT,y+h+36)
        for n in range(MODEL_START,MODEL_END):
            a,aa=props[name,n];b,ab=props[name,n+1]
            ga=cv2.GaussianBlur(cv2.cvtColor(a[y0:y1,x0:x1],cv2.COLOR_BGR2GRAY),(5,5),.8)
            gb=cv2.GaussianBlur(cv2.cvtColor(b[y0:y1,x0:x1],cv2.COLOR_BGR2GRAY),(5,5),.8)
            forward=cv2.calcOpticalFlowFarneback(ga,gb,None,.5,4,35,5,7,1.5,0)
            backward=cv2.calcOpticalFlowFarneback(gb,ga,None,.5,4,35,5,7,1.5,0)
            flows[name,n]=((x0,y0,x1,y1),forward,backward)
            support=cv2.max(aa,ab)[y0:y1,x0:x1]>128
            magnitude=np.linalg.norm(forward,axis=2)[support]
            notes.append({"prop":name,"from_model_frame":n,"to_model_frame":n+1,
                          "roi":[x0,y0,x1,y1],"flow":"bidirectional dense Farneback",
                          "median_motion_pixels":float(np.median(magnitude)),
                          "p95_motion_pixels":float(np.percentile(magnitude,95))})
    return props,flows,notes


def interpolate_motion(props,flows,name,lower,upper,weight):
    """Motion-align endpoints, then blend premultiplied color and the same alpha.

    A straight crossfade doubles moving mouth/chin contours and printed hand
    glyphs. Both endpoint colors and silhouettes instead travel toward the
    intermediate pose before averaging; empty pixels never darken a soft edge.
    """
    if lower==upper or weight==0:
        return props[name,lower]
    a,aa=props[name,lower];b,ab=props[name,upper]
    (x0,y0,x1,y1),forward,backward=flows[name,lower]
    yy,xx=np.mgrid[:y1-y0,:x1-x0].astype(np.float32)
    ma=(xx-weight*forward[:,:,0],yy-weight*forward[:,:,1])
    mb=(xx-(1.-weight)*backward[:,:,0],yy-(1.-weight)*backward[:,:,1])
    al_a=cv2.remap(aa[y0:y1,x0:x1],*ma,cv2.INTER_LINEAR).astype(np.float32)/255.
    al_b=cv2.remap(ab[y0:y1,x0:x1],*mb,cv2.INTER_LINEAR).astype(np.float32)/255.
    premul_a=a[y0:y1,x0:x1].astype(np.float32)*(aa[y0:y1,x0:x1,None]/255.)
    premul_b=b[y0:y1,x0:x1].astype(np.float32)*(ab[y0:y1,x0:x1,None]/255.)
    color=(1.-weight)*cv2.remap(premul_a,*ma,cv2.INTER_LINEAR)+weight*cv2.remap(premul_b,*mb,cv2.INTER_LINEAR)
    opacity=(1.-weight)*al_a+weight*al_b
    rgb=np.zeros((HEIGHT,WIDTH,3),np.uint8);alpha=np.zeros((HEIGHT,WIDTH),np.uint8)
    visible=opacity>1./255.
    patch=np.zeros(color.shape,np.uint8)
    patch[visible]=np.clip(np.rint(color[visible]/opacity[visible,None]),0,255).astype(np.uint8)
    rgb[y0:y1,x0:x1]=patch
    alpha[y0:y1,x0:x1]=np.rint(opacity*255.).astype(np.uint8)
    return rgb,alpha


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--generated", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--preview-only", action="store_true")
    parser.add_argument("--preview-frames", help="Comma-separated original frame indices for a bounded PNG-only review")
    args = parser.parse_args()
    requested=set()
    if args.preview_frames:
        try: requested={int(n) for n in args.preview_frames.split(",")}
        except ValueError: parser.error("Preview frames must be comma-separated integers")
        if not args.preview_only or not requested or min(requested)<0 or max(requested)>TRACK_END:
            parser.error("Preview frames require --preview-only and indices inside the tracked original interval")
    args.output.mkdir(parents=True, exist_ok=True)
    if any((args.output/name).exists() for name in
           ("manifest.json","aligned-presence.mkv","moving-matte.mkv","composite-review.mp4")):
        parser.error("Output already contains evidence or an export; use a new versioned directory")
    started = time.monotonic()
    source = decode_first(args.source, TRACK_END+1)
    generated = decode_first(args.generated, MODEL_END+1)
    registrar = Registration()
    # The observed crop exit is complete before the original whip. Restrict
    # tracking to this verified interval rather than use newly entering
    # combatants as background features and drag props back into frame.
    source_features=[registrar.features(im,polygon_mask(interpolate(ACTOR,n)))
                     for n,im in enumerate(source)]
    camera, camera_notes = [np.eye(3)], [{"frame":0,"inliers":0,"anchor":True}]
    for n in range(1,len(source)):
        step, note = registrar.solve(source_features[n-1],source_features[n],np.eye(3),affine=True)
        camera.append(step @ camera[-1])
        camera_notes.append({"frame":n,**note,"matrix":camera[-1].tolist()})
    mattes, model_world, matching_notes = {}, {}, []
    for n in range(MODEL_START,MODEL_END+1):
        mattes[n] = {name:prop_matte(generated[n],name,n) for name in OUTLINES}
        exclude = np.maximum.reduce(list(mattes[n].values()))
        exclude = cv2.max(exclude, polygon_mask(interpolate(ACTOR,n*2.5)))
        features = registrar.features(generated[n],exclude)
        matches=[]
        for f in range(max(0,round(n*2.5)-4),min(TRACK_END,round(n*2.5)+4)+1):
            matrix,note=registrar.solve(features,source_features[f],np.eye(3))
            if not note["fallback"]:
                matches.append((note["inliers"],f,matrix,note))
        if not matches:
            raise ValueError(f"No reliable original-camera match for model frame {n}")
        _,f,matrix,note=max(matches,key=lambda item:item[0])
        model_world[n] = np.linalg.inv(camera[f]) @ matrix
        matching_notes.append({"model_frame":n,"source_frame":f,**note})
    props,flows,flow_notes=prepare_motion(generated,mattes,model_world)
    previews, preview_mattes, witnesses, preservation, motion_notes = [], [], [], [], []
    encoders=[]
    if not args.preview_only:
        encoders=[Encoder(args.output/"aligned-presence.mkv"),
                  Encoder(args.output/"moving-matte.mkv",matte=True),
                  Encoder(args.output/"composite-review.mp4",review=True)]
    capture=cv2.VideoCapture(str(args.source))
    total=(max(requested)+1 if requested else TRACK_END+1) if args.preview_only else COUNT
    try:
        for index in range(total):
            ok,original=capture.read()
            if not ok: raise ValueError(f"Source ended at frame{index}")
            plate=np.zeros_like(original)
            alpha=np.zeros((HEIGHT,WIDTH),np.uint8)
            object_ids=np.zeros_like(alpha)
            holdout=np.zeros_like(alpha)
            if index<=TRACK_END:
                phase=MODEL_START+(MODEL_END-MODEL_START)*min(1.,index/POSE_END)
                retreat=min(1.,max(0.,(index-48)/24.))
                retreat=retreat*retreat*(3.-2.*retreat)
                lower=int(np.floor(phase));upper=min(MODEL_END,lower+1);weight=phase-lower
                for name in OUTLINES:
                    rgb,world_alpha=interpolate_motion(props,flows,name,lower,upper,weight)
                    matrix=camera[index] @ translated(dy=-260.*retreat)
                    layer=cv2.warpPerspective(rgb,matrix,(WIDTH,HEIGHT))
                    matte=cv2.warpPerspective(world_alpha,matrix,(WIDTH,HEIGHT))
                    choose=matte>alpha
                    plate[choose]=layer[choose]
                    object_ids[choose]=list(OUTLINES).index(name)+1
                    alpha=cv2.max(alpha,matte)
                # V1's grass key is for hands resting low in foreground grass.
                # These wrists enter from above the canopy; source greenery at
                # that height is background, not an intrinsic hand holdout.
                holdout,holdout_note=foreground_holdout(original,index,np.zeros_like(alpha))
                if index<=57:
                    skull=soften(polygon_mask(interpolate(SKELETON_HEAD,index)),2.)
                    holdout=cv2.max(holdout,skull)
                alpha=np.rint(alpha.astype(np.float32)*(1-holdout.astype(np.float32)/255)).astype(np.uint8)
                alpha[:134]=0;alpha[946:]=0;alpha[alpha<=1]=0
                motion_notes.append({"frame":index,"model_phase":phase,
                                     "upward_withdrawal_world_pixels":260.*retreat,"holdout":holdout_note})
            support=alpha>0;plate[~support]=0
            composite=original.copy()
            weights=alpha[support].astype(np.float32)[:,None]/255
            composite[support]=np.rint(plate[support]*weights+original[support]*(1-weights)).astype(np.uint8)
            outside=int(np.count_nonzero(composite[~support]!=original[~support]))
            protected=int(np.count_nonzero(composite[holdout==255]!=original[holdout==255]))
            if outside or protected: raise AssertionError("Original source pixel preservation failed")
            preservation.append({"frame":index,"matte_pixels":int(support.sum()),
                                 "outside_matte_changed_channels":outside,
                                 "protected_foreground_changed_channels":protected,
                                 "visible_props":{name:int(np.count_nonzero(support&(object_ids==i+1)))
                                                  for i,name in enumerate(OUTLINES)}})
            if index<=TRACK_END and index%3==0:
                previews.append(composite);preview_mattes.append(cv2.cvtColor(alpha,cv2.COLOR_GRAY2BGR))
                witnesses.append(f"source{index:03d} {index/FPS:.3f}s")
            if index in (0,6,12,18,24,30,36,42,48,57,64,70,74,78,79) or index in requested:
                cv2.imwrite(str(args.output/f"qc-composite-{index:03d}.png"),composite)
                cv2.imwrite(str(args.output/f"qc-matte-{index:03d}.png"),alpha)
            for encoder,frame in zip(encoders,[plate,alpha,composite]):encoder.write(frame)
            if index%30==0:print(f"frame{index}/{total}, {support.sum()} added pixels; original preservation PASS",flush=True)
        capture.release()
        for encoder in encoders:encoder.finish()
    except BaseException:
        capture.release()
        for encoder in encoders:encoder.abort()
        raise
    finally:
        sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
        from town_run_retention import cleanup_completed_run
        completion=args.output/"completion.log"
        completion.write_text("Owned encoders and capture processes reaped.\n")
        cleanup=cleanup_completed_run("trailer-reference-gods",0 if len(preservation)==total else 1,
                                      completion,Path(__file__).resolve().parents[2],process_reaped=True)
        (args.output/"cleanup.json").write_text(json.dumps(cleanup,indent=2)+"\n")
    save_contact(previews,witnesses,args.output/"qc-dense-composite.jpg")
    save_contact(preview_mattes,witnesses,args.output/"qc-dense-matte.jpg")
    manifest={"source":str(args.source),"source_sha256":sha256(args.source),
              "generated":str(args.generated),"generated_sha256":sha256(args.generated),
              "script_sha256":sha256(Path(__file__)),"dimensions":[WIDTH,HEIGHT],
              "fps":FPS,"frames":total,"duration":total/FPS,"paid_calls":0,
              "preview_only":args.preview_only,"model_pose_range":[MODEL_START,MODEL_END],
              "world_raise_pixels":RAISE,"camera":camera_notes,"model_matches":matching_notes,
              "grimhorn_downward_foreshortening":{"vertical_scale":GRIMHORN_VERTICAL_SCALE,
                                                   "fixed_chin_world_row":GRIMHORN_CHIN_ANCHOR},
              "motion":motion_notes,"frame_preservation":preservation,
              "pose_interpolation":"bidirectional motion-aligned premultiplied RGBA; never a straight moving-pose crossfade",
              "pose_flow":flow_notes,
              "max_outside_matte_changed_channels":max(r["outside_matte_changed_channels"] for r in preservation),
              "max_protected_foreground_changed_channels":max(r["protected_foreground_changed_channels"] for r in preservation),
              "last_nonzero_matte_frame":max((r["frame"] for r in preservation if r["matte_pixels"]),default=None),
              "limits":["Shot-specific polygons and planar background registration approximate depth.",
                        "Early generated head nod is slowed over the original camera push; props withdraw upward during the whip.",
                        "Exact preservation is proved before lossy H264 review encoding."],
              "elapsed_seconds":time.monotonic()-started,"outputs":{}}
    for name in ("aligned-presence.mkv","moving-matte.mkv","composite-review.mp4"):
        p=args.output/name
        if p.exists():manifest["outputs"][name]={"path":str(p),"bytes":p.stat().st_size,"sha256":sha256(p)}
    (args.output/"manifest.json").write_text(json.dumps(manifest,indent=2)+"\n")
    print(json.dumps({k:manifest[k] for k in ("frames","last_nonzero_matte_frame","elapsed_seconds")}),flush=True)
    return 0


if __name__=="__main__":
    raise SystemExit(main())
