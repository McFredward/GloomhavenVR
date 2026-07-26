using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.Hands.Interact;
using MapRuleLibrary.Adventure;
using MapRuleLibrary.MapState;
using MapRuleLibrary.Party;
using ScenarioRuleLibrary;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.Surfaces;

/// <summary>
/// Shared implementation for scenario panels that live in a fixed
/// <see cref="PanelSlot"/> of the curved table layout.
/// </summary>
internal abstract class SlotPanelSurface : WorldSurface
{
    protected abstract PanelSlot Slot { get; }

    protected override void Place()
    {
        if (Panel == null || !PanelLayout.TryGetPose(Slot, out Vector3 pos, out Quaternion rot))
            return;
        CanvasConversion.PlaceHost(Panel, pos, rot, PanelLayout.WorldScale);
    }
}

/// <summary>
/// Test #15 dashboard seam: a converted panel DOCKED onto a <see cref="PlayTray"/>
/// mount anchor. The host POSE-FOLLOWS the mount every tick (position/rotation/
/// scale copied — it is never re-parented under the tray: the host carries live
/// GAME UI, and a tray/rig teardown must never cascade into destroying game-owned
/// canvases). Sized by the SHARED tray density (test #16): the panel's world size
/// follows its content pixel size at <see cref="PlayTray.TrayPixelsPerMeter"/>,
/// scaled DOWN (never up) when it would overflow its dock area — so text and
/// portraits stay consistent across all docked panels instead of each panel being
/// stretched to fill its dock. Everything is in tray-local meters × the mount's
/// lossy scale, so it survives tray grab/move/resize and diorama scaling for
/// free. While the tray is hidden the host hides with it; when no mount exists at
/// all (Cards module off, tray destroyed) the panel falls back to the old
/// floating slot layout.
/// </summary>
internal abstract class TrayMountedPanelSurface : SlotPanelSurface
{
    /// <summary>
    /// Density-scale guards (test #16). Max 1: a SMALL host (the 100 px objectives
    /// placeholder) must keep its content-true size, not balloon to fill the dock —
    /// that was the giant objectives text. Min 0.5: if the content measurement ever
    /// misfires huge again (fullscreen junk in the union), the panel overflows its
    /// dock somewhat instead of shrinking below readability — the tiny initiative
    /// portraits were worse than a slightly oversized panel.
    /// </summary>
    private const float MaxDensityScale = 1f;
    private const float MinDensityScale = 0.5f;

    /// <summary>
    /// Per-panel multiplier on the shared tray density (test #17). 1 = the shared
    /// <see cref="PlayTray.TrayPixelsPerMeter"/> as-is; below 1 renders the SAME
    /// content pixels onto MORE tray meters (0.6 ⇒ ~1.67× bigger text). The dock
    /// fit clamp below still applies, so a lowered density never overflows the
    /// panel's dock budget by more than the shared MinDensityScale allowance.
    /// </summary>
    protected virtual float DensityScale => 1f;

    /// <summary>Live mount anchor (null/destroyed → floating fallback).</summary>
    protected abstract Transform? Mount { get; }

    /// <summary>Target panel width, tray-local meters.</summary>
    protected abstract float MountWidth { get; }

    /// <summary>Max panel height, tray-local meters (caps the scale for tall content).</summary>
    protected abstract float MountMaxHeight { get; }

    /// <summary>Which way the panel extends from the mount origin (unit XY in mount space).</summary>
    protected abstract Vector2 GrowDirection { get; }

    protected override void Place()
    {
        if (Panel == null)
            return;

        Transform? mount = Mount; // Unity-null aware: destroyed mounts fall through
        if (mount == null)
        {
            _dockLoggedMountId = 0; // undocked — the next dock logs its rect again
            if (!Panel.HostGo.activeSelf)
                Panel.HostGo.SetActive(true);
            base.Place(); // old floating layout (fallback per the mount-seam contract)
            return;
        }

        // Tray hidden (out-of-scenario transitions, hands down) → panel hides too.
        bool visible = mount.gameObject.activeInHierarchy;
        if (Panel.HostGo.activeSelf != visible)
            Panel.HostGo.SetActive(visible);
        if (!visible)
            return;

        Rect rect = Panel.HostRect.rect; // content-fitted by CanvasConversion.TickFit
        if (rect.width < 1f || rect.height < 1f)
            return;

        // UNIFORM density (test #16): tray-local meters per uGUI pixel is the SAME
        // for every docked panel (1/TrayPixelsPerMeter), clamped down only when the
        // content would overflow this panel's dock budget. The old per-panel
        // dock-FIT scale made effective densities differ 17× between panels. The
        // mount's lossy scale carries BOTH the tray-grab scale and the diorama
        // scale, so the density holds at tray scale 1 and multiplies uniformly.
        float trayScale = mount.lossyScale.x;
        float density = PlayTray.TrayPixelsPerMeter * DensityScale;
        float fitScale = Mathf.Min(
            MountWidth * density / rect.width,
            MountMaxHeight * density / rect.height);
        float metersPerPx = Mathf.Clamp(fitScale, MinDensityScale, MaxDensityScale)
                            / density;

        Vector2 grow = GrowDirection;
        Vector3 offset = new Vector3(
            grow.x * rect.width * metersPerPx * 0.5f,
            grow.y * rect.height * metersPerPx * 0.5f,
            0f) * trayScale;

        Transform host = Panel.HostTransform;
        host.SetPositionAndRotation(mount.position + mount.rotation * offset, mount.rotation);
        host.localScale = Vector3.one * (metersPerPx * trayScale);

        LogDockedRect(mount);
    }

    // ---- dock world-rect diagnostics (test #19 item 3) ---------------------------------
    private static readonly Vector3[] DockCornerScratch = new Vector3[4];
    private int _dockLoggedMountId;
    private Vector2 _dockLoggedWorldSize;

    /// <summary>
    /// Test #19 item 3: log the world rect the docked host canvas — the exact plane
    /// the laser/poke intersect (RayUguiDriver.TryIntersect uses the same corners) —
    /// actually spans: once per (re-)dock and once more per material size change, so
    /// a future "the laser misses the panel" is diagnosable from the log alone
    /// (compare the logged rect against where the content visibly sits on the tray).
    /// Info level on purpose: BepInEx's default disk config drops Debug entirely
    /// (test #19: zero 'Ray-uGUI canvas' lines in LogOutput.log while laser clicks
    /// demonstrably ran), and a diagnostic that never reaches the hardware log
    /// diagnoses nothing. Change-deduped on mount identity + world size (2 % + 1 mm):
    /// tray grabs and diorama rescales move the panel every tick, so POSITION is
    /// logged but never re-triggers — a handful of lines per session.
    /// </summary>
    private void LogDockedRect(Transform mount)
    {
        if (Panel == null)
            return;
        Panel.HostRect.GetWorldCorners(DockCornerScratch);
        float w = (DockCornerScratch[3] - DockCornerScratch[0]).magnitude;
        float h = (DockCornerScratch[1] - DockCornerScratch[0]).magnitude;
        int mountId = mount.GetInstanceID();
        if (mountId == _dockLoggedMountId
            && Mathf.Abs(w - _dockLoggedWorldSize.x) < _dockLoggedWorldSize.x * 0.02f + 0.001f
            && Mathf.Abs(h - _dockLoggedWorldSize.y) < _dockLoggedWorldSize.y * 0.02f + 0.001f)
            return;
        _dockLoggedMountId = mountId;
        _dockLoggedWorldSize = new Vector2(w, h);
        Rect px = Panel.HostRect.rect;
        VRLog.Info("WorldUI", $"Docked '{Panel.HostGo.name}' on '{mount.name}': " +
                              $"world rect {w:F3}x{h:F3} m ({px.width:F0}x{px.height:F0} px), " +
                              $"BL={DockCornerScratch[0]:F3} TR={DockCornerScratch[2]:F3}.");
    }
}

/// <summary>
/// The game's REAL initiative track, docked to the tray dashboard's TOP edge
/// (test #15; the free-floating world panel is gone — 'die Elemente fliegen
/// aktuell rum'). Target verified via ilspycmd (GH.Runtime.dll):
/// <c>public class InitiativeTrack : MonoBehaviour</c> with <c>public static
/// InitiativeTrack Instance { get; private set; }</c> — the component sits at the
/// track's uGUI root, so its own RectTransform is the conversion target. Avatars
/// stay fully interactive (initiative swap pokes go through the host
/// GraphicRaycaster). The vanilla not-locked-in display comes with it for free:
/// <c>InitiativeTrackActorAvatar</c> (decompiled GH.Runtime, ilspycmd) owns it —
/// <c>private const string Unknown = "?"</c>, <c>ViewDigitInitiative()</c> shows
/// <c>m_DigitViewer.ViewSpecialSymbol("?")</c> for initiative 0, and
/// <c>InitiativeTrackPlayerAvatar.CalculateInitiative</c> returns "?" for online
/// players who have not locked in (<c>!actor.IsUnderMyControl</c> during
/// <c>SelectAbilityCardsOrLongRest</c>).
///
/// PORTRAIT CLICKS SWITCH CHARACTERS (test #18). The host canvas registers with
/// <see cref="Hands.Interact.UguiPokeSurfaces"/> (Convert's pokeable default), so
/// the dominant laser (RayUguiDriver) and the fingertip poke (PokeInteractor)
/// synthesize real pointer events on it. The 2D click path (verified via ilspycmd,
/// decompiled GH.Runtime): each row's <c>ExtendedButton avatarButton</c>
/// (InitiativeTrackActorBehaviour.cs:14, a <c>Button</c> subclass) handles
/// <c>OnPointerClick</c> — gated by
/// <c>InteractabilityManager.ShouldAllowClickForExtendedButton</c>
/// (ExtendedButton.cs:168-173) — whose onClick reaches
/// <c>InitiativeTrackPlayerAvatar.OnClick</c> (<c>isSelectableByClick</c>: only
/// heroes the player controls) → <c>InitiativeTrack.Select(actorUI)</c>
/// (<c>IsSelectable</c> phase gate) → <c>avatar.Select()</c> →
/// <c>CardsHandManager.SwitchHand((CPlayerActor)m_Actor)</c>
/// (InitiativeTrackPlayerAvatar.cs:21-28) — the same switch a miniature click
/// performs. <c>UguiPointer.Release</c> fires
/// <c>ExecuteEvents.pointerClickHandler</c>, which IS <c>Button.OnPointerClick</c>,
/// so the whole chain runs and the game enforces control ownership itself. Clicks
/// stayed dead through test #19 because the track's own NESTED CANVAS
/// (<c>[SerializeField] private Canvas canvas</c>, <c>sortingOrder = 40</c>,
/// InitiativeTrack.cs:53) rode into the host: every portrait Graphic registered
/// with IT instead of the host canvas, so the host GraphicRaycaster raycast an
/// empty set (zero uGUI-click lines), and its sorting override drew the track over
/// the depth-non-writing hands regardless of depth — both fixed by
/// <see cref="CanvasConversion"/> neutralizing nested canvases (test #19; the #18
/// PhaseBanner soft lock was real but not the only blocker). No
/// pointer-over-UI feedback loop:
/// UIManager_IsPointerOverUI_Patch reports over-UI only while the beam/fingertip is
/// actually latched onto a registered surface, and the game never hides the
/// initiative track on over-UI (unlike the stat panels, which are therefore
/// converted non-pokeable).
/// </summary>
internal sealed class InitiativeTrackSurface : TrayMountedPanelSurface, IDepthPortraitPicker
{
    public override string Name => "InitiativeTrack";
    protected override bool ConfigEnabled => WorldUIConfig.InitiativeTrack.Value;
    protected override PanelSlot Slot => PanelSlot.InitiativeTrack;
    protected override Transform? Mount => PlayTray.Current?.InitiativeMount;
    protected override float MountWidth => PlayTray.InitiativeMountWidth;
    protected override float MountMaxHeight => PlayTray.InitiativeMountMaxHeight;
    protected override Vector2 GrowDirection => Vector2.up; // bottom edge on the mount

