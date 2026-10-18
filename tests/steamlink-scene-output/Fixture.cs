using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Core.SteamLink;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

[ExecuteInEditMode]
public sealed class OwnedSceneSink:MonoBehaviour
{
    internal SceneEyeOutput Output;
    public RenderTexture OriginalResolved;
    public Camera.MonoOrStereoscopicEye Label;
    public bool Active=true,Menu,FrameValid=true;
    public int Callbacks,SourceSamples,SourceWidth,SourceHeight;
    public bool SourceBindMS,Restored,SourceSrgb;
    public int SourceGraphicsFormat;
    internal SceneEyeResult Last;
    private void OnRenderImage(RenderTexture source,RenderTexture destination)
    {
        Callbacks++;SourceSamples=source.antiAliasing;SourceBindMS=source.bindTextureMS;
        SourceWidth=source.width;SourceHeight=source.height;SourceSrgb=source.sRGB;SourceGraphicsFormat=(int)source.graphicsFormat;
        Graphics.Blit(source,OriginalResolved); // Independent ordinary resolved source of THIS callback.
        RenderTexture active=RenderTexture.active;bool srgb=GL.sRGBWrite;
        Last=Active&&!Menu&&FrameValid?Output.TryRender(source,destination,Label):SceneEyeResult.UnknownEye;
        Restored=RenderTexture.active==active&&GL.sRGBWrite==srgb;
        if(Last!=SceneEyeResult.Rendered)Graphics.Blit(source,destination);
    }
}

