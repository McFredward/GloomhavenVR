using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// THE UNDISCOVERED TILES ARE RANKED ON THE SAME DISTANCE LADDER AS EVERYTHING ELSE — the
/// draw-order half of the fog-of-war look, in BOTH mixed-reality states.
///
/// <para>THE ONE RULE, stated first because two user reports pull in opposite directions and this
/// driver must answer both with the same mechanism: <b>a fog-of-war hex is painted at the slot its
/// MEASURED DISTANCE earns it on the converted-panel ladder — over everything the ladder puts
/// behind it, under everything the ladder puts in front of it — and this driver never asks what
/// the hex is made of.</b> Correct painter's order is all that is needed for both halves, because
/// the MATERIAL decides what correct order looks like: the shipped hex is translucent, so painting
/// it last lets the board show through it (its hardcoded ZWrite can no longer erase anything —
/// everything behind it is already in the framebuffer); the mixed-reality hex is opaque (the mod's
/// own dark underlay, wafer and rim curtain, <see cref="MixedReality"/>), so painting it last hides
/// the board behind it. One rule, no case distinction, no two code paths that can disagree.</para>
///
/// <para>USER REPORT A (2026-08-09, MR OFF, verbatim): "Wenn nicht der Mixed-Reality-Modus an ist,
/// sind die noch-nicht-entdeckten Tiles durchsichtig. Das ist auch so gewollt. Allerdings ist nicht
/// alles des Boards dahinter sichtbar. Die Entscheidungssymbole, die Initiativreihenfolge und
/// mancher Text des Boards verschwindet dahinter."</para>
///
/// <para>USER REPORT B (2026-08-09, MR ON, verbatim): "Im Mixed-Reality-Modus sind die
/// 'unseen-tiles' nicht mehr transparent, d.h. es soll auch nicht darunter sichtbar sein - aktuell
/// sieht man den Text der Quest am Controllboard noch hindurch - das soll nicht sein."</para>
///
/// <para>ROOT CAUSE, SHARED BY BOTH (the full argument, with its sources, is in
/// <c>WorldUI/CanvasConversion.9b.SeeThrough.cs</c>'s header): everything in the fog-of-war stack
/// sits at the default <c>sortingOrder</c> 0 while the mod's own content sits on the distance
/// ladder at 95..300+, and Unity resolves renderers by sortingLayer → sortingOrder FIRST, distance
/// LAST. So the whole stack is painted BEFORE every converted panel and before the control board's
/// furniture band, whatever the geometry says.
/// <list type="bullet">
/// <item>MR OFF: the shipped hex (<c>Amp_Basic_Unseen</c>, renderQueue 3000, RenderType 'Overlay')
/// is translucent AND hardcodes ZWrite ON in its first pass, so painting it first stamps depth at
/// the tile plane and ERASES every panel and every piece of board text drawn afterwards behind it.
/// That is report A's "nur manche Elemente": the board's opaque parts (slab, lip, keycap bodies,
/// card slabs at queue ≤ 2500) were already in the framebuffer when the tile blended over them and
/// survive; everything transparent drawn later does not.</item>
/// <item>MR ON: the mod re-renders the family opaque with mod-built backings
/// (<c>GloomhavenVR.MrUnseenUnderlay</c> / <c>…Fill</c> / <c>…Rim</c>, Sprites/Default at
/// renderQueue 2500, ZWrite off — <see cref="MixedReality"/> never assigns them a sortingOrder, so
/// they inherit 0 too). An opaque surface painted BEFORE the thing it should hide hides nothing:
/// the board's furniture band (the hardware log has it at 143..291) and the converted panels are
/// painted afterwards, write no depth of their own and are not depth-tested against a plate that
/// wrote none — so the quest text shines straight through a solid tile. That is report B.</item>
/// </list>
/// The two reports are therefore ONE defect seen from two sides, and the fix is the one rule above:
/// stop leaving the stack at order 0.</para>
///
/// <para>WHY NOT SIMPLY CLEAR THE ZWRITE, which is what "prefer a depth-honest fix" would normally
/// mean: the pass state is hardcoded. The in-game probe (<c>MixedReality</c>'s state dump over
/// _ZWrite/_ZTest/_SrcBlend/_DstBlend/_Cull) reported 'hardcoded' for all five — the material
/// exposes no _ZWrite, so no MaterialPropertyBlock and no material write can reach it, and a
/// patched shader would mean a bundle. And it would not answer report B at all, which is an
/// order defect between two surfaces of which NEITHER writes depth. Nothing here touches a game
/// material, a shader, a queue or a mesh: this driver writes ONE integer per renderer,
/// <c>Renderer.sortingOrder</c>, and puts the authored value back when it lets go.</para>
///
/// <para>THE RANK IS MEASURED, NOT ESCALATED. Every hex asks
/// <see cref="WorldUI.CanvasConversion.SeenThroughBounds"/> where it belongs given its own eye
/// distance. This is the same manoeuvre the card cues use (<c>CardGlow.CardCueOrder</c> →
/// <c>OrderAboveDistance</c>, the ModBuild-94 outline fix) and the same one the board's furniture
/// uses (<c>CanvasConversion.9.Furniture</c>) — a measured-distance rank, deliberately chosen over
/// bigger numbers, because a number fight only relocates the problem. PERSPECTIVE STAYS HONEST:
/// a panel genuinely IN FRONT of a tile still keeps its higher slot and still covers it, and an
/// opaque thing in front (a card slab, a figure, the board itself) still occludes the tile through
/// the ordinary depth test, which this driver never disables.</para>
///
/// <para>MIXED REALITY IS RANKED, NOT PARKED (round 2, report B). The MR backings are CHILDREN of
/// the very source renderers this driver adopts, so the same scan picks them up and the same hex
/// decision covers them: a backing and its source share one sortingOrder, and the backing's earlier
/// renderQueue (2500 vs 3000) keeps it drawn first INSIDE that shared slot — bit-identical to the
/// relative order MR has always relied on ("the backings must draw BEFORE the family, against a
/// depth buffer that does not yet contain the family surfaces", MixedReality's round-6 note), and
/// exactly the offset-0 contract <c>WorldUI.MrBacking</c>'s panel plates already use. Raising a
/// backing's order can only move it LATER, never earlier, so MR's own safety argument against the
/// revealed floor (which is opaque, depth-writing, at queue ~2000 and wins by ZTest) is untouched.</para>
///
/// <para>MULTIPLAYER: local rendering only — no wire field, no packet, no shared state. A PEER's
/// mirrored board is drawn by <c>Net/Remote*</c> at BoardVisual's fixed sub-ladder (orders 0/4/8),
/// which is BELOW every value this driver can assign, so a peer board behind an undiscovered tile
/// is revealed by the same lift as everything else the ladder knows about — but it cannot TRIGGER
/// a lift, because those renderers are not ladder-ranked and this driver cannot see them.</para>
/// </summary>
internal static class UnseenTileOrder
{
    /// <summary>
    /// Head-room above the farthest-behind ladder slot, for that slot's own order followers: a
    /// window's close X rides at +2, its grab bar at +4, and the game's hover tooltip laid on a
    /// menu plane at +10 (<c>CanvasConversion.PanelOrderStep</c>'s doc). All of them belong to the
    /// panel behind the tile and are just as much "behind" it, so the tile must clear the largest
    /// of them; and the value must stay under the step of 16 so it can never reach the NEXT
    /// panel's slot. 12 is the value <c>CardGlow.CuePanelLift</c> already uses for the same
    /// question.
    /// </summary>
    private const int TileOrderLift = 12;

