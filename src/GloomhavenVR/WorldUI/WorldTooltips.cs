using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// World-space tooltips on converted surfaces (ROADMAP P3c #8, config-gated).
///
/// SCENARIO-ONLY (I2, hardware test #5): in <see cref="VRMode.Menu2D"/> the tooltip
/// canvas must stay untouched — it is Screen-Space-Camera on the UICamera, which the
/// FlatScreen redirects into its RenderTexture, so menu tooltips already appear on
/// the quad at native size. Test #5 flipped it to world space in the menu anyway:
/// with no poke anchor the flip never applied a scale, leaving the canvas rect at
/// its pixel dimensions in WORLD METERS (a ~2560 px canvas ≈ 2.5 km), rendered by
/// the head camera — the "giant mouseover" report. The conversion now gates on the
/// VR mode state machine (flips in scenario modes, restores on entering Menu2D) and
/// applies its world scale AT FLIP TIME plus every frame, never only on anchor.
///
/// The trigger side is free: fingertip pokes on converted canvases synthesize REAL
/// <c>pointerEnter/Exit</c> events (Phase-2 UguiPointer), which drive the game's own
/// <c>UITooltipTarget.OnPointerEnter/Exit</c> → the static <c>UITooltip</c> API —
/// no patches needed. What this class owns is PRESENTATION: the tooltip lives on the
/// dedicated persistent <c>tooltipCanvas</c> (Screen-Space-Camera), which is useless
/// in the HMD; while active the canvas is flipped to WorldSpace and parked at a FIXED
/// spot — the CONTROL BOARD's (PlayTray) TOP-LEFT corner, offset outward so it clears the
/// cards, facing the player and scaling with the board (user #7a; the pre-existing curved-
/// table <see cref="PanelSlot.Tooltip"/> slot is the menu / no-tray fallback). It is NOT
/// anchored to the fingertip (the hover can come from the laser too, and the user wants a
/// stable reading spot, not a spot that jumps around). A short hover grace (user #7b) keeps
/// it from flickering away on micro-jitter off a tiny target: the game's own show/hide fade
/// is widened (<see cref="FadeGraceSeconds"/>) and the parked position is latched for
/// <see cref="HoverGraceSeconds"/> after the content stops showing. The whole presentation
/// is gated on <see cref="WorldUIConfig.ActionElementHints"/> (user #7c).
///
/// FLAT 2D (part A): the game tooltip's content carries baked local-z / local rotation
/// (subtle styling under the perspective UI camera) that becomes literal geometry on a
/// world-space host — the text protruded in 3D past the panel. Routing this SHARED,
/// game-repositioned canvas through <see cref="CanvasConversion.Convert"/> (reparent +
/// content-fit, the DamageTooltip path) would be too invasive for a persistent canvas
/// the game lays out internally, so we apply the same <see cref="CanvasConversion.FlattenSubtree"/>
/// idea in place — zero the baked local-z / rotation on every descendant every frame
/// (tooltip lines are pooled/rebuilt per hover, and the game rewrites them) — and add a
/// <see cref="RectMask2D"/> on the tooltip frame so any 2D overflow is clipped inside the
/// panel. Both are fully reversed on <see cref="Restore"/> (originals restored; a mask we
/// added is destroyed) so the vanilla 2D menu tooltip keeps its styling.
///
/// Scale sanity (I2): world scale = <see cref="WorldUIConfig.CanvasScaleMm"/> (mm per
/// uGUI pixel, default 1) × 0.001 × diorama scale × 0.5 — half the panel framework's
/// meters-per-pixel (CanvasConversion.cs: <c>metersPerPixel = CanvasScaleMm * 0.001f</c>)
/// so a ~400 px tooltip reads ~20 cm at arm's length instead of 40.
///
/// Verified via ilspycmd (GH.Runtime.dll): <c>CanvasManager</c> holds
/// <c>[SerializeField] private Canvas tooltipCanvas;</c> (publicized) and only
/// re-binds <c>worldCamera</c> on scene load — harmless in world mode, we rebind per
/// frame. <c>UITooltip.uiCamera</c> resolves per render mode incl. a world-camera
/// branch (UI-ARCH §6), so its internal placement math keeps working.
///
/// REVERSIBLE: render mode, worldCamera, plane distance, scale and sorting are
/// restored on disable/shutdown AND on every return to Menu2D; a Screen-Space canvas
/// re-drives its own rect once the mode is set back.
/// </summary>
internal sealed class WorldTooltips
{
    /// <summary>Unanchored world-space parking spot (out of every camera's view).</summary>
    private static readonly Vector3 ParkPosition = new(0f, -1000f, 0f);

