using GloomhavenVR.Board.FigureGrab;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.Surfaces;

/// <summary>
/// Passive hover prop-info cards as a small world panel (hardware test #18): the
/// game's <c>UITextInfoPanel</c> ('Text Info Panel', ID TextInfoPanel — closed
/// doors, chests, pressure plates…) and <c>UIPropInfoPanel</c> (trap / hazardous
/// terrain / difficult terrain / carryable quest item card) are HOVER-driven info
/// popups, not dialogs.
///
/// Verified in the decompiled sources:
/// <code>
///   // decompiled/GH.Runtime/UITextInfoPanel.cs:58-86 — Show(params (title,
///   //   description)[]) / Hide() only toggle the UIWindow; no buttons, no
///   //   blockers, no escape action.
///   // decompiled/GH.Runtime/UIPropInfoPanel.cs:83-264 — ShowTrap/
///   //   ShowHazardousTerrain/ShowQuestItem/ShowDifficultTerrain viewers only.
///   // decompiled/GH.Runtime/WorldspaceStarHexDisplay.cs:408/484 — Update() →
///   //   DisplayCursorHoverStar() → ShowTooltipForTile (:3370): Hide() both
///   //   panels on every hover change (:3574-3575, :3227), Show on prop hover
///   //   (:3602/3607), TryReset() when the hover carries no prop info (:3612).
/// </code>
/// The game therefore hides these panels ITSELF the moment the hover leaves — as
/// long as board picking keeps running. That is exactly why they must never assert
/// ModalUI (see the test-#18 postmortem in <see cref="WorldUI.ModalFallback"/>):
/// ModalUI stops the pick injection, the hover never changes, the hide path never
/// runs, and the panel holds the mode machine hostage forever.
///
/// Presentation (the test-#16 stat-panel treatment, <see cref="StatPanelSurface"/>):
/// - Converted NON-pokeable: never registered in UguiPokeSurfaces, so neither the
///   far ray nor the fingertip poke sees it and IsPointerOverUI can never go true
///   because of it; the host GraphicRaycaster is kept disabled so the vanilla
///   EventSystem ignores it too.
/// - Docked low in view at the <see cref="PanelSlot.PropInfo"/> layout slot (near
///   the player edge, opposite side of the stat panel) — out of the board-hover
///   ray path so it cannot self-occlude the hover that keeps it alive.
/// - Release HYSTERESIS + churn telemetry exactly like the stat panels: the game
///   hides/re-shows liberally while the pointer sweeps the board.
/// - MIP BAKE (<see cref="PanelMipBake"/>), added 2026-08 for "Die Linien und Rahmen auf
///   allen Karten und den Gegnerinfos haben wieder starkes Aliasing": this surface is a
///   world-space quad showing REAL game widgets that sample the game's MIPLESS UI atlases,
///   i.e. the identical texture-space aliasing the card faces and the initiative track were
///   already fixed for — it had simply never been wired to the bake. Rescan on conversion
///   and on <see cref="MipRescanInterval"/> (the release hysteresis keeps one host alive
///   across hover changes, so the panel's CONTENT swaps without a reconversion), restore
///   before release.
/// </summary>
internal sealed class PropInfoSurface
{
    /// <summary>Hide→release hysteresis (unscaled seconds) — absorbs show/hide flicker.</summary>
    private const float ReleaseDelaySeconds = 0.3f;

    /// <summary>Churn telemetry: rolling window for the conversion counter (unscaled seconds).</summary>
    private const float ChurnWindowSeconds = 2f;

    /// <summary>Conversions inside one window above which the single churn warning fires.</summary>
    private const int ChurnWarnCount = 5;

    private sealed class Watch
    {
        public Component? Attached;
        public UIWindow? Window;
        public ConvertedPanel? Panel;
        public bool PendingShow;

        /// <summary>True for the <c>UITextInfoPanel</c> watch, false for the <c>UIPropInfoPanel</c>
        /// one.
        ///
        /// <para>Until ModBuild 366 this flag also answered "may this watch dock at a hand?",
        /// because <c>UITextInfoPanel</c> was the ONE window a held prop could raise. ModBuild 366
        /// moved the held card to the RICH window whenever the prop has one (the user wants "immer
        /// die detaillierteste Info ... inkl. aller effekte"), so the two questions came apart:
        /// this stays the WINDOW discriminator and <c>HeldPropCard.Owns</c> answers the held one.
        /// Leaving the dock keyed on this flag would have fixed the content and lost the
        /// position.</para></summary>
        public bool IsTextInfo;

