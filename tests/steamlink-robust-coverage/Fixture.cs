using System;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Core.SteamLink;
using UnityEngine;
using Object=UnityEngine.Object;

public static class RobustSourceFixture
{
    [Serializable] private sealed class Probe
    {
        public string eye,label;public float[] source_q,source_rgba,scene_atlas,mask_atlas;
    }
    [Serializable] private sealed class Header
    {
        public string eye;public int index,code;public float[] atlas_center,observed_rgb;
    }
    [Serializable] private sealed class Result
    {
        public bool passed,xr_callbacks_executed=false,codec_executed=false,headset_result_proven=false;
        public string unity,device,backend,profile=SceneAtlasProfile.RobustProfile,coordinates="top-down normalized source q; virtual atlas pixels";
        public int checks,carrier=4040,source_size=4040,requested_samples=8,actual_samples,robust_draws,legacy_v2_control_draws;
        public float maximum_rgb_error,maximum_mask_error,maximum_header_error;
        public List<Probe> probes=new List<Probe>();public List<Header> headers=new List<Header>();
    }
    private static readonly List<RenderTexture> owned=new List<RenderTexture>();
    private static Result result=new Result();private static RenderTexture probeTarget;private static Texture2D read;private static Material probe;
    private static void Check(bool value,string label){result.checks++;if(!value)throw new Exception(label);}
    private static void Near(float a,float b,string label,float limit=2.1f/255){Check(!float.IsNaN(a)&&Mathf.Abs(a-b)<=limit,label+": "+a+" expected "+b);}
    private static float[] Vector(Vector2 v)=>new[]{v.x,v.y};
    private static float[] Values(Color c)=>new[]{c.r,c.g,c.b,c.a};
    private static RenderTexture Surface(int size,int samples=1)
    {
        var rt=new RenderTexture(size,size,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear)
            {antiAliasing=samples,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp,useMipMap=false};
        owned.Add(rt);Check(rt.Create(),"Actual4040 UNorm surface created");return rt;
    }
    private static Color At(RenderTexture rt,Vector2 topDown)
    {
        var previous=RenderTexture.active;bool srgb=GL.sRGBWrite;
        try
        {
            GL.sRGBWrite=false;probe.SetVector("_PointUV",new Vector4(topDown.x,1-topDown.y,0,0));
            Graphics.Blit(rt,probeTarget,probe);RenderTexture.active=probeTarget;
            read.ReadPixels(new Rect(0,0,32,32),0,0,false);read.Apply(false,false);return read.GetPixel(16,16);
        }
        finally{RenderTexture.active=previous;GL.sRGBWrite=srgb;}
    }
    private static void Save(RenderTexture rt,string file)
    {
        var previous=RenderTexture.active;var image=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false,true);
        try{RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0,false);image.Apply(false,false);File.WriteAllBytes(file,image.EncodeToPNG());}
        finally{RenderTexture.active=previous;Object.DestroyImmediate(image);}
    }
    private static Vector2 Mask(Vector2 q)
    {
        Vector2 p=new Vector2(Mathf.Clamp(q.x*2048,.5f,2047.5f),Mathf.Clamp(q.y*2048,.5f,2047.5f));
        int tile=Mathf.Min(3,Mathf.FloorToInt(p.y/512));Vector2 local=new Vector2(p.x,p.y-tile*512);
        return RobustProfile.Origins[tile]+(tile==1||tile==3?new Vector2(local.y,2048-local.x):local);
    }
    private static void Inspect(string eye,RenderTexture source,RenderTexture carrier)
    {
        for(int i=0;i<12;i++)
        {
            Vector2 center=RobustProfile.HeaderOrigins[i]+Vector2.one*64;Color observed=At(carrier,center/3168);
            float expected=RobustProfile.Codes[i]/255f;Near(observed.r,expected,"Actual header R",1.1f/255);Near(observed.g,expected,"Actual header G",1.1f/255);Near(observed.b,expected,"Actual header B",1.1f/255);
            result.maximum_header_error=Mathf.Max(result.maximum_header_error,Mathf.Abs(observed.g-expected));
            result.headers.Add(new Header{eye=eye,index=i,code=RobustProfile.Codes[i],atlas_center=Vector(center),observed_rgb=new[]{observed.r,observed.g,observed.b}});
        }
        var points=new List<Vector2>{new Vector2(.0625f,.5f),new Vector2(.25f,.25f),new Vector2(.5f,.5f),new Vector2(.75f,.75f),new Vector2(.75f,.25f)};
        foreach(float y in new[]{.125f,.375f,.625f,.875f})points.Add(new Vector2(.1f,y));
        for(int seam=1;seam<4;seam++)foreach(int side in new[]{-1,1})points.Add(new Vector2(seam*.25f,(seam*512+side)/2048f));
        for(int i=0;i<points.Count;i++)
        {
            Vector2 q=points[i],color=Vector2.one*560+q*2048,mask=Mask(q);
            Color expected=At(source,q),rgb=At(carrier,color/3168),alpha=At(carrier,mask/3168);
            Near(rgb.r,expected.r,"Current source E.r");Near(rgb.g,expected.g,"Current source E.g");Near(rgb.b,expected.b,"Current source E.b");Near(alpha.g,expected.a,"Current source numeric A");
            result.maximum_rgb_error=Mathf.Max(result.maximum_rgb_error,Mathf.Abs(rgb.r-expected.r),Mathf.Abs(rgb.g-expected.g),Mathf.Abs(rgb.b-expected.b));
            result.maximum_mask_error=Mathf.Max(result.maximum_mask_error,Mathf.Abs(alpha.g-expected.a));
            result.probes.Add(new Probe{eye=eye,label="source-"+i,source_q=Vector(q),source_rgba=Values(expected),scene_atlas=Vector(color),mask_atlas=Vector(mask)});
        }
        foreach(var pair in new[]{new Vector2(.25f,.25f),new Vector2(.5f,.5f),new Vector2(.75f,.75f)})Near(At(source,pair).a,pair.x,"Actual fractional glass retained");
        Color emission=At(source,new Vector2(.75f,.25f));Near(emission.a,0,"Actual A0 patch");Check(emission.r>.15f&&emission.g>.25f,"Actual positive emission at A0");
    }
    public static void Run(string output,string bankPath)
    {
        result.unity=Application.unityVersion;result.device=SystemInfo.graphicsDeviceName;result.backend=SystemInfo.graphicsDeviceType.ToString();
        AssetBundle bank=null;SceneEyeOutput pack=null,oldPack=null;Material paint=null;
        try
        {
            Check(result.unity=="2021.3.5f1"&&QualitySettings.activeColorSpace==ColorSpace.Gamma,"Exact game editor and Gamma");
            bank=AssetBundle.LoadFromFile(bankPath);Check(bank,"Actual shipping bank loaded");
            Shader robust=bank.LoadAsset<Shader>("assets/steamlink/steamlinkscenerobustatlas.shader"),old=bank.LoadAsset<Shader>("assets/steamlink/steamlinkscenefullatlas.shader");
            Check(robust&&robust.name==SceneAtlasProfile.RobustShaderName&&robust.isSupported,"Actual compiled robust shader");
            Check(old&&old.name==SceneAtlasProfile.FullShaderName&&old.isSupported,"Unchanged compiled v2 control");
            pack=new SceneEyeOutput(robust);oldPack=new SceneEyeOutput(old);Check(pack.Profile==SceneAtlasProfile.RobustProfile,"Actual immutable v3 profile");
            var descriptor=new RenderTextureDescriptor(4040,4040,RenderTextureFormat.ARGB32,0){msaaSamples=8,sRGB=false};
            result.actual_samples=SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor);Check(result.actual_samples==8,"Actual8xMSAA is required, never inferred from request");
            probeTarget=new RenderTexture(32,32,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);Check(probeTarget.Create(),"Float probe target");
            read=new Texture2D(32,32,TextureFormat.RGBAFloat,false,true);probe=new Material(Shader.Find("SceneOutputOwned/Probe"));paint=new Material(Shader.Find("RobustCoverageOwned/Paint"));
            var references=new RenderTexture[2];var carriers=new RenderTexture[2];
            for(int eye=0;eye<2;eye++)
            {
                string name=eye==0?"left":"right";var label=eye==0?Camera.MonoOrStereoscopicEye.Left:Camera.MonoOrStereoscopicEye.Right;
                var source=Surface(4040,8);Check(source.antiAliasing==8,"Created source has8 actual samples");references[eye]=Surface(4040);carriers[eye]=Surface(4040);var oldCarrier=Surface(4040);
                paint.SetFloat("_Eye",eye);Graphics.Blit(null,source,paint);Graphics.Blit(source,references[eye]);
                Check(pack.TryRender(source,carriers[eye],label)==SceneEyeResult.Rendered,"Actual same-eye v3 draw "+name);result.robust_draws++;
                Check(oldPack.TryRender(source,oldCarrier,label)==SceneEyeResult.Rendered,"Same-source frozen v2 control "+name);result.legacy_v2_control_draws++;
                Inspect(name,references[eye],carriers[eye]);
                Save(references[eye],Path.Combine(output,name+"-source.png"));Save(carriers[eye],Path.Combine(output,name+"-v3-carrier.png"));Save(oldCarrier,Path.Combine(output,name+"-v2-carrier.png"));
            }
            Color first=At(references[0],new Vector2(.0625f,.5f)),second=At(references[1],new Vector2(.0625f,.5f));Check(Mathf.Abs(first.r-second.r)>.45f,"Independent eye content differs");
            Color retained=At(carriers[0],(Vector2.one*560+new Vector2(.0625f,.5f)*2048)/3168);Near(retained.r,first.r,"Left output retained after right draw");Near(retained.g,first.g,"Left G retained after right draw");
            Check(result.robust_draws==2&&result.legacy_v2_control_draws==2&&result.headers.Count==24,"Both current eyes and all24 actual headers executed");
            result.passed=true;
        }
        finally
        {
            pack?.Dispose();oldPack?.Dispose();if(paint)Object.DestroyImmediate(paint);if(probe)Object.DestroyImmediate(probe);if(read)Object.DestroyImmediate(read);
            foreach(RenderTexture rt in owned){rt.Release();Object.DestroyImmediate(rt);}if(probeTarget){probeTarget.Release();Object.DestroyImmediate(probeTarget);}if(bank)bank.Unload(true);
            File.WriteAllText(Path.Combine(output,"gpu-result.json"),JsonUtility.ToJson(result,true)+"\n");
        }
    }
}
