using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using Object=UnityEngine.Object;

public static partial class EnvironmentProgram
{
    private static void PerformanceWallConsumers()
    {
        using var room=new Room();
        MeshRenderer first=room.Surface("Hidden original pillar A",x:1),second=room.Surface("Hidden original pillar B",x:2);
        first.GetComponent<MeshFilter>().sharedMesh=NativePillar();second.GetComponent<MeshFilter>().sharedMesh=NativePillar(true);
        var floor=room.Floor(4);var hidden=new HashSet<Renderer>();int changed=0,ready=0;
        ScenarioEnvironmentBudget.ConfigurePerformanceWallIntegration(hidden.Contains,()=>changed++,r=>{ready++;if(hidden.Contains(r))r.forceRenderingOff=true;});
        try
        {
            ScenarioEnvironmentBudget.ConfigureStructuralBatching(()=>true);Configure(true,true,100);ScenarioEnvironmentBudget.BeforeLoadingComplete();
            Check(room.Chunks().Length>0,"performance wall fixture prepares actual environment geometry consumers");
            Tick("HandlePreCull",room.Camera);
            ScenarioEnvironmentBudget.BeforeNativeRendererWrite(first);hidden.Add(first);first.forceRenderingOff=true;
            ScenarioEnvironmentBudget.BeforeNativeRendererWrite(second);hidden.Add(second);second.forceRenderingOff=true;
            ScenarioEnvironmentBudget.BeforeNativeContentChange();
            Check(first.forceRenderingOff&&second.forceRenderingOff,"content clone recovery cannot unmask performance-owned native wall flags");
            first.forceRenderingOff=false;ScenarioEnvironmentBudget.MaterialReady(first);
            Check(first.forceRenderingOff&&ready==1,"actual native material-ready consumer handoff reapplies exact wall mask last");
            ScenarioEnvironmentBudget.Placed(room.Generated);ScenarioEnvironmentBudget.BeforeLoadingComplete();Tick("HandlePreCull",room.Camera);Tick("HandlePostRender",room.Camera);
            Check(!ScenarioEnvironmentBudget.OwnsRenderSubstitute(first)&&!ScenarioEnvironmentBudget.OwnsRenderSubstitute(second)&&first.forceRenderingOff&&second.forceRenderingOff,"hidden walls cannot re-enter environment private chunks or lose masks on post-render");
            Check(floor.sharedMaterial.shader.name=="GloomhavenVR/ScenarioSimpleEnvironment","unhidden floor retains actual simplified material route beside absent walls");
            Configure(false,false,100);Tick();
            Check(first.forceRenderingOff&&second.forceRenderingOff,"environment settings restoration cannot erase independent wall masks");
            hidden.Clear();first.forceRenderingOff=second.forceRenderingOff=false;ScenarioEnvironmentBudget.MaterialReady(first);ScenarioEnvironmentBudget.MaterialReady(second);
            Configure(true,true,100);ScenarioEnvironmentBudget.BeforeLoadingComplete();Tick("HandlePreCull",room.Camera);
            Check(ScenarioEnvironmentBudget.OwnsRenderSubstitute(first)&&ScenarioEnvironmentBudget.OwnsRenderSubstitute(second),"Regular admits restored native pillars back into current environment geometry");
            Check(changed>=2,"native placement and clone boundaries publish hidden inventory lifecycle invalidation");
        }
        finally{hidden.Clear();first.forceRenderingOff=second.forceRenderingOff=false;ScenarioEnvironmentBudget.ConfigurePerformanceWallIntegration(_=>false,()=>{},_=>{});}
    }
    private static void PerformanceAmbientOverlap()
    {
        using var room=new Room();var particle=room.Particle("p_fire_torch (8)");var renderer=particle.GetComponent<ParticleSystemRenderer>();
        var hidden=new HashSet<Renderer>();ScenarioEnvironmentBudget.ConfigurePerformanceWallIntegration(hidden.Contains,()=>{},_=>{});
        try
        {
            Configure(false,false,0);ScenarioEnvironmentBudget.BeforeLoadingComplete();Tick("HandlePreCull",room.Camera);
            Check(renderer.forceRenderingOff,"actual ambient density route owns native torch mask before wall policy");
            hidden.Add(renderer);renderer.forceRenderingOff=true;
            ScenarioEnvironmentBudget.BeforeNativeContentChange();
            Check(renderer.forceRenderingOff,"ambient recovery cannot clear independently hidden wall attachment flag");
            Configure(false,false,100);Tick();
            Check(renderer.forceRenderingOff,"ambient density change cannot clear independently hidden wall attachment flag");
        }
        finally{hidden.Clear();renderer.forceRenderingOff=false;ScenarioEnvironmentBudget.ConfigurePerformanceWallIntegration(_=>false,()=>{},_=>{});}
    }
}