    protected override RectTransform? FindTarget() =>
        InitiativeTrack.Instance != null ? InitiativeTrack.Instance.transform as RectTransform : null;

    // ---- portrait depth normalization (user #3) ---------------------------------------
    /// <summary>
    /// The game authors the initiative row with real 3D DEPTH: transforms NESTED inside
    /// each portrait (the avatar image, and the selection frame that pops the acting
    /// actor forward) carry a serialized local z — NOT the portrait's direct
    /// <c>initiativeTrackHolder</c> child, whose local z are ~equal (an earlier remap of
    /// only those direct children did nothing and never went flat at 0). So the row
    /// RECEDES/steps in depth and the selected portrait is raised.
    /// On the flat perspective UI camera that reads as gentle 2D styling, but on the
    /// world-space host the z is multiplied by the host scale
    /// (<see cref="WorldUIConfig.CanvasScaleMm"/> mm per uGUI pixel × the tray/diorama
    /// scale) into LITERAL geometry — an EXTREME, head-parallaxing spread (user #3:
    /// "the effect is too strong"). It also broke the LASER: RayUguiDriver clamps the
    /// beam to (and derives its GraphicRaycaster screen point from) the FLAT host
    /// plane, but a z-displaced portrait projects to a DIFFERENT screen position under
    /// perspective, so the pick resolved to a neighbour instead of the portrait the
    /// beam visually touches.
    ///
    /// We KEEP the depth (the user likes the recession) but CLAMP the row's TOTAL
    /// front-to-back spread to a small hard maximum: whatever the authored range, the
    /// deepest and shallowest portrait may differ by at most
    /// <see cref="WorldUIConfig.InitiativeDepthMaxSpreadPx"/>. Each portrait's authored z is remapped
    /// proportionally by a single factor (order/direction/relative spacing preserved)
    /// so the raw spread (max − min z) is scaled down to land at exactly the cap and
    /// the rest scale with it — never amplified (a row already flatter than the cap is
    /// left alone). Normalizing on the FULL spread (not the largest |z|) is what makes
    /// the cap a true hard ceiling on the extremes' separation: the earlier per-|z|
    /// band left the front-to-back total at up to twice the band, which the user still
    /// found too strong. The compressed spread reads as subtle recession, and because
    /// the residual parallax scales with z it stays well under a portrait width, so the
    /// flat-plane screen point once again lands inside the correct portrait's projected
    /// rect and the GraphicRaycaster (which already distance-sorts hits) resolves the
    /// one being pointed at.
    ///
    /// Applied every tick while converted, computed from the RECORDED raw z (not the
    /// live, already-compressed value) so it is idempotent; the raw z is restored on
    /// release so the 2D UI is left exactly as the game authored it (the framework's
    /// root-only restore never touches these deep children).
    /// </summary>
    // Cap the row's TOTAL front↔back depth spread. Live-tunable via the debug menu
    // (Panels -> Initiative) — read fresh each tick from
    // WorldUIConfig.InitiativeDepthMaxSpreadPx (default 10 px ≈ ±0.5 cm at 1 mm/px × scale).

    /// <summary>Raw spread (max − min z) below this (px) counts as flat — a row with no authored depth is a no-op.</summary>
    private const float DepthEpsilonPixels = 0.5f;

    /// <summary>Authored (raw) local z per portrait transform, for idempotent remap + restore.</summary>
    private readonly Dictionary<Transform, float> _rawDepth = new(16);

    /// <summary>Depth-bearing transforms this tick (reused; no per-frame allocation).</summary>
    private readonly List<Transform> _depthScratch = new(64);

    /// <summary>DFS work stack for the per-portrait subtree walk (reused; no per-frame allocation).</summary>
    private readonly List<Transform> _depthStack = new(64);

    /// <summary>
    /// Host canvas this surface registered its per-portrait depth picker against
    /// (user #3 follow-up), captured so it can be unregistered on release even after
    /// the framework has Unity-destroyed the host GameObject.
    /// </summary>
    private Canvas? _depthPickHost;

    // ---- inter-round reorder animation (let the game's slide play out) ---------------------
    /// <summary>
    /// True while the game's initiative REORDER ANIMATION is running (or one is queued behind the
    /// current one). Between rounds the game re-sorts the track and plays
    /// <c>InitiativeTrack.AnimateInitiativeReorder</c>: it DISABLES the holder's
    /// <c>HorizontalLayoutGroup</c> + <c>ContentSizeFitter</c>, records each entry's CURRENT
    /// <c>transform.position.x</c>, sorts, then <c>LeanTween.moveX</c>-slides every entry to its
    /// sorted slot over <c>trackReorderDuration</c> (verified decompiled InitiativeTrack.cs).
    /// <c>LeanTween.moveX</c> tweens each portrait's WORLD <c>transform.position.x</c>
    /// (LTDescr.setMoveX). Our per-tick passes fight that: the central content re-fit
    /// (<see cref="CanvasConversion.FitHostToContent"/>) shifts the track's
    /// <c>Target.anchoredPosition</c> and resizes the host when the (layout-disabled) row's bounds
    /// change during the slide, moving the WORLD reference frame out from under those recorded
    /// world-x targets — the slide only hints, then flickers, then the coroutine's end re-enables
    /// the layout group and snaps to the final order. So while the track is animating we HOLD OFF
    /// both interfering passes (freeze the fit, skip depth normalization); they resume on the final,
    /// settled order once the slide completes.
    /// </summary>
    private bool _reorderActive;

    /// <summary><see cref="ConvertedPanel.FitEnabled"/> captured when the reorder began, restored when it settles.</summary>
    private bool _fitEnabledBeforeReorder = true;

    public override void Tick()
    {
        bool wasConverted = Panel != null;
        base.Tick();
        if (Panel != null)
        {
            // Let the game's reorder slide play out un-stomped (see _reorderActive docs): while it
            // animates, freeze the content re-fit and skip depth normalization; resume when it settles.
            UpdateReorderHold();
            if (!_reorderActive)
                NormalizeDepth();
        }
        else if (wasConverted)
        {
            _reorderActive = false; // host gone — the next conversion starts a fresh hold
            RestoreDepth(); // panel released this tick — hand the 2D row its authored z back
            UnregisterDepthPick();
        }
    }

    /// <summary>
    /// Track the game's reorder-animation state and, on its edges, freeze/thaw the interfering
    /// per-tick passes so the slide is neither stomped (mid-animation re-fit invalidates the tween's
    /// recorded world-x slots → flicker) nor snapped. The signal is the game's own
    /// <c>InitiativeTrack.isAnimating</c> (true for the whole <c>AnimateInitiativeReorder</c> window)
    /// OR'd with <c>animationDelayed</c> (a second reorder queued behind the current one) so a
    /// back-to-back re-sort holds continuously instead of thawing for the one-frame gap between them.
    /// </summary>
    private void UpdateReorderHold()
    {
        InitiativeTrack track = InitiativeTrack.Instance;
        bool animating = track != null && (track.isAnimating || track.animationDelayed);
        if (animating == _reorderActive)
            return;
        _reorderActive = animating;

        if (animating)
        {
            // Freeze the central content fit for the slide's duration: FitHostToContent shifts
            // Target.anchoredPosition and resizes the host, which invalidates the tween's recorded
            // WORLD-x slots mid-animation — the reported flicker. Capture the current state so a
            // surface that was (or wasn't) fitting is restored exactly on settle.
            if (Panel != null)
            {
                _fitEnabledBeforeReorder = Panel.FitEnabled;
                Panel.FitEnabled = false;
            }
            VRLog.Info("WorldUI", "Initiative reorder animation detected — deferring content re-fit " +
                                  "and depth normalization so the game's slide plays out un-stomped.");
        }
        else
        {
            // Settled on the final order: resume the normal fit + depth normalization.
            if (Panel != null)
                Panel.FitEnabled = _fitEnabledBeforeReorder;
            VRLog.Info("WorldUI", "Initiative reorder animation complete — resuming content re-fit " +
                                  "and depth normalization on the final order.");
        }
    }

    public override void Shutdown()
    {
        _reorderActive = false; // the panel is about to be released — drop any active hold
        RestoreDepth(); // before base releases the panel (holder still alive here)
        UnregisterDepthPick();
        base.Shutdown();
    }

    private void UnregisterDepthPick()
    {
        if (_depthPickHost is not null) // reference check: the host may be Unity-destroyed
        {
            DepthPortraitPicks.Unregister(_depthPickHost);
            _depthPickHost = null;
        }
    }

