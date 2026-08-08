using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Net;

/// <summary>
/// A LIVE, PIXEL-FAITHFUL COPY of one of the game's own uGUI panels, rendered on a peer's remote
/// control board — the initiative TRACK and the objectives/quest panel today.
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
/// they would fight the puppeteering below — as do <c>Canvas</c>, <c>CanvasScaler</c> and
/// <c>GraphicRaycaster</c>, because the host supplies the one world-space canvas and a copied
/// screen-space canvas would otherwise blit itself over the player's whole view.
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

    /// <summary>Effective-alpha floor a graphic must clear to size the fallback union — verbatim
    /// <c>CanvasConversion.FitMinAlpha</c>, the value the LOCAL dock's own content fit uses. Kept
    /// here as a literal for the same reason the density clamps above are: this file must not
    /// widen WorldUI's internal surface to read three constants.</summary>
    private const float FitMinAlpha = 0.05f;

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

    private GameObject? _host;              // world-space canvas host (child of _mount)
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
    public RemoteWidgetMirror(string name, Transform mount, float mountWidth, float mountMaxHeight,
        Vector2 grow, bool fitWidth = true, float densityScale = 1f)
    {
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
    /// renderQueue is what keeps it under this content and above everything farther back.</summary>
    int WorldUI.MrBacking.IBackedSurface.BackingOrder => BoardVisual.OrderDockedWidget;

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

            Sync();
            Fit();
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
        if (_clone == null || _pairs.Length == 0)
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

        Neutralize(clone);

        _cloneRect = clone.transform as RectTransform;
        if (_cloneRect == null)
        {
            Clear("the game widget's root is not a RectTransform (unexpected prefab shape)");
            return false;
        }

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

        _pairs = new Pair[n];
        for (int i = 0; i < n; i++)
            _pairs[i] = new Pair(srcNodes[i], _walk[i]);
        int secret = SuppressSecretBranches(clone.transform);
        RebuildStamp++; // CloneOf holders must re-resolve against the fresh clone

        // Own head camera renders the mod layer only; the whole clone is ours, so re-layering it is
        // safe (and required — the game face was on a game UI layer).
        VRLayers.Apply(_host);

        _host.SetActive(true);
        VRLog.Info("Net", $"Remote board '{_name}': now mirroring the REAL game widget " +
                          $"('{source.name}', {n} node(s)) — a live CLONE of the panel this client " +
                          "already shows, driven per frame from the original (positions, portraits, " +
                          "text, progress, animations). Zero wire traffic; the source is never " +
                          "touched, re-parented or mutated." +
                          (secret > 0
                              ? $" {secret} node(s) carrying a PER-CHARACTER SECRET goal were " +
                                "suppressed on the clone (see SuppressSecretBranches)."
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
        // A DOCKED WIDGET of the remote board's fixed sub-ladder: strictly above the board's
        // transparent furniture (pick banner at OrderFurniture), whatever the viewing angle -
        // see BoardVisual's sub-ladder header for the angle-dependent blend this pins down.
        canvas.sortingOrder = BoardVisual.OrderDockedWidget;
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

    /// <summary>
    /// Turn the clone into a PUPPET: destroy everything that is not pure presentation (see the class
    /// note), then add a blocking <see cref="CanvasGroup"/> at the root. Runs while the clone is
    /// still inactive, so no destroyed component has ever executed.
    ///
    /// <c>DestroyImmediate</c> rather than <c>Destroy</c>: a deferred destroy would still let a
    /// behaviour's <c>Awake</c>/<c>OnEnable</c> run on the frame the host activates, which is the
    /// entire thing this method exists to prevent.
    /// </summary>
    private static void Neutralize(GameObject clone)
    {
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
                if (c == null || c is Transform || IsPresentation(c))
                    continue;
                if (c is Canvas && pass == 0)
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
    ///   everything else (the game's own MonoBehaviours) — see the class note.
    /// </summary>
    private static bool IsPresentation(Component c) =>
        c is Graphic || c is CanvasRenderer || c is Mask || c is RectMask2D
        || c is BaseMeshEffect || c is CanvasGroup;

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
        _pairs = System.Array.Empty<Pair>();
        _source = null;
    }

    // ------------------------------------------------------------------ drive --

    /// <summary>Depth-first (parent, then children in sibling order) flattening of a subtree into
    /// <paramref name="into"/>. Iterative so a deep panel cannot blow the stack, and buffer-reusing
    /// so the cadence walk does not allocate.</summary>
    private void Walk(Transform root, List<Transform> into)
    {
        into.Clear();
        _stack.Clear();
        _stack.Add(root);
        while (_stack.Count > 0)
        {
            int last = _stack.Count - 1;
            Transform t = _stack[last];
            _stack.RemoveAt(last);
            into.Add(t);
            // Push in reverse so children come out in sibling order — the clone is walked the same
            // way, which is what makes the two flat lists index-aligned.
            for (int i = t.childCount - 1; i >= 0; i--)
                _stack.Add(t.GetChild(i));
        }
    }

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

    /// <summary>Copy the source's live presentation state onto the clone, node by node — INCLUDING
    /// the root, whose rect is as much a part of the authored layout as any child's (for the
    /// objectives container it is literally the wrap column). Re-centring is the pivot's job, one
    /// level up, precisely so this drive can stay a faithful copy.</summary>
    private void Sync()
    {
        Pair[] pairs = _pairs;
        for (int i = 0; i < pairs.Length; i++)
            pairs[i].Apply(isRoot: i == 0);
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
        if (!TryMeasureDock(out Vector2 sizePx, out Vector2 centerPx))
            return; // mid-layout / nothing visible: keep the previous fit rather than a degenerate one
        float w = sizePx.x, h = sizePx.y;

        float density = PlayTray.TrayPixelsPerMeter * _densityScale;
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

        LogFit(w, h, fit, metersPerPx);
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

        if (TryDockRect(out Vector2 dock))
        {
            _measurePath = "converted host rect";
            sizePx = dock;
            return true;
        }

        if (!TryMeasure(out Bounds b))
            return false;
        _measurePath = "graphics union";
        sizePx = new Vector2(b.size.x, b.size.y);
        centerPx = new Vector2(b.center.x, b.center.y);
        return true;
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

        float density = PlayTray.TrayPixelsPerMeter * _densityScale;
        float maxW = _mountWidth * density * OversizeFactor;
        float maxH = _mountMaxHeight * density * OversizeFactor;

        bool any = false;
        Pair[] pairs = _pairs;
        for (int i = 0; i < pairs.Length; i++)
        {
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

        public Pair(Transform src, Transform dst)
        {
            Src = src;
            Dst = dst;
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
        /// </summary>
        public void Apply(bool isRoot)
        {
            if (Src == null || Dst == null)
                return;

            // SECRECY, before anything else: a suppressed branch is never driven and never
            // re-activated, whatever the source does. See SuppressSecretBranches.
            if (Suppressed)
            {
                if (Dst.gameObject.activeSelf)
                    Dst.gameObject.SetActive(false);
                return;
            }

            bool on = isRoot || Src.gameObject.activeSelf;
            if (Dst.gameObject.activeSelf != on)
                Dst.gameObject.SetActive(on);
            if (!on)
                return; // an invisible branch's interior does not need driving

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
