using System;
using GloomhavenVR.Core.SteamLink;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

// Explicit pool-return boundary. The production postfix and exact five-argument
// target signature execute; the publisher's pool/gameplay implementation does not.
public static class ObjectPool
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static GameObject Spawn(GameObject prefab, Transform parent, Vector3 position, Quaternion rotation, bool forceInstantiate)
    {
        if(prefab==null)throw new InvalidOperationException("original pool exception");
        var result=UnityEngine.Object.Instantiate(prefab,parent);result.transform.SetPositionAndRotation(position,rotation);result.SetActive(true);return result;
    }
}
namespace GloomhavenVR.Core
{
    internal static class VRSession { internal static Harmony Harmony=new Harmony("ghvr.sceneAlpha761.fixture"); }
    internal static class VRLog { internal static void Info(string module,string message){} }
}

public static partial class TransparentMaterialFixture
{
    private static UnityEngine.UI.Graphic traceGraphic761;
    private static System.Collections.Generic.List<string> trace761=new System.Collections.Generic.List<string>();
    internal static void Trace761(string label)
    {
        if(!nativePorts761||traceGraphic761==null||trace761.Count>=60)return;
        var canvas=traceGraphic761.canvasRenderer;var material=canvas.materialCount>0?canvas.GetMaterial():null;
        trace761.Add(label+" currentCamera="+(Camera.current!=null?Camera.current.name:"null")+" material="+(material!=null?material.shader.name+"#"+material.GetInstanceID():"none")+" depth="+typeof(SceneTransparentMaterials).GetField("_depth",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(helper));
    }
    private static void PreCanvas761()=>Trace761("preWillRenderCanvas");
    private static void WillCanvas761()=>Trace761("willRenderCanvas");
    private static Material NativeMaterial761(string name,Color color,bool variant=false)
    {
        string path="NativePorts761/"+name.Replace('/','-')+(variant?"-coverage":"-original");
        Shader shader=Resources.Load<Shader>(path);Check(shader!=null&&shader.isSupported&&!UnityEditor.ShaderUtil.ShaderHasError(shader),"native-state RGBA port compiles: "+name);
        var material=Keep(new Material(shader));material.color=color;
        var texture=Keep(new Texture2D(1,1,TextureFormat.RGBAFloat,false,true));texture.SetPixel(0,0,Color.white);texture.Apply();material.mainTexture=texture;
        return material;
    }
    private static RawImage NativeGraphic761(Material material,float z)
    {
        var go=Keep(new GameObject("Native-state card canvas",typeof(RectTransform),typeof(Canvas)));go.layer=30;
        var canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.worldCamera=head;canvas.sortingOrder=1;
        var frame=(RectTransform)go.transform;frame.sizeDelta=new Vector2(2,2);frame.position=new Vector3(0,0,z);
        var ink=Keep(new GameObject("Original card graphic",typeof(RectTransform)));ink.layer=30;ink.transform.SetParent(frame,false);
        var rect=(RectTransform)ink.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.sizeDelta=Vector2.zero;
        var image=ink.AddComponent<RawImage>();image.material=material;image.texture=material.mainTexture;
        return image;
    }
    private static void NativeAlpha761()
    {
        SceneMaterialObservers.Install();
        foreach(string name in new[]{"room","opaque-wall","late-opaque-ui","two-translucent","smoke-over-wall","additive-room","alpha-clipped","stencil-clipped"})NativeCase761(name);
        NativeArrivalControls761();
        NativeGlobalLifetime761();
        NativeGeneration761();
    }
    private static void NativeGlobalProbe761()
    {
        int first=owned.Count;
        try
        {
            Shader.SetGlobalFloat("_NativePortAlpha761",5);
            var privateVariant=NativeMaterial761("GUI/AbilityCard_Shd",new Color(.2f,.3f,.4f,.5f),true);
            var graphic=NativeGraphic761(privateVariant,0);traceGraphic761=graphic;Canvas.ForceUpdateCanvases();
            Color[] stock=Render(head,false);Check(Mathf.Abs(stock[0].a-.25f)<.00001f,"global alpha state without material property preserves native recurrence");
            nested=true;nestedRenderer=null;
            Color[] candidate=Render(head,true);Color[] other=foreign.GetComponent<TransparentMaterialRead>().pixels;
            for(int i=0;i<256;i++)
            {
                Check(Mathf.Abs(candidate[i].a-.5f)<.00001f,"per-draw global alpha reads current head scope: "+candidate[i]);
                Check(other[i]==stock[i],"per-draw global alpha preserves exact nested foreign RGBA: "+other[i]);
                Check(Bits(candidate[i].r)==Bits(stock[i].r)&&Bits(candidate[i].g)==Bits(stock[i].g)&&Bits(candidate[i].b)==Bits(stock[i].b),"state-only alpha retains exact RGB");
            }
            result.rows.Add(new Row{name="native761-global-state",layers=1,originalAlpha=stock[0].a,coverageAlpha=candidate[0].a});result.rowsCompleted++;
        }
        finally
        {
            Shader.SetGlobalFloat("_NativePortAlpha761",5);helper.DisarmImmediate();nested=false;
            for(int i=owned.Count-1;i>=first;i--){if(owned[i])UnityEngine.Object.DestroyImmediate(owned[i]);owned.RemoveAt(i);}
            helper.BindHead(head);globalState761=false;
        }
    }
    private static void NativeCase761(string name)
    {
        int first=owned.Count;
        try
        {
            helper.DisarmImmediate();Life(helper.BindHead(head),"fresh native head binding");
            Life(helper.RefreshCold(),"native case cold inventory before arrival");SceneMaterialObservers.Bind(helper);
            var color=new Color(.2f,.3f,.4f,.5f);
            string family=name=="additive-room"?"GUI/AbilityCard_Add_Shd":name=="smoke-over-wall"?"VFX/GUI_Smoke_Shd":"GUI/AbilityCard_Shd";
            if(name=="opaque-wall"||name=="smoke-over-wall")
            {
                var wall=Keep(new Material(Shader.Find("Unlit/Color")));wall.color=new Color(.4f,.1f,.2f,1);var wallMesh=Surface(wall,.5f);wallMesh.transform.rotation=Quaternion.Euler(0,180,0);
                Color[] wallOnly=Render(head,false);Check(wallOnly[128].a==1&&wallOnly[128].r>.3f,"opaque native wall is actually rendered before transparent overlay");
            }
            if(name=="late-opaque-ui")
            {
                var body=Keep(new Material(Shader.Find("UI/Default")));body.color=new Color(.4f,.1f,.2f,1);
                var texture=Keep(new Texture2D(1,1,TextureFormat.RGBAFloat,false,true));texture.SetPixel(0,0,Color.white);texture.Apply();body.mainTexture=texture;
                var background=NativeGraphic761(body,.2f);background.canvas.sortingOrder=0;
            }
            Material source=NativeMaterial761(family,color);RawImage graphic=null;Renderer mesh=null;
            if(name=="smoke-over-wall")
            {
                var prefab=Surface(source).gameObject;prefab.SetActive(false);
                var spawned=Keep(ObjectPool.Spawn(prefab,null,Vector3.zero,Quaternion.identity,false));mesh=spawned.GetComponent<Renderer>();
            }
            else
            {
                graphic=NativeGraphic761(source,0);
                traceGraphic761=graphic;
                if(name=="two-translucent")NativeGraphic761(source,-.1f);
                if(name=="alpha-clipped")
                {
                    var t=Keep(new Texture2D(2,1,TextureFormat.RGBAFloat,false,true));t.SetPixel(0,0,new Color(1,1,1,0));t.SetPixel(1,0,Color.white);t.filterMode=FilterMode.Point;t.wrapMode=TextureWrapMode.Clamp;t.Apply();source.mainTexture=t;graphic.texture=t;source.EnableKeyword("UNITY_UI_ALPHACLIP");
                }
                if(name=="stencil-clipped")
                {
                    // No preceding stencil writer: reference7 must reject all
                    // source/derivative pixels without altering the buffer.
                    source.SetInt("_Stencil",7);source.SetInt("_StencilComp",3);
                }
            }
            Canvas.ForceUpdateCanvases();
            Color[] baseline=Render(head,false);
            nested=true;nestedRenderer=mesh;nestedSource=source;
            Color[] candidate=Render(head,true);Color[] foreignPixels=foreign.GetComponent<TransparentMaterialRead>().pixels;
            int compared=0;
            for(int i=0;i<candidate.Length;i++)
            {
                Check(Bits(baseline[i].r)==Bits(candidate[i].r)&&Bits(baseline[i].g)==Bits(candidate[i].g)&&Bits(baseline[i].b)==Bits(candidate[i].b),"native-state RGB identity "+name+"/"+i);
                Check(foreignPixels[i]==baseline[i],"native-state foreign camera exact RGBA "+name+"/"+i+" original="+baseline[i]+" foreign="+foreignPixels[i]+" head="+candidate[i]);
                float expected=name=="opaque-wall"||name=="late-opaque-ui"||name=="smoke-over-wall"?1:name=="two-translucent"?.75f:name=="additive-room"||name=="stencil-clipped"?0:.5f;
                if(name=="alpha-clipped"&&i%16<8)expected=0;
                Check(Mathf.Abs(candidate[i].a-expected)<.00002f,"native-state correct coverage "+name+"/"+i+": "+candidate[i].a+" expected "+expected);compared++;
            }
            Check(compared==256,"complete native-state source extent compared");
            if(graphic!=null)
            {
                Check(graphic.material==source&&graphic.canvasRenderer.GetMaterial()!=source,"native public material remains original while private native shader binding stays batch-stable");
                if(name=="room")
                {
                    source.color=new Color(.1f,.2f,.3f,.3f);source.SetFloat("_GreyOut",.73f);graphic.SetMaterialDirty();Canvas.ForceUpdateCanvases();
                    Color[] updated=Render(head,true);Check(Mathf.Abs(updated[128].a-.3f)<.00002f,"native material property change reaches next draw without cold refresh");
                    helper.EnterHead(head);Material borrowed=graphic.canvasRenderer.GetMaterial();Check(borrowed!=source&&Mathf.Abs(borrowed.GetFloat("_GreyOut")-.73f)<.00001f,"live authored effect state copied to derivative");
                    var replacement=NativeMaterial761(family,Color.white);graphic.canvasRenderer.SetMaterial(replacement,0);helper.ExitHead(head);
                    Check(graphic.canvasRenderer.GetMaterial()==replacement,"foreign canvas slot write survives restoration");
                    graphic.SetMaterialDirty();Canvas.ForceUpdateCanvases();helper.EnterHead(head);helper.DisarmImmediate();Check(graphic.canvasRenderer.GetMaterial()==source,"disarm restores exact native canvas binding mid-head");
                }
            }
            result.rows.Add(new Row{name="native761-"+name,layers=name=="two-translucent"?2:1,originalAlpha=baseline[128].a,coverageAlpha=candidate[128].a});result.rowsCompleted++;
        }
        finally
        {
            SceneMaterialObservers.Unbind(helper);helper.DisarmImmediate();nested=false;nestedRenderer=null;nestedSource=null;
            for(int i=owned.Count-1;i>=first;i--){if(owned[i])UnityEngine.Object.DestroyImmediate(owned[i]);owned.RemoveAt(i);}
        }
    }
    private static void NativeArrivalControls761()
    {
        int first=owned.Count;
        try
        {
            helper.BindHead(head);helper.RefreshCold();SceneMaterialObservers.Unbind(helper);
            var irrelevant=Surface(Keep(new Material(Shader.Find("Unlit/Color"))));irrelevant.gameObject.SetActive(false);
            int beforeUnknown=helper.RendererCount;SceneMaterialObservers.Bind(helper);SceneMaterialObservers.ObjectArrived(irrelevant.gameObject);
            Check(helper.RendererCount==beforeUnknown,"unknown pooled renderer never enters per-eye material loop");
            irrelevant.sharedMaterial=NativeMaterial761("GUI/AbilityCard_Shd",Color.clear);
            helper.RefreshCold();Check(helper.RendererCount==beforeUnknown+1,"cold refresh discovers newly audited shader on prior unknown pooled renderer");
            UnityEngine.Object.DestroyImmediate(irrelevant.gameObject);helper.RefreshCold();SceneMaterialObservers.Unbind(helper);
            var material=NativeMaterial761("GUI/AbilityCard_Shd",new Color(.2f,.3f,.4f,.5f));var graphic=NativeGraphic761(material,0);Canvas.ForceUpdateCanvases();
            Color[] missed=Render(head,true);Check(Mathf.Abs(missed[128].a-.25f)<.00002f,"source-disabled observer control reproduces alpha-square UI leak before first refresh");
            SceneMaterialObservers.Bind(helper);GraphicRegistry.RegisterGraphicForCanvas(graphic.canvas,graphic);
            Color[] seen=Render(head,true);Check(Mathf.Abs(seen[128].a-.5f)<.00002f,"actual GraphicRegistry postfix admits first UI draw immediately");
            graphic.canvasRenderer.Clear();Check(graphic.canvasRenderer.materialCount==0,"controlled fresh native graphic has no material at HeadBegin");
            helper.EnterHead(head);Check(graphic.canvasRenderer.materialCount==0,"head begin cannot borrow a not-yet-created native binding");
            graphic.SetAllDirty();graphic.Rebuild(CanvasUpdate.PreRender);
            Check(graphic.material==material&&graphic.canvasRenderer.GetMaterial()!=material,"actual native Graphic.UpdateMaterial is borrowed before its first draw inside head");
            Check(Mathf.Abs(Render(head,false)[128].a-.5f)<.00002f,"late native material rebuild has correct first head pixels");
            helper.ExitHead(head);Check(graphic.material==material&&graphic.canvasRenderer.GetMaterial()!=material,"late native material keeps batch-stable private binding with original public material");
            helper.EnterHead(head);helper.EnterHead(foreign);graphic.canvasRenderer.Clear();graphic.SetAllDirty();graphic.Rebuild(CanvasUpdate.PreRender);
            Check(graphic.material==material&&graphic.canvasRenderer.GetMaterial()!=material,"foreign rebuild keeps private binding under original native alpha mode");
            Check(Mathf.Abs(Render(foreign,false)[128].a-.25f)<.00002f,"foreign rebuild produces exact original alpha despite private binding");
            helper.ExitHead(foreign);Check(graphic.canvasRenderer.GetMaterial()!=material,"returning exact head resumes new native binding derivative");helper.ExitHead(head);
            graphic.enabled=false;helper.EnterHead(head);helper.ExitHead(head);graphic.enabled=true;Canvas.ForceUpdateCanvases();
            Check(Mathf.Abs(Render(head,true)[128].a-.5f)<.00002f,"pooled graphic disable/re-enable keeps first visible draw correct");
            UnityEngine.Object.DestroyImmediate(graphic.gameObject);SceneMaterialObservers.GraphicArrived(graphic);
            Check(graphic==null,"destroyed graphic enrollment is inert");
            bool nativeException=false;try{ObjectPool.Spawn(null,null,Vector3.zero,Quaternion.identity,false);}catch(InvalidOperationException error){nativeException=error.Message=="original pool exception";}
            Check(nativeException,"observer preserves the original pool exception");
            SceneMaterialObservers.Unbind(helper);var renderer=Surface(material);renderer.gameObject.SetActive(false);
            var spawned=Keep(ObjectPool.Spawn(renderer.gameObject,null,Vector3.zero,Quaternion.identity,false));
            Color[] old=Render(head,true);Check(Mathf.Abs(old[128].a-.25f)<.00002f,"pool-disabled observer control reproduces missing first renderer admission");
            SceneMaterialObservers.Bind(helper);SceneMaterialObservers.ObjectArrived(spawned);
            Check(Mathf.Abs(Render(head,true)[128].a-.5f)<.00002f,"pool subtree arrival admits the next draw without a timer");
            UnityEngine.Object.DestroyImmediate(spawned);SceneMaterialObservers.ObjectArrived(spawned);Check(spawned==null,"destroyed native spawn enrollment is inert");
        }
        finally
        {
            SceneMaterialObservers.Unbind(helper);helper.DisarmImmediate();
            for(int i=owned.Count-1;i>=first;i--){if(owned[i])UnityEngine.Object.DestroyImmediate(owned[i]);owned.RemoveAt(i);}
            helper.BindHead(head);
        }
    }
    private static void NativeGlobalLifetime761()
    {
        int first=owned.Count;
        try
        {
            helper.DisarmImmediate();
            Shader.SetGlobalFloat(SceneTransparentMaterials.NativeStraightAlpha,7);
            Shader.SetGlobalFloat(SceneTransparentMaterials.NativeAdditiveAlpha,8);
            helper.BindHead(head);helper.RefreshCold();SceneMaterialObservers.Bind(helper);
            var material=NativeMaterial761("GUI/AbilityCard_Shd",new Color(.2f,.3f,.4f,.5f));
            var graphic=NativeGraphic761(material,0);Canvas.ForceUpdateCanvases();
            Color[] original=Render(foreign,false);
            Check(Mathf.Abs(original[128].a-.25f)<.00002f,"armed idle foreign target preserves original native recurrence");
            helper.EnterHead(foreign);
            Check(Shader.GetGlobalFloat(SceneTransparentMaterials.NativeStraightAlpha)==5,"foreign-first scope native alpha");
            Color[] candidate=Render(head,true);
            Check(Mathf.Abs(candidate[128].a-.5f)<.00002f,"head nested inside foreign uses coverage alpha");
            Check(Shader.GetGlobalFloat(SceneTransparentMaterials.NativeStraightAlpha)==5,"nested head returns exact foreign alpha state");
            helper.ExitHead(foreign);
            helper.EnterHead(head);helper.EnterHead(foreign);
            Check(Shader.GetGlobalFloat(SceneTransparentMaterials.NativeStraightAlpha)==5,"foreign nested in head uses native state");
            helper.ExitHead(foreign);
            Check(Shader.GetGlobalFloat(SceneTransparentMaterials.NativeStraightAlpha)==1,"nested foreign returns exact head alpha state");
            helper.ExitHead(head);
            material.color=new Color(.4f,.2f,.1f,.3f);
            Check(Mathf.Abs(Render(head,true)[128].a-.3f)<.00002f,"native effect property changed between draws copies without rebuild or cold scan");
            material.shader=Resources.Load<Shader>("NativePorts761/VFX-GUI_Smoke_Shd-original");
            graphic.SetMaterialDirty();Canvas.ForceUpdateCanvases();
            Check(Mathf.Abs(Render(head,true)[128].a-.3f)<.00002f,"native source shader change retires old clone without stale binding");
            helper.DisarmImmediate();
            Check(graphic.canvasRenderer.GetMaterial()==material,"OFF immediately restores current source after shader change");
            Check(Shader.GetGlobalFloat(SceneTransparentMaterials.NativeStraightAlpha)==7&&Shader.GetGlobalFloat(SceneTransparentMaterials.NativeAdditiveAlpha)==8,"OFF restores exact preexisting global values");
            Check(Mathf.Abs(Render(head,false)[128].a-.09f)<.00002f,"first native OFF render uses original alpha and source binding without forced Canvas update");
            helper.BindHead(head);helper.RefreshCold();SceneMaterialObservers.Bind(helper);
            Check(Mathf.Abs(Render(head,true)[128].a-.3f)<.00002f,"first ON render restores coverage with no timer");
            // A failing passive observer cannot escape the original native API.
            var bad=new SceneTransparentMaterials(_=>throw new InvalidOperationException("controlled variant failure"));bad.BindHead(head);
            SceneMaterialObservers.Bind(bad);
            var second=NativeGraphic761(NativeMaterial761("GUI/AbilityCard_Shd",Color.white),-.2f);
            second.SetMaterialDirty();Canvas.ForceUpdateCanvases();
            Check(second.material!=null,"observer exception remains isolated from native Graphic.UpdateMaterial");
            SceneMaterialObservers.Unbind(bad);bad.Dispose();
            RawImage destroyedDuring=null;
            var disappearing=new SceneTransparentMaterials(name=>{UnityEngine.Object.DestroyImmediate(destroyedDuring.gameObject);return Variant(name);});disappearing.BindHead(head);
            destroyedDuring=NativeGraphic761(NativeMaterial761("GUI/AbilityCard_Shd",Color.white),-.3f);Canvas.ForceUpdateCanvases();
            SceneMaterialObservers.Bind(disappearing);
            GraphicRegistry.RegisterGraphicForCanvas(destroyedDuring.canvas,destroyedDuring);
            Check(destroyedDuring==null,"native GraphicRegistry return survives an object destroyed during passive enrollment");
            SceneMaterialObservers.Unbind(disappearing);disappearing.Dispose();SceneMaterialObservers.Bind(helper);
        }
        finally
        {
            SceneMaterialObservers.Unbind(helper);helper.DisarmImmediate();
            for(int i=owned.Count-1;i>=first;i--){if(owned[i])UnityEngine.Object.DestroyImmediate(owned[i]);owned.RemoveAt(i);}
            Shader.SetGlobalFloat(SceneTransparentMaterials.NativeStraightAlpha,0);Shader.SetGlobalFloat(SceneTransparentMaterials.NativeAdditiveAlpha,0);
            helper.BindHead(head);
        }
    }

    private static void NativeGeneration761()
    {
        int first=owned.Count;
        try
        {
            helper.BindHead(head);helper.RefreshCold();SceneMaterialObservers.Bind(helper);
            var source=NativeMaterial761("GUI/AbilityCard_Shd",new Color(.2f,.3f,.4f,.3f));
            var graphic=NativeGraphic761(source,0);Canvas.ForceUpdateCanvases();
            Material stable=graphic.canvasRenderer.GetMaterial();Check(stable!=source,"generation fixture begins with actual private native UI binding");
            SceneMaterialObservers.Unbind(helper);
            var referenceMaterial=Keep(new Material(source));var reference=NativeGraphic761(referenceMaterial,0);
            reference.canvas.gameObject.layer=29;reference.gameObject.layer=29;Canvas.ForceUpdateCanvases();
            var referenceCamera=CameraFor("Unborrowed original reference");referenceCamera.cullingMask=1<<29;
            foreach(bool foreignFirst in new[]{false,true})
            foreach(string family in new[]{"VFX/GUI_Smoke_Shd","UI/Default","GUI/AbilityCard_Shd"})
            {
                Shader shader=family=="UI/Default"?Shader.Find(family):Resources.Load<Shader>("NativePorts761/"+family.Replace('/','-')+"-original");
                source.shader=shader;source.color=new Color(.4f,.2f,.1f,family=="UI/Default"?.4f:.7f);
                referenceMaterial.shader=shader;referenceMaterial.CopyPropertiesFromMaterial(source);
                // Deliberately NO SetMaterialDirty, ForceUpdateCanvases, native
                // rebuild, observer notification or cold scan between writes
                // and the first head draw.
                Color[] other=foreignFirst?Render(foreign,true):null;
                Color[] candidate=Render(head,true);Color[] native=Render(referenceCamera,true);
                if(!foreignFirst)other=Render(foreign,true);
                Check(graphic.canvasRenderer.GetMaterial()==stable,"shader generation preserves batched private Material identity");
                Check(reference.canvasRenderer.GetMaterial()==referenceMaterial,"independent native reference was never enrolled or borrowed");
                for(int i=0;i<256;i++)
                {
                    float expected=family=="UI/Default"?native[i].a:source.color.a;
                    Check(Mathf.Abs(candidate[i].a-expected)<.00002f,"first shader-generation head alpha without native rebuild: "+family+" actual="+candidate[i]+" native="+native[i]);
                    Check(Bits(candidate[i].r)==Bits(native[i].r)&&Bits(candidate[i].g)==Bits(native[i].g)&&Bits(candidate[i].b)==Bits(native[i].b),"first shader-generation head RGB equals unborrowed native: "+family);
                    Check(other[i]==native[i],"shader generation foreign RGBA equals unborrowed native: "+family);
                }
                Check(stable.shader.name==family+(family=="UI/Default"?"":SceneTransparentMaterials.Suffix),"new known shader derivative or exact unknown shader matches current source");
            }
            helper.DisarmImmediate();
            Check(graphic.canvasRenderer.GetMaterial()==source,"generation disarm restores latest exact native source binding");
            Check(Render(head,false)[128]==Render(referenceCamera,false)[128],"generation first OFF RGBA matches independent native reference");
        }
        finally
        {
            SceneMaterialObservers.Unbind(helper);helper.DisarmImmediate();
            for(int i=owned.Count-1;i>=first;i--){if(owned[i])UnityEngine.Object.DestroyImmediate(owned[i]);owned.RemoveAt(i);}
            helper.BindHead(head);
        }
    }

}
