using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Detach a uGUI subtree from every ANCESTOR CLIPPER while it is shown — the "unmask" sibling of
/// <see cref="OnTopUiGraphics"/> (initiative hover popup round 4; the round-by-round history lives
/// on <c>Surfaces/TablePanelSurfaces.cs</c>).
///
/// ─── THE PROBLEM THIS SOLVES ────────────────────────────────────────────────────────────────────
/// ROUND 4 (user, verbatim): "Das Problem mit den Text des Verbündeten über der Gegner info ist
/// unverändert." — after THREE mechanisms all provably ran on hardware (ModBuild 127 log: the
/// draw-order lift fired, 33 graphics rode ZTest-Always clones, the coplanarity pass held the
/// popup plane at z 0) the ally banner was STILL cut at the same razor line. That eliminates the
/// entire render-state family those rounds addressed: paint order (round 1/2), plane z (round 2)
/// and the depth test (round 3) were all fixed and the pixels did not move.
///
/// The one occluder class that is IMMUNE to all three is UI CLIPPING. <c>RectMask2D</c> clips via
/// <c>CanvasRenderer.EnableRectClipping</c> (a per-RENDERER clip rect the shader applies via
/// <c>UNITY_UI_CLIP_RECT</c>/<c>_ClipRect</c>) and CULLS whole renderers that leave the rect
/// (<c>CanvasRenderer.cull</c>); a stencil <see cref="Mask"/> rewrites the child's
/// <c>materialForRendering</c> with stencil-Equal ops. Neither cares about sibling order, about
/// renderQueue, about the material's ZTest, or about the transform's z — a clipped fragment is
/// discarded no matter how "on top" it is. And the popup DOES sit under a clipper: the hover
/// popups are reparented into <c>InitiativeTrack.enemyCardsHolder</c>
/// (InitiativeTrackEnemyBehaviour.SetCardHolder, decompiled), which is the CONTENT of the
/// "Main Area" ScrollRect (.planning/refactor/INVARIANTS-WorldUI.md — found during the Y-swing
/// hunt), and the mod's own <c>EnsureScrollClipping</c> (CanvasConversion.2.Adopt) logged NOTHING
/// for this panel, which by its code means the viewport ALREADY HAD a working clipper (an enabled
/// game-owned RectMask2D or a functioning stencil Mask — prefab-level components, invisible to
/// decompilation). The banner (<c>MonsterBaseUI.roleGameObject</c>, "VERBÜNDETER") is the only
/// popup part that reaches up across that clipper's top edge — enemies have no banner
/// (MonsterBaseUI.SetBaseStats:162-180), which is why their popups were always complete.
///
/// ─── WHAT IT DOES ───────────────────────────────────────────────────────────────────────────────
/// For every <see cref="MaskableGraphic"/> under a given root: <c>maskable = false</c>, then
/// <see cref="MaskableGraphic.RecalculateClipping"/>. BOTH calls are load-bearing, read from the
/// shipped uGUI 1.0.0 source:
/// <list type="bullet">
/// <item>the <c>maskable</c> SETTER only dirties the stencil and the material
///   (MaskableGraphic.cs:59-70) — it kills a stencil <see cref="Mask"/>'s hold on the graphic
///   (stencil depth is recomputed to 0, the material stops being stencil-wrapped) but does NOT
///   re-run <c>UpdateClipParent</c>, so a <see cref="RectMask2D"/> keeps the graphic registered
///   and keeps writing its clip rect;</item>
/// <item><c>RecalculateClipping()</c> (the public IClippable door onto <c>UpdateClipParent</c>,
///   MaskableGraphic.cs:265-284) re-evaluates <c>maskable &amp;&amp; IsActive()</c>, finds false, and
///   detaches: <c>RectMask2D.RemoveClippable</c> calls <c>SetClipRect(new Rect(), false)</c> →
///   <c>CanvasRenderer.DisableRectClipping()</c> (RectMask2D.cs:327-343), and the removal path
///   also runs <c>UpdateCull(false)</c> — clearing a <c>CanvasRenderer.cull</c> the clipper may
///   have set. Rect clipping, renderer culling and stencil masking all end HERE, this frame.</item>
/// </list>
///
/// ─── THE INTERNAL-CLIPPER GUARD ─────────────────────────────────────────────────────────────────
/// A graphic that sits under a clipper INSIDE the treated subtree (a masked portrait, a card frame
/// mask) is skipped: its clipping is the popup's own DESIGN, and <c>maskable=false</c> would make
/// its art bleed outside its frame. The guard is component-PRESENCE (enabled or not) on any node
/// from the graphic up to and including the root — conservative on purpose: a clipper the game
/// toggles at runtime (the row's <c>_mask</c> pattern) must not flip a graphic between treated and
/// skipped. The banner itself is a direct label band on the popup — no internal clipper above it —
/// so the guard can never protect the clip this class exists to remove.
///
/// ─── RESTORE DISCIPLINE ────────────────────────────────────────────────────────────────────────
/// Only graphics whose AUTHORED <c>maskable</c> was true are treated (false-authored ones need
/// nothing and must not be flipped to true on restore). <see cref="RestoreAll"/> hands
/// <c>maskable = true</c> back — value-checked: a graphic the game itself set unmaskable meanwhile
/// is left alone — and runs <c>RecalculateClipping()</c> again so the clipper re-registers it
/// immediately. Records of Unity-destroyed graphics are dropped (the popup's card body is
/// re-instantiated per generation, MonsterBaseUI.cs:293/304 — same prune pressure as the on-top
/// pass).
/// </summary>
internal sealed class UnmaskedUiGraphics
{
    /// <summary>Graphics currently holding our <c>maskable=false</c> (authored value was true).</summary>
    private readonly List<MaskableGraphic> _treated = new(64);

