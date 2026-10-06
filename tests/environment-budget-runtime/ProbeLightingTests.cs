using System;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class EnvironmentProgram
{
    private static void NativeCameraBoundaryLifecycle()
    {
        using var room = new Room();
        var initialOwner=VRSession.Harmony;
        int invocations=0;
        Action<Camera> listener=camera=> { if(camera==room.Camera) invocations++; };
        ScenarioCameraCullBoundary.Subscribe(listener);
        try
        {
            room.Render(); Check(invocations==1,"actual native final camera boundary runs once with the original plugin owner");
            initialOwner.UnpatchSelf();
            room.Render(); Check(invocations==1,"actual plugin owner removal retires the native camera postfix");
            VRSession.Harmony=null!;
            bool failed=false;
            try { ScenarioCameraCullBoundary.Install(); } catch(InvalidOperationException) { failed=true; }
            Check(failed,"missing session Harmony owner fails before optional camera admission becomes installed");
            VRSession.Harmony=new HarmonyLib.Harmony();
            ScenarioCameraCullBoundary.Install(); ScenarioCameraCullBoundary.Install();
            Check(VRSession.Harmony.PatchCalls==1,"new plugin owner installs the shared native camera boundary exactly once");
            room.Render(); Check(invocations==2,"same-domain plugin owner replacement restores the actual native final camera boundary");
        }
        finally
        {
            VRSession.Harmony=initialOwner;
            ScenarioCameraCullBoundary.Install();
            ScenarioCameraCullBoundary.Unsubscribe(listener);
        }
    }

    private static void NativeCameraBoundaryOrder()
    {
        using var room = new Room(); room.Floor(.5f); room.Floor(2.5f);
        var stages = new List<string>();
        var component=room.Camera.gameObject.AddComponent<CameraComponentPreCullFixture>();
        component.Work=()=>stages.Add("component");
        Action<Camera> final=camera=> { if(camera==room.Camera) stages.Add("final"); };
        ScenarioCameraCullBoundary.Subscribe(final);
        room.ObserveRender=()=>stages.Add("event");
        try { room.Render(); }
        finally { room.ObserveRender=null; ScenarioCameraCullBoundary.Unsubscribe(final); }
        Check(stages.Contains("component")&&stages.Contains("event")&&stages.Contains("final"),
            "actual native camera executes component callbacks global events and the real Harmony final boundary");
        Check(stages.IndexOf("final")>stages.IndexOf("event")&&stages.IndexOf("final")>stages.IndexOf("component"),
            "actual managed final camera boundary runs after native component and all pre-cull event callbacks ("+string.Join(",",stages)+")");
    }

    private static void LateNativeCommandBufferConsumer()
    {
        foreach(bool instances in new[]{false,true})
        {
            using var room=new Room(); var first=room.Floor(.5f); var second=room.Floor(2.5f);
            if(instances) { second.GetComponent<MeshFilter>().sharedMesh=first.GetComponent<MeshFilter>().sharedMesh; room.Original.enableInstancing=true; }
            var nativeDraw=room.Material(); nativeDraw.SetColor("_Tint",Color.green);
            var consumer=new CommandBuffer { name="GloomhavenVR.EnvironmentInstances" };
            consumer.DrawRenderer(first,nativeDraw,0,0);
            room.Camera.AddCommandBuffer(CameraEvent.AfterForwardOpaque,consumer);
            Color32[] native=room.Render(); room.Camera.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque,consumer);
            Configure(!instances,false,100); PerfConfig.EnvironmentDrawInstancingOn=instances; ScenarioEnvironmentBudget.BeforeLoadingComplete();
            bool started=false,restored=false;
            room.ObserveRender=()=>
            {
                started=first.forceRenderingOff&&second.forceRenderingOff;
                room.Camera.AddCommandBuffer(CameraEvent.AfterForwardOpaque,consumer);
            };
            Camera.CameraCallback observe=camera=>
            {
                if(camera==room.Camera) restored=!first.forceRenderingOff&&!second.forceRenderingOff&&room.Camera.commandBufferCount==1&&Array.TrueForAll(room.Chunks(),r=>!r.enabled);
            };
            Camera.onPreRender+=observe; PerfMonitor.Counts.Clear();
            Color32[] retained;
            try { retained=room.Render(); }
            finally { Camera.onPreRender-=observe; room.ObserveRender=null; room.Camera.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque,consumer); consumer.Release(); }
            Check(started&&restored,"late foreign native DrawRenderer consumer restores original identity before actual camera culling");
            Check(SamePixels(native,retained),"late foreign native DrawRenderer consumer retains actual original geometry and material pixels");
            Check(PerfMonitor.Counts.TryGetValue("Environment.NativeBufferFallback",out long count)&&count==1
                &&!PerfMonitor.Counts.ContainsKey("Environment.ChunkSources")&&!PerfMonitor.Counts.ContainsKey("Environment.InstanceSources"),
                "late foreign native consumer counts one fallback and no completed substitute source savings");
        }
    }

    private static void LateNativeLightingFlags()
    {
        foreach(LightProbeUsage usage in new[]{LightProbeUsage.CustomProvided,LightProbeUsage.UseProxyVolume,LightProbeUsage.BlendProbes})
        {
            using var room=new Room(); var first=room.Floor(.5f); var second=room.Floor(2.5f);
            room.Original.SetFloat("_FixtureProbeLighting",1);
            first.lightProbeUsage=second.lightProbeUsage=LightProbeUsage.BlendProbes;
            var volume=Room.Child("NativeLateLighting.Override",room.Generated.transform).AddComponent<LightProbeProxyVolume>();
            Color32[] common=room.Render();
            var coefficients=new SphericalHarmonicsL2(); coefficients.AddAmbientLight(new Color(.9f,.05f,.02f,1));
            var block=new MaterialPropertyBlock(); block.CopySHCoefficientArraysFrom(new[]{coefficients});
            Action write=()=>
            {
                first.lightProbeUsage=usage;
                if(usage==LightProbeUsage.CustomProvided) first.SetPropertyBlock(block);
                else first.lightProbeProxyVolumeOverride=volume.gameObject;
            };
            write(); Color32[] native=room.Render();
            if(usage==LightProbeUsage.CustomProvided) Check(!SamePixels(common,native),"native custom-provided SH changes actual original source pixels");
            first.lightProbeUsage=LightProbeUsage.BlendProbes; first.lightProbeProxyVolumeOverride=null; first.SetPropertyBlock(null);
            Configure(true,false,100); ScenarioEnvironmentBudget.BeforeLoadingComplete();
            bool started=false,restored=false;
            room.ObserveRender=()=> { started=first.forceRenderingOff&&second.forceRenderingOff; write(); };
            Camera.CameraCallback observe=camera=>
            {
                if(camera==room.Camera) restored=!first.forceRenderingOff&&!second.forceRenderingOff&&Array.TrueForAll(room.Chunks(),r=>!r.enabled);
            };
            Camera.onPreRender+=observe; PerfMonitor.Counts.Clear(); Color32[] retained;
            try { retained=room.Render(); }
            finally { Camera.onPreRender-=observe; room.ObserveRender=null; }
            Check(started&&restored,"late native custom or proxy lighting flags restore masked original sources before actual culling: "+usage);
            Check(SamePixels(native,retained),"late native custom or proxy lighting flags preserve actual original source pixels: "+usage);
            Check(PerfMonitor.Counts.TryGetValue("Environment.LightingFallback",out long count)&&count==1&&!PerfMonitor.Counts.ContainsKey("Environment.ChunkSources"),
                "late native lighting flag refusal counts one fallback and no substitute source savings: "+usage);
        }
    }
    private static bool SamePixels(Color32[] left, Color32[] right)
    { if (left.Length != right.Length) return false; for (int i=0;i<left.Length;i++) if (!left[i].Equals(right[i])) return false; return true; }

    private static Cubemap CommonReflection(Color color)
    {
        var texture = new Cubemap(16,TextureFormat.RGBA32,false);
        var pixels = new Color[256]; for (int i=0;i<pixels.Length;i++) pixels[i]=color;
        for (int face=0;face<6;face++) texture.SetPixels(pixels,(CubemapFace)face);
        texture.Apply(); return texture;
    }

    private static ReflectionProbe LocalReflection(Room room, string name, Vector3 center, Vector3 size, Cubemap texture)
    {
        var probe = Room.Child(name,room.Generated.transform).AddComponent<ReflectionProbe>();
        probe.transform.position = center; probe.size = size; probe.mode = ReflectionProbeMode.Custom;
        probe.customBakedTexture = texture; probe.boxProjection = false; probe.blendDistance = 0;
        ReflectionProbe.UpdateCachedState();
        return probe;
    }

    private static void CommonAbsentLighting()
    {
        LightProbes savedProbes = LightmapSettings.lightProbes;
        AmbientMode savedMode = RenderSettings.ambientMode;
        Color savedAmbient = RenderSettings.ambientLight;
        SphericalHarmonicsL2 savedSH = RenderSettings.ambientProbe;
        DefaultReflectionMode savedReflection = RenderSettings.defaultReflectionMode;
        Texture savedCube = RenderSettings.customReflection;
        SphericalHarmonicsL2[] savedBaked = NativeProbeFixture!.bakedProbes;
        var common = CommonReflection(new Color(.08f,.15f,.22f,1));
        var localCube = CommonReflection(new Color(.5f,.02f,.02f,1));
        try
        {
            LightmapSettings.lightProbes = null;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.3f,.5f,.2f,1);
            var commonSH = new SphericalHarmonicsL2(); commonSH.AddAmbientLight(new Color(.3f,.5f,.2f,1)); RenderSettings.ambientProbe = commonSH;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom; RenderSettings.customReflection = common;
            using var room = new Room();
            var first = room.Floor(.5f); var second = room.Floor(2.5f);
            room.Original.SetFloat("_FixtureProbeLighting",1);
            first.lightProbeUsage = second.lightProbeUsage = LightProbeUsage.BlendProbes;
            first.reflectionProbeUsage = second.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            Color32[] original = room.Render();
            Check(Array.Exists(original,pixel=>pixel.r>0 || pixel.g>0 || pixel.b>0),"native common ambient and sky reflection oracle produces real colored pixels");
            Configure(true,false,100); ScenarioEnvironmentBudget.BeforeLoadingComplete();
            Check(room.Chunks().Length==1,"authored probe defaults with actual absent light and reflection probes create exact chunks");
            Color32[] grouped = room.Render();
            Check(room.LastRenderedChunks==1 && SamePixels(original,grouped),"absent-probe chunk preserves actual native SH and common reflection pixels");
            Check(first.lightProbeUsage==LightProbeUsage.BlendProbes && first.reflectionProbeUsage==ReflectionProbeUsage.BlendProbes
                && first.sharedMaterial==room.Original && !first.isPartOfStaticBatch,
                "absence-aware grouping retains authored native probe flags material slots and clone source state");
            Configure(false,false,100); Tick();
            RenderSettings.ambientLight = new Color(.6f,.1f,.35f,1);
            commonSH.Clear(); commonSH.AddAmbientLight(new Color(.6f,.1f,.35f,1)); RenderSettings.ambientProbe = commonSH;
            Color32[] changedAmbient = room.Render();
            Check(!SamePixels(original,changedAmbient),"native SH oracle responds to changed common ambient input");
            Configure(true,false,100); ScenarioEnvironmentBudget.BeforeLoadingComplete();
            Check(SamePixels(changedAmbient,room.Render()),"changed common ambient is retained by the next real chunk camera invocation");

            // One volume between separate floor bounds overlaps only aggregate bounds.
            var middle = LocalReflection(room,"NativeReflection.AggregateOnly",new Vector3(1.5f,0,0),new Vector3(.1f,2,2),localCube);
            bool restored = false;
            room.ObserveRender = () => restored = !first.forceRenderingOff && !second.forceRenderingOff && Array.TrueForAll(room.Chunks(),r=>!r.enabled);
            Color32[] aggregateRetained = room.Render(); room.ObserveRender = null;
            Check(restored && SamePixels(changedAmbient,aggregateRetained),"aggregate-only reflection volume rejects the combined sample and preserves original pixels");
            middle.enabled = false;
            ReflectionProbe.UpdateCachedState();
            Check(SamePixels(changedAmbient,room.Render()) && room.LastRenderedChunks==1,"removing a live local reflection volume restores the same prepared common-fallback chunk");
            middle.transform.position = new Vector3(100,0,0); middle.enabled = true; ReflectionProbe.UpdateCachedState();
            room.Render();
            Check(room.LastRenderedChunks==0,"active off-volume reflection consumers stay native before later movement or rebake");
            room.ObserveRender = () => { middle.transform.position=new Vector3(.5f,0,0); middle.size=new Vector3(1.3f,2,1.3f); ReflectionProbe.UpdateCachedState(); };
            Color32[] movedRetained=room.Render(); room.ObserveRender=null;
            Configure(false,false,100); Tick();
            Check(SamePixels(movedRetained,room.Render()),"moving an existing native reflection volume in a later pre-cull callback retains original lighting pixels");
            middle.customBakedTexture=common; ReflectionProbe.UpdateCachedState();
            Color32[] rebakedOriginal=room.Render();
            Configure(true,false,100); ScenarioEnvironmentBudget.BeforeLoadingComplete();
            Check(room.Chunks().Length==0 && SamePixels(rebakedOriginal,room.Render()),"rebaked existing reflection data keeps per-object source rendering native");
            middle.enabled=false; ReflectionProbe.UpdateCachedState();
            ScenarioEnvironmentBudget.BeforeLoadingComplete();

            // Created by a later native pre-cull callback, after the budget acquired masks.
            ReflectionProbe? late = null;
            room.ObserveRender = () => late = LocalReflection(room,"NativeReflection.Late",new Vector3(.5f,0,0),new Vector3(1.3f,2,1.3f),localCube);
            Color32[] lateRetained = room.Render(); room.ObserveRender = null;
            Check(late!=null && !first.forceRenderingOff && !second.forceRenderingOff,
                "late native reflection appearance releases all source leases in the same camera");
            Configure(false,false,100); Tick(); Color32[] lateOriginal = room.Render();
            Check(!SamePixels(changedAmbient,lateOriginal),"native per-object reflection oracle responds to a newly active local probe ("+changedAmbient[1200]+" -> "+lateOriginal[1200]+")");
            Check(SamePixels(lateOriginal,lateRetained),"late native reflection appearance revokes the chunk before render and retains actual original lighting pixels");
            late!.enabled = false;
            ReflectionProbe.UpdateCachedState();
            Check(SamePixels(changedAmbient,room.Render()),"native reflection disable event preserves original common-fallback pixels");
            Configure(true,false,100); ScenarioEnvironmentBudget.BeforeLoadingComplete();
            ReflectionProbe.UpdateCachedState();
            room.ObserveRender = () => { late.enabled = true; ReflectionProbe.UpdateCachedState(); };
            Color32[] lateEnabled = room.Render(); room.ObserveRender = null;
            Configure(false,false,100); Tick();
            Check(SamePixels(room.Render(),lateEnabled),"native reflection enable event releases common-fallback chunks before actual source culling");
            late.enabled = false; ReflectionProbe.UpdateCachedState();

            // A live native baked-volume installation must also release masks
            // before culling; property flags by themselves are not the fixture.
            var baked=NativeProbeFixture!.bakedProbes;
            var positions=NativeProbeFixture.positions;
            var coefficients=new SphericalHarmonicsL2[baked.Length];
            for(int i=0;i<coefficients.Length;i++) coefficients[i].AddAmbientLight(new Color(.1f+.7f*(positions[i].x+2f)/6f,.2f,.15f,1));
            NativeProbeFixture.bakedProbes=coefficients;
            Configure(true,false,100); ScenarioEnvironmentBudget.BeforeLoadingComplete();
            bool finalRestored=false;
            Camera.CameraCallback finalObserver=camera=>
            {
                if(camera==room.Camera) finalRestored=!first.forceRenderingOff&&!second.forceRenderingOff&&Array.TrueForAll(room.Chunks(),r=>!r.enabled);
            };
            Camera.onPreRender+=finalObserver;
            room.ObserveRender = () => LightmapSettings.lightProbes=NativeProbeFixture;
            Color32[] lateBaked;
            try { lateBaked=room.Render(); }
            finally { Camera.onPreRender-=finalObserver; room.ObserveRender=null; }
            Check(finalRestored,"late native baked-probe installation restores original source masks before culling");
            Configure(false,false,100); Tick();
            Color32[] bakedOriginal=room.Render();
            Check(!SamePixels(changedAmbient,bakedOriginal),"native populated baked SH fixture changes actual original lighting pixels");
            Check(SamePixels(lateBaked,bakedOriginal),"late native baked-probe installation retains original lighting pixels before culling");
            LightmapSettings.lightProbes!.bakedProbes=coefficients;
            LightProbes.Tetrahedralize();
            int nearIndex=Array.FindIndex(positions,p=>p.x<0), farIndex=Array.FindIndex(positions,p=>p.x>0);
            Check(nearIndex>=0&&farIndex>=0,"original native baked probe fixture has separated positions on both sides of its hull");
            LightProbes.GetInterpolatedProbe(positions[nearIndex],first,out SphericalHarmonicsL2 nearSH);
            LightProbes.GetInterpolatedProbe(positions[farIndex],second,out SphericalHarmonicsL2 farSH);
            var nearColor=new Color[1]; var farColor=new Color[1];
            nearSH.Evaluate(new[]{Vector3.up},nearColor); farSH.Evaluate(new[]{Vector3.up},farColor);
            Check(Mathf.Abs(nearColor[0].r-farColor[0].r)>.2f,"actual native GetInterpolatedProbe samples distinct baked SH inputs at the original hull positions ("+nearColor[0]+" / "+farColor[0]+")");
            var near = Room.Child("NativeProbeAnchor.Near",room.Generated.transform); near.transform.position=positions[nearIndex];
            var far = Room.Child("NativeProbeAnchor.Far",room.Generated.transform); far.transform.position=positions[farIndex];
            first.probeAnchor=near.transform; second.probeAnchor=far.transform;
            Color32[] anchored=room.Render();
            Check(!anchored[1168].Equals(anchored[1184]),"native populated SH fixture responds to distinct original probe anchors ("+anchored[1168]+" / "+anchored[1184]+")");
            NativeProbeFixture.bakedProbes=baked;

            // A genuinely populated baked light volume remains per-object even with
            // the simplified shader. This uses the editor's native engine bake.
            LightmapSettings.lightProbes = NativeProbeFixture;
            Configure(true,false,100); ScenarioEnvironmentBudget.BeforeLoadingComplete();
            Check(room.Chunks().Length==0,"native populated baked light probes refuse absence-aware chunks");
            first.lightProbeUsage = second.lightProbeUsage = LightProbeUsage.CustomProvided;
            LightmapSettings.lightProbes = null; ScenarioEnvironmentBudget.Placed(room.Generated); ScenarioEnvironmentBudget.BeforeLoadingComplete();
            Check(room.Chunks().Length==0,"unknown custom probe provision never inherits absent baked-probe admission");
            first.lightProbeUsage=second.lightProbeUsage=LightProbeUsage.BlendProbes; first.probeAnchor=second.probeAnchor=null;
            Configure(false,false,100); Tick(); Color32[] removedBaked=room.Render();
            Configure(true,false,100); ScenarioEnvironmentBudget.BeforeLoadingComplete();
            Check(room.Chunks().Length==1&&SamePixels(removedBaked,room.Render()),
                "removing populated native SH data restores equal native common-fallback and grouped pixels");
        }
        finally
        {
            LightmapSettings.lightProbes=savedProbes; RenderSettings.ambientMode=savedMode; RenderSettings.ambientLight=savedAmbient;
            RenderSettings.ambientProbe = savedSH;
            RenderSettings.defaultReflectionMode=savedReflection; RenderSettings.customReflection=savedCube;
            NativeProbeFixture!.bakedProbes=savedBaked;
            UnityEngine.Object.DestroyImmediate(common); UnityEngine.Object.DestroyImmediate(localCube);
        }
    }

    private static void LivePreparationReport()
    {
        using var room = new Room(); room.Floor(.5f); room.Floor(2.5f);
        Configure(true,false,100); VRLog.DebugLines.Clear(); Tick();
        Check(VRLog.DebugLines.Exists(line=>line.Contains("Scenario environment preparation: chunk candidates=2")),
            "live post-load settings rebuild publishes its bounded actual candidate/refusal summary");
        int reports = VRLog.DebugLines.Count;
        Tick(); Tick(); room.Render(); room.Render();
        Check(VRLog.DebugLines.Count==reports,"steady camera and update paths do not repeat preparation diagnostics");
        for (int i=0;i<50;i++) { ScenarioEnvironmentBudget.Placed(room.Generated); Tick(); }
        Check(VRLog.DebugLines.FindAll(line=>line.StartsWith("Scenario environment preparation:")).Count<=32,
            "preparation diagnostics are bounded for each scene despite repeated native placement");
        for(int i=0;i<10;i++) { Configure(false,false,100); Tick(); Configure(true,false,100); Tick(); }
        Check(VRLog.DebugLines.FindAll(line=>line.StartsWith("Scenario environment preparation:")).Count<=32,
            "complete option off/on toggles cannot reset the current scene preparation diagnostic bound");
    }

    private static void DetailedPreparationRefusals()
    {
        using var room=new Room(); var first=room.Floor(.5f); var second=room.Floor(2.5f);
        Configure(true,false,100); ScenarioEnvironmentBudget.BeforeLoadingComplete();
        var block=new MaterialPropertyBlock(); block.SetFloat("_FixtureNativeFlag",1); first.SetPropertyBlock(block);
        first.lightmapIndex=0; first.gameObject.AddComponent<LODGroup>();
        first.additionalVertexStreams=first.GetComponent<MeshFilter>().sharedMesh;
        first.shadowCastingMode=ShadowCastingMode.On; first.receiveShadows=true;
        first.lightProbeUsage=LightProbeUsage.CustomProvided;
        first.reflectionProbeUsage=ReflectionProbeUsage.BlendProbes;
        var cube=CommonReflection(Color.red);
        try
        {
            LocalReflection(room,"NativeReflection.RefusalReport",Vector3.zero,Vector3.one*100,cube);
            ScenarioEnvironmentBudget.ConfigureTerrainIntegration(_=>{},_=>{},_=>{},()=>{},r=>r==second);
            PerfConfig.EnvironmentDrawInstancingOn=true; VRLog.DebugLines.Clear(); ScenarioEnvironmentBudget.BeforeLoadingComplete();
            string? detail=VRLog.DebugLines.Find(line=>line.StartsWith("Scenario environment native refusal observations:"));
            foreach(string field in new[]{"terrain","MPB","lightmap","LOD","shadows(instances)","source geometry","light probes","local reflections"})
                Check(detail!=null && System.Text.RegularExpressions.Regex.IsMatch(detail,System.Text.RegularExpressions.Regex.Escape(field)+"=[1-9][0-9]*"),
                    "live preparation exposes positive native refusal field "+field);
            Check(detail!.Contains("Flags may overlap")&&detail.Contains("both grouping paths"),
                "live refusal diagnostics declare overlapping observations instead of unique scene renderer counts");
            VRLog.DebugEnabled=false; VRLog.DebugLines.Clear(); ScenarioEnvironmentBudget.BeforeLoadingComplete();
            Check(VRLog.DebugLines.Count==0 && (int)Driver.GetType().GetField("_terrainRefusals",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(Driver)! == 0,
                "normal logging skips live refusal sampling and all detailed preparation report formatting");
        }
        finally
        {
            ScenarioEnvironmentBudget.ConfigureTerrainIntegration(_=>{},_=>{},_=>{},()=>{},_=>false);
            VRLog.DebugEnabled=true; UnityEngine.Object.DestroyImmediate(cube);
        }
    }
}

public sealed class CameraComponentPreCullFixture : MonoBehaviour
{
    public Action? Work = null;
    private void OnPreCull() => Work?.Invoke();
}
