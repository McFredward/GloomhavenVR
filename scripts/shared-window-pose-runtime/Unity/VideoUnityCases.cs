using System;
using System.Collections;
using System.IO;
using GloomhavenVR.Net;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;
using Time = FixtureClock;

public static class InteractionProgram
{
    public static int Checks;
    public static string Metrics = "complete video coordinator, actual shared reveal/gesture/resolver and Unity frame/Canvas pixels";
    private static void Check(bool pass,string message) { Checks++;if(!pass)throw new Exception(message); }
    private static string Captures
    {
        get
        {
            string[] args=Environment.GetCommandLineArgs();
            string directory=Path.Combine(Path.GetDirectoryName(args[Array.IndexOf(args,"-interactionManifest")+1])!,"captures",typeof(InteractionProgram).Assembly.GetName().Name!);
            Directory.CreateDirectory(directory);return directory;
        }
    }
    private readonly struct Picture
    {
        internal readonly float Center;
        internal readonly int Area;
        internal Picture(float center,int area) {Center=center;Area=area;}
    }
    private static Picture Render(Camera camera,string name)
    {
        var target=new RenderTexture(512,384,24);camera.targetTexture=target;
        Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;
        var picture=new Texture2D(512,384,TextureFormat.RGBA32,false);picture.ReadPixels(new Rect(0,0,512,384),0,0);picture.Apply();
        File.WriteAllBytes(Path.Combine(Captures,name+".png"),picture.EncodeToPNG());
        Color32[] pixels=picture.GetPixels32();int green=0;long xsum=0;
        for(int i=0;i<pixels.Length;i++)if(pixels[i].g>120&&pixels[i].r<100&&pixels[i].b<130){green++;xsum+=i%512;}
        camera.targetTexture=null;RenderTexture.active=null;UnityEngine.Object.DestroyImmediate(picture);UnityEngine.Object.DestroyImmediate(target);
        return new Picture(green>0?(float)xsum/green:0,green);
    }
    public static IEnumerator Run()
    {
        foreach(bool review in new[]{false,true})
        foreach(bool map in new[]{false,true})
        foreach(int mover in new[]{1,3})
        {
            string name=(review?"review":"native")+"-"+(map?"map":"scenario")+"-"+mover;
            RemoteVideoPlayback.Reset();NativeVideoWindow.NativePlayer=null;NativeVideoWindow.ReviewPlayer=null;
            NativeVideoWindow.AssetAvailable=true;NativeVideoWindow.ResolvedKey=string.Empty;
            Proof.Configure(2);MapRoomDriver.Active=map;WorldUIConfig.CanvasScaleMm.Value=.01f;
            SharedWindowMoves.Reset();SharedWindowMoves.Bind(()=>true);
            var grab=SharedWindows.Grabs[SharedWindowKind.Video];
            SharedWindowSpawnBridge.InitialAuthor=()=>false;
            SharedWindowSpawnBridge.TryInitialPose=ReadInitial;
            NativeVideoWindow.BindPhysical(grab);
            const uint token=11;uint key=unchecked(1u*16777619u)^token;
            // First coordinator tick has no remote content/pose. The real physical reveal must
            // remain pending until the later current-opening72+119+121+123 cohort arrives.
            Check(!NativeVideoWindow.RevealPhysical()&&!NativeVideoWindow.SharedInitialPoseReady,
                "actual native video cannot reveal before its late first pose");
            var empty=new SharedWindowEntry();
            var publisher=new SharedWindowMoveRegister(1,5);publisher.Open(key,1);Check(publisher.Sample(out var opening),"actual video publisher announces initial scope");
            string clip=review?"Review/CP_Intro/GH_CP_Intro.sbs-v2.mov":"CP_Intro/GH_CP_Intro.sbs-v2.mov";
            var initial=new RigPose {Position=new Vector3(0,0,4),Rotation=Quaternion.identity};
            VideoCaseHelpers.Receive(1,VideoCaseHelpers.Packet(1,token,1,clip,initial,1,true,opening),2);
            Check(!NativeVideoWindow.RevealPhysical(),"actual first coordinator tick remains pending before local identity sample");
            RemoteMapStory.SampleVideoPose(key,false,ref empty);
            Check(publisher.Sample(out opening),"late actual heartbeat retains the live opening after local discovery");
            VideoCaseHelpers.Receive(1,VideoCaseHelpers.Packet(1,token,2,clip,initial,1,true,opening),2);VideoCaseHelpers.Tick(2,.01f);
            Check(NativeVideoWindow.RevealPhysical()&&NativeVideoWindow.SharedInitialPoseReady,
                "actual native video reveal receives coordinator key and applies the original initial reader");
            var camera=new GameObject("final observer head camera").AddComponent<Camera>();
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
            camera.nearClipPlane=.01f;camera.farClipPlane=100f;camera.fieldOfView=60;camera.aspect=4f/3f;
            grab.Tick();grab.LateSyncHost();Picture before=Render(camera,name+"-before");
            Check(before.Area>500,"actual video pixels are visible before a manual move");
            var hand=mover==1?publisher:new SharedWindowMoveRegister(mover,5);
            if(mover!=1){hand.Open(key,1);Check(hand.Observe(1,in opening,1,Time.unscaledTime),"actual client observes host movie opening");}
            Check(hand.Begin(Time.unscaledTime),"actual movie gesture begins");Check(hand.Sample(out var movement),"actual movie gesture publishes held pose");
            var destination=new RigPose {Position=new Vector3(.8f,.15f,4),Rotation=Quaternion.Euler(0,12,0)};
            VideoCaseHelpers.Receive(mover,VideoCaseHelpers.Packet(mover,token,3,clip,destination,1.4f,false,movement,native:mover==1),2);
            VideoCaseHelpers.Tick(2);grab.Tick();grab.LateSyncHost();
            Picture after=Render(camera,name+"-after");
            Check(Proof.Same(grab.Root.position,destination.Position)&&after.Center>before.Center+30,
                "revealed mirror moves actual Unity video pixels");
            Check(Proof.Same(grab.Panel.HostGo.transform.position,destination.Position)
                &&Proof.Same(grab.Panel.HostGo.transform.rotation,destination.Rotation),
                "actual modal Tick and LateSyncHost retain the received video pose on the visible Canvas");
            Check(after.Area>before.Area*1.35f,"actual shared resize reaches the final visible video rectangle");
            Metrics+="; "+name+" x="+before.Center.ToString("F1",System.Globalization.CultureInfo.InvariantCulture)
                +"->"+after.Center.ToString("F1",System.Globalization.CultureInfo.InvariantCulture)+" area="+before.Area+"->"+after.Area;
            Check(NativeVideoWindow.RemoteKey==clip,"video movement and resize retain exact shared content");
            hand.Release();Check(hand.Sample(out movement),"actual movie gesture publishes released pose");
            VideoCaseHelpers.Receive(mover,VideoCaseHelpers.Packet(mover,token,4,clip,initial,.8f,false,movement,native:mover==1),2);
            VideoCaseHelpers.Tick(2);grab.Tick();grab.LateSyncHost();Picture returned=Render(camera,name+"-release");
            Check(Proof.Same(grab.Root.position,initial.Position)&&Mathf.Abs(returned.Center-before.Center)<2,
                "actual release endpoint returns both grab frame and video pixels");
            PanelLayout.WorldScale=200;PanelLayout.Origin=new Vector3(14,20,30);VideoCaseHelpers.Tick(2,.02f);
            grab.Tick();grab.LateSyncHost();Picture zoom=Render(camera,name+"-zoom");
            Check(Mathf.Abs(returned.Center-zoom.Center)<.1f&&returned.Area==zoom.Area,
                "settled movie pixels are independent of observer zoom and seat");
            // An existing revealed old physical panel may survive the first tick of a new
            // source token. The replacement gets a fresh real reveal lease after it is built.
            uint nextKey=unchecked(1u*16777619u)^(token+1);publisher.Open(nextKey,1);
            Check(publisher.Sample(out opening),"new playback generation opens its own gesture scope");
            SharedWindowMoves.Close(SharedWindowKind.Video);empty=default;RemoteMapStory.SampleVideoPose(nextKey,false,ref empty);
            VideoCaseHelpers.Receive(1,VideoCaseHelpers.Packet(1,token+1,1,clip,destination,1,true,opening),2);
            grab.Panel.HostGo.SetActive(false);SharedWindows.Grabs[SharedWindowKind.Video]=grab=new GrabbableModal();
            NativeVideoWindow.BindPhysical(grab);
            Check(!NativeVideoWindow.SharedInitialPoseReady&&NativeVideoWindow.RevealPhysical(),
                "replacement real window reveals after coordinator switched opening with an old panel still alive");
            Check(Proof.Same(grab.Root.position,destination.Position),"replacement receives the current generation rather than stale placement");
            RemoteVideoPlayback.Reset();UnityEngine.Object.DestroyImmediate(camera.gameObject);
            foreach(var old in SharedWindows.Grabs.Values)old.Panel.HostGo.SetActive(false);
        }
        Metrics+="; 8 normal/review × scenario/map × host/client mover schedules, 32 PNG views";
        yield break;
    }
    private static bool ReadInitial(SharedWindowKind kind,out Vector3 position,out Quaternion rotation,out float factor)
        =>RemoteMapStory.TryVideoSharedInitialPose(out position,out rotation,out factor);
}
