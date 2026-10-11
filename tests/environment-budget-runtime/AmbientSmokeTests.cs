using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;

public static partial class EnvironmentProgram
{
    private static void DecorativeSmokeCoverage()
    {
        using var room = new Room();
        var admitted = new List<ParticleSystem>();
        foreach (string name in new[] { "p_fire_torch (8)", "p_fire_torch (9)(Clone)",
            "p_fire_torch_blue (1) (Instance)", "p_fire_torch_Demon_01", "P_SewerFog" })
            admitted.Add(room.Particle(name));
        var family = Room.Child("p_fire_torch (8)", room.Generated.transform);
        var alphaSmoke = room.Particle("Smoke", family.transform);
        alphaSmoke.GetComponent<ParticleSystemRenderer>().sharedMaterial = room.Original;
        admitted.Add(alphaSmoke);
        var doorway = Room.Child("Door.Fire", room.Generated.transform);
        doorway.AddComponent<ProceduralDoorway>(); doorway.AddComponent<Animator>();
        var doorSmoke = room.Particle("p_fire_torch (8)", doorway.transform);
        var prop = Room.Child("Decoration.Fire", room.Generated.transform);
        prop.AddComponent<ProceduralProp>(); prop.AddComponent<CInteractable>();
        var propSmoke = room.Particle("p_fire_torch_blue", prop.transform);
        admitted.Add(doorSmoke); admitted.Add(propSmoke);
        var torchLight = family.AddComponent<Light>();
        torchLight.intensity = 1.7f; torchLight.range = 3.2f;
        var water = room.Surface("WaterSurface", room.Generated.transform);
        var combatRoot = Room.Child("P_attack_Fire",room.Generated.transform);
        var pooledTorch = room.Particle("p_fire_torch",combatRoot.transform);
        var combatFog = room.Particle("Fog (2)",combatRoot.transform,loop:false);
        var condition = room.Particle("p_condition_burning",family.transform);
        var unknownSuffix = room.Particle("p_fire_torch (attack)");
        var canvas = Room.Child("Canvas.Fire",room.Generated.transform);canvas.AddComponent<Canvas>();
        var canvasSmoke = room.Particle("p_fire_torch",canvas.transform);
        var actor = Room.Child("Actor.Fire",room.Generated.transform);actor.AddComponent<ActorBehaviour>();
        var actorSmoke = room.Particle("p_fire_torch (8)",actor.transform);
        var foreignSceneSmoke = room.Particle("p_fire_torch",room.ForeignSceneTile().transform);
        Configure(false,false,0);ScenarioEnvironmentBudget.BeforeLoadingComplete();
        foreach (var effect in admitted)
            if(effect!=doorSmoke && effect!=propSmoke)
                Check(effect.isPaused,"authored numbered torch family is paused at zero environment budget");
        Check(doorSmoke.isPaused && propSmoke.isPaused,
            "decorative doorway and prop smoke uses emitter scope independent of static mesh vetoes");
        bool allMasked=false;
        room.ObserveRender=()=>
        {
            allMasked=true;
            foreach(var effect in admitted)allMasked &= effect.GetComponent<ParticleSystemRenderer>().forceRenderingOff;
            Check(alphaSmoke.GetComponent<ParticleSystemRenderer>().forceRenderingOff,
                "decorative smoke masking is independent of its alpha additive or distortion shader");
            Check(!pooledTorch.GetComponent<ParticleSystemRenderer>().forceRenderingOff && pooledTorch.isPlaying,
                "pooled combat parent vetoes even an exact decorative torch leaf");
            foreach(var effect in new[]{combatFog,condition,unknownSuffix,canvasSmoke,actorSmoke,foreignSceneSmoke})
                Check(!effect.GetComponent<ParticleSystemRenderer>().forceRenderingOff && effect.isPlaying,
                    "combat condition figure UI unknown suffix and foreign scenario effects remain native");
            Check(torchLight.enabled && torchLight.intensity==1.7f && torchLight.range==3.2f,
                "zero decorative smoke budget preserves native torch light properties");
            Check(water.enabled && !water.forceRenderingOff && water.sharedMaterial==room.Original,
                "zero decorative smoke budget preserves water geometry and material");
        };
        room.Render();room.ObserveRender=null;
        Check(allMasked,"zero environment budget masks every admitted original decorative family during actual camera rendering");
        foreach(var effect in admitted)
            Check(!effect.GetComponent<ParticleSystemRenderer>().forceRenderingOff,
                "post render recovery removes decorative smoke masks before native cloning");
        var late = room.Particle("p_fire_torch_blue (3)",doorway.transform);
        ScenarioEnvironmentBudget.MaterialReady(late.GetComponent<ParticleSystemRenderer>());
        Check(late.isPaused,"late material readiness adopts decorative smoke below native doorway mesh exclusions");
        admitted.Add(late);
        // A native stop is not our pause anymore. Restoration must not restart it.
        var stopped=admitted[0];stopped.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
        Configure(false,false,100);Tick();
        Check(stopped.isStopped && !stopped.isPlaying,"ambient restoration respects a native stop while suppressed");
        foreach(var effect in admitted)
            if(effect!=stopped)Check(effect.isPlaying && !effect.GetComponent<ParticleSystemRenderer>().forceRenderingOff,
                "restoring full environment budget resumes only owned decorative emitter pauses");
        Check(doorway.activeSelf && prop.activeSelf && doorway.GetComponent<Animator>().enabled,
            "smoke budget never disables native scenery objects or animation controllers");
    }

