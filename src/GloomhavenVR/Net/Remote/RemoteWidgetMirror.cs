using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Net;

/// <summary>
/// A LIVE, PIXEL-FAITHFUL COPY of one of the game's own uGUI panels, rendered on a peer's remote
/// control board — the initiative TRACK, the objectives/quest panel, the furniture's decision row
/// and, since 2026-09-06, the ELEMENT INFUSION BOARD. Each arrived here the same way: the user
/// rejected a mod-drawn stand-in for not looking identical, and the element board's case
/// (<see cref="RemoteElementStrip"/>) is the sharpest of the four, because its "wird erstellt" cell
/// is written entirely by GUIAnimator curves that live in prefab scene data — there is no state a
/// composition could have read.
///
/// ─── THE CLAIM THIS CLASS RETIRES ──────────────────────────────────────────────────────────────
/// <see cref="RemoteBoardContent"/> used to state: "the game instantiates exactly ONE objectives
/// container, ONE infusion board and ONE initiative track per client … A canvas cannot be in two
/// places at once, and re-parenting or duplicating a live game canvas would violate the module's
/// reversibility rule. So a peer's board draws its OWN picture from the same model data." The first
/// half is true and the conclusion does not follow — it is the same mistake
/// <see cref="RemoteAbilityCardSource"/> already retired for card faces:
///
///   • RE-PARENTING a live game canvas is indeed forbidden (a tray teardown must never cascade into
///     destroying game-owned UI). DUPLICATING it is not: <c>Object.Instantiate</c> reads the source
///     and writes a brand-new object tree. The source is not touched, not moved, not re-flagged —
///     exactly the guarantee <see cref="RemoteCardArt"/> has been shipping for round-card faces.
///   • <c>Instantiate</c> copies the LIVE component state, not the prefab's serialized state, so a
///     portrait's <c>RawImage.texture</c> (assigned at runtime by <c>CharacterPortraitsProvider</c>
///     out of the <c>misc_characterportraits</c> bundle), a filled progress bar and the localized
///     TMP strings all come across. That is why the remote board no longer has to "not reproduce
///     the portrait" — the portrait comes for free.
///
/// The consequence: the mod-drawn green/red name chips and the mod-drawn objective rows — the two
/// placeholders the user rejected in round 3 of the 1:1-parity request ("Initiativleiste … nur
/// grüne und rote Rechtecke", "Aufgaben immer noch falsch") — are demoted to FALLBACKS, and what a
/// peer's board shows is the game's own widget, at the game's own detail, in the game's own layout.
///
/// ─── PUPPET, NOT PROGRAM: WHY THE CLONE RUNS NO GAME CODE ──────────────────────────────────────
/// A deep clone of a live UI subtree carries the game's MonoBehaviours along. Letting them wake up
/// would be a real hazard: <c>InitiativeTrackActorBehaviour</c>, <c>MissionObjectiveUI</c> and their
/// neighbours register themselves with singletons, subscribe to game events and mutate the very
/// state the original widget is driven from. So the clone is built under an INACTIVE host (its
/// <c>Awake</c> has therefore not run — the identical trick <see cref="RemoteCardArt"/> uses to
/// strip <c>CardEffects</c>) and every component that is not pure presentation is destroyed before
/// it can ever execute a line. What survives is the whitelist in <see cref="IsPresentation"/>:
/// <c>Graphic</c> (Image / RawImage / Text / TMP), <c>CanvasRenderer</c>, <c>Mask</c> /
/// <c>RectMask2D</c>, mesh effects and <c>CanvasGroup</c>. Layout groups and size fitters go too —
/// they would fight the puppeteering below, and the ONE property they contest is the rect, which is
/// why the single dock that needs them back (<see cref="LayoutOwner.CloneAtBoardOwnersWidth"/>)
/// keeps them and stands the rect drive down instead. <c>Canvas</c>, <c>CanvasScaler</c> and
/// <c>GraphicRaycaster</c> go unconditionally, because the host supplies the one world-space canvas
/// and a copied screen-space canvas would otherwise blit itself over the player's whole view.
///
/// What is left cannot act, so it has to be DRIVEN. Every tick this class walks a pair of flat,
/// pre-paired arrays (source node ⇄ clone node, built once) and copies the presentation state:
/// active flag, rect pose/size, graphic colour + enabled, sprite/texture/fill, TMP and legacy text,
/// canvas-group alpha. That is what makes "alle Positionen, Animationen, Effekte" literally true —
/// the clone reproduces the source's CURRENT layout each frame, including the initiative track's
/// inter-round reorder slide and the selected-portrait pop, without any of the code that produces
/// them running twice.
///
/// STRUCTURE CHANGES (a round ends, an objective is added or removed) are detected on the content
/// cadence by re-walking the source and comparing it node-for-node against the cached pairing; a
/// mismatch rebuilds the clone from scratch. Cheap, because it only happens when the panel itself
/// changes shape.
///
/// ─── INERT ─────────────────────────────────────────────────────────────────────────────────────
/// Nothing here can be interacted with, and it is not a matter of trust: every
/// <c>GraphicRaycaster</c>, <c>Selectable</c> and game click handler is DESTROYED (not disabled)
/// before the clone activates, a blocking <see cref="CanvasGroup"/> is added at the root, and any
/// <c>Collider</c>/<c>Rigidbody</c> that ever came along is destroyed as well. The board's own
/// <see cref="RemoteBoardFurniture.StripColliders"/> sweep then re-proves it at runtime.
///
/// ─── ANTI-CHEAT ────────────────────────────────────────────────────────────────────────────────
/// Both panels this drives are GLOBAL scenario state that is bit-identical on every client and is
/// ALREADY on the local player's own screen — the initiative track and the objectives list are
/// literally the same singletons the local board docks. Rendering the same pixels a second time at
/// a peer's board pose reveals exactly nothing new, which is why this class carries no gate: it
/// mirrors what the local client is already allowed to see, including vanilla's own "?" for a
/// foreign player's hidden initiative. It never reads a card identity and never touches the wire.
///
/// ─── MIXED REALITY (user report 2026-08-08) ────────────────────────────────────────────────────
/// "Die Mixed-Reality-Hintergründe sollen auch für das Remote-Board genauso angezeigt werden, wenn
/// Mixed Reality eingeschaltet ist — aktuell sind die Hintergründe nur auf meinem eigenen Board
/// sichtbar." The asymmetry was structural. On the OWNER's board these two panels are CONVERTED
/// panels, so <c>WorldUI.MrBacking</c>'s panel sweep (which enumerates
/// <c>CanvasConversion.ActivePanels</c>) puts an opaque plate behind them the moment MR turns on.
/// A peer's copy is this clone on OUR OWN world canvas — never a <c>ConvertedPanel</c>, never in
/// that list — so the identical pixels floated bare over the passthrough room while the owner's
/// copy sat on a solid plate. The mirror therefore registers itself as an
/// <c>MrBacking.IBackedSurface</c> and reports the geometry its own fit pass already measures
/// (<see cref="Fit"/> caches <c>_backingSizePx</c>); MrBacking builds, sizes, orders, shows and
/// tears down the very same plate it gives a converted panel. NON-MR RENDERING IS UNCHANGED: no
/// plate object is ever created while MR is off.
/// </summary>
/// <remarks>CLASSIFICATION: GLOBAL — ZERO wire. It renders a CLONE of a game-owned, scenario-wide
/// canvas that the local client already displays. No packet, no per-actor read, no gate of its own.
/// See INVARIANTS-Net-Rig.md "Net — content classification".</remarks>
internal sealed class RemoteWidgetMirror : WorldUI.MrBacking.IBackedSurface
{
    /// <summary>Which mechanism a mirrored section is currently drawing with — reported per section
    /// in the <c>Remote board content</c> diagnostic so a hardware log PROVES parity instead of
    /// merely proving that something was drawn.</summary>
    internal enum Fidelity
    {
        /// <summary>Nothing drawn (source absent and the caller drew no fallback either).</summary>
        None,

        /// <summary>The REAL game widget, cloned and live-driven — full parity.</summary>
        MirroredWidget,

        /// <summary>The mod-drawn stand-in, because the game widget could not be resolved.</summary>
        ModDrawn,
    }

    /// <summary>
    /// WHOSE RESOLVED LAYOUT DOES THE CLONE SHOW — the question, not the mechanism, because the
    /// answer decides four separate things at once (which components survive
    /// <see cref="Neutralize"/>, whether <see cref="Pair.Apply"/> copies rects, which measure
    /// <see cref="TryMeasureDock"/> commits, and whether <see cref="TryFrameExtent"/> clamps).
    ///
    /// <para>Member 0 is TODAY'S BEHAVIOUR and the stricter one on purpose: a call site that does
    /// not think about this gets the faithful source copy, never a mirror that re-runs a layout
    /// engine on a cloned game panel. Deliberately an enum rather than a bool for the same reason
    /// <c>CardDustFx.Permission</c> and <c>RemoteAvatar.HeldCardSizing</c> are — the two values are
    /// not "on/off", they are two different answers to one question, and a bool at a call site
    /// reads as neither.</para>
    /// </summary>
    internal enum LayoutOwner
    {
        /// <summary>
        /// THE SOURCE'S. The clone is a puppet: its layout engine is destroyed and every node's
        /// rect is copied from the live source each tick, so the picture is the one THIS client's
        /// own copy of the widget resolved. Correct for anything whose geometry IS the animation —
        /// the initiative track's inter-round reorder slide is literally a stream of
        /// <c>anchoredPosition</c> writes, and re-deriving it would be re-implementing it.
        /// </summary>
        Source,

        /// <summary>
        /// THE CLONE'S OWN, RUN AT THE BOARD OWNER'S CONTENT WIDTH. The game's layout components
        /// (<c>LayoutGroup</c> / <c>ContentSizeFitter</c> / <c>LayoutElement</c>, and ONLY those)
        /// survive on the clone, this class writes the owner's forced wrap column onto the clone
        /// root, and the rect half of the drive stands down so the two cannot fight. The source
        /// still owns every piece of CONTENT (text, colour, sprite, fill, texture, alpha,
        /// material); the clone owns its own GEOMETRY. See <see cref="ApplyOwnersColumn"/> for the
        /// full derivation and the evidence.
        /// </summary>
        CloneAtBoardOwnersWidth,
    }

    // ---------------------------------------------------------------- fit budget (shared) --

    /// <summary>Lower clamp on the dock fit — verbatim <c>TrayMountedPanelSurface.MinDensityScale</c>.
    /// A mis-measured panel overflows its dock a little rather than shrinking below readability.</summary>
    private const float MinDensityScale = 0.5f;

    /// <summary>Upper clamp on the dock fit — verbatim <c>TrayMountedPanelSurface.MaxDensityScale</c>.
    /// A SMALL source keeps its content-true size instead of ballooning to fill the dock.</summary>
    private const float MaxDensityScale = 1f;

    /// <summary>Below this many uGUI pixels a measured panel counts as degenerate (mid-layout) and
    /// the fit is skipped for this tick rather than applied to nonsense.</summary>
    private const float MinMeasuredPixels = 1f;

    /// <summary>Effective-alpha floor a graphic must clear to size the fallback union —
    /// <c>CanvasConversion.FitMinAlpha</c> itself, the value the LOCAL dock's own content fit uses.
    /// It used to be a literal "for the same reason the density clamps above are: this file must not
    /// widen WorldUI's internal surface to read three constants" — but that reason never covered
    /// THIS constant: the density clamps really are private to <c>TablePanelSurfaces</c>, while
    /// <c>FitMinAlpha</c> has been <c>internal</c> since ModBuild 291 and needs no widening at all.
    /// So this one is the alias and the two above stay mirrors (ModBuild 439, survey row R27). A
    /// mirrored panel and its owner now cannot disagree about which graphic paints, which is the
    /// 1:1 ruling in the one term of the fit that was still copied.</summary>
    private const float FitMinAlpha = WorldUI.CanvasConversion.FitMinAlpha;

    // ---------------------------------------------------------------- built state --

    private readonly string _name;
    private readonly Transform _mount;      // board-local mount anchor (owned by the caller)
    private readonly float _mountWidth;     // tray-local metres
    private readonly float _mountMaxHeight; // tray-local metres
    private readonly Vector2 _grow;         // which way the panel extends from the mount origin
    private readonly bool _fitWidth;        // does the width budget take part in the uniform fit?

    /// <summary>Per-panel multiplier on the shared tray density — verbatim
    /// <c>TrayMountedPanelSurface.DensityScale</c>, which the objectives dock overrides to 0.6
    /// (same content pixels onto ~1.67× more tray metres). A mirror that ignored it would draw a
    /// peer's objectives at 60 % of the size their owner reads them at.</summary>
    private readonly float _densityScale;

    /// <summary>See the constructor's <c>driveFromSource</c> parameter: false turns the per-refresh
    /// source copy off entirely, for a source that is static and whose visible state is driven by
    /// the caller instead.</summary>
    private readonly bool _driveFromSource;

    /// <summary>Whose resolved layout this mirror shows — see <see cref="LayoutOwner"/>. Every
    /// behaviour change this flag buys is written as <c>if (_layoutOwner == ...)</c> and nothing
    /// else reads it, so <see cref="LayoutOwner.Source"/> (member 0, the default) is the code path
    /// that shipped before the flag existed.</summary>
    private readonly LayoutOwner _layoutOwner;

    /// <summary>
    /// WHICH SOURCE NODES HEAD A BRANCH WHOSE VISIBILITY THIS CLASS DOES NOT DECIDE — null (the
    /// default) when every branch's <c>activeSelf</c> is the source's own answer, which is what the
    /// class assumed for its whole life. See <see cref="Pair.External"/> for the defect that
    /// assumption shipped.
    /// </summary>
    private readonly System.Func<Transform, bool>? _externalBranch;

    /// <summary>
    /// THE ONE PLACE THIS FILE TURNS TRAY METRES INTO uGUI PIXELS — <c>TrayPixelsPerMeter</c> ×
    /// this dock's own density scale, verbatim <c>TrayMountedPanelSurface</c>'s
    /// <c>density</c> local and verbatim the one <c>ObjectivesSurface.ApplyContentWidth</c>
    /// multiplies its budget by.
    ///
    /// <para>A PROPERTY RATHER THAN THREE COPIES OF THE EXPRESSION, because the wrap column
    /// <see cref="ApplyOwnersColumn"/> writes and the budget <see cref="TryMeasure"/> screens
    /// against must be the SAME number as the one <see cref="Fit"/> divides by. Two of the three
    /// used to be written out inline; a third inline copy is exactly how two surfaces drift apart
    /// the day one of them is retuned.</para>
    /// </summary>
    private float ContentDensity => PlayTray.TrayPixelsPerMeter * _densityScale;

    private GameObject? _host;              // world-space canvas host (child of _mount)
    private Canvas? _canvas;                // the host's canvas — its LIVE cluster slot backs the MR plate
    private RectTransform? _pivot;          // recentring frame (child of _host); see EnsureHost
    private GameObject? _clone;             // the cloned subtree (child of _pivot)
    private RectTransform? _cloneRect;

    /// <summary>Pre-paired presentation nodes, index-aligned: <c>_pairs[i].Src</c> drives
    /// <c>_pairs[i].Dst</c>. Index 0 is the ROOT pair, which differs in exactly one way — it keeps
    /// its own active flag (see <see cref="Pair.Apply"/>).</summary>
    private Pair[] _pairs = System.Array.Empty<Pair>();