    /// <summary>Seconds between two scans for live 'Preview' nodes. The set changes only when a
    /// room is revealed, a map tile finishes generating, or mixed reality builds/destroys its
    /// backings — all seconds-scale events — and a scan walks the (few dozen)
    /// <c>ProceduralMapTile</c>s, not every renderer in the scene.</summary>
    private const float RescanSeconds = 2f;

    /// <summary>Consecutive evaluations a NEW decision must be asked for before it is written,
    /// once the current one has actually gone invalid. The second flicker gate, mirroring
    /// <c>CanvasConversion.OrderSwapStableFrames</c>. Six frames is ~0.07 s at 90 Hz.</summary>
    private const int SettleFrames = 6;

    /// <summary>
    /// How close two adopted renderers' bounds centres must be (metres) to be treated as ONE HEX
    /// and therefore to share a single order decision. See <see cref="Rescan"/>'s header for why
    /// this exists at all; the number comes straight from the hardware log's own census:
    /// <list type="bullet">
    /// <item>WITHIN one hex the centres spread by ~0.2 m at most — 'EN_CR_FloorTiles_Damaged_03'
    /// is a 1.57 × 0.06 × 1.44 plate at y −0.16..−0.10 (centre y ≈ −0.13),
    /// 'EN_Unseen_FloorHex_Edge_Damage_03_PR' a 1.75 × 0.32 × 2.01 block at y −0.37..−0.05
    /// (centre y ≈ −0.21), 'Simple Tile' the full-height block at y −0.4..−0.1 — all concentric in
    /// XZ, and the MR backings are built from those same meshes on those same transforms
    /// (coplanar underlay, a wafer dropped ~2 cm, a rim inset 3 cm).</item>
    /// <item>BETWEEN hexes the pitch is ≥ 0.86 m (the Hex-layer proxies in the same dump measure
    /// 1.72 × 0.99 and 2.58 × 2.48).</item>
    /// </list>
    /// 0.35 m sits with a comfortable margin on both sides. Getting it wrong is not dangerous in
    /// either direction: too small merely splits a hex back into the renderers it is made of (the
    /// defect this round fixes), too large merges neighbouring hexes into one coarser decision.
    /// </summary>
    private const float HexGroupRadiusMeters = 0.35f;

