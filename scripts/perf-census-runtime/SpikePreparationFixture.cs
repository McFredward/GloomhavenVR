using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using HarmonyLib;
using GloomhavenVR.Core;
using GloomhavenVR.Cards;
using GloomhavenVR.Board.FigureGrab;
using UnityEngine;
using UnityEngine.SceneManagement;

// Game/context/job readiness are external seams. Real Harmony wraps the original observer's
// selected callback and real Unity scenes drive the complete production coordinator.
public sealed class ActorBehaviour
{
    public int Calls;
    [MethodImpl(MethodImplOptions.NoInlining)] public void Update() { Calls++; Thread.Sleep(1); }
}
namespace ScenarioRuleLibrary { public static class ScenarioManager { public static object Scenario = new(); } }
namespace GloomhavenVR.Cards
{
    internal static class ScenarioCardPreparation
    {
        internal static bool IsReady=true; internal static int Begins,Ticks,Resets; internal static int Failures=>0;
        internal static void Begin(){Begins++;IsReady=false;}
        internal static void Tick(){Ticks++;IsReady=true;}
        internal static void CancelPreparation(){IsReady=true;}
        internal static void Reset(){Resets++;IsReady=true;}
    }
}
namespace GloomhavenVR.Board.FigureGrab
{
    internal static class FigureInteractionPreparation
    {
        internal static bool IsReady=true; internal static int Begins,Ticks,Resets;
        internal static void Begin(){Begins++;IsReady=false;}
        internal static void Tick(){Ticks++;IsReady=true;}
        internal static void CancelPreparation(){IsReady=true;}
        internal static void Reset(){Resets++;IsReady=true;}
    }
}
public static class SpikePreparationFixture
{
    private static int count;
    private static void Check(bool yes,string message){count++;if(!yes)throw new Exception(message);}
    public static int Run()
    {
        count=0;
        VRLog.WantsDebug=true;
        VRSession.Harmony = new Harmony("ghvr.frame617.fixture."+typeof(SpikePreparationFixture).Assembly.GetName().Name);
        Scene oldScene=SceneController.Instance.Current;
        Choreographer? oldChoreographer=Choreographer.s_Choreographer;
        Scene game=default,proc=default;
        try
        {
            PerfSpikeDetails.RollFrame(true);
            PerfNativeLoopProbe.Start();
            Type actorType=AccessTools.TypeByName("ActorBehaviour");
            object actor=Activator.CreateInstance(actorType)!;
            MethodInfo callback=actorType.GetMethod("Update")!;
            for(int frame=0;frame<155;frame++)
            {
                callback.Invoke(actor,null);
                PerfSpikeDetails.RollFrame(true);
                if(frame==119)PerfNativeLoopProbe.Stop(120);
            }
            var text=new StringBuilder();PerfSpikeDetails.Append(text);
            Check(text.ToString().Contains("ActorBehaviour.Update ")&&text.ToString().Contains("1 call(s)"),
                "callbacks after summary cap still reach exact frame attribution");
            Check((int)actorType.GetField("Calls")!.GetValue(actor)! ==155,
                "observer preserves every original callback invocation");
            PerfSpikeDetails.RollFrame(true);text.Clear();PerfSpikeDetails.Append(text);
            Check(!text.ToString().Contains("ActorBehaviour.Update "),
                "native frame attribution clears previous callbacks");
            GC.Collect(0);PerfSpikeDetails.RollFrame(true);text.Clear();PerfSpikeDetails.Append(text);
            Check(!text.ToString().Contains("GC collections since previous frame 0/"),
                "real GC collection delta belongs to the observed boundary");
            PerfSpikeDetails.RollFrame(true);text.Clear();PerfSpikeDetails.Append(text);
            Check(text.ToString().Contains("GC collections since previous frame 0/0/0"),
                "GC collection deltas do not accumulate into the next frame");
            VRLog.WantsDebug=false;PerfSpikeDetails.RollFrame(true);text.Clear();PerfSpikeDetails.Append(text);
            Check(text.Length==0 && !(bool)typeof(PerfSpikeDetails).GetField("_on",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!,"ordinary logging leaves spike resources disabled");
            VRLog.WantsDebug=true;
            game=SceneManager.GetSceneByName("Game");proc=SceneManager.GetSceneByName("ProcGen");
            if(!game.IsValid())game=SceneManager.CreateScene("Game");
            if(!proc.IsValid())proc=SceneManager.CreateScene("ProcGen");
            SceneController.Instance.Current=game;
            Choreographer.s_Choreographer=new Choreographer{m_ProcGenScene=proc};
            ScenarioInteractionPreparation.Reset();
            int begins=ScenarioCardPreparation.Begins;
            SceneController.Instance.ScenarioIsLoading=true;
            ScenarioInteractionPreparation.Tick();
            Check(ScenarioControllerLoading()&&ScenarioCardPreparation.Begins==begins,
                "preparation waits for native loading without writing native flags");
            SceneController.Instance.ScenarioIsLoading=false;
            ScenarioInteractionPreparation.Tick();
            Check(ScenarioInteractionPreparation.IsPreparing&&FigureInteractionPreparation.Ticks+ScenarioCardPreparation.Ticks==1,
                "one cold preparation job per frame keeps the spinner visible");
            ScenarioInteractionPreparation.Tick();
            Check(!ScenarioInteractionPreparation.IsPreparing&&FigureInteractionPreparation.IsReady&&ScenarioCardPreparation.IsReady,
                "both original preparation jobs release the visual loading gate");
            begins=ScenarioCardPreparation.Begins;
            for(int i=0;i<10;i++)ScenarioInteractionPreparation.Tick();
            Check(ScenarioCardPreparation.Begins==begins,
                "completed preparation does not restart during normal play");
            SceneController.Instance.Current=oldScene;ScenarioInteractionPreparation.Tick();
            Check(!ScenarioInteractionPreparation.IsPreparing,
                "leaving native scenario releases prepared resources and spinner");
            return count;
        }
        finally
        {
            VRLog.WantsDebug=true;ScenarioInteractionPreparation.Reset();PerfSpikeDetails.Shutdown();
            PerfNativeLoopProbe.Shutdown();VRSession.Harmony.UnpatchSelf();VRSession.Harmony=null;
            SceneController.Instance.Current=oldScene;SceneController.Instance.ScenarioIsLoading=false;
            Choreographer.s_Choreographer=oldChoreographer;
            // The synchronous multi-case runner exits before asynchronous scene unloads can
            // finish. Reuse these two empty real scenes across cases; Editor exit owns teardown.
        }
    }
    private static bool ScenarioControllerLoading()=>SceneController.Instance.ScenarioIsLoading;
}
