using System;
using System.IO;
using System.Globalization;
using GloomhavenVR.Net;
using GloomhavenVR.WorldUI;
using UnityEngine;

/// <summary>Actual seventh-kind coordinator/register/frame methods; native window admission,
/// pose writers and managed Unity arithmetic are explicit ports. Results' independent Unity
/// lifecycle/final-picture fixture, not this transport proof, owns physical rendering.</summary>
internal static class ResultsPoseCases
{
    private static void Configure(int id, bool host = false, bool knownHost = true)
    {
        ScenarioResultsPose.Reset(); SharedWindowMoves.Reset();
        Proof.Configure(id, host, knownHost);
        SharedWindowMoves.Bind(() => true);
        ScenarioResultsNative.Window = new UnityEngine.UI.UIWindow();
    }

    internal static void Run()
    {
        IndependentMotionOrdering();
        ReplayProducerMotion();
        Configure(1, true);
        GrabbableModal author = SharedWindows.Grabs[SharedWindowKind.Results];
        author.Root.position = Proof.HostPose.Position; author.Root.rotation = Proof.HostPose.Rotation;
        ScenarioResultsPose.BeginAuthorOpening();
        var original = new ScenarioResultsState { Visible = true, Revision = 1 };
        ModalFallback.Ready = false;
        Proof.Check(!ScenarioResultsPose.Capture(original),
            "results native-open provisional frame waits for the fitted source reveal");
        ModalFallback.Ready = true;
        Proof.Check(ScenarioResultsPose.Capture(original) && original.Key != 0 && original.Author == 1
            && original.Move.HasValue && original.Move.Value.Kind == 7,
            "results actual author captures the independent seventh-kind opening");
        Proof.Check(!new SharedWindowMoveOrder(new[] { original.Move!.Value }).Valid,
            "results seventh-kind row never enters the frozen six-kind companion123");
        Proof.Check(original.Reference == author.SharedReferenceWorldScale,
            "results retains the source render reference instead of ambient viewer zoom");

        Configure(2);
        Proof.Check(ScenarioResultsPose.Observe(1, original), "results original publisher announces its exact opening");
        Proof.Check(ScenarioResultsPose.TryInitial(out Vector3 first, out Quaternion facing, out float factor)
            && Proof.Same(first, Proof.HostPose.Position) && Proof.Same(facing, Proof.HostPose.Rotation),
            "results first reveal uses author world pose and not a private observer seat");
        GrabbableModal observer = SharedWindows.Grabs[SharedWindowKind.Results];
        Proof.Check(observer.SharedReferenceWorldScale == original.Reference && factor == original.Size,
            "results adopts reference before the first physical reveal");
        foreach (float zoom in new[] { .02f, .2f, 1f, 20f, 200f })
        {
            PanelLayout.WorldScale = zoom; PanelLayout.Origin = new Vector3(zoom, -zoom, 2 * zoom);
            PanelLayout.Yaw = Quaternion.Euler(0, 10 * zoom % 360, 0);
            ScenarioResultsPose.Tick();
            Proof.Check(Proof.Same(observer.Root.position, Proof.HostPose.Position)
                && Proof.Same(observer.Root.rotation, Proof.HostPose.Rotation)
                && observer.SharedReferenceWorldScale == original.Reference,
                "results absolute geometry ignores independent observer scale/seat " + zoom);
        }

        SharedWindowInteractionBridge.Began?.Invoke(SharedWindowKind.Results);
        observer.Root.position = Proof.HandPose.Position; observer.Root.rotation = Proof.HandPose.Rotation;
        var manual = new ScenarioResultsState { Visible = true, HasPresentation = false, Revision = 2 };
        Proof.Check(ScenarioResultsPose.Capture(manual) && manual.Author == 1 && manual.Move.HasValue
            && manual.Move.Value.Phase == SharedWindowMovePhase.Held,
            "results manual participant publishes a pose claim without impersonating content author");
        SharedWindowInteractionBridge.Released?.Invoke(SharedWindowKind.Results);
        Proof.Check(ScenarioResultsPose.Capture(manual) && manual.Move!.Value.Phase == SharedWindowMovePhase.Released,
            "results manual release retains the original shared gesture clock");
        Configure(3);
        Proof.Check(ScenarioResultsPose.Observe(1, original) && ScenarioResultsPose.Observe(2, manual),
            "results third observer admits content author plus a distinct native drag participant");
        ScenarioResultsPose.Tick();
        observer = SharedWindows.Grabs[SharedWindowKind.Results];
        Proof.Check(Proof.Same(observer.Root.position, Proof.HandPose.Position)
            && Proof.Same(observer.Root.rotation, Proof.HandPose.Rotation),
            "results participant's ordered pose is applied through the original grab writer");
        SharedWindowMoveEntry heartbeat = original.Move!.Value; heartbeat.Sequence += 100;
        original.Move = heartbeat;
        Proof.Check(ScenarioResultsPose.Observe(1, original), "results content refresh survives another participant's pose claim");
        ScenarioResultsPose.Tick();
        Proof.Check(Proof.Same(observer.Root.position, Proof.HandPose.Position),
            "results repeated author initialization cannot rewind an accepted human drag");
        var foreign = new ScenarioResultsState { Author = 99, Key = 999, Visible = true };
        Proof.Check(!ScenarioResultsPose.Observe(99, foreign), "results refuses a foreign content author");
        Proof.Check(!ScenarioResultsPose.Observe(2, new ScenarioResultsState { Author = 1, Key = manual.Key + 1,
            HasPresentation = false, Visible = true }), "results pose-only packet cannot establish another opening");
        // Native close retires the old key even if the unreliable close heartbeat
        // were lost. A future author picture arriving before local native Show survives.
        Configure(2);
        Proof.Check(ScenarioResultsPose.Observe(1, original), "results old episode begins for close guard");
        ScenarioResultsPose.ObserveNativeBoundary(out _, out _);
        ScenarioResultsNative.Window!.IsOpen = ScenarioResultsNative.Window.IsVisible = false;
        Proof.Check(ScenarioResultsPose.ObserveNativeBoundary(out int retiredAuthor, out uint retiredKey)
            && retiredAuthor == 1 && retiredKey == original.Key,
            "results actual native close retires the exact visible opening");
        Proof.Check(!ScenarioResultsPose.TryInitial(out _, out _, out _)
            && !ScenarioResultsPose.Observe(1, original), "results lost close packet cannot resurrect its cached picture");
        var future = new ScenarioResultsState { Key = original.Key + 1, Author = 1, Visible = true,
            Revision = 100, Pose = original.Pose, Origin = original.Origin,
            Rotation = original.Rotation, Scale = original.Scale, Reference = original.Reference };
        SharedWindowMoveEntry nextOpening = original.Move!.Value;
        nextOpening.ContentKey = future.Key; nextOpening.Opening++; nextOpening.Sequence++;
        future.Move = nextOpening;
        Proof.Check(ScenarioResultsPose.Observe(1, future) && ScenarioResultsPose.TryInitial(out _, out _, out _),
            "results early new opening is retained while its local native template is hidden");
        ScenarioResultsNative.Window.IsOpen = ScenarioResultsNative.Window.IsVisible = true;
        ScenarioResultsPose.ObserveWindowVisibility(ScenarioResultsNative.Window, true);
        Proof.Check(!ScenarioResultsPose.ObserveNativeBoundary(out _, out _) && ScenarioResultsPose.Key == future.Key,
            "results first native reveal does not accidentally retire its early matching picture");
        // Native events preserve the logical boundary even if Hide and Show both
        // happen while this observer has no Update, e.g. during a brief suspension.
        Configure(2);
        ScenarioResultsPose.Observe(1, original); ScenarioResultsPose.ObserveNativeBoundary(out _, out _);
        ScenarioResultsPose.Observe(1, future);
        ScenarioResultsPose.ObserveWindowVisibility(ScenarioResultsNative.Window!, false);
        ScenarioResultsPose.ObserveWindowVisibility(ScenarioResultsNative.Window!, true);
        Proof.Check(ScenarioResultsPose.ObserveNativeBoundary(out retiredAuthor, out retiredKey)
            && retiredKey == original.Key && ScenarioResultsPose.Key == future.Key
            && ScenarioResultsPose.TryInitial(out _, out _, out _),
            "results exact transition event retires old episode and preserves already received future one");
        ScenarioResultsPose.Reset();
        Proof.Check(!ScenarioResultsPose.TryInitial(out _, out _, out _), "results reset revokes cached reveal and pose");
        Configure(2, false, false);
        ScenarioResultsPose.BeginAuthorOpening();
        Proof.Check(ScenarioResultsPose.Author == 2 && ScenarioResultsPose.Key != 0,
            "results one compatible VR surrogate remains movable with a native flat host");
        ScenarioResultsPose.Reset(); SharedWindowMoves.Reset();
    }