    /// <summary>Reusable walk buffers — the structural re-validation runs on the content cadence and
    /// must not allocate once warm.</summary>
    private readonly List<Transform> _walk = new(256);
    private readonly List<Transform> _stack = new(64);

    /// <summary>Depth of each node of the last <see cref="Walk"/>, index-aligned with
    /// <see cref="_walk"/>. Feeds the subtree extents that make the per-frame drive skippable — see
    /// <see cref="Sync"/>.</summary>
    private readonly List<int> _walkDepth = new(256);

    /// <summary>Depth scratch for <see cref="Walk"/>, parallel to <see cref="_stack"/>.</summary>
    private readonly List<int> _stackDepth = new(64);

    /// <summary>The source subtree this clone was built from (Unity-null aware).</summary>
    private Transform? _source;

    /// <summary>What the mirror is currently showing (never <see cref="Fidelity.ModDrawn"/> — that
    /// verdict belongs to the caller, which owns the fallback).</summary>
    public Fidelity State { get; private set; } = Fidelity.None;

    /// <summary>Why the mirror is not showing the real widget, for the diagnostic line. Empty while
    /// it is.</summary>
    public string Reason { get; private set; } = "not built";

    /// <summary>Which measure produced the last applied fit ("converted host rect" = the OWNER's
    /// own dock rect, "graphics union" = this class's fallback). Surfaced in the per-peer board
    /// content line so a "the panel sits too high" report is answerable from the log alone.</summary>
    public string MeasurePath => _measurePath;

    /// <summary>Size of the last applied fit in MOUNT-LOCAL METRES (zero until the first successful
    /// fit). What a caller stacking content below this panel measures from — see <see cref="Fit"/>.</summary>
    public Vector2 FittedSize { get; private set; }

    /// <summary>Bumps every time the clone is rebuilt from scratch (structure change / new
    /// source). The cache-invalidation key for anything holding <see cref="CloneOf"/> results —
    /// a holder re-resolves when this moves, and never dereferences a node of a dead clone.</summary>
    public int RebuildStamp { get; private set; }

    /// <summary>
    /// The CLONE node paired with source node <paramref name="src"/>, or null when the mirror is
    /// down or the node is not part of the mirrored subtree.
    ///
    /// THE SEAM FOR STATE OVERRIDES: the drive (<see cref="Pair.Apply"/>) is deliberately a
    /// faithful copy of the source, but some source state is LOCAL-PLAYER state that must not be
    /// mirrored onto a peer's board — the initiative track's hover artefacts are the shipped
    /// case (defect (b) of the initiative-mouseover report: MY hover popup showed on the PEER's
    /// mirrored track). A caller resolves the affected clone nodes through this, then overrides
    /// them AFTER each Sync — the drive re-copies, the override re-corrects, both change-gated.
    /// Linear scan over the pre-paired arrays; cache the result keyed on
    /// <see cref="RebuildStamp"/> rather than calling this per frame.
    /// </summary>
    public Transform? CloneOf(Transform? src)
    {
        if (src == null || _clone == null)
            return null;
        Pair[] pairs = _pairs;
        for (int i = 0; i < pairs.Length; i++)
        {
            if (ReferenceEquals(pairs[i].Src, src))
                return pairs[i].Dst;
        }
        return null;
    }

    /// <summary>
    /// Create a mirror that will host its clone under <paramref name="mount"/> (a board-local
    /// anchor the CALLER owns and positions from the authored per-style layout), fitted into
    /// <paramref name="mountWidth"/> × <paramref name="mountMaxHeight"/> tray-local metres and
    /// growing along <paramref name="grow"/> from the mount origin — the same three numbers the
    /// LOCAL board's <c>TrayMountedPanelSurface</c> docks the very same panel with.
    ///
    /// <paramref name="fitWidth"/> mirrors <c>TrayMountedPanelSurface.FitWidthToMount</c>: false for
    /// a panel whose CONTENT is already forced to the width budget (the objectives container is —
    /// <c>ObjectivesSurface.ApplyContentWidth</c> writes the budget onto its root as a pixel width),
    /// because re-fitting a width the content already satisfies only re-scales the glyphs by the
    /// constant header overhang. That was a real, hardware-diagnosed defect on the LOCAL panel; a
    /// mirror that fitted differently would render the same content at a different size.
    /// </summary>
    /// <param name="densityScale">Mirror of <c>TrayMountedPanelSurface.DensityScale</c> — 1 for
    /// every dock except the objectives, which renders at 0.6 (bigger glyphs on more tray metres).</param>
    /// <param name="driveFromSource">Whether the clone is PUPPETED from the live source every
    /// refresh (the default, and what the initiative track and objectives need — their source
    /// animates). FALSE for a source that is a STATIC, HIDDEN panel whose visible state comes from
    /// somewhere else entirely: the mirrored decision row's source is this client's own
    /// <c>TakeDamagePanel</c>, which the game populated for a DIFFERENT player's decision and then
    /// hid, so every number on it is stale and every fact a viewer must see arrives on the wire
    /// (see <see cref="RemoteDecisionWidgets"/>). Copying that source would do nothing but fight the
    /// caller's own wire-driven overrides, several times a second, dirtying this board's canvas each
    /// time. <c>Instantiate</c> has already carried the authored layout across, which is the only
    /// thing the copy would have contributed.</param>
    /// <param name="layoutOwner">Whose resolved layout the clone shows — see <see cref="LayoutOwner"/>.
    /// The default is the source's, i.e. everything this class did before the flag existed. Only a
    /// dock whose content width is FORCED from a synced dial asks for
    /// <see cref="LayoutOwner.CloneAtBoardOwnersWidth"/>, and today that is the objectives panel
    /// alone.</param>
    /// <param name="externallyShownBranch">Which SOURCE nodes head a branch whose visibility the
    /// CALLER decides — see <see cref="Pair.External"/>. Evaluated once per clone REBUILD, on the
    /// source and on the clone, never per frame. Null (the default) is every mirror that has no
    /// such branch, i.e. the behaviour that shipped before this parameter existed.</param>
    public RemoteWidgetMirror(string name, Transform mount, float mountWidth, float mountMaxHeight,
        Vector2 grow, bool fitWidth = true, float densityScale = 1f, bool driveFromSource = true,
        LayoutOwner layoutOwner = LayoutOwner.Source,
        System.Func<Transform, bool>? externallyShownBranch = null)
    {
        _layoutOwner = layoutOwner;
        _externalBranch = externallyShownBranch;
        _driveFromSource = driveFromSource;
        _name = name;
        _mount = mount;
        _mountWidth = mountWidth;
        _mountMaxHeight = mountMaxHeight;
        _grow = grow;
        _fitWidth = fitWidth;
        _densityScale = densityScale > 0f ? densityScale : 1f;
        // MR readability: claim the converted-panel treatment the owner's own copy of this panel
        // gets for free (see the class doc's MIXED REALITY block). Registration is MR-agnostic and
        // costs one list entry — no plate exists until MR is actually on.
        WorldUI.MrBacking.Surface(this);
    }

    // ------------------------------------------------- MrBacking.IBackedSurface --

    /// <summary>Content extent of the last applied fit, in HOST-LOCAL units (uGUI pixels — the host's
    /// own <c>metersPerPx</c> scale carries them into board metres, exactly as a converted panel's
    /// host rect does). Zero until the first successful fit, which reads as "nothing to back".</summary>
    private Vector2 _backingSizePx;

    /// <summary>
    /// The AUTHORED PIXEL SIZE the last applied fit measured — the number <see cref="Fit"/> divides
    /// the mount budget by, i.e. the emulated host rect (union, framed and padded exactly as
    /// <c>CanvasConversion.TryMeasureContent</c> would). Zero until the first successful fit.
    ///
    /// <para>Published so an instrument can state it beside <see cref="FittedSize"/> and be diffed
    /// against the OWNER's own <c>px</c> / <c>mount</c> pair — see the short-rest 1:1 line in
    /// <see cref="RemoteDecisionWidgets"/>. Read-only: nothing outside this class may write a
    /// measure.</para>
    /// </summary>
    public Vector2 MeasuredSizePx => _backingSizePx;

    /// <summary>Set by <see cref="Destroy"/> — the ONLY prune signal MrBacking honours, because a
    /// null host legitimately means "not built yet" for a mirror registered in its constructor.</summary>
    private bool _destroyed;

    bool WorldUI.MrBacking.IBackedSurface.BackingAlive => !_destroyed;

    Transform? WorldUI.MrBacking.IBackedSurface.BackingAnchor
        => _host != null ? _host.transform : null;

    /// <summary>Backed only while the clone is genuinely on screen: the caller hides the mirror
    /// (<see cref="SetShown"/>) whenever it falls back to its mod-drawn stand-in, and THAT surface
    /// carries its own MR treatment. An opaque plate left standing behind a hidden mirror would be
    /// a dark rectangle floating on the peer's board.</summary>
    bool WorldUI.MrBacking.IBackedSurface.BackingVisible
        => _host != null && _host.activeInHierarchy && _clone != null;

    Vector2 WorldUI.MrBacking.IBackedSurface.BackingSize => _backingSizePx;

    /// <summary>Zero by construction: <see cref="Fit"/> re-centres the measured content on the host
    /// origin by moving the pivot, so the host origin IS the content centre.</summary>
    Vector2 WorldUI.MrBacking.IBackedSurface.BackingCenter => Vector2.zero;

    /// <summary>The plate shares the mirror canvas's own ladder slot; MrBacking's earlier
    /// renderQueue is what keeps it under this content and above everything farther back. Read
    /// LIVE off the canvas since 2026-08-09: the slot is now the board cluster's, and it moves
    /// with the board on the distance ladder.</summary>
    int WorldUI.MrBacking.IBackedSurface.BackingOrder => _canvas != null ? _canvas.sortingOrder : 0;

    /// <summary>The ONE world-space canvas that draws this mirror's whole clone (see
    /// <see cref="Neutralize"/>: every cloned Canvas is destroyed, so there is exactly one). Read
    /// only — the board's cluster sweep is its single writer. Exposed so a caller that must seat a
    /// POPUP of its own relative to this mirror's live cluster slot can read that slot rather than
    /// guess at it (<c>RemoteInitiativeTrack.SeatPopupOverlay</c>).</summary>
    internal Canvas? HostCanvas => _canvas;

    /// <summary>
    /// Content-cadence entry point: (re)build the clone when <paramref name="source"/> changed
    /// identity or shape, then re-fit it. Returns true iff the real widget is being mirrored — a
    /// false return means the caller must draw (and show) its own fallback.
    ///
    /// Wrapped whole: a half-built panel, a destroyed singleton or a hostile prefab must degrade to
    /// "no mirror", never take down the remote-avatar tick this runs inside.
    /// </summary>
    public bool Refresh(Transform? source)
    {
        try
        {
            if (source == null)
            {
                Clear("the game widget does not exist on this client yet");
                return false;
            }

            if (!ReferenceEquals(source, _source) || _clone == null || !StructureMatches(source))
            {
                if (!Rebuild(source))
                    return false;
            }

            if (_driveFromSource)
                Sync();
            Fit();
            AuditCloneGrowth();

            // (1) KEEP THE CALLER'S FALLBACK REACHABLE. Under LayoutOwner.CloneAtBoardOwnersWidth
            // the committed geometry is re-derived from this class's own union rather than read off
            // the owner's dock (see TryMeasureDock), so "the clone exists" no longer implies "the
            // clone is presentable". A false return here hands the panel back to its mod-drawn rows
            // — a peer reading plain rows beats a peer reading a cropped or unfitted clone — and the
            // clone is KEPT, not destroyed, so recovery costs a re-measure rather than an
            // Instantiate and a rebuild loop is impossible.
            if (_layoutOwner == LayoutOwner.CloneAtBoardOwnersWidth)
            {
                string? withhold = _withhold ?? (_fitApplied
                    ? null
                    : "the re-wrapped clone has not been fitted yet (no measure has been committed "
                      + "to it), so showing it would draw the panel at the host's identity scale");
                if (withhold != null)
                {
                    Withhold(withhold);
                    return false;
                }
            }

            State = Fidelity.MirroredWidget;
            Reason = string.Empty;
            return true;
        }
        catch (System.Exception e)
        {
            Clear($"mirror failed ({e.Message})");
            return false;
        }
    }

    /// <summary>
    /// Per-FRAME puppeteering: copy the source's current presentation state onto the clone. This is
    /// what carries the ANIMATIONS (the initiative track's reorder slide, the selected-portrait pop,
    /// a progress bar filling) — a clone refreshed only on the 4 Hz content cadence would step
    /// through them. Pure array walk over pre-resolved component references: no allocation, no
    /// <c>GetComponent</c>, no hierarchy search.
    /// </summary>
    public void TickLive()
    {
        if (_clone == null || _pairs.Length == 0 || !_driveFromSource)
            return;
        try { Sync(); }
        catch (System.Exception e) { Clear($"live sync failed ({e.Message})"); }
    }

    /// <summary>Hide the mirror (the caller's fallback takes over) without destroying the clone, so
    /// a transient source outage does not cost a rebuild.</summary>
    public void SetShown(bool shown)
    {
        if (_host != null && _host.activeSelf != shown)
            _host.SetActive(shown);
    }

    public void Destroy()
    {
        _destroyed = true;   // MrBacking prunes its plate on the next tick / next registration
        _backingSizePx = Vector2.zero;
        DestroyClone();
        if (_host != null)
        {
            Object.Destroy(_host);
            _host = null;
            _pivot = null;
        }
    }

    // ------------------------------------------------------------------ build --

    /// <summary>Drop the clone and record WHY, so the diagnostic line can state the reason rather
    /// than leaving a reader to guess between "absent", "failed" and "not implemented".</summary>
    private void Clear(string reason)
    {
        DestroyClone();
        SetShown(false);
        State = Fidelity.None;
        Reason = reason;
    }

    /// <summary>Stop SHOWING the clone and say why, without destroying it — the difference from
    /// <see cref="Clear"/>, and the whole reason this exists separately. A withheld mirror is one
    /// whose picture failed a sanity check, not one whose source went away: the next content tick
    /// re-measures the clone it already has, and recovers the moment the measure is sane again.
    /// Destroying it instead would turn a transient bad measure into an Instantiate every 250 ms.
    /// </summary>
    private void Withhold(string reason)
    {
        SetShown(false);
        State = Fidelity.None;
        Reason = reason;
    }

