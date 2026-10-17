using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Owned compiled-bundle control only. No game assets, runtime hooks or XR.
public static class CoverageBankFixture
{
    [Serializable] public sealed class Result { public bool passed, original_and_derivative_simultaneously_loaded; public int checks, cases; public string unity, backend, device, color_space, failure; }
    private static Result result=new Result();
    private static void Require(bool value,string message){result.checks++;if(!value)throw new Exception(message);}
    private static string Argument(string key){string[] args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,key);if(i<0||i+1>=args.Length)throw new Exception("Missing "+key);return args[i+1];}
    public static void Build()
    {
        int code=1;
        try{
            string output=Argument("-bankOutput");Directory.CreateDirectory(output);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneLinux64,new[]{GraphicsDeviceType.Vulkan});
            var bank=new AssetBundleBuild{assetBundleName="owned-original.bundle",assetNames=new[]{"Assets/Source.shader"}};
            var manifest=BuildPipeline.BuildAssetBundles(output,new[]{bank},BuildAssetBundleOptions.UncompressedAssetBundle|BuildAssetBundleOptions.ForceRebuildAssetBundle,BuildTarget.StandaloneLinux64);
            if(!manifest)throw new Exception("Owned shader bundle build failed");code=0;
        }catch(Exception e){Debug.LogException(e);}EditorApplication.Exit(code);
    }
    public static void Probe()
    {
        int code=1;AssetBundle original=null,derivative=null;Shader first=null,second=null;
        try{
            result.unity=Application.unityVersion;result.backend=SystemInfo.graphicsDeviceType.ToString();result.device=SystemInfo.graphicsDeviceName;result.color_space=QualitySettings.activeColorSpace.ToString();
            Require(result.backend=="Vulkan","Actual Vulkan required");
            original=AssetBundle.LoadFromFile(Argument("-originalBank"));derivative=AssetBundle.LoadFromFile(Argument("-derivativeBank"));
            Require(original&&derivative&&original!=derivative,"Distinct original and derivative banks coexist");
            first=original.LoadAsset<Shader>("Assets/Source.shader");second=derivative.LoadAsset<Shader>("Assets/Source.shader");
            Require(first&&second&&first!=second&&first.name=="CoverageBank/Original"&&second.name=="CoverageBank/Derived","Distinct named shader objects coexist");
            Require(first.isSupported&&second.isSupported&&!ShaderUtil.ShaderHasError(first)&&!ShaderUtil.ShaderHasError(second),"Both compiled Vulkan programs supported");
            result.original_and_derivative_simultaneously_loaded=true;
            foreach(var format in new[]{RenderTextureFormat.ARGB32,RenderTextureFormat.ARGBFloat})
                foreach(int layers in new[]{1,2})foreach(bool clip in new[]{false,true})ProbePair(first,second,format,layers,clip);
            result.passed=true;code=0;
        }catch(Exception e){result.failure=e.ToString();Debug.LogException(e);}
        finally{if(derivative)derivative.Unload(true);if(original)original.Unload(true);File.WriteAllText(Argument("-bankResult"),JsonUtility.ToJson(result,true)+"\n");}
        EditorApplication.Exit(code);
    }
    private static int Bits(float value){return BitConverter.ToInt32(BitConverter.GetBytes(value),0);}
    private static void ProbePair(Shader original,Shader derivative,RenderTextureFormat format,int layers,bool clip)
    {
        Color[] a=Draw(original,format,layers,clip),b=Draw(derivative,format,layers,clip);Require(a.Length==b.Length,"Equal complete pixel extent");
        for(int i=0;i<a.Length;i++){
            Require(Bits(a[i].r)==Bits(b[i].r)&&Bits(a[i].g)==Bits(b[i].g)&&Bits(a[i].b)==Bits(b[i].b),"Every RGB float bit value identical after compiled state-only edit");
            bool covered=!clip||(i%16)>=8;float want=covered?(layers==1?.5f:.75f):0;float tolerance=format==RenderTextureFormat.ARGB32?.006f:.00001f;
            Require(Mathf.Abs(b[i].a-want)<=tolerance,"Derived alpha is over coverage, including clipped pixels");
            float legacy=covered?(layers==1?.25f:.375f):0;Require(Mathf.Abs(a[i].a-legacy)<=tolerance,"Original shared-factor alpha remains unchanged and wrong for coverage");
        }result.cases++;
    }
    private static Color[] Draw(Shader shader,RenderTextureFormat format,int layers,bool clip)
    {
        var rt=new RenderTexture(16,16,24,format,RenderTextureReadWrite.Linear){antiAliasing=1};var pixels=new Texture2D(16,16,TextureFormat.RGBAFloat,false,true);var material=new Material(shader);var mesh=new Mesh();var commands=new CommandBuffer();RenderTexture previous=RenderTexture.active;
        try{
            Require(rt.Create(),"Admitted GPU target created");material.SetFloat("_Clip",clip?1:0);material.SetColor("_Color",new Color(.2f,.4f,.6f,.5f));
            mesh.vertices=new[]{new Vector3(-1,-1,0),new Vector3(1,-1,0),new Vector3(1,1,0),new Vector3(-1,1,0)};mesh.uv=new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(1,1),new Vector2(0,1)};mesh.triangles=new[]{0,1,2,0,2,3};
            commands.SetRenderTarget(rt);commands.ClearRenderTarget(true,true,Color.clear);for(int i=0;i<layers;i++)commands.DrawMesh(mesh,Matrix4x4.identity,material,0,0);Graphics.ExecuteCommandBuffer(commands);
            RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,16,16),0,0);pixels.Apply(false,false);return pixels.GetPixels();
        }finally{RenderTexture.active=previous;commands.Release();UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(pixels);rt.Release();UnityEngine.Object.DestroyImmediate(rt);}
    }
}