    private static ScenarioResultsState Moving(ulong revision, uint sequence, float x, bool picture = false)
        => new() { Key = 71, Author = 1, Revision = revision, Visible = true, HasPresentation = picture,
            Pose = new RigPose { Position = new Vector3(x, 0, 0), Rotation = Quaternion.identity },
            Move = new SharedWindowMoveEntry { Kind = 7, Phase = SharedWindowMovePhase.Held,
                ContentKey = 71, OpeningAuthor = 1, Opening = 1, Claim = 9, Sequence = sequence } };

    private static void IndependentMotionOrdering()
    {
        Configure(2);
        var fresh = Moving(10, 10, 10);
        Proof.Check(!ScenarioResultsPose.Observe(1, fresh) && ScenarioResultsPose.Key == 0
            && !ScenarioResultsPose.TryInitial(out _, out _, out _),
            "early independent Results motion cannot manufacture its native opening or picture");
        var basis = Moving(1, 1, 0, true);
        Proof.Check(ScenarioResultsPose.Observe(1, basis) && ScenarioResultsPose.Observe(1, fresh),
            "the retained pre-bootstrap motion applies immediately after its exact full opening");
        ScenarioResultsPose.Tick();
        var grab = SharedWindows.Grabs[SharedWindowKind.Results];
        Proof.Check(Proof.Same(grab.Root.position, new Vector3(10, 0, 0)), "first independent live target reaches the actual grab writer");
        var late = Moving(5, 5, -100, true); late.Move = null;
        Proof.Check(ScenarioResultsPose.Observe(1, late), "delayed complete Results content remains admissible after a newer motion revision");
        ScenarioResultsPose.Tick();
        Time.unscaledTime += .3f; ScenarioResultsPose.Tick();
        Proof.Check(Proof.Same(grab.Root.position, new Vector3(10, 0, 0)),
            "delayed complete Results content cannot rewind the newer independent live pose");
        Proof.Check(!ScenarioResultsPose.Observe(1, Moving(9, 9, -20)), "out-of-order independent Results pose is rejected");
        grab.IsGrabbed = true; grab.Root.position = new Vector3(22, 0, 0);
        ScenarioResultsPose.Observe(1, Moving(11, 11, 11)); ScenarioResultsPose.Tick();
        Proof.Check(Proof.Same(grab.Root.position, new Vector3(22, 0, 0)), "actual local grip retains physical Results pose authority");
        grab.IsGrabbed = false;
        var nextMover = Moving(1, 1, 30); var row = nextMover.Move!.Value; row.Claim = 10; nextMover.Move = row;
        Proof.Check(ScenarioResultsPose.Observe(3, nextMover), "a different mover uses its own revision clock and the existing winning gesture");
        ScenarioResultsPose.Tick();
        Proof.Check(Proof.Same(grab.Root.position, new Vector3(30, 0, 0)), "a new winning Results mover seeds the existing smooth track exactly once");
        Proof.Check(ScenarioResultsPose.Observe(1, Moving(12, 12, -30, true)), "a losing original writer still updates native Results content");
        Time.unscaledTime += .3f; ScenarioResultsPose.Tick();
        Proof.Check(Proof.Same(grab.Root.position, new Vector3(30, 0, 0)), "a losing original writer cannot replace the winning Results mover pose");
        ScenarioResultsPose.ForgetPeer(3);
        Proof.Check(!ScenarioResultsPose.TryInitial(out _, out _, out _), "peer retirement discards the retained independent Results motion target");
        ScenarioResultsPose.ObserveNativeBoundary(out _, out _);
        ScenarioResultsNative.Window!.IsOpen = ScenarioResultsNative.Window.IsVisible = false;
        ScenarioResultsPose.ObserveNativeBoundary(out _, out _);
        Proof.Check(!ScenarioResultsPose.Observe(1, fresh), "native close rejects retained live motion from the retired Results opening");
        var next = Moving(13, 13, 40, true); next.Key = 72; row = next.Move!.Value;
        row.ContentKey = 72; row.Opening = 2; next.Move = row;
        Proof.Check(ScenarioResultsPose.Observe(1, next) && !ScenarioResultsPose.Observe(1, fresh),
            "a later Results opening accepts its own baseline and rejects old live motion");
    }