    // ---- board top-left anchor (user #7a) ---------------------------------------------
    // The tooltip is pinned to the CONTROL BOARD's (PlayTray) top-left corner and offset
    // OUTWARD (further left + up, proud toward the viewer) so it never overlaps the cards.
    // All in board-LOCAL metres at board scale 1; PlayTray.Root.TransformPoint carries the
    // live board pose + scale, so the hint moves/tilts/scales with the board for free.

    /// <summary>Metres beyond the board's LEFT edge (board-local, at scale 1).</summary>
    private const float BoardAnchorMarginX = 0.03f;

    /// <summary>Metres beyond the board's TOP (far) edge (board-local, at scale 1).</summary>
    private const float BoardAnchorMarginY = 0.03f;

    /// <summary>Metres proud toward the viewer (board-local -Z is the viewer side).</summary>
    private const float BoardAnchorProudZ = 0.02f;

    // ---- hover grace / stickiness (user #7b) ------------------------------------------
    /// <summary>
    /// Placement latch window: after the game's tooltip content stops showing, keep the
    /// canvas parked at the anchor this long so a micro-jitter off a tiny UITooltipTarget
    /// does not teleport the canvas offscreen-and-back (a jarring flicker of its own) and a
    /// re-hover within the window finds it already anchored.
    /// </summary>
    private const float HoverGraceSeconds = 0.5f;

    /// <summary>
    /// Native fade grace: while converted we widen the game's OWN show/hide fade (its
    /// <c>UITooltip.transition = Fade</c>, <c>transitionDuration</c>) to this so a jitter
    /// exit→enter inside the window is bridged by the game's own tween interruption (the
    /// fade-out is re-tweened back to full) — the tooltip never fully vanishes on tiny
    /// movements. Uses the game's self-cleaning mechanism only — we never fight its alpha /
    /// visual-state machine (it reads its own CanvasGroup alpha back, so forcing it would
    /// strand the shared singleton). Fully restored on <see cref="Restore"/>.
    /// </summary>
    private const float FadeGraceSeconds = 0.4f;

    /// <summary>Local rotation counts as 3D beyond this angle (degrees) off identity.</summary>
    private const float FlattenAngleEpsilon = 0.05f;

    /// <summary>Local z counts as 3D beyond this many uGUI pixels.</summary>
    private const float FlattenZEpsilon = 0.01f;

    /// <summary>Tooltip content is only "shown" (worth placing) above this CanvasGroup alpha.</summary>
    private const float ShownAlphaEpsilon = 0.05f;

    private Canvas? _canvas;
    private bool _converted;

    // Restore data.
    private RenderMode _originalMode;
    private Camera? _originalCamera;
    private float _originalPlaneDistance;
    private Vector3 _originalScale;

    // ---- 2D flatten + frame clip (part A) ---------------------------------------------
    /// <summary>The persistent tooltip content singleton under the canvas (frame + text lines).</summary>
    private UITooltip? _tooltip;

    /// <summary>A RectMask2D WE added to the tooltip frame (null when none / the frame already had one).</summary>
    private RectMask2D? _addedMask;

    // ---- grace + native-fade override state (user #7b) --------------------------------
    /// <summary>Last time the game's tooltip content was genuinely shown (placement latch).</summary>
    private float _lastShownTime = float.NegativeInfinity;

    /// <summary>We widened the game's fade transition; originals for <see cref="Restore"/>.</summary>
    private bool _transitionOverridden;
    private UITooltip.Transition _originalTransition;
    private float _originalTransitionDuration;

    // ---- diagnostics (dedupe so a per-frame path logs once) ---------------------------
    private bool _parkedLogged;
    private Vector3 _parkedLogPos;
    private bool _gateOffLogged;

    /// <summary>Descendant transforms flattened this session (original local z + rotation for Restore).</summary>
    private readonly List<FlattenEntry> _flattened = new(32);

    /// <summary>Reused per-frame scan buffer (no steady-state allocation).</summary>
    private static readonly List<RectTransform> RectScratch = new(64);

    private struct FlattenEntry
    {
        public Transform Transform;
        public float OriginalLocalZ;
        public Quaternion OriginalLocalRotation;
    }