    /// <summary>Extra levels below a map tile the 'Generated Content' search may descend
    /// (0 = direct children only). The node sits directly under the tile or one level down —
    /// WallSegmentFade's census header records the same layout ("map tiles nest 'Generated
    /// Content' a level down") — and the limit is what keeps the scan from walking a built
    /// tile's thousands of generated transforms.</summary>
    private const int GeneratedContentDepth = 1;

    /// <summary>Panels/renderers named in one diagnostic line, and the log throttles — same
    /// currency as the ladder's own diagnostics so the two read together.</summary>
    private const float DiagMinIntervalSeconds = 1.5f;

    private const float DiagHeartbeatSeconds = 20f;

    /// <summary>One adopted renderer: the renderer and the sortingOrder it was AUTHORED with
    /// (handed back on release — this driver never keeps a game object changed). The ORDER itself
    /// is not stored here; it belongs to the renderer's hex (<see cref="Group"/>).</summary>
    private struct Member
    {
        public Renderer? Renderer;
        public int Authored;
    }

    /// <summary>
    /// One hex: the renderers standing at one map position, the cached union of their world AABBs,
    /// and THE order decision they all share. The AABB is cached as six plain floats on purpose —
    /// the per-frame pass must not touch a single Unity object (see <see cref="Tick"/>).
    /// </summary>
    private struct Group
    {
        /// <summary>Bounds centre of the first renderer that landed here — the identity anchor a
        /// later renderer is matched against. Fixed for the group's life, so the grouping cannot
        /// drift as members are added.</summary>
        public Vector3 Anchor;

        public Vector3 Min;
        public Vector3 Max;

        /// <summary>Slice of <see cref="Members"/> that belongs to this hex.</summary>
        public int First;

        public int Count;

        /// <summary>Highest / lowest authored sortingOrder among the members — the pair the
        /// stickiness test uses while the group is <see cref="Parked"/>.</summary>
        public int AuthoredMax;

        public int AuthoredMin;

        /// <summary>True while the members carry their own authored orders. False while they all
        /// carry <see cref="Applied"/>.</summary>
        public bool Parked;

        public int Applied;

        /// <summary>False until the first decision has been written — a brand-new hex seats
        /// immediately rather than spending <see cref="SettleFrames"/> frames at order 0, which is
        /// exactly the defect state.</summary>
        public bool Seated;

        public bool PendingParked;
        public int PendingOrder;
        public int Streak;
    }

    private static readonly List<Member> Members = new(1024);
    private static readonly List<Group> Groups = new(256);

    /// <summary>The active 'Preview' nodes the current adoption was taken from.</summary>
    private static readonly List<Transform> Nodes = new(8);

    private static readonly List<Renderer> ScanScratch = new(1024);
    private static readonly List<Transform> NodeScratch = new(8);

    /// <summary>Per-renderer group index produced by the first pass of <see cref="Rescan"/>, and
    /// the per-group member counts the second pass turns into slice offsets. Fields rather than
    /// locals so a rescan allocates nothing.</summary>
    private static readonly List<int> ScanGroupOf = new(1024);

    private static readonly List<int> ScanFill = new(256);

    /// <summary>Landing zone for the counting sort that makes each hex's members contiguous.</summary>
    private static readonly List<Member> MemberScratch = new(1024);

    /// <summary>Scratch for the reconcile in <see cref="Rescan"/> (renderer instance id → what
    /// that renderer already had). A field rather than a local so a rescan allocates nothing; it
    /// is always empty between rescans.</summary>
    private static readonly Dictionary<int, Carry> Carried = new(1024);

    /// <summary>What survives a rebuild for one renderer: the renderer itself, its authored order,
    /// and the decision its hex was carrying. Rebuilding from scratch instead would park every
    /// surviving hex back at order 0 for six frames — a visible flash of the very defect this
    /// driver removes, every time any room is opened or MR toggles.</summary>
    private struct Carry
    {
        public Renderer? Renderer;
        public int Authored;
        public int Applied;
        public bool Parked;
        public bool Seated;
    }

    private static float _nextScanAt;
    private static bool _parked = true;
    private static int _offLayer;
    private static float _diagNextAllowed;
    private static float _diagHeartbeatAt;
    private static int _diagLastHash;

