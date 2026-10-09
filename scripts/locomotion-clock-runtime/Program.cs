using System;
using System.Reflection;
using GloomhavenVR;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.Rig;
using UnityEngine;

public static class ClockProgram
{
    private static int _checks;
    private static Comfort? _transientObserver;
    private static void Check(bool condition, string message)
    { _checks++; if (!condition) throw new InvalidOperationException(message); }
    private static void Tick(MonoBehaviour component) => component.GetType().GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, null);
    public static void BeginTransientClock()
    {
        _checks = 0;
        GloomhavenVR.Core.VRLog.WantsDebug = true;
        GloomhavenVR.Core.VRLog.Messages.Clear();
        RigTarget.Current = new GameObject("TransientRigRoot").transform;
        VRHands.Left = new GameObject("TransientTrackedLeft").AddComponent<VRHand>();
        VRHands.Left.ThumbstickClick = true;
        VRHands.Right = null;
        _transientObserver = new GameObject("TransientClockObserver").AddComponent<Comfort>();
        _transientObserver.enabled = false;
        typeof(Comfort).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_transientObserver, null);
    }
    public static int CheckTransientClock()
    {
        Check(Time.timeScale == 1f && Time.deltaTime > 0f, "native Unity resumed frame has a live scaled clock");
        MethodInfo sample = typeof(Comfort).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic)!;
        FieldInfo nextAt = typeof(Comfort).GetField("_nextMotionSampleAt", BindingFlags.Instance | BindingFlags.NonPublic)!;
        nextAt.SetValue(_transientObserver, Time.unscaledTime - 1f);
        sample.Invoke(_transientObserver, null);
        string observation = GloomhavenVR.Core.VRLog.Messages[0];
        Check(observation.Contains("unityTimeScale=1.000") && observation.Contains("zeroScaledRequestFrames=1 ")
            && observation.Contains("minRequestedTimeScale=0.000 "),
            "bounded window retains a requested zero-clock interval after the clock resumes");
        nextAt.SetValue(_transientObserver, Time.unscaledTime - 1f);
        sample.Invoke(_transientObserver, null);
        observation = GloomhavenVR.Core.VRLog.Messages[1];
        Check(observation.Contains("zeroScaledRequestFrames=0 ") && observation.Contains("minRequestedTimeScale=1.000 "),
            "transient clock counters reset with the existing diagnostic window");
        return _checks;
    }
    public static int Run()
    {
        Check(Application.isPlaying && Time.timeScale == 0f && Time.deltaTime == 0f && Time.unscaledDeltaTime > 0f,
            "native Unity paused frame has a live tracking clock");
        var root = new GameObject("RigRoot");
        RigTarget.Current = root.transform; RigTarget.IsDevProxy = false;
        var left = new GameObject("TrackedLeft").AddComponent<VRHand>();
        var right = new GameObject("TrackedRight").AddComponent<VRHand>();
        left.Side = HandSide.Left; right.Side = HandSide.Right;
        left.transform.SetParent(root.transform, false); right.transform.SetParent(root.transform, false);
        VRHands.Left = left; VRHands.Right = right;
        var head = new GameObject("HeadCamera").AddComponent<Camera>();
        head.enabled = false; head.transform.SetParent(root.transform, false); head.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        VRRigDriver.HeadCamera = head;
        var grab = new GameObject("WorldGrabDriver").AddComponent<WorldGrab>(); grab.enabled = false;
        VRModeStateMachine.CurrentMode = VRMode.TableIdle;
        left.ThumbstickClick = left.ThumbstickClickDown = true;
        left.Grabber.Held = new object(); // Carrying a card/window never owns this distinct button.
        Tick(grab); left.ThumbstickClickDown = false;
        Check(grab.IsHandGrabbing(left) && grab.IsGrabbing, "held object preserves the world's distinct stick click");
        left.transform.localPosition += new Vector3(.2f, .15f, .1f);
        Vector3 before = root.transform.position;
        Tick(grab);
        Check(Vector3.Distance(before, root.transform.position) > .001f, "paused one-hand drag remains movable");
        Check(root.transform.position.y < before.y, "paused world drag retains its vertical component");
        left.ThumbstickClick = false; Tick(grab);
        Check(!grab.IsGrabbing && !grab.IsHandGrabbing(left), "release immediately relinquishes flight stick ownership");

        root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        left.transform.localPosition = new Vector3(-.2f, 1f, 0f); right.transform.localPosition = new Vector3(.2f, 1f, 0f);
        left.ThumbstickClick = left.ThumbstickClickDown = right.ThumbstickClick = right.ThumbstickClickDown = true;
        Tick(grab); left.ThumbstickClickDown = right.ThumbstickClickDown = false;
        left.transform.localPosition = new Vector3(-.3f, 1f, -.2f); right.transform.localPosition = new Vector3(.3f, 1f, .2f);
        Tick(grab);
        Check(root.transform.localScale.x < .999f && Quaternion.Angle(root.transform.rotation, Quaternion.identity) > .1f,
            "paused two-hand rotate and scale remain movable");
        left.HasPose = right.HasPose = false; Tick(grab);
        Check(!grab.IsGrabbing && !grab.IsHandGrabbing(left) && !grab.IsHandGrabbing(right), "tracking loss releases both world ownership slots");
        left.HasPose = right.HasPose = true;
        left.ThumbstickClick = true; left.ThumbstickClickDown = true;
        right.ThumbstickClick = right.ThumbstickClickDown = false; Tick(grab);
        Check(grab.IsHandGrabbing(left), "restored tracking and a real click can engage again");
        left.ThumbstickClick = false; Tick(grab);

        var turn = new GameObject("SnapTurnDriver").AddComponent<SnapTurn>(); turn.enabled = false;
        right.Thumbstick = new Vector2(.6f, 0f);
        Quaternion beforeTurn = root.transform.rotation;
        Vector3 pivot = head.transform.position;
        Tick(turn);
        Check(Quaternion.Angle(root.transform.rotation, beforeTurn) > .1f, "paused smooth turn remains movable");
        Check(Vector3.Distance(pivot, head.transform.position) < .0001f, "turn preserves the tracked head pivot");
        VRModeStateMachine.CurrentMode = VRMode.ModalUI; beforeTurn = root.transform.rotation;
        Tick(turn);
        Check(Quaternion.Angle(root.transform.rotation, beforeTurn) > .1f, "floating modal UI preserves comfort turning");
        VRModeStateMachine.CurrentMode = VRMode.Menu2D; beforeTurn = root.transform.rotation;
        Tick(turn);
        Check(Quaternion.Angle(root.transform.rotation, beforeTurn) < .001f, "flat main menu retains its original no-world turn gate");
        VRModeStateMachine.CurrentMode = VRMode.TableIdle;
        UiScrollFocus.NoteScrollHover(right, root, "fixture original list");
        right.Thumbstick = new Vector2(.4f, 1f); beforeTurn = root.transform.rotation; Tick(turn);
        Check(Quaternion.Angle(root.transform.rotation, beforeTurn) < .001f && turn.ScrollBlocked,
            "a real scroll keeps incidental sideways motion blocked");
        right.Thumbstick = new Vector2(.9f, .2f); Tick(turn);
        Check(Quaternion.Angle(root.transform.rotation, beforeTurn) > .1f,
            "deliberate sideways override cannot strand the scroll gate");
        // The real hover is still stamped this same frame. Override relinquishes
        // to its owner on the first rest tick; the next rest evaluation opens it.
        right.Thumbstick = Vector2.zero; Tick(turn); Tick(turn);
        Check(!turn.ScrollBlocked, "released stick resets the scroll latch");
        Check(!UiScrollFocus.IsScrolling(left), "one player's other hand never inherits a scroll claim");

        VRModeStateMachine.CurrentMode = VRMode.ModalUI;
        left.ThumbstickClick = left.ThumbstickClickDown = true; Tick(grab);
        left.ThumbstickClickDown = false; left.transform.localPosition += Vector3.right * .15f; before = root.transform.position;
        Tick(grab);
        Check(Vector3.Distance(before, root.transform.position) > .001f, "floating modal UI preserves world dragging");
        ComfortSettings.WorldGrabEnabled.Value = false; Tick(grab);
        Check(!grab.IsGrabbing && !grab.IsHandGrabbing(left), "explicit disabled world-grab releases its ownership");

        var observer = new GameObject("MotionObserver").AddComponent<Comfort>(); observer.enabled = false;
        MethodInfo sample = typeof(Comfort).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic)!;
        FieldInfo nextAt = typeof(Comfort).GetField("_nextMotionSampleAt", BindingFlags.Instance | BindingFlags.NonPublic)!;
        FieldInfo sampledRig = typeof(Comfort).GetField("_motionSampleRig", BindingFlags.Instance | BindingFlags.NonPublic)!;
        GloomhavenVR.Core.VRLog.Messages.Clear();
        sample.Invoke(observer, null);
        Check(GloomhavenVR.Core.VRLog.Messages.Count == 0, "initial observer does not allocate a diagnostic line per frame");
        Vector3 sampleStart = root.transform.position;
        root.transform.position += Vector3.right * .5f; sample.Invoke(observer, null);
        root.transform.position = sampleStart;
        nextAt.SetValue(observer, Time.unscaledTime - 1f); sample.Invoke(observer, null);
        Check(GloomhavenVR.Core.VRLog.Messages.Count == 1, "diagnostic emits one bounded requested-input sample");
        string observation = GloomhavenVR.Core.VRLog.Messages[0];
        Check(observation.Contains("phase=Comfort.LateUpdate") && observation.Contains("net=0")
            && observation.Contains("sampledTravel=") && !observation.Contains("sampledTravel=0"),
            "sampled reversal reports zero net progress separately from accumulated travel");
        Check(observation.Contains("unityTimeScale=0") && observation.Contains("click=True/False")
            && observation.Contains("pose=True") && observation.Contains("worldGrab=False"),
            "diagnostic observes current clock click pose and actual ownership gates");
        sample.Invoke(observer, null);
        Check(GloomhavenVR.Core.VRLog.Messages.Count == 1, "same-frame observation cannot bypass its sample throttle");
        GloomhavenVR.Core.VRLog.WantsDebug = false;
        nextAt.SetValue(observer, Time.unscaledTime - 1f); sample.Invoke(observer, null);
        Check(GloomhavenVR.Core.VRLog.Messages.Count == 1 && sampledRig.GetValue(observer) == null,
            "normal log level bypasses all motion sampling and string construction");
        UnityEngine.Object.DestroyImmediate(observer.gameObject);
        UnityEngine.Object.DestroyImmediate(grab.gameObject); UnityEngine.Object.DestroyImmediate(turn.gameObject);
        UnityEngine.Object.DestroyImmediate(root);
        return _checks;
    }
}