    public void LateTick()
    {
        // Menu2D keeps the vanilla 2D tooltip path (UICamera → FlatScreen RT); every
        // scenario mode (incl. ModalUI/BoardTargeting — Recompute() only leaves
        // Menu2D while a scenario runs) gets the world-space presentation.
        bool modeWantsTooltip = WorldUIConfig.Tooltips.Value && WorldUIConfig.ConversionActive
                                && VRModeStateMachine.CurrentMode != VRMode.Menu2D;
        // User #7c: the in-VR settings toggle gates the whole world-space presentation.
        // Read live so a flip takes effect without a restart; the 2D menu tooltip (Menu2D
        // path above) is never touched by this gate.
        bool hintsEnabled = WorldUIConfig.ActionElementHints.Value;
        bool want = modeWantsTooltip && hintsEnabled;

        if (!want)
        {
            // Diagnostic (user #7c): note the one case where the USER'S hints toggle is
            // what holds the presentation off (would otherwise be showing), once per flip.
            if (modeWantsTooltip && !hintsEnabled && !_gateOffLogged)
            {
                _gateOffLogged = true;
                VRLog.Info("WorldUI",
                    "Action element hints disabled ([WorldUI] ActionElementHints=false) — " +
                    "tooltip never flipped to world space / never shown.");
            }
            Restore();
            return;
        }
        _gateOffLogged = false;

        if (_canvas == null)
        {
            var manager = Object.FindObjectOfType<CanvasManager>();
            _canvas = manager != null ? manager.tooltipCanvas : null;
            if (_canvas == null)
                return;
        }

        // Scale WITH THE BOARD (user #7a): the control board's live lossy scale (diorama ×
        // tray-grab resize) when it exists, else the diorama scale (menu / no-tray fallback).
        float scale = ResolveWorldScale();
        Vector3 worldScale = Vector3.one * (WorldUIConfig.CanvasScaleMm.Value * 0.001f * scale * 0.5f);

        if (!_converted)
        {
            _originalMode = _canvas.renderMode;
            _originalCamera = _canvas.worldCamera;
            _originalPlaneDistance = _canvas.planeDistance;
            _originalScale = _canvas.transform.localScale;
            _canvas.renderMode = RenderMode.WorldSpace;
            // Scale + park IMMEDIATELY: an unanchored flip must never leave the
            // canvas rect at pixel size in world meters (test-#5 giant tooltip).
            _canvas.transform.localScale = worldScale;
            _canvas.transform.position = ParkPosition;
            _converted = true;
            CanvasConversion.AddMaskRequest();
            VRLog.Info("WorldUI", "Tooltip canvas flipped to world space (scenario mode).");
        }

        Camera? head = CanvasConversion.WorldCamera;
        if (head == null)
            return;
        if (_canvas.worldCamera != head)
            _canvas.worldCamera = head;

        // Re-assert the scale every frame (config/diorama scale are live; the game
        // may rewrite the transform) — independent of placement.
        if (_canvas.transform.localScale != worldScale)
            _canvas.transform.localScale = worldScale;

        // Resolve the persistent tooltip content (singleton under the canvas) for the
        // flatten pass, frame clip and visibility gate.
        if (_tooltip == null)
            _tooltip = _canvas.GetComponentInChildren<UITooltip>(includeInactive: true);

        // FLATTEN + CLIP (part A): kill the baked local-z / rotation that renders as 3D
        // depth on a world-space host, and clip 2D overflow inside the frame. Both are
        // undone on Restore().
        FlattenSubtree();
        EnsureFrameClip();
        // HOVER GRACE (user #7b): widen the game's own show/hide fade so a jitter off a
        // tiny target is bridged by its native tween. Undone on Restore().
        EnsureFadeGrace();

        // FIXED PLACEMENT (user #7a): while a tooltip is shown — OR within the placement
        // grace window just after it stopped (user #7b) — park the canvas at the control
        // board's top-left corner, facing the player. Otherwise leave it out of view —
        // never at the fingertip.
        bool contentShown = _tooltip != null && _tooltip.IsActive() && _tooltip.alpha > ShownAlphaEpsilon;
        if (contentShown)
            _lastShownTime = Time.unscaledTime;
        bool withinGrace = _tooltip != null && Time.unscaledTime - _lastShownTime <= HoverGraceSeconds;

        if (!(contentShown || withinGrace) || !TryResolveTooltipPose(out Vector3 pos, out Quaternion rot))
        {
            if (_canvas.transform.position != ParkPosition)
            {
                _canvas.transform.position = ParkPosition;
                _parkedLogged = false; // re-arm the park diagnostic for the next appearance
            }
            return;
        }

        _canvas.transform.SetPositionAndRotation(pos, rot);

        // Diagnostic (user #7a): one line when the hint parks at the resolved board anchor
        // (deduped; re-logs if the anchor drifts > ~2 cm, e.g. the board was grabbed/moved).
        if (!_parkedLogged || (pos - _parkedLogPos).sqrMagnitude > 0.0004f)
        {
            _parkedLogged = true;
            _parkedLogPos = pos;
            bool onBoard = PlayTray.Current != null && PlayTray.Current.IsVisible;
            VRLog.Info("WorldUI",
                $"Tooltip parked at {(onBoard ? "control-board top-left" : "fallback slot")} " +
                $"anchor {pos:F3} (scale {scale:F3}).");
        }
    }

