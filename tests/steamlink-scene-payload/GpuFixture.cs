using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using GloomhavenVR.Core.SteamLink;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Uses the existing finite Editor coroutine driver, not its old fixture/suite.
public static class RegistrationGpuFixture
{
    [Serializable] private sealed class Result
    {
        public string name = "complete", profile = ScenePayloadDraft.Profile;
        public string unity = "", graphics = "", device = "", color_space = "";
        public int checks, points, seam_points, emission_points, mutant_rejections, eyes;
        public float max_rgb_error, max_coverage_error;
        public bool passed, gpu_executed = true, game_coverage_authorized = false, xr_callbacks_executed = false;
        public bool native_carriage_proven = false, passthrough_authorized = false;
        public int atlas_width = ScenePayloadDraft.CanvasWidth, atlas_height = ScenePayloadDraft.CanvasHeight;
        public int actual_fixture_destination_width = ScenePayloadDraft.SceneSize;
        public int actual_fixture_destination_height = ScenePayloadDraft.SceneSize;
    }
    private static Result result = new Result();
    private static string output = "";
    private static Texture2D probe = null!;
    private static int readbacks;
    private static RenderTexture probeSurface = null!;
    private static Material probeMaterial = null!;
    private static void Stage(string value) => File.AppendAllText(Path.Combine(output,"stages.log"),value+"\n");
    private static void Check(bool value, string message)
    {
        result.checks++;
        if (!value) throw new Exception("Scene payload draft: " + message);
    }
    private static void Near(float a, float b, string label, float tolerance = 0.0008f)
    {
        Check(!float.IsNaN(a) && !float.IsInfinity(a) && Mathf.Abs(a-b)<tolerance,
            label+": "+a+" expected "+b);
    }
    private static Shader ShaderFor(string name)
    {
        Shader shader=Shader.Find("ScenePayloadDraft/"+name);
        Check(shader && shader.isSupported,"actual shader supported: "+name);
        return shader;
    }
    private static RenderTexture Surface(string name,int width,int height)
    {
        var rt=new RenderTexture(width,height,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear)
        {name=name,antiAliasing=1,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,useMipMap=false};
        Check(rt.Create(),"actual float RT created: "+name);
        return rt;
    }
    private static Color Read(RenderTexture rt,int x,int y)
    {
        RenderTexture previous=RenderTexture.active;
        try
        {
            bool first=readbacks++==0;
            probeMaterial.SetVector("_PointUV",new Vector4((x+0.5f)/rt.width,1-(y+0.5f)/rt.height,0,0));
            Graphics.Blit(rt,probeSurface,probeMaterial,0);
            RenderTexture.active=probeSurface;
            if(first)Stage("first complete32x32 probe ReadPixels");
            probe.ReadPixels(new Rect(0,0,32,32),0,0,false);
            if(first)Stage("first ReadPixels completed");
            probe.Apply(false,false);
            if(first)Stage("first probe Apply completed");
            Color value=probe.GetPixel(16,16);
            if(first)Stage("first GetPixel completed");
            return value;
        }
        finally {RenderTexture.active=previous;}
    }
    private static Color Painted(int x,int y,int eye)
    {
        float qx=(x+0.5f)/2048,qy=(y+0.5f)/2048;
        float a1=qx<0.45f?0.5f:0,a2=qy>0.35f&&qy<0.75f?0.375f:0;
        var c1=new Vector3(0.6f,0.2f,0.1f+eye*0.2f);
        var c2=new Vector3(0.1f+eye*0.3f,0.5f,0.25f);
        Vector3 e=c2*a2+c1*a1*(1-a2);float a=a2+a1*(1-a2);
        if(qx>0.15f&&qx<0.35f&&qy>0.15f&&qy<0.3f){e=new Vector3(0.2f,0.3f+eye*0.2f,0.4f);a=1;}
        if(qx>0.65f&&qx<0.9f&&qy>0.05f&&qy<0.65f)e+=new Vector3(0.25f+eye*0.1f,0.125f,0.0625f);
        if(qx>0.92f){a=y%7<3?0.03125f:0;e=new Vector3(a*0.5f,a*0.25f,a*0.75f);}
        return new Color(e.x,e.y,e.z,a);
    }
    private static float Mask(int x,int y,int eye)
    {
        x=Math.Max(0,Math.Min(1023,x));y=Math.Max(0,Math.Min(1023,y));
        return (Painted(2*x,2*y,eye).a+Painted(2*x+1,2*y,eye).a
            +Painted(2*x,2*y+1,eye).a+Painted(2*x+1,2*y+1,eye).a)*0.25f;
    }
    private static float ExpectedCoverage(int x,int y,int eye)
    {
        float px=(x+0.5f)*0.5f-0.5f,py=(y+0.5f)*0.5f-0.5f;
        int ix=Mathf.FloorToInt(px),iy=Mathf.FloorToInt(py);float fx=px-ix,fy=py-iy;
        return Mask(ix,iy,eye)*(1-fx)*(1-fy)+Mask(ix+1,iy,eye)*fx*(1-fy)
            +Mask(ix,iy+1,eye)*(1-fx)*fy+Mask(ix+1,iy+1,eye)*fx*fy;
    }
    private static List<Vector2Int> Points()
    {
        var points=new List<Vector2Int>();
        foreach(int x in new[]{0,1,400,920,1500,1900,2046,2047})
            foreach(int y in new[]{0,1,100,512,1024,1536,1800,2046,2047})points.Add(new Vector2Int(x,y));
        foreach(int seam in new[]{512,1024,1536})
            for(int d=-3;d<=3;d++)points.Add(new Vector2Int(1951,seam+d));
        return points;
    }
    public static IEnumerator Run(string evidence)
    {
        output=evidence; Stage("entered owned fixture"); result.unity=Application.unityVersion;
        result.graphics=SystemInfo.graphicsDeviceType.ToString();result.device=SystemInfo.graphicsDeviceName;
        result.color_space=QualitySettings.activeColorSpace.ToString();
        Check(SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Null&&SystemInfo.supportsGraphicsFence,"actual GPU/fences required");
        Check(SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat),"linear RGBAFloat supported");
        Stage("before contribution RT");var e=Surface("owned E",2048,2048);
        Stage("before coverage RT");var a=Surface("owned A",2048,2048);
        Stage("before atlas RT");var atlas=Surface("owned draft atlas",2112,2752);
        Stage("before decode RT");var decoded=Surface("owned decode",2048,2048);
        Stage("all owned RTs created");
        probe=new Texture2D(32,32,TextureFormat.RGBAFloat,false,true);
        probeSurface=new RenderTexture(32,32,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear)
        {antiAliasing=1,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
        Check(probeSurface.Create(),"complete small readback surface");
        probeMaterial=new Material(ShaderFor("Probe"));
        Stage("before painter material");var paint=new Material(ShaderFor("Paint"));
        Stage("before production draft owner");var packer=new ScenePayloadDraft(ShaderFor("Pack"),ShaderFor("Decode"));
        var wrong=new ScenePayloadDraft(ShaderFor("Pack"),ShaderFor("DecodeDoublePremultiply"));
        var seamWrong=new ScenePayloadDraft(ShaderFor("PackWrongHalo"),ShaderFor("Decode"));
        Stage("all owners prepared");RenderTexture saved=RenderTexture.active;bool savedSrgb=GL.sRGBWrite;
        try
        {
            for(int eye=0;eye<2;eye++)
            {
                Stage("paint eye "+eye);paint.SetFloat("_Eye",eye);GL.sRGBWrite=false;
                Graphics.Blit(null,e,paint,0);Graphics.Blit(null,a,paint,1);
                RenderTexture.active=saved;
                GL.sRGBWrite=true; // Producer must restore caller state independently.
                Stage("pack eye "+eye);packer.PackSameEye(e,a,atlas);packer.Decode(atlas,decoded);
                Check(GL.sRGBWrite&&RenderTexture.active==saved,"packing restores active RT and sRGB write");
                GL.sRGBWrite=false;
                Stage("before completion fence eye "+eye);GraphicsFence fence=Graphics.CreateGraphicsFence(GraphicsFenceType.CPUSynchronisation,SynchronisationStageFlags.AllGPUOperations);
                GL.Flush();var clock=Stopwatch.StartNew();
                while(!fence.passed){Check(clock.Elapsed.TotalSeconds<20,"bounded actual fence completion");yield return null;}
                Stage("readback eye "+eye);foreach(Vector2Int p in Points())
                {
                    Color wanted=Painted(p.x,p.y,eye),actual=Read(decoded,p.x,p.y);
                    float ca=ExpectedCoverage(p.x,p.y,eye);
                    Near(actual.r,wanted.r,"E.r");Near(actual.g,wanted.g,"E.g");Near(actual.b,wanted.b,"E.b");Near(actual.a,ca,"independent downsample A");
                    result.max_rgb_error=Mathf.Max(result.max_rgb_error,Mathf.Abs(actual.r-wanted.r),Mathf.Abs(actual.g-wanted.g),Mathf.Abs(actual.b-wanted.b));
                    result.max_coverage_error=Mathf.Max(result.max_coverage_error,Mathf.Abs(actual.a-ca));
                    if(p.x==1951)result.seam_points++;
                    if(wanted.a==0&&wanted.r>0.1f){Check(actual.r>0.1f&&actual.a<0.001f,"emission survives A=0");result.emission_points++;}
                    result.points++;
                }
                // Output alpha is numeric A, despite the unrelated input E alpha.
                Near(Read(e,1500,100).a,0.875f,"source alpha poison is present");
                Near(Read(decoded,1500,100).a,0,"source alpha is not coverage");
                // Every internal halo carries global rows, including the rows
                // beyond the tile interior, rather than edge-clamping that tile.
                for(int t=0;t<4;t++)
                {
                    int ox=(t%2)*1056,oy=t<2?32:2432;
                    foreach(int d in new[]{-2,-1,0,255,256,257})
                    {
                        Color v=Read(atlas,ox+16+975,oy+16+d);
                        float wanted=Mask(975,t*256+d,eye);
                        Near(v.r,wanted,"halo R");Near(v.g,wanted,"halo G");Near(v.b,wanted,"halo B");
                    }
                }
                wrong.Decode(atlas,decoded);
                Color mutant=Read(decoded,1500,100);
                Check(mutant.r<0.001f,"double-premultiply causal mutant loses alpha-zero emission");result.mutant_rejections++;
                seamWrong.PackSameEye(e,a,atlas);packer.Decode(atlas,decoded);
                int rejected=0;
                foreach(int y in new[]{511,512,1023,1024,1535,1536})
                    if(Mathf.Abs(Read(decoded,1951,y).a-ExpectedCoverage(1951,y,eye))>0.001f)rejected++;
                Check(rejected>0,"wrong internal tile clamp is causally rejected");result.mutant_rejections++;
                // Ordinary/off path is an unchanged blit of the externally owned
                // source. No previous atlas, coverage or old pair is substituted.
                Graphics.Blit(e,decoded);
                Color stock=Read(decoded,1500,100),original=Read(e,1500,100);
                Near(stock.r,original.r,"off original R");Near(stock.g,original.g,"off original G");Near(stock.b,original.b,"off original B");Near(stock.a,original.a,"off original alpha");
                bool alias=false;
                try{packer.PackSameEye(e,e,atlas);}catch(ArgumentException){alias=true;}
                Check(alias,"aliased planes refused without treating source alpha as coverage");
                bool shape=false;
                try{packer.PackSameEye(e,a,decoded);}catch(ArgumentException){shape=true;}
                Check(shape,"actual wrong carrier shape refused");
                result.eyes++;
            }
            Check(result.emission_points>0&&result.seam_points>0&&result.mutant_rejections==4,"required independent controls executed");
            result.passed=true;
            File.WriteAllText(Path.Combine(output,"gpu-result.json"),JsonUtility.ToJson(result,true)+"\n");
        }
        finally
        {
            RenderTexture.active=saved;GL.sRGBWrite=savedSrgb;
            packer.Dispose();wrong.Dispose();seamWrong.Dispose();Object.Destroy(paint);Object.Destroy(probe);Object.Destroy(probeMaterial);
            probeSurface.Release();Object.Destroy(probeSurface);
            foreach(var rt in new[]{e,a,atlas,decoded}){rt.Release();Object.Destroy(rt);}
        }
    }
}