    private static void DecorativeSmokeSimulationOwnership()
    {
        using var room=new Room();
        var collision=room.Particle("p_fire_torch (8)");
        var collisionModule=collision.collision;collisionModule.enabled=true;
        var trigger=room.Particle("p_fire_torch_blue");
        var triggerModule=trigger.trigger;triggerModule.enabled=true;
        var callback=room.Particle("p_fire_torch_Demon_01");
        var callbackMain=callback.main;callbackMain.stopAction=ParticleSystemStopAction.Callback;
        var initiallyPaused=room.Particle("p_fire_torch (9)");initiallyPaused.Pause(false);
        var initiallyStopped=room.Particle("p_fire_torch_blue (2)");
        initiallyStopped.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
        Configure(false,false,0);ScenarioEnvironmentBudget.BeforeLoadingComplete();
        Check(collision.isPlaying,"decorative collision callbacks keep native particle simulation under zero FX");
        Check(trigger.isPlaying,"decorative trigger callbacks keep native particle simulation under zero FX");
        Check(callback.isPlaying,"decorative stop callbacks keep native particle simulation under zero FX");
        room.ObserveRender=()=>
        {
            foreach(var effect in new[]{collision,trigger,callback})
                Check(effect.GetComponent<ParticleSystemRenderer>().forceRenderingOff,
                    "callback-bearing decorative smoke can be hidden without pausing gameplay-capable simulation");
        };
        room.Render();room.ObserveRender=null;
        var owned=room.Particle("p_fire_torch");
        ScenarioEnvironmentBudget.MaterialReady(owned.GetComponent<ParticleSystemRenderer>());
        Check(owned.isPaused,"new safe decorative emitter is paused before a later native module change");
        var ownedCollision=owned.collision;ownedCollision.enabled=true;
        Configure(false,true,0);Tick();
        Check(owned.isPlaying,"late native collision enable releases owned pause while retaining visual budget");
        Configure(false,false,100);Tick();
        Check(initiallyPaused.isPaused,"full FX restore preserves a preexisting native pause");
        Check(initiallyStopped.isStopped,"full FX restore preserves a preexisting native stop");
        foreach(var effect in new[]{collision,trigger,callback})
            Check(effect.isPlaying && !effect.GetComponent<ParticleSystemRenderer>().forceRenderingOff,
                "full FX restore preserves native callback simulation and unmasks only owned renders");
        Configure(false,false,0);Tick();
        ScenarioEnvironmentBudget.Shutdown();
        Check(initiallyPaused.isPaused && initiallyStopped.isStopped,
            "teardown preserves native pause and stop baselines");
        Check(owned.isPlaying,"teardown restores only owned safe simulation state");
    }
}
