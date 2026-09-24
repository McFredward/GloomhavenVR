using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GloomhavenVR.Net;
using GloomhavenVR.WorldUI;
using UnityEngine;

// Export the actual final imported skin, not target markers or limb capsules.
// Offline triangle intersection checks retain the entire selected arm/torso surface.
internal static class ArmGeometry
{
    internal static void Export(Transform root, byte service, TownServiceActivityRig rig, Animation animation)
    {
        string[] args=Environment.GetCommandLineArgs();int arg=Array.IndexOf(args,"-anatomyExport");if(arg<0)return;
        rig.BeforeBodySample();root.SetPositionAndRotation(Vector3.zero,Quaternion.identity);root.localScale=Vector3.one;
        var skin=root.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r=>r.sharedMesh.subMeshCount==3&&r.enabled);
        Mesh mesh=skin.sharedMesh;var vertices=mesh.vertices;var weights=mesh.boneWeights;var bind=mesh.bindposes;
        var names=skin.bones.Select(b=>b.name).ToArray();var classes=new byte[vertices.Length];
        for(int n=0;n<vertices.Length;n++)
        {
            BoneWeight w=weights[n];float left=0,right=0,axial=0,distal=0;
            foreach(var pair in new[]{(w.boneIndex0,w.weight0),(w.boneIndex1,w.weight1),(w.boneIndex2,w.weight2),(w.boneIndex3,w.weight3)})
            {
                string name=names[pair.Item1];float value=pair.Item2;
                bool arm=name.StartsWith("UpperArm")||name.StartsWith("Forearm")||name.StartsWith("Hand")||name.StartsWith("Thumb")||name.StartsWith("Index")||name.StartsWith("Middle")||name.StartsWith("Ring")||name.StartsWith("Little");
                if(arm&&name.EndsWith(".L"))left+=value;
                if(arm&&name.EndsWith(".R"))right+=value;
                if(arm&&!name.StartsWith("UpperArm"))distal+=value;
                if(name=="Chest"||name=="Spine"||name=="Hips")axial+=value;
            }
            classes[n]=axial>.85f?(byte)3:distal>.10f&&left>.90f?(byte)1:distal>.10f&&right>.90f?(byte)2:(byte)0;
        }
        var triangles=new List<int>();var labels=new List<byte>();var used=new HashSet<int>();
        int[] original=mesh.triangles;
        for(int i=0;i<original.Length;i+=3)
        {
            int a=original[i],b=original[i+1],c=original[i+2];byte label=classes[a];
            if(label==0||classes[b]!=label||classes[c]!=label)continue;
            triangles.Add(a);triangles.Add(b);triangles.Add(c);labels.Add(label);used.Add(a);used.Add(b);used.Add(c);
        }
        int[] indices=used.OrderBy(v=>v).ToArray();var lookup=indices.Select((v,i)=>(v,i)).ToDictionary(p=>p.v,p=>p.i);
        var poses=new List<TownActivityPose>();float duration=service==1?28.6f:service==2?8f:10.2f;
        // Twelve samples per second plus every phase's complete greeting and departure.
        for(float t=0;t<duration;t+=1f/12f)poses.Add(new TownActivityPose{WorkClock=t,TransitionAge=.65f});
        foreach(float start in new[]{0f,duration*.23f,duration*.51f,duration*.79f})
        {
            var pose=new TownActivityPose{WorkClock=start,TransitionAge=.65f};
            for(int frame=0;frame<90;frame++)
            {
                if(frame==0)TownServiceActivityMotion.Engage(ref pose,true);
                if(frame==45)TownServiceActivityMotion.Engage(ref pose,false);
                pose=TownServiceActivityMotion.Advance(pose,1f/30f);poses.Add(pose);
            }
        }
        using var output=new BinaryWriter(File.Create(Path.Combine(args[arg+1],"service"+service+"-skin.bin")));
        output.Write(indices.Length);output.Write(labels.Count);output.Write(poses.Count);
        for(int i=0;i<labels.Count;i++){output.Write(labels[i]);for(int v=0;v<3;v++)output.Write(lookup[triangles[i*3+v]]);}
        var matrices=new Matrix4x4[skin.bones.Length];
        foreach(var pose in poses)
        {
            rig.BeforeBodySample();animation.Stop();var idle=animation["Idle"];idle.enabled=true;idle.weight=1;idle.time=pose.WorkClock;animation.Sample();idle.enabled=false;rig.Apply(in pose);
            output.Write(pose.WorkClock);output.Write(TownServiceActivityMotion.Blend(in pose));
            for(int i=0;i<matrices.Length;i++)matrices[i]=root.worldToLocalMatrix*skin.bones[i].localToWorldMatrix*bind[i];
            foreach(int i in indices)
            {
                BoneWeight w=weights[i];Vector3 v=vertices[i];
                Vector3 p=matrices[w.boneIndex0].MultiplyPoint3x4(v)*w.weight0+matrices[w.boneIndex1].MultiplyPoint3x4(v)*w.weight1+matrices[w.boneIndex2].MultiplyPoint3x4(v)*w.weight2+matrices[w.boneIndex3].MultiplyPoint3x4(v)*w.weight3;
                output.Write(p.x);output.Write(p.y);output.Write(p.z);
            }
        }
        rig.BeforeBodySample();Console.WriteLine("Actual anatomy export service="+service+" vertices="+indices.Length+" triangles="+labels.Count+" poses="+poses.Count);
    }
}
