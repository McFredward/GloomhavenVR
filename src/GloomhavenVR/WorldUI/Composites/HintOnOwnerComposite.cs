using System.Collections.Generic;
using GLOO.Introduction;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Parks the native introduction group inside the screen that actually produced its current
/// message. Build 504's save-game logs showed seven opens but one park, and that park captured
/// a converted host as its home. The old global isShown scan confused queued processes with the
/// current message; parking an already converted target also left two writers resizing it.
/// Message provenance now follows native queue objects (including asynchronous highlight steps),
/// and ModalFallback hands back any standalone conversion before this class records the home.
/// Native contents, callbacks and dismiss buttons remain intact. No focused/topmost window guess.
/// </summary>
internal static class HintOnOwnerComposite
{
    private const string Scope = "WorldUI";

    /// <summary>Authored uGUI px between the hint's painted BOTTOM edge and the owner window's
    /// painted bottom edge. Small on purpose: the hint sits ON the window like a tooltip, it does
    /// not hang off it.</summary>
    private const float HintGapPx = 24f;

    /// <summary>Authored uGUI px of clearance kept between the hint's ink and the owner window's own
    /// frame, so a uGUI mask on the host can never cull the hint the way it clipped the ModBuild 241
    /// confirm plate.</summary>
    private const float FrameMarginPx = 12f;

    /// <summary>Below this many authored px the placement correction is not written at all — a hint
    /// that is already where it should be must never be nudged, or the "correction" becomes the
    /// per-frame jitter that makes the two eyes disagree. That is this parker's own statement of the
    /// shared reason, so it takes the shared value (ModBuild 439, survey row R35).
    ///
    /// <para><see cref="HintGapPx"/> beside it deliberately does NOT join
    /// <see cref="ChromeParkTuning.ChromeGapPx"/>: it is the same 24 px by coincidence of taste and
    /// carries its own argument (the hint sits ON the window like a tooltip rather than hanging off
    /// it), and a number argued independently is exactly what must not be collapsed.</para></summary>
    private const float OffsetEpsilonPx = ChromeParkTuning.OffsetEpsilonPx;

    /// <summary>A hint rect smaller than this in either axis is not a message box; refuse to park it
    /// rather than do anchor arithmetic on a degenerate rect.</summary>
    private const float MinParkSizePx = 8f;

    // ---- the moved object and its home ---------------------------------------------------------

    private static UIWindow? _hint;
    private static UIWindow? _ownerWindow;
    private static RectTransform? _parked;

    private static Transform? _home;

    /// <summary>The scene the hint window lived in before the move, and whether that scene was the
    /// <c>DontDestroyOnLoad</c> one. Recorded because re-parenting MOVES a GameObject into its new
    /// parent's scene ([[adopting-a-window-takes-its-lifetime]]) — the exact mechanism that stopped
    /// 'Quest verwerfen' opening its dialog on 2026-09-03 and that
    /// <c>CanvasConversion.KeepHostInTargetScene</c> was written for. See
    /// <see cref="RefusesForScene"/>.</summary>
    private static UnityEngine.SceneManagement.Scene _homeScene;
    private static bool _homeWasPersistent;

    private static int _homeIndex;
    private static Vector2 _homeAnchorMin, _homeAnchorMax, _homePivot;
    private static Vector2 _homeAnchoredPos, _homeSizeDelta;
    private static Quaternion _homeRotation;
    private static Vector3 _homeScale;
    private static LayoutElement? _addedIgnore;
    private static bool _homeIgnoreLayout;

    /// <summary>Every transform of the parked hint whose layer this class overwrote, and the value
    /// the GAME had there — the shared record (<see cref="MovedSubtreeLayers"/>), which carries the
    /// walk, the restore guard and the two counts the report line below prints.</summary>
    private static readonly MovedSubtreeLayers Layers = new(64);

    // Native message identity changes even while the group remains continuously open.
    private static object? _message;
    private static HintMessageOrigins.Origin? _origin;
    private static Graphic? _screenDimmer;

    /// <summary>
    /// PER-HINT LATCHES FOR THE LEDGER, and they are not tidiness. <see cref="ResolveOwner"/> and
    /// <see cref="RefusesForScene"/> run EVERY tick a hint stands, so counting there without a latch
    /// would change the report's signature once per frame and turn the change-gated
    /// <c>VRLog.Note</c> into a per-frame flood — the exact failure ModBuild 331 was cleaning up
    /// when it silenced this subsystem. The counters are therefore EDGE counts: one per hint, per
    /// kind, cleared on the rising edge of the next hint window.
    /// </summary>
    private static bool _countedNoOwner;
    private static bool _countedRefused;

    /// <summary>How the owner was resolved, in the log line's own words. Never null.</summary>
    private static string _how = "no hint has been seen yet";

    // ---- the ledger ------------------------------------------------------------------------------

