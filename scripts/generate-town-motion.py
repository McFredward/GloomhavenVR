#!/usr/bin/env python3
"""Generate bounded NPC occupations with Kimodo's documented empty-text/pose conditioning.

Run in an isolated Kimodo environment. No service credentials, paid APIs, or model
weights are distributed. The retained NPZ output and JSON receipts are authoring evidence.
"""
import argparse, hashlib, json, time
from pathlib import Path
import numpy as np
import torch
from kimodo import load_model
from kimodo.constraints import FullBodyConstraintSet, EndEffectorConstraintSet, Root2DConstraintSet

class EmptyText:
    """Exact empty-text representation used by Kimodo._generate; never accepts text."""
    def __call__(self, texts):
        if any(text.strip() for text in texts):
            raise ValueError('Pose-only authoring does not provide a text encoder')
        return torch.zeros(len(texts), 1, 4096), [1] * len(texts)
    def to(self, *args, **kwargs): return self
    def eval(self): return self

# Positions are station-space palm/coin contacts. Source skeleton faces +Z; the
# station faces -Z with the actor at Z=.65. Left stays positive in contact space.
PROFILES = {
 'merchant-count': {'seconds':3.6, 'seed':54801, 'left':[
     (0,(.22,1.12,.39)),(.65,(.27,.970,.35)),(1.12,(.27,.970,.35)),
     (1.65,(.13,1.22,.34)),(2.05,(.13,1.22,.34)),(2.75,(.08,.970,.34)),
     (3.10,(.08,.970,.34)),(3.566667,(.22,1.12,.39))],
     'right':[(0,(-.20,.98,.37)),(3.566667,(-.20,.98,.37))]},
 'merchant-return': {'seconds':3.6, 'seed':54802, 'left':[
     (0,(.22,1.12,.39)),(.65,(.08,.970,.34)),(1.12,(.08,.970,.34)),
     (1.65,(.17,1.15,.29)),(2.05,(.17,1.15,.29)),(2.75,(.27,.970,.35)),
     (3.10,(.27,.970,.35)),(3.566667,(.22,1.12,.39))],
     'right':[(0,(-.20,.98,.37)),(3.566667,(-.20,.98,.37))]},
 'enchantress': {'seconds':10.2,'seed':54803,'left':[
     (0,(.15,1.035,.39)),(1.2,(.15,1.035,.39)),(2.2,(.12,1.24,.34)),
     (3.0,(.28,1.37,.25)),(4.0,(.12,1.42,.26)),(5.2,(.20,1.10,.40)),
     (6.6,(.14,1.14,.38)),(7.7,(.09,1.29,.30)),(8.6,(.17,1.20,.35)),(10.166667,(.15,1.035,.39))],
     'right':[(0,(-.14,1.035,.40)),(1.2,(-.14,1.035,.40)),(2.2,(-.12,1.21,.32)),
     (3.0,(-.22,1.39,.29)),(4.0,(-.30,1.30,.37)),(5.2,(-.19,1.12,.40)),
     (6.6,(-.20,1.24,.30)),(7.7,(-.20,1.27,.30)),(8.6,(-.16,1.30,.33)),(10.166667,(-.14,1.035,.40))]},
 'priestess': {'seconds':8.0,'seed':54804,'left':[
     (0,(.025,1.21,.39)),(2.5,(.026,1.225,.38)),(4.7,(.025,1.20,.385)),(7.966667,(.025,1.21,.39))],
     'right':[(0,(-.025,1.21,.39)),(2.5,(-.026,1.225,.38)),(4.7,(-.025,1.20,.385)),(7.966667,(-.025,1.21,.39))]}}

def orient_arm(s, p, r, side, stage):
    """Construct an anatomically consistent key pose with two-bone reach, no scaling."""
    i,j,k=[s.bone_index[side+n] for n in ('Arm','ForeArm','Hand')]
    target=torch.tensor([stage[0],stage[1],.65-stage[2]],dtype=p.dtype)
    # Palm/coin centre is 55 mm forward of wrist; runtime applies exact mesh contact.
    target[2]-=.045
    shoulder=p[i].clone(); a=torch.linalg.vector_norm(p[j]-p[i]);b=torch.linalg.vector_norm(p[k]-p[j])
    d=target-shoulder; distance=torch.linalg.vector_norm(d).clamp(abs(a-b)+.001,a+b-.001);direction=d/torch.linalg.vector_norm(d)
    pole=torch.tensor([.35 if side=='Left' else -.35,1.05,-.08])-shoulder
    bend=pole-direction*torch.dot(pole,direction);bend/=torch.linalg.vector_norm(bend)
    along=(a*a-b*b+distance*distance)/(2*distance); elbow=shoulder+direction*along+bend*torch.sqrt((a*a-along*along).clamp(min=0))
    def align(old,new):
        v=old/torch.linalg.vector_norm(old);w=new/torch.linalg.vector_norm(new);cross=torch.linalg.cross(v,w);c=torch.dot(v,w)
        skew=torch.tensor([[0,-cross[2],cross[1]],[cross[2],0,-cross[0]],[-cross[1],cross[0],0]])
        return torch.eye(3)+skew+skew@skew/(1+c).clamp(min=1e-6)
    r[i]=align(p[j]-p[i],elbow-shoulder)@r[i];r[j]=align(p[k]-p[j],target-elbow)@r[j]
    shift=target-p[k]
    for name,index in s.bone_index.items():
        if name.startswith(side+'Hand'):p[index]+=shift
    p[j]=elbow


