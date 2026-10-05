#if TOWN_FINAL_CAPTURE
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Unrelated surfaces/samplers and the native handoff writer are explicit adapters.
// Registration, dispatch, use-bar finally, town Late sequencing, source discovery,
// publication, capture, completion, codec, motion and inert observer are production.
namespace GloomhavenVR.WorldUI
{
    internal static class FinalCaptureState
    {
        internal static readonly List<string> Trace = new();
        internal static Transform Frame = null!, Head = null!, Card = null!, Face = null!, Rod = null!, Ring = null!;
        internal static UINewEnhancementShopInventory Pool = null!;
        internal static Transform NewRow = null!;
        internal static Vector3 FinalRod => new(.032f, -.06f, -.003f);
        internal static Color FinalInk => new(.17f, .74f, .39f, .91f);
        internal static int Captures;
        internal static bool ThrowUseBars;
        internal static void Handoff()
        {
            Trace.Add("Handoff.Geometry");
            Card.position = Head.position + Head.forward * .43f + Head.right * .03f;
            Card.rotation = Quaternion.LookRotation(Head.position - Card.position, Head.up);
            Ring.localRotation = Quaternion.Euler(0, 0, 49);
            if (!Pool.slotsPool.Contains(NewRow.GetComponent<UINewEnhancementShopSlot>()))
                Pool.slotsPool.Add(NewRow.GetComponent<UINewEnhancementShopSlot>());
        }
    }
    internal class ScheduledWidget { internal void Tick() { } internal void LateTick() { } }
    internal class WorldSurface : ScheduledWidget { }
    internal static class TickGuard
    {
        internal static void Run(string name, Action action, string scope)
        { FinalCaptureState.Trace.Add(name); try { action(); } catch (Exception e) { FinalCaptureState.Trace.Add("caught:" + e.Message); } }
    }
    internal static partial class CanvasConversion
    {
        internal static void BeginFramePhase(string name) => FinalCaptureState.Trace.Add("begin:" + name);
        internal static void EndFramePhase() => FinalCaptureState.Trace.Add("end");
        internal static void TickPanelOrder()
        {
            FinalCaptureState.Trace.Add("Canvas.FinalState");
            FinalCaptureState.Face.GetComponent<Canvas>().sortingOrder = 127;
            FinalCaptureState.Face.GetComponent<Image>().color = FinalCaptureState.FinalInk;
        }
    }
    internal static partial class GrabBarTween
    { internal static void TickAll() { FinalCaptureState.Trace.Add("GrabBar.DrawnPose"); FinalCaptureState.Rod.localPosition = FinalCaptureState.FinalRod; } }
    internal sealed partial class UseBarsSurface
    {
        private readonly ScheduledWidget _characterMirror = new();
        private readonly List<UseBarAnimationState> AnimationStateScratch = new();
        private readonly UIUseActiveBonus?[] AnimationSources = new UIUseActiveBonus?[8];
        private readonly Component?[] NativeSources = new Component?[32];
        private readonly NativeUseBarState?[] WireNativeStates = new NativeUseBarState?[32];
        private UseBarAnimationState[]? WireAnimationStates;
        private uint WireBarMask;
        internal void Tick() { }
        private void RestoreOriginalTooltips()
        { if (FinalCaptureState.ThrowUseBars) throw new InvalidOperationException("native-usebar-source"); }
        private void StackDocked() { }
    }
    internal sealed class UIUseActiveBonus : MonoBehaviour { }
    internal sealed partial class TownServiceEnhancementHandoff { internal void LateTick() => FinalCaptureState.Handoff(); }
    internal sealed partial class TownServiceSurface { internal void LateTick() { } }
    internal sealed partial class TownServiceCatalog { internal void LateTick() { } }
    internal sealed partial class TownServiceTray { internal void LateTick() { } }
    internal static partial class TownServicePublicMerchant { internal static void LateTick() { } }
    internal static partial class TownServiceMerchantHandoff { internal static void LateTick() { } }
    internal static partial class TownServicePalmConfirmation { internal static void LateTick() { } }
    internal static partial class TownServicePresentation
    {
        private static TownServiceRitual? _ritual => Ritual;
        private static TownServiceCatalog? _catalog => Catalog;
        private static TownServiceTray? _tray => Tray;
        private static List<TownServiceSurface> Surfaces => LocalSurfaces;
        private static Transform StationRoot => FinalCaptureState.Frame;
    }
    internal static partial class TownServicePopulation
    {
        internal static Transform Frame => FinalCaptureState.Frame;
        internal static TownFaceState PublishedFaces;
        internal static TownActivityState PublishedActivities;
    }
}
namespace GloomhavenVR.Core
{
    internal static class VRSession { internal static bool IsRunning = true; }
    internal static class PerfMonitor
    { internal static IDisposable Scope(string name) => new Nothing(); private sealed class Nothing : IDisposable { public void Dispose() { } } }
}
namespace GloomhavenVR.Net
{
    internal static class NetSession { internal static bool FlatNetMode; }
    internal sealed class FfsNetTransport
    {
        internal bool IsOnline = true; internal int LocalPlayerId = 1;
        internal readonly List<byte[]> Packets = new();
        internal int Drains; internal bool SendTownControl(byte[] bytes, int length, bool hostOnly) => true;
        internal void Send(byte[] bytes, int length, object? identity = null)
        {
            if (length <= 0) return;
            var packet = new byte[length]; Buffer.BlockCopy(bytes, 0, packet, 0, length); Packets.Add(packet);
            if (identity is TownServiceFrame frame) TownServiceDelivery.Completed?.Invoke(frame);
        }
        // The real 50 ms transport budget has its wire suite. This adapter records
        // drain placement only; it does not claim Photon/network timing evidence.
        internal void TickFragments(float now) { Drains++; FinalCaptureState.Trace.Add("Transport.Drain"); }
    }
    internal sealed class NativeUseBarState { }
    internal sealed class NativeBoardState { internal float SampleTime; internal NativeBoardState CopyWithTime(float time) => new() { SampleTime = time }; }
    internal sealed class CardPlumeState { }
    internal sealed class UseBarAnimationState { }
    internal sealed class NativeUseBarSnapshot
    {
        internal float SampleTime; internal byte Bar, Slot; internal NativeUseBarState? State;
        internal NativeUseBarSnapshot(float time, byte bar, byte slot, NativeUseBarState? state)
        { SampleTime = time; Bar = bar; Slot = slot; State = state; }
    }
    internal sealed class CardPlumeSnapshot { internal CardPlumeSnapshot(float time, CardPlumeState[] states) { } }
    internal static class NativeBoardCodec { internal const int MaxSize = 1; internal static int Write(NativeBoardState state, byte[] bytes) => 0; }
    internal static class NativeBoardSampler
    { internal static NativeBoardState? Sample() { FinalCaptureState.Trace.Add("Native.Board"); return null; } }
    internal static class NativeUseBarPacket { internal const int MaxSize = 1; }
    internal static class CardPlumeCodec { internal const int MaxSize = 1; internal static int Write(CardPlumeSnapshot snapshot, byte[] buffer) => 0; }
    internal static class CardPlumeSampler
    { internal static CardPlumeState[] Sample() { FinalCaptureState.Trace.Add("Native.Plume"); return Array.Empty<CardPlumeState>(); } }
    internal static class NativeDecisionHighlightSampler
    { internal static object Sample() { FinalCaptureState.Trace.Add("Native.Highlight"); return new(); } }
    internal static class NativeUseBarSampler { internal static NativeUseBarState? Sample(Component source, byte bar, byte slot) => null; }
    internal static class UseBarAnimationSampler { internal static UseBarAnimationState? Sample(UIUseActiveBonus source, byte slot) => null; }
    internal struct TownFaceState { internal bool Active; }
    internal struct TownActivityState { }
    internal static class TownActivityCodec
    { internal const int PacketBytes = 1; internal static int WritePacket(byte[] bytes, in TownActivityState activity, in TownFaceState face) => 0; }
    internal sealed partial class NetAvatarDriver
    {
        private static NetAvatarDriver? _instance;
        internal bool isActiveAndEnabled = true;
        internal readonly FfsNetTransport _transport = new();
        private object? _decisionHighlightSnapshot;
        private readonly byte[] _activityBuffer = new byte[1];
        internal static NetAvatarDriver FixtureInstall() => _instance = new();
        internal static void FixtureWithdraw() => _instance = null;
        private void LogPhaseError(string name, Exception error) => throw new InvalidOperationException(name, error);
        private void TickAnimationSend(UseBarAnimationState[]? states) => FinalCaptureState.Trace.Add("Native.Bonus");
        private void TickNativePromptSend(float now) => FinalCaptureState.Trace.Add("Native.Prompt");
        private void TickItemAppearanceSend(float now) => FinalCaptureState.Trace.Add("Native.Item");
        private void TickMapButtonTooltipSend(float now) => FinalCaptureState.Trace.Add("Native.Tooltip");
        private void TickCardAppearanceSend(float now) => FinalCaptureState.Trace.Add("Native.Card");
        private void SendNative(NativeUseBarSnapshot snapshot) { }
    }
}
namespace GloomhavenVR.Net.TownServices
{
    internal static class TownMerchantControlSync
    { internal static Func<byte[],int,bool,bool>? SendReliable; internal static void Tick(FfsNetTransport transport,float now) { } }
    internal static partial class TownServiceGrantSync
    { internal static void Tick(FfsNetTransport transport, float now) { FinalCaptureState.Captures++; FinalCaptureState.Trace.Add("Town.Capture"); } }
}

