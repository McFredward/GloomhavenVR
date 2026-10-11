using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI;
using Object=UnityEngine.Object;

public static partial class MirrorProgram
{
    private static void RestoreCounterLayout634(Transform[] transforms)
    {
        using var stream=File.OpenRead(Path.Combine(Application.dataPath,"NativeFirstPicture632/native-layout.bin"));
        using var data=new BinaryReader(stream);
        for(int count=data.ReadUInt16();count>0;count--)
        {
            var node=transforms[data.ReadUInt16()];int kind=data.ReadByte();bool enabled=data.ReadBoolean();
            if(kind==1||kind==2)
            {
                HorizontalOrVerticalLayoutGroup layout=kind==1?node.gameObject.AddComponent<HorizontalLayoutGroupExtended>():node.gameObject.AddComponent<HorizontalLayoutGroup>();
                layout.padding=new RectOffset(data.ReadInt32(),data.ReadInt32(),data.ReadInt32(),data.ReadInt32());
                layout.childAlignment=(TextAnchor)data.ReadInt32();layout.spacing=data.ReadSingle();
                layout.childForceExpandWidth=data.ReadBoolean();layout.childForceExpandHeight=data.ReadBoolean();
                layout.childControlWidth=data.ReadBoolean();layout.childControlHeight=data.ReadBoolean();
                layout.childScaleWidth=data.ReadBoolean();layout.childScaleHeight=data.ReadBoolean();layout.reverseArrangement=data.ReadBoolean();
                if(kind==1)
                    foreach(string field in new[]{"m_SubtractMarginHorizontal","m_SubtractMarginVertical","m_InvertOrder","m_OrderByPriority"})
                        typeof(HorizontalOrVerticalLayoutGroupExtended).GetField(field,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(layout,data.ReadBoolean());
                layout.enabled=enabled;
            }
            else if(kind==3)
            {
                var layout=node.gameObject.AddComponent<LayoutElement>();layout.ignoreLayout=data.ReadBoolean();
                layout.minWidth=data.ReadSingle();layout.minHeight=data.ReadSingle();layout.preferredWidth=data.ReadSingle();layout.preferredHeight=data.ReadSingle();
                layout.flexibleWidth=data.ReadSingle();layout.flexibleHeight=data.ReadSingle();layout.layoutPriority=data.ReadInt32();layout.enabled=enabled;
            }
            else
            {
                var text=node.GetComponent<TMP_Text>();text.enableAutoSizing=data.ReadBoolean();text.enableWordWrapping=data.ReadBoolean();
                text.overflowMode=(TextOverflowModes)data.ReadInt32();text.fontSizeMin=data.ReadSingle();text.fontSizeMax=data.ReadSingle();
            }
        }
        Check(stream.Position==stream.Length,"actual serialized native counter layout grammar is consumed exactly");
    }
    private static IEnumerator MageCounter634()
    {
        TownServiceMirror.Shutdown();Baselines.Clear();GloomhavenVR.Net.NetPlayerActors.Peer=10;
        Transform owner=Go("Original mage book workspace").transform,observer=Go("Observer workspace").transform;
        observer.SetPositionAndRotation(Vector3.right*3f,Quaternion.Euler(0,19,0));
        ((RectTransform)owner).sizeDelta=new Vector2(388f,600f);
        owner.gameObject.AddComponent<Canvas>().renderMode=RenderMode.WorldSpace;
        Transform original=NativeRow632(owner,"");
        Image backing=original.GetComponent<Image>();var icon=original.Find("Image").GetComponentsInChildren<Image>(true);
        TMP_Text number=original.Find("Text").GetComponent<TMP_Text>();TMP_Text label=original.Find("Info").GetComponent<TMP_Text>();
        Check(original.name=="Info"&&backing.enabled&&backing.sprite==null&&backing.color==Color.white,
            "serialized actual header reproduces the quiet null-sprite white backing");
        Check(icon.Length==2&&icon.All(image=>image.sprite!=null&&image.enabled),"actual native counter retains both original icon layers");
        Canvas.ForceUpdateCanvases();
        Check(original.GetComponent<HorizontalLayoutGroupExtended>()!=null,"original extended header layout is active before conversion");
        using(var surface=new NativeSurface634(13,(RectTransform)original,Vector3.zero,.28f,owner,Quaternion.identity,.065f))
        {
            surface.Tick(Vector3.zero,Quaternion.identity,1f);
            Check(!backing.enabled&&icon.All(image=>image.enabled)&&number.enabled&&label.enabled,
                "original native points heading suppresses only its opaque full-cover backing");
            Check(surface.Panel.HiddenBackgrounds.Count==1&&ReferenceEquals(surface.Panel.HiddenBackgrounds[0],backing),
                "only the exact native header backing enters reversible conversion ownership");
            _camera.orthographic=true;
            foreach(bool german in new[]{false,true})
            {
                label.text=german?"Verzauberungspunkte":"Enhancement points";number.text="5";
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)original);Canvas.ForceUpdateCanvases();
                number.ForceMeshUpdate();label.ForceMeshUpdate();
                Check(label.textInfo.lineCount==1,"actual original layout keeps the localized counter label on one line");
                TownServiceMirror.Shutdown();Baselines.Clear();
                Transform localDefault=Object.Instantiate(original.gameObject,observer,false).transform;
                localDefault.GetComponent<Image>().enabled=true;
                localDefault.Find("Text").GetComponent<TMP_Text>().text="Wrong observer value";
                TownServiceMirror.RegisterTemplate(3,1,localDefault,address:"enchant.capacity|");
                TownServiceMirror.BeginSession(3,(uint)(german?6342:6341),owner,owner);
                TownServiceMirror.RegisterModule(1,1,original,address:"enchant.capacity|");
                var captured=CaptureFast();var pool=new TownServiceCodec.OriginalValuePoolBuilder();
                foreach(var bytes in captured.Artwork)Check(pool.TryAdd(bytes),"real native counter and census enter the atomic original-value bundle");
                byte[] atomic=pool.Write();
                Check(TownServiceCodec.TryReadBundle(atomic,atomic.Length,out byte[][]? members),"actual TLV110 counter picture expands without defaults");
                // The real owner/observer are different processes. Park only this
                // fixture sender's private lease during its observer playback.
                CounterSender634(false);TownServiceMirror.SharedFrameForRemote=_=>observer;
                Receive(2,members!);var settle=FastSettle(observer,.18f);while(settle.MoveNext())yield return settle.Current;
                var remote=Remote(2,1)!;
                Check(remote!=null&&!remote.Root.GetComponent<Image>().enabled,"observer suppresses the exact owner-disabled background"
                    +" (ready="+(remote!=null)+", enabled="+(remote!=null?remote.Root.GetComponent<Image>().enabled:false)+")");
                Check(remote.Root.Find("Text").GetComponent<TMP_Text>().text=="5"&&remote.Root.Find("Info").GetComponent<TMP_Text>().text==label.text,
                    "observer retains original point value and owner-localized native label");
                CheckCounterGeometry634(original,remote.Root,owner,observer);
                CompareCounter634(original,remote.Root,german?"counter-de":"counter-en");
                for(float until=Time.unscaledTime+.1f;Time.unscaledTime<until;)yield return null;
                number.text="4";LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)original);Canvas.ForceUpdateCanvases();CounterSender634(true);var update=CaptureFast();CounterSender634(false);Receive(2,update.Artwork);
                settle=FastSettle(observer,.18f);while(settle.MoveNext())yield return settle.Current;
                Check(remote.Root.Find("Text").GetComponent<TMP_Text>().text=="4"&&!backing.enabled&&!remote.Root.GetComponent<Image>().enabled,
                    "native points updates retain transparent source and observer headings");
                CheckCounterGeometry634(original,remote.Root,owner,observer);
                CompareCounter634(original,remote.Root,german?"updated-de":"updated-en");
            }
        }
        Check(backing.enabled,"native header backing restores when immersive folio is released");
        using(var other=new NativeSurface634(14,(RectTransform)original,Vector3.zero,.28f,owner,Quaternion.identity,.065f))
            Check(backing.enabled,"unrelated native folio sections retain their original background");
    }
    private static void CheckCounterGeometry634(Transform source,Transform target,Transform owner,Transform observer)
    {
        Check(Vector3.Distance(target.position,observer.TransformPoint(owner.InverseTransformPoint(source.position)))<.00002f,
            "counter world position is owner-authored before render recentering");
        Check(Quaternion.Angle(target.rotation,observer.rotation*Quaternion.Inverse(owner.rotation)*source.rotation)<.02f,
            "counter world orientation is owner-authored before render recentering");
        var from=new Vector3[4];var to=new Vector3[4];
        foreach(var ink in source.GetComponentsInChildren<Graphic>(true).Where(x=>x.enabled&&x.gameObject.activeInHierarchy))
        {
            var address=new Stack<int>();
            for(Transform node=ink.transform;node!=source;node=node.parent)address.Push(node.GetSiblingIndex());
            Transform observed=target;while(address.Count>0)observed=observed.GetChild(address.Pop());
            Graphic copy=observed.GetComponent<Graphic>();
            Check(copy!=null&&copy.GetType()==ink.GetType(),"counter geometry uses the same native child address and graphic type");
            ink.rectTransform.GetWorldCorners(from);copy.rectTransform.GetWorldCorners(to);
            for(int corner=0;corner<4;corner++)
                Check(Vector3.Distance(to[corner],observer.TransformPoint(owner.InverseTransformPoint(from[corner])))<.000025f,
                    "native original counter ink world corners retain observer geometry");
        }
    }
    private static void CounterSender634(bool active)
    {
        object sender=typeof(TownServiceMirror).GetField("PrivateLane",PrivateStatic)!.GetValue(null)!;
        sender.GetType().GetField("Active",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(sender,active);
    }
    private static Color32[] RenderCounter634(Transform root,int layer,string name)
    {
        foreach(GameObject item in Objects)if(item!=null)Layer(item.transform,30);
        Layer(root,layer);foreach(Canvas canvas in root.GetComponentsInParent<Canvas>(true))canvas.gameObject.layer=layer;
        foreach(Canvas canvas in root.GetComponentsInChildren<Canvas>(true))canvas.worldCamera=_camera;
        _camera.cullingMask=1<<layer;_camera.transform.SetPositionAndRotation(root.position-root.forward*2f,root.rotation);
        _camera.orthographicSize=.07f;_camera.backgroundColor=new Color(.025f,.03f,.04f,1f);
        Canvas.ForceUpdateCanvases();
        var rt=new RenderTexture(1024,256,24,RenderTextureFormat.ARGB32);var picture=new Texture2D(1024,256,TextureFormat.RGBA32,false);
        try{_camera.targetTexture=rt;_camera.Render();RenderTexture.active=rt;picture.ReadPixels(new Rect(0,0,1024,256),0,0);picture.Apply();
            File.WriteAllBytes(Path.Combine(_output,name+".png"),picture.EncodeToPNG());return picture.GetPixels32();}
        finally{RenderTexture.active=null;_camera.targetTexture=null;Object.DestroyImmediate(rt);Object.DestroyImmediate(picture);}
    }
    private static void CompareCounter634(Transform owner,Transform observer,string name)
    {
        var a=RenderCounter634(owner,8,name+"-owner");var b=RenderCounter634(observer,9,name+"-observer");
        int ink=a.Count(p=>p.r>60||p.g>60||p.b>60);int differences=0;
        for(int i=0;i<a.Length;i++)if(Math.Abs(a[i].r-b[i].r)>2||Math.Abs(a[i].g-b[i].g)>2||Math.Abs(a[i].b-b[i].b)>2)differences++;
        Check(ink>250&&ink<12000,"native floating counter renders readable original ink without an opaque strip");
        Check(differences<15,"native counter owner and inert observer retain rendered text and original icon parity");
        File.AppendAllText(Path.Combine(_output,"render-counts.txt"),name+": ink="+ink+" differing="+differences+"\n");
    }
}
