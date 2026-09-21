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
        Transform? coin=TownServiceDecor.CoinTemplate;
        Check(coin!=null&&!coin.gameObject.activeSelf,"native coin template is inert and hidden");
        Check(coin!.GetComponentsInChildren<MeshRenderer>(true).Length==1,"native coin rendering retained");
        Check(Math.Abs(coin.localScale.x-.05f)<.0001f,"coin normalized to five centimetres");
        Check(GloomhavenVR.Net.TownServices.TownServiceMirror.Assets.Items.Count>0,"coin texture explicit original provenance registered");
        GloomhavenVR.Net.TownServices.TownServiceMirror.Assets.Items.Clear();
        GloomhavenVR.Net.TownServices.TownServiceMirror.Assets.Generation++;
        Tick(decor,.22f);
        Check(GloomhavenVR.Net.TownServices.TownServiceMirror.Assets.Items.Count>0,"network asset reset rebinds living native coin textures");
        decor.Dispose();Check(TownServiceDecor.CoinTemplate==null&&Addressables.Held==0,"coin template cannot outlive owner materials");UnityEngine.Object.DestroyImmediate(root);
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
