using System;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Board.Patches;
using GloomhavenVR.Core;
using UnityEditor;
using UnityEngine;

public static class QuestPingStreamsProbe
{
    private static int checks;
    private static readonly List<ParticleSystemVertexStream> original = new List<ParticleSystemVertexStream>
        { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV };
    private static readonly List<ParticleSystemVertexStream> packed = new List<ParticleSystemVertexStream>
        { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV };
    private static void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
    private static bool Streams(ParticleSystemRenderer renderer, List<ParticleSystemVertexStream> wanted)
    {
        var actual = new List<ParticleSystemVertexStream>(); renderer.GetActiveVertexStreams(actual);
        if (actual.Count != wanted.Count) return false;
        for (int i = 0; i < actual.Count; i++) if (actual[i] != wanted[i]) return false;
        return true;
    }
    public static void Run()
    {
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        EditorApplication.update += WaitForPlay; EditorApplication.EnterPlaymode();
    }
    private static void WaitForPlay()
    {
        if (!EditorApplication.isPlaying) return;
        EditorApplication.update -= WaitForPlay;
        try
        {
            if (Environment.GetEnvironmentVariable("QUEST_PING_AUTOPLAY_ONLY") != "1") { Probe(); Shield(); QuestNativeFxLayoutPose.Run(); }
            QuestPingAutoClock.Begin();
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
    private static Color32[] Pixels(Camera camera, RenderTexture target)
    {
        camera.Render(); RenderTexture previous = RenderTexture.active;
        var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false, true);
        try { RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply(false); return image.GetPixels32(); }
        finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(image); }
    }
    private static void Save(Color32[] pixels, string path)
    {
        var texture = new Texture2D(128, 128, TextureFormat.RGBA32, false, true);
        texture.SetPixels32(pixels); texture.Apply(false); File.WriteAllBytes(path, texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture);
    }
    private static void Shield()
    {
        var prefab = Resources.Load<GameObject>("NativeShield"); if (prefab == null) return; int start=checks;
        var effect = UnityEngine.Object.Instantiate(prefab);
        var sources = effect.GetComponentsInChildren<ParticleSystemRenderer>(true);
        Check(sources.Length == 3, "Exact original shield retains its two frame meshes and fog");
        var materials = new Material[sources.Length];
        foreach (var system in effect.GetComponentsInChildren<ParticleSystem>(true))
        { system.useAutoRandomSeed=false; system.randomSeed=21; system.Simulate(.3f,false,true,true); }
        for (int i=0;i<sources.Length;i++) materials[i]=sources[i].sharedMaterial;
        var owner = new GameObject("native-shield-head"); var camera = owner.AddComponent<Camera>(); camera.enabled=false;
        camera.transform.position=new Vector3(0,2,-5); camera.transform.LookAt(new Vector3(0,1,0));
        camera.orthographic=true; camera.orthographicSize=2; camera.aspect=1; camera.nearClipPlane=.05f; camera.farClipPlane=20;
        camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.black;
        var target=new RenderTexture(128,128,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);target.Create();camera.targetTexture=target;
        var before=Pixels(camera,target); QuestStandalonePlatform.Enabled=true; VRCameraPolicy.AllowedHead=camera;
        QuestPingStreams.AttachShield(effect);
        Check(effect.GetComponentsInChildren<MeshRenderer>(true).Length==2, "Only the two exact original additive shield meshes receive uploads");
        var after=Pixels(camera,target); int max=0,visible=0;
        for(int i=0;i<before.Length;i++)
        { if(before[i].r>2 || before[i].g>2 || before[i].b>2)visible++;
          max=Math.Max(max,Math.Max(Math.Abs(before[i].r-after[i].r),Math.Max(Math.Abs(before[i].g-after[i].g),Math.Abs(before[i].b-after[i].b)))); }
        Check(visible>50,"Native original shield texture contributes visible pixels");
        Check(max<=2,"Explicit shield mesh upload preserves original pixels without duplicate contribution");
        for(int i=0;i<sources.Length;i++) Check(sources[i].sharedMaterial==materials[i] && !sources[i].forceRenderingOff,"Shield source material/visibility return to native ownership");
        Save(before,"ProbeOutput/shield-before.png");Save(after,"ProbeOutput/shield-after.png");
        effect.SetActive(false);
        for(int i=0;i<sources.Length;i++) Check(sources[i].sharedMaterial==materials[i],"Pooled shield retains its original materials");
        QuestPingStreams.ResetAll();UnityEngine.Object.DestroyImmediate(effect);UnityEngine.Object.DestroyImmediate(owner);target.Release();UnityEngine.Object.DestroyImmediate(target);
        File.WriteAllText("ProbeOutput/shield-results.json","{\"assertions\":"+(checks-start)+",\"visiblePixels\":"+visible+",\"pixelMaxDelta\":"+max+",\"hardwareVerified\":false}\n");
        Debug.Log("PASS native shield pixels="+visible+" maximumDelta="+max);
    }
    private static void Probe()
    {
        var shieldPrefab = Resources.Load<GameObject>("NativeShield");
        if (shieldPrefab != null)
            foreach (var shield in shieldPrefab.GetComponentsInChildren<ParticleSystemRenderer>(true))
                Debug.Log("NATIVE_SHIELD name="+shield.name+" shader="+shield.sharedMaterial.shader.name+" mode="+shield.renderMode+" gpu="+shield.enableGPUInstancing+" supports="+shield.supportsMeshInstancing+" materialInstancing="+shield.sharedMaterial.enableInstancing+" mesh="+(shield.mesh != null ? shield.mesh.name : "none"));
        GameObject prefab = Resources.Load<GameObject>("NativePing"); Check(prefab != null, "Exact original ping prefab imports");
        GameObject ping = UnityEngine.Object.Instantiate(prefab);
        var renderers = ping.GetComponentsInChildren<ParticleSystemRenderer>(true); var systems = ping.GetComponentsInChildren<ParticleSystem>(true);
        Check(renderers.Length == 2 && systems.Length == 2, "Both original ping particle consumers are retained");
        foreach (ParticleSystem system in systems)
        {
            Check(system.main.cullingMode == ParticleSystemCullingMode.Pause, "Original ping authors visibility-based simulation pause");
            system.useAutoRandomSeed = false; system.randomSeed = 21; system.Simulate(.5f, false, true, true);
        }
        var materialIds = new int[2]; var textures = new Texture[2]; var bounds = new Bounds[2]; var particleCounts = new int[2]; var colors = new Color[2];
        for (int i = 0; i < 2; i++)
        {
            Check(Streams(renderers[i], original), "Native ping source has its authored stream layout");
            materialIds[i] = renderers[i].sharedMaterial.GetInstanceID(); textures[i] = renderers[i].sharedMaterial.mainTexture;
            bounds[i] = renderers[i].bounds; particleCounts[i] = systems[i].particleCount;
            colors[i] = renderers[i].sharedMaterial.HasProperty("_TintColor") ? renderers[i].sharedMaterial.GetColor("_TintColor") : Color.white;
        }
        var owner = new GameObject("ping-camera"); Camera camera = owner.AddComponent<Camera>(); camera.enabled = false;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
        camera.transform.position = new Vector3(0, 5, -5); camera.transform.LookAt(Vector3.zero);
        camera.nearClipPlane = .05f; camera.farClipPlane = 20; camera.orthographic = true; camera.orthographicSize = 2;
        var target = new RenderTexture(128, 128, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear); target.Create(); camera.targetTexture = target;
        VRCameraPolicy.AllowedHead = camera; Color32[] before = Pixels(camera, target);
        QuestStandalonePlatform.Enabled = false; QuestPingStreams.Attach(ping);
        Check(ping.GetComponent<QuestPingStreams>() == null, "Desktop ping source receives no Quest component"); QuestStandalonePlatform.Enabled = true;
        foreach (var source in renderers) Debug.Log("NATIVE_ADMISSION name="+source.name+" mode="+source.renderMode+" block="+source.HasPropertyBlock()+" mainTex="+source.sharedMaterial.HasProperty("_MainTex")+" force="+source.forceRenderingOff+" shader="+source.sharedMaterial.shader.name+" particles="+source.GetComponent<ParticleSystem>().particleCount);
        var managerOwner = new GameObject("exact-native-manager-boundary"); var manager = managerOwner.AddComponent<PingManager>(); manager.Assigned(ping); QuestPingStreams.OnPingShown(manager);
        Check(ping.GetComponent<QuestPingStreams>() != null, "Original assigned-ping owner admits its exact scoped consumer");
        foreach (var renderer in renderers) Check(Streams(renderer, original), "Original ping retains its authored native streams");
        bool nativeVisible = false;
        Camera.CameraCallback inspect = active => {
            if (active != camera) return;
            foreach (ParticleSystemRenderer renderer in renderers)
            {
                Check(renderer.sharedMaterial != null, "Actual head draw has a native material");
                if (Environment.GetEnvironmentVariable("QUEST_PING_PIXEL_CONTROL") != "1" && renderer.GetComponent<ParticleSystem>().particleCount > 0) Check(renderer.sharedMaterial.mainTexture == Texture2D.blackTexture, "Only native material texture is suppressed during head submission");
            }
            foreach (ParticleSystemRenderer renderer in renderers) Check(!renderer.forceRenderingOff && renderer.enabled, "Native ping renderer remains enabled during actual head culling");
            nativeVisible = true;
        };
        Camera.onPreCull += inspect; Color32[] after = Pixels(camera, target); Camera.onPreCull -= inspect;
        Check(nativeVisible, "The actual head callback observes the native ping draw");
        Check(ping.GetComponentsInChildren<MeshRenderer>(true).Length == 2, "Exactly the two original billboards use explicit upload meshes");
        foreach (var renderer in renderers) Check(!renderer.forceRenderingOff && renderer.enabled, "Native visibility remains owned by the enabled original renderer");
        for (int i = 0; i < 2; i++)
        {
            Check(renderers[i].sharedMaterial.GetInstanceID() == materialIds[i] && renderers[i].sharedMaterial.mainTexture == textures[i], "Original ping material and texture identity remain unchanged");
            Check(renderers[i].bounds == bounds[i] && systems[i].particleCount == particleCounts[i] && (!renderers[i].sharedMaterial.HasProperty("_TintColor") || renderers[i].sharedMaterial.GetColor("_TintColor") == colors[i]), "Original ping simulation, geometry bounds and tint remain unchanged");
        }
        long error = 0; int visible = 0, max = 0;
        for (int i = 0; i < before.Length; i++)
        {
            if (before[i].r > 2 || before[i].g > 2 || before[i].b > 2) visible++;
            foreach (int delta in new[] { Math.Abs(before[i].r-after[i].r), Math.Abs(before[i].g-after[i].g), Math.Abs(before[i].b-after[i].b) }) { error += delta; max = Math.Max(max, delta); }
        }
        Check(visible > 50, "Actual original ping particles contribute visible native pixels"); Directory.CreateDirectory("ProbeOutput");
        Save(before, "ProbeOutput/before.png"); Save(after, "ProbeOutput/after.png");
        Check(max <= 2 && error < before.Length * 3, "Native upload draws the original effect once without duplicate contribution");
        ping.SetActive(false); foreach (var renderer in renderers) Check(Streams(renderer, original), "Pooling retains exact original ping streams");
        ping.SetActive(true); foreach (var renderer in renderers) Check(Streams(renderer, original), "Pooled native ping retains its authored stream request");
        var blocker=GameObject.CreatePrimitive(PrimitiveType.Cube);blocker.transform.position=new Vector3(-.3f,.5f,-.4f);blocker.transform.localScale=new Vector3(.65f,2,2);
        var black=new Material(Shader.Find("Unlit/Color"));black.color=Color.black;blocker.GetComponent<MeshRenderer>().sharedMaterial=black;
        QuestStandalonePlatform.Enabled=false;var nativeOccluded=Pixels(camera,target);
        QuestStandalonePlatform.Enabled=true;var uploadOccluded=Pixels(camera,target);int occludedVisible=0,occlusionDelta=0;
        for(int i=0;i<nativeOccluded.Length;i++)
        {if(nativeOccluded[i].r>2 || nativeOccluded[i].g>2 || nativeOccluded[i].b>2)occludedVisible++;
         occlusionDelta=Math.Max(occlusionDelta,Math.Max(Math.Abs(nativeOccluded[i].r-uploadOccluded[i].r),Math.Max(Math.Abs(nativeOccluded[i].g-uploadOccluded[i].g),Math.Abs(nativeOccluded[i].b-uploadOccluded[i].b))));}
        Check(occludedVisible<visible,"Actual opaque geometry occludes the original native effect");
        Check(occlusionDelta<=2,"Explicit native upload retains ordinary opaque depth occlusion");
        UnityEngine.Object.DestroyImmediate(blocker);UnityEngine.Object.DestroyImmediate(black);
        Material nativeMaterial = renderers[0].sharedMaterial; Shader nativeShader = nativeMaterial.shader;
        // An unrelated camera must see the untouched original native material;
        // owned upload meshes are disabled before its culling starts.
        VRCameraPolicy.AllowedHead = null;
        Color32[] foreignView = Pixels(camera, target);
        Check(renderers[0].sharedMaterial == nativeMaterial, "Foreign cameras never retain the private visibility material");
        foreach (MeshRenderer upload in ping.GetComponentsInChildren<MeshRenderer>(true)) Check(!upload.enabled, "Foreign cameras do not duplicate the native effect");
        VRCameraPolicy.AllowedHead = camera;
        ping.SetActive(false); nativeMaterial.shader = Shader.Find("Sprites/Default"); ping.SetActive(true);
        foreach(var system in systems)system.Simulate(.5f,false,true,true);
        bool changedNativeRetained=false;
        Camera.CameraCallback observeChanged = active => { if (active==camera) changedNativeRetained=renderers[0].sharedMaterial==nativeMaterial; };
        Camera.onPreCull+=observeChanged; Pixels(camera,target); Camera.onPreCull-=observeChanged;
        Check(changedNativeRetained,"Changed native shader retains its native draw during head culling");
        Check(Streams(renderers[0], original) && !renderers[0].forceRenderingOff, "Changed native contracts retain authored streams");
        ping.SetActive(false); nativeMaterial.shader = nativeShader; ping.SetActive(true);
        Check(Streams(renderers[0], original), "Original shader can resume its qualified native draw");
        var originalProperties=new MaterialPropertyBlock();renderers[0].GetPropertyBlock(originalProperties);
        var textureWriter=new MaterialPropertyBlock();renderers[0].GetPropertyBlock(textureWriter);textureWriter.SetTexture("_MainTex",Texture2D.whiteTexture);renderers[0].SetPropertyBlock(textureWriter);
        bool textureOwnerRetained=false;
        Camera.CameraCallback textureObserver=active=>{if(active==camera)textureOwnerRetained=renderers[0].sharedMaterial==nativeMaterial && !renderers[0].transform.Find("GloomhavenVR.Ping.Upload").GetComponent<MeshRenderer>().enabled;};
        Camera.onPreCull+=textureObserver;Pixels(camera,target);Camera.onPreCull-=textureObserver;
        Check(textureOwnerRetained,"Independent native texture override retains the complete original draw");renderers[0].SetPropertyBlock(originalProperties);
        bool pooledInsideCull=false;
        Camera.CameraCallback poolingObserver=active=>{if(active==camera){ping.SetActive(false);pooledInsideCull=renderers[0].sharedMaterial==nativeMaterial;}};
        Camera.onPreCull+=poolingObserver;Pixels(camera,target);Camera.onPreCull-=poolingObserver;
        Check(pooledInsideCull,"Pooling during an active head lease restores the original material immediately");ping.SetActive(true);
        renderers[0].forceRenderingOff = true; Pixels(camera, target);
        Check(renderers[0].forceRenderingOff, "Foreign visibility mask is not cleared by any ping callback"); renderers[0].forceRenderingOff = false;
        var foreignStreams = new List<ParticleSystemVertexStream> { ParticleSystemVertexStream.Position }; renderers[0].SetActiveVertexStreams(foreignStreams);
        QuestPingStreams.ResetAll(); Check(Streams(renderers[0], foreignStreams), "A later foreign vertex writer retains ownership during reset");
        Check(Streams(renderers[1], original), "Reset restores the untouched original ping stream source");
        UnityEngine.Object.DestroyImmediate(ping); UnityEngine.Object.DestroyImmediate(managerOwner);
        var foreign = new GameObject("foreign-particle"); foreign.AddComponent<ParticleSystem>(); ParticleSystemRenderer other = foreign.GetComponent<ParticleSystemRenderer>();
        other.sharedMaterial = new Material(Shader.Find("Sprites/Default")); other.SetActiveVertexStreams(original); QuestPingStreams.Attach(foreign);
        Check(Streams(other, original), "Foreign particle materials retain their authored streams");
        Check(foreign.GetComponentsInChildren<MeshRenderer>(true).Length==0,"Foreign particle materials receive no upload geometry");
        UnityEngine.Object.DestroyImmediate(other.sharedMaterial); UnityEngine.Object.DestroyImmediate(foreign);
        UnityEngine.Object.DestroyImmediate(owner); target.Release(); UnityEngine.Object.DestroyImmediate(target);
        File.WriteAllText("ProbeOutput/results.json", "{\"assertions\":" + checks + ",\"visiblePixels\":" + visible + ",\"pixelMaxDelta\":" + max + ",\"hardwareVerified\":false}\n");
        Debug.Log("PASS Quest ping streams: " + checks + " assertions, actual native pixels=" + visible);
    }
}
