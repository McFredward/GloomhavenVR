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
        internal static bool IsReady=true; internal static int Begins,Ticks,Resets; internal static int Failures=>0; internal static string PendingDescription=>"fixture resources";
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
        internal static bool IsReady=true; internal static int Begins,Ticks,Resets; internal static int CompletedCount=>Ticks; internal static int TotalCount=>Ticks+1;
        internal static void Begin(){Begins++;IsReady=false;}
        internal static void BeginNewActors(){IsReady=false;}
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
            var text=new StringBuilder();PerfSpikeDetails.Append(text);
            Check(text.ToString().Contains("n/a (no selected callback completed)"),
                "first native frame excludes synthetic hook calibration");
            PerfNativeLoopProbe.Start();
            Type actorType=typeof(Choreographer).Assembly.GetType("ActorBehaviour")!;
            object actor=Activator.CreateInstance(actorType)!;
            MethodInfo callback=actorType.GetMethod("Update")!;
            for(int frame=0;frame<155;frame++)
            {
                callback.Invoke(actor,null);
                PerfSpikeDetails.RollFrame(true);
                if(frame==119)PerfNativeLoopProbe.Stop(120);
            }
            text.Clear();PerfSpikeDetails.Append(text);
            Check(text.ToString().Contains("ActorBehaviour.Update ")&&text.ToString().Contains("1 call(s)"),
                "callbacks after summary cap still reach exact frame attribution");
            Check((int)actorType.GetField("Calls")!.GetValue(actor)! ==155,
                "observer preserves every original callback invocation");
            PerfSpikeDetails.RollFrame(true);text.Clear();PerfSpikeDetails.Append(text);
            Check(!text.ToString().Contains("ActorBehaviour.Update "),
                "native frame attribution clears previous callbacks");
            var choreographer = new Choreographer();
            choreographer.Dispatch(new CMessageData{m_Type=CMessageData.MessageType.Nested});
            bool preservedFailure=false;
            try { choreographer.Dispatch(new CMessageData{m_Type=CMessageData.MessageType.Throwing}); }
            catch(InvalidOperationException error){preservedFailure=error.Message=="original native failure";}
            PerfSpikeDetails.RollFrame(true);text.Clear();PerfSpikeDetails.Append(text);
            string native=text.ToString();
            Check(native.Contains("native previous-frame messages (Debug; inclusive):")
                  &&native.Contains("ActionSelection ")&&native.Contains("Nested ")&&native.Contains("Throwing "),
                "original message labels survive nested callbacks after summary cap");
            Check(native.Contains("exception(s) 1")&&preservedFailure&&choreographer.Processed==3,
                "message finalizer observes failures without changing original callbacks or exceptions");
            Check(native.Contains("CardsHandManager.Show ")&&native.Contains("CardsHandUI.UpdateView "),
                "selected nested native presentation methods retain inclusive substep evidence");
            PerfSpikeDetails.RollFrame(true);text.Clear();PerfSpikeDetails.Append(text);
            Check(!text.ToString().Contains("ActionSelection "),
                "native message ledgers clear before the next frame");
            var background=new Thread(()=>choreographer.Dispatch(new CMessageData{m_Type=CMessageData.MessageType.ActionSelection}));
            background.Start();background.Join();
            PerfSpikeDetails.RollFrame(true);text.Clear();PerfSpikeDetails.Append(text);
            Check(!text.ToString().Contains("ActionSelection ")&&choreographer.Processed==4,
                "off-main native dispatch preserves callbacks without charging the main-thread ledger");
            GC.Collect(0);PerfSpikeDetails.RollFrame(true);text.Clear();PerfSpikeDetails.Append(text);
            Check(!text.ToString().Contains("GC collections since previous frame 0/"),
                "real GC collection delta belongs to the observed boundary");
            PerfSpikeDetails.RollFrame(true);text.Clear();PerfSpikeDetails.Append(text);
            Check(text.ToString().Contains("GC collections since previous frame 0/0/0"),
                "GC collection deltas do not accumulate into the next frame");
            VRLog.WantsDebug=false;PerfSpikeDetails.RollFrame(true);text.Clear();PerfSpikeDetails.Append(text);
            Check(text.Length==0 && !(bool)typeof(PerfSpikeDetails).GetField("_on",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!,"ordinary logging leaves spike resources disabled");
            choreographer.Dispatch(new CMessageData{m_Type=CMessageData.MessageType.ActionSelection});
            Check(choreographer.Processed==5,
                "ordinary logging preserves uninstrumented native message dispatch");
            VRLog.WantsDebug=true;
            // Bind the actual production edge observer and exercise Unity's process setting.
            // Scene flags/resources below remain external game seams; no native game assembly
            // is claimed by this small loading lifecycle fixture.
            var loadingObserver = new GloomhavenVR.WorldUI.LoadingIndicator();
            MethodInfo observeLoading = loadingObserver.GetType().GetMethod("TickNativeLoading", BindingFlags.Instance | BindingFlags.NonPublic)!;
            UnityEngine.ThreadPriority originalPriority = Application.backgroundLoadingPriority;
            try
            {
                foreach (UnityEngine.ThreadPriority priority in new[] { UnityEngine.ThreadPriority.BelowNormal, UnityEngine.ThreadPriority.Normal, UnityEngine.ThreadPriority.High, UnityEngine.ThreadPriority.Low })
                {
                    Application.backgroundLoadingPriority = priority;
                    VRLog.Lines.Clear();
                    foreach (bool edge in new[] { false, true, true, false, false, true, false })
                    {
                        observeLoading.Invoke(loadingObserver, new object[] { edge });
                        Check(Application.backgroundLoadingPriority == priority,
                            "loading display preserves the actual native async priority on every edge");
                    }
                    Check(VRLog.Lines.Count == 4,
                        "normal loading lifecycle logs each real edge without growing every frame");
                    Check(VRLog.Lines[0].Contains("Loading started:") && VRLog.Lines[1].Contains("Loading ended:"),
                        "normal lifecycle retains useful native loading start and completion context");
                }
            }
            finally { Application.backgroundLoadingPriority = originalPriority; }
            game=SceneManager.GetSceneByName("Game");proc=SceneManager.GetSceneByName("ProcGen");
            if(!game.IsValid())game=SceneManager.CreateScene("Game");
            if(!proc.IsValid())proc=SceneManager.CreateScene("ProcGen");
            SceneController.Instance.Current=game;
            Choreographer.s_Choreographer=new Choreographer{m_ProcGenScene=proc};
            ScenarioInteractionPreparation.Reset();
            int begins=ScenarioCardPreparation.Begins;
            int coldPreparationTicks=FigureInteractionPreparation.Ticks+ScenarioCardPreparation.Ticks;
            SceneController.Instance.ScenarioIsLoading=true;
            ScenarioInteractionPreparation.Tick();
            Check(ScenarioControllerLoading()&&ScenarioCardPreparation.Begins==begins
                &&!ScenarioInteractionPreparation.IsPreparing,
                "preparation waits for native loading without writing native flags");
            SceneController.Instance.ScenarioIsLoading=false;
            ScenarioSceneryBudget.IsPreparingPresentation=true;
            ScenarioInteractionPreparation.Tick();
            Check(ScenarioInteractionPreparation.IsPreparing&&ScenarioCardPreparation.Begins==begins,
                "initial preparation defers finite resource deadlines until native visual queues settle");
            Check(!ScenarioInteractionPreparation.ShowsLoadingIndicator,
                "initial cosmetic preparation cannot extend completed native loading");
            ScenarioSceneryBudget.IsPreparingPresentation=false;
            ScenarioInteractionPreparation.Tick();
            Check(ScenarioInteractionPreparation.IsPreparing&&FigureInteractionPreparation.Ticks+ScenarioCardPreparation.Ticks==coldPreparationTicks+1,
                "one cold preparation job per frame keeps original resources available in the background");
            Check(!ScenarioInteractionPreparation.ShowsLoadingIndicator,
                "initial card and figure jobs cannot own the completed scenario spinner");
            ScenarioInteractionPreparation.Tick();
            Check(!ScenarioInteractionPreparation.IsPreparing&&FigureInteractionPreparation.IsReady&&ScenarioCardPreparation.IsReady,
                "both original preparation jobs complete without delaying native loading");
            begins=ScenarioCardPreparation.Begins;
            for(int i=0;i<10;i++)ScenarioInteractionPreparation.Tick();
            Check(ScenarioCardPreparation.Begins==begins,
                "completed preparation does not restart during normal play");
            var room = new GameObject("native revealed room").AddComponent<ProceduralMapTile>();
            SceneManager.MoveGameObjectToScene(room.gameObject,proc);
            var entity=room.gameObject.AddComponent<ApparanceEntity>();entity.IsBusy=true;
            var nativeRenderer=room.gameObject.AddComponent<MeshRenderer>();nativeRenderer.enabled=true;
            var loader=room.gameObject.AddComponent<MaterialLoader>();
            Shader shader=Shader.Find("Unlit/Color");var material=new Material(shader);
            var request=new MaterialLoaderData{Renderer=nativeRenderer};request.SetLoaded(new[]{material});loader.LoadersData.Add(request);
            ScenarioRoomLoading.Install();RoomVisibilityTracker.Emit(room,false);
            Check(!ScenarioRoomLoading.HasPendingReveal,"unrevealed rooms never start loading presentation");
            RoomVisibilityTracker.Emit(room,true);
            Check(ScenarioInteractionPreparation.IsPreparing&&ScenarioInteractionPreparation.ShowsLoadingIndicator,"native reveal event immediately publishes the loading indicator state");
            Check(!ScenarioRoomLoading.Tick(),"room reveal waits for native Update activation before readiness");
            typeof(ScenarioRoomLoading).GetField("_revealedFrame",BindingFlags.NonPublic|BindingFlags.Static)!.SetValue(null,Time.frameCount-2);
            ScenarioInteractionPreparation.Tick();
            Check(ScenarioInteractionPreparation.IsPreparing&&FigureInteractionPreparation.IsReady,
                "room spinner follows native generation before preparing new actors");
            Check(!ScenarioRoomLoading.Tick(),"room spinner follows native generation before preparing new actors");
            entity.IsBusy=false;request.SetLoaded(new Material[]{null!,null!});
            using var manager=new UnityEngine.ResourceManagement.ResourceManager();
            var operation=new DeferredMaterialOperation();
            var handle=manager.StartOperation(operation,default);
            request.SetHandle(handle);
            Check(!ScenarioRoomLoading.Tick(),"room readiness uses original pending materials even when renderer is enabled");
            request.ReleaseObservation(true);
            Check(ScenarioRoomLoading.Tick(),"released native requests cannot hold room loading open");
            request.ReleaseObservation(false);
            Check(!ScenarioRoomLoading.Tick(),"unreleased native request retains loading while its operation runs");
            operation.Finish(material);
            Check(ScenarioRoomLoading.Tick(),"completed native handles close the spinner despite preserved material holes");
            var failed=new DeferredMaterialOperation();var failedHandle=manager.StartOperation(failed,default);
            request.SetHandle(failedHandle);failed.Finish(null!,false);
            Check(ScenarioRoomLoading.Tick(),"completed failed material requests do not masquerade as continuing asset loading");
            request.SetHandle(default);
            Check(ScenarioRoomLoading.Tick(),"invalid dormant handles do not keep a completed room loading");
            request.SetHandle(handle);
            ScenarioSceneryBudget.IsPreparingPresentation=true;ScenarioInteractionPreparation.Tick();
            Check(ScenarioInteractionPreparation.IsPreparing&&!ScenarioInteractionPreparation.ShowsLoadingIndicator,
                "completed room assets hide the spinner while cosmetic preparation continues");
            ScenarioSceneryBudget.IsPreparingPresentation=false;ScenarioInteractionPreparation.Tick();
            for(int i=0;i<3;i++)ScenarioInteractionPreparation.Tick();
            Check(!ScenarioInteractionPreparation.IsPreparing&&!ScenarioRoomLoading.HasPendingReveal
                &&ScenarioCardPreparation.Begins==begins,"room completion preserves shared card preparation and closes the visual gate");
            UnityEngine.Object.DestroyImmediate(room.gameObject);UnityEngine.Object.DestroyImmediate(material);
            SceneController.Instance.Current=oldScene;ScenarioInteractionPreparation.Tick();
            Check(!ScenarioInteractionPreparation.IsPreparing,
                "leaving native scenario releases prepared resources and spinner");
            return count;
        }
        finally
        {
            VRLog.WantsDebug=true;ScenarioInteractionPreparation.Reset();ScenarioRoomLoading.Shutdown();
            ScenarioSceneryBudget.IsPreparingPresentation=false;PerfSpikeDetails.Shutdown();
            PerfNativeLoopProbe.Shutdown();VRSession.Harmony.UnpatchSelf();VRSession.Harmony=null;
            SceneController.Instance.Current=oldScene;SceneController.Instance.ScenarioIsLoading=false;
            Choreographer.s_Choreographer=oldChoreographer;
            // The synchronous multi-case runner exits before asynchronous scene unloads can
            // finish. Reuse these two empty real scenes across cases; Editor exit owns teardown.
        }
    }
    private static bool ScenarioControllerLoading()=>SceneController.Instance.ScenarioIsLoading;
}
