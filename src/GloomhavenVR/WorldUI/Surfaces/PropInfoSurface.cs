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
    // ---- THE HOVER-PANEL ANTI-CHURN WATCH IS SHARED IN SHAPE WITH StatPanelSurface, AND THAT IS
    //      A DECISION, NOT AN OVERSIGHT (survey row R37; the 2026-08 review ruled on it).
    //
    // The three constants below, the `Watch` class, `DetachWatch`, `CountConversion` and
    // `ScheduleRelease` are ~60 lines that still diff to ZERO against
    // WorldUI/Surfaces/StatPanelSurface.cs. The review considered merging them and ruled AGAINST it:
    // the constants are PER-SURFACE TUNABLES — a stat panel and a prop info panel are hovered
    // differently and are allowed to want different hysteresis — and a shared core would have to
    // take all three as arguments, which is two call sites with the same three numbers rather than
    // one implementation. What it recommended instead was the zero-risk half: a cross-reference at
    // each duplicated member, so that whoever retunes one is told the other exists. That
    // recommendation was never carried out and this block is it.
    //
    // IF YOU CHANGE ANY OF THE SIX BELOW, READ StatPanelSurface's counterpart FIRST and decide
    // DELIBERATELY whether it follows. They are equal today by history, not by contract.
    //
    // THE REST OF THE PAIR HAS ALREADY CONSOLIDATED, and well: this class CALLS
    // StatPanelSurface.TryComputeHeldPose, .SignFor, .StripLogicComponents and .BuildStaticCopy
    // rather than carrying its own — the direct answer to the "props rewritten from scratch"
    // complaint. What is left duplicated is exactly the tunable half.

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
        {
            _liveDock.Route = null;
            _copyDock.Route = null;
        }

        TickWatch(_textInfo,
            Singleton<UITextInfoPanel>.IsInitialized ? Singleton<UITextInfoPanel>.Instance : null,
            "TextInfoPanel");
        TickWatch(_propInfo,
            Singleton<UIPropInfoPanel>.IsInitialized ? Singleton<UIPropInfoPanel>.Instance : null,
            "PropInfoPanel");
        TickSecondCard();
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
            && TryGetHeldDockPose(watch.IsTextInfo, out Vector3 heldPos, out Quaternion heldRot,
                                  out string route, out HandSide dockSide))
        {
            // The user's live "Infotafel-Groesse" dial still owns the size, exactly as in the
            // docked case - the hold changes WHERE the card is, never how big it is.
            CanvasConversion.PlaceHost(watch.Panel, heldPos, heldRot,
                PanelLayout.WorldScale * WorldUIConfig.HoverInfoScaleLive());
            ReportDock(_liveDock, route, dockSide,
                watch.IsTextInfo ? "the GAME window UITextInfoPanel" : "the GAME window UIPropInfoPanel",
                Board.FigureGrab.HeldPropCard.OwnerOf(watch.IsTextInfo
                    ? HeldPropCardWindow.TextInfo : HeldPropCardWindow.PropInfo)?.Label);
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

    /// <summary>Edge state for the one-shot dock line - the route, hand and content we last
    /// reported docking by, or a null route while no held card is up. Kept as fields rather than
    /// one composed key because <see cref="ReportDock"/> is on a per-frame path and composing a
    /// key there would allocate a string every frame of every hold. One edge PER CARD: the live
    /// window and the second card dock on the same frames, and a shared edge would flip between
    /// them every frame and print every frame.</summary>
    private sealed class DockEdge
    {
        public string? Route;
        public HandSide Side;
        public string? Content;
    }

    private static readonly DockEdge _liveDock = new();
    private static readonly DockEdge _copyDock = new();

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
    /// <param name="isTextInfo">Which window is asking. Since the two-props build each live window
    /// docks at ITS OWNER's hand (<c>HeldPropCard.OwnerOf</c>), so a chest in one hand and a trap
    /// in the other put each card beside its own prop; only an owner without a known hand falls
    /// back to the ModBuild 404 rule (the most recent grab).</param>
    internal static bool TryGetHeldDockPose(bool isTextInfo, out Vector3 pos, out Quaternion rot,
                                            out string route, out HandSide side)
    {
        pos = default;
        rot = Quaternion.identity;
        route = "no held prop";
        side = HandSide.Right;
        if (HeldProps.Count == 0 || !FigureGrabConfig.HeldFigureInfoEnabled)
            return false;
        Board.FigureGrab.GrabbableProp? owner = Board.FigureGrab.HeldPropCard.OwnerOf(
            isTextInfo ? HeldPropCardWindow.TextInfo : HeldPropCardWindow.PropInfo);
        HandSide? ownerSide = owner?.HolderSide;
        Transform? anchor;
        if (ownerSide.HasValue)
        {
            side = ownerSide.Value;
            if (!TryResolveAnchorForHand(side, out anchor, out route) || anchor == null)
                return false;
        }
        else if (!TryResolveHeldAnchor(out anchor, out side, out route) || anchor == null)
        {
            return false;
        }
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
            if (!TryResolveGrabAnchor(s, out anchor, out how) || anchor == null)
                continue;
            side = s;
            return true;
        }
        return false;
    }

    /// <summary>The same three routes as <see cref="TryResolveHeldAnchor"/>, for ONE named hand:
    /// its explicit registration, else the prop visual under its grab anchor, else the grab anchor.
    /// This is what lets two cards dock at two different hands on the same frame.</summary>
    private static bool TryResolveAnchorForHand(HandSide side, out Transform? anchor, out string how)
    {
        anchor = _regAnchors[(int)side];
        if (anchor != null)
        {
            how = "registered prop visual";
            return true;
        }
        for (int slot = 0; slot < HeldProps.Count; slot++)
        {
            if (!HeldProps.TryGetSlot(slot, out _, out HandSide s) || s != side)
                continue;
            return TryResolveGrabAnchor(side, out anchor, out how);
        }
        how = "unresolved (no held prop in that hand)";
        return false;
    }

    /// <summary>Routes 2 and 3 of <see cref="TryResolveHeldAnchor"/> for one hand: the child of
    /// its <c>Rig.GrabAnchor</c> that <see cref="HeldProps.OwnsRendererOf"/> claims, else the
    /// anchor itself. False only when the hand has no rig.</summary>
    private static bool TryResolveGrabAnchor(HandSide side, out Transform? anchor, out string how)
    {
        anchor = null;
        how = "unresolved (no grab anchor for that hand)";
        VRHand? hand = VRHands.Get(side);
        Transform? grab = hand != null && hand.Rig != null ? hand.Rig.GrabAnchor : null;
        if (grab == null)
            return false;
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

    /// <summary>
    /// One line per hold saying WHERE the card docked and by which route - the single fact a
    /// hardware log has to carry for "die Info folgt jetzt dem Prop". Edge-triggered on the route,
    /// so it prints once per pickup and never per frame; its ABSENCE during a hold means the held
    /// branch never ran and the card is still on the fixed slot with the head-follower on it.
    /// </summary>
    /// <param name="edge">The card's own edge state — one per card, see <see cref="DockEdge"/>.</param>
    /// <param name="kind">Which card this is: the game's own window, or the mod's frozen copy.</param>
    /// <param name="content">The content key — the held prop's label — so a log with two docks
    /// says which prop each card is showing.</param>
    private static void ReportDock(DockEdge edge, string route, HandSide side, string kind, string? content)
    {
        // Ordinal compare, not ReferenceEquals: the routes ARE interned literals today, but a
        // reference test would silently start logging every frame the day one of them is composed.
        if (edge.Side == side && string.Equals(edge.Route, route, System.StringComparison.Ordinal)
            && string.Equals(edge.Content, content, System.StringComparison.Ordinal))
            return;   // steady state: no allocation, no string built
        edge.Route = route;
        edge.Side = side;
        edge.Content = content;
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
            + "PropInfo slot and the head-follower still owns it."
            // Two-props build: which card, whose hand, and what it shows. With a prop in each
            // hand of the same card kind this line prints TWICE — once for the game window (the
            // later grab) and once for the mod card (the earlier grab's frozen copy).
            + $" CARD: {kind}; hand {side}; content key {content ?? "<unknown>"}.");
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
        DropSecondCard("surface shutdown");
    }

    // ---- the SECOND held-prop card (two-props build) ----------------------------------------
    //
    // User, 2026-09-03 (translated): "When holding two PROPS (one in each hand) one sees the
    // floating info of only ONE of them; I want to be able to see the info of BOTH. Figure + prop
    // mixed works — the problem is only with a prop in both hands."
    //
    // THE GAME HAS ONE WINDOW PER CARD KIND AND ONE CONTENT SLOT IN EACH. Verified in the
    // decompiled sources: UITextInfoPanel (decompiled/GH.Runtime/UITextInfoPanel.cs) is a
    // Singleton<UITextInfoPanel> whose Show(params (title, description)[]) writes ONE fixed row
    // set (_elements, _maxNumberOfPanels = 2 rows of the SAME card) and whose Awake calls
    // SetInstance(this); UIPropInfoPanel (decompiled/GH.Runtime/UIPropInfoPanel.cs) is a
    // Singleton<UIPropInfoPanel> with one propName and one conditions list, rebuilt by every
    // ShowTrap/ShowHazardousTerrain/ShowDifficultTerrain/ShowQuestItem. Two props of the same
    // kind therefore cannot both be shown by the game's own windows, and a plain Instantiate of
    // a window would run that Awake and STEAL the singleton.
    //
    // SO THE SECOND CARD IS A FROZEN COPY, built the way StatPanelSurface.BuildStaticCopy builds
    // the second held FIGURE's card — the machinery this project already trusts for exactly this
    // shape: Instantiate under an INACTIVE holder (no Awake ever runs), DestroyImmediate every
    // Singleton-derived component and the UIWindow while still never-activated (no OnDestroy
    // either), and only then hand the bare imagery to CanvasConversion. The one addition is the
    // rich window's AutoScrollRect, which restarts a scroll coroutine on every OnEnable; a frozen
    // card has nothing to scroll, so it goes too.
    //
    // WHICH PROP GETS WHICH CARD. The LATER grab keeps the game's live window and the EARLIER
    // grab gets the copy — the figure card's rule (StatPanelSurface.Reconcile, "PRIMARY IS THE
    // LATEST-GRABBED HAND"), and for the figure card's reason: at the instant of the second grab
    // the window is already showing the earlier prop, fully populated, so the copy is taken from
    // what is on screen (HeldPropCard.BeforePopulate, called inside GrabbableProp's push routes
    // BEFORE the overwrite) with nothing borrowed and nothing blanked; the prop just picked up is
    // the one the player is looking at, and it keeps the live window's re-assert and mip cadence.
    // It also leaves the ModBuild 404 dock rule ("the LATER grab docks the card") exactly as it
    // was. Both cards clear on release: the understudy's release destroys the copy; the owner's
    // release promotes the understudy back into the live window (HeldPropCard.Release) and
    // destroys the copy on the same frame.
    //
    // NEVER AN EMPTY WINDOW. The copy's filled fields are COUNTED at build time (active TMP
    // texts with non-blank content); a readable zero means the window was hidden or blank when
    // the later grab arrived, and then the copy is destroyed before it is ever converted. The
    // HELD PROP SECOND CARD line prints the count either way.
    //
    // MULTIPLAYER. The held-prop feature is local-only by design and carries nothing on the wire
    // (HeldProps class doc: "This build is deliberately LOCAL-ONLY and adds nothing to the wire";
    // no Net/ record exists for a held prop, unlike ExtIdSecondFigure / the second held card for
    // figures). A peer does not see the prop in the hand, so the FIRST held-prop card is not
    // mirrored today and the second is not either — consistent, and no per-sub-feature toggle.
    // When the held-prop record the HeldProps doc specifies is claimed, it names BOTH hands'
    // props and the receive side derives both cards itself, exactly as it would the first.

    private static GameObject? _copyHolder;
    private static RectTransform? _copyRect;
    private static ConvertedPanel? _copyPanel;
    private static HeldPropCardWindow _copyWindow;
    private static HandSide _copySide;
    private static string _copyLabel = string.Empty;
    private static bool _copyCommitted;

    /// <summary>
    /// Take the frozen copy of <paramref name="window"/> for the prop that owns it, NOW — the
    /// caller is about to overwrite that window for the other hand. <paramref name="side"/> is the
    /// displaced prop's hand (the copy docks there) and <paramref name="label"/> its content key.
    /// The copy is provisional until <see cref="CommitSecondCard"/>; an uncommitted copy is
    /// destroyed by <see cref="DiscardUncommittedSnapshot"/>. At most one copy exists at a time:
    /// two hands can displace each other only once per grab.
    /// </summary>
    internal static void SnapshotDisplacedCard(HeldPropCardWindow window, HandSide side, string label)
    {
        DropSecondCard(null);
        if (!WorldUIConfig.ConversionActive || !FigureGrabConfig.HeldFigureInfoEnabled)
            return;
        Component? live = window switch
        {
            HeldPropCardWindow.TextInfo => Singleton<UITextInfoPanel>.IsInitialized
                ? Singleton<UITextInfoPanel>.Instance : null,
            HeldPropCardWindow.PropInfo => Singleton<UIPropInfoPanel>.IsInitialized
                ? Singleton<UIPropInfoPanel>.Instance : null,
            _ => null,
        };
        if (live == null)
            return;

        int stripped;
        int filled;
        GameObject holder;
        GameObject copy;
        try
        {
            holder = new GameObject("GloomhavenVR.PropInfoCopyHolder");
            holder.SetActive(false); // MUST precede the Instantiate — keeps every Awake from running
            copy = Object.Instantiate(live.gameObject, holder.transform, false);
            copy.name = "GloomhavenVR.PropInfoPanelCopy";
            stripped = StatPanelSurface.StripLogicComponents(copy);
            AutoScrollRect[] scrollers = copy.GetComponentsInChildren<AutoScrollRect>(true);
            for (int i = 0; i < scrollers.Length; i++)
            {
                if (scrollers[i] == null)
                    continue;
                Object.DestroyImmediate(scrollers[i]);
                stripped++;
            }
            // The window may have been copied mid fade — force the copy fully opaque and inert.
            var group = copy.GetComponent<CanvasGroup>();
            if (group != null)
            {
                group.alpha = 1f;
                group.interactable = false;
                group.blocksRaycasts = false;
            }
            filled = CountFilledFields(copy);
        }
        catch (System.Exception e)
        {
            // A copy that cannot be built must never cost the player the LIVE card: the later
            // grab's push proceeds untouched, and the earlier prop simply keeps no card.
            VRLog.Alert("WorldUI",
                $"HELD PROP SECOND CARD could not be built for {label} ({WindowName(window)}, hand "
                + $"{side}): {e.GetType().Name}: {e.Message} — the earlier prop keeps no card.");
            return;
        }

        // HW-VERIFY: the line the two-props round is waiting on — it must stay at a tier the
        // DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        VRLog.Note("WorldUI",
            $"HELD PROP SECOND CARD built for {label}: a frozen snapshot copy of {WindowName(window)} "
            + $"for hand {side} — {filled} field(s) filled, {stripped} logic component(s) stripped "
            + "(game singleton untouched). "
            + (filled > 0
                ? "It is shown beside that hand as the MOD card once the later grab's content has "
                  + "taken the game window; the 'held-prop INFO card docks' line names both."
                : "ZERO fields filled: this would have been an EMPTY window, so the copy is destroyed "
                  + "and NOT shown — the earlier prop keeps no card for the rest of this hold. The "
                  + "window was hidden or blank at the instant of the second grab (a lost re-assert, "
                  + "see the INFO CONCEDED line if present)."));
        if (filled == 0)
        {
            Object.Destroy(holder);
            return;
        }

        _copyHolder = holder;
        _copyRect = copy.transform as RectTransform;
        _copyWindow = window;
        _copySide = side;
        _copyLabel = label;
        _copyCommitted = false;
    }

    /// <summary>The later grab landed in <paramref name="window"/>: the copy taken of it is the
    /// displaced prop's card from now on.</summary>
    internal static void CommitSecondCard(HeldPropCardWindow window)
    {
        if (_copyHolder != null && _copyWindow == window)
            _copyCommitted = true;
    }

    /// <summary>The election is over and no claim committed the copy — destroy it.</summary>
    internal static void DiscardUncommittedSnapshot()
    {
        if (_copyHolder != null && !_copyCommitted)
            DropSecondCard("the later grab took the other window");
    }

    /// <summary>Destroy the second card (idempotent). <paramref name="why"/> is logged at the debug
    /// tier when a card actually existed; null means silent.</summary>
    internal static void DropSecondCard(string? why)
    {
        bool had = _copyHolder != null || _copyPanel != null;
        if (_copyPanel != null)
        {
            CanvasConversion.Release(_copyPanel);
            _copyPanel = null;
        }
        if (_copyHolder != null)
        {
            Object.Destroy(_copyHolder);
            _copyHolder = null;
        }
        _copyRect = null;
        _copyCommitted = false;
        _copyDock.Route = null;
        if (had && why != null)
            VRLog.Info("WorldUI", $"held-prop second card ({_copyLabel}) torn down: {why}.");
    }

    /// <summary>Convert the committed copy once and keep it docked at its hand; tear it down when
    /// the feature or the conversion goes off. Update-pass half; <see cref="LateTickSecondCard"/>
    /// re-asserts the pose after the hands have moved.</summary>
    private static void TickSecondCard()
    {
        bool active = WorldUIConfig.ConversionActive && FigureGrabConfig.HeldFigureInfoEnabled;
        if (_copyHolder != null && (!active || HeldProps.Count < 2))
        {
            // Fewer than two props held and a copy still standing means a hold ended without a
            // release (teardown, prune) and the registry was cleared around us — never show a
            // card for a prop that is not in a hand.
            DropSecondCard(!active ? "conversion or the HeldFigureInfo dial went off" : "fewer than two props held");
            return;
        }
        if (_copyHolder == null || !_copyCommitted || _copyRect == null)
            return;
        if (_copyPanel != null && !_copyPanel.IsAlive)
            _copyPanel = null;
        if (_copyPanel == null)
        {
            // Same treatment as the live window (TickWatch): non-pokeable, flattened, no MR plate
            // (it is the same opaque card art), one mip pass — a static copy never loads anything
            // afterwards, and this is a throwaway we own, so no restore is ever needed.
            _copyPanel = CanvasConversion.Convert(_copyRect, "PropInfoPanelCopy", pokeable: false,
                flatten2D: true);
            if (_copyPanel == null)
                return;
            _copyPanel.MrBackingSuppressed = true;
            PanelMipBake.Rescan(_copyRect, "PropInfoPanelCopy");
        }
        if (_copyPanel.HostRaycaster != null && _copyPanel.HostRaycaster.enabled)
            _copyPanel.HostRaycaster.enabled = false;
        PlaceSecondCard();
    }

    /// <summary>LateUpdate re-assert of the copy's pose — the hands move in LateUpdate, and the
    /// live held card gets the same second placement from <c>HexHintFacing.LateTick</c>.</summary>
    internal static void LateTickSecondCard()
    {
        if (_copyPanel == null || _copyHolder == null)
            return;
        PlaceSecondCard();
    }

    private static void PlaceSecondCard()
    {
        if (_copyPanel == null)
            return;
        if (!TryResolveAnchorForHand(_copySide, out Transform? anchor, out string how) || anchor == null)
            return;
        if (!StatPanelSurface.TryComputeHeldPose(anchor.position, StatPanelSurface.SignFor(_copySide),
                                                 out Vector3 pos, out Quaternion rot))
            return;
        CanvasConversion.PlaceHost(_copyPanel, pos, rot,
            PanelLayout.WorldScale * WorldUIConfig.HoverInfoScaleLive());
        ReportDock(_copyDock, how, _copySide,
            _copyWindow == HeldPropCardWindow.TextInfo
                ? "the MOD card (a frozen snapshot copy of UITextInfoPanel; the EARLIER grab)"
                : "the MOD card (a frozen snapshot copy of UIPropInfoPanel; the EARLIER grab)",
            _copyLabel);
    }

    /// <summary>How many text fields the copy would actually show: TMP texts that are active all
    /// the way up to the copy root (the holder itself is inactive, so <c>activeInHierarchy</c>
    /// cannot be used) with non-blank content. Zero is the "empty window" verdict.</summary>
    private static int CountFilledFields(GameObject copy)
    {
        TMPro.TMP_Text[] texts = copy.GetComponentsInChildren<TMPro.TMP_Text>(true);
        int filled = 0;
        for (int i = 0; i < texts.Length; i++)
        {
            TMPro.TMP_Text t = texts[i];
            if (t == null || string.IsNullOrWhiteSpace(t.text))
                continue;
            bool active = true;
            for (Transform? cur = t.transform; cur != null && active; cur = cur.parent)
            {
                active = cur.gameObject.activeSelf;
                if (ReferenceEquals(cur.gameObject, copy))
                    break;
            }
            if (active)
                filled++;
        }
        return filled;
    }

    private static string WindowName(HeldPropCardWindow window) =>
        window == HeldPropCardWindow.TextInfo ? "UITextInfoPanel ('Text Info Panel')"
                                              : "UIPropInfoPanel ('Prop Info Panel')";
}

