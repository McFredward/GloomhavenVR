using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using GloomhavenVR.Net;
using GloomhavenVR.WorldUI;
using UnityEngine;
internal static class ActivityRender
{
    internal static void Render(GameObject obj,byte service,TownServiceActivityRig rig)
    {
        string[] args=Environment.GetCommandLineArgs();int output=Array.IndexOf(args,"-activityRender");if(output<0)return;
        string folder=args[output+1];Transform root=obj.transform;rig.BeforeBodySample();root.SetPositionAndRotation(Vector3.zero,Quaternion.identity);root.localScale=Vector3.one;
        Shader shader=obj.GetComponentsInChildren<SkinnedMeshRenderer>(true)[0].sharedMaterial.shader;
        using var props=new TownServiceActivityProps(root,service,shader);props.SetVisibility(1);
        int book=Array.IndexOf(args,"-activityBook");if(book>=0&&service!=2)Book(root,args[book+1]);
        var coin=GameObject.CreatePrimitive(PrimitiveType.Cylinder);coin.name="Diagnostic coin contact volume (not native asset)";coin.transform.SetParent(root,false);coin.transform.localScale=new Vector3(.026f,.0015f,.026f);coin.GetComponent<Renderer>().sharedMaterial=new Material(Shader.Find("Standard")){color=new Color(.6f,.4f,.1f)};
        if(service==1)props.BindCoin(coin.transform,Vector3.zero);else coin.SetActive(false);
        var camera=new GameObject("Activity diagnostic camera").AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.14f,.17f);camera.fieldOfView=48;camera.nearClipPlane=.02f;
        var rt=new RenderTexture(1000,900,24);camera.targetTexture=rt;
        var light=new GameObject("Diagnostic stand light").AddComponent<Light>();light.type=LightType.Point;light.transform.position=new Vector3(-.4f,2.2f,-.7f);light.intensity=3;light.range=6;light.color=new Color(1,.89f,.72f);
        RenderSettings.ambientLight=new Color(.25f,.28f,.32f);RenderSettings.ambientIntensity=1;
        var block=new MaterialPropertyBlock();block.SetFloat("_TownVisibility",1);foreach(Renderer r in obj.GetComponentsInChildren<Renderer>(true))r.SetPropertyBlock(block);
        Animation animation=root.GetComponentInChildren<Animation>();
        var faceRig=new TownServiceFaceRig(root);
        using var metrics=new StreamWriter(Path.Combine(folder,"service"+service+"-contacts.csv"));metrics.WriteLine("phase,handX,handY,handZ,gripX,gripY,gripZ,tipX,tipY,tipZ");
        foreach(int phase in new[]{0,1,2,3})
        {
            faceRig.BeforeBodySample();rig.BeforeBodySample();animation.Stop();var body=animation["Idle"];body.enabled=true;body.weight=1;body.time=0;animation.Sample();body.enabled=false;
            var state=new TownActivityPose{WorkClock=phase==0?2:8,TransitionAge=.65f,FromBlend=phase==2?1:0,Engaged=phase==2};rig.Apply(in state);props.Sample(in state);
            if(phase==3)
            {
                // Normal settled work focus, using the exact production residual-angle solver
                // after the real body/activity pose. This is not an extra synthetic22° bend.
                var gaze=default(TownFacePose);Vector3 focus=root.TransformPoint(TownServiceActivityMotion.RestFocus(service));
                for(int frame=0;frame<90;frame++)gaze=TownServiceFaceMotion.Aim(faceRig.OpticalRotation,root.lossyScale.x,
                    faceRig.HeadPosition,faceRig.LeftPosition,faceRig.RightPosition,focus,in gaze,1f/90f);
                TownServiceFacePose face=TownServiceFaceMotion.Evaluate(in gaze,8f,service,Vector3.zero);faceRig.Apply(in face);
                metrics.WriteLine("# normal-work-gaze pitch="+gaze.HeadPitch.ToString("R",CultureInfo.InvariantCulture)+" yaw="+gaze.HeadYaw.ToString("R",CultureInfo.InvariantCulture));
            }
            Transform hand=root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Hand.R");Transform grip=root.Find("ActivityGripRight"),pen=root.Find("Town.ReedPen");Vector3 tip=pen!=null?pen.TransformPoint(new Vector3(0,-TownServiceActivityProps.PenTipDistance,0)):Vector3.zero;
            Transform shoulder=root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="UpperArm.R");Transform elbow=root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Forearm.R");
            metrics.WriteLine("# shoulder="+shoulder.position.ToString("F5")+" upper="+Vector3.Distance(shoulder.position,elbow.position)+" fore="+Vector3.Distance(elbow.position,hand.position));
            metrics.WriteLine(string.Join(",",new[]{(float)phase,hand.position.x,hand.position.y,hand.position.z,grip.position.x,grip.position.y,grip.position.z,tip.x,tip.y,tip.z}.Select(v=>v.ToString("R",CultureInfo.InvariantCulture))));
            using(var poses=new StreamWriter(Path.Combine(folder,"service"+service+"-phase"+phase+"-bones.json")))
            {
                poses.Write("{\"bones\":[");bool comma=false;
                foreach(Transform bone in root.GetComponentsInChildren<Transform>(true))
                {
                    if(!(bone.name=="Chest"||bone.name=="Neck"||bone.name=="Head"||bone.name.Contains(".L")||bone.name.Contains(".R")))continue;
                    if(comma)poses.Write(",");comma=true;Quaternion q=bone.localRotation;
                    poses.Write("{\"name\":\""+bone.name+"\",\"rotation\":["+string.Join(",",new[]{q.x,q.y,q.z,q.w}.Select(v=>v.ToString("R",CultureInfo.InvariantCulture)))+"]}");
                }
                poses.Write("]}");
            }
            GameObject snapshot=Snapshot(root);
            foreach(int view in new[]{0,1,2})
            {
                if(pen!=null)pen.gameObject.SetActive(view!=2);
                camera.transform.position=view==0?new Vector3(-.7f,1.9f,-.8f):new Vector3(.5f,2.05f,.15f);camera.transform.LookAt(new Vector3(0,1.15f,.4f));camera.Render();RenderTexture.active=rt;
                var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();File.WriteAllBytes(Path.Combine(folder,"service"+service+"-phase"+phase+"-view"+view+".png"),image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
            }
            UnityEngine.Object.DestroyImmediate(snapshot);
        }
        RenderTexture.active=null;camera.targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(camera.gameObject);UnityEngine.Object.DestroyImmediate(light.gameObject);
    }
    // Camera.Render calls in one Editor tick can reuse the previous GPU skinning upload.
    // Freeze the current bone matrices into a diagnostic static LOD0 snapshot so tool
    // contact is evaluated against exactly the pose whose bone coordinates are recorded.
    private static GameObject Snapshot(Transform root)
    {
        var holder=new GameObject("CPU-skinned current-pose diagnostic");holder.transform.SetParent(root,false);
        var lod=root.GetComponentInChildren<LODGroup>();
        var selected=lod.GetLODs()[0].renderers.OfType<SkinnedMeshRenderer>().ToArray();lod.enabled=false;
        foreach(var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))renderer.enabled=false;
        foreach(var renderer in selected)
        {
            Mesh original=renderer.sharedMesh;var mesh=UnityEngine.Object.Instantiate(original);
            Vector3[] vertices=original.vertices,delta=new Vector3[original.vertexCount];
            for(int shape=0;shape<original.blendShapeCount;shape++)
            {
                float weight=renderer.GetBlendShapeWeight(shape);if(Mathf.Abs(weight)<.0001f)continue;
                int last=original.GetBlendShapeFrameCount(shape)-1;original.GetBlendShapeFrameVertices(shape,last,delta,null,null);
                for(int n=0;n<vertices.Length;n++)vertices[n]+=delta[n]*(weight/original.GetBlendShapeFrameWeight(shape,last));
            }
            BoneWeight[] weights=original.boneWeights;Matrix4x4[] bind=original.bindposes;
            Matrix4x4[] matrices=renderer.bones.Select((bone,n)=>root.worldToLocalMatrix*bone.localToWorldMatrix*bind[n]).ToArray();
            for(int n=0;n<vertices.Length;n++)
            {
                BoneWeight w=weights[n];Vector3 v=vertices[n];
                vertices[n]=matrices[w.boneIndex0].MultiplyPoint3x4(v)*w.weight0+matrices[w.boneIndex1].MultiplyPoint3x4(v)*w.weight1
                    +matrices[w.boneIndex2].MultiplyPoint3x4(v)*w.weight2+matrices[w.boneIndex3].MultiplyPoint3x4(v)*w.weight3;
            }
            mesh.vertices=vertices;mesh.RecalculateNormals();mesh.RecalculateBounds();mesh.RecalculateTangents();
            var copy=new GameObject(renderer.name);copy.transform.SetParent(holder.transform,false);copy.AddComponent<MeshFilter>().sharedMesh=mesh;
            var shown=copy.AddComponent<MeshRenderer>();shown.sharedMaterials=renderer.sharedMaterials;
            var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);shown.SetPropertyBlock(block);
        }
        return holder;
    }
    private static void Book(Transform root,string path)
    {
        var vertices=new List<Vector3>();var triangles=new List<int>();
        foreach(string line in File.ReadLines(path))
        {
            string[] f=line.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries);if(f.Length==0)continue;
            if(f[0]=="v")vertices.Add(new Vector3(float.Parse(f[1],CultureInfo.InvariantCulture),float.Parse(f[3],CultureInfo.InvariantCulture),-float.Parse(f[2],CultureInfo.InvariantCulture)));
            else if(f[0]=="f")for(int n=2;n<f.Length-1;n++){triangles.Add(int.Parse(f[1].Split('/')[0])-1);triangles.Add(int.Parse(f[n].Split('/')[0])-1);triangles.Add(int.Parse(f[n+1].Split('/')[0])-1);}
        }
        var mesh=new Mesh{vertices=vertices.ToArray(),triangles=triangles.ToArray()};mesh.RecalculateNormals();mesh.RecalculateBounds();
        var obj=new GameObject("Original game open book geometry (neutral diagnostic material)");obj.transform.SetParent(root,false);float scale=.32f/mesh.bounds.size.x;obj.transform.localScale=Vector3.one*scale;obj.transform.localPosition=new Vector3(0,.957f,.22f)-new Vector3(mesh.bounds.center.x,mesh.bounds.min.y,mesh.bounds.center.z)*scale;
        obj.AddComponent<MeshFilter>().sharedMesh=mesh;obj.AddComponent<MeshRenderer>().sharedMaterial=new Material(Shader.Find("Standard")){color=new Color(.72f,.66f,.5f)};
    }
}
