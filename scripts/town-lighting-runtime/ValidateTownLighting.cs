using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GloomhavenVR.Core;
using GloomhavenVR.Net;
using GloomhavenVR.WorldUI;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using GloomhavenVR.Net.TownServices;

public static class ValidateTownLighting
{
    static IEnumerator routine;
    static int assertions, negatives;
    static string output;
    static Camera camera;
    static Texture2D readback;
    static readonly List<string> results = new List<string>();
    public static void Run()
    {
        output = Directory.GetParent(Application.dataPath).FullName;
        Debug.Log("Town lighting fixture starting");
        routine = Execute(); EditorApplication.update += Step;
    }
    static void Step()
    {
        try { if (!routine.MoveNext()) { EditorApplication.update -= Step; EditorApplication.Exit(0); } }
        catch (Exception error) { Debug.LogException(error); EditorApplication.update -= Step; EditorApplication.Exit(1); }
    }
    static void Check(bool value, string reason)
    { assertions++; if (!value) throw new Exception("Town lighting assertion: " + reason); }
    static Color32[] Picture(string name = null)
    {
        camera.Render(); RenderTexture.active = camera.targetTexture;
        readback.ReadPixels(new Rect(0, 0, 512, 512), 0, 0); readback.Apply();
        if (name != null) File.WriteAllBytes(Path.Combine(output, name + ".png"), readback.EncodeToPNG());
        return readback.GetPixels32();
    }
    static float Difference(Color32[] first, Color32[] second)
    {
        double difference = 0; int count = 0;
        for (int i = 0; i < first.Length; i++)
        {
            if (first[i].r <= 10 || second[i].r <= 10) continue;
            difference += Math.Abs(first[i].r - second[i].r) + Math.Abs(first[i].g - second[i].g) + Math.Abs(first[i].b - second[i].b);
            count++;
        }
        return (float)(difference / Math.Max(1, count) / 3);
    }
    static float Brightness(Color32[] image) => image.Sum(p => (long)p.r + p.g + p.b) / (image.Length * 3f);
    static float WholeDifference(Color32[] first, Color32[] second)
    {
        long delta=0;for(int n=0;n<first.Length;n++)delta+=Math.Abs(first[n].r-second[n].r)+Math.Abs(first[n].g-second[n].g)+Math.Abs(first[n].b-second[n].b);
        return delta/(first.Length*3f);
    }
    static void Save(string name, Color32[] image)
    { readback.SetPixels32(image); readback.Apply(); File.WriteAllBytes(Path.Combine(output, name + ".png"), readback.EncodeToPNG()); }
    static readonly string[] LocalUniforms={"_TownSharedAmbient","_TownSharedKey","_TownKeyDirection","_TownKeyColour",
        "_TownAmbientAr","_TownAmbientAg","_TownAmbientAb","_TownAmbientBr","_TownAmbientBg","_TownAmbientBb","_TownAmbientC"};
    static bool LocalUniform(string name)=>Array.IndexOf(LocalUniforms,name)>=0;
    static TownServiceValue LegacyRoom(TownServiceValue source,float seed)
    {
        var value=new TownServiceValue{Numbers=(float[])source.Numbers.Clone(),Text=(string[])source.Text.Clone()};
        for(int i=0;i<(value.Text.Length-2)/2;i++)
            if(LocalUniform(value.Text[2+i*2]))
                for(int n=0;n<4;n++)value.Numbers[5+i*5+n]=seed+n*.17f;
        return value;
    }
    static void MaterialIsolation()
    {
        var assets=new GloomhavenVR.Net.TownServices.TownServiceAssets();
        foreach(string name in new[]{"TownNpc","TownEye","TownCornea","ForeignEnvironment"})
        {
            var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/"+name+".shader");
            Check(shader!=null&&shader.isSupported,name+" material shader compiles");
            var original=new Material(shader);bool owned=name!="ForeignEnvironment";
            original.SetFloat("_TownVisibility",.73f);original.EnableKeyword("FIXTURE_ORIGINAL_KEYWORD");
            if(original.HasProperty("_MainTex"))
            {
                original.SetTexture("_MainTex",Texture2D.whiteTexture);original.SetTextureScale("_MainTex",new Vector2(.6f,.8f));
                original.SetTextureOffset("_MainTex",new Vector2(.1f,.2f));
            }
            var pristine=new Material(original);
            foreach(string uniform in LocalUniforms)
            {
                Check(original.HasProperty(uniform),name+" carries original environment field "+uniform);
                if(uniform=="_TownSharedAmbient"||uniform=="_TownSharedKey")original.SetFloat(uniform,1);
                else original.SetVector(uniform,new Vector4(.13f,.27f,.41f,.55f));
            }
            TownServiceMaterial.Reset();var snapshot=TownServiceMaterial.Read(original,assets);
            TownServiceMaterial.Validate(snapshot,assets);
            // All non-environment properties are compared directly to the native
            // source material, including texture identity/UVs and controller data.
            Check(snapshot.Text[1]==string.Join("\n",original.shaderKeywords),name+" original keywords remain exact");
            for(int i=0;i<shader.GetPropertyCount();i++)
            {
                string property=shader.GetPropertyName(i);int n=5+i*5;
                var actual=new Vector4(snapshot.Numbers[n],snapshot.Numbers[n+1],snapshot.Numbers[n+2],snapshot.Numbers[n+3]);
                if(owned&&LocalUniform(property)){Check(actual==Vector4.zero,name+" publishes neutral "+property);continue;}
                switch(shader.GetPropertyType(i))
                {
                    case ShaderPropertyType.Color:Check(actual==(Vector4)original.GetColor(property),name+" native color "+property);break;
                    case ShaderPropertyType.Vector:Check(actual==original.GetVector(property),name+" native vector "+property);break;
                    case ShaderPropertyType.Float:case ShaderPropertyType.Range:
                        Check(actual.x==original.GetFloat(property),name+" native scalar "+property);break;
                    case ShaderPropertyType.Texture:
                        Vector2 scale=original.GetTextureScale(property),offset=original.GetTextureOffset(property);
                        Check(actual==new Vector4(scale.x,scale.y,offset.x,offset.y)
                            &&ReferenceEquals(assets.Resolve<Texture>(snapshot.Text[3+i*2]),original.GetTexture(property)),name+" native texture and UVs "+property);break;
                }
            }
            var replay=TownServiceMaterial.Apply(snapshot,assets,null);
            if(owned)
            {
                var oldA=LegacyRoom(snapshot,.2f);var oldB=LegacyRoom(snapshot,.8f);
                TownServiceMaterial.Validate(oldA,assets);TownServiceMaterial.Validate(oldB,assets);
                var before=(float[])oldA.Numbers.Clone();
                var same=TownServiceMaterial.Apply(oldA,assets,replay);
                Check(ReferenceEquals(replay,same),name+" old host environment cannot replace observer material");
                same=TownServiceMaterial.Apply(oldB,assets,same);
                Check(ReferenceEquals(replay,same),name+" another old room reuses original neutral pooled material");
                Check(oldA.Numbers.SequenceEqual(before),name+" received old room packet stays immutable");
                foreach(string uniform in LocalUniforms)
                    Check(uniform=="_TownSharedAmbient"||uniform=="_TownSharedKey"?same.GetFloat(uniform)==0:same.GetVector(uniform)==Vector4.zero,
                        name+" old room cannot write observer "+uniform);
                original.SetFloat("_TownVisibility",.36f);TownServiceMaterial.Reset();
                var controller=TownServiceMaterial.Read(original,assets);
                Check(!snapshot.Same(controller),name+" original dissolve change still publishes");
                var changed=TownServiceMaterial.Apply(controller,assets,same);
                Check(!ReferenceEquals(changed,replay)&&changed.GetFloat("_TownVisibility")==.36f,name+" original dissolve still replays");
                replay=changed;
            }
            else
                foreach(string uniform in LocalUniforms)
                    Check(uniform=="_TownSharedAmbient"||uniform=="_TownSharedKey"?replay.GetFloat(uniform)==1:replay.GetVector(uniform)==original.GetVector(uniform),
                        "foreign shader retains identically named native field "+uniform);
            if(original.HasProperty("_MainTex"))Check(replay.GetTexture("_MainTex")==pristine.GetTexture("_MainTex")
                &&replay.GetTextureScale("_MainTex")==pristine.GetTextureScale("_MainTex")
                &&replay.GetTextureOffset("_MainTex")==pristine.GetTextureOffset("_MainTex"),name+" replay retains original texture identity/UVs");
            TownServiceMaterial.Release(replay);UnityEngine.Object.DestroyImmediate(original);UnityEngine.Object.DestroyImmediate(pristine);
        }
    }
    static Light Lamp(Vector3 position)
    {
        Light light = new GameObject("Actual practical").AddComponent<Light>();
        light.type = LightType.Point; light.renderMode = LightRenderMode.ForceVertex;
        light.range = 2.65f; light.intensity = 2.6f; light.color = new Color(1, .75f, .5f);
        light.cullingMask = 1 << VRLayers.ModLayer; light.transform.position = position;
        TownServiceLighting.ClaimPractical(light); return light;
    }
    static void Release(Light light)
    { TownServiceLighting.ForgetPractical(light); UnityEngine.Object.DestroyImmediate(light.gameObject); }
    static void CameraPose(float x, float scale, float eye = 0)
    {
        camera.nearClipPlane = .01f * scale; camera.farClipPlane = 10 * scale;
        camera.transform.position = new Vector3(x + eye, 1.35f, -2.7f) * scale;
        camera.transform.LookAt(new Vector3(x + eye, 1.05f, .1f) * scale);
    }
    static IEnumerator Execute()
    {
        Debug.Log("Town lighting fixture entering render routine");
        QualitySettings.pixelLightCount = 0;
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black;
        RenderSettings.ambientIntensity = 0; RenderSettings.reflectionIntensity = 0;
        camera = new GameObject("Head camera").AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
        camera.cullingMask = 1 << VRLayers.ModLayer; camera.fieldOfView = 45;
        camera.targetTexture = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32);
        readback = new Texture2D(512, 512, TextureFormat.RGB24, false);
        var current = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/TownNpc.shader");
        var old = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/TownNpc548.shader");
        Check(current != null && current.isSupported && old != null && old.isSupported, "production and regression shaders compile");
        var opaqueDefaults=new Material(current);
        Check(opaqueDefaults.GetFloat("_TownTransparent")==0f
            && opaqueDefaults.GetFloat("_SrcBlend")==1f
            && opaqueDefaults.GetFloat("_DstBlend")==0f
            && opaqueDefaults.GetFloat("_ZWrite")==1f,
            "normal TownNpc shader retains opaque blend and solid depth defaults");
        MaterialIsolation();TownLightingEditorLifetime.Flush();
        var guideRoot=new GameObject("Actual temple guide lighting probe");guideRoot.layer=VRLayers.ModLayer;
        var guide=GameObject.CreatePrimitive(PrimitiveType.Cube);guide.transform.SetParent(guideRoot.transform,false);guide.layer=VRLayers.ModLayer;
        guide.transform.localScale=new Vector3(.8f,.8f,.1f);
        var guideMesh=guide.GetComponent<MeshRenderer>();
        var originalGuide=opaqueDefaults;originalGuide.SetFloat("_TownVisibility",0);
        var guideMaterial=ActualGuideMaterial.GhostMaterial(originalGuide);guideMesh.sharedMaterial=guideMaterial;
        Check(guideMaterial.shader==current && guideMaterial.GetFloat("_TownTransparent")==1f
            && guideMaterial.GetFloat("_ZWrite")==0 && guideMaterial.renderQueue==(int)RenderQueue.Transparent,
            "actual guide material preserves owned shader, alpha transparency and non-solid depth");
        Check(originalGuide.GetFloat("_TownTransparent")==0 && originalGuide.GetFloat("_ZWrite")==1,
            "guide material never changes its source body shader state");
        Check(guideMaterial.GetFloat("_TownVisibility")==1,
            "guide alpha never inherits the hidden source template's dissolve");
        camera.transform.position=new Vector3(0,0,-2);camera.transform.LookAt(Vector3.zero);camera.backgroundColor=new Color(.1f,.3f,.1f);
        var guideAmbient=new SphericalHarmonicsL2();guideAmbient.AddAmbientLight(new Color(.3f,.3f,.3f));
        RenderSettings.ambientMode=AmbientMode.Custom;RenderSettings.ambientIntensity=1;RenderSettings.ambientProbe=guideAmbient;
        using(var presentation=new TownServiceLighting(guideRoot.transform,2))
        {
            var state=new TownActivityState{HasSharedPerformance=true,HasAuthoredFoley=true};TownServiceLighting.SampleEnvironment(guideRoot.transform,ref state);
            presentation.BindEnvironment(guideRoot.transform,guideRoot.transform,in state);
            Check(!guideMesh.HasPropertyBlock(),"local lighting leaves original guide mesh publishable without undeclared property blocks");
            var assets=new GloomhavenVR.Net.TownServices.TownServiceAssets();
            var materialSnapshot=GloomhavenVR.Net.TownServices.ActualMeshCapture.Capture(guideMesh,assets);
            GloomhavenVR.Net.TownServices.TownServiceMaterial.Validate(materialSnapshot,assets);
            var ownerGuide=Picture("guide-local-owner");
            var observerAmbient=new SphericalHarmonicsL2();observerAmbient.AddAmbientLight(new Color(.8f,0,0));RenderSettings.ambientProbe=observerAmbient;
            var remoteGuide=GloomhavenVR.Net.TownServices.TownServiceMaterial.Apply(materialSnapshot,assets,null);
            guideMesh.sharedMaterial=remoteGuide;
            var observerGuide=Picture("guide-local-observer");
            guideMesh.sharedMaterial=guideMaterial;var nativeObserverGuide=Picture("guide-native-observer");guideMesh.sharedMaterial=remoteGuide;
            Check(WholeDifference(nativeObserverGuide,observerGuide)<.1f,"actual material playback matches the native original in the observer's own environment");
            Check(WholeDifference(ownerGuide,observerGuide)>.5f,"the actual guide shader responds to each viewer's own environment");
            var oldRoom=LegacyRoom(materialSnapshot,.8f);GloomhavenVR.Net.TownServices.TownServiceMaterial.Validate(oldRoom,assets);
            var retained=GloomhavenVR.Net.TownServices.TownServiceMaterial.Apply(oldRoom,assets,remoteGuide);
            Check(ReferenceEquals(retained,remoteGuide)&&WholeDifference(observerGuide,Picture("guide-observer-after-remote-switch"))<.1f,
                "another player's old-format room change neither replaces the observer material nor changes its final pixels");
            var contaminated=new Material(remoteGuide);contaminated.SetFloat("_TownSharedAmbient",1);
            var packed=new MaterialPropertyBlock();packed.CopySHCoefficientArraysFrom(new[]{guideAmbient});
            string[] nativeSH={"unity_SHAr","unity_SHAg","unity_SHAb","unity_SHBr","unity_SHBg","unity_SHBb","unity_SHC"};
            string[] ownedSH={"_TownAmbientAr","_TownAmbientAg","_TownAmbientAb","_TownAmbientBr","_TownAmbientBg","_TownAmbientBb","_TownAmbientC"};
            for(int term=0;term<7;term++)contaminated.SetVector(ownedSH[term],packed.GetVectorArray(nativeSH[term])[0]);
            guideMesh.sharedMaterial=contaminated;var wrongRoom=Picture("guide-negative-other-room");guideMesh.sharedMaterial=remoteGuide;
            Check(WholeDifference(observerGuide,wrongRoom)>.5f,"negative original author-room replay visibly contaminates observer pixels");negatives++;
            UnityEngine.Object.DestroyImmediate(contaminated);
            remoteGuide.SetFloat("_TownTransparent",0);var opaqueGuide=Picture("guide-negative-opaque");
            Check(WholeDifference(observerGuide,opaqueGuide)>1f,"guide negative alpha removal is visibly opaque");negatives++;
            remoteGuide.SetFloat("_TownTransparent",1);
            var undeclared=new MaterialPropertyBlock();undeclared.SetFloat("_UnknownOutput",1);guideMesh.SetPropertyBlock(undeclared);
            bool rejected=false;try{GloomhavenVR.Net.TownServices.ActualMeshCapture.Capture(guideMesh,assets);}catch(System.IO.InvalidDataException){rejected=true;}
            Check(rejected,"actual mesh capture still rejects undeclared shader property blocks");negatives++;
            guideMesh.SetPropertyBlock(null);
            guideMesh.sharedMaterial=guideMaterial;
            GloomhavenVR.Net.TownServices.TownServiceMaterial.Release(remoteGuide);
            guideMaterial.SetFloat("_TownVisibility",.77f);presentation.BindEnvironment(guideRoot.transform,guideRoot.transform,in state);
            Check(guideMaterial.GetFloat("_TownVisibility")==.77f,"local mesh uniform binding preserves controller-owned dissolve updates");
        }
        TownLightingEditorLifetime.Flush();UnityEngine.Object.DestroyImmediate(guideRoot);UnityEngine.Object.DestroyImmediate(guideMaterial);UnityEngine.Object.DestroyImmediate(originalGuide);
        camera.backgroundColor=Color.black;RenderSettings.ambientProbe=default;RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientIntensity=0;
        var bundle = AssetBundle.LoadFromFile(File.ReadAllText(Path.Combine(output, "bundle-path.txt")).Trim());
        Debug.Log("Town lighting fixture loaded actual bundle");
        foreach (string npc in new[] { "merchant", "priestess", "enchantress" })
        {
            var root = UnityEngine.Object.Instantiate(bundle.LoadAsset<GameObject>("assets/bundle/townservices/prefabs/town" + npc + ".prefab"));
            foreach (var transform in root.GetComponentsInChildren<Transform>(true)) transform.gameObject.layer = VRLayers.ModLayer;
            var actor = root.transform.Find("Actor");
            var skins = new List<Material>();
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.transform.IsChildOf(actor)) renderer.enabled = false;
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    materials[i] = new Material(materials[i]);
                    string shaderName = materials[i].shader.name;
                    if (shaderName == "GloomhavenVR/TownNpc") { materials[i].shader = current; skins.Add(materials[i]); }
                    else if (shaderName == "GloomhavenVR/TownEye" || shaderName == "GloomhavenVR/TownCornea")
                        materials[i].shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/" + shaderName.Substring(shaderName.IndexOf('/') + 1) + ".shader");
                }
                renderer.sharedMaterials = materials;
            }
            var skin = root.GetComponentInChildren<SkinnedMeshRenderer>();
            var block = new MaterialPropertyBlock(); block.SetFloat("_TownVisibility", 1); block.SetFloat("_LightingUnrelated", .73f);
            skin.SetPropertyBlock(block);
            Vector3 centre = skin.bounds.center;
            // Compare actual native shader output to the production owner's SH
            // sampler/renderer binder before any competing practicals exist.
            CameraPose(0, 1);
            var ambientOwner=new SphericalHarmonicsL2();
            ambientOwner.AddAmbientLight(new Color(.06f,.08f,.10f));
            ambientOwner.AddDirectionalLight(new Vector3(.8f,.7f,-.2f).normalized,new Color(.12f,.09f,.06f),.6f);
            RenderSettings.ambientMode=AmbientMode.Custom;RenderSettings.ambientIntensity=1;RenderSettings.ambientProbe=ambientOwner;
            Color32[] nativeAmbient=Picture(npc+"-native-ambient");
            using(var presentation=new TownServiceLighting(root.transform,1))
            {
                var authored=new TownActivityState{HasSharedPerformance=true,HasAuthoredFoley=true};
                TownServiceLighting.SampleEnvironment(root.transform,ref authored);
                var nativePacking=new MaterialPropertyBlock();nativePacking.CopySHCoefficientArraysFrom(new[]{ambientOwner});
                presentation.BindEnvironment(root.transform,root.transform,in authored);
                skin.GetPropertyBlock(block);
                string[] originalNames={"unity_SHAr","unity_SHAg","unity_SHAb","unity_SHBr","unity_SHBg","unity_SHBb","unity_SHC"};
                string[] sharedNames={"_TownAmbientAr","_TownAmbientAg","_TownAmbientAb","_TownAmbientBr","_TownAmbientBg","_TownAmbientBb","_TownAmbientC"};
                for(int term=0;term<7;term++)
                    Check(Vector4.Distance(nativePacking.GetVectorArray(originalNames[term])[0],block.GetVector(sharedNames[term]))<.00001f,
                        npc+" authored probe packing matches Unity's actual native packing term "+term);
                Color32[] ownerAmbient=Picture(npc+"-local-owner-ambient");
                Check(Difference(nativeAmbient,ownerAmbient)<.5f,npc+" local probe preserves native owner pixels");
                var ambientObserver=new SphericalHarmonicsL2();ambientObserver.AddAmbientLight(new Color(.6f,0,0));
                RenderSettings.ambientProbe=ambientObserver;
                // The independent expected image is Unity's unmodified native
                // shader/probe path, using the same current shipping geometry.
                foreach(Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    var local=new MaterialPropertyBlock();renderer.GetPropertyBlock(local);
                    local.SetFloat("_TownSharedAmbient",0);local.SetFloat("_TownSharedKey",0);renderer.SetPropertyBlock(local);
                }
                Color32[] nativeObserver=Picture(npc+"-native-observer-ambient");
                var localState=new TownActivityState{HasSharedPerformance=true,HasAuthoredFoley=true};
                TownServiceLighting.SampleEnvironment(root.transform,ref localState);
                presentation.BindEnvironment(root.transform,root.transform,in localState);
                Color32[] observerAmbient=Picture(npc+"-local-observer-ambient");
                Check(Difference(nativeObserver,observerAmbient)<.5f,npc+" local observer binding matches native local ambient pixels");
                Check(Difference(ownerAmbient,observerAmbient)>.5f,npc+" own room affects the real local actor");
                presentation.BindEnvironment(root.transform,root.transform,in authored);
                Color32[] wrongRoom=Picture(npc+"-negative-other-room-ambient");
                Check(Difference(observerAmbient,wrongRoom)>.5f,npc+" negative other-player probe visibly contaminates local actor pixels");negatives++;
                presentation.BindEnvironment(root.transform,root.transform,in localState);
                Check(Difference(observerAmbient,Picture(npc+"-observer-after-remote-switch"))<.1f,
                    npc+" retaining the observer's own sample isolates final pixels from another room");
                foreach(Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    var restored=new MaterialPropertyBlock();renderer.GetPropertyBlock(restored);
                    restored.SetFloat("_TownSharedAmbient",0);restored.SetFloat("_TownSharedKey",0);renderer.SetPropertyBlock(restored);
                }
            }
            TownLightingEditorLifetime.Flush();RenderSettings.ambientProbe=default;RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientIntensity=0;
            var lamps = new List<Light>();
            for (int i = 0; i < 6; i++)
            {
                float angle = i * Mathf.PI / 3;
                lamps.Add(Lamp(centre + new Vector3(Mathf.Cos(angle) * .9f, .15f, Mathf.Sin(angle) * .9f)));
            }
            // These real actor bounds cross a lamp-ranking tie with only 0.1 mm of
            // movement. The camera follows identically, eliminating silhouette drift.
            foreach (bool legacy in new[] { true, false })
            {
                Debug.Log("Town lighting sequence: " + npc + " legacy=" + legacy);
                foreach (var material in skins) material.shader = legacy ? old : current;
                Color32[] previous = null, before = null, after = null; float peak = 0;
                for (int frame = 0; frame <= 40; frame++)
                {
                    float x = (frame - 20) * .0001f;
                    root.transform.position = new Vector3(x, 0, 0); CameraPose(x, 1);
                    yield return null; var pixels = Picture();
                    if (previous != null)
                    {
                        float difference = Difference(previous, pixels);
                        if (difference > peak) { peak = difference; before = previous; after = pixels; }
                    }
                    previous = pixels;
                }
                results.Add(npc + (legacy ? " legacy" : " stable") + " maximum adjacent mean RGB delta=" + peak.ToString("F5") + "/255, 0.1 mm movement");
                Debug.Log(results.Last());
                Save(npc + (legacy ? "-legacy-before" : "-stable-before"), before);
                Save(npc + (legacy ? "-legacy-after" : "-stable-after"), after);
                if (legacy) { Check(peak > 5, npc + " historical shader reproduces visible lamp-list discontinuity"); negatives++; }
                else Check(peak < .5f, npc + " physical lighting varies continuously across the same tie");
            }
            root.transform.position = Vector3.zero; CameraPose(0, 1);
            yield return null; var unit = Picture(npc + "-fixed");
            Check(Brightness(unit) > 2, npc + " actual practicals light the real body");
            var values = new List<Vector3>(); foreach (var light in lamps) { values.Add(light.transform.position); light.transform.position *= 198; light.range *= 198; }
            root.transform.localScale = Vector3.one * 198; CameraPose(0, 198);
            yield return null; var scaled = Picture(npc + "-scale198");
            Check(Difference(unit, scaled) < 1, npc + " actual game world scale preserves lighting");
            root.transform.localScale = Vector3.one;
            for (int i = 0; i < lamps.Count; i++) { lamps[i].transform.position = values[i]; lamps[i].range /= 198; }
            CameraPose(0, 1, -.032f); yield return null; var left = Picture(npc + "-left-eye");
            CameraPose(0, 1, .032f); yield return null; var right = Picture(npc + "-right-eye");
            Check(Mathf.Abs(Brightness(left) - Brightness(right)) < 1, npc + " translated stereo views retain lighting energy");
            CameraPose(0, 1);
            // A peer can create the same actual lights in a different arrival order.
            foreach (var lamp in lamps) TownServiceLighting.ForgetPractical(lamp);
            for (int i = lamps.Count - 1; i >= 0; i--) TownServiceLighting.ClaimPractical(lamps[i]);
            yield return null; var reordered = Picture();
            Check(Difference(unit, reordered) < .1f, npc + " peer arrival order does not select another light");
            skin.GetPropertyBlock(block);
            Check(Mathf.Abs(block.GetFloat("_LightingUnrelated") - .73f) < .0001f && block.GetFloat("_TownVisibility") == 1, npc + " unrelated and dissolve property blocks survive binding");
            int originalCap = QualitySettings.pixelLightCount;
            QualitySettings.pixelLightCount = 4; yield return null; var higherCap = Picture();
            Check(Difference(unit, higherCap) < .1f, npc + " local pixel-light budget does not change practical presentation");
            QualitySettings.pixelLightCount = originalCap;
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var enabled = renderers.Select(renderer => renderer.enabled).ToArray();
            // Render both anatomical optical layers without the skin occluding them.
            foreach (string optical in new[] { "GloomhavenVR/TownEye", "GloomhavenVR/TownCornea" })
            {
                Bounds eyeBounds = default; bool any = false;
                foreach (var renderer in renderers)
                {
                    renderer.enabled = renderer.sharedMaterials.Any(material => material.shader.name == optical);
                    if (!renderer.enabled) continue;
                    if (!any) { eyeBounds = renderer.bounds; any = true; } else eyeBounds.Encapsulate(renderer.bounds);
                }
                Check(any, npc + " original optical layer exists: " + optical);
                camera.transform.position = eyeBounds.center + new Vector3(0,0,-.23f); camera.transform.LookAt(eyeBounds.center);
                var glint = Lamp(camera.transform.position + new Vector3(.03f,.03f,0));
                yield return null; var opticalPixels = Picture(npc + "-" + optical.Substring(optical.IndexOf('/') + 1));
                Check(opticalPixels.Count(pixel => pixel.r > 20 || pixel.g > 20 || pixel.b > 20) > 20,
                    npc + " actual globe/cornea receives physical lighting: " + optical);
                Release(glint);
            }
            for (int i = 0; i < renderers.Length; i++) renderers[i].enabled = enabled[i];
            CameraPose(0, 1);
            foreach (var light in lamps) light.enabled = false;
            yield return null; var dark = Picture(npc + "-no-practicals");
            Check(Brightness(dark) < .1f, npc + " no phantom lighting or duplicate SH with lamps disabled");
            foreach (var light in lamps) Release(light);
            Check(Shader.GetGlobalInt("_TownPracticalCount") == 0, npc + " teardown clears shader count immediately");
            UnityEngine.Object.DestroyImmediate(root);
        }
        // Actual owner/visitor registration and teardown, not a copied test binder.
        var host = new GameObject("Station"); var lighting = new TownServiceLighting(host.transform, 1);
        lighting.SetFlame(new Vector3(-.6f,1.3f,0), 0); lighting.SetFlame(new Vector3(.6f,1.3f,0), 1); lighting.SetVisibility(.5f);
        TownServiceLightList.Bind(); var positions = Shader.GetGlobalVectorArray("_TownPracticalPositions");
        Check(Shader.GetGlobalInt("_TownPracticalCount") == 2 && positions[0].x == -.6f && positions[1].x == .6f, "real SetFlame publishes both actual lamp anchors");
        var colours = Shader.GetGlobalVectorArray("_TownPracticalColours");
        Check(Mathf.Abs(colours[0].x - 1.3f) < .001f, "owned visibility scales actual intensity once");
        lighting.Dispose(); UnityEngine.Object.DestroyImmediate(host); TownServiceLightList.Bind();
        Check(Shader.GetGlobalInt("_TownPracticalCount") == 0, "last station disposal clears all owned practicals");
        TownLightingEditorLifetime.Flush();
        var native = new GameObject("Native light").AddComponent<Light>(); native.intensity = .93f; native.renderMode = LightRenderMode.Auto; native.cullingMask = -1;
        var full = new List<Light>(); for (int i = 0; i < TownServiceLightList.Capacity; i++) full.Add(Lamp(new Vector3(i,1,0)));
        Light overflow = Lamp(Vector3.zero); TownServiceLighting.ClaimPractical(overflow);
        Check(VRLog.Warnings == 1, "over-capacity anomaly is bounded and deduplicated");
        TownServiceLightList.Bind(); Check(Shader.GetGlobalInt("_TownPracticalCount") == TownServiceLightList.Capacity, "maximum real lamp capacity is bounded");
        var timer = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++) TownServiceLightList.Bind();
        timer.Stop(); results.Add("32 live lamp CPU binding average=" + (timer.Elapsed.TotalMilliseconds / 1000).ToString("F5") + " ms (Editor/GL; not headset GPU timing)");
        var allocationMethod = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread");
        if (allocationMethod != null)
        {
            var allocated = (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), allocationMethod);
            long start = allocated();
            for (int i = 0; i < 1000; i++) TownServiceLightList.Bind();
            long delta = allocated() - start;
            Check(delta == 0, "steady-state shader binding allocates no managed bytes: " + delta);
            results.Add("Managed allocation: " + delta + " bytes / 1000 full-capacity binds");
        }
        full[2].enabled = false; full[3].gameObject.SetActive(false); full[4].cullingMask = 1; full[5].intensity = 0;
        TownServiceLightList.Bind(); colours = Shader.GetGlobalVectorArray("_TownPracticalColours");
        for (int i = 2; i <= 5; i++) Check(colours[i] == Vector4.zero, "inactive/hidden/out-of-mask lights contribute zero");
        UnityEngine.Object.DestroyImmediate(full[6].gameObject); TownServiceLightList.Bind();
        Check(Shader.GetGlobalVectorArray("_TownPracticalColours")[6] == Vector4.zero, "destroyed Unity lights clear stale shader slots");
        Check(native.intensity == .93f && native.renderMode == LightRenderMode.Auto && native.cullingMask == -1, "native lights remain untouched");
        foreach (var light in full) if (light != null) Release(light); Release(overflow);
        Check(Shader.GetGlobalInt("_TownPracticalCount") == 0, "all capacity slots clear after lifecycle churn");
        File.WriteAllText(Path.Combine(output, "result.txt"), "Town lighting: " + assertions + " assertions; " + negatives + " visual regression controls\n" + string.Join("\n", results));
    }
}