    private static int _parks;
    private static int _unparks;
    private static int _hintsSeen;
    private static int _noOwner;

    /// <summary>Parks refused after the owner WAS resolved — a degenerate rect, or the lifetime
    /// guard. Separate from <see cref="_noOwner"/> because the two send a reader to different
    /// files.</summary>
    private static int _refused;

    private static int _throws;
    private static bool _disabledByError;

    private static string _verdict = string.Empty;
    private static int _reports;

    /// <summary>One "no owner" per hint (see the latch fields).</summary>
    private static void CountNoOwner()
    {
        if (_countedNoOwner)
            return;
        _countedNoOwner = true;
        _noOwner++;
    }

    /// <summary>One "park refused" per hint (see the latch fields).</summary>
    private static void CountRefused()
    {
        if (_countedRefused)
            return;
        _countedRefused = true;
        _refused++;
    }

    /// <summary>True while this class is drawing a hint inside a floated window — read by nothing
    /// today, and kept because it is the one question a future reader will ask of this file.</summary>
    internal static bool Standing => _parked != null && _ownerWindow != null;

    internal static bool IsParkedContent(Transform? transform) =>
        _parked != null && transform != null
        && (ReferenceEquals(transform, _parked) || transform.IsChildOf(_parked));

    /// <summary>
    /// One tick. Called from the first lines of <c>StoryComposite.Tick</c>, i.e. from the first line
    /// of <c>ModalFallback.TickWindowLiveness</c>.
    ///
    /// <para><b>THAT CALL SITE IS LOAD-BEARING, FOR THE TWO REASONS StoryComposite RECORDS.</b> The
    /// unpark has to run BEFORE the release loop, which calls <c>CanvasConversion.Release</c> and
    /// destroys the host the hint was parked under; and the park has to run BEFORE the convert pass,
    /// so the ancestor rule has already made the hint a sub-view by the time anything would have
    /// floated it.</para>
    /// </summary>
    internal static void Tick()
    {
        if (_disabledByError)
            return;
        try
        {
            TickCore();
        }
        catch (System.Exception e)
        {
            _throws++;
            VRLog.Alert(Scope, $"HINT ON WINDOW: the tick threw ({e.GetType().Name}: {e.Message}) — "
                               + $"throw {_throws}. The hint is handed back to its own floated frame, "
                               + "which is the ModBuild 380 presentation and is reachable. The class "
                               + "disarms itself for the rest of the session rather than throwing once "
                               + "per frame.");
            _disabledByError = true;
            try
            {
                Unpark("the hint composite threw and disarmed itself");
            }
            catch (System.Exception)
            {
                // Nothing left to do: the hand-back is the recovery, and it has already failed.
            }
        }
    }

    private static void TickCore()
    {
        UIWindow? hint = HintWindow();
        bool open = hint != null && (hint.IsOpen || hint.IsVisible);

        if (!open)
        {
            if (_parked != null)
                Unpark("the hint was dismissed — the game closed the introduction window");
            _hint = null;
            _message = null;
            _origin = null;
            _screenDimmer = null;
            return;
        }

        object? message = Singleton<UIIntroductionManager>.Instance.m_CurrentlyDisplayedMessageInfo;
        // ShowNextMessage clears the current message before the group's native hide animation
        // finishes. Keep that last message's owner during its visible fade; null is not a new hint
        // and must not unpark it into a freshly spawned standalone grab bar.
        if (!ReferenceEquals(_hint, hint) || (message != null && !ReferenceEquals(_message, message)))
        {
            if (_parked != null && !ReferenceEquals(_hint, hint))
                Unpark("a different native introduction window took over");
            _hint = hint;
            _message = message;
            _origin = HintMessageOrigins.For(message);
            // LevelMessageUILayout.Init controls this exact root Image through ShowScreenBG.
            // It is a 1920-wide screen dimmer, not the narrower native message plate (the build504
            // logs also measure 644/844-wide content). Preserve its rendering, but do not let its
            // width shrink the message text threefold when parking on a 304px character column.
            // Keep the reference through the native closing fade, just like its message origin.
            _screenDimmer = Singleton<UIIntroductionManager>.Instance.LayoutGroup._currentMessage?.GetComponent<Image>();
            _hintsSeen++;
            _countedNoOwner = false;
            _countedRefused = false;
        }

        ConvertedPanel? panel = ResolveOwner(_origin?.Anchor, out UIWindow? owner, out string how);
        _how = how;

        if (panel == null || owner == null)
        {
            if (_parked != null)
                Unpark("the owner window is no longer a live floated host");
            Report(hint!, null, null);
            return;
        }

        if (_parked == null || !ReferenceEquals(_ownerWindow, owner))
        {
            if (_parked != null)
                Unpark("the owner window changed under a standing hint");
            if (!Park(hint!, owner, panel))
            {
                Report(hint!, null, null);
                return;
            }
        }
        else if (!StillOurs(owner))
        {
            Unpark("the game re-parented the hint out of the owner window");
            Report(hint!, null, null);
            return;
        }
        else
        {
            int layer = owner.gameObject.layer;
            if (Layers.Written >= 0 && _parked!.gameObject.layer != layer)
                WriteLayers(layer, _parked);
            KeepOnTop();
            ApplyPose(owner, panel);
        }

        Report(hint!, owner, panel);
    }

