using GloomhavenVR.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Net;

/// <summary>
/// A PEER'S SYNCED BOARD TOOLTIP -- the hint panel at the remote control board's TOOLTIP AREA
/// (top-left corner) that shows what that player is currently reading on their own board
/// (user request 2026-08-04: "Auch Tooltipps sollen im Multiplayer synchronisiert werden und
/// vollstaendig am Remote-Board angezeigt werden").
///
/// WHY IT RIDES THE WIRE AT ALL: like the pick banner, a tooltip's text is composed by the
/// OWNER's client out of what THEY hover -- local UI state that exists nowhere else, not
/// derivable from replicated game state. So it is sent verbatim as extension record
/// <see cref="NetProtocol.ExtIdBoardTooltip"/>.
///
/// WHAT IT DOES NOT LEAK -- THE IDENTITY GATE, which distinguishes this record from the pick
/// banner: a tooltip CAN name a card (hovering an ability card surfaces its title and effect
/// text), and the standing rule is absolute -- no card identity on the wire, ever; reveals only
/// through <see cref="RevealGate"/>. The gate therefore lives on the SENDER
/// (<c>WorldUI.WorldTooltips</c>, the only place that knows what the tooltip is anchored to):
/// only content already public to peers is ever transmitted, and ambiguity suppresses. By the
/// time bytes reach this class they are public by construction; this class is pure display.
///
/// LANGUAGE: the text arrives already composed, in the SENDER's language, and is shown verbatim
/// -- the same argument as the pick banner (it is their board; it reads in their language).
///
/// ─── 1:1 PRESENTATION (user report 2026-08-08) ─────────────────────────────────────────────────
/// "Das Aussehen des Mouse-over-Hints beim Remote-Board ist nicht identisch. Aktuell ist es beim
/// Remote-Board ein Rechteck als Hintergrund und beim Spieler der spieleigene Hintergrund -- der
/// soll auch beim Remote-Board angezeigt werden. Auch die Position und Groesse soll 1:1 so
/// synchronisiert werden."
///
/// The OWNER does not look at a mod-drawn anything: <c>WorldUI.WorldTooltips</c> takes the game's
/// OWN <c>UITooltip</c> widget -- its 9-sliced background <c>Image</c>, its <c>m_TitleFont</c>
/// type, its <c>ContentSizeFitter</c> footprint -- and flips that live canvas into world space.
/// The previous revision of this mirror drew a flat dark <c>BoardVisual.Quad</c> with a mod TMP on
/// it, so a peer's hint was a plain rectangle where the owner's was the game's frame art. It is
/// now built from the GAME's own materials, sampled off the live widget every client already has:
///
///   • SAME BACKGROUND ART: the background is an <c>Image</c> carrying the tooltip's own sprite,
///     sprite TYPE (<c>Sliced</c> -- the 9-slice corners/edges survive at any size), colour,
///     material and PPU multiplier, read straight off <c>UITooltip</c>'s own <c>Image</c>
///     (<see cref="GameSkin"/>). NOTHING on the game widget is written; only read. This is the
///     ModBuild-73 decision-dock discipline (<c>WorldUI.NativeButtonSkin</c>): sample the live
///     game UI, re-host the art, never adopt the widget.
///   • GAME TYPE, BUT ONE STYLE OF FOUR: the game's <c>m_TitleFont</c> at <c>m_PCTitleFontSize</c>
///     (16 px) in <c>m_TitleFontColor</c> -- not a mod font at a mod size. This bullet used to stop
///     there and claim that WAS the owner's type. It is the owner's type only for
///     <c>UITooltipLines.LineStyle.Title</c>. See the RESIDUE block below for what falsified it.
///   • SAME UNITS: the mirror is a world-space uGUI canvas scaled at exactly the owner's
///     metre-per-pixel (<see cref="_metersPerUiPixel"/>), so a layout done in the game's own PIXELS
///     lands at the owner's own METRES. That half is sound and is what makes the box comparable at
///     all. The bullet used to continue "...SO SAME SIZE ... Same text ⇒ same box, by construction",
///     and THAT half is false: right units, wrong number and wrong rule. See the RESIDUE block.
///
/// ─── RESIDUE: THE BOX AND THE TYPE ARE NOT THE OWNER'S, AND CLOSING IT NEEDS WIRE BYTES ────────
/// (R2 finding F7, 2026-09-07. Both halves re-derived against <c>decompiled/GH.Runtime/</c>.)
///
///   • THE WIDTH. This mirror wraps at <c>GameSkin.WidthPx</c> = <c>UITooltip.m_DefaultWidth</c>
///     (257 px) and then HUGS the result (<see cref="Layout"/>'s
///     <c>Mathf.Clamp(pref.x, 1f, innerWrap)</c>). The game does neither.
///     <c>UITooltipTarget.PrepareTooltip</c> ALWAYS sets the box width from the hovered target's
///     own serialized <c>width</c> field -- <c>UITooltip.SetWidth(width)</c> when
///     <c>autoAdjustHeight</c>, <c>SetSize(width, height)</c> otherwise
///     (<c>UITooltipTarget.cs:145-152</c>, field declared <c>: 20</c> with default <c>100f</c>) --
///     and <c>Internal_SetWidth</c> writes <c>m_Rect.sizeDelta.x</c> outright
///     (<c>UITooltip.cs:804-807</c>). <c>m_DefaultWidth</c> is only what <c>InternalOnHide</c>
///     RESETS to (<c>UITooltip.cs:613</c>), i.e. a value the shown box never has unless a target
///     happens to serialize it. The horizontal <c>ContentSizeFitter</c> stays
///     <c>Unconstrained</c>: <c>SetHorizontalFitMode</c> has NO caller anywhere in the decompiled
///     game (verified -- the only hits are its own declaration and forwarder). So the owner's box
///     is a FIXED width that auto-fits vertically only, and this one is a hugging box at a
///     different wrap.
///   • THE TYPE. <see cref="GameSkin"/> samples exactly one trio, and
///     <c>UITooltip.CreateLineColumn</c> switches per LINE across four styles -- Title, Attribute,
///     Description, Keyword -- each with its own font, size, colour, <c>lineSpacing</c>,
///     <c>fontStyle</c> and text effect (<c>UITooltip.cs:679-732</c>). Every
///     <c>UITooltipLines.AddLine</c> overload that does not take a style defaults to
///     <b>Attribute</b>, not Title (<c>UITooltipLines.cs:59-84</c>), and the ordinary board path is
///     title + description.
///   • WHY IT IS NOT FIXED HERE. Extension record 9 carries the composed TEXT and nothing else --
///     no width, no per-line style (<c>PresenceState.cs:440-445, 2136-2145</c>). The width is the
///     hovered TARGET's serialized field and the styles are per line, so neither is derivable on a
///     receiver that is told only what the string says. This is the one place in this class where
///     "GLOBAL, zero wire" does not reach, and closing it means BYTES on record 9 -- which this
///     lane may not allocate. The layout line below prints the deciding fields so the next round
///     starts from a measurement.
///   • WHAT IT LOOKS LIKE. Owner hovers an initiative-track portrait: bold title in the title font
///     over a body paragraph in the description font, at the description size and colour, in a box
///     fixed at that target's width. Viewer: both lines in the title font at 16 px in
///     <c>m_TitleFontColor</c> at TMP default line spacing, in a box wrapped at 257 px and then
///     shrunk to hug the single line. Two untuned clients, no dial touched.
///   • SAME AREA CONTRACT: the frame's bottom-left corner seats <see cref="MarginY"/> above the
///     area origin and the box grows UP/RIGHT into open air -- the local
///     <c>WorldTooltips.TryGetBoardAreaPose</c> contract verbatim.
///   • THE OWNER'S OWN DIALS, since extension record 28: their <c>HoverInfoScale</c> and
///     <c>CanvasScaleMm</c> (the two factors of the world scale <c>WorldTooltips</c> applies) and
///     their tuned <c>HoverHintOffset</c> (through <see cref="RemoteBoardLayout"/>). This used to
///     be a DELIBERATELY-NOT note; under the 1:1 ruling a tuned owner's tooltip must read the same
///     size and sit at the same corner everywhere. Untuned players are unaffected by a byte.
///
/// WHERE IT SITS: at the board's MEASURED top-left corner -- <see cref="Reseat"/> runs the owner's
/// own <c>PlayTray.MeasureBoardLocalExtents</c> over this peer's board root and expresses the
/// answer as a correction on <see cref="RemoteBoardLayout.TooltipMount"/> (the AUTHORED
/// <c>PlayTray.TooltipAreaBase</c> corner, already proud of the board face, plus the owner's tuned
/// per-board offset). The authored corner ALONE is the expression the owner-side fix replaced --
/// see <see cref="Reseat"/> for what it was costing here.
///
/// MIXED REALITY: the background is a GAME sprite on a GAME material, so it must never be
/// re-tinted or alpha-forced the way the old mod plate was (<c>MrBacking.Opacify</c> writes into
/// the material it is handed -- here that would be a shared game asset). Instead this mirror
/// registers as an <c>MrBacking.IBackedSurface</c> and gets the same opaque host plate a converted
/// panel does, behind the frame art. Normal mode is unaffected: no plate exists while MR is off.
/// </summary>
/// <remarks>CLASSIFICATION: MIXED (WIRE extension record 9 -- the sender-composed, identity-gated
/// TEXT; GLOBAL -- the frame art, font and layout metrics, read from the game's own
/// <c>UITooltip</c> on the receiver, zero wire). See INVARIANTS-Net-Rig.md "Net -- content
/// classification".</remarks>
internal sealed class RemoteBoardTooltip : WorldUI.MrBacking.IBackedSurface
{
    /// <summary>Board-local metres per uGUI pixel at the DEFAULT dials: CanvasScaleMm (1) × 0.001
    /// × 0.5 (the WorldTooltips halving) -- the board-scale factor is carried by the board root's
    /// own synced scale. THE reason the layout below can be done entirely in the game's own pixels
    /// and still come out at the owner's metres.
    ///
    /// <para>The two dials in that product are the OWNER's, and they are now transmitted
    /// (<see cref="NetProtocol.TuneCanvasScaleMm"/> / <see cref="NetProtocol.TuneHoverInfoScale"/>,
    /// record 28): <see cref="_metersPerUiPixel"/> is this constant × their CanvasScaleMm × their
    /// HoverInfoScale/default, i.e. exactly <c>WorldTooltips</c>'s own expression. Both resolve to
    /// this constant for every player who has not tuned them.</para></summary>
    private const float DefaultMetersPerUiPixel = 0.0005f;