        /// <summary>Unscaled time at which a scheduled release fires; 0 = none pending.</summary>
        public float ReleaseAt;

        // Churn telemetry (test #16 pattern): conversions inside the rolling window.
        public float CycleWindowStart;
        public int CycleCount;
        public bool ChurnWarned;

        public UnityEngine.Events.UnityAction OnShown = null!;
        public UnityEngine.Events.UnityAction OnHidden = null!;

        /// <summary>Unscaled time of the next mip-bake rescan for this panel (see
        /// <see cref="MipRescanInterval"/>).</summary>
        public float NextMipRescan;
    }

    /// <summary>
    /// Mip-bake rescan cadence while a hover panel is converted (mirrors
    /// <c>CardFace.MipRescanInterval</c> / the initiative track's, halved because the release
    /// HYSTERESIS deliberately keeps ONE converted panel alive across hover changes: the same
    /// host is repopulated with a different prop's icons and frame sprites without a
    /// reconversion, so the scan — not the conversion — is what has to catch the new graphics.
    /// </summary>
    private const float MipRescanInterval = 0.5f;

    private readonly Watch _textInfo = new();
    private readonly Watch _propInfo = new();

    public string Name => "PropInfo";

    public PropInfoSurface()
    {
        _textInfo.IsTextInfo = true;
        _textInfo.OnShown = () => _textInfo.PendingShow = true;
        _textInfo.OnHidden = () => ScheduleRelease(_textInfo);
        _propInfo.OnShown = () => _propInfo.PendingShow = true;
        _propInfo.OnHidden = () => ScheduleRelease(_propInfo);
    }

    public void Tick()
    {
        // Re-arm the one-shot dock line per HOLD, not per session: PlaceWatch only runs while a
        // panel is converted, so it cannot clear the edge on the frame the prop is dropped, and
        // without this the second pickup of the same prop in the same hand would print nothing.
        if (HeldProps.Count == 0)
            _dockedRoute = null;

        TickWatch(_textInfo,
            Singleton<UITextInfoPanel>.IsInitialized ? Singleton<UITextInfoPanel>.Instance : null,
            "TextInfoPanel");
        TickWatch(_propInfo,
            Singleton<UIPropInfoPanel>.IsInitialized ? Singleton<UIPropInfoPanel>.Instance : null,
            "PropInfoPanel");
    }

