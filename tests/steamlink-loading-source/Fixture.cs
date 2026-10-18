using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GloomhavenVR.Core;
using GloomhavenVR.Core.SteamLink;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class LoadingSourceFixture
{
    [Serializable] private sealed class Report
    {
        public bool passed, actual_camera_raster, actual_production_callbacks;
        public bool xr_executed=false, connection_detection_executed=false, mixed_reality_owner_executed=false;
        public bool native_receiver_executed=false, game_wide_coverage_proven=false;
        public string unity="", backend="", device="", color_space="";
        public int checks, renders, resumes, normal_closes, fault_refusals, guard_cells;
        public List<string> cases=new List<string>();
    }
    private static readonly FieldInfo CurrentField=typeof(SteamLinkSceneRuntime).GetField("_output",BindingFlags.Static|BindingFlags.NonPublic)!;
    private static readonly FieldInfo MaterialsField=typeof(SceneHeadOutput).GetField("_materials",BindingFlags.Instance|BindingFlags.NonPublic)!;
    private static readonly List<Object> owned=new List<Object>();
    private static readonly Color Background=new Color(40f/255,20f/255,10f/255,0);
    private static readonly Color Opaque=new Color(77f/255,102f/255,204f/255,0);
    private static readonly Color Glass=new Color(204f/255,51f/255,102f/255,.5f);
    private static readonly int[] Codes={111,208,128,104,227,251,255,193,102,168,15,90};
    private static Report result=new Report();
    private static string output="";
    private static Material probe=null!, opaque=null!, glass=null!;
    private static Renderer glassRenderer=null!;
    private static RenderTexture probeTarget=null!;
    private static Texture2D read=null!;
    private static SceneHeadOutput? Current=>(SceneHeadOutput?)CurrentField.GetValue(null);
    private static void Check(bool value,string why)
    { result.checks++;if(!value)throw new InvalidOperationException(why); }
    private static T Keep<T>(T value)where T:Object{owned.Add(value);return value;}
    private static void Near(float value,float expected,string why,float tolerance=2.1f/255)
        =>Check(!float.IsNaN(value)&&Mathf.Abs(value-expected)<=tolerance,why+": "+value+" expected "+expected);
    private static void Stage(string name){result.cases.Add(name);File.AppendAllText(Path.Combine(output,"stages.log"),name+"\n");}
    private static Camera CameraFor(string name)
    {
        var go=Keep(new GameObject(name));var camera=go.AddComponent<Camera>();
        camera.enabled=true;camera.renderingPath=RenderingPath.Forward;camera.allowHDR=false;camera.allowMSAA=false;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Background;
        camera.orthographic=true;camera.orthographicSize=1;camera.aspect=1;camera.nearClipPlane=.1f;camera.farClipPlane=10;
        camera.transform.position=new Vector3(0,0,-2);camera.cullingMask=1<<30;
        var target=Keep(new RenderTexture(256,256,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear));
        target.filterMode=FilterMode.Point;Check(target.Create(),"Created actual camera target");camera.targetTexture=target;
        return camera;
    }
    private static Renderer Surface(string name,float x,Material material)
    {
        var go=Keep(GameObject.CreatePrimitive(PrimitiveType.Quad));go.name=name;go.layer=30;
        go.transform.position=new Vector3(x,0,0);go.transform.localScale=new Vector3(.6f,1.2f,1);
        var renderer=go.GetComponent<Renderer>();renderer.sharedMaterial=material;return renderer;
    }
    private static Color At(RenderTexture target,float x,float y)
    {
        var active=RenderTexture.active;bool srgb=GL.sRGBWrite;
        try
        {
            GL.sRGBWrite=false;
            // Probe an exact destination texel centre, not an interpolated guard edge.
            float u=(Mathf.Floor(x*target.width)+.5f)/target.width;
            float v=1-(Mathf.Floor(y*target.height)+.5f)/target.height;
            probe.SetVector("_PointUV",new Vector4(u,v,0,0));
            Graphics.Blit(target,probeTarget,probe);RenderTexture.active=probeTarget;
            read.ReadPixels(new Rect(0,0,32,32),0,0,false);read.Apply(false,false);return read.GetPixel(16,16);
        }
        finally{GL.sRGBWrite=srgb;RenderTexture.active=active;}
    }
    private static Color AtlasScene(Camera camera,float x,float y)
        =>At(camera.targetTexture,(32+x*2048)/2112,(352+y*2048)/2752);
    private static float AtlasMask(Camera camera,float x,float y)
    {
        int tile=Math.Min(3,(int)(y*1024)/256);
        return At(camera.targetTexture,((tile%2)*1056+16+x*1024)/2112,
            ((tile<2?48:2448)+y*1024-tile*256)/2752).g;
    }
    private static void Packed(Camera camera,string label)
    {
        int before=LoadingSourceBoundary.EyeReads;
        camera.Render();result.renders++;
        Check(LoadingSourceBoundary.EyeReads==before+1,label+": actual production OnRenderImage entered once");
        Check(SteamLinkSceneRuntime.Active&&Current!.Ready,label+": output remains ready after raster");
        for(int i=0;i<12;i++)
        {
            Color cell=At(camera.targetTexture,(80+i*160f)/2112,2416f/2752);
            Near(cell.r,Codes[i]/255f,label+": guard R");Near(cell.g,Codes[i]/255f,label+": guard G");
            Near(cell.b,Codes[i]/255f,label+": guard B");result.guard_cells++;
        }
        Color left=AtlasScene(camera,.25f,.5f),right=AtlasScene(camera,.75f,.5f),clear=AtlasScene(camera,.95f,.1f);
        Near(left.r,Opaque.r,label+": opaque E.r");Near(left.g,Opaque.g,label+": opaque E.g");Near(left.b,Opaque.b,label+": opaque E.b");
        Near(AtlasMask(camera,.25f,.5f),1,label+": actual depth stamps opaque A1");
        Near(AtlasMask(camera,.75f,.5f),Glass.a,label+": real shipping transparent variant supplies fractional A");
        Near(right.r,Glass.r*.5f+Background.r*.5f,label+": fractional contributed RGB");
        Near(clear.r,Background.r,label+": positive RGB at zero coverage preserved");
        Near(AtlasMask(camera,.95f,.1f),0,label+": transparent clear stays A0");
        Check(glassRenderer.sharedMaterial==glass,label+": actual material callback restored original material");
        var materials=(SceneTransparentMaterials)MaterialsField.GetValue(Current!)!;
        Check(materials.CachedMaterials==1&&materials.Ready,label+": real material scope has the known overlay");
        result.actual_camera_raster=result.actual_production_callbacks=true;
    }
    private static void Ordinary(Camera camera,string label,bool warming=false)
    {
        int before=LoadingSourceBoundary.EyeReads;camera.Render();result.renders++;
        Check(LoadingSourceBoundary.EyeReads==before+(warming?1:0),label+": ordinary image has expected current-eye admission read");
        Color clear=At(camera.targetTexture,.95f,.1f),left=At(camera.targetTexture,.25f,.5f);
        Near(clear.r,Background.r,label+": ordinary background RGB");Near(clear.a,0,label+": original ordinary alpha");
        Near(left.r,Opaque.r,label+": ordinary surface visible");
    }
    private static SceneHeadOutput Enter(Camera camera,string why)
    {
        VRCameraPolicy.AllowedHead=camera;
        Check(SteamLinkSceneRuntime.TryEnter(camera),why);
        Check(Current!=null&&Current.Ready&&string.IsNullOrEmpty(Current.Failure),why+": ready without failure");
        Check(camera.GetCommandBuffers(CameraEvent.AfterForwardOpaque).Length==1,why+": one exact owned coverage command buffer");
        // Edit-mode Camera.Render has no XR frame cadence. Supply explicit frame
        // grouping, then execute two actual production callbacks/raster draws.
        LoadingSourceBoundary.FrameCount++;
        var savedEye=LoadingSourceBoundary.CurrentEye;
        LoadingSourceBoundary.CurrentEye=Camera.MonoOrStereoscopicEye.Left;Ordinary(camera,why+": warmup left",true);
        LoadingSourceBoundary.CurrentEye=Camera.MonoOrStereoscopicEye.Right;Ordinary(camera,why+": warmup right",true);
        LoadingSourceBoundary.CurrentEye=savedEye;
        LoadingSourceBoundary.FrameCount++;
        return Current!;
    }
    public static IEnumerator Run(string evidence,string bank)
    {
        output=evidence;result.unity=Application.unityVersion;result.backend=SystemInfo.graphicsDeviceType.ToString();
        result.device=SystemInfo.graphicsDeviceName;result.color_space=QualitySettings.activeColorSpace.ToString();
        try
        {
            Stage("initialize actual source components and shipping bank");
            Check(result.backend!="Null"&&result.color_space=="Gamma","Actual Gamma GPU required");
            ScenePayloadAssets.Load(bank);
            Check(ScenePayloadAssets.TryGet(out _,out _,out _),"Exact shipping pack/coverage shaders supported");
            probe=Keep(new Material(Shader.Find("LoadingSourceFixture/Probe")));
            probeTarget=Keep(new RenderTexture(32,32,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear));
            Check(probeTarget.Create(),"Small numeric probe target");read=Keep(new Texture2D(32,32,TextureFormat.RGBAFloat,false,true));
            opaque=Keep(new Material(Shader.Find("LoadingSourceFixture/Opaque")));opaque.SetColor("_Color",Opaque);
            glass=Keep(new Material(Shader.Find("GloomhavenVR/Overlay")));glass.SetColor("_Color",Glass);glass.SetFloat("_VertexColor",0);
            Surface("Owned opaque depth",-.5f,opaque);glassRenderer=Surface("Owned real overlay",.5f,glass);
            var first=CameraFor("Original loading head");
            SteamLinkSceneRuntime.Reset();
            Stage("initial left eye real raster");var previous=Enter(first,"Initial scene output prepares");Packed(first,"initial left");

            Stage("normal GameObject disable and same-camera re-enable");
            first.gameObject.SetActive(false);
            Check(!previous.Ready&&string.IsNullOrEmpty(previous.Failure),"Real OnDisable closes normally without failure");
            Check(first.GetCommandBuffers(CameraEvent.AfterForwardOpaque).Length==0,"Disable detaches coverage immediately");result.normal_closes++;
            first.gameObject.SetActive(true);
            var resumed=Enter(first,"Normal camera re-enable must prepare a fresh scene output");
            Check(!ReferenceEquals(previous,resumed),"Recovery replaces normally closed component");result.resumes++;
            yield return null;Packed(first,"same camera resumed");

            Stage("zero-camera loading gap");previous=Current!;
            Check(!SteamLinkSceneRuntime.TryEnter(null),"No camera retains no source");
            Check(!previous.Ready&&first.GetCommandBuffers(CameraEvent.AfterForwardOpaque).Length==0,"Zero-camera gap releases old source");
            Enter(first,"Same camera returns after zero-camera gap");result.resumes++;
            yield return null;Packed(first,"after zero-camera gap");

            Stage("master MR OFF and ON without menu cancellation");previous=Current!;
            MixedReality.Enabled.Value=false;Ordinary(first,"master OFF");
            Check(!previous.Ready&&string.IsNullOrEmpty(previous.Failure),"Master OFF closes ownership without a render fault");
            SteamLinkSceneRuntime.Exit();MixedReality.Enabled.Value=true;
            Enter(first,"Master ON prepares current camera");result.resumes++;
            yield return null;Packed(first,"master ON resumed");

            Stage("actual camera replacement and destruction");previous=Current!;
            var second=CameraFor("Replacement loading head");Enter(second,"Replacement camera prepares");
            Check(!previous.Ready&&first.GetCommandBuffers(CameraEvent.AfterForwardOpaque).Length==0,"Camera replacement releases old commands");
            Object.DestroyImmediate(first.gameObject);result.resumes++;
            LoadingSourceBoundary.CurrentEye=Camera.MonoOrStereoscopicEye.Right;
            yield return null;Packed(second,"replacement right eye");
            previous=Current!;Object.DestroyImmediate(second.gameObject);
            Check(!previous.Ready,"Actual camera destruction closes source");
            Check(!SteamLinkSceneRuntime.TryEnter(null)&&!SteamLinkSceneRuntime.Active,"Destroyed camera gap has no stale source");
            var third=CameraFor("Post-destruction loading head");Enter(third,"Post-destruction camera prepares");result.resumes++;
            yield return null;Packed(third,"post destruction resumed");

            Stage("actual nonempty attachment fault stays refused");previous=Current!;
            third.allowHDR=true;Ordinary(third,"HDR attachment fault");
            Check(!previous.Ready&&!string.IsNullOrEmpty(previous.Failure),"Actual OnPreCull attachment fault records a nonempty failure");
            third.allowHDR=false;
            Check(!SteamLinkSceneRuntime.TryEnter(third),"Restoring attachments alone must not reopen failed head");result.fault_refusals++;
            SteamLinkSceneRuntime.Reset();Enter(third,"Explicit reset recovers actual failed head");
            yield return null;Packed(third,"after explicit fault reset");

            Stage("temporary mono XR render closes without poisoning the returning head");previous=Current!;
            LoadingSourceBoundary.CurrentEye=Camera.MonoOrStereoscopicEye.Mono;
            third.Render();result.renders++;
            Check(!previous.Ready&&string.IsNullOrEmpty(previous.Failure),"Temporary mono closes without an attachment fault");
            LoadingSourceBoundary.CurrentEye=Camera.MonoOrStereoscopicEye.Left;
            Enter(third,"Stereo recovery after temporary mono needs no explicit reset");result.resumes++;
            yield return null;Packed(third,"final left resumed");
            Check(result.resumes==6&&result.fault_refusals==1&&result.guard_cells==96,"All bounded lifecycle/raster cases executed");
            result.passed=true;
        }
        finally
        {
            SteamLinkSceneRuntime.Reset();VRCameraPolicy.AllowedHead=null;
            for(int i=owned.Count-1;i>=0;i--)if(owned[i])Object.DestroyImmediate(owned[i]);
            ScenePayloadAssets.Close();
            File.WriteAllText(Path.Combine(output,"gpu-result.json"),JsonUtility.ToJson(result,true)+"\n");
        }
    }
}