/// <summary>
/// THE HOVER CARD'S CONTENT — the attribution diagnostic (test #18), and since ModBuild 445 the
/// GOLD REPAIR that the 2026-09-05 report needs.
///
/// <para><b>The report.</b> "Hinterlassene Goldhaufen von Gegnern haben keine Tooltips beim
/// Laser-Mouseover und können auch nicht in die Hand genommen werden." The second half is a
/// collider defect and is fixed elsewhere (<c>VRInteractables.IsUsablePickShape</c>). The FIRST
/// half is not the same bug and the recon that paired them had it wrong: the hover tooltip is
/// raised entirely from the HEX, never from the prop's own collider. The chain is
/// <c>WorldspaceStarHexDisplay.Update</c> → <c>DisplayCursorHoverStar</c> (:3200) →
/// <c>Interactable()</c> (:3813) → <c>MF.FindInteractableAtMousePosition</c> (MF.cs:387) on
/// <c>m_HexSelectionRaycastLayer</c>, which the mod prefixes with the VR ray
/// (<c>Board.Patches.PickingPatches</c>), then <c>GetComponent&lt;TileBehaviour&gt;</c> and
/// <c>ShowTooltipForTile(tile)</c> (:3370), which reads <c>tile.m_Tile.m_Props</c>. A prop's
/// collider state cannot reach any step of that. And the gold pile IS in that list: an enemy drop
/// is built by <c>DelayedDropSMB</c> (:124) and added through <c>CTile.SpawnProp</c>
/// (CTile.cs:130).</para>
///
/// <para><b>THE ACTUAL CAUSE IS A DEDUP COLLISION IN THE GAME'S OWN TOOLTIP BUILDER, and it is
/// specific to gold.</b> <c>ShowTooltipForTile</c> walks the hex's props through a ladder of
/// <c>if (ObjectType == …)</c> branches. Every branch writes <c>info.ImportType =
/// cObjectProp.ObjectType</c> — Chest at :3425, GoalChest :3430, Obstacle :3436, Door :3442,
/// PressurePlate :3446, Portal :3451, CarryableQuestItem :3457, Resource :3464 — with exactly
/// ONE exception: the MoneyToken branch (:3413-3421) sets only <c>info.Gold</c> and leaves
/// <c>ImportType</c> at its initialiser, <c>None</c>. The list is then deduped on that same field:
/// <c>if (!list.Exists(x =&gt; x.ImportType == info.ImportType)) list.Add(info);</c> (:3468).</para>
///
/// <para>Several prop families have NO branch at all in that ladder — <c>MonsterGrave</c>,
/// <c>GenericProp</c>, <c>TerrainVisualEffect</c>, rubble, water, thorns, traps. Such a prop falls
/// through with a null title AND <c>ImportType == None</c>, and is added anyway because the list
/// was empty. When the gold arrives after it, the reunion test at :3415 looks for
/// <c>ImportType == MoneyToken</c> or <c>(ImportType == None &amp;&amp; Gold &gt; 0)</c> — the
/// blank entry has <c>Gold == 0</c>, so it does not match and the gold gets a FRESH
/// <c>PropInfo</c>, which then collides with the blank one's <c>None</c> at :3468 and IS SILENTLY
/// DISCARDED. The list is left holding one entry with a null title, <c>list.Count &gt; 0</c> so
/// the <c>TryReset()</c> branch is skipped, and <c>Show((null, null))</c> runs.</para>
///
/// <para><b>Why that is invisible rather than merely wrong, and worse in VR.</b>
/// <c>UITextInfoElement.Set</c> deactivates its whole row on an empty TITLE
/// (UITextInfoElement.cs:21), while <c>UITextInfoPanel.Show</c> calls <c>_window.Show()</c>
/// unconditionally (UITextInfoPanel.cs:69). Flat-screen still paints the panel's container; this
/// surface converts with content-fitting off and <c>MrBackingSuppressed</c> (see
/// <see cref="PropInfoSurface"/>'s dock), so the player gets a fully transparent quad — no card
/// at all, which is exactly the word the report uses.</para>
///
/// <para><b>Why a chest on the same hex never shows this.</b> Chest and Obstacle claim their own
/// <c>ImportType</c>, so a blank neighbour can never dedup them away. Gold is the only entry whose
/// identity is <c>None</c>, i.e. the only one that can lose a collision it did not know it was
/// in. And an enemy drop is always APPENDED to the hex's prop list, so anything already standing
/// there — a grave the same death created, terrain the enemy was standing on — is at a lower
/// index and always takes the slot first.</para>
///
/// <para><b>THE REPAIR, and what it deliberately does not do.</b> A prefix that re-adds the entry
/// the game dropped, and nothing else. It reads the hovered hex through
/// <c>BoardPick.TryGetHoveredTile</c>, counts its MoneyToken props and converts them with the
/// game's own expression (<c>SLTE == null ? 1 : SLTE.GoldConversion</c> per token —
/// WorldspaceStarHexDisplay.cs:3420, the same term CollectLootSMB.cs:87 and CAbilityLoot.cs:302
/// use), and appends the line the game builds in <c>PropInfo.Get()</c> (:85-91) only when the
/// incoming array does not already carry it. NOTHING IS WRITTEN TO GAME STATE: the prop list, the
/// scenario and the level table are read and never assigned, and the only mutation is to the
/// argument array this call is about to render. When the hex has no gold, when the gold is
/// already in the array, or when the VR pick is not on a hex, the prefix returns having changed
/// nothing — so every tooltip that works today is bit-identical.</para>
///
/// <para><b>Blank entries are dropped in the same pass, and only then.</b> A <c>PropInfo</c> with
/// no title renders as a deactivated row, so removing it costs no information; keeping it is what
/// made <c>list.Count &gt; 0</c> true and suppressed the game's own <c>TryReset()</c>. They are
/// dropped only on a call this repair is already rewriting, so a panel that is legitimately blank
/// for some other reason is untouched.</para>
///
/// <para><b>THE DIAGNOSTIC WAS ALWAYS HERE AND COULD NOT BE READ.</b> The postfix below has logged
/// the hovered titles since test #18 — including <c>&lt;untitled&gt;</c>, which is this exact
/// defect printing its own name — at <c>VRLog.Info</c>, i.e. the DEBUG tier, which a shipped
/// build does not print. It is promoted to <c>Note</c>: one change-deduped line per hover change
/// is affordable and it is the line that settles gold tooltips for good.</para>
///
/// <para>Both hooks sit on the params overload; the <c>(title, description)</c> overload delegates
/// to it (UITextInfoPanel.cs:74-77). <c>Show</c> can still early-return inside the game (scene
/// transition, results shown, <c>DoShow</c> off), so the log records the hover REQUEST, not
/// necessarily a visible panel.</para>
/// </summary>
[HarmonyPatch(typeof(UITextInfoPanel), nameof(UITextInfoPanel.Show), typeof((string, string)[]))]
internal static class UITextInfoPanel_Show_Patch
{
    private static string? _lastLogged;