    private void TickWatch(Watch watch, Component? live, string name)
    {
        if (watch.Panel != null && !watch.Panel.IsAlive)
            watch.Panel = null;

        if (!ReferenceEquals(live, watch.Attached))
        {
            DetachWatch(watch);
            watch.Attached = live;
            watch.Window = live != null ? live.GetComponent<UIWindow>() : null;
            if (watch.Window != null)
            {
                watch.Window.onShown.AddListener(watch.OnShown);
                watch.Window.onHidden.AddListener(watch.OnHidden);
                if (watch.Window.IsOpen)
                    watch.PendingShow = true;
            }
        }

        if (watch.PendingShow)
        {
            watch.PendingShow = false;
            if (watch.Panel != null)
            {
                // Re-shown inside the hysteresis window — keep the live conversion.
                watch.ReleaseAt = 0f;
            }
            // [WorldUI] PropInfoCards is GONE (user ruling 2026-08-13): the hover cards are how a
            // door, chest, trap or quest item names itself, and OFF left them on the hidden 2D
            // stack. The conversion gate is the only gate now.
            else if (WorldUIConfig.ConversionActive
                && watch.Attached != null)
            {
                // Informational panel (no buttons, verified) — NOT pokeable: never in
                // UguiPokeSurfaces, so neither ray nor poke nor IsPointerOverUI see it.
                // flatten2D (test #21): the prop/text info card carries the same baked local-z /
                // local rotation as the stat panels — neutralize it (and re-flatten every frame via
                // LateTick) so hover-card text/icons lie flat instead of protruding in 3D.
                watch.Panel = CanvasConversion.Convert(watch.Attached.transform as RectTransform, name,
                    pokeable: false, flatten2D: true);
                if (watch.Panel != null)
                {
                    // MR BACKING OPT-OUT (user report 2026-08-09, verbatim: "Die fliegenden
                    // Hinweise beim Hovern wie 'Geschlossene Tür' haben in mixed reality auch
                    // einen größeren Hintergrund, da sie nicht transparent sind oder transparente
                    // Stellen haben, brauchen sie das nicht - entferne das dort.")
                    //
                    // These two windows are the game's OWN hover prop cards and they carry their
                    // own fully opaque card art, exactly like the figure-grab stat card that got
                    // this same exclusion on 2026-08-04 (StatPanelSurface, "hier wird das nicht
                    // gebraucht"). MrBacking.TickPanels plates every live ConvertedPanel by
                    // default — deliberately, because under-coverage is the reported bug and
                    // over-coverage is normally invisible behind opaque art. It is NOT invisible
                    // here: the plate is fitted to the HOST RECT (the hardware log reads
                    // "Converted 'TextInfoPanel' to world space (336x200 px)"), which is the
                    // content fit's union of the visible graphics' rectangles and therefore
                    // LARGER than the drawn card, and MrBacking.GlyphTrueRect then grows it
                    // further for any line that renders past that union plus the standard label
                    // margin. The result is the reported dark border proud of the card — "einen
                    // größeren Hintergrund" — added for a card that never had a transparent pixel
                    // to protect in the first place.
                    //
                    // WHICH HINTS THIS EXEMPTS, precisely: the two windows THIS surface converts
                    // and nothing else — UITextInfoPanel ('Geschlossene Tür', '3 Gold', chests,
                    // pressure plates) and UIPropInfoPanel (trap / hazardous terrain / difficult
                    // terrain / carryable quest item). The test is STRUCTURAL, not name-based:
                    // the flag is written by the owning surface on the panel it just converted,
                    // so no other panel family can ever be caught by it, and every genuinely bare
                    // free-floating label (MrBacking.Label registrants) keeps its plate untouched.
                    // Per conversion, like every other flag here: the hover show/hide hysteresis
                    // re-converts these windows constantly and each fresh ConvertedPanel needs it.
                    watch.Panel.MrBackingSuppressed = true;
                    CountConversion(watch, name);
                    PlaceWatch(watch);
                    // MIP BAKE (user report 2026-08: "Die Linien und Rahmen auf allen Karten und
                    // den Gegnerinfos haben wieder starkes Aliasing"). These info cards are REAL
                    // game widgets reparented onto a world-space host, so they sample the game's
                    // MIPLESS UI atlases exactly like the card faces did — and unlike the
                    // initiative track and the tooltip box, this surface never ran the bake at
                    // all. Immediate pass on conversion + the cadence below for the async /
                    // hover-swapped content.
                    RescanMips(watch, name);
                }
            }
        }

        // Deferred release (hysteresis): the window stayed hidden past the delay.
        if (watch.Panel != null && watch.ReleaseAt > 0f && Time.unscaledTime >= watch.ReleaseAt)
            Release(watch);

        if (watch.Panel != null)
        {
            // The lock mirror in CanvasConversion.Tick may re-enable host raycasters
            // wholesale — keep this one dark so the vanilla EventSystem never hits it.
            if (watch.Panel.HostRaycaster != null && watch.Panel.HostRaycaster.enabled)
                watch.Panel.HostRaycaster.enabled = false;
            PlaceWatch(watch);
            RescanMips(watch, name); // cadence-gated inside; catches hover-swapped / async graphics
        }
    }

    /// <summary>
    /// One cadence-gated mip-bake pass over the converted hover panel. Config-gated and fully
    /// guarded inside <see cref="PanelMipBake.Rescan"/> (a bake surprise can never break the
    /// surface's tick), and idempotent-cheap once warm — a graphic already wearing a baked
    /// sprite resolves to a dictionary hit and is not rewritten.
    /// </summary>
    private static void RescanMips(Watch watch, string name)
    {
        if (watch.Panel == null || watch.Attached == null || Time.unscaledTime < watch.NextMipRescan)
            return;
        watch.NextMipRescan = Time.unscaledTime + MipRescanInterval;
        // Scan the GAME widget root, not the host: it is the exact subtree Convert reparented,
        // and it stays the right root in both states — which is what lets Restore below use the
        // same handle after the content has gone home.
        PanelMipBake.Rescan(watch.Attached, name);
    }