    /// <summary>
    /// Clamp the row's TOTAL front-to-back depth spread to <see cref="WorldUIConfig.InitiativeDepthMaxSpreadPx"/>
    /// (see the field docs): walk every active portrait's SUBTREE and scale each authored
    /// non-zero local z by the single factor that maps the raw protrusion spread (from the
    /// flat holder plane) onto the cap — so cap 0 collapses the whole row (portraits AND
    /// their selection frames) onto one plane. Change-gated writes; nothing to fight since
    /// the game never animates portrait z (Select/Deselect toggle material FX + a child
    /// selection object's visibility, never a transform z/scale).
    /// </summary>
    private void NormalizeDepth()
    {
        Transform? holder = InitiativeTrack.Instance != null
            ? InitiativeTrack.Instance.initiativeTrackHolder
            : null;
        if (holder == null)
            return;

        // The row's visible recession/emphasis is authored NOT on the holder's DIRECT
        // children (their local z are ~equal — remapping only those did nothing and 0 was
        // never flat) but on transforms NESTED inside each portrait: the avatar image, and
        // the selection frame whose forward z pops the acting actor toward the head ("manche
        // hervorgehoben"). The game's own selection path never moves a portrait transform in
        // z or scale (Select → selectionObject.SetActive + material _FXAnim only), so the
        // pop is authored geometry, not an animated offset — a subtree walk reaches all of
        // it. Zeroing local z on this single world-space uGUI canvas removes ONLY the
        // geometric protrusion; draw order is hierarchy-based, so the selection glow stays
        // visible (whose turn it is is never hidden) — the row just goes flat.
        _depthScratch.Clear();
        float rawMin = 0f; // the holder plane (local z == 0) is the shallow reference
        float rawMax = 0f;
        foreach (Transform rootChild in holder)
        {
            if (!rootChild.gameObject.activeSelf)
                continue;
            _depthStack.Add(rootChild);
            while (_depthStack.Count > 0)
            {
                int last = _depthStack.Count - 1;
                Transform t = _depthStack[last];
                _depthStack.RemoveAt(last);

                // Record the authored z once; thereafter the remap reads from here, so a
                // prior frame's compressed value never becomes the new baseline. Only
                // depth-BEARING transforms are tracked (|z| >= epsilon) — a flat transform's
                // target is always 0, so tracking it would only add pointless writes.
                if (!_rawDepth.TryGetValue(t, out float raw))
                {
                    raw = t.localPosition.z;
                    if (Mathf.Abs(raw) >= DepthEpsilonPixels)
                        _rawDepth[t] = raw;
                }
                if (_rawDepth.ContainsKey(t))
                {
                    _depthScratch.Add(t);
                    if (raw < rawMin)
                        rawMin = raw;
                    if (raw > rawMax)
                        rawMax = raw;
                }

                for (int i = 0; i < t.childCount; i++)
                {
                    Transform c = t.GetChild(i);
                    if (c.gameObject.activeSelf)
                        _depthStack.Add(c);
                }
            }
        }

        float rawSpread = rawMax - rawMin;
        if (_depthScratch.Count == 0 || rawSpread < DepthEpsilonPixels)
            return; // genuinely flat row (or depth not yet laid out) — nothing to compress

        // Single proportional factor: remap the FULL protrusion spread (deepest ↔ shallowest,
        // measured from the flat holder plane) onto the cap so the extremes never differ by
        // more than the (live) cap, order/direction and relative spacing preserved. Compress
        // only, never amplify. At cap 0 the factor is 0 → every tracked transform's local z
        // collapses to 0, so EVERY portrait (and its selection frame) sits on the one flat
        // plane: a truly flat row at all times. Read the cap fresh each tick so a debug-menu
        // change re-clamps next tick (idempotent — target always from the recorded RAW z).
        float maxSpread = Mathf.Max(0f, WorldUIConfig.InitiativeDepthMaxSpreadPx.Value);
        float scale = Mathf.Min(1f, maxSpread / rawSpread);
        for (int i = 0; i < _depthScratch.Count; i++)
        {
            Transform t = _depthScratch[i];
            float target = _rawDepth[t] * scale;
            Vector3 lp = t.localPosition;
            if (Mathf.Abs(lp.z - target) > 0.001f)
                t.localPosition = new Vector3(lp.x, lp.y, target);
        }
    }

    /// <summary>Restore each recorded portrait's authored z (reversibility) and forget them.</summary>
    private void RestoreDepth()
    {
        if (_rawDepth.Count == 0)
            return;
        foreach (KeyValuePair<Transform, float> kv in _rawDepth)
        {
            Transform t = kv.Key;
            if (t == null)
                continue;
            Vector3 lp = t.localPosition;
            t.localPosition = new Vector3(lp.x, lp.y, kv.Value);
        }
        _rawDepth.Clear();
    }

    /// <summary>
    /// Test #16: measure ONLY the visible portrait row. The InitiativeTrack root
    /// canvas is a full 1920x1080 window whose union of visible graphics measured
    /// ~1714x959 px — siblings of the row (the <c>enemyCardsHolder</c> card-reveal
    /// area, the <c>enemyCardsBlocker</c> raycast blocker, the <c>helpBox</c>,
    /// gamepad hotkey tips) span nearly the whole screen, and dock-fitting that
    /// rect made the actually-visible row minuscule on the tray. The game's own
    /// serialized <c>initiativeTrackHolder</c> (publicized field; verified in
    /// decompiled/GH.Runtime/InitiativeTrack.cs — the HorizontalLayoutGroup +
    /// ContentSizeFitter holder every avatar is pooled under, incl. their '?'
    /// digit labels) IS the row by construction, so scoping the content fit to it
    /// excludes the fullscreen junk by rule, not by name matching.
    /// </summary>
    protected override void OnConverted()
    {
        if (Panel != null && InitiativeTrack.Instance != null)
            Panel.FitContentRoot = InitiativeTrack.Instance.initiativeTrackHolder as RectTransform;

        // Register per-portrait depth-aware laser picking against the live host canvas
        // (user #3 follow-up). RayUguiDriver intersects (and hands UguiPointer) exactly
        // this HostCanvas, so keying the picker on it scopes the depth path to this panel.
        if (Panel != null)
        {
            _depthPickHost = Panel.HostCanvas;
            DepthPortraitPicks.Register(_depthPickHost, this);
        }

        LogTrackTextureDiag();
    }

    // ---- rendered-texture diagnostics (aliasing round 3) --------------------------------

    /// <summary>Latched once a conversion had portrait textures to report (avatars are
    /// pooled in over the first frames — retry on later conversions until one does).</summary>
    private static bool s_texDiagLogged;

    /// <summary>
    /// One-line diag for the initiative-track shimmer investigation (user report: MSAA
    /// helps the board but NOT the initiative track). The portraits/frames are GAME
    /// sprites on an adopted world-space canvas — their edge shimmer is TEXTURE-space,
    /// which MSAA (geometry edges only) cannot touch. Log the biggest source textures'
    /// mip/aniso/filter state once per session so the hardware log PROVES whether mips
    /// exist: mipmapCount == 1 ⇒ the atlas ships MIPLESS, minification shimmer is baked
    /// into the data, and the honest lever is eye-texture supersampling
    /// ([RenderQuality] EyeResolutionScale, e.g. 1.3).
    /// </summary>
    private void LogTrackTextureDiag()
    {
        if (s_texDiagLogged || InitiativeTrack.Instance == null)
            return;
        try
        {
            var seen = new HashSet<int>();
            var sb = new System.Text.StringBuilder(256);
            int count = 0;
            Image[] images = InitiativeTrack.Instance.GetComponentsInChildren<Image>(includeInactive: false);
            foreach (Image img in images)
            {
                Sprite? sprite = img != null ? img.sprite : null;
                Texture2D? tex = sprite != null ? sprite.texture : null;
                if (tex == null || !seen.Add(tex.GetInstanceID()))
                    continue;
                if (count > 0)
                    sb.Append(", ");
                sb.Append('\'').Append(tex.name).Append("' ").Append(tex.width).Append('x')
                  .Append(tex.height).Append(" mips=").Append(tex.mipmapCount)
                  .Append(" aniso=").Append(tex.anisoLevel).Append(' ').Append(tex.filterMode);
                if (++count >= 8)
                    break;
            }
            if (count == 0)
                return; // portraits not pooled in yet — retry next conversion
            s_texDiagLogged = true;
            VRLog.Info("WorldUI", "INITIATIVE TEXTURE DIAG (game sprites the adopted track samples): " +
                                  $"{sb} — mips=1 ⇒ MIPLESS source data: MSAA/aniso cannot stop the " +
                                  "shimmer; raise [RenderQuality] EyeResolutionScale (e.g. 1.3) instead.");
        }
        catch (System.Exception ex)
        {
            s_texDiagLogged = true; // never spam a throwing path
            VRLog.Warn("WorldUI", $"Initiative texture diag skipped ({ex.Message}).");
        }
    }

    // ---- per-portrait pick geometry (user #3 follow-up) -------------------------------
    private static readonly Vector3[] PickCorners = new Vector3[4];

    /// <summary>
    /// Depth-aware laser pick over the initiative portraits (<see cref="IDepthPortraitPicker"/>).
    /// Each portrait is a direct child of <c>initiativeTrackHolder</c> carrying its own
    /// (normalized) local z, so its world rect sits at the portrait's ACTUAL position and
    /// depth. We intersect the true aim ray with each active portrait's world rect and
    /// return the NEAREST hit's clickable <c>avatarButton</c> — so pointing at a portrait
    /// selects THAT portrait, respecting its height/depth, where the flat host-plane pick
    /// projected onto a depth-displaced neighbour. Non-portrait siblings under the holder
    /// (the gamepad hotkey tips) carry no <c>InitiativeTrackActorBehaviour</c>/button and
    /// are skipped by rule. Ray misses fall back to the ordinary flat pick in UguiPointer.
    /// </summary>
    bool IDepthPortraitPicker.TryPickPortrait(Vector3 rayOrigin, Vector3 rayDirection,
        out GameObject target, out Vector3 worldHit)
    {
        target = null!;
        worldHit = default;

        Transform? holder = InitiativeTrack.Instance != null
            ? InitiativeTrack.Instance.initiativeTrackHolder
            : null;
        if (holder == null)
            return false;

        float bestDist = float.PositiveInfinity;
        foreach (Transform child in holder)
        {
            if (!child.gameObject.activeSelf || child is not RectTransform rect)
                continue;
            InitiativeTrackActorBehaviour behaviour = child.GetComponent<InitiativeTrackActorBehaviour>();
            if (behaviour == null || behaviour.avatarButton == null)
                continue; // not a portrait (hotkey tip / separator) — no click target
            GameObject buttonGo = behaviour.avatarButton.gameObject;
            if (!buttonGo.activeInHierarchy)
                continue;

            // Portrait cell world rect (child inherits the stepped z → real depth).
            if (TryIntersectRect(rect, rayOrigin, rayDirection, bestDist, out float dist, out Vector3 point))
            {
                bestDist = dist;
                target = buttonGo;
                worldHit = point;
            }
        }

        return target != null;
    }