    /// <summary>Repair lines this session. The repair fires per hover change over a broken hex,
    /// which a player can do dozens of times; the first two lines carry the whole finding and the
    /// counter below carries the rest.</summary>
    private const int RepairLogBudget = 2;

    private static int _repairLogsLeft = RepairLogBudget;

    private static int _repairs;

    /// <summary>Hexes seen carrying MoneyToken props whose converted total was ZERO — i.e. the
    /// scenario's <c>GoldConversion</c> is 0. A different cause with the same symptom, counted so
    /// it cannot be confused with the dedup collision.</summary>
    private static int _zeroConversion;

    private static void Prefix(ref (string title, string description)[] input)
    {
        if (!VRSession.IsRunning || input == null)
            return;

        int gold = HoveredHexGold(out int tokens);
        if (tokens == 0)
            return; // the hovered hex carries no gold: nothing this repair has an opinion about

        if (gold <= 0)
        {
            _zeroConversion++;
            return; // GoldConversion is 0; inventing a number here would be a fudge, not a fix
        }

        // The game's own term, through the game's own localiser (WorldspaceStarHexDisplay.cs:91),
        // so the repaired card reads in the player's language exactly as a working one would.
        string goldLine = $"{gold} {GLOOM.LocalizationManager.GetTranslation("Gold")}";

        int titled = 0;
        for (int i = 0; i < input.Length; i++)
        {
            if (string.IsNullOrEmpty(input[i].title))
                continue;
            titled++;
            // The game composes the gold either alone or appended after another prop's title with
            // "\n&\n" (PropInfo.Get, WorldspaceStarHexDisplay.cs:85-91), so a Contains test is
            // what "the gold is already on this card" means for both shapes.
            if (input[i].title.Contains(goldLine))
                return; // the game got it right for this hex; change nothing
        }

        var rebuilt = new (string title, string description)[titled + 1];
        int w = 0;
        for (int i = 0; i < input.Length; i++)
        {
            if (!string.IsNullOrEmpty(input[i].title))
                rebuilt[w++] = input[i];
        }
        rebuilt[w] = (goldLine, null!);
        int dropped = input.Length - titled;
        input = rebuilt;

        _repairs++;
        if (_repairLogsLeft <= 0)
            return;
        _repairLogsLeft--;
        // HW-VERIFY: this line is the falsifier for the 2026-09-05 "gold piles have no tooltip"
        // diagnosis. It prints only when the game's own tooltip builder dropped a gold entry that
        // the hovered hex demonstrably carries, so its presence proves the dedup collision is real
        // on this board and its ABSENCE, while the user still reports no card, sends the search
        // downstream (the hover re-entry latch, or the conversion). It must stay at a tier the
        // DEFAULT log level prints (Note/Alert/Error); scripts/check-hw-verify.py enforces it.
        VRLog.Note("WorldUI",
            $"HOVER TOOLTIP REPAIRED: the hex under the laser carries {tokens} MoneyToken prop(s) "
            + $"worth {gold} gold, and the game's own tooltip builder handed us "
            + $"{input.Length - 1} titled entr(y/ies) with none of them naming it. That is "
            + "WorldspaceStarHexDisplay.ShowTooltipForTile:3468 discarding the gold: MoneyToken is "
            + "the ONE prop family whose branch (:3413-3421) never sets PropInfo.ImportType, so it "
            + "stays None and collides with any prop on the same hex that has no branch in that "
            + "ladder at all (a MonsterGrave the death itself created, terrain, a generic prop) — "
            + "those also come out None, and being EARLIER in the hex's prop list they take the "
            + $"slot. {dropped} blank entr(y/ies) dropped and the gold line re-added, so the card "
            + "says what the hex holds. A chest or obstacle can never hit this because it claims "
            + "its own ImportType (:3425/:3436), which is why only ENEMY-DROP gold was reported. "
            + $"({_repairLogsLeft} more of these lines this session; the count is in the Show line "
            + "below.)");
    }