    /// <summary>
    /// Per-frame service, called from <c>CanvasConversion.TickPanelOrder</c> AFTER the panel
    /// ladder, the furniture bands and the ladder snapshot have been built for this frame — the
    /// hexes are ranked against those numbers, so reading them one step later in the same pass is
    /// what makes the answer current rather than one frame stale.
    ///
    /// <para>COST — the whole point of this shape (perf pass 2026-08-09). The loop below touches
    /// NO Unity object at all: one managed point-to-AABB distance and two binary searches over a
    /// ~17-entry array per HEX (113 of them in the shipped scene, for 333+ renderers), and a write
    /// only when a decision actually changes. A steady scene writes nothing and reads nothing.
    /// Everything that must ask Unity a question — the renderer scan, the bounds, the sorting
    /// layer census — lives in <see cref="Rescan"/>, which runs at most every
    /// <see cref="RescanSeconds"/>. The counters below turn "is the see-through driver expensive?"
    /// into arithmetic: '[Perf] STEPS' UnseenTiles, '[Perf] COUNTS' UnseenTiles.Hexes /
    /// UnseenTiles.Renderers / UnseenTiles.Rescans.</para>
    /// </summary>
    internal static void Tick(Vector3 eye)
    {
        using var _perf = PerfMonitor.Scope("UnseenTiles");

        // Nothing of ours on the ladder at all (no live converted panel, no seated furniture
        // group): there is nothing to reveal and nothing to hide, in either MR state. Hand every
        // authored order back rather than hold a lift that no longer stands for anything.
        if (WorldUI.CanvasConversion.SeenThroughLadderEmpty)
        {
            if (!_parked)
                Release();
            return;
        }

        float now = Time.unscaledTime;
        if (now >= _nextScanAt)
        {
            _nextScanAt = now + RescanSeconds;
            using (PerfMonitor.Scope("UnseenTiles.Rescan"))
                Rescan();
            PerfMonitor.Count("UnseenTiles.Rescans");
        }
        if (Groups.Count == 0)
            return;

        // The per-frame cost is LINEAR in the HEX count and in nothing else; the renderer count is
        // what the writes are linear in, and the ratio of the two is the grouping's whole value.
        PerfMonitor.Count("UnseenTiles.Hexes", Groups.Count);
        PerfMonitor.Count("UnseenTiles.Renderers", Members.Count);

        _parked = false;
        int lifted = 0, lowest = int.MaxValue, highest = int.MinValue;
        float near = float.MaxValue, far = 0f;
        for (int g = 0; g < Groups.Count; g++)
        {
            Group grp = Groups[g];

            // Nearest point of the HEX's own cached world AABB — the same "how close does this
            // surface actually come to the eye" question CanvasConversion.PanelEyeDistance answers
            // for a panel, and the reason the answer is per HEX rather than per REGION: a
            // fog-of-war region spans ten metres and more (MAPTILE bounds in the hardware log), so
            // one distance for the whole region would rank its far edge by its near edge and hand
            // a panel that is genuinely in front of that far edge a wrong-side answer.
            float d = AabbDistance(grp, eye);
            WorldUI.CanvasConversion.SeenThroughBounds(d, out int behindTop, out int frontFloor);

            // ---- STICKINESS: hysteresis on the DECISION, not on the distance -------------------
            //
            // USER REPORT (2026-08-09, verbatim): "Ich sehe zwar alles unter dem 'unseen-tiles',
            // allerdings flackern diese jetzt seit deiner Änderung komisch, wenn ich meinen Kopf
            // bewege. Das soll nicht sein."
            //
            // WHAT THE FIRST CUT DID WRONG, and why its two gates could not catch it. It recomputed
            // a wanted ORDER every frame and gated the CHANGE of that number. But the number is a
            // ladder slot, and the ladder underneath is a rank ladder that renumbers itself: in the
            // hardware log (ModBuild 100) thirteen panels sit between 26 m and 33 m — nine ActorBars
            // within 0.6 m of each other, and ActorBars billboard with a full 3-axis LookAt, so head
            // ROTATION alone moves their measured nearest-point distances and reshuffles them. Every
            // reshuffle renumbers panels by whole steps of 16 ('Panel_Objectives' goes 100 → 212 →
            // 196 → 180 → 132 across five consecutive 'PANEL DRAW ORDER' lines), and the wanted
            // number moved with it even when NOTHING about the tile's own position on the ladder had
            // changed. A six-frame settle gate does not reject that: the new number is perfectly
            // stable for six frames, so the gate passes it through and the tile pops. The log shows
            // it directly — 'UNSEEN TILE ORDER' spans jump 0..304, 144..304, 192..304, 276..320,
            // 116..304 line after line, with the lifted count breathing between 290 and 333 of 333.
            //
            // The cure is to stop asking "what would I choose now" and ask "is what I already chose
            // still correct". An order that still clears everything behind and still stays under
            // everything in front is CORRECT, whatever the ladder renumbered itself to, so it is
            // kept and nothing is written. Only a genuine violation — something behind climbed over
            // us, or something in front dropped below us — moves the hex, and then the settle gate
            // below still has to agree six frames running.
            //
            // This also removes the OTHER cliff the first cut had: a hex with nothing behind it was
            // slammed back to its authored 0 the instant the farthest panel drifted past it. Now
            // "nothing behind me" only lifts the LOWER constraint; a hex already lifted stays where
            // it is until it would actually overtake something in front. That is what stops a whole
            // ring of hexes flipping between 0 and ~300 together as the head moves — which is the
            // single largest jump this driver was capable of producing.
            int currentTop = grp.Parked ? grp.AuthoredMax : grp.Applied;
            int currentLow = grp.Parked ? grp.AuthoredMin : grp.Applied;
            bool valid = grp.Seated
                         && currentTop <= frontFloor
                         && (behindTop == WorldUI.CanvasConversion.NoSurfaceBehind
                             || currentLow >= behindTop);

            if (valid)
            {
                grp.Streak = 0;
            }
            else
            {
                int want = WorldUI.CanvasConversion.ResolveSeenThrough(behindTop, frontFloor, TileOrderLift);
                bool wantParked = want == WorldUI.CanvasConversion.NoSurfaceBehind;
                if (!grp.Seated)
                {
                    Apply(ref grp, wantParked, want); // newcomer rule: seat at once, no defect frames
                }
                else if (grp.Streak > 0 && wantParked == grp.PendingParked
                         && (wantParked || want == grp.PendingOrder))
                {
                    if (++grp.Streak >= SettleFrames)
                        Apply(ref grp, wantParked, want);
                }
                else
                {
                    grp.PendingParked = wantParked;
                    grp.PendingOrder = want;
                    grp.Streak = 1;
                }
            }
            Groups[g] = grp;

            if (!grp.Parked)
            {
                lifted++;
                if (grp.Applied < lowest) lowest = grp.Applied;
                if (grp.Applied > highest) highest = grp.Applied;
            }
            if (d < near) near = d;
            if (d > far) far = d;
        }

        LogState(lifted, lowest, highest, near, far);
    }