    /// <summary>
    /// Ray ∩ a portrait's world rect via its actual WORLD-SPACE corners (mirrors
    /// RayUguiDriver.TryIntersect's host-plane math, applied per portrait): the plane is
    /// spanned by the real corners at the portrait's true position/depth, so a hit means
    /// the beam geometrically lands inside that portrait. Front-side only (uGUI faces
    /// -normal); a nearer portrait wins via the shrinking <paramref name="maxDist"/>.
    /// </summary>
    private static bool TryIntersectRect(RectTransform rect, Vector3 origin, Vector3 direction,
        float maxDist, out float dist, out Vector3 point)
    {
        dist = 0f;
        point = default;

        rect.GetWorldCorners(PickCorners); // 0=BL, 1=TL, 2=TR, 3=BR
        Vector3 right = PickCorners[3] - PickCorners[0];
        Vector3 up = PickCorners[1] - PickCorners[0];
        float rightLen2 = right.sqrMagnitude;
        float upLen2 = up.sqrMagnitude;
        if (rightLen2 < 1e-12f || upLen2 < 1e-12f)
            return false; // degenerate / not laid out yet

        Vector3 normal = Vector3.Cross(right, up).normalized;
        float denom = Vector3.Dot(direction, normal);
        if (denom < 1e-5f)
            return false; // parallel or back-side

        float d = Vector3.Dot(PickCorners[0] - origin, normal) / denom;
        if (d <= 0f || d >= maxDist)
            return false;

        Vector3 p = origin + direction * d;
        Vector3 rel = p - PickCorners[0];
        float u = Vector3.Dot(rel, right) / rightLen2;
        float v = Vector3.Dot(rel, up) / upLen2;
        if (u < 0f || u > 1f || v < 0f || v > 1f)
            return false;

        dist = d;
        point = p;
        return true;
    }
}

/// <summary>
/// Element infusion board, docked in the tray dashboard's LEFT column below the
/// objectives panel (test #20; the free-floating world panel is gone — it hung over
/// the table as an orphaned 'circle with squiggles'; floating slot layout only as
/// the no-tray fallback). Behaves exactly like the objectives dock: pose-follows
/// its mount, shared tray density, moves/scales/pins with the tray.
/// Verified: <c>public class InfusionBoardUI : MonoBehaviour</c> with
/// <c>public static InfusionBoardUI Instance { get; private set; }</c>.
/// </summary>
internal sealed class ElementBoardSurface : TrayMountedPanelSurface
{
    public override string Name => "ElementBoard";
    protected override bool ConfigEnabled => WorldUIConfig.ElementBoard.Value;
    protected override PanelSlot Slot => PanelSlot.ElementBoard;
    protected override Transform? Mount => PlayTray.Current?.ElementMount;
    protected override float MountWidth => PlayTray.ElementMountWidth;
    protected override float MountMaxHeight => PlayTray.ElementMountMaxHeight;
    protected override Vector2 GrowDirection => Vector2.left; // right edge on the mount, same column as the objectives

    /// <summary>
    /// The element icons are glanced at from board distance, not read like text —
    /// at the shared density they render smaller than that job warrants. 0.8×
    /// draws them 1.25× bigger; the dock fit clamp still bounds the board to its
    /// mount budget like every docked panel.
    /// </summary>
    protected override float DensityScale => 0.8f;

    protected override RectTransform? FindTarget() =>
        InfusionBoardUI.Instance != null ? InfusionBoardUI.Instance.transform as RectTransform : null;

    /// <summary>
    /// Scope the content fit to the game's own serialized <c>elementsHolder</c>
    /// (publicized field; verified in decompiled/GH.Runtime/InfusionBoardUI.cs —
    /// every <c>InfusionElementUI</c> is instantiated under it in Awake), the same
    /// by-construction rule the initiative track uses: fullscreen siblings on the
    /// root canvas can never leak into the measured rect.
    /// </summary>
    protected override void OnConverted()
    {
        if (Panel != null && InfusionBoardUI.Instance != null)
            Panel.FitContentRoot = InfusionBoardUI.Instance.elementsHolder as RectTransform;
    }
}

/// <summary>
/// Scenario objectives ('das Ziel'), docked to the tray dashboard's LEFT edge
/// (test #15; floating slot layout only as fallback).
/// Verified: <c>UIManager.MissionObjectiveContainer</c> property
/// (<c>public MissionObjectiveContainer MissionObjectiveContainer =&gt;
/// missionObjectiveContainer;</c>) — populated by <c>UIManager.InitScenario</c>.
/// The container's own rect is ZERO-sized (its objective entries overflow it), so
/// it converts via the 100 px placeholder path — the content fit measures the
/// real text bounds through <see cref="ConvertedPanel.FitFrameDegenerate"/>
/// (test #16: a 100 px host stretched to the dock made the text giant).
/// </summary>
internal sealed class ObjectivesSurface : TrayMountedPanelSurface
{
    public override string Name => "Objectives";
    protected override bool ConfigEnabled => WorldUIConfig.Objectives.Value;
    protected override PanelSlot Slot => PanelSlot.Objectives;
    protected override Transform? Mount => PlayTray.Current?.ObjectivesMount;

    /// <summary>
    /// Task-panel WIDTH ('Breite' in the debug menu). The dock fits its content UNIFORMLY into
    /// <see cref="MountWidth"/> × <see cref="MountMaxHeight"/>, and this budget is the base
    /// <see cref="PlayTray.ObjectivesMountWidth"/> scaled by the per-board <c>ObjectivesWidth</c>
    /// multiplier. The panel grows LEFTWARD from the board edge into open space
    /// (GrowDirection = left ⇒ no board overlap / run-off). Read live each tick, so a debug-menu
    /// change re-fits next frame — no mount rebuild.
    ///
    /// The budget ALONE is not the lever, though — see <see cref="ApplyContentWidth"/> for the
    /// root cause of "die Breite verändert nichts" and for the pixel width this budget is now
    /// actually FORCED onto the game's objective rows.
    /// </summary>
    protected override float MountWidth =>
        PlayTray.ObjectivesMountWidth * CardsConfig.ObjectivesWidth(CardsConfig.CurrentBoard).Value;
    protected override float MountMaxHeight => PlayTray.ObjectivesMountMaxHeight;
    protected override Vector2 GrowDirection => Vector2.left; // right edge on the mount

    /// <summary>Last objectives width budget we logged (change-gated so a per-tick re-fit stays quiet).</summary>
    private float _loggedWidth = -1f;

    protected override void Place()
    {
        // Log the applied objectives width budget on (re)layout — once, and again whenever the
        // debug-menu multiplier changes — so a "the task/progress bar is too narrow" report is
        // diagnosable from the hardware log alone (Info: BepInEx's default disk config drops Debug).
        // NOTE: this line only proves the BUDGET moved. Whether anything VISIBLE moved is proven by
        // the 'OBJECTIVES WIDTH' / 'OBJECTIVES WIDTH APPLIED' lines (ApplyContentWidth) — the old
        // hardware log had 31 of these budget lines sweeping 364→624 mm and NOT ONE re-logged
        // 'Docked GloomhavenVR.Panel_Objectives' rect in between, which is exactly the bug.
        float w = MountWidth;
        if (Mathf.Abs(w - _loggedWidth) > 1e-4f)
        {
            _loggedWidth = w;
            VRLog.Info("WorldUI", $"Objectives dock width budget {w * 1000f:F0} mm " +
                                  $"({CardsConfig.ObjectivesWidth(CardsConfig.CurrentBoard).Value:F2}× base " +
                                  $"{PlayTray.ObjectivesMountWidth * 1000f:F0} mm) — forced onto the objective " +
                                  "rows as a pixel width; see the 'OBJECTIVES WIDTH' lines.");
        }
        base.Place();
    }

    // ---- forced CONTENT width: the real lever behind the 'Breite' dial ---------------------

    /// <summary>
    /// ROOT CAUSE of the user report "die Aufgabenbreite hat immer noch überhaupt keinen Einfluss"
    /// (third attempt) — this time established from the game's own PREFABS instead of guessed.
    ///
    /// THE ACTUAL HIERARCHY (UnityPy dump of GH_Data; row prefab 'UI Mission Objective' in
    /// sharedassets6.assets, container 'Mission Objective Container' in level6):
    /// <code>
    /// Mission Objective Container   RectTransform + VerticalLayoutGroup(ctrlW=1, expandW=1, pad 0)
    ///                               anchors (0,0)-(0,0)  sizeDelta (0,0)   ← MissionObjectiveContainer
    ///   UI Quest Type               LayoutElement(prefH 36)                ← questHeader
    ///   Container                   RectTransform + VerticalLayoutGroup(ctrlW=1, expandW=1, padL 11)
    ///                               anchors (0,0)-(0,0)  sizeDelta (0,0)   ← objectiveContainer
    ///     UI Mission Objective (×n) HorizontalLayoutGroup(ctrlW=1, expandW=1, padL 25, padR 5)
    ///                             + ContentSizeFitter(horizontal = Unconstrained, vertical = Preferred)
    ///                               anchors (0,0)-(0,0)  sizeDelta (503,0)
    ///       Image Progress bar      anchors (0,0)-(1,1) sizeDelta (-22.5,0), LayoutElement.ignore=1
    ///       Check                   20×20, LayoutElement.ignore=1
    ///       TextMeshPro Text        anchors (0,0)-(0,0) sizeDelta (0,0), NO LayoutElement  ← the text
    ///       Space                   LayoutElement(minW 82)
    ///       Marker                  36×36, LayoutElement.ignore=1
    /// </code>
    ///
    /// So the wrap column is NOT authored anywhere — it is DERIVED, top-down, by three nested
    /// layout groups: <c>MissionObjectiveContainer</c>'s VerticalLayoutGroup drives
    /// <c>objectiveContainer</c>'s width, which drives every row's width, whose
    /// HorizontalLayoutGroup finally hands the leftover to 'TextMeshPro Text'
    /// (≈ rowWidth − 11 − 25 − 5 − 82). The row's own ContentSizeFitter is horizontally
    /// UNCONSTRAINED, so a row NEVER sizes itself — it is always the parent's width. The progress
    /// bar is stretch-anchored (0→1) with a −22.5 inset, so its length is the row width, minus a
    /// constant, for free. ONE rect therefore decides the whole column: the container ROOT.
    ///
    /// WHY IT WAS 0 PX WIDE (the "0 full-width rect(s)" in the last hardware log): that root's
    /// authored rect is literally (0,0) — its width normally comes from ITS parent's layout in the
    /// game's 2D HUD. Conversion re-parents it under the world-space host, where nothing drives it
    /// any more, so <see cref="CanvasConversion"/> falls back to its degenerate-rect placeholder:
    /// <c>size = max(size, 100)</c> ⇒ the container converts 100 px WIDE (LogOutput.log:250,
    /// "Docked 'GloomhavenVR.Panel_Objectives' … (100x100 px)"). 100 px cascades down to a
    /// ~89 px row, and 89 − 11 − 30 − 82 leaves the text a NEGATIVE budget, i.e. its layout minimum
    /// — which is exactly the "ONE WORD PER LINE" in .planning/debug/position_gegnerinfo.png.
    ///
    /// WHY THE PREVIOUS FIX WROTE NOTHING: it scaled against <c>Panel.HostRect.rect.width</c> = 194
    /// px (the fitted union of visible GRAPHICS — bigger than the 100 px container because the quest
    /// header's Background stretches 70 px past it) and then only accepted rects between 0.6× and 4×
    /// of that, i.e. 116…776 px. The real rects are 100 px (container), 89 px (row), &lt;60 px
    /// (text) — ALL BELOW THE 116 px FLOOR — and the progress bar is stretch-anchored, which the
    /// filter skipped by design. Every single candidate was rejected: "0 full-width rect(s)", no
    /// write, no visible change. The 194 px base was never the wrap column; it was a symptom.
    ///
    /// THE FIX — widen the ONE rect the column is derived from: <c>Panel.Target</c>, i.e.
    /// <c>MissionObjectiveContainer</c>'s own RectTransform. After conversion its anchors are
    /// collapsed to the host centre (CanvasConversion.Convert), so its width IS its
    /// <c>sizeDelta.x</c> and nothing else drives it — writing it sticks. The two VerticalLayoutGroups
    /// then propagate the new width to the list and to every row, the row's HorizontalLayoutGroup
    /// re-hands the leftover to the TMP text (TMP re-wraps at the wider measure), and the
    /// stretch-anchored progress bar gets longer. That is the exact OPPOSITE of the old pass, which
    /// skipped stretched rects and DISABLED <c>childControlWidth</c> — i.e. it dismantled the very
    /// mechanism that carries the width downward. Nothing below the root is touched any more, so
    /// icons, check marks and digits cannot distort.
    ///
    /// The dial also becomes literal. The dock fit computes
    /// <c>fitScale = MountWidth·density / contentW</c> and clamps it to [0.5, 1]; with the content
    /// forced to <c>wantPx = MountWidth·density</c> the measured union is <c>wantPx + c</c> (c ≈ the
    /// constant 94 px header overhang), so fitScale ≈ 0.85…0.89 — inside the clamp for the whole dial
    /// range. World width then evaluates to <c>union · fitScale / density = MountWidth</c> EXACTLY,
    /// while the rendered font size stays put. Before, fitScale was 2.7 (194 px content vs a 524 px
    /// budget), saturated at the clamp, and the applied geometry was a constant — 31 budget lines
    /// swept 364→624 mm in LogOutput.log with the panel frozen at 194×164 px / 2.305 m.
    ///
    /// REVERSIBILITY: the container's <c>sizeDelta.x</c> as the conversion installed it is recorded
    /// at capture and written back in <see cref="RestoreContentWidth"/> — but ONLY while the live
    /// value is still the one WE wrote. <c>CanvasConversion.Release</c> restores the pristine 2D
    /// <c>OriginalSizeDelta</c> and runs BEFORE our released-this-tick restore, so that guard is what
    /// keeps us from stomping the game's authored rect back to the conversion placeholder.
    ///
    /// MULTIPLAYER: purely local presentation on the local player's HUD — no game state, no
    /// simulation input, nothing serialized. Remote clients are unaffected by construction.
    /// </summary>
    /// <remarks>Delay before the applied-width verification / subtree dump (lets layout settle).</remarks>
    private const float WidthVerifyDelay = 1.5f;

