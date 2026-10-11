using System;
using System.Collections.Generic;
using UnityEngine;
using GloomhavenVR.WorldUI;

namespace GloomhavenVR.Net.TownServices;

internal static partial class TownServiceMirror
{
    private sealed class HoverClock
    { internal float Started; }
    private sealed class HoverPicture
    {
        internal Transform Root = null!;
        internal Transform? Canvas;
        internal Vector3 Offset;
        internal TownOfferedHover? Recipe;
        internal uint Session;
        internal float EpochFloor = float.NegativeInfinity;
    }
    private static readonly Dictionary<(int Owner, uint Session, uint Epoch), HoverClock> HoverClocks = new();
    private static readonly Dictionary<RemoteModule, HoverPicture> HoverPictures = new();
    private static readonly List<RemoteModule> DeadHoverPictures = new();
    private static readonly HashSet<(int Owner, uint Session, uint Epoch)> LiveHoverClocks = new();
    private static readonly List<(int Owner, uint Session, uint Epoch)> DeadHoverClocks = new();

    // Called after reading the actual native parent/canvas, before retaining or
    // encoding a probe. No original node/property or native object is changed.
    private static void CaptureOfferedHover(LocalModule module, TownServiceFrame frame)
    {
        TownOfferedHover? previous = frame.OfferedHover;
        frame.OfferedHover = null;
        if (_sharedFrame == null || frame.VisitorStock || frame.PublicCatalog || !frame.Visible
            || frame.Service is not (1 or 3) || frame.ParentModule != TownServiceFrame.ManifestModule
            || !ValidMotionScale(_sharedFrame.lossyScale)) return;
        Transform source = module.Binding.Root, physical = source;
        bool offered = MotionOffering(source);
        if (!offered)
            for (Transform? node = source; node != null; node = node.parent)
                if (OfferedFrames.TryGetValue(node, out Transform? print) && print != null)
                { physical = print; offered = true; break; }
        if (!offered) return;
        CardReturnReference? native = CardReturn(physical);
        if (native != null && native.Sample(physical, _sharedFrame, native.Hand, out _, out _)) return;
        if (!TownServiceOfferingPose.TryHover(physical, out uint epoch,
            out Vector3 amplitude, out Vector3 offset)) return;
        Vector3 axis = _sharedFrame.InverseTransformVector(amplitude);
        Vector3 delta = _sharedFrame.InverseTransformVector(offset);
        // Match ReadCanvasFrame's actual active ROOT canvas selection. A
        // nested or disabled nearest canvas is not the transmitted author.
        Canvas? authoredCanvas = null;
        if (frame.HasCanvasFrame)
        {
            source.GetComponentsInParent(true, ParentCanvases);
            foreach (Canvas candidate in ParentCanvases)
                if (candidate.isActiveAndEnabled) { authoredCanvas = candidate.rootCanvas; break; }
        }
        bool canvasHover = authoredCanvas != null
            && TownServiceOfferingPose.TryHover(authoredCanvas.transform, out uint canvasEpoch, out _, out _)
            && canvasEpoch == epoch;
        var recipe = new TownOfferedHover(epoch, axis.x, axis.y, axis.z, canvasHover);
        frame.OfferedHover = TownOfferedHover.Same(previous, recipe) ? previous : recipe;
        StripHover(frame.Pose, delta);
        if (canvasHover) StripHover(frame.CanvasPose, delta);
    }
    private static void StripHover(float[] pose, Vector3 delta)
    { pose[0] -= delta.x; pose[1] -= delta.y; pose[2] -= delta.z; }

    // Undo only our last render displacement before any new ordinary/native
    // tween reads the picture. Both enclosing canvas and print keep their base;
    // a header/color/layout heartbeat cannot recapture or duplicate the sine.
    private static void RestoreOfferedHoverBase()
    {
        foreach (HoverPicture picture in HoverPictures.Values)
        {
            if (picture.Root == null) continue;
            Vector3 position = picture.Root.position;
            if (picture.Canvas != null) picture.Canvas.position -= picture.Offset;
            picture.Root.position = position - picture.Offset;
            picture.Offset = Vector3.zero;
        }
    }