    /// <summary>
    /// Hide → deferred release (test #16 pattern). The game's hover logic hides/
    /// re-shows the panel on every hover change; releasing instantly would re-parent
    /// the whole uGUI subtree at sweep rate. A re-show within the window cancels the
    /// pending release.
    /// </summary>
    private static void ScheduleRelease(Watch watch)
    {
        watch.PendingShow = false;
        if (watch.Panel != null)
            watch.ReleaseAt = Time.unscaledTime + ReleaseDelaySeconds;
    }

    /// <summary>One warning if a panel still churns through conversions.</summary>
    private static void CountConversion(Watch watch, string name)
    {
        float now = Time.unscaledTime;
        if (now - watch.CycleWindowStart > ChurnWindowSeconds)
        {
            watch.CycleWindowStart = now;
            watch.CycleCount = 0;
        }
        watch.CycleCount++;
        if (watch.CycleCount > ChurnWarnCount && !watch.ChurnWarned)
        {
            watch.ChurnWarned = true;
            VRLog.Warn("WorldUI", $"{name} convert/release churn: >{ChurnWarnCount} conversions in " +
                                  $"{ChurnWindowSeconds:F0}s despite the {ReleaseDelaySeconds:F1}s release " +
                                  "hysteresis — something still occludes/toggles the window per frame.");
        }
    }

    /// <summary>
    /// Dock at the PropInfo layout slot (low in view, near the player edge), sized by the
    /// user's live "Infotafel-Größe" dial.
    ///
    /// The 0.6 that used to be hard-coded here is now the DEFAULT of
    /// <see cref="WorldUIConfig.HoverInfoScale"/> (<see cref="WorldUIConfig.DefaultHoverInfoScale"/>),
    /// so the factory value reproduces the previous size exactly. It is read HERE, on every
    /// placement tick (this method runs per frame while a panel is converted — see
    /// <see cref="TickWatch"/>), instead of being captured at conversion time: that is what makes
    /// the stepper apply LIVE to an ALREADY SHOWN hover card, not just to the next hover.
    /// <see cref="CanvasConversion.PlaceHost"/> multiplies it into the host's uniform localScale
    /// (metres-per-pixel × this), so the whole panel — frame, text, icons — zooms as one; nothing
    /// re-wraps and no rect is rewritten, which keeps the mutation trivially reversible on release
    /// and keeps the card readable at any board scale / distance.
    /// </summary>
    private static void PlaceWatch(Watch watch)
    {
        if (watch.Panel == null)
            return;

        // HELD-PROP DOCK FIRST (ModBuild 360). User, 2026-09-03, verbatim: "Die Info bei den props
        // in der Hand folgt aktuell dem Kopf - das soll nicht sein - es soll sich wenn man es in
        // der Hand haelt genau so verhalten wie die Figur-Info neben der Figur wenn man die Figur
        // in der Hand hat, also der Figur daneben folgen! Die Info die dem Kopf folgt ist nur beim
        // Laser-hover."
        //
        // Same three-way shape StatPanelSurface.PlaceWatch has had since P8: held branch first,
        // then the anchored branch, then the fixed slot. This surface has no board-cell branch (a
        // hover card names whatever the pointer is on, and the pointer is the head's business), so
        // it is a two-way selector: held, else the slot.
        //
        // THE GATE IS "IS THIS THE HELD CARD?", NOT "IS THIS THE TEXT PANEL?" (ModBuild 366). Those
        // were the same question only while a held prop could raise nothing but UITextInfoPanel;
        // 364 gives a trap in the hand the RICH UIPropInfoPanel so it keeps its effect rows, and a
        // dock still keyed on the window would have put the card back in front of the head the
        // moment the content got better. HeldPropCard.Owns answers the real question, and the OTHER
        // window - the one the laser hover is driving - still takes the fixed slot on the same
        // frame, because only one of the two watches can be the held card at a time.
        if (Board.FigureGrab.HeldPropCard.Owns(watch.IsTextInfo)
            && TryGetHeldDockPose(out Vector3 heldPos, out Quaternion heldRot,
                                  out string route, out HandSide dockSide))
        {
            // The user's live "Infotafel-Groesse" dial still owns the size, exactly as in the
            // docked case - the hold changes WHERE the card is, never how big it is.
            CanvasConversion.PlaceHost(watch.Panel, heldPos, heldRot,
                PanelLayout.WorldScale * WorldUIConfig.HoverInfoScaleLive());
            ReportDock(route, dockSide);
            return;
        }

        if (PanelLayout.TryGetPose(PanelSlot.PropInfo, out Vector3 pos, out Quaternion rot))
            CanvasConversion.PlaceHost(watch.Panel, pos, rot,
                PanelLayout.WorldScale * WorldUIConfig.HoverInfoScaleLive());
    }