    /// <summary>Hard cap on the one-shot subtree dump so a pathological tree cannot flood the log.</summary>
    private const int WidthDumpMaxRects = 64;

    /// <summary>The rect we widen (the converted <c>MissionObjectiveContainer</c> root).</summary>
    private RectTransform? _widthLever;

    /// <summary>Its <c>sizeDelta.x</c> as captured — handed back verbatim on restore.</summary>
    private float _widthAuthoredX;

    /// <summary>The last <c>sizeDelta.x</c> WE wrote — the guard that makes the restore safe.</summary>
    private float _widthForcedX;

    /// <summary>A horizontal ContentSizeFitter on the lever itself would drive our write away.</summary>
    private ContentSizeFitter? _widthFitter;
    private ContentSizeFitter.FitMode _widthFitterMode;

    private bool _widthCaptured;
    private float _widthAppliedPx = -1f;
    private float _widthVerifyAt;
    private bool _widthDumped;
    private float _widthDumpAt;
    private static bool s_widthErrorLogged;

    /// <summary>
    /// The game's own serialized objective LIST holder (publicized
    /// <c>MissionObjectiveContainer.objectiveContainer</c> — every <c>MissionObjectiveUI</c> is
    /// instantiated under it, decompiled GH.Runtime/MissionObjectiveContainer.cs:123). Only used to
    /// know WHEN rows exist (for the one-shot subtree dump) — the width lever is its PARENT, see
    /// <see cref="ApplyContentWidth"/>.
    /// </summary>
    private RectTransform? ObjectiveList()
    {
        UIManager manager = UIManager.Instance;
        MissionObjectiveContainer? container = manager != null ? manager.MissionObjectiveContainer : null;
        return container != null ? container.objectiveContainer as RectTransform : null;
    }

    /// <summary>
    /// Force the configured width budget onto the objectives container root (see the big
    /// <see cref="WidthVerifyDelay"/> comment for the hierarchy evidence, the root cause and the
    /// full contract). Runs every tick while converted: two float comparisons in the steady state,
    /// and idempotent by construction — the target is derived from the CONFIG, never from the live
    /// (already widened) width, so re-running can never compound.
    /// </summary>
    private void ApplyContentWidth()
    {
        if (Panel == null)
            return;

        try
        {
            RectTransform lever = Panel.Target;
            if (lever == null)
                return;

            // A re-conversion hands us a different rect (or the same rect reset to the 100 px
            // placeholder): drop the stale record so the next capture reads the fresh authored one.
            if (_widthCaptured && !ReferenceEquals(lever, _widthLever))
                RestoreContentWidth();
            if (!_widthCaptured)
                CaptureContentWidth(lever);

            float density = PlayTray.TrayPixelsPerMeter * DensityScale;
            float wantPx = MountWidth * density;

            bool changed = false;

            // Re-assert every tick (change-gated): a ContentSizeFitter DRIVES SizeDeltaX, so one
            // game-side rebuild with it re-enabled would silently stomp the width we wrote and the
            // dial would look dead again. Vertical fitting is untouched — the rows must keep growing
            // in height as the text re-wraps.
            if (_widthFitter != null && _widthFitter.horizontalFit != ContentSizeFitter.FitMode.Unconstrained)
            {
                _widthFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                changed = true;
            }

            if (Mathf.Abs(lever.sizeDelta.x - wantPx) > 0.5f)
            {
                lever.sizeDelta = new Vector2(wantPx, lever.sizeDelta.y);
                _widthForcedX = wantPx;
                changed = true;
            }

            // Arm the one-shot subtree dump as soon as the game has actually instantiated rows —
            // dumping an empty list would name nothing (the count-only diagnostic is what cost the
            // last two rounds).
            RectTransform? list = ObjectiveList();
            if (!_widthDumped && _widthDumpAt <= 0f && list != null && list.childCount > 0)
                _widthDumpAt = Time.unscaledTime + WidthVerifyDelay;

            if (!changed)
                return;

            // Re-wrap NOW: the layout groups re-run top-down and TMP re-measures at the new column,
            // so the objectives host is already the right size when the central TickFit next runs.
            LayoutRebuilder.ForceRebuildLayoutImmediate(lever);

            if (Mathf.Abs(wantPx - _widthAppliedPx) > 0.5f)
            {
                _widthAppliedPx = wantPx;
                _widthVerifyAt = Time.unscaledTime + WidthVerifyDelay;
                VRLog.Info("WorldUI", $"OBJECTIVES WIDTH: container root '{lever.name}' forced to " +
                                      $"{wantPx:F0} px (was {_widthAuthoredX:F0} px authored) for the " +
                                      $"{MountWidth * 1000f:F0} mm budget at {density:F0} px/m — the two " +
                                      "VerticalLayoutGroups carry it to every row, the row's " +
                                      "HorizontalLayoutGroup re-hands the leftover to the TMP text.");
            }
        }
        catch (System.Exception ex)
        {
            // A game-side surprise must never starve the WorldUI tick (the unguarded-Update lesson):
            // hand the rect back, stop trying, and log once.
            if (!s_widthErrorLogged)
            {
                s_widthErrorLogged = true;
                VRLog.Warn("WorldUI", $"OBJECTIVES WIDTH: forcing the task width failed ({ex.Message}) — " +
                                      "the objectives keep their authored layout.");
            }
            RestoreContentWidth();
        }
    }

    /// <summary>
    /// Record the lever's PRISTINE horizontal state — the <c>sizeDelta.x</c> the conversion
    /// installed, plus a horizontal ContentSizeFitter if one ever appears on it — so
    /// <see cref="RestoreContentWidth"/> can put it back exactly. Nothing below the root is
    /// recorded because nothing below the root is written: every descendant width is DERIVED by the
    /// game's own layout groups (see the hierarchy in the <see cref="WidthVerifyDelay"/> comment).
    /// </summary>
    private void CaptureContentWidth(RectTransform lever)
    {
        _widthLever = lever;
        _widthAuthoredX = lever.sizeDelta.x;
        _widthForcedX = float.NaN; // nothing written yet — the restore guard must not match
        _widthFitter = lever.GetComponent<ContentSizeFitter>();
        _widthFitterMode = _widthFitter != null
            ? _widthFitter.horizontalFit
            : ContentSizeFitter.FitMode.Unconstrained;
        _widthAppliedPx = -1f; // force the next apply to log against the fresh capture
        _widthDumped = false;
        _widthDumpAt = 0f;
        _widthCaptured = true;
    }

    /// <summary>
    /// Hand the objectives container its captured horizontal layout back and forget the capture.
    /// Called on release, on shutdown, on re-conversion and on any failure, so the game UI is never
    /// left modified (the framework's root-only restore in <c>CanvasConversion.Release</c> restores
    /// the pristine 2D rect, which is a DIFFERENT value than the conversion placeholder we captured).
    ///
    /// The write is guarded on the live value still being the one we forced: on a release
    /// <c>CanvasConversion.Release</c> has ALREADY restored the pristine 2D <c>OriginalSizeDelta</c>
    /// by the time this runs, and writing our captured placeholder over it would leave the game's
    /// own HUD container 100 px wide for the rest of the session.
    /// </summary>
    private void RestoreContentWidth()
    {
        if (_widthCaptured && _widthLever != null
            && !float.IsNaN(_widthForcedX)
            && Mathf.Abs(_widthLever.sizeDelta.x - _widthForcedX) <= 0.5f)
        {
            _widthLever.sizeDelta = new Vector2(_widthAuthoredX, _widthLever.sizeDelta.y);
        }
        if (_widthFitter != null)
            _widthFitter.horizontalFit = _widthFitterMode;

        _widthLever = null;
        _widthFitter = null;
        _widthCaptured = false;
        _widthAuthoredX = 0f;
        _widthForcedX = float.NaN;
        _widthAppliedPx = -1f;
        _widthVerifyAt = 0f;
        _widthDumpAt = 0f;
        _widthDumped = false;
    }