    /// <summary>
    /// Resolve the world pose the tooltip parks at (user #7a): the CONTROL BOARD's
    /// (PlayTray) top-left corner, offset OUTWARD (beyond the left + top edges, proud
    /// toward the viewer) so it never overlaps the cards, facing the player exactly like
    /// the board's docked panels (their <c>mount.rotation == Root.rotation</c> faces the
    /// player). Falls back to the pre-existing <see cref="PanelSlot.Tooltip"/> table slot
    /// when no board is present (menu / Cards module off).
    /// </summary>
    private static bool TryResolveTooltipPose(out Vector3 position, out Quaternion rotation)
    {
        if (TryGetBoardRoot(out Transform root))
        {
            // Board-local top-left corner (mounts are placed in this same _root-local frame,
            // using BoardW/BoardH): -X = left, +Y = top/far edge, -Z = viewer side.
            float halfWidth = PlayTray.InitiativeMountWidth * 0.5f;
            float topY = PlayTray.BoardTopLocalY;
            Vector3 localCorner = new(
                -halfWidth - BoardAnchorMarginX,
                topY + BoardAnchorMarginY,
                -BoardAnchorProudZ);
            position = root.TransformPoint(localCorner); // carries live board pose + scale
            rotation = root.rotation;                    // docked-panel facing (faces the player)
            return true;
        }

        // Safe fallback: the original curved-table slot (top-right, above initiative).
        return PanelLayout.TryGetPose(PanelSlot.Tooltip, out position, out rotation);
    }

    /// <summary>The live, visible control board root, or false (menu / no tray).</summary>
    private static bool TryGetBoardRoot(out Transform root)
    {
        root = null!;
        PlayTray? tray = PlayTray.Current;
        if (tray == null || !tray.IsVisible)
            return false;
        Transform? r = tray.Root;
        if (r == null)
            return false;
        root = r;
        return true;
    }

    /// <summary>World scale for the tooltip canvas: the board's live lossy scale when the
    /// board exists (so the hint scales with it — user #7a), else the diorama scale.</summary>
    private static float ResolveWorldScale() =>
        TryGetBoardRoot(out Transform root) ? Mathf.Max(root.lossyScale.x, 0.01f) : PanelLayout.WorldScale;

    /// <summary>
    /// Widen the game's OWN tooltip fade so brief hover jitter is bridged (user #7b). We
    /// switch it to <see cref="UITooltip.Transition.Fade"/> at <see cref="FadeGraceSeconds"/>
    /// and store the originals — the game only ever sets these from the prefab at load, so a
    /// one-shot override holds. Fully reverted in <see cref="Restore"/>. We deliberately do
    /// NOT touch the tooltip's alpha / CanvasGroup: the game reads its own alpha back to
    /// drive its visual-state machine and clean up its content, so forcing it would strand
    /// the shared singleton — letting its native fade run long is safe and self-cleaning.
    /// </summary>
    private void EnsureFadeGrace()
    {
        if (_tooltip == null || _transitionOverridden)
            return;
        _originalTransition = _tooltip.transition;
        _originalTransitionDuration = _tooltip.transitionDuration;
        _tooltip.transition = UITooltip.Transition.Fade;
        _tooltip.transitionDuration = FadeGraceSeconds;
        _transitionOverridden = true;
    }

