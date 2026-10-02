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
    private static Scene actorScene;
    private static GameObject board=null!;
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
        // Mirror the supplied Choreographer.SetActor creation path: native model roots are
        // under the main Game board, with metadata beneath the model Animator. Additive
        // ProcGen owns scenery, never the native figure root. This must be real Unity scene
        // membership, not a stubbed name or a direct private Adopt call.
        bool scenario = scene.name == "ProcGen";
        SceneManager.MoveGameObjectToScene(figure.Root,scenario ? actorScene : scene);
        if(scenario)figure.Root.transform.SetParent(board.transform);
        // The native actor component can be a sibling of the actual model/LODGroup.
        figure.Actor=Node(figure.Root.transform,"Actor metadata").AddComponent<ActorBehaviour>();
        ActorBehaviour.SetActor(figure.Root,new CActor { Type=type });
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
        typeof(ActorBehaviour_SetActor_FigureDetailPatch).GetMethod("Postfix",BindingFlags.Static|BindingFlags.NonPublic)!
            .Invoke(null,new object[]{figure.Root});
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
        actorScene=SceneManager.GetSceneByName("Game");
        if(!actorScene.IsValid())actorScene=SceneManager.CreateScene("Game");
        board=Node(null,"Native client scenario board");SceneManager.MoveGameObjectToScene(board,actorScene);
        ClientScenarioManager.s_ClientScenarioManager=board.AddComponent<ClientScenarioManager>();
        ClientScenarioManager.s_ClientScenarioManager.m_Board=board;
        var host=Node(null,"Figure budget host");ScenarioFigureDetailBudget.Install(host);
        driver=host.GetComponent(Budget.GetNestedType("Driver",BindingFlags.NonPublic));
        var hero=Build(scenario,CActor.EType.Player);var enemy=Build(scenario,CActor.EType.Enemy);
        Check(hero.Root.scene==actorScene && hero.Root.scene!=scenario,
            "native actor creation retains real Game scene beneath board while ProcGen owns scenery");
        Check(hero.Actor.gameObject!=hero.Root && hero.Actor.Actor.Type==CActor.EType.Player,
            "completed native SetActor binds child metadata to original model root");
        Tick();Check(Original(hero)&&Original(enemy),"100 percent preserves original native LOD table");
        var recorded=(System.Collections.ICollection)driver.GetType().GetField("_actors",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(driver)!;
        Check(recorded.Count==0,"original settings bypass all actor discovery and per-frame native reflection");
        HeldFigures.Held=hero.Actor;hero.Root.transform.SetParent(null);
        PerfConfig.PlayerFigureDetailPercent=0;PerfConfig.EnemyFigureDetailPercent=100;
        PerfConfig.FigureClothSimulationEnabled=false;Tick();
        Check(Capped(hero,2)&&Original(enemy),"player detail controls only player native LODs");
        Check(hero.Root.transform.parent==null&&!hero.Cloth.enabled,
            "quality activation discovers an exact original held actor already detached from board");
        HeldFigures.Held=null;hero.Root.transform.SetParent(board.transform);
        Check(!hero.Cloth.enabled&&!enemy.Cloth.enabled,"original enabled native cloth solvers obey separate compromise");
        Check(hero.Animator.enabled&&enemy.Animator.enabled&&hero.Collider.enabled,"LOD compromise preserves native animation and collision");
        foreach(Renderer renderer in hero.Meshes)Check(renderer.enabled,"native renderer enabled flags stay intact");
        Check(hero.Meshes[0].forceRenderingOff&&hero.Meshes[1].forceRenderingOff&&!hero.Meshes[2].forceRenderingOff,
            "orphaned fine meshes are masked while the original coarse body remains visible");
        LOD[] applied=hero.Group.GetLODs();
        for(int i=0;i<3;i++)Check(applied[i].screenRelativeTransitionHeight==hero.Original[i].screenRelativeTransitionHeight
            &&applied[i].fadeTransitionWidth==hero.Original[i].fadeTransitionWidth,"native transitions and far culling survive the cap");
        PerfConfig.EnemyFigureDetailPercent=0;Tick();Check(Capped(enemy,2),"enemy detail independently selects actual original coarse meshes");
        HeldFigures.Held=hero.Actor;Tick();Check(Capped(hero,2)&&!hero.Cloth.enabled,"local hold keeps configured native detail and cloth solver OFF");
        Check(VRLog.Messages.Exists(message=>message.Contains("original/selected near-mesh vertices 24/8")),
            "bounded Debug summary reports cached original/selected near-mesh cost without claiming far-view savings");
        HeldFigures.Held=null;Tick();Check(Capped(hero,2)&&!hero.Cloth.enabled,"local release resumes saved quality compromise");
        NetHeldFigures.Held=enemy.Actor;Tick();Check(Capped(enemy,2)&&!enemy.Cloth.enabled,"remote hold keeps configured native detail and cloth solver OFF");
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
        var unrelated=Build(actorScene,CActor.EType.Player);Tick();
        Check(Original(unrelated)&&unrelated.Cloth.enabled,
            "unrelated Game-scene models outside exact native board ancestry stay intact");
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
        Check(!hero.Cloth.enabled,"native cloth teleport reset stays disabled while setting is OFF");
        PerfConfig.FigureClothSimulationEnabled=true;Tick();
        Check(!hero.Cloth.enabled,"native cloth teleport reset cannot be cancelled by setting restoration");
        PerfConfig.FigureClothSimulationEnabled=false;Tick();
        hero.Actor.ForceNativePosition(0);hero.Cloth.enabled=true;Tick();
        Check(!hero.Cloth.enabled,"native cloth re-enable during a remote hold is suppressed while simulation is OFF");
        hero.Root.transform.position=new Vector3(5,2,3);hero.Root.transform.rotation=Quaternion.Euler(35,75,15);
        hero.Root.transform.localScale=Vector3.one*2;hero.Cloth.enabled=true;Tick();
        Check(!hero.Cloth.enabled,"held motion rotation and scale cannot revive the disabled cloth solver");
        NetHeldFigures.Held=null;Tick();Check(!hero.Cloth.enabled,"quality compromise resumes after native reset and hold");
        PerfConfig.FigureClothSimulationEnabled=true;Tick();Check(hero.Cloth.enabled&&enemy.Cloth.enabled,"cloth setting restores only previously enabled native solvers");
        var disabled=Build(scenario,CActor.EType.Enemy);disabled.Cloth.enabled=false;
        PerfConfig.FigureClothSimulationEnabled=false;Tick();PerfConfig.FigureClothSimulationEnabled=true;Tick();
        Check(!disabled.Cloth.enabled,"an originally disabled cloth solver must not be enabled by settings");
        int lateOrder=driver.GetType().GetCustomAttribute<DefaultExecutionOrder>()!.order;
        Check(lateOrder>20000&&lateOrder<30000,"cloth OFF enforces after native writers inside the measured logic phase");
        PerfConfig.FigureClothSimulationEnabled=false;
        var suspended=Build(scenario,CActor.EType.Player);suspended.Cloth.enabled=false;
        FigureCloth.DisabledClaims.Add(suspended.Cloth);Tick();
        Check(!suspended.Cloth.enabled && FigureCloth.DisabledClaims.Count==0,
            "rescale cook original-enable claim transfers once without simulating while OFF");
        PerfConfig.FigureClothSimulationEnabled=true;Tick();
        Check(suspended.Cloth.enabled,"formerly enabled cook-down cloth resumes original simulation on ON");
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
        var wrongBoard=Node(null,"Anomaly control: replacement board has no model children");
        SceneManager.MoveGameObjectToScene(wrongBoard,actorScene);
        ClientScenarioManager.s_ClientScenarioManager.m_Board=wrongBoard;
        var anomalyHost=Node(null,"Bounded anomaly reporting host");ScenarioFigureDetailBudget.Install(anomalyHost);
        driver=anomalyHost.GetComponent(Budget.GetNestedType("Driver",BindingFlags.NonPublic));
        for(int i=0;i<12;i++)Tick();
        int missing=0;foreach(string message in VRLog.Messages)if(message.Contains("adopted none after loading"))missing++;
        Check(missing==1,"confirmed native board scope failure has one bounded normal-level report");
        ScenarioFigureDetailBudget.Shutdown();ClientScenarioManager.s_ClientScenarioManager.m_Board=board;
        return checks;
    }
}
