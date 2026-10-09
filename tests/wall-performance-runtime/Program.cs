using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using GloomhavenVR.Core;
using GloomhavenVR.Board.FigureGrab;
using GloomhavenVR.WorldUI;
using Object=UnityEngine.Object;

public static class WallPerformanceProgram
{
    private static int assertions;
    private static readonly List<Object> owned=new();
    private static Camera camera=null!;
    private static Material material=null!;
    private static void Check(bool yes,string message){assertions++;if(!yes)throw new Exception(message);}
    private static void Setup()
    {
        PerfConfig.WallVisibilityMode=0;PerfConfig.WallAutoHideBelowFps=15;VRSession.IsRunning=true;VRSession.InputFocus=true;
        ScenarioRuleLibrary.ScenarioManager.Scenario=new object();
        Scene game=SceneManager.GetSceneByName("Game");if(!game.IsValid())game=SceneManager.CreateScene("Game");
        SceneController.Instance=new SceneController{GetCurrentScene=game};
        var gen=new GameObject("Original generated room registry");owned.Add(gen);
        TilesOcclusionGenerator.s_Instance=gen.AddComponent<TilesOcclusionGenerator>();TilesOcclusionGenerator.s_Instance.m_RoomRenderers.Add(new object());
        ProceduralWall.m_WallCache.Clear();HeldProps.Visuals.Clear();NetHeldProps.Visuals.Clear();
        WallFixtureClock.frameCount=0;WallFixtureClock.unscaledTime=0;WallFixtureClock.unscaledDeltaTime=.016f;
        WallFixtureFocus.isFocused=true;VROptionsTab.IsOpen=false;
        ScenarioInteractionPreparation.IsPreparing=ScenarioRoomLoading.HasPendingReveal=false;
        ScenarioEnvironmentBudget.BeforeWrite=null;ScenarioEnvironmentBudget.Writes=0;
        WallSegmentFade.ConfigurePerformanceMaskRestored(_=>{});
        var go=new GameObject("Original head camera");owned.Add(go);camera=go.AddComponent<Camera>();
        camera.transform.position=new Vector3(0,0,-10);camera.orthographic=true;camera.orthographicSize=4;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.nearClipPlane=.1f;camera.farClipPlane=20;
        camera.targetTexture=new RenderTexture(128,128,24,RenderTextureFormat.ARGB32);owned.Add(camera.targetTexture);
        camera.enabled=false;GloomhavenVR.Rig.VRRigDriver.HeadCamera=camera;
        material=new Material(Shader.Find("Fixture/WallPerformance"));owned.Add(material);material.color=Color.red;
    }
    private static MeshRenderer Piece(string name,float x=0,float y=0)
    {var go=GameObject.CreatePrimitive(PrimitiveType.Cube);owned.Add(go);go.name=name;go.transform.position=new Vector3(x,y,0);go.transform.localScale=Vector3.one;var r=go.GetComponent<MeshRenderer>();r.sharedMaterial=material;return r;}
    private static Color Pixel(float x,float y=0)
    {
        camera.Render();RenderTexture old=RenderTexture.active;RenderTexture.active=camera.targetTexture;
        var tex=new Texture2D(128,128,TextureFormat.RGBA32,false);tex.ReadPixels(new Rect(0,0,128,128),0,0);tex.Apply();
        Color color=tex.GetPixel(Mathf.RoundToInt((x/8+.5f)*128),Mathf.RoundToInt((y/8+.5f)*128));
        RenderTexture.active=old;Object.DestroyImmediate(tex);return color;
    }
    private static void Cleanup()
    {foreach(Object obj in owned)if(obj!=null)Object.DestroyImmediate(obj);owned.Clear();ScenarioEnvironmentBudget.BeforeWrite=null;}
    public static int Run()
    {
        assertions=0;Setup();
        try{Clock();Presentation();ProtectedFamilies();Ownership();SceneryOwnership();Auto();Lifecycle();WireRecovery();Exceptions();CommandDraw();return assertions;}
        finally{Cleanup();}
    }
    private static void Clock()
    {
        var clock=new WallSegmentFade.PerformanceWallClock();bool fired=false;
        for(int i=0;i<19;i++)fired|=clock.Observe(i,true,.1f,15);
        Check(!fired,"Auto waits for a whole loaded two-second window");
        fired=clock.Observe(19,true,.1f,15);Check(fired,"sustained ten FPS triggers Auto");
        clock.Reset();Check(!clock.Observe(100,true,10f,15),"one long paused frame cannot trigger Auto");
        for(int i=101;i<231;i++)Check(!clock.Observe(i,true,1f/60,15),"ordinary frames after one hitch do not hide walls");
        clock.Reset();for(int i=0;i<100;i++){Check(!clock.Observe(i,false,.1f,15),"ineligible loading frames cannot fill Auto window");}
        for(int i=100;i<119;i++)Check(!clock.Observe(i,true,.1f,15),"loading duration does not leak into new eligible window");
        Check(clock.Observe(119,true,.1f,15),"Auto samples a complete fresh window after loading");
        clock.Reset();for(int i=0;i<40;i++)Check(!clock.Observe(1,true,.1f,15),"multiple eyes in one frame do not accelerate Auto");
        clock.Reset();foreach(float bad in new[]{float.NaN,float.PositiveInfinity,0f,-.1f})Check(!clock.Observe(++WallFixtureClock.frameCount,true,bad,15),"invalid clock input resets without hiding");
    }
    private static void Presentation()
    {
        var wall=Piece("Wall 1",-3);var shelf=Piece("Attached shelf",-2);var column=Piece("Attached column",-1);
        var floor=Piece("Actual floor",0);floor.gameObject.AddComponent<FloorMarker>();var actor=Piece("Enemy",1);actor.gameObject.AddComponent<ActorBehaviour>();
        var door=Piece("Door gate",2);var shared=Piece("Shared arch column",3);
        var point=new GameObject("Native light");owned.Add(point);var light=point.AddComponent<Light>();light.enabled=true;
        using(var f=new WallSegmentFade.Fixture())
        {
            int a=f.Add(wall),b=f.Add(door,gate:true);f.Attach(a,shelf,"body");f.Attach(a,column,"stacked");
            f.Attach(a,floor,"mounted");f.Attach(a,actor,"unit");f.Attach(a,shared,"sibling");f.Attach(b,shared,"sibling");
            f.Corner(a,b,shared);f.Peer();
            var block=new MaterialPropertyBlock();block.SetColor("_Color",Color.green);wall.SetPropertyBlock(block);f.Fade(a,.4f,true);
            Check(Pixel(-3).g>.6f,"native wall MPB produces visible pre-hide pixels");
            PerfConfig.WallVisibilityMode=1;f.Frame();
            Check(wall.forceRenderingOff&&shelf.forceRenderingOff&&column.forceRenderingOff,"hide all masks wall and original attached shelf/column");
            Check(!floor.forceRenderingOff&&!actor.forceRenderingOff&&!door.forceRenderingOff&&!shared.forceRenderingOff,"floor actor gate and shared protected attachment remain visible");
            Check(Pixel(-3).maxColorComponent<.1f&&Pixel(-2).maxColorComponent<.1f&&Pixel(-1).maxColorComponent<.1f,"camera pixels lose walls and attached geometry immediately");
            Check(Pixel(0).r>.6f&&Pixel(1).r>.6f&&Pixel(2).r>.6f&&Pixel(3).r>.6f,"camera pixels retain floors actors gate and shared arch");
            Check(light.enabled&&wall.enabled&&wall.gameObject.activeSelf&&wall.GetComponent<Collider>().enabled,"hide policy keeps light gameplay activation and native collider state");
            Check(f.FadesCleared(a)&&f.PeerCount==0,"entry clears old native fade blocks animation state and shared decisions");
            int ticks=f.Ticks,begins=f.Begins,steps=f.Steps,writes=ScenarioEnvironmentBudget.Writes;
            var dest=new uint[]{101,102,103};f.Fade(a,.5f,false);
            for(int i=0;i<300;i++){f.Frame();Check(f.Sample(dest)==0,"hidden network sample emits no faded wall keys");f.Draw(a,wall);}
            Check(f.Ticks==ticks&&f.Begins==begins&&f.Steps==steps&&ScenarioEnvironmentBudget.Writes==writes,"settled hidden entry performs no old Tick collector or renderer writes");
            Check(dest[0]==101&&dest[2]==103&&f.DrawWrites==0,"hidden sender and diagnostic leave existing buffers untouched");
            block.Clear();wall.GetPropertyBlock(block);Check(block.isEmpty,"actual ClearAllBlocks retires native wall fade MPB");
            PerfConfig.WallVisibilityMode=0;f.Frame();
            Check(!wall.forceRenderingOff&&!shelf.forceRenderingOff&&!column.forceRenderingOff&&Pixel(-3).r>.6f,"returning Regular restores native visible pixels immediately");
            Check(f.Ticks==ticks+1&&f.Guarded,"Regular resumes the actual entry through ordinary Tick finally");
            f.Fade(a,.5f,false);f.Fade(b,.5f,false);f.Draw(a,wall);
            Check(f.Sample(dest)==2&&dest[0]!=0&&dest[1]>dest[0]&&f.DrawWrites==1,"Regular restores native sorted sender and diagnostic activity");
        }
        foreach(var r in new[]{wall,shelf,column,floor,actor,door,shared})Object.DestroyImmediate(r.gameObject);
    }
    private static void ProtectedFamilies()
    {
        var wall=Piece("Protected-family carrier",-3);var door=Piece("Original doorway",-2);
        var root=new GameObject("TO_INT_Wood_Shack_Doorway_01_PR(Clone)");owned.Add(root);
        var frame=Piece("Native doorway frame",0);frame.transform.SetParent(root.transform,true);
        var frameMesh=Object.Instantiate(frame.GetComponent<MeshFilter>().sharedMesh);owned.Add(frameMesh);frameMesh.name="TO_INT_Shack_Doorway_Frame";frame.GetComponent<MeshFilter>().sharedMesh=frameMesh;
        var candle=Piece("CandleFlame",2);candle.transform.SetParent(root.transform,true);
        var ordinary=Piece("CandleFlame",3);var water=Piece("Water bank",1);var mod=Piece("GloomhavenVR.Helper",-1);
        using(var f=new WallSegmentFade.Fixture())
        {
            int key=f.Add(wall);f.Add(door,door:door.transform);f.Attach(key,candle,"mounted");f.Attach(key,ordinary,"mounted");f.Attach(key,water,"body");f.Attach(key,mod,"unit");
            f.SetArch(new Bounds(frame.bounds.center,Vector3.one*1.1f));f.SetWater(new Bounds(water.bounds.center,Vector3.one*1.1f));
            PerfConfig.WallVisibilityMode=1;f.Frame();
            Check(!door.forceRenderingOff&&!candle.forceRenderingOff&&!water.forceRenderingOff&&!mod.forceRenderingOff,"door-root water mod and actual native arch-effect hierarchy remain protected");
            Check(ordinary.forceRenderingOff&&wall.forceRenderingOff,"ordinary similarly named CandleFlame does not inherit unrelated arch exception");
            Check(Pixel(2).r>.6f&&Pixel(3).maxColorComponent<.1f,"actual protected arch child pixels remain beside absent ordinary effect");
        }
        foreach(var r in new[]{wall,door,frame,candle,ordinary,water,mod})if(r!=null)Object.DestroyImmediate(r.gameObject);
    }
    private static void Ownership()
    {
        var wall=Piece("Native owner wall");var prior=Piece("Previously hidden wall",2);prior.forceRenderingOff=true;
        using(var f=new WallSegmentFade.Fixture())
        {
            f.Add(wall);f.Add(prior);int restores=0;
            ScenarioEnvironmentBudget.BeforeWrite=r=>{if(WallSegmentFade.IsPerformanceHidden(r))r.forceRenderingOff=true;};
            WallSegmentFade.ConfigurePerformanceMaskRestored(r=>{Check(!WallSegmentFade.IsPerformanceHidden(r),"ownership is removed before foreign restoration callback");restores++;});
            PerfConfig.WallVisibilityMode=1;f.Frame();
            var native=new MaterialPropertyBlock();native.SetColor("_Color",Color.blue);wall.SetPropertyBlock(native);wall.enabled=false;
            WallSegmentFade.NotifyPerformanceRendererReady(wall);Check(wall.forceRenderingOff,"native-ready hook reasserts known wall mask synchronously");
            f.Frame();f.Frame();Check(restores==0,"temporary lifecycle collector unmask never publishes final restoration callback");
            wall.enabled=true;Check(WallSegmentFade.RetainPerformanceMaskOnForeignRelease(prior),"scenery can release its prior claim while exact wall remains hidden");
            Check(prior.forceRenderingOff,"foreign scenery release keeps wall mask in place");
            PerfConfig.WallVisibilityMode=0;f.Frame();
            var read=new MaterialPropertyBlock();wall.GetPropertyBlock(read);
            Check(!wall.forceRenderingOff&&!prior.forceRenderingOff&&read.GetColor("_Color")==Color.blue,"Regular preserves current native MPB and relinquished foreign flag");
            Check(restores==2,"final release notifies each surviving exact masked renderer once");
            prior.forceRenderingOff=true;PerfConfig.WallVisibilityMode=1;f.Frame();PerfConfig.WallVisibilityMode=0;f.Frame();
            Check(prior.forceRenderingOff,"unreleased pre-existing foreign hiding survives Regular");
            Check(!WallSegmentFade.RetainPerformanceMaskOnForeignRelease(wall),"foreign release does not acquire absent wall ownership");
        }
        ScenarioEnvironmentBudget.BeforeWrite=null;Object.DestroyImmediate(wall.gameObject);Object.DestroyImmediate(prior.gameObject);
    }
    private static void SceneryOwnership()
    {
        var wall=Piece("Actual scenery overlap wall");var scenery=new ScenarioSceneryBudget.Fixture(wall);
        using(var f=new WallSegmentFade.Fixture())
        {
            f.Add(wall);scenery.Hide(true);Check(wall.forceRenderingOff&&scenery.Owned,"actual scenery primitive acquires native mask before wall policy");
            PerfConfig.WallVisibilityMode=1;f.Frame();scenery.Hide(false);
            Check(wall.forceRenderingOff&&!scenery.Owned,"actual scenery release retains wall-owned flag and relinquishes its own claim");
            PerfConfig.WallVisibilityMode=0;f.Frame();Check(!wall.forceRenderingOff,"wall Regular does not resurrect released actual scenery claim");
        }
        Object.DestroyImmediate(wall.gameObject);
    }
    private static void WireRecovery()
    {
        var wall=Piece("Fresh hidden wire wall");
        using(var f=new WallSegmentFade.Fixture())
        {
            int key=f.Add(wall);PerfConfig.WallVisibilityMode=1;f.Frame();f.ClearKeys();
            int rebuilt=f.KeyRebuilds;for(int i=0;i<100;i++)f.Frame();Check(f.KeyRebuilds==rebuilt,"settled Hide all performs no wire key rebuild");
            string context=f.RecoveryContext;PerfConfig.WallVisibilityMode=0;f.Frame();f.Fade(key,.4f,false);var dest=new uint[4];
            int emitted=f.Sample(dest);Check(f.KeyRebuilds==rebuilt+1&&emitted==1&&dest[0]!=0,"Hidden to Regular rebuilds actual native wire keys once before sender resumes"+"; rebuilds="+f.KeyRebuilds+", before="+rebuilt+", emitted="+emitted+", key="+dest[0]+", "+context);
            uint stable=dest[0];for(int i=0;i<100;i++)f.Frame();Check(f.KeyRebuilds==rebuilt+1,"ordinary Regular frames do not repeat recovery wire census");
            PerfConfig.WallVisibilityMode=1;f.Frame();Check(f.KeyRebuilds==rebuilt+1,"Regular to Hide all performs no wire recovery");f.ClearKeys();PerfConfig.WallVisibilityMode=2;f.Frame();f.Fade(key,.4f,false);
            Check(f.KeyRebuilds==rebuilt+2&&f.Sample(dest)==1&&dest[0]==stable,"Hidden to unlatched Auto rebuilds the same cross-machine stable key once");
            PerfConfig.WallVisibilityMode=1;f.Frame();Check(f.KeyRebuilds==rebuilt+2,"Auto to Hide all skips recovery wire census");int before=f.KeyRebuilds;f.Reset();Check(f.KeyRebuilds==before,"scenario teardown does not pay recovery wire census");
        }
        Object.DestroyImmediate(wall.gameObject);
    }
    private static void Lifecycle()
    {
        var wall=Piece("Lifecycle wall");var late=Piece("Late native generated wall",2);
        using(var f=new WallSegmentFade.Fixture())
        {
            int key=f.Add(wall);PerfConfig.WallVisibilityMode=1;f.Frame();
            ScenarioInteractionPreparation.IsPreparing=true;
            f.SetCollector(x=>x.Add(late));WallSegmentFade.NotifyPerformanceContentChange();f.Frame();f.Frame();
            Check(late.forceRenderingOff&&f.Begins==1&&f.Steps==1,"same-count native generation collects fresh new wall membership");
            Check(ScenarioInteractionPreparation.IsPreparing&&late.forceRenderingOff,"cosmetic prewarm does not veto dirty hidden inventory refresh");
            ScenarioInteractionPreparation.IsPreparing=false;
            int b=f.Begins;f.SetCollector(null);for(int i=0;i<100;i++)f.Frame();Check(f.Begins==b,"completed lifecycle does not resume recurring census");
            SceneController.Instance.IsLoading=true;TilesOcclusionGenerator.s_Instance.m_RoomRenderers.Add(new object());f.Frame();
            Check(f.Begins==b&&wall.forceRenderingOff,"loading room change preserves old mask and coalesces inventory");
            SceneController.Instance.IsLoading=false;f.Frame();f.Frame();Check(f.Begins==b+1,"loaded new room triggers one collector cycle");
            HeldProps.Visuals.Add(wall.gameObject);f.Frame();Check(!wall.forceRenderingOff&&late.forceRenderingOff,"taking a wall attachment immediately preserves held visual");
            HeldProps.Visuals[0]=late.gameObject;f.Frame();Check(wall.forceRenderingOff&&!late.forceRenderingOff,"same-count held root swap is observed by exact identity");
            HeldProps.Visuals.Clear();f.Frame();Check(late.forceRenderingOff,"released held attachment returns to hidden membership");
            f.Remove(key);WallSegmentFade.NotifyPerformanceContentChange();f.Frame();f.Frame();Check(!wall.forceRenderingOff,"native retirement restores no-longer-member flag");
            Object.DestroyImmediate(late.gameObject);WallSegmentFade.NotifyPerformanceContentChange();f.Frame();f.Frame();
            Check(f.Masks==0,"destroyed renderer ledger is safely retired");
            f.Reset();Check(!WallSegmentFade.PerformanceWallsHidden,"scenario reset drops hidden latch and ownership");
        }
        Object.DestroyImmediate(wall.gameObject);
    }
    private static void Auto()
    {
        var wall=Piece("Auto wall");
        using(var f=new WallSegmentFade.Fixture())
        {
            f.Add(wall);PerfConfig.WallVisibilityMode=2;
            foreach(Action<bool> hold in new Action<bool>[]{x=>SceneController.Instance.IsLoading=x,x=>SceneController.Instance.ScenarioIsLoading=x,x=>ScenarioRoomLoading.HasPendingReveal=x,x=>VRSession.InputFocus=!x})
            {
                // Start with an incomplete slow window. Every genuine loading/focus edge
                // must discard it, even after100 low-FPS frames in the blocked state.
                RestartAuto(f);for(int i=0;i<15;i++)f.Frame(.1f);
                hold(true);for(int i=0;i<100;i++)f.Frame(.1f);
                Check(!wall.forceRenderingOff&&!f.Latched,"native loading reveal or explicit XR focus loss does not trigger Auto");
                hold(false);for(int i=0;i<21;i++)f.Frame(.1f);
                Check(!wall.forceRenderingOff&&!f.Latched,"native loading reveal or XR focus recovery starts a fresh grace and full window");
                for(int i=0;i<12;i++)f.Frame(.1f);
                Check(wall.forceRenderingOff&&f.Latched,"native loaded focused recovery eventually triggers Auto");
            }
            foreach(var policy in new (Action Start,Action Finish,string Assertion)[]{
                (()=>ScenarioInteractionPreparation.IsPreparing=true,()=>ScenarioInteractionPreparation.IsPreparing=false,"cosmetic prewarm does not veto Auto in native settled gameplay"),
                (()=>VROptionsTab.IsOpen=true,()=>VROptionsTab.IsOpen=false,"open VR Options do not veto sustained low-FPS Auto"),
                (()=>WallFixtureFocus.isFocused=false,()=>WallFixtureFocus.isFocused=true,"desktop window focus is irrelevant to headset Auto"),
                (()=>VRSession.InputFocus=null,()=>VRSession.InputFocus=true,"unknown XR focus does not permanently veto loaded Auto")})
            {
                RestartAuto(f);policy.Start();for(int i=0;i<32;i++)f.Frame(i%2==0?.1f:.125f);
                Check(wall.forceRenderingOff&&f.Latched,policy.Assertion);policy.Finish();
            }
            RestartAuto(f);
            f.Frame(10f);for(int i=0;i<100;i++)f.Frame(1f/60);Check(!wall.forceRenderingOff,"one long post-load hitch does not trigger Auto");
            // Replay the actual Frame653 policy inputs: native reveal settled, cosmetic
            // preparation ongoing, open VR Options, unfocused Wine desktop, focused XR.
            ScenarioInteractionPreparation.IsPreparing=true;VROptionsTab.IsOpen=true;WallFixtureFocus.isFocused=false;
            for(int i=0;i<160;i++)f.Frame(1f/19);
            Check(!wall.forceRenderingOff&&!f.Latched,"first room at19FPS remains visible with Frame653 policy inputs");
            for(int i=0;i<32;i++)f.Frame(i%2==0?.1f:.125f);
            Check(wall.forceRenderingOff&&f.Latched,"Frame653 fully opened low-FPS room replay latches hidden policy");
            ScenarioInteractionPreparation.IsPreparing=false;VROptionsTab.IsOpen=false;WallFixtureFocus.isFocused=true;
            for(int i=0;i<300;i++)f.Frame(1f/90);Check(wall.forceRenderingOff&&f.Latched,"restored high FPS does not oscillate Auto walls");
            VROptionsTab.IsOpen=true;f.Frame();Check(wall.forceRenderingOff,"options opened after Auto trigger do not resurrect costly wallwork");VROptionsTab.IsOpen=false;
            PerfConfig.WallVisibilityMode=0;f.Frame();Check(!wall.forceRenderingOff&&!f.Latched,"manual Regular resets Auto latch immediately");
            PerfConfig.WallVisibilityMode=2;for(int i=0;i<30;i++)f.Frame(.1f);Check(wall.forceRenderingOff,"new Auto window can trigger again");
            var replacement=new GameObject("Replacement scenario generator");owned.Add(replacement);TilesOcclusionGenerator.s_Instance=replacement.AddComponent<TilesOcclusionGenerator>();TilesOcclusionGenerator.s_Instance.m_RoomRenderers.Add(new object());f.Frame();Check(!wall.forceRenderingOff&&!f.Latched,"new scenario identity resets Auto instead of inheriting old hidden latch");
        }
        Object.DestroyImmediate(wall.gameObject);
    }
    private static void RestartAuto(WallSegmentFade.Fixture f)
    {PerfConfig.WallVisibilityMode=0;f.Frame();PerfConfig.WallVisibilityMode=2;}
    private static void Exceptions()
    {
        var wall=Piece("Retry wall");
        using(var f=new WallSegmentFade.Fixture())
        {
            f.Add(wall);PerfConfig.WallVisibilityMode=1;f.Frame();
            f.SetCollector(null,()=>throw new InvalidOperationException("native collector failure"));WallSegmentFade.NotifyPerformanceContentChange();f.Frame();
            Check(wall.forceRenderingOff&&WallSegmentFade.IsPerformanceHidden(wall),"collector exception finally restores every temporary wall mask");
            int begins=f.Begins;f.Frame(.1f);Check(f.Begins==begins,"collector exception backs off rather than retrying every frame");
            f.SetCollector(null);f.Frame(2.1f);f.Frame();Check(f.Begins==begins+1&&wall.forceRenderingOff,"lifecycle retry recovers without dropping wall mask");
            Scene menu=SceneManager.GetSceneByName("MainMenu");if(!menu.IsValid())menu=SceneManager.CreateScene("MainMenu");SceneController.Instance.GetCurrentScene=menu;f.Frame();Check(!wall.forceRenderingOff&&!WallSegmentFade.PerformanceWallsHidden,"leaving scenario restores policy even while Hide all remains configured");
        }
        SceneController.Instance.GetCurrentScene=SceneManager.GetSceneByName("Game");Object.DestroyImmediate(wall.gameObject);
    }
    private static void CommandDraw()
    {
        var wall=Piece("Foreign native command draw");
        using(var f=new WallSegmentFade.Fixture())
        {
            f.Add(wall);PerfConfig.WallVisibilityMode=1;f.Frame();Check(Pixel(0).maxColorComponent<.1f,"ordinary native wall route respects forceRenderingOff pixels");
            var cloned=Object.Instantiate(wall.gameObject);owned.Add(cloned);VRLog.Info("Fixture","CLONED FORCERENDERINGOFF="+cloned.GetComponent<MeshRenderer>().forceRenderingOff);Object.DestroyImmediate(cloned);
            var cmd=new CommandBuffer{name="Independent foreign command draw"};cmd.DrawRenderer(wall,material,0,0);camera.AddCommandBuffer(CameraEvent.AfterForwardOpaque,cmd);
            var color=Pixel(0);VRLog.Info("Fixture","FOREIGN DRAWRENDERER PIXEL="+color);
            camera.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque,cmd);cmd.Dispose();
            // This is an evidence boundary, never an assertion pretending our mask owns
            // unrelated external command buffers. Root source audit must cover game writers.
            Check(wall.forceRenderingOff,"foreign command draw does not clear exact ordinary renderer mask");
        }
        Object.DestroyImmediate(wall.gameObject);
    }
    public static double TimeEntry(int mode,int iterations)
    {
        Setup();try{PerfConfig.WallVisibilityMode=mode;using(var f=new WallSegmentFade.Fixture())
        {for(int i=0;i<2000;i++)f.Frame();var watch=Stopwatch.StartNew();for(int i=0;i<iterations;i++)f.Frame();watch.Stop();return watch.Elapsed.TotalMilliseconds/iterations;}}
        finally{Cleanup();}
    }
    public static string Messages()=>String.Join("\n",VRLog.Messages);
}
