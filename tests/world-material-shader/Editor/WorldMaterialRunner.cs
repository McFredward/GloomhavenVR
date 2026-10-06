using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Actual Unity GL pixels, with explicit native parameter/lighting boundaries.
// No native controller, Windows bytecode, OpenXR frame or GPU speed is measured.
public static class WorldMaterialRunner
{
    [Serializable] private class Entry { public string name, shader, high, noise, caster, expected; }
    [Serializable] private class Manifest { public string result, evidence; public Entry[] cases; }
    private static Camera camera;
    private static GameObject surface;
    private static MeshRenderer renderer;
    private static Mesh mesh;
    private static Texture2D albedo, map;
    private static int assertions;
    private static readonly List<Object> temporary = new List<Object>();
    private const int Size = 96;
    private static void Check(bool value, string message)
    {
        assertions++;
        if (!value) throw new Exception(message);
    }
    public static void Run()
    {
        var args = Environment.GetCommandLineArgs();
        var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(args[Array.IndexOf(args,"-worldMaterialManifest")+1]));
        bool passed = true;
        using (var report = new StreamWriter(manifest.result))
        {
            report.WriteLine("Unity " + Application.unityVersion + "; device " + SystemInfo.graphicsDeviceName);
            foreach (var entry in manifest.cases)
            {
                assertions = 0;
                try
                {
                    Shader shader=Shader.Find(entry.shader), high=Shader.Find(entry.high), noise=Shader.Find(entry.noise), caster=Shader.Find(entry.caster);
                    Check(shader!=null && shader.isSupported && !ShaderUtil.ShaderHasError(shader),"production shader must compile before any control can pass");
                    Check(high!=null && high.isSupported && !ShaderUtil.ShaderHasError(high),"bounded HIGH shader must compile");
                    Check(noise!=null && noise.isSupported && !ShaderUtil.ShaderHasError(noise),"source-bound simplex probe must compile");
                    Check(caster!=null && caster.isSupported && !ShaderUtil.ShaderHasError(caster),"source-bound caster probe must compile");
                    CreateScene();
                    SlotPixels(shader);
                    ParameterPixels(shader);
                    RenderStatePixels(shader);
                    ClipPixels(high);
                    LightingPixels(shader);
                    NoisePixels(noise);
                    CasterPixels(caster);
                    if (!String.IsNullOrEmpty(entry.expected)) throw new Exception("negative control escaped: "+entry.name);
                    report.WriteLine("PASS "+entry.name+": "+assertions+" rendered assertions");
                }
                catch (Exception error)
                {
                    if (!String.IsNullOrEmpty(entry.expected) && error.Message.Contains(entry.expected))
                        report.WriteLine("PASS negative control "+entry.name+": "+error.Message);
                    else { passed=false;report.WriteLine("FAIL "+entry.name+": "+error); }
                }
                finally { CleanScene(); }
                report.Flush();
            }
        }
        EditorApplication.Exit(passed?0:1);
    }
    private static void CreateScene()
    {
        RenderSettings.fog=false;
        RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=Color.black;
        Shader.SetGlobalInteger("ToggleWallFade",0);Shader.SetGlobalFloat("_EnableOcclusionMap",1);
        surface=new GameObject("NativeStaticWorldSurface");renderer=surface.AddComponent<MeshRenderer>();
        renderer.shadowCastingMode=ShadowCastingMode.On;renderer.receiveShadows=true;
        mesh=new Mesh {name="OriginalUnchanged3DSource"};
        mesh.vertices=new[]{new Vector3(-1,-1,0),new Vector3(0,-1,0),new Vector3(0,1,0),new Vector3(-1,1,0),
            new Vector3(0,-1,0),new Vector3(1,-1,0),new Vector3(1,1,0),new Vector3(0,1,0)};
        mesh.uv=new[]{new Vector2(0,0),new Vector2(.5f,0),new Vector2(.5f,1),new Vector2(0,1),
            new Vector2(.5f,0),new Vector2(1,0),new Vector2(1,1),new Vector2(.5f,1)};
        var normals=new Vector3[8];for(int i=0;i<8;i++)normals[i]=Vector3.back;mesh.normals=normals;
        mesh.subMeshCount=2;mesh.SetTriangles(new[]{0,3,2,0,2,1},0);mesh.SetTriangles(new[]{4,7,6,4,6,5},1);mesh.RecalculateBounds();
        surface.AddComponent<MeshFilter>().sharedMesh=mesh;
        camera=new GameObject("ActualFixtureCamera").AddComponent<Camera>();camera.enabled=false;
        camera.transform.position=new Vector3(0,1,-4);camera.transform.LookAt(new Vector3(0,1,0));
        camera.orthographic=true;camera.orthographicSize=1.5f;camera.nearClipPlane=.1f;camera.farClipPlane=100;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;camera.allowHDR=true;
        camera.targetTexture=new RenderTexture(Size,Size,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
        surface.transform.position=new Vector3(0,1,0);
        albedo=new Texture2D(16,16,TextureFormat.RGBAFloat,false,true) {filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Repeat};
        var pixels=new Color[256];for(int y=0;y<16;y++)for(int x=0;x<16;x++)
            pixels[y*16+x]=new Color(.08f+.55f*x/15f,.1f+.5f*y/15f,.08f+.24f*((x+3*y)%7)/6f,.05f+.9f*x/15f);
        albedo.SetPixels(pixels);albedo.Apply();
        map=new Texture2D(8,8,TextureFormat.RGBAFloat,false,true) {filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
        var occlusion=new Color[64];for(int y=0;y<8;y++)for(int x=0;x<8;x++)occlusion[y*8+x]=new Color(x/7f,0,0,0);
        map.SetPixels(occlusion);map.Apply();Shader.SetGlobalTexture("_TilesOcclusionMap",map);
    }
    private static Material Material(Shader shader,int route)
    {
        var m=new Material(shader);temporary.Add(m);m.SetFloat("_GHVRWorldNativeRoute",route);m.SetFloat("_GHVRWorldMaterialMode",2);
        m.SetTexture("_MainTex",albedo);m.SetColor("_Tint",new Color(.9f,.5f,.7f,0));m.SetColor("_Color",new Color(.6f,.8f,.9f,.7f));
        m.SetFloat("_Cutoff",.37f);m.SetFloat("_Cutout",.78f);m.SetFloat("_UVTiling",1.37f);m.SetFloat("_UV_Offset",.17f);
        m.SetTextureScale("_MainTex",new Vector2(1.13f,.61f));m.SetTextureOffset("_MainTex",new Vector2(.23f,-.19f));
        m.SetTextureScale("_texcoord",new Vector2(.71f,1.21f));m.SetTextureOffset("_texcoord",new Vector2(-.11f,.29f));
        m.SetFloat("_WorldSpace_tiling",1.73f);m.SetFloat("_WorldSpace_FallOff",.61f);
        m.SetFloat("_Diffuse_Boost",1.27f);m.SetFloat("_Desaturation",.42f);m.SetFloat("_IsDimmed",.63f);m.SetFloat("_DimmFactor",.26f);
        return m;
    }
    private static Color[] Pixels(Material material)
    {
        renderer.sharedMaterials=new[]{material,material};camera.Render();
        var image=new Texture2D(Size,Size,TextureFormat.RGBAFloat,false,true);
        var previous=RenderTexture.active;RenderTexture.active=camera.targetTexture;
        image.ReadPixels(new Rect(0,0,Size,Size),0,0);image.Apply();var pixels=image.GetPixels();RenderTexture.active=previous;Object.DestroyImmediate(image);return pixels;
    }
    private static void Same(Color[] actual,Color[] expected,string label)
    {
        int mismatchedCoverage=0,visible=0;float maxError=0;
        for(int i=0;i<actual.Length;i++)
        {
            bool a=actual[i].a>.5f,e=expected[i].a>.5f;
            if(a!=e)mismatchedCoverage++;
            if(!a||!e)continue;
            visible++;
            maxError=Mathf.Max(maxError,Mathf.Abs(actual[i].r-expected[i].r),Mathf.Abs(actual[i].g-expected[i].g),Mathf.Abs(actual[i].b-expected[i].b));
        }
        Check(mismatchedCoverage<=2 && maxError<.004f,label+" (coverage="+mismatchedCoverage+", maxRGB="+maxError+", visible="+visible+")");
    }
    private static int Visible(Color[] pixels) {int n=0;foreach(var p in pixels)if(p.a>.5f)n++;return n;}
    private static void ParameterPixels(Shader shader)
    {
        var reference=Shader.Find("Fixture/NativeWorldParameters");
        foreach(int route in new[]{1,2,3,4,5,6,9,10})
        {
            var actual=Material(shader,route);var expected=Material(reference,route);
            foreach(bool projected in new[]{false,true})
            foreach(bool desaturate in new[]{false,true})
            {
                if(projected&&route>=9)continue;
                actual.shaderKeywords=expected.shaderKeywords=new string[0];
                if(projected){actual.EnableKeyword("_WORLDSPACE_ON");expected.EnableKeyword("_WORLDSPACE_ON");}
                if(desaturate){actual.EnableKeyword("_DESATURATION_ON");expected.EnableKeyword("_DESATURATION_ON");}
                // A tilted native mesh has signed, mixed-axis triplanar weights.
                surface.transform.rotation=projected?Quaternion.Euler(24,-37,11):Quaternion.identity;
                Same(Pixels(actual),Pixels(expected),"native albedo/UV/tint/desaturation/dim route "+route+" world="+projected+" desat="+desaturate);
            }
            surface.transform.rotation=Quaternion.identity;
            Object.DestroyImmediate(actual);Object.DestroyImmediate(expected);
        }
        var alphaActual=Material(shader,9);var alphaExpected=Material(reference,9);
        foreach(int route in new[]{1,2,9})
        foreach(bool glossAlpha in new[]{false,true})
        {
            alphaActual.SetFloat("_GHVRWorldNativeRoute",route);alphaExpected.SetFloat("_GHVRWorldNativeRoute",route);
            alphaActual.shaderKeywords=alphaExpected.shaderKeywords=new[]{route==9?"_ALPHATEST_ON":"_DIFUSE_ALPHA_ON_ON"};
            if(glossAlpha){alphaActual.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");alphaExpected.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");}
            foreach(float threshold in new[]{.15f,.45f,.8f,1.2f})
            {
                alphaActual.SetFloat("_Cutoff",threshold);alphaExpected.SetFloat("_Cutoff",threshold);
                alphaActual.SetFloat("_Cutout",threshold*.8f);alphaExpected.SetFloat("_Cutout",threshold*.8f);
                Same(Pixels(alphaActual),Pixels(alphaExpected),"native alpha threshold and Unity tint alpha route "+route);
            }
        }
        Object.DestroyImmediate(alphaActual);Object.DestroyImmediate(alphaExpected);
        // Unity's desktop-GL fog macro uses clip-space z. A perspective view
        // supplies a positive distance coordinate; the orthographic coverage
        // fixture's near-depth z would otherwise clamp the fog factor to one.
        camera.orthographic=false;
        var fogActual=Material(shader,1);var fogExpected=Material(reference,1);
        Color unfogged=Pixels(fogExpected)[48*Size+48];
        RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogColor=new Color(.1f,.2f,.4f);RenderSettings.fogStartDistance=0;RenderSettings.fogEndDistance=8;
        fogActual.EnableKeyword("FOG_LINEAR");fogExpected.EnableKeyword("FOG_LINEAR");
        var fogPixels=Pixels(fogExpected);
        Check(Vector3.Distance(new Vector3(unfogged.r,unfogged.g,unfogged.b),new Vector3(fogPixels[48*Size+48].r,fogPixels[48*Size+48].g,fogPixels[48*Size+48].b))>.03f,"explicit original fog boundary produces visible native parameter pixels");
        Same(Pixels(fogActual),fogPixels,"native Unity fog boundary survives radical textured stage");
        RenderSettings.fog=false;camera.orthographic=true;Object.DestroyImmediate(fogActual);Object.DestroyImmediate(fogExpected);
    }
    private static void SlotPixels(Shader shader)
    {
        var material=Material(shader,1);material.SetTexture("_MainTex",Texture2D.whiteTexture);material.SetFloat("_Desaturation",0);material.SetFloat("_IsDimmed",0);material.SetFloat("_Diffuse_Boost",1);
        renderer.sharedMaterials=new[]{material,material};
        var whole=new MaterialPropertyBlock();whole.SetColor("_Tint",Color.blue);renderer.SetPropertyBlock(whole);
        var left=new MaterialPropertyBlock();left.SetColor("_Tint",Color.green);renderer.SetPropertyBlock(left,0);
        var right=new MaterialPropertyBlock();right.SetColor("_Tint",Color.red);renderer.SetPropertyBlock(right,1);
        var pixels=Pixels(material);Color l=pixels[48*Size+32],r=pixels[48*Size+64];
        Check(l.g>.95f&&l.r<.01f&&r.r>.95f&&r.g<.01f,"actual material-slot MPB pixels override renderer tint without cross-slot leakage");
        left.SetFloat("_Cutoff",-.1f);right.SetFloat("_Cutoff",1.2f);
        renderer.SetPropertyBlock(left,0);renderer.SetPropertyBlock(right,1);
        pixels=Pixels(material);
        Check(pixels[48*Size+32].a>.95f&&pixels[48*Size+64].a<.01f,"actual material-slot MPB authored cutoffs keep separate native visibility");
        renderer.SetPropertyBlock(null,0);renderer.SetPropertyBlock(null,1);renderer.SetPropertyBlock(null);Object.DestroyImmediate(material);
    }
    private static void RenderStatePixels(Shader shader)
    {
        var front=Material(shader,10);front.SetTexture("_MainTex",Texture2D.whiteTexture);front.SetColor("_Color",Color.green);
        surface.transform.rotation=Quaternion.Euler(0,180,0);
        Check(Visible(Pixels(front))==0,"native Back culling preserves the original rear-face boundary");
        surface.transform.rotation=Quaternion.identity;
        var rear=new GameObject("IndependentDepthWitness");rear.AddComponent<MeshFilter>().sharedMesh=mesh;
        var rearRenderer=rear.AddComponent<MeshRenderer>();var behind=new Material(Shader.Find("Unlit/Color"));behind.SetColor("_Color",Color.red);behind.renderQueue=2010;
        temporary.Add(rear);temporary.Add(behind);
        rearRenderer.sharedMaterials=new[]{behind,behind};rear.transform.position=new Vector3(0,1,.25f);
        Color pixel=Pixels(front)[48*Size+48];
        Check(pixel.g>.95f&&pixel.r<.01f,"native depth-writing geometry occludes later rear draws");
        Object.DestroyImmediate(rear);Object.DestroyImmediate(behind);Object.DestroyImmediate(front);
    }
    private static void ClipPixels(Shader high)
    {
        var reference=Shader.Find("Fixture/NativeWorldParameters");
        Shader.SetGlobalInteger("ToggleWallFade",1);
        foreach(int route in new[]{1,2,3,4})
        {
            var actual=Material(high,route);var expected=Material(reference,route);actual.SetFloat("_NativeNoise",.003f);expected.SetFloat("_NativeNoise",.003f);
            string gate=route==1?"_WALLFADE_ON_ON":route==2?"_TOGGLEWALLFADE_ON":"";
            foreach(bool active in new[]{false,true})
            foreach(bool alpha in new[]{false,true})
            {
                actual.shaderKeywords=expected.shaderKeywords=new string[0];
                if(active&&gate.Length>0){actual.EnableKeyword(gate);expected.EnableKeyword(gate);}
                if(!active&&route>=3){actual.EnableKeyword("_TOGGLEWALLFADEOFF_ON");expected.EnableKeyword("_TOGGLEWALLFADEOFF_ON");}
                if(alpha){actual.EnableKeyword("_DIFUSE_ALPHA_ON_ON");expected.EnableKeyword("_DIFUSE_ALPHA_ON_ON");}
                int changes=0,last=-1;
                for(int step=0;step<9;step++)
                {
                    float cutoff=-.1f+step*.17f;
                    actual.SetFloat("_Cutoff",cutoff);expected.SetFloat("_Cutoff",cutoff);
                    actual.SetFloat("_Cutout",cutoff+.03f);expected.SetFloat("_Cutout",cutoff+.03f);
                    var pixels=Pixels(actual);Same(pixels,Pixels(expected),"native continuous wall/alpha coverage route "+route+" active="+active+" alpha="+alpha);
                    int count=Visible(pixels);if(last>=0&&count!=last)changes++;last=count;
                }
                if(active&&!alpha)Check(changes>=3,"active native wall fade keeps multiple visible intermediate pictures");
            }
            // Original WORLD-height gate must remain correct under translated source vertices.
            if(route==2||route==4)
            {
                actual.shaderKeywords=expected.shaderKeywords=new[]{gate};
                if(route==4)actual.shaderKeywords=expected.shaderKeywords=new string[0];
                surface.transform.position=new Vector3(0,.1f,0);camera.transform.position=new Vector3(0,.1f,-4);
                actual.SetFloat("_Cutoff",.9f);expected.SetFloat("_Cutoff",.9f);actual.SetFloat("_Cutout",.9f);expected.SetFloat("_Cutout",.9f);
                Same(Pixels(actual),Pixels(expected),"native LOW foundation uses translated world height");
                surface.transform.position=new Vector3(0,1,0);camera.transform.position=new Vector3(0,1,-4);
            }
            foreach(int toggle in new[]{0,1})
            foreach(float enable in new[]{0f,.35f,1f})
            {
                Shader.SetGlobalInteger("ToggleWallFade",toggle);Shader.SetGlobalFloat("_EnableOcclusionMap",enable);
                foreach(float cutoff in new[]{-.2f,.51f,1.2f})
                {
                    actual.SetFloat("_Cutoff",cutoff);expected.SetFloat("_Cutoff",cutoff);
                    Same(Pixels(actual),Pixels(expected),"live native global toggle/map-scale and authored cutoff route "+route);
                }
            }
            Shader.SetGlobalInteger("ToggleWallFade",1);Shader.SetGlobalFloat("_EnableOcclusionMap",1);
            actual.SetFloat("_GHVRWorldNeverFade",1);expected.SetFloat("_GHVRWorldNeverFade",1);
            actual.EnableKeyword("_DIFUSE_ALPHA_ON_ON");expected.EnableKeyword("_DIFUSE_ALPHA_ON_ON");
            actual.SetFloat("_Cutoff",.5f);expected.SetFloat("_Cutoff",.5f);actual.SetFloat("_Cutout",.5f);expected.SetFloat("_Cutout",.5f);
            Same(Pixels(actual),Pixels(expected),"authored floor bypass retains independent albedo cutout");
            Object.DestroyImmediate(actual);Object.DestroyImmediate(expected);
        }
        Shader.SetGlobalInteger("ToggleWallFade",0);Shader.SetGlobalFloat("_EnableOcclusionMap",1);
    }
    private static void LightingPixels(Shader shader)
    {
        var material=Material(shader,10);material.SetColor("_Color",Color.white);material.SetTexture("_MainTex",Texture2D.whiteTexture);
        RenderSettings.ambientLight=new Color(.1f,.2f,.3f);DynamicGI.UpdateEnvironment();
        var sun=new GameObject("DeliberatelySimplifiedNativeLight").AddComponent<Light>();sun.type=LightType.Directional;sun.color=Color.white;sun.intensity=.4f;sun.transform.rotation=Quaternion.identity;RenderSettings.sun=sun;
        temporary.Add(sun.gameObject);
        material.SetFloat("_GHVRWorldMaterialMode",1);Color lit=Pixels(material)[48*Size+48];
        material.SetFloat("_GHVRWorldMaterialMode",2);Color radical=Pixels(material)[48*Size+48];
        Check(Mathf.Abs(radical.r-1)<.005f&&Mathf.Abs(radical.g-1)<.005f&&Mathf.Abs(radical.b-1)<.005f,"radical textured mode removes lighting work while retaining native albedo");
        Check(Vector3.Distance(new Vector3(lit.r,lit.g,lit.b),Vector3.one)>.1f&&lit.r>.1f,"simple lit stage has a separate, useful diffuse-light result");
        Object.DestroyImmediate(sun.gameObject);RenderSettings.sun=null;RenderSettings.ambientLight=Color.black;Object.DestroyImmediate(material);
    }
    [Serializable] private class Sample { public float[] position; public float nativeNoise42; }
    [Serializable] private class Samples { public Sample[] samples; }
    private static void NoisePixels(Shader shader)
    {
        var values=JsonUtility.FromJson<Samples>(File.ReadAllText(Environment.GetEnvironmentVariable("GHVR_WORLD_NOISE_VECTORS")));
        var material=new Material(shader);
        temporary.Add(material);
        foreach(var sample in values.samples)
        {
            material.SetVector("_SamplePosition",new Vector4(sample.position[0],sample.position[1],sample.position[2],0));
            float actual=(Pixels(material)[48*Size+48].r-.5f)*2;
            Check(Mathf.Abs(actual-sample.nativeNoise42)<.002f,"production simplex matches independent original DXBC float32 samples");
        }
        Object.DestroyImmediate(material);
    }
    private static void CasterPixels(Shader shader)
    {
        var material=Material(shader,1);material.SetTexture("_MainTex",Texture2D.blackTexture);material.SetFloat("_Cutoff",.95f);material.SetFloat("_Cutout",0);
        foreach(int route in new[]{1,2,3,4,6,10})
        {
            material.SetFloat("_GHVRWorldNativeRoute",route);material.shaderKeywords=new[]{"_DIFUSE_ALPHA_ON_ON","_ALPHATEST_ON"};
            Check(Visible(Pixels(material))>1000,"audited native AMP/fallback caster keeps opaque geometry independently of main fade/alpha");
        }
        material.SetFloat("_GHVRWorldNativeRoute",9);material.SetColor("_Color",Color.white);
        Check(Visible(Pixels(material))==0,"native Standard caster retains its tinted albedo alpha clip");
        material.SetFloat("_GHVRWorldNativeRoute",5);material.SetFloat("_Cutoff",1.2f);
        Check(Visible(Pixels(material))==0,"native non-dissolving HIGH Basic caster retains authored cutoff above one");
        Object.DestroyImmediate(material);
    }
    private static void CleanScene()
    {
        // Failed causal cases own these objects too. In particular, a depth
        // witness must never survive its failed assertion into a later case.
        foreach(Object value in temporary)if(value!=null)Object.DestroyImmediate(value);
        temporary.Clear();RenderSettings.sun=null;RenderSettings.ambientLight=Color.black;
        if(camera!=null){if(camera.targetTexture!=null)Object.DestroyImmediate(camera.targetTexture);Object.DestroyImmediate(camera.gameObject);}
        if(surface!=null)Object.DestroyImmediate(surface);if(mesh!=null)Object.DestroyImmediate(mesh);
        if(albedo!=null)Object.DestroyImmediate(albedo);if(map!=null)Object.DestroyImmediate(map);
        Shader.SetGlobalInteger("ToggleWallFade",0);Shader.SetGlobalTexture("_TilesOcclusionMap",null);RenderSettings.fog=false;
    }
}