    /// <summary>The owner's own metres-per-pixel (see <see cref="DefaultMetersPerUiPixel"/>).</summary>
    private readonly float _metersPerUiPixel;

    /// <summary>Clearance between the board's top edge and the frame's bottom edge -- the remote
    /// mirror of <c>WorldUI.WorldTooltips.BoardAnchorMarginY</c> (board-local metres).</summary>
    private const float MarginY = 0.03f;

    /// <summary>Hard ceiling on the fitted text height in PIXELS (the 192-byte wire cap bounds the
    /// content anyway; this only guards a pathological all-newline text). Overflow truncates.</summary>
    private const float MaxTextHeightPx = 440f;

    /// <summary>Frame width used until the game's own <c>m_DefaultWidth</c> can be sampled --
    /// the authored value itself, so a pre-sample layout is already the right shape.</summary>
    private const float FallbackWidthPx = 257f;

    /// <summary>Frame font size / padding used until the game's own values can be sampled.</summary>
    private const float FallbackFontPx = 16f;
    private const float FallbackPadPx = 20f;

    /// <summary>Fallback frame ink for the (rare, transient) case where the game's tooltip widget
    /// has not been reachable yet: the tooltip's dark warm frame, opaque enough to read over
    /// anything. Replaced by the real sprite the moment one can be sampled.</summary>
    private static readonly Color FallbackInk = new(0.09f, 0.075f, 0.06f, 0.94f);

