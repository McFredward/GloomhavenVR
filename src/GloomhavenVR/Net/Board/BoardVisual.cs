using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// Tiny shared factory for the read-only remote-board visuals (<see cref="RemoteControlBoard"/>
/// + <see cref="OwnerTag"/>). Everything is UNLIT: the remote board and its ownership tag are
/// mod-owned cosmetics rendered by the mod head camera, and Gloomhaven's scenario/void lighting
/// is not something we control — the same rule the hands and head masks follow
/// (<see cref="HeadMaskLibrary"/>). Never touches game state.
/// </summary>
internal static class BoardVisual
{
    // ---- a remote board is a DISTANCE-RANKED CLUSTER, ordered inside by its own depth --------
    //
    // WHAT WAS HERE BEFORE, AND WHY IT WAS WRONG (user reports 2026-08-09 #3 and #5). The remote
    // board carried a STATIC sub-ladder - OrderFurniture 0 / OrderDockedWidget 4 / OrderTooltip 8
    // - written onto its renderers and canvases at build time. Two defects fell out of it, and
    // both were reported in the same session:
    //
    //   (#5) "Die remote tooltipps respektieren die Perspektive nicht und ueberlagern die
    //        Initiativreihenfolge des remote boards." READ FROM SOURCE, not inferred: the synced
    //        tooltip is seated at PlayTray.TooltipAreaBase - board-local z = -0.02, i.e. 2 cm
    //        proud of the board face - and grows UP/RIGHT from the board's top-LEFT corner, while
    //        the initiative mirror is docked at RemoteBoardLayout.InitiativeMount, whose shipped
    //        z is -0.048 (Oak) / -0.070 (Steel), and grows UP from the top edge. The two overlap
    //        by construction, and the track is 2.8-5.0 cm NEARER THE VIEWER than the hint. The
    //        static ladder said 8 > 4 anyway, so the farther plate painted over the nearer one at
    //        every viewing angle - the exact opposite of the perspective the ladder exists to
    //        respect. (Bronze ships the track at z ~ 0, where the hint IS in front; one rule has
    //        to answer both, and a constant cannot.)
    //
    //   (#3) "Das Steam Bild ueber der Maske ueberlagert sich ueber der Initiativreihenfolge."
    //        The identity tags (RemoteNameTag over a peer's head, OwnerTag on their board corner)
    //        have ridden the converted-panel distance ladder since 2026-08-04 and therefore draw
    //        at order >= PanelOrderBase - PanelOrderStep + lift ~ 96. Everything on the mirrored
    //        board sat at 0..8. No geometry could ever change that comparison: the picture won
    //        against the peer's initiative mirror whether it was in front of the board or a metre
    //        behind it.
    //
    // THE FIX IS THE LADDER'S OWN MEDICINE, TWICE OVER, and it is one mechanism rather than two
    // patches:
    //
    //   1. THE WHOLE BOARD RANKS AS ONE CLUSTER. RemoteControlBoard is an
    //      IFurnitureOrderAnchor, exactly like the LOCAL board (PlayTray), so its content is
    //      seated in the interstitial band just under the nearest panel in front of it and above
    //      every panel behind it (CanvasConversion.9.Furniture.cs). A menu behind a peer's board
    //      no longer paints over their board, and one in front of it now covers it - the mod-wide
    //      perspective ruling, applied to the one surface that was still exempt from it.
    //
    //   2. INSIDE THE CLUSTER, DEPTH DECIDES - AND DEPTH IS A CONSTANT. Every element on a
    //      remote board is seated by its owner's own authored mount, i.e. at a FIXED board-local
    //      z (RemoteBoardLayout). Quantizing that z into a small tier (TierForDepth) reproduces
    //      the true front-to-back order of the board's own plane without measuring anything per
    //      frame - so it is as flicker-free as the constant it replaces (the whole point of the
    //      2026-08-04 #2 round: "the winner inside one board's plane is decided ONCE, never by
    //      distance"), while finally being RIGHT. The tooltip lands under the initiative mirror
    //      on Oak/Steel and over it on Bronze, from the same line of code.
    //
    // WHY A SWEEP AND NOT PER-CALL-SITE CONSTANTS (AdoptBoardOrder). Only seven call sites ever
    // wrote one of the three constants; everything else a peer's board draws - the card faces and
    // their glows, the pile fronts, the objective/element/status stand-ins, the ping rings, the
    // focus stroke - ships at Unity's default 0 and TIED with them. Lifting only the seven into
    // the band would have inverted every one of those ties. The cluster therefore adopts the
    // board's whole transparent subtree, which is also what makes it impossible for a future
    // surface to be forgotten.