    // ---- held-prop dock (ModBuild 360) ------------------------------------------------
    //
    // THE ASYMMETRY THIS ENDS. The FIGURE card has a held mode (StatPanelSurface.ShowHeldFigure to
    // TryComputeHeldPose) and no head-follower. The PROP card had a head-follower
    // (WorldUI/Tooltips/HexHintFacing) and no held mode - and both the laser hover and a prop in
    // the hand drive the SAME window, the UITextInfoPanel singleton (GrabbableProp.PushInfo).
    // HexHintFacing gates only on UIWindow.IsVisible, so it could not tell the two apart and drove
    // the held card to the centre of view. The missing term is not in the follower, it is here:
    // this surface never had a concept of a held prop at all.

    /// <summary>
    /// Explicit held-prop registration - the ONE-LINE HOOK for the grab side, mirroring
    /// <c>StatPanelSurface.ShowHeldFigure</c>. <paramref name="anchor"/> must be the prop's LIVE
    /// visual transform (never a captured position: riding the object is the whole point), and
    /// <paramref name="holdingHand"/> picks the viewer side so the holding hand never occludes its
    /// own card.
    ///
    /// <para><b>THE DOCK DOES NOT DEPEND ON THIS CALL.</b> <c>Board/FigureGrab/GrabbableProp.cs</c>
    /// is owned by another lane, so a fix that only worked once that file called us would be a
    /// remedy gated behind a change we cannot land - this project has already paid for one of
    /// those. <see cref="TryGetHeldDockPose"/> therefore resolves the anchor from the shared
    /// <see cref="HeldProps"/> registry on its own and works today; this method exists so the grab
    /// side can hand us the exact visual instead of us finding it, and it is strictly a fidelity
    /// upgrade. The patch text is in <c>.planning/LANE-PROPINFO-357-NEEDED-OUTSIDE.md</c>.</para>
    /// </summary>
    internal static void ShowHeldProp(Transform? anchor, HandSide holdingHand)
    {
        if (anchor == null)
            return;
        // ModBuild 404: one registration PER HAND; the most recent grab is the one the card
        // docks beside ("neben der Hand die zuletzt ein prop genommen hat").
        _regAnchors[(int)holdingHand] = anchor;
        _regStamp[(int)holdingHand] = ++_regSerial;
    }

    /// <summary>Drop an explicit registration (release / teardown). Idempotent, and a no-op for a
    /// hand that never registered. The registry-driven resolution below keeps working either
    /// way.</summary>
    internal static void ClearHeldProp(HandSide holdingHand)
    {
        _regAnchors[(int)holdingHand] = null;
        _regStamp[(int)holdingHand] = 0;
    }

    private static readonly Transform?[] _regAnchors = new Transform?[2];
    private static readonly long[] _regStamp = new long[2];
    private static long _regSerial;

    /// <summary>Edge state for the one-shot dock line - the route and hand we last reported
    /// docking by, or null while no held card is up. Kept as two fields rather than one composed
    /// key because <see cref="ReportDock"/> is on a per-frame path and composing a key there would
    /// allocate a string every frame of every hold.</summary>
    private static string? _dockedRoute;
    private static HandSide _dockedSide;

