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
/// <para>ROUND 3 (2026-08-09, verbatim): "Ich habe immer noch das Problem das ich Bewegungen in den
/// unsee-tiles sehe wenn ich a) mein Kopf bewege und b) wenn ich das controllboard darüber oder
/// darunter bewege. Dein letzter fix hat das mit den Kopf bewegungen weniger schlimm gemacht, aber
/// es ist immer noch da. Es ist zum ersten mal aufgetreten bei deinem fix das controllboard
/// vollständig darunter anzuzeigen."</para>
///
/// <para>WHAT THE HEX IS FIGHTING IS THE HEX NEXT TO IT. Round 2 removed the fight INSIDE one hex
/// (three co-located renderers, three decisions); it did not even ask about the fight BETWEEN
/// hexes, and that is the one the report is about — "Bewegungen IN den unseen-tiles", movement in
/// the fog field itself, which is what a seam between two neighbouring hexes changing brightness
/// looks like. The kit's hexes are large translucent surfaces (1.75 × 0.32 × 2.01 for the damage
/// block) laid edge to edge on a table the player looks ACROSS, so from any seated angle a nearer
/// hex overlaps the hexes behind it in screen space — and the family hardcodes ZWrite ON. Which of
/// two overlapping hexes is painted first therefore decides whether the overlap is blended twice
/// (far first, near over it) or once (near first, its depth rejecting the far one): a visibly
/// different shade. Before this driver existed the whole family sat at one order (0) and Unity
/// resolved them among themselves by renderQueue and then by DISTANCE — always back to front,
/// always stable. This driver replaced that with 44 INDEPENDENT decisions taken from a ladder that
/// renumbers itself continuously, which is precisely why the artefact "appeared with the fix that
/// shows the control board underneath".</para>
///
/// <para>SO THE FIELD DECIDES AS A FIELD. Two properties do it, and neither is a tuning value:
/// <list type="number">
/// <item>ONE SNAPSHOT, ALL HEXES. <c>CanvasConversion.ResolveSeenThrough</c> is MONOTONE in the
/// query distance (its doc carries the proof): resolved against the SAME frame's snapshot, a
/// farther hex can never get a higher order than a nearer one. So the field is painted strictly
/// back to front — the shipped behaviour — as long as every hex's order comes from ONE snapshot.
/// A per-hex settle gate breaks exactly that: hex A re-seats on frame n and hex B, six frames
/// behind on its own streak, still carries frame n−6's answer, and for those frames the pair is
/// inverted. <see cref="Tick"/> therefore keeps ONE field-wide gate and writes EVERY hex from the
/// snapshot that opened it. The TRIGGER is unchanged and still sticky — nothing is written unless
/// some hex's current order actually stopped satisfying the ladder — so a ladder that merely
/// renumbers itself still costs nothing.</item>
/// <item>A CONTRADICTORY LADDER IS NOT ANSWERED, IT IS WAITED OUT. While the panel ladder is
/// mid-swap it hands out a HIGHER order to a panel that is measurably FARTHER; a hex between the
/// pair is then asked for an order that is both above and below, and the resolver's clamp answers
/// "above" — lifting the hex over a panel in front of it and dropping it back six frames later,
/// for no reason in the scene at all. <see cref="WorldUI.CanvasConversion.SeenThroughContradiction"/>
/// names that state, and the field is never written on such a frame — the settle streak keeps
/// counting through it, so the first self-consistent frame afterwards applies and nothing settles
/// late. A completed swap does not change HOW MANY panels lie behind a given distance (the ladder
/// hands out the same SET of slots either way, only permuted), so the decision held across the
/// swap is the decision that is correct after it: holding costs nothing at all.</item>
/// </list></para>
///
/// <para>AND THE QUERY ITSELF STOPS TWITCHING (report half a). The ladder's shared dead band is
/// <c>OrderSwapMarginMeters</c> = 2 cm, and a seated player's head sway is several centimetres —
/// the band is NARROWER than the motion it is supposed to reject, so sway alone kept walking
/// panels across each hex's breakpoints. The fix is not a bigger shared margin (that would
/// re-tune every panel on the ladder) but a dead band on THIS driver's own query point: the field
/// is measured from a held eye position and that position only follows the head once it has
/// genuinely travelled <see cref="EyeSwayDeadBandMeters"/>. Below that the query distances are
/// bit-identical from frame to frame, so the bounds are, so the decisions are, so nothing is
/// written. Sway is rejected by construction rather than by a streak that sway can outlast.</para>
///
/// <para>WHAT THIS COSTS: still no Unity call and no allocation on the per-frame path — one
/// squared distance for the dead band, then per HEX one point-to-AABB distance, two binary
/// searches over ~17 floats and a clamp. The clamp now runs every frame instead of only on a
/// violation (a few integer compares per hex, ~44 hexes here) and buys the field-wide hash that
/// makes the atomic gate possible. Writes happen only on an apply, and an apply needs a real
/// violation plus <see cref="SettleFrames"/> stable frames.</para>
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

    /// <summary>Consecutive evaluations the WHOLE FIELD's new answer must be asked for before it
    /// is written, once the current one has actually gone invalid. The second flicker gate,
    /// mirroring <c>CanvasConversion.OrderSwapStableFrames</c>. Six frames is ~0.07 s at 90 Hz.
    ///
    /// <para>FIELD-WIDE, NOT PER HEX (round 3) — a per-hex streak lets neighbouring hexes re-seat
    /// on different frames, and two hexes carrying answers from two different snapshots are exactly
    /// the pair that can end up inverted. See the class header.</para></summary>
    private const int SettleFrames = 6;

    /// <summary>
    /// How far the eye must actually TRAVEL (metres) before the field re-measures itself. The
    /// query point is held between snaps, so every hex distance — and therefore every bound, every
    /// wanted order and the field hash below — is bit-identical from frame to frame while the
    /// player merely sways.
    ///
    /// <para>The number has to sit above natural seated head sway (several centimetres, which is
    /// what walked panels across the ladder's own 2 cm dead band frame after frame) and far below
    /// any motion whose perspective actually matters here: the fog field measures 6-23 m from the
    /// eye in the shipped hardware scene and the panels it is ranked against are metres apart, so
    /// 8 cm of held staleness cannot change which side of a panel a hex is on unless that panel is
    /// already within a hand's width of the tile plane — where the answer is arbitrary in any case
    /// and the only thing that matters is that it stays put.</para>
    /// </summary>
    private const float EyeSwayDeadBandMeters = 0.08f;

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
        /// exactly the defect state. An unseated hex seats the WHOLE field with it, so the field
        /// never mixes a fresh answer with a stale one.</summary>
        public bool Seated;

        /// <summary>What this frame's snapshot would give this hex
        /// (<see cref="WorldUI.CanvasConversion.NoSurfaceBehind"/> = park at the authored order).
        /// Recomputed every frame for every hex and written only when the field applies — that is
        /// what makes every applied order come from ONE snapshot.</summary>
        public int Want;

        public bool WantParked;
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

    /// <summary>The held query point (see <see cref="EyeSwayDeadBandMeters"/>) and whether one has
    /// been taken yet.</summary>
    private static Vector3 _queryEye;

    private static bool _hasQueryEye;

    /// <summary>The field-wide settle gate: how many consecutive frames the SAME wanted-order
    /// vector has been asked for while the current one is invalid, and the hash identifying that
    /// vector. One gate for the whole field, deliberately — see <see cref="SettleFrames"/>.</summary>
    private static int _fieldStreak;

    private static int _fieldWantHash;

    /// <summary>Diagnostic only: the field is holding because the panel ladder is mid-swap and
    /// hands out a contradictory pair of bounds (<see cref="Tick"/>).</summary>
    private static bool _diagHeldContradiction;

    /// <summary>Diagnostic only: how many times the field has been rewritten. THE number a
    /// follow-up flicker report is read by — a rewrite is the only event this driver has that can
    /// change anything on screen, so "the tiles moved while I only turned my head" and "applies did
    /// not move" cannot both be true of this driver.</summary>
    private static int _applies;

    /// <summary>
    /// Per-frame service, called from <c>CanvasConversion.TickPanelOrder</c> AFTER the panel
    /// ladder, the furniture bands and the ladder snapshot have been built for this frame — the
    /// hexes are ranked against those numbers, so reading them one step later in the same pass is
    /// what makes the answer current rather than one frame stale.
    ///
    /// <para>COST — the whole point of this shape (perf pass 2026-08-09). The loop below touches
    /// NO Unity object at all: one managed point-to-AABB distance, two binary searches over a
    /// ~17-entry array and a handful of integer compares per HEX (44-113 of them in the shipped
    /// scene, for 132-333 renderers), and a write only when a decision actually changes. A steady
    /// scene writes nothing and reads nothing; a merely swaying player does not even re-measure
    /// (<see cref="EyeSwayDeadBandMeters"/>). Everything that must ask Unity a question — the
    /// renderer scan, the bounds, the sorting layer census — lives in <see cref="Rescan"/>, which
    /// runs at most every <see cref="RescanSeconds"/>. The counters below turn "is the see-through
    /// driver expensive?" into arithmetic: '[Perf] STEPS' UnseenTiles, '[Perf] COUNTS'
    /// UnseenTiles.Hexes / UnseenTiles.Renderers / UnseenTiles.Rescans / UnseenTiles.FieldApplies.</para>
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

        // ---- (1) THE QUERY POINT IS HELD AGAINST HEAD SWAY ------------------------------------
        //
        // The field measures itself from _queryEye, not from the live eye, and _queryEye only
        // follows once the head has genuinely travelled EyeSwayDeadBandMeters. Below that every
        // AabbDistance below returns the SAME float as last frame, so every bound, every wanted
        // order and the field hash are bit-identical and the gate at the bottom cannot fire.
        // This is the half of the report that says "wenn ich meinen Kopf bewege": the ladder's own
        // dead band is 2 cm and a seated player's sway is bigger than that, so sway alone kept
        // pushing panels across each hex's breakpoints. Sway is now rejected before it is measured
        // rather than after it has been turned into a decision.
        if (!_hasQueryEye || (eye - _queryEye).sqrMagnitude > EyeSwayDeadBandMeters * EyeSwayDeadBandMeters)
        {
            _queryEye = eye;
            _hasQueryEye = true;
        }
        Vector3 query = _queryEye;

        // ---- (2) ONE PASS, ONE SNAPSHOT: what would the WHOLE field be right now ---------------
        //
        // Every hex's wanted order is computed here, for every hex, every frame - and NOT written.
        // Writing is a field-wide decision taken once at the bottom, because
        // CanvasConversion.ResolveSeenThrough is monotone in the query distance only WITHIN one
        // snapshot: answers taken from two different snapshots can put a farther hex above a nearer
        // one, and two overlapping translucent hexes in the wrong order is a visibly different
        // shade along their seam. That is the "Bewegungen in den unseen-tiles" of the report, and
        // it is the one failure mode a per-hex settle gate cannot avoid - it exists precisely to
        // let hexes re-seat on different frames.
        bool contradiction = false, anyInvalid = false, anyUnseated = false;
        int wantHash = 17;
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
            float d = AabbDistance(grp, query);
            WorldUI.CanvasConversion.SeenThroughBounds(d, out int behindTop, out int frontFloor);

            // The ladder is mid-swap across this hex: it is handing a HIGHER order to something
            // measurably FARTHER, so no order satisfies both bounds. Whatever the resolver returns
            // is a guess that will be revoked within OrderSwapStableFrames frames - hold instead.
            if (WorldUI.CanvasConversion.SeenThroughContradiction(behindTop, frontFloor))
                contradiction = true;

            int want = WorldUI.CanvasConversion.ResolveSeenThrough(behindTop, frontFloor, TileOrderLift);
            grp.Want = want;
            grp.WantParked = want == WorldUI.CanvasConversion.NoSurfaceBehind;
            wantHash = wantHash * 31 + want;

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
            // us, or something in front dropped below us — moves the field, and then the settle gate
            // below still has to agree six frames running. THAT TEST IS UNCHANGED IN ROUND 3: what
            // changed is only that it now decides for the field rather than for one hex.
            //
            // This also removes the OTHER cliff the first cut had: a hex with nothing behind it was
            // slammed back to its authored 0 the instant the farthest panel drifted past it. Now
            // "nothing behind me" only lifts the LOWER constraint; a hex already lifted stays where
            // it is until it would actually overtake something in front. That is what stops a whole
            // ring of hexes flipping between 0 and ~300 together as the head moves — which is the
            // single largest jump this driver was capable of producing.
            int currentTop = grp.Parked ? grp.AuthoredMax : grp.Applied;
            int currentLow = grp.Parked ? grp.AuthoredMin : grp.Applied;
            bool valid = currentTop <= frontFloor
                         && (behindTop == WorldUI.CanvasConversion.NoSurfaceBehind
                             || currentLow >= behindTop);
            if (!grp.Seated)
                anyUnseated = true;
            else if (!valid)
                anyInvalid = true;

            Groups[g] = grp;
            if (d < near) near = d;
            if (d > far) far = d;
        }

        // ---- (3) ONE GATE FOR THE WHOLE FIELD -------------------------------------------------
        //
        // A hex that has never been written sits at its authored 0, which IS the defect state, so
        // it seats at once - and it seats the whole field with it, from this same snapshot, rather
        // than being dropped into a field whose other members answer an older one.
        bool apply;
        if (anyUnseated)
        {
            apply = true;
            _fieldStreak = 0;
        }
        else if (!anyInvalid)
        {
            // Everything still satisfies the ladder — the common case, and it holds no matter how
            // much the ladder renumbers itself underneath us, because a permutation of the panel
            // slots does not change HOW MANY of them lie behind a given distance. No writes.
            apply = false;
            _fieldStreak = 0;
        }
        else if (_fieldStreak > 0 && wantHash == _fieldWantHash)
        {
            // The settle gate is on the whole answer, so a contradictory frame's answer differs
            // from the consistent frames around it and resets the streak by itself; the explicit
            // test here is what stops the one case where it would not (a swap that happens to
            // leave every wanted order unchanged) from being written on a mid-swap frame. The
            // streak keeps counting through it, so the field applies on the first frame after the
            // ladder is self-consistent again and nothing settles late.
            apply = ++_fieldStreak >= SettleFrames && !contradiction;
            if (apply)
                _fieldStreak = 0;
        }
        else
        {
            _fieldWantHash = wantHash;
            _fieldStreak = 1;
            apply = false;
        }
        _diagHeldContradiction = contradiction && !apply;

        if (apply)
        {
            PerfMonitor.Count("UnseenTiles.FieldApplies");
            _applies++;
            for (int g = 0; g < Groups.Count; g++)
            {
                Group grp = Groups[g];
                Apply(ref grp, grp.WantParked, grp.Want);
                Groups[g] = grp;
            }
        }

        int lifted = 0, lowest = int.MaxValue, highest = int.MinValue;
        for (int g = 0; g < Groups.Count; g++)
        {
            Group grp = Groups[g];
            if (grp.Parked)
                continue;
            lifted++;
            if (grp.Applied < lowest) lowest = grp.Applied;
            if (grp.Applied > highest) highest = grp.Applied;
        }

        LogState(lifted, lowest, highest, near, far);
    }

    /// <summary>Write one hex's decision onto every renderer that belongs to it. Called only from
    /// the field apply in <see cref="Tick"/> — never for one hex on its own, which is what keeps
    /// every applied order in the field an answer from the SAME snapshot — and the field applies
    /// only when a decision actually stopped being correct, which is why it may afford the Unity
    /// round trips.</summary>
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
        _hasQueryEye = false;
        _fieldStreak = 0;
        _fieldWantHash = 0;
        _diagHeldContradiction = false;
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
        // The group set changed, so the previous field hash describes a vector of a different
        // length: it must not be allowed to match by accident and let a half-formed answer through
        // the settle gate.
        _fieldStreak = 0;
        _fieldWantHash = 0;
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
    ///
    /// <para>ROUND 3 ADDS THE TWO NUMBERS A FLICKER REPORT IS NOW READ BY. 'applies' is the count
    /// of times the field has been rewritten since the mod started: a session in which the player
    /// only looked around and moved the board must leave it nearly still, because every rewrite is
    /// the only event that can change anything the player can see. 'held' says the field is
    /// currently refusing to move because the panel ladder is mid-swap and self-contradictory —
    /// expected in bursts while the ladder resorts, a defect only if it is permanent. Note that
    /// even a rising apply count cannot produce movement BETWEEN hexes: an apply writes the whole
    /// field from one snapshot and that snapshot is monotone in distance, so the fog stays painted
    /// strictly back to front. What an apply can change is a hex's relation to a PANEL.</para>
    /// </summary>
    private static void LogState(int lifted, int lowest, int highest, float near, float far)
    {
        int hash = (Groups.Count * 397) ^ (lifted * 31) ^ (lowest * 7) ^ highest
                   ^ (_applies * 131) ^ (_diagHeldContradiction ? 0x5EED : 0);
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
                           $"onto the panel ladder, {span}, {_applies} field apply(s) so far" +
                           (_diagHeldContradiction ? ", HELD (ladder mid-swap)" : string.Empty) +
                           ". All renderers of ONE hex — the " +
                           "authored trio and any MR backing built on them — share that single " +
                           "order, so they can never fight each other, and the whole FIELD is " +
                           "written from one snapshot at one moment, so two hexes can never carry " +
                           "answers from two different ladders and invert; the field is only " +
                           "rewritten when some hex stops satisfying the ladder, not when the " +
                           "ladder renumbers itself. A ranked tile is painted AFTER everything behind " +
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
