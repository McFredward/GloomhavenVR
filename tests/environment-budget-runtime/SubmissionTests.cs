using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class EnvironmentProgram
{
    private static void SharedReadOptionToggle()
    {
        using var room=new Room();var second=room.Material();
        for(int i=0;i<24;i++){room.Floor();room.Surface("CV_Floor_Base_SharedToggle",material:second);}
        Configure(false,true,100);ScenarioEnvironmentBudget.BeforeLoadingComplete();
        materialReads=0;room.Render();Check(materialReads==2,"shared material read option starts with exact camera-local reuse");
        PerfConfig.SharedEnvironmentMaterialReadsOn=false;materialReads=0;room.Render();
        Check(materialReads==48,"shared material read option Off repeats every original per-surface validation");
        PerfConfig.SharedEnvironmentMaterialReadsOn=true;materialReads=0;room.Render();
        Check(materialReads==2,"shared material read option On restores exact camera-local reuse");
    }
    private static Mesh BankOriginal(string name)
    {
        foreach (string file in Directory.GetFiles(Environment.GetEnvironmentVariable("GHVR_ENVIRONMENT_BANK_FIXTURE")!,"*-100.bytes"))
        {
            Mesh mesh=ScenarioEnvironmentMeshStream.Read(File.ReadAllBytes(file));
            if(mesh.name==name)return mesh;UnityEngine.Object.DestroyImmediate(mesh);
        }
        throw new InvalidOperationException("Native environment bank fixture original absent");
    }
    private static void VerifiedEnvironmentBank()
    {
        Mesh native=BankOriginal("EN_CR_Pillar_Thin");
        try
        {
            Check(ScenarioEnvironmentMeshBank.TryGetExact(native,out Mesh exact),"original metadata and actual source bundle SHA admit the private exact mesh");
            Check(exact!=native&&exact.isReadable&&exact.vertexCount==native.vertexCount&&exact.bounds==native.bounds,"bank exact render mesh remains private readable and preserves native bounds");
            Vector3[] nv=native.vertices,ev=exact.vertices;for(int i=0;i<nv.Length;i++)Check(nv[i]==ev[i],"exact prepared original positions remain unchanged");
            Vector3[] nn=native.normals,en=exact.normals;for(int i=0;i<nn.Length;i++)Check(nn[i]==en[i],"exact prepared native normal channels remain unchanged");
            Vector2[] nu=native.uv,eu=exact.uv;for(int i=0;i<nu.Length;i++)Check(nu[i]==eu[i],"exact prepared native UV channels remain unchanged");
            for(int sub=0;sub<native.subMeshCount;sub++){int[] ni=native.GetIndices(sub),ei=exact.GetIndices(sub);Check(ni.Length==ei.Length,"exact native index count");for(int i=0;i<ni.Length;i++)Check(ni[i]==ei[i],"exact original triangle winding/index order");}
            Check(ScenarioEnvironmentMeshBank.TryGetDetail(native,0,out Mesh coarse),"prepared static detail mesh resolves");
            Check(coarse.vertexCount==exact.vertexCount&&coarse.bounds==native.bounds&&coarse.subMeshCount==native.subMeshCount,"coarse 3D tiers preserve same-index morph correspondence and original material slots/bounds");
            string originalName=native.name;native.name="Unverified.Foreign.Mesh";
            Check(!ScenarioEnvironmentMeshBank.TryGetExact(native,out _),"unknown original metadata keeps native rendering");native.name=originalName;
            Bounds bounds=native.bounds;native.bounds=new Bounds(bounds.center+Vector3.up,bounds.size);
            Check(!ScenarioEnvironmentMeshBank.TryGetExact(native,out _),"stale original geometry bounds reject a substitute immediately");native.bounds=bounds;
            var entries=(IDictionary)typeof(ScenarioEnvironmentMeshBank).GetField("Entries",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;object? entry=null;
            foreach(object item in entries.Values){object signature=item.GetType().GetField("signature")!.GetValue(item)!;if((string)signature.GetType().GetField("name")!.GetValue(signature)! == originalName)entry=item;}
            Check(entry!=null,"bank provenance fixture binds actual native entry");
            Array sources=(Array)entry!.GetType().GetField("sources")!.GetValue(entry)!;var saved=new List<string>();
            foreach(object source in sources){var field=source.GetType().GetField("sha256")!;saved.Add((string)field.GetValue(source)!);field.SetValue(source,new string('0',64));}
            Check(!ScenarioEnvironmentMeshBank.TryGetExact(native,out _),"wrong actual source bundle hash keeps native rendering");
            for(int i=0;i<sources.Length;i++)sources.GetValue(i)!.GetType().GetField("sha256")!.SetValue(sources.GetValue(i),saved[i]);
            Check(ScenarioEnvironmentMeshBank.TryGetExact(native,out Mesh again)&&again==exact,"valid original provenance recovers its exact shared mesh");
            Array variants=(Array)entry.GetType().GetField("variants")!.GetValue(entry)!;
            foreach(object variant in variants)
            {
                if((int)variant.GetType().GetField("tier")!.GetValue(variant)!=50)continue;
                string filename=(string)variant.GetType().GetField("file")!.GetValue(variant)!;
                ((IDictionary)typeof(ScenarioEnvironmentMeshBank).GetField("Meshes",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!).Remove(filename);
                variant.GetType().GetField("sha256")!.SetValue(variant,new string('0',64));
                Check(!ScenarioEnvironmentMeshBank.TryGetDetail(native,50,out _),"corrupt prepared geometry hash keeps native rendering");
            }
            bool malformed=false;try{ScenarioEnvironmentMeshStream.Read(new byte[20]);}catch(InvalidDataException){malformed=true;}
            Check(malformed,"malformed private stream cannot create a native replacement");
        }
        finally{UnityEngine.Object.DestroyImmediate(native);}
    }
    private static void UnreadableExactChunks()
    {
        using var room=new Room();
        Mesh native=BankOriginal("CV_Floor_Basic_01");native.UploadMeshData(true);
        var first=room.Floor(.5f);var second=room.Floor(2.5f);
        first.GetComponent<MeshFilter>().sharedMesh=native;second.GetComponent<MeshFilter>().sharedMesh=native;
        Color32[] baseline=room.Render();
        Configure(true,false,100);PerfConfig.EnvironmentMeshBankOn=true;ScenarioEnvironmentBudget.BeforeLoadingComplete();
        Check(room.Chunks().Length==1,"verified unreadable native floor originals create a bounded private exact chunk");
        Check(!native.isReadable&&first.GetComponent<MeshFilter>().sharedMesh==native&&first.sharedMaterials.Length==1&&!first.isPartOfStaticBatch,
            "bank-backed chunks never rewrite unreadable native mesh/material slots or static-batch metadata");
        bool same=true;Color32[] replacement=room.Render();for(int i=0;i<baseline.Length;i++)same&=baseline[i].Equals(replacement[i]);
        Check(same,"actual bank-backed exact geometry preserves native unreadable floor pixels");
        room.Render();Check(!first.forceRenderingOff&&!second.forceRenderingOff,"second eye/camera releases its private exact chunk source lease");
        room.ObserveRender=()=>
        {
            typeof(ApparanceEntity_EnvironmentBudgetPatch).GetMethod("Prefix",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,null);
            var clone=UnityEngine.Object.Instantiate(first.gameObject,room.Generated.transform,false);var renderer=clone.GetComponent<MeshRenderer>();
            Check(!renderer.forceRenderingOff&&renderer.sharedMaterials.Length==1&&renderer.sharedMaterial==room.Original&&clone.GetComponent<MeshFilter>().sharedMesh==native&&!renderer.isPartOfStaticBatch,
                "revealed room cloned inside actual pre-cull preserves original unreadable mesh and nonempty material slots");
            clone.SetActive(false);
        };
        room.Render();room.ObserveRender=null;
        PerfConfig.EnvironmentMeshBankOn=false;Tick();ScenarioEnvironmentBudget.BeforeLoadingComplete();
        Check(room.Chunks().Length==0&&!first.forceRenderingOff&&!second.forceRenderingOff,"private mesh bank option Off restores unreadable original rendering immediately");
        PerfConfig.EnvironmentMeshBankOn=true;Tick();ScenarioEnvironmentBudget.BeforeLoadingComplete();
        Check(room.Chunks().Length==1,"private mesh bank option On recreates an exact substitute without mutating native originals");
        Configure(false,false,100);Tick();UnityEngine.Object.DestroyImmediate(native);
    }
    private static void MultipleSubmeshInstances()
    {
        using var room=new Room();var first=room.Floor(.5f);var second=room.Floor(2.5f);
        Mesh mesh=first.GetComponent<MeshFilter>().sharedMesh;int[] triangles=mesh.triangles;
        mesh.subMeshCount=2;mesh.SetTriangles(new[]{triangles[0],triangles[1],triangles[2]},0);mesh.SetTriangles(new[]{triangles[3],triangles[4],triangles[5]},1);
        second.GetComponent<MeshFilter>().sharedMesh=mesh;Material green=room.Material();green.SetColor("_Tint",Color.green);
        room.Original.enableInstancing=true;green.enableInstancing=true;
        first.sharedMaterials=new[]{room.Original,green};second.sharedMaterials=new[]{room.Original,green};
        Color32[] original=room.Render();PerfConfig.EnvironmentDrawInstancingOn=true;ScenarioEnvironmentBudget.BeforeLoadingComplete();
        Check(Members("_instances")==1,"multiple native submesh/material slots create one bounded exact instance group");
        Color32[] substituted=room.Render();bool same=true;for(int i=0;i<original.Length;i++)same&=original[i].Equals(substituted[i]);
        Check(same&&first.sharedMaterials.Length==2&&first.sharedMaterials[1]==green&&first.GetComponent<MeshFilter>().sharedMesh==mesh,
            "actual explicit multi-submesh draws retain original material order geometry and rendered pixels");
        second.transform.localPosition=new Vector3(1f,0,0);bool overlapNative=false;
        room.ObserveRender=()=>overlapNative=!first.forceRenderingOff&&!second.forceRenderingOff&&room.Camera.commandBufferCount==0;
        room.Render();room.ObserveRender=null;Check(overlapNative,"new overlapping instance bounds retain native opaque submission order");
        second.transform.localPosition=new Vector3(2.5f,0,0);
        first.lightmapIndex=0;bool retained=false;room.ObserveRender=()=>retained=!first.forceRenderingOff&&room.Camera.commandBufferCount==0;room.Render();room.ObserveRender=null;
        Check(retained,"native lightmapped instance sources immediately keep original renderer/lightmap behavior");
        first.lightmapIndex=-1;room.Original.SetFloat("_WallFade_On",1);retained=false;room.ObserveRender=()=>retained=!first.forceRenderingOff&&room.Camera.commandBufferCount==0;room.Render();room.ObserveRender=null;
        Check(retained,"new continuous native wall channel between eyes immediately keeps original geometry and materials");
    }

    private static void ExplicitCameraInstances()
    {
        using var room=new Room();var first=room.Floor(.5f);var second=room.Floor(2.5f);Mesh source=first.GetComponent<MeshFilter>().sharedMesh;second.GetComponent<MeshFilter>().sharedMesh=source;room.Original.enableInstancing=true;
        Color32[] baseline=room.Render();PerfConfig.EnvironmentDrawInstancingOn=true;ScenarioEnvironmentBudget.BeforeLoadingComplete();
        Check(Members("_instances")==1&&room.Chunks().Length==0,"explicit camera instance groups replace repeated eligible meshes without combined duplicate submission");
        bool leased=false;room.ObserveRender=()=>leased=first.forceRenderingOff&&second.forceRenderingOff&&room.Camera.commandBufferCount==1 && !ScenarioEnvironmentBudget.HasNativeCommandBufferConsumers(room.Camera);
        Color32[] instanced=room.Render();room.ObserveRender=null;bool same=true;int visible=0;for(int i=0;i<baseline.Length;i++){same&=baseline[i].Equals(instanced[i]);if(baseline[i].r>10)visible++;}
        Check(visible>20&&same,"actual camera-bound instanced draws preserve original repeated opaque floor pixels");
        Check(leased&&!first.forceRenderingOff&&!second.forceRenderingOff&&room.Camera.commandBufferCount==0,"instance camera completion restores source masks and removes private commands");
        Check(PerfMonitor.Counts["Environment.InstanceSources"]==2&&PerfMonitor.Counts["Environment.InstanceGroups"]==1,
            "completed instance camera reports actually leased sources and groups");
        MotionVectorGenerationMode motion=first.motionVectorGenerationMode;first.motionVectorGenerationMode=MotionVectorGenerationMode.ForceNoMotion;room.ObserveRender=()=>Check(!first.forceRenderingOff&&room.Camera.commandBufferCount==0,"unsupported native instance render flags immediately keep originals");room.Render();room.ObserveRender=null;first.motionVectorGenerationMode=motion;
        PerfMonitor.Counts.Clear();bool released=false;room.ObserveRender=()=>{Check(first.forceRenderingOff&&room.Camera.commandBufferCount==1,"late native write begins after actual instance submission");ScenarioEnvironmentBudget.BeforeNativeRendererWrite(first);var block=new MaterialPropertyBlock();block.SetColor("_Tint",Color.green);first.SetPropertyBlock(block);released=!first.forceRenderingOff&&!second.forceRenderingOff&&room.Camera.commandBufferCount==0;};room.Render();room.ObserveRender=null;
        Check(released,"late native pre-cull write revokes queued instance geometry before restoring originals");
        Check(!PerfMonitor.Counts.ContainsKey("Environment.InstanceSources"),"revoked late instance submission does not report completed source savings");
        first.SetPropertyBlock(null);ScenarioEnvironmentBudget.MaterialReady(first);ScenarioEnvironmentBudget.BeforeLoadingComplete();
        var foreign=new CommandBuffer();room.Camera.AddCommandBuffer(CameraEvent.BeforeForwardOpaque,foreign);PerfMonitor.Counts.Clear();bool foreignOriginal=false;
        room.ObserveRender=()=>foreignOriginal=!first.forceRenderingOff&&room.Camera.commandBufferCount==1;
        try{room.Render();}finally{room.ObserveRender=null;room.Camera.RemoveCommandBuffer(CameraEvent.BeforeForwardOpaque,foreign);foreign.Release();}
        Check(foreignOriginal&&PerfMonitor.Counts["Environment.NativeBufferFallback"]==1&&!PerfMonitor.Counts.ContainsKey("Environment.InstanceSources"),
            "actual foreign command buffer reports native fallback without claimed source savings");
        var nested=Room.Child("RuntimeFixture.InstanceNestedCamera",room.Root.transform).AddComponent<Camera>();nested.CopyFrom(room.Camera);nested.enabled=false;var target=new RenderTexture(16,16,24);target.Create();nested.targetTexture=target;
        bool ran=false,inner=false,outer=false;Camera.CameraCallback callback=camera=>{if(camera==nested){inner=first.forceRenderingOff&&nested.commandBufferCount==1;return;}if(camera!=room.Camera||ran)return;ran=true;nested.Render();outer=first.forceRenderingOff&&room.Camera.commandBufferCount==1&&nested.commandBufferCount==0;};Camera.onPreCull+=callback;
        try{room.Render();}finally{Camera.onPreCull-=callback;nested.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(nested.gameObject);}
        Check(ran&&inner&&outer&&!first.forceRenderingOff&&room.Camera.commandBufferCount==0,"nested actual camera instance leases retain independent revocable commands and restore the outer mask");
        Tick("HandlePreCull",room.Camera);Check(first.forceRenderingOff,"interrupted instance camera establishes a temporary lease");
        typeof(ApparanceEntity_EnvironmentBudgetPatch).GetMethod("Prefix",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,null);
        var clone=UnityEngine.Object.Instantiate(first.gameObject,room.Generated.transform,false);var cr=clone.GetComponent<MeshRenderer>();
        Check(!cr.forceRenderingOff&&cr.sharedMaterials.Length==1&&cr.sharedMaterial==room.Original&&clone.GetComponent<MeshFilter>().sharedMesh==source&&!cr.isPartOfStaticBatch,"actual later-room clone retains original mesh/material slots and draw flags after the production Apparance prefix");
        Check(room.Camera.commandBufferCount==0&&!first.forceRenderingOff,"room content changes revoke interrupted queued commands before cloning");
        PerfConfig.EnvironmentDrawInstancingOn=false;Tick();Check(Members("_instances")==0&&!first.forceRenderingOff&&!second.forceRenderingOff&&room.Camera.commandBufferCount==0,"live instance option Off restitutes native sources and private command buffers");
    }

    private static int VisiblePixels(Color32[] pixels)
    {int count=0;foreach(Color32 pixel in pixels)if(pixel.r>10)count++;return count;}
    private static void RevealedClonePixels()
    {
        using var room=new Room();var first=room.Floor(.5f);var second=room.Floor(2.5f);
        Mesh native=first.GetComponent<MeshFilter>().sharedMesh;second.GetComponent<MeshFilter>().sharedMesh=native;
        room.Original.enableInstancing=true;PerfConfig.EnvironmentDrawInstancingOn=true;ScenarioEnvironmentBudget.BeforeLoadingComplete();
        GameObject? clone=null;bool nativeClone=false;
        Action reveal=()=>
        {
            typeof(ApparanceEntity_EnvironmentBudgetPatch).GetMethod("Prefix",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,null);
            clone=UnityEngine.Object.Instantiate(first.gameObject,room.Generated.transform,false);clone.transform.localPosition=new Vector3(1.5f,0,0);
            var renderer=clone.GetComponent<MeshRenderer>();nativeClone=!renderer.forceRenderingOff&&renderer.sharedMaterials.Length==1&&renderer.sharedMaterial==room.Original&&clone.GetComponent<MeshFilter>().sharedMesh==native&&!renderer.isPartOfStaticBatch;
            first.enabled=false;second.enabled=false;
        };
        Action reset=()=>{if(clone!=null)UnityEngine.Object.DestroyImmediate(clone);clone=null;first.enabled=true;second.enabled=true;};
        reveal();Check(nativeClone&&VisiblePixels(room.Render())>20,"revealed clone alone draws real original pixels between cameras");reset();
        room.ObserveRender=reveal;Color32[] during=room.Render();room.ObserveRender=null;
        Check(nativeClone&&VisiblePixels(during)>20,"revealed clone alone draws real original pixels after in-camera source restitution");reset();
        var nested=Room.Child("RuntimeFixture.RevealNestedCamera",room.Root.transform).AddComponent<Camera>();nested.CopyFrom(room.Camera);nested.enabled=false;
        var target=new RenderTexture(16,16,24);target.Create();nested.targetTexture=target;bool ran=false;
        Camera.CameraCallback callback=camera=>{if(camera==nested){reveal();return;}if(camera==room.Camera&&!ran){ran=true;nested.Render();}};
        Camera.onPreCull+=callback;Color32[] outer;
        try{outer=room.Render();}
        finally{Camera.onPreCull-=callback;nested.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(nested.gameObject);}
        Check(ran&&nativeClone&&VisiblePixels(outer)>20&&room.Camera.commandBufferCount==0,"revealed clone alone draws real original pixels after nested-camera source restitution");reset();
    }
}