    /// <summary>
    /// TRUE while a prop in the hand owns the text-info card, with the pose it must take.
    ///
    /// <para>The gate is <see cref="HeldProps.Count"/> AND the same
    /// <c>[FigureGrab] HeldFigureInfo</c> dial <c>GrabbableProp.ShowInfo</c> obeys: with that dial
    /// off nothing pushes a held card at all, so the window is a pure hover panel and must keep the
    /// head-follow. (Known residual: with a prop in one hand, the OTHER hand's laser can still
    /// hover a hex and repopulate this same singleton. WHICH TEXT wins that race is decided by
    /// GrabbableProp's 0.25 s re-assert, not here; this method follows the content by docking
    /// wherever the held prop is, which is what the re-assert makes true most of the time.)</para>
    ///
    /// <para>The pose itself is <c>StatPanelSurface.TryComputeHeldPose</c> - CALLED, not copied, so
    /// "genau so wie die Figur-Info" survives the next time those offsets are tuned.</para>
    /// </summary>
    /// <remarks><paramref name="route"/> is one of a handful of CONSTANT literals naming how the
    /// anchor was found, never a composed sentence: this runs every frame of every hold from both
    /// <see cref="PlaceWatch"/> and <c>HexHintFacing.LateTick</c>, and a formatted string here
    /// would be a per-frame allocation on a VR hot path. The sentence is composed once, on the
    /// edge, in <see cref="ReportDock"/>.</remarks>
    internal static bool TryGetHeldDockPose(out Vector3 pos, out Quaternion rot,
                                            out string route, out HandSide side)
    {
        pos = default;
        rot = Quaternion.identity;
        route = "no held prop";
        side = HandSide.Right;
        if (HeldProps.Count == 0 || !FigureGrabConfig.HeldFigureInfoEnabled)
            return false;
        if (!TryResolveHeldAnchor(out Transform? anchor, out side, out route) || anchor == null)
            return false;
        if (!StatPanelSurface.TryComputeHeldPose(anchor.position, StatPanelSurface.SignFor(side),
                                                 out pos, out rot))
        {
            route = "no head camera";
            return false;
        }
        return true;
    }

    /// <summary>
    /// The live transform the held card must ride, in preference order.
    ///
    /// <para>1. An explicit <see cref="ShowHeldProp"/> registration, when the grab side has landed
    /// the hook.</para>
    ///
    /// <para>2. Otherwise, resolved from the shared registry with no outside help: the MOST RECENT
    /// held prop (<see cref="HeldProps.TryGetSlot"/> at the last slot - grab order, and the last
    /// grab is the one whose text this singleton is showing) names the hand, the hand names its
    /// <c>Rig.GrabAnchor</c>, and the prop's visual is the child of that anchor which
    /// <see cref="HeldProps.OwnsRendererOf"/> claims. That predicate is exact - it answers "is this
    /// transform, or an ancestor of it, a registered held-prop visual" - so the child it selects IS
    /// the prop, not a guess by name or by layer. A grab anchor carries at most a couple of
    /// children, so the scan is trivial.</para>
    ///
    /// <para>3. If the anchor has no such child (a frame between the reparent and the registry
    /// write, or a prop whose visual sits deeper), the GrabAnchor itself. That is the hand, i.e.
    /// within a few centimetres of the prop - degraded, never wrong, and named as such in the dock
    /// line so a hardware log can tell the two routes apart.</para>
    /// </summary>
    private static bool TryResolveHeldAnchor(out Transform? anchor, out HandSide side, out string how)
    {
        anchor = null;
        side = HandSide.Right;
        how = "unresolved";

        // The newest live registration wins — a prop in each hand docks the card beside the hand
        // that grabbed last, and a release hands it back to the other hand's prop.
        int best = -1;
        for (int i = 0; i < 2; i++)
        {
            if (_regAnchors[i] != null && (best < 0 || _regStamp[i] > _regStamp[best]))
                best = i;
        }
        if (best >= 0)
        {
            anchor = _regAnchors[best];
            side = (HandSide)best;
            how = _regAnchors[1 - best] != null
                ? "registered prop visual (two hands hold a prop; the LATER grab docks the card)"
                : "registered prop visual";
            return true;
        }

        for (int slot = HeldProps.Count - 1; slot >= 0; slot--)
        {
            if (!HeldProps.TryGetSlot(slot, out _, out HandSide s))
                continue;
            VRHand? hand = VRHands.Get(s);
            Transform? grab = hand != null && hand.Rig != null ? hand.Rig.GrabAnchor : null;
            if (grab == null)
                continue;
            side = s;
            for (int i = 0; i < grab.childCount; i++)
            {
                Transform child = grab.GetChild(i);
                if (child == null || !HeldProps.OwnsRendererOf(child))
                    continue;
                anchor = child;
                how = "prop visual under the grab anchor";
                return true;
            }
            anchor = grab;
            how = "grab anchor (prop visual not found under it)";
            return true;
        }
        return false;
    }