    private bool Rebuild(Transform source)
    {
        DestroyClone();
        EnsureHost();
        if (_host == null || _pivot == null)
            return false;

        // A REBUILD lands in an ALREADY ACTIVE host, and Instantiate into an active hierarchy wakes
        // the clone's components on the spot — which is the one thing Neutralize must get in front
        // of. Park the host inactive for the duration; it is re-activated at the end.
        _host.SetActive(false);

        // The pivot inherits the SOURCE PARENT's rect size, so the clone's own anchors resolve
        // against an identically sized frame and its authored layout survives the move (see
        // EnsureHost / Fit). Set before the clone exists so no frame sees the wrong frame size.
        AdoptParentRect(source);

        // Build under the INACTIVE host: the cloned game components never reach Awake, so
        // destroying them below is side-effect free (RemoteCardArt's proven ordering).
        var clone = Object.Instantiate(source.gameObject, _pivot, worldPositionStays: false);
        clone.name = $"{_name}Clone";
        _clone = clone;
        _source = source;

        Neutralize(clone, _layoutOwner, _externalBranch);

        _cloneRect = clone.transform as RectTransform;
        if (_cloneRect == null)
        {
            Clear("the game widget's root is not a RectTransform (unexpected prefab shape)");
            return false;
        }

        // (d) FIRST, AND BEFORE ANY WIDTH IS WRITTEN — see LatchFrameDegenerate. The verdict is
        // about the AUTHORED rect, and this is the last moment at which the clone still carries
        // one: ApplyOwnersColumn below overwrites it.
        LatchFrameDegenerate();
        _cloneFitter = _layoutOwner == LayoutOwner.CloneAtBoardOwnersWidth
            ? _cloneRect.GetComponent<ContentSizeFitter>()
            : null;

        // Pair the two subtrees BEFORE activation: the walk order is deterministic (depth-first,
        // sibling order) and Instantiate preserves hierarchy shape, so index i on the source is
        // index i on the clone. Pairing after activation would race TMP, which spawns its own
        // sub-mesh children on first render and would offset one side of the pairing.
        Walk(source, _walk);
        int n = _walk.Count;
        var srcNodes = new Transform[n];
        _walk.CopyTo(srcNodes);
        Walk(clone.transform, _walk);
        if (_walk.Count != n)
        {
            Clear("the clone's shape does not match the source (Instantiate anomaly)");
            return false;
        }

        // Subtree extents FIRST: they are read off the clone walk that is still in _walkDepth, and
        // they are what lets the per-frame drive jump over a switched-off branch (see
        // BuildSubtreeExtents). Built once per rebuild, never per frame.
        var skipTo = new int[n];
        BuildSubtreeExtents(skipTo);

        _pairs = new Pair[n];
        for (int i = 0; i < n; i++)
            _pairs[i] = new Pair(srcNodes[i], _walk[i], skipTo[i]);
        // THE BRANCHES THIS CLASS DOES NOT OWN THE VISIBILITY OF — latched through the array
        // element for the same reason Suppress() is (see the Pair remarks). Evaluated on the
        // SOURCE: Neutralize already ran on the clone and took the game components the predicate
        // asks about with it.
        int external = 0;
        if (_externalBranch != null)
        {
            for (int i = 0; i < n; i++)
            {
                if (srcNodes[i] == null || !_externalBranch(srcNodes[i]))
                    continue;
                _pairs[i].MarkExternal();
                external++;
            }
        }
        int secret = SuppressSecretBranches(clone.transform);
        RebuildStamp++; // CloneOf holders must re-resolve against the fresh clone
        // A clone rebuild is an Instantiate of a whole game panel plus a full re-pair — the single
        // most expensive thing this class can do. Counting it makes "the mirror is REBUILDING, not
        // just driving" a number in the log instead of an inference from a log-line histogram.
        Core.PerfMonitor.Count("Mirror.CloneRebuilds");

        // Own head camera renders the mod layer only; the whole clone is ours, so re-layering it is
        // safe (and required — the game face was on a game UI layer).
        VRLayers.Apply(_host);

        _host.SetActive(true);

        // (a) LAST, AND ONLY AFTER ACTIVATION. LayoutRebuilder strips disabled behaviours from its
        // own work list (UnityEngine.UI.LayoutRebuilder.StripDisabledBehavioursFromList drops every
        // component whose isActiveAndEnabled is false), so a forced rebuild on the still-inactive
        // host above would have been a silent no-op and the rows would have kept the viewer's
        // column — the exact class of "the remedy never ran" this project has shipped before.
        ApplyOwnersColumn();

        VRLog.Info("Net", $"Remote board '{_name}': now mirroring the REAL game widget " +
                          $"('{source.name}', {n} node(s)) — a live CLONE of the panel this client " +
                          "already shows, driven per frame from the original (positions, portraits, " +
                          "text, progress, animations). Zero wire traffic; the source is never " +
                          "touched, re-parented or mutated." +
                          (secret > 0
                              ? $" {secret} node(s) carrying a PER-CHARACTER SECRET goal were " +
                                "suppressed on the clone (see SuppressSecretBranches)."
                              : string.Empty) +
                          (external > 0
                              ? $" {external} branch(es) are EXTERNALLY SHOWN — the caller owns " +
                                "their active flag and the clone owns their geometry (see " +
                                "Pair.External)."
                              : string.Empty));
        return true;
    }

    /// <summary>
    /// THE SECRECY NET on the clone: permanently kill any branch that renders a PER-CHARACTER
    /// SECRET goal, so no future prefab reshuffle can carry one onto a peer's board by accident.
    ///
    /// WHY IT EXISTS AT ALL, given the two widgets this class currently mirrors (the initiative
    /// track and <c>MissionObjectiveContainer</c>) contain none. The mirror's whole premise is
    /// "the pixels being copied are pixels this client is already displaying, so copying them
    /// reveals nothing new". That premise holds for GLOBAL widgets and breaks the moment a
    /// mirrored widget contains something the GAME itself only draws for the LOCAL player —
    /// because then the copy renders MY entitlement at a PEER's pose, and a viewer who later
    /// focuses a foreign character would see a goal the game deliberately hides from them.
    /// Researched 2026-08-08 from the game's own code, and the rule is not symmetric:
    ///   • BATTLE GOAL — <c>UIScenarioPlayerBattleGoal</c> / <c>UIBattleGoalProgress</c>. HARD
    ///     SECRET online: <c>ActorStatPanel.cs:566</c> and <c>BattleGoalContainer.cs:44/73/81</c>
    ///     both gate it on <c>!FFSNetwork.IsOnline || actor.IsUnderMyControl</c>.
    ///   • PERSONAL QUEST — <c>UIPersonalQuestProgress</c>. Public BY DEFAULT; secret only when its
    ///     owner ticked conceal (<c>ActorStatPanel.cs:557</c>). Suppressed here anyway, because a
    ///     CLONE cannot re-evaluate <c>IsConcealed</c> per viewer and a mirror that shows a
    ///     concealed quest would be the exact leak this method exists to make impossible. If a
    ///     future surface wants to draw one, it asks <see cref="RevealGate.ShowPersonalQuest"/> for
    ///     the character it is about and draws it itself — that is the supported path.
    /// See <see cref="RevealGate"/> for the full evidence block.
    ///
    /// Cost: one <c>GetComponentsInChildren</c> per component type per clone REBUILD (never per
    /// frame), and on both widgets that ships today the result is zero hits.
    /// </summary>
    private int SuppressSecretBranches(Transform cloneRoot)
    {
        if (_pairs == null)
            return 0;
        int killed = 0;
        killed += Suppress(cloneRoot.GetComponentsInChildren<UIScenarioPlayerBattleGoal>(true));
        killed += Suppress(cloneRoot.GetComponentsInChildren<UIBattleGoalProgress>(true));
        killed += Suppress(cloneRoot.GetComponentsInChildren<UIPersonalQuestProgress>(true));
        return killed;
    }

    /// <summary>Mark every pair at or below each of <paramref name="roots"/> as suppressed and
    /// deactivate it. Returns how many PAIRS were suppressed (0 in the shipping configuration).</summary>
    private int Suppress<T>(T[] roots) where T : Component
    {
        if (roots == null || roots.Length == 0 || _pairs == null)
            return 0;
        int killed = 0;
        for (int r = 0; r < roots.Length; r++)
        {
            Transform? root = roots[r] != null ? roots[r].transform : null;
            if (root == null)
                continue;
            for (int i = 0; i < _pairs.Length; i++)
            {
                Transform? dst = _pairs[i].Dst;
                if (dst == null || _pairs[i].Suppressed || !IsSelfOrDescendant(dst, root))
                    continue;
                _pairs[i].Suppress();
                killed++;
            }
        }
        return killed;
    }

    private static bool IsSelfOrDescendant(Transform node, Transform root)
    {
        for (Transform? t = node; t != null; t = t.parent)
        {
            if (ReferenceEquals(t, root))
                return true;
        }
        return false;
    }

    private void EnsureHost()
    {
        if (_host != null)
            return;

        _host = new GameObject($"{_name}Mirror");
        _host.transform.SetParent(_mount, worldPositionStays: false);
        _host.transform.localRotation = Quaternion.identity;
        _host.transform.localScale = Vector3.one;

        var canvas = _host.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        _canvas = canvas;
        // DRAW ORDER is NOT set here any more: this host is seated by the owning board's cluster
        // sweep, at the tier its own board-local depth earns (BoardVisual.AdoptBoardOrder /
        // TierForDepth). That still puts a dock strictly above the board's face furniture - a dock
        // IS proud of the face - but it derives the relation from the owner's authored mount
        // instead of asserting it, which is what the 2026-08-09 reports needed (see BoardVisual).
        Camera? head = Rig.VRRigDriver.HeadCamera != null ? Rig.VRRigDriver.HeadCamera : Camera.main;
        if (head != null)
            canvas.worldCamera = head;

        // THE PIVOT: a plain RectTransform between the host and the clone, for two jobs.
        //   1. RE-CENTRING. The dock conventions are stated in terms of the panel's own edges, so
        //      the measured content has to be centred on the host origin. Doing that by rewriting
        //      the CLONE ROOT's anchors would be wrong: these containers are stretch-anchored, and
        //      collapsing their anchors changes the rect every child resolves against (the exact
        //      trap CanvasConversion documents — it collapses them and ObjectivesSurface then has
        //      to force the width back). Moving a frame ABOVE the clone changes nothing inside it.
        //   2. ANCHOR FRAME. Its rect is sized to the SOURCE's parent rect, so a stretch-anchored
        //      clone lands at the same resolved size it has in the game's own hierarchy.
        var pivotGo = new GameObject("Pivot", typeof(RectTransform));
        pivotGo.transform.SetParent(_host.transform, worldPositionStays: false);
        _pivot = (RectTransform)pivotGo.transform;
        _pivot.anchorMin = _pivot.anchorMax = _pivot.pivot = new Vector2(0.5f, 0.5f);
        _pivot.localRotation = Quaternion.identity;
        _pivot.localScale = Vector3.one;
        _pivot.sizeDelta = Vector2.zero;

        _host.SetActive(false); // stays inactive until a clone is built AND neutralized
    }

    /// <summary>Size the pivot to the SOURCE's parent rect, so the clone's anchors resolve against
    /// the same frame they do in the game's own hierarchy. A source whose parent is not a
    /// RectTransform (a bare Canvas root) leaves the pivot at zero, which is what its own anchors
    /// then expect.</summary>
    private void AdoptParentRect(Transform source)
    {
        if (_pivot == null)
            return;
        var parent = source.parent as RectTransform;
        Vector2 size = parent != null ? parent.rect.size : Vector2.zero;
        if (_pivot.sizeDelta != size)
            _pivot.sizeDelta = size;
    }

    /// <summary>A horizontal <c>ContentSizeFitter</c> on the CLONE ROOT, resolved once per rebuild
    /// so the per-tick re-assert in <see cref="ApplyOwnersColumn"/> costs no <c>GetComponent</c>.
    /// Null on the <see cref="LayoutOwner.Source"/> path, where <see cref="Neutralize"/> destroyed
    /// every fitter anyway.</summary>
    private ContentSizeFitter? _cloneFitter;

    /// <summary>The wrap column last written onto the clone root, in uGUI px; −1 until the first
    /// write. Change gate for <see cref="LogOwnersColumn"/> and the number
    /// <see cref="MeasurePath"/> reports into the per-peer seat line.</summary>
    private float _ownersColumnPx = -1f;

