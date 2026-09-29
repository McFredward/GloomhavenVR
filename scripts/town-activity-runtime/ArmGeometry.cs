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
        int selected=Array.IndexOf(args,"-anatomyService");if(selected>=0&&int.Parse(args[selected+1])!=service)return;
        rig.BeforeBodySample();root.SetPositionAndRotation(Vector3.zero,Quaternion.identity);root.localScale=Vector3.one;
        var skin=root.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r=>r.sharedMesh.subMeshCount==3&&r.enabled);
        Mesh mesh=skin.sharedMesh;var vertices=mesh.vertices;var weights=mesh.boneWeights;var bind=mesh.bindposes;
        Vector3[] Shape(string name,out int index,out float frameWeight)
        {
            index=mesh.GetBlendShapeIndex(name);frameWeight=100f;
            var delta=new Vector3[vertices.Length];
            if(index>=0)
            {
                int frame=mesh.GetBlendShapeFrameCount(index)-1;
                frameWeight=mesh.GetBlendShapeFrameWeight(index,frame);
                mesh.GetBlendShapeFrameVertices(index,frame,delta,new Vector3[vertices.Length],new Vector3[vertices.Length]);
            }
            return delta;
        }
        Vector3[] visitorLeft=Shape("MerchantAttentionClearanceL",out int visitorLeftIndex,out float visitorLeftFrame);
        Vector3[] visitorRight=Shape("MerchantAttentionClearanceR",out int visitorRightIndex,out float visitorRightFrame);
        var names=skin.bones.Select(b=>b.name).ToArray();var classes=new byte[vertices.Length];
        var handWeights=new float[vertices.Length];
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
                if(name.StartsWith("Hand")||name.StartsWith("Thumb")||name.StartsWith("Index")
                    ||name.StartsWith("Middle")||name.StartsWith("Ring")||name.StartsWith("Little"))
                    handWeights[n]+=value;
                if(name=="Chest"||name=="Spine"||name=="Hips")axial+=value;
            }
            classes[n]=axial>.85f?(byte)3:distal>.10f&&left>.90f?(byte)1:distal>.10f&&right>.90f?(byte)2:(byte)0;
        }
        var triangles=new List<int>();var labels=new List<byte>();var materials=new List<byte>();var used=new HashSet<int>();
        int[] original=mesh.triangles;
        int bodyFaces=mesh.GetTriangles(0).Length/3;
        int headFaces=mesh.GetTriangles(1).Length/3;
        for(int i=0;i<original.Length;i+=3)
        {
            int a=original[i],b=original[i+1],c=original[i+2];byte label=classes[a];
            if(label==0||classes[b]!=label||classes[c]!=label)continue;
            triangles.Add(a);triangles.Add(b);triangles.Add(c);labels.Add(label);
            materials.Add((byte)(i/3<bodyFaces?0:i/3<bodyFaces+headFaces?1:2));
            used.Add(a);used.Add(b);used.Add(c);
        }
        File.WriteAllLines(Path.Combine(args[arg+1],"service"+service+"-triangle-materials.csv"),
            materials.Select((material,index)=>index+","+material));
        int[] indices=used.OrderBy(v=>v).ToArray();var lookup=indices.Select((v,i)=>(v,i)).ToDictionary(p=>p.v,p=>p.i);
        string Scalar(float value)=>value.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
        string MatrixValues(Matrix4x4 m)=>string.Join(",",new[]{Scalar(m.m00),Scalar(m.m01),Scalar(m.m02),Scalar(m.m03),
            Scalar(m.m10),Scalar(m.m11),Scalar(m.m12),Scalar(m.m13),Scalar(m.m20),Scalar(m.m21),Scalar(m.m22),Scalar(m.m23),
            Scalar(m.m30),Scalar(m.m31),Scalar(m.m32),Scalar(m.m33)});
        File.WriteAllLines(Path.Combine(args[arg+1],"service"+service+"-source-vertices.csv"),
            new[]{"skinIndex,meshIndex,x,y,z,b0,w0,b1,w1,b2,w2,b3,w3"}.Concat(indices.Select((meshIndex,skinIndex)=>
            {
                BoneWeight w=weights[meshIndex];Vector3 v=vertices[meshIndex];
                return string.Join(",",new[]{skinIndex.ToString(),meshIndex.ToString(),Scalar(v.x),Scalar(v.y),Scalar(v.z),
                    w.boneIndex0.ToString(),Scalar(w.weight0),w.boneIndex1.ToString(),Scalar(w.weight1),
                    w.boneIndex2.ToString(),Scalar(w.weight2),w.boneIndex3.ToString(),Scalar(w.weight3)});
            })));
        File.WriteAllLines(Path.Combine(args[arg+1],"service"+service+"-bindposes.csv"),
            new[]{"boneIndex,name,"+string.Join(",",Enumerable.Range(0,16).Select(i=>"m"+i))}.Concat(
                Enumerable.Range(0,bind.Length).Select(i=>i+","+names[i]+","+MatrixValues(bind[i]))));
        // Preserve the exact imported hand/finger vertices for a surface-area
        // contact measurement. Nearest arm/coat points alone once reported a
        // 6 mm gap while the visible palms hovered 6-8 cm above the belly.
        File.WriteAllLines(Path.Combine(args[arg+1],"service"+service+"-hands.csv"),
            indices.Where(i=>handWeights[i]>.85f&&(classes[i]==1||classes[i]==2))
                .Select(i=>classes[i]+","+lookup[i]));
        var seams = new List<(int cloth, int skin, float gap)>();
        int[] clothVertices=mesh.GetTriangles(0).Distinct().ToArray(), skinVertices=mesh.GetTriangles(2).Distinct().ToArray();
        foreach(string side in new[]{"L","R"})
        {
            int handBone=Array.IndexOf(names,"Hand."+side), indexBone=Array.IndexOf(names,"Index1."+side);
            Vector3 wrist=bind[handBone].inverse.MultiplyPoint3x4(Vector3.zero);
            int foreBone=Array.IndexOf(names,"Forearm."+side);
            float unit=Vector3.Distance(root.InverseTransformPoint(skin.bones[handBone].position),root.InverseTransformPoint(skin.bones[foreBone].position))
                / Vector3.Distance(wrist,bind[foreBone].inverse.MultiplyPoint3x4(Vector3.zero));
            Vector3 forward=(bind[indexBone].inverse.MultiplyPoint3x4(Vector3.zero)-wrist).normalized;
            int[] candidates=skinVertices.Where(i=>lookup.ContainsKey(i)&&Vector3.Distance(vertices[i],wrist)*unit<.10f).ToArray();
            Console.WriteLine("Seam import service="+service+" side="+side+" unit="+unit+" wrist="+wrist.ToString("F6")+" candidates="+candidates.Length+" bounds="+mesh.bounds);
            foreach(int i in clothVertices)
            {
                Vector3 delta=vertices[i]-wrist;float along=Vector3.Dot(delta,forward)*unit;
                if(!lookup.ContainsKey(i)||along<-.085f||along>.026f||delta.magnitude*unit>.12f)continue;
                int nearest=-1;float gap=.04f;
                foreach(int j in candidates){float distance=Vector3.Distance(vertices[i],vertices[j])*unit;if(distance<gap){gap=distance;nearest=j;}}
                if(nearest>=0)seams.Add((lookup[i],lookup[nearest],gap));
            }
        }
        if(seams.Count<20)throw new InvalidOperationException("Imported cuff/skin seam was not covered: "+service+" pairs="+seams.Count);
        File.WriteAllLines(Path.Combine(args[arg+1],"service"+service+"-seams.csv"),seams.Select(p=>p.cloth+","+p.skin+","+p.gap.ToString("R",System.Globalization.CultureInfo.InvariantCulture)));
        var poses=new List<(TownActivityPose state,float cover,float blessingAge)>();
        var visualOverrides=new Dictionary<int,TownActivityVisual>();
        float duration=service==2?64f:96f;
        bool focused=Array.IndexOf(args,"-anatomyFocus")>=0;
        // Twelve samples per second plus every phase's complete greeting and departure.
        if(!focused)
        {
            for(float t=0;t<duration;t+=1f/12f)poses.Add((new TownActivityPose{WorkClock=t,TransitionAge=TownServiceActivityMotion.TransitionSeconds},0f,-1f));
            foreach(float start in new[]{0f,duration*.23f,duration*.51f,duration*.79f})
            {
                var pose=new TownActivityPose{WorkClock=start,TransitionAge=TownServiceActivityMotion.TransitionSeconds};
                for(int frame=0;frame<90;frame++)
                {
                    if(frame==0)TownServiceActivityMotion.Engage(ref pose,true);
                    if(frame==45)TownServiceActivityMotion.Engage(ref pose,false);
                    pose=TownServiceActivityMotion.Advance(pose,1f/30f);poses.Add((pose,0f,-1f));
                }
            }
            var interrupted=new TownActivityPose{WorkClock=duration*.51f,TransitionAge=TownServiceActivityMotion.TransitionSeconds};
            for(int frame=0;frame<90;frame++)
            {
                if(frame==0||frame==16)TownServiceActivityMotion.Engage(ref interrupted,true);
                if(frame==8||frame==44)TownServiceActivityMotion.Engage(ref interrupted,false);
                interrupted=TownServiceActivityMotion.Advance(interrupted,1f/30f);poses.Add((interrupted,0f,-1f));
            }
        }
        if(service==2)
        {
            // The unavailable bowl path was covered, but the ordinary
            // prayer-to-hanging-arm visit reported in the headset was missing
            // from the focused skin export. Scan both directions at 90 Hz too;
            // a pair of clean endpoints cannot establish a clean descent.
            var available=new TownActivityPose{WorkClock=5f,
                TransitionAge=TownServiceActivityMotion.TransitionSeconds};
            TownServiceActivityMotion.Engage(ref available,true);
            for(int frame=0;frame<=90;frame++)
            {
                available=TownServiceActivityMotion.Advance(available,1f/90f);
                poses.Add((available,0f,-1f));
            }
            TownServiceActivityMotion.Engage(ref available,false);
            for(int frame=0;frame<=90;frame++)
            {
                available=TownServiceActivityMotion.Advance(available,1f/90f);
                poses.Add((available,0f,-1f));
            }
            // The earlier export covered prayer and attention but never sampled the
            // unavailable bowl-cover modifier. An isolated final-pose screenshot
            // cannot catch crossed sleeves halfway through the live transition.
            var approach=new TownActivityPose{WorkClock=7f,TransitionAge=TownServiceActivityMotion.TransitionSeconds};
            TownServiceActivityMotion.Engage(ref approach,true);
            for(int frame=0;frame<=90;frame++)
            {
                approach=TownServiceActivityMotion.Advance(approach,1f/90f);
                poses.Add((approach,1f,-1f));
            }
            var donation=new TownActivityPose{WorkClock=8f,TransitionAge=TownServiceActivityMotion.TransitionSeconds,Engaged=true,FromBlend=1f};
            for(int frame=0;frame<=220;frame++)
            {
                float age=frame/90f;
                poses.Add((donation,Mathf.Clamp01(age/TownServiceActivityMotion.TransitionSeconds),age));
            }
            var departure=new TownActivityPose{WorkClock=9f,TransitionAge=TownServiceActivityMotion.TransitionSeconds,Engaged=true,FromBlend=1f};
            TownServiceActivityMotion.Engage(ref departure,false);
            for(int frame=0;frame<=90;frame++)
            {
                departure=TownServiceActivityMotion.Advance(departure,1f/90f);
                poses.Add((departure,1f,-1f));
            }
        }
        if(service==1&&focused)
        {
            var segments=new List<string> { "start,end,state" };
            // The author waits until the coin is released before greeting.
            int start=poses.Count;
            var visit=new TownActivityPose{WorkClock=3.5f,TransitionAge=TownServiceActivityMotion.TransitionSeconds};
            TownServiceActivityMotion.Engage(ref visit,true);
            for(int frame=0;frame<=90;frame++)
            {
                visit=TownServiceActivityMotion.Advance(visit,1f/90f);
                poses.Add((visit,0f,-1f));
            }
            segments.Add(start+","+(poses.Count-1)+",visit_approach");
            start=poses.Count;
            TownServiceActivityMotion.Engage(ref visit,false);
            for(int frame=0;frame<=90;frame++)
            {
                visit=TownServiceActivityMotion.Advance(visit,1f/90f);
                poses.Add((visit,0f,-1f));
            }
            segments.Add(start+","+(poses.Count-1)+",visit_withdrawal");
            start=poses.Count;
            // The right arm must leave the coat without carrying the visitor
            // shape into the offered-card position, then return continuously.
            var sale=new TownActivityPose{WorkClock=4f,TransitionAge=TownServiceActivityMotion.TransitionSeconds,
                Engaged=true,FromBlend=1f};
            for(int frame=0;frame<=90;frame++)
            {
                sale.WorkClock=4f+frame/90f;
                TownActivityVisual offered=TownServiceActivityMotion.Visual(1,in sale);
                float blend=frame<=45?Mathf.SmoothStep(0f,1f,frame/45f)
                    :Mathf.SmoothStep(0f,1f,(90f-frame)/45f);
                TownServiceActivityMotion.ApplyMerchantOffering(ref offered,blend);
                visualOverrides.Add(poses.Count,offered);
                poses.Add((sale,0f,-1f));
            }
            segments.Add(start+","+(poses.Count-1)+",card_offering_and_return");
            start=poses.Count;
            // A new authority may have already finished that coin while the old
            // peer last displayed it in a pinch. Exercise the actual network
            // handover blend rather than forcing an impossible local greeting.
            var held=new TownActivityPose{WorkClock=2f,TransitionAge=TownServiceActivityMotion.TransitionSeconds};
            var ready=new TownActivityPose{WorkClock=3.975f,TransitionAge=TownServiceActivityMotion.TransitionSeconds,
                Engaged=true,FromBlend=1f};
            TownActivityVisual from=TownServiceActivityMotion.Visual(1,in held);
            TownActivityVisual to=TownServiceActivityMotion.Visual(1,in ready);
            var handover=new TownServiceActivityHandover();
            var face=default(TownFacePose);
            handover.Sample(1,1,0f,in from,in face,out _,out _);
            for(int frame=0;frame<=90;frame++)
            {
                handover.Sample(2,2,1f/90f,in to,in face,out TownActivityVisual shown,out _);
                int index=poses.Count;
                poses.Add((new TownActivityPose{WorkClock=Mathf.Lerp(2f,3.975f,frame/90f),
                    TransitionAge=TownServiceActivityMotion.TransitionSeconds},0f,-1f));
                visualOverrides.Add(index,shown);
            }
            segments.Add(start+","+(poses.Count-1)+",coin_author_handover");
            start=poses.Count;
            // A visitor may also arrive before the first coin is picked up.
            // Idle samples a different torso phase there, so the same authored
            // belly-rest pose must keep contact without cutting through the coat.
            var early=new TownActivityPose{WorkClock=.15f,
                TransitionAge=TownServiceActivityMotion.TransitionSeconds};
            TownServiceActivityMotion.Engage(ref early,true);
            for(int frame=0;frame<=90;frame++)
            {
                early=TownServiceActivityMotion.Advance(early,1f/90f);
                poses.Add((early,0f,-1f));
            }
            segments.Add(start+","+(poses.Count-1)+",early_visit_approach");
            start=poses.Count;
            TownServiceActivityMotion.Engage(ref early,false);
            for(int frame=0;frame<=90;frame++)
            {
                early=TownServiceActivityMotion.Advance(early,1f/90f);
                poses.Add((early,0f,-1f));
            }
            segments.Add(start+","+(poses.Count-1)+",early_visit_withdrawal");
            start=poses.Count;
            // The imported Idle torso continues beneath an attentive visitor.
            // Sweep more than two arrival clocks so a posed hand cannot be
            // validated against one convenient coat sample alone.
            for(int frame=0;frame<=256;frame++)
                poses.Add((new TownActivityPose{WorkClock=frame*.25f,Engaged=true,FromBlend=1f,
                    TransitionAge=TownServiceActivityMotion.TransitionSeconds},0f,-1f));
            segments.Add(start+","+(poses.Count-1)+",attentive_phase_sweep");
            File.WriteAllLines(Path.Combine(args[arg+1],"service1-segments.csv"),segments);
        }
        using var handMarkers=new StreamWriter(Path.Combine(args[arg+1],"service"+service+"-hand-markers.csv"));
        handMarkers.WriteLine("frame,side,palmX,palmY,palmZ,indexX,indexY,indexZ,thumbX,thumbY,thumbZ,normalX,normalY,normalZ,fingerX,fingerY,fingerZ");
        using var digitMarkers=new StreamWriter(Path.Combine(args[arg+1],"service"+service+"-digit-markers.csv"));
        digitMarkers.WriteLine("frame,side,name,x,y,z");
        using var phaseFacts=new StreamWriter(Path.Combine(args[arg+1],"service"+service+"-phase-facts.csv"));
        phaseFacts.WriteLine("frame,workClock,attention,merchantCanAttend,coinGrip,leftShape,rightShape");
        using var boneFrames=new StreamWriter(Path.Combine(args[arg+1],"service"+service+"-bones.csv"));
        boneFrames.WriteLine("frame,boneIndex,name,"+string.Join(",",Enumerable.Range(0,16).Select(i=>"actorM"+i))+","+
            string.Join(",",Enumerable.Range(0,16).Select(i=>"localM"+i)));
        using var boneFramesBefore=new StreamWriter(Path.Combine(args[arg+1],"service"+service+"-bones-before.csv"));
        boneFramesBefore.WriteLine("frame,boneIndex,name,"+string.Join(",",Enumerable.Range(0,16).Select(i=>"actorM"+i))+","+
            string.Join(",",Enumerable.Range(0,16).Select(i=>"localM"+i)));
        var digits=root.GetComponentsInChildren<Transform>(true).Where(t=>
            (t.name.EndsWith(".L")||t.name.EndsWith(".R")) &&
            (new[]{"Thumb1.","Thumb2.","Index1.","Index2.","Middle1.","Middle2.",
                    "Ring1.","Ring2.","Little1.","Little2."}.Any(prefix=>t.name.StartsWith(prefix))||
             t.name.EndsWith("Pad.L")||t.name.EndsWith("Pad.R")||
             t.name.EndsWith("Tip.L")||t.name.EndsWith("Tip.R"))).ToArray();
        var palmLeft=root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="PalmContact.L");
        var palmRight=root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="PalmContact.R");
        var indexLeft=root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="IndexTip.L");
        var indexRight=root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="IndexTip.R");
        var thumbLeft=root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="ThumbTip.L");
        var thumbRight=root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="ThumbTip.R");
        using var output=new BinaryWriter(File.Create(Path.Combine(args[arg+1],"service"+service+"-skin.bin")));
        output.Write(indices.Length);output.Write(labels.Count);output.Write(poses.Count);
        for(int i=0;i<labels.Count;i++){output.Write(labels[i]);for(int v=0;v<3;v++)output.Write(lookup[triangles[i*3+v]]);}
        var matrices=new Matrix4x4[skin.bones.Length];
        int frameNumber=0;
        foreach(var (pose,cover,blessingAge) in poses)
        {
            rig.BeforeBodySample();animation.Stop();var idle=animation["Idle"];idle.enabled=true;idle.weight=1;idle.time=pose.WorkClock;animation.Sample();idle.enabled=false;
            for(int i=0;i<skin.bones.Length;i++)
            {
                Transform bone=skin.bones[i];
                Matrix4x4 actor=root.worldToLocalMatrix*bone.localToWorldMatrix;
                Matrix4x4 local=Matrix4x4.TRS(bone.localPosition,bone.localRotation,bone.localScale);
                boneFramesBefore.WriteLine(frameNumber+","+i+","+names[i]+","+MatrixValues(actor)+","+MatrixValues(local));
            }
            TownActivityVisual visual=TownServiceActivityMotion.Visual(service,in pose);
            if(visualOverrides.TryGetValue(frameNumber,out TownActivityVisual shown))visual=shown;
            if(service==2&&cover>0f)TownServiceActivityMotion.ApplyTempleAvailability(ref visual,false,cover);
            if(service==2&&blessingAge>=0f)TownServiceActivityMotion.ApplyTempleBlessing(ref visual,blessingAge);
            rig.Apply(in visual);
            float leftShapeWeight=visitorLeftIndex>=0 ? skin.GetBlendShapeWeight(visitorLeftIndex)/visitorLeftFrame : 0f;
            float rightShapeWeight=visitorRightIndex>=0 ? skin.GetBlendShapeWeight(visitorRightIndex)/visitorRightFrame : 0f;
            float coinGrip=Mathf.Max(visual.CoinGrip.x,Mathf.Max(visual.CoinGrip.y,visual.CoinGrip.z));
            phaseFacts.WriteLine(string.Join(",",new[]{frameNumber.ToString(),
                pose.WorkClock.ToString("R",System.Globalization.CultureInfo.InvariantCulture),
                visual.Attention.ToString("R",System.Globalization.CultureInfo.InvariantCulture),
                TownServiceActivityMotion.MerchantCanAttend(pose.WorkClock)?"1":"0",
                coinGrip.ToString("R",System.Globalization.CultureInfo.InvariantCulture),
                leftShapeWeight.ToString("R",System.Globalization.CultureInfo.InvariantCulture),
                rightShapeWeight.ToString("R",System.Globalization.CultureInfo.InvariantCulture)}));
            for(int i=0;i<skin.bones.Length;i++)
            {
                Transform bone=skin.bones[i];
                Matrix4x4 actor=root.worldToLocalMatrix*bone.localToWorldMatrix;
                Matrix4x4 local=Matrix4x4.TRS(bone.localPosition,bone.localRotation,bone.localScale);
                boneFrames.WriteLine(frameNumber+","+i+","+names[i]+","+MatrixValues(actor)+","+MatrixValues(local));
            }
            foreach(Transform digit in digits)
            {
                Vector3 place=root.InverseTransformPoint(digit.position);
                digitMarkers.WriteLine(string.Join(",",new[]{frameNumber.ToString(),digit.name.EndsWith(".L")?"L":"R",digit.name,
                    place.x.ToString("R",System.Globalization.CultureInfo.InvariantCulture),
                    place.y.ToString("R",System.Globalization.CultureInfo.InvariantCulture),
                    place.z.ToString("R",System.Globalization.CultureInfo.InvariantCulture)}));
            }
            foreach(var marker in new[]{("L",palmLeft,indexLeft,thumbLeft),("R",palmRight,indexRight,thumbRight)})
            {
                Vector3 palm=root.InverseTransformPoint(marker.Item2.position);
                Vector3 index=root.InverseTransformPoint(marker.Item3.position);
                Vector3 thumb=root.InverseTransformPoint(marker.Item4.position);
                Vector3 normal=root.InverseTransformDirection(marker.Item2.forward);
                Vector3 finger=root.InverseTransformDirection(marker.Item2.up);
                handMarkers.WriteLine(string.Join(",",new[]{frameNumber.ToString(),marker.Item1,
                    palm.x.ToString("R",System.Globalization.CultureInfo.InvariantCulture),
                    palm.y.ToString("R",System.Globalization.CultureInfo.InvariantCulture),
                    palm.z.ToString("R",System.Globalization.CultureInfo.InvariantCulture),
                    index.x.ToString("R",System.Globalization.CultureInfo.InvariantCulture),
                    index.y.ToString("R",System.Globalization.CultureInfo.InvariantCulture),
                    index.z.ToString("R",System.Globalization.CultureInfo.InvariantCulture),
                    thumb.x.ToString("R",System.Globalization.CultureInfo.InvariantCulture),
                    thumb.y.ToString("R",System.Globalization.CultureInfo.InvariantCulture),
                    thumb.z.ToString("R",System.Globalization.CultureInfo.InvariantCulture),
                    normal.x.ToString("R",System.Globalization.CultureInfo.InvariantCulture),
                    normal.y.ToString("R",System.Globalization.CultureInfo.InvariantCulture),
                    normal.z.ToString("R",System.Globalization.CultureInfo.InvariantCulture),
                    finger.x.ToString("R",System.Globalization.CultureInfo.InvariantCulture),
                    finger.y.ToString("R",System.Globalization.CultureInfo.InvariantCulture),
                    finger.z.ToString("R",System.Globalization.CultureInfo.InvariantCulture)}));
            }
            frameNumber++;
            output.Write(pose.WorkClock);output.Write(visual.Attention);
            for(int i=0;i<matrices.Length;i++)matrices[i]=root.worldToLocalMatrix*skin.bones[i].localToWorldMatrix*bind[i];
            foreach(int i in indices)
            {
                BoneWeight w=weights[i];Vector3 v=vertices[i]+visitorLeft[i]*leftShapeWeight+visitorRight[i]*rightShapeWeight;
                Vector3 p=matrices[w.boneIndex0].MultiplyPoint3x4(v)*w.weight0+matrices[w.boneIndex1].MultiplyPoint3x4(v)*w.weight1+matrices[w.boneIndex2].MultiplyPoint3x4(v)*w.weight2+matrices[w.boneIndex3].MultiplyPoint3x4(v)*w.weight3;
                output.Write(p.x);output.Write(p.y);output.Write(p.z);
            }
        }
        rig.BeforeBodySample();Console.WriteLine("Actual anatomy export service="+service+" vertices="+indices.Length+" triangles="+labels.Count+" poses="+poses.Count);
    }
}