    /// <summary>
    /// Neutralize REAL 3D inside the tooltip subtree (part A): every descendant carrying a
    /// non-identity local rotation or non-zero local z is recorded once and clamped
    /// (rotation → identity, z → 0). X/Y are never touched (the game's fade/slide
    /// animations keep playing flat). Re-run every frame — tooltip lines are pooled and
    /// rebuilt per hover and the game rewrites them — with destroyed entries pruned so the
    /// record list stays bounded to the live subtree. Mirrors
    /// <see cref="CanvasConversion.FlattenSubtree"/> for this shared, un-converted canvas.
    /// </summary>
    private void FlattenSubtree()
    {
        if (_canvas == null)
            return;

        // Prune destroyed entries (pooled tooltip lines come and go per hover).
        for (int i = _flattened.Count - 1; i >= 0; i--)
        {
            if (_flattened[i].Transform == null)
                _flattened.RemoveAt(i);
        }

        RectScratch.Clear();
        _canvas.GetComponentsInChildren(includeInactive: true, RectScratch);
        Transform canvasTf = _canvas.transform;
        for (int i = 0; i < RectScratch.Count; i++)
        {
            RectTransform rect = RectScratch[i];
            // The canvas root's own pose is ours (scale + placement above) — flatten only
            // the content below it.
            if (rect == null || ReferenceEquals(rect, canvasTf))
                continue;

            Vector3 lp = rect.localPosition;
            Quaternion lr = rect.localRotation;
            bool tiltedRot = Quaternion.Angle(lr, Quaternion.identity) > FlattenAngleEpsilon;
            bool tiltedZ = Mathf.Abs(lp.z) > FlattenZEpsilon;
            if (!tiltedRot && !tiltedZ)
                continue;

            if (!IsFlattenRecorded(rect))
            {
                _flattened.Add(new FlattenEntry
                {
                    Transform = rect,
                    OriginalLocalZ = lp.z,
                    OriginalLocalRotation = lr,
                });
            }
            if (tiltedRot)
                rect.localRotation = Quaternion.identity;
            if (tiltedZ)
                rect.localPosition = new Vector3(lp.x, lp.y, 0f);
        }
        RectScratch.Clear();
    }

    private bool IsFlattenRecorded(Transform rect)
    {
        for (int i = 0; i < _flattened.Count; i++)
        {
            if (ReferenceEquals(_flattened[i].Transform, rect))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Add a <see cref="RectMask2D"/> on the tooltip frame so text is clipped inside the
    /// panel (part A). Idempotent; if the frame already carries one we leave it alone and
    /// never destroy it on Restore.
    /// </summary>
    private void EnsureFrameClip()
    {
        if (_tooltip == null || _addedMask != null)
            return;
        var frame = _tooltip.transform as RectTransform;
        if (frame == null)
            return;
        RectMask2D existing = frame.GetComponent<RectMask2D>();
        if (existing != null)
            return; // game already clips this frame — don't touch/destroy it
        _addedMask = frame.gameObject.AddComponent<RectMask2D>();
    }

    private void Restore()
    {
        if (!_converted)
            return;
        _converted = false;
        CanvasConversion.RemoveMaskRequest();

        // Un-flatten: original local z + rotation back per live recorded transform.
        for (int i = 0; i < _flattened.Count; i++)
        {
            Transform tf = _flattened[i].Transform;
            if (tf == null)
                continue;
            Vector3 lp = tf.localPosition;
            tf.localPosition = new Vector3(lp.x, lp.y, _flattened[i].OriginalLocalZ);
            tf.localRotation = _flattened[i].OriginalLocalRotation;
        }
        _flattened.Clear();

        // Remove only a mask we added.
        if (_addedMask != null)
        {
            Object.Destroy(_addedMask);
            _addedMask = null;
        }

        // Revert the widened fade (user #7b) so the vanilla 2D menu tooltip keeps its
        // authored transition — before we drop the reference.
        if (_transitionOverridden && _tooltip != null)
        {
            _tooltip.transition = _originalTransition;
            _tooltip.transitionDuration = _originalTransitionDuration;
        }
        _transitionOverridden = false;

        _tooltip = null;
        _lastShownTime = float.NegativeInfinity;
        _parkedLogged = false;

        if (_canvas != null)
        {
            _canvas.renderMode = _originalMode;
            _canvas.worldCamera = _originalCamera;
            _canvas.planeDistance = _originalPlaneDistance;
            _canvas.transform.localScale = _originalScale;
            VRLog.Info("WorldUI", "Tooltip canvas restored to screen space (flatten + frame clip reverted).");
        }
    }

    public void Shutdown() => Restore();
}