    /// <summary>Fallback type colour (the game's <c>m_TitleFontColor</c> is white).</summary>
    private static readonly Color FallbackTextInk = new(0.93f, 0.90f, 0.83f);

    private readonly Transform _root;

    /// <summary>The peer's board root -- the subtree the area corner is MEASURED off, which is the
    /// same subtree the owner measures (<c>WorldTooltips.TryGetBoardRoot</c> hands
    /// <c>MeasureBoardLocalExtents</c> the tray ROOT). Kept as a field because the corner is
    /// re-resolved every time the hint comes up, not once at build.</summary>
    private readonly Transform _boardRoot;

    /// <summary>The AUTHORED seat this mirror used to sit at outright:
    /// <see cref="RemoteBoardLayout.TooltipMount"/> = <c>PlayTray.TooltipAreaBase</c> + the owner's
    /// tuned <c>HoverHintOffset</c>. The measurement is applied as a CORRECTION on it, so the
    /// owner's dial rides through untouched and an unmeasurable board degrades to exactly the old
    /// seat.</summary>
    private readonly Vector3 _authoredSeat;

    /// <summary>The last correction <see cref="Reseat"/> applied (board-local metres) -- carried
    /// only so the layout line can state how far the measured corner sits from the authored one.
    /// No separate log token: it rides the line that was already there.</summary>
    private Vector3 _seatCorrection;

    private readonly GameObject _hostGo;
    private readonly Canvas _canvas;
    private readonly RectTransform _hostRect;
    private readonly RectTransform _frame;
    private readonly Image _frameImage;
    private readonly TextMeshProUGUI _label;

    private string _shown = string.Empty;
    private Vector2 _framePx;
    private bool _skinApplied;
    private bool _destroyed;

    // ---- ModBuild 405: the SEAT LATCH, the owner's rule mirrored 1:1 ---------------------------
    // The owner (WorldUI.WorldTooltips, the _latch* block) measures its seat ONCE per shown
    // episode in the BOARD's frame and re-measures only on a content change. This mirror already
    // held its seat by construction — _root is a child of the board root, and Reseat runs on the
    // way UP only — but it never re-seated on a content change while up, so after the owner's
    // re-latch the two boxes could disagree by whatever the docks had moved in between. Now a text
    // change while shown re-measures too, with the root briefly inactive so the walk cannot see
    // our own MR plate (Reseat's last paragraph). Frame and count ride the layout line.
    private int _latchFrame;     // Time.frameCount of the episode's first seat
    private int _latchRelatches; // re-seats on content change within this shown episode