    // ---- identity ---------------------------------------------------------------------------------

    /// <summary>
    /// THE HINT WINDOW: <c>UIIntroductionManager</c>'s OWN <c>LevelMessageUILayoutGroup</c>, by
    /// reference. Deliberately NOT "any window carrying a LevelMessageUILayoutGroup" — the scripted
    /// tutorial box and help-text strip carry the identical component and belong to
    /// <c>LevelMessagesUIHandler</c>, which has its own family, its own placement and its own
    /// chain-pose continuity in <c>WorldUI/Modal/**</c>. [[containment-is-not-identity]] applies to
    /// component TYPE the same way it applies to ancestry: the type is shared, the instance is not.
    /// </summary>
    private static UIWindow? HintWindow()
    {
        if (!Singleton<UIIntroductionManager>.IsInitialized)
            return null;
        UIIntroductionManager mgr = Singleton<UIIntroductionManager>.Instance;
        if (mgr == null)
            return null;
        LevelMessageUILayoutGroup? group = mgr.LayoutGroup;
        return group == null ? null : group.window;
    }

    /// <summary>
    /// FROM THE INTRODUCER TO THE FLOATED WINDOW IT LIVES IN — the tooltip path first, the ancestry
    /// walk second, and a named refusal when neither answers.
    /// </summary>
    private static ConvertedPanel? ResolveOwner(Transform? anchor, out UIWindow? owner,
                                                out string how)
    {
        owner = null;
        if (anchor == null)
        {
            how = _origin?.Description ?? "native message has no recorded presentation owner";
            CountNoOwner();
            return null;
        }

        // ARM 1 — the tooltip path, byte-for-byte: ownership, never topmost-ness.
        ConvertedPanel? panel = ModalFallback.FindOwningWindow(anchor);
        if (panel != null && panel.IsAlive && panel.HostRect != null)
        {
            owner = OwnerWindowOf(anchor, panel);
            if (owner != null)
            {
                how = $"ModalFallback.FindOwningWindow(introducer '{anchor.name}') — the same "
                      + "ownership walk every local tooltip is placed by";
                return panel;
            }
        }

        // ARM 2 — the introducer's own ancestor windows, for the case where the owner's subtree has
        // not been re-parented under a host. Nearest first: the innermost floated window wins.
        for (Transform? t = anchor; t != null; t = t.parent)
        {
            var w = t.GetComponent<UIWindow>();
            if (w == null || ReferenceEquals(w, _hint))
                continue;
            ConvertedPanel? p = ModalFallback.PanelFor(w);
            if (p == null || !p.IsAlive || p.HostRect == null)
                continue;
            owner = w;
            how = $"the nearest ancestor UIWindow of the introducer '{anchor.name}' that the mod "
                  + "is floating this instant (ModalFallback.PanelFor)";
            return p;
        }

        how = $"NONE — the introducer '{anchor.name}' resolves to no floated window: neither the "
              + "tooltip ownership walk nor its own ancestor UIWindows name one the mod is drawing "
              + "right now (the owner screen is on the flat screen, or not open)";
        CountNoOwner();
        return null;
    }

    /// <summary>The game <c>UIWindow</c> that the panel converted — the object the parked hint is
    /// made a CHILD of, so <c>ModalFallback.RendersInsideFloatedAncestor</c> can find it by walking
    /// parents. Falls back to the introducer's own ancestry when the panel's target carries no
    /// window on its own GameObject.</summary>
    private static UIWindow? OwnerWindowOf(Transform anchor, ConvertedPanel panel)
    {
        if (panel.Target != null)
        {
            var onTarget = panel.Target.GetComponent<UIWindow>();
            // AND IT HAS TO BE ONE THE MOD IS ACTUALLY FLOATING. The park's whole effect is that
            // ModalFallback.RendersInsideFloatedAncestor finds this window above the hint and refuses
            // the float; a window that is not a live floated host would suppress nothing and the hint
            // would end up drawn inside a screen-space parent nobody renders in VR.
            // [[parent-wins-needs-a-real-parent]]: suppressing X because Y handles it requires Y to
            // ACTUALLY handle it.
            if (onTarget != null && ModalFallback.PanelFor(onTarget) != null)
                return onTarget;
        }
        for (Transform? t = anchor; t != null; t = t.parent)
        {
            var w = t.GetComponent<UIWindow>();
            if (w != null && !ReferenceEquals(w, _hint) && ModalFallback.PanelFor(w) != null)
                return w;
        }
        return null;
    }