    private static HoverPicture ObserveHoverEpoch(RemoteModule module)
    {
        if (!HoverPictures.TryGetValue(module, out HoverPicture? picture))
        { picture = new HoverPicture(); HoverPictures.Add(module, picture); }
        TownServiceFrame frame = module.LastFrame!;
        if (picture.Session != frame.Session || !TownOfferedHover.Same(picture.Recipe, frame.OfferedHover))
        { picture.Session = frame.Session; picture.Recipe = frame.OfferedHover; picture.EpochFloor = frame.SampleTime; }
        return picture;
    }
    private static bool HoverRootCurrent(RemoteModule module, MotionSlot sample)
    {
        // An OFF header still supersedes the preceding canonical active root.
        // Modules which never had a hover recipe keep their existing clocks.
        if (module.LastFrame?.OfferedHover == null && !HoverPictures.ContainsKey(module)) return true;
        return sample.SampleTime >= ObserveHoverEpoch(module).EpochFloor;
    }
    private static bool CurrentOfferedHover(int owner, RemoteModule module, float now)
    {
        TownServiceFrame? frame = module.LastFrame;
        if (!module.Alive || frame?.OfferedHover == null || !frame.Visible || frame.ParentAlpha <= 0f
            || !module.Host.activeInHierarchy || frame.VisitorStock || frame.PublicCatalog
            || !Sessions.TryGetValue(owner, out TownServiceSessionInfo? session) || !session.Active
            || session.Service != frame.Service || session.Session != frame.Session
            || session.PublicClaim != frame.PublicClaim || now - session.LastSeenTime > NetProtocol.StaleTimeoutSeconds
            || Array.BinarySearch(session.Modules, frame.Module) < 0 || PendingReturnModule(owner, module)) return false;
        if (frame.Service == 1 && (!session.TransactionActive || TransactionOwner(1) != owner)) return false;
        if (MotionPeers.TryGetValue(owner, out PeerMotion? peer))
        {
            if (peer.Slots.TryGetValue(new TownServiceMotionKey(1, 0, frame.Module, 0, 0, 0), out MotionSlot? root)
                && root.Entry.Session == frame.Session && root.Entry.Structure == frame.Structure
                && root.SampleTime >= ObserveHoverEpoch(module).EpochFloor
                && (root.Entry.Hand != 0 || !root.Entry.Visible || root.Entry.ParentAlpha <= 0f)) return false;
            for (byte kind = 7; kind <= 8; kind++)
                if (peer.Slots.TryGetValue(new TownServiceMotionKey(kind, 0, frame.Module, 0, 0, 0), out MotionSlot? flight)
                    && flight.Entry.Session == frame.Session && flight.Entry.Structure == frame.Structure
                    && (kind == 8 ? LiveCardReturn(flight, now)
                        : flight.Entry.Numbers[0] + Mathf.Max(0f, now - flight.ReceivedAt) <= flight.Entry.Numbers[1] + .25f)) return false;
        }
        return true;
    }

    private static void ApplyOfferedHover(float now)
    {
        LiveHoverClocks.Clear(); DeadHoverPictures.Clear();
        foreach (var peer in Remote)
        {
            if (peer.Key <= 0) continue;
            foreach (RemoteModule module in peer.Value.Values)
            {
                if (module.LastFrame != null && (module.LastFrame.OfferedHover != null || HoverPictures.ContainsKey(module)))
                    ObserveHoverEpoch(module);
                if (!CurrentOfferedHover(peer.Key, module, now)) continue;
                HoverPicture picture = ObserveHoverEpoch(module);
                TownServiceFrame frame = module.LastFrame!;
                TownOfferedHover recipe = frame.OfferedHover!;
                var key = (peer.Key, frame.Session, recipe.Epoch);
                LiveHoverClocks.Add(key);
                if (!HoverClocks.TryGetValue(key, out HoverClock? clock))
                { clock = new HoverClock { Started = now }; HoverClocks.Add(key, clock); }
                Transform? shared = SharedFrameForRemote?.Invoke(peer.Key);
                if (shared == null || !ValidMotionScale(shared.lossyScale)) continue;
                Transform root = module.AddedCanvas != null && !frame.HasCanvasFrame
                    ? module.Host.transform : module.Binding.Root;
                Vector3 position = root.position;
                picture.Root = root;
                picture.Canvas = recipe.CanvasFollowsHover && module.AddedCanvas != null ? module.Host.transform : null;
                picture.Offset = shared.TransformVector(new Vector3(recipe.X, recipe.Y, recipe.Z))
                    * Mathf.Sin((now - clock.Started) * TownServiceOfferingPose.HoverAngularSpeed);
                if (picture.Canvas != null) picture.Canvas.position += picture.Offset;
                root.position = position + picture.Offset;
            }
        }
        foreach (var pair in HoverPictures)
            if (!pair.Key.Alive) DeadHoverPictures.Add(pair.Key);
        foreach (RemoteModule dead in DeadHoverPictures) HoverPictures.Remove(dead);
        DeadHoverClocks.Clear();
        foreach (var pair in HoverClocks) if (!LiveHoverClocks.Contains(pair.Key)) DeadHoverClocks.Add(pair.Key);
        foreach (var key in DeadHoverClocks) HoverClocks.Remove(key);
    }
    private static void ResetOfferedHover()
    {
        RestoreOfferedHoverBase(); HoverPictures.Clear(); HoverClocks.Clear();
        DeadHoverPictures.Clear(); LiveHoverClocks.Clear(); DeadHoverClocks.Clear();
    }
}
