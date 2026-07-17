using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.Surfaces;

/// <summary>
/// Take-damage choice ON the control board (test #21). When lethal damage forces a
/// choice, the game opens the 'Take Damage Panel' (UIWindowID TakeDamagePanel,
/// decompiled GH.Runtime/TakeDamagePanel.cs) — pre-#21 the generic
/// <see cref="ModalFallback"/> floated the WHOLE 1920x1080 window (blood-red
/// 'DamageBackground' vignette included) in front of the HMD and asserted ModalUI,
/// which also disabled the card fan's palm gate — even though the burn follow-up
/// (LoseCard flow: pick 1 available / 2 discarded cards) NEEDS the fan (hardware
/// test #21 log: <c>fan state: mode=LoseCard … gateEnabled=False (vrMode=ModalUI)</c>).
///
/// This surface instead docks ONLY the choice row — the deepest common ancestor of
/// the panel's three serialized choice widgets (<c>burnAvailableCardsToggle</c>,
/// <c>burnDiscardedCardsToggle</c>, <c>takeDamageButton</c>, all publicized
/// serialized fields; the game's REAL widgets, so localization and the native
/// interactable/dim states ride along) — onto <see cref="PlayTray.DamageMount"/>,
/// centered over the slot zone. Poke AND laser click it through the standard
/// conversion registration (Convert's pokeable default → UguiPokeSurfaces +
/// RayUguiDriver), exactly like the floated modal the clicks already worked on.
///
/// The REST of the panel — the vignette and the flat window — is VISUALLY
/// suppressed the way the 2D hand is (<see cref="Cards.Patches.HandSuppression"/>):
/// the window root's required CanvasGroup is forced to alpha 0 / blocksRaycasts
/// false every tick (the game rewrites alpha on Show/ToggleVisibility), and nested
/// <see cref="Canvas"/> components under the root (the 'DamageBackground' vignette
/// — its own canvas renders INDEPENDENTLY of the root CanvasGroup alpha) are
/// component-disabled, which stops their whole subtree rendering (the
/// CanvasConversion nested-canvas lesson, tests #19/#20). Nothing is destroyed;
/// everything is restored on undock.
///
/// MODE MACHINE (task B): no ModalUI is asserted for this window — ModalFallback
/// skips windows claimed via its tray-dock claim map (<see cref="ClaimsWindow"/>),
/// so the flow mode (HalfSelection/LoseCard) and the fan's palm gate stay live
/// through the whole prompt, and the game itself never locks the UI here (test #21
/// log: mode returned straight to HalfSelection on close).
///
/// LIFECYCLE (task C): level-triggered on the window's open state like
/// ModalFallback — any close path (any of the three choices, game-side close,
/// scene death) releases the conversion, which restores the row to its exact 2D
/// home (CanvasConversion restore records) and lifts the suppression. Dock and
/// undock are logged once per flip.
///
/// SAFETY NET: while the claim holds, the generic fallback stands down — but if
/// the row cannot be converted (widgets missing, common ancestor degenerates to
/// the window root, Convert failure) the claim is RELEASED after a short grace and
/// ModalFallback handles the window generically next tick (floating window +
/// ModalUI): a worse experience, never a deadlock. The manual A/X screen chord
/// stays the universal rescue regardless (it releases the conversion so the full
/// panel shows on the 2D composite).
/// </summary>
internal sealed class TakeDamageSurface : WorldSurface
{
    /// <summary>Claim grace: how long the row may fail to convert before the generic fallback takes over.</summary>
    private const float ClaimGraceSeconds = 1.5f;

    /// <summary>
    /// Density-scale guards, mirroring <see cref="TrayMountedPanelSurface"/>
    /// (test #16): max 1 keeps small content content-true, min 0.5 keeps a
    /// misfired-huge measurement readable (slight dock overflow over shrinking
    /// below readability).
    /// </summary>
    private const float MaxDensityScale = 1f;
    private const float MinDensityScale = 0.5f;

    /// <summary>
    /// Choice buttons are the one thing the player MUST read and hit under
    /// pressure — 0.8x the shared tray density renders them 1.25x bigger (the
    /// element-board rationale); the dock fit clamp still bounds the row to its
    /// mount budget.
    /// </summary>
    private const float DensityScale = 0.8f;