    /// <summary>Write one hex's decision onto every renderer that belongs to it. Called only when
    /// the decision actually changed, which is why it may afford the Unity round trips.</summary>
    private static void Apply(ref Group grp, bool parked, int order)
    {
        for (int i = grp.First; i < grp.First + grp.Count; i++)
        {
            Member m = Members[i];
            Renderer? r = m.Renderer;
            if (r == null)
                continue; // destroyed with its map tile; the next rescan drops it
            int want = parked ? m.Authored : order;
            if (r.sortingOrder != want)
                r.sortingOrder = want;
        }
        grp.Parked = parked;
        grp.Applied = parked ? grp.AuthoredMax : order;
        grp.Seated = true;
        grp.Streak = 0;
        grp.PendingParked = parked;
        grp.PendingOrder = order;
    }

    /// <summary>Distance from <paramref name="eye"/> to the nearest point of a hex's cached union
    /// AABB. Deliberately a hand-rolled managed computation rather than
    /// <c>Renderer.bounds.SqrDistance</c>: <c>Renderer.bounds</c> is a marshalled property read on
    /// a native object, and this is the one line that runs per hex per frame.</summary>
    private static float AabbDistance(in Group g, Vector3 eye)
    {
        float dx = eye.x < g.Min.x ? g.Min.x - eye.x : (eye.x > g.Max.x ? eye.x - g.Max.x : 0f);
        float dy = eye.y < g.Min.y ? g.Min.y - eye.y : (eye.y > g.Max.y ? eye.y - g.Max.y : 0f);
        float dz = eye.z < g.Min.z ? g.Min.z - eye.z : (eye.z > g.Max.z ? eye.z - g.Max.z : 0f);
        return Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>Hand every authored sortingOrder back and forget the adoption (the ladder going
    /// empty, or the last 'Preview' node going away). Renderers Unity already destroyed are simply
    /// dropped — there is nothing left to restore.</summary>
    internal static void Release()
    {
        for (int i = 0; i < Members.Count; i++)
        {
            Member m = Members[i];
            if (m.Renderer != null && m.Renderer.sortingOrder != m.Authored)
                m.Renderer.sortingOrder = m.Authored;
        }
        Members.Clear();
        Groups.Clear();
        Nodes.Clear();
        _parked = true;
        _offLayer = 0;
        _diagLastHash = 0;
        // Re-adopt on the very next tick rather than up to RescanSeconds later. A release is not a
        // teardown here - the usual cause is the ladder emptying for a moment (every panel closed,
        // a scene swap) - and waiting two seconds after it fills again would leave the tiles at
        // order 0, i.e. in the defect state, for exactly as long as anyone would notice it.
        _nextScanAt = 0f;
    }

    /// <summary>
    /// Find the ACTIVE 'Preview' nodes — the stand-in stacks <c>ProceduralMapTile.ShowContent</c>
    /// switches on for a room that has not been discovered yet — and adopt their renderers,
    /// GROUPED BY HEX.
    ///
    /// <para>WHY THE GROUPING EXISTS (user report 2026-08-09: "flackern diese jetzt komisch, wenn
    /// ich meinen Kopf bewege"). The kit puts THREE authored renderers at every hex position —
    /// 'Simple Tile', 'EN_Unseen_FloorHex_Edge_Damage_03_PR' and 'EN_CR_FloorTiles_Damaged_03', 113
    /// of each in the shipped scene per the MR region census — and mixed reality adds up to three
    /// mod-built backings per source on top. They are co-located but NOT identical: the log's own
    /// numbers make the damage-hex a 1.75 × 0.32 × 2.01 block and the floor plate a 1.57 × 0.06 ×
    /// 1.44 wafer, so their world AABBs give nearest-point distances that differ by 10-20 cm. The
    /// first cut ranked each renderer on its own, and 10-20 cm is more than enough to straddle a
    /// ladder breakpoint in a scene whose panels sit 0.03-0.6 m apart: two surfaces of the SAME hex
    /// then took orders 16 apart, their paint order flipped as the head moved, and because the
    /// authored hex hardcodes ZWrite ON, whichever went first erased part of the other. That is a
    /// flicker of the tiles THEMSELVES rather than of what shows through them, which is what the
    /// report describes, and it is the one failure mode that no amount of hysteresis on a
    /// per-renderer decision can remove — the two decisions were simply allowed to disagree.
    /// Grouping makes disagreement unrepresentable: one hex, one AABB, one decision, and the three
    /// surfaces go back to resolving among themselves by their own queue and depth exactly as
    /// shipped.</para>
    ///
    /// <para>Rebuilds only when the node set OR the renderer count actually changed (a room
    /// revealed, a tile finished generating, MR building or destroying its backings), because a
    /// rebuild costs every hex its settle state.</para>
    /// </summary>
    private static void Rescan()
    {
        NodeScratch.Clear();
        ProceduralMapTile[] tiles = Object.FindObjectsOfType<ProceduralMapTile>();
        for (int t = 0; t < tiles.Length; t++)
        {
            ProceduralMapTile tile = tiles[t];
            if (tile == null)
                continue;
            Transform? preview = FindPreviewNode(tile.transform);
            if (preview != null && preview.gameObject.activeInHierarchy)
                NodeScratch.Add(preview);
        }

        // The cheap re-scan test, in two halves. The RENDERER-COUNT half is what makes the MR case
        // work at all: MR builds its opaque backings as CHILDREN of these very renderers without
        // ever touching the 'Preview' node set, so a node-set test alone would never notice them
        // and they would stay at order 0 forever — report B, unfixed.
        if (SameNodes(NodeScratch) && SameRendererCount(NodeScratch))
            return;

        // RECONCILE, never rebuild from scratch: what survives keeps its authored order and its
        // hex's decision, what is gone gets its authored order handed back, what is new starts
        // unseated (and therefore seats on its very first tick).
        Carried.Clear();
        for (int g = 0; g < Groups.Count; g++)
        {
            Group grp = Groups[g];
            for (int i = grp.First; i < grp.First + grp.Count; i++)
            {
                Member m = Members[i];
                if (m.Renderer == null)
                    continue;
                Carried[m.Renderer.GetInstanceID()] = new Carry
                {
                    Renderer = m.Renderer,
                    Authored = m.Authored,
                    Applied = grp.Applied,
                    Parked = grp.Parked,
                    Seated = grp.Seated,
                };
            }
        }

        Members.Clear();
        Groups.Clear();
        Nodes.Clear();
        ScanScratch.Clear();
        ScanGroupOf.Clear();
        _offLayer = 0;

        // PASS 1 — assign every renderer to a hex and accumulate that hex's union AABB. The hex
        // search checks the LAST matched group first: the kit emits a hex's renderers (and MR its
        // backings) consecutively, so that one test resolves nearly every renderer and the whole
        // pass stays linear in practice while remaining correct if the order is ever different.
        float radiusSq = HexGroupRadiusMeters * HexGroupRadiusMeters;
        int lastGroup = -1;
        for (int i = 0; i < NodeScratch.Count; i++)
        {
            Transform node = NodeScratch[i];
            Nodes.Add(node);
            ScanScratch.Clear();
            node.GetComponentsInChildren(includeInactive: true, ScanScratch);
            for (int j = 0; j < ScanScratch.Count; j++)
            {
                Renderer r = ScanScratch[j];
                if (r == null)
                    continue;
                Bounds b = r.bounds;
                int hit = -1;
                if (lastGroup >= 0 && (Groups[lastGroup].Anchor - b.center).sqrMagnitude <= radiusSq)
                {
                    hit = lastGroup;
                }
                else
                {
                    for (int g = Groups.Count - 1; g >= 0 && hit < 0; g--)
                    {
                        if ((Groups[g].Anchor - b.center).sqrMagnitude <= radiusSq)
                            hit = g;
                    }
                }

                int id = r.GetInstanceID();
                int authored = r.sortingOrder;
                bool carried = Carried.TryGetValue(id, out Carry keep);
                if (carried)
                    authored = keep.Authored; // never re-read our OWN write as the authored value

                if (hit < 0)
                {
                    hit = Groups.Count;
                    Groups.Add(new Group
                    {
                        Anchor = b.center,
                        Min = b.min,
                        Max = b.max,
                        AuthoredMax = authored,
                        AuthoredMin = authored,
                        Applied = carried ? keep.Applied : authored,
                        Parked = !carried || keep.Parked,
                        Seated = carried && keep.Seated,
                    });
                }
                else
                {
                    Group grp = Groups[hit];
                    grp.Min = Vector3.Min(grp.Min, b.min);
                    grp.Max = Vector3.Max(grp.Max, b.max);
                    if (authored > grp.AuthoredMax) grp.AuthoredMax = authored;
                    if (authored < grp.AuthoredMin) grp.AuthoredMin = authored;
                    // The hex's decision comes from whichever member carried one; a hex whose
                    // members were all new stays unseated and seats on the next tick.
                    if (carried && keep.Seated && !grp.Seated)
                    {
                        grp.Applied = keep.Applied;
                        grp.Parked = keep.Parked;
                        grp.Seated = true;
                    }
                    Groups[hit] = grp;
                }
                lastGroup = hit;

                // THE one assumption this whole manoeuvre rests on that no repo artefact could
                // confirm: Unity compares sortingLayer BEFORE sortingOrder, so if the fog-of-war
                // kit were authored onto a non-default sorting layer, no order this driver writes
                // could reach the panels at all. Counted rather than assumed — the log line says
                // it out loud, and a non-zero count is the first thing to read after a "still
                // hidden" report. Counted HERE rather than per frame: a renderer does not change
                // sorting layer, and the census is worth exactly one Unity read per adoption.
                if (r.sortingLayerID != 0)
                    _offLayer++;

                ScanGroupOf.Add(hit);
                Members.Add(new Member { Renderer = r, Authored = authored });
                Carried.Remove(id);
            }
        }

        // PASS 2 — a counting sort that makes each hex's members a CONTIGUOUS slice, so the
        // per-frame apply walks one range per hex instead of chasing indirection. Runs only on a
        // rebuild (a room revealed, a tile generated, MR toggling), never per frame.
        ScanFill.Clear();
        for (int g = 0; g < Groups.Count; g++)
            ScanFill.Add(0);
        for (int i = 0; i < ScanGroupOf.Count; i++)
            ScanFill[ScanGroupOf[i]] = ScanFill[ScanGroupOf[i]] + 1;
        int at = 0;
        for (int g = 0; g < Groups.Count; g++)
        {
            Group grp = Groups[g];
            grp.First = at;
            grp.Count = ScanFill[g];
            Groups[g] = grp;
            at += ScanFill[g];
            ScanFill[g] = grp.First; // reused below as the running fill cursor
        }
        MemberScratch.Clear();
        for (int i = 0; i < Members.Count; i++)
            MemberScratch.Add(default);
        for (int i = 0; i < Members.Count; i++)
            MemberScratch[ScanFill[ScanGroupOf[i]]++] = Members[i];
        Members.Clear();
        for (int i = 0; i < MemberScratch.Count; i++)
            Members.Add(MemberScratch[i]);
        MemberScratch.Clear();
        ScanGroupOf.Clear();

        // Whatever is LEFT in Carried was adopted before and is not adopted now — its room was
        // revealed, or MR destroyed its backings. Hand the authored order back so the now-visible
        // room renders exactly as the shipped game does. (A renderer Unity already destroyed reads
        // as null and needs nothing.)
        foreach (Carry gone in Carried.Values)
        {
            if (gone.Renderer != null && gone.Renderer.sortingOrder != gone.Authored)
                gone.Renderer.sortingOrder = gone.Authored;
        }
        Carried.Clear();
        _parked = Groups.Count == 0;
    }

    /// <summary>Set-identity test for the scanned 'Preview' nodes against the adopted ones.
    /// ORDER-INSENSITIVE: <c>FindObjectsOfType</c> promises no stable ordering, and treating a
    /// reshuffle as a change would reconcile every two seconds forever.</summary>
    private static bool SameNodes(List<Transform> scanned)
    {
        if (scanned.Count != Nodes.Count)
            return false;
        for (int i = 0; i < Nodes.Count; i++)
        {
            Transform mine = Nodes[i];
            if (mine == null)
                return false;
            bool found = false;
            for (int j = 0; j < scanned.Count && !found; j++)
                found = ReferenceEquals(scanned[j], mine);
            if (!found)
                return false;
        }
        return true;
    }

    /// <summary>Second half of the cheap re-scan test: the renderer population under the (already
    /// identical) nodes must still be the same size. This is what catches mixed reality building
    /// or destroying its backing children, which never changes the node set.</summary>
    private static bool SameRendererCount(List<Transform> scanned)
    {
        int total = 0;
        for (int i = 0; i < scanned.Count; i++)
        {
            ScanScratch.Clear();
            scanned[i].GetComponentsInChildren(includeInactive: true, ScanScratch);
            total += ScanScratch.Count;
        }
        return total == Members.Count;
    }

    /// <summary>
    /// The 'Preview' child of a map tile's 'Generated Content' — the node
    /// <c>ProceduralMapTile.ShowContent</c> switches on while a room is still undiscovered
    /// (decompiled GH.Runtime: <c>gameObject.SetActive(show_preview)</c> on the child named
    /// "Preview" under "Generated Content").
    ///
    /// <para>The search is DEPTH-LIMITED on purpose, unlike WallSegmentFade's diagnostic
    /// <c>FindChildByName</c>. That one is an unbounded depth-first walk, which is affordable in a
    /// once-per-heartbeat census but not here: a built map tile's subtree holds hundreds of
    /// generated renderers and thousands of transforms, and a depth-first walk can descend into
    /// all of them before it ever reaches the sibling it wants. The layout is known from the
    /// hardware log's MAPTILE lines ('Generated Content' under the tile, 'Preview' as its
    /// <c>child[0]</c>), so two shallow levels are enough and the scan stays a fixed, tiny cost
    /// per tile.</para>
    /// </summary>
    private static Transform? FindPreviewNode(Transform root)
    {
        Transform? gen = FindChildByName(root, "Generated Content", GeneratedContentDepth);
        return gen == null ? null : FindChildByName(gen, "Preview", 0);
    }

    /// <summary>Exact-name child search over at most <paramref name="depth"/> further levels
    /// (0 = direct children only). Level-by-level, so a shallow hit is never missed because a
    /// deep sibling subtree was walked first.</summary>
    private static Transform? FindChildByName(Transform root, string name, int depth)
    {
        for (int i = 0; i < root.childCount; i++)
        {
            if (root.GetChild(i).name == name)
                return root.GetChild(i);
        }
        if (depth <= 0)
            return null;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform? hit = FindChildByName(root.GetChild(i), name, depth - 1);
            if (hit != null)
                return hit;
        }
        return null;
    }

    /// <summary>
    /// THE line the next hardware test is read against, and the counterpart of the ladder's own
    /// 'PANEL DRAW ORDER' line: how many hexes are adopted, how many are actually LIFTED right
    /// now, and the order span they occupy. A follow-up report of either family is answered
    /// directly by it — "the initiative track still disappears behind an undiscovered tile" (MR
    /// off) or "the quest text still shows through a solid tile" (MR on) is either "the hexes are
    /// still parked at 0", i.e. the ladder had nothing behind them, or "they are lifted and the
    /// artefact is not an ordering one". A FLICKER report is answered by the span: with the sticky
    /// decision in place the span must be quiet while the head moves, and a span that still breathes
    /// from line to line means the ladder underneath is churning, not this driver.
    /// </summary>
    private static void LogState(int lifted, int lowest, int highest, float near, float far)
    {
        int hash = (Groups.Count * 397) ^ (lifted * 31) ^ (lowest * 7) ^ highest;
        float now = Time.unscaledTime;
        bool changed = hash != _diagLastHash;
        bool heartbeat = now >= _diagHeartbeatAt;
        if (!changed && !heartbeat)
            return;
        if (changed && now < _diagNextAllowed && !heartbeat)
            return;
        _diagLastHash = hash;
        _diagNextAllowed = now + DiagMinIntervalSeconds;
        _diagHeartbeatAt = now + DiagHeartbeatSeconds;

        string span = lifted > 0 ? $"orders {lowest}..{highest}" : "none lifted";
        float perHex = Groups.Count > 0 ? (float)Members.Count / Groups.Count : 0f;
        VRLog.Info("Core", $"UNSEEN TILE ORDER ({Nodes.Count} undiscovered 'Preview' node(s), " +
                           $"{Groups.Count} hex(es), {Members.Count} renderer(s) = " +
                           $"{perHex:F1}/hex, d={near:F1}..{far:F1}m): {lifted} hex(es) ranked " +
                           $"onto the panel ladder, {span}. All renderers of ONE hex — the " +
                           "authored trio and any MR backing built on them — share that single " +
                           "order, so they can never fight each other; the order is only " +
                           "rewritten when it stops satisfying the ladder, not when the ladder " +
                           "renumbers itself. A ranked tile is painted AFTER everything behind " +
                           "it: see-through while it is translucent (MR off), occluding while it " +
                           "is opaque (MR on). READ THE RATIO FIRST on any follow-up flicker " +
                           "report: it must be ~3/hex with MR off and higher with MR on. At " +
                           "1.0/hex the proximity grouping failed (HexGroupRadiusMeters vs the " +
                           "kit's real hex pitch) and the co-located pieces are deciding " +
                           "separately again, which is exactly what flickers." +
                           (_offLayer > 0
                               ? $" WARNING: {_offLayer} renderer(s) sit on a NON-DEFAULT sorting " +
                                 "layer — Unity compares the layer before the order, so no order " +
                                 "written here can reach the panels for those; that would need the " +
                                 "layer matched too."
                               : string.Empty));
    }
}
