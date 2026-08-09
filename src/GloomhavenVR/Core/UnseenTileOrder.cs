using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// SEE-THROUGH UNDISCOVERED TILES MEAN SEE-THROUGH TO EVERYTHING — the draw-order half of the
/// fog-of-war look, for the NON-MR case.
///
/// <para>USER REPORT (2026-08-09, verbatim): "Wenn nicht der Mixed-Reality-Modus an ist, sind die
/// noch-nicht-entdeckten Tiles durchsichtig. Das ist auch so gewollt. Allerdings ist nicht alles
/// des Boards dahinter sichtbar. Die Entscheidungssymbole, die Initiativreihenfolge und mancher
/// Text des Boards verschwindet dahinter. Wenn ich da durch sehen kann, will ich auch alles
/// dahinter durchsehen können, nicht nur manche Elemente."</para>
///
/// <para>ROOT CAUSE (the full argument, with its sources, is in
/// <c>WorldUI/CanvasConversion.9b.SeeThrough.cs</c>'s header): the game's undiscovered-room hex
/// kit is a TRANSLUCENT surface that WRITES DEPTH. <c>Amp_Basic_Unseen</c> draws at renderQueue
/// 3000 with RenderType 'Overlay' and hardcodes ZWrite ON in its first pass (ShaderOcclusionPatcher's
/// bundle scan), at the default sortingOrder 0. Unity resolves transparent renderers by
/// sortingLayer → sortingOrder FIRST, so the tile is painted BEFORE every converted panel (ladder
/// order ≥ 100) and before the control board's furniture band (≥ 95) — and its depth then erases
/// every one of them that lies behind it. The board's OPAQUE parts (slab, lip, keycap bodies,
/// card slabs at queue ≤ 2500) were already in the framebuffer when the tile blended over them,
/// which is why they survive. That is the user's "nur manche Elemente", exactly.</para>
///
/// <para>WHY NOT SIMPLY CLEAR THE ZWRITE, which is what "prefer a depth-honest fix" would normally
/// mean: the pass state is hardcoded. The in-game probe (<c>MixedReality</c>'s state dump over
/// _ZWrite/_ZTest/_SrcBlend/_DstBlend/_Cull) reported 'hardcoded' for all five — the material
/// exposes no _ZWrite, so no MaterialPropertyBlock and no material write can reach it, and a
/// patched shader would mean a bundle. The only lever left mod-side is ORDER; and once the order
/// is depth-correct the depth write stops mattering, because a surface painted LAST cannot erase
/// anything behind it — everything behind it is already in the framebuffer. Nothing here touches
/// a game material, a shader, a queue or a mesh: this driver writes ONE integer per renderer,
/// <c>Renderer.sortingOrder</c>, and puts the authored value back when it lets go. The authored
/// translucent look — which the user explicitly wants kept — is therefore untouched by
/// construction, including how the kit's three co-located renderers per hex ('Simple Tile', the
/// unseen edge hex, the top plate) resolve AMONG THEMSELVES: they measure the same distance, so
/// they take the same order and keep resolving by their own queue and depth exactly as shipped.</para>
///
/// <para>THE RANK IS MEASURED, NOT ESCALATED. Every tile renderer asks
/// <see cref="WorldUI.CanvasConversion.OrderSeenThrough"/> where it belongs given its own eye
/// distance: above everything the ladder puts behind it, below everything the ladder puts in
/// front of it. This is the same manoeuvre the card cues use (<c>CardGlow.CardCueOrder</c> →
/// <c>OrderAboveDistance</c>, the ModBuild-94 outline fix) and the same one the board's furniture
/// uses (<c>CanvasConversion.9.Furniture</c>) — a measured-distance rank, deliberately chosen over
/// bigger numbers, because a number fight only relocates the problem. PERSPECTIVE STAYS HONEST:
/// a panel genuinely IN FRONT of a tile still keeps its higher slot and still covers it, and an
/// opaque thing in front (a card slab, a figure, the board itself) still occludes the tile through
/// the ordinary depth test, which this driver never disables.</para>
///
/// <para>WHAT IS DELIBERATELY LEFT ALONE. A tile with NOTHING of ours behind it keeps its authored
/// sortingOrder 0 (<see cref="WorldUI.CanvasConversion.NoSurfaceBehind"/>): there is nothing to
/// reveal, and lifting it anyway would only buy this manoeuvre's one real regression — a lifted
/// tile draws over foreign order-0 transparent art that is in FRONT of it and writes no depth of
/// its own. That residue is accepted where the lift is actually needed (it is the same trade part
/// 9 accepted for the board's furniture) and avoided everywhere else. It is also structurally
/// small here: the fog-of-war kit lies flat in the floor band (y ≈ −0.4…−0.1 in the hardware log's
/// MAPTILE dumps), so the art that can be "in front of" it in screen space is art standing ABOVE
/// the floor and seen at a grazing angle — and an undiscovered region, by definition, has no
/// figures and no effects standing on it.</para>
///
/// <para>MIXED REALITY IS NOT THIS DRIVER'S SUBJECT. While MR is wanted the unseen family is
/// re-rendered opaque with mod-built backings (<see cref="MixedReality"/>), which is a different
/// mechanism with its own hardware history; this driver parks itself and hands every authored
/// order back, so MR behaviour is bit-identical to the shipped build.</para>
///
/// <para>MULTIPLAYER: local rendering only — no wire field, no packet, no shared state. A PEER's
/// mirrored board is drawn by <c>Net/Remote*</c> at BoardVisual's fixed sub-ladder (orders 0/4/8),
/// which is BELOW every value this driver can assign, so a peer board behind an undiscovered tile
/// is revealed by the same lift as everything else the ladder knows about — but it cannot TRIGGER
/// a lift, because those renderers are not ladder-ranked and this driver cannot see them. See the
/// hand-off note in the round's report.</para>
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
    /// room is revealed or a map tile finishes generating — both are seconds-scale events — and a
    /// scan walks the (few dozen) <c>ProceduralMapTile</c>s, not every renderer in the scene.</summary>
    private const float RescanSeconds = 2f;

    /// <summary>Consecutive evaluations a NEW order must be asked for before it is written. The
    /// second flicker gate, mirroring <c>CanvasConversion.OrderSwapStableFrames</c>: the ladder
    /// below us is already hysteresis-damped, and this stops a tile that happens to sit within a
    /// hair of a panel's distance from chattering between two slots while the head micro-moves.
    /// Six frames is ~0.07 s at 90 Hz.</summary>
    private const int SettleFrames = 6;

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

    /// <summary>One adopted fog-of-war renderer: the renderer, the sortingOrder it was AUTHORED
    /// with (handed back on release — this driver never keeps a game object changed), the order
    /// currently written, and the settle gate's state.</summary>
    private struct Entry
    {
        public Renderer? Renderer;
        public int Authored;
        public int Applied;
        public int Pending;
        public int Streak;
    }

    private static readonly List<Entry> Entries = new(512);

    /// <summary>The active 'Preview' nodes the current <see cref="Entries"/> were taken from —
    /// the cheap re-scan test: same nodes, same renderer count, nothing to rebuild.</summary>
    private static readonly List<Transform> Nodes = new(8);

    private static readonly List<Renderer> ScanScratch = new(256);
    private static readonly List<Transform> NodeScratch = new(8);

    /// <summary>Scratch for the reconcile in <see cref="Rescan"/> (renderer instance id → the
    /// entry that already exists for it). A field rather than a local so a rescan allocates
    /// nothing; it is always empty between rescans.</summary>
    private static readonly Dictionary<int, Entry> Carried = new(512);

    private static float _nextScanAt;
    private static bool _parked = true;
    private static float _diagNextAllowed;
    private static float _diagHeartbeatAt;
    private static int _diagLastHash;

    /// <summary>
    /// Per-frame service, called from <c>CanvasConversion.TickPanelOrder</c> AFTER the panel
    /// ladder and the furniture bands have been assigned for this frame — the tiles are ranked
    /// against those numbers, so reading them one step later in the same pass is what makes the
    /// answer current rather than one frame stale.
    ///
    /// <para>Cost: one AABB distance plus one walk of the (≈15-entry) ladder per adopted renderer,
    /// and change-gated writes. A steady scene writes nothing at all. The renderer scan is
    /// throttled to <see cref="RescanSeconds"/> and never enumerates the scene's renderers — it
    /// walks the map tiles' own 'Preview' subtrees.</para>
    /// </summary>
    internal static void Tick(Vector3 eye)
    {
        // MR owns the unseen family while it is on (opaque re-render + backings). Park and hand
        // every authored order back, so MR is bit-identical to the shipped build.
        if (MixedReality.BackingsWanted)
        {
            if (!_parked)
                Release();
            return;
        }

        float now = Time.unscaledTime;
        if (now >= _nextScanAt)
        {
            _nextScanAt = now + RescanSeconds;
            Rescan();
        }
        if (Entries.Count == 0)
            return;

        _parked = false;
        int lifted = 0, lowest = int.MaxValue, highest = int.MinValue, offLayer = 0;
        float near = float.MaxValue, far = 0f;
        for (int i = Entries.Count - 1; i >= 0; i--)
        {
            Entry e = Entries[i];
            Renderer? r = e.Renderer;
            if (r == null)
            {
                Entries.RemoveAt(i); // destroyed with its map tile; nothing to hand back
                continue;
            }

            // Nearest point of the renderer's own world AABB — the same "how close does this
            // surface actually come to the eye" question CanvasConversion.PanelEyeDistance
            // answers for a panel, and the reason the answer is per RENDERER rather than per
            // region: a fog-of-war region spans ten metres and more (MAPTILE bounds in the
            // hardware log), so one distance for the whole region would rank its far edge by
            // its near edge and hand a panel that is genuinely in front of that far edge a
            // wrong-side answer. The resulting orders are quantised to ladder slots anyway, so
            // a whole region still collapses onto a handful of distinct values.
            float d = Mathf.Sqrt(r.bounds.SqrDistance(eye));
            int want = WorldUI.CanvasConversion.OrderSeenThrough(eye, d, TileOrderLift);
            if (want == WorldUI.CanvasConversion.NoSurfaceBehind)
                want = e.Authored; // nothing of ours behind it — leave the shipped look alone

            if (want != e.Applied)
            {
                if (want != e.Pending)
                {
                    e.Pending = want;
                    e.Streak = 1;
                }
                else if (++e.Streak >= SettleFrames)
                {
                    r.sortingOrder = want;
                    e.Applied = want;
                    e.Pending = want;
                    e.Streak = 0;
                }
            }
            else
            {
                e.Pending = want;
                e.Streak = 0;
            }
            Entries[i] = e;

            if (e.Applied != e.Authored)
                lifted++;
            if (e.Applied < lowest) lowest = e.Applied;
            if (e.Applied > highest) highest = e.Applied;
            if (d < near) near = d;
            if (d > far) far = d;
            // THE one assumption this whole manoeuvre rests on that no repo artefact could
            // confirm: Unity compares sortingLayer BEFORE sortingOrder, so if the fog-of-war kit
            // were authored onto a non-default sorting layer, no order this driver writes could
            // reach the panels at all. Counted rather than assumed — the log line below says it
            // out loud, and a non-zero count is the first thing to read after a "still hidden"
            // report.
            if (r.sortingLayerID != 0)
                offLayer++;
        }

        // Every adopted renderer died inside this pass (a room revealed between two scans):
        // nothing to state, and the sentinels would print as int.MaxValue..int.MinValue.
        if (Entries.Count == 0)
        {
            Nodes.Clear();
            _parked = true;
            return;
        }
        LogState(lifted, lowest, highest, near, far, offLayer);
    }

    /// <summary>Hand every authored sortingOrder back and forget the adoption (MR taking over,
    /// or the last 'Preview' node going away). Renderers Unity already destroyed are simply
    /// dropped — there is nothing left to restore.</summary>
    internal static void Release()
    {
        for (int i = 0; i < Entries.Count; i++)
        {
            Entry e = Entries[i];
            if (e.Renderer != null && e.Renderer.sortingOrder != e.Authored)
                e.Renderer.sortingOrder = e.Authored;
        }
        Entries.Clear();
        Nodes.Clear();
        _parked = true;
        _diagLastHash = 0;
    }

    /// <summary>
    /// Find the ACTIVE 'Preview' nodes — the stand-in stacks <c>ProceduralMapTile.ShowContent</c>
    /// switches on for a room that has not been discovered yet — and adopt their renderers.
    /// Rebuilds only when the node set actually changed (a room revealed, a tile finished
    /// generating), because a rebuild costs every adopted renderer its settle state.
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

        if (SameNodes(NodeScratch))
            return;

        // RECONCILE, never rebuild from scratch. A node-set change means ONE region was revealed
        // or ONE tile finished generating; the surviving regions' renderers are untouched, and
        // dropping their applied order (and their settle state) would park them back at 0 for six
        // frames — i.e. a visible flash of the very defect this driver removes, every time any
        // room is opened. Carried over by renderer identity: what survives keeps its state, what
        // is gone gets its authored order handed back, what is new starts parked.
        Carried.Clear();
        for (int i = 0; i < Entries.Count; i++)
        {
            Entry old = Entries[i];
            if (old.Renderer != null)
                Carried[old.Renderer.GetInstanceID()] = old;
        }

        Entries.Clear();
        Nodes.Clear();
        for (int i = 0; i < NodeScratch.Count; i++)
        {
            Transform node = NodeScratch[i];
            Nodes.Add(node);
            ScanScratch.Clear();
            // includeInactive on purpose: a piece of the stack that happens to be switched off
            // right now must still be adopted, because nothing would ever bring us back to adopt
            // it later — the next scan only fires on a NODE-set change.
            node.GetComponentsInChildren(includeInactive: true, ScanScratch);
            for (int j = 0; j < ScanScratch.Count; j++)
            {
                Renderer r = ScanScratch[j];
                if (r == null)
                    continue;
                int id = r.GetInstanceID();
                if (Carried.TryGetValue(id, out Entry kept))
                {
                    Entries.Add(kept);
                    Carried.Remove(id);
                    continue;
                }
                Entries.Add(new Entry
                {
                    Renderer = r,
                    Authored = r.sortingOrder,
                    Applied = r.sortingOrder,
                    Pending = r.sortingOrder,
                });
            }
        }

        // Whatever is left in Carried left the undiscovered set (its room was revealed): hand the
        // authored order back so the now-visible room renders exactly as the shipped game does.
        foreach (Entry gone in Carried.Values)
        {
            if (gone.Renderer != null && gone.Renderer.sortingOrder != gone.Authored)
                gone.Renderer.sortingOrder = gone.Authored;
        }
        Carried.Clear();
        _parked = Entries.Count == 0;
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
    /// 'PANEL DRAW ORDER' line: how many fog-of-war renderers are adopted, how many are actually
    /// LIFTED right now, and the order span they occupy. A follow-up report of the form "the
    /// initiative track still disappears behind an undiscovered tile" is answered directly by it —
    /// either the tiles are still parked at 0 (the ladder had nothing behind them, i.e. the
    /// geometry assumption is wrong) or they are lifted and the artefact is not an ordering one.
    /// </summary>
    private static void LogState(
        int lifted, int lowest, int highest, float near, float far, int offLayer)
    {
        int hash = (Entries.Count * 397) ^ (lifted * 31) ^ (lowest * 7) ^ highest;
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

        VRLog.Info("Core", $"UNSEEN TILE ORDER ({Nodes.Count} undiscovered 'Preview' node(s), " +
                           $"{Entries.Count} renderer(s), d={near:F1}..{far:F1}m): {lifted} lifted " +
                           $"onto the panel ladder, orders {lowest}..{highest}. A lifted tile is " +
                           "painted AFTER everything the ladder puts behind it, so its hardcoded " +
                           "ZWrite can no longer erase the board's panels and text — you see them " +
                           "THROUGH it. A tile with nothing behind it keeps its authored order 0." +
                           (offLayer > 0
                               ? $" WARNING: {offLayer} renderer(s) sit on a NON-DEFAULT sorting " +
                                 "layer — Unity compares the layer before the order, so no order " +
                                 "written here can reach the panels for those; that would need the " +
                                 "layer matched too."
                               : string.Empty));
    }
}