    /// <summary>Instance IDs of every graphic already treated (or deliberately skipped), so the
    /// per-frame Apply is one hash probe per already-seen graphic.</summary>
    private readonly HashSet<int> _seen = new(128);

    private static readonly List<MaskableGraphic> GraphicScratch = new(64);

    /// <summary>Prune dead records above this — see the class doc (per-generation card body).</summary>
    private const int RecordCap = 512;

    /// <summary>Graphics currently treated.</summary>
    public int Count => _treated.Count;

    /// <summary>
    /// Make every untreated <see cref="MaskableGraphic"/> under <paramref name="root"/> (inactive
    /// included — the banner is inactive on enemy popups and toggles on per actor type) ignore
    /// ancestor clippers, except graphics under an INTERNAL clipper (see the class doc's guard
    /// section). Idempotent and cheap in steady state: one walk + one hash probe per node.
    /// Returns how many NEW graphics were unmasked.
    /// </summary>
    public int Apply(Transform root, string context)
    {
        if (root == null)
            return 0;

        GraphicScratch.Clear();
        root.GetComponentsInChildren(includeInactive: true, GraphicScratch);

        // First pass: is there anything NEW at all? (steady state exits here)
        bool anyNew = false;
        for (int i = 0; i < GraphicScratch.Count && !anyNew; i++)
        {
            MaskableGraphic g = GraphicScratch[i];
            if (g != null && !_seen.Contains(g.GetInstanceID()))
                anyNew = true;
        }
        if (!anyNew)
        {
            GraphicScratch.Clear();
            return 0;
        }

        int unmasked = 0;
        int guarded = 0;
        for (int i = 0; i < GraphicScratch.Count; i++)
        {
            MaskableGraphic g = GraphicScratch[i];
            if (g == null)
                continue;
            int id = g.GetInstanceID();
            if (_seen.Contains(id))
                continue;
            _seen.Add(id); // treated OR skipped — either way judged once

            try
            {
                if (!g.maskable)
                    continue; // authored unmaskable — nothing to change, nothing to restore
                if (UnderInternalClipper(g.transform, root))
                {
                    guarded++; // the popup's own masked art — its clipping is design, keep it
                    continue;
                }
                g.maskable = false;      // ends STENCIL masking (recomputes stencil depth to 0)…
                g.RecalculateClipping(); // …and THIS detaches RectMask2D clip rect + cull (see doc)
                _treated.Add(g);
                unmasked++;
            }
            catch
            {
                // Leave this graphic vanilla (it stays merely clipped, never mis-rendered); the
                // ID stays recorded — a component that throws here throws every tick.
            }
        }
        GraphicScratch.Clear();

        PruneDeadRecords();

        if (unmasked > 0)
        {
            VRLog.Info("WorldUI",
                $"UNMASK ({context}): {unmasked} graphic(s) set maskable=false + " +
                $"RecalculateClipping ({Count} held, {guarded} under the subtree's own internal " +
                "clipper left masked). Ancestor RectMask2D clip rects, their renderer culling and " +
                "any ancestor stencil Mask stop applying to the subtree; authored maskable is " +
                "restored when the holder leaves the panel or the panel releases (same lifetime " +
                "as the round-3 on-top materials).");
        }
        return unmasked;
    }

    /// <summary>
    /// Hand every treated graphic <c>maskable = true</c> back — value-checked, so a graphic the
    /// game itself set unmaskable meanwhile is never stomped — re-registering it with its clipper
    /// via <c>RecalculateClipping()</c>, and forget everything. Cheap no-op while nothing is held.
    /// </summary>
    public void RestoreAll(string reason)
    {
        int had = _treated.Count;
        for (int i = 0; i < _treated.Count; i++)
        {
            MaskableGraphic g = _treated[i];
            try
            {
                if (g != null && !g.maskable)
                {
                    g.maskable = true;
                    g.RecalculateClipping(); // re-register with the ancestor clipper NOW
                }
            }
            catch { /* destroyed under us — nothing to restore */ }
        }
        _treated.Clear();
        _seen.Clear();
        if (had > 0)
            VRLog.Info("WorldUI", $"UNMASK restore ({reason}): {had} graphic(s) back on " +
                                  "authored maskable=true and re-registered with their clippers.");
    }

    /// <summary>Is there a clipper COMPONENT (RectMask2D, or a Mask with a graphic to write the
    /// stencil) on any node from <paramref name="t"/> up to and including <paramref name="root"/>,
    /// excluding <paramref name="t"/> itself? Presence, not enabled-ness — see the class doc.</summary>
    private static bool UnderInternalClipper(Transform t, Transform root)
    {
        for (Transform? link = t.parent; link != null; link = link.parent)
        {
            if (link.GetComponent<RectMask2D>() != null)
                return true;
            Mask m = link.GetComponent<Mask>();
            if (m != null && m.graphic != null)
                return true;
            if (ReferenceEquals(link, root))
                break;
        }
        return false;
    }

    /// <summary>Bounded records — see <see cref="RecordCap"/> and the class doc.</summary>
    private void PruneDeadRecords()
    {
        if (_treated.Count <= RecordCap)
            return;
        _treated.RemoveAll(static g => g == null);
        // _seen keeps dead IDs — instance IDs are never reused within a session, so a stale ID
        // can only suppress re-treating an object that no longer exists. Rebuild it from the live
        // records to keep it in step with the cap.
        _seen.Clear();
        for (int i = 0; i < _treated.Count; i++)
            _seen.Add(_treated[i].GetInstanceID());
    }
}
