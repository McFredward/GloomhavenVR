using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GloomhavenVR.Cards;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.Rig;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;

internal static class YawClock665
{
    internal static bool Controlled;
    internal static float Now;
    internal static float Read => Controlled ? Now : Time.unscaledTime;
}

public static partial class MirrorProgram
{
    private static IEnumerator Yaw665()
    {
        foreach (byte service in new byte[] { 3, 1 })
        foreach (bool headers in new[] { false, true })
        foreach (bool irregular in new[] { false, true })
        {
            TownServiceMirror.Shutdown(); Baselines.Clear(); YawClock665.Controlled = false;
            Transform owner = Go("665 owner shared world").transform;
            Transform observer = Go("665 observer shared world").transform;
            observer.SetPositionAndRotation(new Vector3(7f, .2f, -.3f), Quaternion.Euler(0f, 79f, 0f));
            observer.localScale = Vector3.one * 1.2f;
            TownServiceMirror.SharedFrameForRemote = _ => observer;
            Transform palm = Go("665 actual activity palm", owner).transform;
            palm.localPosition = new Vector3(.4f, .5f, -.2f);
            Transform seat = Go("665 actual offering seat", owner).transform;
            Transform card = Go("665 source card", seat).transform;
            VRCard actual = card.gameObject.AddComponent<VRCard>();
            actual.FixtureBacking(service == 3 ? new Vector2(.14406f, .2205f) : new Vector2(.14406f, .14406f));
            Transform body = card.Find("Visual/Backing");
            RectTransform canvas = (RectTransform)Go("FaceCanvas", card).transform;
            canvas.localScale = Vector3.one * .00049f;
            canvas.localPosition = new Vector3(0f, 0f, -.0012f);
            canvas.sizeDelta = service == 3 ? new Vector2(294f, 450f) : new Vector2(294f, 294f);
            canvas.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            RectTransform face = (RectTransform)Go("Actual native printed face", canvas).transform;
            face.sizeDelta = canvas.sizeDelta; face.gameObject.AddComponent<CanvasGroup>();
            Image ink = Image("Original printed artwork", face, Vector2.zero, face.sizeDelta, Color.red);
            Texture2D texture = new Texture2D(2, 2); texture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white }); texture.Apply();
            Sprite first = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * .5f);
            first.name = "665 first original";
            Sprite second = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * .5f);
            second.name = "665 second original";
            Assets.Add(texture); Assets.Add(first); Assets.Add(second); ink.sprite = first;
            TownServiceNativeEnhancementCardMask? mask = null;
            RectTransform? holder = null, sourceRow = null, sourceRing = null;
            if (service == 3)
            {
                RectTransform nativeCanvas = (RectTransform)Go("Original converted enhancement canvas", owner).transform;
                nativeCanvas.localScale = new Vector3(.00073f, .00116f, .001f);
                nativeCanvas.localRotation = Quaternion.Euler(0f, 27f, 11f);
                nativeCanvas.sizeDelta = new Vector2(680f, 660f);
                nativeCanvas.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                holder = (RectTransform)Go("CardHilight", nativeCanvas).transform;
                holder.sizeDelta = new Vector2(325f, 450f); holder.localScale = Vector3.one * .52f;
                holder.gameObject.AddComponent<CanvasGroup>();
                var highlighter = holder.gameObject.AddComponent<UIEnhancementCardHighlighter>();
                var nativeCard = Go("Native pooled card", holder).AddComponent<AbilityCardUI>();
                RectTransform nativePrint = (RectTransform)Go("FullAbilityCard", nativeCard.transform).transform;
                nativePrint.sizeDelta = face.sizeDelta; nativePrint.localScale = Vector3.one * .84f;
                nativePrint.pivot = new Vector2(.31f, .68f); nativePrint.anchoredPosition = new Vector2(19f, -11f);
                nativeCard.fullAbilityCard = nativePrint; highlighter.Card = nativeCard;
                Image row = Image("Original selected row", nativePrint, new Vector2(47f, -62f), new Vector2(171f, 82f), Color.cyan);
                row.gameObject.AddComponent<UIEnhancementButtonHighlight>(); sourceRow = row.rectTransform;
                RectTransform aura = (RectTransform)Go("Aura", holder).transform;
                aura.sizeDelta = new Vector2(600f, 600f);
                sourceRing = Image("Original aura ink", aura, Vector2.zero, new Vector2(520f, 520f), new Color(.1f, .7f, 1f, .35f)).rectTransform;
                TownServiceEnhancementHandoff.PhysicalCardFace = face;
                mask = nativeCard.gameObject.AddComponent<TownServiceNativeEnhancementCardMask>(); mask.Mask();
            }
            var head = VRRigDriver.HeadCamera!.transform;
            head.position = palm.position - Vector3.forward;
            TownServiceOfferingPose.Place(seat, palm, owner, 0f);
            mask?.SendMessage("LateUpdate");
            string faceAddress = service == 3 ? "face.665|" : "item.665|";
            string bodyAddress = service == 3 ? TownServiceAbilityBody.Key(actual) + "|" : "inspectionbody.3e138dbe.3e138dbe.p|";
            TownServiceMirror.RegisterMotionOffering(face, true); TownServiceMirror.RegisterMotionOffering(body, true);
            if (service == 3) TownServiceMirror.RegisterOfferedPhysical(body, face);
            TownServiceMirror.RegisterTemplate(service, 1, face, address: faceAddress);
            TownServiceMirror.RegisterTemplate(service, 2, body, address: bodyAddress);
            if (holder != null) TownServiceMirror.RegisterTemplate(service, 3, holder, address: "enchant.holder|");
            TownServiceMirror.BeginSession(service, 665, owner, owner);
            TownServiceMirror.SetLocalTransactionActive(service, true);
            TownServiceMirror.RegisterModule(11, 1, face, address: faceAddress);
            TownServiceMirror.RegisterModule(12, 2, body, address: bodyAddress);
            if (holder != null) { TownServiceMirror.RegisterModule(10, 3, holder, address: "enchant.holder|"); TownServiceMirror.SetPriority(10, true); }
            TownServiceMirror.SetPriority(11, true); TownServiceMirror.SetPriority(12, true);
            FastCapture initial = OfferedCapture629(); Receive(1, initial.Artwork); DeliverMotion(1, initial);
            IEnumerator settle = FastSettle(observer, .14f); while (settle.MoveNext()) yield return settle.Current;
            TownServiceBinding copy = Remote(1, 11)!;
            TownServiceBinding bodyCopy = Remote(1, 12)!;
            RectTransform? copiedRow = holder == null ? null : (RectTransform)Remote(1, 10)!.Root.Find("Native pooled card/FullAbilityCard/Original selected row");
            RectTransform? copiedRing = holder == null ? null : (RectTransform)Remote(1, 10)!.Root.Find("Aura/Original aura ink");
            Check(copy != null && Remote(1, 12) != null, "665 both original physical parts admitted before yaw");
            float began = Time.unscaledTime + 1f;
            YawClock665.Controlled = true;
            float previous = 0f, previousSource = 0f;
            string csv = Path.Combine(_output, "yaw665-service" + service + "-headers" + headers + "-irregular" + irregular + ".csv");
            File.WriteAllText(csv, "frame,time,sourceYaw,remoteYaw,deltaYaw,sourceDelta,artworkPackets,motionPackets\n");
            var queue = new List<(int Due, byte[] Bytes, Vector3[]? Row, Vector3[]? Ring)>();
            Vector3[]? sampledRow = sourceRow == null ? null : NativeRowCorners665(face, sourceRow);
            Vector3[]? sampledRing = sourceRing == null ? null : NativeRowCorners665(face, sourceRing);
            ulong sampledSequence = 0;
            int packets = 0;
            for (int frame = 0; frame <= 180; frame++)
            {
                float age = frame / 90f;
                YawClock665.Now = began + age;
                float angle = irregular && age > 1f ? 36f - (age - 1f) * 54f : age * 36f;
                head.position = palm.position - Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                TownServiceOfferingPose.Place(seat, palm, owner, age);
                Check(Mathf.Abs(seat.position.y - palm.position.y - .17f - .006f * Mathf.Sin(age * 1.8f)) < .000001f,
                    "owner retains the exact native offered hover amplitude speed and sine waveform");
                if (sourceRing != null) sourceRing.parent.localRotation = Quaternion.Euler(0f, 0f, -72f * age);
                if (sourceRow != null) sourceRow.GetComponent<Image>().color = frame % 12 < 6 ? Color.cyan : Color.green;
                mask?.SendMessage("LateUpdate");
                if (headers && frame % 6 == 3) ink.sprite = ink.sprite == first ? second : first;
                if (frame % 6 == 0)
                    typeof(TownServiceMirror).GetField("_nextMotionSend", PrivateStatic)!.SetValue(null, 0f);
                // First sample the currently displayed pose at this same render
                // clock. A new packet may change the target, never this pose.
                TownServiceMirror.TickRemote(_ => observer);
                Quaternion before = copy.Root.rotation;
                FastCapture capture = CaptureFast(); Receive(1, capture.Artwork);
                foreach (byte[] bytes in capture.Motion)
                {
                    int index = packets++;
                    if (irregular && index == 4) continue; // One real lost event.
                    int delay = !irregular ? 0 : index == 7 ? 10 : index % 4 == 1 ? 2 : index % 4 == 3 ? 3 : 0;
                    queue.Add((frame + delay, bytes, sourceRow == null ? null : NativeRowCorners665(face, sourceRow),
                        sourceRing == null ? null : NativeRowCorners665(face, sourceRing)));
                }
                for (int i = 0; i < queue.Count;)
                    if (queue[i].Due <= frame)
                    {
                        var queued = queue[i]; byte[] bytes = queued.Bytes; queue.RemoveAt(i);
                        var delivered = new FastCapture(); delivered.Motion.Add(bytes); DeliverMotion(1, delivered);
                        TownServiceMotionCodec.TryRead(bytes, bytes.Length, out TownServiceMotionPacket? packet);
                        foreach (TownServiceMotionEntry entry in packet!.Entries)
                            if (entry.Kind == 9 && entry.Module == 10 && entry.Visible && packet.Sequence > sampledSequence)
                            { sampledRow = queued.Row; sampledRing = queued.Ring; sampledSequence = packet.Sequence; }
                    }
                    else i++;
                TownServiceMirror.TickRemote(_ => observer);
                float sourceYaw = face.eulerAngles.y;
                float remoteYaw = (Quaternion.Inverse(observer.rotation) * copy.Root.rotation).eulerAngles.y;
                float delta = frame == 0 ? 0f : Mathf.DeltaAngle(previous, remoteYaw);
                float sourceDelta = frame == 0 ? 0f : Mathf.DeltaAngle(previousSource, sourceYaw);
                File.AppendAllText(csv, frame + "," + YawClock665.Now + "," + sourceYaw + "," + remoteYaw + "," + delta + "," + sourceDelta
                    + "," + capture.Artwork.Count + "," + capture.Motion.Count + "\n");
                previous = remoteYaw; previousSource = sourceYaw;
                if (frame > 20 && !irregular)
                    Check(Mathf.Abs(delta - sourceDelta) < .08f,
                        "native UI headers preserve a continuous offered yaw clock service=" + service
                        + " headers=" + headers + " frame=" + frame + " ownerDelta=" + sourceDelta + " remoteDelta=" + delta);
                if (frame > 20)
                {
                    Check(Vector3.Distance(before * Vector3.forward, copy.Root.forward) < .00003f,
                        "arriving owner root and native UI targets cannot snap the currently rendered offered yaw service=" + service + " frame=" + frame);
                    Check(Mathf.Abs(delta) < 1.2f, "irregular delivered native turn remains bounded per render without extrapolation");
                    CheckOfferedBody665(face, body, copy.Root, bodyCopy.Root);
                    if (sourceRow != null)
                    {
                        Vector3[] received = new Vector3[4]; copiedRow!.GetWorldCorners(received);
                        for (int corner = 0; corner < 4; corner++)
                            Check(Vector3.Distance(copy.Root.TransformPoint(sampledRow![corner]), received[corner]) < .00003f,
                                "native selection rows retain their exact last transmitted original print registration during offered yaw service=" + service
                                + " frame=" + frame + " corner=" + corner);
                        var ringCorners = new Vector3[4]; copiedRing!.GetWorldCorners(ringCorners);
                        Vector3 expectedNormal = Vector3.Cross(copy.Root.TransformVector(sampledRing![3] - sampledRing[0]),
                            copy.Root.TransformVector(sampledRing[1] - sampledRing[0])).normalized;
                        Vector3 observedNormal = Vector3.Cross(ringCorners[3] - ringCorners[0], ringCorners[1] - ringCorners[0]).normalized;
                        Check(Vector3.Distance(expectedNormal, observedNormal) < .00001f,
                            "native aura retains its last transmitted original plane while following offered yaw service=" + service + " frame=" + frame);
                    }
                }
            }
            // No new owner samples: finish the finite last native target, then
            // hold it. Delayed/reordered events use the unchanged decoder and
            // receiver sequence guards, with no observer extrapolation.
            for (int frame = 181; frame <= 220; frame++)
            {
                YawClock665.Now = began + frame / 90f;
                for (int i = 0; i < queue.Count;)
                    if (queue[i].Due <= frame)
                    {
                        var delivered = new FastCapture(); delivered.Motion.Add(queue[i].Bytes); queue.RemoveAt(i); DeliverMotion(1, delivered);
                    }
                    else i++;
                TownServiceMirror.TickRemote(_ => observer);
                CheckOfferedBody665(face, body, copy.Root, bodyCopy.Root);
            }
            Quaternion expectedFacing = observer.rotation * Quaternion.Inverse(owner.rotation) * face.rotation;
            Check(Vector3.Distance(expectedFacing * Vector3.forward, copy.Root.forward) < .00001f,
                "finite offered yaw ends at the exact latest authored facing after reversal loss and reordered arrivals");
            foreach (bool back in new[] { false, true })
            {
                string label = "service" + service + "-headers" + headers + "-irregular" + irregular + "-back" + back;
                Color32[] reference = NativePixels665(body, face, 8, back, label + "-owner");
                Color32[] observed = NativePixels665(bodyCopy.Root, copy.Root, 9, back, label + "-observer");
                int inkCount = 0, different = 0;
                for (int pixel = 0; pixel < reference.Length; pixel++)
                {
                    bool sourceInk = reference[pixel].r > 40 || reference[pixel].g > 40 || reference[pixel].b > 40;
                    bool copiedInk = observed[pixel].r > 40 || observed[pixel].g > 40 || observed[pixel].b > 40;
                    if (sourceInk) inkCount++;
                    if (sourceInk != copiedInk) different++;
                }
                Check(inkCount > 3000 && different < 80,
                    "offered native front and opposing body retain their real camera silhouettes after smooth yaw service=" + service + " back=" + back);
                File.AppendAllText(Path.Combine(_output, "pixels665.txt"), label + ": sourceInk=" + inkCount
                    + ", silhouetteDifference=" + different + "\n");
            }
            CheckFlightOwnership665(service, face, card, owner);
            card.gameObject.SetActive(false); if (holder != null) holder.gameObject.SetActive(false);
            YawClock665.Now += .08f;
            FastCapture hidden = OfferedCapture629(); Receive(1, hidden.Artwork); DeliverMotion(1, hidden);
            TownServiceMirror.TickRemote(_ => observer);
            Check(!copy.Root.gameObject.activeInHierarchy && !bodyCopy.Root.gameObject.activeInHierarchy,
                "native withdrawal immediately revokes both old offered roots");
            card.gameObject.SetActive(true); if (holder != null) holder.gameObject.SetActive(true);
            YawClock665.Now += .08f;
            head.position = palm.position - Quaternion.Euler(0f, 119f, 0f) * Vector3.forward;
            TownServiceOfferingPose.Place(seat, palm, owner, 2.9f); mask?.SendMessage("LateUpdate");
            FastCapture reoffered = OfferedCapture629(); Receive(1, reoffered.Artwork); DeliverMotion(1, reoffered);
            TownServiceMirror.TickRemote(_ => observer);
            copy = Remote(1, 11)!; bodyCopy = Remote(1, 12)!;
            Check(copy != null && copy.Root.gameObject.activeInHierarchy && bodyCopy != null && bodyCopy.Root.gameObject.activeInHierarchy,
                "actual native reoffer reveals both originals without waiting for a stale flight slot");
            expectedFacing = observer.rotation * Quaternion.Inverse(owner.rotation) * face.rotation;
            Check(Vector3.Distance(expectedFacing * Vector3.forward, copy.Root.forward) < .00001f,
                "first visible reoffer paints its current authored facing rather than the withdrawn clock");
            CheckOfferedBody665(face, body, copy.Root, bodyCopy.Root);
            YawClock665.Controlled = false;
            yield return null;
        }
    }

    private static void CheckFlightOwnership665(byte service, Transform face, Transform card, Transform owner)
    {
        // Use an authentic return descriptor from the production source sampler
        // and wire decoder to test the guard's exact clock lifetime. Full native
        // callback/atomic cohort migration is covered by the independent return
        // suite, not replaced by this predicate boundary probe.
        var remotes = (IDictionary)typeof(TownServiceMirror).GetField("Remote", PrivateStatic)!.GetValue(null)!;
        object module = ((IDictionary)remotes[1]!)[(ushort)11]!;
        var peers = (IDictionary)typeof(TownServiceMirror).GetField("MotionPeers", PrivateStatic)!.GetValue(null)!;
        object peer = peers[1]!;
        var slots = (IDictionary)peer.GetType().GetField("Slots", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(peer)!;
        var key = new TownServiceMotionKey(1, 0, 11, 0, 0, 0);
        object sample = slots[key]!;
        var entry = (TownServiceMotionEntry)sample.GetType().GetField("Entry", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(sample)!;
        MethodInfo guard = typeof(TownServiceMirror).GetMethod("ContinuousOfferedRoot", PrivateStatic)!;
        Check((bool)guard.Invoke(null, new object[] { 1, module, sample, YawClock665.Now })!, "current visible exact offered original owns its continuous root clock");
        foreach (byte kind in new byte[] { 8 })
        {
            float[] numbers = TownCardReturnMotion.Capture(face, card, owner, null, 0f, .05f, 0, 0f,
                card.localToWorldMatrix, card.rotation, card.localToWorldMatrix, card.rotation, Vector3.zero);
            var flight = new TownServiceMotionEntry { Kind = kind, Service = service, Session = entry.Session, Module = entry.Module,
                Structure = entry.Structure, Lane = 0, Revision = 665, Hand = 0, Numbers = numbers };
            ulong sequence = (ulong)sample.GetType().GetField("ReceivedSequence", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(sample)!;
            var packet = new TownServiceMotionPacket { Sequence = sequence + 1, SampleTime = YawClock665.Now };
            packet.Entries.Add(flight);
            byte[] bytes = TownServiceMotionCodec.TryWritePacked(packet)!;
            Check(bytes != null && TownServiceMotionCodec.TryRead(bytes, bytes.Length, out packet), "native return lifetime input survives the existing frozen wire grammar");
            TownServiceMirror.ReceiveMotion(1, packet!);
            Check(!(bool)guard.Invoke(null, new object[] { 1, module, sample, YawClock665.Now })!, "a currently live exact native return supersedes the offered root clock");
            YawClock665.Now += .4f;
            Check((bool)guard.Invoke(null, new object[] { 1, module, sample, YawClock665.Now })!, "expired native returns cannot hold a current offered clock until the three second peer expiry");
            slots.Remove(flight.Key); // Explicit end of this isolated predicate boundary.
        }
    }

    private static Vector3[] NativeRowCorners665(RectTransform print, RectTransform row)
    {
        var corners = new Vector3[4]; row.GetWorldCorners(corners);
        for (int i = 0; i < corners.Length; i++) corners[i] = print.InverseTransformPoint(corners[i]);
        return corners;
    }

    private static void CheckOfferedBody665(RectTransform originalPrint, Transform originalBody, Transform copiedPrint, Transform copiedBody)
    {
        Mesh source = originalBody.GetComponent<MeshFilter>().sharedMesh;
        Check(ReferenceEquals(source, copiedBody.GetComponent<MeshFilter>().sharedMesh), "offered yaw retains the actual native opposing body and printed-face mesh");
        foreach (Vector3 vertex in source.vertices)
        {
            Vector3 expected = copiedPrint.TransformPoint(originalPrint.InverseTransformPoint(originalBody.TransformPoint(vertex)));
            Check(Vector3.Distance(expected, copiedBody.TransformPoint(vertex)) < .00004f,
                "offered yaw retains coherent native front and opposing body vertices on every render");
        }
    }

    private static Color32[] NativePixels665(Transform body, Transform print, int layer, bool back, string name)
    {
        foreach (GameObject fixture in Objects) if (fixture != null) Layer(fixture.transform, 30);
        Layer(body, layer); Layer(print, layer);
        foreach (Canvas canvas in print.GetComponentsInParent<Canvas>(true)) canvas.gameObject.layer = layer;
        _camera.cullingMask = 1 << layer;
        Quaternion view = back ? print.rotation * Quaternion.Euler(0f, 180f, 0f) : print.rotation;
        _camera.transform.SetPositionAndRotation(print.position - view * Vector3.forward * 2f, view);
        _camera.orthographicSize = ((RectTransform)print).rect.height * .65f;
        // Normalize only this measurement camera to each already-verified native
        // print matrix. Never change a production body, print or observer pose.
        Matrix4x4 localView = Matrix4x4.TRS(new Vector3(0f, 0f, back ? 10f : -10f),
            back ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity, Vector3.one);
        _camera.worldToCameraMatrix = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * localView.inverse * print.worldToLocalMatrix;
        Canvas.ForceUpdateCanvases();
        var target = new RenderTexture(256, 192, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
        var pixels = new Texture2D(256, 192, TextureFormat.RGBA32, false);
        bool culling = GL.invertCulling;
        try
        {
            GL.invertCulling = culling ^ (print.localToWorldMatrix.determinant < 0f);
            _camera.targetTexture = target; _camera.Render(); RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 256, 192), 0, 0); pixels.Apply();
            File.WriteAllBytes(Path.Combine(_output, name + ".png"), pixels.EncodeToPNG());
            return pixels.GetPixels32();
        }
        finally
        {
            RenderTexture.active = null; _camera.targetTexture = null; GL.invertCulling = culling;
            _camera.ResetWorldToCameraMatrix();
            UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(pixels);
        }
    }
}