    /// <summary>
    /// Proof lines for the next hardware log. Two of them, on purpose:
    /// <list type="bullet">
    /// <item>'OBJECTIVES WIDTH APPLIED' — the SETTLED host rect in pixels and metres a moment after
    ///   a width change. Grep it and compare two consecutive entries: if the dial works, both
    ///   numbers move with it. (The very first diagnostic only proved the BUDGET moved, which is
    ///   why a dead dial looked alive in the log for two rounds.)</item>
    /// <item>'OBJECTIVES TREE' — a ONE-SHOT dump of the real rect chain (see
    ///   <see cref="LogObjectiveSubtree"/>). If the width STILL does not move, this line names the
    ///   rect that ate it instead of leaving the next round to guess again.</item>
    /// </list>
    /// </summary>
    private void LogWidthVerification()
    {
        if (Panel == null)
            return;

        float now = Time.unscaledTime;

        if (_widthVerifyAt > 0f && now >= _widthVerifyAt)
        {
            _widthVerifyAt = 0f;
            Rect px = Panel.HostRect.rect;
            Panel.HostRect.GetWorldCorners(QuestCorners); // 0=BL, 1=TL, 2=TR, 3=BR
            float w = (QuestCorners[3] - QuestCorners[0]).magnitude;
            float h = (QuestCorners[1] - QuestCorners[0]).magnitude;
            VRLog.Info("WorldUI", $"OBJECTIVES WIDTH APPLIED: content settled at {px.width:F0}x{px.height:F0} px " +
                                  $"(forced {_widthAppliedPx:F0} px, budget {MountWidth * 1000f:F0} mm) — " +
                                  $"world rect {w:F3}x{h:F3} m. Both numbers MUST move when 'Breite' changes.");
        }

        if (!_widthDumped && _widthDumpAt > 0f && now >= _widthDumpAt)
        {
            _widthDumpAt = 0f;
            _widthDumped = true;
            if (Panel.Target != null)
                LogObjectiveSubtree(Panel.Target);
        }
    }

    /// <summary>
    /// One-shot dump of the objectives subtree, in ONE log record: per rect its name, RESOLVED
    /// width×height, horizontal anchor span (a stretched rect follows its parent and cannot be
    /// sized directly), <c>sizeDelta.x</c>, the layout components it carries (VLG/HLG with their
    /// <c>childControlWidth</c>/<c>childForceExpandWidth</c> flags, ContentSizeFitter's horizontal
    /// mode, LayoutElement's min/preferred/flexible width) and whether it holds TMP text. That is
    /// exactly the information needed to tell WHICH rect sets the wrap column — the count-only
    /// diagnostic that preceded it ("0 full-width rect(s)") identified nothing and cost two rounds.
    /// </summary>
    private void LogObjectiveSubtree(RectTransform root)
    {
        var sb = new System.Text.StringBuilder(2048);
        sb.Append("OBJECTIVES TREE (widths are RESOLVED px; the FIRST line is the rect the 'Breite' ")
          .Append("dial writes — every deeper width is derived from it by the layout groups):");
        int budget = WidthDumpMaxRects;
        AppendRectLine(sb, root, 0, ref budget);
        if (budget <= 0)
            sb.Append("\n  … truncated at ").Append(WidthDumpMaxRects).Append(" rects.");
        VRLog.Info("WorldUI", sb.ToString());
    }

    /// <summary>Recursive worker for <see cref="LogObjectiveSubtree"/> (inactive children included).</summary>
    private static void AppendRectLine(System.Text.StringBuilder sb, RectTransform? rect, int depth, ref int budget)
    {
        if (rect == null || budget <= 0)
            return;
        budget--;

        Rect r = rect.rect;
        sb.Append('\n').Append(' ', 2 + depth * 2)
          .Append(rect.name).Append("  ").Append(r.width.ToString("F0")).Append('x')
          .Append(r.height.ToString("F0"))
          .Append("  anchorX ").Append(rect.anchorMin.x.ToString("F2")).Append("..")
          .Append(rect.anchorMax.x.ToString("F2"))
          .Append(rect.anchorMax.x - rect.anchorMin.x > 0.01f ? " STRETCHED" : "")
          .Append("  sizeDeltaX ").Append(rect.sizeDelta.x.ToString("F0"));

        var group = rect.GetComponent<HorizontalOrVerticalLayoutGroup>();
        if (group != null)
            sb.Append("  [").Append(group is VerticalLayoutGroup ? "VLG" : "HLG")
              .Append(" ctrlW=").Append(group.childControlWidth ? '1' : '0')
              .Append(" expandW=").Append(group.childForceExpandWidth ? '1' : '0')
              .Append(" padL=").Append(group.padding.left)
              .Append(" padR=").Append(group.padding.right).Append(']');

        var fitter = rect.GetComponent<ContentSizeFitter>();
        if (fitter != null)
            sb.Append("  [CSF h=").Append(fitter.horizontalFit).Append(" v=").Append(fitter.verticalFit).Append(']');

        var element = rect.GetComponent<LayoutElement>();
        if (element != null)
            sb.Append("  [LE ignore=").Append(element.ignoreLayout ? '1' : '0')
              .Append(" minW=").Append(element.minWidth.ToString("F0"))
              .Append(" prefW=").Append(element.preferredWidth.ToString("F0"))
              .Append(" flexW=").Append(element.flexibleWidth.ToString("F0")).Append(']');

        var tmp = rect.GetComponent<TMP_Text>();
        if (tmp != null)
            sb.Append("  [TMP wrap=").Append(tmp.enableWordWrapping ? '1' : '0')
              .Append(" \"").Append(tmp.text.Length > 24 ? tmp.text.Substring(0, 24) + "…" : tmp.text).Append("\"]");

        if (!rect.gameObject.activeSelf)
            sb.Append("  (inactive)");

        for (int i = 0; i < rect.childCount && budget > 0; i++)
            AppendRectLine(sb, rect.GetChild(i) as RectTransform, depth + 1, ref budget);
    }

    /// <summary>
    /// Test #17: at the shared density the objectives text read too small (the
    /// initiative track at the SAME density was verdict-perfect — do not touch it).
    /// 0.6× density renders the objectives ~1.67× bigger; the dock fit clamp keeps
    /// them inside the mount budget.
    /// </summary>
    protected override float DensityScale => 0.6f;

    protected override RectTransform? FindTarget()
    {
        UIManager manager = UIManager.Instance;
        return manager != null && manager.MissionObjectiveContainer != null
            ? manager.MissionObjectiveContainer.transform as RectTransform
            : null;
    }

    // ---- battle goal line (per-scenario secret goal) -------------------------------------

    /// <summary>
    /// The CURRENT character's SECRET BATTLE GOAL for the running scenario ("persönliche
    /// Quest für das Szenario" — NOT the campaign retirement quest, which the first cut
    /// showed by mistake), shown as a compact mod-drawn TMP block anchored just BELOW the
    /// converted objectives panel (left of the control board). The objectives host carries
    /// live GAME UI (MissionObjectiveContainer), so appending content INSIDE it would be
    /// invasive — a standalone world-space label that pose-follows the host every tick is
    /// robust against reconversion and tray moves.
    ///
    /// Data path (verified in decompiled GH.Runtime/ActorStatPanel.cs:566-571 — the exact
    /// resolution the game's own stat panel uses):
    /// <c>AdventureState.MapState.InProgressQuestState.GetChosenBattleGoal(actor.Class.ID)</c>
    /// → <c>CBattleGoalState</c> (MapRuleLibrary.Party); its <c>BattleGoal</c> property
    /// resolves the <c>BattleGoalYMLData</c> record whose <c>LocalisedName</c> /
    /// <c>LocalisedDescription</c> are the SAME localization keys the game's
    /// <c>UIBattleGoalProgress.SetBattleGoal</c> feeds through TextLocalizedListener.
    /// Progress mirrors <c>UIScenarioBattleGoalProgress</c>:
    /// <c>BattleGoalConditionState.CurrentProgress / TotalConditionsAndTargets</c>.
    ///
    /// SECRECY (multiplayer): battle goals are secret. The game's own gate
    /// (BattleGoalContainer.Show / ActorStatPanel.cs:566): online, a goal renders ONLY for
    /// actors under my control (<c>!FFSNetwork.IsOnline || actor.IsUnderMyControl</c>) —
    /// mirrored verbatim here, and the current character is always the LOCAL tab-switchable
    /// hand (<c>CardsGameApi.ActiveHand().PlayerActor</c> — CardsHandManager.CurrentHand),
    /// so remote players' goals can never render. Exists in campaign AND guildmaster
    /// (CampaignOnly goal cards are filtered at deal time by the game itself,
    /// CQuestState.RollAndAssignBattleGoals) — no IsCampaign gate. Empty until the player
    /// picks a goal on the scenario intro; updates via the 0.5 s refresh.
    /// </summary>
    private const float QuestRefreshInterval = 0.5f;
    /// <summary>Label rect height as a fraction of the panel width (label-local units).</summary>
    private const float QuestRectHeightFrac = 0.34f;
    /// <summary>Gap between the panel's bottom edge and the label top (fraction of width).</summary>
    private const float QuestGapFrac = 0.03f;

    private static readonly Vector3[] QuestCorners = new Vector3[4];
    private static bool s_questErrorLogged;

    private GameObject? _questGo;
    private TextMeshPro? _questTmp;
    private float _nextQuestRefresh;
    private string _questShown = "";

    public override void Tick()
    {
        bool wasConverted = Panel != null;
        base.Tick();
        if (Panel != null)
        {
            ApplyContentWidth(); // the 'Breite' dial's real lever — see ApplyContentWidth
            LogWidthVerification();
        }
        else if (wasConverted)
        {
            // Released this tick: the host is gone but the game's rows are alive again in their
            // 2D home — hand them their authored horizontal layout back before we lose the records.
            RestoreContentWidth();
        }
        TickQuestLabel();
    }

    public override void Shutdown()
    {
        RestoreContentWidth(); // before base releases the panel (the rows are still alive here)
        if (_questGo != null)
            Object.Destroy(_questGo);
        _questGo = null;
        _questTmp = null;
        _questShown = "";
        base.Shutdown();
    }