def generate(args):
    torch.set_num_threads(args.threads)
    model=load_model('Kimodo-SOMA-RP-v1.1',device='cpu',text_encoder=EmptyText());s=model.skeleton
    # A released generated neutral stance supplies only the starting anatomical pose.
    # Its provenance/license are retained in the receipt, not represented as mocap.
    source=args.kimodo/'kimodo/assets/demo/examples/kimodo-soma-rp/04_ee_constraint/motion.npz'
    original=np.load(source);base=torch.from_numpy(original['posed_joints'][0]).clone();base_r=torch.from_numpy(original['global_rot_mats'][0]).clone()
    base[:,[0,2]]-=base[0,[0,2]].clone()
    for name in args.clips:
        spec=PROFILES[name];n=round(spec['seconds']*30);output=args.output/name;output.mkdir(parents=True,exist_ok=True)
        if (output/'motion.npz').exists():print(name,'already generated; preserving original output',flush=True);continue
        torch.manual_seed(spec['seed'])
        p=base.clone();r=base_r.clone()
        for side,key in [('Left','left'),('Right','right')]:orient_arm(s,p,r,side,spec[key][0][1])
        constraints=[FullBodyConstraintSet(s,torch.tensor([0,n-1]),p.repeat(2,1,1),r.repeat(2,1,1,1)),
            EndEffectorConstraintSet(s,torch.arange(0,n,3),p.repeat(len(range(0,n,3)),1,1),r.repeat(len(range(0,n,3)),1,1,1),None,joint_names=['LeftFoot','RightFoot']),
            Root2DConstraintSet(s,torch.arange(0,n,3),torch.zeros(len(range(0,n,3)),2))]
        for side,key in [('Left','left'),('Right','right')]:
            frames=torch.tensor([min(n-1,round(t*30)) for t,_ in spec[key]]);kp=[];kr=[]
            for _,target in spec[key]:
                q=base.clone();v=base_r.clone();orient_arm(s,q,v,side,target);kp.append(q);kr.append(v)
            constraints.append(EndEffectorConstraintSet(s,frames,torch.stack(kp),torch.stack(kr),None,joint_names=[side+'Hand']))
        receipt={'model':'nvidia/Kimodo-SOMA-RP-v1.1','conditioning':'empty-text plus full-body/end-effector/root constraints','profile':spec,'fps':30,'steps':args.steps,'threads':args.threads,'post_processing':False,'base_pose_source':str(source),'base_pose_sha256':hashlib.sha256(source.read_bytes()).hexdigest(),'estimated_api_cost_usd':0}
        (output/'plan.json').write_text(json.dumps(receipt,indent=2)+'\n')
        started=time.time();out=model([''],[n],constraint_lst=constraints,num_denoising_steps=args.steps,num_samples=1,multi_prompt=False,post_processing=False,return_numpy=True)
        arrays={k:v for k,v in out.items() if isinstance(v,np.ndarray)}
        if not all(np.isfinite(v).all() for v in arrays.values()):raise ValueError('Nonfinite generation output')
        np.savez_compressed(output/'motion.npz',**arrays)
        receipt.update(seconds=time.time()-started,output_sha256=hashlib.sha256((output/'motion.npz').read_bytes()).hexdigest())
        (output/'receipt.json').write_text(json.dumps(receipt,indent=2)+'\n');print(name,'generated in',receipt['seconds'],'seconds',flush=True)

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--kimodo',type=Path,required=True);p.add_argument('--output',type=Path,required=True);p.add_argument('--clips',nargs='+',choices=list(PROFILES),default=list(PROFILES));p.add_argument('--steps',type=int,default=100);p.add_argument('--threads',type=int,default=4);generate(p.parse_args())