    /// <summary>
    /// One line per hold saying WHERE the card docked and by which route - the single fact a
    /// hardware log has to carry for "die Info folgt jetzt dem Prop". Edge-triggered on the route,
    /// so it prints once per pickup and never per frame; its ABSENCE during a hold means the held
    /// branch never ran and the card is still on the fixed slot with the head-follower on it.
    /// </summary>
    private static void ReportDock(string route, HandSide side)
    {
        // Ordinal compare, not ReferenceEquals: the routes ARE interned literals today, but a
        // reference test would silently start logging every frame the day one of them is composed.
        if (_dockedSide == side && string.Equals(_dockedRoute, route, System.StringComparison.Ordinal))
            return;   // steady state: no allocation, no string built
        _dockedRoute = route;
        _dockedSide = side;
        string how = $"{route}; hand {side}, dock side "
                     + (StatPanelSurface.SignFor(side) < 0f ? "viewer-LEFT" : "viewer-RIGHT");
        // HW-VERIFY: a standing hardware question is waiting on this line - it must stay at a tier
        // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        VRLog.Note("WorldUI",
            "held-prop INFO card docks BESIDE THE PROP IN THE HAND, not at the head - anchor via "
            + $"{how}; pose from StatPanelSurface.TryComputeHeldPose, the same arithmetic the held "
            + "FIGURE's stat card uses. HexHintFacing stands down for this window while the hold "
            + "lasts and keeps the head-follow for laser hover, which the user asked to leave "
            + "alone. If this line is absent while a prop is held, the card is still on the fixed "
            + "PropInfo slot and the head-follower still owns it.");
    }

    private static void Release(Watch watch)
    {
        watch.PendingShow = false;
        watch.ReleaseAt = 0f;
        if (watch.Panel != null)
        {
            // Mutate-and-restore house style: hand every graphic its ORIGINAL mipless sprite
            // back BEFORE the subtree goes home to the 2D UI, so the game's own screen-space
            // panel is left exactly as authored (the baked copies are a VR presentation detail).
            PanelMipBake.Restore(watch.Attached);
            CanvasConversion.Release(watch.Panel);
            watch.Panel = null;
        }
        watch.NextMipRescan = 0f; // a fresh conversion rescans immediately
    }

    private void DetachWatch(Watch watch)
    {
        if (watch.Window != null)
        {
            watch.Window.onShown.RemoveListener(watch.OnShown);
            watch.Window.onHidden.RemoveListener(watch.OnHidden);
        }
        watch.Window = null;
        // Release BEFORE dropping Attached: the release path restores the original sprites
        // through that very handle (PanelMipBake.Restore), so nulling it first would silently
        // leave our baked copies on the game's 2D panel.
        Release(watch);
        watch.Attached = null;
    }

    public void Shutdown()
    {
        DetachWatch(_textInfo);
        DetachWatch(_propInfo);
    }
}

/// <summary>
/// Attribution diagnostic (test #18): the 'Geschlossene Tür' panel appeared with
/// nothing in the log naming WHY — the lock had to be reconstructed from fan-state
/// lines. One change-deduped Info line per Show while VR runs names the hovered
/// prop titles, so any future "mystery panel" report is attributable from
/// LogOutput.log alone. Postfix on the params overload — the (title, description)
/// overload delegates to it (UITextInfoPanel.cs:74-77). Note Show can still
/// early-return inside the game (scene transition, results shown, DoShow off), so
/// the line records the hover REQUEST, not necessarily a visible panel.
/// </summary>
[HarmonyPatch(typeof(UITextInfoPanel), nameof(UITextInfoPanel.Show), typeof((string, string)[]))]
internal static class UITextInfoPanel_Show_Patch
{
    private static string? _lastLogged;

    private static void Postfix((string title, string description)[] input)
    {
        if (!VRSession.IsRunning)
            return;
        string titles = "<none>";
        if (input != null && input.Length > 0)
        {
            var sb = new System.Text.StringBuilder(64);
            for (int i = 0; i < input.Length; i++)
            {
                if (i > 0)
                    sb.Append(" | ");
                sb.Append(string.IsNullOrEmpty(input[i].title) ? "<untitled>" : input[i].title);
            }
            titles = sb.ToString();
        }
        if (titles == _lastLogged)
            return; // change-deduped: the hover path re-Shows per hover change
        _lastLogged = titles;
        VRLog.Info("WorldUI", $"UITextInfoPanel.Show (hover prop info): {titles}.");
    }
}
