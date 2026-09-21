using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.AddressableAssets;
using GloomhavenVR.WorldUI;
public static class InteractionProgram
{
    private static int _checks;
    private static void Check(bool pass,string text){_checks++;if(!pass)throw new Exception(text);}
    private static void Tick(TownServiceDecor decor,float time){DecorClock.Now=time;decor.Tick();}
    private static GameObject Source(string name,string material)
    {
        var root=new GameObject(name);
        var child=GameObject.CreatePrimitive(PrimitiveType.Cube);child.name=name;child.transform.SetParent(root.transform,false);
        if(name=="lantern")child.name="CR_INT_Lantern_01_b";
        if(name=="flame") {child.name="CandlePivot";var glow=GameObject.CreatePrimitive(PrimitiveType.Quad);glow.name="Glow";glow.transform.SetParent(child.transform,false);glow.GetComponent<MeshRenderer>().sharedMaterial=(Material)Addressables.Assets["good"];}
        var renderer=child.GetComponent<MeshRenderer>();renderer.sharedMaterials=Array.Empty<Material>();
        root.AddComponent<MaterialLoader>().LoadersData=new[]{new MaterialLoaderData{Renderer=renderer,MaterialReferences=new[]{new AssetReferenceT<Material>{RuntimeKey=material}}}};
        return root;
    }
    private static void List(string list,params (string entry,string name,string material)[] entries)
        =>Addressables.Assets["Assets/PCG/PCG_"+list+".asset"]=new ApparanceResourceList{Objects=entries.Select(x=>new ApparanceObjectResource{Name=x.entry,Object=Source(x.name,x.material)}).ToArray()};
    private static void Setup(int failures)
    {
        Addressables.Assets.Clear();Addressables.Failures.Clear();Addressables.Requests.Clear();Addressables.Pending.Clear();
        DecorClock.Now=0;GloomhavenVR.Core.VRLog.Warnings.Clear();
        var material=new Material(Shader.Find("Unlit/Texture"));material.mainTexture=Texture2D.whiteTexture;
        Addressables.Assets["good"]=material;Addressables.Assets["bad"]=material;Addressables.Failures["bad"]=failures;
        List("Gaslight",("Gaslight.Lighting.Torch.Wall#1","lantern","bad"));
        List("Tone_Candlelight",("Candlelight.Lighting.Torch.Wall#1","flame","good"));
        List("Library",("Library.Clutter.Shelf.Individual#7","book","good"));
        List("Treasure",("Treasure.Clutter.FloorSmall#3","coins","good"),("Treasure.Clutter.Shelf.Individual#1","coin","good"));
        List("AlchemyLab",("AlchemyLab.Clutter.Shelf.Individual#7","balance","good"),("AlchemyLab.Clutter.Shelf.Individual#3","jugs","good"),("AlchemyLab.Clutter.Shelf.Individual#11","oiler","good"));
        List("Chapel",("Chapel.Clutter.Shelf.Individual#7","bowl","good"),("Chapel.Clutter.Shelf.Individual#2","scrolls","good"));
    }
    public static int Run()
    {
        Setup(1);var root=new GameObject("station");var light=new TownServiceLighting();var decor=new TownServiceDecor(root.transform,1,light);
        Tick(decor,0);Tick(decor,.11f);
        Check(root.transform.Find("Original.Library.Clutter.Shelf.Individual#7")!=null,"unrelated book builds while lantern material fails");
        Check(light.Flames.Count==0,"failed lamp cannot light unsupported source");
        Tick(decor,1.2f);Tick(decor,1.31f);
        Check(light.Flames.Count==2&&decor.Ready,"independent material retry restores both practicals");
        Check(Addressables.Requests["bad"]==2,"shared dependency requested once per attempt");
        Check(!root.GetComponentsInChildren<MaterialLoader>(true).Any(),"native controllers never copied");
        decor.Dispose();Check(Addressables.Held==0,"successful material/list handles released exactly once");
        UnityEngine.Object.DestroyImmediate(root);
        Setup(10);root=new GameObject("terminal");decor=new TownServiceDecor(root.transform,1,light);
        foreach(float time in new[]{0f,.11f,1.2f,1.31f,5.4f,5.51f,100f,200f})Tick(decor,time);
        Check(Addressables.Requests["bad"]==3,"terminal material failure bounded to three attempts");
        Check(decor.Ready,"terminal prop failure cannot gate service flow");
        Check(GloomhavenVR.Core.VRLog.Warnings.Count<=3,"bounded contextual warnings");decor.Dispose();Check(Addressables.Held==0,"failed attempts all released");UnityEngine.Object.DestroyImmediate(root);
        Setup(0);Addressables.Pending.Add("bad");root=new GameObject("timeout");decor=new TownServiceDecor(root.transform,1,light);
        foreach(float time in new[]{0f,31f,33f,64f,69f,100f})Tick(decor,time);
        Check(Addressables.Requests["bad"]==3&&decor.Ready,"stalled material requests time out without a service deadlock");decor.Dispose();Check(Addressables.Held==0,"pending handles released on teardown");UnityEngine.Object.DestroyImmediate(root);
        Setup(0);root=new GameObject("priestess");decor=new TownServiceDecor(root.transform,2,light);Tick(decor,0);Tick(decor,.11f);
        Check(TownServiceDecor.StaticPropSource(2)==root.transform,"static prop source identifies the permanent station lifetime");
        int staticCount=0;
        for(int index=0;index<TownServiceDecor.StaticPropCount(2);index++)
            if(TownServiceDecor.TryStaticProp(2,index,out var source,out var address))
            {
                Check(source!=null&&source!=TownServiceDecor.CoinTemplate,"hidden coin template excluded from workspace props");
                Check(address=="decor.2."+index,"static prop address remains service/index stable");staticCount++;
            }
        Check(staticCount==7,"all priestess static lamps ledger bowl and scrolls exposed");
        Check(TownServiceDecor.TryStaticProp(2,0,out var lampSource,out _),"priestess static lamp ready");
        Check(TownServiceDecor.TryPractical(2,0,out var lampPoint,out var rangeScale),"original lamp has exact flame calibration");
        var lamp=UnityEngine.Object.Instantiate(lampSource!.gameObject);lamp.SetActive(false);
        int nodes=lamp.GetComponentsInChildren<Transform>(true).Length;
        TownServiceWorkspacePractical.RebindClone("decor.2.0|",lamp);
        Check(TownServiceLighting.Owned.Count==0,"inactive frozen template never creates a practical");
        lamp.SetActive(true);
        Check(TownServiceLighting.Owned.Count==1,"active owner or remote lantern receives one practical");
        TownServiceWorkspacePractical.RebindClone("decor.2.0|",lamp);
        Check(TownServiceLighting.Owned.Count==1,"repeated clone binding cannot duplicate practical");
        Check(lamp.GetComponentsInChildren<Transform>(true).Length==nodes&&lamp.GetComponentsInChildren<Light>(true).Length==0,"lighting cannot alter mirrored prop topology");
        var owned=TownServiceLighting.Owned.Single();
        Check(Vector3.Distance(owned.transform.position,lamp.transform.TransformPoint(lampPoint))<.0001f,"practical follows exact source flame point");
        var surface=lamp.GetComponentInChildren<MeshRenderer>();var replacement=new Material(surface.sharedMaterial);replacement.SetFloat("_TownVisibility",.4f);surface.sharedMaterial=replacement;
        var helper=lamp.GetComponent<TownServiceWorkspacePractical>();
        void UpdateLamp()=>typeof(TownServiceWorkspacePractical).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(helper,null);
        UpdateLamp();Check(Math.Abs(owned.intensity-.5f)<.0001f,"remote replacement material opacity reaches practical");
        var block=new MaterialPropertyBlock();block.SetFloat("_TownVisibility",.2f);surface.SetPropertyBlock(block);UpdateLamp();
        Check(Math.Abs(owned.intensity-.25f)<.0001f,"property-block dissolve reaches practical");
        lamp.transform.position+=Vector3.right;lamp.transform.localScale*=2;UpdateLamp();
        Check(Math.Abs(owned.range-2.65f*rangeScale*Math.Abs(lamp.transform.lossyScale.x))<.0001f,"practical range follows mirrored world scale");
        Check(Vector3.Distance(owned.transform.position,lamp.transform.TransformPoint(lampPoint))<.0001f,"moved workspace carries practical in same frame");
        lamp.SetActive(false);Check(TownServiceLighting.Owned.Count==0&&!owned.gameObject.activeSelf,"disable immediately releases and darkens standalone light");
        lamp.SetActive(true);Check(TownServiceLighting.Owned.Count==1,"reopening workspace restores one owned practical");
        UnityEngine.Object.DestroyImmediate(lamp);Check(TownServiceLighting.Owned.Count==0,"clone destruction releases practical ownership");
        var partition=new GameObject("partition");TownServiceWorkspacePractical.RebindClone("decor.2.0|0",partition);
        Check(partition.GetComponent<TownServiceWorkspacePractical>()==null,"partition modules cannot duplicate root lighting");UnityEngine.Object.DestroyImmediate(partition);
        Transform? coin=TownServiceDecor.CoinTemplate;
        Check(coin!=null&&!coin.gameObject.activeSelf,"native coin template is inert and hidden");
        Check(coin!.GetComponentsInChildren<MeshRenderer>(true).Length==1,"native coin rendering retained");
        Check(Math.Abs(coin.localScale.x-.05f)<.0001f,"coin normalized to five centimetres");
        Check(GloomhavenVR.Net.TownServices.TownServiceMirror.Assets.Items.Count>0,"coin texture explicit original provenance registered");
        GloomhavenVR.Net.TownServices.TownServiceMirror.Assets.Items.Clear();
        GloomhavenVR.Net.TownServices.TownServiceMirror.Assets.Generation++;
        Tick(decor,.22f);
        Check(GloomhavenVR.Net.TownServices.TownServiceMirror.Assets.Items.Count>0,"network asset reset rebinds living native coin textures");
        decor.Dispose();Check(TownServiceDecor.StaticPropSource(2)==null&&TownServiceDecor.StaticPropCount(2)==0,"disposed station invalidates static prop lookup");Check(TownServiceDecor.CoinTemplate==null&&Addressables.Held==0,"coin template cannot outlive owner materials");UnityEngine.Object.DestroyImmediate(root);
        Setup(0);root=new GameObject("enchantress");
        var grip=new GameObject("ActivityGripRight");grip.transform.SetParent(root.transform,false);grip.transform.localPosition=new Vector3(.2f,1.1f,.3f);
        decor=new TownServiceDecor(root.transform,3,light);Tick(decor,0);Tick(decor,.11f);decor.SetVisibility(.8f);decor.SetClock(100f);
        var visual=new GloomhavenVR.Net.TownActivityVisual{Cast=.4f};decor.SampleActivity(in visual);
        Transform effect=root.transform.Find("Town.ArcaneConstellation");
        Check(effect!=null&&effect.gameObject.activeSelf,"native constellation visible during authored cast");
        Check(effect!.childCount==13,"magic draw count is bounded");
        Material glow=effect.GetComponentInChildren<MeshRenderer>().sharedMaterial;
        Check(Math.Abs(glow.GetFloat("_TownVisibility")-.32f)<.0001f,"magic opacity combines station fade and cast envelope");
        Vector3 orbit=effect.GetChild(4).position;decor.SampleActivity(in visual);
        Check(effect.GetChild(4).position==orbit,"same shared clock yields identical orbit on owner and observer");
        visual.Cast=.1f;decor.SampleActivity(in visual);
        Check(Math.Abs(glow.GetFloat("_TownVisibility")-.08f)<.0001f,"interrupting magic fades continuously before shutdown");
        visual.Cast=0f;decor.SampleActivity(in visual);Check(!effect.gameObject.activeSelf,"finished cast has no orphan glow");
        decor.Dispose();Check(Addressables.Held==0,"effect teardown releases native dependency handles");UnityEngine.Object.DestroyImmediate(root);
        Shader amp=Shader.Find("Amp_TownDecorFixture");Check(amp!=null,"native shader input fixture imported");
        var original=new Material(amp);original.mainTextureScale=new Vector2(.5f,.5f);original.SetFloat("_UVTiling",1f);original.SetColor("_Tint",new Color(.8f,.7f,.6f,0));
        var adapted=TownServiceDecorMaterial.Copy(original,Shader.Find("Unlit/Texture"));
        Check(adapted.mainTextureScale==Vector2.one,"native atlas UVs do not sample stale Standard quadrant");
        Check(original.mainTextureScale==new Vector2(.5f,.5f),"original game material remains unchanged");
        var standard=new Material(Shader.Find("Unlit/Texture"));standard.mainTextureScale=new Vector2(.3f,.4f);
        Check(TownServiceDecorMaterial.Copy(standard,standard.shader).mainTextureScale==standard.mainTextureScale,"non-Amp texture transforms retained");
        return _checks;
    }
}
