using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using GloomhavenVR.Core;
using UnityEngine;

public static class InteractionProgram
{
    private static int _checks;
    private static string _output = "";
    private static readonly List<GameObject> PreviewPrefabs = new();
    private static IEnumerator? _preview;
    public static int Checks => _checks;
    public static bool RenderNext() => _preview != null && _preview.MoveNext();
    private static void Check(bool good, string why) { _checks++; if (!good) throw new Exception(why); }
    private static int Triangles(Mesh mesh)
    { int n = 0; for (int i = 0; i < mesh.subMeshCount; i++) n += (int)mesh.GetIndexCount(i) / 3; return n; }
    private static string Arg(string key)
    { string[] args = Environment.GetCommandLineArgs(); return args[Array.IndexOf(args, key) + 1]; }
    private static void Policy()
    {
        var policy = new FigureDistanceLodPolicy(); var bounds = new Bounds(Vector3.zero, Vector3.one);
        float radius = bounds.extents.magnitude;
        Check(policy.Select(100,bounds,Vector3.forward*radius*3,false)==100,"near retains configured cap");
        Check(policy.Select(100,bounds,Vector3.forward*radius*12,false)==45,"mid selects actual prepared tier");
        Check(policy.Select(100,bounds,Vector3.forward*radius*30,false)==-1,"far selects stronger tier");
        Check(policy.Select(100,bounds,Vector3.forward*radius*19,false)==-1,"far has hysteresis");
        Check(policy.Select(100,bounds,Vector3.forward*radius*17,false)==45,"far exits without chatter");
        Check(policy.Select(20,bounds,Vector3.forward*radius*12,false)==20,"distance never exceeds selected cap");
        Check(policy.Select(20,bounds,Vector3.forward*radius*30,true)==20,"held bodies retain configured near cap");
        bounds.size*=10;
        Check(policy.Select(100,bounds,Vector3.forward*radius*120,false)==45,"world scale retains projected quality");
        PerfConfig.FigureDistanceLodEnabled=false;
        Check(policy.Select(100,bounds,Vector3.forward*radius*300,false)==100,"off restores original quality");
        PerfConfig.FigureDistanceLodEnabled=true;
    }
    private static void Skinning(SkinnedMeshRenderer skin)
    {
        SkinQuality original = skin.quality; SkinWeights native = QualitySettings.skinWeights;
        QualitySettings.skinWeights=SkinWeights.FourBones;
        var guard = new OriginalHandsSkinningGuard();
        var hand = new GameObject("Original four-bone hand").AddComponent<SkinnedMeshRenderer>();
        hand.quality = SkinQuality.Bone4;
        var unrelated = new GameObject("Unowned native skin").AddComponent<SkinnedMeshRenderer>();
        unrelated.quality = SkinQuality.Auto;
        skin.quality=SkinQuality.Bone4;
        var record=new FigureSkinningBudget.Record { Renderer=skin };
        PerfConfig.MaximumSkinningBones=2; FigureSkinningBudget.Tick();record.Apply();
        guard.EnforceGlobalSkinWeights();
        Check(QualitySettings.skinWeights==SkinWeights.FourBones,"actor budget leaves native global skinning untouched");
        Check(skin.quality==SkinQuality.Bone2,"explicit original FourBones renderer is capped");
        int repairs=VRLog.HandRepairs;
        // Execute the original production hand guard in both actor/hand update orders.
        // This rejects the two global writers that ran continuously in hardware Build612.
        for(int frame=0;frame<120;frame++)
        {
            if((frame&1)==0)guard.EnforceGlobalSkinWeights();
            FigureSkinningBudget.Tick();record.Apply();
            if((frame&1)!=0)guard.EnforceGlobalSkinWeights();
            Check(QualitySettings.skinWeights==SkinWeights.FourBones&&hand.quality==SkinQuality.Bone4&&skin.quality==SkinQuality.Bone2,
                "four-bone hands and two-bone actor coexist in both update orders");
        }
        Check(VRLog.HandRepairs==repairs,"ordinary actor updates never trigger another hand repair");
        Check(unrelated.quality==SkinQuality.Auto,"unowned native skins retain their original slots");
        Mesh source=skin.sharedMesh; Transform[] bones=skin.bones; Material[] materials=skin.sharedMaterials;
        PerfConfig.MaximumSkinningBones=1; FigureSkinningBudget.Tick();record.Apply();
        Check(skin.quality==SkinQuality.Bone1&&QualitySettings.skinWeights==SkinWeights.FourBones,"single influence optional body cap preserves hands");
        Check(skin.sharedMesh==source&&skin.bones.Length==bones.Length&&skin.sharedMaterials.Length==materials.Length,"skinning never swaps geometry or rig");
        PerfConfig.MaximumSkinningBones=0; FigureSkinningBudget.Tick();record.Apply();
        Check(skin.quality==SkinQuality.Bone4&&QualitySettings.skinWeights==SkinWeights.FourBones,"off restores exact renderer and preserves global quality");
        PerfConfig.MaximumSkinningBones=2; FigureSkinningBudget.Tick();record.Apply(); VRSession.IsRunning=false;
        FigureSkinningBudget.Tick();record.Apply();
        Check(skin.quality==SkinQuality.Bone4&&QualitySettings.skinWeights==SkinWeights.FourBones,"VR stop restores exact skinning quality");
        VRSession.IsRunning=true;PerfConfig.MaximumSkinningBones=2;record.Apply();skin.quality=SkinQuality.Bone1;record.Apply(true);
        Check(skin.quality==SkinQuality.Bone1,"foreign native influence writer is never overwritten by restoration");

        skin.quality=SkinQuality.Auto;record=new FigureSkinningBudget.Record { Renderer=skin };
        record.Apply();Check(skin.quality==SkinQuality.Bone2,"original Auto actor is capped without changing the global");
        PerfConfig.MaximumSkinningBones=0;record.Apply();Check(skin.quality==SkinQuality.Auto,"Off restores original Auto exactly");
        PerfConfig.MaximumSkinningBones=2;record.Apply();VRSession.IsRunning=false;record.Apply();
        Check(skin.quality==SkinQuality.Auto,"VR stop restores original Auto exactly");VRSession.IsRunning=true;
        record.Apply();skin.quality=SkinQuality.Bone1;record.Apply();
        Check(skin.quality==SkinQuality.Bone1,"a stricter live native renderer setting wins");
        PerfConfig.MaximumSkinningBones=0;record.Apply();Check(skin.quality==SkinQuality.Bone1,"Off retains the updated native renderer setting");

        skin.quality=SkinQuality.Auto;record=new FigureSkinningBudget.Record { Renderer=skin };
        PerfConfig.MaximumSkinningBones=2;record.Apply();
        QualitySettings.skinWeights=SkinWeights.OneBone;FigureSkinningBudget.Tick();
        Check(QualitySettings.skinWeights==SkinWeights.OneBone,"actor lifecycle never overrides a native global quality change");
        repairs=VRLog.HandRepairs;guard.EnforceGlobalSkinWeights();
        Check(QualitySettings.skinWeights==SkinWeights.FourBones&&VRLog.HandRepairs==repairs+1,
            "original hand guard repairs a genuine native quality change once");
        FigureSkinningBudget.Tick();record.Apply();guard.EnforceGlobalSkinWeights();
        Check(VRLog.HandRepairs==repairs+1&&skin.quality==SkinQuality.Bone2,"body cap stays local after a native quality change");
        QualitySettings.skinWeights=SkinWeights.TwoBones;FigureSkinningBudget.Restore();record.Apply(true);
        Check(QualitySettings.skinWeights==SkinWeights.TwoBones&&skin.quality==SkinQuality.Auto,
            "teardown restores Auto without restoring over a foreign global value");
        record.Apply(true);Check(skin.quality==SkinQuality.Auto,"renderer restoration is idempotent");
        QualitySettings.skinWeights=SkinWeights.FourBones;PerfConfig.MaximumSkinningBones=4;record.Apply();
        Check(skin.quality==SkinQuality.Bone4&&QualitySettings.skinWeights==SkinWeights.FourBones,"optional four-bone body limit stays local");
        record.Apply(true);
        UnityEngine.Object.DestroyImmediate(hand.gameObject);UnityEngine.Object.DestroyImmediate(unrelated.gameObject);
        skin.quality=original;PerfConfig.MaximumSkinningBones=0;QualitySettings.skinWeights=native;
    }
    [DataContract] private sealed class Sources { [DataMember] public Source[] meshes=Array.Empty<Source>(); }
    [DataContract] private sealed class Source { [DataMember] public string file=""; [DataMember] public bool readable=false; }
    private static void NativeBodies()
    {
        using var input=File.OpenRead(Path.Combine(Arg("-figureNativeDir"),"sources.json"));
        var sources=(Sources)new DataContractJsonSerializer(typeof(Sources)).ReadObject(input);
        var readability=new Dictionary<string,bool>();foreach(Source source in sources.meshes)readability.Add(source.file,source.readable);
        int bodies=0;
        foreach(string path in Directory.GetFiles(Arg("-figureNativeDir"),"*.mesh"))
        {
            if(Path.GetFileName(path).StartsWith("town-",StringComparison.Ordinal))continue;
            Mesh original=NativeFigureMeshStream.Read(path);
            // The stream is CPU-readable; source bundle slots are imported with read/write OFF.
            if(!readability[Path.GetFileName(path)]) original.UploadMeshData(true);
            ScenarioFigureMeshBank.Prepare(original,0);ScenarioFigureMeshBank.Prepare(original,45);ScenarioFigureMeshBank.Prepare(original,-1);
            Mesh? near=ScenarioFigureMeshBank.Resolve(original,0),mid=ScenarioFigureMeshBank.Resolve(original,45),far=ScenarioFigureMeshBank.Resolve(original,-1);
            Check(near!=null&&mid!=null&&far!=null,"actual original body has complete prepared near/mid/far meshes");
            Check(Triangles(far!)<Triangles(near!)*.65f,"far materially reduces already simplified native body");
            var owner=new GameObject("Actual native body "+original.name);var renderer=owner.AddComponent<SkinnedMeshRenderer>();renderer.sharedMesh=original;
            var record=new ScenarioFigureMeshBank.Record {Renderer=renderer,Original=original};
            record.Apply(-1);Check(renderer.sharedMesh==far,"far original body actually changes its native renderer mesh");
            record.Restore();Check(renderer.sharedMesh==original,"original native body returns exactly when restored");
            Debug.Log("NATIVE DISTANCE "+original.name+": original="+Triangles(original)+" near20="+Triangles(near!)+" mid45="+Triangles(mid!)+" far="+Triangles(far!));
            UnityEngine.Object.DestroyImmediate(owner);bodies++;
        }
        Check(bodies>=5,"all five original hero/drake/demon body cases execute");
    }
    private static void Render(SkinnedMeshRenderer skin,Camera camera,string name)
    {
        var target=new RenderTexture(512,512,24,RenderTextureFormat.ARGB32);target.Create();camera.targetTexture=target;
        Vector3 pose=camera.transform.position;
        camera.transform.position=skin.bounds.center+Vector3.back*Mathf.Max(skin.bounds.extents.magnitude,.1f)*2;
        camera.transform.LookAt(skin.bounds.center);
        camera.Render();camera.transform.position=pose;camera.transform.LookAt(skin.bounds.center);RenderTexture prior=RenderTexture.active;RenderTexture.active=target;
        var image=new Texture2D(512,512,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,512,512),0,0);image.Apply();
        int visible = 0;
        foreach (Color pixel in image.GetPixels()) if (pixel.r < .8f || pixel.g < .8f || pixel.b < .8f) visible++;
        Check(visible > 8000, "actual skinned body pixels remain visible at every tier and return");
        File.WriteAllBytes(Path.Combine(_output,name+".png"),image.EncodeToPNG());
        RenderTexture.active=prior;camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(target);
    }
    private static void Npcs()
    {
        string path=Arg("-townOriginalBundle");AssetBundle? bundle=null;
        foreach(AssetBundle loaded in AssetBundle.GetAllLoadedAssetBundles())
            if(loaded.name==Path.GetFileName(path)||loaded.name==Path.GetFileNameWithoutExtension(path)) {bundle=loaded;break;}
        bundle ??= AssetBundle.LoadFromFile(path);
        if(bundle==null)throw new Exception("Actual original town bundle unavailable");
        var camera=new GameObject("Actual headset camera").AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.white;
        camera.nearClipPlane=.01f;camera.farClipPlane=100;VRCameraPolicy.AllowedHead=camera;
        var light=new GameObject("Actual NPC proof light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1;light.transform.rotation=Quaternion.Euler(35,-35,0);
        RenderSettings.ambientLight=Color.gray;
        int actors=0;
        foreach(GameObject prefab in bundle.LoadAllAssets<GameObject>())
        {
            Transform? actor=prefab.transform.Find("Actor");if(actor==null)continue;
            GameObject root=UnityEngine.Object.Instantiate(actor.gameObject);root.name=prefab.name;root.SetActive(true);
            StationDetail.PreserveActorDetail(root.transform);
            SkinnedMeshRenderer skin=root.GetComponentInChildren<SkinnedMeshRenderer>(true);Check(skin!=null,"original NPC body renderer exists");
            if(skin==null)throw new Exception("Missing original NPC body renderer");
            Mesh original=skin.sharedMesh;Material[] materials=skin.sharedMaterials;Transform[] bones=skin.bones;
            // Windows asset shaders have no Linux editor GL program. This preview adapter
            // retains each original material object/texture and uses one common Standard
            // program at all tiers; production never replaces any material or shader.
            foreach(Renderer originalRenderer in root.GetComponentsInChildren<Renderer>(true))
                foreach(Material material in originalRenderer.sharedMaterials)
                    if(material!=null&&!material.shader.isSupported)
                    {
                        bool cornea=material.shader.name.Contains("Cornea"); Texture texture=material.mainTexture;
                        material.shader=Shader.Find(cornea?"Unlit/Transparent":"Standard");
                        if(cornea)
                        {
                            var transparent=new Texture2D(1,1);transparent.SetPixel(0,0,Color.clear);transparent.Apply();texture=transparent;
                        }
                        material.mainTexture=texture;
                    }
            LODGroup[] groups=root.GetComponentsInChildren<LODGroup>(true);var groupFlags=new bool[groups.Length];for(int i=0;i<groups.Length;i++)groupFlags[i]=groups[i].enabled;
            Skinning(skin);
            var helper=new TownNpcSkinningQuality(root.transform);Bounds bounds=skin.bounds;float radius=Mathf.Max(bounds.extents.magnitude,.1f);
            var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static;
            int ResidentParts() => ((ICollection)typeof(ScenarioFigureMeshBank).GetField("Banks",flags)!.GetValue(null)).Count;
            int SourceSignatures() => ((ICollection)typeof(ScenarioFigureMeshBank).GetField("SourceKeys",flags)!.GetValue(null)).Count;
            int partsBefore=ResidentParts(), signaturesBefore=SourceSignatures();
            // Exercise both proximity and saved retired cap values. Production no longer
            // reads the retired key, and never opens a derivative part for these residents.
            foreach(float distance in new[] {3f,12f,30f,3f})
            foreach(int retiredCap in new[] {0,45,100})
            {
                PerfConfig.TownNpcMeshDetailPercent=retiredCap;
                camera.transform.position=bounds.center+Vector3.forward*radius*distance;
                camera.transform.LookAt(bounds.center);helper.Tick();
                Check(skin.sharedMesh==original,"NPC retains exact original mesh at every distance");
                Check(skin.sharedMesh.blendShapeCount==original.blendShapeCount,"original NPC expression channels remain unchanged");
                for(int i=0;i<materials.Length;i++)Check(skin.sharedMaterials[i]==materials[i],"NPC materials retain original identity");
                for(int i=0;i<bones.Length;i++)Check(skin.bones[i]==bones[i],"NPC bones retain original identity");
            }
            Check(ResidentParts()==partsBefore&&SourceSignatures()==signaturesBefore,
                "NPC lifecycle never prewarms scenario mesh identities or derivative parts");
            for(int i=0;i<groups.Length;i++)Check(groups[i].enabled==groupFlags[i],"legacy LOD groups remain locked to the original surface");
            PerfConfig.FigureDistanceLodEnabled=false;helper.Tick();Check(skin.sharedMesh==original,"distance setting never changes an NPC mesh");
            PerfConfig.FigureDistanceLodEnabled=true;helper.Tick();helper.Dispose();Check(skin.sharedMesh==original,"NPC teardown preserves exact original mesh");
            Debug.Log("NPC ORIGINAL "+prefab.name+": triangles="+Triangles(original)+"; no derivative identity or bank prepared");
            PreviewPrefabs.Add(prefab); UnityEngine.Object.DestroyImmediate(root);actors++;
        }
        Check(actors==3,"all three actual shipped NPC bodies are verified");VRCameraPolicy.AllowedHead=null;
        UnityEngine.Object.DestroyImmediate(light.gameObject);UnityEngine.Object.DestroyImmediate(camera.gameObject);bundle.Unload(false);
    }
    private static IEnumerator Preview()
    {
        var camera=new GameObject("Actual frame-by-frame headset").AddComponent<Camera>();
        camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.white; camera.nearClipPlane=.01f; camera.farClipPlane=100;
        var light=new GameObject("Actual preview light").AddComponent<Light>(); light.type=LightType.Directional; light.intensity=1;
        light.transform.rotation=Quaternion.Euler(35,-35,0); RenderSettings.ambientLight=Color.gray;
        VRCameraPolicy.AllowedHead=camera; PerfConfig.FigureDistanceLodEnabled=true; PerfConfig.TownNpcMeshDetailPercent=100;
        foreach(GameObject prefab in PreviewPrefabs)
        {
            GameObject root=UnityEngine.Object.Instantiate(prefab.transform.Find("Actor").gameObject); root.SetActive(true);
            StationDetail.PreserveActorDetail(root.transform);
            SkinnedMeshRenderer skin=root.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var helper=new TownNpcSkinningQuality(root.transform); Bounds bounds=skin.bounds; float radius=Mathf.Max(bounds.extents.magnitude,.1f);
            string[] labels={"near","mid","far","returned-near"}; float[] distances={3,12,30,3};
            for(int tier=0;tier<labels.Length;tier++)
            {
                camera.transform.position=bounds.center+Vector3.forward*radius*distances[tier]; camera.transform.LookAt(bounds.center); helper.Tick();
                // Render original shipped residents across actual player-loop boundaries.
                // No distance or retired detail choice may replace their mesh.
                yield return null;
                for(int frame=0;frame<2;frame++)
                {
                    foreach(Animator animator in root.GetComponentsInChildren<Animator>(true)) animator.Update(1f/60);
                    Render(skin,camera,prefab.name+"-"+labels[tier]+"-"+frame);
                    yield return null;
                }
            }
            helper.Dispose(); UnityEngine.Object.DestroyImmediate(root);
        }
        VRCameraPolicy.AllowedHead=null; UnityEngine.Object.DestroyImmediate(camera.gameObject); UnityEngine.Object.DestroyImmediate(light.gameObject);
    }
    public static int Run()
    {
        if(Array.IndexOf(Environment.GetCommandLineArgs(),"-figureSkinningOnly")>=0)
        {
            var actor=new GameObject("Native Unity renderer skinning proof").AddComponent<SkinnedMeshRenderer>();
            Skinning(actor);UnityEngine.Object.DestroyImmediate(actor.gameObject);return _checks;
        }
        _output=Arg("-figureRenders");Directory.CreateDirectory(_output);Policy();NativeBodies();Npcs();_preview=Preview();return _checks;
    }
}