    /// <summary>No-tray fallback float distance (HMD-anchored, the ModalFallback pattern).</summary>
    private const float FloatDistanceMeters = 1.1f;

    /// <summary>Extra shrink on the no-tray float (the ModalFallback window factor).</summary>
    private const float FloatScaleFactor = 0.7f;

    /// <summary>Live instance for the static claim query (single instance per driver).</summary>
    private static TakeDamageSurface? _instance;

    private bool _gaveUp;
    private float _wantSince;
    private bool _targetWarned;
    private bool _hmdFloatPlaced;

    // Suppression records (restored on undock; nothing destroyed).
    private CanvasGroup? _suppressedGroup;
    private readonly List<Canvas> _disabledCanvases = new(2);
    private static readonly List<Canvas> CanvasScratch = new(8);

    public TakeDamageSurface() => _instance = this;

    public override string Name => "TakeDamageBoard";
    protected override bool ConfigEnabled => WorldUIConfig.TakeDamageBoard.Value;

    /// <summary>
    /// Live claim for <see cref="ModalFallback"/>'s tray-dock claim map: while true,
    /// the generic modal path (floating window + ModalUI) stands down for the
    /// TakeDamagePanel window. Deliberately NOT "already converted" — the claim
    /// must hold from the window's Show event (which can precede this surface's
    /// first Tick in the frame) or ModalFallback would float the whole window for
    /// one frame and both would fight over the same subtree.
    /// </summary>
    internal static bool ClaimsWindow
    {
        get
        {
            TakeDamageSurface? s = _instance;
            return s != null && s.ConfigEnabled && WorldUIConfig.ConversionActive && !s._gaveUp;
        }
    }

    private static TakeDamagePanel? PanelInstance =>
        Singleton<TakeDamagePanel>.IsInitialized ? Singleton<TakeDamagePanel>.Instance : null;

    /// <summary>The panel's UIWindow (publicized <c>myWindow</c>; null before its Awake).</summary>
    private static UIWindow? PanelWindow(TakeDamagePanel? panel) =>
        panel != null ? panel.myWindow : null;

    /// <summary>
    /// Level-triggered on the game's own window state (the ModalFallback contract):
    /// converted while the prompt is open, released the moment it closes. The
    /// manual screen chord releases too — the full panel must be in the 2D
    /// composite the screen mirrors (the ModalFallback release rule).
    /// </summary>
    protected override bool WantConverted
    {
        get
        {
            if (!base.WantConverted || FlatScreen.ManualScreenActive || _gaveUp)
                return false;
            UIWindow? window = PanelWindow(PanelInstance);
            return window != null && window.IsOpen;
        }
    }

    /// <summary>
    /// The choice ROW: deepest common ancestor of the three serialized choice
    /// widgets — found structurally (never by name), so it survives hierarchy
    /// renames and carries exactly the widgets plus whatever the row container
    /// itself holds. Null (→ retry, claim grace running) when the widgets are
    /// missing or the ancestor degenerates to the window root — converting the
    /// root would drag the vignette along, which is the pre-#21 behavior the
    /// generic fallback already provides.
    /// </summary>
    protected override RectTransform? FindTarget()
    {
        TakeDamagePanel? panel = PanelInstance;
        UIWindow? window = PanelWindow(panel);
        if (panel == null || window == null)
            return null;

        Transform? a = panel.burnAvailableCardsToggle != null ? panel.burnAvailableCardsToggle.transform : null;
        Transform? b = panel.burnDiscardedCardsToggle != null ? panel.burnDiscardedCardsToggle.transform : null;
        Transform? c = panel.takeDamageButton != null ? panel.takeDamageButton.transform : null;
        Transform? row = CommonAncestor(CommonAncestor(a, b), c);
        if (row == null)
        {
            WarnTargetOnce("one of the three choice widgets is missing");
            return null;
        }
        if (ReferenceEquals(row, window.transform) || !row.IsChildOf(window.transform))
        {
            WarnTargetOnce($"the widgets' common ancestor is '{row.name}' " +
                           "(the window root or outside it) — docking it would drag the vignette along");
            return null;
        }
        return row as RectTransform;
    }

    private void WarnTargetOnce(string reason)
    {
        if (_targetWarned)
            return;
        _targetWarned = true;
        VRLog.Warn("WorldUI", $"TAKE DAMAGE BOARD: cannot isolate the choice row — {reason}. " +
                              $"Claim releases after {ClaimGraceSeconds:F1}s; the generic modal " +
                              "fallback (floating window + ModalUI) takes over.");
    }

