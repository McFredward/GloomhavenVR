using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using GloomhavenVR.Core;
using UnityEngine;
using Object = UnityEngine.Object;

// CPU evidence: real Unity renderers/material APIs and the complete production
// PreCull execute. No replacement algorithms, native-call observers or per-call
// reflection run in the timed region. Controllers/config remain explicit models.
public static class WorldMaterialCpuProgram
{
    [Serializable] public sealed class Measurement
    {
        public double elapsedMilliseconds;
        public long allocatedBytes;
        public int frames;
        public long monoHeapDelta;
        public int collections;
        public double[] frameMilliseconds=Array.Empty<double>();
    }
    private sealed class State
    {
        internal GameObject Host=null!, Root=null!, CameraRoot=null!;
        internal Camera Camera=null!;
        internal Action<Camera> Cull=null!;
        internal readonly List<MeshRenderer> Sources=new();
        internal readonly List<Material> Materials=new();
        internal readonly List<Object> Owned=new();
        internal MaterialPropertyBlock Block=new();
        internal bool Mutations, Options, Nested, Off;
        internal int Frame;
        internal void FrameWork()
        {
            if(Mutations)
            {
                Material material=Materials[Frame%Materials.Count];
                material.SetColor("_Tint",(Frame&1)==0?Color.red:Color.green);
                if((Frame&1)==0)material.EnableKeyword("_WALLFADE_ON_ON");
                else material.DisableKeyword("_WALLFADE_ON_ON");
                MeshRenderer source=Sources[Frame%Sources.Count];
                WorldMaterialBudget.BeforeNativeRendererWrite(source);
                source.sharedMaterial=material;
                Block.Clear();Block.SetColor("_Tint",(Frame&1)==0?Color.blue:Color.white);
                source.SetPropertyBlock(Block);
            }
            for(int eye=0;eye<2;eye++)
            {
                if(Nested)
                {
                    using(IDisposable pass=WorldMaterialBudget.BeginMaterialReadPass())
                    {
                        WorldMaterialBudget.VariantFor(Materials[0]);
                        Cull(Camera);
                    }
                }
                else Cull(Camera);
            }
            if(Options)
            {
                Camera.cullingMask=1<<31;
                Cull(Camera);Cull(Camera);
                Camera.cullingMask=-1;
            }
            Frame++;
        }
    }
    public static object Create(string workload)
    {
        var state=new State();
        state.Host=new GameObject("CPU budget host "+workload);
        state.Root=new GameObject("CPU native scenario "+workload);
        state.Root.AddComponent<ProceduralScenario>();
        var generated=new GameObject("Generated Content");generated.transform.SetParent(state.Root.transform,false);
        var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);
        Mesh mesh=cube.GetComponent<MeshFilter>().sharedMesh;Object.DestroyImmediate(cube);
        bool realistic=workload.StartsWith("representative",StringComparison.Ordinal);
        bool unsupported=workload.Contains("unsupported"), effect=workload.Contains("effect-refused");
        int unique=workload.Contains("unique")?440:realistic?23:workload.Contains("many")?64:1;
        int slots=workload.Contains("four-slot")?4:workload.Contains("two-slot")?2:1;
        bool blocks=workload.Contains("mpb"), standard=workload.Contains("standard");
        RenderTexture? video=null;
        if(workload.Contains("mpb-video")){video=new RenderTexture(1,1,0);video.Create();state.Owned.Add(video);}
        Shader shader=Shader.Find(unsupported?"Fixture/UnreviewedWorld":standard?"Standard":"Amp_Basic_N_MRAO");
        if(shader==null)throw new Exception("Missing fixture shader "+workload);
        for(int i=0;i<unique;i++)
        {
            var material=new Material(shader){name="CPU native "+i};
            material.SetColor(standard?"_Color":"_Tint",Color.red);
            if(effect)material.SetFloat("_AddVertexAnim",1f);
            state.Materials.Add(material);
        }
        var rooms=new Transform[3];
        for(int i=0;i<rooms.Length;i++)
        {
            var room=new GameObject("Room "+i);room.transform.SetParent(generated.transform,false);
            room.AddComponent<ProceduralMapTile>();
            if(room.GetComponent<ProceduralStyle>()==null)room.AddComponent<ProceduralStyle>();
            if(room.GetComponent<ApparanceEntity>()==null)room.AddComponent<ApparanceEntity>();
            rooms[i]=room.transform;
        }
        for(int i=0;i<440;i++)
        {
            Transform parent=rooms[i%3];
            // Approximate the supplied ~580 scope nodes/eye without inventing
            // persisted scope certificates: 200 active intermediate transforms.
            if(realistic&&i>=64&&i<264||workload.Contains("deep"))
            {
                int depth=workload.Contains("deep")?4:1;
                for(int d=0;d<depth;d++)
                {var branch=new GameObject("Native branch "+i+" "+d);branch.transform.SetParent(parent,false);parent=branch.transform;}
            }
            var go=new GameObject("CPU native source "+i);go.transform.SetParent(parent,false);
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=go.AddComponent<MeshRenderer>();
            // 376 active sources, 402 active slots: representative ~374 variant
            // +28 native slots/eye; native refusals stay actual unsupported slots.
            int count=realistic&&i>=64&&i<90?2:slots;
            var native=new Material[count];
            for(int slot=0;slot<count;slot++)native[slot]=state.Materials[(i+slot)%unique];
            renderer.sharedMaterials=native;
            if(blocks)
            {
                if(workload!="representative-slot-mpb"&&!workload.Contains("indexed-only"))
                {state.Block.Clear();state.Block.SetColor("_Tint",Color.blue);
                    if(video!=null)state.Block.SetTexture("_MainTex",video);
                    if(workload.Contains("mpb-animation"))state.Block.SetFloat("_AddVertexAnim",1f);
                    renderer.SetPropertyBlock(state.Block);}
                if(!workload.Contains("wall-mpb"))
                    for(int slot=0;slot<count;slot++)
                    {state.Block.Clear();state.Block.SetColor("_Tint",Color.yellow);
                        if(workload.Contains("indexed-only"))
                        {if(video!=null)state.Block.SetTexture("_MainTex",video);if(workload.Contains("mpb-animation"))state.Block.SetFloat("_AddVertexAnim",1f);}
                        renderer.SetPropertyBlock(state.Block,slot);}
            }
            state.Sources.Add(renderer);
        }
        state.CameraRoot=new GameObject("CPU native eye "+workload);state.Camera=state.CameraRoot.AddComponent<Camera>();
        state.Camera.enabled=false;state.Camera.cullingMask=workload.Contains("excluded")?1<<31:-1;
        state.Mutations=workload.Contains("mutations");state.Options=workload.Contains("options");
        state.Nested=workload.Contains("nested");state.Off=workload=="off";
        PerfConfig.WorldMaterialQualityMode=2;
        PerfConfig.SharedEnvironmentMaterialReadsOn=true;
        VRLog.Level=workload.Contains("debug")?VRLogLevel.Debug:VRLogLevel.Info;
        VRLog.Messages.Clear();
        WorldMaterialBudget.ConfigureAmbientWeight(()=>.65f);
        WorldMaterialBudget.ConfigureAssetPreparation(()=>true);
        WorldMaterialBudget.ConfigureRenderSubstituteOwnership(_=>false);
        WorldMaterialBudget.ConfigureRenderSubstituteRevocation(_=>false);
        WorldMaterialBudget.Install(state.Host);
        var budget=typeof(WorldMaterialBudget);var field=budget.GetField("_driver",BindingFlags.NonPublic|BindingFlags.Static)!;
        object driver=field.GetValue(null)!;
        MethodInfo update=driver.GetType().GetMethod("Update",BindingFlags.NonPublic|BindingFlags.Instance)!;
        update.Invoke(driver,null);
        foreach(MeshRenderer renderer in state.Sources)WorldMaterialBudget.MaterialReady(renderer);
        MethodInfo cull=driver.GetType().GetMethod("PreCull",BindingFlags.NonPublic|BindingFlags.Instance)!;
        state.Cull=(Action<Camera>)Delegate.CreateDelegate(typeof(Action<Camera>),driver,cull);
        if(realistic)
        {
            var native=new Material(Shader.Find("Fixture/UnreviewedWorld")){name="Representative unsupported"};
            state.Materials.Add(native);
            for(int i=90;i<118;i++)state.Sources[i].sharedMaterial=native;
            for(int i=0;i<64;i++)state.Sources[i].enabled=false;
        }
        if(state.Off){PerfConfig.WorldMaterialQualityMode=0;update.Invoke(driver,null);}
        for(int i=0;i<24;i++)state.FrameWork();
        Validate(state);
        return state;
    }
    private static void Validate(State state)
    {
        foreach(string message in VRLog.Messages)
            if(message.Contains("failed"))throw new Exception("PreCull fail-open would invalidate measurement: "+message);
        if(!state.Off&&state.Sources.Count!=440)throw new Exception("Fixture source scale drift");
    }
    public static string Batch(object fixture,int frames)
    {
        var state=(State)fixture;
        var durations=new double[frames];
        // Outside the timed region and symmetric for both lanes. Prevent unrelated
        // old fixture garbage from determining which lane pays a collection.
        GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
        long heapStart=UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
        int collections=GC.CollectionCount(0)+GC.CollectionCount(1)+GC.CollectionCount(2);
        long allocationStart=GC.GetAllocatedBytesForCurrentThread();
        long start=Stopwatch.GetTimestamp();
        for(int i=0;i<frames;i++)
        {
            long frameStart=Stopwatch.GetTimestamp();state.FrameWork();
            durations[i]=(Stopwatch.GetTimestamp()-frameStart)*1000.0/Stopwatch.Frequency;
        }
        long end=Stopwatch.GetTimestamp();
        long allocated=GC.GetAllocatedBytesForCurrentThread()-allocationStart;
        long heapDelta=UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong()-heapStart;
        collections=GC.CollectionCount(0)+GC.CollectionCount(1)+GC.CollectionCount(2)-collections;
        Validate(state);
        return JsonUtility.ToJson(new Measurement{elapsedMilliseconds=(end-start)*1000.0/Stopwatch.Frequency,
            allocatedBytes=allocated,frames=frames,monoHeapDelta=heapDelta,collections=collections,frameMilliseconds=durations});
    }
    public static string AllocationProbe()
    {
        GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
        long before=GC.GetAllocatedBytesForCurrentThread();
        long heap=UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
        var positive=new byte[65536];positive[positive.Length-1]=1;
        long after=GC.GetAllocatedBytesForCurrentThread();
        long heapDelta=UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong()-heap;
        GC.KeepAlive(positive);
        return "{\"threadAllocatedPositive\":"+(after-before)+",\"monoHeapPositive\":"+heapDelta+"}";
    }
    public static string Describe(object fixture)
    {
        var state=(State)fixture;int active=0,slots=0,wideNonempty=0,indexedNonempty=0;
        VRLogLevel level=VRLog.Level;VRLog.Level=VRLogLevel.Debug;state.Cull(state.Camera);
        PerfMonitor.Counts.TryGetValue("WorldMaterial.ScopeNodeReads",out int nodes);VRLog.Level=level;
        var block=new MaterialPropertyBlock();var originals=new HashSet<Material>();
        foreach(MeshRenderer source in state.Sources)
        {
            if(!source.enabled)continue;active++;
            source.GetPropertyBlock(block);if(!block.isEmpty)wideNonempty++;
            Material[] materials=source.sharedMaterials;slots+=materials.Length;
            for(int slot=0;slot<materials.Length;slot++)
            {
                originals.Add(WorldMaterialBudget.CanonicalMaterial(materials[slot]));
                source.GetPropertyBlock(block,slot);if(!block.isEmpty)indexedNonempty++;
            }
        }
        return "{\"sources\":"+state.Sources.Count+",\"active\":"+active+",\"activeSlots\":"+slots+",\"activeOriginals\":"+originals.Count
            +",\"rendererBlocks\":"+wideNonempty+",\"indexedBlocks\":"+indexedNonempty+",\"scopeNodes\":"+nodes+"}";
    }
    public static void Destroy(object fixture)
    {
        var state=(State)fixture;
        WorldMaterialBudget.Shutdown();VRSession.Harmony.UnpatchSelf();
        Object.DestroyImmediate(state.Root);Object.DestroyImmediate(state.Host);Object.DestroyImmediate(state.CameraRoot);
        foreach(Material material in state.Materials)Object.DestroyImmediate(material);
        foreach(Object owned in state.Owned)Object.DestroyImmediate(owned);
    }
}