    public RemoteBoardTooltip(Transform boardRoot, in RemoteBoardLayout layout,
                              in RemoteBoardTuning tuning)
    {
        // THE OWNER'S OWN SIZE (record 28). WorldTooltips scales its canvas by
        // CanvasScaleMm × 0.001 × boardScale × 0.5 × (HoverInfoScale / default); board scale is
        // already carried by the synced board root, so the two DIALS are all that was missing.
        // Both are sent together deliberately: transmitting one factor of a product would leave
        // the mirror wrong for anyone who tuned the other. Untuned ⇒ exactly the old constant.
        _metersPerUiPixel = DefaultMetersPerUiPixel
                            * Mathf.Max(0.01f, tuning.CanvasScaleMm)
                            * Mathf.Max(0.01f, tuning.HoverInfoScale)
                            / Defaults.HoverInfoScale;

        // The root IS the area origin (the authored top-left corner, proud of the board face);
        // Layout() lays the fitted box out in host-PIXEL space, growing up/right from it.
        _root = new GameObject("BoardTooltip").transform;
        _root.SetParent(boardRoot, worldPositionStays: false);
        _boardRoot = boardRoot;
        _authoredSeat = layout.TooltipMount;
        Reseat();   // nothing of ours renders yet, so this first walk cannot see itself

        // The host canvas: uGUI pixels in, the OWNER's board-local metres out. Everything below is
        // therefore expressed in the game's own pixel numbers (257 px wide, 16 px type, the
        // tooltip's own padding) and needs no second unit system.
        _hostGo = new GameObject("Host");
        _hostGo.transform.SetParent(_root, worldPositionStays: false);
        var canvas = _hostGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        // DRAW ORDER is NOT set here any more. It used to be a fixed "tooltip beats everything on
        // this board" tier, and that is precisely user report #5 of 2026-08-09: this hint sits
        // 2 cm proud of the board face while the initiative mirror it overlaps is docked 4.8-7.0 cm
        // proud, so the fixed tier painted the FARTHER plate over the nearer one at every angle.
        // The board's own cluster sweep now seats this canvas by its measured board-local depth
        // (BoardVisual.AdoptBoardOrder / TierForDepth), which answers the same question with the
        // geometry instead of a constant — and ranks the whole board against the panel ladder.
        // 9-SLICE SCALE: uGUI resolves a sliced sprite's border into RECT pixels through the
        // canvas's referencePixelsPerUnit. Ours must therefore be the OWNER's tooltip canvas's, or
        // the frame art would render with corners of a different thickness at the same box size.
        // Re-stamped in ApplySkin once the real value is sampled; 100 (Unity's default, and the
        // game's) until then.
        canvas.referencePixelsPerUnit = GameSkin.ReferencePixelsPerUnit;
        _canvas = canvas;
        Camera? head = Rig.VRRigDriver.HeadCamera != null ? Rig.VRRigDriver.HeadCamera : Camera.main;
        if (head != null)
            canvas.worldCamera = head;
        _hostRect = (RectTransform)_hostGo.transform;
        _hostRect.sizeDelta = Vector2.zero;
        _hostRect.localRotation = Quaternion.identity;
        _hostRect.localScale = Vector3.one * _metersPerUiPixel;

        // The FRAME: the game's own 9-sliced tooltip background once one can be sampled. Pivot
        // (0,0) so the area contract ("bottom-left seats MarginY above the origin, grows up and
        // right") is a single anchoredPosition write.
        var frameGo = new GameObject("Frame", typeof(RectTransform));
        frameGo.transform.SetParent(_hostGo.transform, worldPositionStays: false);
        _frame = (RectTransform)frameGo.transform;
        _frame.anchorMin = _frame.anchorMax = _frame.pivot = Vector2.zero;
        _frame.localRotation = Quaternion.identity;
        _frame.localScale = Vector3.one;
        _frameImage = frameGo.AddComponent<Image>();
        _frameImage.raycastTarget = false;   // the board is contractually INERT
        _frameImage.color = FallbackInk;

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(frameGo.transform, worldPositionStays: false);
        _label = labelGo.AddComponent<TextMeshProUGUI>();
        var labelRect = (RectTransform)labelGo.transform;
        labelRect.anchorMin = new Vector2(0f, 1f);
        labelRect.anchorMax = new Vector2(0f, 1f);
        labelRect.pivot = new Vector2(0f, 1f);
        _label.alignment = TextAlignmentOptions.TopLeft;
        _label.color = FallbackTextInk;
        _label.fontSize = FallbackFontPx;
        _label.enableAutoSizing = false;    // the FRAME fits the text, never the text the frame
        _label.enableWordWrapping = true;
        // NEVER Truncate (2026-08-25). This frame SIZES ITSELF TO ITS TEXT (see the line above:
        // "the FRAME fits the text, never the text the frame"), so Truncate here could only ever
        // fire on a tooltip the sizing had got wrong — and then it would hide that fact by dropping
        // the tail mid-word, silently, exactly as the board keycap did in ModBuild 281
        // ("AUSWAHL BEEN", user report: "Der Text muss immer voll lesbar sein"). Overflow draws the
        // whole string, so a sizing defect shows as text past the frame and gets reported as one.
        // Linted by tests/GloomhavenVR.WireTests/CapLabelFitVectors.NoTruncateModeSurvivesInTheMod.
        _label.overflowMode = TextOverflowModes.Overflow;
        _label.richText = true;             // the sender transmits the game's own markup verbatim
        _label.raycastTarget = false;

        VRLayers.Apply(_hostGo);
        // MR readability: the frame art is a GAME sprite on a GAME material, so it must never be
        // alpha-forced (that would write into a shared game asset). It gets the converted-panel
        // treatment instead -- the same opaque host plate MrBacking gives the owner's own tooltip
        // panel, behind the frame. Nothing is created while MR is off.
        WorldUI.MrBacking.Surface(this);

        _root.gameObject.SetActive(false);
    }