public static class SceneOutputFixture
{
    [Serializable] private sealed class Row
    {
        public int input_width,input_height,target_samples,callback_samples,callbacks;
        public string label="",result="";
    }
    [Serializable] private sealed class Result
    {
        public string profile=SceneAtlasProfile.Profile,unity="",device="",backend="",color_space="";
        public int checks,camera_cases,mask_points,edge_rgb_points,guards,emission_points,original_fallbacks;
        public int actual_msaa_sources,unsupported_sample_counts,partial_geometric_msaa_pixels;
        public int paired_source_draws,paired_requested_samples,paired_actual_samples;
        public bool dynamic_scale_control_supported=false;
        public string mode="full";
        public bool shipping_windows_packshader=false;
        public float max_rgb_error,max_mask_error;
        public bool passed,actual_camera_callbacks=false,xr_callbacks_executed=false;
        public bool game_wide_coverage_proven=false,native_codec_proven=false,passthrough_authorized=false;
        public List<Row> rows=new List<Row>();
    }
    private static readonly int[] Codes={111,208,128,104,227,251,255,193,102,168,15,90};
    private static Result result=new Result();
    private static string output;
    private static AssetBundle shippingBank;
    private static Shader shippingPack;
    private static Material probeMaterial;
    private static RenderTexture probeTarget;
    private static Texture2D read;
    private static bool EdgeMode=>result.mode=="edges"||result.mode=="edge-witness";
    private static void Stage(string value)=>File.AppendAllText(Path.Combine(output,"stages.log"),value+"\n");
    private static void Check(bool value,string reason){result.checks++;if(!value)throw new Exception(reason);}
    private static void Near(float a,float b,string reason,float tolerance=2.1f/255)
    {Check(!float.IsNaN(a)&&!float.IsInfinity(a)&&Mathf.Abs(a-b)<tolerance,reason+": "+a+" wanted "+b);}
    private static Shader Get(string name){Shader s=name==SceneAtlasProfile.ShaderName&&shippingPack?shippingPack:Shader.Find(name);Check(s&&s.isSupported,"shader "+name);return s;}
    private static RenderTexture Surface(int width,int height,int samples=1,RenderTextureFormat format=RenderTextureFormat.ARGB32)
    {
        var rt=new RenderTexture(width,height,24,format,RenderTextureReadWrite.Linear)
        {antiAliasing=samples,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Repeat,useMipMap=false};
        Check(rt.Create(),"RT created");return rt;
    }
    private static Color At(RenderTexture rt,float u,float topDown)
    {
        RenderTexture previous=RenderTexture.active;bool srgb=GL.sRGBWrite;
        try
        {
            GL.sRGBWrite=false;
            probeMaterial.SetVector("_PointUV",new Vector4(u,1-topDown,0,0));
            Graphics.Blit(rt,probeTarget,probeMaterial,0);RenderTexture.active=probeTarget;
            read.ReadPixels(new Rect(0,0,32,32),0,0,false);read.Apply(false,false);
            return read.GetPixel(16,16);
        }
        finally{RenderTexture.active=previous;GL.sRGBWrite=srgb;}
    }
    private static Color Pixel(RenderTexture rt,int x,int y)=>At(rt,(x+.5f)/rt.width,(y+.5f)/rt.height);
    private static float Cell(RenderTexture source,int x,int y)
    {
        x=Math.Max(0,Math.Min(1023,x));y=Math.Max(0,Math.Min(1023,y));
        float sum=0;
        for(int dy=0;dy<2;dy++)for(int dx=0;dx<2;dx++)
            sum+=At(source,(2*x+dx+.5f)/2048,(2*y+dy+.5f)/2048).a;
        return sum*.25f;
    }
    private static float Mask(RenderTexture source,float gx,float gy)
    {
        float x=gx-.5f,y=gy-.5f;int ix=Mathf.FloorToInt(x),iy=Mathf.FloorToInt(y);float fx=x-ix,fy=y-iy;fx=fx*fx*(3-2*fx);fy=fy*fy*(3-2*fy);
        // Independent current spatial weights over individually probed original A.
        return Cell(source,ix,iy)*(1-fx)*(1-fy)+Cell(source,ix+1,iy)*fx*(1-fy)
            +Cell(source,ix,iy+1)*(1-fx)*fy+Cell(source,ix+1,iy+1)*fx*fy;
    }
    private static Vector2Int CarrierPixel(RenderTexture target,float logicalX,float logicalY)
    {
        return new Vector2Int(Mathf.Clamp(Mathf.FloorToInt(logicalX*target.width/2112),0,target.width-1),
            Mathf.Clamp(Mathf.FloorToInt(logicalY*target.height/2752),0,target.height-1));
    }
    private static void ColorCheck(RenderTexture target,RenderTexture original,float x,float y)
    {
        Vector2Int p=CarrierPixel(target,32+x*2048,352+y*2048);
        float px=(p.x+.5f)*2112/target.width,py=(p.y+.5f)*2752/target.height;
        Color actual=Pixel(target,p.x,p.y),wanted=At(original,(px-32)/2048,(py-352)/2048);
        Near(actual.r,wanted.r,"same callback E.r");Near(actual.g,wanted.g,"same callback E.g");Near(actual.b,wanted.b,"same callback E.b");
        result.max_rgb_error=Mathf.Max(result.max_rgb_error,Mathf.Abs(actual.r-wanted.r),Mathf.Abs(actual.g-wanted.g),Mathf.Abs(actual.b-wanted.b));
        result.edge_rgb_points++;
        if(wanted.a<.001f&&wanted.r>.15f){Check(actual.r>.15f,"additive E remains positive at A=0");result.emission_points++;}
    }
    private static void MaskCheck(RenderTexture target,RenderTexture original,int t,float x,float y)
    {
        float ox=t%2*1056+16,oy=t<2?48:2448;
        Vector2Int p=CarrierPixel(target,ox+x,oy+y);
        float px=(p.x+.5f)*2112/target.width,py=(p.y+.5f)*2752/target.height;
        float expected=Mask(original,px-ox,py-oy+t*256);
        Color actual=Pixel(target,p.x,p.y);
        Near(actual.r,expected,"numeric A.r");Near(actual.g,expected,"numeric A.g");Near(actual.b,expected,"numeric A.b");
        result.max_mask_error=Mathf.Max(result.max_mask_error,Mathf.Abs(actual.g-expected));result.mask_points++;
    }
    private static void Guards(RenderTexture target)
    {
        for(int i=0;i<12;i++)
        {
            Vector2Int p=CarrierPixel(target,80+i*160,2416);
            Color value=Pixel(target,p.x,p.y);float expected=Codes[i]/255f;
            Near(value.r,expected,"guard R",.6f/255);Near(value.g,expected,"guard G",.6f/255);Near(value.b,expected,"guard B",.6f/255);result.guards++;
        }
    }
    private static void Ordinary(RenderTexture target,RenderTexture original)
    {
        foreach(Vector2 p in new[]{new Vector2(.15f,.2f),new Vector2(.77f,.1f),new Vector2(.94f,.499f)})
        {
            Color actual=At(target,p.x,p.y),wanted=At(original,p.x,p.y);
            Near(actual.r,wanted.r,"original R");Near(actual.g,wanted.g,"original G");Near(actual.b,wanted.b,"original B");Near(actual.a,wanted.a,"original alpha");
        }
        result.original_fallbacks++;
    }
    private static void Save()=>File.WriteAllText(Path.Combine(output,"gpu-result.json"),JsonUtility.ToJson(result,true)+"\n");
    private static void Release(RenderTexture rt){rt.Release();Object.DestroyImmediate(rt);}
    private static void Pair4040(SceneEyeOutput helper)
    {
        Check(QualitySettings.activeColorSpace==ColorSpace.Gamma,"Pair4040 requires Gamma");
        result.paired_requested_samples=8;
        var descriptor=new RenderTextureDescriptor(4040,4040,RenderTextureFormat.ARGB32,0){msaaSamples=8,sRGB=false};
        int supported=SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor);
        Check(supported>=1,"Provider reports a usable multisample count");
        if(supported!=8)result.unsupported_sample_counts++;
        result.paired_actual_samples=supported;descriptor.msaaSamples=supported;
        var owned=new List<RenderTexture>();var originals=new RenderTexture[2];var carriers=new RenderTexture[2];
        var painter=new Material(Get("SceneOutputOwned/Paint"));
        try
        {
            for(int eye=0;eye<2;eye++)
            {
                var source=new RenderTexture(descriptor){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
                owned.Add(source);Check(source.Create(),"Independent4040 source created");
                Check(source.antiAliasing==supported,"Actual provider sample count recorded");
                originals[eye]=Surface(4040,4040);owned.Add(originals[eye]);
                carriers[eye]=Surface(4040,4040);owned.Add(carriers[eye]);
                painter.SetFloat("_Eye",eye);Graphics.Blit(null,source,painter);Graphics.Blit(source,originals[eye]);
                var label=eye==0?Camera.MonoOrStereoscopicEye.Left:Camera.MonoOrStereoscopicEye.Right;
                Check(helper.Profile==SceneAtlasProfile.Profile,"Actual retained v1 output selected");
                Check(helper.TryRender(source,carriers[eye],label)==SceneEyeResult.Rendered,"Current independent4040 "+label+" draw");
                result.paired_source_draws++;
                foreach(Vector2 point in new[]{new Vector2(.25f,.1f),new Vector2(.77f,.1f),new Vector2(.35f,.5f)})
                    ColorCheck(carriers[eye],originals[eye],point.x,point.y);
                MaskCheck(carriers[eye],originals[eye],1,975.25f,-.25f);Guards(carriers[eye]);
                result.actual_msaa_sources++;Stage("paired4040 "+label+" requested8 actual"+source.antiAliasing);
            }
            Check(Mathf.Abs(At(originals[0],.25f,.1f).b-At(originals[1],.25f,.1f).b)>.09f,"Independent eye source artwork differs");
            // A second eye must not overwrite/reuse the previously produced image.
            ColorCheck(carriers[0],originals[0],.25f,.1f);ColorCheck(carriers[0],originals[0],.77f,.1f);
            Check(result.paired_source_draws==2,"Exactly two successful current-eye draws");
        }
        finally{Object.DestroyImmediate(painter);foreach(RenderTexture target in owned)Release(target);}
    }
    public static IEnumerator Run(string evidence)
    {
        output=evidence;Stage("start current scene-eye fixture");result.unity=Application.unityVersion;
        result.device=SystemInfo.graphicsDeviceName;result.backend=SystemInfo.graphicsDeviceType.ToString();result.color_space=QualitySettings.activeColorSpace.ToString();
        string[] arguments=Environment.GetCommandLineArgs();int modeIndex=Array.IndexOf(arguments,"-sceneMode");
        result.mode=modeIndex>=0?arguments[modeIndex+1]:"full";
        int bankIndex=Array.IndexOf(arguments,"-sceneBank");
        if(bankIndex>=0)
        {
            shippingBank=AssetBundle.LoadFromFile(arguments[bankIndex+1]);Check(shippingBank,"actual shipping Windows bank loaded");
            foreach(Shader shader in shippingBank.LoadAllAssets<Shader>())
                if(shader.name==SceneAtlasProfile.ShaderName){Check(!shippingPack,"unique shipping packshader");shippingPack=shader;}
            Check(shippingPack&&shippingPack.isSupported,"actual shipping packshader supported");
            result.shipping_windows_packshader=true;Stage("loaded shipping Windows packshader");
        }
        probeTarget=new RenderTexture(32,32,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
        Check(probeTarget.Create(),"owned full small probe RT");read=new Texture2D(32,32,TextureFormat.RGBAFloat,false,true);
        probeMaterial=new Material(Get("SceneOutputOwned/Probe"));
        var outputHelper=new SceneEyeOutput(Get(SceneAtlasProfile.ShaderName));
        try
        {
            if(result.mode=="eye-pair4040")
            {
                Pair4040(outputHelper);result.passed=true;Save();yield break;
            }
            foreach(int size in (result.mode=="negatives"?new int[0]:EdgeMode?new[]{256}:result.color_space=="Linear"?new[]{128}:new[]{2048,3408}))foreach(int samples in (result.color_space=="Linear"?new[]{1}:result.mode=="edge-witness"?new[]{4}:new[]{1,2,4,8}))
            {
                var descriptor=new RenderTextureDescriptor(size,size,RenderTextureFormat.ARGB32,24){msaaSamples=samples,sRGB=false};
                if(SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor)!=samples){result.unsupported_sample_counts++;continue;}
                Stage("camera size="+size+" samples="+samples);
                var target=Surface(size,size,samples);var original=Surface(size,size);
                var cameraObject=new GameObject("Owned same-eye source camera");var camera=cameraObject.AddComponent<Camera>();
                camera.enabled=false;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;
                camera.orthographic=true;camera.orthographicSize=1;camera.nearClipPlane=.1f;camera.farClipPlane=10;
                camera.transform.position=new Vector3(0,0,-1);camera.targetTexture=target;camera.allowHDR=false;camera.allowMSAA=true;
                camera.cullingMask=1<<30;
                var sink=cameraObject.AddComponent<OwnedSceneSink>();sink.Output=outputHelper;sink.OriginalResolved=original;
                sink.Label=samples%4==0?Camera.MonoOrStereoscopicEye.Right:Camera.MonoOrStereoscopicEye.Left;
                var painter=new Material(Get("SceneOutputOwned/Paint"));painter.SetFloat("_Eye",sink.Label==Camera.MonoOrStereoscopicEye.Left?0:1);
                var quad=GameObject.CreatePrimitive(PrimitiveType.Quad);quad.layer=30;quad.transform.localScale=new Vector3(2,2,1);
                quad.GetComponent<Renderer>().sharedMaterial=painter;
                GameObject edge=null;Mesh edgeMesh=null;Material edgeMaterial=null;
                if(EdgeMode)
                {
                    edge=new GameObject("Owned geometric MSAA edge");edge.layer=30;edgeMesh=new Mesh();
                    edgeMesh.vertices=new[]{new Vector3(-.72f,-.6f,0),new Vector3(.75f,-.6f,0),new Vector3(.36f,.6f,0)};
                    edgeMesh.triangles=new[]{0,1,2};edge.AddComponent<MeshFilter>().sharedMesh=edgeMesh;
                    painter.renderQueue=2000;edgeMaterial=new Material(Get("SceneOutputOwned/Edge")){renderQueue=2100};edge.AddComponent<MeshRenderer>().sharedMaterial=edgeMaterial;
                }
                try
                {
                    camera.Render();Stage("callback samples="+sink.SourceSamples+" format="+sink.SourceGraphicsFormat+" srgb="+sink.SourceSrgb+" result="+sink.Last);Check(sink.Callbacks==1,"actual OnRenderImage executed once");Check(sink.Restored,"helper holds graphics state");
                    var ownedMaterial=(Material)typeof(SceneEyeOutput).GetField("_material",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(outputHelper);
                    Check(!(ownedMaterial.GetTexture("_MainTex") is RenderTexture),"borrowed source RT binding closed after callback");
                    if(QualitySettings.activeColorSpace==ColorSpace.Gamma)
                    {
                        Check(sink.Last==SceneEyeResult.Rendered,"Gamma current-eye accepted: "+sink.Last);
                        foreach(Vector2 point in new[]{new Vector2(.001f,.001f),new Vector2(.999f,.999f),new Vector2(.4498f,.5f),new Vector2(.4502f,.5f),new Vector2(.77f,.1f),new Vector2(.94f,.5f)})ColorCheck(target,original,point.x,point.y);
                        if(!EdgeMode)for(int t=0;t<4;t++)foreach(float y in new[]{-.25f,.5f,255.5f,256.25f})MaskCheck(target,original,t,975.25f,y);
                        if(EdgeMode)
                        {
                            MaskCheck(target,original,1,975.25f,-.25f);
                            for(int row=100;row<120;row++)
                            {
                                float qy=(row+.5f)/size,qx=.86f-.9f*qy;
                                ColorCheck(target,original,qx,qy);
                                int col=Mathf.FloorToInt(qx*size);Color edgeValue=Pixel(original,col,row);
                                if(edgeValue.a>.71f&&edgeValue.a<.98f)result.partial_geometric_msaa_pixels++;
                            }
                        }
                        Guards(target);
                        painter.SetFloat("_Phase",1);camera.Render();ColorCheck(target,original,.77f,.1f);
                        // Actual borrowed callback sources may be reused by Unity;
                        // no source reference is retained by the helper across calls.
                        Check(sink.Callbacks==2,"new current frame captured without a previous-pair delay");
                    }
                    else{Check(sink.Last==SceneEyeResult.WorkingColorSpace,"Linear correctly refused");Ordinary(target,original);}
                    sink.Label=Camera.MonoOrStereoscopicEye.Mono;camera.Render();Check(sink.Last==SceneEyeResult.UnknownEye,"unknown eye refused");Ordinary(target,original);
                    sink.Active=false;camera.Render();Ordinary(target,original);
                    sink.Active=true;sink.Menu=true;camera.Render();Ordinary(target,original);
                    sink.Menu=false;sink.FrameValid=false;camera.Render();Ordinary(target,original);
                    result.rows.Add(new Row{input_width=size,input_height=size,target_samples=samples,callback_samples=sink.SourceSamples,callbacks=sink.Callbacks,label=sink.Label.ToString(),result=sink.Last.ToString()});
                    result.camera_cases++;result.actual_camera_callbacks=true;Save();
                }
                finally{camera.targetTexture=null;if(edge)Object.DestroyImmediate(edge);if(edgeMesh)Object.DestroyImmediate(edgeMesh);if(edgeMaterial)Object.DestroyImmediate(edgeMaterial);Object.DestroyImmediate(quad);Object.DestroyImmediate(painter);Object.DestroyImmediate(cameraObject);Release(target);Release(original);}
                yield return null;
            }
            // Descriptor MSAA route, distinct from Unity's already-resolved image
            // effect source. Compare to the untouched ordinary Graphics.Blit resolve.
            if(QualitySettings.activeColorSpace==ColorSpace.Gamma)
            {
                foreach(int samples in (result.mode=="full"?new[]{1,2,4,8}:new int[0]))
                {
                    var d=new RenderTextureDescriptor(128,128,RenderTextureFormat.ARGB32,24){msaaSamples=samples,sRGB=false};
                    if(SystemInfo.GetRenderTextureSupportedMSAASampleCount(d)!=samples)continue;
                    var source=Surface(128,128,samples);var original=Surface(128,128);var carrier=Surface(2112,2752);
                    var paint=new Material(Get("SceneOutputOwned/Paint"));Graphics.Blit(null,source,paint);
                    Graphics.Blit(source,original);
                    Check(outputHelper.TryRender(source,carrier,Camera.MonoOrStereoscopicEye.Left)==SceneEyeResult.Rendered,"actual ordinary MSAA source accepted");
                    ColorCheck(carrier,original,.77f,.1f);MaskCheck(carrier,original,1,975.25f,-.25f);Guards(carrier);
                    result.actual_msaa_sources++;Save();
                    Release(source);Release(original);Release(carrier);Object.DestroyImmediate(paint);
                }
                var color=Surface(64,64);var destination=Surface(64,64);var hdr=Surface(64,64,1,RenderTextureFormat.ARGBFloat);
                Check(outputHelper.TryRender(hdr,destination,Camera.MonoOrStereoscopicEye.Left)==SceneEyeResult.SourceFormat,"HDR refused");
                var srgbSource=new RenderTexture(new RenderTextureDescriptor(64,64){graphicsFormat=GraphicsFormat.R8G8B8A8_SRGB,depthBufferBits=0,msaaSamples=1});
                Check(srgbSource.Create(),"actual SRGB source");
                SceneEyeResult srgbResult=outputHelper.TryRender(srgbSource,destination,Camera.MonoOrStereoscopicEye.Left);
                Check(srgbResult==SceneEyeResult.SourceFormat||srgbResult==SceneEyeResult.SourceSrgb,"SRGB source refused");
                Release(srgbSource);
                var multiDescriptor=new RenderTextureDescriptor(64,64,RenderTextureFormat.ARGB32,0){sRGB=false,msaaSamples=4,bindMS=true};
                if(SystemInfo.GetRenderTextureSupportedMSAASampleCount(multiDescriptor)==4)
                {
                    var explicitMS=new RenderTexture(multiDescriptor);Check(explicitMS.Create(),"explicit multisample binding source");
                    Check(explicitMS.bindTextureMS,"provider reports explicit binding");
                    Check(outputHelper.TryRender(explicitMS,destination,Camera.MonoOrStereoscopicEye.Left)==SceneEyeResult.SourceMultisample,"unknown explicit MS sampling refused");
                    Release(explicitMS);
                }
                var arrayDescriptor=new RenderTextureDescriptor(64,64,RenderTextureFormat.ARGB32,0){sRGB=false,msaaSamples=1,dimension=TextureDimension.Tex2DArray,volumeDepth=2};
                var arraySource=new RenderTexture(arrayDescriptor);Check(arraySource.Create(),"actual array source");
                Check(outputHelper.TryRender(arraySource,destination,Camera.MonoOrStereoscopicEye.Left)==SceneEyeResult.SourceShape,"single-pass array is not admitted as one eye");Release(arraySource);
                Check(outputHelper.TryRender(color,color,Camera.MonoOrStereoscopicEye.Left)==SceneEyeResult.SourceDestinationAlias,"alias refused");
                var dynamicDescriptor=new RenderTextureDescriptor(64,64,RenderTextureFormat.ARGB32,0){useDynamicScale=true,sRGB=false,msaaSamples=1};
                var dynamicSource=new RenderTexture(dynamicDescriptor);Check(dynamicSource.Create(),"dynamic candidate RT");
                result.dynamic_scale_control_supported=dynamicSource.useDynamicScale;
                if(dynamicSource.useDynamicScale)Check(outputHelper.TryRender(dynamicSource,destination,Camera.MonoOrStereoscopicEye.Left)==SceneEyeResult.SourceDynamicScale,"unknown dynamic scaling refused");
                Release(dynamicSource);
                outputHelper.Dispose();Check(outputHelper.TryRender(color,destination,Camera.MonoOrStereoscopicEye.Left)==SceneEyeResult.Disposed,"closed helper refuses new work");
                Release(color);Release(destination);Release(hdr);
            }
            Save();Check(result.mode=="negatives"||result.camera_cases>0,"actual camera cases required");
            if(EdgeMode&&result.color_space=="Gamma")Check(result.partial_geometric_msaa_pixels>0,"actual resolved geometric partial samples required");
            result.passed=true;
            File.WriteAllText(Path.Combine(output,"gpu-result.json"),JsonUtility.ToJson(result,true)+"\n");
        }
        finally{outputHelper.Dispose();Object.DestroyImmediate(probeMaterial);Object.DestroyImmediate(read);Release(probeTarget);if(shippingBank)shippingBank.Unload(true);}
    }
}
