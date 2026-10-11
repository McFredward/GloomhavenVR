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
        int selected=Array.IndexOf(args,"-activityService");if(selected>=0&&int.Parse(args[selected+1])!=service)return;
        bool sequence=Array.IndexOf(args,"-activitySequence")>=0;
        bool closeTransition=Array.IndexOf(args,"-activityCloseTransition")>=0;
        bool attentionSequence=Array.IndexOf(args,"-activityAttentionSequence")>=0||closeTransition;
        bool templeUnavailable=Array.IndexOf(args,"-activityTempleUnavailable")>=0;
        bool templeBlessing=Array.IndexOf(args,"-activityTempleBlessing")>=0;
        float frameSeconds=closeTransition?1f/90f:attentionSequence?1f/24f:1f/8f;
        string folder=args[output+1];Transform root=obj.transform;rig.BeforeBodySample();root.SetPositionAndRotation(Vector3.zero,Quaternion.identity);root.localScale=Vector3.one;
        Shader shader=obj.GetComponentsInChildren<SkinnedMeshRenderer>(true)[0].sharedMaterial.shader;
        using var props=new TownServiceActivityProps(root,service,shader);props.SetVisibility(1);
        // The Windows-built stone shader renders magenta in this Linux Editor. Diagnostic
        // materials retain the prefab geometry and source maps, and the bowl guide uses the
        // authored Chapel prop seat (TownServiceDecor), not a guessed camera-space overlay.
        var originals=new Dictionary<MeshRenderer,Material[]>();var diagnostic=new List<Material>();
        foreach(MeshRenderer shown in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            originals.Add(shown,shown.sharedMaterials);
            Material[] substitutes=shown.sharedMaterials.Select(source=>
            {
                var material=new Material(Shader.Find("Standard"));
                if(source!=null)material.mainTexture=source.mainTexture;
                material.color=new Color(.53f,.49f,.43f);diagnostic.Add(material);return material;
            }).ToArray();
            shown.sharedMaterials=substitutes;
        }
        GameObject? bowl=service==2?BowlGuide(root):null;
        GameObject? purseTemplate=null;
        TownServiceTempleBowlMarker? blessing=null;
        if(service==2&&templeBlessing)
        {
            purseTemplate=GameObject.CreatePrimitive(PrimitiveType.Sphere);
            purseTemplate.name="Diagnostic source purse (hidden)";
            purseTemplate.SetActive(false);
            TownServiceDecor.MoneyBagTemplate=purseTemplate.transform;
            blessing=new TownServiceTempleBowlMarker(root,stationSpace:true);
            blessing.Tick(false);
        }
        int book=Array.IndexOf(args,"-activityBook");if(book>=0&&service!=2)Book(root,args[book+1]);
        var coin=GameObject.CreatePrimitive(PrimitiveType.Cylinder);coin.name="Diagnostic coin contact volume (not native asset)";coin.transform.SetParent(root,false);coin.transform.localScale=new Vector3(.026f,.0015f,.026f);coin.GetComponent<Renderer>().sharedMaterial=new Material(Shader.Find("Standard")){color=new Color(.6f,.4f,.1f)};
        if(service==1)props.BindCoin(coin.transform,Vector3.zero);else coin.SetActive(false);
        var camera=new GameObject("Activity diagnostic camera").AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.14f,.17f);camera.fieldOfView=48;camera.nearClipPlane=.02f;
        var rt=new RenderTexture(sequence&&!closeTransition?640:1000,sequence&&!closeTransition?576:900,24);camera.targetTexture=rt;
        var light=new GameObject("Diagnostic stand light").AddComponent<Light>();light.type=LightType.Point;light.transform.position=new Vector3(-.4f,2.2f,-.7f);light.intensity=3;light.range=6;light.color=new Color(1,.89f,.72f);
        TownServiceLightList.Claim(light); TownServiceLightList.Bind();
        RenderSettings.ambientLight=new Color(.25f,.28f,.32f);RenderSettings.ambientIntensity=1;
        var block=new MaterialPropertyBlock();block.SetFloat("_TownVisibility",1);foreach(Renderer r in obj.GetComponentsInChildren<Renderer>(true))r.SetPropertyBlock(block);
        Animation animation=root.GetComponentInChildren<Animation>();
        var faceRig=new TownServiceFaceRig(root);
        var gaze=default(TownFacePose);
        using var metrics=new StreamWriter(Path.Combine(folder,"service"+service+"-contacts.csv"));metrics.WriteLine("phase,handX,handY,handZ,gripX,gripY,gripZ,tipX,tipY,tipZ");
        using var templeGeometry=service==2?new StreamWriter(Path.Combine(folder,"service2-geometry.csv")):null;
        templeGeometry?.WriteLine("seconds,attention,leftRoll,leftShoulderX,leftShoulderY,leftShoulderZ,leftElbowX,leftElbowY,leftElbowZ,leftPalmX,leftPalmY,leftPalmZ,rightShoulderX,rightShoulderY,rightShoulderZ,rightElbowX,rightElbowY,rightElbowZ,rightPalmX,rightPalmY,rightPalmZ");
        using var particleCounts=templeBlessing&&service==2
            ?new StreamWriter(Path.Combine(folder,"service2-blessing-particles.csv")):null;
        particleCounts?.WriteLine("phase,system,time,count,worldX,worldY,worldZ,minZ,maxZ");
        float[] phases={.8f,1.8f,1.8f,22f,24f,26f,28f,32f};
        if(sequence||closeTransition)phases=Enumerable.Range(0,closeTransition?450:attentionSequence?(templeBlessing?120:192):384).Select(n=>n*frameSeconds).ToArray();
        var transition=new TownActivityPose{WorkClock=5.3f,TransitionAge=TownServiceActivityMotion.TransitionSeconds};
        var envelope=new Bounds();bool envelopeStarted=false;
        for(int phase=0;phase<phases.Length;phase++)
        {
            FaceClock.Now=phases[phase];
            if(blessing!=null)
            {
                if(phase>0&&phases[phase-1]<2f&&phases[phase]>=2f)blessing.Bless(0f);
                else if(phases[phase]>2f)
                    foreach(ParticleSystem system in blessing.Root.GetComponentsInChildren<ParticleSystem>(true))
                        system.Simulate(frameSeconds,true,false,false);
                blessing.Tick(false);
                if(phase%12==0)
                    foreach(ParticleSystem system in blessing.Root.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        var particles=new ParticleSystem.Particle[system.main.maxParticles];int count=system.GetParticles(particles);
                        Vector3 sum=Vector3.zero;float minZ=float.PositiveInfinity,maxZ=float.NegativeInfinity;
                        for(int i=0;i<count;i++)
                        {
                            Vector3 world=system.transform.TransformPoint(particles[i].position);
                            sum+=world;minZ=Mathf.Min(minZ,world.z);maxZ=Mathf.Max(maxZ,world.z);
                        }
                        Vector3 mean=count>0?sum/count:Vector3.zero;
                        particleCounts?.WriteLine(phases[phase].ToString("R",CultureInfo.InvariantCulture)+","+
                            system.name+","+string.Join(",",new[]{system.time,(float)count,
                            mean.x,mean.y,mean.z,minZ,maxZ}.Select(v=>v.ToString("R",CultureInfo.InvariantCulture))));
                    }
            }
            faceRig.BeforeBodySample();rig.BeforeBodySample();animation.Stop();var body=animation["Idle"];body.enabled=true;body.weight=1;
            // Station.Sample receives resident.Age, not a permanently frozen first
            // clip frame. Freezing this diagnostic at zero hid pose-dependent skin
            // intersections that the hardware player sees during later visits.
            body.time=phases[phase];animation.Sample();body.enabled=false;
            bool attentive=!sequence&&phase==2;
            var state=new TownActivityPose{WorkClock=phases[phase],TransitionAge=TownServiceActivityMotion.TransitionSeconds,FromBlend=attentive?1:0,Engaged=attentive};
            if(attentionSequence)
            {
                TownServiceActivityMotion.Engage(ref transition,phases[phase]>=1f&&phases[phase]<(templeBlessing?6f:4f));
                transition=TownServiceActivityMotion.Advance(transition,frameSeconds);state=transition;
                attentive=TownServiceActivityMotion.Blend(in state)>.5f;
            }
            TownActivityVisual rendered=TownServiceActivityMotion.Visual(service,in state);
            if(service==2&&(templeUnavailable||templeBlessing)&&(attentionSequence||attentive))
            {
                // An unavailable return visit starts its attention transition
                // covered. A newly committed donation instead starts available
                // and covers the bowl over the shared transition age at t=2s.
                // Earlier sequence renders unconditionally covered both cases and
                // could not reveal a bad donation/cover/blessing overlap.
                float cover=templeBlessing
                    ?Mathf.Clamp01((phases[phase]-2f)/TownServiceActivityMotion.TransitionSeconds):1f;
                TownServiceActivityMotion.ApplyTempleAvailability(ref rendered,false,cover);
            }
            if(service==2&&templeBlessing)
                TownServiceActivityMotion.ApplyTempleBlessing(ref rendered,phases[phase]-2f);
            rig.Apply(in rendered);props.Sample(in rendered);
            if(templeGeometry!=null)
            {
                Transform[] bones=root.GetComponentsInChildren<Transform>(true);
                var joints=new List<Vector3>();
                foreach(string side in new[]{"L","R"})
                    foreach(string name in new[]{"UpperArm.","Forearm.","PalmContact."})
                        joints.Add(root.InverseTransformPoint(bones.Single(b=>b.name==name+side).position));
                var values=new List<float>{phases[phase],rendered.Attention,rendered.LeftRoll};
                foreach(Vector3 joint in joints){values.Add(joint.x);values.Add(joint.y);values.Add(joint.z);}
                templeGeometry.WriteLine(string.Join(",",values.Select(v=>v.ToString("R",CultureInfo.InvariantCulture))));
            }
            {
                Vector3 focus=attentive?new Vector3(-.7f,1.9f,-.8f):root.Find("ActivityWorkFocus").position;
                for(int frame=0;frame<(sequence?1:90);frame++)gaze=TownServiceFaceMotion.Aim(faceRig.OpticalRotation,root.lossyScale.x,
                    faceRig.HeadPosition,faceRig.LeftPosition,faceRig.RightPosition,focus,in gaze,sequence?frameSeconds:1f/90f);
                TownServiceFacePose face=TownServiceFaceMotion.Evaluate(in gaze,phases[phase],service,Vector3.zero,
                    templeBlessing?phases[phase]-2f:float.PositiveInfinity);faceRig.Apply(in face);
                metrics.WriteLine("# normal-work-gaze pitch="+gaze.HeadPitch.ToString("R",CultureInfo.InvariantCulture)+" yaw="+gaze.HeadYaw.ToString("R",CultureInfo.InvariantCulture));
            }
            Transform hand=root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Hand.R");Transform grip=root.Find("ActivityGripRight"),pen=root.Find("Town.ReedPen");Vector3 tip=Vector3.zero;
            Transform shoulder=root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="UpperArm.R");Transform elbow=root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Forearm.R");
            Transform clavicle=root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Clavicle.R");
            if(service==1)
            {
                Transform index=root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="IndexTip.L");
                Transform thumb=root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="ThumbTip.L");
                metrics.WriteLine("# coin pinch gap="+Vector3.Distance(index.position,thumb.position).ToString("R",CultureInfo.InvariantCulture));
            }
            metrics.WriteLine("# clavicle="+clavicle.position.ToString("F5")+" shoulder="+shoulder.position.ToString("F5")
                +" elbow="+elbow.position.ToString("F5")+" upper="+Vector3.Distance(shoulder.position,elbow.position)
                +" fore="+Vector3.Distance(elbow.position,hand.position));
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
            // Keep the 90 Hz state/face history from t=0, but capture only the
            // prayer-to-neutral middle and its release. A sampled screenshot at
            // each 90 Hz step can expose a transient sleeve kink that a 24 fps
            // sequence or a first/last pose misses.
            if(closeTransition&&!((phase>=110&&phase<=171)||(phase>=380&&phase<=441)))continue;
            var originalSkins=root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var enabledSkins=originalSkins.Select(r=>r.enabled).ToArray();
            GameObject snapshot=Snapshot(root);
            foreach(MeshFilter skin in snapshot.GetComponentsInChildren<MeshFilter>())
            {if(!envelopeStarted){envelope=skin.sharedMesh.bounds;envelopeStarted=true;}else envelope.Encapsulate(skin.sharedMesh.bounds);}
            foreach(int view in sequence&&!(service==2&&phase%12==0)?new[]{0}
                :service==2?new[]{0,1,2,3,4}:new[]{0,1,2,4})
            {
                if(pen!=null)pen.gameObject.SetActive(view!=2);
                camera.transform.position=view==0?new Vector3(-.7f,1.9f,-.8f)
                    :view==1?new Vector3(.5f,2.05f,.15f)
                    :view==2?new Vector3(0f,1.52f,-1.1f):view==3?new Vector3(-.8f,1.45f,-.15f)
                    :new Vector3(0f,1.74f,-.60f);
                camera.transform.LookAt(new Vector3(0,1.15f,.4f));camera.Render();RenderTexture.active=rt;
                var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();File.WriteAllBytes(Path.Combine(folder,"service"+service+"-phase"+phase+"-view"+view+".png"),image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
            }
            if(service==3&&!sequence)
            {
                // Albedo-only closeup separates real dark atlas regions and open
                // geometry from lighting on the sharply folded shoulder cloth.
                foreach(MeshRenderer shown in snapshot.GetComponentsInChildren<MeshRenderer>())
                    foreach(Material material in shown.sharedMaterials)
                        material.shader=Shader.Find("Unlit/Texture");
                camera.fieldOfView=30;
                foreach(string side in new[]{"L","R"})
                {
                    Transform upper=root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="UpperArm."+side);
                    Vector3 center=upper.position+new Vector3(0f,-.08f,0f);
                    camera.transform.position=center+new Vector3(side=="L"?.36f:-.36f,.10f,-.40f);
                    camera.transform.LookAt(center);
                    camera.Render();RenderTexture.active=rt;
                    var close=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
                    close.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);close.Apply();
                    File.WriteAllBytes(Path.Combine(folder,"service3-phase"+phase+"-shoulder"+side+".png"),close.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(close);
                }
                camera.fieldOfView=48;
            }
            foreach(MeshFilter mesh in snapshot.GetComponentsInChildren<MeshFilter>())UnityEngine.Object.DestroyImmediate(mesh.sharedMesh);
            foreach(MeshRenderer shown in snapshot.GetComponentsInChildren<MeshRenderer>())
                foreach(Material material in shown.sharedMaterials)UnityEngine.Object.DestroyImmediate(material);
            UnityEngine.Object.DestroyImmediate(snapshot);
            for(int i=0;i<originalSkins.Length;i++)originalSkins[i].enabled=enabledSkins[i];
        }
        Console.WriteLine("Actual skinned work envelope service="+service+" min="+envelope.min.ToString("F5")+" max="+envelope.max.ToString("F5"));
        RenderTexture.active=null;camera.targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(camera.gameObject);TownServiceLightList.Forget(light);UnityEngine.Object.DestroyImmediate(light.gameObject);
        if(bowl!=null)UnityEngine.Object.DestroyImmediate(bowl);
        blessing?.Dispose();
        TownServiceDecor.MoneyBagTemplate=null;
        if(purseTemplate!=null)UnityEngine.Object.DestroyImmediate(purseTemplate);
        foreach(var original in originals)if(original.Key!=null)original.Key.sharedMaterials=original.Value;
        foreach(Material material in diagnostic)UnityEngine.Object.DestroyImmediate(material);
    }
    private static GameObject BowlGuide(Transform root)
    {
        var bowl=new GameObject("Diagnostic Chapel bowl at production prop seat");
        bowl.transform.SetParent(root,false);
        bowl.transform.localPosition=new Vector3(0f,.957f,.18f);
        const int sections=32;
        var vertices=new List<Vector3>();var triangles=new List<int>();
        for(int i=0;i<=sections;i++)
        {
            float angle=i*2f*Mathf.PI/sections,c=Mathf.Cos(angle),s=Mathf.Sin(angle);
            vertices.Add(new Vector3(c*.09f,.13f,s*.09f));
            vertices.Add(new Vector3(c*.057f,.075f,s*.057f));
        }
        for(int i=0;i<sections;i++)
        {
            int a=i*2,b=a+1,c=a+2,d=a+3;
            triangles.Add(a);triangles.Add(b);triangles.Add(c);
            triangles.Add(c);triangles.Add(b);triangles.Add(d);
        }
        var mesh=new Mesh{vertices=vertices.ToArray(),triangles=triangles.ToArray()};mesh.RecalculateNormals();
        bowl.AddComponent<MeshFilter>().sharedMesh=mesh;
        bowl.AddComponent<MeshRenderer>().sharedMaterial=new Material(Shader.Find("Standard")){color=new Color(.38f,.25f,.11f)};
        return bowl;
    }
    // Camera.Render calls in one Editor tick can reuse the previous GPU skinning upload.
    // Freeze the current bone matrices into a diagnostic static LOD0 snapshot so tool
    // contact is evaluated against exactly the pose whose bone coordinates are recorded.
    private static GameObject Snapshot(Transform root)
    {
        var holder=new GameObject("CPU-skinned current-pose diagnostic");holder.transform.SetParent(root,false);
        var lod=root.GetComponentInChildren<LODGroup>();
        var selected=lod!=null?lod.GetLODs()[0].renderers.OfType<SkinnedMeshRenderer>().ToArray()
            :root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r=>r.enabled&&r.gameObject.activeInHierarchy).ToArray();
        if(lod!=null)lod.enabled=false;
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
            var shown=copy.AddComponent<MeshRenderer>();
            // Windows bundle shaders are deliberately not compiled for this Linux
            // diagnostic Editor. Preserve original atlas UVs with a neutral Standard
            // material so actual skinned cracks, rather than magenta fallback, render.
            shown.sharedMaterials=renderer.sharedMaterials.Select((source,index)=>
            {
                var material=new Material(Shader.Find("Standard"));
                material.mainTexture=source.mainTexture;
                material.color=index==2?new Color(.82f,.60f,.48f):index==0
                    ?new Color(.42f,.37f,.40f):new Color(.58f,.48f,.50f);
                return material;
            }).ToArray();
            var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);shown.SetPropertyBlock(block);
        }
        return holder;
    }
    private static void Book(Transform root,string path)
    {
        var vertices=new List<Vector3>();var triangles=new List<int>();var uv=new List<Vector2>();
        foreach(string line in File.ReadLines(path))
        {
            string[] f=line.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries);if(f.Length==0)continue;
            if(f[0]=="v")vertices.Add(new Vector3(float.Parse(f[1],CultureInfo.InvariantCulture),float.Parse(f[3],CultureInfo.InvariantCulture),-float.Parse(f[2],CultureInfo.InvariantCulture)));
            else if(f[0]=="vt")uv.Add(new Vector2(float.Parse(f[1],CultureInfo.InvariantCulture),float.Parse(f[2],CultureInfo.InvariantCulture)));
            else if(f[0]=="f")for(int n=2;n<f.Length-1;n++){triangles.Add(int.Parse(f[1].Split('/')[0])-1);triangles.Add(int.Parse(f[n].Split('/')[0])-1);triangles.Add(int.Parse(f[n+1].Split('/')[0])-1);}
        }
        var mesh=new Mesh{vertices=vertices.ToArray(),triangles=triangles.ToArray()};if(uv.Count==vertices.Count)mesh.uv=uv.ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();
        var obj=new GameObject("Original game open book geometry (neutral diagnostic material)");obj.transform.SetParent(root,false);float scale=.32f/mesh.bounds.size.x;obj.transform.localScale=Vector3.one*scale;obj.transform.localPosition=new Vector3(0,.957f,.22f)-new Vector3(mesh.bounds.center.x,mesh.bounds.min.y,mesh.bounds.center.z)*scale;
        obj.AddComponent<MeshFilter>().sharedMesh=mesh;
        var material=new Material(Shader.Find("Standard")){color=Color.white};
        string[] args=Environment.GetCommandLineArgs();int textureAt=Array.IndexOf(args,"-activityBookTexture");
        if(textureAt>=0)
        {
            var texture=new Texture2D(2,2,TextureFormat.RGBA32,true);texture.LoadImage(File.ReadAllBytes(args[textureAt+1]));
            material.mainTexture=texture;material.mainTextureScale=Vector2.one;
        }
        obj.AddComponent<MeshRenderer>().sharedMaterial=material;
    }
}