    private void TickQuestLabel()
    {
        bool panelVisible = Panel != null && Panel.HostGo != null && Panel.HostGo.activeInHierarchy;
        if (!panelVisible)
        {
            if (_questGo != null && _questGo.activeSelf)
                _questGo.SetActive(false); // hides with the tray/panel, like the dock itself
            return;
        }

        if (Time.unscaledTime >= _nextQuestRefresh)
        {
            _nextQuestRefresh = Time.unscaledTime + QuestRefreshInterval;
            string text = BuildQuestText();
            if (text.Length > 0)
            {
                bool fresh = EnsureQuestLabel(); // rebuilds after scene unloads (Unity-null aware)
                if (fresh || text != _questShown)
                    _questTmp!.text = text;
            }
            _questShown = text;
        }

        bool show = _questShown.Length > 0 && _questGo != null;
        if (_questGo != null && _questGo.activeSelf != show)
            _questGo.SetActive(show);
        if (!show)
            return;

        // Pose-follow: anchored below the host's world rect (the exact plane the converted
        // objectives render on), sized proportional to the panel width so it rides tray
        // grabs/resizes and diorama zoom for free.
        Panel!.HostRect.GetWorldCorners(QuestCorners); // 0=BL, 1=TL, 2=TR, 3=BR
        Vector3 bl = QuestCorners[0];
        float width = (QuestCorners[3] - bl).magnitude;
        if (width < 1e-4f)
            return; // not laid out yet
        Vector3 up = (QuestCorners[1] - bl).normalized;
        Vector3 bottomCenter = (bl + QuestCorners[3]) * 0.5f;
        Transform t = _questGo!.transform;
        t.rotation = Panel.HostTransform.rotation;
        t.localScale = Vector3.one * width;
        t.position = bottomCenter - up * (width * (QuestGapFrac + QuestRectHeightFrac * 0.5f));
    }

    /// <summary>Build (or rebuild after a scene unload) the quest TMP label. True when fresh.</summary>
    private bool EnsureQuestLabel()
    {
        if (_questGo != null && _questTmp != null)
            return false;
        if (_questGo != null)
            Object.Destroy(_questGo); // half-built remnant — never expected, but never leak
        _questGo = new GameObject("GloomhavenVR.BattleGoal");
        _questTmp = _questGo.AddComponent<TextMeshPro>();
        _questTmp.alignment = TextAlignmentOptions.Top;
        _questTmp.color = new Color(0.92f, 0.88f, 0.76f);
        NativeButtonSkin.ApplyFont(_questTmp); // native HUD font, like the pile captions
        // Label-local units: scale = panel width, so 0.96 ≈ full panel width; auto-size
        // shrinks/wraps long localized strings inside the block (TmpFit policy).
        TmpFit.Fit(_questTmp, 0.96f, QuestRectHeightFrac, maxFontSize: 0.65f);
        VRLayers.Apply(_questGo);
        return true;
    }

    /// <summary>
    /// Completion-state colors, matching the read of the game's own battle-goal tracker
    /// (UIScenarioBattleGoalProgress: green check when completed, red X when failed).
    /// </summary>
    private const string CheckColorHex = "#58C24E"; // green — completedCheck equivalent
    private const string CrossColorHex = "#D9453C"; // red — failedCheck equivalent

    /// <summary>
    /// Completion glyph for the requirement line, mirroring the game's char-info tracker
    /// (<c>UIScenarioBattleGoalProgress.RefreshState</c>/<c>UpdateProgress</c> — the
    /// green <c>completedCheck</c> / red <c>failedCheck</c> objects) as colored TMP text:
    /// - <c>Failed</c> (the goal's fail condition tripped) → red ✗;
    /// - fulfilled → green ✓. Fulfilled mirrors the game's two paths exactly: a
    ///   single-condition goal (total ≤ 1) completes the moment progress reaches the
    ///   total; a multi-condition goal additionally requires a POSITIVE condition
    ///   (the game's OnCompletedProgress listener refuses NegativeCondition goals,
    ///   which only "complete" by surviving to scenario end);
    /// - otherwise (in progress) → no glyph, the "(x/y)" progress already narrates.
    /// Returns a trailing-space-suffixed rich-text prefix or "".
    /// </summary>
    private string QuestStateGlyph(CUnlockConditionState? cond)
    {
        if (cond == null)
            return "";
        int total = cond.TotalConditionsAndTargets;
        int current = cond.CurrentProgress;
        if (cond.Failed)
            return $"<color={CrossColorHex}>{PickGlyph('✗', 'X')}</color> ";
        bool fulfilled = total > 0 && current >= total && (total <= 1 || !cond.NegativeCondition);
        return fulfilled ? $"<color={CheckColorHex}>{PickGlyph('✓', '+')}</color> " : "";
    }

    /// <summary>
    /// The preferred glyph when the label's font (incl. fallbacks) can render it, else the
    /// ASCII stand-in — a missing glyph would draw as a hollow box. ✓ is proven on hardware
    /// in this same harvested HUD font (the tray's "✓ READY" label); ✗ gets the same check.
    /// Unresolvable while the label/font is still building → stand-in; the 0.5 s refresh
    /// re-evaluates and the changed string re-applies automatically.
    /// </summary>
    private char PickGlyph(char preferred, char fallback)
    {
        TMP_FontAsset? font = _questTmp != null ? _questTmp.font : null;
        try
        {
            return font != null && font.HasCharacter(preferred, searchFallbacks: true)
                ? preferred
                : fallback;
        }
        catch (System.Exception)
        {
            return fallback; // font asset mid-teardown — never break the quest line over a glyph
        }
    }

    /// <summary>
    /// "title\n[✓/✗ ]requirement (progress/target)" for the current character's chosen SECRET
    /// BATTLE GOAL, or "" (hidden): no running quest / no goal chosen yet / remote actor
    /// online (secrecy gate — see the class doc). The ✓ (green) / ✗ (red) completion glyph
    /// mirrors the game's own char-info tracker — see <see cref="QuestStateGlyph"/>.
    /// Guarded — a game-side surprise must
    /// never starve the WorldUI tick (the unguarded-Update lesson); failures log once
    /// and render nothing.
    /// </summary>
    private string BuildQuestText()
    {
        try
        {
            CardsHandUI? hand = CardsGameApi.ActiveHand();
            CPlayerActor? actor = hand != null ? hand.PlayerActor : null;
            if (actor == null || actor.Class == null)
                return "";
            var mapState = AdventureState.MapState;
            MapRuleLibrary.MapState.CQuestState? questState =
                mapState != null ? mapState.InProgressQuestState : null;
            if (questState == null)
                return ""; // no running quest (level editor / pre-scenario) — nothing dealt
            // SECRECY (the game's own gate, BattleGoalContainer.Show / ActorStatPanel.cs:566):
            // online, a battle goal is shown ONLY for actors under my control. ActiveHand is
            // the local player's hand, so this is belt-and-braces — but the game enforces it
            // this exact way and so do we.
            if (FFSNetwork.IsOnline && !actor.IsUnderMyControl)
                return "";
            CBattleGoalState? goal = questState.GetChosenBattleGoal(actor.Class.ID);
            var data = goal != null ? goal.BattleGoal : null;
            if (goal == null || data == null)
                return ""; // not chosen yet (intro picker still open) — hidden like the game's tracker
            string title = Loc.Game(data.LocalisedName, "Battle goal");
            string body = Loc.Game(data.LocalisedDescription, "");
            // Progress the way the game's UIScenarioBattleGoalProgress renders it.
            var cond = goal.BattleGoalConditionState;
            if (cond != null && cond.TotalConditionsAndTargets > 0)
            {
                string progress = $"{cond.CurrentProgress}/{cond.TotalConditionsAndTargets}";
                body = body.Length > 0 ? body + "  (" + progress + ")" : "(" + progress + ")";
            }
            // Green ✓ / red ✗ before the requirement line, exactly like the char-info panel
            // (updates live: the 0.5 s refresh re-derives the state and re-applies on change).
            string glyph = QuestStateGlyph(cond);
            if (glyph.Length > 0)
                body = body.Length > 0 ? glyph + body : glyph.TrimEnd();
            return body.Length > 0 ? title + "\n" + body : title;
        }
        catch (System.Exception ex)
        {
            if (!s_questErrorLogged)
            {
                s_questErrorLogged = true;
                VRLog.Warn("WorldUI", $"Battle-goal line unavailable ({ex.Message}) — hidden.");
            }
            return "";
        }
    }
}

/// <summary>
/// SELECTION-PHASE "who still has to choose" cue, rendered ON THE INITIATIVE ORDER BAR (the
/// <see cref="InitiativeTrackSurface"/> docked to the control-board tray) instead of on the board
/// minis. A small amber RING pulses AROUND the portrait of every actor the local player controls
/// that has not yet finished card selection, so a glance at the initiative bar shows which
/// characters still need choosing. The instant an actor commits (two cards / long rest) its ring
/// clears, and the whole set clears when the selection phase ends.
///
/// WHY A FRAME AROUND THE PORTRAIT, NOT A FILL OVER IT (user feedback): the first cut washed a flat
/// amber field over the whole initiative entry and the user saw NOTHING and disliked the "flat area
/// that doesn't fit". Two things sank it: it was parented to the <c>InitiativeTrackActorBehaviour</c>
/// (the layout/click NODE), whose RectTransform is not the visible avatar rect — a fill+few-px
/// overlay on it lands off the actual portrait art — and a low-alpha wash reads as nothing anyway.
/// This version attaches a hollow, 9-sliced amber border sprite as a child of the AVATAR PORTRAIT
/// itself (<c>InitiativeTrackActorAvatar.m_AvatarImage</c>, the <c>RawImage</c> the game draws the
/// character face on), sized to the portrait rect + an outset, so the glow sits in the margin band
/// straddling the portrait edge — visibly framing the face rather than covering it, and correctly
/// placed/scaled because it inherits the portrait's own rect, depth, and layer.
///
/// The initiative bar is the game's OWN <c>InitiativeTrack</c> (adopted into world space by
/// <see cref="InitiativeTrackSurface"/>, not copied); each actor's entry is the
/// <c>InitiativeTrackActorBehaviour</c> resolved by the game's public
/// <c>InitiativeTrack.FindInitiativeTrackActor(actor)</c> (its <c>.Actor</c> is the very
/// <c>CPlayerActor</c> from the scenario, so the match is reference-exact). The ring:
/// <list type="bullet">
/// <item>has <c>raycastTarget = false</c>, so it never intercepts the portrait's character-switch
///   click (the whole point of the pokeable track);</item>
/// <item>is a hollow (fillCenter=false) 9-sliced border — a thin amber outline in the portrait's
///   margin — so the face stays fully visible; it breathes (alpha + a subtle scale pulse) on
///   <c>Time.unscaledTime</c> so it keeps animating while the game is time-paused during the
///   selection camera move;</item>
/// <item>is a nested descendant of the portrait (not a direct <c>initiativeTrackHolder</c> child) at
///   local z 0, so it is invisible to <see cref="InitiativeTrackSurface"/>'s per-portrait depth
///   normalization (z==0 ⇒ ignored) and depth-aware laser pick (which only scans direct holder
///   children), while inheriting the portrait's compressed depth for correct occlusion;</item>
/// <item>copies the portrait's CURRENT layer every tick, so it rides the surface's mod-layer
///   re-layering across convert/release cycles and renders on exactly the cameras the portrait does.</item>
/// </list>
/// Entries are pooled/reused by the game, so we re-resolve pending → entry every tick and hide any
/// ring whose entry is no longer pending; rings are kept (deactivated) for cheap reuse and only
/// destroyed on <see cref="Reset"/> (module hot-reload).
/// </summary>
internal static class InitiativeSelectionGlow
{
    /// <summary>Warm amber "still waiting" ring; only alpha (× the sprite's own falloff) is tinted.</summary>
    private static readonly Color GlowColor = new Color(1f, 0.72f, 0.20f);