    public override void Tick()
    {
        bool hadPanel = Panel != null;
        UIWindow? window = PanelWindow(PanelInstance);
        bool open = window != null && window.IsOpen;
        if (!open)
        {
            // Window closed/gone: a broken claim re-arms for the next prompt.
            _gaveUp = false;
            _wantSince = 0f;
            _targetWarned = false;
        }

        base.Tick(); // convert / release / Place (level-triggered on WantConverted)

        if (Panel != null)
        {
            _wantSince = 0f;
            if (!hadPanel)
                VRLog.Info("WorldUI", "TAKE DAMAGE BOARD: choice row docked on the control board " +
                                      "(vignette + window suppressed, fan gate stays live, no ModalUI) — " +
                                      "restored to 2D when the prompt closes.");
            ApplySuppression(window!); // window non-null: WantConverted required IsOpen
            // The one surface that must accept input even under the game's UI-lock
            // raycaster mirror — the exact ModalFallback floating-modal exemption.
            if (Panel.HostRaycaster != null && !Panel.HostRaycaster.enabled)
                Panel.HostRaycaster.enabled = true;
        }
        else
        {
            _hmdFloatPlaced = false;
            if (hadPanel)
            {
                RestoreSuppression(window);
                VRLog.Info("WorldUI", "TAKE DAMAGE BOARD: choice row released — restored to its 2D home " +
                                      $"(open={open}), suppression lifted.");
            }
            // Claim grace: wanted but unconverted (target not isolatable / Convert
            // failed) → after the grace, hand the window to the generic fallback.
            if (open && !_gaveUp && WantConverted)
            {
                if (_wantSince <= 0f)
                {
                    _wantSince = Time.unscaledTime;
                }
                else if (Time.unscaledTime - _wantSince > ClaimGraceSeconds)
                {
                    _gaveUp = true;
                    VRLog.Warn("WorldUI", "TAKE DAMAGE BOARD: choice row not converted within " +
                                          $"{ClaimGraceSeconds:F1}s — claim released, the generic modal " +
                                          "fallback floats the whole panel for this prompt.");
                }
            }
        }
    }

    /// <summary>
    /// Dock on the tray's <see cref="PlayTray.DamageMount"/> (pose-follow, shared
    /// tray density — the <see cref="TrayMountedPanelSurface"/> math with a
    /// CENTERED origin: the prompt sits symmetrically over the slot zone). While
    /// no usable mount exists (Cards module off, tray hidden/destroyed) the row
    /// floats HMD-anchored at reading distance instead (placed once, ModalFallback
    /// pattern) — the prompt must never be invisible.
    /// </summary>
    protected override void Place()
    {
        if (Panel == null)
            return;

        Transform? mount = PlayTray.Current?.DamageMount; // Unity-null aware
        if (mount == null || !mount.gameObject.activeInHierarchy)
        {
            if (!Panel.HostGo.activeSelf)
                Panel.HostGo.SetActive(true);
            if (!_hmdFloatPlaced)
                _hmdFloatPlaced = TryPlaceAtHmd();
            return;
        }
        _hmdFloatPlaced = false;

        if (!Panel.HostGo.activeSelf)
            Panel.HostGo.SetActive(true);

        Rect rect = Panel.HostRect.rect; // content-fitted by CanvasConversion.TickFit
        if (rect.width < 1f || rect.height < 1f)
            return;

        float trayScale = mount.lossyScale.x;
        float density = PlayTray.TrayPixelsPerMeter * DensityScale;
        float fitScale = Mathf.Min(
            PlayTray.DamageMountWidth * density / rect.width,
            PlayTray.DamageMountMaxHeight * density / rect.height);
        float metersPerPx = Mathf.Clamp(fitScale, MinDensityScale, MaxDensityScale) / density;

        Transform host = Panel.HostTransform;
        host.SetPositionAndRotation(mount.position, mount.rotation); // centered origin
        host.localScale = Vector3.one * (metersPerPx * trayScale);
    }

