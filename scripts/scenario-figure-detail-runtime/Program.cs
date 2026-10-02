using System;
using System.Reflection;
using GloomhavenVR.Board.FigureGrab;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class InteractionProgram
{
    private static int checks;
    private static readonly Type Budget=typeof(ScenarioFigureDetailBudget);
    private static Component driver=null!;
    private static int id;
    private static void Check(bool value,string message) { checks++; if(!value)throw new Exception(message); }
    private static void Tick()
    {
        FigureClock.Now+=2f;
        driver.GetType().GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(driver,null);
        driver.GetType().GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(driver,null);
    }
    private static GameObject Node(Transform? parent,string name)
    {
        var root=new GameObject(name);
        if(parent!=null)root.transform.SetParent(parent);
        return root;
    }
    private static Mesh Mesh(int count)
    {
        var mesh=new Mesh(); var vertices=new Vector3[count];
        for(int i=0;i<count;i++)vertices[i]=new Vector3(i%3,(i/3)%3,0);
        mesh.vertices=vertices;mesh.triangles=new[]{0,1,2};mesh.RecalculateBounds();return mesh;
    }
    private sealed class Figure
    {
        internal GameObject Root=null!;
        internal ActorBehaviour Actor=null!;
        internal LODGroup Group=null!;
        internal LOD[] Original=null!;
        internal Renderer[] Meshes=null!;
        internal Cloth Cloth=null!;
        internal Collider Collider=null!;
        internal Animator Animator=null!;
    }
    private static Figure Build(Scene scene,CActor.EType type,bool empty=false,bool withCloth=true)
    {
        var figure=new Figure { Root=Node(null,"Native model "+id++) };
        SceneManager.MoveGameObjectToScene(figure.Root,scene);
        // The native actor component can be a sibling of the actual model/LODGroup.
        figure.Actor=Node(figure.Root.transform,"Actor metadata").AddComponent<ActorBehaviour>();
        figure.Actor.Bind(figure.Root,new CActor { Type=type });
        var body=Node(figure.Root.transform,"Native animated body");
        figure.Animator=body.AddComponent<Animator>();figure.Collider=figure.Root.AddComponent<BoxCollider>();
        figure.Group=body.AddComponent<LODGroup>();figure.Meshes=new Renderer[3];
        for(int i=0;i<3;i++)
        {
            var item=Node(body.transform,"LOD"+i); item.AddComponent<MeshFilter>().sharedMesh=Mesh(12-i*4);
            figure.Meshes[i]=item.AddComponent<MeshRenderer>();
        }
        figure.Original=new[]{new LOD(.6f,new[]{figure.Meshes[0]}) { fadeTransitionWidth=.1f },
            new LOD(.3f,new[]{figure.Meshes[1]}) { fadeTransitionWidth=.2f },
            new LOD(.05f,empty?Array.Empty<Renderer>():new[]{figure.Meshes[2]}) { fadeTransitionWidth=.3f }};
        figure.Group.SetLODs(figure.Original);
        if(withCloth)
        {
            var fabric=Node(body.transform,"Original cloak");
            fabric.AddComponent<SkinnedMeshRenderer>().sharedMesh=Mesh(4);
            figure.Cloth=fabric.AddComponent<Cloth>();figure.Cloth.enabled=true;
        }
        ScenarioFigureDetailBudget.ActorReady(figure.Root);
        return figure;
    }
    private static bool Original(Figure figure)
    {
        LOD[] current=figure.Group.GetLODs();
        for(int i=0;i<current.Length;i++)
            if(current[i].screenRelativeTransitionHeight!=figure.Original[i].screenRelativeTransitionHeight
               || current[i].fadeTransitionWidth!=figure.Original[i].fadeTransitionWidth
               || current[i].renderers.Length!=figure.Original[i].renderers.Length
               || (current[i].renderers.Length>0 && current[i].renderers[0]!=figure.Original[i].renderers[0]))return false;
        return true;
    }
    private static bool Capped(Figure figure,int level)
    {
        LOD[] current=figure.Group.GetLODs();
        for(int i=0;i<level;i++)if(current[i].renderers.Length==0||current[i].renderers[0]!=figure.Meshes[level])return false;
        return true;
    }
    public static int Run()
    {
        checks=0;var scenario=SceneManager.GetSceneByName("ProcGen");
        if(!scenario.IsValid())scenario=SceneManager.CreateScene("ProcGen");
        Choreographer.s_Choreographer.m_ProcGenScene=scenario;
        PerfConfig.PlayerFigureDetailPercent=100;PerfConfig.EnemyFigureDetailPercent=100;
        PerfConfig.FigureClothSimulationEnabled=true;
        var host=Node(null,"Figure budget host");ScenarioFigureDetailBudget.Install(host);
        driver=host.GetComponent(Budget.GetNestedType("Driver",BindingFlags.NonPublic));
        var hero=Build(scenario,CActor.EType.Player);var enemy=Build(scenario,CActor.EType.Enemy);
        Tick();Check(Original(hero)&&Original(enemy),"100 percent preserves original native LOD table");
        var recorded=(System.Collections.ICollection)driver.GetType().GetField("_actors",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(driver)!;
        Check(recorded.Count==0,"original settings bypass all actor discovery and per-frame native reflection");
        PerfConfig.PlayerFigureDetailPercent=0;PerfConfig.EnemyFigureDetailPercent=100;
        PerfConfig.FigureClothSimulationEnabled=false;Tick();
        Check(Capped(hero,2)&&Original(enemy),"player detail controls only player native LODs");
        Check(!hero.Cloth.enabled&&!enemy.Cloth.enabled,"original enabled native cloth solvers obey separate compromise");
        Check(hero.Animator.enabled&&enemy.Animator.enabled&&hero.Collider.enabled,"LOD compromise preserves native animation and collision");
        foreach(Renderer renderer in hero.Meshes)Check(renderer.enabled,"native renderer enabled flags stay intact");
        Check(hero.Meshes[0].forceRenderingOff&&hero.Meshes[1].forceRenderingOff&&!hero.Meshes[2].forceRenderingOff,
            "orphaned fine meshes are masked while the original coarse body remains visible");
        LOD[] applied=hero.Group.GetLODs();
        for(int i=0;i<3;i++)Check(applied[i].screenRelativeTransitionHeight==hero.Original[i].screenRelativeTransitionHeight
            &&applied[i].fadeTransitionWidth==hero.Original[i].fadeTransitionWidth,"native transitions and far culling survive the cap");
        PerfConfig.EnemyFigureDetailPercent=0;Tick();Check(Capped(enemy,2),"enemy detail independently selects actual original coarse meshes");
        HeldFigures.Held=hero.Actor;Tick();Check(Original(hero)&&hero.Cloth.enabled,"local hold restores original detail and cloth before rendering");
        HeldFigures.Held=null;Tick();Check(Capped(hero,2)&&!hero.Cloth.enabled,"local release resumes saved quality compromise");
        NetHeldFigures.Held=enemy.Actor;Tick();Check(Original(enemy)&&enemy.Cloth.enabled,"remote hold restores original detail and cloth before rendering");
        NetHeldFigures.Held=null;Tick();Check(Capped(enemy,2)&&!enemy.Cloth.enabled,"remote release resumes saved quality compromise");
        var empty=Build(scenario,CActor.EType.Enemy,true);Tick();Check(Capped(empty,1),"empty far-cull LOD can never become a body replacement");
        var shared=Build(scenario,CActor.EType.Enemy);
        var accessory=Node(shared.Group.transform,"Native weapon shared by every LOD");
        accessory.AddComponent<MeshFilter>().sharedMesh=Mesh(3);var weapon=accessory.AddComponent<MeshRenderer>();
        for(int i=0;i<3;i++)shared.Original[i].renderers=new[]{shared.Meshes[i],weapon};
        shared.Group.SetLODs(shared.Original);Tick();
        Check(Capped(shared,2)&&!weapon.forceRenderingOff,"weapon shared by native levels is never omitted or masked");
        var protectedMask=Build(scenario,CActor.EType.Enemy);protectedMask.Meshes[0].forceRenderingOff=true;Tick();
        PerfConfig.EnemyFigureDetailPercent=100;Tick();
        Check(protectedMask.Meshes[0].forceRenderingOff&&!protectedMask.Meshes[1].forceRenderingOff,
            "foreign renderer masks survive owned cap restoration");
        PerfConfig.EnemyFigureDetailPercent=0;Tick();
        var fx=Build(scenario,CActor.EType.Enemy);
        var particle=Node(fx.Group.transform,"Native combat effect").AddComponent<ParticleSystem>().GetComponent<ParticleSystemRenderer>();
        fx.Original[1].renderers=new Renderer[]{particle};fx.Group.SetLODs(fx.Original);Tick();
        Check(Original(fx)&&!particle.forceRenderingOff,"mixed effect LOD table cannot admit particle masking");
        var summon=Build(scenario,CActor.EType.HeroSummon);Tick();Check(Capped(summon,2),"hero summon uses player figure policy");
        var unknown=Build(scenario,CActor.EType.Unknown);Tick();Check(Original(unknown)&&unknown.Cloth.enabled,"unknown non-figure native identity stays intact");
        var map=SceneManager.GetSceneByName("CampaignMap");if(!map.IsValid())map=SceneManager.CreateScene("CampaignMap");var mapHero=Build(map,CActor.EType.Player);Tick();
        Check(Original(mapHero)&&mapHero.Cloth.enabled,"map models and immersive NPCs are never admitted");
        var foreign=Build(scenario,CActor.EType.Enemy);Tick();
        LOD[] foreignTable=foreign.Group.GetLODs();foreignTable[0].screenRelativeTransitionHeight=.9f;
        foreign.Group.SetLODs(foreignTable);PerfConfig.EnemyFigureDetailPercent=100;Tick();
        Check(foreign.Group.GetLODs()[0].screenRelativeTransitionHeight==.9f,"foreign LOD controller table cannot be overwritten during restoration");
        Check(Original(enemy)&&Original(empty),"100 percent restores exact original native LOD table after change");
        PerfConfig.EnemyFigureDetailPercent=0;Tick();
        var takeover=Build(scenario,CActor.EType.Enemy);Tick();
        LOD[] taken=takeover.Group.GetLODs();taken[0].renderers=takeover.Original[0].renderers;
        taken[0].screenRelativeTransitionHeight=.85f;takeover.Group.SetLODs(taken);
        for(int i=0;i<30;i++)Tick();
        Check(!takeover.Meshes[0].forceRenderingOff&&takeover.Group.GetLODs()[0].screenRelativeTransitionHeight==.85f,
            "bounded steady ownership check releases masks when a foreign controller replaces the table");
        PerfConfig.EnemyFigureDetailPercent=100;Tick();
        Check(!enemy.Meshes[0].forceRenderingOff&&!enemy.Meshes[1].forceRenderingOff,"100 percent restores only owned omitted-renderer masks");
        hero.Actor.ForceNativePosition(2);hero.Cloth.enabled=false;NetHeldFigures.Held=hero.Actor;Tick();
        Check(!hero.Cloth.enabled,"native cloth teleport reset cannot be cancelled by hold restoration");
        hero.Actor.ForceNativePosition(0);hero.Cloth.enabled=true;Tick();
        Check(hero.Cloth.enabled,"native cloth reset completes normally during hold");
        NetHeldFigures.Held=null;Tick();Check(!hero.Cloth.enabled,"quality compromise resumes after native reset and hold");
        PerfConfig.FigureClothSimulationEnabled=true;Tick();Check(hero.Cloth.enabled&&enemy.Cloth.enabled,"cloth setting restores only previously enabled native solvers");
        var disabled=Build(scenario,CActor.EType.Enemy);disabled.Cloth.enabled=false;
        PerfConfig.FigureClothSimulationEnabled=false;Tick();PerfConfig.FigureClothSimulationEnabled=true;Tick();
        Check(!disabled.Cloth.enabled,"an originally disabled cloth solver must not be enabled by settings");
        PerfConfig.PlayerFigureDetailPercent=0;Tick();VRSession.IsRunning=false;Tick();
        Check(Original(hero)&&hero.Cloth.enabled,"VR off restores exact original LOD table and owned cloth");
        VRSession.IsRunning=true;for(int i=0;i<12;i++)Tick();Check(Capped(hero,2),"VR re-entry rediscovers native figures without stale ownership");
        Choreographer.s_Choreographer.m_ProcGenScene=default;SceneManager.SetActiveScene(map);Tick();
        Check(Original(hero),"scenario exit restores owned native figure table");
        // Late native actor creation is the SetActor boundary, independent of scene rescans.
        Choreographer.s_Choreographer.m_ProcGenScene=scenario;for(int i=0;i<12;i++)Tick();
        var late=Build(scenario,CActor.EType.Player);Tick();Check(Capped(late,2),"late spawned actor receives its configured original native LOD cap");
        ScenarioFigureDetailBudget.Shutdown();Check(Original(late)&&Original(hero),"shutdown restores owned tables and releases discovery state");
        var faultHost=Node(null,"Fault containment host");ScenarioFigureDetailBudget.Install(faultHost);
        driver=faultHost.GetComponent(Budget.GetNestedType("Driver",BindingFlags.NonPublic));
        PerfConfig.FigureClothSimulationEnabled=false;for(int i=0;i<16;i++)Tick();
        Check(Capped(hero,2)&&!hero.Cloth.enabled,"fault control starts from actual owned LOD and cloth changes");
        PerfConfig.ThrowOnRead=true;Tick();
        Check(Original(hero)&&hero.Cloth.enabled&&!hero.Meshes[0].forceRenderingOff,
            "optional driver fault restores actual native LOD table cloth and owned renderer masks");
        Check(!((Behaviour)driver).enabled,"first optional driver fault disables future work");
        for(int i=0;i<16;i++)Tick();
        int faults=0;foreach(string message in VRLog.Messages)if(message.Contains("optional driver stopped"))faults++;
        Check(faults==1,"optional driver fault emits one bounded report without a per-frame flood");
        PerfConfig.ThrowOnRead=false;ScenarioFigureDetailBudget.Shutdown();
        return checks;
    }
}