    /// <summary>Ring outset (uGUI px) past the portrait rect, so the frame sits in the margin band.</summary>
    private const float OutsetPixels = 8f;

    /// <summary>Peak of the breathing scale pulse (1 = portrait+outset size). Subtle, in-theme.</summary>
    private const float ScalePulse = 0.05f;

    /// <summary>Breaths per second of the ring's scale pulse (matches the driver's alpha period).</summary>
    private const float PulseHz = 1f / 1.5f;

    // Ring Image per entry we have ever lit (entry may be Unity-destroyed on scene change → pruned).
    private static readonly Dictionary<InitiativeTrackActorBehaviour, Image> s_rings = new(16);
    private static readonly HashSet<InitiativeTrackActorBehaviour> s_active = new();
    private static readonly List<InitiativeTrackActorBehaviour> s_stale = new(16);

    /// <summary>Shared generated ring sprite (hollow 9-sliced amber border); built once, reused for all.</summary>
    private static Sprite? s_ringSprite;

    // One-shot on-screen diagnostic per entry so a future "I see no ring" is answerable from the log.
    private static readonly HashSet<InitiativeTrackActorBehaviour> s_diagLogged = new();
    private static readonly Vector3[] s_diagCorners = new Vector3[4];

    /// <summary>
    /// Pulse the ring around every pending actor's portrait at <paramref name="alpha"/>, and hide the
    /// ring on every entry that is no longer pending. Safe to call with an empty list (hides
    /// everything). No-op-safe when the track is not built yet.
    /// </summary>
    public static void Apply(List<CPlayerActor> pending, float alpha)
    {
        s_active.Clear();
        // Breathing scale, in phase with the driver's alpha pulse; unscaledTime so it animates even
        // while TimeManager is paused during the selection camera move.
        float breath = 1f + ScalePulse * (Mathf.Sin(Time.unscaledTime * (2f * Mathf.PI * PulseHz)) + 1f) * 0.5f;

        InitiativeTrack track = InitiativeTrack.Instance;
        if (track != null && pending != null)
        {
            for (int i = 0; i < pending.Count; i++)
            {
                CPlayerActor actor = pending[i];
                if (actor == null)
                    continue;
                InitiativeTrackActorBehaviour entry = track.FindInitiativeTrackActor(actor);
                if (entry == null || !entry.gameObject.activeInHierarchy)
                    continue; // entry not spawned / not on screen — light nothing this frame

                Image ring = EnsureRing(entry);
                if (ring == null)
                    continue; // portrait not resolvable yet — retry next tick
                s_active.Add(entry);

                // Ride the portrait's current layer (mod layer when the surface is converted).
                GameObject go = ring.gameObject;
                int layer = ring.transform.parent != null ? ring.transform.parent.gameObject.layer : go.layer;
                if (go.layer != layer)
                    go.layer = layer;

                Color c = GlowColor;
                c.a = alpha;
                ring.color = c;
                ring.transform.localScale = new Vector3(breath, breath, 1f);
                if (!go.activeSelf)
                    go.SetActive(true);

                LogRingDiagOnce(entry, ring);
            }
        }

        // Deactivate rings whose entry is no longer pending (committed, reassigned by pooling,
        // exhausted, or gone). Prune entries the game has since destroyed.
        s_stale.Clear();
        foreach (KeyValuePair<InitiativeTrackActorBehaviour, Image> kv in s_rings)
        {
            if (kv.Key == null || kv.Value == null || !s_active.Contains(kv.Key))
                s_stale.Add(kv.Key!); // Unity fake-null (destroyed entry) is a real ref, never null
        }
        for (int i = 0; i < s_stale.Count; i++)
        {
            InitiativeTrackActorBehaviour entry = s_stale[i];
            if (entry == null || !s_rings.TryGetValue(entry, out Image ring) || ring == null)
            {
                s_rings.Remove(entry!);
                continue;
            }
            if (ring.gameObject.activeSelf)
                ring.gameObject.SetActive(false);
        }
    }

    /// <summary>Hide every ring (phase exit / feature off). Rings are kept for cheap reuse.</summary>
    public static void ClearAll()
    {
        if (s_rings.Count == 0)
            return;
        foreach (Image ring in s_rings.Values)
        {
            if (ring != null && ring.gameObject.activeSelf)
                ring.gameObject.SetActive(false);
        }
    }

    /// <summary>Destroy every ring GameObject and forget them (module shutdown / hot-reload).</summary>
    public static void Reset()
    {
        foreach (Image ring in s_rings.Values)
        {
            if (ring != null)
                Object.Destroy(ring.gameObject);
        }
        s_rings.Clear();
        s_active.Clear();
        s_stale.Clear();
        s_diagLogged.Clear();
    }

    /// <summary>
    /// Resolve the actual VISIBLE portrait rect of an entry — the avatar face
    /// (<c>InitiativeTrackActorAvatar.m_AvatarImage</c>, a <c>RawImage</c>). We take the first active
    /// RawImage under the entry's <c>Avatar</c> (the character face is the avatar's only RawImage;
    /// every other graphic — initiative digit, name, selection frame — is a TMP/Image). Returns null
    /// while the avatar has not been pooled in yet.
    /// </summary>
    private static RectTransform? FindPortraitRect(InitiativeTrackActorBehaviour entry)
    {
        InitiativeTrackActorAvatar avatar = entry.Avatar;
        if (avatar == null)
            return null;
        RawImage[] raws = avatar.GetComponentsInChildren<RawImage>(includeInactive: false);
        for (int i = 0; i < raws.Length; i++)
        {
            RawImage r = raws[i];
            if (r != null && r.transform is RectTransform rt)
                return rt;
        }
        return null;
    }

    /// <summary>Lazily build (or rebuild after a scene-swap destroyed it) the entry's portrait ring.</summary>
    private static Image EnsureRing(InitiativeTrackActorBehaviour entry)
    {
        if (s_rings.TryGetValue(entry, out Image existing))
        {
            if (existing != null && existing.transform.parent != null)
                return existing;
            s_rings.Remove(entry); // destroyed / re-pooled with the old track — rebuild below
        }

        RectTransform? portrait = FindPortraitRect(entry);
        if (portrait == null)
            return null!; // avatar face not laid out yet — caller retries next tick

        var go = new GameObject("GloomhavenVR.SelectionRing", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(portrait, worldPositionStays: false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(-OutsetPixels, -OutsetPixels);
        rt.offsetMax = new Vector2(OutsetPixels, OutsetPixels);
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
        rt.localPosition = new Vector3(rt.localPosition.x, rt.localPosition.y, 0f); // z==0 ⇒ ignored by depth passes
        rt.SetAsLastSibling(); // draw after the face — the ring lives in the margin, nothing occludes it

        var img = go.GetComponent<Image>();
        img.sprite = GetRingSprite();
        img.type = Image.Type.Sliced;
        img.fillCenter = false; // hollow — a frame, never a wash over the face
        img.color = GlowColor;
        img.raycastTarget = false; // MUST NOT eat the portrait's character-switch click
        go.layer = portrait.gameObject.layer;
        go.SetActive(false); // Apply activates it this same tick

        s_rings[entry] = img;
        return img;
    }

    /// <summary>
    /// Generate the shared hollow ring sprite once: a soft amber OUTLINE that fades from a bright core
    /// near the portrait edge inward to nothing, authored so it 9-slices cleanly (the falloff lives
    /// entirely inside the sprite border, the stretched center is fully transparent). White pixels —
    /// the amber comes from <see cref="GlowColor"/> tinting the Image.
    /// </summary>
    private static Sprite GetRingSprite()
    {
        if (s_ringSprite != null)
            return s_ringSprite;

        const int size = 48;
        const int border = 16; // 9-slice margin (px) — the whole glow falloff fits inside it
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false, linear: false)
        {
            name = "GloomhavenVR.SelectionRingTex",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            int dyEdge = Mathf.Min(y, size - 1 - y);
            for (int x = 0; x < size; x++)
            {
                int dxEdge = Mathf.Min(x, size - 1 - x);
                float d = Mathf.Min(dxEdge, dyEdge); // px to the nearest outer edge
                float a;
                if (d < 2f)
                    a = d / 2f;                       // soft outer lip
                else if (d <= 6f)
                    a = 1f;                           // bright outline core
                else if (d < border)
                {
                    float t = (d - 6f) / (border - 6f); // fade inward to transparent
                    a = (1f - t) * (1f - t);
                }
                else
                    a = 0f;                           // transparent center (stretched by 9-slice)
                px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255));
            }
        }
        tex.SetPixels32(px);
        tex.Apply(updateMipmaps: false);

        // ppu 100 == the uGUI reference, so the border strips render ~border px thick in UI space.
        s_ringSprite = Sprite.Create(
            tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f),
            100f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        s_ringSprite.name = "GloomhavenVR.SelectionRing";
        return s_ringSprite;
    }

    /// <summary>
    /// One line per entry the first time its ring lights: the resolved portrait, its world-space rect
    /// size, and the layer it renders on — so a "the ring is invisible" report is diagnosable from the
    /// hardware log alone (Info level: BepInEx's default disk config drops Debug). Never throws.
    /// </summary>
    private static void LogRingDiagOnce(InitiativeTrackActorBehaviour entry, Image ring)
    {
        if (!s_diagLogged.Add(entry))
            return;
        try
        {
            if (ring.transform is not RectTransform rt)
                return;
            rt.GetWorldCorners(s_diagCorners); // 0=BL,1=TL,2=TR,3=BR
            float w = (s_diagCorners[3] - s_diagCorners[0]).magnitude;
            float h = (s_diagCorners[1] - s_diagCorners[0]).magnitude;
            string portrait = rt.parent != null ? rt.parent.name : "?";
            VRLog.Info("Board", $"[SelectionReady] ring on portrait '{portrait}' (entry '{entry.name}'): " +
                                $"world {w:F3}x{h:F3} m, layer {LayerMask.LayerToName(ring.gameObject.layer)}.");
        }
        catch (System.Exception ex)
        {
            VRLog.Warn("Board", $"[SelectionReady] ring diag skipped ({ex.Message}).");
        }
    }
}