    /// <summary>Is the hint still parented where THIS class put it? A window the game has taken back
    /// is no longer ours to move ([[dont-win-a-write-war]]).</summary>
    private static bool StillOurs(UIWindow owner)
    {
        if (_parked == null || owner.transform == null)
            return false;
        Transform? p = _parked.parent;
        return p != null && ReferenceEquals(p, owner.transform);
    }

    // ---- the park ---------------------------------------------------------------------------------

    /// <summary>
    /// Move the hint window whole into the owner window's rect, recording its home first so the
    /// hand-back is exact.
    ///
    /// <para>THE SIZE IS CAPTURED BEFORE THE ANCHORS ARE COLLAPSED, and that order is the whole of
    /// the ModBuild 232 lesson: the hint window is a STRETCH child of 'Introduction Canvas', so its
    /// drawn size comes entirely from its parent and collapsing its anchors to a point without
    /// writing <c>sizeDelta</c> leaves it 0x0 ([[anchors-own-a-stretch-child-size]]).</para>
    /// </summary>
    private static bool Park(UIWindow hint, UIWindow owner, ConvertedPanel panel)
    {
        if (hint.transform is not RectTransform rect || owner.transform is not RectTransform win)
        {
            _how = $"REFUSED — the hint '{hint.name}' or the owner '{owner.name}' is not a "
                   + "RectTransform, so the anchor arithmetic this class is built on does not apply. "
                   + "Nothing is moved";
            return false;
        }

        // A late owner can arrive after the fallback floated this group. Release that writer
        // before recording any transform; otherwise the saved home is a disposable mod host.
        if (!ModalFallback.ReleaseForComposite(hint))
        {
            _how = "REFUSED — the standalone hint conversion has not handed back ownership";
            CountRefused();
            return false;
        }

        Vector2 measured = rect.rect.size;
        if (measured.x < MinParkSizePx || measured.y < MinParkSizePx)
        {
            _how = $"REFUSED — the hint '{hint.name}' measures {measured.x:0}x{measured.y:0} px, "
                   + $"below the {MinParkSizePx:0} px floor: its layout has not been built yet, so "
                   + "capturing that size into sizeDelta would freeze a degenerate rect "
                   + "([[anchors-own-a-stretch-child-size]]). Nothing is moved; the park is retried "
                   + "on the next tick";
            CountRefused();
            return false;
        }
        if (RefusesForScene(hint, owner))
            return false;

        _home = rect.parent;
        _homeScene = hint.gameObject.scene;
        _homeWasPersistent = CanvasConversion.IsPersistentScene(_homeScene);
        _homeIndex = rect.GetSiblingIndex();
        _homeAnchorMin = rect.anchorMin;
        _homeAnchorMax = rect.anchorMax;
        _homePivot = rect.pivot;
        _homeAnchoredPos = rect.anchoredPosition;
        _homeSizeDelta = rect.sizeDelta;
        _homeRotation = rect.localRotation;
        _homeScale = rect.localScale;

        // ignoreLayout BEFORE the move — MapTravelConfirm's discipline, for its reason: this takes
        // the anchors and the anchoredPosition out of any parent layout group's hands rather than
        // racing it for them.
        LayoutElement? le = hint.gameObject.GetComponent<LayoutElement>();
        if (le == null)
        {
            le = hint.gameObject.AddComponent<LayoutElement>();
            _addedIgnore = le;
        }
        _homeIgnoreLayout = le.ignoreLayout;
        le.ignoreLayout = true;

        _parked = rect;
        _ownerWindow = owner;

        rect.SetParent(win, worldPositionStays: false);
        rect.SetAsLastSibling();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = measured;              // ← the size this class now owns
        rect.anchoredPosition = Vector2.zero;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;

        WriteLayers(owner.gameObject.layer, rect);
        ApplyPose(owner, panel);
        _parks++;
        return true;
    }