    /// <summary>HMD-anchored fallback float (the ModalFallback.PlaceAtHmd pattern). True when placed.</summary>
    private bool TryPlaceAtHmd()
    {
        if (Panel == null)
            return false;
        Camera? head = CanvasConversion.WorldCamera;
        if (head == null)
            return false;
        float scale = PanelLayout.WorldScale;
        Transform h = head.transform;
        Vector3 fwd = h.forward;
        Vector3 pos = h.position + fwd * (FloatDistanceMeters * scale);
        Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up);
        CanvasConversion.PlaceHost(Panel, pos, rot, scale * FloatScaleFactor);
        return true;
    }

    // ---- panel-remainder suppression (HandSuppression pattern; nothing destroyed) -------

    /// <summary>
    /// Re-asserted every docked tick: the game rewrites the CanvasGroup alpha
    /// (Show, ToggleVisibility) and could re-enable canvases live. The converted
    /// row is OUTSIDE the window subtree while docked, so neither touches it.
    /// </summary>
    private void ApplySuppression(UIWindow window)
    {
        CanvasGroup? group = window.m_CanvasGroup != null
            ? window.m_CanvasGroup
            : window.GetComponent<CanvasGroup>();
        if (group != null)
        {
            _suppressedGroup = group;
            if (group.alpha != 0f)
                group.alpha = 0f;
            if (group.blocksRaycasts)
                group.blocksRaycasts = false;
        }

        // The 'DamageBackground' vignette is a NESTED CANVAS (test #21 log: adopted
        // with overrideSorting=true when the whole window floated) — a nested canvas
        // renders independently of ancestor CanvasGroup alpha, so it needs the
        // component disabled (stops its entire subtree rendering; the standard
        // hide-UI optimization, see CanvasConversion.AdoptNestedCanvases).
        CanvasScratch.Clear();
        window.GetComponentsInChildren(includeInactive: true, CanvasScratch);
        for (int i = 0; i < CanvasScratch.Count; i++)
        {
            Canvas nested = CanvasScratch[i];
            if (nested == null || !nested.enabled)
                continue;
            nested.enabled = false;
            if (!_disabledCanvases.Contains(nested))
            {
                _disabledCanvases.Add(nested);
                VRLog.Info("WorldUI", $"TAKE DAMAGE BOARD: nested canvas '{nested.name}' disabled " +
                                      "(vignette/backdrop suppressed while the choice row is docked).");
            }
        }
        CanvasScratch.Clear();
    }

    /// <summary>
    /// Undo (undock/shutdown): re-enable every canvas WE disabled; give the
    /// CanvasGroup back only while the window is still open (a closed window's
    /// group belongs to the game's own hide fade — the HandSuppression.Restore rule).
    /// </summary>
    private void RestoreSuppression(UIWindow? window)
    {
        for (int i = 0; i < _disabledCanvases.Count; i++)
        {
            if (_disabledCanvases[i] != null)
                _disabledCanvases[i].enabled = true;
        }
        _disabledCanvases.Clear();

        CanvasGroup? group = _suppressedGroup;
        _suppressedGroup = null;
        if (group != null && window != null && window.IsOpen)
        {
            group.alpha = 1f;
            group.blocksRaycasts = true;
        }
    }

    public override void Shutdown()
    {
        bool hadPanel = Panel != null;
        base.Shutdown(); // releases the conversion → row back in its 2D home
        if (hadPanel)
            RestoreSuppression(PanelWindow(PanelInstance));
        _gaveUp = false;
        _wantSince = 0f;
        _targetWarned = false;
        _hmdFloatPlaced = false;
        if (ReferenceEquals(_instance, this))
            _instance = null; // claim drops → generic fallback owns the window again
    }

    // ---- transform helpers --------------------------------------------------------------

    /// <summary>Deepest common ancestor of two transforms (null-tolerant).</summary>
    private static Transform? CommonAncestor(Transform? a, Transform? b)
    {
        if (a == null || b == null)
            return null;
        int da = Depth(a), db = Depth(b);
        while (da > db) { a = a!.parent; da--; }
        while (db > da) { b = b!.parent; db--; }
        while (a != null && b != null && !ReferenceEquals(a, b))
        {
            a = a.parent;
            b = b.parent;
        }
        return a != null && ReferenceEquals(a, b) ? a : null;
    }

    private static int Depth(Transform t)
    {
        int d = 0;
        for (Transform? p = t.parent; p != null; p = p.parent)
            d++;
        return d;
    }
}