    /// <summary>
    /// Push the peer's tooltip text (null/empty = hidden, which is also what a sender predating
    /// record 9 -- or one whose identity gate is suppressing -- produces). Change-gated on both
    /// the active flag and the text; the layout also re-runs once, on the tick the game's own
    /// tooltip skin first becomes samplable, so a hint shown before the game UI existed upgrades
    /// from the fallback ink to the real frame art instead of staying a rectangle for its life
    /// (the decision dock's upgrade gate, same reason).
    /// </summary>
    public void Apply(string? text)
    {
        bool show = !string.IsNullOrEmpty(text);
        if (!show)
        {
            if (_shown.Length != 0)
            {
                _shown = string.Empty;
                if (_root != null && _root.gameObject.activeSelf)
                    _root.gameObject.SetActive(false);
                VRLog.Info("Net", "Remote board tooltip: hidden (record absent -- the owner's tooltip is down).");
            }
            return;
        }

        bool upgrade = !_skinApplied && GameSkin.TryEnsure();
        if (text == _shown && !upgrade)
            return;
        bool contentChanged = text != _shown;
        _shown = text!;
        // THE CORNER IS RE-MEASURED ONLY WHILE THE ROOT IS INACTIVE, and the flags below are what
        // enforce it -- see Reseat's last paragraph: our own MR backing plate is a MeshRenderer
        // parented under this root, so a walk taken while we are visible would feed our own box
        // back into the corner that seats us. ModBuild 405: the owner re-latches its seat on a
        // CONTENT change (WorldTooltips' seat latch), so a text change while this mirror is up
        // re-measures too -- root off, walk, root back on inside the same Apply, which no frame
        // renders between. A skin upgrade alone is not content and keeps the seat.
        bool comingUp = _root != null && !_root.gameObject.activeSelf;
        if (comingUp)
        {
            Reseat();
            _latchFrame = Time.frameCount;
            _latchRelatches = 0;
        }
        else if (contentChanged && _root != null)
        {
            _root.gameObject.SetActive(false);
            Reseat();
            _root.gameObject.SetActive(true);
            _latchRelatches++;
        }
        Layout(_shown, upgrade);
        if (comingUp && _root != null)
            _root.gameObject.SetActive(true);
    }

    public void Destroy()
    {
        _destroyed = true;
        if (_root != null)
            Object.Destroy(_root.gameObject);
    }

    // ------------------------------------------------------------------ layout --

    /// <summary>
    /// Seat the area origin at the board's MEASURED top-left corner -- the owner's own rule
    /// (<c>WorldTooltips.TryGetBoardAreaPose</c> -&gt; <c>PlayTray.MeasureBoardLocalExtents</c>),
    /// run against this peer's clone of the same board.
    ///
    /// <para>WHY THE AUTHORED CONSTANT IS NOT ENOUGH. This mirror used to sit at
    /// <c>PlayTray.TooltipAreaBase</c> = <c>(-BoardW x 0.5, BoardH x 0.5, -0.02)</c> outright --
    /// the AUTHORED PLATE corner, and the exact expression the owner-side fix replaced after the
    /// "still inside" report. What makes the difference today is NOT the bundled slab: the
    /// re-authored Oak/Steel/Bronze meshes measure 0.640 x 0.320, i.e. the authored plate to five
    /// decimals, so a walk over the prefab clone alone would return the constant and move nothing.
    /// It is the board's own DOCKS, which hang off the board root on the owner's board and on this
    /// mirror alike: the discard/burnt/item stacks reach x ~ 0.408 board-local (the number
    /// <c>PlayTray.BuildMounts</c>' own collision note carries), and the active-card MATRIX is
    /// docked further out again at <c>ActiveMountBase.x</c> = 0.502.</para>
    ///
    /// <para>THAT MATRIX IS THREE COLUMNS WIDE, NOT ONE. This paragraph said "its cards reaching
    /// x ~ 0.53", which is the arithmetic for a single card centred on the mount
    /// (0.502 + CardWidth/2 = 0.534) — and <c>ActivePileViewer.Columns</c> has been 3 since
    /// 85bbb8ca, with <c>RemoteActiveCards</c> reading it rather than its own hand-typed 2 since
    /// ModBuild 461. The grid is CENTRED on the mount, so a full row of three puts the rightmost
    /// card's centre one column step out: colStep = CardWidth x ActiveGridSpacing.x =
    /// 0.0635 x 1.06 = 0.0673 m at the shipped dials, and the card's right edge lands at
    /// 0.502 + 0.0673 + 0.0318 = <b>x ~ 0.601</b>. (Two cards in the row reach 0.567; one still
    /// reaches 0.534, which is why the old number looked right whenever it was checked against a
    /// single persistent card.) Well inside <c>MeasureBoardLocalExtents</c>' ceiling of
    /// 0.320 + 0.35 = 0.670, so none of it is clamped away.</para>
    ///
    /// <para>The measurement is on |x|, so a dock on the RIGHT widens the LEFT corner too: 0.408 -
    /// 0.320 = 88 mm off the pile stacks, which are always drawn, and up to 0.601 - 0.320 =
    /// <b>281 mm</b> while the owner has a full row of three active cards (214 mm with a single
    /// one — the number this note used to quote as the maximum). The owner's hint has been reading
    /// that corner since the fix; a peer's was still reading the plate.</para>
    ///
    /// <para>SCOPE -- THE WHOLE BOARD ROOT, BECAUSE THAT IS THE OWNER'S SCOPE, not a wider one:
    /// <c>WorldTooltips.TryGetBoardRoot</c> hands the measurement <c>PlayTray.Root</c>, whose
    /// children include those same pile and active mounts (<c>PlayTray.BuildMounts</c> parents
    /// both under the root and lets them host mod-owned stacks directly). The risk this scope has
    /// to answer is the mirror's OWN furniture dragging the corner somewhere the owner has no
    /// corner -- and after the rest plate went (<see cref="RemoteStatusReadouts"/>) the only piece
    /// on a peer's board with no owner-side original is the "INI" badge, which spans x +-0.055 and
    /// y 0.111..0.153: wholly inside the 0.32 x 0.16 authored plate, which is this measurement's
    /// clamp FLOOR. It therefore cannot move either extent by construction, not by luck.</para>
    ///
    /// <para>WHEN -- on build, and on every transition from hidden to shown, never while the hint
    /// is already up. That is correctness, not thrift: in MIXED REALITY this mirror's own
    /// <c>MrBacking</c> plate is a <c>MeshRenderer</c> parented under our root, hence under the
    /// BOARD root, so a walk taken while we are visible would measure our own box, push the corner
    /// up, and walk the hint up the board on every re-layout until the sanity clamp caught it. The
    /// owner's tooltip cannot do this -- their canvas is not parented to their board. Measuring
    /// only while our root is inactive removes the loop by construction (the walk skips inactive
    /// renderers), and a corner only moves when the board's DOCKS change, which no single hint
    /// outlives. ModBuild 405 adds the one re-measure the owner also does — on a CONTENT change
    /// while up — and <see cref="Apply"/> takes the root inactive around it for the same reason.</para>
    /// </summary>
    private void Reseat()
    {
        if (_root == null || _boardRoot == null)
            return;
        Cards.PlayTray.MeasureBoardLocalExtents(_boardRoot, out float topLocalY, out float halfLocalX);
        // Expressed as a correction ON the authored seat: the owner's tuned HoverHintOffset
        // (record 28, id 8) is already inside _authoredSeat and must not be re-derived here, and a
        // board with nothing measurable degrades to the authored constants -- which cancel to zero.
        _seatCorrection = new Vector3(
            Cards.PlayTray.BoardHalfWidthLocal - halfLocalX,
            topLocalY - Cards.PlayTray.BoardTopLocalY,
            0f);
        _root.localPosition = _authoredSeat + _seatCorrection;
    }

