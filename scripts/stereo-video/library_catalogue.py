#!/usr/bin/env python3
"""Read-only native movie/embedded-clip inventory and frame-bound shot evidence."""
from __future__ import annotations
import argparse, json, subprocess, hashlib
from pathlib import Path

def digest(path):
    with Path(path).open("rb") as f: return hashlib.file_digest(f,"sha256").hexdigest()

def probe(path):
    return json.loads(subprocess.check_output(["ffprobe","-v","error","-show_streams","-show_format","-of","json",str(path)]))

def scan(data_root, out):
    import UnityPy
    out.mkdir(parents=True,exist_ok=True)
    movies=[]
    for path in sorted((data_root/"StreamingAssets/Movies").rglob("*.mov")):
        key=path.relative_to(data_root/"StreamingAssets/Movies").as_posix()
        movies.append({"key":key,"kind":"external","source_relative":str(path.relative_to(data_root)),"source_sha256":digest(path),"source_bytes":path.stat().st_size,"probe":probe(path)})
    census=[]
    for asset in sorted(data_root.iterdir()):
        if not asset.is_file() or not (asset.suffix==".assets" or (asset.name.startswith("level") and "." not in asset.name)): continue
        env=UnityPy.load(str(asset)); count=0
        for obj in env.objects:
            if obj.type.name!="VideoClip": continue
            clip=obj.read(); resource=clip.m_ExternalResources
            target=out/(clip.m_Name+".mov")
            with (asset.parent/resource.m_Source).open("rb") as source:
                source.seek(resource.m_Offset); data=source.read(resource.m_Size)
            if len(data)!=resource.m_Size: raise ValueError("Truncated native embedded resource")
            target.write_bytes(data)
            movies.append({"key":"Embedded/"+clip.m_Name+".mov","kind":"embedded","asset":asset.name,"path_id":obj.path_id,"resource":resource.m_Source,"resource_offset":resource.m_Offset,"resource_size":resource.m_Size,"unity_declared_frames":clip.m_FrameCount,"source_sha256":digest(target),"source_bytes":target.stat().st_size,"probe":probe(target)})
            count+=1
        census.append({"asset":asset.name,"video_clips":count})
        del env
    result={"schema":1,"unitypy_version":UnityPy.__version__,"assets_scanned":census,"movies":movies}
    (out/"catalogue.json").write_text(json.dumps(result,indent=2)+"\n")
    return result

def analyze(path, out):
    import numpy as np
    p=probe(path); stream=next(s for s in p["streams"] if s["codec_type"]=="video")
    cmd=["ffmpeg","-hide_banner","-v","error","-threads","1","-i",str(path),"-map","0:v:0","-an","-vf","scale=160:90","-pix_fmt","gray","-fps_mode","passthrough","-f","rawvideo","-"]
    raw=subprocess.check_output(cmd)
    frames=np.frombuffer(raw,np.uint8).reshape(-1,90,160)
    # Rows that never contain source content are letterbox, not inferred geometry.
    activity=np.max(frames,axis=(0,2)); active=np.where(activity>12)[0]
    top=int(active[0])*12 if len(active) else 0; bottom=(int(active[-1])+1)*12 if len(active) else 1080
    body=frames[:,top//12:bottom//12,:].astype(np.float32)
    means=body.mean(axis=(1,2)); differences=np.zeros(len(body)); differences[1:]=np.mean(np.abs(body[1:]-body[:-1]),axis=(1,2))
    result={"source_sha256":digest(path),"frames":len(frames),"fps":stream["avg_frame_rate"],"body_rows":[top,bottom],"mean_luma":np.round(means,3).tolist(),"frame_difference":np.round(differences,3).tolist(),"largest_differences":sorted([{"frame":i,"difference":float(differences[i]),"luma":float(means[i])} for i in range(1,len(body))],key=lambda x:x["difference"],reverse=True)[:80]}
    out.parent.mkdir(parents=True,exist_ok=True);out.write_text(json.dumps(result,indent=2)+"\n")
    return result

def main():
    parser=argparse.ArgumentParser(description=__doc__); sub=parser.add_subparsers(dest="command",required=True)
    scanp=sub.add_parser("scan");scanp.add_argument("--data-root",type=Path,required=True);scanp.add_argument("--out",type=Path,required=True)
    ap=sub.add_parser("analyze");ap.add_argument("--source",type=Path,required=True);ap.add_argument("--out",type=Path,required=True)
    args=parser.parse_args()
    if args.command=="scan": result=scan(args.data_root,args.out)
    else: result=analyze(args.source,args.out)
    print(json.dumps({"command":args.command,"movies":len(result.get("movies",[])),"frames":result.get("frames")}))
    return 0

if __name__=="__main__":raise SystemExit(main())
