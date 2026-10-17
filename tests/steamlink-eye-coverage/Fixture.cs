using System;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Core.SteamLink;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Readback/allocations belong only to this owned editor test, never the component.
[ExecuteInEditMode]
public sealed class CoverageRuntimeRead : MonoBehaviour
{
    public Color[] pixels;
    public string sourceFormat;
    public int sourceSamples;
    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        RenderTexture before=RenderTexture.active;
        var read=new Texture2D(source.width,source.height,TextureFormat.RGBAFloat,false,true);
        try
        {
            sourceFormat=source.graphicsFormat.ToString();sourceSamples=source.antiAliasing;
            RenderTexture.active=source;read.ReadPixels(new Rect(0,0,source.width,source.height),0,0,false);
            read.Apply(false,false);pixels=read.GetPixels();Graphics.Blit(source,destination);
        }
        finally {RenderTexture.active=before;DestroyImmediate(read);}
    }
}
[ExecuteInEditMode]
public sealed class CoverageRuntimeNested : MonoBehaviour
{
    public Camera foreign;
    private void OnPreRender() {if(foreign)foreign.Render();}
}
public static class CoverageRuntimeFixture
{
    [Serializable] public sealed class Row
    {
        public string name,format,sourceFormat;
        public int samples,sourceSamples,partialPixels;
        public float maxAlphaError;
    }
    [Serializable] public sealed class Report
    {
        public bool passed,reversedZ,xrExecuted,windowsExecuted,gameWideCoverageProven;
        public string unity,backend,device,colorSpace,failure;
        public int checks,lifecycleChecks,depthlessControls;
        public List<Row> rows=new List<Row>();
    }
    private static Report result=new Report();
    private static List<UnityEngine.Object> owned=new List<UnityEngine.Object>();
    private static Camera camera,foreign;
    private static SceneEyeCoverage coverage;
    private static Mesh triangle,quad;
    private static Shader surfaceShader;
    private static void Check(bool value,string why)
    {result.checks++;if(!value)throw new Exception(why);}
    private static void Life(bool value,string why){result.lifecycleChecks++;Check(value,why);}
    private static T Keep<T>(T value) where T:UnityEngine.Object {owned.Add(value);return value;}
    public static void Start()
    {
        string[] args=Environment.GetCommandLineArgs();
        string output=args[Array.IndexOf(args,"-coverageRuntimeResult")+1];
        int code=1;
        try
        {
            result.unity=Application.unityVersion;result.backend=SystemInfo.graphicsDeviceType.ToString();
            result.device=SystemInfo.graphicsDeviceName;result.colorSpace=QualitySettings.activeColorSpace.ToString();
            result.reversedZ=SystemInfo.usesReversedZBuffer;
            Check(result.backend!="Null" && result.colorSpace=="Gamma","Actual Gamma GPU required");
            camera=CameraFor("Owned head camera");foreign=CameraFor("Foreign nested camera");foreign.cullingMask=0;
            var nested=camera.gameObject.AddComponent<CoverageRuntimeNested>();nested.foreign=foreign;
            surfaceShader=Shader.Find("CoverageRuntimeFixture/Surface");
            Shader shader=Shader.Find(SceneEyeCoverage.ShaderName);
            Check(shader && shader.isSupported && !ShaderUtil.ShaderHasError(shader),"Actual production coverage shader compiled");
            Check(surfaceShader && surfaceShader.isSupported && !ShaderUtil.ShaderHasError(surfaceShader),"Actual surface shader compiled");
            coverage=new SceneEyeCoverage(shader);
            triangle=Keep(new Mesh());triangle.vertices=new[]{new Vector3(-.91f,-.83f,0),new Vector3(.87f,-.72f,0),new Vector3(-.63f,.89f,0)};
            triangle.uv=new[]{Vector2.zero,Vector2.right,Vector2.up};triangle.triangles=new[]{0,1,2};triangle.RecalculateBounds();
            quad=Keep(new Mesh());quad.vertices=new[]{new Vector3(-1,-1,0),new Vector3(1,-1,0),new Vector3(1,1,0),new Vector3(-1,1,0)};
            quad.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};quad.triangles=new[]{0,1,2,0,2,3};quad.RecalculateBounds();
            var foreignTarget=Keep(new RenderTexture(32,32,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear));
            Check(foreignTarget.Create(),"Foreign target allocated");foreign.targetTexture=foreignTarget;
            Lifecycle(shader);
            foreach(int samples in new[]{1,2,4,8})
            {
                var desc=new RenderTextureDescriptor(32,32,RenderTextureFormat.ARGB32,24){msaaSamples=samples};
                if(SystemInfo.GetRenderTextureSupportedMSAASampleCount(desc)!=samples)continue;
                foreach(string name in new[]{"empty","opaque","cutout","alpha-to-coverage","stencil-suffix","excluded-depthless"})Probe(name,samples);
            }
            Check(result.rows.Count>=6 && result.depthlessControls>0,"Actual sample matrix and excluded route executed");
            coverage.Dispose();Life(!coverage.Ready && !coverage.Arm(camera,99) && coverage.Reason==SceneEyeCoverage.Refusal.Disposed,"Disposed cannot rearm");
            result.passed=true;code=0;
        }
        catch(Exception error){result.failure=error.ToString();Debug.LogException(error);}
        finally
        {
            if(coverage!=null)coverage.Dispose();
            for(int i=owned.Count-1;i>=0;i--)if(owned[i])UnityEngine.Object.DestroyImmediate(owned[i]);
            File.WriteAllText(output,JsonUtility.ToJson(result,true)+"\n");
        }
        EditorApplication.Exit(code);
    }
    private static Camera CameraFor(string name)
    {
        var go=Keep(new GameObject(name));var c=go.AddComponent<Camera>();c.enabled=false;
        c.orthographic=true;c.orthographicSize=1;c.transform.position=new Vector3(0,0,-5);
        c.nearClipPlane=.1f;c.farClipPlane=20;c.clearFlags=CameraClearFlags.SolidColor;
        c.backgroundColor=new Color(0,0,0,.31f);c.allowHDR=false;c.allowMSAA=true;
        c.renderingPath=RenderingPath.Forward;c.cullingMask=1<<30;go.AddComponent<CoverageRuntimeRead>();return c;
    }
    private static void Lifecycle(Shader shader)
    {
        Life(!coverage.Ready && coverage.Reason==SceneEyeCoverage.Refusal.Disarmed,"Cold component is inert");
        Life(!coverage.Arm(camera,0) && coverage.Reason==SceneEyeCoverage.Refusal.Epoch,"Zero epoch refused");
        Life(!coverage.Arm(null,1) && coverage.Reason==SceneEyeCoverage.Refusal.Camera,"Null camera refused");
        camera.allowHDR=true;Life(!coverage.Arm(camera,1) && coverage.Reason==SceneEyeCoverage.Refusal.Hdr,"HDR refused without changing camera");camera.allowHDR=false;
        camera.clearFlags=CameraClearFlags.Depth;Life(!coverage.Arm(camera,1) && coverage.Reason==SceneEyeCoverage.Refusal.ClearPolicy,"Nonclear refused without writes");camera.clearFlags=CameraClearFlags.SolidColor;
        Life(coverage.Arm(camera,1) && coverage.IsArmedFor(camera,1),"Exact epoch camera armed");
        Life(!coverage.IsArmedFor(foreign,1) && !coverage.IsArmedFor(camera,2),"Foreign camera and epoch excluded");
        Life(camera.GetCommandBuffers(CameraEvent.AfterForwardOpaque).Length==1 && foreign.GetCommandBuffers(CameraEvent.AfterForwardOpaque).Length==0,"Only owned camera contains CB");
        Life(coverage.Arm(foreign,2) && camera.GetCommandBuffers(CameraEvent.AfterForwardOpaque).Length==0,"Camera transition immediately detaches old CB");
        coverage.Disarm();Life(foreign.GetCommandBuffers(CameraEvent.AfterForwardOpaque).Length==0 && !coverage.Ready,"Disarm detaches immediately");
        using(var bad=new SceneEyeCoverage(surfaceShader))Life(!bad.Arm(camera,3) && bad.Reason==SceneEyeCoverage.Refusal.Shader,"Wrong shader refused");
        Camera destroyed=CameraFor("Destroyed camera");Life(coverage.Arm(destroyed,4),"Destroy test armed");
        UnityEngine.Object.DestroyImmediate(destroyed.gameObject);Life(!coverage.Ready,"Destroyed camera invalidates readiness");coverage.Disarm();
        var wrong=Keep(new RenderTexture(32,32,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear));Check(wrong.Create(),"Wrong-format target created");camera.targetTexture=wrong;
        Life(!coverage.Arm(camera,5) && coverage.Reason==SceneEyeCoverage.Refusal.Target,"Non-UNorm target refused");camera.targetTexture=null;
    }
    private static Material MaterialFor(){var m=Keep(new Material(surfaceShader));return m;}
    private static void Surface(Mesh mesh,Material material,float z)
    {
        var go=Keep(new GameObject("Owned surface"));go.layer=30;go.transform.position=new Vector3(0,0,z);
        go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=material;
    }
    private static Color[] Render(RenderTexture target)
    {
        camera.targetTexture=target;camera.Render();var read=camera.GetComponent<CoverageRuntimeRead>();
        Check(read.pixels!=null && read.pixels.Length==1024,"Actual same-camera OnRenderImage source");return read.pixels;
    }
    private static void Probe(string name,int samples)
    {
        int start=owned.Count;
        var target=new RenderTexture(32,32,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear){antiAliasing=samples};
        try
        {
            Check(target.Create() && target.antiAliasing==samples,"Actual target samples");camera.targetTexture=target;
            if(name!="empty")
            {
                Material m=MaterialFor();
                if(name=="cutout")m.SetFloat("_Cutout",1);
                if(name=="alpha-to-coverage"){m.SetFloat("_AlphaToMask",1);m.SetColor("_Color",new Color(1,.375f,.625f,.5f));}
                if(name=="excluded-depthless")m.SetFloat("_ZWrite",0);
                if(name=="stencil-suffix")
                {
                    Material mask=MaterialFor();mask.renderQueue=1900;mask.SetFloat("_Mask",0);mask.SetFloat("_ZWrite",0);
                    mask.SetFloat("_StencilRef",1);mask.SetFloat("_StencilPass",(int)StencilOp.Replace);
                    var go=Keep(new GameObject("Owned stencil mask"));go.layer=30;go.transform.position=new Vector3(-.5f,0,-.5f);go.transform.localScale=new Vector3(.5f,1,1);
                    go.AddComponent<MeshFilter>().sharedMesh=quad;go.AddComponent<MeshRenderer>().sharedMaterial=mask;
                    m.SetFloat("_StencilRef",1);m.SetFloat("_StencilComp",(int)CompareFunction.Equal);
                    Material suffix=MaterialFor();suffix.renderQueue=3000;suffix.SetFloat("_ZWrite",0);suffix.SetFloat("_Mask",(int)ColorWriteMask.Red|(int)ColorWriteMask.Green|(int)ColorWriteMask.Blue);
                    suffix.SetFloat("_StencilRef",1);suffix.SetFloat("_StencilComp",(int)CompareFunction.Equal);
                    suffix.SetColor("_Color",new Color(1,.125f,.5f,.17f));Surface(triangle,suffix,-.1f);
                }
                Surface(triangle,m,0);
            }
            coverage.Disarm();Color[] stock=Render(target);
            Check(coverage.Arm(camera,(ulong)(samples+10)),"Production Arm succeeds");
            Color[] stamped=Render(target);
            var row=new Row{name=name,format=target.graphicsFormat.ToString(),samples=samples};
            var read=camera.GetComponent<CoverageRuntimeRead>();row.sourceFormat=read.sourceFormat;row.sourceSamples=read.sourceSamples;
            bool depthless=false,oldFailed=false;
            for(int i=0;i<stock.Length;i++)
            {
                Check(Bits(stock[i].r)==Bits(stamped[i].r) && Bits(stock[i].g)==Bits(stamped[i].g) && Bits(stock[i].b)==Bits(stamped[i].b),"RGB bit identity "+name+"/"+i);
                if(name=="excluded-depthless"){if(stock[i].r>.9f && stamped[i].a<.01f)depthless=true;continue;}
                float error=Mathf.Abs(stamped[i].a-stock[i].r);row.maxAlphaError=Mathf.Max(row.maxAlphaError,error);
                Check(error<.008f,"Coverage equals independent RGB silhouette "+name+"/"+i);
                if(stock[i].r>.01f && stock[i].r<.99f)row.partialPixels++;
                if(Mathf.Abs(stock[i].a-stock[i].r)>.1f)oldFailed=true;
            }
            Check(oldFailed || depthless,"Original framebuffer alpha is a causal invalid oracle");
            if(depthless){result.depthlessControls++;Check(depthless,"Depthless silhouette stays explicitly excluded");}
            Color[] foreignPixels=foreign.GetComponent<CoverageRuntimeRead>().pixels;
            Check(foreignPixels!=null && Mathf.Abs(foreignPixels[0].a-.31f)<.008f,"Nested foreign camera receives no stamp");
            coverage.Disarm();Color[] off=Render(target);
            for(int i=0;i<stock.Length;i++)Check(stock[i]==off[i],"Disarm restores full original RGBA");
            Check(camera.GetCommandBuffers(CameraEvent.AfterForwardOpaque).Length==0,"No commands retained after Disarm");
            result.rows.Add(row);
        }
        finally
        {
            coverage.Disarm();camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);
            for(int i=owned.Count-1;i>=start;i--){if(owned[i])UnityEngine.Object.DestroyImmediate(owned[i]);owned.RemoveAt(i);}
        }
    }
    private static int Bits(float value){return BitConverter.ToInt32(BitConverter.GetBytes(value),0);}
}
