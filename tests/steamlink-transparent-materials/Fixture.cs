using System;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Core.SteamLink;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteInEditMode]
public sealed class TransparentMaterialRead : MonoBehaviour
{
    public Color[] pixels;
    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        RenderTexture old=RenderTexture.active;
        var read=new Texture2D(source.width,source.height,TextureFormat.RGBAFloat,false,true);
        try{RenderTexture.active=source;read.ReadPixels(new Rect(0,0,source.width,source.height),0,0,false);read.Apply();pixels=read.GetPixels();Graphics.Blit(source,destination);}
        finally{RenderTexture.active=old;DestroyImmediate(read);}
    }
}
public static class TransparentMaterialFixture
{
    [Serializable] public sealed class Row{public string name;public int layers;public float originalAlpha,coverageAlpha;}
    [Serializable] public sealed class Report
    {
        public bool passed,xrExecuted,windowsExecuted,nativeVfxCertified;
        public string unity,backend,device,colorSpace,failure;
        public int checks,lifecycleChecks,rowsCompleted;
        public List<Row> rows=new List<Row>();
    }
    private static Report result=new Report();
    private static List<UnityEngine.Object> owned=new List<UnityEngine.Object>();
    private static SceneTransparentMaterials helper;
    private static AssetBundle bank;
    private static Dictionary<string,Shader> bankShaders=new Dictionary<string,Shader>();
    private static Camera head,foreign;
    private static bool hooks,nested;
    private static Renderer nestedRenderer;
    private static Material nestedSource;
    private static void Check(bool condition,string why){result.checks++;if(!condition)throw new Exception(why);}
    private static void Life(bool condition,string why){result.lifecycleChecks++;Check(condition,why);}
    private static T Keep<T>(T value)where T:UnityEngine.Object{owned.Add(value);return value;}
    private static void Before(Camera c)
    {
        if(!hooks)return;
        helper.EnterHead(c);
        if(c==foreign && nestedRenderer!=null)Life(nestedRenderer.sharedMaterial==nestedSource,"Nested foreign sees exact original material");
        if(c==head && nested)
        {
            nested=false;foreign.Render();
            if(nestedRenderer!=null)Life(nestedRenderer.sharedMaterial!=nestedSource,"Head clone resumes after nested foreign camera");
        }
    }
    private static void After(Camera c){if(hooks)helper.ExitHead(c);}
    public static void Start()
    {
        string[] args=Environment.GetCommandLineArgs();string output=args[Array.IndexOf(args,"-transparentResult")+1];int code=1;
        try
        {
            result.unity=Application.unityVersion;result.backend=SystemInfo.graphicsDeviceType.ToString();result.device=SystemInfo.graphicsDeviceName;
            result.colorSpace=QualitySettings.activeColorSpace.ToString();Check(result.backend!="Null" && result.colorSpace=="Gamma","Actual Gamma GPU");
            bank=AssetBundle.LoadFromFile(args[Array.IndexOf(args,"-transparentBank")+1]);
            Shader[] loaded=bank!=null?bank.LoadAllAssets<Shader>():new Shader[0];
            Check(loaded.Length==17,"Actual frozen shipping shader bank loads");
            foreach(Shader item in loaded)bankShaders.Add(item.name,item);
            head=CameraFor("Owned head");foreign=CameraFor("Foreign capture");
            helper=new SceneTransparentMaterials(name=>Variant(name));Life(helper.BindHead(head),"Explicit cold head binding");
            Camera.onPreRender+=Before;Camera.onPostRender+=After;
            foreach(string name in new[]{"GloomhavenVR/EnvParticleAlpha","GloomhavenVR/EnvParticleAdd","GloomhavenVR/Overlay"})
                foreach(int layers in new[]{1,2})Probe(name,layers);
            Lifecycle();
            helper.Dispose();Life(!helper.Ready && !helper.BindHead(head),"Disposed cannot bind");
            result.passed=true;code=0;
        }
        catch(Exception error){result.failure=error.ToString();Debug.LogException(error);}
        finally
        {
            hooks=false;Camera.onPreRender-=Before;Camera.onPostRender-=After;
            if(helper!=null)helper.Dispose();
            for(int i=owned.Count-1;i>=0;i--)if(owned[i])UnityEngine.Object.DestroyImmediate(owned[i]);
            if(bank!=null)bank.Unload(true);
            File.WriteAllText(output,JsonUtility.ToJson(result,true)+"\n");
        }
        EditorApplication.Exit(code);
    }
    private static Camera CameraFor(string name)
    {
        var go=Keep(new GameObject(name));Camera c=go.AddComponent<Camera>();c.enabled=false;c.orthographic=true;c.orthographicSize=1;
        c.transform.position=new Vector3(0,0,-5);c.nearClipPlane=.1f;c.farClipPlane=20;c.clearFlags=CameraClearFlags.SolidColor;
        c.backgroundColor=Color.clear;c.allowHDR=false;c.renderingPath=RenderingPath.Forward;c.cullingMask=1<<30;go.AddComponent<TransparentMaterialRead>();
        var rt=Keep(new RenderTexture(16,16,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear));Check(rt.Create(),"Actual float target");c.targetTexture=rt;return c;
    }
    private static Material MaterialFor(string name)
    {
        Shader shader=Shader.Find(name);Shader variant=Variant(name);
        Check(shader && variant && shader.isSupported && variant.isSupported && !ShaderUtil.ShaderHasError(shader) && !ShaderUtil.ShaderHasError(variant),
              "Actual original and variant compile: "+name+" original="+(shader?shader.isSupported.ToString():"missing")+" variant="+(variant?variant.isSupported.ToString():"missing")
              +" originalErrors="+(shader?ShaderUtil.ShaderHasError(shader).ToString():"missing")+" variantErrors="+(variant?ShaderUtil.ShaderHasError(variant).ToString():"missing"));
        var m=Keep(new Material(shader));m.SetColor(name=="GloomhavenVR/Overlay"?"_Color":"_Tint",new Color(.2f,.3f,.4f,.5f));
        var texture=Keep(new Texture2D(1,1,TextureFormat.RGBAFloat,false,true));texture.SetPixel(0,0,Color.white);texture.Apply();m.SetTexture("_MainTex",texture);return m;
    }
    private static Shader Variant(string name){Shader shader;return bankShaders.TryGetValue(name+SceneTransparentMaterials.Suffix,out shader)?shader:null;}
    private static Renderer Surface(Material m,float z=0)
    {
        var go=Keep(new GameObject("Owned test surface"));go.layer=30;go.transform.position=new Vector3(0,0,z);
        var mesh=Keep(new Mesh());mesh.vertices=new[]{new Vector3(-1,-1,0),new Vector3(1,-1,0),new Vector3(1,1,0),new Vector3(-1,1,0)};
        mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};mesh.colors=new[]{Color.white,Color.white,Color.white,Color.white};mesh.triangles=new[]{0,1,2,0,2,3};mesh.RecalculateBounds();
        go.AddComponent<MeshFilter>().sharedMesh=mesh;Renderer r=go.AddComponent<MeshRenderer>();r.sharedMaterial=m;return r;
    }
    private static Color[] Render(Camera camera,bool enabled){hooks=enabled;camera.Render();hooks=false;return camera.GetComponent<TransparentMaterialRead>().pixels;}
    private static int Bits(float value){return BitConverter.ToInt32(BitConverter.GetBytes(value),0);}
    private static void Probe(string name,int layers)
    {
        int start=owned.Count;
        try
        {
            Material source=MaterialFor(name);Renderer r=Surface(source);if(layers==2)Surface(source,-.2f);
            Life(helper.RefreshCold(),"Cold inventory");Color[] baseline=Render(head,false);
            nestedRenderer=r;nestedSource=source;nested=true;
            Color[] candidate=Render(head,true);Color[] foreignPixels=foreign.GetComponent<TransparentMaterialRead>().pixels;
            float expected=name.EndsWith("Add")?0:(layers==1?.5f:.75f);
            for(int i=0;i<256;i++)
            {
                Check(Bits(baseline[i].r)==Bits(candidate[i].r) && Bits(baseline[i].g)==Bits(candidate[i].g) && Bits(baseline[i].b)==Bits(candidate[i].b),"Actual original/clone RGB bits "+name);
                Check(Mathf.Abs(candidate[i].a-expected)<.00001f,"Correct alpha recurrence "+name+": "+candidate[i].a);
                Check(baseline[i]==foreignPixels[i],"Same Renderer foreign Camera retains exact original RGBA");
            }
            Life(r.sharedMaterial==source,"Head postrender restored original slot");
            Color[] off=Render(head,false);for(int i=0;i<256;i++)Check(off[i]==baseline[i],"OFF original pixels");
            result.rows.Add(new Row{name=name,layers=layers,originalAlpha=baseline[128].a,coverageAlpha=candidate[128].a});result.rowsCompleted++;
        }
        finally
        {
            nestedRenderer=null;nestedSource=null;
            for(int i=owned.Count-1;i>=start;i--){if(owned[i])UnityEngine.Object.DestroyImmediate(owned[i]);owned.RemoveAt(i);}
        }
    }
    private static void Lifecycle()
    {
        Material source=MaterialFor("GloomhavenVR/Overlay"),foreignWrite=MaterialFor("GloomhavenVR/Overlay");Renderer r=Surface(source);
        helper.RefreshCold();helper.EnterHead(head);Material clone=r.sharedMaterial;Life(clone!=source,"Borrowed private material");
        source.SetColor("_Color",new Color(.7f,.2f,.3f,.25f));helper.EnterHead(foreign);Life(r.sharedMaterial==source,"Foreign suspension");
        helper.ExitHead(foreign);Life(r.sharedMaterial.GetColor("_Color")==source.GetColor("_Color"),"Reentry copies current properties");
        r.sharedMaterial=foreignWrite;helper.ExitHead(head);Life(r.sharedMaterial==foreignWrite,"Foreign slot write wins stale restoration");
        r.sharedMaterial=source;source.SetFloat("_SrcBlend",7);helper.EnterHead(head);Life(r.sharedMaterial==source,"Destination-alpha RGB feedback mode refused");helper.ExitHead(head);
        source.SetFloat("_SrcBlend",5);source.SetFloat("_DstBlend",10);
        var block=new MaterialPropertyBlock();block.SetFloat("_SrcBlend",5);r.SetPropertyBlock(block);
        helper.EnterHead(head);Life(r.sharedMaterial==source,"Render-state MPB refused");helper.ExitHead(head);
        block.Clear();block.SetColor("_Color",Color.red);r.SetPropertyBlock(block);
        helper.EnterHead(head);Life(r.sharedMaterial!=source,"Harmless color MPB retained with clone");helper.ExitHead(head);r.SetPropertyBlock(null);
        helper.EnterHead(head);r.sharedMaterials=new[]{r.sharedMaterial,foreignWrite};helper.ExitHead(head);
        Material[] slots=r.sharedMaterials;Life(slots.Length==2 && slots[0]==source && slots[1]==foreignWrite,"Changed slots preserve count/order and foreign write");
        r.sharedMaterial=source;helper.EnterHead(head);helper.ExitHead(foreign);Life(!helper.Ready && helper.Reason==SceneTransparentMaterials.Refusal.Sequence && r.sharedMaterial==source,"Mismatched callback fails closed");
        Life(helper.RefreshCold() && helper.Ready,"Cold refresh resets sequence fault");
        for(int i=0;i<9;i++)helper.EnterHead(head);
        Life(!helper.Ready && helper.Reason==SceneTransparentMaterials.Refusal.StackOverflow && r.sharedMaterial==source,"Bounded stack overflow restores originals");
        helper.RefreshCold();helper.EnterHead(head);
        var growth=new Material[65];for(int i=0;i<growth.Length;i++)growth[i]=i==0?r.sharedMaterial:foreignWrite;r.sharedMaterials=growth;
        helper.ExitHead(head);slots=r.sharedMaterials;
        Life(!helper.Ready && helper.Reason==SceneTransparentMaterials.Refusal.SlotOverflow && slots[0]==source && slots[64]==foreignWrite,"Exceptional foreign growth restores then refuses epoch");
        helper.RefreshCold();r.sharedMaterial=source;helper.RefreshCold();helper.EnterHead(head);helper.DisarmImmediate();
        Life(r.sharedMaterial==source && !helper.Ready,"Immediate disarm restores during head render");
        Shader unknownShader=Shader.Find("Hidden/Internal-Colored");Check(unknownShader!=null,"Unknown fixture shader exists");
        Material unknown=Keep(new Material(unknownShader));Renderer untouched=Surface(unknown);
        helper.BindHead(head);helper.RefreshCold();helper.EnterHead(head);
        Life(untouched.sharedMaterial==unknown && helper.UnclassifiedSlots>0,"Unknown native-style shader remains untouched and unclassified");helper.ExitHead(head);
        helper.BindHead(head);helper.RefreshCold();helper.EnterHead(head);helper.Dispose();
        Life(r.sharedMaterial==source,"Dispose restores only owned slots");
    }
}