    /// <summary>
    /// CONTENT-FITTED layout, done entirely in the GAME's own uGUI pixels: measure the text at the
    /// game's type metric under <c>m_DefaultWidth</c>, size the frame to text + the tooltip's own
    /// padding, and seat the box so its BOTTOM-LEFT corner sits <see cref="MarginY"/> above the
    /// area origin. The host canvas's metre-per-pixel scale turns all of it into the owner's own
    /// metres.
    ///
    /// <para>THE FIT RULE IS THIS MIRROR'S OWN AND IT IS NOT THE GAME'S — the class doc's RESIDUE
    /// block holds the decompiled citations. The game fixes the box at the hovered target's
    /// serialized <c>width</c> and auto-fits only the HEIGHT; this hugs the text at a 257 px wrap.
    /// It is left standing deliberately rather than "corrected" to a fixed 257 px box: 257 is
    /// <c>m_DefaultWidth</c>, which is the width the game's box has only while HIDDEN, so pinning
    /// to it would trade a hugging box of roughly the right area for a fixed box of the wrong
    /// width in every case. The receiver is not told which target was hovered, so the right number
    /// cannot be derived — it has to travel. Until it does, the log line below prints what was
    /// used so the gap is a measurement rather than a guess.</para>
    /// </summary>
    private void Layout(string text, bool skinChanged)
    {
        if (skinChanged || !_skinApplied)
            ApplySkin();

        float wrapPx = GameSkin.WidthPx;
        Vector4 pad = GameSkin.PadPx;   // x = left, y = bottom, z = right, w = top

        float innerWrap = Mathf.Max(1f, wrapPx - pad.x - pad.z);
        _label.text = text;
        // Preferred size under the wrap constraint: narrow content reports its own width (the frame
        // then hugs it), long content wraps at the game's authored frame width and grows up.
        Vector2 pref = _label.GetPreferredValues(text, innerWrap, 0f);
        float textW = Mathf.Clamp(pref.x, 1f, innerWrap);
        float textH = Mathf.Clamp(pref.y, 1f, MaxTextHeightPx);
        var labelRect = (RectTransform)_label.transform;
        labelRect.sizeDelta = new Vector2(textW, textH);
        labelRect.anchoredPosition = new Vector2(pad.x, -pad.w);

        _framePx = new Vector2(textW + pad.x + pad.z, textH + pad.y + pad.w);
        _frame.sizeDelta = _framePx;
        // The area contract: bottom-left MarginY above the origin, growing up and right. MarginY is
        // the one number here that is genuinely metres, so it converts once.
        _frame.anchoredPosition = new Vector2(0f, MarginY / _metersPerUiPixel);

        VRLog.Info("Net", $"Remote board tooltip: {text.Length} chars -> frame " +
                          $"{_framePx.x:F0}x{_framePx.y:F0} px = " +
                          $"{_framePx.x * _metersPerUiPixel:F3}x{_framePx.y * _metersPerUiPixel:F3} m " +
                          $"(art={GameSkin.Describe()}, wrap {wrapPx:F0} px, font {_label.fontSize:F0} px, " +
                          $"pad {pad.x:F0}/{pad.w:F0}/{pad.z:F0}/{pad.y:F0}) — content-fitted in the " +
                          "GAME's own pixels at the owner's metre-per-pixel, bottom-left " +
                          $"{MarginY:F3} m above the area origin at board-local {_root.localPosition:F3} " +
                          $"= the authored plate corner {_authoredSeat:F3} corrected by " +
                          $"({_seatCorrection.x * 1000f:F0}, {_seatCorrection.y * 1000f:F0}) mm from " +
                          "THE BOX SIZE AND THE TYPE ARE THIS MIRROR'S OWN RULE, NOT THE OWNER'S "
                        + "(R2 F7): the game fixes its box at the hovered UITooltipTarget's "
                        + "serialized 'width' (default 100 px) and auto-fits the HEIGHT only, and "
                        + "it switches font/size/colour/lineSpacing PER LINE across Title, "
                        + "Attribute, Description and Keyword, defaulting to Attribute. Record 9 "
                        + "carries the text and nothing else, so the two DECIDING FIELDS this "
                        + "receiver would need are (a) the shown box width in authored px and (b) "
                        + "one style code per line — both sender-side, neither on the wire, and "
                        + "adding them is a protocol change. Read the 'wrap' and 'font' fields "
                        + "above as what was actually used, and compare the frame's metres against "
                        + "the owner's own tooltip in their log before spending bytes on it. "
                        + "THE CORNER IS MEASURED OVER THIS CLIENT'S OWN MIRRORED BOARD (R2 F16): "
                        + "the mm correction printed here comes from "
                        + "PlayTray.MeasureBoardLocalExtents walking THE MIRROR's subtree, while "
                        + "the owner runs the same call over THEIRS — and the two subtrees are not "
                        + "guaranteed equal, because the objectives and initiative docks are "
                        + "MirroredWidget clones only when they resolve and fall back to narrower "
                        + "mod-drawn panels otherwise, and the active-card matrix's width is a "
                        + "function of how many cards this mirror actually seated. Diff this mm "
                        + "pair against the owner's own tooltip seat line: equal means the two "
                        + "boards measured the same and the hint is at the same corner; unequal "
                        + "means the difference IS the offset, and the 'Remote board content' line "
                        + "says which dock fell back. "
                        + "the board's MEASURED extents (PlayTray.MeasureBoardLocalExtents, the " +
                          "owner's own rule — zero here means the docks reach no further than the " +
                          "authored plate). " +
                          $"LATCH (ModBuild 405): seat latched in the board's frame on frame {_latchFrame}, " +
                          $"re-latched {_latchRelatches} time(s) this shown episode (content change only; " +
                          "the board moving carries the box rigidly, nothing is re-derived per frame).");
    }

