using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Bounded original native shadow clip/depth oracle, separate from game.</summary>
public sealed class QuestShaderShadowOracle : MonoBehaviour
{
    [Serializable] public sealed class Configuration
    {
        public string originalBundle, candidateBundle, shaderAddress, output, originalReadbacks;
        public bool vulkan;
        public int pass = 3, width = 128;
    }
    [Serializable] public sealed class Case { public string id; public float maximumError; public int changed, foreground; public bool passed; }
    [Serializable] public sealed class Receipt
    {
        public string unityVersion, backend, device;
        public Case[] cases;
        public bool actualOriginalNativeShadowPass, originalD3DReadbacksCompared, headsetPictureVerified;
    }
    private Configuration input;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "--quest-shadow-config");
        if (index < 0) return;
        var oracle = new GameObject("OriginalShadowDepthOracle").AddComponent<QuestShaderShadowOracle>();
        oracle.input = JsonUtility.FromJson<Configuration>(File.ReadAllText(args[index + 1]));
    }
    private IEnumerator Start()
    {
        yield return null;
        try { Run(); Application.Quit(0); }
        catch (Exception error) { Debug.LogError(error); Application.Quit(1); }
    }
    private void Run()
    {
        if (Application.unityVersion != "2021.3.5f1" || SystemInfo.graphicsDeviceType != (input.vulkan ? GraphicsDeviceType.Vulkan : GraphicsDeviceType.Direct3D11))
            throw new InvalidOperationException("Shadow witness requires the actual original graphics backend.");
        Directory.CreateDirectory(input.output);
        var bundle = AssetBundle.LoadFromFile(input.vulkan ? input.candidateBundle : input.originalBundle);
        var shader = bundle.LoadAsset<Shader>(input.shaderAddress);
        if (shader == null || shader.name != "Amp_Char_Shader") throw new InvalidOperationException("Exact original shadow shader identity is absent.");
        var material = new Material(shader);
        material.shaderKeywords = new[] { "SHADOWS_DEPTH" };
        material.SetFloat("_Opacity", 1); material.SetFloat("_Cutoff", 0.05f);
        material.SetFloat("_AddVertexAnim", 0); material.SetFloat("_Toggle_Dissolve", 0);
        material.SetFloat("_CharHeight", 2); material.SetFloat("_InvisibilityControl", 0);
        material.SetTexture("_Diffuse", Texture2D.whiteTexture); material.SetTexture("_OpacityTexture", Texture2D.whiteTexture);
        material.SetTexture("_EmissiveMap", Texture2D.whiteTexture);
        var mesh = new Mesh { vertices = new[] { new Vector3(-.7f,-.7f,0), new Vector3(0,.7f,0), new Vector3(.7f,-.7f,0) },
            normals = new[] { Vector3.back, Vector3.back, Vector3.back }, tangents = new[] { new Vector4(1,0,0,1),new Vector4(1,0,0,1),new Vector4(1,0,0,1) },
            uv = new[] { Vector2.zero, Vector2.up, Vector2.right }, triangles = new[] { 0,1,2 } };
        mesh.RecalculateBounds();
        var copy = bundle.LoadAsset<Shader>("depth-copy");
        if (copy == null)
        {
            var helper = AssetBundle.LoadFromFile(input.candidateBundle);
            copy = helper.LoadAsset<Shader>("depth-copy");
        }
        if (copy == null) throw new InvalidOperationException("Exact depth copy shader is absent.");
        var copier = new Material(copy);
        var cases = new Case[3]; float[] baseline = null;
        var biases = new[] { new Vector4(.05f,.2f,0,0), new Vector4(.9f,.2f,0,0), new Vector4(.05f,.2f,0,0) };
        string[] ids = { "baseline", "increased-shadow-bias", "alpha-clip-negative" };
        for (int test = 0; test < cases.Length; test++)
        {
            
            material.SetFloat("_Cutoff", test == 2 ? 2f : .05f);
            float[] actual = Capture(material, copier, mesh, biases[test]);
            if (test == 0) baseline = actual;
            byte[] bytes = new byte[actual.Length * 4]; Buffer.BlockCopy(actual,0,bytes,0,bytes.Length);
            File.WriteAllBytes(Path.Combine(input.output,ids[test]+".f32"),bytes);
            float max = 0; int foreground = 0, changed = 0;
            for(int n=0;n<actual.Length;n++) { if(actual[n]>.00001f)foreground++;if(Math.Abs(actual[n]-baseline[n])>.00001f)changed++; }
            if(input.vulkan)
            {
                byte[] original = File.ReadAllBytes(Path.Combine(input.originalReadbacks,ids[test]+".f32"));
                if(original.Length!=bytes.Length)throw new InvalidOperationException("Original shadow readback extent differs.");
                var native = new float[actual.Length];Buffer.BlockCopy(original,0,native,0,original.Length);
                for(int n=0;n<actual.Length;n++)max=Math.Max(max,Math.Abs(actual[n]-native[n]));
            }
            bool passed=max<0.000001f&&(test==2?foreground==0:foreground>100)&&(test==0||changed>100);
            cases[test]=new Case{id=ids[test],maximumError=max,foreground=foreground,changed=changed,passed=passed};
            if(!passed)throw new InvalidOperationException("Native shadow/depth witness failed: "+ids[test]+" foreground="+foreground+" changed="+changed+" max="+max);
        }
        File.WriteAllText(Path.Combine(input.output,"shadow-depth.json"),JsonUtility.ToJson(new Receipt{unityVersion=Application.unityVersion,backend=SystemInfo.graphicsDeviceType.ToString(),device=SystemInfo.graphicsDeviceName,cases=cases,actualOriginalNativeShadowPass=!input.vulkan,originalD3DReadbacksCompared=input.vulkan},true)+"\n");
        Debug.Log("PASS native original shadow clip/depth witness.");
    }
    private float[] Capture(Material material, Material copy, Mesh mesh, Vector4 bias)
    {
        QualitySettings.shadows=ShadowQuality.All;QualitySettings.shadowCascades=0;QualitySettings.shadowDistance=5;
        var camera = new GameObject("NativeShadowCamera").AddComponent<Camera>();
        camera.enabled=false;camera.orthographic=true;camera.orthographicSize=1.2f;camera.nearClipPlane=.01f;camera.farClipPlane=5;
        camera.transform.position=new Vector3(0,0,-3);camera.transform.LookAt(Vector3.zero);camera.renderingPath=RenderingPath.Forward;
        var light = new GameObject("NativeShadowLight").AddComponent<Light>();
        light.type=LightType.Directional;light.shadows=LightShadows.Hard;light.shadowCustomResolution=input.width;
        light.shadowBias=bias.x;light.shadowNormalBias=0;light.shadowNearPlane=bias.y;light.transform.rotation=Quaternion.Euler(24,-16,0);
        var subject=new GameObject("NativeShadowSubject");subject.AddComponent<MeshFilter>().sharedMesh=mesh;
        var renderer=subject.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.On;renderer.receiveShadows=true;
        var target=new RenderTexture(input.width,input.width,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);target.Create();camera.targetTexture=target;
        var color = new RenderTexture(input.width,input.width,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);color.Create();
        var previous = RenderTexture.active;var pixels = new Texture2D(input.width,input.width,TextureFormat.RGBAFloat,false,true);
        var command = new CommandBuffer();
        try
        {
            command.SetShadowSamplingMode(BuiltinRenderTextureType.CurrentActive,ShadowSamplingMode.RawDepth);
            command.SetGlobalTexture("_ShadowWitnessTex",BuiltinRenderTextureType.CurrentActive,RenderTextureSubElement.Depth);
            command.Blit(BuiltinRenderTextureType.None,color,copy,0);
            command.SetShadowSamplingMode(BuiltinRenderTextureType.CurrentActive,ShadowSamplingMode.CompareDepths);
            light.AddCommandBuffer(LightEvent.AfterShadowMap,command);
            camera.Render();
            RenderTexture.active=color;pixels.ReadPixels(new Rect(0,0,input.width,input.width),0,0,false);pixels.Apply(false,false);
            var colors=pixels.GetPixels();var values=new float[colors.Length];for(int n=0;n<values.Length;n++)values[n]=colors[n].r;return values;
        }
        finally { light.RemoveCommandBuffer(LightEvent.AfterShadowMap,command);command.Dispose();RenderTexture.active=previous;DestroyImmediate(pixels);target.Release();color.Release();DestroyImmediate(target);DestroyImmediate(color);DestroyImmediate(subject);DestroyImmediate(light.gameObject);DestroyImmediate(camera.gameObject); }
    }
}
