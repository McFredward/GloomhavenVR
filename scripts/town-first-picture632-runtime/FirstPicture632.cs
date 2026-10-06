using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Object = UnityEngine.Object;

// JsonUtility fills DTO reference fields before fixture use.
#pragma warning disable CS8618, CS8600
public static partial class MirrorProgram
{
    [Serializable] private class NativeRows632 { public NativeNode632[] nodes; public NativeMaterial632[] materials; }
    [Serializable] private class NativeNode632 {
        public string name; public int parent; public bool active;
        public Vector3 position,scale; public Quaternion rotation;
        public Vector2 anchorMin,anchorMax,anchored,size,pivot;
        public NativeGraphic632[] graphics; public NativeGroup632[] groups;
    }
    [Serializable] private class NativeGraphic632 {
        public bool enabled; public Color color; public string material,kind,image,text;
        public float fontSize,spacing; public int alignment,fontStyle; public Vector4 margin;
    }
    [Serializable] private class NativeGroup632 { public float alpha; public bool ignoreParents; }
    [Serializable] private class NativeMaterial632 { public string key,name; public NativeFloat632[] floats;public NativeColor632[] colors; }
    [Serializable] private class NativeFloat632 { public string name; public float value; }
    [Serializable] private class NativeColor632 { public string name; public Color value; }
    private static readonly Dictionary<string,Texture2D> GameTextures632 = new();
    private static readonly Dictionary<string,Material> GameMaterials632 = new();
    private static Transform NativeRow632(Transform parent,string localized)
    {
        string folder = Path.Combine(Application.dataPath,"NativeFirstPicture632");
        var source = ReadNativeRow632(Path.Combine(folder,"native-row.bin"));
        var transforms = new Transform[source.nodes.Length];
        foreach(var material in source.materials)
        {
            if(GameMaterials632.ContainsKey(material.key))continue;
            var restored = new Material(TMP_Settings.defaultFontAsset.material){name=material.name};
            foreach(var value in material.floats)if(restored.HasProperty(value.name))restored.SetFloat(value.name,value.value);
            foreach(var value in material.colors)if(restored.HasProperty(value.name))restored.SetColor(value.name,value.value);
            GameMaterials632.Add(material.key,restored);
        }
        for(int i=0;i<source.nodes.Length;i++)
        {
            NativeNode632 node=source.nodes[i];
            var root=Go(node.name);var rect=root.GetComponent<RectTransform>();
            rect.SetParent(node.parent<0?parent:transforms[node.parent],false);transforms[i]=rect;
            rect.localPosition=node.position;rect.localRotation=node.rotation;rect.localScale=node.scale;
            rect.anchorMin=node.anchorMin;rect.anchorMax=node.anchorMax;rect.pivot=node.pivot;
            rect.anchoredPosition=node.anchored;rect.sizeDelta=node.size;
            foreach(var group in node.groups){var g=root.AddComponent<CanvasGroup>();g.alpha=group.alpha;g.ignoreParentGroups=group.ignoreParents;}
            foreach(var visual in node.graphics)
            {
                Graphic graphic;
                if(visual.kind=="tmp")
                {
                    var text=root.AddComponent<TextMeshProUGUI>();text.font=TMP_Settings.defaultFontAsset;
                    text.text=node.name=="Name"?localized:visual.text;text.fontSize=visual.fontSize;
                    text.alignment=(TextAlignmentOptions)visual.alignment;text.fontStyle=(FontStyles)visual.fontStyle;
                    text.characterSpacing=visual.spacing;text.margin=visual.margin;
                    if(visual.material.Length!=0)text.fontSharedMaterial=GameMaterials632[visual.material];
                    graphic=text;
                }
                else if(visual.kind=="raw") {var image=root.AddComponent<RawImage>();if(visual.image.Length!=0)image.texture=NativeTexture632(folder,visual.image);graphic=image;}
                else {var image=root.AddComponent<Image>();if(visual.image.Length!=0){var texture=NativeTexture632(folder,visual.image);image.sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),Vector2.one*.5f);}graphic=image;}
                graphic.enabled=visual.enabled;graphic.color=visual.color;graphic.raycastTarget=false;
            }
            root.SetActive(node.active);
        }
        transforms[0].gameObject.SetActive(true);
        return transforms[0];
    }
    private static NativeRows632 ReadNativeRow632(string path)
    {
        using var stream=File.OpenRead(path);using var r=new BinaryReader(stream);
        string Text()=>System.Text.Encoding.UTF8.GetString(r.ReadBytes(r.ReadUInt16()));
        Vector2 V2()=>new(r.ReadSingle(),r.ReadSingle());Vector3 V3()=>new(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());
        Vector4 V4()=>new(r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),r.ReadSingle());
        Color C()=>new(r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),r.ReadSingle());
        var source=new NativeRows632{materials=new NativeMaterial632[r.ReadUInt16()]};
        for(int i=0;i<source.materials.Length;i++)
        {
            var m=new NativeMaterial632{key=Text(),name=Text(),floats=new NativeFloat632[r.ReadUInt16()]};
            for(int f=0;f<m.floats.Length;f++)m.floats[f]=new NativeFloat632{name=Text(),value=r.ReadSingle()};
            m.colors=new NativeColor632[r.ReadUInt16()];for(int c=0;c<m.colors.Length;c++)m.colors[c]=new NativeColor632{name=Text(),value=C()};
            source.materials[i]=m;
        }
        source.nodes=new NativeNode632[r.ReadUInt16()];
        for(int i=0;i<source.nodes.Length;i++)
        {
            var n=new NativeNode632{name=Text(),parent=r.ReadInt16(),active=r.ReadBoolean(),position=V3(),
                rotation=new Quaternion(r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),r.ReadSingle()),scale=V3(),
                anchorMin=V2(),anchorMax=V2(),anchored=V2(),size=V2(),pivot=V2(),groups=new NativeGroup632[r.ReadByte()]};
            for(int g=0;g<n.groups.Length;g++)n.groups[g]=new NativeGroup632{alpha=r.ReadSingle(),ignoreParents=r.ReadBoolean()};
            n.graphics=new NativeGraphic632[r.ReadByte()];
            for(int g=0;g<n.graphics.Length;g++)n.graphics[g]=new NativeGraphic632{enabled=r.ReadBoolean(),color=C(),
                material=Text(),kind=Text(),image=Text(),text=Text(),fontSize=r.ReadSingle(),spacing=r.ReadSingle(),
                alignment=r.ReadInt32(),fontStyle=r.ReadInt32(),margin=V4()};
            source.nodes[i]=n;
        }
        Check(stream.Position==stream.Length,"serialized original fixture grammar is consumed exactly");return source;
    }
    private static Texture2D NativeTexture632(string folder,string name)
    {
        if(GameTextures632.TryGetValue(name,out var existing))return existing;
        var texture=new Texture2D(2,2){name=name};texture.LoadImage(File.ReadAllBytes(Path.Combine(folder,name)));
        GameTextures632.Add(name,texture);return texture;
    }

    private static IEnumerator FirstPicture632()
    {
        FirstPictureVisibleInk632();
        foreach(bool stress in new[]{false,true})
        foreach(bool german in new[]{false,true})
        {
            TownServiceMirror.Shutdown();Baselines.Clear();NetPlayerActors.Peer=10;GloomhavenVR.Core.VRLog.Messages.Clear();
            Transform owner=Go("Original full enhancement owner").transform,observer=Go("Original full enhancement observer").transform;
            var ownerCanvas=owner.gameObject.AddComponent<Canvas>();ownerCanvas.renderMode=RenderMode.WorldSpace;ownerCanvas.worldCamera=_camera;
            var sources=new List<Transform>();var addresses=new List<string>();
            // The hardware has66 prepared partitions and44 required originals.
            // Fourteen are actual serialized26-node shop rows, with material/font
            // descriptor density absent from the old six-light-widget fixture.
            for(ushort i=0;i<66;i++)
            {
                bool row=i>=7&&i<21;
                Transform source;
                if (row || i >= 2)
                {
                    Transform originalRow = NativeRow632(owner,german?"Verstärkung "+i+" — Schild Äöü":"Enhancement "+i+" — shield");
                    if (row) source = originalRow;
                    else
                    {
                        // Other hardware members are small original card/UI partitions,
                        // mostly0.3–2KiB, not another30 full shop/preview windows.
                        // Retain an actual serialized original renderer subtree here.
                        source = originalRow.GetComponentsInChildren<TMP_Text>(true).First(x=>x.name=="Name").transform;
                        source.SetParent(owner,false);Object.DestroyImmediate(originalRow.gameObject);
                    }
                }
                else
                { source=Source(owner);source.Find("Name").GetComponent<TextMeshProUGUI>().text=(german?"Originale Fähigkeit ":"Original ability ")+i; }
                string address=i==0?"face.63201|":i==1?"enchant.holder|":row?"enchant.row|"+i:"face.63201|native.part."+i;
                sources.Add(source);addresses.Add(address);
                var observerDefault=Object.Instantiate(source.gameObject,observer,false).transform;
                // Equivalent local hierarchy, deliberately different localized native
                // text/material defaults. No pre-shared observer-value assumption.
                foreach(var text in observerDefault.GetComponentsInChildren<TMP_Text>(true)) {text.text="different local native default";text.fontSize+=3;}
                TownServiceMirror.RegisterTemplate(3,(ushort)(i+1),observerDefault,address:address);
            }
            TownServiceMirror.BeginSession(3,(uint)((german?633:632)+(stress?100:0)),owner,owner);
            for(ushort i=0;i<66;i++){TownServiceMirror.RegisterModule((ushort)(i+1),(ushort)(i+1),sources[i],address:addresses[i]);TownServiceMirror.SetPriority((ushort)(i+1),true);sources[i].gameObject.SetActive(false);}
            TownServiceMirror.SetLocalTransactionActive(3,false);
            var scheduler=new ExtrasSendScheduler(0,3,4);var fragments=new TownServiceFragments();
            FillOtherQueues632(scheduler,stress);
            var snapshots=new List<TownServiceFrame>();var originals=new List<byte[]>();
            Action<byte[],int,object?> publish=(bytes,length,identity)=>{snapshots.Add((TownServiceFrame)identity!);originals.Add(bytes);scheduler.Enqueue(bytes,length,identity:identity);};
            NativeSenderCapture629(publish);double clock=0;
            var visit=NativeVisitorReady629(scheduler,fragments,observer,3,value=>clock=value);while(visit.MoveNext())yield return visit.Current;
            snapshots.Clear();originals.Clear();
            for(int i=0;i<44;i++)sources[i].gameObject.SetActive(true);
            SetNativeSenderActive629(true);TownServiceMirror.SetLocalTransactionActive(3,true);var captureWatch=System.Diagnostics.Stopwatch.StartNew();NativeSenderCapture629(publish);captureWatch.Stop();
            File.AppendAllText(Path.Combine(_output,"first-picture632-cost.txt"),"Priority: "+string.Join(",",snapshots.Select(x=>x.Module+":"+x.HighPriority))+"\n");
            var census=snapshots.Find(x=>x.Module==TownServiceFrame.ManifestModule);
            Check(census!=null&&census.RequiredVisibleModules.Length==44,"actual44-module native picture includes every owner-visible original");
            Check(snapshots.Count==45,"cold first picture excludes the22 never-painted prepared originals");
            TownServiceMirror.SharedFrameForRemote=_=>observer;double began=clock,ready=-1;int events=0,wire=0;double sendCpu=0,receiveCpu=0,applyCpu=0;
            for(int turn=0;turn<160&&ready<0;turn++)
            {
                clock+=.050001;if(!stress&&turn%2==0)FillOtherQueues632(scheduler,false);
                var timer=System.Diagnostics.Stopwatch.StartNew();byte[] batch=scheduler.NextBatch(clock);timer.Stop();sendCpu+=timer.Elapsed.TotalMilliseconds;if(batch==null){yield return null;continue;}
                events++;wire+=batch.Length;Check(batch.Length<=PresentationBatch.MaxSize,"full native picture keeps864-byte global event cap");
                Check(scheduler.NextBatch(clock)==null,"native preparation cannot emit extra budget catch-up events");
                foreach(byte[] page in PresentationBatch.TryRead(batch,batch.Length,out var pages)?pages!:new[]{batch})
                {
                    if(TownServiceFragments.Stream(page,page.Length)<0)continue;
                    byte[] packet=fragments.Accept(2,page,page.Length,clock);if(packet==null)continue;
                    byte[][] expanded=TownServiceCodec.TryReadBundle(packet,packet.Length,out var bundle)?bundle!:new[]{packet};
                    File.AppendAllText(Path.Combine(_output,"first-picture632-cost.txt"),"Assembled @"+(clock-began)+"s bytes="+packet.Length+" packed="+(PresentationCompression.TryCompress(packet,packet.Length,true)?.Length??packet.Length)+" members="+expanded.Length+"\n");
                    timer.Restart();Receive(2,expanded);timer.Stop();receiveCpu+=timer.Elapsed.TotalMilliseconds;
                }
                timer.Restart();TownServiceMirror.TickRemote(_=>observer);timer.Stop();applyCpu+=timer.Elapsed.TotalMilliseconds;
                bool complete=true;for(ushort id=1;id<=44;id++)complete&=Remote(2,id)!=null&&Remote(2,id).Root.gameObject.activeInHierarchy;
                if(complete)ready=clock-began;yield return null;
            }
            File.AppendAllText(Path.Combine(_output,"first-picture632-cost.txt"),(german?"DE":"EN")+" stress="+stress+" first actual44 native originals="+ready+"s; events="+events+"; wire="+wire+"; captureCPUms="+captureWatch.Elapsed.TotalMilliseconds+"; sendCPUms="+sendCpu+"; receiveCPUms="+receiveCpu+"; applyCPUms="+applyCpu+"; module bytes="+string.Join(",",originals.Select(x=>x.Length))+"\n");
            if (ready < 0) File.AppendAllText(Path.Combine(_output,"first-picture632-cost.txt"),string.Join("\n",GloomhavenVR.Core.VRLog.Messages)+"\n");
            Check(ready>=0&&ready<=(stress?1.0001:.6501),"full original first picture arrives within1s without a template repair wait");
            Check(GloomhavenVR.Core.VRLog.Messages.Any(x=>x.Contains("Native original bundle assembled:")),"bounded hardware trace measures real fragment assembly rather than only modeled queue readiness");
            Check(GloomhavenVR.Core.VRLog.Messages.Any(x=>x.Contains("Native enhancement picture displayed:")),"bounded hardware trace confirms exact original clones after their actual Unity apply and activation");
            File.AppendAllText(Path.Combine(_output,"production-hardware-traces.txt"),string.Join("\n",GloomhavenVR.Core.VRLog.Messages)+"\n");
            for(ushort id=1;id<=44;id++)
            {
                var remote=Remote(2,id);var source=sources[id-1];
                foreach(var text in source.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (!text.enabled || !text.gameObject.activeInHierarchy) continue;
                    string path=Relative632(source,text.transform);var clone=(path.Length==0?remote.Root:remote.Root.Find(path))?.GetComponent<TMP_Text>();
                    Check(clone!=null&&clone.text==text.text&&clone.fontSize==text.fontSize,"all exact original row glyph content and owner appearance are present on first publication");
                }
            }
            // Reuse the actually admitted44-module picture; change one native row's
            // selected content and pose through CaptureCore, not a handcrafted delta.
            // Observers keep all prior originals while the new bounded packet assembles.
            string warmText=(german?"Ausgewählt: Schild":"Selected: shield")+" exact warm revision";
            sources[7].GetComponentsInChildren<TMP_Text>(true).First(x=>x.name=="Name").text=warmText;
            sources[7].localPosition+=new Vector3(.01f,.02f,0f);
            snapshots.Clear();NativeSenderCapture629(publish);double warmBegin=clock,warmReady=-1;
            for(int turn=0;turn<60&&warmReady<0;turn++)
            {
                clock+=.050001;if(!stress&&turn%2==0)FillOtherQueues632(scheduler,false);
                byte[] batch=scheduler.NextBatch(clock);if(batch==null){yield return null;continue;}
                Check(batch.Length<=PresentationBatch.MaxSize,"warm original revision retains the global cap");
                foreach(byte[] page in PresentationBatch.TryRead(batch,batch.Length,out var pages)?pages!:new[]{batch})
                {
                    if(TownServiceFragments.Stream(page,page.Length)<0)continue;
                    byte[] packet=fragments.Accept(2,page,page.Length,clock);if(packet==null)continue;
                    Receive(2,TownServiceCodec.TryReadBundle(packet,packet.Length,out var children)?children!:new[]{packet});
                }
                TownServiceMirror.TickRemote(_=>observer);
                for(ushort id=1;id<=44;id++)Check(Remote(2,id)?.Root.gameObject.activeInHierarchy==true,"a warm owner revision keeps the previous complete exact picture visible");
                var updated=Remote(2,8).Root.GetComponentsInChildren<TMP_Text>(true).First(x=>x.name=="Name");
                if(updated.text==warmText)warmReady=clock-warmBegin;
                yield return null;
            }
            File.AppendAllText(Path.Combine(_output,"first-picture632-cost.txt"),(german?"DE":"EN")+" stress="+stress+" warm actual original row="+warmReady+"s\n");
            Check(warmReady>=0&&warmReady<=1.2001,"warm native content updates without full original republishing or invisible gaps");
        }
    }
    private static string Relative632(Transform root,Transform node){string path="";while(node!=root){path=node.name+(path.Length==0?"":"/"+path);node=node.parent;}return path;}
    private static void FillOtherQueues632(ExtrasSendScheduler scheduler,bool stress)
    {
        var random=new System.Random(632);
        foreach(string field in new[]{"_presence","_animation","_plumes","_board","_appearance","_prompt","_itemAppearance","_mapTooltip"})
        {
            var queue=(ExtrasSendQueue)typeof(ExtrasSendScheduler).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(scheduler);
            int limit=(int)typeof(ExtrasSendQueue).GetField("_snapshotLimit",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(queue);
            byte type=(byte)typeof(ExtrasSendQueue).GetField("_payloadType",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(queue);
            var bytes=new byte[stress?limit:128];random.NextBytes(bytes);bytes[0]=0x31;bytes[1]=0x52;bytes[2]=0x56;bytes[3]=0x47;bytes[4]=3;bytes[5]=type;queue.Enqueue(bytes,bytes.Length);
        }
    }
    private static void FirstPictureVisibleInk632()
    {
        var parent=Go("Hidden source ancestor");var root=Source(parent.transform);var binding=new TownServiceBinding(root);
        Check(binding.HasVisibleOutput(),"original ink is visible at normal native alpha");
        foreach(var graphic in root.GetComponentsInChildren<Graphic>(true))graphic.enabled=false;
        foreach(var mesh in root.GetComponentsInChildren<MeshRenderer>(true))mesh.enabled=false;
        Check(!binding.HasVisibleOutput(),"active but disabled original graphics do not gate first picture");
        var text=root.Find("Name").GetComponent<TextMeshProUGUI>();text.enabled=true;text.canvasRenderer.cull=false;
        var hidden=parent.AddComponent<CanvasGroup>();hidden.alpha=0;
        var ignore=text.gameObject.AddComponent<CanvasGroup>();ignore.ignoreParentGroups=true;
        Check(binding.HasVisibleOutput(),"a painted child that ignores native ancestor alpha stays in the exact visible census");
        ignore.ignoreParentGroups=false;Check(!binding.HasVisibleOutput(),"truly transparent ancestor removes original ink from first-picture census");
        var physical=root.GetComponentsInChildren<MeshRenderer>(true).First();physical.enabled=true;
        Check(binding.HasVisibleOutput(),"native canvas alpha cannot hide an actual visible physical mesh partition");
        physical.enabled=false;
        var canvas=root.GetComponent<Canvas>();Check(canvas!=null,"native ink fixture owns its real active source canvas");canvas.enabled=false;hidden.alpha=1f;
        Check(!binding.HasVisibleOutput(),"an inactive original canvas cannot gate a first picture despite active graphic transforms");
        binding.Dispose();
    }
}