public static partial class MirrorProgram
{
    private static void FinalGuard(Action change, Action restore, string message)
    {
        FinalCaptureState.Captures = 0; change(); NetAvatarDriver.PublishTownServicesFinal();
        Check(FinalCaptureState.Captures == 0, message); restore();
    }
    public static IEnumerator RunFinalCapture(string output, string variant, string suite)
    {
        _output = Path.Combine(output, variant + "-evidence"); Directory.CreateDirectory(_output); _assertions = 0;
        try
        {
            TownServiceSync.Reset(); TownServiceMirror.Shutdown(); Baselines.Clear();
            NativeTemplates.Originals.Clear(); NativeTemplates.BoundaryRoots.Clear();
            TownServicePresentation.Active = true; TownServicePresentation.Service = 3;
            TownServicePresentation.Session = 622; TownServicePresentation.SessionAge = 0;
            TownServicePresentation.RelocationRevision = 0; TownServicePresentation.RelocationVisibility = 1;
            TownServicePresentation.Window = null; TownServicePresentation.Catalog = null;
            TownServicePresentation.WorkspaceProps = null; TownServicePresentation.Tray = null;
            TownServicePresentation.CounterFurniture = null; TownServicePresentation.LocalSurfaces.Clear();
            TownServicePresentation.Samples.Clear(); TownServiceMerchantHandoff.Active = false;
            TownServiceMerchantHandoff.OwnedChips.Clear(); TownServicePalmConfirmation.Active.Clear();
            TownServiceEnhancementHandoff.Returning.Clear(); NativeTemplates.Tooltip = null;
            TownServiceSync.Calls.Clear(); TownServiceSync.UseProductionPublish = true; NetPlayerActors.Peer = 1;
            var driver = NetAvatarDriver.FixtureInstall();
            FinalGuard(() => driver.isActiveAndEnabled = false, () => driver.isActiveAndEnabled = true, "disabled avatar cannot publish final town");
            FinalGuard(() => NetSession.FlatNetMode = true, () => NetSession.FlatNetMode = false, "flat mode cannot publish final town");
            FinalGuard(() => GloomhavenVR.Core.VRSession.IsRunning = false, () => GloomhavenVR.Core.VRSession.IsRunning = true, "stopped VR cannot publish final town");
            FinalGuard(() => driver._transport.IsOnline = false, () => driver._transport.IsOnline = true, "offline transport cannot publish final town");
            FinalGuard(() => driver._transport.LocalPlayerId = 0, () => driver._transport.LocalPlayerId = 1, "missing local player cannot publish final town");
            FinalGuard(NetAvatarDriver.FixtureWithdraw, () => { driver = NetAvatarDriver.FixtureInstall(); }, "withdrawn avatar cannot publish final town");
            var cameraGo = Go("Final capture head"); _camera = cameraGo.AddComponent<Camera>(); _camera.enabled = false;
            GloomhavenVR.Rig.VRRigDriver.HeadCamera = _camera; FinalCaptureState.Head = cameraGo.transform;
            FinalCaptureState.Head.SetPositionAndRotation(new Vector3(.27f, 1.3f, -.8f), Quaternion.Euler(17, 39, 13));
            Transform author = Go("Final native author").transform; FinalCaptureState.Frame = author;
            var ritual = new TownServiceRitual(); TownServicePresentation.Ritual = ritual;
            Transform card = Go("Exact offered owner card", author).transform; FinalCaptureState.Card = card;
            card.SetPositionAndRotation(new Vector3(-.2f, .5f, .3f), Quaternion.Euler(-31, -62, 8));
            Transform visual = Go("Visual", card).transform;
            Image("Backing", visual, Vector2.zero, new Vector2(.15f, .23f), Color.black);
            Transform face = Image("Original offered face", visual, Vector2.zero, new Vector2(.14f, .22f), Color.red).transform;
            FinalCaptureState.Face = face;
            Canvas canvas = face.gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.overrideSorting = true;
            FinalCaptureState.Ring = Image("Original aura", face, Vector2.zero, new Vector2(.16f, .24f), Color.cyan).transform;
            FinalCaptureState.Rod = Image("Original drawn grab rod", face, Vector2.zero, new Vector2(.1f, .01f), Color.gray).transform;
            ritual.Handoff = new TownServiceEnhancementHandoff { OfferedCardId = 62201, Card = card, Face = face, NativeSource = null };
            Transform inventory = Rect("Original options", author, Vector2.zero, new Vector2(350, 400)); inventory.localScale = Vector3.one * .001f;
            inventory.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            FinalCaptureState.Pool = inventory.gameObject.AddComponent<UINewEnhancementShopInventory>();
            Transform row = Image("New original pooled row", inventory, Vector2.zero, new Vector2(320, 40), Color.yellow).transform;
            row.gameObject.AddComponent<UINewEnhancementShopSlot>(); FinalCaptureState.NewRow = row;
            ritual.Surfaces.Add(new TownServiceSurface { Id = 10, Panel = new PanelFixture { Target = inventory } });
            TownServiceSharedCue.LocalReady = true; TownServiceSharedCue.LocalStrength = 1;
            TownServiceSync.Tick(author, author); TownServiceMirror.InteractionOwner(3);
            for (float until = Time.unscaledTime + .5f; Time.unscaledTime < until && TownServiceMirror.InteractionOwner(3) != 1;) yield return null;
            Check(TownServiceMirror.InteractionOwner(3) == 1, "actual source endpoint acquires the enchantress lease before capture");
            var ui = new WorldUIModule(); ui.FixtureBuild();
            File.WriteAllText(Path.Combine(_output, "registered-stages.json"), JsonUtility.ToJson(new FinalStages { Update = ui.FixtureUpdate, Late = ui.FixtureLateNames }, true));
            FinalCaptureState.Trace.Clear(); FinalCaptureState.Captures = 0; driver._transport.Packets.Clear();
            ui.FixtureLate();
            File.WriteAllLines(Path.Combine(_output, "scheduled-trace.txt"), FinalCaptureState.Trace);
            Check(FinalCaptureState.Captures == 1, "registered Late frame captures town exactly once");
            string[] native = FinalCaptureState.Trace.Where(x => x.StartsWith("Native.")).ToArray();
            Check(native.SequenceEqual(new[] { "Native.Bonus", "Native.Highlight", "Native.Prompt", "Native.Item", "Native.Tooltip", "Native.Card", "Native.Board", "Native.Plume" }),
                "all existing native samplers retain their exact registered order");
            ushort faceId = TownServiceSync.ModuleId(face), rowId = TownServiceSync.ModuleId(row);
            TownServiceFrame captured = driver._transport.Packets.Select(bytes => TownServiceCodec.TryRead(bytes, bytes.Length, out TownServiceFrame? f) ? f : null).First(f => f != null && f.Module == faceId)!;
            Vector3 position = author.InverseTransformPoint(face.position);
            Check(Vector3.Distance(new Vector3(captured.Pose[0], captured.Pose[1], captured.Pose[2]), position) < .00001f,
                "capture includes the current offered card plane");
            Check(TownServiceSync.HasPublishedSource(row) && driver._transport.Packets.Any(bytes => TownServiceCodec.TryRead(bytes, bytes.Length, out TownServiceFrame? f) && f!.Module == rowId),
                "final capture includes the newly discovered actual native pool row");
            TownServiceValue? sort = captured.Nodes.SelectMany(node => node.Values).Where(value => value.Key == TownServiceProperty.Canvas).Select(value => value.Value).FirstOrDefault();
            // Suspend only this fixture's local endpoint before delivering the
            // owner's real immutable packets to an independent observer endpoint.
            object lane = typeof(TownServiceMirror).GetField("PrivateLane", PrivateStatic)!.GetValue(null)!;
            lane.GetType().GetField("Active", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(lane, false);
            TownServicePresentation.Active = false; TownServiceMirror.SharedFrameForRemote = null; NetPlayerActors.Peer = 2;
            Transform observer = Go("Observer final frame").transform;
            observer.SetPositionAndRotation(new Vector3(3, .5f, -1), Quaternion.Euler(0, -33, 0));
            yield return null;
            foreach (byte[] bytes in driver._transport.Packets)
            {
                if (TownServiceMotionCodec.TryRead(bytes, bytes.Length, out TownServiceMotionPacket? motion)) TownServiceMirror.ReceiveMotion(1, motion!);
                else Check(TownServiceMirror.Receive(1, bytes, bytes.Length), "actual final owner packet reaches observer");
            }
            TownServiceMirror.InteractionOwner(3);
            for (float until = Time.unscaledTime + .5f; Time.unscaledTime < until && TownServiceMirror.InteractionOwner(3) != 1;) yield return null;
            TownServiceMirror.TickRemote(_ => observer);
            TownServiceBinding? copy = Remote(1, faceId);
            Check(copy != null, "final published owner face creates the inert observer module");
            Vector3 expected = observer.TransformPoint(author.InverseTransformPoint(face.position));
            Check(Vector3.Distance(copy!.Root.position, expected) < .00003f, "observer receives the exact current owner offered-card position");
            Check(Quaternion.Angle(copy.Root.rotation, observer.rotation * Quaternion.Inverse(author.rotation) * face.rotation) < .01f,
                "observer receives the exact current owner offered-card orientation");
            Check(Quaternion.Angle(copy.Root.Find("Original aura").localRotation, FinalCaptureState.Ring.localRotation) < .01f,
                "observer aura retains the actual offered card plane and native phase");
            Check(Vector3.Distance(copy.Root.Find("Original drawn grab rod").localPosition, FinalCaptureState.FinalRod) < .00003f,
                "capture includes the final drawn grab-bar geometry");
            Check(sort != null && sort.Numbers[2] == 127, "capture includes the final canvas sorting state");
            Check(copy.Root.GetComponent<Canvas>().sortingOrder == 127 && copy.Root.GetComponent<Image>().color == FinalCaptureState.FinalInk,
                "observer receives the final native canvas sort and appearance");
            Check(Remote(1, rowId) != null, "new actual native pool member reaches the observer in this frame");
            // A failed unrelated native source still invokes the old finally
            // transport and cannot suppress the independent town publication tail.
            NetPlayerActors.Peer = 1; lane.GetType().GetField("Active", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(lane, true);
            TownServicePresentation.Active = true; FinalCaptureState.ThrowUseBars = true;
            FinalCaptureState.Trace.Clear(); FinalCaptureState.Captures = 0; ui.FixtureLate();
            Check(FinalCaptureState.Trace.Contains("Native.Bonus") && FinalCaptureState.Trace.Contains("caught:native-usebar-source") && FinalCaptureState.Captures == 1,
                "failed use-bar source preserves both native finally transport and final town capture");
            Check(FinalCaptureState.Trace.Last() == "end", "actual Late dispatcher closes its frame phase after final publication");
            File.WriteAllText(Path.Combine(_output, "assertions.txt"), _assertions + " assertions\n");
        }
        finally
        {
            FinalCaptureState.ThrowUseBars = false; NetAvatarDriver.FixtureWithdraw();
            TownServiceSync.Reset(); TownServiceSync.ResetPublic(); TownServiceMirror.Shutdown(); TownServiceSync.UseProductionPublish = false;
            foreach (var go in Objects) if (go != null) Object.DestroyImmediate(go);
            foreach (var asset in Assets) if (asset != null) Object.DestroyImmediate(asset);
            Objects.Clear(); Assets.Clear(); Baselines.Clear(); GloomhavenVR.Rig.VRRigDriver.HeadCamera = null;
            NativeTemplates.Originals.Clear(); NativeTemplates.BoundaryRoots.Clear();
        }
    }
    [Serializable] private sealed class FinalStages { public string[] Update = Array.Empty<string>(), Late = Array.Empty<string>(); }
}
#endif