    /// <summary>
    /// Board-local metres per intra-board tier. 2 cm resolves every relationship the shipped
    /// layouts actually contain - board face (-0.004) under the tooltip / pick banner (-0.02)
    /// under the docked widgets (-0.044..-0.048) under a deep initiative dock (-0.070) - while
    /// being coarse enough that the millimetre standoffs INSIDE an element (a label 1 mm proud of
    /// its plate, a glow 4 mm proud of a card face) stay a TIE, which is exactly what those
    /// standoffs want: an equal order falls back to Unity's per-renderer distance, and distance is
    /// the correct answer for two surfaces that are millimetres apart and parallel.
    /// </summary>
    private const float TierDepthMeters = 0.02f;

    /// <summary>
    /// The intra-cluster tier for something seated at <paramref name="boardLocalZ"/> board-local
    /// metres (+Z points AWAY from the viewer, so proud = negative). Clamped into the band the
    /// cluster machinery reserves for entries; anything at or behind the board face is tier 0.
    /// </summary>
    internal static int TierForDepth(float boardLocalZ) =>
        Mathf.Clamp(Mathf.RoundToInt(-boardLocalZ / TierDepthMeters),
                    0, WorldUI.CanvasConversion.FurnitureClusterTopOffset);

    // ---- free-floating identity tags ride the PANEL distance ladder -------------------------
    //
    // ROOT CAUSE (user report 2026-08-04: "the Steam logo mixes with the menu window behind it").
    // The Steam-avatar identity tags (RemoteNameTag over a peer's head, OwnerTag on a peer's
    // board corner) are transparent renderers (Sprites/Default quad + TMP label) that shipped at
    // sortingOrder 0. Unity resolves transparent renderers by sortingLayer -> sortingOrder FIRST
    // and only falls back to distance on a tie, and EVERY converted panel lives on the distance
    // ladder at order >= CanvasConversion.PanelOrderBase (100) - so a floated menu that was
    // spatially BEHIND the tag still painted LATER and alpha-blended over the avatar picture:
    // exactly the reported mixing. Perspective must be respected mod-wide (standing user
    // ruling), so the tags now rank against the ladder per frame through the same seam the
    // board tooltip uses: CanvasConversion.OrderAboveDistance answers "which order does a
    // non-panel plate at THIS eye distance need to draw over everything farther and under
    // every panel genuinely nearer".

    /// <summary>
    /// Sub-step lift of an identity tag above the farther panel's ladder slot. Must stay under
    /// CanvasConversion.PanelOrderStep (16) so the tag can never climb into the NEXT panel's
    /// slot; 12 puts a tag that is genuinely in front of a window above that window's own
    /// furniture too (close X +2, grab bar +4, menu-laid tooltip +10) - a billboard hovering
    /// before a window covers the whole window, decorations included.
    /// </summary>
    private const int TagPanelLift = 12;