    /// <summary>
    /// REFUSE A PARK THAT WOULD COST THE HINT ITS LIFETIME. Re-parenting moves a GameObject into its
    /// new parent's SCENE, so parking a <c>DontDestroyOnLoad</c> window under a host that lives in an
    /// ordinary scene makes the window die at the next scene load while
    /// <c>UIIntroductionManager</c>'s serialized <c>layoutGroup</c> keeps pointing at it — every
    /// future hint in the session then throws instead of appearing. That is not a hypothetical: it is
    /// the 2026-09-03 'Quest verwerfen' deadlock, and <c>CanvasConversion.KeepHostInTargetScene</c>
    /// exists for it. The repair that class uses (<c>DontDestroyOnLoad</c> on the host) is not
    /// available here — Unity only accepts a ROOT GameObject, and the host is already parented.
    ///
    /// <para><b>SO THE ANSWER IS TO NOT PARK, AND TO SAY SO.</b> Today's evidence says this refusal
    /// should never fire: <c>UIIntroductionManager</c> derives from the game's plain
    /// <c>Singleton&lt;T&gt;</c> (decompiled Singleton.cs — <c>Awake</c> assigns <c>_instance</c> and
    /// there is no <c>DontDestroyOnLoad</c> anywhere in it), so 'Introduction Canvas' is an ordinary
    /// scene object like the windows it annotates. The guard is here because a refusal that never
    /// fires costs one scene comparison, and the failure it prevents is permanent for the session —
    /// and because if it DOES fire, the log names the exact thing a future round has to build (a
    /// persistent dock) instead of leaving it to be inferred from a dead hint chain.</para>
    /// </summary>
    private static bool RefusesForScene(UIWindow hint, UIWindow owner)
    {
        bool hintPersistent = CanvasConversion.IsPersistentScene(hint.gameObject.scene);
        if (!hintPersistent || CanvasConversion.IsPersistentScene(owner.gameObject.scene))
            return false;
        _how = $"REFUSED FOR LIFETIME — the hint window '{hint.name}' lives in the DontDestroyOnLoad "
               + $"scene and the owner '{owner.name}' lives in '{owner.gameObject.scene.name}'. "
               + "Re-parenting would move the hint into that scene and the next scene load would "
               + "DELETE it while UIIntroductionManager's serialized layoutGroup still pointed at it "
               + "— the 2026-09-03 deadlock class. Nothing is moved";
        CountRefused();
        return true;
    }

    /// <summary>Keep the hint the LAST child of the owner window, so it is drawn AND hit-tested above
    /// the owner's own content — <c>TooltipOnWindow.RaiseToWindowTop</c>'s job, for its reason: the
    /// ModBuild 190 hardware log proved a correctly placed box invisible underneath the very list it
    /// hangs inside. Change-gated: a re-write per frame would fight the owner's own layout.</summary>
    private static void KeepOnTop()
    {
        if (_parked == null || _parked.parent == null)
            return;
        int last = _parked.parent.childCount - 1;
        if (_parked.GetSiblingIndex() != last)
            _parked.SetAsLastSibling();
    }

    /// <summary>
    /// WRITE THE OWNER ROOT'S OWN LAYER OVER THE MOVED SUBTREE, recording what the game had there so
    /// the hand-back can put it back. <c>StoryComposite.WriteLayers</c> and
    /// <c>LoadoutConfirmPark.WriteLayers</c>, verbatim, and not optional here either: per-window
    /// capture cameras cull BY LAYER ([[one-shared-layer-leaks]]) and the owner is already converted
    /// when the hint arrives, so a subtree that lands between two of the conversion's own sweeps is
    /// drawn by the wrong camera or by two.
    ///
    /// <para>A FOREIGN RENDER SUBTREE IS SKIPPED WHOLE — <c>CanvasConversion.ApplyModLayer</c>'s own
    /// rule for its own reason. <c>CanvasRenderer</c> is not a <c>Renderer</c>, so ordinary uGUI is
    /// unaffected.</para>
    /// </summary>
    private static void WriteLayers(int layer, Transform root)
    {
        Layers.Begin(layer);
        Layers.Walk(root);
    }

    // ---- the placement ---------------------------------------------------------------------------

    private static readonly List<Graphic> PaintScratch = new(64);
    private static readonly Vector3[] Corners = new Vector3[4];

