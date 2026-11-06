using System;
using GloomhavenVR.Net;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;

/// <summary>Complete production playback coordinator before the real video/shared-pose resolver.
/// Decoder callbacks, native window admission and physical frame storage remain explicit ports
/// in the CLR proof; the Unity companion replaces the frame/Canvas writers and checks pixels.</summary>
internal static class VideoPoseCases
{
    private const SharedWindowKind Kind = SharedWindowKind.Video;
    private static uint Key(int source, uint token) => unchecked((uint)source * 16777619u) ^ token;
    private static PresenceState Packet(int source, uint token, uint revision, string clip,
        RigPose pose, float size, bool initial, SharedWindowMoveEntry row, bool native = true)
    {
        SharedWindowPoseContext context = Proof.Context(source, 5, initial);
        var packet = new PresenceState { HasVideoWindow = true, VideoWindow = new VideoWindowState {
            Native = native, Playing = false, Token = token, Revision = revision, Clip = clip,
            Millis = 1234, Window = new SharedWindowEntry { ContentKey = row.ContentKey,
                Flags = NetProtocol.SharedOpenBit | NetProtocol.SharedPoseBit, PoseStamp = (byte)revision,
                SizeCode = NetProtocol.EncodeStorySize(size), Frame = NetProtocol.SharedFrameSeatAnchor,
                Pose = Proof.Relative(pose, context) } }, WindowPoseContext = Proof.Wire(context),
            WindowScales = Proof.ScalePacket(5, 198.12f) };
        var bytes = new byte[512]; int at = 0;
        Proof.Check(VideoWindowCodec.Write(bytes, ref at, in packet.VideoWindow)
            && VideoWindowCodec.TryRead(bytes, 2, bytes[1], out packet.VideoWindow),
            "video full coordinator receives actual72 content and pose codec");
        at = 0;
        Proof.Check(SharedWindowMoveOrder.Write(bytes, ref at, new SharedWindowMoveOrder(new[] { row }))
            && SharedWindowMoveOrder.TryRead(bytes, 2, bytes[1], out packet.WindowMoves),
            "video full coordinator receives the matching actual123 gesture cohort");
        return packet;
    }
    private static void Receive(int sender, PresenceState packet, int viewer)
    {
        SharedWindowMoves.Observe(sender, in packet);
        RemoteVideoPlayback.Observe(sender, in packet);
        RemoteVideoPlayback.Resolve(viewer);
    }
    private static void Tick(int viewer, float seconds = .3f)
    { Time.unscaledTime += seconds; Time.frameCount += 3; RemoteVideoPlayback.Resolve(viewer); }