    /// <summary>Copy the sampled game presentation onto our frame + label (idempotent).</summary>
    private void ApplySkin()
    {
        if (!GameSkin.TryEnsure())
        {
            _skinApplied = false;
            return;
        }
        _skinApplied = true;

        if (_canvas != null)
            _canvas.referencePixelsPerUnit = GameSkin.ReferencePixelsPerUnit;
        _frameImage.sprite = GameSkin.Sprite;
        _frameImage.type = GameSkin.SpriteType;
        _frameImage.color = GameSkin.SpriteColor;
        _frameImage.pixelsPerUnitMultiplier = GameSkin.PixelsPerUnitMultiplier;
        if (GameSkin.SpriteMaterial != null)
            _frameImage.material = GameSkin.SpriteMaterial;

        if (GameSkin.Font != null)
            _label.font = GameSkin.Font;
        _label.fontSize = GameSkin.FontPx;
        _label.color = GameSkin.FontColor;
    }

    // ------------------------------------------------- MrBacking.IBackedSurface --

    /// <summary>The host is built in the constructor and dies with the board root it hangs under,
    /// so a Unity-null host IS this surface's death — there is no "not built yet" window to
    /// confuse it with. That matters because the board's teardown destroys the whole root without
    /// calling <see cref="Destroy"/>, and a registration that could never go dead would leak.</summary>
    bool WorldUI.MrBacking.IBackedSurface.BackingAlive => !_destroyed && _hostGo != null;

    Transform? WorldUI.MrBacking.IBackedSurface.BackingAnchor
        => _hostGo != null ? _hostGo.transform : null;

    bool WorldUI.MrBacking.IBackedSurface.BackingVisible
        => _root != null && _root.gameObject.activeInHierarchy && _shown.Length != 0;

    Vector2 WorldUI.MrBacking.IBackedSurface.BackingSize => _framePx;

    /// <summary>The frame's centre in host-pixel space (its pivot is its bottom-left corner).</summary>
    Vector2 WorldUI.MrBacking.IBackedSurface.BackingCenter
        => _frame != null
            ? _frame.anchoredPosition + _framePx * 0.5f
            : Vector2.zero;

    /// <summary>The plate shares this mirror's LIVE cluster slot (the board's sweep writes it);
    /// MrBacking's earlier renderQueue is what keeps the plate under the frame art inside that
    /// shared slot. Reading the canvas rather than a constant is what makes the plate follow the
    /// board up and down the distance ladder instead of staying behind at the old fixed tier.</summary>
    int WorldUI.MrBacking.IBackedSurface.BackingOrder => _canvas != null ? _canvas.sortingOrder : 0;

    // ------------------------------------------------------------- the game skin --

    /// <summary>
    /// The game's OWN tooltip presentation, sampled once off the live <c>UITooltip</c> every client
    /// already has (<c>CanvasManager.tooltipCanvas</c>). READ ONLY -- the widget is never moved,
    /// re-flagged or mutated; this is the <c>NativeButtonSkin</c> discipline the decision dock
    /// established in ModBuild 73, applied to the tooltip's frame instead of the button's.
    ///
    /// STATIC because it is the same widget for every remote board on this client, and because the
    /// sample must survive a board rebuild: a peer joining mid-scenario must not pay another
    /// scene-wide search. Retried on an interval while it has not landed (at main-menu bootstrap
    /// the tooltip canvas does not exist yet), never per frame.
    /// </summary>
    private static class GameSkin
    {
        /// <summary>Frames between sampling attempts while the game widget is not reachable --
        /// the same cadence <c>WorldTooltips</c> uses for its own canvas search.</summary>
        private const int RetryIntervalFrames = 15;