    /// <summary>
    /// Put the hint's INK where a tooltip would sit on this window: horizontally centred on the
    /// owner's painted content, its painted bottom <see cref="HintGapPx"/> above the owner's painted
    /// bottom, and then clamped inside the owner's own frame.
    ///
    /// <para><b>THE CORRECTION IS RELATIVE, WHICH IS WHAT MAKES IT PIVOT-FREE</b> —
    /// <c>StoryComposite.ApplyPose</c>'s argument verbatim. The hint holds a whole subtree whose ink
    /// sits wherever the game's own FixedLowerRight layout put it inside a 1920x1080 rect; there is
    /// no pivot arithmetic that could predict it. So the ink is measured where it currently is, the
    /// delta to where it should be is computed, and that delta is ADDED to
    /// <c>anchoredPosition</c> — which converges in one tick and then writes nothing.</para>
    ///
    /// <para>A measurement that fails leaves the hint where it is rather than moving it to a
    /// fabricated position: "not ready yet" is a state, never a placement.</para>
    /// </summary>
    private static void ApplyPose(UIWindow owner, ConvertedPanel? panel)
    {
        if (_parked == null || owner.transform is not RectTransform win)
            return;
        if (!TryPaintedBounds(_parked, win, panel: null, exclude: null, out Rect hintInk, out int hintCount)
            || hintCount == 0)
            return;
        if (!TryPaintedBounds(win, win, panel, exclude: _parked, out Rect ownerInk, out int ownerCount)
            || ownerCount == 0)
            return;

        Rect frame = win.rect;
        if (panel?.HostRect != null)
        {
            // Native targets can retain a 1920px screen rect while their converted capture has
            // fitted to a narrow column. Clamp in the host's actual rectangle mapped into the
            // owner's authored coordinates, not that invisible native screen reservation.
            panel.HostRect.GetWorldCorners(Corners);
            Vector3 first = win.InverseTransformPoint(Corners[0]);
            float minFrameX = first.x, maxFrameX = first.x;
            float minFrameY = first.y, maxFrameY = first.y;
            for (int i = 1; i < Corners.Length; i++)
            {
                Vector3 point = win.InverseTransformPoint(Corners[i]);
                minFrameX = Mathf.Min(minFrameX, point.x);
                maxFrameX = Mathf.Max(maxFrameX, point.x);
                minFrameY = Mathf.Min(minFrameY, point.y);
                maxFrameY = Mathf.Max(maxFrameY, point.y);
            }
            if (maxFrameX > minFrameX && maxFrameY > minFrameY)
                frame = Rect.MinMaxRect(minFrameX, minFrameY, maxFrameX, maxFrameY);
        }
        float availableWidth = Mathf.Max(1f, Mathf.Min(ownerInk.width, frame.width) - 2f * FrameMarginPx);
        float availableHeight = Mathf.Max(1f, Mathf.Min(ownerInk.height, frame.height) - HintGapPx - FrameMarginPx);
        // Measure in current scale and compute an absolute authored scale. Never grow beyond the
        // original authored size, and never use the hint to grow the host that determines this fit.
        float ratio = Mathf.Min(availableWidth / hintInk.width, availableHeight / hintInk.height);
        float scale = Mathf.Min(1f, _parked.localScale.x * ratio);
        if (Mathf.Abs(_parked.localScale.x - scale) > 0.0001f)
        {
            _parked.localScale = Vector3.one * scale;
            if (!TryPaintedBounds(_parked, win, panel: null, exclude: null, out hintInk, out hintCount))
                return;
        }

        float wantMinX = ownerInk.center.x - hintInk.width * 0.5f;
        float wantMinY = ownerInk.yMin + HintGapPx;

        // The frame clamp is last and it is never skipped: a uGUI mask on the host culls whatever
        // leaves the frame, and a hint the player cannot see is the failure this whole file exists
        // to remove.
        float maxX = frame.xMax - FrameMarginPx - hintInk.width;
        float minX = frame.xMin + FrameMarginPx;
        wantMinX = maxX >= minX ? Mathf.Clamp(wantMinX, minX, maxX) : frame.center.x - hintInk.width * 0.5f;

        float maxY = frame.yMax - FrameMarginPx - hintInk.height;
        float minY = frame.yMin + FrameMarginPx;
        wantMinY = maxY >= minY ? Mathf.Clamp(wantMinY, minY, maxY) : frame.center.y - hintInk.height * 0.5f;

        Vector2 delta = new(wantMinX - hintInk.xMin, wantMinY - hintInk.yMin);
        if (Mathf.Abs(delta.x) < OffsetEpsilonPx && Mathf.Abs(delta.y) < OffsetEpsilonPx)
            return;
        _parked.anchoredPosition += delta;
    }

    /// <summary>
    /// The painted bounds of a subtree in <paramref name="win"/>'s local (authored uGUI) space, with
    /// an optional subtree EXCLUDED.
    ///
    /// <para>THE VISIBILITY VERDICT IS BORROWED, NOT RE-INVENTED: with a panel in hand this asks
    /// <c>CanvasConversion.CountsAsFitContent</c> — the fit's own test — so the numbers here and the
    /// numbers in the host's fit line cannot disagree. Same choice, same recorded reason, as
    /// <c>StoryComposite.TryPaintedBounds</c> and <c>LoadoutConfirmPark.TryPaintedBounds</c>.</para>
    /// </summary>
    private static bool TryPaintedBounds(RectTransform root, RectTransform win, ConvertedPanel? panel,
                                         RectTransform? exclude, out Rect local, out int counted)
    {
        local = default;
        counted = 0;
        try
        {
            PaintScratch.Clear();
            root.GetComponentsInChildren(includeInactive: false, PaintScratch);
            if (PaintScratch.Count == 0)
                return false;
            if (panel != null)
                CanvasConversion.BeginContentQuery();

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            Vector3[] corners = Corners;
            for (int i = 0; i < PaintScratch.Count; i++)
            {
                Graphic g = PaintScratch[i];
                if (g == null)
                    continue;
                if (ReferenceEquals(root, _parked) && ReferenceEquals(g, _screenDimmer))
                    continue;
                RectTransform rt = g.rectTransform;
                if (rt == null)
                    continue;
                if (exclude != null && (ReferenceEquals(rt, exclude) || rt.IsChildOf(exclude)))
                    continue;
                if (panel != null)
                {
                    if (!CanvasConversion.CountsAsFitContent(panel, g))
                        continue;
                }
                else if (!CountsAsPaintedHere(g))
                {
                    continue;
                }
                Vector2 size = rt.rect.size;
                if (size.x < 1f || size.y < 1f)
                    continue;
                rt.GetWorldCorners(corners);
                for (int c = 0; c < 4; c++)
                {
                    Vector3 p = win.InverseTransformPoint(corners[c]);
                    if (p.x < minX) minX = p.x;
                    if (p.x > maxX) maxX = p.x;
                    if (p.y < minY) minY = p.y;
                    if (p.y > maxY) maxY = p.y;
                }
                counted++;
            }
            PaintScratch.Clear();
            if (counted == 0)
                return false;
            local = Rect.MinMaxRect(minX, minY, maxX, maxY);
            return true;
        }
        catch (System.Exception)
        {
            PaintScratch.Clear();
            counted = 0;
            return false;
        }
    }