    /// <summary>
    /// The gold the hovered hex actually holds, computed with the GAME's expression so the number
    /// on the repaired card is the number the game would have printed.
    /// </summary>
    /// <param name="tokens">How many MoneyToken props the hex carries — separate from the value,
    /// because "no gold here" and "gold worth zero" are different answers and only the first one
    /// means this repair has nothing to say.</param>
    private static int HoveredHexGold(out int tokens)
    {
        tokens = 0;
        if (!Board.BoardPick.TryGetHoveredTile(out ScenarioRuleLibrary.CTile? hovered))
            return 0;
        ScenarioRuleLibrary.CTile? tile = hovered;
        if (tile == null)
            return 0;
        System.Collections.Generic.List<ScenarioRuleLibrary.CObjectProp>? props = tile.m_Props;
        if (props == null)
            return 0;

        ScenarioRuleLibrary.CScenario? scenario = ScenarioRuleLibrary.ScenarioManager.Scenario;
        ScenarioRuleLibrary.YML.ScenarioLevelTableEntry? slte = scenario != null ? scenario.SLTE : null;
        int per = slte == null ? 1 : slte.GoldConversion;
        int total = 0;
        for (int i = 0; i < props.Count; i++)
        {
            ScenarioRuleLibrary.CObjectProp prop = props[i];
            if (prop == null
                || prop.ObjectType != ScenarioRuleLibrary.ScenarioManager.ObjectImportType.MoneyToken)
                continue;
            tokens++;
            total += per;
        }
        return total;
    }

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
        // HW-VERIFY: PROMOTED Info -> Note in ModBuild 445, text otherwise unchanged. This line has
        // printed "<untitled>" — the gold-dedup defect naming itself — since test #18, at the DEBUG
        // tier, i.e. never in a shipped build, which is why two hardware rounds argued about gold
        // tooltips with the answer already written. It is the ONE line that says what the hover
        // card was asked to contain. Tier enforced by scripts/check-hw-verify.py.
        VRLog.Note("WorldUI",
            $"UITextInfoPanel.Show (hover prop info): {titles}. "
            + $"Gold entries this session that the game dropped and we re-added: {_repairs}; "
            + $"hover(s) over a gold hex whose converted value was ZERO (the scenario's "
            + $"GoldConversion is 0, a different cause with the same blank card): {_zeroConversion}. "
            + "READ IT LIKE THIS: '<untitled>' here means the game built a tooltip entry with no "
            + "title, which renders as a deactivated row and, on this world-space surface, as "
            + "nothing at all.");
    }
}
