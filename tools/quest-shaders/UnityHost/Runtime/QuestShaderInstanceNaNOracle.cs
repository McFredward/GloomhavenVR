using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Actual original instanced Amp_Low NaN/min/max pixel oracle.</summary>
public sealed class QuestShaderInstanceNaNOracle : MonoBehaviour
{
    public static readonly string[] Keywords = { "DIRECTIONAL", "DISABLE_VERTICAL_GRADIENT", "INSTANCING_ON", "LIGHTPROBE_SH", "SHADOWS_SCREEN", "TOGGLE_FLIP_DISSOLVE_DIRECTION", "TOGGLE_SHEEN", "USE_EMISSIVE_MAP" };
    public const string Address = "Assets/Content/Characters/Common/Shaders/Amp_low/Amp_CharShader_Low.shader";
    [Serializable] public sealed class Configuration
    { public string originalBundle, candidateBundle, output, originalReadbacks; public bool vulkan; public int width = 128; }
    [Serializable] public sealed class Case
    { public string id; public float maximumFiniteError; public int foregroundLeft, foregroundRight, changedLeft, changedRight, nonFiniteMismatches; public bool passed; }
    [Serializable] public sealed class Receipt
    { public string unityVersion, backend, device; public int hardwareTier; public string[] keywords; public Case[] cases; public bool actualDrawMeshInstanced, originalD3DReadbacksCompared, headsetPictureVerified; }
    private Configuration input;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        var args=Environment.GetCommandLineArgs(); int index=Array.IndexOf(args,"--quest-instance-nan-config");
        if(index<0)return;
        var oracle=new GameObject("OriginalInstanceNaNOracle").AddComponent<QuestShaderInstanceNaNOracle>();
        oracle.input=JsonUtility.FromJson<Configuration>(File.ReadAllText(args[index+1]));
    }
    private IEnumerator Start()
    {
        yield return null;
        try { Run();Application.Quit(0); }
        catch(Exception error){Debug.LogError(error);Application.Quit(1);}
    }
    private void Run()
    {
        if(Application.unityVersion!="2021.3.5f1"||SystemInfo.graphicsDeviceType!=(input.vulkan?GraphicsDeviceType.Vulkan:GraphicsDeviceType.Direct3D11))
            throw new InvalidOperationException("Instanced native oracle requires the actual original graphics backend.");
        Graphics.activeTier=GraphicsTier.Tier2;
        if(Graphics.activeTier!=GraphicsTier.Tier2)throw new InvalidOperationException("The actual formerly failing native hardware tier is unavailable.");
        Directory.CreateDirectory(input.output);
        var bundle=AssetBundle.LoadFromFile(input.vulkan?input.candidateBundle:input.originalBundle);
        var shader=bundle.LoadAsset<Shader>(Address);
        if(shader==null||shader.name!="Amp_Low/Amp_CharShader_Low"||!shader.isSupported)
            throw new InvalidOperationException("Exact original Amp_Low shader identity is absent or unsupported.");
        var material=new Material(shader){enableInstancing=true,shaderKeywords=Keywords};
        material.SetFloat("_Opacity",1);material.SetFloat("_Cutoff",.01f);material.SetFloat("_Cutout",0);
        material.SetFloat("_CharHeight",2);material.SetFloat("_Toggle_Dissolve",0);material.SetFloat("_AddVertexAnim",0);
        material.SetFloat("_EmissiveMapBoost",1);material.SetFloat("_Emissive",0);material.SetFloat("_EmissiveMapAsMask",0);
        material.SetFloat("_ToggleSheen",0);material.SetFloat("_ManualSheen",0);material.SetFloat("_Glow",0);
        material.SetColor("_MOD_TINT",Color.white);
        material.SetTexture("_MainTex",Texture2D.whiteTexture);material.SetTexture("_OpacityTexture",Texture2D.whiteTexture);
        material.SetTexture("_EmissiveMap",Texture2D.whiteTexture);material.SetTexture("_SheenMask",Texture2D.whiteTexture);
        var mesh=new Mesh { vertices=new[]{new Vector3(-.7f,-.7f,0),new Vector3(0,.7f,0),new Vector3(.7f,-.7f,0)},
            normals=new[]{Vector3.back,Vector3.back,Vector3.back},tangents=new[]{new Vector4(1,0,0,1),new Vector4(1,0,0,1),new Vector4(1,0,0,1)},
            uv=new[]{Vector2.zero,Vector2.up,Vector2.right},triangles=new[]{0,1,2} };
        mesh.RecalculateBounds();
        var controls=new[]{0f,.6f,1f,-1f,float.PositiveInfinity,float.NegativeInfinity,float.NaN};
        var ids=new[]{"baseline","fractional","one","negative","positive-infinity","negative-infinity","nan"};
        var cases=new Case[ids.Length];float[] baseline=null;
        for(int test=0;test<ids.Length;test++)
        {
            float[] actual=Capture(material,mesh,controls[test]);if(test==0)baseline=actual;
            var bytes=new byte[actual.Length*4];Buffer.BlockCopy(actual,0,bytes,0,bytes.Length);
            File.WriteAllBytes(Path.Combine(input.output,ids[test]+".f32"),bytes);
            var row=new Case{id=ids[test]};
            for(int pixel=0;pixel<input.width*input.width;pixel++)
            {
                int index=pixel*4;bool left=pixel%input.width<input.width/2;
                bool foreground=Math.Abs(actual[index])+Math.Abs(actual[index+1])+Math.Abs(actual[index+2])>.01f;
                if(foreground){if(left)row.foregroundLeft++;else row.foregroundRight++;}
                bool changed=false;for(int component=0;component<4;component++)if(!Equivalent(actual[index+component],baseline[index+component],.00001f))changed=true;
                if(changed){if(left)row.changedLeft++;else row.changedRight++;}
            }
            if(input.vulkan)
            {
                byte[] original=File.ReadAllBytes(Path.Combine(input.originalReadbacks,ids[test]+".f32"));
                if(original.Length!=bytes.Length)throw new InvalidOperationException("Original instanced readback extent differs.");
                var native=new float[actual.Length];Buffer.BlockCopy(original,0,native,0,original.Length);
                for(int n=0;n<actual.Length;n++)
                {
                    if(float.IsNaN(actual[n])||float.IsInfinity(actual[n])||float.IsNaN(native[n])||float.IsInfinity(native[n]))
                    {if(!Equivalent(actual[n],native[n],0))row.nonFiniteMismatches++;}
                    else row.maximumFiniteError=Math.Max(row.maximumFiniteError,Math.Abs(actual[n]-native[n]));
                }
            }
            row.passed=row.maximumFiniteError<.00002f&&row.nonFiniteMismatches==0&&row.foregroundRight>100&&row.changedRight==0;
            if(test==0)row.passed&=row.foregroundLeft>100;
            if(test==1||test==2)row.passed&=row.changedLeft>100;
            cases[test]=row;
            if(!row.passed)throw new InvalidOperationException("Native instanced NaN witness failed: "+JsonUtility.ToJson(row));
        }
        File.WriteAllText(Path.Combine(input.output,"instance-nan.json"),JsonUtility.ToJson(new Receipt{unityVersion=Application.unityVersion,backend=SystemInfo.graphicsDeviceType.ToString(),device=SystemInfo.graphicsDeviceName,hardwareTier=(int)Graphics.activeTier,keywords=Keywords,cases=cases,actualDrawMeshInstanced=true,originalD3DReadbacksCompared=input.vulkan},true)+"\n");
        Debug.Log("PASS actual original instanced Amp_Low NaN/min/max pixels.");
    }
    private static bool Equivalent(float first,float second,float tolerance)
    {if(float.IsNaN(first)||float.IsNaN(second))return float.IsNaN(first)&&float.IsNaN(second);if(float.IsInfinity(first)||float.IsInfinity(second))return first==second;return Math.Abs(first-second)<=tolerance;}
    private float[] Capture(Material material,Mesh mesh,float control)
    {
        var camera=new GameObject("NativeInstanceCamera").AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=1.2f;
        camera.nearClipPlane=.01f;camera.farClipPlane=5;camera.transform.position=new Vector3(0,0,-3);camera.transform.LookAt(Vector3.zero);
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;camera.renderingPath=RenderingPath.Forward;
        var target=new RenderTexture(input.width,input.width,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);target.Create();camera.targetTexture=target;
        var pixels=new Texture2D(input.width,input.width,TextureFormat.RGBAFloat,false,true);var previous=RenderTexture.active;
        var command=new CommandBuffer();var properties=new MaterialPropertyBlock();properties.SetFloatArray("_InvisibilityControl",new[]{control,.25f});
        var matrices=new[]{Matrix4x4.TRS(new Vector3(-.55f,0,0),Quaternion.identity,Vector3.one*.6f),Matrix4x4.TRS(new Vector3(.55f,0,0),Quaternion.identity,Vector3.one*.6f)};
        try
        {
            command.SetGlobalVector("_Time",Vector4.zero);command.SetGlobalVector("_SinTime",Vector4.zero);command.SetGlobalVector("_CosTime",Vector4.one);
            command.SetGlobalVector("_WorldSpaceLightPos0",new Vector4(0,0,-1,0));command.SetGlobalVector("_LightColor0",Vector4.one);
            command.SetGlobalVector("unity_ProbeVolumeParams",Vector4.zero);command.SetGlobalVector("_LightShadowData",new Vector4(1,0,0,0));
            command.SetGlobalTexture("_ShadowMapTexture",Texture2D.whiteTexture);
            command.DrawMeshInstanced(mesh,0,material,0,matrices,2,properties);
            camera.AddCommandBuffer(CameraEvent.AfterForwardOpaque,command);camera.Render();
            RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,input.width,input.width),0,0,false);pixels.Apply(false,false);
            var colors=pixels.GetPixels();var result=new float[colors.Length*4];
            for(int n=0;n<colors.Length;n++){result[n*4]=colors[n].r;result[n*4+1]=colors[n].g;result[n*4+2]=colors[n].b;result[n*4+3]=colors[n].a;}
            return result;
        }
        finally{camera.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque,command);command.Dispose();RenderTexture.active=previous;DestroyImmediate(pixels);target.Release();DestroyImmediate(target);DestroyImmediate(camera.gameObject);}
    }
}