    /// <summary>
    /// (a) THE WHOLE POINT OF <see cref="LayoutOwner.CloneAtBoardOwnersWidth"/>: force the BOARD
    /// OWNER's wrap column onto the clone root, so a peer's task panel wraps where ITS OWNER reads
    /// it and stops re-wrapping every time the VIEWER touches their own 'Breite'.
    ///
    /// ─── THE DEFECT, AND WHY THE OBVIOUS FIX IS INERT ──────────────────────────────────────────
    /// This mirror's SOURCE ROOT IS the rect the local surface forces: <c>ObjectivesSurface</c>'s
    /// <c>FindTarget</c> returns <c>UIManager.Instance.MissionObjectiveContainer.transform</c> and
    /// that is the identical transform <see cref="RemoteObjectivesPanel"/> hands to
    /// <see cref="Refresh"/>. So the subtree cloned here arrives ALREADY WRAPPED — to the VIEWER's
    /// column, because <c>ObjectivesSurface.ApplyContentWidth</c> wrote
    /// <c>wantPx = MountWidth · density</c> onto it out of the VIEWER's own
    /// <c>CardsConfig.ObjectivesWidth(CurrentBoard)</c>.
    ///
    /// Writing the owner's number onto the clone root and stopping there — the first design of this
    /// fix — moves nothing at all, for two independent structural reasons:
    ///   • <see cref="Sync"/> copies <c>sizeDelta</c> from source to clone node by node INCLUDING
    ///     the root, every frame (its own summary says so, and the objectives root is the case it
    ///     names). The write would be overwritten before it was ever rendered.
    ///   • Even surviving, it would have no consumer. The column is DERIVED top-down by three
    ///     nested layout groups — <c>TablePanelSurfaces</c>' prefab dump: the container's
    ///     VerticalLayoutGroup drives the list's width, the list's drives every row's, and the row's
    ///     HorizontalLayoutGroup hands the leftover to the TMP text — and <see cref="Neutralize"/>
    ///     destroys all three. Every node below the root is anchor-PINNED, not stretch-anchored
    ///     ((0,0)-(0,0) on the list, on the rows and on the text); the one stretch-anchored graphic
    ///     is the progress bar, and it stretches against its ROW. With the layout engine gone and
    ///     the children pinned, the root's width is read by nobody.
    ///
    /// ─── THE FIX: THE CLONE OWNS ITS GEOMETRY, THE SOURCE OWNS ITS CONTENT ─────────────────────
    /// That split IS the fix, and stating it the other way round is how the next reader re-breaks
    /// it. Under this flag the game's own layout components come back on the clone
    /// (<see cref="IsStockLayout"/>), this method writes the owner's column onto the clone root, and
    /// the rect half of <see cref="Pair.Apply"/> stands down so the two cannot fight. Everything the
    /// source still owns keeps driving unchanged: active flags, colour, TMP and legacy text, sprite,
    /// <c>fillAmount</c>, texture, CanvasGroup alpha and the shared material.
    ///
    /// WHY EXACTLY ONE PROPERTY HAD TO STAND DOWN, audited against every write <see cref="Sync"/>
    /// makes rather than assumed. <c>LayoutGroup</c> writes children's <c>anchorMin</c>/
    /// <c>anchorMax</c>/<c>sizeDelta</c>/<c>anchoredPosition</c> (through
    /// <c>SetInsetAndSizeAlongAxis</c>) and <c>ContentSizeFitter</c> writes its own
    /// <c>sizeDelta</c> — the RECT, and nothing else. They never write an active flag, a colour, a
    /// text, a sprite, a fill, a texture, an alpha, a material, a local scale or a local rotation.
    /// The rest of the drive is therefore not merely compatible with them, it is what FEEDS them: a
    /// TMP text setter marks the layout dirty, which is precisely what re-wraps a row when the
    /// source's wording changes. That is why the answer is "keep the groups and drop the one write",
    /// not "keep the groups and hope".
    ///
    /// ─── THE ARITHMETIC, TERM FOR TERM WITH ApplyContentWidth ──────────────────────────────────
    /// <c>wantPx = _mountWidth · ContentDensity</c>, where for this dock <c>_mountWidth</c> is
    /// <c>RemoteBoardLayout.ObjectivesWidth</c> = <c>PlayTray.ObjectivesMountWidth</c> ×
    /// the owner's synced multiplier (extension record 28, field id
    /// <c>NetProtocol.TuneObjectivesWidth</c> = 129) and <c>ContentDensity</c> is
    /// <c>PlayTray.TrayPixelsPerMeter × _densityScale</c>. Both sides evaluate the SAME constants
    /// through the SAME expression, so at the shipped defaults (multiplier 0.8 on all three board
    /// styles) they agree exactly: 0.26 m × 0.8 = 0.208 m × (2400 × 0.6 = 1440 px/m) = 299.5 px on
    /// the owner's panel and 299.5 px here. They diverge only when the two players' dials do — a
    /// viewer at 1.6 against an owner at 0.8 used to render the peer's panel at 599 px, i.e. half
    /// the line count the owner actually sees. No literal is re-typed here on purpose: a duplicated
    /// constant is how two surfaces drift apart the day one of them is retuned.
    ///
    /// The three writes below are <c>ApplyContentWidth</c>'s three, in its order and with its dead
    /// band: the horizontal <c>ContentSizeFitter</c> to Unconstrained (it DRIVES SizeDeltaX and
    /// would stomp the column on the clone's first layout pass), the <c>sizeDelta.x</c> itself, and
    /// an immediate rebuild so the very first <see cref="Fit"/> measures the owner's column instead
    /// of a one-frame-stale viewer column. Re-asserted on the content cadence and change-gated,
    /// which in the steady state is two float compares.
    ///
    /// WHY RE-ASSERTING IS NOT PARANOIA HERE, AND WHY IT IS ALSO NOT THE DIAL'S REFRESH PATH. The
    /// OWNER moving their dial does not need this method to notice: record 28 bumps
    /// <c>RemoteAvatar.BoardTuningRevision</c>, and <c>RemoteControlBoard</c> tears the whole board
    /// down and rebuilds every dock from a fresh <c>RemoteBoardLayout</c> — so <c>_mountWidth</c> is
    /// re-captured by construction and cannot go stale. The re-assert exists for the OTHER writer:
    /// the clone is a public surface that callers decorate through <see cref="CloneOf"/>, and a
    /// decorator that ever re-enables a fitter or re-parents under the root would silently take the
    /// column back. It is the same guarantee <c>ApplyContentWidth</c> gives itself, for the same
    /// reason, at the same cost.
    ///
    /// REJECTED: (a) adding a VERTICAL <c>ContentSizeFitter</c> to the clone root so its rect would
    /// bound its own content and make the frame clamp in <see cref="TryFrameExtent"/> harmless —
    /// it would put a component on the clone that the OWNER's panel does not have, which is the
    /// opposite of a mirror; (d)'s degeneracy latch is the honest answer to that clamp.
    /// (b) Scaling the copied rects by the ratio of the two columns instead of re-running the
    /// layout — the propagation is ADDITIVE, not multiplicative (each level passes width down minus
    /// its own constant padding: 11 + 25 + 5 px, plus an 82 px minimum spacer), and how the surplus
    /// is split between the text and that spacer is decided by uGUI's flexible-width distribution,
    /// not by a formula this file can safely restate. Hand-computing a layout engine against a
    /// prefab dump is how a fix ships a column that is right in no case at all.
    /// (c) Re-running the layout on the SOURCE at the owner's width and snapshotting it — that
    /// writes the local player's own live panel, which is forbidden outright, and would flicker
    /// their objectives once per peer per tick.
    /// </summary>
    private void ApplyOwnersColumn()
    {
        if (_layoutOwner != LayoutOwner.CloneAtBoardOwnersWidth || _cloneRect == null)
            return;

        float wantPx = _mountWidth * ContentDensity;
        bool changed = false;

        if (_cloneFitter != null
            && _cloneFitter.horizontalFit != ContentSizeFitter.FitMode.Unconstrained)
        {
            _cloneFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            changed = true;
        }

        if (Mathf.Abs(_cloneRect.sizeDelta.x - wantPx) > 0.5f)
        {
            _cloneRect.sizeDelta = new Vector2(wantPx, _cloneRect.sizeDelta.y);
            changed = true;
        }

        if (changed)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(_cloneRect);

            // THE STATE WRITE LIVES HERE, NEXT TO THE WRITE IT RECORDS — never inside the logger.
            // This number is read back by TryMeasureDock to build the seat line's measure path, so
            // latching it inside a Log* method would mean that retiring a spent diagnostic silently
            // blanks a number something else depends on. That is a defect this project has shipped
            // once already.
            float previous = _ownersColumnPx;
            _ownersColumnPx = wantPx;
            if (Mathf.Abs(wantPx - previous) > 0.5f)
                LogOwnersColumn(wantPx);
        }