    // The transport companion writes these exact decoded production emissions.
    // This projection contains every field consumed by ScenarioResultsPose; no
    // graph reconstruction or interpolation algorithm is substituted here.
    private static void ReplayProducerMotion()
    {
        string? path = Environment.GetEnvironmentVariable("GVR_RESULTS_MOTION_TRACE");
        if (path == null) return;
        Configure(2);
        string[] lines = File.ReadAllLines(path); int at = 0, movingFrames = 0, stalePictures = 0;
        ulong latestMotion = 0; float worstError = 0, greatestStep = 0, previousX = 0;
        var grab = SharedWindows.Grabs[SharedWindowKind.Results];
        for (int frame = 0; frame <= 450; frame++)
        {
            float now = frame / 90f; Time.unscaledTime = 10 + now; ++Time.frameCount;
            while (at < lines.Length)
            {
                string[] cells = lines[at].Split('\t');
                float F(int i) => float.Parse(cells[i], CultureInfo.InvariantCulture);
                uint U(int i) => uint.Parse(cells[i], CultureInfo.InvariantCulture);
                int I(int i) => int.Parse(cells[i], CultureInfo.InvariantCulture);
                if (F(0) > now + .00001f) break;
                ++at;
                var state = new ScenarioResultsState { Revision = ulong.Parse(cells[2], CultureInfo.InvariantCulture),
                    HasPresentation = I(3) != 0, Visible = I(4) != 0, Key = U(5), Author = I(6), SampleTime = F(7),
                    Pose = new RigPose { Position = new Vector3(F(8), F(9), F(10)), Rotation = new Quaternion(F(11), F(12), F(13), F(14)) },
                    Size = F(15), Reference = F(16), Origin = new Vector3(F(17), F(18), F(19)),
                    Rotation = new Quaternion(F(20), F(21), F(22), F(23)), Scale = F(24),
                    Move = new SharedWindowMoveEntry { Kind = (byte)I(25), Phase = (SharedWindowMovePhase)I(26),
                        ContentKey = U(27), OpeningAuthor = I(28), Opening = U(29), Claim = U(30),
                        Sequence = U(31), ParentClaim = U(32), ParentAuthor = I(33) } };
                if (!state.HasPresentation) latestMotion = Math.Max(latestMotion, state.Revision);
                else if (state.Revision < latestMotion) ++stalePictures;
                ScenarioResultsPose.Observe(I(1), state);
            }
            ScenarioResultsPose.Tick();
            if (now > 1 && now < 4.9f)
            {
                ++movingFrames; float x = grab.Root.position.x;
                worstError = Math.Max(worstError, Math.Abs(x - (now - .5f) * 2));
                greatestStep = Math.Max(greatestStep, Math.Abs(x - previousX));
            }
            previousX = grab.Root.position.x;
        }
        Proof.Check(at == lines.Length && movingFrames > 300 && stalePictures > 0,
            "actual Results producer trace reaches the actual consumer with delayed complete native pictures");
        Proof.Check(worstError < .55f && greatestStep < .15f,
            "actual Results producer and pose consumer remain smooth under incomplete heavy content, jitter, loss and late full pictures");
        Console.WriteLine($"RESULTS MOTION consumer frames={movingFrames}, stale full={stalePictures}, maximum world error={worstError:F3}, maximum frame step={greatestStep:F3}");
    }
}
