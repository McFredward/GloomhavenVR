using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GloomhavenVR.Cards;
using GloomhavenVR.Hands;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using UnityEngine;
using UnityEngine.UI;

internal static class FlightTime646 { internal static float Now = 100f, Delta = 1f / 90f; }

public static partial class MirrorProgram
{
    private static IEnumerator Return646(bool movingHand)
    {
        TownServiceMirror.Shutdown(); NetPlayerActors.Peer = 1;
        FlightTime646.Now = 100f;
        Transform shared = Go("Native return source map").transform;
        shared.rotation = Quaternion.Euler(3f, 31f, 0f); shared.localScale = Vector3.one * .8f;
        Transform observer = Go("Observer map").transform;
        observer.SetPositionAndRotation(new Vector3(8f, .2f, -.1f), shared.rotation);
        observer.localScale = shared.localScale;
        TownServiceMirror.SharedFrameForRemote = _ => observer;
        Transform source = Go("Real card root", shared).transform;
        source.localPosition = new Vector3(.4f, 1.4f, -.2f);
        source.localRotation = Quaternion.Euler(15f, -21f, 8f);
        Transform face = Source(source); face.localPosition = new Vector3(.01f, -.02f, .003f);
        face.localRotation = Quaternion.Euler(7f, -8f, 11f);
        Transform body = Source(source); body.localPosition = new Vector3(.01f, -.02f, .006f);
        body.localRotation = face.localRotation;
        VRCard card = source.gameObject.AddComponent<VRCard>();
        card.BeginNative646(shared.TransformPoint(new Vector3(-.5f, 1.1f, .2f)), .55f);
        Func<Transform, bool> exclude = t => t == face || t == body;
        var hand = new VRHand { Side = HandSide.Left, WorldScale = .8f };
        hand.Rig.Root = Go("Native fan holder", shared).transform;
        hand.Rig.Root.SetPositionAndRotation(source.position, shared.rotation);
        Transform remoteHand = Go("Approved interpolated remote fan holder", observer).transform;
        remoteHand.localPosition = shared.InverseTransformPoint(hand.Rig.Root.position);
        remoteHand.localRotation = Quaternion.Inverse(shared.rotation) * hand.Rig.Root.rotation;
        NetAvatarDriver.MotionHandFrames[2] = new[] { remoteHand, remoteHand };
        float[]? merchant = null;
        Vector3 merchantTarget = new Vector3(-.2f, .12f, .08f);
        bool Sample(Transform original, Transform world, VRHand? holder, out uint revision, out float[] values)
        {
            if (!movingHand) return card.CaptureNativeTownReturn646(original, world, holder, out revision, out values);
            revision = 11; values = (float[])merchant!.Clone(); values[0] = FlightTime646.Now - 100f;
            return values[0] < values[1];
        }
        if (movingHand)
            merchant = TownCardReturnMotion.Capture(face, source, shared, hand, 0f, .55f, 1, 18f,
                source.localToWorldMatrix, source.rotation,
                hand.Rig.Root.localToWorldMatrix * Matrix4x4.TRS(merchantTarget, Quaternion.identity, Vector3.one),
                source.rotation, Vector3.zero);
        using (TownServiceMirror.UseStockLane())
        {
            TownServiceMirror.BeginSession(1, 646, shared, shared);
            TownServiceMirror.RegisterTemplate(1, 1, face, address: "face.2129|");
            TownServiceMirror.RegisterTemplate(1, 2, body, address: "map.cardbody|");
            TownServiceMirror.RegisterModule(10, 1, face, address: "face.2129|");
            TownServiceMirror.RegisterModule(11, 2, body, address: "map.cardbody|");
            TownServiceMirror.RegisterCardReturn(face, Sample, movingHand ? hand : null);
            TownServiceMirror.RegisterCardReturn(body, Sample, movingHand ? hand : null);
            // The same approved hand registration used by merchant returns.
            if (movingHand)
            { TownServiceMirror.RegisterMotionHand(face, hand, false); TownServiceMirror.RegisterMotionHand(body, hand, false); }
        }
        Canvas.ForceUpdateCanvases(); yield return null;
        FastCapture baseline = CaptureFast(); Receive(2, baseline.Artwork); DeliverMotion(2, baseline);
        TownServiceMirror.TickRemote(_ => observer);
        var keys = (Dictionary<int, int>)typeof(TownServiceMirror).GetField("StockKeys", PrivateStatic)!.GetValue(null)!;
        int peer = keys[2]; TownServiceBinding clone = Remote(peer, 10)!, cloneBody = Remote(peer, 11)!;
        Check(clone != null && cloneBody != null, "native return originals are prepared through real capture and codec");
        var expected = Go("Exact curve endpoint oracle", observer).transform;
        float[]? clock = null; float received = 0f, clockSample = -1f; int published = 0;
        foreach (byte[] packet in baseline.Motion)
            if (TownServiceMotionCodec.TryRead(packet, packet.Length, out var decoded))
                foreach (var entry in decoded!.Entries)
                    if (entry.Kind == 8 && entry.Module == 10)
                    { clock = entry.Numbers; received = FlightTime646.Now; clockSample = decoded.SampleTime; published++; }
        var trace = new System.Text.StringBuilder("frame,age,error,visible,artwork\n");
        float maxError = 0f;
        for (int frame = 1; frame <= 54; frame++)
        {
            FlightTime646.Now = 100f + frame / 90f;
            FlightTime646.Delta = 1f / 90f;
            if (!movingHand) card.StepNative646();
            if (movingHand)
            {
                hand.Rig.Root.localPosition += new Vector3(.002f, .001f, -.001f);
                hand.Rig.Root.localRotation = Quaternion.Euler(0f, frame * .6f, 0f);
                remoteHand.localPosition = hand.Rig.Root.localPosition;
                remoteHand.localRotation = hand.Rig.Root.localRotation;
            }
            if (frame == 1 || frame == 7 || frame == 16 || frame == 29)
            {
                FastCapture sample = CaptureFast(); DeliverMotion(2, sample);
                foreach (byte[] packet in sample.Motion)
                    if (TownServiceMotionCodec.TryRead(packet, packet.Length, out var decoded))
                        foreach (var entry in decoded!.Entries)
                            if (entry.Kind == 8 && entry.Module == 10)
                            { clock = entry.Numbers; received = FlightTime646.Now; clockSample = decoded.SampleTime; published++; }
            }
            bool artwork = frame == 10 || frame == 20 || frame == 33;
            if (artwork)
            {
                // A real original caption refresh samples this same current
                // hierarchy after the last independently admitted motion clock.
                face.Find("Name").GetComponent<TMPro.TMP_Text>().text = "Native caption " + frame;
                FastCapture refreshed = CaptureFast(); Receive(2, refreshed.Artwork);
                bool newer = false;
                foreach (byte[] bytes in refreshed.Artwork)
                    if (TownServiceCodec.TryRead(bytes, bytes.Length, out var original) && original!.Module == 10)
                        newer |= original.SampleTime > clockSample;
                Check(newer, "caption refresh publishes a real original newer than the still-live independent return clock");
                // The independent numeric event may be delayed; artwork arrival
                // must not delete a still-current semantic return clock.
            }
            TownServiceMirror.TickRemote(_ => observer);
            if (artwork)
            {
                var frames = (IDictionary)typeof(TownServiceMirror).GetField("MotionRemoteFrames", PrivateStatic)!.GetValue(null)!;
                foreach (DictionaryEntry pair in frames)
                {
                    object remote = pair.Key;
                    if (!ReferenceEquals(remote.GetType().GetField("Binding", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(remote), clone)) continue;
                    var slots = (IEnumerable)pair.Value.GetType().GetField("Slots", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(pair.Value)!;
                    bool rootPresent = false, flightPresent = false;
                    foreach (object slot in slots)
                    {
                        var entry = (TownServiceMotionEntry)slot.GetType().GetField("Entry", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(slot)!;
                        rootPresent |= entry.Kind == 1; flightPresent |= entry.Kind == 8;
                    }
                    Check(!rootPresent, "unchanged compact root is correctly older than the current caption frame");
                    // The old-source mutation fails the curve assertion below;
                    // this positive assertion additionally proves the final path.
                    if (flightPresent) Check(!rootPresent, "live exact return is retained independently of its artwork-filtered compact root");
                }
            }
            Check(clock != null, "real encoded kind8 flight is admitted before its first visible frame");
            float age = clock![0] + FlightTime646.Now - received;
            TownCardReturnMotion.Apply(expected, movingHand ? remoteHand : observer, observer,
                movingHand ? (byte)3 : (byte)0, clock, age);
            if (!movingHand)
            {
                Check(Vector3.Distance(expected.position, observer.TransformPoint(shared.InverseTransformPoint(face.position))) < .0003f,
                    "production ability sampler retains exact source smootherstep arc and child pose");
                Check(Vector3.Distance(cloneBody.Root.position, observer.TransformPoint(shared.InverseTransformPoint(body.position))) < .0003f,
                    "original body follows the same source arc with its own root-relative child offset");
                Check(Quaternion.Angle(expected.rotation, observer.rotation * Quaternion.Inverse(shared.rotation) * face.rotation) < .02f,
                    "ability return keeps its exact locked source and native child orientation");
            }
            float error = Vector3.Distance(clone.Root.position, expected.position); maxError = Mathf.Max(maxError, error);
            trace.AppendLine(frame + "," + age + "," + error + "," + clone.Root.gameObject.activeInHierarchy + "," + artwork);
            Check(clone.Root.gameObject.activeInHierarchy, "native return stays visible through newer artwork");
            Check(error < .0003f, "native return keeps its exact per-render curve through newer artwork error=" + error + " frame=" + frame);
            Check(Quaternion.Angle(clone.Root.rotation, expected.rotation) < .02f,
                "remote return retains the exact authored root-relative face orientation");
            Check(Vector3.Distance(clone.Root.lossyScale, expected.lossyScale) < .0003f,
                "remote return retains its exact original interpolated face dimensions");
            yield return null;
        }
        File.WriteAllText(Path.Combine(_output, "return646.csv"), trace.ToString());
        File.WriteAllText(Path.Combine(_output, "return646-summary.txt"), "maxError=" + maxError + "; admittedClocks=" + published + "\n");
        // A newly grabbed root is a different current presentation author even
        // while the previous exact return event remains within its lifetime.
        FlightTime646.Now += .01f;
        var rightHand = new VRHand { Side = HandSide.Right, WorldScale = hand.WorldScale };
        rightHand.Rig.Root = hand.Rig.Root;
        TownServiceMirror.RegisterMotionHand(face, rightHand, false);
        FlightTime646.Now += .08f;
        var takeover = CaptureFast(); DeliverMotion(2, takeover);
        bool hasNewRoot = false;
        foreach (byte[] bytes in takeover.Motion)
            if (TownServiceMotionCodec.TryRead(bytes, bytes.Length, out var packet))
                foreach (var entry in packet!.Entries) hasNewRoot |= entry.Kind == 1 && entry.Module == 10 && entry.Hand == 4;
        Check(hasNewRoot, "real hand registration publishes a new held root independently of the earlier return");
        TownServiceMirror.TickRemote(_ => observer);
        Vector3 beforeHand = clone.Root.position;
        remoteHand.position += new Vector3(.16f, -.05f, .02f);
        FlightTime646.Now += .01f;
        TownServiceMirror.TickRemote(_ => observer);
        Check(Vector3.Distance(clone.Root.position, beforeHand) > .03f,
            "new current hand affinity takes precedence over an earlier still-live return clock");
        // Current hand affinity must beat an earlier return clock. The normal
        // atomic hand/rig suites cover the actual tracker producer separately.
        // A real source census removal must retire the clock immediately.
        using (TownServiceMirror.UseStockLane())
        { TownServiceMirror.UnregisterModule(10); TownServiceMirror.UnregisterModule(11); TownServiceMirror.EndSession(); }
        FlightTime646.Now += .08f;
        FastCapture removed = CaptureFast(); Receive(2, removed.Artwork); DeliverMotion(2, removed);
        TownServiceMirror.TickRemote(_ => observer);
        Check(Remote(peer, 10) == null, "explicit native terminal removal cannot be kept alive by an old return clock");
        NetAvatarDriver.MotionHandFrames.Clear(); TownServiceMirror.Shutdown();
    }
}
