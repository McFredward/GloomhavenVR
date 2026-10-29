using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GLOOM.MainMenu;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.Rig;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.Surfaces;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public static partial class MenuWorldProgram
{
    private static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public;
    private static readonly List<Object> Owned=new();private static FlatScreen? screen;private static int checks;
    private static T Keep<T>(T value)where T:Object{Owned.Add(value);return value;}
    private static void Check(bool okay,string label){checks++;if(!okay)throw new Exception(label);}
    private static void Near(float a,float b,string label,float epsilon=.002f)=>Check(Mathf.Abs(a-b)<=epsilon,label+": "+a+" versus "+b);
    private static void Near(Vector3 a,Vector3 b,string label,float epsilon=.002f)=>Check(Vector3.Distance(a,b)<=epsilon,label+": "+a+" versus "+b);
    private static T Get<T>(object owner,string name)=>(T)owner.GetType().GetField(name,Flags)!.GetValue(owner)!;
    private static void Put(object owner,string name,object? value)=>owner.GetType().GetField(name,Flags)!.SetValue(owner,value);
    private static void Invoke(object owner,string name,params object[] values)
    {try{owner.GetType().GetMethod(name,Flags)!.Invoke(owner,values);}catch(TargetInvocationException error){throw error.InnerException!;}}
    private static IEnumerable<int> PlayerSteps(float seconds,Action tick,string output,string name,Func<string> state)
    {
        // This iterator is driven by EditorApplication.update, which can miss
        // player frames. Count the real deltas consumed by the disabled native
        // component, rather than the editor interval between manual calls.
        var wall=System.Diagnostics.Stopwatch.StartNew();float consumed=0;int frame=Time.frameCount;
        using var trace=new StreamWriter(Path.Combine(output,name+"-frames.csv"));
        trace.WriteLine("frame,unscaledDt,scaledDt,consumedDt,state");trace.Flush();
        while(consumed<seconds&&wall.Elapsed.TotalSeconds<30)
        {
            yield return 0;
            if(frame==Time.frameCount)continue;
            frame=Time.frameCount;float dt=Time.unscaledDeltaTime;tick();consumed+=dt;
            trace.WriteLine(FormattableString.Invariant($"{frame},{dt:R},{Time.deltaTime:R},{consumed:R},{state()}"));trace.Flush();
        }
        Check(consumed>=seconds,"fixture consumes the requested distinct native frame deltas within its wall deadline");
    }
    private static GameObject Quad(string name,Material material)
    {
        var go=Keep(GameObject.CreatePrimitive(PrimitiveType.Quad));go.name=name;go.layer=VRLayers.ModLayer;
        Object.DestroyImmediate(go.GetComponent<Collider>());go.GetComponent<MeshRenderer>().sharedMaterial=material;return go;
    }
    private static bool ReverseMesh(Mesh native,Mesh reverse)
    {
        if(native==reverse||native.vertices.Length!=reverse.vertices.Length||native.uv.Length!=reverse.uv.Length)return false;
        Vector3[] vertices=native.vertices,reversedVertices=reverse.vertices;Vector2[] uv=native.uv,reversedUv=reverse.uv;
        for(int i=0;i<vertices.Length;i++)if(vertices[i]!=reversedVertices[i])return false;
        for(int i=0;i<uv.Length;i++)if(uv[i]!=reversedUv[i])return false;
        int[] front=native.triangles,back=reverse.triangles;if(front.Length!=back.Length)return false;
        for(int i=0;i<front.Length;i+=3)if(front[i]!=back[i]||front[i+1]!=back[i+2]||front[i+2]!=back[i+1])return false;
        return front.Length>0;
    }
    private static void Pointer(FlatScreen window,VRHand hand,Vector2 uv)
    {
        var quad=Get<GameObject>(window,"_quad");Vector3 point=quad.transform.TransformPoint(new Vector3(uv.x-.5f,uv.y-.5f,0));
        Vector3 origin=VRRigDriver.HeadCamera!.transform.position;
        hand.Ray.Pick=new PickPose(origin,(point-origin).normalized);VRHands.Primary=hand;VRHands.PrimaryPick=hand.Ray;
        Invoke(window,"TickPointer");Near(VirtualMouse.Pixel.x,uv.x*1920,"native pointer U stays on the transformed world window",.05f);
        Near(VirtualMouse.Pixel.y,uv.y*1080,"native pointer V stays on the transformed world window",.05f);
        Near(hand.Ray.WorldUiHit,point,"pointer reticle ends on the actual world-space menu plane");
    }
    private static void LaserRoute(FlatScreen window,VRHand hand,VRHand offhand,PanelGrabHandle handle,string output)
    {
        Physics.SyncTransforms();
        Collider rod=handle.BarCollider??throw new Exception("missing actual narrow laser target");
        Vector3 point=rod.bounds.center,origin=point-Vector3.forward*1.2f;
        float savedClock=Time.timeScale;Time.timeScale=0;
        void Aim(VRHand source,Vector3 at)
        {
            source.transform.SetPositionAndRotation(at,Quaternion.LookRotation(point-at));
            source.Ray.Pick=new PickPose(at,(point-at).normalized);
            source.Ray.HasFreshUiHit=source.Ray.FreshUiHitIsPanelHoverOnly=source.Ray.HasFreshTriggerClaim=false;
        }
        void Start()
        {hand.TriggerDown=hand.TriggerPressed=true;hand.RayUgui.Tick();hand.RayGrab.Tick();hand.Grabber.Tick();Invoke(window,"TickPointer");}
        void Release()
        {
            hand.TriggerDown=hand.TriggerPressed=false;hand.TriggerUp=true;hand.RayUgui.Tick();hand.Grabber.Tick();
            hand.TriggerUp=false;hand.RayUgui.Cancel();
            hand.Ray.HasFreshUiHit=hand.Ray.FreshUiHitIsPanelHoverOnly=hand.Ray.HasFreshTriggerClaim=false;
        }
        Canvas CanvasAt(string name,float depth)
        {
            var go=Keep(new GameObject(name,typeof(RectTransform),typeof(Canvas)));
            go.transform.SetPositionAndRotation(point+Vector3.forward*depth,Quaternion.identity);
            ((RectTransform)go.transform).sizeDelta=new Vector2(2,1);
            var canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.worldCamera=VRRigDriver.HeadCamera;
            UguiPokeSurfaces.Surfaces.Add(canvas);UguiPointer.Widgets[canvas]=go;return canvas;
        }
        VRHands.Primary=hand;VRHands.PrimaryPick=hand.Ray;Aim(hand,origin);
        Check(hand.Ray.Active&&hand.Grabber.Enabled,"ready dominant hand enables actual ray and grab policies");
        using(var trace=new StreamWriter(Path.Combine(output,"native-menu-angle-physics.csv")))
        {
            trace.WriteLine("angle,vertical,targetX,physicsCollider,physicsDistance,barDistance,picked");
            for(int angle=-75;angle<=75;angle+=15)for(int vertical=-1;vertical<=1;vertical++)for(int end=-1;end<=1;end++)
            {
                Vector3 aim=point+Vector3.right*(rod.bounds.extents.x*.9f*end);
                Vector3 from=aim+new Vector3(Mathf.Sin(angle*Mathf.Deg2Rad)*1.2f,vertical*.7f,-Mathf.Cos(angle*Mathf.Deg2Rad)*1.2f);
                Vector3 direction=(aim-from).normalized;hand.transform.SetPositionAndRotation(from,Quaternion.LookRotation(direction));
                hand.Ray.CaptureNativePhysics(from,direction);
                var native=hand.Ray.Current;
                bool accepted=hand.RayGrab.TryPickBar(out _,out _,out float range);
                trace.WriteLine($"{angle},{vertical},{end},{native.HitCollider?.name},{native.HitDistance:R},{range:R},{accepted}");
            }
            trace.WriteLine($"zoneBounds={handle.GetComponent<Collider>().bounds};rodBounds={rod.bounds}");
        }
        Aim(hand,origin);
        bool nativeHit=Physics.Raycast(origin,(point-origin).normalized,out RaycastHit physical,20*hand.WorldScale,Physics.DefaultRaycastLayers);
        File.WriteAllText(Path.Combine(output,"native-menu-physics.txt"),$"hit={nativeHit};collider={physical.collider};distance={physical.distance};queriesHitTriggers={Physics.queriesHitTriggers};zone={handle.GetComponent<Collider>()};rod={rod}\n");
        hand.Ray.CaptureNativePhysics(origin,(point-origin).normalized);
        bool picked=hand.RayGrab.TryPickBar(out var candidate,out _,out float distance);
        File.WriteAllText(Path.Combine(output,"laser-route.txt"),$"rod={rod.bounds};enabled={rod.enabled};active={rod.gameObject.activeInHierarchy};canGrab={handle.CanGrab};picked={picked};candidate={candidate};distance={distance};point={point};origin={origin}\n");
        Check(picked&&ReferenceEquals(candidate,handle),"ready main-menu laser resolves the visible production rod before native pointer delivery");
        var behind=CanvasAt("native interactive panel behind menu rod",.2f);int presses=UguiPointer.Presses;
        Start();
        Check(ReferenceEquals(hand.Grabber.Held,handle)&&handle.LastGrabWasLaser,"normal laser route starts actual menu carry through ForceGrab");
        Check(UguiPointer.Presses==presses&&!hand.RayUgui.IsPressing,"actual uGUI dispatch yields to nearer menu rod before any widget callback");
        Check(VirtualMouse.Pixel==VirtualMouse.ParkPixel&&!Get<bool>(window,"_pressing"),"active menu laser carry parks native screen pointer without click-through");
        Release();Check(hand.Grabber.Held==null,"menu laser carry releases through the original trigger-up route");
        UguiPokeSurfaces.Surfaces.Remove(behind);behind.gameObject.SetActive(false);
        Aim(hand,origin);var front=CanvasAt("native interactive panel in front of menu rod",-.2f);presses=UguiPointer.Presses;
        Start();Check(hand.Grabber.Held==null&&hand.RayUgui.IsPressing&&UguiPointer.Presses==presses+1,"nearer native widget receives its press and blocks menu laser carry");
        Release();Check(!hand.RayUgui.IsPressing&&UguiPointer.Releases>0,"latched native widget keeps original trigger-up before later menu grab");
        UguiPokeSurfaces.Surfaces.Remove(front);front.gameObject.SetActive(false);
        Aim(hand,origin);hand.Ray.SolidOccluderDistance=.5f;
        Check(!hand.RayGrab.TryPickBar(out _,out _,out _),"solid geometry in front of menu rod blocks laser pickup");hand.Ray.SolidOccluderDistance=float.PositiveInfinity;
        hand.Ray.Pick=new PickPose(origin,(point-origin).normalized,true,.5f);
        Check(!hand.RayGrab.TryPickBar(out _,out _,out _),"nearer native physics hit blocks menu rod");Aim(hand,origin);
        hand.GripPressed=true;Check(!hand.RayGrab.TryPickBar(out _,out _,out _),"physical grip posture keeps its ordinary laser stand-down");hand.GripPressed=false;
        hand.HasPose=false;Check(!hand.RayGrab.TryPickBar(out _,out _,out _),"lost controller pose cannot offer menu laser grab");hand.HasPose=true;
        hand.Grabber.Enabled=false;Check(!hand.RayGrab.TryPickBar(out _,out _,out _),"disabled grab policy cannot promise menu laser pickup");hand.Grabber.Enabled=true;
        Aim(offhand,origin);Check(!offhand.RayGrab.TryPickBar(out _,out _,out _),"non-dominant hand cannot start a hidden second menu laser");
        Aim(hand,point-Vector3.forward*7);Check(hand.RayGrab.TryPickBar(out var distant,out _,out _)&&ReferenceEquals(distant,handle),"ordinary twenty-metre laser reach still admits a seven-metre menu handle");
        Aim(hand,origin);
        var blocker=Keep(GameObject.CreatePrimitive(PrimitiveType.Cube));blocker.name="independent native solid blocker";blocker.layer=0;
        blocker.transform.position=origin+Vector3.forward*.5f;blocker.transform.localScale=Vector3.one*.12f;Physics.SyncTransforms();
        hand.Ray.CaptureNativePhysics(origin,(point-origin).normalized);
        Check(hand.Ray.Current.HasHit&&ReferenceEquals(hand.Ray.Current.HitCollider,blocker.GetComponent<Collider>())&&!hand.RayGrab.TryPickBar(out _,out _,out _),"native solid geometry still blocks menu pickup after mod-layer exclusion");
        blocker.SetActive(false);Physics.SyncTransforms();
        int normal=hand.Ray.Mask.value,offhandNormal=offhand.Ray.Mask.value;
        Controller.Instance=new Controller{m_ActiveSelectionRaycastLayer=1<<8};
        typeof(GloomhavenVR.Board.BoardDriver).GetMethod("SyncRayMask",Flags)!.Invoke(null,null);
        Check(hand.Ray.Mask.value==(1<<8)&&offhand.Ray.Mask.value==(1<<8),"original scenario selection-mask author overrides both hands verbatim");
        hand.Ray.Mask=normal;offhand.Ray.Mask=offhandNormal;
        var map=new GloomhavenVR.WorldUI.MapRoom.MapLocationInteractor();Invoke(map,"ApplyMask");
        Check(hand.Ray.Mask.value==(1<<15)&&offhand.Ray.Mask.value==(1<<15),"original map selection-mask author remains authoritative over both hands");
        map.ReleaseMaskOnly("return to menu");
        Check(hand.Ray.Mask.value==normal&&offhand.Ray.Mask.value==offhandNormal,"actual map mask handback restores each prior native-only menu mask");
        Controller.Instance=null;
        VRHands.Left=VRHands.Right=null;var beforeHands=new GloomhavenVR.WorldUI.MapRoom.MapLocationInteractor();Invoke(beforeHands,"ApplyMask");
        VRHands.Left=offhand;VRHands.Right=hand;Invoke(beforeHands,"ApplyMask");
        Check(hand.Ray.Mask.value==(1<<15)&&offhand.Ray.Mask.value==(1<<15),"map arriving before tracked hands still applies native location mask on hand arrival");
        beforeHands.ReleaseMaskOnly("hands arrived during map");
        Check(hand.Ray.Mask.value==normal&&offhand.Ray.Mask.value==normal,"missing-hand map fallback restores the same native-only default on return to menu");
        Aim(hand,origin);Time.timeScale=savedClock;
    }
    private static IEnumerable<int> LaserMotion(FlatScreen window,VRHand hand,PanelGrabHandle handle,Transform frame,string output,string phase)
    {
        float savedClock=Time.timeScale;Time.timeScale=0;Physics.SyncTransforms();
        Vector3 point=handle.BarCollider!.bounds.center,origin=point-Vector3.forward*(1.5f*hand.WorldScale);
        hand.transform.SetPositionAndRotation(origin,Quaternion.LookRotation(point-origin));
        var direction=(point-origin).normalized;
        bool hit=Physics.Raycast(origin,direction,out RaycastHit native,20*hand.WorldScale,Physics.DefaultRaycastLayers);
        hand.Ray.CaptureNativePhysics(origin,direction);VRHands.Primary=hand;VRHands.PrimaryPick=hand.Ray;
        hand.Ray.HasFreshUiHit=hand.Ray.FreshUiHitIsPanelHoverOnly=hand.Ray.HasFreshTriggerClaim=false;hand.GripPressed=false;
        bool pickable=hand.RayGrab.TryPickBar(out var picked,out var pickedPoint,out float pickedRange);
        hand.TriggerDown=hand.TriggerPressed=true;hand.RayUgui.Tick();
        File.WriteAllText(Path.Combine(output,"motion-start-"+phase+".txt"),$"point={point};barTransform={handle.BarCollider.transform.position};barBounds={handle.BarCollider.bounds};zone={handle.GetComponent<Collider>().bounds};hit={hit};native={native.collider};nativeDistance={native.distance:R};ready={MenuWorldContext.IsReady};ray={hand.Ray.Active};primary={VRHands.Primary==hand};enabled={hand.Grabber.Enabled};canGrab={handle.CanGrab};pickable={pickable};picked={picked};range={pickedRange};uguiHit={hand.RayUgui.HasHit};uguiDistance={hand.RayUgui.HitDistance};uguiPress={hand.RayUgui.IsPressing};offer={hand.Grabber.TriggerGrabOffered};figureOffer={hand.Grabber.FigureGrabOffered}\n");
        hand.RayGrab.Tick();hand.Grabber.Tick();hand.TriggerDown=false;
        File.WriteAllLines(Path.Combine(output,"motion-start-log-"+phase+".txt"),VRLog.Lines);
        Check(ReferenceEquals(hand.Grabber.Held,handle)&&handle.LastGrabWasLaser,"paused menu laser gesture engages after "+phase);
        var quad=Get<GameObject>(window,"_quad");Vector3 frameStart=frame.position,quadStart=quad.transform.position,palm=hand.Rig.PalmCenter.position;
        Quaternion rotation=frame.rotation;float scale=frame.localScale.x;
        hand.transform.rotation=Quaternion.AngleAxis(18,Vector3.up)*hand.transform.rotation;
        foreach(var step in PlayerSteps(.3f,()=>{hand.RayGrab.Tick();Invoke(window,"FollowHead");},output,"laser-sweep-"+phase,
            ()=>$"held={hand.Grabber.Held};root={frame.position};quad={quad.transform.position};distance={Get<float>(handle,"_carryDistance"):R}"))yield return 0;
        Check(Vector3.Distance(frame.position,frameStart)>.15f*hand.WorldScale,"paused menu follows changed laser aim with stationary palm after "+phase);
        Near(hand.Rig.PalmCenter.position,palm,"laser sweep leaves actual palm at fixed origin after "+phase);
        Near(quad.transform.position-quadStart,frame.position-frameStart,"original captured menu moves with laser-carried owner after "+phase);
        Near(frame.localScale.x,scale,"laser carry preserves existing manual menu scale after "+phase);
        Near(Quaternion.Angle(frame.rotation,rotation),0,"laser sweep translates without rotating menu owner after "+phase);
        float distance=Get<float>(handle,"_carryDistance");hand.Thumbstick=Vector2.up;Vector3 beforeOut=frame.position;
        foreach(var step in PlayerSteps(.25f,()=>{hand.RayGrab.Tick();Invoke(window,"FollowHead");},output,"laser-reel-out-"+phase,
            ()=>$"distance={Get<float>(handle,"_carryDistance"):R};root={frame.position}"))yield return 0;
        float farther=Get<float>(handle,"_carryDistance");
        Check(farther-distance>.08f*hand.WorldScale&&Vector3.Dot(frame.position-beforeOut,hand.transform.forward)>.04f*hand.WorldScale,"paused native menu stick reel actually pushes the carried window farther after "+phase);
        hand.Thumbstick=Vector2.down;Vector3 beforeIn=frame.position;
        foreach(var step in PlayerSteps(.25f,()=>{hand.RayGrab.Tick();Invoke(window,"FollowHead");},output,"laser-reel-in-"+phase,
            ()=>$"distance={Get<float>(handle,"_carryDistance"):R};root={frame.position}"))yield return 0;
        Check(farther-Get<float>(handle,"_carryDistance")>.08f*hand.WorldScale&&Vector3.Dot(frame.position-beforeIn,hand.transform.forward)<-.04f*hand.WorldScale,"paused native menu stick reel actually pulls the carried window nearer after "+phase);
        hand.Thumbstick=Vector2.zero;hand.TriggerPressed=false;hand.TriggerUp=true;hand.RayGrab.Tick();hand.Grabber.Tick();
        Check(hand.Grabber.Held==null&&!handle.IsGrabbed,"laser release retires carry and keeps manipulated menu after "+phase);
        int released=Time.frameCount;while(Time.frameCount==released)yield return 0;
        hand.TriggerUp=false;hand.Ray.HasFreshUiHit=hand.Ray.FreshUiHitIsPanelHoverOnly=hand.Ray.HasFreshTriggerClaim=false;
        Time.timeScale=savedClock;
    }
    private static Texture2D Capture(Camera head)
    {
        var target=new RenderTexture(320,240,24,RenderTextureFormat.ARGB32);target.Create();var saved=head.targetTexture;var active=RenderTexture.active;
        var result=new Texture2D(320,240,TextureFormat.RGBA32,false,true);
        try{head.targetTexture=target;head.Render();RenderTexture.active=target;result.ReadPixels(new Rect(0,0,320,240),0,0);result.Apply(false,false);return result;}
        finally{head.targetTexture=saved;RenderTexture.active=active;target.Release();Object.DestroyImmediate(target);}
    }
    private static void Pixels(Camera head,GameObject actual,GameObject reference,string output,string name)
    {
        actual.SetActive(true);reference.SetActive(false);var image=Capture(head);
        actual.SetActive(false);reference.SetActive(true);var expected=Capture(head);actual.SetActive(true);reference.SetActive(false);
        try
        {
            var a=image.GetPixels32();var b=expected.GetPixels32();int changed=0,ink=0;
            for(int i=0;i<a.Length;i++){if(a[i].r!=b[i].r||a[i].g!=b[i].g||a[i].b!=b[i].b)changed++;if(a[i].r+a[i].g+a[i].b>60)ink++;}
            Check(ink>3000,"world main menu contributes actual final head pixels");
            Check(changed==0,"moving head sees the same fixed world-menu pixels as an independent world geometry reference");
            File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());
        }
        finally{Object.DestroyImmediate(image);Object.DestroyImmediate(expected);}
    }
    public static IEnumerator Run(string output)
    {
        checks=0;VRSession.IsRunning=true;MenuWorldContext.Reset();Time.timeScale=1;
        MainMenuUIManager.Instance=null;Singleton<MapChoreographer>.IsInitialized=false;VRModeStateMachine.TableInFrontOfPlayer=false;
        var rig=Keep(new GameObject("actual menu tracking root"));var headGo=new GameObject("tracked head",typeof(Camera));headGo.transform.SetParent(rig.transform,false);
        var head=headGo.GetComponent<Camera>();head.enabled=false;head.cullingMask=VRLayers.ModLayerMask;head.clearFlags=CameraClearFlags.SolidColor;head.backgroundColor=Color.black;
        head.nearClipPlane=.01f;head.farClipPlane=100;head.fieldOfView=75;head.aspect=4f/3;
        var driver=rig.AddComponent<VRRigDriver>();driver.Configure(rig,head,new Vector3(0,1,-10),Quaternion.identity);
        MenuWorldContext.Tick(true,false);Check(!MenuWorldContext.IsReady&&!MenuWorldContext.AllowsSurroundings,"boot rig alone cannot admit world-menu surroundings");
        var managerGo=Keep(new GameObject("native main menu manager"));var manager=managerGo.AddComponent<MainMenuUIManager>();MainMenuUIManager.Instance=manager;
        manager.MainMenuCamera=Keep(new GameObject("native main menu camera"));manager.mainMenu=Keep(new GameObject("native main menu",typeof(RectTransform),typeof(UIWindow))).AddComponent<UIMainOptionsMenu>();
        var window=manager.mainMenu.GetComponent<UIWindow>();manager.Initialised=true;
        MenuWorldContext.Tick(true,false);Check(!MenuWorldContext.IsReady,"intro and untracked startup do not become a world menu");
        window.IsVisible=true;Invoke(driver,"TickArrival");
        Check(!MenuWorldContext.IsReady,"shown menu before tracked recenter cannot admit a provisional world pose");
        window.IsVisible=false;Put(driver,"_pendingRecenter",false);MenuWorldContext.Tick(true,true);
        Check(!MenuWorldContext.IsReady,"EULA or sign-in initialization does not stand in for the actual visible main menu");
        var left=Keep(new GameObject("native tracked left input")).AddComponent<VRHand>();left.Configure(HandSide.Left);
        var right=Keep(new GameObject("native tracked right input")).AddComponent<VRHand>();right.Configure(HandSide.Right);
        left.transform.SetParent(rig.transform,false);right.transform.SetParent(rig.transform,false);
        VRHands.Left=left;VRHands.Right=right;VRHands.Primary=right;
        var handsDriver=new HandsDriver(left,right);Invoke(handsDriver,"Update");
        Check(!left.Grabber.Enabled&&!right.Grabber.Enabled,"actual native hand mask keeps near-grab policy off during intro");
        var flight=Keep(new GameObject("actual stick flight")).AddComponent<Flight>();flight.enabled=false;
        var turn=Keep(new GameObject("actual stick turn")).AddComponent<SnapTurn>();turn.enabled=false;
        var world=Keep(new GameObject("actual stick world grab")).AddComponent<WorldGrab>();world.enabled=false;
        Vector3 introPosition=rig.transform.position;Quaternion introRotation=rig.transform.rotation;
        left.Thumbstick=Vector2.up;right.Thumbstick=Vector2.right;left.ThumbstickClick=left.ThumbstickClickDown=true;
        Invoke(flight,"Update");Invoke(turn,"Update");Invoke(world,"Update");
        Check(rig.transform.position==introPosition&&rig.transform.rotation==introRotation&&!world.IsGrabbing,"actual flight turn and world-grab stay inactive behind the intro");
        left.Thumbstick=right.Thumbstick=Vector2.zero;left.ThumbstickClick=left.ThumbstickClickDown=false;
        head.transform.localPosition=new Vector3(.11f,1.01f,.10f);rig.transform.position=new Vector3(0,1,-10);
        Put(driver,"_pendingRecenter",true);window.IsVisible=true;Invoke(driver,"TickArrival");
        Near(head.transform.position,new Vector3(0,1,-10),"actual menu recenter resolves the final ready eye before room/window spawn");
        Check(MenuWorldContext.IsReady&&MenuWorldContext.AllowsSurroundings,"first actual shown menu admits the recentered world place");uint opening=MenuWorldContext.Opening;
        var oldController=Keep(new GameObject("retained native gameplay camera controller")).AddComponent<CameraController>();oldController.FocusPoint=new Vector3(0,40,0);CameraController.s_CameraController=oldController;
        ComfortSettings.FreeMovement.Value=false;Vector3 unclamped=rig.transform.position;RigClamp.Apply(rig.transform);
        Check(rig.transform.position==unclamped&&!RigClamp.LastClampActive,"ready world menu ignores a retained gameplay-table clamp even with saved free movement disabled");
        ComfortSettings.FreeMovement.Value=true;CameraController.s_CameraController=null!;
        Invoke(handsDriver,"Update");Check(left.Grabber.Enabled&&right.Grabber.Enabled,"actual HandsDriver readiness edge enables both real near-grabbers without a mode transition");
        Check(!left.Ray.Enabled&&right.Ray.Enabled,"near-grab readiness retains the single dominant laser policy");
        manager.mainMenu.gameObject.SetActive(false);MenuWorldContext.Tick(true,true);
        Check(MenuWorldContext.IsReady&&MenuWorldContext.Opening==opening,"main-menu subpages retain the same ready opening after the root menu hides");manager.mainMenu.gameObject.SetActive(true);
        var texture=Keep(new Texture2D(64,36,TextureFormat.RGBA32,false,true));
        for(int y=0;y<36;y++)for(int x=0;x<64;x++)texture.SetPixel(x,y,(x/4+y/3)%2==0?new Color(.95f,.7f,.15f):new Color(.1f,.4f,.8f));texture.Apply();texture.filterMode=FilterMode.Point;
        var material=Keep(new Material(Shader.Find("Unlit/Texture")));material.mainTexture=texture;
        var quad=Quad("original captured native menu picture",material);
        var rt=Keep(new RenderTexture(1920,1080,0));rt.Create();
        screen=new FlatScreen();Put(screen,"_quad",quad);Put(screen,"_quadRenderer",quad.GetComponent<Renderer>());Put(screen,"_rt",rt);Put(screen,"_coverageOwner",screen);
        Invoke(screen,"FollowHead");var frame=((IPanelGrabOwner)Get<SurfaceGrabBar>(screen,"_worldMenuGrab")).GrabRoot??throw new Exception("missing production world-menu frame");
        Check(Get<GameObject>(screen,"_worldMenuHost")!=null,"actual flat-screen coordinator mounts the menu on the standard world-window grab frame");
        var rear=Get<GameObject>(screen,"_worldMenuRear");var rearMesh=Get<Mesh>(screen,"_worldMenuRearMesh");
        Check(rear!=null&&rearMesh!=null&&rear.transform.parent==quad.transform,"actual menu coordinator creates one owned rear mesh under the original capture");
        Check(ReverseMesh(quad.GetComponent<MeshFilter>().sharedMesh,rearMesh!),"rear mesh reverses native face winding while keeping native UVs");
        Check(!rear!.GetComponent<Renderer>().enabled,"unadmitted backdrop material keeps its prepared reverse surface hidden");
        Vector3 initialRear=rear.transform.position;
        Near(quad.transform.TransformVector(Vector3.right).magnitude,2,"initial world menu preserves native captured screen width");
        Near(quad.transform.position,head.transform.position+Vector3.forward*1.6f,"actual first placement uses the recentered eye");
        var handle=frame.GetComponent<PanelGrabHandle>()??throw new Exception("missing production world-menu handle");Check(handle.CanGrab,"real grab handle accepts a visible menu owner without a HostCanvas");
        foreach(var step in SharedController746(left,right,head,output))yield return null;
        LaserRoute(screen,right,left,handle,output);
        foreach(var step in LaserMotion(screen,right,handle,frame,output,"fresh-open"))yield return null;
        initialRear=rear.transform.position;
        var reference=Quad("independent fixed menu geometry reference",material);reference.transform.SetPositionAndRotation(quad.transform.position,quad.transform.rotation);reference.transform.localScale=new Vector3(2,2/(16f/9),1);reference.SetActive(false);
        Vector3 position=quad.transform.position;Quaternion rotation=quad.transform.rotation;float width=quad.transform.TransformVector(Vector3.right).magnitude;
        // Exclude chrome from the head-image comparison only: its actual colliders,
        // frame and carry code stay live and are exercised below.
        foreach(var renderer in frame.GetComponentsInChildren<Renderer>())renderer.gameObject.layer=0;
        // The real locomotion writers move only the rig. The independent menu and
        // its separate geometry reference retain their original world metric.
        Time.timeScale=0;left.Thumbstick=Vector2.up;Vector3 travelStart=rig.transform.position;
        foreach(var step in PlayerSteps(.12f,()=>{Invoke(flight,"Update");Invoke(screen,"FollowHead");},output,"flight",
            ()=>$"ready={MenuWorldContext.IsReady};stick={left.Thumbstick};flying={flight.IsFlying};step={flight.LastStepMeters:R};rig={rig.transform.position}"))yield return null;
        left.Thumbstick=Vector2.zero;
        File.WriteAllLines(Path.Combine(output,"flight-log.txt"),VRLog.Lines);
        Check(Vector3.Distance(rig.transform.position,travelStart)>.02f,"actual ready-menu stick flight moves the player while native time is paused");
        Near(quad.transform.position,position,"actual stick flight retains the independent menu's world pose");
        Pixels(head,quad,reference,output,"actual-flight");
        Vector3 turnEye=head.transform.position;Quaternion turnBefore=rig.transform.rotation;
        right.Thumbstick=Vector2.right;Invoke(turn,"Update");right.Thumbstick=Vector2.zero;Invoke(turn,"Update");Invoke(screen,"FollowHead");
        Near(Quaternion.Angle(rig.transform.rotation,turnBefore),15,"actual ready-menu snap turn rotates the live rig by its configured step");
        Near(head.transform.position,turnEye,"actual menu snap turn keeps the tracked eye pivot fixed");
        Near(quad.transform.position,position,"actual snap turning retains the independent menu's world pose");
        left.transform.localPosition=new Vector3(-.2f,.8f,.2f);right.transform.localPosition=new Vector3(.2f,.8f,.2f);
        left.ThumbstickClick=left.ThumbstickClickDown=true;Invoke(world,"Update");left.ThumbstickClickDown=false;
        Vector3 dragStart=rig.transform.position;left.transform.localPosition+=new Vector3(.12f,.04f,-.02f);
        foreach(var step in PlayerSteps(.4f,()=>{Invoke(world,"Update");Invoke(screen,"FollowHead");},output,"world-drag",
            ()=>$"grabbing={world.IsGrabbing};rig={rig.transform.position}"))yield return null;
        Check(world.IsGrabbing&&Vector3.Distance(rig.transform.position,dragStart)>.05f,"actual ready-menu stick-click world drag moves the live rig");
        Near(quad.transform.position,position,"actual world dragging retains the independent menu's world pose");
        right.ThumbstickClick=right.ThumbstickClickDown=true;Invoke(world,"Update");right.ThumbstickClickDown=false;
        float zoomBefore=rig.transform.localScale.x;Vector3 mid=(left.transform.localPosition+right.transform.localPosition)*.5f;
        Vector3 half=(right.transform.localPosition-left.transform.localPosition)*.5f;
        left.transform.localPosition=mid-half*.75f;right.transform.localPosition=mid+half*.75f;
        foreach(var step in PlayerSteps(.4f,()=>{Invoke(world,"Update");Invoke(screen,"FollowHead");},output,"world-pinch",
            ()=>$"twoHand={world.IsTwoHand};scale={rig.transform.localScale.x:R}"))yield return null;
        Check(world.IsTwoHand&&rig.transform.localScale.x>zoomBefore*1.1f,"actual menu two-stick world pinch changes the player scale");
        Near(quad.transform.TransformVector(Vector3.right).magnitude,width,"actual world pinch preserves the menu's absolute world width");
        Pixels(head,quad,reference,output,"actual-world-pinch");
        left.ThumbstickClick=right.ThumbstickClick=false;Invoke(world,"Update");Time.timeScale=1;
        for(int pose=0;pose<6;pose++)
        {
            rig.transform.SetPositionAndRotation(new Vector3(pose*.035f,pose*.01f,-pose*.025f),Quaternion.Euler(0,pose*3,0));
            // Use motion relative to the initial recentered rig rather than a test
            // camera projection override; target image is rendered from the live head.
            rig.transform.position+=new Vector3(-.11f,-.01f,-10.10f);
            rig.transform.localScale=Vector3.one*(pose%2==0?1f:1.15f);
            Invoke(screen,"FollowHead");Near(quad.transform.position,position,"world menu does not follow walking, turning or player zoom");
            Near(Quaternion.Angle(quad.transform.rotation,rotation),0,"world menu retains its yaw under player motion");
            Near(quad.transform.TransformVector(Vector3.right).magnitude,width,"world menu absolute size is independent of rig zoom");
            Pointer(screen,right,new Vector2(.25f,.68f));Pixels(head,quad,reference,output,"world-pose-"+pose);yield return null;
        }
        Near(rear.transform.position,initialRear,"walking turning and player zoom do not move the prepared fixed rear surface");
        Put(screen,"_screenMaterial",material);Invoke(screen,"TickWorldMenuBackdrop");
        Check(rear.GetComponent<Renderer>().enabled&&ReferenceEquals(rear.GetComponent<Renderer>().sharedMaterial,material),"actual ready backdrop enables the reverse surface with the original capture material");
        Check(material.shader.name=="GloomhavenVR/Overlay","actual world-menu backing executes the unchanged shipped Overlay shader");
        Near(rear.transform.position,quad.transform.position,"single capture rear surface stays exactly coplanar with its original picture");
        // A split compositor can create its background after the ready-menu host.
        var back=Quad("late original split background",material);Put(screen,"_backQuad",back);Invoke(screen,"FollowHead");
        Check(back.transform.parent==quad.transform,"late split background joins the same physical world-menu frame");
        Near(Vector3.Dot(back.transform.position-quad.transform.position,quad.transform.forward),.06f,"late split background retains the physical six-centimetre gap");
        Near(Vector3.Dot(rear.transform.position-quad.transform.position,quad.transform.forward),-.06f,"late split creation seats the reverse backing behind the glass from the rear");
        Invoke(screen,"RestoreWorldMenuBackdrop");Check(!rear.GetComponent<Renderer>().enabled,"restoring the intro material hides the reverse world-menu surface");
        Invoke(screen,"TickWorldMenuBackdrop");Check(rear.GetComponent<Renderer>().enabled,"same ready opening reactivates its existing owned reverse mesh");
        back.SetActive(false);reference.SetActive(false);
        rig.transform.localScale=Vector3.one;Invoke(driver,"RecenterMenu");Invoke(screen,"FollowHead");
        Physics.SyncTransforms();var zone=frame.GetComponent<BoxCollider>();Vector3 palm=zone.transform.TransformPoint(zone.center);
        left.transform.position=palm+Vector3.left*.15f;right.transform.position=palm+Vector3.right*.15f;Physics.SyncTransforms();
        left.GripDown=left.GripPressed=true;left.Grabber.Tick();left.GripDown=false;
        Check(ReferenceEquals(left.Grabber.Held,handle),"actual proximity election takes the standard menu grab bar on a palm grip");
        Vector3 beforeMove=quad.transform.position,delta=new(.24f,.08f,.04f);left.transform.position+=delta;
        Time.timeScale=0;
        float carryUntil=Time.unscaledTime+.65f;
        // Editor update callbacks are not player frames. Let the real Unity
        // MonoBehaviour.Update/LateUpdate carry for an actual unscaled interval.
        while(Time.unscaledTime<carryUntil){Invoke(screen,"FollowHead");yield return null;}
        Invoke(screen,"FollowHead");
        Near(quad.transform.position,beforeMove+delta,"paused native menu follows actual one-hand carry on the unscaled tracking clock",.005f);
        right.transform.position=zone.transform.TransformPoint(zone.center)+Vector3.right*.15f;Physics.SyncTransforms();right.GripDown=right.GripPressed=true;right.Grabber.Tick();right.GripDown=false;
        Check(ReferenceEquals(right.Grabber.Held,handle),"second real proximity grip joins the same menu frame for resize");
        Vector3 pair=(left.transform.position+right.transform.position)*.5f;Vector3 spread=(right.transform.position-left.transform.position)*.5f;
        float scaleBefore=frame.localScale.x;left.transform.position=pair-spread*1.45f;right.transform.position=pair+spread*1.45f;
        carryUntil=Time.unscaledTime+.65f;
        while(Time.unscaledTime<carryUntil){Invoke(screen,"FollowHead");yield return null;}
        Invoke(screen,"FollowHead");
        Near(frame.localScale.x,scaleBefore*1.45f,"paused native menu supports actual two-hand scale",.005f);
        Near(quad.transform.TransformVector(Vector3.right).magnitude,2*frame.localScale.x,"menu capture and grab frame apply pinch scale exactly once",.01f);
        Check(!right.Ray.Active&&!left.Ray.Active,"near-held menu suppresses both hands pointer until release");
        right.GripPressed=false;right.Grabber.Tick();left.GripPressed=false;left.Grabber.Tick();Time.timeScale=1;
        foreach(var step in LaserMotion(screen,right,handle,frame,output,"palm-move-and-two-hand-resize"))yield return null;
        Pointer(screen,right,new Vector2(.8f,.4f));
        Invoke(screen,"FollowHead");position=quad.transform.position;rotation=quad.transform.rotation;width=quad.transform.TransformVector(Vector3.right).magnitude;
        // Movie/options presentation releases the wrapper, while its original
        // capture survives. The same opening must retain its absolute world size.
        rig.transform.localScale=Vector3.one*2;int releaseFrame=Time.frameCount;Invoke(screen,"ReleaseWorldMenu");
        Check(Get<GameObject>(screen,"_worldMenuRear")==null&&Get<Mesh>(screen,"_worldMenuRearMesh")==null,"menu presentation release immediately clears both reverse-surface owners");
        while(Time.frameCount==releaseFrame)yield return null;
        Check(rear==null&&rearMesh==null,"closing menu presentation destroys its owned reverse mesh and surface");
        Invoke(screen,"FollowHead");
        rear=Get<GameObject>(screen,"_worldMenuRear");rearMesh=Get<Mesh>(screen,"_worldMenuRearMesh");
        Check(rear!=null&&rearMesh!=null&&ReverseMesh(quad.GetComponent<MeshFilter>().sharedMesh,rearMesh),"same-opening recovery rebuilds exactly one correctly wound reverse surface");
        Near(quad.transform.position,position,"same-opening movie/recovery round trip retains the player-moved world pose");
        Near(Quaternion.Angle(quad.transform.rotation,rotation),0,"same-opening round trip retains the chosen window yaw");
        Near(quad.transform.TransformVector(Vector3.right).magnitude,width,"same-opening movie round trip retains absolute window size after independent world zoom");
        var owner=Get<ConvertedPanel>(screen,"_worldMenuPanel");
        var nativeCanvas=Keep(new GameObject("original native camera canvas",typeof(RectTransform),typeof(Canvas))).GetComponent<Canvas>();nativeCanvas.renderMode=RenderMode.ScreenSpaceCamera;
        var nativeCamera=Keep(new GameObject("original native captured camera",typeof(Camera))).GetComponent<Camera>();nativeCanvas.worldCamera=nativeCamera;
        var source=Keep(new GameObject("native confirmation producer",typeof(RectTransform)));source.transform.SetParent(nativeCanvas.transform,false);
        Check(!FlatScreen.TryGetMenuOwner(source.transform,out _),"uncaptured original camera cannot accidentally become a menu confirmation owner");
        // Both null render textures were previously equal, which falsely admitted
        // a captured camera before an actual native target existed.
        Invoke(screen,"EnrollCamera",nativeCamera);Put(screen,"_uiRt",null);nativeCamera.targetTexture=null;
        Check(!FlatScreen.TryGetMenuOwner(source.transform,out _),"captured camera with null target is not proof of menu ownership");
        nativeCamera.targetTexture=rt;Check(FlatScreen.TryGetMenuOwner(source.transform,out var found)&&ReferenceEquals(found,owner),"confirmation owner resolves the actual captured native camera and current menu host");
        Get<GameObject>(screen,"_worldMenuHost").SetActive(false);
        Check(!FlatScreen.TryGetMenuOwner(source.transform,out _)&&!FlatScreen.IsMenuOwner(owner),"hidden menu host cannot own a visible native confirmation");Get<GameObject>(screen,"_worldMenuHost").SetActive(true);
        manager.MainMenuCamera.SetActive(false);Check(!MenuWorldContext.IsReady&&!MenuWorldContext.AllowsSurroundings,"native menu camera loss revokes the live world place");
        manager.MainMenuCamera.SetActive(true);Check(MenuWorldContext.IsReady,"temporary subpage camera restoration retains the same opening");
        int lossFrame=Time.frameCount;Object.Destroy(quad);while(Time.frameCount==lossFrame)yield return null;
        screen.TickCaptureLifetime();lossFrame=Time.frameCount;while(Time.frameCount==lossFrame)yield return null;
        Check(Get<GameObject>(screen,"_worldMenuHost")==null&&Get<Mesh>(screen,"_worldMenuRearMesh")==null&&rearMesh==null,
            "external original capture loss retires its wrapper and owned reverse mesh");
        Vector3 tracked=head.transform.localPosition;head.transform.localPosition=Vector3.zero;Put(driver,"_pendingRecenter",true);Invoke(driver,"TickArrival");Invoke(handsDriver,"Update");
        Check(!MenuWorldContext.IsReady&&!left.Grabber.Enabled&&!right.Grabber.Enabled,"tracking reset revokes ready place and both real hand masks");
        head.transform.localPosition=tracked;Invoke(driver,"TickArrival");Check(MenuWorldContext.Opening!=opening,"fresh settled arrival owns a new menu opening");
        var replacement=Keep(new GameObject("replacement live rig"));var newHeadGo=new GameObject("replacement tracked head",typeof(Camera));newHeadGo.transform.SetParent(replacement.transform,false);
        var nextDriver=replacement.AddComponent<VRRigDriver>();nextDriver.Configure(replacement,newHeadGo.GetComponent<Camera>(),Vector3.zero,Quaternion.identity);
        Check(!MenuWorldContext.IsReady,"actual rig/head replacement cannot inherit a previous ready menu pose");MenuWorldContext.Tick(false,true);Check(!MenuWorldContext.IsReady,"a non-menu rig cannot reopen the menu latch");
        oldController.FocusPoint=new Vector3(0,4,0);CameraController.s_CameraController=oldController;ComfortSettings.FreeMovement.Value=false;RigClamp.Apply(replacement.transform);
        Check(RigClamp.LastClampActive,"outside the menu original native table eye clamps still apply");Near(newHeadGo.transform.position.y,4.1f,"non-menu clamp retains its original native focus metric");
        ComfortSettings.FreeMovement.Value=true;CameraController.s_CameraController=null!;
        VRSession.IsRunning=false;Check(!MenuWorldContext.AllowsSurroundings&&!MenuWorldContext.IsReady,"VR stop revokes all menu-world presentation admission");
        File.WriteAllText(Path.Combine(output,"limits.txt"),"Whole production MenuWorldContext, WorldMenu, Placement/FollowHead, SurfaceGrabBar, PanelGrabHandle, ProximityGrabber, RayGrabDriver, LaserPointerPolicy, registry, rod geometry/tween, Flight, WorldGrab, SnapTurn and their actual resolvers/wedges execute in Unity2021.3.5/OpenGL. Original RecenterMenu, RigClamp, hand-mask policy, HandsDriver readiness Update/ApplyMode, VRHand.SetInteractorMask, native physics mask initializer and Physics.Raycast, RayUgui Tick/TickPressed/canvas geometry/Cancel, pointer TickPointer/YieldScreenPointer/HideReticle, original scenario/map mask authors and map restoration, and canvas order/hide registration are exact source-extracted bodies; their input/classification/visual-build/native callback boundaries are declared fixture ports. MainMenuUIManager/UIWindow visibility, tracking samples, native art/camera-target admission, config storage and UI callbacks are ports. Unrelated scenario/tilt/scroll claims are explicit inactive ports. Final head pixels compare a synthetic captured-native-art texture against separately placed world geometry. Native main-menu art/controller scheduling, actual OpenXR input, D3D11 and headset picture/frame pacing are not certified.\n");
        yield return checks;
    }
    public static void Cleanup()
    {
        Time.timeScale=1;if(screen!=null)Invoke(screen,"ReleaseWorldMenu");screen=null;MenuWorldContext.Reset();
        VRHands.Left?.Grabber.CancelAll();VRHands.Right?.Grabber.CancelAll();VRHands.Left=VRHands.Right=VRHands.Primary=null;VRHands.PrimaryPick=null;
        foreach(var value in Owned)if(value!=null)Object.DestroyImmediate(value);Owned.Clear();
        VRRigDriver.Instance=null;MainMenuUIManager.Instance=null;VRInteractables.Grabbables.Clear();VRInteractables.Pokeables.Clear();
    }
}