    /// <summary>
    /// Per-frame: seat a free-floating tag's renderers at the converted-panel-ladder order for
    /// its eye distance (see the root-cause note above). One shared order for the whole row -
    /// the row's own internals (label 1 mm proud of its plate/quad) resolve on the distance
    /// tie-break exactly as before. Change-gated writes; Unity-null entries are skipped (the
    /// caller refreshes its cache on rebuild).
    ///
    /// <para>CLUSTER-AWARE since the 2026-08-09 round (report #3): the query also ranks the tag
    /// against every board CLUSTER on the ladder, which is what decides whether the Steam picture
    /// is in front of a peer's board or behind it. Ranking only against panels is what let it
    /// paint over that board's initiative mirror from any angle - see the header above and
    /// <c>CanvasConversion.OrderAboveDistanceAndClusters</c>.</para>
    /// </summary>
    internal static void OrderWithPanels(Renderer?[] renderers, float eyeDistance)
    {
        int order = PanelLadderOrder(eyeDistance);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer? r = renderers[i];
            if (r != null && r.sortingOrder != order)
                r.sortingOrder = order;
        }
    }

    /// <summary>
    /// The same seat for a free-floating tag drawn by a mod-owned world-space CANVAS rather than by
    /// Renderers — the ping name tag's vanilla-tooltip clone route, whose whole row is uGUI. A
    /// CanvasRenderer is not a Renderer, so the array overload above cannot reach it and a canvas
    /// left at its creation order of 0 loses to every converted panel on the ladder (>= 100) at
    /// every distance and every angle.
    /// </summary>
    internal static void OrderWithPanels(Canvas? canvas, float eyeDistance)
    {
        if (canvas == null)
            return;
        int order = PanelLadderOrder(eyeDistance);
        if (canvas.sortingOrder != order)
            canvas.sortingOrder = order;
    }

    /// <summary>The ladder query both tag seats share — one answer to "which order does a
    /// non-panel plate at THIS eye distance need to draw over everything farther and under every
    /// panel and board cluster genuinely nearer".</summary>
    private static int PanelLadderOrder(float eyeDistance)
        => WorldUI.CanvasConversion.OrderAboveDistanceAndClusters(eyeDistance, TagPanelLift);

    /// <summary>
    /// Adopt every TRANSPARENT renderer and every mod-owned world-space canvas under
    /// <paramref name="boardRoot"/> into <paramref name="anchor"/>'s draw-order cluster, each at
    /// the tier its own board-local depth earns (<see cref="TierForDepth"/>). The remote twin of
    /// <c>PlayTray.AdoptFurniture</c>, with three differences that all follow from what a peer's
    /// board IS:
    ///
    /// <list type="bullet">
    /// <item>IT RUNS ON A CADENCE, not once at build time. A mirrored board rebuilds its cards,
    ///   its fallback chips and its cloned widgets all session long; a one-shot adopt would seat
    ///   the board it was built with and nothing that came after. Registration is idempotent and
    ///   O(1) per candidate (the cluster keeps an id set), so a re-adopt of an unchanged board is
    ///   a walk and no writes. The board re-arms the sweep whenever its CONTENT pass runs, so the
    ///   surfaces that pass creates are adopted in the same tick they appear. RESIDUE, STATED
    ///   HONESTLY: an element created outside that pass still draws at its own creation order (0)
    ///   until the next sweep. It is under its own board's furniture for that window, never over
    ///   the panels - the ordering that was reported - and the elements this can happen to (a card
    ///   face, a chip) are millimetres from the surfaces they tie with.</item>
    /// <item>CANVASES COUNT. Most of what a peer's board draws is a mod-owned world-space canvas
    ///   (the widget mirrors, the synced tooltip, the hosted card faces). The clones inside them
    ///   have had their own Canvas components stripped (RemoteWidgetMirror.Neutralize), so one
    ///   canvas per host is exactly what this finds - it can never stomp a game widget's internal
    ///   layering.</item>
    /// <item>IT SKIPS THE MR BACKING PLATES (<c>MrBacking.PlateObjectName</c>) and the OWNER TAG
    ///   subtree. Both are already driven every frame by somebody else - a plate copies the live
    ///   order of the content it backs, and the tag rides the panel ladder through
    ///   <see cref="OrderWithPanels"/> because it is a free-floating billboard on the board's
    ///   corner, not a surface in the board's plane. Adopting either would be two writers
    ///   fighting over one field.</item>
    /// </list>
    ///
    /// <para>Opaque and AlphaTest materials (queue &lt;= 2500) are skipped exactly as on the local
    /// board: they write depth and already resolve against depthless plates per pixel. Inactive
    /// objects ARE adopted (<c>includeInactive</c>) so a hidden card is seated before it shows.</para>
    /// </summary>
    internal static BoardOrderSweep AdoptBoardOrder(WorldUI.IFurnitureOrderAnchor anchor,
                                                    Transform? boardRoot, Transform? excludedSubtree)
    {
        var sweep = default(BoardOrderSweep);
        if (anchor == null || boardRoot == null)
            return sweep;
        foreach (Renderer r in boardRoot.GetComponentsInChildren<Renderer>(includeInactive: true))
        {
            if (r == null || r.gameObject.name == WorldUI.MrBacking.PlateObjectName)
                continue;
            Material? m = r.sharedMaterial;
            if (m == null || m.renderQueue <= 2500)
                continue; // depth-writing opaque/cutout: correct against depthless plates already
            // A surface that is transparent only because the board is currently FADING (user
            // request 15 — PeerBoardFade installs private alpha-capable clones of the opaque
            // BoardLit slab/keycaps for the duration) is NOT board furniture. Adopting it would
            // lift the slab into the cluster band, i.e. OVER the board's own content — and would
            // leave a cluster registration and a written sortingOrder behind on a renderer that
            // is opaque again the moment the fade releases. Skipped by the clone's name marker,
            // which is the only thing that distinguishes the two states.
            if (m.name.EndsWith(PeerBoardFade.CloneMarker, System.StringComparison.Ordinal))
                continue;
            if (IsUnder(r.transform, excludedSubtree, boardRoot))
                continue;
            sweep.Adopt(TierOn(boardRoot, r.transform), anchor, r, null);
        }
        foreach (Canvas c in boardRoot.GetComponentsInChildren<Canvas>(includeInactive: true))
        {
            if (c == null || IsUnder(c.transform, excludedSubtree, boardRoot))
                continue;
            // A NESTED canvas that opted into overrideSorting is a POPUP a dock opened, not a dock:
            // it hangs out of its host's rect across the board face, so its seat's depth says
            // nothing about what it has to cover. See PopupOverlayTier. Every HOST canvas on a
            // peer's board is a root world-space canvas (RemoteWidgetMirror.Neutralize destroys
            // every cloned Canvas, so nothing here has one by accident), and Unity ignores
            // overrideSorting on a root canvas — so this test names exactly the surfaces that
            // deliberately asked to sort for themselves and nothing else.
            sweep.Adopt(c.overrideSorting ? PopupOverlayTier : TierOn(boardRoot, c.transform),
                        anchor, null, c);
        }
        return sweep;
    }

    /// <summary>
    /// What one <see cref="AdoptBoardOrder"/> pass found, as the next hardware log needs to read
    /// it: how many renderers and canvases the board offered and how they split across the depth
    /// tiers. THE line a "still the wrong way round" report is answered against — together with
    /// the ladder's own <c>FURNITURE ORDER</c> line (which states the band the tiers sit in), it
    /// gives the absolute order of every surface on a peer's board without a screenshot.
    /// Allocation-free: a struct of counters, and the caller only formats it when the split
    /// actually changed.
    /// </summary>
    internal struct BoardOrderSweep
    {
        public int Renderers;
        public int Canvases;

        // One counter per legal tier. Four, because CanvasConversion.FurnitureClusterTopOffset is
        // 3; the assert-by-clamp in TierForDepth is what keeps that true, and the switch below
        // folds anything else into the top tier rather than growing a fifth field silently.
        public int Tier0;
        public int Tier1;
        public int Tier2;
        public int Tier3;

        internal void Adopt(int tier, WorldUI.IFurnitureOrderAnchor anchor,
                            Renderer? renderer, Canvas? canvas)
        {
            if (renderer != null)
            {
                Renderers++;
                WorldUI.CanvasConversion.RegisterFurniture(anchor, renderer, tier);
            }
            else if (canvas != null)
            {
                Canvases++;
                WorldUI.CanvasConversion.RegisterFurniture(anchor, canvas, tier);
            }
            switch (tier)
            {
                case 0: Tier0++; break;
                case 1: Tier1++; break;
                case 2: Tier2++; break;
                default: Tier3++; break;
            }
        }

        /// <summary>Change gate for the caller's log line: the split itself. It moves whenever a
        /// board grows or loses a surface, and stands still for a board that is merely being
        /// redrawn — which is what makes one line per real change possible.</summary>
        internal int Signature =>
            (((Renderers * 397 + Canvases) * 397 + Tier0) * 397 + Tier1) * 397 * 397
            + Tier2 * 397 + Tier3;

        public override string ToString() =>
            $"{Renderers} renderer(s) + {Canvases} canvas(es); depth tiers " +
            $"0:{Tier0} (board face) 1:{Tier1} (tooltip/placard) 2:{Tier2} (docks) 3:{Tier3} (deep dock)";
    }

    /// <summary>The cluster tier of <paramref name="node"/> on <paramref name="boardRoot"/>: its
    /// own position expressed in board-local metres (scale-free - InverseTransformPoint undoes the
    /// board's synced scale), quantized by <see cref="TierForDepth"/>.</summary>
    internal static int TierOn(Transform boardRoot, Transform node) =>
        TierForDepth(boardRoot.InverseTransformPoint(node.position).z);

    /// <summary>
    /// The tier a POPUP OVERLAY takes: the top slot a cluster entry may hold, i.e. above every
    /// other surface on that board and still below every converted panel genuinely in front of it.
    ///
    /// <para>WHY DEPTH IS THE WRONG QUESTION FOR ONE (user report 2026-09-06 item 11, "Die
    /// Gegnerinfo beim laser-mouseover hat eine andere Tiefe beim remote board als beim lokalen
    /// eigenen Board"). <see cref="TierForDepth"/> answers "how proud of the board face is the seat
    /// this surface was DOCKED at", which is right for a dock and wrong for something the dock
    /// opens that hangs OUT of the dock's own rect across the board face. The mirrored initiative
    /// track's enemy-info popup is exactly that: the dock ships at
    /// <c>Defaults.InitiativeOffset_*</c> z = -0.009 on all three boards, i.e. tier 0 — TIED with
    /// the board face, the recess card faces and their art — so the tie fell to Unity's distance
    /// tie-break, the recess cards' canvases measured nearer than the track's canvas above the
    /// board's top edge, and they painted over the popup. That is what the screenshot shows: the
    /// popup draws OVER the opaque stone (an opaque queue draws before any transparent one) and
    /// UNDER two cards that are recessed BEHIND that stone, which no depth test can produce.
    ///
    /// <para>THE LOCAL BOARD'S OWN TERM, MIRRORED. On the local board the initiative track is a
    /// board-DOCKED converted panel, and <c>CanvasConversion.9.Furniture</c> pins the whole
    /// furniture band under the lowest ladder slot any same-board panel holds ("rigid sub-ladder:
    /// band must stay below this slot"). So locally the popup — drawn inside that panel — is above
    /// every piece of that board's furniture at every angle, by construction and not by distance.
    /// A peer's board has no panels, only cluster entries, so this constant is the same contract
    /// expressed in the cluster's own vocabulary.</para></para>
    /// </summary>
    internal const int PopupOverlayTier = WorldUI.CanvasConversion.FurnitureClusterTopOffset;

    /// <summary>True while <paramref name="node"/> lies inside <paramref name="excluded"/>. Walks
    /// UP and stops at <paramref name="stopAt"/> (the board root), so the cost is the node's depth
    /// under the board and never the scene's.</summary>
    private static bool IsUnder(Transform node, Transform? excluded, Transform stopAt)
    {
        if (excluded == null)
            return false;
        for (Transform? t = node; t != null; t = t.parent)
        {
            if (ReferenceEquals(t, excluded))
                return true;
            if (ReferenceEquals(t, stopAt))
                return false;
        }
        return false;
    }

    /// <summary>An unlit material (optionally textured), so mod visuals read the same regardless
    /// of the surrounding scene lights. Mirrors <c>HeadMaskLibrary.UnlitMaterial</c>.</summary>
    internal static Material Unlit(Color color, Texture? texture = null)
    {
        Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default")
                        ?? Shader.Find("Hidden/InternalErrorShader");
        var m = new Material(shader)
        {
            color = new Color(color.r, color.g, color.b, color.a),
        };
        if (texture != null)
            m.mainTexture = texture;
        return m;
    }

    /// <summary>
    /// A collider-free quad on <paramref name="parent"/>, sized <paramref name="size"/> local
    /// metres in its XY plane. By the module convention (+Z points AWAY from the viewer) the quad's
    /// front reads from the −Z side, so a parent whose −Z faces the viewer shows the quad face-on.
    /// </summary>
    internal static MeshRenderer Quad(Transform parent, string name, Vector2 size, Material material)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, worldPositionStays: false);
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = material;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return mr;
    }
}