        private static int _nextAttemptFrame;
        private static bool _sampled;
        private static bool _loggedSample;

        internal static Sprite? Sprite { get; private set; }
        internal static Image.Type SpriteType { get; private set; } = Image.Type.Sliced;
        internal static Color SpriteColor { get; private set; } = Color.white;
        internal static Material? SpriteMaterial { get; private set; }
        internal static float PixelsPerUnitMultiplier { get; private set; } = 1f;

        internal static TMP_FontAsset? Font { get; private set; }
        internal static float FontPx { get; private set; } = FallbackFontPx;
        internal static Color FontColor { get; private set; } = FallbackTextInk;

        internal static float WidthPx { get; private set; } = FallbackWidthPx;

        /// <summary>The owner's tooltip CANVAS reference PPU — what turns a sliced sprite's border
        /// into rect pixels. 100 is Unity's (and the game's) default until the real one is read.</summary>
        internal static float ReferencePixelsPerUnit { get; private set; } = 100f;

        /// <summary>Frame padding in px, x/y/z/w = left/bottom/right/top -- the tooltip's own
        /// <c>VerticalLayoutGroup.padding</c>, which is exactly what its <c>ContentSizeFitter</c>
        /// adds around the lines.</summary>
        internal static Vector4 PadPx { get; private set; }
            = new(FallbackPadPx, FallbackPadPx, FallbackPadPx, FallbackPadPx);

        /// <summary>True once the real widget has been read. Cheap after that (one bool).</summary>
        internal static bool TryEnsure()
        {
            if (_sampled)
                return true;
            if (Time.frameCount < _nextAttemptFrame)
                return false;
            _nextAttemptFrame = Time.frameCount + RetryIntervalFrames;
            try
            {
                UITooltip? tip = FindTooltip();
                if (tip == null)
                    return false;

                var img = tip.GetComponent<Image>();
                if (img != null)
                {
                    Sprite = img.sprite;
                    SpriteType = img.type;
                    SpriteColor = img.color;
                    SpriteMaterial = img.material;
                    PixelsPerUnitMultiplier = img.pixelsPerUnitMultiplier;
                }

                // Publicized serialized fields -- the game's own authored numbers, no reflection.
                Font = tip.m_TitleFont;
                FontPx = tip.m_PCTitleFontSize > 0 ? tip.m_PCTitleFontSize : FallbackFontPx;
                FontColor = tip.m_TitleFontColor;
                WidthPx = tip.m_DefaultWidth > 1f ? tip.m_DefaultWidth : FallbackWidthPx;

                Canvas? srcCanvas = tip.GetComponentInParent<Canvas>();
                if (srcCanvas != null && srcCanvas.referencePixelsPerUnit > 0f)
                    ReferencePixelsPerUnit = srcCanvas.rootCanvas.referencePixelsPerUnit;

                var group = tip.GetComponent<VerticalLayoutGroup>();
                if (group != null && group.padding != null)
                {
                    PadPx = new Vector4(group.padding.left, group.padding.bottom,
                                        group.padding.right, group.padding.top);
                }

                _sampled = Sprite != null || Font != null;
                if (_sampled && !_loggedSample)
                {
                    _loggedSample = true;
                    VRLog.Info("Net", $"Remote board tooltip: sampled the GAME's own tooltip skin — " +
                                      $"{Describe()}, font '{(Font != null ? Font.name : "<default>")}' " +
                                      $"at {FontPx:F0} px, frame width {WidthPx:F0} px, padding " +
                                      $"{PadPx.x:F0}/{PadPx.w:F0}/{PadPx.z:F0}/{PadPx.y:F0} px, " +
                                      $"referencePixelsPerUnit {ReferencePixelsPerUnit:F0}. Peers' " +
                                      "hints now use the same background art and the same footprint " +
                                      "rule as the owner's own UITooltip (nothing on the game widget " +
                                      "was written).");
                }
                return _sampled;
            }
            catch (System.Exception ex)
            {
                VRLog.Warn("Net", $"Remote board tooltip: the game's tooltip skin could not be " +
                                  $"sampled ({ex.Message}) — peers' hints stay on the fallback ink " +
                                  "until the next attempt.");
                return false;
            }
        }

        /// <summary>The live widget, preferring the canvas the game itself parks it under. The
        /// scene-wide fallback covers a build that re-homes it.</summary>
        private static UITooltip? FindTooltip()
        {
            var manager = Object.FindObjectOfType<CanvasManager>();
            Canvas? canvas = manager != null ? manager.tooltipCanvas : null;
            UITooltip? tip = canvas != null
                ? canvas.GetComponentInChildren<UITooltip>(includeInactive: true)
                : null;
            return tip != null ? tip : Object.FindObjectOfType<UITooltip>(includeInactive: true);
        }

        internal static string Describe()
            => Sprite != null
                ? $"sprite '{Sprite.name}' ({SpriteType}, border {Sprite.border})"
                : "fallback ink (no game sprite sampled yet)";
    }
}