    /// <summary>The minimal "is this drawn?" test, used ONLY when there is no converted panel to ask
    /// the fit's own verdict of. Deliberately weaker and deliberately stated as such —
    /// <c>StoryComposite.CountsAsPaintedHere</c>'s twin.</summary>
    private static bool CountsAsPaintedHere(Graphic g) =>
        g.enabled && g.gameObject.activeInHierarchy
        && (g.canvasRenderer == null || !g.canvasRenderer.cull)
        && g.color.a > 0.02f
        && !g.gameObject.name.StartsWith("GloomhavenVR.", System.StringComparison.Ordinal);

    // ---- the hand-back ---------------------------------------------------------------------------

    /// <summary>
    /// Put the hint window back exactly where the game had it. The TRANSFORM is restored only while
    /// the object is still parented under our owner, because re-taking it from wherever the game has
    /// since put it is a write war ([[dont-win-a-write-war]]).
    ///
    /// <para>NOTHING IS WRITTEN TO THE GAME HERE EITHER: no <c>Show</c>, no <c>Hide</c>, no
    /// <c>Escape</c>, no <c>SetActive</c>, no <c>CanvasGroup</c>. The hint's own visibility stays
    /// <c>UIIntroductionManager</c>'s, and on the next tick the ancestor rule stops answering, so
    /// the hint floats on its own again — the ModBuild 380 presentation, which is reachable.</para>
    /// </summary>
    private static void Unpark(string why)
    {
        RectTransform? rect = _parked;
        UIWindow? owner = _ownerWindow;
        _parked = null;
        _ownerWindow = null;
        if (rect == null)
        {
            _home = null;
            return;
        }
        _unparks++;
        try
        {
            // THE LAYER GOES BACK FIRST, while the transforms are still the ones we recorded.
            Layers.Restore();
            if (_addedIgnore != null)
            {
                // A queued message can move to a different owner in this same tick. Deferred
                // Destroy would let Park adopt the doomed component, losing ignoreLayout at the
                // end of the frame. This is exclusively the component this class created.
                Object.DestroyImmediate(_addedIgnore);
                _addedIgnore = null;
            }
            else
            {
                LayoutElement? le = rect.GetComponent<LayoutElement>();
                if (le != null)
                    le.ignoreLayout = _homeIgnoreLayout;
            }
            if (_home == null || owner == null || owner.transform == null)
                return;
            if (rect.parent == null || !rect.parent.IsChildOf(owner.transform))
                return;
            rect.SetParent(_home, worldPositionStays: false);
            rect.SetSiblingIndex(Mathf.Clamp(_homeIndex, 0, Mathf.Max(0, _home.childCount - 1)));
            rect.anchorMin = _homeAnchorMin;
            rect.anchorMax = _homeAnchorMax;
            rect.pivot = _homePivot;
            rect.sizeDelta = _homeSizeDelta;
            rect.anchoredPosition = _homeAnchoredPos;
            rect.localRotation = _homeRotation;
            rect.localScale = _homeScale;
            // THE SCENE COMES BACK WITH THE PARENT, and this is the belt to that brace. SetParent
            // moves a GameObject into its new parent's scene, so landing back under the original
            // parent restores the original scene by construction — unless that parent has itself
            // been moved since. Re-assert it explicitly for the persistent case, where getting it
            // wrong is a session-long deadlock rather than a cosmetic one.
            if (_homeWasPersistent && !CanvasConversion.IsPersistentScene(rect.gameObject.scene))
                VRLog.Alert(Scope, $"HINT ON WINDOW: '{rect.name}' came back to '{_home.name}' but is "
                                   + $"in scene '{rect.gameObject.scene.name}', not the "
                                   + "DontDestroyOnLoad scene it started in — its own home parent has "
                                   + "moved. The next scene load will DELETE it while "
                                   + "UIIntroductionManager still points at it. This is the "
                                   + "2026-09-03 deadlock class and RefusesForScene should have "
                                   + "prevented the park; that it did not is the thing to fix.");
            VRLog.Info(Scope, $"HINT ON WINDOW UNPARKED — {why}. The introduction window "
                              + $"'{rect.name}' is back under '{_home.name}' at sibling {_homeIndex} "
                              + "with its parent, anchors, pivot, sizeDelta, anchoredPosition, local "
                              + "rotation and local scale restored verbatim from the record taken "
                              + "before the first move, and the layout opt-out released or destroyed. "
                              + "It floats on its own again from the next tick.");
        }
        catch (System.Exception e)
        {
            VRLog.Alert(Scope, $"HINT ON WINDOW: handing the introduction window back threw "
                               + $"({e.GetType().Name}) — it may be left under the owner window's "
                               + "root. It is the GAME's own object and UIIntroductionManager "
                               + "re-shows it through its own layout group on the next hint.");
        }
        finally
        {
            _home = null;
            _homeScene = default;
            _homeWasPersistent = false;
        }
    }

