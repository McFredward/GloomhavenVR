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
}
