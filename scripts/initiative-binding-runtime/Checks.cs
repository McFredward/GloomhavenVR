#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Cards;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.Surfaces;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
public static class InitiativeBindingChecks
{
    [Serializable] private sealed class GeometryEvidence
    {
        public Vector3 origin, direction, center, hostScale, actorScale, rawNormal, normalizedArea, normalizedAxes;
        public Vector3[] corners = Array.Empty<Vector3>();
        public bool hit;
        public string found = "";
    }
    private static string _output = "";
    private static int _checks;
    private static readonly List<GameObject> Owned = new();
    private static void Check(bool condition, string message) { _checks++; if (!condition) throw new InvalidOperationException(message); }
    private static GameObject Go(string name, bool rect = false)
    { var g = rect ? new GameObject(name, typeof(RectTransform)) : new GameObject(name); Owned.Add(g); return g; }
    private static InitiativeTrack Native(string name, Vector3 position, float scale)
    {
        var home = Go(name + " native canvas", true).transform;
        home.position = position; home.localScale = Vector3.one * scale;
        var root = (RectTransform)Go(name, true).transform; root.SetParent(home, false);
        root.sizeDelta = new Vector2(1920, 1080); root.anchoredPosition = new Vector2(120, 350);
        var track = root.gameObject.AddComponent<InitiativeTrack>();
        var holder = (RectTransform)Go("initiativeTrackHolder", true).transform; holder.SetParent(root, false);
        holder.anchorMin = holder.anchorMax = holder.pivot = new Vector2(.5f, .5f);
        holder.sizeDelta = new Vector2(800, 180); holder.anchoredPosition = Vector2.zero;
        track.initiativeTrackHolder = holder;
        for (int i=0; i<4; i++)
        {
            var entry = (RectTransform)Go(name + " actor " + i, true).transform;
            entry.SetParent(holder, false); entry.sizeDelta = new Vector2(150, 130);
            entry.anchoredPosition = new Vector2((i - 1.5f) * 180, 0);
            var image = entry.gameObject.AddComponent<Image>(); image.color = i == 0 ? Color.yellow : Color.blue;
            var button = entry.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            var actor = entry.gameObject.AddComponent<InitiativeTrackActorBehaviour>(); actor.avatarButton = button;
            button.onClick.AddListener(() => actor.Clicks++);
        }
        return track;
    }
    private static Vector3 Center(Transform t) => t.TransformPoint(Vector3.zero);
    private static bool Pick(InitiativeTrackSurface surface, int index)
    {
        var track = InitiativeTrack.Instance!;
        Transform actor = track.initiativeTrackHolder!.GetChild(index);
        Vector3 center = Center(actor), direction = actor.forward;
        bool hit = ((IDepthPortraitPicker)surface).TryPickPortrait(center - direction * 2, direction,
            out GameObject found, out _);
        if (index == 0 && actor.lossyScale.x < .00002f)
        {
            string evidence = Path.Combine(_output, typeof(InitiativeBindingChecks).Assembly.GetName().Name + "-geometry.json");
            if (!File.Exists(evidence))
            {
                var corners = new Vector3[4]; ((RectTransform)actor).GetWorldCorners(corners);
                Vector3 right=corners[3]-corners[0], up=corners[1]-corners[0];
                File.WriteAllText(evidence, JsonUtility.ToJson(new GeometryEvidence {
                    origin=center-direction*2, direction=direction, center=center, corners=corners,
                    hostScale=surface.Current!.HostTransform.lossyScale, actorScale=actor.lossyScale,
                    rawNormal=Vector3.Cross(right,up), normalizedArea=Vector3.Cross(right,up).normalized,
                    normalizedAxes=Vector3.Cross(right.normalized,up.normalized).normalized,
                    hit=hit, found=found!=null?found.name:"" },true));
            }
        }
        if (!hit || !ReferenceEquals(found, actor.gameObject))
        {
            var corners = new Vector3[4]; ((RectTransform)actor).GetWorldCorners(corners);
            throw new InvalidOperationException("ray resolves the exact displayed actor after native rebind: index=" + index
                + " scale=" + actor.lossyScale.ToString("F8") + " center=" + center + " corners=" + string.Join(";", corners)
                + " direction=" + direction + " found=" + (found != null ? found.name : "null"));
        }
        return true;
    }
    private static void Verify(InitiativeTrackSurface surface, InitiativeTrack expected, string reason)
    {
        Check(surface.Current != null && ReferenceEquals(surface.Current.Target, expected.transform), reason);
        Check(ReferenceEquals(surface.Animated, expected), "late animation follows the same original as the displayed panel");
        Check(ReferenceEquals(surface.Current!.FitContentRoot, expected.initiativeTrackHolder), "current original supplies the fitted row");
        Check(DepthPortraitPicks.Entries.Count == 1 && DepthPortraitPicks.Entries.ContainsKey(surface.Current.HostCanvas),
            "only current host retains its registered portrait picker");
        for (int i=0; i<4; i++) Check(Pick(surface,i), "ray resolves the exact displayed actor after native rebind");
        Vector3 offset = surface.Current.HostTransform.InverseTransformPoint(Center(expected.initiativeTrackHolder!));
        Check(offset.sqrMagnitude < .001f, "visible current row remains centered inside its own dock hit plane");
    }
    public static string Run(string output)
    {
        _checks = 0; _output=output;
        try
        {
            Choreographer.s_Choreographer = new Choreographer(); WorldUIConfig.ConversionActive = true;
            Go("native pointer boundary").AddComponent<EventSystem>();
            var board = Go("manually posed control board").transform;
            board.position = new Vector3(2,1,-3); board.rotation = Quaternion.Euler(24, 38, 0);
            board.localScale = Vector3.one * 12;
            var mount = Go("InitiativeMount").transform; mount.SetParent(board,false);
            mount.localPosition = new Vector3(0,.172f,-.009f); PlayTray.Current = new PlayTray { InitiativeMount = mount };
            var old = Native("still alive old scenario track", new Vector3(0,6,20),.14f);
            var oldRoot = (RectTransform)old.transform;
            Transform home = oldRoot.parent; Vector3 originalPosition = oldRoot.localPosition, originalScale = oldRoot.localScale;
            var surface = new InitiativeTrackSurface(); InitiativeTrack.Instance = old;
            surface.Tick(); surface.LateTick(); Verify(surface, old, "first native track adopts normally");
            var oldCanvas = surface.Current!.HostCanvas;
            Check(!surface.Current.FitEnabled, "repro enters the settled fit-disabled lifetime before replacement");
            var replacement = Native("replacement singleton", new Vector3(0,8,25),.14f);
            Check(old != null && old.gameObject.activeInHierarchy, "repro old native object stays alive and active");
            old!.gameObject.SetActive(false); // old UI may be hidden while its additive scene retires
            InitiativeTrack.Instance = replacement;
            surface.Tick(); surface.LateTick();
            Verify(surface,replacement,"replacement native singleton is adopted while old panel is still alive");
            Check(oldRoot.parent == home && oldRoot.localPosition == originalPosition && oldRoot.localScale == originalScale,
                "released original retains its exact native hierarchy and pose");
            Check(!old!.gameObject.activeSelf, "handoff preserves the old original's native hidden state");
            Check(old!.RestoredMips == 1 && replacement.RestoredMips == 0,
                "retarget restores mip ownership to the old original, never the replacement");
            Check(!DepthPortraitPicks.Entries.ContainsKey(oldCanvas), "old host portrait registration retires on rebind");
            // A singleton replacement after Update must be consumed by the actual late coordinator.
            var late = Native("late replacement", new Vector3(-4,5,19),.3f);
            InitiativeTrack.Instance = late; surface.LateTick();
            Verify(surface,late,"late native replacement is rebound before row animation and capture");
            Check(replacement.RestoredMips == 1 && late.RestoredMips == 0, "late handoff restores the exact previous original");
            // Current missing and old still alive: no current original can use a stale dock or picker.
            InitiativeTrack.Instance = null; surface.Tick(); surface.LateTick();
            Check(surface.Current == null && DepthPortraitPicks.Entries.Count == 0 && CanvasConversion.Active.Count == 0,
                "native singleton gap retires stale panels and input registrations immediately");
            Check(late.RestoredMips == 1, "singleton null gap still restores the previous original");
            InitiativeTrack.Instance = replacement; surface.Tick(); surface.LateTick();
            Verify(surface,replacement,"returning singleton rebinds without rejoining the game");
            // Framework release restores the original but the native object stays alive.
            // This is the exact production IsAlive contract, not a modeled host-lifetime gate.
            var retired = surface.Current!; CanvasConversion.Release(retired);
            Check(retired.IsAlive && retired.Target != null, "released native target can stay alive after host retirement");
            var nativePosition = ((RectTransform)replacement.transform).anchoredPosition + new Vector2(45,9);
            ((RectTransform)replacement.transform).anchoredPosition = nativePosition;
            try { surface.Tick(); surface.LateTick(); }
            catch (MissingReferenceException e) { throw new InvalidOperationException("same current native original re-adopts after its host lease was retired", e); }
            Verify(surface,replacement,"same current native original re-adopts after its host lease was retired");
            Check(!ReferenceEquals(surface.Current,retired), "new conversion replaces the retired lease without replacing the native track");
            Check(surface.Current!.OriginalPosition == nativePosition,
                "retired lease cannot overwrite subsequent native layout before new adoption");
            var stableLease = surface.Current;
            var wrapper=(RectTransform)Go("valid nested content frame",true).transform;
            wrapper.SetParent(stableLease.HostTransform,false);wrapper.sizeDelta=stableLease.HostRect.sizeDelta;
            replacement.transform.SetParent(wrapper,false);
            surface.Tick();surface.LateTick();
            Check(ReferenceEquals(surface.Current,stableLease),"valid nested content under the live host is not mistaken for lost ownership");
            Verify(surface,replacement,"nested live content keeps the current original and picker intact");
            // World pose/scale only belong to the board; target handoff never writes the board.
            foreach (float scale in new[] {.04f, 1f, 198.12f})
            {
                board.position += new Vector3(.2f,.1f,-.3f); board.rotation *= Quaternion.Euler(0,19,0);
                board.localScale = Vector3.one*scale;
                Vector3 beforePosition = board.position; Quaternion beforeRotation = board.rotation;
                surface.LateTick();
                Check(board.position == beforePosition && board.rotation == beforeRotation && board.localScale == Vector3.one*scale,
                    "dock lifetime never writes manual board pose or independent zoom");
                Verify(surface,replacement,"native ownership remains consistent through board scale changes");
                float density=surface.Current!.HostTransform.lossyScale.x / mount.lossyScale.x;
                Check(Mathf.Abs(density - 1f/PlayTray.TrayPixelsPerMeter) < 1e-6f,
                    "current row retains the original tray pixel density at every world zoom");
            }
            var picker = (IDepthPortraitPicker)surface;
            Transform currentHolder = replacement.initiativeTrackHolder!;
            Transform firstActor = currentHolder.GetChild(0);
            Vector3 firstCenter = Center(firstActor), forward = firstActor.forward;
            Check(!picker.TryPickPortrait(firstCenter + forward*2, -forward, out _, out _),
                "back-side portrait rays retain the native front-side-only rule");
            Vector3 outside = currentHolder.TransformPoint(new Vector3(10000,0,0));
            Check(!picker.TryPickPortrait(outside - forward*2, forward, out _, out _),
                "a ray outside every real portrait cannot become an actor click");
            var sizes = new List<Vector2>();
            foreach(Transform entry in currentHolder)
            {
                var rt=(RectTransform)entry; sizes.Add(rt.sizeDelta); rt.sizeDelta = new Vector2(rt.sizeDelta.x,0);
            }
            Check(!picker.TryPickPortrait(firstCenter - forward*2, forward, out _, out _),
                "zero-height native portraits retain the degenerate-geometry refusal");
            for(int i=0;i<sizes.Count;i++) ((RectTransform)currentHolder.GetChild(i)).sizeDelta = sizes[i];
            board.localScale = new Vector3(.04f,.08f,.12f); board.rotation *= Quaternion.Euler(16,28,9);
            surface.LateTick(); Verify(surface,replacement,"rotated nonuniform board never splits current portrait and input");
            var actorBoundary=firstActor.GetComponent<InitiativeTrackActorBehaviour>();
            var buttonBoundary=actorBoundary.avatarButton!; int beforeClicks=actorBoundary.Clicks;
            Check(picker.TryPickPortrait(Center(firstActor)-firstActor.forward*2,firstActor.forward,out var clicked,out _)
                && ReferenceEquals(clicked,buttonBoundary.gameObject), "front ray returns the exact original current native button");
            var pointer=new PointerEventData(EventSystem.current) { button=PointerEventData.InputButton.Left };
            buttonBoundary.OnPointerClick(pointer);
            Check(actorBoundary.Clicks==beforeClicks+1,"real uGUI pointer callback reaches the selected current original exactly once");
            buttonBoundary.interactable=false;buttonBoundary.OnPointerClick(pointer);
            Check(actorBoundary.Clicks==beforeClicks+1,"native button non-interactability remains authoritative after docking repair");
            buttonBoundary.interactable=true;
            // Existing native child animation is untouched: no blanket target/subtree flatten repair.
            var child = replacement.initiativeTrackHolder!.GetChild(0);
            child.localRotation = Quaternion.Euler(0,0,9); child.localScale = new Vector3(.9f,1.1f,1);
            child.localPosition += new Vector3(0,4,-3); Vector3 childPose = child.localPosition;
            surface.Tick(); surface.LateTick();
            Check(child.localPosition == childPose && child.localRotation == Quaternion.Euler(0,0,9)
                && child.localScale == new Vector3(.9f,1.1f,1), "native portrait child animation is not overwritten by ownership repair");
            // Native destruction + new creation, board rebuild and VR/manual-screen gate lifecycle.
            UnityEngine.Object.DestroyImmediate(replacement.gameObject);
            var next = Native("after native destruction", new Vector3(1,4,16),.2f); InitiativeTrack.Instance = next;
            surface.Tick(); surface.LateTick(); Verify(surface,next,"destroyed target also hands off through the same release path");
            PlayTray.Current = null; surface.LateTick();
            Check(surface.Current != null && ReferenceEquals(surface.Current.Target,next.transform), "temporary board rebuild does not replace native content");
            var nextMount = Go("rebuilt InitiativeMount").transform; nextMount.SetParent(board,false); nextMount.localPosition = mount.localPosition;
            PlayTray.Current = new PlayTray { InitiativeMount = nextMount }; surface.LateTick(); Verify(surface,next,"new mount reattaches the same current native track");
            WorldUIConfig.ConversionActive = false; surface.Tick();
            Check(surface.Current == null && CanvasConversion.Active.Count == 0 && DepthPortraitPicks.Entries.Count == 0,
                "VR-off restores native root and unregisters every picker");
            WorldUIConfig.ConversionActive = true; surface.Tick(); surface.LateTick(); Verify(surface,next,"VR re-enable adopts the current original afresh");
            surface.Shutdown();
            Check(surface.Current == null && CanvasConversion.Active.Count == 0 && DepthPortraitPicks.Entries.Count == 0,
                "shutdown leaves no owned panel, host, or input registration");
            return $"Initiative native-binding coordinator: {_checks} actual Unity assertions passed.";
        }
        finally
        {
            foreach (var p in new List<ConvertedPanel>(CanvasConversion.Active)) CanvasConversion.Release(p);
            for(int i=Owned.Count-1;i>=0;i--) if(Owned[i]!=null) UnityEngine.Object.DestroyImmediate(Owned[i]);
            Owned.Clear(); DepthPortraitPicks.Entries.Clear(); PlayTray.Current=null; InitiativeTrack.Instance=null;
        }
    }
}