    // ---- the falsifier ----------------------------------------------------------------------------

    /// <summary>
    /// THE ONE LINE A HARDWARE ROUND READS. Change-gated on its own verdict signature, so a settled
    /// hint prints once and a hint that moves between owners prints once per move — never per frame,
    /// and never silent while something is happening ([[a-held-instrument-reads-as-dead]]: the
    /// running counts are IN the line, so a repeat that looks identical is impossible unless nothing
    /// has changed).
    ///
    /// <para>It states four things and asserts nothing it did not measure this tick: WHICH hint,
    /// WHICH owner it was anchored to, HOW that owner was resolved, and — when there is none — the
    /// fallback and what the player therefore sees.</para>
    /// </summary>
    private static void Report(UIWindow hint, UIWindow? owner, ConvertedPanel? panel)
    {
        string verdict = owner != null
            ? $"ON '{owner.name}' (ID {owner.ID}) via {_how}"
            : $"NOT ANCHORED — {_how}";
        string signature = $"{verdict}|{_hintsSeen}|{_parks}|{_unparks}|{_noOwner}|{_refused}";
        if (signature == _verdict)
            return;
        _verdict = signature;
        _reports++;

        string body = owner != null
            ? $"the introduction hint '{hint.name}' (ID {hint.ID}) is drawn INSIDE the floated "
              + $"window '{owner.name}' (ID {owner.ID}). OWNER RESOLVED BY: {_how}. "
              + $"Host layer written over {Layers.Count} transform(s), {Layers.Skipped} foreign "
              + $"render subtree(s) left alone; host rect {panel?.HostRect?.name ?? "<none>"}. "
              + $"Message scale {(_parked != null ? _parked.localScale.x : 1f):0.###}; "
              + $"screen dimmer excluded from measurement={_screenDimmer != null}. "
              + $"ITS HOME: native parent '{_home?.name ?? "<none>"}', sibling {_homeIndex}, "
              + $"scene '{_homeScene.name}' (persistent={_homeWasPersistent})."
            : $"the introduction hint '{hint.name}' (ID {hint.ID}) is NOT anchored to a window "
              + $"this tick. WHY: {_how}. FALLBACK, AND WHAT IT GETS WRONG: the hint cannot yet "
              + "annotate its actual owner; its native message remains in a standalone presentation "
              + "until its actual owner has a live VR host. Only the native dismiss/action advances "
              + "the introduction; no focused or topmost owner is guessed.";

        // HW-VERIFY: this family printed NOTHING at a printed tier before ModBuild 381 — "TUTORIAL"
        // and "Tutorial hint" both return zero hits in the ModBuild 380 log — which is why the
        // defect survived two hardware rounds. The next round's answer is read off this line.
        VRLog.Note(Scope, $"HINT ON WINDOW: {body} COUNTS: hints seen {_hintsSeen}, parked {_parks}, "
                          + $"handed back {_unparks}, no owner {_noOwner}, park refused {_refused}, "
                          + $"throws {_throws}, reports {_reports}; current message owner: {_origin?.Description ?? "<none>"}.");
    }

    /// <summary>Module teardown — hand the hint back before anything else is torn down.</summary>
    internal static void Reset()
    {
        Unpark("module teardown");
        _hint = null;
        _message = null;
        _origin = null;
        _screenDimmer = null;
        HintMessageOrigins.Reset();
        _verdict = string.Empty;
        _disabledByError = false;
    }
}