        // UNCONDITIONALLY, for the same reason ObjectivesSurface.ApplyContentHeight is: the column
        // moves when a DIAL moves, the row count moves when the GAME does, and only the first of
        // those sets `changed`.
        ApplyOwnersRowHeight();
    }

    /// <summary>Last content height written onto the clone root — the vertical twin of
    /// <see cref="_ownersColumnPx"/>, and the gate that keeps its log line quiet in the steady
    /// state.</summary>
    private float _ownersHeightPx = -1f;

    /// <summary>
    /// THE MIRRORED HALF OF THE OBJECTIVE-OVERLAP FIX (user report 2026-09-06 item 8).
    ///
    /// <para>The clone arrives by <c>Object.Instantiate</c> of the owner's converted subtree, so it
    /// arrives carrying the same <c>CanvasConversion</c> placeholder height the source has — and
    /// then <see cref="LayoutOwner.CloneAtBoardOwnersWidth"/> re-runs the game's own layout groups
    /// on it LOCALLY at the owner's column, which is precisely the arrangement that turns a short
    /// <c>VerticalLayoutGroup</c> into overlapping rows (the mechanism is derived in full on
    /// <see cref="GloomhavenVR.Core.LayoutContentHeight"/>). The rect half of <see cref="Pair.Apply"/>
    /// stands down on this path, so the source's own corrected height is NOT copied across: fixing
    /// the owner alone would leave every peer's copy of the panel overlapping. Both call sites share
    /// one helper so they cannot drift.</para>
    ///
    /// <para>1:1: this is a pure function of the same rows at the same column, so the two clients
    /// compute the same number from the same inputs. It is not a viewer dial being ANDed with an
    /// owner dial — there is no dial, and no wire field is needed for a value both sides can derive.
    /// The frame clamp in <see cref="TryFrameExtent"/> cannot crop the taller panel either: this
    /// source is latched degenerate at conversion (<see cref="LatchFrameDegenerate"/>), which skips
    /// the clamp outright.</para>
    /// </summary>
    private void ApplyOwnersRowHeight()
    {
        // Cheap test first — this runs on the content cadence and the row-guard walk allocates. The
        // BEFORE shortfall needs no walk: it is the room the root was short, which is the difference
        // the write just closed.
        if (!LayoutContentHeight.Apply(_cloneRect, out float beforePx, out float afterPx))
            return;

        LayoutRebuilder.ForceRebuildLayoutImmediate(_cloneRect);
        float missAfter = LayoutContentHeight.MeasureRowOverflowPx(_cloneRect, out string worst);

        float previous = _ownersHeightPx;
        _ownersHeightPx = afterPx;
        if (Mathf.Abs(afterPx - previous) <= LayoutContentHeight.DeadBandPx)
            return;

        // HW-VERIFY
        VRLog.Note("Net", $"OBJECTIVES HEIGHT (mirrored) '{_name}': the clone's container root is " +
                          $"sized from its own content, {beforePx:F0} -> {afterPx:F0} authored px " +
                          $"(it was short by {afterPx - beforePx:F0} px, which is the room a row was " +
                          $"drawing into its neighbour). Rows still squeezed after the write: " +
                          $"{missAfter:F0} px" + (worst.Length > 0 ? $" on '{worst}'" : "") + ". " +
                          "Read it against the OWNER's 'OBJECTIVES HEIGHT: container root ... -> N " +
                          "authored px' line in THEIR log: at equal dials the two must agree to the " +
                          "pixel, because both sides measure the same rows at the same column. " +
                          "'still squeezed' must be 0 px in every line; a non-zero one is a cause " +
                          "neither instrument has seen. NO line here while the owner's log has one " +
                          "means this dock never mirrored the real widget — read the 'Remote board " +
                          "content' line for which mechanism is live before reading anything into " +
                          "the silence.");
    }

    /// <summary>
    /// THE ONE LINE THAT PROVES THE COLUMN WAS ACTUALLY WRITTEN — grep
    /// <c>OBJECTIVES COLUMN (mirrored)</c>, and read it directly against the owner's own
    /// <c>OBJECTIVES WIDTH:</c> line from <c>ObjectivesSurface.ApplyContentWidth</c>. The two are
    /// deliberately in the same units and name the same terms, so the first hardware round answers
    /// "does a peer's panel wrap at its owner's column?" by comparing two numbers in two logs
    /// instead of by eye.
    ///
    /// <para>Change-gated by its caller on the written pixel value, with
    /// <c>ApplyContentWidth</c>'s own 0.5 px dead band, so a rebuild that re-writes the same column
    /// is silent and a dial change is not. It prints on the path that matters by construction: the
    /// only caller is the write itself, inside the <c>changed</c> branch, so a line here means a
    /// column reached the rows — never merely that something was attempted. PURE: it latches
    /// nothing, so retiring it can break nothing.</para>
    /// </summary>
    private void LogOwnersColumn(float wantPx)
    {
        VRLog.Info("Net", $"OBJECTIVES COLUMN (mirrored) '{_name}': the clone's container root is " +
                          $"forced to {wantPx:F0} px — the BOARD OWNER's wrap column " +
                          $"({_mountWidth * 1000f:F0} mm at {ContentDensity:F0} px/m), not this " +
                          "viewer's. The clone keeps the game's own layout groups for exactly this " +
                          "reason and the rect drive stands down (LayoutOwner." +
                          "CloneAtBoardOwnersWidth), so the two VerticalLayoutGroups carry the " +
                          "column to every row and the row's HorizontalLayoutGroup re-hands the " +
                          "leftover to the TMP text, which re-wraps. Compare this number against " +
                          "the OWNER's own 'OBJECTIVES WIDTH: container root ... forced to N px' " +
                          "line in THEIR log: the two must match to the pixel, and glyph size must " +
                          "move in NEITHER (ObjectivesWidth is shape, ObjectivesScale is size).");
    }

    /// <summary>See <see cref="TryFrameExtent"/>: true when this source has no REAL rect for a
    /// union to be clamped into. Latched once per rebuild, and read on the
    /// <see cref="LayoutOwner.CloneAtBoardOwnersWidth"/> path only.</summary>
    private bool _frameDegenerate;

    /// <summary>
    /// (d) THE PIECE MOST LIKELY TO BITE, and the reason it has the longest comment.
    ///
    /// <see cref="TryFrameExtent"/> clamps the measured union into the CLONE ROOT's own rect,
    /// skipping the clamp only for a rect that is under 1 px on an axis. That live test is correct
    /// today because nothing ever writes the clone root — and it becomes WRONG the moment
    /// <see cref="ApplyOwnersColumn"/> does. The objectives container's AUTHORED rect is literally
    /// (0,0) (the prefab dump in <c>TablePanelSurfaces</c>), so it has no frame at all; but by the
    /// time this mirror sees it, <c>CanvasConversion.Convert</c> has already clamped it to the
    /// 100 px placeholder and the local surface has already forced a width onto it. Re-running the
    /// 1 px test on that would report "not degenerate" and clamp the panel's HEIGHT to the 100 px
    /// placeholder — cropping content the log records at 80–98 px today and more once it re-wraps
    /// into a narrower column. A cropped panel that still measures sanely is precisely the silent
    /// failure this round is not allowed to ship.
    ///
    /// THE LOCAL FIT DOES NOT HAVE THIS PROBLEM because it never re-derives the verdict: it latches
    /// <c>ConvertedPanel.FitFrameDegenerate</c> at Convert, from the PRISTINE rect, before anything
    /// has written to it (<c>CanvasConversion.1.Core.cs</c>: <c>degenerate = size.x &lt; 1f ||
    /// size.y &lt; 1f</c> taken off <c>target.rect.size</c>), and <c>TryMeasureContent</c> then skips
    /// the whole frame clamp when it is set.
    ///
    /// SO THIS READS THE SAME LATCH RATHER THAN RECONSTRUCTING IT. The <c>ConvertedPanel</c> whose
    /// <c>Target</c> is our source is the very object the local fit consults, and its verdict is a
    /// property of the GAME PREFAB — not of any dial — so it is identical on every client and using
    /// the viewer's copy of it leaks nothing. Read-only, and the same linear walk over
    /// <c>ActivePanels</c> that <see cref="TryDockRect"/> already makes.
    ///
    /// FALLBACK, for a source that is not converted (flat mode, conversion disabled, mid-conversion
    /// frames): the clone still carries the source's AUTHORED rect at this point, because this runs
    /// BEFORE <see cref="ApplyOwnersColumn"/> writes — so the local latch's own 1 px test can simply
    /// be re-run here and gives the same answer for the same reason.
    /// </summary>
    private void LatchFrameDegenerate()
    {
        _frameDegenerate = false;
        if (_layoutOwner != LayoutOwner.CloneAtBoardOwnersWidth || _cloneRect == null)
            return;

        IReadOnlyList<WorldUI.ConvertedPanel> panels = WorldUI.CanvasConversion.ActivePanels;
        for (int i = 0; i < panels.Count; i++)
        {
            WorldUI.ConvertedPanel p = panels[i];
            if (p == null || !ReferenceEquals(p.Target, _source))
                continue;
            _frameDegenerate = p.FitFrameDegenerate;
            return;
        }

        Rect authored = _cloneRect.rect;
        _frameDegenerate = authored.width < 1f || authored.height < 1f;
    }

    /// <summary>
    /// Turn the clone into a PUPPET: destroy everything that is not pure presentation (see the class
    /// note), then add a blocking <see cref="CanvasGroup"/> at the root. Runs while the clone is
    /// still inactive, so no destroyed component has ever executed.
    ///
    /// <c>DestroyImmediate</c> rather than <c>Destroy</c>: a deferred destroy would still let a
    /// behaviour's <c>Awake</c>/<c>OnEnable</c> run on the frame the host activates, which is the
    /// entire thing this method exists to prevent.
    /// </summary>
    private static void Neutralize(GameObject clone, LayoutOwner layoutOwner,
                                  System.Func<Transform, bool>? externallyShownBranch)
    {
        // THE BRANCHES THE CLONE WILL LAY OUT ITSELF, resolved BEFORE a single component is
        // destroyed — the predicate reads the very game components the loop below removes, so
        // asking afterwards would always answer "none". See Pair.External for why these exist and
        // IsStockLayout for why exactly three component families are exempted.
        List<Transform>? cloneLaidOut = null;
        if (externallyShownBranch != null)
        {
            foreach (Transform node in clone.GetComponentsInChildren<Transform>(includeInactive: true))
            {
                if (node == null || !externallyShownBranch(node))
                    continue;
                (cloneLaidOut ??= new List<Transform>(4)).Add(node);
            }
        }

        // Unity refuses to destroy a component that a SURVIVING one declares as a
        // [RequireComponent] dependency (GraphicRaycaster→Canvas is the common case, and game
        // scripts have their own chains). Two mitigations, both cheap:
        //   • walk the component array in REVERSE — dependents are added after their dependencies,
        //     so the dependent is normally reached first and the refusal never happens;
        //   • repeat until a pass frees nothing, so a chain longer than one link still unwinds.
        // Canvas is held back to the last pass on purpose: it is the single most depended-on
        // component in a uGUI tree.
        for (int pass = 0; pass < 3; pass++)
        {
            var components = clone.GetComponentsInChildren<Component>(includeInactive: true);
            bool freed = false;
            for (int i = components.Length - 1; i >= 0; i--)
            {
                Component c = components[i];
                if (c == null || c is Transform || IsPresentation(c, layoutOwner))
                    continue;
                if (c is Canvas && pass == 0)
                    continue;
                if (cloneLaidOut != null && IsStockLayout(c) && InsideAny(c.transform, cloneLaidOut))
                    continue;
                Object.DestroyImmediate(c);
                if (c == null)
                    freed = true;
            }
            if (!freed && pass > 0)
                break;
        }

        // Belt and braces for the INERT invariant: colliders/rigidbodies are not Components the
        // whitelist would have kept, but a second pass costs nothing and states the guarantee here
        // rather than only in the board-wide sweep.
        foreach (Collider col in clone.GetComponentsInChildren<Collider>(true))
            Object.DestroyImmediate(col);

        CanvasGroup cg = clone.GetComponent<CanvasGroup>();
        if (cg == null)
            cg = clone.AddComponent<CanvasGroup>();
        cg.interactable = false;
        cg.blocksRaycasts = false;
        cg.alpha = 1f;
    }

    /// <summary>
    /// The presentation whitelist — the ONLY component kinds a mirrored clone may keep.
    ///
    /// <c>Graphic</c> covers Image, RawImage, uGUI Text and every TMP text/sub-mesh (all derive from
    /// it), i.e. everything that actually draws. <c>CanvasRenderer</c> is what a Graphic renders
    /// through. <c>Mask</c>/<c>RectMask2D</c> keep clipped panels clipped. <c>BaseMeshEffect</c> is
    /// the shadow/outline family. <c>CanvasGroup</c> carries fades — and is how we neutralize input.
    ///
    /// Deliberately NOT whitelisted, each for its own reason:
    ///   <c>Canvas</c>/<c>CanvasScaler</c>/<c>GraphicRaycaster</c> — the HOST owns the one
    ///     world-space canvas; a copied screen-space canvas would blit itself over the whole view
    ///     and a raycaster would make a display pokeable.
    ///   layout groups, <c>ContentSizeFitter</c>, <c>LayoutElement</c> — the clone's rects are
    ///     driven directly from the source every tick, so a second layout pass could only fight it.
    ///     UNLESS the caller asked for <see cref="LayoutOwner.CloneAtBoardOwnersWidth"/>, in which
    ///     case the rect drive is the thing that stands down and these three come back; see
    ///     <see cref="IsStockLayout"/> for why exactly three, and <see cref="ApplyOwnersColumn"/>
    ///     for the audit of what they contest. They also come back INSIDE an externally shown
    ///     branch, whatever the layout owner: the drive skips such a branch entirely, so the clone's
    ///     own layout contests nothing and is the only thing that can resolve it. See
    ///     <see cref="Pair.External"/>.
    ///   everything else (the game's own MonoBehaviours) — see the class note.
    /// </summary>
    private static bool IsPresentation(Component c, LayoutOwner layoutOwner) =>
        c is Graphic || c is CanvasRenderer || c is Mask || c is RectMask2D
        || c is BaseMeshEffect || c is CanvasGroup
        || (layoutOwner == LayoutOwner.CloneAtBoardOwnersWidth && IsStockLayout(c));

    /// <summary>
    /// STOCK uGUI LAYOUT, and nothing that merely looks like it — the three component families the
    /// objectives column is derived through, each named in the game's own prefab dump
    /// (<c>TablePanelSurfaces</c>, the <c>WidthVerifyDelay</c> hierarchy block): two
    /// <c>VerticalLayoutGroup</c>s and a per-row <c>HorizontalLayoutGroup</c> (both
    /// <c>LayoutGroup</c>), the row's <c>ContentSizeFitter</c> (vertical = Preferred, which is what
    /// lets a re-wrapped row get taller), and the <c>LayoutElement</c>s that carry the quest
    /// header's preferred height, the spacer's 82 px minimum and the three
    /// <c>ignoreLayout</c> flags.
    ///
    /// <para>THE ASSEMBLY TEST IS NOT DECORATION. The class's whole premise is that no cloned
    /// component can execute a line of GAME code (see the class note's PUPPET, NOT PROGRAM block),
    /// and <c>LayoutGroup</c> / <c>LayoutElement</c> are ordinary public base classes that a game
    /// script is free to derive from. A type test alone would therefore be a hole in that premise:
    /// it would keep — and WAKE — any game behaviour whose author happened to subclass a layout
    /// component. Comparing against <c>typeof(LayoutGroup).Assembly</c> restricts the exemption to
    /// UnityEngine.UI's own types, which carry no game state and touch nothing but the clone's own
    /// rects. One <c>Assembly</c> compare per component per clone REBUILD, never per frame.</para>
    /// </summary>
    private static bool IsStockLayout(Component c) =>
        (c is LayoutGroup || c is ContentSizeFitter || c is LayoutElement)
        && ReferenceEquals(c.GetType().Assembly, typeof(LayoutGroup).Assembly);

    /// <summary>Is <paramref name="node"/> inside (or itself) any of <paramref name="roots"/>?
    /// One parent walk per stock-layout component per clone REBUILD — never per frame.</summary>
    private static bool InsideAny(Transform node, List<Transform> roots)
    {
        for (int i = 0; i < roots.Count; i++)
        {
            if (IsSelfOrDescendant(node, roots[i]))
                return true;
        }
        return false;
    }

    private void DestroyClone()
    {
        RebuildStamp++; // even a teardown without a rebuild invalidates every CloneOf result
        if (_clone != null)
        {
            // DestroyImmediate: a deferred Destroy would leave the OLD clone rendering on top of the
            // new one for the rest of the frame a structure change lands on. Safe here — the clone
            // is ours and carries no behaviour that could run OnDestroy (see Neutralize).
            Object.DestroyImmediate(_clone);
            _clone = null;
        }
        _cloneRect = null;
        _cloneFitter = null;
        _pairs = System.Array.Empty<Pair>();
        _source = null;
        // Per-CLONE verdicts, all of them: a fresh clone has not been fitted, has not failed a
        // sanity check, and has not had its frame verdict taken. _ownersColumnPx deliberately
        // SURVIVES — it is the log's change gate and the column itself did not change just because
        // the panel gained a row.
        _fitApplied = false;
        _withhold = null;
        _frameDegenerate = false;
    }

    // ------------------------------------------------------------------ drive --

    /// <summary>Depth-first (parent, then children in sibling order) flattening of a subtree into
    /// <paramref name="into"/>. Iterative so a deep panel cannot blow the stack, and buffer-reusing
    /// so the cadence walk does not allocate.</summary>
    private void Walk(Transform root, List<Transform> into)
    {
        into.Clear();
        _walkDepth.Clear();
        _stack.Clear();
        _stackDepth.Clear();
        _stack.Add(root);
        _stackDepth.Add(0);
        while (_stack.Count > 0)
        {
            int last = _stack.Count - 1;
            Transform t = _stack[last];
            int depth = _stackDepth[last];
            _stack.RemoveAt(last);
            _stackDepth.RemoveAt(last);
            into.Add(t);
            // Depth rides alongside the node because the flat list is PRE-ORDER: a node's whole
            // subtree is the contiguous run of following entries with a GREATER depth. That is the
            // one fact <see cref="BuildSubtreeExtents"/> needs to turn "this branch is off" into a
            // single index jump instead of a walk over every hidden descendant.
            _walkDepth.Add(depth);
            // Push in reverse so children come out in sibling order — the clone is walked the same
            // way, which is what makes the two flat lists index-aligned.
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                _stack.Add(t.GetChild(i));
                _stackDepth.Add(depth + 1);
            }
        }
    }

    /// <summary>
    /// Turn the pre-order depths of the last <see cref="Walk"/> into, for every node, the index of
    /// the first entry that is NOT one of its descendants.
    ///
    /// <para>THIS IS THE PER-FRAME DRIVE'S WHOLE COST MODEL. <see cref="Sync"/> used to touch all
    /// N nodes every frame per peer, whether or not their branch was even switched on; on the
    /// hardware log's initiative track that is 767 nodes × ~20 Unity property accesses × once per
    /// peer per frame, and it measured as 85–96 ms/s of <c>Net.Avatar</c> with a single peer (0.9 ms
    /// per frame at the start of a scenario, 2.7 ms once the track was full). Most of those nodes
    /// live under a branch the game has switched OFF — vanilla's initiative module keeps pooled
    /// entries, condition-icon slots and popup panels inactive — and driving the interior of an
    /// invisible branch changes nothing anybody can see.</para>
    ///
    /// <para>With the extents in hand, an off branch costs ONE <c>activeSelf</c> read and one index
    /// jump. The moment a branch's own node reports active again, the loop continues into it IN THE
    /// SAME FRAME (the jump only happens on the false result), so there is no staleness and no
    /// one-frame catch-up.</para>
    ///
    /// <para>"NOTHING IS SKIPPED THAT COULD BE VISIBLE" USED TO BE WRITTEN HERE AS A FACT. It is
    /// the source's answer to a question a CALLER may also answer: <c>RemoteInitiativeTrack</c>
    /// shows a mirrored enemy-info popup from the PEER's hover while this client's own copy of it
    /// is off. That branch is now marked (see <see cref="Pair.External"/>) and skipped for a
    /// DIFFERENT reason — its geometry belongs to the clone — rather than on a premise that does
    /// not hold for it.</para>
    /// </summary>
    private void BuildSubtreeExtents(int[] skipTo)
    {
        int n = skipTo.Length;
        for (int i = n - 1; i >= 0; i--)
        {
            int depth = _walkDepth[i];
            int j = i + 1;
            // Hop subtree by subtree rather than node by node: every j reached here is already
            // resolved (we walk backwards), so this is O(n) overall, not O(n²).
            while (j < n && _walkDepth[j] > depth)
                j = skipTo[j];
            skipTo[i] = j;
        }
    }

    /// <summary>
    /// THE TRIPWIRE FOR OBJECTS PARENTED ONTO A CLONE AND NEVER TAKEN OFF AGAIN.
    ///
    /// <para>WHY IT EXISTS (2026-08-09, and it is the most expensive lesson in this file).
    /// <see cref="StructureMatches"/> validates the SOURCE subtree only — that is its job, because
    /// the source is what the pairing describes. Nothing validated the CLONE. But the clone is a
    /// public surface: callers legitimately decorate it through <see cref="CloneOf"/> (the peer's
    /// track rings are the shipped case), and a decorator that adds on every content tick and
    /// removes on none has no other detector at all. One did exactly that — two ring objects per
    /// initiative entry, four times a second, forever, each an Image on the mirrored board's
    /// world-space canvas.</para>
    ///
    /// <para>AND IT WAS INVISIBLE TO EVERY EXISTING NUMBER. The mod's Update steps stayed flat per
    /// second, because the cost is not in Update: Unity rebuilds canvases after LateUpdate and
    /// before the render loop, so an ever-larger canvas shows up in the frame split as "blocked
    /// (waiting on GPU/compositor)" and looks like a network or GPU problem. The renderer census
    /// could not see it either — a uGUI Graphic is not a <c>Renderer</c>. Both instruments have
    /// since been widened; this one is the SOURCE-side counterpart, and it is the cheapest of the
    /// three because the mirror already knows exactly how many nodes it built.</para>
    ///
    /// <para>MEASURE, NEVER ACT. Excess is legitimate by design, so this must not trigger a rebuild
    /// and must not delete anything it did not create — a mirror that tore off a caller's rings
    /// would break the feature it is instrumenting. It counts, and it says so ONCE per doubling, on
    /// the content cadence (4 Hz), which is where the callers add.</para>
    /// </summary>
    private void AuditCloneGrowth()
    {
        if (_clone == null || _pairs.Length == 0)
            return;

        // _walk still holds the SOURCE walk that StructureMatches just took, so re-walking the
        // CLONE here would clobber it for nobody's benefit — but the walk buffers are reusable and
        // this runs at 4 Hz, not per frame, so a fresh walk is affordable and unambiguous.
        Walk(_clone.transform, _walk);
        int live = _walk.Count;
        int excess = live - _pairs.Length;
        if (excess < 0)
            excess = 0;
        Core.PerfMonitor.Count("Mirror.CloneExcessNodes", excess);

        // One line per DOUBLING of the excess, so a decorator that adds a bounded set of rings is
        // silent forever and one that adds two per entry per tick names itself within seconds.
        if (excess <= _loggedExcess * 2 || excess < ExcessLogFloor)
            return;
        _loggedExcess = excess;
        VRLog.Warn("Net", $"Remote board '{_name}' mirror: the CLONE now carries {live} node(s) "
                          + $"against {_pairs.Length} paired — {excess} extra object(s) that this "
                          + "mirror did not build. That is legitimate when a caller decorates the "
                          + "clone through CloneOf (the per-peer track rings do), and it is an "
                          + "OBJECT LEAK when it keeps climbing: every extra uGUI node is batched "
                          + "into this board's world-space canvas and re-batched on every rebuild, "
                          + "a cost that lands in the frame split's 'blocked' bucket rather than in "
                          + "any mod step. Cross-check '[Perf] COUNTS' Mirror.CloneExcessNodes and "
                          + "the uGUI half of the '[Perf] SPLIT' scene census: if all three climb "
                          + "together with a steady scenario, the decorator is not releasing.");
    }

    /// <summary>Below this many extra clone nodes the audit stays silent — a handful of decorations
    /// is the designed case and must not produce a warning.</summary>
    private const int ExcessLogFloor = 16;

    /// <summary>Last excess this mirror warned about; the gate is a DOUBLING, so a bounded
    /// decoration logs at most once.</summary>
    private int _loggedExcess = ExcessLogFloor;

    /// <summary>True while the cached pairing still describes the live source exactly (same nodes,
    /// same order, none destroyed). A single mismatch — a round ended and the track re-spawned its
    /// entries, an objective row was added or removed — means the clone must be rebuilt.</summary>
    private bool StructureMatches(Transform source)
    {
        Walk(source, _walk);
        if (_walk.Count != _pairs.Length)
            return false;
        for (int i = 0; i < _pairs.Length; i++)
        {
            if (!ReferenceEquals(_walk[i], _pairs[i].Src) || _pairs[i].Dst == null)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Copy the source's live presentation state onto the clone, node by node — INCLUDING the root,
    /// whose rect is as much a part of the authored layout as any child's (for the objectives
    /// container it is literally the wrap column). Re-centring is the pivot's job, one level up,
    /// precisely so this drive can stay a faithful copy.
    ///
    /// <para>WITH ONE EXCEPTION, AND IT IS THE ROOT'S RECT THAT PROVES WHY IT IS NEEDED: under
    /// <see cref="LayoutOwner.CloneAtBoardOwnersWidth"/> the rect half of <see cref="Pair.Apply"/>
    /// stands down for every node, because the clone is laying itself out at a DIFFERENT column
    /// (see <see cref="ApplyOwnersColumn"/>) and copying the source's resolved rects would put the
    /// viewer's column straight back. The content half — active flags, colour, text, sprite, fill,
    /// texture, alpha, material — keeps driving exactly as before; it is what marks the clone's own
    /// layout dirty and therefore what makes a re-worded row re-wrap.</para>
    /// </summary>
    private void Sync()
    {
        Pair[] pairs = _pairs;
        int n = pairs.Length;
        // Hoisted: one enum compare per Sync rather than one per node, and it states the invariant
        // in a single place — the clone owns its geometry iff it is running its own layout.
        bool driveRects = _layoutOwner == LayoutOwner.Source;
        int driven = 0;
        int i = 0;
        while (i < n)
        {
            if (pairs[i].Apply(isRoot: i == 0, driveRects: driveRects))
            {
                driven++;
                i++;
                continue;
            }
            // The node is suppressed or its branch is switched off on the source: its whole subtree
            // is invisible, so jump past it in one step (see BuildSubtreeExtents). The guard is
            // structural paranoia, not a real case — a zero extent would spin this loop forever,
            // and an infinite loop inside a per-frame net tick is not a failure mode worth risking
            // on a pre-built index.
            int next = pairs[i].SkipTo;
            i = next > i ? next : i + 1;
        }

        // MEASUREMENT LEFT BEHIND (see the class doc's cost model). Two counters, both free when
        // the perf monitor is off: how many nodes this mirror actually drove, and how many the
        // extents let it skip. Their ratio is the answer to "is the mirrored board still the
        // expensive thing?" in the next hardware log, without a profiler and without a rebuild —
        // grep '[Perf] COUNTS' for Mirror.NodesDriven / Mirror.NodesSkipped.
        Core.PerfMonitor.Count("Mirror.NodesDriven", driven);
        Core.PerfMonitor.Count("Mirror.NodesSkipped", n - driven);
    }

    /// <summary>
    /// Size and place the clone in its dock, mirroring <c>TrayMountedPanelSurface.Place</c>: ONE
    /// shared pixel density for every docked panel (<c>PlayTray.TrayPixelsPerMeter</c>), clamped
    /// down only when the content would overflow this dock's budget, and the panel grows from the
    /// mount origin along <see cref="_grow"/>.
    /// </summary>
    private void Fit()
    {
        if (_host == null || _pivot == null || _cloneRect == null || _source == null)
            return;

        AdoptParentRect(_source);
        // (a) Re-assert the owner's column BEFORE measuring, so the union below is taken off the
        // layout the panel will actually present. Change-gated to two float compares in the steady
        // state, and a no-op entirely on the LayoutOwner.Source path.
        ApplyOwnersColumn();
        if (!TryMeasureDock(out Vector2 sizePx, out Vector2 centerPx))
            return; // mid-layout / nothing visible: keep the previous fit rather than a degenerate one
        if (!AcceptMeasure(sizePx))
            return; // absurd measure — nothing is committed and Refresh withholds (see AcceptMeasure)
        float w = sizePx.x, h = sizePx.y;

        float density = ContentDensity;
        float heightFit = _mountMaxHeight * density / h;
        float fit = Mathf.Clamp(
            _fitWidth ? Mathf.Min(_mountWidth * density / w, heightFit) : heightFit,
            MinDensityScale, MaxDensityScale);
        float metersPerPx = fit / density;

        _host.transform.localPosition = new Vector3(
            _grow.x * w * metersPerPx * 0.5f,
            _grow.y * h * metersPerPx * 0.5f,
            0f);
        _host.transform.localScale = Vector3.one * metersPerPx;

        // Centre the measured content on the host origin — by moving the PIVOT, never the clone
        // (see EnsureHost for why touching the clone root's anchors would rewrite its layout).
        _pivot.anchoredPosition = new Vector2(-centerPx.x, -centerPx.y);

        // The MR backing rides THIS measure, not a second one: whatever rect the mirror decided to
        // present is exactly the rect the plate must cover (see the class doc's MIXED REALITY
        // block). Host-local px — the host scale above carries them into board metres.
        _backingSizePx = sizePx;

        // The applied fit in MOUNT-LOCAL METRES. Published because a caller that stacks something
        // UNDER this panel has to know how tall it actually came out — the mirrored decision row's
        // use-bar drawer is the shipped case, and it used to hang below an ASSUMED plate height,
        // which is exactly the kind of drift the 1:1 rule is about.
        FittedSize = new Vector2(w * metersPerPx, h * metersPerPx);
        _fitApplied = true;

        LogFit(w, h, fit, metersPerPx);
    }

    /// <summary>True once a fit has actually been APPLIED to the current clone (reset with the
    /// clone). The gate that keeps an unfitted clone off a peer's board — see
    /// <see cref="AcceptMeasure"/>.</summary>
    private bool _fitApplied;

    /// <summary>Why the mirror is currently withholding a clone it HAS built, or null. Set only on
    /// the <see cref="LayoutOwner.CloneAtBoardOwnersWidth"/> path; <see cref="Refresh"/> turns it
    /// into a false return so the caller draws its own fallback.</summary>
    private string? _withhold;

    /// <summary>
    /// (1) NEVER SILENTLY DRAW A BROKEN PANEL — the sanity envelope on a measure that is now
    /// RE-DERIVED rather than read off the owner's own dock.
    ///
    /// <para>WHY THIS EXISTS ONLY ON THE FLAGGED PATH. On <see cref="LayoutOwner.Source"/> the
    /// primary measure is <see cref="TryDockRect"/>, i.e. the rect the owner's own converted panel
    /// already committed — exact by construction, nothing to sanity-check. Under
    /// <see cref="LayoutOwner.CloneAtBoardOwnersWidth"/> that rect no longer describes this clone
    /// (see <see cref="TryMeasureDock"/>) and the number comes from this class's own union of clone
    /// graphics instead. A union is a reconstruction, and a reconstruction can be wrong: a
    /// half-built layout, a clipper that has not settled, a decorator's stray graphic. A peer seeing
    /// the mod-drawn fallback rows is a far better outcome than a cropped, zero-height or
    /// board-covering clone, so an implausible number withholds the mirror instead of committing
    /// it.</para>
    ///
    /// <para>THE BOUND, AND WHY IT CANNOT FIRE ON A HEALTHY PANEL. It is the dock budget times
    /// <see cref="OversizeFactor"/> — the identical envelope <see cref="TryMeasure"/> already uses
    /// to decide that a single graphic is a full-screen backdrop rather than content. If one
    /// GRAPHIC that size is by definition not part of this panel, a committed UNION that size is by
    /// definition not this panel either. With the shipped defaults that is
    /// 0.208 m × 1440 px/m × 3 = 899 px wide and 0.32 m × 1440 px/m × 3 = 1382 px tall, against a
    /// panel the hardware log settles at roughly 300–400 px wide and 80–98 px tall: a margin of
    /// more than 2× on the width and more than 14× on the height. Nothing short of a genuinely
    /// broken measure reaches it.</para>
    ///
    /// <para>The SMALL end is already covered and deliberately handled differently:
    /// <see cref="TryMeasureDock"/> returns false below <see cref="MinMeasuredPixels"/>, which keeps
    /// the previous fit rather than committing a degenerate one — the right answer for a transient
    /// mid-layout frame. What that cannot cover is the FIRST fit, where there is no previous one to
    /// keep and the host would still be sitting at its identity scale, i.e. one metre per pixel.
    /// <see cref="_fitApplied"/> is the gate for that case: no fit, no picture.</para>
    /// </summary>
    private bool AcceptMeasure(Vector2 sizePx)
    {
        _withhold = null;
        if (_layoutOwner != LayoutOwner.CloneAtBoardOwnersWidth)
            return true;

        float density = ContentDensity;
        float maxW = _mountWidth * density * OversizeFactor;
        float maxH = _mountMaxHeight * density * OversizeFactor;
        if (sizePx.x <= maxW && sizePx.y <= maxH)
            return true;

        _withhold = $"the re-wrapped clone measured {sizePx.x:F0}x{sizePx.y:F0} px, past the " +
                    $"{maxW:F0}x{maxH:F0} px sanity envelope ({OversizeFactor:F0}x this dock's " +
                    $"{_mountWidth:F3}x{_mountMaxHeight:F3} m budget at {density:F0} px/m) — the " +
                    "graphics union is not measuring this panel, so the mirror is withheld and the " +
                    "mod-drawn rows are drawn instead of a cropped or board-covering clone";
        return false;
    }

    /// <summary>Last logged panel size in metres — the change gate for <see cref="LogFit"/>.</summary>
    private Vector2 _loggedSize = new(-1f, -1f);

    /// <summary>
    /// State the panel's applied geometry ONCE per real change (2 % + 1 mm dead band, so the 4 Hz
    /// re-fit stays silent). This is the mirror's counterpart to <c>TrayMountedPanelSurface</c>'s
    /// "Docked …" line, and it exists for the same reason: a "the panel on his board is the wrong
    /// size / in the wrong place" report has to be answerable from the hardware log alone, without
    /// a screenshot — the metres, the pixel measure and the glyph scale are all in one line.
    /// </summary>
    private void LogFit(float w, float h, float fit, float metersPerPx)
    {
        var size = new Vector2(w * metersPerPx, h * metersPerPx);
        if (Mathf.Abs(size.x - _loggedSize.x) < _loggedSize.x * 0.02f + 0.001f
            && Mathf.Abs(size.y - _loggedSize.y) < _loggedSize.y * 0.02f + 0.001f)
            return;
        _loggedSize = size;
        VRLog.Info("Net", $"Remote board '{_name}' mirror fitted: {size.x:F3}x{size.y:F3} m " +
                          $"({w:F0}x{h:F0} px), glyph scale {metersPerPx * 1000f:F4} mm/px " +
                          $"(fit {fit:F3}), budget {_mountWidth:F3}x{_mountMaxHeight:F3} m, " +
                          $"measured via {_measurePath}, " +
                          $"mount-local {_host!.transform.localPosition:F3} under '{_mount.name}' " +
                          $"(mount board-local {_mount.localPosition:F3}, grow {_grow}). " +
                          "'via converted host rect' means this panel is fitted to the EXACT rect " +
                          "the OWNER's own dock uses (TrayMountedPanelSurface.Place reads the same " +
                          "number), so its size and its lift above the mount are theirs by " +
                          "construction; 'via graphics union' is the fallback measure for a source " +
                          "that is not a converted panel — a panel sitting too high/low is a " +
                          "measure question and this line says which measure produced it.");
    }

    /// <summary>Corner scratch for <see cref="TryMeasure"/> (<c>GetWorldCorners</c> fills a caller
    /// buffer, so the measure allocates nothing).</summary>
    private static readonly Vector3[] CornerScratch = new Vector3[4];

    /// <summary>Which of the two measure paths produced the last applied fit — stated in the fit
    /// log so a hardware run says whether the mirror matched the owner's dock exactly or fell back
    /// to its own union. See <see cref="TryMeasureDock"/>.</summary>
    private string _measurePath = "none";

    /// <summary>
    /// THE PANEL'S DOCK GEOMETRY — defect (b) of this round ("die gespiegelte Initiativleiste sitzt
    /// VIEL zu hoch über dem Brett").
    ///
    /// ─── WHAT WENT WRONG ───────────────────────────────────────────────────────────────────────
    /// A docked panel's world position is <c>mount + grow · size/2</c>: the SIZE is what lifts the
    /// initiative track above the board's top edge. The owner's dock takes that size from
    /// <c>Panel.HostRect.rect</c> — the rect <c>CanvasConversion.FitHostToContent</c> already
    /// fitted to the panel's visible content, with the content re-centred inside it. This mirror
    /// instead re-derived a size from its own union of clone graphics, using a much laxer
    /// visibility test than the one the local fit uses (alpha &gt; 0.001 instead of an EFFECTIVE
    /// alpha ≥ 0.05, no <c>CanvasRenderer.cull</c> test, no clipper clamp) and an oversize guard
    /// that required a graphic to blow BOTH budgets — which a full-screen element cannot do
    /// against the track's 0.64 m wide dock (3 × 0.64 m × 2400 px/m = 4608 px, wider than any
    /// screen). So the track's own full-canvas blocker rode into the union, the height ballooned
    /// past the budget, the fit clamped at <see cref="MinDensityScale"/>, and the panel was drawn
    /// half-size and lifted by half of a screen-sized height. Exactly the symptom.
    ///
    /// ─── THE FIX: MEASURE WHAT THE OWNER'S DOCK MEASURES ───────────────────────────────────────
    /// PRIMARY — when the source is a CONVERTED panel (which is what both mirrored widgets are in
    /// VR: the game's own track/objectives canvas, moved onto a WorldUI world-space host), its
    /// fitted HOST RECT is available directly, and it is the very number
    /// <c>TrayMountedPanelSurface.Place</c> feeds into the same formula. Using it makes the
    /// mirrored panel geometrically identical to the owner's dock BY CONSTRUCTION rather than by
    /// two measurements agreeing — and the centre is zero, because that fit already re-centred the
    /// content on the host origin (the pivot is sized to that same rect, see AdoptParentRect).
    ///
    /// FALLBACK — the graphics union, for a source that is not converted (flat mode, conversion
    /// disabled, mid-conversion frames), now with the LOCAL fit's own visibility rules.
    /// </summary>
    private bool TryMeasureDock(out Vector2 sizePx, out Vector2 centerPx)
    {
        sizePx = default;
        centerPx = Vector2.zero;

        // (c) THE OWNER'S HOST RECT NO LONGER DESCRIBES THIS CLONE once the clone is wrapping at a
        // DIFFERENT column: TryDockRect returns the VIEWER's Panel.HostRect.rect, which
        // CanvasConversion fitted around the VIEWER's wrapped content. Committing it for a clone
        // that re-wrapped at the owner's column would crop or float the panel by exactly the
        // difference between the two dials — the defect this flag exists to remove, re-entering
        // through the measure. So the flagged path takes the union, which measures the clone that
        // is actually on the board. (It is the same fallback the decision row has shipped on since
        // ModBuild 137, calibrated there against the owner's own numbers to the pixel — see the
        // DOCK MEASURE derivation below.)
        if (_layoutOwner == LayoutOwner.Source && TryDockRect(out Vector2 dock))
        {
            _measurePath = "converted host rect";
            sizePx = dock;
            return true;
        }

        if (!TryMeasure(out Bounds b))
            return false;
        // (2) The wrap column rides into the seat line through here, so the per-peer 'Remote board
        // content' diagnostic states which column the panel it just measured was wrapped at.
        _measurePath = _layoutOwner == LayoutOwner.CloneAtBoardOwnersWidth
            ? $"graphics union at the owner's {_ownersColumnPx:F0} px column"
            : "graphics union";

        // ─── THE FALLBACK MEASURES WHAT THE OWNER'S FIT MEASURES, PADDING AND CLAMP INCLUDED ────
        //
        // USER RULING (standing, ModBuild 137): a mirrored piece of the control board must be
        // „genau die gleiche Größe und Position" as on its owner's board. The PRIMARY path is exact
        // by construction (it reads the owner's own host rect), and this fallback was not: it took
        // the BARE union of visible clone graphics, while <c>CanvasConversion.TryMeasureContent</c>
        // — the measure behind every <c>Panel.HostRect.rect</c> a docked surface fits to — does
        // three things to that same union before it becomes a host rect
        // (CanvasConversion.3.Fit.cs:800-830):
        //   1. clamps it into the TARGET'S OWN RECT (the frame), unless the target is degenerate
        //      (<c>ConvertedPanel.FitFrameDegenerate</c>, latched at Convert from a target whose
        //      rect is under 1 px on an axis — the 0x0 layout containers ObjectivesSurface found);
        //   2. adds <c>FitContentPaddingPx</c> on EVERY side, i.e. 2 × 12 px per axis;
        //   3. clamps the padded size back to that same frame extent.
        // Steps 1 and 3 are why the padding is not simply "+24 px": on a source whose own rect
        // already bounds its content — the decision row is exactly that, an isolated row of the
        // game's own take-damage widgets — the frame wins and the fitted rect IS the row's rect.
        //
        // HIS ModBuild 137 LOGS, both halves of the same row. The owner (peer log) converted it at
        // `Converted 'DecisionDock' to world space (720x48 px)` and never re-fitted it — there is no
        // `Host rect fit 'GloomhavenVR.Panel_DecisionDock'` line in the whole session, because the
        // fit's own result equalled the current size and an unchanged fit is not applied. The
        // mirror (host log) reported `Remote board 'DecisionRow' mirror fitted: 0.369x0.025 m
        // (708x48 px) … measured via graphics union`. 708 + 24 = 732 → clamped to the 720 px frame;
        // 48 + 24 = 72 → clamped to the 48 px frame: 720x48, the owner's number to the pixel. The
        // bare union was 1.7 % narrow. It did not bite in that session only because both sides
        // clamp the density at MaxDensityScale = 1.0 (owner min(0.420·1920/720, 0.120·1920/48) =
        // 1.12, mirror 1.139 — both clamp), so the error stayed a size error and never became a
        // scale error; on longer wording or a narrower budget the two would fit at different
        // densities, which is the divergence the ruling forbids.
        //
        // REJECTED: (a) padding without the clamps — that is the naive reading of "add the same
        // padding" and it would have made the row 732x72, i.e. turned a 1.7 % width error into a
        // 50 % HEIGHT error and moved everything the drawer stacks under it (RowHeight); (b) making
        // the OWNER publish its rect on the wire — no wire change is warranted for a number both
        // machines can derive from the same object, and the record layout is settled; (c) leaving it
        // and relying on the density clamp — that is an accident of his budget, not a guarantee.
        Vector2 min = new(b.min.x, b.min.y);
        Vector2 max = new(b.max.x, b.max.y);
        bool framed = TryFrameExtent(out Vector2 frameMin, out Vector2 frameMax);
        if (framed)
        {
            min = Vector2.Max(min, frameMin);
            max = Vector2.Min(max, frameMax);
        }
        Vector2 sz = max - min;
        if (sz.x < MinMeasuredPixels || sz.y < MinMeasuredPixels)
            return false; // the frame cropped the union away — keep the previous fit
        Vector2 union = sz;
        sz += Vector2.one * (2f * WorldUI.CanvasConversion.FitContentPaddingPx);
        if (framed)
        {
            sz.x = Mathf.Min(sz.x, frameMax.x - frameMin.x);
            sz.y = Mathf.Min(sz.y, frameMax.y - frameMin.y);
        }
        sizePx = sz;
        centerPx = (min + max) * 0.5f;
        LogDockMeasure(union, sz, framed, frameMax - frameMin);
        return true;
    }

    /// <summary>
    /// The CLONE ROOT'S own rect in pivot-local pixels — the mirror's counterpart to the frame
    /// <c>CanvasConversion.TryMeasureContent</c> clamps into (<c>panel.Target</c>'s live world
    /// corners, expressed in host-local space). Read from the clone rather than the source so it is
    /// the same object the union above was measured on, and via world corners for the same reason
    /// the local fit uses them: a live show-animation scale must be honoured.
    ///
    /// <para>False for a DEGENERATE root — the local fit's <c>FitFrameDegenerate</c> rule, same 1 px
    /// test (CanvasConversion.1.Core.cs:160): a 0x0 layout container has no real frame, and clamping
    /// to it would crop the union to a corner of visibly overflowing content.</para>
    ///
    /// <para>(d) THE LIVE TEST IS ONLY VALID WHILE NOBODY WRITES THE CLONE ROOT, which stops being
    /// true under <see cref="LayoutOwner.CloneAtBoardOwnersWidth"/>: <see cref="ApplyOwnersColumn"/>
    /// forces a real width onto a rect that was authored (0,0), and the live test would then report
    /// a frame where the game has none and crop the panel's height to the conversion placeholder.
    /// <see cref="LatchFrameDegenerate"/> takes the AUTHORED verdict before that write and it is
    /// consulted first here. On the <see cref="LayoutOwner.Source"/> path the latch is never set, so
    /// what runs is exactly the live 1 px test that shipped.</para>
    /// </summary>
    private bool TryFrameExtent(out Vector2 frameMin, out Vector2 frameMax)
    {
        frameMin = default;
        frameMax = default;
        if (_pivot == null || _cloneRect == null)
            return false;
        if (_frameDegenerate)
            return false; // authored 0x0 container — the union IS the frame (see LatchFrameDegenerate)
        Rect r = _cloneRect.rect;
        if (r.width < 1f || r.height < 1f)
            return false; // degenerate root — the union IS the frame (see the doc)

        _cloneRect.GetWorldCorners(CornerScratch);
        Vector3 a = _pivot.InverseTransformPoint(CornerScratch[0]);
        Vector3 c = _pivot.InverseTransformPoint(CornerScratch[2]);
        // Min/max-normalised exactly like the local fit: a mid-animation rotation or negative scale
        // must not invert the frame and turn the clamp into garbage.
        frameMin = Vector2.Min(a, c);
        frameMax = Vector2.Max(a, c);
        return frameMax.x - frameMin.x >= 1f && frameMax.y - frameMin.y >= 1f;
    }

    /// <summary>Change gate for the <c>DOCK MEASURE</c> line (union|committed|frame).</summary>
    private string? _loggedDockMeasure;

    /// <summary>
    /// THE ONE LINE that proves the fallback measure now agrees with the owner's — grep
    /// <c>DOCK MEASURE</c>. It prints the three numbers the equivalence rests on (the visible union,
    /// the committed rect, and the frame that bounded it), so "his row is a different size than
    /// mine" stays answerable from a hardware log without a screenshot. Change-gated to the pixel,
    /// and only ever emitted on the union path — the converted-host-rect path is exact by
    /// construction and says so in the fit line.
    /// </summary>
    private void LogDockMeasure(Vector2 union, Vector2 committed, bool framed, Vector2 frame)
    {
        string state = $"{union.x:F0}x{union.y:F0}|{committed.x:F0}x{committed.y:F0}|" +
                       $"{(framed ? $"{frame.x:F0}x{frame.y:F0}" : "none")}";
        if (_loggedDockMeasure == state)
            return;
        _loggedDockMeasure = state;
        VRLog.Info("Net", $"DOCK MEASURE '{_name}': visible union {union.x:F0}x{union.y:F0} px " +
                          $"+ 2x{WorldUI.CanvasConversion.FitContentPaddingPx:F0} px fit padding per axis " +
                          (framed
                              ? $"clamped to the source's own {frame.x:F0}x{frame.y:F0} px frame "
                              : "with NO frame clamp (the source root's rect is degenerate — the union is the frame) ") +
                          $"⇒ {committed.x:F0}x{committed.y:F0} px. This is term for term what " +
                          "CanvasConversion.TryMeasureContent produces for the OWNER's Panel.HostRect.rect, " +
                          "so the mirrored panel and the owner's dock fit the SAME rect at the same " +
                          "density — the fallback used to commit the bare union and came out narrow " +
                          "(708x48 instead of 720x48 on his ModBuild 137 decision row).");
    }

    /// <summary>
    /// The fitted HOST RECT of the converted panel whose target is our mirror source, in uGUI
    /// pixels. False when the source is not a converted panel or its host rect is still degenerate
    /// (pre-fit / mid-teardown), so the caller falls back to its own union.
    ///
    /// A linear walk over <c>CanvasConversion.ActivePanels</c> — a handful of entries, on the 4 Hz
    /// content cadence. Read-only: nothing here touches the panel, its host or its target.
    /// </summary>
    private bool TryDockRect(out Vector2 sizePx)
    {
        sizePx = default;
        System.Collections.Generic.IReadOnlyList<WorldUI.ConvertedPanel> panels =
            WorldUI.CanvasConversion.ActivePanels;
        for (int i = 0; i < panels.Count; i++)
        {
            WorldUI.ConvertedPanel p = panels[i];
            if (p == null || p.HostRect == null || !ReferenceEquals(p.Target, _source))
                continue;
            Rect r = p.HostRect.rect;
            if (r.width < MinMeasuredPixels || r.height < MinMeasuredPixels)
                return false; // converted but not fitted yet — keep the previous fit
            sizePx = new Vector2(r.width, r.height);
            return true;
        }
        return false;
    }

    /// <summary>
    /// The panel's extent, in clone-root pixel space: the UNION OF VISIBLE GRAPHICS, not the root's
    /// rect.
    ///
    /// WHY NOT THE RECT. <c>ObjectivesSurface</c> established from the game's own prefabs that these
    /// containers have authored rects of literally (0,0) — their size normally comes from a parent
    /// layout in the 2D HUD, which does not exist here — and that the conversion machinery therefore
    /// falls back to a 100 px placeholder. <c>RectTransformUtility.CalculateRelativeRectTransformBounds</c>
    /// would fold that meaningless root rect (and every stretch-anchored, canvas-sized child) into the
    /// measure. The local dock measures the fitted union of visible GRAPHICS for exactly this reason,
    /// so this does too — and it is nearly free here, because the graphics are already resolved in
    /// <see cref="Pair"/>.
    ///
    /// VISIBILITY IS THE LOCAL FIT'S TEST, VERBATIM (<c>CanvasConversion.TryGetVisibleHostRect</c>):
    /// enabled, a live <c>CanvasRenderer</c> that is not culled, an EFFECTIVE alpha (own colour ×
    /// the inherited CanvasGroup alpha) at or above <see cref="FitMinAlpha"/>, and a non-degenerate
    /// draw rect. The laxer test this used to run — <c>color.a &gt; 0.001</c>, no cull test, no
    /// inherited alpha — is half of why a faded-out full-canvas element could inflate the union
    /// (defect (b)); an invisible click-catcher stays clickable but must not size a panel.
    ///
    /// A graphic larger than the dock budget on EITHER axis is skipped: that is a full-screen
    /// blocker or backdrop (the initiative track owns one for its enemy-card reveal — an always-
    /// active <c>Graphic</c> that only toggles <c>raycastTarget</c>), which by definition cannot be
    /// part of what fits into a 0.26–0.64 m dock, and letting it into the union would collapse the
    /// whole panel to the minimum density for as long as it is up. It used to require BOTH axes,
    /// which no screen-sized element can satisfy against a 0.64 m budget (3 × 0.64 × 2400 = 4608 px
    /// — wider than any screen), so the guard never once fired: the other half of defect (b).
    /// </summary>
    private bool TryMeasure(out Bounds bounds)
    {
        bounds = default;
        if (_pivot == null)
            return false;

        float density = ContentDensity;
        float maxW = _mountWidth * density * OversizeFactor;
        float maxH = _mountMaxHeight * density * OversizeFactor;

        bool any = false;
        Pair[] pairs = _pairs;
        for (int i = 0; i < pairs.Length; i++)
        {
            // AN EXTERNALLY SHOWN BRANCH IS NOT PART OF THE PANEL'S EXTENT. It is a hover POPUP the
            // caller raises over the widget, and it stays active across the whole frame now that
            // the drive no longer flips it off (see Pair.External) — where before it was only ever
            // active between ApplyHoverOverrides and the next Sync, i.e. never at this measure. Left
            // in, the panel would shrink on a peer's board for exactly as long as that peer hovers
            // an enemy: the owner's own dock never sizes itself to the popup either, so including it
            // would be a 1:1 breach, not a fix. One index jump per branch, the extents' whole point.
            if (pairs[i].External)
            {
                int past = pairs[i].SkipTo;
                i = past > i ? past - 1 : i;
                continue;
            }
            RectTransform? rect = pairs[i].DstRect;
            Graphic? g = pairs[i].DstGraphic;
            if (rect == null || g == null || !g.enabled || !rect.gameObject.activeInHierarchy)
                continue;
            CanvasRenderer cr = g.canvasRenderer;
            if (cr == null || cr.cull)
                continue;
            if (g.color.a * cr.GetInheritedAlpha() < FitMinAlpha)
                continue;
            Rect r = rect.rect;
            if (r.width < 0.5f || r.height < 0.5f)
                continue; // collapsed layout cell / empty stretch container
            if (r.width >= maxW || r.height >= maxH)
                continue; // full-screen blocker / backdrop — see the note above

            rect.GetWorldCorners(CornerScratch);
            for (int k = 0; k < 4; k++)
            {
                Vector3 p = _pivot.InverseTransformPoint(CornerScratch[k]);
                if (!any)
                {
                    bounds = new Bounds(p, Vector3.zero);
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(p);
                }
            }
        }

        return any && bounds.size.x >= MinMeasuredPixels && bounds.size.y >= MinMeasuredPixels;
    }

    /// <summary>How many dock budgets wide/high a graphic may be before it counts as a backdrop
    /// rather than content (see <see cref="TryMeasure"/>). Deliberately generous: normal content
    /// overflowing its dock a little is what the density clamp is for.</summary>
    private const float OversizeFactor = 3f;

    // ------------------------------------------------------------------ node pair --

    /// <summary>
    /// One source ⇄ clone node with its presentation components resolved ONCE at build time. Storing
    /// the references (rather than looking them up) is what makes the per-frame drive affordable:
    /// a two-hundred-node panel costs a couple of hundred field copies and not one
    /// <c>GetComponent</c>.
    /// </summary>
    /// <remarks>Deliberately NOT a <c>readonly struct</c>: <see cref="Suppressed"/> is latched
    /// once at build time by <see cref="SuppressSecretBranches"/> through the array element
    /// (<c>_pairs[i].Suppress()</c>), which needs an addressable, mutable element. Every other
    /// field stays <c>readonly</c>, and the array is only ever indexed — never enumerated by value
    /// — so no copy can lose the flag.</remarks>
    private struct Pair
    {
        public readonly Transform Src;
        public readonly Transform Dst;

        /// <summary>Index of the first pair that is NOT a descendant of this one — i.e. where this
        /// node's subtree ends in the pre-order pairing. Built once per rebuild by
        /// <see cref="BuildSubtreeExtents"/>; <see cref="Sync"/> jumps here when
        /// <see cref="Apply"/> reports the branch is not live.</summary>
        public readonly int SkipTo;

        /// <summary>PERMANENTLY off: this node renders a per-character SECRET (a battle goal, a
        /// personal quest) and must never appear on a mirrored board. See
        /// <see cref="SuppressSecretBranches"/> for the evidence and the rule.</summary>
        public bool Suppressed { get; private set; }

        /// <summary>Latch <see cref="Suppressed"/> and hide the clone node for good.</summary>
        public void Suppress()
        {
            Suppressed = true;
            if (Dst != null && Dst.gameObject.activeSelf)
                Dst.gameObject.SetActive(false);
        }

        /// <summary>
        /// THE CALLER OWNS THIS BRANCH'S VISIBILITY, AND THE CLONE OWNS ITS GEOMETRY.
        ///
        /// <para><b>THE DEFECT THIS RETIRES</b> — user report 2026-09-05, item 14, verbatim: "Bei
        /// der remote Gegnerinfo hatte ich wieder den Fall dass alle Buchstaben untereinander statt
        /// nebeneinander aufgeführt wurden … Das tritt nicht immer auf - aber es sollte NIEMALS
        /// auftreten" (<c>remote-generinfo-textproblem.jpg</c>: the mirrored enemy-info card with
        /// its caption rendered as a single column of glyphs). Same shape as ModBuild 448's
        /// short-rest captions, one class up.</para>
        ///
        /// <para><b>WHY.</b> <see cref="Sync"/>'s cost model rests on one sentence — "an off branch
        /// is invisible, so its interior does not need driving" (see <see cref="BuildSubtreeExtents"/>).
        /// That sentence is FALSE for exactly one branch in this mod. A peer's mirrored initiative
        /// track shows an enemy's info popup when the PEER hovers it (record 16), and
        /// <c>RemoteInitiativeTrack.ApplyHoverOverrides</c> re-activates the CLONE of a
        /// <c>MonsterBaseUI</c> whose SOURCE — this client's own copy — is switched off, because
        /// this client is not hovering anything. So the branch the drive skips as "invisible" is the
        /// one the viewer is looking at, and its interior is frozen at whatever
        /// <c>Instantiate</c> captured.</para>
        ///
        /// <para>What <c>Instantiate</c> captures is the trouble: <c>MonsterBaseUI.GenerateCard</c>
        /// destroys and re-instantiates the card body on every generation (MonsterBaseUI.cs:293/304)
        /// while the popup is INACTIVE — <c>TogglePreview</c> only activates it afterwards, and
        /// <b>uGUI runs no layout on an inactive object</b>. A body regenerated and never activated
        /// therefore carries layout-DRIVEN rects nobody has driven: a caption box far narrower than
        /// any wording, and TMP, doing exactly what it is told, wraps after every glyph. The clone
        /// of that is a permanent column, because <see cref="Neutralize"/> then destroyed the very
        /// layout components that would have fixed it.</para>
        ///
        /// <para><b>AND THAT IS THE INTERMITTENCY.</b> The picture is correct exactly when the
        /// LOCAL player happened to open that same enemy's popup (or the enemy-reveal screen
        /// animated it, which also activates it) at some point AFTER the clone was built: the source
        /// is active then, the branch is not skipped, and the drive copies the now-resolved rects in
        /// and they stay. Nothing about the PEER decides it — which is why "das tritt nicht immer
        /// auf".</para>
        ///
        /// <para><b>THE FIX, and why it cannot depend on the source.</b> A marked node's active flag
        /// is never written here (the caller decides it) and its subtree is never driven from an
        /// off source, and <see cref="Neutralize"/> KEEPS the stock uGUI layout components inside
        /// it. The clone is a live subtree of an active world-space canvas, so uGUI lays it out
        /// itself the moment the caller shows it — the same engine, the same widths, the same text,
        /// and no dependence whatever on this client having opened the popup first.</para>
        /// </summary>
        public bool External { get; private set; }

        /// <summary>Latch <see cref="External"/>. Through the array element, like
        /// <see cref="Suppress"/>.</summary>
        public void MarkExternal() => External = true;

        /// <summary>The clone-side rect and graphic, exposed for <see cref="TryMeasure"/> — the fit
        /// measures the same objects the drive writes, so the two can never disagree.</summary>
        public RectTransform? DstRect => _dstRect;
        public Graphic? DstGraphic => _dstGraphic;

        private readonly RectTransform? _srcRect;
        private readonly RectTransform? _dstRect;
        private readonly Graphic? _srcGraphic;
        private readonly Graphic? _dstGraphic;
        private readonly TMP_Text? _srcTmp;
        private readonly TMP_Text? _dstTmp;
        private readonly Text? _srcText;
        private readonly Text? _dstText;
        private readonly Image? _srcImage;
        private readonly Image? _dstImage;
        private readonly RawImage? _srcRaw;
        private readonly RawImage? _dstRaw;
        private readonly CanvasGroup? _srcGroup;
        private readonly CanvasGroup? _dstGroup;

        public Pair(Transform src, Transform dst, int skipTo)
        {
            Src = src;
            Dst = dst;
            SkipTo = skipTo;
            _srcRect = src as RectTransform;
            _dstRect = dst as RectTransform;
            _srcGraphic = src.GetComponent<Graphic>();
            _dstGraphic = dst.GetComponent<Graphic>();
            _srcTmp = _srcGraphic as TMP_Text;
            _dstTmp = _dstGraphic as TMP_Text;
            _srcText = _srcGraphic as Text;
            _dstText = _dstGraphic as Text;
            _srcImage = _srcGraphic as Image;
            _dstImage = _dstGraphic as Image;
            _srcRaw = _srcGraphic as RawImage;
            _dstRaw = _dstGraphic as RawImage;
            _srcGroup = src.GetComponent<CanvasGroup>();
            _dstGroup = dst.GetComponent<CanvasGroup>();
        }

        /// <summary>
        /// Push one node's live state across. Every write is change-gated: a uGUI setter dirties the
        /// graphic (and a TMP text setter re-runs auto-size layout), so writing an unchanged value
        /// every frame would be the single most expensive thing this class does.
        ///
        /// THE ROOT NODE KEEPS ITS OWN ACTIVE FLAG. Everything else copies the source's
        /// <c>activeSelf</c> verbatim — that is how a completed objective row or a departed
        /// initiative entry disappears — but the root does not, because whether the LOCAL client is
        /// currently DISPLAYING the panel is a local presentation question (the flat HUD is hidden
        /// in VR; a WorldUI surface can be switched off) and has nothing to do with whether a PEER's
        /// board should show it. The remote board's own visibility gate decides that.
        ///
        /// <para>RETURNS whether this node's SUBTREE is live and therefore worth walking. False
        /// means suppressed, destroyed, or switched off on the source — in every one of those cases
        /// the descendants render nothing, so <see cref="Sync"/> jumps the whole run
        /// (<see cref="SkipTo"/>) instead of paying ~20 Unity property accesses per hidden node.
        /// The frame a branch comes back on, this returns true again and the interior is driven in
        /// that same frame, so nothing is ever a frame stale.</para>
        ///
        /// <para><paramref name="driveRects"/> FALSE hands this node's GEOMETRY to the clone itself
        /// (<see cref="LayoutOwner.CloneAtBoardOwnersWidth"/>): the anchors, pivot, size and
        /// anchored position are left for the clone's own surviving layout groups to decide at the
        /// board owner's column, while every CONTENT write below still comes from the source. Local
        /// rotation and scale keep driving in both modes — a <c>LayoutGroup</c> reads a child's
        /// scale but never writes it, so they contest nothing.</para>
        /// </summary>
        public bool Apply(bool isRoot, bool driveRects)
        {
            if (Src == null || Dst == null)
                return false;

            // SECRECY, before anything else: a suppressed branch is never driven and never
            // re-activated, whatever the source does. See SuppressSecretBranches. Skipping the
            // interior is strictly SAFER than driving it — a suppressed node's descendants could
            // previously re-activate themselves from the source, inside a branch whose whole point
            // is that it must never be visible.
            if (Suppressed)
            {
                if (Dst.gameObject.activeSelf)
                    Dst.gameObject.SetActive(false);
                return false;
            }

            // EXTERNALLY SHOWN, before the active flag is touched: the caller decides whether this
            // branch is on, and the clone's own surviving layout decides what is inside it. Writing
            // the source's flag here would fight the caller once a frame (an enable/disable flap
            // that re-dirties the whole branch's layout every frame), and driving the interior from
            // an off source would copy rects no layout has ever resolved. See External.
            if (External)
                return false;

            bool on = isRoot || Src.gameObject.activeSelf;
            if (Dst.gameObject.activeSelf != on)
                Dst.gameObject.SetActive(on);
            if (!on)
                return false; // an invisible branch's interior does not need driving

            if (driveRects)
            {
                if (_srcRect != null && _dstRect != null)
                {
                    if (_dstRect.anchorMin != _srcRect.anchorMin) _dstRect.anchorMin = _srcRect.anchorMin;
                    if (_dstRect.anchorMax != _srcRect.anchorMax) _dstRect.anchorMax = _srcRect.anchorMax;
                    if (_dstRect.pivot != _srcRect.pivot) _dstRect.pivot = _srcRect.pivot;
                    if (_dstRect.sizeDelta != _srcRect.sizeDelta) _dstRect.sizeDelta = _srcRect.sizeDelta;
                    if (_dstRect.anchoredPosition3D != _srcRect.anchoredPosition3D)
                        _dstRect.anchoredPosition3D = _srcRect.anchoredPosition3D;
                }
                else if (_dstRect == null && Dst.localPosition != Src.localPosition)
                {
                    Dst.localPosition = Src.localPosition;
                }
            }
            if (Dst.localRotation != Src.localRotation) Dst.localRotation = Src.localRotation;
            if (Dst.localScale != Src.localScale) Dst.localScale = Src.localScale;

            if (_srcGraphic != null && _dstGraphic != null)
            {
                if (_dstGraphic.enabled != _srcGraphic.enabled) _dstGraphic.enabled = _srcGraphic.enabled;
                if (_dstGraphic.color != _srcGraphic.color) _dstGraphic.color = _srcGraphic.color;
            }

            // TMP first: TMP_Text IS a Graphic, so it must not also be treated as a plain Text.
            if (_srcTmp != null && _dstTmp != null)
            {
                if (_dstTmp.text != _srcTmp.text) _dstTmp.text = _srcTmp.text;
                if (_dstTmp.fontSize != _srcTmp.fontSize) _dstTmp.fontSize = _srcTmp.fontSize;
            }
            else if (_srcText != null && _dstText != null)
            {
                if (_dstText.text != _srcText.text) _dstText.text = _srcText.text;
            }

            if (_srcImage != null && _dstImage != null)
            {
                if (!ReferenceEquals(_dstImage.sprite, _srcImage.sprite)) _dstImage.sprite = _srcImage.sprite;
                if (_dstImage.type != _srcImage.type) _dstImage.type = _srcImage.type;
                // The fill amount IS the progress bar and the cooldown sweep — the one number whose
                // omission would leave a mirrored panel looking right and reading wrong.
                if (_dstImage.fillAmount != _srcImage.fillAmount) _dstImage.fillAmount = _srcImage.fillAmount;
                CopyMaterial(_srcImage, _dstImage);
            }
            else if (_srcRaw != null && _dstRaw != null)
            {
                // The character PORTRAIT: CharacterPortraitsProvider assigns this texture at runtime
                // out of the misc_characterportraits bundle. Copying the reference is the whole
                // reason a mirrored initiative entry shows the real face.
                if (!ReferenceEquals(_dstRaw.texture, _srcRaw.texture)) _dstRaw.texture = _srcRaw.texture;
                if (_dstRaw.uvRect != _srcRaw.uvRect) _dstRaw.uvRect = _srcRaw.uvRect;
                CopyMaterial(_srcRaw, _dstRaw);
            }

            if (_srcGroup != null && _dstGroup != null && _dstGroup.alpha != _srcGroup.alpha)
                _dstGroup.alpha = _srcGroup.alpha;

            return true;
        }

        /// <summary>
        /// SHARE the source graphic's MATERIAL by reference — the state a colour/sprite/texture
        /// copy provably cannot carry, and the last GLOBAL initiative-track highlight the mirror
        /// was silently dropping.
        ///
        /// <para>WHAT IT FIXES, read from the game's own source:</para>
        /// <list type="bullet">
        /// <item>GRAYSCALE. <c>InitiativeTrack.UpdateInitiativeTrack</c> (InitiativeTrack.cs:614/625)
        ///   calls <c>InitiativeTrackActorAvatar.SetGrayscale</c>, whose ENTIRE effect is
        ///   <c>m_AvatarImage.material = grayscale ? grayscaleMaterial : regularMaterial</c>
        ///   (InitiativeTrackActorAvatar.cs:123-126). It marks an EXHAUSTED hero and, during the
        ///   action phases, every character that is not the one at turn. Both inputs (the phase and
        ///   <c>Choreographer.m_CurrentActor</c>) are host-replicated, so this is GLOBAL state that
        ///   was simply not being mirrored: a peer's board showed the whole party in full colour
        ///   while every real track in the session had them greyed.</item>
        /// <item>The UIFX INITIATIVE GLOW and the DAMAGE-WARNING animation. <c>UIFX_MaterialFX_Control</c>
        ///   INSTANTIATES its own material per image in <c>Awake</c> (UIFX_MaterialFX_Control.cs:93-116)
        ///   and then animates <c>material.SetFloat("_FXAnim", …)</c> on that instance
        ///   (:528-544); <c>LeanTweenGuiAnimationSettingMaterial(PropertyFloat)</c> — the family
        ///   <c>InitiativeTrackPlayerBehaviour.warningAnimation</c> is built from — does the same.
        ///   Sharing the reference means the clone renders through the very instance the original is
        ///   animating, so the effect plays on the mirror for free, at zero cost and with no second
        ///   animator.</item>
        /// </list>
        ///
        /// <para>WHY SHARING IS SAFE, and why it is not a write into game state: the clone carries
        /// no behaviour at all (<see cref="Neutralize"/> destroyed every non-presentation
        /// component before it ever woke), so nothing on this side can write to the material it now
        /// points at. This is the identical contract the sprite and the portrait TEXTURE have been
        /// shipping under since the mirror existed.</para>
        ///
        /// <para>DELIBERATELY IMAGE/RAWIMAGE ONLY. TMP overrides the <c>material</c> accessor and
        /// re-derives sub-mesh materials from its font atlas; assigning across a clone boundary
        /// there would fight TMP rather than mirror it, and no track or objectives highlight is
        /// carried by a TMP material.</para>
        ///
        /// <para>Change-gated on reference equality: <c>Instantiate</c> already copied the field, so
        /// a panel whose materials never change costs one reference compare per node per frame and
        /// writes nothing (a <c>Graphic.material</c> setter dirties the graphic).</para>
        /// </summary>
        private static void CopyMaterial(Graphic src, Graphic dst)
        {
            Material s = src.material;
            if (!ReferenceEquals(dst.material, s))
                dst.material = s;
        }
    }
}