    internal static void Run()
    {
        foreach (bool review in new[] { false, true })
        foreach (bool map in new[] { false, true })
        foreach (int mover in new[] { 1, 3 })
            Follow(review, map, mover);
    }
    private static void Follow(bool review, bool map, int mover)
    {
        const int viewer = 2, source = 1; const uint token = 11;
        string title = (review ? "review" : "native") + "/" + (map ? "map" : "scenario")
            + "/" + (mover == 1 ? "host" : "client");
        RemoteVideoPlayback.Reset(); NativeVideoWindow.NativePlayer = null;
        NativeVideoWindow.ReviewPlayer = null; NativeVideoWindow.AssetAvailable = true;
        NativeVideoWindow.ResolvedKey = string.Empty;
        Proof.Configure(viewer); MapRoomDriver.Active = map; ModalFallback.Ready = false;
        SharedWindowMoves.Reset(); SharedWindowMoves.Bind(() => true);
        GrabbableModal grab = SharedWindows.Grabs[Kind]; grab.Visible = false;
        uint key = Key(source, token);
        var empty = new SharedWindowEntry();
        var publisher = new SharedWindowMoveRegister(1, 5); publisher.Open(key, 1);
        Proof.Check(publisher.Sample(out SharedWindowMoveEntry opening), "video actual publisher announces opening");
        string clip = review ? "Review/CP_Intro/GH_CP_Intro.sbs-v2.mov" : "CP_Intro/GH_CP_Intro.sbs-v2.mov";
        PresenceState initial = Packet(source, token, 1, clip, Proof.HostPose, 1f, true, opening);
        Receive(source, initial, viewer);
        Proof.Check(!RemoteMapStory.TryVideoSharedInitialPose(out _, out _, out _),
            "video first coordinator tick retains pending pose before local sample opens current identity: " + title);
        RemoteMapStory.SampleVideoPose(key, false, ref empty);
        Proof.Check(publisher.Sample(out opening), "video later heartbeat carries the same live opening");
        Receive(source, Packet(source, token, 2, clip, Proof.HostPose, 1f, true, opening), viewer);
        Tick(viewer, .01f);
        Proof.Check(NativeVideoWindow.Remote != null && NativeVideoWindow.RemoteKey == clip,
            "video shared decoder receives original native/review content: " + title);
        Proof.Check(RemoteMapStory.TryVideoSharedInitialPose(out Vector3 first, out Quaternion facing, out float size),
            "video pending physical view can receive initial placement: " + title);
        Proof.Snap(grab, first, facing, size); ModalFallback.Ready = true;
        Tick(viewer);
        Proof.Check(Proof.Same(grab.Root.position, Proof.HostPose.Position)
            && grab.SharedReferenceWorldScale == 198.12f,
            "video initial shared pose and reference revealed: " + title);
        var hand = mover == 1 ? publisher : new SharedWindowMoveRegister(mover, 5);
        if (mover != 1)
        { hand.Open(key, 1); Proof.Check(hand.Observe(1, in opening, 1, Time.unscaledTime), "video client admits original opening before a real grip"); }
        Proof.Check(hand.Begin(Time.unscaledTime), "video real host/client gesture has permission");
        Proof.Check(hand.Sample(out SharedWindowMoveEntry movement),
            "video real host/client gesture starts after reveal");
        Receive(mover, Packet(mover, token, 3, clip, Proof.HandPose, 1.4f, false, movement, native: mover == source), viewer);
        Tick(viewer);
        Proof.Check(Proof.Same(grab.Root.position, Proof.HandPose.Position)
            && Proof.Same(grab.Root.rotation, Proof.HandPose.Rotation),
            "revealed mirror follows the physical shared drag: " + title);
        Proof.Check(Mathf.Abs(grab.Root.localScale.x - NetProtocol.DecodeStorySize(NetProtocol.EncodeStorySize(1.4f))) < .0001f,
            "revealed mirror resizes its physical frame: " + title);
        Proof.Check(NativeVideoWindow.Remote != null && NativeVideoWindow.RemoteKey == clip,
            "shared move never replaces playback content: " + title);
        var released = new RigPose { Position = Proof.HandPose.Position + new Vector3(3, 1, -2), Rotation = Quaternion.Euler(0, 73, 0) };
        hand.Release(); Proof.Check(hand.Sample(out movement), "video actual gesture publishes final release");
        Receive(mover, Packet(mover, token, 4, clip, released, .7f, false, movement, native: mover == source), viewer);
        Tick(viewer);
        Proof.Check(Proof.Same(grab.Root.position, released.Position) && Proof.Same(grab.Root.rotation, released.Rotation),
            "revealed mirror follows release endpoint rather than a stored remote target: " + title);
        for (int i = 0; i < 4; i++)
        {
            PanelLayout.WorldScale = .2f + 25f * i; PanelLayout.Origin = new Vector3(i, -i, i * 2);
            Tick(viewer, .02f);
            Proof.Check(Proof.Same(grab.Root.position, released.Position), "video settled pose survives independent observer zoom: " + title);
        }
        // Original view retired. A replacement generation has a fresh pending/reveal boundary,
        // never the previous movie's baseline, even when it uses the same selected clip.
        RemoteVideoPlayback.Reset(); SharedWindowMoves.Reset(); ModalFallback.Ready = false;
        grab.Visible = false; SharedWindows.Grabs[Kind] = grab = new GrabbableModal { Visible = false };
        uint next = Key(source, token + 1); empty = default; RemoteMapStory.SampleVideoPose(next, false, ref empty);
        publisher = new SharedWindowMoveRegister(source, 5); publisher.Open(next, source);
        Proof.Check(publisher.Sample(out opening), "video replacement announces fresh playback scope");
        Receive(source, Packet(source, token + 1, 1, clip, released, 1.2f, true, opening), viewer); Tick(viewer, .01f);
        Proof.Check(RemoteMapStory.TryVideoSharedInitialPose(out first, out facing, out size) && Proof.Same(first, released.Position),
            "video close/reopen still warms the new unrevealed generation: " + title);
        Proof.Snap(grab, first, facing, size); ModalFallback.Ready = true; Tick(viewer);
        Proof.Check(NativeVideoWindow.RemoteKey == clip && Proof.Same(grab.Root.position, released.Position),
            "video replacement retains content and new opening pose: " + title);
        RemoteVideoPlayback.Reset();
    }
}
