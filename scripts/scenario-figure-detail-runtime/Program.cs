using System;
using System.IO;
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
    private static bool AdmittedMesh(Renderer renderer)
    {
        var records=(System.Collections.IEnumerable)driver.GetType().GetField("_actors",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(driver)!;
        foreach(object record in records)
        {
            var slots=(System.Collections.IEnumerable)record.GetType().GetField("MeshDetails",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)!.GetValue(record)!;
            foreach(ScenarioFigureMeshBank.Record slot in slots)if(slot.Renderer==renderer)return true;
        }
        return false;
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
    private static Material Solid(Color color,string name)
    {
        var shader=Shader.Find("Unlit/Color");
        if(shader==null)throw new Exception("Real Unity Unlit/Color shader required for render proof");
        return new Material(shader) { color=color,name=name };
    }
    private static SkinnedMeshRenderer FlatBody(Transform parent,string name,Vector3 position,Material material)
    {
        var node=Node(parent,name);node.layer=25;node.transform.localPosition=position;
        var mesh=new Mesh { name=name };
        mesh.vertices=new[]{new Vector3(-.4f,-.4f,0),new Vector3(.4f,-.4f,0),new Vector3(.4f,.4f,0),new Vector3(-.4f,.4f,0)};
        mesh.triangles=new[]{0,2,1,0,3,2};mesh.boneWeights=new[]{new BoneWeight { boneIndex0=0,weight0=1 },new BoneWeight { boneIndex0=0,weight0=1 },new BoneWeight { boneIndex0=0,weight0=1 },new BoneWeight { boneIndex0=0,weight0=1 }};
        mesh.bindposes=new[]{Matrix4x4.identity};mesh.RecalculateBounds();
        var renderer=node.AddComponent<SkinnedMeshRenderer>();renderer.sharedMesh=mesh;
        renderer.bones=new[]{node.transform};renderer.rootBone=node.transform;renderer.sharedMaterial=material;
        renderer.localBounds=new Bounds(Vector3.zero,Vector3.one*2);return renderer;
    }
    private static ParticleSystem StaticParticle(Transform parent,string name,Vector3 position,Color color,bool playing=true)
    {
        var node=Node(parent,name);node.layer=25;var system=node.AddComponent<ParticleSystem>();
        system.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=system.main;main.loop=true;main.playOnAwake=false;main.startLifetime=1000;
        main.startSpeed=0;main.startSize=.65f;main.simulationSpace=ParticleSystemSimulationSpace.World;
        var emission=system.emission;emission.enabled=false;
        system.GetComponent<ParticleSystemRenderer>().sharedMaterial=Solid(color,"Fixture particle "+name);
        if(playing)
        {
            system.Play(false);
            system.Emit(new ParticleSystem.EmitParams { position=position,startLifetime=1000,startSize=.65f,startColor=color },1);
        }
        return system;
    }
    private static Color[] Render(Camera camera,string suffix)
    {
        var target=new RenderTexture(128,128,16,RenderTextureFormat.ARGB32);
        camera.targetTexture=target;camera.Render();RenderTexture.active=target;
        var pixels=new Texture2D(128,128,TextureFormat.RGBA32,false);pixels.ReadPixels(new Rect(0,0,128,128),0,0);pixels.Apply();
        var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"-interactionManifest");
        if(index>=0)File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(args[index+1])!,typeof(InteractionProgram).Assembly.GetName().Name+"-"+suffix+".png"),pixels.EncodeToPNG());
        Color[] result=pixels.GetPixels();RenderTexture.active=null;camera.targetTexture=null;
        UnityEngine.Object.DestroyImmediate(pixels);UnityEngine.Object.DestroyImmediate(target);return result;
    }
    private static int Colored(Color[] pixels,Color color)
    {
        int count=0;foreach(Color pixel in pixels)
            if(Mathf.Abs(pixel.r-color.r)<.1f&&Mathf.Abs(pixel.g-color.g)<.1f&&Mathf.Abs(pixel.b-color.b)<.1f)count++;
        return count;
    }
    private static void MaterialReady(MaterialLoaderData data)
    {
        typeof(MaterialLoaderData_CheckAllMaterialLoaded_FigureEffectsPatch).GetMethod("Postfix",BindingFlags.Static|BindingFlags.NonPublic)!
            .Invoke(null,new object[]{data});
    }
    private static void AllGameEffectsProof(Scene scenario,Camera camera)
    {
        var created=new System.Collections.Generic.List<GameObject>();
        // These model/effect identities come from independently exported prefab metadata;
        // they are not read from the production dictionary. Different hierarchy families
        // cover heroes, bosses, ordinary/elite monsters and summons beyond the old demons.
        string[,] identities={
            {"MO_LivingSpirit_PR","P_Living_Spirit_Idle (1)"},
            {"MO_LivingSpirit_Elite_PR","P_Living_Spirit_Idle (1)"},
            {"HE_Elementalist_PR","Elementalist_Idle_FX"},
            {"MO_SavvasIceStorm_PR","Savvas_Icestorm_Idle_FX"},
            {"MO_Ooze_PR","P_Ooze_Idle"},
            {"MO_PrimeDemon_PR","PrimeDemon_IdleFX"},
            {"MO_TheGloom_PR","P_TheGloom_Idle"},
            {"MO_Harrower_Infester_Elite_PR","P_HarrowerInfester_Idle (1)"},
            {"SU_HealingSprite_PR","P_HealingSprite"},
            {"SU_ManaSphere_PR","ManaSphere_FX"},
            {"SU_PlagueRat_PR","p_PlagueRat_Idle"},
        };
        for(int i=0;i<identities.GetLength(0);i++)
        {
            var figure=Build(scenario,CActor.EType.Enemy,false,false);
            created.Add(figure.Root);
            var model=Node(figure.Root.transform,identities[i,0]+"(Clone)");
            var idle=Node(model.transform,identities[i,1]);
            var nativeBody=FlatBody(model.transform,"Native original core",new Vector3(15,0,0),Solid(Color.green,"Body"));
            var ambient=StaticParticle(idle.transform,"Fog",new Vector3(15,0,0),Color.red);
            var combat=StaticParticle(idle.transform,"P_GainStrengthen",new Vector3(15,0,0),Color.blue);
            var light=Node(idle.transform,"Native unchanged light").AddComponent<Light>();light.intensity=.12f;
            Tick();
            for(int frame=0;frame<64&&ScenarioFigureDetailBudget.IsPreparingPresentation;frame++)Tick();
            Check(ambient.isPaused&&ambient.GetComponent<ParticleSystemRenderer>().forceRenderingOff,
                "complete original prefab family suppresses resident cosmetics: "+identities[i,0]);
            Check(combat.isPlaying&&!combat.isPaused&&!combat.GetComponent<ParticleSystemRenderer>().forceRenderingOff,
                "all-game effect catalog never captures pooled combat under resident cosmetics: "+identities[i,0]);
            Check(!nativeBody.forceRenderingOff&&light.enabled&&Mathf.Approximately(light.intensity,.12f),
                "all-game effect compromise retains every core silhouette and original light");
            if(i<2)
            {
                var meshNode=Node(idle.transform,"geo_bendyband");
                meshNode.layer=25;meshNode.transform.position=new Vector3(3,2,0);
                var quad=new Mesh { name="geo_bendyband" };
                quad.vertices=new[]{new Vector3(-.4f,-.4f,0),new Vector3(.4f,-.4f,0),new Vector3(.4f,.4f,0),new Vector3(-.4f,.4f,0)};
                quad.triangles=new[]{0,2,1,0,3,2};quad.RecalculateBounds();
                meshNode.AddComponent<MeshFilter>().sharedMesh=quad;
                var band=meshNode.AddComponent<MeshRenderer>();
                band.sharedMaterial=Solid(Color.magenta,i==0?"LivingSpirit_EyeBand_Mat":"LivingSpirit_Elite_EyeBand_Mat");
                Check(Colored(Render(camera,"spirit"+i+"-mesh-before"),Color.magenta)>100,
                    "real Unity camera draws original-identity LivingSpirit mesh cosmetics before admission");
                MaterialReady(new MaterialLoaderData { Renderer=band });Tick();
                Check(band.forceRenderingOff,"LivingSpirit original mesh eye bands obey zero density after late material completion");
                Check(Colored(Render(camera,"spirit"+i+"-mesh-zero"),Color.magenta)==0,
                    "LivingSpirit mesh cosmetic admission suppresses actual pixels at zero density");
                var foreign=Node(idle.transform,"geo_bendyband (1)");foreign.AddComponent<MeshFilter>().sharedMesh=Mesh(4);
                var unverified=foreign.AddComponent<MeshRenderer>();unverified.sharedMaterial=Solid(Color.magenta,"Unverified body material");
                MaterialReady(new MaterialLoaderData { Renderer=unverified });Tick();
                Check(!unverified.forceRenderingOff,"LivingSpirit mesh name alone cannot suppress unknown material/body geometry");
                PerfConfig.FigureEffectsDensityPercent=100;Tick();
                Check(!band.forceRenderingOff&&ambient.isPlaying,"LivingSpirit mesh and native particle state restore at original quality");
                PerfConfig.FigureEffectsDensityPercent=0;Tick();
                for(int frame=0;frame<64&&ScenarioFigureDetailBudget.IsPreparingPresentation;frame++)Tick();
            }
        }
        foreach(GameObject root in created)UnityEngine.Object.DestroyImmediate(root);
        Tick();
    }
    private static void EffectsProof(Scene scenario,Scene map)
    {
        PerfConfig.PlayerFigureDetailPercent=100;PerfConfig.EnemyFigureDetailPercent=100;
        PerfConfig.FigureClothSimulationEnabled=true;PerfConfig.FigureEffectsDensityPercent=100;
        var host=Node(null,"Ambient effects render host");ScenarioFigureDetailBudget.Install(host);
        driver=host.GetComponent(Budget.GetNestedType("Driver",BindingFlags.NonPublic));Tick();
        var figure=Build(scenario,CActor.EType.Enemy,false,false);
        figure.Root.name="9d8f7c61-native-actor-guid";UnityEngine.Object.DestroyImmediate(figure.Group);
        foreach(Renderer renderer in figure.Meshes)renderer.enabled=false;
        var model=Node(figure.Root.transform,"MO_WindDemon_PR(Clone)");
        var body=FlatBody(model.transform,"MO_WindDemon_main",Vector3.zero,Solid(Color.green,"MO_WindDemon_MAT"));
        var alpha=FlatBody(model.transform,"MO_WindDemon_Alpha",new Vector3(0,2,0),Solid(Color.magenta,"MO_WindDemon_Alpha_MAT"));
        // Match Choreographer.CreateCharacterActor: material instancing happens after the
        // addressable child is created beneath an outer wrapper named by the actor GUID.
        alpha.sharedMaterial=new Material(alpha.material);Material originalAlpha=alpha.sharedMaterial;
        Check(originalAlpha.name.EndsWith("(Instance)",StringComparison.Ordinal),
            "real native renderer material access creates Unity instance provenance suffix");
        Mesh originalBody=body.sharedMesh;
        var idle=Node(model.transform,"P_WindDemon_Idle");
        var ambient=StaticParticle(idle.transform,"Fog",new Vector3(-2,0,0),Color.red);
        var extra=StaticParticle(idle.transform,"Particle System (4)",new Vector3(-2,-2,0),Color.red);
        var initiallyPaused=StaticParticle(idle.transform,"Bits (3)",new Vector3(9,0,0),Color.red);initiallyPaused.Pause(false);
        var initiallyStopped=StaticParticle(idle.transform,"Cloud (3)",new Vector3(9,0,0),Color.red,false);
        var foreignMask=StaticParticle(idle.transform,"Bits (4)",new Vector3(9,0,0),Color.red);
        foreignMask.GetComponent<ParticleSystemRenderer>().forceRenderingOff=true;
        var trailNode=Node(idle.transform,"Ambient trail");trailNode.layer=25;var trail=trailNode.AddComponent<TrailRenderer>();trail.emitting=true;
        var condition=StaticParticle(idle.transform,"P_Invisibility_Idle",new Vector3(2,0,0),Color.blue);
        var pooledAbility=StaticParticle(idle.transform,"P_GainStrengthen",new Vector3(9,0,0),Color.blue);
        var attack=StaticParticle(figure.Root.transform,"P_WindDemon_Ranged_BuildUp",new Vector3(2,-2,0),Color.blue);
        var callback=StaticParticle(idle.transform,"Callback cosmetic",new Vector3(9,0,0),Color.blue);
        var callbackMain=callback.main;callbackMain.stopAction=ParticleSystemStopAction.Callback;
        var collision=StaticParticle(idle.transform,"Collision cosmetic",new Vector3(9,0,0),Color.blue);
        var collisionModule=collision.collision;collisionModule.enabled=true;
        var ui=Node(idle.transform,"Card UI");ui.AddComponent<Canvas>();
        var card=StaticParticle(ui.transform,"Card plume",new Vector3(9,0,0),Color.blue);
        var foreignBody=FlatBody(figure.Root.transform,"Unverified Alpha body",new Vector3(9,0,0),Solid(Color.green,"Unknown"));
        var mapFigure=Build(map,CActor.EType.Enemy,false,false);mapFigure.Root.name="MO_WindDemon_PR";
        var mapIdle=Node(mapFigure.Root.transform,"P_WindDemon_Idle");var mapParticle=StaticParticle(mapIdle.transform,"Fog",new Vector3(9,0,0),Color.blue);
        var camera=Node(null,"Actual particle render camera").AddComponent<Camera>();camera.transform.position=new Vector3(0,0,-10);
        camera.orthographic=true;camera.orthographicSize=4;camera.cullingMask=1<<25;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
        Color[] before=Render(camera,"fx100-before");
        Check(Colored(before,Color.red)>100&&Colored(before,Color.magenta)>100&&Colored(before,Color.green)>100&&Colored(before,Color.blue)>100,
            "real graphics device draws original ambient particles shell body and combat signals");
        // MaterialLoader.Start can defer the original alpha assignment until after SetActor.
        // A missing material at discovery must not permanently exclude its authored shell.
        alpha.sharedMaterial=null;alpha.enabled=false;var alphaData=new MaterialLoaderData { Renderer=alpha };
        PerfConfig.FigureEffectsDensityPercent=0;for(int i=0;i<24;i++)Tick();
        Check(ambient.isPaused&&extra.isPaused&&!trail.emitting,"FX-only activation pauses identified ambient solvers and trail emission");
        Check(!alpha.forceRenderingOff&&!alpha.enabled,"pending alpha material retains native disabled state before genuine completion");
        alphaData.Complete(new[]{originalAlpha});MaterialReady(alphaData);Tick();
        Check(alpha.enabled&&alpha.forceRenderingOff,"late native alpha material completion admits original instanced shell without overwriting enabled state");
        MaterialReady(alphaData);Tick();
        Check(alpha.forceRenderingOff,"duplicate native material completion keeps one owned optional shell");
        Check(!condition.isPaused&&condition.isPlaying,"pooled invisibility condition under Idle remains playing");
        Check(!pooledAbility.isPaused&&pooledAbility.isPlaying,"pooled ability under Idle remains playing despite generic ambient ancestry");
        Check(!attack.isPaused&&attack.isPlaying&&!attack.GetComponent<ParticleSystemRenderer>().forceRenderingOff,
            "looping native attack buildup remains fully original");
        Check(!callback.isPaused&&!collision.isPaused&&!card.isPaused,"native callbacks collision and card UI are excluded from ambient suppression");
        Check(!mapParticle.isPaused&&!mapParticle.GetComponent<ParticleSystemRenderer>().forceRenderingOff,
            "map and NPC effects stay outside exact native scenario actor scope");
        Check(body.sharedMesh==originalBody&&!body.forceRenderingOff&&!foreignBody.forceRenderingOff,
            "core body without authored LOD stays rendered with its original mesh");
        Check(figure.Animator.enabled&&figure.Collider.enabled,"effects reduction preserves native animator and picking collider");
        Color[] reduced=Render(camera,"fx0");
        Check(Colored(reduced,Color.red)==0,"ambient particles produce no rendered pixels at zero density");
        Check(Colored(reduced,Color.magenta)==0,"identified supplemental alpha shell produces no rendered pixels at zero density");
        Check(Colored(reduced,Color.green)==Colored(before,Color.green)&&Colored(reduced,Color.blue)==Colored(before,Color.blue),
            "actual opaque body and gameplay effect pixels survive zero density unchanged");
        Check(VRLog.Messages.Exists(message=>message.Contains("last-frame visible unmasked body meshes/vertices")),
            "visible mesh diagnostics explicitly describe any-camera last-frame evidence rather than projected LOD savings");
        Check(VRLog.Messages.Exists(message=>message.Contains("Ambient FX density=0%; 8 owned renderer(s) masked, 3 particle solver(s) paused")),
            "bounded Debug evidence counts actual optional renderer masks and paused solvers: "+string.Join(" | ",VRLog.Messages.FindAll(message=>message.Contains("Ambient FX density=0%"))));
        Check(callback.GetComponent<ParticleSystemRenderer>().forceRenderingOff
              && collision.GetComponent<ParticleSystemRenderer>().forceRenderingOff
              && callback.isPlaying && collision.isPlaying,
            "authored cosmetics with callbacks and collisions suppress only pixels while native side effects keep running");
        var lateAmbient=StaticParticle(idle.transform,"Late native ambient fog",new Vector3(9,0,0),Color.red);
        var lateRenderer=lateAmbient.GetComponent<ParticleSystemRenderer>();lateRenderer.enabled=false;
        var lateData=new MaterialLoaderData { Renderer=lateRenderer };MaterialReady(lateData);
        Check(!lateAmbient.isPaused&&!lateRenderer.forceRenderingOff,"unfinished native material load cannot adopt late original effects");
        lateData.Complete(new[]{lateRenderer.sharedMaterial});MaterialReady(lateData);Tick();
        Check(lateAmbient.isPaused&&lateRenderer.enabled&&lateRenderer.forceRenderingOff,"completed late original Idle particles are adopted from exact native actor ancestry");
        var lateAttack=StaticParticle(idle.transform,"P_WindDemon_Ranged_BuildUp_Late",new Vector3(9,0,0),Color.blue);
        MaterialReady(new MaterialLoaderData { Renderer=lateAttack.GetComponent<ParticleSystemRenderer>() });Tick();
        Check(lateAttack.isPlaying&&!lateAttack.isPaused&&!lateAttack.GetComponent<ParticleSystemRenderer>().forceRenderingOff,
            "late pooled attack effect remains original through the shared material completion seam");
        foreach(string kind in new[]{"Wind","Sun","Flame"})
        {
            var elite=Build(scenario,CActor.EType.Enemy,false,false);elite.Root.name="Elite actor GUID "+kind;
            UnityEngine.Object.DestroyImmediate(elite.Group);foreach(Renderer mesh in elite.Meshes)mesh.enabled=false;
            var eliteModel=Node(elite.Root.transform,"MO_"+kind+"Demon_Elite_PR(Clone)");
            Transform carrier=kind=="Sun"?eliteModel.transform:Node(eliteModel.transform,"MO_"+kind+"Demon_Elite").transform;
            string bodyName=kind=="Wind"?"MO_WindDemon_Elite_main":kind=="Sun"?"MO_SunDemon_Elite_Mesh":"MO_FlameDemon_Mesh";
            var eliteBody=FlatBody(carrier,bodyName,new Vector3(9,0,0),Solid(Color.green,"MO_"+kind+"Demon_Elite_MAT"));
            SkinnedMeshRenderer? eliteAlpha=null;
            if(kind!="Sun")eliteAlpha=FlatBody(carrier,kind=="Wind"?"MO_WindDemon_Elite_Alpha":"MO_FlameDemon_Alpha",new Vector3(9,0,0),Solid(Color.magenta,"MO_"+kind+"Demon_Elite_Alpha_MAT"));
            var retained=FlatBody(carrier,kind=="Sun"?"MO_SunDemon_Elite_Halo":"MO_FlameDemon_Elite_Horns",new Vector3(9,0,0),Solid(Color.green,"MO_"+kind+"Demon_Elite_MAT"));
            var eliteIdle=Node(eliteModel.transform,kind=="Flame"?"P_FlameDemon":"P_"+kind+"Demon_Idle");
            var eliteParticle=StaticParticle(eliteIdle.transform,"Fog",new Vector3(9,0,0),Color.red);
            for(int i=0;i<3;i++)Tick();
            Check(eliteParticle.isPaused&&eliteParticle.GetComponent<ParticleSystemRenderer>().forceRenderingOff,
                "verified "+kind+" elite model retains exact authored ambient effect policy");
            Check((eliteAlpha==null||eliteAlpha.forceRenderingOff)&&!eliteBody.forceRenderingOff&&!retained.forceRenderingOff,
                "verified "+kind+" elite alpha is optional while original body horns and halo remain rendered");
        }
        AllGameEffectsProof(scenario,camera);
        HeldFigures.Held=figure.Actor;figure.Root.transform.SetParent(null);Tick();
        Check(ambient.isPaused&&alpha.forceRenderingOff,"local hold retains zero ambient density without reintroducing cosmetics");
        HeldFigures.Held=null;NetHeldFigures.Held=figure.Actor;Tick();
        Check(ambient.isPaused&&alpha.forceRenderingOff,"remote hold retains zero ambient density without reintroducing cosmetics");
        NetHeldFigures.Held=null;figure.Root.transform.SetParent(board.transform);
        PerfConfig.FigureEffectsDensityPercent=50;Tick();
        Check(ambient.isPlaying&&extra.isPlaying&&alpha.forceRenderingOff&&!trail.emitting,
            "intermediate density retains a stable subset of authored ambient systems");
        PerfConfig.FigureEffectsDensityPercent=100;Tick();
        Check(ambient.isPlaying&&extra.isPlaying&&trail.emitting&&!alpha.forceRenderingOff,
            "100 percent restores only owned native ambient simulation trail and shell");
        Check(!initiallyStopped.isPlaying&&initiallyPaused.isPaused,"originally stopped and paused effects remain in their original states at 100 percent");
        Check(lateAmbient.isPlaying&&!lateRenderer.forceRenderingOff&&alpha.sharedMaterial==originalAlpha,
            "100 percent restores late adopted effects and exact native instance materials");
        Check(foreignMask.GetComponent<ParticleSystemRenderer>().forceRenderingOff,"originally foreign renderer mask survives ambient restoration");
        Color[] restored=Render(camera,"fx100-restored");
        Check(Colored(restored,Color.red)==Colored(before,Color.red)&&Colored(restored,Color.magenta)==Colored(before,Color.magenta),
            "100 percent restores actual ambient and alpha rendered pixels");
        PerfConfig.FigureEffectsDensityPercent=0;for(int i=0;i<24;i++)Tick();
        VRSession.IsRunning=false;Tick();
        Check(!ambient.GetComponent<ParticleSystemRenderer>().forceRenderingOff&&!alpha.forceRenderingOff&&ambient.isPlaying,
            "VR off restores owned ambient particle renderers and simulation");
        VRSession.IsRunning=true;for(int i=0;i<24;i++)Tick();
        Check(ambient.isPaused&&alpha.forceRenderingOff,"VR re-entry re-adopts original static ambient parts");
        Choreographer.s_Choreographer.m_ProcGenScene=default;SceneManager.SetActiveScene(map);Tick();
        Check(!ambient.isPaused&&!alpha.forceRenderingOff,"scene exit restores owned ambient parts");
        Choreographer.s_Choreographer.m_ProcGenScene=scenario;for(int i=0;i<24;i++)Tick();
        Check(ambient.isPaused,"scene re-entry captures original ambient presentation anew");
        ScenarioFigureDetailBudget.Shutdown();Check(ambient.isPlaying&&!alpha.forceRenderingOff,"shutdown restores owned ambient presentation");
        PerfConfig.FigureEffectsDensityPercent=100;UnityEngine.Object.DestroyImmediate(camera.gameObject);
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
        Mesh originalClothMesh=Mesh(1204);originalClothMesh.name="Original 1204-vertex solver topology";
        hero.Cloth.GetComponent<SkinnedMeshRenderer>().sharedMesh=originalClothMesh;
        var exactBody=Node(hero.Root.transform,"Original body with no authored LOD").AddComponent<SkinnedMeshRenderer>();
        exactBody.sharedMesh=Mesh(1204);exactBody.sharedMesh.name="Native original body";
        var exactWeapon=Node(hero.Root.transform,"Native weapon").AddComponent<SkinnedMeshRenderer>();
        exactWeapon.sharedMesh=Mesh(1204);exactWeapon.sharedMesh.name="WP_Original weapon";
        var mirrorBody=Node(hero.Root.transform,"VR_existing-home-twin").AddComponent<SkinnedMeshRenderer>();
        mirrorBody.sharedMesh=exactBody.sharedMesh;
        HeldFigures.Held=hero.Actor;hero.Root.transform.SetParent(null);
        PerfConfig.PlayerFigureDetailPercent=0;PerfConfig.EnemyFigureDetailPercent=100;
        PerfConfig.FigureClothSimulationEnabled=false;
        FigureClock.Now+=2f;
        driver.GetType().GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(driver,null);
        Check(!ScenarioFigureDetailBudget.MeasurementReady,
            "new figure state cannot become measurement-ready before its late pass");
        driver.GetType().GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(driver,null);
        Check(ScenarioFigureDetailBudget.MeasurementReady,
            "completed native figure late pass publishes readiness without a renderer census");
        Check(Capped(hero,2)&&Original(enemy),"player detail controls only player native LODs");
        Check(AdmittedMesh(exactBody)&&!AdmittedMesh(exactWeapon)&&!AdmittedMesh(mirrorBody),
            "only original native body is admitted beside excluded weapon and pre-existing mirror");
        Check(!AdmittedMesh(hero.Cloth.GetComponent<SkinnedMeshRenderer>())&&hero.Cloth.GetComponent<SkinnedMeshRenderer>().sharedMesh==originalClothMesh,
            "large native Cloth topology is never admitted even while solver is OFF");
        int coefficients=hero.Cloth.coefficients.Length;
        PerfConfig.FigureClothSimulationEnabled=true;Tick();
        Check(hero.Cloth.enabled&&!AdmittedMesh(hero.Cloth.GetComponent<SkinnedMeshRenderer>())
            &&hero.Cloth.GetComponent<SkinnedMeshRenderer>().sharedMesh==originalClothMesh&&hero.Cloth.coefficients.Length==coefficients,
            "Cloth ON restores its solver without changing original mesh or coefficients");
        PerfConfig.FigureClothSimulationEnabled=false;Tick();
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
        EffectsProof(scenario,map);
        return checks;
    }
}
